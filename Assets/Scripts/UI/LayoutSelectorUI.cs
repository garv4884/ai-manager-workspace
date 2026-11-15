using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>Desk panel: team brief, layout templates, group discussion, demo mode and MR toggle.</summary>
    public class LayoutSelectorUI : MonoBehaviour
    {
        [SerializeField] private Button[] layoutButtons;      // index == (int)LayoutType
        [SerializeField] private Image[] layoutHighlights;
        [SerializeField] private TextMeshProUGUI currentLabel;
        [SerializeField] private Button briefButton;
        [SerializeField] private Button meetingButton;
        [SerializeField] private TextMeshProUGUI meetingLabel;
        [SerializeField] private Button autoTasksButton;
        [SerializeField] private TextMeshProUGUI autoTasksLabel;
        [SerializeField] private Button passthroughButton;
        [SerializeField] private TextMeshProUGUI passthroughLabel;
        [SerializeField] private PassthroughToggle passthrough;
        [SerializeField] private Button holdButton;
        [SerializeField] private TextMeshProUGUI holdLabel;
        [SerializeField] private Button settingsButton;
        [SerializeField] private SettingsPanelUI settings;

        private static readonly Color Idle = new Color(0.20f, 0.23f, 0.28f);
        private static readonly Color Active = new Color(0.18f, 0.55f, 0.52f);

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            for (int i = 0; i < layoutButtons.Length; i++)
            {
                var type = (LayoutType)i;
                layoutButtons[i].onClick.AddListener(() => gm.Layout.ApplyLayout(type));
            }
            briefButton.onClick.AddListener(gm.OpenTeamBriefKeyboard);
            gm.Planner.Changed += Refresh;
            meetingButton.onClick.AddListener(() => { gm.Agents.ToggleMeeting(); Refresh(); });
            autoTasksButton.onClick.AddListener(() => { gm.Dispatcher.AutoDispatch = !gm.Dispatcher.AutoDispatch; Refresh(); });
            passthroughButton.onClick.AddListener(() => { passthrough.Toggle(); Refresh(); });
            gm.Layout.Changed += Refresh;
            holdButton.onClick.AddListener(() => gm.Queue.Hold = !gm.Queue.Hold);
            gm.Queue.Changed += Refresh;
            settingsButton.onClick.AddListener(() => settings.Show());
            Refresh();
        }

        private void Refresh()
        {
            GameManager gm = GameManager.Instance;
            LayoutType current = gm.Layout.Current;
            currentLabel.text = $"Layout: <b>{WorkspaceLayout.DisplayName(current)}</b>";
            var brief = gm.Planner.Active;
            if (brief != null) currentLabel.text += $"   <color=#4FD1C5>Goal {brief.Finished}/{brief.tasks.Count}</color>";
            for (int i = 0; i < layoutButtons.Length; i++)
            {
                var colors = layoutButtons[i].colors;
                Color c = (int)current == i ? Active : Idle;
                colors.normalColor = c;
                colors.selectedColor = c;
                colors.highlightedColor = Color.Lerp(c, Color.white, 0.25f);
                layoutButtons[i].colors = colors;
            }

            meetingLabel.text = gm.Agents.MeetingActive ? "END DISCUSSION" : "DISCUSSION";
            autoTasksLabel.text = gm.Dispatcher.AutoDispatch ? "DEMO: ON" : "DEMO: OFF";
            holdLabel.text = gm.Queue.Hold ? "<color=#FFB347>RESUME QUEUE</color>" : "HOLD QUEUE";
            passthroughLabel.text = passthrough != null && passthrough.IsMixedReality ? "VIRTUAL OFFICE" : "MIXED REALITY";
        }
    }
}
