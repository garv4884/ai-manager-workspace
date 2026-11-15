using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Routes gestures to whichever action fits the current request: thumbs up never "approves"
    /// a question, it lets the agent decide; thumbs down only works where changes can be asked for.
    /// </summary>
    public class GestureInteractionManager : MonoBehaviour
    {
        [SerializeField] private HandGestureRecognizer recognizer;

        public HandGestureRecognizer Recognizer => recognizer;

        private void OnEnable()
        {
            if (recognizer == null) recognizer = GetComponent<HandGestureRecognizer>();
            recognizer.OnThumbsUp.AddListener(Primary);
            recognizer.OnThumbsDown.AddListener(Rework);
            recognizer.OnPalmPush.AddListener(Dismiss);
        }

        private void OnDisable()
        {
            recognizer.OnThumbsUp.RemoveListener(Primary);
            recognizer.OnThumbsDown.RemoveListener(Rework);
            recognizer.OnPalmPush.RemoveListener(Dismiss);
        }

        private static bool Typing => GameManager.Instance.Keyboard.IsOpen;

        private void Primary() { if (!Typing) GameManager.Instance.PerformPrimary(); }
        private void Rework() { if (!Typing) GameManager.Instance.PerformRework(); }
        private void Dismiss() { if (!Typing) GameManager.Instance.PerformDismiss(); }
    }
}
