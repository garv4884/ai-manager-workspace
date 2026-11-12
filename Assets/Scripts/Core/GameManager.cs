using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Central singleton manager for the AI Manager Workspace VR session.
    /// Persists across scenes and provides global access to all subsystems.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ─── Inspector Fields ─────────────────────────────────────────────────
        [Header("Agent Configuration")]
        [SerializeField] private int numberOfAgents = 6;

        [SerializeField] private string[] agentNames =
        {
            "Claude", "GPT-4", "Gemini", "Llama", "Mistral", "Copilot"
        };

        [SerializeField] private string[] agentRoles =
        {
            "Code Writer", "Researcher", "Designer", "Analyst", "Writer", "Tester"
        };

        // ─── Public Properties (subsystem access) ─────────────────────────────
        public AgentManager AgentManager { get; private set; }
        public QueueManager QueueManager { get; private set; }
        public WorkspaceLayoutManager WorkspaceLayoutManager { get; private set; }
        public AIServiceManager AIServiceManager { get; private set; }

        // ─── Exposed Config Properties ────────────────────────────────────────
        public int NumberOfAgents => numberOfAgents;
        public string[] AgentNames => agentNames;
        public string[] AgentRoles => agentRoles;

        // ─── Events ───────────────────────────────────────────────────────────
        [Header("Events")]
        public UnityEvent OnSessionStarted = new UnityEvent();

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            InitializeSystems();
            OnSessionStarted?.Invoke();
        }

        // ─── Initialisation ───────────────────────────────────────────────────

        /// <summary>
        /// Finds existing manager components or creates default GameObjects for each.
        /// </summary>
        private void InitializeSystems()
        {
            AgentManager = FindOrCreate<AgentManager>("AgentManager");
            QueueManager = FindOrCreate<QueueManager>("QueueManager");
            WorkspaceLayoutManager = FindOrCreate<WorkspaceLayoutManager>("WorkspaceLayoutManager");
            AIServiceManager = FindOrCreate<AIServiceManager>("AIServiceManager");

            Debug.Log("[GameManager] All systems initialised.");
        }

        /// <summary>
        /// Returns an existing instance of T in the scene, or creates a new
        /// child GameObject with T attached.
        /// </summary>
        private T FindOrCreate<T>(string goName) where T : Component
        {
            T existing = FindFirstObjectByType<T>();
            if (existing != null)
                return existing;

            GameObject go = new GameObject(goName);
            go.transform.SetParent(transform);
            T component = go.AddComponent<T>();
            Debug.Log($"[GameManager] Created {goName}.");
            return component;
        }

        // ─── Public Agent Event Handlers ──────────────────────────────────────

        /// <summary>
        /// Called when the manager (player) approves an agent's work.
        /// </summary>
        public void OnAgentApproved(AgentController agent)
        {
            if (agent == null) return;
            Debug.Log($"[GameManager] Agent '{agent.name}' approved.");
            agent.HandleApproval();
            QueueManager?.OnManagerDoneWithPresenter();
        }

        /// <summary>
        /// Called when the manager (player) rejects an agent's work.
        /// </summary>
        public void OnAgentRejected(AgentController agent)
        {
            if (agent == null) return;
            Debug.Log($"[GameManager] Agent '{agent.name}' rejected.");
            agent.HandleRejection();
            QueueManager?.OnManagerDoneWithPresenter();
        }

        /// <summary>
        /// Called when the manager (player) types / speaks input for an agent
        /// that is waiting for clarification.
        /// </summary>
        public void OnAgentInputReceived(AgentController agent, string text)
        {
            if (agent == null || string.IsNullOrWhiteSpace(text)) return;
            Debug.Log($"[GameManager] Input for '{agent.name}': {text}");
            agent.HandleInput(text);
        }
    }
}
