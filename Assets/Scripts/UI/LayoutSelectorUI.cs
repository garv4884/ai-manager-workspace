using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>Desk panel: layout templates, group discussion, auto-tasks and MR toggle.</summary>
    public class LayoutSelectorUI : MonoBehaviour
    {
        [SerializeField] private Button[] layoutButtons;      // index == (int)LayoutType
        [SerializeField] private Image[] layoutHighlights;
        [SerializeField] private TextMeshProUGUI currentLabel;
        [SerializeField] private Button meetingButton;
        [SerializeField] private TextMeshProUGUI meetingLabel;
        [SerializeField] private Button autoTasksButton;
        [SerializeField] private TextMeshProUGUI autoTasksLabel;
        [SerializeField] private Button passthroughButton;
        [SerializeField] private TextMeshProUGUI passthroughLabel;
        [SerializeField] private PassthroughToggle passthrough;

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
            meetingButton.onClick.AddListener(() => { gm.Agents.ToggleMeeting(); Refresh(); });
            autoTasksButton.onClick.AddListener(() => { gm.Dispatcher.AutoDispatch = !gm.Dispatcher.AutoDispatch; Refresh(); });
            passthroughButton.onClick.AddListener(() => { passthrough.Toggle(); Refresh(); });
            gm.Layout.Changed += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            GameManager gm = GameManager.Instance;
            LayoutType current = gm.Layout.Current;
            currentLabel.text = $"Layout: <b>{WorkspaceLayout.DisplayName(current)}</b>";
            for (int i = 0; i < layoutHighlights.Length; i++)
                layoutHighlights[i].color = (int)current == i ? Active : Idle;

            meetingLabel.text = gm.Agents.MeetingActive ? "END DISCUSSION" : "GROUP DISCUSSION";
            autoTasksLabel.text = gm.Dispatcher.AutoDispatch ? "AUTO TASKS: ON" : "AUTO TASKS: OFF";
            passthroughLabel.text = passthrough != null && passthrough.IsMixedReality ? "VIRTUAL OFFICE" : "MIXED REALITY";
        }
    }
}
