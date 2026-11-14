using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Scene-level hub. Holds references to every subsystem and is the single entry point
    /// for manager decisions, whether they come from gestures, desk buttons or the keyboard.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] private AgentManager agentManager;
        [SerializeField] private QueueManager queueManager;
        [SerializeField] private AIServiceManager aiService;
        [SerializeField] private TaskDispatcher dispatcher;
        [SerializeField] private WorkspaceLayoutManager layoutManager;
        [SerializeField] private VirtualKeyboardManager keyboard;

        public AgentManager Agents => agentManager;
        public QueueManager Queue => queueManager;
        public AIServiceManager AI => aiService;
        public TaskDispatcher Dispatcher => dispatcher;
        public WorkspaceLayoutManager Layout => layoutManager;
        public VirtualKeyboardManager Keyboard => keyboard;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable() { if (queueManager != null) queueManager.PresenterReady += OnPresenterReady; }
        private void OnDisable() { if (queueManager != null) queueManager.PresenterReady -= OnPresenterReady; }

        private void OnPresenterReady(AgentController agent)
        {
            if (agent.Data.status == AgentStatus.WaitingForInput) OpenAnswerKeyboard();
        }

        // ── Decisions on the current presenter ────────────────────────────────

        public AgentController Presenter => queueManager.IsPresenterReady ? queueManager.Presenter : null;

        public void Approve() => Presenter?.Approve();

        /// <summary>Quick "redo" without feedback (thumbs down).</summary>
        public void RequestChanges() => Presenter?.RequestChanges("");

        public void Dismiss()
        {
            if (Presenter == null) return;
            if (keyboard.IsOpenFor(Presenter)) keyboard.Close();
            Presenter.Dismiss();
        }

        /// <summary>Desk button: type what should change, then send the agent back.</summary>
        public void RequestChangesWithFeedback()
        {
            AgentController p = Presenter;
            if (p == null || p.Data.status != AgentStatus.WaitingForApproval) return;
            keyboard.Open($"What should {p.Data.agentName} change?", p, allowEmpty: true,
                onSubmit: text => p.RequestChanges(text), stillValid: () => Presenter == p);
        }

        public void OpenAnswerKeyboard()
        {
            AgentController p = Presenter;
            if (p == null || p.Data.status != AgentStatus.WaitingForInput) return;
            keyboard.Open($"Answer {p.Data.agentName}: {p.CurrentTask?.question}", p, allowEmpty: false,
                onSubmit: text => p.ProvideInput(text), stillValid: () => Presenter == p);
        }

        public void OpenAssignTaskKeyboard(AgentController agent)
        {
            if (agent == null) return;
            keyboard.Open($"New task for {agent.Data.agentName} ({agent.Data.agentRole})", agent, allowEmpty: false,
                onSubmit: text =>
                {
                    if (!dispatcher.AssignCustom(agent, text))
                        Debug.Log($"[GameManager] {agent.Data.agentName} is busy; task not assigned.");
                },
                stillValid: () => agent.CanTakeTask);
        }
    }
}
