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
        [SerializeField] private SettingsWindow settings;
        [SerializeField] private Button addAgentButton;
        [SerializeField] private TextMeshProUGUI addAgentLabel;
        [Header("Dropdowns")]
        [SerializeField] private Button layoutDropdown;
        [SerializeField] private TextMeshProUGUI layoutValue;
        [SerializeField] private Button themeDropdown;
        [SerializeField] private TextMeshProUGUI themeValue;
        [SerializeField] private Button historyButton;
        [SerializeField] private Button tourButton;

        private GameObject popup;

        private static readonly Color Idle = new Color(0.20f, 0.23f, 0.28f);
        private static readonly Color Active = new Color(0.18f, 0.55f, 0.52f);

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            if (layoutButtons != null)
                for (int i = 0; i < layoutButtons.Length; i++)
                {
                    var type = (LayoutType)i;
                    if (layoutButtons[i] != null) layoutButtons[i].onClick.AddListener(() => gm.Layout.ApplyLayout(type));
                }
            if (layoutDropdown != null) layoutDropdown.onClick.AddListener(() => OpenPopup(layoutDropdown, LayoutNames(),
                gm.Layout.Current == LayoutType.Custom ? -1 : (int)gm.Layout.Current, i => gm.Layout.ApplyLayout((LayoutType)i)));
            if (themeDropdown != null) themeDropdown.onClick.AddListener(() => OpenPopup(themeDropdown, ThemeNames(),
                (int)AppSettings.Current.theme, i => { AppSettings.Current.theme = (RoomTheme)i; AppSettings.Save(); Refresh(); }));
            if (historyButton != null) historyButton.onClick.AddListener(() => settings.OpenHistory());
            if (tourButton != null) tourButton.onClick.AddListener(() => TutorialManager.Instance?.Begin());
            AppSettings.Changed += Refresh;
            briefButton.onClick.AddListener(gm.OpenTeamBriefKeyboard);
            gm.Planner.Changed += Refresh;
            meetingButton.onClick.AddListener(() => { gm.Agents.ToggleMeeting(); Refresh(); });
            autoTasksButton.onClick.AddListener(() => { gm.Dispatcher.AutoDispatch = !gm.Dispatcher.AutoDispatch; Refresh(); });
            passthroughButton.onClick.AddListener(passthrough.Toggle);
            passthrough.Changed += Refresh;
            gm.Layout.Changed += Refresh;
            holdButton.onClick.AddListener(() => gm.Queue.Hold = !gm.Queue.Hold);
            gm.Queue.Changed += Refresh;
            settingsButton.onClick.AddListener(() => settings.Toggle());
            addAgentButton.onClick.AddListener(() =>
            {
                AgentController added = gm.Agents.AddAgent();
                if (added != null) gm.Agents.Select(added);
            });
            gm.Agents.RosterChanged += Refresh;
            Refresh();
        }

        private static string[] LayoutNames()
        {
            var n = new string[(int)LayoutType.Custom];
            for (int i = 0; i < n.Length; i++) n[i] = WorkspaceLayout.DisplayName((LayoutType)i);
            return n;
        }

        private static string[] ThemeNames()
        {
            var values = (RoomTheme[])System.Enum.GetValues(typeof(RoomTheme));
            var n = new string[values.Length];
            for (int i = 0; i < n.Length; i++) n[i] = AppSettings.ThemeName(values[i]);
            return n;
        }

        private void OpenPopup(Button anchor, string[] options, int current, System.Action<int> pick)
        {
            if (popup != null) { Destroy(popup); popup = null; return; }   // second tap closes
            var rt = (RectTransform)anchor.transform;
            var parent = (RectTransform)rt.parent;
            Vector2 centre = (Vector2)rt.localPosition - parent.rect.center;   // works with any anchors
            Vector2 top = centre + new Vector2(0f, -rt.rect.height * 0.5f - 4f);
            popup = UiKit.Popup(rt.parent, top, rt.rect.width, options, current, i => { popup = null; pick(i); });
        }

        private void Refresh()
        {
            GameManager gm = GameManager.Instance;
            LayoutType current = gm.Layout.Current;
            int waiting = gm.Queue.WaitingTotal;
            currentLabel.text = $"<b>{gm.Agents.Agents.Count}</b> agents   <color=#9AA3B2>{(waiting > 0 ? $"{waiting} waiting for you" : "nobody waiting")}</color>";
            var brief = gm.Planner.Active;
            if (brief != null) currentLabel.text += $"   <color=#4FD1C5>Goal {brief.Finished}/{brief.tasks.Count}</color>";
            if (layoutValue != null) layoutValue.text = WorkspaceLayout.DisplayName(current);
            if (themeValue != null) themeValue.text = AppSettings.ThemeName(AppSettings.Current.theme);
            if (layoutButtons != null) for (int i = 0; i < layoutButtons.Length; i++)
            {
                if (layoutButtons[i] == null) continue;
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
            addAgentButton.interactable = gm.Agents.CanAdd;
            addAgentLabel.text = gm.Agents.CanAdd ? $"ADD AGENT  <color=#9AA3B2>{gm.Agents.Agents.Count}/{gm.Agents.MaxAgents}</color>" : "TEAM FULL";
            passthroughLabel.text = passthrough != null && passthrough.IsMixedReality ? "VIRTUAL OFFICE" : "MIXED REALITY";
        }
    }
}
