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
        [SerializeField] private Button mrButton;
        [SerializeField] private Button deskButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button heightUpButton;
        [SerializeField] private Button heightDownButton;
        [SerializeField] private Button autoHeightButton;
        [SerializeField] private Button holdButton;
        [SerializeField] private TextMeshProUGUI holdLabel;
        [SerializeField] private PassthroughToggle passthrough;
        [SerializeField] private Vector3 offsetFromPalm = new Vector3(0f, 0.12f, 0f);
        [SerializeField] private float showDot = 0.55f;

        private Hand left;   // the menu hand (left by default; right in left-handed setups)
        private Handedness menuHand = Handedness.Left;
        public bool IsVisible => panel != null && panel.activeSelf;
        private float nextSearch;
        private float visibleBlend;

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            if (heightUpButton != null) heightUpButton.onClick.AddListener(() => gm.Station.NudgeHeight(0.05f));
            if (heightDownButton != null) heightDownButton.onClick.AddListener(() => gm.Station.NudgeHeight(-0.05f));
            if (autoHeightButton != null) autoHeightButton.onClick.AddListener(gm.Station.AutoHeight);
            mrButton.onClick.AddListener(passthrough.Toggle);
            passthrough.Changed += Refresh;
            gm.Queue.Changed += Refresh;
            deskButton.onClick.AddListener(() => { gm.Station.ReturnToDesk(); gm.Station.CorrectHeight(false); gm.Station.FitDeskToHead(); });
            settingsButton.onClick.AddListener(() => { if (SettingsWindow.Instance != null) SettingsWindow.Instance.Open(); gm.Station.ReturnToDesk(); });
            holdButton.onClick.AddListener(() => { gm.Queue.Hold = !gm.Queue.Hold; Refresh(); });
            // The wrist menu is always the closest thing to you: draw it over every other panel (tutorial, settings).
            var canvas = panel.GetComponent<Canvas>();
            if (canvas != null) canvas.sortingOrder = 60;
            panel.SetActive(false);
            Refresh();
        }

        private void Refresh()
        {
            GameManager gm = GameManager.Instance;

            int waiting = gm.Queue.WaitingTotal;
            holdLabel.text = gm.Queue.Hold ? $"RESUME QUEUE ({waiting} waiting)" : "HOLD QUEUE";
        }

        private void LateUpdate()
        {
            Handedness wanted = AppSettings.Current.menuOnRightHand ? Handedness.Right : Handedness.Left;
            if (wanted != menuHand)
            {
                menuHand = wanted;
                left = null;
                nextSearch = 0f;
                var filter = GetComponent<HandednessFilter>();
                if (filter != null) filter.Allowed = wanted == Handedness.Left ? Handedness.Right : Handedness.Left;
            }
            if (left == null && Time.unscaledTime > nextSearch)
            {
                nextSearch = Time.unscaledTime + 2f;
                foreach (var h in FindObjectsByType<Hand>(FindObjectsInactive.Exclude))
                    if (h.Handedness == menuHand) { left = h; break; }
            }

            Transform head = Camera.main != null ? Camera.main.transform : null;
            bool show = false;
            Pose palm = default;
            if (left != null && head != null && left.IsTrackedDataValid && left.GetJointPose(HandJointId.HandPalm, out palm)
                && left.GetJointPose(HandJointId.HandWristRoot, out Pose wrist)
                && left.GetJointPose(HandJointId.HandIndex1, out Pose index)
                && left.GetJointPose(HandJointId.HandPinky1, out Pose pinky))
            {
                // Left hand: this cross product points out of the palm (flipped for the right hand).
                Vector3 normal = Vector3.Cross(index.position - wrist.position, pinky.position - wrist.position).normalized;
                if (menuHand == Handedness.Right) normal = -normal;
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
