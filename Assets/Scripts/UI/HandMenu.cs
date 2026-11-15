using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Horizon-style wrist menu: turn your left palm towards your face and a small panel appears
    /// above the hand; poke it with the other hand. Works anywhere in the office, so you can
    /// manage the team after teleporting away from the desk. Hidden while hands aren't tracked.
    /// </summary>
    public class HandMenu : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Button briefButton;
        [SerializeField] private Button demoButton;
        [SerializeField] private TextMeshProUGUI demoLabel;
        [SerializeField] private Button layoutButton;
        [SerializeField] private TextMeshProUGUI layoutLabel;
        [SerializeField] private Button mrButton;
        [SerializeField] private Button deskButton;
        [SerializeField] private Button fitButton;
        [SerializeField] private Button holdButton;
        [SerializeField] private TextMeshProUGUI holdLabel;
        [SerializeField] private PassthroughToggle passthrough;
        [SerializeField] private Vector3 offsetFromPalm = new Vector3(0f, 0.12f, 0f);
        [SerializeField] private float showDot = 0.55f;

        private Hand left;
        private float nextSearch;
        private float visibleBlend;

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            briefButton.onClick.AddListener(gm.OpenTeamBriefKeyboard);
            demoButton.onClick.AddListener(() => { gm.Dispatcher.AutoDispatch = !gm.Dispatcher.AutoDispatch; Refresh(); });
            layoutButton.onClick.AddListener(() =>
            {
                int next = ((int)gm.Layout.Current + 1) % (int)LayoutType.Custom;
                gm.Layout.ApplyLayout((LayoutType)next);
                Refresh();
            });
            mrButton.onClick.AddListener(passthrough.Toggle);
            deskButton.onClick.AddListener(gm.Station.ReturnToDesk);
            fitButton.onClick.AddListener(gm.Station.FitDeskToHead);
            holdButton.onClick.AddListener(() => { gm.Queue.Hold = !gm.Queue.Hold; Refresh(); });
            panel.SetActive(false);
            Refresh();
        }

        private void Refresh()
        {
            GameManager gm = GameManager.Instance;
            demoLabel.text = gm.Dispatcher.AutoDispatch ? "DEMO: ON" : "DEMO: OFF";
            layoutLabel.text = WorkspaceLayout.DisplayName(gm.Layout.Current).ToUpperInvariant();
            int waiting = gm.Queue.Waiting.Count;
            holdLabel.text = gm.Queue.Hold ? $"RESUME QUEUE ({waiting} waiting)" : "HOLD QUEUE";
        }

        private void LateUpdate()
        {
            if (left == null && Time.unscaledTime > nextSearch)
            {
                nextSearch = Time.unscaledTime + 2f;
                foreach (var h in FindObjectsByType<Hand>(FindObjectsInactive.Exclude))
                    if (h.Handedness == Handedness.Left) { left = h; break; }
            }

            Transform head = Camera.main != null ? Camera.main.transform : null;
            bool show = false;
            Pose palm = default;
            if (left != null && head != null && left.IsTrackedDataValid && left.GetJointPose(HandJointId.HandPalm, out palm)
                && left.GetJointPose(HandJointId.HandWristRoot, out Pose wrist)
                && left.GetJointPose(HandJointId.HandIndex1, out Pose index)
                && left.GetJointPose(HandJointId.HandPinky1, out Pose pinky))
            {
                // Left hand: this cross product points out of the palm.
                Vector3 normal = Vector3.Cross(index.position - wrist.position, pinky.position - wrist.position).normalized;
                Vector3 toHead = (head.position - palm.position).normalized;
                show = Vector3.Dot(normal, toHead) > showDot;
            }

            // Small hysteresis so the menu doesn't flicker at the threshold.
            visibleBlend = Mathf.MoveTowards(visibleBlend, show ? 1f : 0f, Time.deltaTime * 6f);
            bool visible = visibleBlend > 0.5f;
            if (panel.activeSelf != visible)
            {
                panel.SetActive(visible);
                if (visible) Refresh();
            }
            if (!visible) return;

            Vector3 pos = palm.position + offsetFromPalm;
            Vector3 face = pos - head.position;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face, Vector3.up));
        }
    }
}
