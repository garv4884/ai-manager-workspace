using TMPro;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// One agent's desk, chair and monitor. The root's forward axis is the direction the
    /// seated agent faces. Layout templates and hand-dragging move this root, and a seated
    /// agent moves with it (it is parented to the seat).
    /// </summary>
    public class Workstation : MonoBehaviour
    {
        [SerializeField] private Transform seatPoint;
        [SerializeField] private Transform approachPoint;
        [SerializeField] private Renderer screen;
        [SerializeField] private Renderer statusBar;
        [SerializeField] private Renderer[] chairRenderers;
        [SerializeField] private TextMeshProUGUI nameplate;
        [SerializeField] private TextMeshProUGUI screenText;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock block;
        private float phase;
        private int lastPct = -2;
        private string lastTitle;

        private void Awake() => phase = Random.Range(0f, 6.28f);

        public AgentController Owner { get; private set; }
        public Transform SeatPoint => seatPoint;
        public Vector3 ApproachPosition => approachPoint != null ? approachPoint.position : seatPoint.position - seatPoint.forward * 0.6f;

        public void SetOwner(AgentController agent)
        {
            Owner = agent;
            if (nameplate != null) nameplate.text = agent != null ? agent.Data.agentName.ToUpperInvariant() : "";
            if (agent == null) return;

            if (agent != null && chairRenderers != null)
            {
                var b = new MaterialPropertyBlock();
                b.SetColor(BaseColorId, Color.Lerp(agent.Data.accentColor, new Color(0.2f, 0.2f, 0.22f), 0.35f));
                foreach (var r in chairRenderers) if (r != null) r.SetPropertyBlock(b);
            }
        }

        private void Update()
        {
            if (Owner == null || Owner.Data == null) return;
            block ??= new MaterialPropertyBlock();

            AgentStatus status = Owner.Data.status;
            Color statusColor = Owner.Data.StatusColor;

            if (statusBar != null)
            {
                float pulse = Owner.Data.NeedsManager ? 0.6f + 0.4f * Mathf.Sin(Time.time * 5f) : 1f;
                block.Clear();
                block.SetColor(BaseColorId, statusColor * pulse);
                statusBar.SetPropertyBlock(block);
            }

            if (screen != null)
            {
                bool active = status == AgentStatus.Working;
                Color c = active ? new Color(0.35f, 0.62f, 0.95f) : new Color(0.16f, 0.2f, 0.26f);
                float flicker = active ? 0.9f + 0.1f * Mathf.Sin(Time.time * 9f + phase) : 0.5f;
                block.Clear();
                block.SetColor(BaseColorId, c * flicker);
                screen.SetPropertyBlock(block);
            }

            if (screenText != null)
            {
                TaskData task = Owner.CurrentTask;
                float progress = Owner.WorkProgress;
                int pct = progress >= 0f ? Mathf.RoundToInt(progress * 100f) : -1;
                string title = status == AgentStatus.Working ? Owner.WorkTitle ?? "" : "";
                if (pct != lastPct || title != lastTitle)
                {
                    lastPct = pct;
                    lastTitle = title;
                    screenText.text = title.Length > 0 ? title + (pct >= 0 ? $"\n{pct}%" : "") : "";
                }
            }
        }
    }
}
