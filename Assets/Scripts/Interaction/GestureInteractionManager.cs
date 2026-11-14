using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>Routes recognised hand gestures to the manager's review decisions.</summary>
    public class GestureInteractionManager : MonoBehaviour
    {
        [SerializeField] private HandGestureRecognizer recognizer;

        public HandGestureRecognizer Recognizer => recognizer;

        private void OnEnable()
        {
            if (recognizer == null) recognizer = GetComponent<HandGestureRecognizer>();
            recognizer.OnThumbsUp.AddListener(Approve);
            recognizer.OnThumbsDown.AddListener(Changes);
            recognizer.OnPalmPush.AddListener(Dismiss);
        }

        private void OnDisable()
        {
            recognizer.OnThumbsUp.RemoveListener(Approve);
            recognizer.OnThumbsDown.RemoveListener(Changes);
            recognizer.OnPalmPush.RemoveListener(Dismiss);
        }

        private static bool Typing => GameManager.Instance.Keyboard.IsOpen;

        private void Approve() { if (!Typing) GameManager.Instance.Approve(); }
        private void Changes() { if (!Typing) GameManager.Instance.RequestChanges(); }
        private void Dismiss() { GameManager.Instance.Dismiss(); }
    }
}
