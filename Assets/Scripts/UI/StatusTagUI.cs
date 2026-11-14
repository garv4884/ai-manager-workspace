using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Floating tag above an agent's head: name, role, a colour-coded status pill and a
    /// detail line (progress, place in line...). It turns to face the manager and grows
    /// with distance so it stays readable from the elevated desk.
    /// </summary>
    public class StatusTagUI : MonoBehaviour
    {
        [SerializeField] private Image pill;
        [SerializeField] private Image accentBar;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI roleText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI detailText;
        [SerializeField] private float headClearance = 0.38f;
        [SerializeField] private float referenceDistance = 3.0f;
        [SerializeField] private float maxGrowth = 3.2f;

        private AgentController agent;
        private Transform cam;
        private Vector3 baseScale;

        public void Bind(AgentController owner)
        {
            agent = owner;
            baseScale = transform.localScale;
            agent.Changed += _ => Refresh();
            if (accentBar != null) accentBar.color = agent.Data.accentColor;
            Refresh();
        }

        private void Refresh()
        {
            if (agent == null) return;
            AgentData d = agent.Data;
            if (nameText != null) nameText.text = d.agentName;
            if (roleText != null) roleText.text = d.agentRole;
            if (statusText != null) statusText.text = d.StatusLabel;
            if (pill != null) pill.color = d.StatusColor;

            if (detailText != null)
            {
                TaskData t = agent.CurrentTask;
                string detail = d.ActivityLabel;
                if (d.status == AgentStatus.Working && t != null && d.activity == AgentActivity.AtDesk)
                    detail = $"{Mathf.RoundToInt(t.progress * 100f)}%  {t.title}";
                detailText.text = detail;
            }
        }

        private void LateUpdate()
        {
            if (agent == null) return;
            if (cam == null) { if (Camera.main == null) return; cam = Camera.main.transform; }

            Transform head = agent.Avatar.Head;
            transform.position = head.position + Vector3.up * headClearance;

            Vector3 toTag = transform.position - cam.position;
            if (toTag.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(toTag, Vector3.up);

            float growth = Mathf.Clamp(toTag.magnitude / referenceDistance, 1f, maxGrowth);
            if (agent.Data.NeedsManager) growth *= 1f + 0.06f * Mathf.Sin(Time.time * 6f);
            transform.localScale = baseScale * growth;

            if (agent.Data.status == AgentStatus.Working && Time.frameCount % 10 == 0) Refresh();
        }
    }
}
