using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Routes gestures to whichever action fits the current request: thumbs up never "approves"
    /// a question, it lets the agent decide; thumbs down only works where changes can be asked for.
    ///
    /// Gestures keep working while the desk keyboard is open:
    ///   thumbs up   -> send what you typed (or, with nothing typed, the presenter's primary action)
    ///   thumbs down -> ask the presenter for changes
    ///   palm push   -> close the keyboard (never drops a task while you are typing)
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

        private static GameManager GM => GameManager.Instance;

        private static void Primary()
        {
            TextInputService kb = GM.Keyboard;
            if (kb.IsOpen)
            {
                if (kb.SubmitCurrent()) return;
                // Nothing typed: only act if the keyboard belongs to the presenter (e.g. "you decide").
                if (GM.Presenter != null && kb.IsOpenFor(GM.Presenter)) GM.PerformPrimary();
                return;
            }
            GM.PerformPrimary();
        }

        private static void Rework()
        {
            TextInputService kb = GM.Keyboard;
            if (kb.IsOpen && !(GM.Presenter != null && kb.IsOpenFor(GM.Presenter))) return;
            GM.PerformRework();
        }

        private static void Dismiss()
        {
            TextInputService kb = GM.Keyboard;
            if (kb.IsOpen) { kb.Close(); return; }
            GM.PerformDismiss();
        }
    }
}
