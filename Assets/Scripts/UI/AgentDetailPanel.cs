using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>Desk card for the agent selected by pointing and pinching.</summary>
    public class AgentDetailPanel : MonoBehaviour
    {
        [SerializeField] private Image accentBar;
        [SerializeField] private Image statusPill;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI bodyText;
        [SerializeField] private Button assignButton;
        [SerializeField] private TextMeshProUGUI assignLabel;
        [SerializeField] private Button closeButton;

        private AgentController agent;

        private void Start()
        {
            var agents = GameManager.Instance.Agents;
            agents.SelectionChanged += Show;
            agents.AnyAgentChanged += Refresh;
            assignButton.onClick.AddListener(() => GameManager.Instance.OpenAssignTaskKeyboard(agent));
            closeButton.onClick.AddListener(() => GameManager.Instance.Agents.Select(null));
            Show(null);
        }

        private void Show(AgentController a)
        {
            agent = a;
            Refresh();
        }

        private void Refresh()
        {
            if (agent == null)
            {
                nameText.text = "No agent selected";
                statusText.text = "";
                statusPill.color = new Color(0.3f, 0.33f, 0.38f);
                accentBar.color = new Color(0.3f, 0.33f, 0.38f);
                bodyText.text = "Point at an agent or a desk and pinch to select it.\nPinch and sweep to slide a desk somewhere else.";
                assignButton.interactable = false;
                closeButton.gameObject.SetActive(false);
                return;
            }

            AgentData d = agent.Data;
            TaskData t = agent.CurrentTask;
            nameText.text = $"{d.agentName}  <size=70%><color=#9AA3B2>{d.agentRole}</color></size>";
            statusText.text = d.StatusLabel;
            statusPill.color = d.StatusColor;
            accentBar.color = d.accentColor;
            closeButton.gameObject.SetActive(true);

            string body = t != null
                ? $"<b>{t.title}</b>\n{t.description}\n<color=#9AA3B2>{(d.status == AgentStatus.Working ? $"Progress {Mathf.RoundToInt(t.progress * 100f)}%" : d.ActivityLabel)}</color>"
                : $"<color=#9AA3B2>{(d.activity == AgentActivity.InMeeting ? "In group discussion." : "Idle and ready for a task.")}</color>";
            bodyText.text = body + $"\n\n<color=#9AA3B2>Tasks completed: {d.completedTasks}</color>";

            assignButton.interactable = agent.CanTakeTask;
            assignLabel.text = agent.CanTakeTask ? "ASSIGN TASK" : "BUSY";
        }

        private void Update()
        {
            if (agent != null && agent.Data.status == AgentStatus.Working && Time.frameCount % 15 == 0) Refresh();
        }
    }
}
