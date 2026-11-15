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
            if (CurrentTask != null) list.Add(CurrentTask);
            list.AddRange(backlog);
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
            float minTime = ai.IsLive ? 2.5f : UnityEngine.Random.Range(range.x, range.y);
            if (task.isWrapUp) minTime = 2f;
            float t = 0f;
            while (!done || t < minTime)
            {
                t += Time.deltaTime;
                task.progress = done
                    ? Mathf.Max(task.progress, Mathf.Clamp01(t / minTime))
                    : Mathf.Min(0.9f, 1f - Mathf.Exp(-t / (minTime * 0.8f)));
                if (Time.frameCount % 15 == 0) NotifyChanged();
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

            switch (action.type)
            {
                case ManagerActionType.Approve:
                case ManagerActionType.Acknowledge:
                    task.status = TaskExecutionStatus.Completed;
                    task.completedAt = DateTime.UtcNow;
                    Data.completedTasks++;
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
                    Record(task, "Dropped");
                    GameManager.Instance.Planner.OnTaskFinished(task);
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
            bool carryBack = newStatus == AgentStatus.Working;
            if (carryBack) { page.SetVisible(true); avatar.SetCarrying(true); }
            ReturnToDesk(() =>
            {
                if (carryBack) { page.SetVisible(false); avatar.SetCarrying(false); }
                atDesk?.Invoke();
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
