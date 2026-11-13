using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Manages the queue of agents waiting to present to the manager.
    /// Agents line up in world-space slots and advance one at a time
    /// to the manager's desk.
    /// </summary>
    public class QueueManager : MonoBehaviour
    {
        // ─── Inspector Fields ─────────────────────────────────────────────────
        [Header("Queue Layout")]
        [Tooltip("4 world-space positions forming the line leading to the manager desk.")]
        [SerializeField] private Transform[] queueSlotTransforms;

        [Tooltip("The position directly in front of the manager's desk.")]
        [SerializeField] private Transform presentingTransform;

        // ─── Private State ────────────────────────────────────────────────────
        private Queue<AgentController> waitingQueue = new Queue<AgentController>();
        private AgentController currentPresenter = null;
        private bool isManagerBusy = false;

        // ─── Public Accessors ─────────────────────────────────────────────────

        /// <summary>Returns the agent currently presenting to the manager.</summary>
        public AgentController GetCurrentPresenter() => currentPresenter;

        /// <summary>Property alias for the current presenter (used by GestureInteractionManager).</summary>
        public AgentController CurrentPresenter => currentPresenter;

        /// <summary>Returns the number of agents waiting in the queue (excluding the presenter).</summary>
        public int GetQueueLength() => waitingQueue.Count;

        // ─── Queue Operations ─────────────────────────────────────────────────

        /// <summary>
        /// Adds an agent to the waiting queue, repositions all queued agents,
        /// and advances the queue if the manager is free.
        /// </summary>
        public void EnqueueAgent(AgentController agent)
        {
            if (agent == null) return;

            // Avoid duplicates
            if (waitingQueue.Contains(agent) || agent == currentPresenter)
            {
                Debug.LogWarning($"[QueueManager] '{agent.Data?.agentName}' is already in the queue or presenting.");
                return;
            }

            waitingQueue.Enqueue(agent);
            Debug.Log($"[QueueManager] '{agent.Data?.agentName}' enqueued. Queue length: {waitingQueue.Count}");

            UpdateQueuePositions();

            if (!isManagerBusy)
                AdvanceQueue();
        }

        /// <summary>
        /// If the manager is free and there are waiting agents, dequeues the
        /// next agent and sends them to present.
        /// </summary>
        public void AdvanceQueue()
        {
            if (isManagerBusy)
            {
                Debug.Log("[QueueManager] Manager is busy; cannot advance queue.");
                return;
            }

            if (waitingQueue.Count == 0)
            {
                Debug.Log("[QueueManager] Queue is empty; nothing to advance.");
                return;
            }

            currentPresenter = waitingQueue.Dequeue();
            isManagerBusy = true;

            Debug.Log($"[QueueManager] Advancing '{currentPresenter.Data?.agentName}' to manager desk.");

            // Reposition remaining agents now that one has left the queue
            UpdateQueuePositions();

            // Override the agent's target with the presenting transform if set,
            // otherwise AgentController.MoveToManager uses managerPresentTransform.
            if (presentingTransform != null)
                currentPresenter.MoveToQueue(presentingTransform.position);

            currentPresenter.MoveToManager();
        }

        /// <summary>
        /// Called when the manager has finished interacting with the current
        /// presenter. Sends them back to their desk and advances the queue.
        /// </summary>
        public void OnManagerDoneWithPresenter()
        {
            if (currentPresenter == null)
            {
                Debug.LogWarning("[QueueManager] OnManagerDoneWithPresenter called but no current presenter.");
                return;
            }

            Debug.Log($"[QueueManager] Manager done with '{currentPresenter.Data?.agentName}'.");
            currentPresenter.MoveToDesk();
            currentPresenter = null;
            isManagerBusy = false;

            AdvanceQueue();
        }

        // ─── Position Updates ─────────────────────────────────────────────────

        /// <summary>
        /// Moves each queued agent to the corresponding slot transform.
        /// Agents beyond available slots stay at their last position.
        /// </summary>
        private void UpdateQueuePositions()
        {
            if (queueSlotTransforms == null || queueSlotTransforms.Length == 0) return;

            int slotIndex = 0;
            foreach (AgentController agent in waitingQueue)
            {
                if (agent == null) continue;

                if (slotIndex < queueSlotTransforms.Length)
                {
                    Transform slot = queueSlotTransforms[slotIndex];
                    if (slot != null)
                    {
                        agent.MoveToQueue(slot.position);
                        Debug.Log($"[QueueManager] '{agent.Data?.agentName}' → slot {slotIndex}.");
                    }
                }
                else
                {
                    // More agents than slots: they stack at the last slot
                    Transform lastSlot = queueSlotTransforms[queueSlotTransforms.Length - 1];
                    if (lastSlot != null)
                        agent.MoveToQueue(lastSlot.position);
                }

                slotIndex++;
            }
        }
    }
}
