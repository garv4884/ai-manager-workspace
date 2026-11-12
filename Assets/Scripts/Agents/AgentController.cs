using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Controls a single AI agent's movement, state, tasks, and interactions.
    /// Works with NavMesh for pathfinding and drives a simple status state machine.
    /// </summary>
    public class AgentController : MonoBehaviour
    {
        // ─── Inspector Fields ─────────────────────────────────────────────────
        [Header("Agent Data")]
        [SerializeField] private AgentData data;

        [Header("Components")]
        [SerializeField] private NavMeshAgent navAgent;
        [SerializeField] private Animator animator;

        [Header("UI / Output")]
        [SerializeField] private StatusTagUI statusTag;
        [SerializeField] private OutputPageRenderer outputPage;
        [SerializeField] private Transform pageHoldPoint;

        // ─── Public Transforms ────────────────────────────────────────────────
        public Transform assignedDeskTransform;
        public Transform managerPresentTransform;

        // ─── Private State ────────────────────────────────────────────────────
        private TaskData currentTask;
        private AgentStatus arrivalCallbackStatus;
        private bool hasArrived = false;
        private const float ArrivalThreshold = 0.3f;

        // ─── Properties ───────────────────────────────────────────────────────
        public AgentData Data => data;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Update()
        {
            if (navAgent == null || !navAgent.isOnNavMesh) return;

            // Check for arrival
            if (!navAgent.pathPending
                && navAgent.remainingDistance < ArrivalThreshold
                && !hasArrived)
            {
                hasArrived = true;
                OnArrival();
            }

            // Drive animator speed parameter if present
            if (animator != null)
                animator.SetFloat("Speed", navAgent.velocity.magnitude);
        }

        // ─── Initialisation ───────────────────────────────────────────────────

        /// <summary>
        /// Sets up this agent with its data, assigned desk, and presentation point.
        /// Called by AgentManager after spawning.
        /// </summary>
        public void Initialize(AgentData agentData, Transform desk, Transform presentPoint)
        {
            data = agentData;
            assignedDeskTransform = desk;
            managerPresentTransform = presentPoint;

            SetStatus(AgentStatus.Free);
            MoveToDesk();

            Debug.Log($"[AgentController] Initialised agent '{data?.agentName}'.");
        }

        // ─── Task Handling ────────────────────────────────────────────────────

        /// <summary>
        /// Assigns a task to this agent. Begins simulated work immediately.
        /// </summary>
        public void AssignTask(TaskData task)
        {
            currentTask = task;
            SetStatus(AgentStatus.Working);
            StartCoroutine(SimulateWork(task));
        }

        /// <summary>
        /// Simulates work over a random duration, then moves to the queue.
        /// </summary>
        private IEnumerator SimulateWork(TaskData task)
        {
            float workDuration = Random.Range(3f, 8f);
            yield return new WaitForSeconds(workDuration);

            if (task == null)
            {
                Debug.LogWarning("[AgentController] Task was null after SimulateWork wait.");
                yield break;
            }

            // Determine next status based on task type
            if (task.type == TaskType.NeedsTextInput)
                SetStatus(AgentStatus.WaitingForInput);
            else
                SetStatus(AgentStatus.WaitingForApproval);

            // Request a spot in the queue
            GameManager.Instance?.AgentManager?.RequestQueue(this);
        }

        // ─── Movement ─────────────────────────────────────────────────────────

        /// <summary>Sets the NavMeshAgent destination.</summary>
        public void MoveToPosition(Vector3 target)
        {
            if (navAgent == null || !navAgent.isOnNavMesh) return;
            hasArrived = false;
            navAgent.SetDestination(target);
        }

        /// <summary>Send agent back to their assigned desk.</summary>
        public void MoveToDesk()
        {
            if (assignedDeskTransform == null) return;
            MoveToPosition(assignedDeskTransform.position);
            SetStatus(AgentStatus.Walking);
        }

        /// <summary>Send agent to a queue slot position.</summary>
        public void MoveToQueue(Vector3 queuePos)
        {
            MoveToPosition(queuePos);
            SetStatus(AgentStatus.InQueue);
        }

        /// <summary>
        /// Send agent to stand in front of the manager's desk to present.
        /// Also spawns / shows the output page.
        /// </summary>
        public void MoveToManager()
        {
            if (managerPresentTransform == null) return;
            MoveToPosition(managerPresentTransform.position);
            SetStatus(AgentStatus.Presenting);
            SpawnOutputPage();
        }

        // ─── Approval / Rejection / Input ─────────────────────────────────────

        /// <summary>
        /// Manager approved this agent's work. Mark task complete, return to desk,
        /// then become Free after a short delay.
        /// </summary>
        public void HandleApproval()
        {
            if (currentTask != null)
                currentTask.status = TaskExecutionStatus.Completed;

            DespawnOutputPage();
            MoveToDesk();
            StartCoroutine(SetFreeAfterDelay(5f));
        }

        /// <summary>
        /// Manager rejected this agent's work. Mark task rejected, return to desk.
        /// </summary>
        public void HandleRejection()
        {
            if (currentTask != null)
                currentTask.status = TaskExecutionStatus.Rejected;

            DespawnOutputPage();
            MoveToDesk();
        }

        /// <summary>
        /// Manager provided text input. Forwards to AIServiceManager for a response,
        /// then auto-approves on success.
        /// </summary>
        public void HandleInput(string userInput)
        {
            if (currentTask != null)
                currentTask.userInput = userInput;

            AIServiceManager aiService = GameManager.Instance?.AIServiceManager;
            if (aiService == null)
            {
                Debug.LogWarning("[AgentController] AIServiceManager not found; auto-approving.");
                HandleApproval();
                return;
            }

            aiService.SendRequest(data.agentId, currentTask?.description ?? "Provide a helpful response.", userInput);

            // Listen for the response
            aiService.OnResponseReceived += OnAIResponse;
        }

        private void OnAIResponse(string respondingAgentId, string response)
        {
            if (respondingAgentId != data?.agentId) return;

            AIServiceManager aiService = GameManager.Instance?.AIServiceManager;
            if (aiService != null)
                aiService.OnResponseReceived -= OnAIResponse;

            if (currentTask != null)
                currentTask.outputResult = response;

            HandleApproval();
        }

        // ─── Status ───────────────────────────────────────────────────────────

        /// <summary>Updates the agent's status and refreshes the UI tag.</summary>
        public void SetStatus(AgentStatus status)
        {
            if (data == null) return;
            data.currentStatus = status;
            statusTag?.UpdateStatus(data);

            Debug.Log($"[AgentController] '{data.agentName}' → {status}");
        }

        // ─── Arrival Callback ─────────────────────────────────────────────────

        /// <summary>
        /// Called once per movement command when the agent reaches its destination.
        /// Dispatches to the appropriate logic based on current status.
        /// </summary>
        private void OnArrival()
        {
            if (data == null) return;

            switch (data.currentStatus)
            {
                case AgentStatus.Walking:
                    // Arrived at desk — just stand idle
                    SetStatus(AgentStatus.Free);
                    break;

                case AgentStatus.InQueue:
                    // Arrived at queue slot — wait there
                    break;

                case AgentStatus.Presenting:
                    // Arrived at manager desk — face manager
                    FaceTarget(managerPresentTransform);
                    break;

                default:
                    break;
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private IEnumerator SetFreeAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            SetStatus(AgentStatus.Free);
        }

        private void SpawnOutputPage()
        {
            if (outputPage != null && pageHoldPoint != null)
            {
                outputPage.gameObject.SetActive(true);
                outputPage.transform.SetPositionAndRotation(
                    pageHoldPoint.position,
                    pageHoldPoint.rotation);

                if (currentTask != null)
                    outputPage.DisplayTaskOutput(data, currentTask);
            }
        }

        private void DespawnOutputPage()
        {
            if (outputPage != null)
                outputPage.gameObject.SetActive(false);
        }

        private void FaceTarget(Transform target)
        {
            if (target == null) return;
            Vector3 dir = (target.position - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(dir);
        }

        private void ReturnToDesk() => MoveToDesk();
    }
}
