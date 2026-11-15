using System.Text;
using TMPro;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>Large team board on the far wall: every agent's state at a glance.</summary>
    public class SessionDashboardUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI headerText;
        [SerializeField] private TextMeshProUGUI rosterText;
        [SerializeField] private TextMeshProUGUI footerText;
        [SerializeField] private float refreshInterval = 0.5f;

        private readonly StringBuilder sb = new StringBuilder();

        private void OnEnable() => InvokeRepeating(nameof(Refresh), 0.2f, refreshInterval);
        private void OnDisable() => CancelInvoke(nameof(Refresh));

        private void Refresh()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            AgentManager agents = gm.Agents;
            QueueManager queue = gm.Queue;

            int waiting = agents.Count(AgentStatus.WaitingForApproval) + agents.Count(AgentStatus.WaitingForInput) + agents.Count(AgentStatus.HasUpdate);
            headerText.text =
                $"<color=#4FD1C5>TEAM</color>   " +
                $"{Chip(AgentStatus.Free, agents.Count(AgentStatus.Free), "free")}   " +
                $"{Chip(AgentStatus.Working, agents.Count(AgentStatus.Working), "working")}   " +
                $"{Chip(AgentStatus.WaitingForApproval, waiting, "waiting for you")}   " +
                $"{Chip(AgentStatus.Error, agents.Count(AgentStatus.Error), "blocked")}";

            sb.Clear();
            foreach (var a in agents.Agents)
            {
                AgentData d = a.Data;
                string task = a.CurrentTask != null ? a.CurrentTask.title : "-";
                if (a.BacklogCount > 0) task += $" (+{a.BacklogCount})";
                string where = d.ActivityLabel;
                if (d.status == AgentStatus.Working && a.CurrentTask != null && d.activity == AgentActivity.AtDesk)
                    where = $"{Mathf.RoundToInt(a.CurrentTask.progress * 100f)}%";
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(d.accentColor)}>|</color> <b>{d.agentName}</b> <size=70%><color=#9AA3B2>{d.agentRole}</color></size>")
                  .Append($"<pos=30%><color=#{ColorUtility.ToHtmlStringRGB(d.StatusColor)}>{d.StatusLabel}</color>")
                  .Append($"<pos=52%>{Trim(task, 24)}")
                  .Append($"<pos=82%><color=#9AA3B2>{where}</color>\n");
            }
            rosterText.text = sb.ToString();

            string presenting = queue.Presenter != null ? queue.Presenter.Data.agentName : "nobody";
            var brief = gm.Planner.Active;
            string goal = brief != null ? $"Goal: <b>{TaskLibrary.Trim(brief.goal, 34)}</b> ({brief.Finished}/{brief.tasks.Count})     " : "";
            footerText.text = goal +
                $"Presenting: <b>{presenting}</b>     In line: <b>{queue.Waiting.Count}</b>     " +
                $"Approved today: <b>{queue.Desk.Completed}</b>     <color=#9AA3B2>{gm.AI.ModeLabel}</color>";
        }

        private static string Chip(AgentStatus s, int n, string label) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(AgentData.ColorFor(s))}><b>{n}</b> {label}</color>";

        private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 3) + "...";
    }
}
