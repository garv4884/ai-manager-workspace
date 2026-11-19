using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// The Settings window: a large tabbed panel that floats above the desk. Opens from the workspace
    /// panel, the wrist menu or the done tray (History). While open, the queue is held so nobody walks
    /// up mid-change.
    /// Tabs: General · Hands & Input · AI · Agent Bridge · Room · Sound · History · About.
    /// </summary>
    public class SettingsWindow : MonoBehaviour, IResizableWindow
    {
        public enum Tab { General, Hands, Comfort, AI, Bridge, Room, Sound, History, About }

        [SerializeField] private GameObject root;
        [SerializeField] private PassthroughToggle passthrough;
        [SerializeField] private Vector2 size = new Vector2(1100f, 680f);

        private readonly List<Action> refreshers = new List<Action>();
        private readonly Dictionary<Tab, GameObject> pages = new Dictionary<Tab, GameObject>();
        private readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
        private Tab current;
        private bool heldBySettings;
        private float resetArmedUntil, defaultsArmedUntil;
        private string aiStatus = "";
        private int historyIndex;
        private TextMeshProUGUI resetLabel, defaultsLabel, historyTitle, historyBody, bridgeStatus, aiMode, aiStatusText;
        private float nextLiveRefresh;

        public static SettingsWindow Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;

        // ── Resizing (corner handle on the window) ───────────────────────────
        public Vector2 Size => size;
        public Vector2 MinSize => new Vector2(860f, 520f);
        public Vector2 MaxSize => new Vector2(1600f, 1000f);
        public bool Live => false;          // a full rebuild: preview frame while dragging, reflow on release
        public void ResizeLive(Vector2 size) { }

        /// <summary>Rebuilds the whole layout for a new width / height (content reflows; long pages scroll).</summary>
        public void Resize(Vector2 newSize)
        {
            size = new Vector2(Mathf.Clamp(newSize.x, MinSize.x, MaxSize.x), Mathf.Clamp(newSize.y, MinSize.y, MaxSize.y));
            Tab keep = current;
            // Keep the Interaction SDK objects (ray / poke surfaces): deleting them made rays pass through Settings.
            foreach (Transform c in root.transform)
                if (!c.name.StartsWith("ISDK_") && c.GetComponentInChildren<Oculus.Interaction.PointableCanvas>(true) == null)
                    Destroy(c.gameObject);
            refreshers.Clear();
            pages.Clear();
            tabButtons.Clear();
            ((RectTransform)root.transform).sizeDelta = size;
            Build();
            Show(keep);
            var ch = GetComponent<WindowChrome>();
            if (ch != null) ch.LayoutForSize(size);
            var setting = AppSettings.Current.Window("settings");
            setting.width = size.x; setting.height = size.y;
            AppSettings.Save();
        }
        /// <summary>Raised when the window opens (the tutorial listens).</summary>
        public event Action Opened;

        private void Awake() => Instance = this;

        private void Start()
        {
            var saved = AppSettings.Current.Window("settings");
            if (saved.width > 0f && saved.height > 0f)
                size = new Vector2(Mathf.Clamp(saved.width, MinSize.x, MaxSize.x), Mathf.Clamp(saved.height, MinSize.y, MaxSize.y));
            ((RectTransform)root.transform).sizeDelta = size;
            Build();
            var chromeLayout = GetComponent<WindowChrome>();
            if (chromeLayout != null) chromeLayout.LayoutForSize(size);
            GameManager gm = GameManager.Instance;
            gm.AI.ConfigChanged += Refresh;
            if (gm.Bridge != null) gm.Bridge.StateChanged += Refresh;
            if (gm.Session != null) gm.Session.HistoryChanged += () => { if (current != Tab.History) historyIndex = 0; Refresh(); };
            AppSettings.Changed += Refresh;
            root.SetActive(false);
            var chrome = GetComponent<WindowChrome>();
            if (chrome != null) chrome.HideBar();
        }

        // ── Open / close ──────────────────────────────────────────────────────

        public void Toggle() { if (IsOpen) Close(); else Open(current); }
        public void OpenHistory() => Open(Tab.History);
        public void Open() => Open(current);

        public void Open(Tab tab)
        {
            GameManager gm = GameManager.Instance;
            if (!IsOpen && !gm.Queue.Hold) { gm.Queue.Hold = true; heldBySettings = true; }
            root.SetActive(true);
            var chrome = GetComponent<WindowChrome>();
            if (chrome != null) chrome.Show();
            Show(tab);
            Opened?.Invoke();
        }

        public void Close()
        {
            GameManager gm = GameManager.Instance;
            if (gm.Keyboard.IsOpenFor(this)) gm.Keyboard.Close();
            root.SetActive(false);
            var chrome = GetComponent<WindowChrome>();
            if (chrome != null) chrome.HideBar();
            if (heldBySettings) { gm.Queue.Hold = false; heldBySettings = false; }
        }

        private void Show(Tab tab)
        {
            current = tab;
            foreach (var kv in pages) kv.Value.SetActive(kv.Key == tab);
            Refresh();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Time.unscaledTime > nextLiveRefresh) { nextLiveRefresh = Time.unscaledTime + 1f; Refresh(); }
        }

        private void Refresh()
        {
            if (root == null || !root.activeSelf) return;
            foreach (var kv in tabButtons) UiKit.Tint(kv.Value, kv.Key == current ? UiKit.Raised : new Color(0, 0, 0, 0));
            foreach (var r in refreshers) r();
        }

        // ── Building ──────────────────────────────────────────────────────────

        private void Build()
        {
            Transform t = root.transform;
            var canvas = root.GetComponent<Canvas>();
            if (canvas != null) canvas.sortingOrder = 30;
            var bg = UiKit.Box(t, Vector2.zero, size, new Color(0.075f, 0.08f, 0.095f, 1f), 1.6f, "Background");
            bg.raycastTarget = true;   // blocks rays from passing through to what's behind

            // Sidebar.
            float sideW = 250f;
            UiKit.Box(t, new Vector2(-size.x * 0.5f + sideW * 0.5f, 0f), new Vector2(sideW, size.y), new Color(0.055f, 0.06f, 0.07f, 1f), 1.6f, "Sidebar");
            UiKit.Label(t, "Settings", 30f, UiKit.Text, new Vector2(-size.x * 0.5f + 28f, size.y * 0.5f - 26f), new Vector2(200f, 44f),
                TextAlignmentOptions.Left, true, false).rectTransform.pivot = new Vector2(0f, 0.5f);

            string[] names = { "General", "Hands & Input", "Comfort & View", "AI", "Agent Bridge", "Room", "Sound", "History", "About" };
            string[] icons = { "settings", "user", "Arrow", "chat", "command-center", "category-all", "volume-on", "recently-played", "more-horizontal" };
            for (int i = 0; i < names.Length; i++)
            {
                Tab tab = (Tab)i;
                var b = UiKit.Button(t, names[i], new Vector2(-size.x * 0.5f + sideW * 0.5f, size.y * 0.5f - 92f - i * 58f), new Vector2(sideW - 28f, 50f),
                    new Color(0, 0, 0, 0), () => Show(tab), out var label, 18f, false, icons[i]);
                label.alignment = TextAlignmentOptions.Left;
                tabButtons[tab] = b;
            }

            // Close.
            UiKit.Button(t, "Close", new Vector2(size.x * 0.5f - 80f, size.y * 0.5f - 34f), new Vector2(120f, 44f), UiKit.Control, Close, out _, 17f);

            // Content pages.
            var area = UiKit.Rect("Content", t, new Vector2(sideW * 0.5f, -20f), new Vector2(size.x - sideW - 60f, size.y - 90f));
            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                // Each page scrolls: pinch (or poke) and drag up / down.
                var viewport = UiKit.Rect(tab + "Page", area, Vector2.zero, area.sizeDelta);
                viewport.gameObject.AddComponent<RectMask2D>();
                var hit = viewport.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);    // catches drags between rows
                var content = UiKit.Rect("Content", viewport, Vector2.zero, Vector2.zero);
                content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = new Vector2(0f, area.sizeDelta.y);
                var scroll = viewport.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport;
                scroll.content = content;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 40f;
                scroll.inertia = true;
                scroll.decelerationRate = 0.12f;
                pages[tab] = viewport.gameObject;
            }
            BuildGeneral(Page(Tab.General));
            BuildHands(Page(Tab.Hands));
            BuildComfort(Page(Tab.Comfort));
            BuildAI(Page(Tab.AI));
            BuildBridge(Page(Tab.Bridge));
            BuildRoom(Page(Tab.Room));
            BuildSound(Page(Tab.Sound));
            BuildHistory(Page(Tab.History));
            BuildAbout(Page(Tab.About));
            Canvas.ForceUpdateCanvases();
            FitPages();
            Show(Tab.General);
        }

        private Transform Page(Tab tab) => pages[tab].GetComponent<ScrollRect>().content;

        /// <summary>Sizes each page's scroll content to what was built on it.</summary>
        private void FitPages()
        {
            foreach (var kv in pages)
            {
                var scroll = kv.Value.GetComponent<ScrollRect>();
                RectTransform content = scroll.content;
                float lowest = 0f;
                var corners = new Vector3[4];
                foreach (RectTransform child in content)
                {
                    child.GetWorldCorners(corners);
                    for (int i = 0; i < 4; i++) lowest = Mathf.Min(lowest, content.InverseTransformPoint(corners[i]).y);
                }
                float view = ((RectTransform)kv.Value.transform).rect.height;
                content.sizeDelta = new Vector2(0f, Mathf.Max(view, -lowest + 16f));
                content.anchoredPosition = Vector2.zero;
            }
        }

        private float W => size.x - 250f - 60f;

        // A row: title + description on the left, a control on the right. Returns the control's top-left position.
        private Vector2 Row(Transform page, ref float y, string title, string description, float height = 62f)
        {
            UiKit.Label(page, title, 20f, UiKit.Text, new Vector2(0f, -y), new Vector2(W * 0.55f, 28f), TextAlignmentOptions.TopLeft, true, true);
            if (!string.IsNullOrEmpty(description))
                UiKit.Label(page, description, 15f, UiKit.Muted, new Vector2(0f, -y - 28f), new Vector2(W * 0.55f, height - 30f), TextAlignmentOptions.TopLeft, false, true);
            var line = UiKit.Box(page, new Vector2(0f, -y - height + 2f), new Vector2(W, 1f), UiKit.Divider, 0f, "Divider", true);
            var pos = new Vector2(W * 0.58f, -y - 6f);
            y += height + 10f;
            return pos;
        }

        private void Heading(Transform page, ref float y, string text)
        {
            UiKit.Label(page, text.ToUpperInvariant(), 14f, UiKit.Accent, new Vector2(0f, -y), new Vector2(W, 22f), TextAlignmentOptions.TopLeft, true, true)
                .characterSpacing = 6f;
            y += 30f;
        }

        private float ControlW => W * 0.42f;

        private void BuildGeneral(Transform p)
        {
            GameManager gm = GameManager.Instance;
            float y = 0f;
            Heading(p, ref y, "Work");
            var c = Row(p, ref y, "Demo mode", "Simulated tasks keep the team busy. Great for pitches; turn off to give only your own work.");
            refreshers.Add(UiKit.Switch(p, c, () => gm.Dispatcher.AutoDispatch, v => { gm.Dispatcher.AutoDispatch = v; Refresh(); }));
            c = Row(p, ref y, "Auto-open keyboard for questions", "When an agent asks something, the desk keyboard opens straight away.");
            refreshers.Add(UiKit.Switch(p, c, () => AppSettings.Current.autoKeyboardForQuestions, v => { AppSettings.Current.autoKeyboardForQuestions = v; AppSettings.Save(); }));

            Heading(p, ref y, "Review flow");
            c = Row(p, ref y, "Agents in line", "How many wait in front of your desk. The rest wait at their desks until there's room.", 62f);
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "1", "2", "3", "4" },
                () => AppSettings.Current.maxInLine - 1, i => { AppSettings.Current.maxInLine = i + 1; AppSettings.Save(); }));
            c = Row(p, ref y, "Arrival pace", "Minimum time between two agents joining the line.", 62f);
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "Brisk", "Calm", "Relaxed" },
                () => (int)AppSettings.Current.arrivalPace, i => { AppSettings.Current.arrivalPace = (ArrivalPace)i; AppSettings.Save(); }));

            Heading(p, ref y, "Start over");
            c = Row(p, ref y, "Replay the tutorial", "A guided tour of the hands, the desk and the team (about 4 minutes).");
            UiKit.Button(p, "Start tutorial", c, new Vector2(ControlW, 44f), UiKit.Primary, () => { Close(); TutorialManager.Instance?.Begin(); }, out _, 16f, true);
            c = Row(p, ref y, "Reset session", "Clears the team, queued work and history. Your API key and preferences stay.");
            UiKit.Button(p, "Reset session", c, new Vector2(ControlW, 44f), UiKit.Danger, () =>
            {
                if (Time.unscaledTime < resetArmedUntil) { gm.Session.ResetSession(); return; }
                resetArmedUntil = Time.unscaledTime + 4f;
                Refresh();
            }, out resetLabel, 16f, true);
            c = Row(p, ref y, "Restore default settings", "Hands mode, normal gestures, Daylight Studio, default sound.");
            UiKit.Button(p, "Restore defaults", c, new Vector2(ControlW, 44f), UiKit.Control, () =>
            {
                if (Time.unscaledTime < defaultsArmedUntil) { AppSettings.ResetToDefaults(); defaultsArmedUntil = 0f; return; }
                defaultsArmedUntil = Time.unscaledTime + 4f;
                Refresh();
            }, out defaultsLabel, 16f, true);
            refreshers.Add(() =>
            {
                resetLabel.text = Time.unscaledTime < resetArmedUntil ? "Press again to reset" : "Reset session";
                defaultsLabel.text = Time.unscaledTime < defaultsArmedUntil ? "Press again" : "Restore defaults";
            });
        }

        private void BuildHands(Transform p)
        {
            GameManager gm = GameManager.Instance;
            float y = 0f;
            Heading(p, ref y, "Input");
            var c = Row(p, ref y, "Input mode", "Hands is how this workspace is meant to be used. Controller mode turns on controller rays and models.", 86f);
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "Hands", "Controllers" },
                () => (int)AppSettings.Current.inputMode, i => { AppSettings.Current.inputMode = (InputMode)i; AppSettings.Save(); }));
            c = Row(p, ref y, "Gesture shortcuts", "Thumbs up, thumbs down and palm out act on the request on your desk.");
            refreshers.Add(UiKit.Switch(p, c, () => AppSettings.Current.gestures, v => { AppSettings.Current.gestures = v; AppSettings.Save(); }));
            c = Row(p, ref y, "Only while facing the page", "Gestures count only when you look at the page on your desk, not the side monitors.", 56f);
            refreshers.Add(UiKit.Switch(p, c, () => AppSettings.Current.gesturesNeedFacing, v => { AppSettings.Current.gesturesNeedFacing = v; AppSettings.Save(); }));
            c = Row(p, ref y, "Palm out gesture", "Push your open palm at the page to reject or drop. Off: use the REJECT button.", 56f);
            refreshers.Add(UiKit.Switch(p, c, () => AppSettings.Current.palmGesture, v => { AppSettings.Current.palmGesture = v; AppSettings.Save(); }));
            c = Row(p, ref y, "Gesture hold", "How long to hold a gesture before it counts. Careful avoids accidents.");
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "Quick", "Normal", "Careful" },
                () => (int)AppSettings.Current.gestureTiming, i => { AppSettings.Current.gestureTiming = (GestureTiming)i; AppSettings.Save(); }));
            c = Row(p, ref y, "Wrist menu", "Which palm shows the menu. The other hand presses its buttons.");
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "Left palm", "Right palm" },
                () => AppSettings.Current.menuOnRightHand ? 1 : 0, i => { AppSettings.Current.menuOnRightHand = i == 1; AppSettings.Save(); }));
        }

        private void BuildComfort(Transform p)
        {
            GameManager gm = GameManager.Instance;
            float y = 0f;
            Heading(p, ref y, "Height & position");
            var c = Row(p, ref y, "Posture", "Seated brings the desk a little closer to your eyes.", 60f);
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "Standing", "Seated" },
                () => AppSettings.Current.seated ? 1 : 0, i => { AppSettings.Current.seated = i == 1; AppSettings.Save(); gm.Station.FitDeskToHead(); }));
            c = Row(p, ref y, "Desk height", "Fine-tune on top of the automatic fit.", 60f);
            float[] offsets = { -0.10f, -0.05f, 0f, 0.05f, 0.10f };
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "-10 cm", "-5", "Auto", "+5", "+10 cm" },
                () => System.Array.IndexOf(offsets, Mathf.Round(AppSettings.Current.deskHeightOffset * 20f) / 20f),
                i => { AppSettings.Current.deskHeightOffset = offsets[i]; AppSettings.Save(); }, 40f, 13f));
            c = Row(p, ref y, "Reposition", "Fit the desk to your height now, return to it, or re-centre your view.", 60f);
            float bw = (ControlW - 12f) / 3f;
            UiKit.Button(p, "Fit desk", c, new Vector2(bw, 42f), UiKit.Control, gm.Station.FitDeskToHead, out _, 14f, true);
            UiKit.Button(p, "Back to desk", c + new Vector2(bw + 6f, 0f), new Vector2(bw, 42f), UiKit.Control, gm.Station.ReturnToDesk, out _, 14f, true);
            UiKit.Button(p, "Recenter", c + new Vector2(2f * (bw + 6f), 0f), new Vector2(bw, 42f), UiKit.Control, gm.Station.Recenter, out _, 14f, true);
            c = Row(p, ref y, "Eye height", "Auto-correct fixes the view if tracking drops you to the floor (after sleep). Reset snaps to a normal height.", 60f);
            refreshers.Add(UiKit.Switch(p, c, () => AppSettings.Current.autoHeight, v => { AppSettings.Current.autoHeight = v; AppSettings.Save(); }));
            UiKit.Button(p, "Reset height", c + new Vector2(110f, 0f), new Vector2(ControlW - 110f, 40f), UiKit.Control, () => gm.Station.CorrectHeight(true), out _, 14f, true);
            c = Row(p, ref y, "Standing view in the room", "Off the platform, lift your view to standing eye height so the team is to scale, even if you sit.", 60f);
            refreshers.Add(UiKit.Switch(p, c, () => AppSettings.Current.standingViewWhenRoaming, v => { AppSettings.Current.standingViewWhenRoaming = v; AppSettings.Save(); }));

            Heading(p, ref y, "View & movement");
            c = Row(p, ref y, "Panel size", "Size of the desk monitors and windows.", 52f);
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "Compact", "Standard", "Large" },
                () => (int)AppSettings.Current.uiSize, i => { AppSettings.Current.uiSize = (UiSize)i; AppSettings.Save(); }));
            c = Row(p, ref y, "Hand teleport", "L-shape + pinch to jump around the office. Off keeps you at the desk.", 52f);
            refreshers.Add(UiKit.Switch(p, c, () => AppSettings.Current.teleport, v => { AppSettings.Current.teleport = v; AppSettings.Save(); }));
            c = Row(p, ref y, "Agent walking speed", "How fast agents walk to you and back.", 52f);
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "Relaxed", "Normal", "Brisk" },
                () => (int)AppSettings.Current.walkSpeed, i => { AppSettings.Current.walkSpeed = (WalkSpeed)i; AppSettings.Save(); }));
            c = Row(p, ref y, "Team board on the wall", "The big status board behind the team.", 52f);
            refreshers.Add(UiKit.Switch(p, c, () => AppSettings.Current.showBoard, v => { AppSettings.Current.showBoard = v; AppSettings.Save(); }));
            c = Row(p, ref y, "Window layout", "Put every window back on the desk at standard size.", 52f);
            UiKit.Button(p, "Reset windows", c, new Vector2(ControlW, 42f), UiKit.Control, () =>
            {
                foreach (var w in FindObjectsByType<WindowChrome>(FindObjectsInactive.Include)) w.ResetLayout();
            }, out _, 15f, true);
        }

        private void BuildAI(Transform p)
        {
            GameManager gm = GameManager.Instance;
            float y = 0f;
            Heading(p, ref y, "Team brain");
            aiMode = UiKit.Label(p, "", 18f, UiKit.Text, new Vector2(0f, -y), new Vector2(W, 28f), TextAlignmentOptions.TopLeft, false, true);
            y += 32f;
            aiStatusText = UiKit.Label(p, "", 14f, UiKit.Muted, new Vector2(0f, -y), new Vector2(W, 40f), TextAlignmentOptions.TopLeft, false, true);
            y += 46f;
            string[] ids = { AIServiceManager.Simulation, "anthropic", "openai", "gemini", "custom" };
            var c = Row(p, ref y, "Default brain", "Demo mode = Simulation (no keys, no internet). Pick a provider once its key is set; each agent can override it on its card.", 70f);
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "Demo", "Claude", "OpenAI", "Gemini", "Custom" },
                () => System.Array.IndexOf(ids, gm.AI.DefaultBackend),
                i =>
                {
                    if (i > 0 && !gm.AI.Usable(ids[i])) { aiStatus = $"<color=#FFB347>Set up {AIServiceManager.DisplayName(ids[i])} below first.</color>"; Refresh(); return; }
                    gm.AI.SetDefaultBackend(ids[i]);
                    aiStatus = i == 0 ? "Demo mode: simulated agents." : $"Team brain: {AIServiceManager.DisplayName(ids[i])}.";
                }, 40f, 13f));

            Heading(p, ref y, "Providers");
            foreach (string id in AIServiceManager.Providers)
            {
                string pid = id;
                bool custom = pid == "custom";
                string desc = custom ? "Any OpenAI-compatible server: Groq, OpenRouter, Together, Ollama, LM Studio (URL like https://api.groq.com/openai/v1)."
                                     : pid == "anthropic" ? "Anthropic Messages API." : pid == "openai" ? "OpenAI Chat Completions." : "Google AI Studio key.";
                var row = Row(p, ref y, AIServiceManager.DisplayName(pid), desc, 72f);
                var state = UiKit.Label(p, "", 13f, UiKit.Muted, new Vector2(0f, -(y - 30f)), new Vector2(W * 0.55f, 20f), TextAlignmentOptions.TopLeft, false, true);
                int n = custom ? 4 : 3;
                float bw = (ControlW - (n - 1) * 6f) / n;
                int k = 0;
                Vector2 Next() => row + new Vector2((k++) * (bw + 6f), 0f);
                if (custom)
                    UiKit.Button(p, "URL", Next(), new Vector2(bw, 40f), UiKit.Control, () =>
                        gm.Keyboard.Open("Base URL, e.g. https://api.groq.com/openai/v1 or http://192.168.1.5:11434/v1", this, true,
                            v => gm.AI.SetBackend(pid, baseUrl: v)), out _, 14f, true);
                UiKit.Button(p, "Key", Next(), new Vector2(bw, 40f), UiKit.Primary, () =>
                    gm.Keyboard.Open($"{AIServiceManager.DisplayName(pid)} API key{(custom ? " (empty if none)" : "")}", this, custom,
                        v => { gm.AI.SetBackend(pid, apiKey: v); TestProvider(pid); }, secretText: true), out _, 14f, true);
                UiKit.Button(p, "Model", Next(), new Vector2(bw, 40f), UiKit.Control, () =>
                    gm.Keyboard.Open($"{AIServiceManager.DisplayName(pid)} model (empty = {AIServiceManager.DefaultModelFor(pid)})", this, true,
                        v => gm.AI.SetBackend(pid, model: v)), out _, 14f, true);
                var test = UiKit.Button(p, "Test", Next(), new Vector2(bw, 40f), UiKit.Control, () => TestProvider(pid), out _, 14f, true);
                refreshers.Add(() =>
                {
                    var b = gm.AI.Get(pid);
                    bool ok = gm.AI.Usable(pid);
                    state.text = ok ? $"<color=#4FD1C5>Ready</color> · {b.model}{(custom ? " · " + b.baseUrl : "")}"
                                    : b != null && !string.IsNullOrEmpty(b.apiKey) ? "Key set, needs a model / URL" : "Not set up";
                    test.interactable = ok;
                });
            }
            var c2 = Row(p, ref y, "Simulated work speed", "How long demo agents take per task.", 56f);
            refreshers.Add(UiKit.Segmented(p, c2, ControlW, new[] { "Fast", "Normal", "Realistic" },
                () => (int)AppSettings.Current.workSpeed, i => { AppSettings.Current.workSpeed = (WorkSpeed)i; AppSettings.Save(); }));
            UiKit.Label(p, "Keys are stored only on this headset (never in the app build). Claude can also be pushed from Unity: Tools > AI Manager > Quest API Key.",
                13f, UiKit.Muted, new Vector2(0f, -y), new Vector2(W, 40f), TextAlignmentOptions.TopLeft, false, true);
            refreshers.Add(() =>
            {
                aiMode.text = gm.AI.IsLive
                    ? $"<color=#4FD1C5><b>LIVE</b></color>  {AIServiceManager.DisplayName(gm.AI.DefaultBackend)} · {gm.AI.Model}"
                    : "<color=#FFB347><b>DEMO</b></color>  simulated agents: no keys or internet needed";
                aiStatusText.text = aiStatus;
            });
        }

        private void TestProvider(string id)
        {
            aiStatus = $"Testing {AIServiceManager.DisplayName(id)}...";
            Refresh();
            GameManager.Instance.AI.TestConnection(id, (ok, msg) =>
            {
                aiStatus = ok ? $"<color=#4FD1C5>{msg}</color>" : $"<color=#F07272>{msg}</color>";
                Refresh();
            });
        }

        private void Test() => TestProvider(GameManager.Instance.AI.DefaultBackend == AIServiceManager.Simulation ? "anthropic" : GameManager.Instance.AI.DefaultBackend);

        private void BuildBridge(Transform p)
        {
            GameManager gm = GameManager.Instance;
            float y = 0f;
            Heading(p, ref y, "Real agents on your PC");
            bridgeStatus = UiKit.Label(p, "", 18f, UiKit.Text, new Vector2(0f, -y), new Vector2(W, 64f), TextAlignmentOptions.TopLeft, false, true);
            y += 72f;
            var c = Row(p, ref y, "Find my PC", "Open the AI Manager Bridge on your PC (same Wi-Fi). When your PC shows up here, tap it and type the 6-digit code from its window.", 92f);
            float bw = (ControlW - 12f) / 3f;
            var pcButtons = new Button[2];
            var pcLabels = new TextMeshProUGUI[2];
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                pcButtons[i] = UiKit.Button(p, "", c + new Vector2(i * (bw + 6f), 0f), new Vector2(bw, 48f), UiKit.Primary, () =>
                {
                    var list = gm.Bridge != null ? gm.Bridge.Found : null;
                    if (list == null || index >= list.Count) return;
                    var pc = list[index];
                    gm.Keyboard.Open($"Pairing code shown on {pc.name} (6 digits)", this, false, code => gm.Bridge.Pair(pc, code));
                }, out pcLabels[i], 14f, true);
            }
            UiKit.Button(p, "Search again", c + new Vector2(2f * (bw + 6f), 0f), new Vector2(bw, 48f), UiKit.Control, () => { if (gm.Bridge != null) gm.Bridge.Rescan(); }, out _, 14f, true);

            c = Row(p, ref y, "Advanced", "Manual address and token, or forget this PC. Empty address = USB (adb reverse).", 70f);
            UiKit.Button(p, "Address", c, new Vector2(bw, 44f), UiKit.Control, () =>
                gm.Keyboard.Open("Bridge address, e.g. 192.168.1.20:8787 (empty = USB)", this, true, url => gm.AI.SetBridge(url)), out _, 15f, true);
            UiKit.Button(p, "Token", c + new Vector2(bw + 6f, 0f), new Vector2(bw, 44f), UiKit.Control, () =>
                gm.Keyboard.Open("Bridge token (only if you copied it from ~/.aim/token)", this, true,
                    tok => gm.AI.SetBridge(gm.AI.BridgeUrl, tok.Trim()), secretText: true), out _, 15f, true);
            UiKit.Button(p, "Forget PC", c + new Vector2(2f * (bw + 6f), 0f), new Vector2(bw, 44f), UiKit.Control, () => { if (gm.Bridge != null) gm.Bridge.Unpair(); }, out _, 15f, true);

            UiKit.Label(p,
                "<b>Setup, one time</b>\n" +
                "1  On your PC open <color=#4FD1C5>AI Manager Bridge</color> (Bridge/AIManagerBridge.exe, or python Bridge/aim_bridge.py).\n" +
                "2  Tap your PC above and type the code from its window. No IP address, no cable.\n" +
                "3  Click <color=#4FD1C5>Connect Claude Code</color> in that window. Every Claude Code session then appears as an agent, and its permission prompts walk up to your desk.\n\n" +
                "Live agents get a teal halo and a LIVE tag. Your decision goes straight back to them.",
                15f, UiKit.Muted, new Vector2(0f, -y), new Vector2(W, 220f), TextAlignmentOptions.TopLeft, false, true);
            refreshers.Add(() =>
            {
                var b = gm.Bridge;
                if (b == null) { bridgeStatus.text = "Bridge client missing."; return; }
                if (b.Connected)
                    bridgeStatus.text = $"<color=#4FD1C5><b>CONNECTED</b></color>  {b.LinkedCount} live agent(s)\n<color=#9AA3B2>{b.Url}</color>";
                else if (b.Found.Count > 0 && !b.Paired)
                    bridgeStatus.text = $"<color=#4FD1C5><b>PC FOUND</b></color>  tap it below and type the code\n<color=#9AA3B2>{b.PairMessage}</color>";
                else if (b.Paired)
                    bridgeStatus.text = $"<color=#FFB347><b>PAIRED</b></color>  looking for your PC...\n<color=#9AA3B2>{b.LastError}</color>";
                else
                    bridgeStatus.text = $"<color=#9AA3B2><b>LOOKING FOR YOUR PC</b></color>  same Wi-Fi, bridge open\n<color=#9AA3B2>{b.PairMessage}</color>";
                for (int i = 0; i < 2; i++)
                {
                    bool has = i < b.Found.Count;
                    pcButtons[i].gameObject.SetActive(has);
                    if (has) pcLabels[i].text = (b.Paired ? "Re-pair " : "Pair ") + TaskLibrary.Trim(b.Found[i].name, 12);
                }
            });
        }

        private void BuildRoom(Transform p)
        {
            float y = 0f;
            Heading(p, ref y, "Environment");
            var c = Row(p, ref y, "Room theme", "Lighting and materials of the office.", 120f);
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "Daylight", "Golden Hour" },
                () => AppSettings.Current.theme == RoomTheme.DaylightStudio ? 0 : AppSettings.Current.theme == RoomTheme.GoldenHour ? 1 : -1,
                i => { AppSettings.Current.theme = i == 0 ? RoomTheme.DaylightStudio : RoomTheme.GoldenHour; AppSettings.Save(); }));
            refreshers.Add(UiKit.Segmented(p, c + new Vector2(0f, -50f), ControlW, new[] { "Night Loft", "Nordic Calm" },
                () => AppSettings.Current.theme == RoomTheme.NightLoft ? 0 : AppSettings.Current.theme == RoomTheme.NordicCalm ? 1 : -1,
                i => { AppSettings.Current.theme = i == 0 ? RoomTheme.NightLoft : RoomTheme.NordicCalm; AppSettings.Save(); }));
            c = Row(p, ref y, "Mixed reality", "See your real room through passthrough. The office shell hides; desk, agents and team stay.");
            refreshers.Add(UiKit.Switch(p, c, () => passthrough != null && passthrough.IsMixedReality, v => { if (passthrough != null) passthrough.Set(v); }));
        }

        private void BuildSound(Transform p)
        {
            float y = 0f;
            Heading(p, ref y, "Audio");
            var c = Row(p, ref y, "Volume", "Desk sounds and agent chimes.");
            float[] levels = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            refreshers.Add(UiKit.Segmented(p, c, ControlW, new[] { "Off", "25", "50", "75", "100" },
                () => Mathf.RoundToInt(AppSettings.Current.volume * 4f),
                i => { AppSettings.Current.volume = levels[i]; AppSettings.Save(); GameManager.Instance.Queue.Desk.PlayNotify(); }));
            c = Row(p, ref y, "Button clicks", "A soft tick when you press a panel button.");
            refreshers.Add(UiKit.Switch(p, c, () => AppSettings.Current.uiClicks, v => { AppSettings.Current.uiClicks = v; AppSettings.Save(); }));
            c = Row(p, ref y, "Agent chimes", "A spatial chime from an agent's desk when it needs you, so you can turn towards it.");
            refreshers.Add(UiKit.Switch(p, c, () => AppSettings.Current.chimes, v => { AppSettings.Current.chimes = v; AppSettings.Save(); }));
        }

        private void BuildHistory(Transform p)
        {
            historyTitle = UiKit.Label(p, "", 20f, UiKit.Text, new Vector2(0f, 0f), new Vector2(W, 30f), TextAlignmentOptions.TopLeft, true, true);
            var card = UiKit.Box(p, new Vector2(0f, -40f), new Vector2(W, 420f), UiKit.Raised, 2.4f, "Card", true);
            historyBody = UiKit.Label(card.transform, "", 17f, UiKit.Text, new Vector2(20f, -16f), new Vector2(W - 40f, 390f), TextAlignmentOptions.TopLeft, false, true);
            historyBody.enableAutoSizing = true; historyBody.fontSizeMin = 12f; historyBody.fontSizeMax = 17f;
            var older = UiKit.Button(p, "Older", new Vector2(0f, -476f), new Vector2(W * 0.5f - 6f, 48f), UiKit.Control, () => { historyIndex++; Refresh(); }, out _, 16f, true);
            var newer = UiKit.Button(p, "Newer", new Vector2(W * 0.5f + 6f, -476f), new Vector2(W * 0.5f - 6f, 48f), UiKit.Control, () => { historyIndex--; Refresh(); }, out _, 16f, true);
            refreshers.Add(() =>
            {
                var history = GameManager.Instance.Session != null ? GameManager.Instance.Session.History : null;
                int count = history?.Count ?? 0;
                historyIndex = Mathf.Clamp(historyIndex, 0, Mathf.Max(0, count - 1));
                historyTitle.text = count == 0 ? "Work history" : $"Work history   <color=#9AA3B2>{historyIndex + 1} of {count}, newest first</color>";
                if (count == 0)
                    historyBody.text = "<color=#9AA3B2>Nothing finished yet. Everything you approve, answer or drop is kept here, across sessions.</color>";
                else
                {
                    HistoryEntry h = history[count - 1 - historyIndex];
                    string colour = h.outcome == "Dropped" ? "#F07272" : "#4FD1C5";
                    historyBody.text = $"<size=125%><b>{h.title}</b></size>\n<color=#9AA3B2>{h.agent} · {h.role} · {h.time}</color>   " +
                                       $"<color={colour}><b>{h.outcome.ToUpperInvariant()}</b></color>\n\n{h.text}";
                }
                older.interactable = historyIndex < count - 1;
                newer.interactable = historyIndex > 0;
            });
        }

        private void BuildAbout(Transform p)
        {
            UiKit.Label(p,
                "<size=130%><b>AI Manager Workspace</b></size>\n" +
                "<color=#9AA3B2>A mixed-reality command desk for people who run many AI agents at once.</color>\n\n" +
                "Agents work in parallel at their desks. When one needs you (a plan to approve, a question, a choice, a blocker, " +
                "a finished result) it walks the request to your desk as a page. Decide with a glance and a gesture, and the team keeps moving.\n\n" +
                "<b>Gestures</b> (hold briefly; the ring at your hand fills)\n" +
                "<color=#4FD1C5>Thumbs up</color>  approve · let the agent decide · retry · got it · send what you typed\n" +
                "<color=#FFB347>Thumbs down</color>  request changes\n" +
                "<color=#F07272>Palm out</color>  reject or drop · close the keyboard\n\n" +
                "<b>Hands</b>  point + pinch for far things · poke panels within reach · left palm for the wrist menu · L-shape + pinch to teleport\n\n" +
                $"<color=#9AA3B2>Version {Application.version} · Unity {Application.unityVersion} · Meta XR SDK · Claude by Anthropic</color>",
                16f, UiKit.Text, new Vector2(0f, 0f), new Vector2(W, 560f), TextAlignmentOptions.TopLeft, false, true);
        }
    }
}
