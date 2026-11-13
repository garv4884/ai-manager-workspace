using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    public class OutputPageRenderer : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private TextMeshProUGUI headerTitleText;
        [SerializeField] private TextMeshProUGUI agentSignatureText;
        [SerializeField] private TextMeshProUGUI bodyContentText;
        [SerializeField] private TextMeshProUGUI actionPromptText;
        [SerializeField] private Image statusBanner;

        [Header("Paper Styling")]
        [SerializeField] private GameObject paperMesh;
        [SerializeField] private Canvas worldCanvas;

        private TaskData currentTask;

        private void Awake()
        {
            SanitizeScales();
        }

        private void OnEnable()
        {
            SanitizeScales();
        }

        public void SanitizeScales()
        {
            // Enforce (1, 1, 1) scale on every child element under this canvas
            var allTransforms = GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (t != transform)
                {
                    t.localScale = Vector3.one;
                }
            }
        }

        public void DisplayTaskOutput(AgentData agent, TaskData task)
        {
            SanitizeScales();
            currentTask = task;

            if (headerTitleText != null)
            {
                headerTitleText.text = task != null ? task.title : "Agent Work Output";
            }

            if (agentSignatureText != null)
            {
                agentSignatureText.text = agent != null 
                    ? $"Agent: {agent.agentName} ({agent.agentRole})" 
                    : "Agent Output";
            }

            if (bodyContentText != null)
            {
                if (task != null && !string.IsNullOrEmpty(task.outputResult))
                {
                    bodyContentText.text = task.outputResult;
                }
                else if (agent != null && !string.IsNullOrEmpty(agent.currentOutput))
                {
                    bodyContentText.text = agent.currentOutput;
                }
                else
                {
                    bodyContentText.text = "Plan formulated. Awaiting confirmation to proceed with execution.";
                }
            }

            if (actionPromptText != null)
            {
                if (agent != null && agent.currentStatus == AgentStatus.WaitingForApproval)
                {
                    actionPromptText.text = "✋ Action Required: Thumbs Up 👍 to Approve | Thumbs Down 👎 to Reject";
                    actionPromptText.color = new Color(1.0f, 0.7f, 0.1f);
                }
                else if (agent != null && agent.currentStatus == AgentStatus.WaitingForInput)
                {
                    actionPromptText.text = "⌨️ Action Required: Tap virtual keyboard or dictate to supply input";
                    actionPromptText.color = new Color(0.2f, 0.8f, 1.0f);
                }
                else
                {
                    actionPromptText.text = "Reviewing output";
                    actionPromptText.color = Color.white;
                }
            }

            if (statusBanner != null && agent != null)
            {
                statusBanner.color = agent.GetStatusColor();
            }
        }

        public void Clear()
        {
            if (headerTitleText != null) headerTitleText.text = string.Empty;
            if (bodyContentText != null) bodyContentText.text = string.Empty;
            if (actionPromptText != null) actionPromptText.text = string.Empty;
        }
    }
}
