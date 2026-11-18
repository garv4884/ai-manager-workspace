using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// A small ring that appears just above the hand holding a review gesture and fills while you hold it,
    /// with the gesture's icon in the middle. It fades in and out, so ordinary hand poses never feel noisy.
    /// </summary>
    public class GestureRing : MonoBehaviour
    {
        [SerializeField] private HandGestureRecognizer recognizer;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image fill;
        [SerializeField] private RawImage icon;
        [SerializeField] private Texture2D thumbsUp;
        [SerializeField] private Texture2D thumbsDown;
        [SerializeField] private Texture2D palm;

        private float alpha;
        private Transform head;

        private void Awake()
        {
            var c = GetComponentInChildren<Canvas>(true);
            if (c != null) c.sortingOrder = 50;
        }

        private void LateUpdate()
        {
            var gesture = recognizer.Pending;
            var hand = recognizer.ActiveHand;
            bool show = gesture != HandGestureRecognizer.Gesture.None && hand != null &&
                        hand.GetRootPose(out Pose wrist);
            alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, Time.deltaTime * 8f);
            group.alpha = alpha;
            if (!show) return;

            if (head == null && Camera.main != null) head = Camera.main.transform;
            hand.GetRootPose(out Pose root);
            Vector3 pos = root.position + Vector3.up * 0.13f;
            transform.position = Vector3.Lerp(transform.position, pos, alpha < 0.2f ? 1f : 1f - Mathf.Exp(-Time.deltaTime * 20f));
            if (head != null) transform.rotation = Quaternion.LookRotation(transform.position - head.position);

            fill.fillAmount = recognizer.HoldProgress;
            Color c = gesture == HandGestureRecognizer.Gesture.ThumbsUp ? new Color(0.31f, 0.82f, 0.77f)
                    : gesture == HandGestureRecognizer.Gesture.ThumbsDown ? new Color(1f, 0.7f, 0.28f)
                    : new Color(0.94f, 0.45f, 0.45f);
            fill.color = c;
            icon.texture = gesture == HandGestureRecognizer.Gesture.ThumbsUp ? thumbsUp
                         : gesture == HandGestureRecognizer.Gesture.ThumbsDown ? thumbsDown : palm;
        }
    }
}
