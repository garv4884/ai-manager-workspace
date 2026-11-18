using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// The line of agents waiting for the manager, and the presenting agent on the lift.
    /// Agents queue along <see cref="queueStart"/>'s forward axis (away from the desk),
    /// one at a time ride the lift up, and leave once the manager decides.
    /// </summary>
    public class QueueManager : MonoBehaviour
    {
        public enum PageExit { HandBack, File, Discard }

        [SerializeField] private Transform queueStart;
        [SerializeField] private float spacing = 1.1f;
        [SerializeField] private LiftPad lift;
        [SerializeField] private ReviewDesk reviewDesk;

        private readonly List<AgentController> waiting = new List<AgentController>();
        /// <summary>Agents that need you but wait at their desk until the line has room (keeps the line short and calm).</summary>
        private readonly List<AgentController> held = new List<AgentController>();
        private float lastJoin = -999f;

        public AgentController Presenter { get; private set; }
        public bool IsPresenterReady { get; private set; }
        public IReadOnlyList<AgentController> Waiting => waiting;
        public ReviewDesk Desk => reviewDesk;
        /// <summary>
        /// "Hold the queue": agents still line up, but nobody rides the lift until it is released,
        /// so the manager can think, talk or read without the next request arriving.
        /// </summary>
        public bool Hold
        {
            get => hold;
            set
            {
                if (hold == value) return;
                hold = value;
                Changed?.Invoke();
                if (!hold) TryAdvance();
            }
        }
        private bool hold;

        public int PendingCount => waiting.Count + held.Count + (Presenter != null ? 1 : 0);
        /// <summary>Everyone waiting for you: in line plus waiting at their desks.</summary>
        public int WaitingTotal => waiting.Count + held.Count;
        public bool IsHeldAtDesk(AgentController agent) => held.Contains(agent);

        public Quaternion LineFacing => Quaternion.LookRotation(-queueStart.forward);

        /// <summary>Raised when the line or the presenter changes.</summary>
        public event Action Changed;
        /// <summary>Raised when a presenter's page is on the desk and decisions are accepted.</summary>
        public event Action<AgentController> PresenterReady;

        public Vector3 SlotPosition(AgentController agent)
        {
            int index = waiting.IndexOf(agent);
            if (index < 0) index = waiting.Count;
            return queueStart.position + queueStart.forward * (spacing * index);
        }

        /// <summary>
        /// An agent needs you. It walks to the line only when there is room (Settings > General > Review flow:
        /// at most N waiting in line, one new arrival every few seconds); otherwise it waits at its desk.
        /// </summary>
        public void Enqueue(AgentController agent)
        {
            if (agent == null || waiting.Contains(agent) || held.Contains(agent) || agent == Presenter) return;
            if (CanJoinNow) Join(agent);
            else { held.Add(agent); agent.NotifyChanged(); Changed?.Invoke(); }
        }

        private bool CanJoinNow =>
            waiting.Count < Mathf.Max(1, AppSettings.Current.maxInLine) &&
            Time.time - lastJoin >= AppSettings.Current.ArrivalGapSeconds;

        private void Join(AgentController agent)
        {
            held.Remove(agent);
            waiting.Add(agent);
            lastJoin = Time.time;
            RefreshPositions();
            agent.GoToQueueSlot(this);
            Changed?.Invoke();
            TryAdvance();
        }

        private void Update()
        {
            if (held.Count == 0 || !CanJoinNow) return;
            AgentController next = held[0];
            if (next == null) { held.RemoveAt(0); return; }
            Join(next);
        }

        /// <summary>True while the agent waits (in line or at its desk) or is on the lift.</summary>
        public bool IsInLine(AgentController agent) => agent != null && (waiting.Contains(agent) || held.Contains(agent) || Presenter == agent);

        /// <summary>
        /// Takes an agent out of the line without a decision (its request was cancelled by the real agent,
        /// e.g. answered in the terminal). Returns false if the agent is mid-presentation and can't leave yet.
        /// </summary>
        public bool Withdraw(AgentController agent, AgentStatus newStatus)
        {
            if (held.Remove(agent))
            {
                agent.AfterDecision(newStatus, true, null);
                Changed?.Invoke();
                return true;
            }
            if (waiting.Remove(agent))
            {
                RefreshPositions();
                agent.AfterDecision(newStatus, true, null);
                Changed?.Invoke();
                return true;
            }
            if (agent == Presenter && IsPresenterReady)
                return FinishPresenting(agent, PageExit.Discard, null, newStatus, clearTask: true);
            return false;
        }

        /// <summary>Moves a waiting agent to the front of the line ("review next").</summary>
        public void PromoteToFront(AgentController agent)
        {
            int h = held.IndexOf(agent);
            if (h > 0) { held.RemoveAt(h); held.Insert(0, agent); Changed?.Invoke(); return; }
            int i = waiting.IndexOf(agent);
            if (i <= 0) return;
            waiting.RemoveAt(i);
            waiting.Insert(0, agent);
            RefreshPositions();
            Changed?.Invoke();
        }

        private void TryAdvance()
        {
            if (hold || Presenter != null || waiting.Count == 0) return;

            Presenter = waiting[0];
            waiting.RemoveAt(0);
            IsPresenterReady = false;
            RefreshPositions();
            Changed?.Invoke();

            AgentController agent = Presenter;
            agent.GoPresent(lift, reviewDesk, () =>
            {
                if (Presenter != agent) return;
                IsPresenterReady = true;
                agent.NotifyChanged();
                Changed?.Invoke();
                PresenterReady?.Invoke(agent);
            });
        }

        /// <summary>
        /// Ends the presentation: the page is handed back, filed or discarded, the lift goes
        /// down, and the agent walks back to its desk with <paramref name="newStatus"/>.
        /// </summary>
        public bool FinishPresenting(AgentController agent, PageExit exit, Action atDesk, AgentStatus newStatus, bool clearTask = false)
        {
            if (agent == null || agent != Presenter || !IsPresenterReady) return false;
            IsPresenterReady = false;
            Changed?.Invoke();
            StartCoroutine(FinishRoutine(agent, exit, atDesk, newStatus, clearTask));
            return true;
        }

        private IEnumerator FinishRoutine(AgentController agent, PageExit exit, Action atDesk, AgentStatus newStatus, bool clearTask)
        {
            switch (exit)
            {
                case PageExit.File:
                    reviewDesk.Play(ReviewDesk.Sfx.Approve);
                    yield return agent.Page.FileTo(reviewDesk.DoneTray);
                    reviewDesk.AddCompleted();
                    break;
                case PageExit.Discard:
                    reviewDesk.Play(ReviewDesk.Sfx.Dismiss);
                    yield return agent.Page.Discard();
                    break;
                default:
                    reviewDesk.Play(ReviewDesk.Sfx.Changes);
                    yield return agent.Page.FlyBack();
                    break;
            }

            yield return lift.Lower();
            agent.StepOffLift(lift);
            Presenter = null;
            agent.AfterDecision(newStatus, clearTask, atDesk);
            Changed?.Invoke();
            TryAdvance();
        }

        private void RefreshPositions()
        {
            for (int i = 0; i < waiting.Count; i++)
            {
                waiting[i].Data.queuePosition = i;
                waiting[i].NotifyChanged();
            }
            if (Presenter != null) Presenter.Data.queuePosition = -1;
        }
    }
}
