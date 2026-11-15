using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Scene-level hub. Holds references to every subsystem and is the single entry point for
    /// manager decisions, whether they come from gestures, desk buttons, the wrist menu or the keyboard.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] private AgentManager agentManager;
        [SerializeField] private QueueManager queueManager;
        [SerializeField] private AIServiceManager aiService;
        [SerializeField] private TaskDispatcher dispatcher;
        [SerializeField] private TeamPlanner planner;
        [SerializeField] private WorkspaceLayoutManager layoutManager;
        [SerializeField] private TextInputService keyboard;
        [SerializeField] private ManagerStation station;
        [SerializeField] private SessionStore session;

        public AgentManager Agents => agentManager;
        public QueueManager Queue => queueManager;
        public AIServiceManager AI => aiService;
        public TaskDispatcher Dispatcher => dispatcher;
        public TeamPlanner Planner => planner;
        public WorkspaceLayoutManager Layout => layoutManager;
        public TextInputService Keyboard => keyboard;
        public ManagerStation Station => station;
        public SessionStore Session => session;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable() { if (queueManager != null) queueManager.PresenterReady += OnPresenterReady; }
        private void OnDisable() { if (queueManager != null) queueManager.PresenterReady -= OnPresenterReady; }

        private void OnPresenterReady(AgentController agent)
        {
            // An open question can only be answered by typing, so bring the keyboard up straight away.
            if (agent.CurrentTask != null && agent.CurrentTask.request == RequestKind.Question)
                foreach (var a in agent.AvailableActions())
                    if (a.type == ManagerActionType.Answer) { Perform(a); break; }
        }

        // ── Decisions on the current presenter ────────────────────────────────

        public AgentController Presenter => queueManager.IsPresenterReady ? queueManager.Presenter : null;

        public List<ManagerAction> CurrentActions() =>
            Presenter != null ? Presenter.AvailableActions() : new List<ManagerAction>();

        /// <summary>Runs an action on the presenter, asking for text first when the action needs it.</summary>
        public void Perform(ManagerAction action)
        {
            AgentController p = Presenter;
            if (p == null) return;
            if (!action.needsText)
            {
                if (keyboard.IsOpenFor(p)) keyboard.Close();
                p.Perform(action);
                return;
            }
            keyboard.Open(action.textPrompt, p, action.textOptional,
                submit: text => p.Perform(action, text),
                valid: () => Presenter == p);
        }

        public void PerformPrimary() => PerformWhere(a => a.isPrimary);
        public void PerformRework() => PerformWhere(a => a.isRework);
        public void PerformDismiss() => PerformWhere(a => a.isDismiss);

        private void PerformWhere(System.Predicate<ManagerAction> match)
        {
            foreach (var a in CurrentActions())
                if (match(a)) { Perform(a); return; }
        }

        // ── Manager commands ──────────────────────────────────────────────────

        public void OpenAssignTaskKeyboard(AgentController agent)
        {
            if (agent == null) return;
            string prompt = agent.CurrentTask == null && agent.BacklogCount == 0
                ? $"New task for {agent.Data.agentName} ({agent.Data.agentRole})"
                : $"Queue a task for {agent.Data.agentName} (busy, {agent.BacklogCount} queued)";
            keyboard.Open(prompt, agent, false, text => dispatcher.AssignCustom(agent, text));
        }

        public void OpenRoleKeyboard(AgentController agent)
        {
            if (agent == null) return;
            keyboard.Open($"New role for {agent.Data.agentName} (now: {agent.Data.agentRole})", agent, false, agent.SetRole);
        }

        public void OpenTeamBriefKeyboard()
        {
            keyboard.Open("Brief the team: what should everyone work towards?", planner, false, planner.StartBrief);
        }
    }
}
