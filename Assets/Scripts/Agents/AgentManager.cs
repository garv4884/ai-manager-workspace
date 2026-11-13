using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Spawns and manages all AgentController instances in the scene.
    /// Acts as the registry for all agents and delegates queue requests
    /// to QueueManager.
    /// </summary>
    public class AgentManager : MonoBehaviour
    {
        // ─── Inspector Fields ─────────────────────────────────────────────────
        [Header("Prefab & Layout")]
        [SerializeField] private GameObject agentPrefab;
        [SerializeField] private Transform[] deskTransforms;
        [SerializeField] private Transform managerPresentPoint;

        [Header("Dependencies")]
        [SerializeField] private QueueManager queueManager;
        [SerializeField] private AgentDetailPanel agentDetailPanel;

        // ─── Private State ────────────────────────────────────────────────────
        private List<AgentController> agents = new List<AgentController>();

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            // Fallback: resolve QueueManager from GameManager if not assigned
            if (queueManager == null)
                queueManager = GameManager.Instance?.QueueManager;

            int count = GameManager.Instance != null
                ? GameManager.Instance.NumberOfAgents
                : 6;

            SpawnAgents(count);
        }

        // ─── Spawning ─────────────────────────────────────────────────────────

        /// <summary>
        /// Instantiates <paramref name="count"/> agents, one per desk slot.
        /// Excess agents beyond available desks are skipped.
        /// </summary>
        public void SpawnAgents(int count)
        {
            if (agentPrefab == null)
            {
                Debug.LogError("[AgentManager] agentPrefab is not assigned!");
                return;
            }

            string[] names = GameManager.Instance?.AgentNames
                             ?? new[] { "Claude", "GPT-4", "Gemini", "Llama", "Mistral", "Copilot" };
            string[] roles = GameManager.Instance?.AgentRoles
                             ?? new[] { "Code Writer", "Researcher", "Designer", "Analyst", "Writer", "Tester" };

            int spawnCount = Mathf.Min(count,
                deskTransforms != null ? deskTransforms.Length : 0);

            for (int i = 0; i < spawnCount; i++)
            {
                Transform desk = deskTransforms[i];

                // Spawn at desk position
                GameObject go = Instantiate(agentPrefab, desk.position, desk.rotation);
                go.name = $"Agent_{(i < names.Length ? names[i] : $"Agent{i}")}";

                AgentController controller = go.GetComponent<AgentController>();
                if (controller == null)
                {
                    Debug.LogError($"[AgentManager] agentPrefab missing AgentController on '{go.name}'.");
                    continue;
                }

                // Build data
                AgentData agentData = new AgentData
                {
                    agentId = System.Guid.NewGuid().ToString(),
                    agentName = i < names.Length ? names[i] : $"Agent{i}",
                    agentRole = i < roles.Length ? roles[i] : "Assistant",
                    currentStatus = AgentStatus.Free
                };

                controller.Initialize(agentData, desk, managerPresentPoint);
                agents.Add(controller);
            }

            Debug.Log($"[AgentManager] Spawned {agents.Count} agent(s).");
        }

        // ─── Queries ──────────────────────────────────────────────────────────

        /// <summary>Returns the first agent whose status is Free, or null.</summary>
        public AgentController GetFreeAgent()
        {
            return agents.FirstOrDefault(a =>
                a != null && a.Data?.currentStatus == AgentStatus.Free);
        }

        /// <summary>Returns all agents whose status matches <paramref name="status"/>.</summary>
        public List<AgentController> GetAgentsByStatus(AgentStatus status)
        {
            return agents
                .Where(a => a != null && a.Data?.currentStatus == status)
                .ToList();
        }

        /// <summary>Returns the agent whose AgentData.agentId matches <paramref name="id"/>.</summary>
        public AgentController GetAgentById(string id)
        {
            return agents.FirstOrDefault(a =>
                a != null && a.Data?.agentId == id);
        }

        // ─── Queue ────────────────────────────────────────────────────────────

        /// <summary>
        /// Tells QueueManager to add this agent to the queue so they can
        /// present their work to the manager.
        /// </summary>
        public void RequestQueue(AgentController agent)
        {
            if (queueManager == null)
            {
                Debug.LogWarning("[AgentManager] QueueManager is null; cannot enqueue agent.");
                return;
            }
            queueManager.EnqueueAgent(agent);
        }

        // ─── Desk Reassignment ────────────────────────────────────────────────

        /// <summary>
        /// Reassigns an agent to a new desk index and sends them to it.
        /// </summary>
        public void ReassignDesk(AgentController agent, int newIndex)
        {
            if (agent == null) return;
            if (deskTransforms == null || newIndex < 0 || newIndex >= deskTransforms.Length)
            {
                Debug.LogWarning($"[AgentManager] Invalid desk index {newIndex}.");
                return;
            }

            agent.assignedDeskTransform = deskTransforms[newIndex];
            agent.MoveToDesk();
            Debug.Log($"[AgentManager] '{agent.Data?.agentName}' reassigned to desk {newIndex}.");
        }

        // ─── Demo / Testing ───────────────────────────────────────────────────

        /// <summary>
        /// For demo/testing: picks a random free agent and assigns a placeholder task.
        /// </summary>
        public void SimulateAgentWork()
        {
            List<AgentController> freeAgents = GetAgentsByStatus(AgentStatus.Free);
            if (freeAgents.Count == 0)
            {
                Debug.Log("[AgentManager] No free agents to simulate work for.");
                return;
            }

            AgentController chosen = freeAgents[Random.Range(0, freeAgents.Count)];

            TaskData task = new TaskData
            {
                taskId = System.Guid.NewGuid().ToString(),
                title = "Demo Task",
                description = "Simulated work for demonstration purposes.",
                type = (Random.value > 0.5f) ? TaskType.NeedsTextInput : TaskType.NeedsApproval,
                status = TaskExecutionStatus.InProgress,
                assignedAgentId = chosen.Data?.agentId
            };

            Debug.Log($"[AgentManager] Simulating work for '{chosen.Data?.agentName}' (type: {task.type}).");
            chosen.AssignTask(task);
        }
        /// <summary>Returns the nearest desk Transform within snapRadius, or null.</summary>
        public Transform GetNearestDesk(Vector3 position, float snapRadius)
        {
            Transform nearest = null;
            float bestDist = snapRadius;
            foreach (Transform desk in deskTransforms)
            {
                if (desk == null) continue;
                float d = Vector3.Distance(position, desk.position);
                if (d < bestDist) { bestDist = d; nearest = desk; }
            }
            return nearest;
        }

        // ─── Detail Panel ─────────────────────────────────────────────────────

        /// <summary>
        /// Opens the AgentDetailPanel for the given agent.
        /// Called by GestureInteractionManager when an agent is pinched/selected.
        /// </summary>
        public void ShowAgentDetailPanel(AgentController agent)
        {
            if (agent == null) return;

            if (agentDetailPanel == null)
            {
                Debug.LogWarning("[AgentManager] agentDetailPanel is not assigned; cannot show detail panel.");
                return;
            }

            agentDetailPanel.ShowForAgent(agent);
        }
    }
}

