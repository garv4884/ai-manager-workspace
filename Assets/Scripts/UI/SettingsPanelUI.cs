using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Second page of the desk's workspace panel: AI connection (enter / test / remove the API key),
    /// the work history (every approved, acknowledged or dropped piece of work, newest first) and
    /// "reset session". Pinching the done tray on the desk opens it on the history.
    /// </summary>
    public class SettingsPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject mainView;
        [SerializeField] private GameObject settingsView;
        [SerializeField] private TextMeshProUGUI modeText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button enterKeyButton;
        [SerializeField] private Button testButton;
        [SerializeField] private Button removeKeyButton;
        [SerializeField] private TextMeshProUGUI historyTitle;
        [SerializeField] private TextMeshProUGUI historyBody;
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private TextMeshProUGUI resetLabel;
        [SerializeField] private Button backButton;

        private int index;            // 0 = newest
        private float resetArmedUntil;
        private string status = "";

        public static SettingsPanelUI Instance { get; private set; }

        private void Awake() => Instance = this;

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            enterKeyButton.onClick.AddListener(() =>
                gm.Keyboard.Open("Paste or type your Anthropic API key", this, false, key =>
                {
                    gm.AI.SetApiKey(key);
                    status = "Key saved. Testing...";
                    Refresh();
                    Test();
                }, secretText: true));
            testButton.onClick.AddListener(Test);
            removeKeyButton.onClick.AddListener(() => { gm.AI.ClearApiKey(); status = "Key removed: agents use the offline simulation."; Refresh(); });
            prevButton.onClick.AddListener(() => { index++; Refresh(); });
            nextButton.onClick.AddListener(() => { index--; Refresh(); });
            resetButton.onClick.AddListener(OnReset);
            backButton.onClick.AddListener(Hide);
            gm.AI.ConfigChanged += Refresh;
            if (gm.Session != null) gm.Session.HistoryChanged += () => { index = 0; Refresh(); };
            settingsView.SetActive(false);
        }

        public void Show(bool history = false)
        {
            if (history) index = 0;
            status = "";
            mainView.SetActive(false);
            settingsView.SetActive(true);
            Refresh();
        }

        /// <summary>Opens straight on the newest history entry (done tray click).</summary>
        public void ShowHistory() => Show(true);

        public void Hide()
        {
            GameManager gm = GameManager.Instance;
            if (gm.Keyboard.IsOpenFor(this)) gm.Keyboard.Close();
            settingsView.SetActive(false);
            mainView.SetActive(true);
        }

        private void Test()
        {
            status = "Testing the connection...";
            Refresh();
            GameManager.Instance.AI.TestConnection((ok, msg) =>
            {
                status = ok ? $"<color=#4FD1C5>{msg}</color>" : $"<color=#F07272>{msg}</color>";
                Refresh();
            });
        }

        private void OnReset()
        {
            if (Time.unscaledTime < resetArmedUntil)
            {
                GameManager.Instance.Session.ResetSession();
                return;
            }
            resetArmedUntil = Time.unscaledTime + 4f;
            Refresh();
        }

        private void Update()
        {
            if (resetArmedUntil > 0f && Time.unscaledTime >= resetArmedUntil) { resetArmedUntil = 0f; Refresh(); }
        }

        private void Refresh()
        {
            if (settingsView == null || !settingsView.activeSelf) return;
            GameManager gm = GameManager.Instance;
            AIServiceManager ai = gm.AI;

            modeText.text = ai.IsLive
                ? $"AI: <color=#4FD1C5><b>LIVE</b></color>  {ai.Model}  <color=#9AA3B2>(key saved on this device)</color>"
                : "AI: <color=#FFB347><b>OFFLINE SIMULATION</b></color>  <color=#9AA3B2>(no API key)</color>";
            statusText.text = status.Length > 0 ? status
                : ai.IsLive ? "<color=#9AA3B2>Agents call Claude for real work.</color>"
                : "<color=#9AA3B2>Enter a key here, or in Unity: Tools > AI Manager > Quest API Key (pushes it over USB).</color>";
            testButton.interactable = ai.IsLive;
            removeKeyButton.interactable = ai.IsLive;

            var history = gm.Session != null ? gm.Session.History : null;
            int count = history != null ? history.Count : 0;
            index = Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));
            historyTitle.text = count == 0 ? "WORK HISTORY" : $"WORK HISTORY   <color=#9AA3B2>{index + 1} / {count}</color>";
            if (count == 0)
            {
                historyBody.text = "<color=#9AA3B2>Nothing finished yet. Approved, acknowledged and dropped work shows up here, " +
                                   "and is kept between sessions.</color>";
            }
            else
            {
                HistoryEntry h = history[count - 1 - index];
                string colour = h.outcome == "Dropped" ? "#F07272" : "#4FD1C5";
                historyBody.text = $"<b>{h.title}</b>\n<size=80%><color=#9AA3B2>{h.agent} · {h.role} · {h.time}</color>   " +
                                   $"<color={colour}>{h.outcome.ToUpperInvariant()}</color></size>\n{h.text}";
            }
            prevButton.interactable = index < count - 1;
            nextButton.interactable = index > 0;

            bool armed = Time.unscaledTime < resetArmedUntil;
            resetLabel.text = armed ? "PRESS AGAIN TO RESET" : "RESET SESSION";
        }
    }
}
