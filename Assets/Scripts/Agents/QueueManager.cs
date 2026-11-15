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

        public int PendingCount => waiting.Count + (Presenter != null ? 1 : 0);

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

        public void Enqueue(AgentController agent)
        {
            if (agent == null || waiting.Contains(agent) || agent == Presenter) return;
            waiting.Add(agent);
            RefreshPositions();
            agent.GoToQueueSlot(this);
            Changed?.Invoke();
            TryAdvance();
        }

        /// <summary>Moves a waiting agent to the front of the line ("review next").</summary>
        public void PromoteToFront(AgentController agent)
        {
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
