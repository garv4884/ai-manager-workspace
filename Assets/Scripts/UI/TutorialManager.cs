using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// First-run guided tour (replay from Settings > General; skip at any step). Five chapters:
    /// Welcome · Your hands · Your team · Your tools · Make it yours. Hands-on steps detect what you do
    /// (a pinch, the wrist menu opening, a gesture, a decision) and tick themselves off; Next is always
    /// available, so nobody gets stuck. Demo mode is paused during the tour and agents only come up
    /// when a step asks for it.
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Transform panel;
        [SerializeField] private HandMenu handMenu;
        [SerializeField] private HandGestureRecognizer gestures;
        [SerializeField] private Vector3 centerPose = new Vector3(0f, 0.52f, 1.24f);   // ~75 cm in front, eye-level-ish, nearly upright
        [SerializeField] private Vector3 sidePose = new Vector3(-0.52f, 0.68f, 1.22f);
        [SerializeField] private float startDelay = 2.5f;

        private class Step
        {
            public string chapter, title, body, task, icon, practice;
            public bool side;                 // move aside so the page in the middle stays visible
            public Func<bool> done;
            public Action enter;
        }

        private readonly List<Step> steps = new List<Step>();
        private int index = -1;
        private bool completed, practiced, wristSeen, gestureSeen, selected, shaped, settingsSeen, pinchSeen;
        private bool prevDemo;
        private float skipArmedUntil;
        private TaskData guidedApproval, guidedQuestion;
        private readonly List<Hand> hands = new List<Hand>();

        // UI
        private TextMeshProUGUI chapterText, counterText, titleText, bodyText, taskText, nextLabel, skipLabel;
        private Image progressFill, taskBox;
        private RawImage iconImage;
        private Button backButton, nextButton, practiceButton, altButton;
        private TextMeshProUGUI practiceLabel, altLabel;

        public static TutorialManager Instance { get; private set; }
        public bool Running => index >= 0;

        private void Awake() => Instance = this;

        private IEnumerator Start()
        {
            BuildSteps();
            BuildUI();
            root.SetActive(false);
            if (gestures != null) gestures.Fired += _ => gestureSeen = true;
            GameManager gm = GameManager.Instance;
            gm.Agents.SelectionChanged += a => { if (a != null) selected = true; };
            gm.Layout.Changed += () => shaped = true;
            gm.Agents.RosterChanged += () => shaped = true;
            bool firstRun = !AppSettings.Current.tutorialDone;
            if (firstRun) { prevDemo = gm.Dispatcher.AutoDispatch; gm.Dispatcher.AutoDispatch = false; pendingFirstRun = true; }
            yield return new WaitForSeconds(startDelay);
            if (SettingsWindow.Instance != null) SettingsWindow.Instance.Opened += () => settingsSeen = true;
            if (!AppSettings.Current.tutorialDone) Begin();
        }

        // ── Flow ──────────────────────────────────────────────────────────────

        private bool pendingFirstRun;

        public void Begin()
        {
            GameManager gm = GameManager.Instance;
            if (!pendingFirstRun && index < 0) prevDemo = gm.Dispatcher.AutoDispatch;
            pendingFirstRun = false;
            gm.Dispatcher.AutoDispatch = false;
            gm.Queue.Hold = true;    // nobody walks up during the tour unless a step asks for it
            gm.Station.ReturnToDesk();
            practiced = wristSeen = gestureSeen = selected = shaped = settingsSeen = pinchSeen = completed = false;
            guidedApproval = guidedQuestion = null;
            root.SetActive(true);
            Go(0);
        }

        private void Go(int i)
        {
            index = Mathf.Clamp(i, 0, steps.Count - 1);
            completed = false;
            skipArmedUntil = 0f;
            Step s = steps[index];
            GameManager.Instance.Queue.Hold = !s.side;   // only the guided review steps let an agent come up
            s.enter?.Invoke();
            ResetFlags(s);
            Refresh(true);
            GameManager.Instance.Queue.Desk.Play(ReviewDesk.Sfx.Click);
        }

        private void ResetFlags(Step s)
        {
            // Each hands-on step starts fresh, so a pinch from the previous step doesn't count.
            pinchSeen = practiced = gestureSeen = wristSeen = false;
            if (s.title.StartsWith("Select")) selected = false;
            if (s.title.StartsWith("Shape")) shaped = false;
        }

        private void Next()
        {
            if (index < 0) return;
            if (index >= steps.Count - 1) { Finish(true); return; }
            Go(index + 1);
        }

        private void Back() { if (index > 0) Go(index - 1); }   // index < 0: hidden, nothing to do

        private void Skip()
        {
            if (Time.unscaledTime > skipArmedUntil) { skipArmedUntil = Time.unscaledTime + 3f; Refresh(false); return; }
            Finish(prevDemo);
        }

        private void Finish(bool demo)
        {
            GameManager gm = GameManager.Instance;
            index = -1;
            root.SetActive(false);
            gm.Queue.Hold = false;
            gm.Dispatcher.AutoDispatch = demo;
            AppSettings.Current.tutorialDone = true;
            AppSettings.Save();
        }

        private void Update()
        {
            if (index < 0) return;
            Step s = steps[index];

            if (!pinchSeen)
            {
                if (hands.Count == 0 && Time.frameCount % 60 == 0) hands.AddRange(FindObjectsByType<Hand>(FindObjectsInactive.Exclude));
                foreach (var h in hands)
                    if (h != null && h.IsTrackedDataValid && h.GetIndexFingerIsPinching()) { pinchSeen = true; break; }
            }
            if (handMenu != null && handMenu.IsVisible) wristSeen = true;

            bool nowDone = s.done != null && s.done();
            if (nowDone && !completed)
            {
                completed = true;
                GameManager.Instance.Queue.Desk.PlayNotify();
                Refresh(false);
            }
            if (skipArmedUntil > 0f && Time.unscaledTime > skipArmedUntil) { skipArmedUntil = 0f; Refresh(false); }

            // Ease the panel to where this step wants it.
            bool aside = s.side || GameManager.Instance.Keyboard.IsOpen;
            Vector3 target = aside ? sidePose : centerPose;
            Quaternion rot = Quaternion.Euler(aside ? 4f : 5f, aside ? -26f : 0f, 0f);
            panel.localScale = Vector3.Lerp(panel.localScale, Vector3.one * (aside ? 0.78f : 1f), 1f - Mathf.Exp(-Time.deltaTime * 5f));
            panel.localPosition = Vector3.Lerp(panel.localPosition, target, 1f - Mathf.Exp(-Time.deltaTime * 5f));
            panel.localRotation = Quaternion.Slerp(panel.localRotation, rot, 1f - Mathf.Exp(-Time.deltaTime * 5f));
        }

        // ── Steps ─────────────────────────────────────────────────────────────

        private void BuildSteps()
        {
            steps.Add(new Step
            {
                chapter = "WELCOME", icon = "command-center",
                title = "Welcome to AI Manager Workspace",
                body = "Your command desk for a team of AI agents.\n\nAgents work in parallel at their own desks. Whenever one needs you " +
                       "(a plan to approve, a question, a choice, a blocker or a finished result) it walks the request to your desk as a page. " +
                       "You decide with a glance and a gesture, and the whole team keeps moving.\n\nThis tour takes about 4 minutes. Skip it any time."
            });
            steps.Add(new Step
            {
                chapter = "WELCOME", icon = "category-all",
                title = "Why a desk in VR?",
                body = "With many agents running, <b>you</b> become the bottleneck: approvals, questions and results pile up in different windows " +
                       "and work stalls without anyone noticing.\n\nHere every request joins <b>one line</b>, you see at a glance who is working, " +
                       "waiting or blocked, and most decisions take a single gesture. Simulated agents work out of the box; add a Claude key or " +
                       "connect real agents from your PC later."
            });
            steps.Add(new Step
            {
                chapter = "YOUR HANDS", icon = "Arrow",
                title = "Point and pinch",
                body = "This app is built for your hands: no controllers needed.\n\nHold a hand out: a soft ray comes from it. Aim at something far " +
                       "away and <b>pinch</b> (touch thumb and index) to select it. Release to let go.",
                task = "Pinch your thumb and index finger together", done = () => pinchSeen
            });
            steps.Add(new Step
            {
                chapter = "YOUR HANDS", icon = "check-alt", practice = "Tap me",
                title = "Poke like a touchscreen",
                body = "Panels within reach are pressed directly: reach out and tap a button with your index finger. The desk panels, " +
                       "the keyboard and this card all work this way.",
                task = "Tap the button on the right with your finger", done = () => practiced
            });
            steps.Add(new Step
            {
                chapter = "YOUR HANDS", icon = "more-horizontal",
                title = "Your wrist menu",
                body = "Turn your <b>left palm</b> towards your face: your wrist menu appears above the hand. Press it with your right index finger. " +
                       "It works anywhere in the room: adjust your height (+, - or auto), hold the queue, open Settings, switch to mixed reality, or jump back to your desk.\n\n" +
                       "Left-handed? Settings > Hands & Input moves it to your right palm.",
                task = "Open the wrist menu by looking at your palm", done = () => wristSeen
            });
            steps.Add(new Step
            {
                chapter = "YOUR HANDS", icon = "ThumbsUp",
                title = "Decide with a gesture",
                body = "Gestures act on the request on your desk. Hold one for half a second; a ring at your hand fills up.\n\n" +
                       "<color=#4FD1C5><b>Thumbs up</b></color>   approve, or the main action\n" +
                       "<color=#FFB347><b>Thumbs down</b></color>   request changes\n" +
                       "<color=#F07272><b>Palm out</b></color>   reject or drop (closes the keyboard while typing)",
                task = "Look at the middle of your desk and try any gesture", done = () => gestureSeen
            });
            steps.Add(new Step
            {
                chapter = "YOUR TEAM", icon = "UserIcon",
                title = "Meet your agents",
                body = "Each robot is an agent with a role. The tag above its head shows what it needs from you:\n" +
                       "<color=#4DD173>FREE</color>  <color=#4D9EFF>WORKING</color>  <color=#FF9E26>NEEDS APPROVAL</color>  " +
                       "<color=#FFDB33>NEEDS INPUT</color>  <color=#F24747>BLOCKED</color>  <color=#4FD1C5>HAS UPDATE</color>\n\n" +
                       "Their eyes and antenna light show it too, and the board on the far wall lists the whole team. When an agent needs you, " +
                       "it chimes from its desk and walks to the line in front of you."
            });
            steps.Add(new Step
            {
                chapter = "YOUR TEAM", icon = "ThumbsUp", side = true,
                title = "Your first review",
                body = "An agent is bringing you a short plan. It queues in front of the desk, rides the lift, and the page lands on your desk.\n\n" +
                       "Read it, then <b>thumbs up</b> or press <b>APPROVE</b> on the console. (Thumbs down asks for changes, palm out rejects.)",
                task = "Approve the plan when it arrives",
                enter = () => guidedApproval ??= Guide("Plan the onboarding email", "Write a short plan for a welcome email to new users.", TaskType.NeedsApproval),
                done = () => guidedApproval != null && guidedApproval.status == TaskExecutionStatus.Completed
            });
            steps.Add(new Step
            {
                chapter = "YOUR TEAM", icon = "chat", side = true,
                title = "Answer a question",
                body = "Agents ask when something is unclear. You only get the actions that fit: an open question has <b>ANSWER</b> " +
                       "(the desk keyboard opens), <b>YOU DECIDE</b> and <b>DROP</b>; never Approve.\n\n" +
                       "Type on the keyboard lying on your desk, then <b>thumbs up</b> or SEND. Prefer voice? Press META KEYBOARD and use dictation.",
                task = "Answer the agent's question",
                enter = () => guidedQuestion ??= Guide("Pick the launch audience", "Ask the manager who the launch post is for.", TaskType.NeedsTextInput),
                done = () => guidedQuestion != null && guidedQuestion.askedOnce && GameManager.Instance.Queue.Presenter?.CurrentTask != guidedQuestion
                             && !string.IsNullOrEmpty(guidedQuestion.answer)
            });
            steps.Add(new Step
            {
                chapter = "YOUR TOOLS", icon = "Arrow",
                title = "Select an agent",
                body = "Point at any agent or desk and pinch. Its card opens on the right side of your desk: give it a task (ASSIGN TASK), " +
                       "change its role, bring it to the front of the line, or remove it.\n\nPinch and sweep along the floor to slide a desk somewhere else.",
                task = "Pinch an agent or a desk", done = () => selected
            });
            steps.Add(new Step
            {
                chapter = "YOUR TOOLS", icon = "category-all",
                title = "Shape your workspace",
                body = "The panel on your left runs the room:\n<b>BRIEF THE TEAM</b>  one goal; a lead splits it into roles and tasks\n" +
                       "<b>Layouts</b>  classroom, circle, U-shape, pods, coworking, war room\n<b>ADD AGENT</b>  up to 10   " +
                       "<b>HOLD QUEUE</b>  pause arrivals while you think\n<b>DISCUSSION</b>  free agents gather in the glass room",
                task = "Switch to another layout (or add an agent)", done = () => shaped
            });
            steps.Add(new Step
            {
                chapter = "YOUR TOOLS", icon = "Teleport-Hands",
                title = "Move around",
                body = "Walk around your play space, or teleport with your hand: make an <b>L-shape</b> (thumb up, index pointing) " +
                       "towards the floor, aim the arc, and <b>pinch</b> to jump.\n\nYour wrist menu's <b>BACK TO DESK</b> always brings you home, " +
                       "and <b>FIT DESK</b> matches the desk to your height."
            });
            steps.Add(new Step
            {
                chapter = "MAKE IT YOURS", icon = "settings",
                title = "Settings and real agents",
                body = "<b>SETTINGS</b> (left panel or wrist menu) has everything: hands or controllers, gesture timing, room themes, " +
                       "sound, your Claude API key, history, and the <b>Agent Bridge</b>, which brings real agents from your PC " +
                       "(Claude Code, scripts, MCP tools) to desks here.",
                task = "Open Settings once (optional)", done = () => settingsSeen
            });
            steps.Add(new Step
            {
                chapter = "READY", icon = "check-alt",
                title = "You're ready",
                body = "That's the whole loop: agents work, bring you what needs a human, you decide, they continue.\n\n" +
                       "Start with the <b>demo team</b> (simulated work keeps everyone busy), or a <b>quiet team</b> and brief them yourself.\n\n" +
                       "Replay this tour any time: Settings > General."
            });
        }

        /// <summary>Gives a free agent a quick, guided task for the tour.</summary>
        private static TaskData Guide(string title, string description, TaskType type)
        {
            GameManager gm = GameManager.Instance;
            AgentController pick = null;
            foreach (var a in gm.Agents.Agents) if (a.CanTakeTask && !a.Data.IsLinked) { pick = a; break; }
            if (pick == null) foreach (var a in gm.Agents.Agents) if (!a.Data.IsLinked) { pick = a; break; }
            if (pick == null) return null;
            var task = new TaskData(title, description, type) { quick = true };
            pick.AssignTask(task);
            return task;
        }

        // ── UI ────────────────────────────────────────────────────────────────

        private const float Wd = 780f, Ht = 470f;

        private void BuildUI()
        {
            Transform t = root.transform;
            var canvas = root.GetComponent<Canvas>();
            if (canvas != null) canvas.sortingOrder = 20;   // over the desk panels it floats in front of
            var bg = UiKit.Box(t, Vector2.zero, new Vector2(Wd, Ht), new Color(0.075f, 0.08f, 0.095f, 1f), 1.6f, "Background");
            bg.raycastTarget = true;

            chapterText = UiKit.Label(t, "", 14f, UiKit.Accent, new Vector2(-Wd * 0.5f + 36f, Ht * 0.5f - 34f), new Vector2(400f, 24f), TextAlignmentOptions.Left, true);
            chapterText.rectTransform.pivot = new Vector2(0f, 0.5f);
            chapterText.characterSpacing = 6f;
            counterText = UiKit.Label(t, "", 14f, UiKit.Muted, new Vector2(Wd * 0.5f - 36f, Ht * 0.5f - 34f), new Vector2(200f, 24f), TextAlignmentOptions.Right);
            counterText.rectTransform.pivot = new Vector2(1f, 0.5f);
            UiKit.Box(t, new Vector2(0f, Ht * 0.5f - 54f), new Vector2(Wd - 72f, 4f), UiKit.Divider, 0f, "Track");
            progressFill = UiKit.Box(t, new Vector2(0f, Ht * 0.5f - 54f), new Vector2(Wd - 72f, 4f), UiKit.Accent, 0f, "Progress");
            progressFill.sprite = UiKit.Rounded;
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;

            iconImage = UiKit.Icon(t, "", new Vector2(-Wd * 0.5f + 76f, Ht * 0.5f - 118f), 64f, UiKit.Accent);
            titleText = UiKit.Label(t, "", 30f, UiKit.Text, new Vector2(-Wd * 0.5f + 132f, Ht * 0.5f - 118f), new Vector2(Wd - 170f, 48f), TextAlignmentOptions.Left, true);
            titleText.rectTransform.pivot = new Vector2(0f, 0.5f);
            bodyText = UiKit.Label(t, "", 18f, new Color(0.86f, 0.88f, 0.91f), new Vector2(0f, -14f), new Vector2(Wd - 72f, 182f), TextAlignmentOptions.TopLeft);
            bodyText.enableAutoSizing = true; bodyText.fontSizeMin = 13f; bodyText.fontSizeMax = 18f;

            taskBox = UiKit.Box(t, new Vector2(-70f, -142f), new Vector2(Wd - 212f, 52f), UiKit.Raised, 2.4f, "Task");
            taskText = UiKit.Label(taskBox.transform, "", 16f, UiKit.Text, Vector2.zero, new Vector2(Wd - 250f, 48f), TextAlignmentOptions.Left);
            practiceButton = UiKit.Button(t, "Tap me", new Vector2(Wd * 0.5f - 104f, -142f), new Vector2(140f, 52f), UiKit.Primary,
                () => practiced = true, out practiceLabel, 17f);

            backButton = UiKit.Button(t, "Back", new Vector2(-Wd * 0.5f + 96f, -Ht * 0.5f + 44f), new Vector2(120f, 48f), UiKit.Control, Back, out _, 16f);
            UiKit.Button(t, "Skip tutorial", new Vector2(-Wd * 0.5f + 270f, -Ht * 0.5f + 44f), new Vector2(190f, 48f), new Color(0, 0, 0, 0), Skip, out skipLabel, 15f);
            skipLabel.color = UiKit.Muted;
            altButton = UiKit.Button(t, "Quiet team", new Vector2(Wd * 0.5f - 310f, -Ht * 0.5f + 44f), new Vector2(190f, 48f), UiKit.Control,
                () => Finish(false), out altLabel, 16f);
            nextButton = UiKit.Button(t, "Next", new Vector2(Wd * 0.5f - 116f, -Ht * 0.5f + 44f), new Vector2(180f, 48f), UiKit.Primary, Next, out nextLabel, 17f);
        }

        private void Refresh(bool newStep)
        {
            if (index < 0) return;
            Step s = steps[index];
            chapterText.text = s.chapter;
            counterText.text = $"{index + 1} / {steps.Count}";
            progressFill.fillAmount = (index + 1f) / steps.Count;
            titleText.text = s.title;
            bodyText.text = s.body;

            bool hasTask = !string.IsNullOrEmpty(s.task);
            taskBox.gameObject.SetActive(hasTask);
            taskText.text = !hasTask ? "" : completed
                ? $"<color=#4FD1C5><b>DONE</b></color>   {s.task}"
                : $"<color=#9AA3B2><b>TRY IT</b></color>   {s.task}";
            taskBox.color = completed ? new Color(0.1f, 0.26f, 0.24f) : UiKit.Raised;
            practiceButton.gameObject.SetActive(!string.IsNullOrEmpty(s.practice));
            if (!string.IsNullOrEmpty(s.practice)) practiceLabel.text = practiced ? "Nice!" : s.practice;

            if (UiKit.Icons.TryGetValue(s.icon ?? "", out var tex)) { iconImage.texture = tex; iconImage.enabled = true; }
            else iconImage.enabled = false;

            bool last = index == steps.Count - 1;
            backButton.interactable = index > 0;
            altButton.gameObject.SetActive(last);
            nextLabel.text = index == 0 ? "Start tour" : last ? "Demo team" : hasTask && !completed ? "Skip step" : "Next";
            UiKit.Tint(nextButton, hasTask && !completed ? UiKit.Control : UiKit.Primary);
            skipLabel.text = Time.unscaledTime < skipArmedUntil ? "Press again to skip" : "Skip tutorial";
            if (last) skipLabel.transform.parent.gameObject.SetActive(false);
            else skipLabel.transform.parent.gameObject.SetActive(true);
        }
    }
}
