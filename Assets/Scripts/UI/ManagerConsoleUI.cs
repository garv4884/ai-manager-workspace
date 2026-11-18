using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Review console on the manager's desk. Its buttons are rebuilt for every request, so a
    /// question shows ANSWER / YOU DECIDE, a choice shows one button per option, a plan shows
    /// APPROVE / CHANGES / REJECT, and so on. Every gesture has a button twin here.
    /// </summary>
    public class ManagerConsoleUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI headline;
        [SerializeField] private TextMeshProUGUI subline;
        [SerializeField] private Button[] actionButtons;
        [SerializeField] private TextMeshProUGUI[] actionLabels;
        [SerializeField] private Image gestureFill;
        [SerializeField] private TextMeshProUGUI gestureLabel;
        [SerializeField] private HandGestureRecognizer gestures;
        [SerializeField] private Button scrollUp;
        [SerializeField] private Button scrollDown;

        private readonly List<ManagerAction> current = new List<ManagerAction>();

        private void Start()
        {
            for (int i = 0; i < actionButtons.Length; i++)
            {
                int index = i;
                actionButtons[i].onClick.AddListener(() =>
                {
                    if (index < current.Count) GameManager.Instance.Perform(current[index]);
                });
            }
            GameManager gm = GameManager.Instance;
            if (scrollUp != null) scrollUp.onClick.AddListener(() => { if (gm.Presenter != null) gm.Presenter.Page.Scroll(-140f); });
            if (scrollDown != null) scrollDown.onClick.AddListener(() => { if (gm.Presenter != null) gm.Presenter.Page.Scroll(140f); });
            gm.Queue.Changed += Refresh;
            gm.Agents.AnyAgentChanged += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            GameManager gm = GameManager.Instance;
            QueueManager q = gm.Queue;
            AgentController p = gm.Presenter;

            if (p == null)
            {
                headline.text = q.Presenter != null ? $"{q.Presenter.Data.agentName} is on the way up..."
                    : q.Hold ? "<color=#FFB347>Queue on hold</color>" : "All clear";
                subline.text = q.Hold
                    ? (q.WaitingTotal > 0 ? $"{q.WaitingTotal} waiting" : "Nobody comes up until you resume")
                    : q.WaitingTotal > 0 ? $"{q.WaitingTotal} waiting for you" : "No one is waiting for you";
            }
            else
            {
                AgentData d = p.Data;
                headline.text = $"<color=#{ColorUtility.ToHtmlStringRGB(d.StatusColor)}>{d.StatusLabel}</color>  {d.agentName}";
                subline.text = q.WaitingTotal > 0 ? $"{q.WaitingTotal} more waiting" : "Last one";
            }

            bool scrollable = p != null && p.Page.CanScroll;
            if (scrollUp != null) scrollUp.gameObject.SetActive(scrollable);
            if (scrollDown != null) scrollDown.gameObject.SetActive(scrollable);

            current.Clear();
            current.AddRange(gm.CurrentActions());
            for (int i = 0; i < actionButtons.Length; i++)
            {
                bool on = i < current.Count;
                actionButtons[i].gameObject.SetActive(on);
                if (!on) continue;
                actionLabels[i].text = current[i].label;
                var colors = actionButtons[i].colors;
                Color c = current[i].color;
                colors.normalColor = c;
                colors.selectedColor = c;
                colors.highlightedColor = Color.Lerp(c, Color.white, 0.25f);
                colors.pressedColor = Color.Lerp(c, Color.black, 0.25f);
                actionButtons[i].colors = colors;
            }
            LayoutButtons(current.Count);
        }

        /// <summary>Spreads the visible buttons evenly across the console.</summary>
        private void LayoutButtons(int count)
        {
            if (count == 0) return;
            const float total = 600f, gap = 10f;
            float w = (total - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                var rt = (RectTransform)actionButtons[i].transform;
                rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
                rt.anchoredPosition = new Vector2(-total / 2f + w / 2f + i * (w + gap), rt.anchoredPosition.y);
            }
        }

        private void Update()
        {
            if (gestures == null || gestureFill == null) return;
            gestureFill.fillAmount = gestures.HoldProgress;
            string verb = null;
            foreach (var a in current)
            {
                if (gestures.Pending == HandGestureRecognizer.Gesture.ThumbsUp && a.isPrimary) verb = a.label;
                if (gestures.Pending == HandGestureRecognizer.Gesture.ThumbsDown && a.isRework) verb = a.label;
                if (gestures.Pending == HandGestureRecognizer.Gesture.PalmPush && a.isDismiss) verb = a.label;
            }
            gestureLabel.text = verb != null ? $"Hold to {verb.ToLowerInvariant()}" : "";
        }
    }
}
