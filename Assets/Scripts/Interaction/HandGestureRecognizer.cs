using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Recognises the review gestures on either hand from Meta Interaction SDK hand data.
    /// A gesture must be held briefly before it fires, so ordinary poses (an open hand while
    /// looking around, a loose fist) never trigger a decision.
    ///
    ///   Thumbs up   -> the request's primary action (approve / let agent decide / retry / got it)
    ///   Thumbs down -> request changes (only where that applies)
    ///   Palm push   -> reject / drop task
    ///
    /// Editor shortcuts: F1 thumbs up, F2 thumbs down, F3 palm push.
    /// </summary>
    public class HandGestureRecognizer : MonoBehaviour
    {
        public enum Gesture { None, ThumbsUp, ThumbsDown, PalmPush }

        [SerializeField] private float thumbHoldSeconds = 0.45f;
        [SerializeField] private float palmHoldSeconds = 0.7f;
        [SerializeField] private float cooldownSeconds = 1.0f;

        public UnityEvent OnThumbsUp = new UnityEvent();
        public UnityEvent OnThumbsDown = new UnityEvent();
        public UnityEvent OnPalmPush = new UnityEvent();

        private const float CurledMax = 0.075f;
        private const float ExtendedMin = 0.095f;

        private readonly List<Hand> hands = new List<Hand>();
        private Gesture holding = Gesture.None;
        private float holdTime;
        private float cooldown;
        private bool needsRelease;
        private float nextSearch;

        public Gesture Pending => holding;
        /// <summary>The hand currently holding a gesture (null when none), for the ring at the hand.</summary>
        public Hand ActiveHand { get; private set; }
        /// <summary>Raised whenever a gesture fires (the tutorial listens).</summary>
        public event System.Action<Gesture> Fired;
        public float HoldProgress => holding == Gesture.None ? 0f : Mathf.Clamp01(holdTime / Required(holding));

        private void Update()
        {
            if (cooldown > 0f) cooldown -= Time.deltaTime;

            var kb = Keyboard.current;
            if (kb != null && cooldown <= 0f)
            {
                if (kb.f1Key.wasPressedThisFrame) { Fire(Gesture.ThumbsUp); return; }
                if (kb.f2Key.wasPressedThisFrame) { Fire(Gesture.ThumbsDown); return; }
                if (kb.f3Key.wasPressedThisFrame) { Fire(Gesture.PalmPush); return; }
            }

            if (hands.Count < 2 && Time.unscaledTime > nextSearch)
            {
                nextSearch = Time.unscaledTime + 2f;
                foreach (var found in FindObjectsByType<Hand>(FindObjectsInactive.Exclude))
                    if (!hands.Contains(found)) hands.Add(found);
            }

            Transform head = Camera.main != null ? Camera.main.transform : null;
            Gesture now = Gesture.None;
            ActiveHand = null;
            Vector3? page = PageTarget();
            bool facing = head != null && (!AppSettings.Current.gesturesNeedFacing || page == null || LookingAt(head, page.Value));
            FacingPage = facing;
            if (head != null && AppSettings.Current.gestures && facing)
                foreach (Hand hand in hands)
                {
                    if (hand == null || !hand.isActiveAndEnabled || !hand.IsTrackedDataValid) continue;
                    now = Detect(hand, head, page);
                    if (now == Gesture.PalmPush && !AppSettings.Current.palmGesture) now = Gesture.None;
                    if (now != Gesture.None) { ActiveHand = hand; break; }
                }

            if (now == Gesture.None) { needsRelease = false; holding = Gesture.None; holdTime = 0f; return; }
            if (needsRelease || cooldown > 0f) return;

            if (now != holding) { holding = now; holdTime = 0f; }
            holdTime += Time.deltaTime;
            if (holdTime >= Required(now)) Fire(now);
        }

        private float Required(Gesture g)
        {
            float hold = AppSettings.Current.GestureHoldSeconds;
            return g == Gesture.PalmPush ? hold + (palmHoldSeconds - thumbHoldSeconds) + 0.25f : hold;   // palm: deliberately slower
        }

        private void Fire(Gesture g)
        {
            holding = Gesture.None;
            holdTime = 0f;
            cooldown = cooldownSeconds;
            needsRelease = true;
            Fired?.Invoke(g);
            switch (g)
            {
                case Gesture.ThumbsUp: OnThumbsUp.Invoke(); break;
                case Gesture.ThumbsDown: OnThumbsDown.Invoke(); break;
                case Gesture.PalmPush: OnPalmPush.Invoke(); break;
            }
        }

        /// <summary>True while the user looks at the page on the desk (gestures only count then).</summary>
        public bool FacingPage { get; private set; }

        private static Vector3? PageTarget()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Queue == null || gm.Queue.Desk == null || gm.Queue.Desk.ReviewAnchor == null) return null;
            return gm.Queue.Desk.ReviewAnchor.position;
        }

        /// <summary>Within ~35 degrees of the head's forward direction, and not too far away (you are at the desk).</summary>
        private static bool LookingAt(Transform head, Vector3 target)
        {
            Vector3 to = target - head.position;
            if (to.magnitude > 3.5f) return false;
            return Vector3.Angle(head.forward, to) < 35f;
        }

        private static Gesture Detect(IHand hand, Transform head, Vector3? page)
        {
            if (!P(hand, HandJointId.HandPalm, out Vector3 palm) ||
                !P(hand, HandJointId.HandWristRoot, out Vector3 wrist) ||
                !P(hand, HandJointId.HandThumbTip, out Vector3 thumbTip) ||
                !P(hand, HandJointId.HandThumb2, out Vector3 thumbBase) ||
                !P(hand, HandJointId.HandIndexTip, out Vector3 indexTip) ||
                !P(hand, HandJointId.HandMiddleTip, out Vector3 middleTip) ||
                !P(hand, HandJointId.HandRingTip, out Vector3 ringTip) ||
                !P(hand, HandJointId.HandPinkyTip, out Vector3 pinkyTip) ||
                !P(hand, HandJointId.HandIndex1, out Vector3 indexBase) ||
                !P(hand, HandJointId.HandPinky1, out Vector3 pinkyBase))
                return Gesture.None;

            float dIndex = Vector3.Distance(indexTip, palm);
            float dMiddle = Vector3.Distance(middleTip, palm);
            float dRing = Vector3.Distance(ringTip, palm);
            float dPinky = Vector3.Distance(pinkyTip, palm);

            bool fist = dIndex < CurledMax && dMiddle < CurledMax && dRing < CurledMax && dPinky < CurledMax;
            if (fist && Vector3.Distance(thumbTip, palm) > CurledMax)
            {
                float up = (thumbTip - thumbBase).normalized.y;
                if (up > 0.75f) return Gesture.ThumbsUp;
                if (up < -0.75f) return Gesture.ThumbsDown;
            }

            bool open = dIndex > ExtendedMin && dMiddle > ExtendedMin && dRing > ExtendedMin && dPinky > ExtendedMin * 0.85f;
            if (open)
            {
                Vector3 normal = Vector3.Cross(indexBase - wrist, pinkyBase - wrist).normalized;
                if (hand.Handedness == Handedness.Right) normal = -normal;   // now points out of the palm
                bool inFront = Vector3.Dot((palm - head.position).normalized, head.forward) > 0.6f;
                Vector3 aim = page.HasValue ? (page.Value - palm).normalized : head.forward;
                bool facingAway = Vector3.Dot(normal, head.forward) > 0.7f && Vector3.Dot(normal, aim) > 0.65f;   // palm pushed at the page
                bool fingersUp = (indexTip - wrist).normalized.y > 0.3f;
                if (inFront && facingAway && fingersUp) return Gesture.PalmPush;
            }
            return Gesture.None;
        }

        private static bool P(IHand hand, HandJointId id, out Vector3 p)
        {
            if (hand.GetJointPose(id, out Pose pose)) { p = pose.position; return true; }
            p = default;
            return false;
        }
    }
}
