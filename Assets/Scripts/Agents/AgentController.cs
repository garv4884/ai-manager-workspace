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
    ///   Free --assign--> Working --done--> NeedsApproval | NeedsInput --queue/lift/present-->
    ///     approve        -> Free (page filed)
    ///     request changes-> Working (revision+1) -> back to the queue
    ///     answer         -> Working (with answer) -> back to the queue
    ///     dismiss        -> Free (task cancelled)
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

        public void SetSelected(bool selected)
        {
            if (selectionRing != null) selectionRing.SetActive(selected);
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

        public bool CanTakeTask =>
            Data != null && CurrentTask == null && Data.status == AgentStatus.Free &&
            Data.activity == AgentActivity.AtDesk && IsSeated;

        public bool AssignTask(TaskData task)
        {
            if (task == null || !CanTakeTask) return false;
            CurrentTask = task;
            task.status = TaskExecutionStatus.InProgress;
            StartWork();
            return true;
        }

        private void StartWork()
        {
            if (workRoutine != null) StopCoroutine(workRoutine);
            CurrentTask.progress = 0f;
            CurrentTask.status = TaskExecutionStatus.InProgress;
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
            if (task != CurrentTask) yield break;   // dismissed meanwhile

            task.progress = 1f;
            task.status = TaskExecutionStatus.WaitingForUser;
            if (result.failed)
            {
                task.error = result.text;
                SetStatus(AgentStatus.Error);
            }
            else if (result.isQuestion)
            {
                task.question = result.text;
                SetStatus(AgentStatus.WaitingForInput);
            }
            else
            {
                task.output = result.text;
                SetStatus(AgentStatus.WaitingForApproval);
            }

            avatar.SetPose(AgentAvatar.Pose.Sitting);
            GameManager.Instance.Queue.Enqueue(this);
        }

        // ── Decisions (only while presenting on the lift) ─────────────────────

        public bool Approve()
        {
            if (!IsReadyPresenter() || (Data.status != AgentStatus.WaitingForApproval && Data.status != AgentStatus.Error))
                return false;

            if (Data.status == AgentStatus.Error)
            {
                CurrentTask.error = "";
                return GameManager.Instance.Queue.FinishPresenting(this, QueueManager.PageExit.HandBack, StartWork, AgentStatus.Working);
            }

            CurrentTask.status = TaskExecutionStatus.Completed;
            CurrentTask.completedAt = DateTime.UtcNow;
            Data.completedTasks++;
            return GameManager.Instance.Queue.FinishPresenting(this, QueueManager.PageExit.File, null, AgentStatus.Free, clearTask: true);
        }

        public bool RequestChanges(string feedback)
        {
            if (!IsReadyPresenter() || Data.status != AgentStatus.WaitingForApproval) return false;
            CurrentTask.feedback = feedback?.Trim() ?? "";
            CurrentTask.revision++;
            return GameManager.Instance.Queue.FinishPresenting(this, QueueManager.PageExit.HandBack, StartWork, AgentStatus.Working);
        }

        public bool ProvideInput(string answer)
        {
            if (!IsReadyPresenter() || Data.status != AgentStatus.WaitingForInput || string.IsNullOrWhiteSpace(answer))
                return false;
            CurrentTask.answer = answer.Trim();
            return GameManager.Instance.Queue.FinishPresenting(this, QueueManager.PageExit.HandBack, StartWork, AgentStatus.Working);
        }

        public bool Dismiss()
        {
            if (!IsReadyPresenter()) return false;
            CurrentTask.status = TaskExecutionStatus.Cancelled;
            return GameManager.Instance.Queue.FinishPresenting(this, QueueManager.PageExit.Discard, null, AgentStatus.Free, clearTask: true);
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
                AgentActivity.WalkingToDesk, AgentActivity.AtDesk, onSeated));
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
