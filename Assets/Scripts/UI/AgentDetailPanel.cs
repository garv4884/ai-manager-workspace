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
        [SerializeField] private Button roleButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button closeButton;

        private AgentController agent;

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            gm.Agents.SelectionChanged += Show;
            gm.Agents.AnyAgentChanged += Refresh;
            gm.Queue.Changed += Refresh;
            assignButton.onClick.AddListener(() => gm.OpenAssignTaskKeyboard(agent));
            roleButton.onClick.AddListener(() => gm.OpenRoleKeyboard(agent));
            nextButton.onClick.AddListener(() => { gm.Queue.PromoteToFront(agent); Refresh(); });
            closeButton.onClick.AddListener(() => gm.Agents.Select(null));
            Show(null);
        }

        private void Show(AgentController a)
        {
            agent = a;
            Refresh();
        }

        private void Refresh()
        {
            bool has = agent != null;
            assignButton.gameObject.SetActive(has);
            roleButton.gameObject.SetActive(has);
            closeButton.gameObject.SetActive(has);

            if (!has)
            {
                nameText.text = "No agent selected";
                statusText.text = "";
                statusPill.color = new Color(0.3f, 0.33f, 0.38f);
                accentBar.color = new Color(0.3f, 0.33f, 0.38f);
                bodyText.text = "Point at an agent or desk and pinch to select it.\nPinch and sweep to slide a desk.\n\nTo give everyone one goal, use BRIEF THE TEAM on the left panel or your wrist menu.";
                nextButton.gameObject.SetActive(false);
                return;
            }

            AgentData d = agent.Data;
            TaskData t = agent.CurrentTask;
            nameText.text = $"{d.agentName}  <size=70%><color=#9AA3B2>{d.agentRole}</color></size>";
            statusText.text = d.StatusLabel;
            statusPill.color = d.StatusColor;
            accentBar.color = d.accentColor;

            string body = t != null
                ? $"<b>{t.title}</b>\n{TaskLibrary.Trim(t.description, 140)}\n<color=#9AA3B2>{(d.status == AgentStatus.Working ? $"Progress {Mathf.RoundToInt(t.progress * 100f)}%" : d.ActivityLabel)}</color>"
                : $"<color=#9AA3B2>{(d.activity == AgentActivity.InMeeting ? "In group discussion." : "Idle and ready for a task.")}</color>";
            if (agent.BacklogCount > 0) body += $"\n<color=#4FD1C5>{agent.BacklogCount} more task(s) queued</color>";
            bodyText.text = body + $"\n<color=#9AA3B2>Tasks completed: {d.completedTasks}</color>";

            bool idle = t == null && agent.BacklogCount == 0;
            assignLabel.text = idle ? "ASSIGN TASK" : "QUEUE TASK";
            nextButton.gameObject.SetActive(d.activity == AgentActivity.InQueue && d.queuePosition > 0);
        }

        private void Update()
        {
            if (agent != null && agent.Data.status == AgentStatus.Working && Time.frameCount % 15 == 0) Refresh();
        }
    }
}
