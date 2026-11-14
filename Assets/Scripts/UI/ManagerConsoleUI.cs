using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Review buttons on the manager's desk. Every gesture has a button twin here, so the
    /// loop also works with controllers, poke, or when hand tracking is lost.
    /// </summary>
    public class ManagerConsoleUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI headline;
        [SerializeField] private TextMeshProUGUI subline;
        [SerializeField] private Button approveButton;
        [SerializeField] private TextMeshProUGUI approveLabel;
        [SerializeField] private Button changesButton;
        [SerializeField] private Button answerButton;
        [SerializeField] private Button dismissButton;
        [SerializeField] private Image gestureFill;
        [SerializeField] private TextMeshProUGUI gestureLabel;
        [SerializeField] private HandGestureRecognizer gestures;

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            approveButton.onClick.AddListener(gm.Approve);
            changesButton.onClick.AddListener(gm.RequestChangesWithFeedback);
            answerButton.onClick.AddListener(gm.OpenAnswerKeyboard);
            dismissButton.onClick.AddListener(gm.Dismiss);
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
                headline.text = q.Presenter != null ? $"{q.Presenter.Data.agentName} is on the way up..." : "All clear";
                subline.text = q.Waiting.Count > 0 ? $"{q.Waiting.Count} waiting in line" : "No one is waiting for you";
            }
            else
            {
                AgentData d = p.Data;
                headline.text = $"<color=#{ColorUtility.ToHtmlStringRGB(d.StatusColor)}>{d.StatusLabel}</color>  {d.agentName}";
                subline.text = (q.Waiting.Count > 0 ? $"{q.Waiting.Count} more in line" : "Last in line");
            }

            bool approval = p != null && p.Data.status == AgentStatus.WaitingForApproval;
            bool input = p != null && p.Data.status == AgentStatus.WaitingForInput;
            bool error = p != null && p.Data.status == AgentStatus.Error;

            approveButton.interactable = approval || error;
            approveLabel.text = error ? "RETRY" : "APPROVE";
            changesButton.interactable = approval;
            answerButton.interactable = input;
            dismissButton.interactable = p != null;
        }

        private void Update()
        {
            if (gestures == null || gestureFill == null) return;
            float k = gestures.HoldProgress;
            gestureFill.fillAmount = k;
            gestureLabel.text = gestures.Pending switch
            {
                HandGestureRecognizer.Gesture.ThumbsUp => "Hold thumbs up to approve",
                HandGestureRecognizer.Gesture.ThumbsDown => "Hold thumbs down for changes",
                HandGestureRecognizer.Gesture.PalmPush => "Hold palm out to dismiss",
                _ => ""
            };
        }
    }
}
