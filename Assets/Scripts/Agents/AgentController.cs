using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// One AI agent: sits at its workstation, works on a task, walks the result to the
    /// manager's queue, rides the lift to present, and reacts to the manager's decision.
    ///
    /// Task loop:
    ///   backlog --> Working --done--> a request (approval / question / choice / error / report)
    ///   --queue / lift / present--> the manager picks one of the actions that fit the request
    ///   (see ManagerAction.For). Answers, choices, changes and retries send the agent back to
    ///   work; approve / acknowledge / reject end the task and the agent takes the next one.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class AgentController : MonoBehaviour
    {
        [SerializeField] private AgentAvatar avatar;
        [SerializeField] private StatusTagUI statusTag;
        [SerializeField] private OutputPage page;
        [SerializeField] private GameObject selectionRing;

        private NavMeshAgent nav;
        private Coroutine moveRoutine;
        private Coroutine workRoutine;
        private Transform seatedOn;

        public AgentData Data { get; private set; }
        public TaskData CurrentTask { get; private set; }
        public Workstation Workstation { get; private set; }
        public OutputPage Page => page;
        public AgentAvatar Avatar => avatar;
        public bool IsSeated => seatedOn != null;

        /// <summary>Raised whenever status, activity, task or queue position changes.</summary>
        public event Action<AgentController> Changed;
        /// <summary>Raised a few times a second while work progresses (cheap UI updates; no save).</summary>
        public event Action<AgentController> ProgressChanged;

        private void Awake()
        {
            nav = GetComponent<NavMeshAgent>();
            nav.enabled = false;
            if (selectionRing != null) selectionRing.SetActive(false);
        }

        public void Initialize(AgentData data, Workstation workstation)
        {
            Data = data;
            Workstation = workstation;
            name = $"Agent_{data.agentName}";
            workstation.SetOwner(this);
            avatar.SetColors(data.accentColor);
            page.Bind(this, avatar.HoldPoint);
            statusTag.Bind(this);
            SnapIntoSeat(workstation.SeatPoint);
            Data.activity = AgentActivity.AtDesk;
            SetStatus(AgentStatus.Free);
        }

        private bool selected, hovered;

        public void SetSelected(bool value) { selected = value; UpdateRing(); }
        public void SetHovered(bool value) { hovered = value; UpdateRing(); }

        private void UpdateRing()
        {
            if (selectionRing != null) selectionRing.SetActive(selected || hovered);
        }

        public void NotifyChanged() => Changed?.Invoke(this);

        // ── Status ────────────────────────────────────────────────────────────

        private void SetStatus(AgentStatus status)
        {
            Data.status = status;
            avatar.SetStatus(status);
            page.SetVisible(Data.NeedsManager);
            avatar.SetCarrying(Data.NeedsManager);
            NotifyChanged();
        }

        private void SetActivity(AgentActivity activity)
        {
            Data.activity = activity;
            NotifyChanged();
        }

        // ── Tasks ─────────────────────────────────────────────────────────────

        private readonly System.Collections.Generic.Queue<TaskData> backlog = new System.Collections.Generic.Queue<TaskData>();

        /// <summary>Tasks waiting for this agent after the current one.</summary>
        public int BacklogCount => backlog.Count;

        /// <summary>Free, seated at its own desk, nothing queued: ready for demo work.</summary>
        public bool CanTakeTask =>
            Data != null && CurrentTask == null && backlog.Count == 0 && Data.status == AgentStatus.Free &&
            Data.activity == AgentActivity.AtDesk && IsSeated;

        /// <summary>Assigns or queues a task. Agents in a meeting or busy pick it up when they are back at their desk.</summary>
        public void AssignTask(TaskData task)
        {
            if (task == null) return;
            if (Data.IsLinked && !task.isTeamPlan && !task.isWrapUp && GameManager.Instance.Bridge != null)
            {
                SendToLinkedAgent(task);
                return;
            }
            task.status = TaskExecutionStatus.Pending;
            backlog.Enqueue(task);
            NotifyChanged();
            TryStartNext();
        }

        public void SetRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role)) return;
            Data.agentRole = role.Trim();
            NotifyChanged();
        }

        /// <summary>Current task (if any) followed by the backlog, for the session save.</summary>
        public System.Collections.Generic.List<TaskData> TasksForSave()
        {
            var list = new System.Collections.Generic.List<TaskData>();
            if (CurrentTask != null && !CurrentTask.isExternal) list.Add(CurrentTask);
            foreach (var t in backlog) if (!t.isExternal) list.Add(t);
            return list;
        }

        /// <summary>
        /// Restores saved tasks. A task that was waiting for the manager keeps its request and the
        /// agent goes back into line (returns true; the caller enqueues in the saved order). A task
        /// that was in progress starts over. Must be called while the agent is idle at its desk.
        /// </summary>
        public bool RestoreTasks(System.Collections.Generic.List<TaskData> tasks)
        {
            if (tasks == null || tasks.Count == 0) return false;
            TaskData first = tasks[0];
            bool waits = first.status == TaskExecutionStatus.WaitingForUser && first.request != RequestKind.None;
            for (int i = waits ? 1 : 0; i < tasks.Count; i++)
            {
                TaskData t = tasks[i];
                if (t == null) continue;
                if (t.status != TaskExecutionStatus.Pending) { t.status = TaskExecutionStatus.Pending; t.request = RequestKind.None; t.progress = 0f; }
                backlog.Enqueue(t);
            }
            if (!waits) { TryStartNext(); return false; }

            CurrentTask = first;
            first.progress = 1f;
            SetStatus(first.request switch
            {
                RequestKind.Question => AgentStatus.WaitingForInput,
                RequestKind.Choice => AgentStatus.WaitingForInput,
                RequestKind.Error => AgentStatus.Error,
                RequestKind.Report => AgentStatus.HasUpdate,
                _ => AgentStatus.WaitingForApproval
            });
            return true;
        }

        /// <summary>Starts the next queued task if the agent is idle at its desk.</summary>
        private void TryStartNext()
        {
            if (CurrentTask == null && externalRequests.Count > 0 && !GameManager.Instance.Queue.IsInLine(this))
            {
                PresentExternal(externalRequests.Dequeue());
                return;
            }
            if (CurrentTask != null || backlog.Count == 0) return;
            if (Data.status != AgentStatus.Free || Data.activity != AgentActivity.AtDesk || !IsSeated) return;
            CurrentTask = backlog.Dequeue();
            StartWork();
        }

        private void StartWork()
        {
            if (workRoutine != null) StopCoroutine(workRoutine);
            CurrentTask.progress = 0f;
            CurrentTask.status = TaskExecutionStatus.InProgress;
            CurrentTask.request = RequestKind.None;
            SetStatus(AgentStatus.Working);
            workRoutine = StartCoroutine(WorkRoutine());
        }

        private IEnumerator WorkRoutine()
        {
            avatar.SetPose(AgentAvatar.Pose.Typing);
            TaskData task = CurrentTask;
            AIServiceManager ai = GameManager.Instance.AI;

            bool done = false;
            AIResult result = default;
            ai.RequestWork(Data, task, r => { result = r; done = true; });

            Vector2 range = ai.SimulatedWorkSeconds;
            float minTime = ai.BrainFor(Data) != null ? 2.5f : UnityEngine.Random.Range(range.x, range.y);
            if (task.isWrapUp || task.quick) minTime = 2f;
            float t = 0f;
            while (!done || t < minTime)
            {
                t += Time.deltaTime;
                task.progress = done
                    ? Mathf.Max(task.progress, Mathf.Clamp01(t / minTime))
                    : Mathf.Min(0.9f, 1f - Mathf.Exp(-t / (minTime * 0.8f)));
                if (Time.frameCount % 15 == 0) ProgressChanged?.Invoke(this);
                yield return null;
            }

            workRoutine = null;
            if (task != CurrentTask) yield break;   // dropped meanwhile

            task.progress = 1f;
            task.status = TaskExecutionStatus.WaitingForUser;
            task.request = result.kind;
            task.options.Clear();
            switch (result.kind)
            {
                case RequestKind.Error:
                    task.error = result.text;
                    SetStatus(AgentStatus.Error);
                    break;
                case RequestKind.Question:
                    task.question = result.text;
                    task.askedOnce = true;
                    SetStatus(AgentStatus.WaitingForInput);
                    break;
                case RequestKind.Choice:
                    task.question = result.text;
                    task.askedOnce = true;
                    if (result.options != null) task.options.AddRange(result.options);
                    SetStatus(AgentStatus.WaitingForInput);
                    break;
                case RequestKind.Report:
                    task.output = result.text;
                    SetStatus(AgentStatus.HasUpdate);
                    break;
                default:
                    task.request = RequestKind.Approval;
                    task.output = result.text;
                    SetStatus(AgentStatus.WaitingForApproval);
                    break;
            }

            avatar.SetPose(AgentAvatar.Pose.Sitting);
            GameManager.Instance.Queue.Desk.PlayNotifyAt(transform.position);
            GameManager.Instance.Queue.Enqueue(this);
        }

        // ── Decisions (only while presenting on the lift) ─────────────────────

        public System.Collections.Generic.List<ManagerAction> AvailableActions() =>
            IsReadyPresenter() ? ManagerAction.For(CurrentTask, Data.agentName) : new System.Collections.Generic.List<ManagerAction>();

        /// <summary>Carries out a manager decision. <paramref name="text"/> is the typed answer/feedback when the action needs one.</summary>
        public bool Perform(ManagerAction action, string text = null)
        {
            if (!IsReadyPresenter()) return false;
            TaskData task = CurrentTask;
            QueueManager q = GameManager.Instance.Queue;
            text = text?.Trim() ?? "";
            if (task.isExternal) return PerformExternal(action, text);

            switch (action.type)
            {
                case ManagerActionType.Approve:
                case ManagerActionType.Acknowledge:
                    task.status = TaskExecutionStatus.Completed;
                    task.completedAt = DateTime.UtcNow;
                    Data.completedTasks++;
                    avatar.React(true);
                    Record(task, task.isTeamPlan ? "Plan approved" : task.isWrapUp ? "Wrap-up read"
                        : action.type == ManagerActionType.Acknowledge ? "Acknowledged" : "Approved");
                    if (task.isTeamPlan) GameManager.Instance.Planner.Execute(task);
                    else GameManager.Instance.Planner.OnTaskFinished(task);
                    return q.FinishPresenting(this, QueueManager.PageExit.File, AfterTaskEnded, AgentStatus.Free, clearTask: true);

                case ManagerActionType.FollowUp:
                    task.status = TaskExecutionStatus.Completed;
                    Data.completedTasks++;
                    Record(task, "Acknowledged, follow-up queued");
                    GameManager.Instance.Planner.OnTaskFinished(task);
                    if (text.Length > 0) backlog.Enqueue(GameManager.Instance.Dispatcher.CreateCustomTask(text));
                    return q.FinishPresenting(this, QueueManager.PageExit.File, AfterTaskEnded, AgentStatus.Free, clearTask: true);

                case ManagerActionType.Reject:
                    task.status = TaskExecutionStatus.Cancelled;
                    avatar.React(false);
                    Record(task, "Dropped");
                    if (task.isTeamPlan) GameManager.Instance.Planner.CloseBrief(task.planId);
                    else GameManager.Instance.Planner.OnTaskFinished(task);
                    return q.FinishPresenting(this, QueueManager.PageExit.Discard, AfterTaskEnded, AgentStatus.Free, clearTask: true);

                case ManagerActionType.RequestChanges:
                    task.feedback = text;
                    task.revision++;
                    return q.FinishPresenting(this, QueueManager.PageExit.HandBack, StartWork, AgentStatus.Working);

                case ManagerActionType.Answer:
                    if (text.Length == 0) return false;
                    task.answer = text;
                    return q.FinishPresenting(this, QueueManager.PageExit.HandBack, StartWork, AgentStatus.Working);

                case ManagerActionType.LetAgentDecide:
                    task.answer = "Use your best judgment.";
                    return q.FinishPresenting(this, QueueManager.PageExit.HandBack, StartWork, AgentStatus.Working);

                case ManagerActionType.ChooseOption:
                    if (action.optionIndex < 0 || action.optionIndex >= task.options.Count) return false;
                    task.answer = task.options[action.optionIndex];
                    return q.FinishPresenting(this, QueueManager.PageExit.HandBack, StartWork, AgentStatus.Working);

                case ManagerActionType.Retry:
                    task.error = "";
                    return q.FinishPresenting(this, QueueManager.PageExit.HandBack, StartWork, AgentStatus.Working);
            }
            return false;
        }

        private void AfterTaskEnded() => TryStartNext();

        // ── Bridge: this desk stands in for a real agent ──────────────────────

        private readonly System.Collections.Generic.Queue<TaskData> externalRequests = new System.Collections.Generic.Queue<TaskData>();
        private readonly System.Collections.Generic.Dictionary<string, TaskData> sentTasks = new System.Collections.Generic.Dictionary<string, TaskData>();
        private string externalTitle = "";
        private string pendingCancel;

        /// <summary>Called when this agent's page lands: applies a cancel that arrived during the lift ride.</summary>
        public bool ApplyPendingCancel()
        {
            if (string.IsNullOrEmpty(pendingCancel)) return false;
            bool match = CurrentTask != null && CurrentTask.externalId == pendingCancel;
            pendingCancel = null;
            return match && GameManager.Instance.Queue.Withdraw(this, AgentStatus.Free);
        }
        private float externalProgress = -1f;

        /// <summary>What the agent is doing, for tags and boards (current task, or what a linked agent reported).</summary>
        public string WorkTitle => CurrentTask != null ? CurrentTask.title : externalTitle;
        /// <summary>0..1, or -1 when unknown.</summary>
        public float WorkProgress => CurrentTask != null ? CurrentTask.progress : externalProgress;

        public void LinkTo(string bridgeName, string kind, string role)
        {
            if (!Data.IsLinked) Data.linkedRoleBeforeLink = Data.agentRole;
            Data.linkedTo = bridgeName;
            Data.linkedKind = string.IsNullOrEmpty(kind) ? "agent" : kind;
            Data.agentName = bridgeName;
            Data.agentRole = !string.IsNullOrWhiteSpace(role) ? role.Trim() : "Live agent";
            Workstation.SetOwner(this);
            avatar.SetLinked(true);
            NotifyChanged();
        }

        public void Unlink()
        {
            if (!Data.IsLinked) return;
            Data.linkedTo = "";
            Data.linkedKind = "";
            Data.agentName = Data.profileName;
            if (!string.IsNullOrEmpty(Data.linkedRoleBeforeLink)) Data.agentRole = Data.linkedRoleBeforeLink;
            externalRequests.Clear();
            sentTasks.Clear();
            externalTitle = "";
            externalProgress = -1f;
            Workstation.SetOwner(this);
            avatar.SetLinked(false);
            if (!GameManager.Instance.Queue.IsInLine(this) && CurrentTask == null) SetStatus(AgentStatus.Free);
            NotifyChanged();
        }

        /// <summary>Status reported by the real agent ("working" / "idle"), shown while it isn't waiting for you.</summary>
        public void SetExternalState(string state, string title, float progress)
        {
            externalTitle = title ?? "";
            externalProgress = progress;
            if (CurrentTask != null || GameManager.Instance.Queue.IsInLine(this)) { NotifyChanged(); return; }
            bool working = state == "working";
            if (working != (Data.status == AgentStatus.Working)) SetStatus(working ? AgentStatus.Working : AgentStatus.Free);
            if (IsSeated) avatar.SetPose(working ? AgentAvatar.Pose.Typing : AgentAvatar.Pose.Sitting);
            NotifyChanged();
        }

        /// <summary>A request from the real agent: it walks it up to you like any other page.</summary>
        public void ReceiveExternalRequest(TaskData request)
        {
            // Already presenting or queued (snapshot after a reconnect re-sends pending requests).
            if (CurrentTask != null && CurrentTask.externalId == request.externalId) return;
            foreach (var queued in externalRequests) if (queued.externalId == request.externalId) return;

            // A request about a task you assigned (brief / desk card) reuses that task, so the goal tracks it.
            if (!string.IsNullOrEmpty(request.externalTaskId) && sentTasks.TryGetValue(request.externalTaskId, out var original))
            {
                original.isExternal = true;
                original.externalId = request.externalId;
                original.request = request.request;
                original.output = request.output;
                original.question = request.question;
                original.error = request.error;
                original.options = request.options;
                request = original;
            }
            if (CurrentTask != null || GameManager.Instance.Queue.IsInLine(this)) { externalRequests.Enqueue(request); NotifyChanged(); return; }
            PresentExternal(request);
        }

        /// <summary>The real agent withdrew a request (answered elsewhere or timed out).</summary>
        public void CancelExternalRequest(string requestId)
        {
            if (CurrentTask != null && CurrentTask.isExternal && CurrentTask.externalId == requestId)
            {
                // Riding the lift: can't leave mid-ride, so withdraw as soon as the page lands.
                if (!GameManager.Instance.Queue.Withdraw(this, AgentStatus.Free)) pendingCancel = requestId;
                return;
            }
            var keep = new System.Collections.Generic.List<TaskData>(externalRequests);
            keep.RemoveAll(t => t.externalId == requestId);
            externalRequests.Clear();
            foreach (var t in keep) externalRequests.Enqueue(t);
            NotifyChanged();
        }

        private void PresentExternal(TaskData request)
        {
            if (workRoutine != null) { StopCoroutine(workRoutine); workRoutine = null; }
            CurrentTask = request;
            request.progress = 1f;
            request.status = TaskExecutionStatus.WaitingForUser;
            SetStatus(request.request switch
            {
                RequestKind.Question => AgentStatus.WaitingForInput,
                RequestKind.Choice => AgentStatus.WaitingForInput,
                RequestKind.Error => AgentStatus.Error,
                RequestKind.Report => AgentStatus.HasUpdate,
                _ => AgentStatus.WaitingForApproval
            });
            avatar.SetPose(AgentAvatar.Pose.Sitting);
            GameManager.Instance.Queue.Desk.PlayNotifyAt(transform.position);
            GameManager.Instance.Queue.Enqueue(this);
        }

        private void SendToLinkedAgent(TaskData task)
        {
            task.status = TaskExecutionStatus.InProgress;
            sentTasks[task.taskId] = task;
            GameManager.Instance.Bridge.SendTask(this, task);
            externalTitle = task.title;
            externalProgress = -1f;
            if (CurrentTask == null && !GameManager.Instance.Queue.IsInLine(this))
            {
                SetStatus(AgentStatus.Working);
                if (IsSeated) avatar.SetPose(AgentAvatar.Pose.Typing);
            }
            NotifyChanged();
        }

        private bool PerformExternal(ManagerAction action, string text)
        {
            TaskData task = CurrentTask;
            QueueManager q = GameManager.Instance.Queue;
            string verb, value = text, outcome;
            QueueManager.PageExit exit = QueueManager.PageExit.HandBack;
            AgentStatus next = AgentStatus.Working;
            bool finished = false, dropped = false;

            switch (action.type)
            {
                case ManagerActionType.Approve: verb = "approve"; outcome = "Approved"; exit = QueueManager.PageExit.File; finished = true; break;
                case ManagerActionType.Acknowledge: verb = "ack"; outcome = "Acknowledged"; exit = QueueManager.PageExit.File; finished = true; break;
                case ManagerActionType.FollowUp: verb = "followup"; outcome = "Acknowledged, follow-up sent"; exit = QueueManager.PageExit.File; finished = true; break;
                case ManagerActionType.Reject: verb = "reject"; outcome = "Dropped"; exit = QueueManager.PageExit.Discard; next = AgentStatus.Free; dropped = true; break;
                case ManagerActionType.RequestChanges: verb = "changes"; outcome = "Changes requested"; task.revision++; break;
                case ManagerActionType.Answer:
                    if (text.Length == 0) return false;
                    verb = "answer"; outcome = "Answered"; break;
                case ManagerActionType.LetAgentDecide: verb = "decide"; value = "Use your best judgment."; outcome = "Left to the agent"; break;
                case ManagerActionType.ChooseOption:
                    if (action.optionIndex < 0 || action.optionIndex >= task.options.Count) return false;
                    verb = "choose"; value = task.options[action.optionIndex]; outcome = $"Chose: {value}"; break;
                case ManagerActionType.Retry: verb = "retry"; outcome = "Retry"; break;
                default: return false;
            }

            GameManager.Instance.Bridge.SendDecision(task.externalId, verb, value);
            Record(task, outcome);
            if (finished) { task.status = TaskExecutionStatus.Completed; task.completedAt = DateTime.UtcNow; Data.completedTasks++; }
            if (dropped) task.status = TaskExecutionStatus.Cancelled;
            if ((finished || dropped) && !string.IsNullOrEmpty(task.planId)) GameManager.Instance.Planner.OnTaskFinished(task);
            if (finished || dropped) sentTasks.Remove(task.taskId);
            externalTitle = finished || dropped ? "" : task.title;
            return q.FinishPresenting(this, exit, AfterTaskEnded, next, clearTask: true);
        }

        /// <summary>Leaves the team: unfinished work is dropped and the desk is packed away.</summary>
        public void Retire()
        {
            StopAllCoroutines();
            var dropped = new System.Collections.Generic.List<TaskData>();
            if (CurrentTask != null) dropped.Add(CurrentTask);
            dropped.AddRange(backlog);
            backlog.Clear();
            CurrentTask = null;
            foreach (var t in dropped)
            {
                if (t.isExternal) continue;
                t.status = TaskExecutionStatus.Cancelled;
                Record(t, "Dropped (agent left)");
                if (t.isTeamPlan) GameManager.Instance.Planner.CloseBrief(t.planId);
                else GameManager.Instance.Planner.OnTaskFinished(t);
            }
            if (GameManager.Instance.Keyboard.IsOpenFor(this)) GameManager.Instance.Keyboard.Close();
            transform.SetParent(null, true);
            Workstation.SetOwner(null);
            Workstation.gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void Record(TaskData task, string outcome)
        {
            SessionStore store = GameManager.Instance.Session;
            if (store != null) store.Record(this, task, outcome);
        }

        private bool IsReadyPresenter()
        {
            QueueManager q = GameManager.Instance.Queue;
            return CurrentTask != null && q.Presenter == this && q.IsPresenterReady;
        }

        /// <summary>Called by QueueManager once the page has left and the lift is down.</summary>
        internal void AfterDecision(AgentStatus newStatus, bool clearTask, Action atDesk)
        {
            if (clearTask) CurrentTask = null;
            SetStatus(newStatus);

            // Rework: the agent carries the page back to the desk.
            bool carryBack = newStatus == AgentStatus.Working && !clearTask;
            if (carryBack) { page.SetVisible(true); avatar.SetCarrying(true); }
            ReturnToDesk(() =>
            {
                if (carryBack) { page.SetVisible(false); avatar.SetCarrying(false); }
                if (atDesk != null) atDesk(); else TryStartNext();
            });
        }

        // ── Movement commands ─────────────────────────────────────────────────

        public void ReturnToDesk(Action onSeated = null)
        {
            StartMove(SitRoutine(Workstation.SeatPoint, () => Workstation.ApproachPosition,
                AgentActivity.WalkingToDesk, AgentActivity.AtDesk, onSeated ?? TryStartNext));
        }

        public void GoToMeeting(Transform seat)
        {
            StartMove(SitRoutine(seat, () => seat.position - seat.forward * 0.45f,
                AgentActivity.WalkingToMeeting, AgentActivity.InMeeting, null));
        }

        internal void GoToQueueSlot(QueueManager queue)
        {
            StartMove(QueueRoutine(queue));
        }

        internal void GoPresent(LiftPad lift, ReviewDesk desk, Action onReady)
        {
            StartMove(PresentRoutine(lift, desk, onReady));
        }

        internal void StepOffLift(LiftPad lift)
        {
            transform.SetParent(null, true);
            Vector3 p = lift.BoardPosition;
            transform.position = p;
            EnableNav(p);
        }

        private void StartMove(IEnumerator routine)
        {
            if (moveRoutine != null) StopCoroutine(moveRoutine);
            moveRoutine = StartCoroutine(routine);
        }

        private IEnumerator SitRoutine(Transform seat, Func<Vector3> approach, AgentActivity walking, AgentActivity arrived, Action onSeated)
        {
            SetActivity(walking);
            yield return WalkTo(approach, 0.12f);

            // Slide from the approach point into the chair.
            DisableNav();
            Vector3 p0 = transform.position;
            Quaternion r0 = transform.rotation;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.45f;
                float k = Mathf.SmoothStep(0f, 1f, t);
                transform.SetPositionAndRotation(Vector3.Lerp(p0, seat.position, k), Quaternion.Slerp(r0, seat.rotation, k));
                yield return null;
            }
            SnapIntoSeat(seat);
            SetActivity(arrived);
            moveRoutine = null;
            onSeated?.Invoke();
        }

        private IEnumerator QueueRoutine(QueueManager queue)
        {
            SetActivity(AgentActivity.WalkingToQueue);
            yield return WalkTo(() => queue.SlotPosition(this), 0.1f);
            // Hold the slot: keep following it as the line advances.
            SetActivity(AgentActivity.InQueue);
            while (true)
            {
                Vector3 slot = queue.SlotPosition(this);
                if ((Flat(transform.position) - Flat(slot)).sqrMagnitude > 0.09f)
                    yield return WalkTo(() => queue.SlotPosition(this), 0.1f);
                FaceTowards(queue.LineFacing, 6f);
                yield return null;
            }
        }

        private IEnumerator PresentRoutine(LiftPad lift, ReviewDesk desk, Action onReady)
        {
            SetActivity(AgentActivity.WalkingToQueue);
            while (!lift.IsDown || lift.IsMoving) yield return null;
            yield return WalkTo(() => lift.BoardPosition, 0.08f);

            DisableNav();
            transform.SetParent(lift.Platform, true);
            Vector3 top = lift.Platform.position;
            float t = 0f;
            Vector3 p0 = transform.position;
            Quaternion r0 = transform.rotation;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.35f;
                transform.SetPositionAndRotation(Vector3.Lerp(p0, new Vector3(top.x, top.y, top.z), t),
                    Quaternion.Slerp(r0, lift.FacingRotation, t));
                yield return null;
            }
            avatar.SetPose(AgentAvatar.Pose.Holding);

            yield return lift.Raise();
            SetActivity(AgentActivity.Presenting);
            yield return page.FlyTo(desk.ReviewAnchor);
            desk.Play(ReviewDesk.Sfx.Arrive);
            avatar.SetPose(AgentAvatar.Pose.Standing);
            avatar.Wave();
            moveRoutine = null;
            onReady?.Invoke();
        }

        // ── Locomotion helpers ────────────────────────────────────────────────

        private IEnumerator WalkTo(Func<Vector3> target, float tolerance)
        {
            if (seatedOn != null)
            {
                // Stand up and step back from the chair.
                Transform seat = seatedOn;
                seatedOn = null;
                transform.SetParent(null, true);
                avatar.SetPose(AgentAvatar.Pose.Standing);
                Vector3 from = transform.position;
                Vector3 to = seat.position - seat.forward * 0.6f;
                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / 0.4f;
                    transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
                    yield return null;
                }
            }

            if (!EnableNav(transform.position)) yield break;

            avatar.SetPose(AgentAvatar.Pose.Walking);
            Vector3 last = target();
            nav.isStopped = false;
            nav.SetDestination(last);
            float timeout = 45f;

            while (timeout > 0f)
            {
                timeout -= Time.deltaTime;
                Vector3 current = target();
                if ((current - last).sqrMagnitude > 0.04f)
                {
                    last = current;
                    nav.SetDestination(last);
                }

                bool closeEnough = (Flat(transform.position) - Flat(last)).magnitude <= tolerance + 0.05f;
                bool pathDone = !nav.pathPending && nav.hasPath && nav.remainingDistance <= nav.stoppingDistance + tolerance;
                if (closeEnough || pathDone || (!nav.pathPending && nav.pathStatus == NavMeshPathStatus.PathInvalid))
                    break;
                yield return null;
            }

            if (nav.enabled && nav.isOnNavMesh)
            {
                nav.isStopped = true;
                nav.ResetPath();
            }
            avatar.SetPose(AgentAvatar.Pose.Standing);
        }

        private bool EnableNav(Vector3 near)
        {
            if (!NavMesh.SamplePosition(near, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[Agent] {Data?.agentName} is off the NavMesh near {near}.");
                return false;
            }
            nav.enabled = true;
            nav.speed = AppSettings.Current.WalkSpeedMetres;
            nav.Warp(hit.position);
            return true;
        }

        private void DisableNav()
        {
            if (nav.enabled && nav.isOnNavMesh) nav.ResetPath();
            nav.enabled = false;
        }

        private void SnapIntoSeat(Transform seat)
        {
            DisableNav();
            seatedOn = seat;
            transform.SetParent(seat, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            avatar.SetPose(Data != null && Data.status == AgentStatus.Working ? AgentAvatar.Pose.Typing : AgentAvatar.Pose.Sitting);
        }

        private void FaceTowards(Quaternion rotation, float speed)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, Time.deltaTime * speed);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
