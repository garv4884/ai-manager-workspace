using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Recognises the three review gestures on either hand using XR Hands joint data.
    /// A gesture must be held for a moment before it fires, so ordinary hand poses
    /// (an open hand while looking around, a loose fist) do not trigger decisions.
    ///
    ///   Thumbs up   -> approve
    ///   Thumbs down -> request changes
    ///   Palm push   -> dismiss (open hand, palm facing away from you, held)
    ///
    /// Editor shortcuts: F1 approve, F2 changes, F3 dismiss.
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

        // Tunables in metres.
        private const float CurledMax = 0.075f;
        private const float ExtendedMin = 0.095f;

        private XRHandSubsystem hands;
        private Gesture holding = Gesture.None;
        private float holdTime;
        private float cooldown;
        private bool needsRelease;
        private Transform head;

        /// <summary>The gesture currently being held (for UI feedback).</summary>
        public Gesture Pending => holding;
        /// <summary>0..1 progress towards firing <see cref="Pending"/>.</summary>
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

            if (hands == null || !hands.running)
            {
                var list = new List<XRHandSubsystem>();
                SubsystemManager.GetSubsystems(list);
                hands = list.Count > 0 ? list[0] : null;
                if (hands == null) { holding = Gesture.None; return; }
            }
            if (head == null && Camera.main != null) head = Camera.main.transform;

            Gesture now = Gesture.None;
            if (hands.rightHand.isTracked) now = Detect(hands.rightHand, true);
            if (now == Gesture.None && hands.leftHand.isTracked) now = Detect(hands.leftHand, false);

            if (now == Gesture.None) { needsRelease = false; holding = Gesture.None; holdTime = 0f; return; }
            if (needsRelease || cooldown > 0f) return;

            if (now != holding) { holding = now; holdTime = 0f; }
            holdTime += Time.deltaTime;
            if (holdTime >= Required(now)) Fire(now);
        }

        private float Required(Gesture g) => g == Gesture.PalmPush ? palmHoldSeconds : thumbHoldSeconds;

        private void Fire(Gesture g)
        {
            holding = Gesture.None;
            holdTime = 0f;
            cooldown = cooldownSeconds;
            needsRelease = true;
            switch (g)
            {
                case Gesture.ThumbsUp: OnThumbsUp.Invoke(); break;
                case Gesture.ThumbsDown: OnThumbsDown.Invoke(); break;
                case Gesture.PalmPush: OnPalmPush.Invoke(); break;
            }
        }

        private Gesture Detect(XRHand hand, bool isRight)
        {
            if (!Pos(hand, XRHandJointID.Palm, out Vector3 palm) ||
                !Pos(hand, XRHandJointID.Wrist, out Vector3 wrist) ||
                !Pos(hand, XRHandJointID.ThumbTip, out Vector3 thumbTip) ||
                !Pos(hand, XRHandJointID.ThumbProximal, out Vector3 thumbProx) ||
                !Pos(hand, XRHandJointID.IndexTip, out Vector3 indexTip) ||
                !Pos(hand, XRHandJointID.MiddleTip, out Vector3 middleTip) ||
                !Pos(hand, XRHandJointID.RingTip, out Vector3 ringTip) ||
                !Pos(hand, XRHandJointID.LittleTip, out Vector3 littleTip) ||
                !Pos(hand, XRHandJointID.IndexProximal, out Vector3 indexProx) ||
                !Pos(hand, XRHandJointID.LittleProximal, out Vector3 littleProx))
                return Gesture.None;

            float dIndex = Vector3.Distance(indexTip, palm);
            float dMiddle = Vector3.Distance(middleTip, palm);
            float dRing = Vector3.Distance(ringTip, palm);
            float dLittle = Vector3.Distance(littleTip, palm);

            bool fist = dIndex < CurledMax && dMiddle < CurledMax && dRing < CurledMax && dLittle < CurledMax;
            if (fist && Vector3.Distance(thumbTip, palm) > CurledMax)
            {
                float up = (thumbTip - thumbProx).normalized.y;
                if (up > 0.75f) return Gesture.ThumbsUp;
                if (up < -0.75f) return Gesture.ThumbsDown;
            }

            bool open = dIndex > ExtendedMin && dMiddle > ExtendedMin && dRing > ExtendedMin && dLittle > ExtendedMin * 0.85f;
            if (open && head != null)
            {
                Vector3 normal = Vector3.Cross(indexProx - wrist, littleProx - wrist).normalized;
                if (isRight) normal = -normal;           // now points out of the palm
                Vector3 toHand = (palm - head.position).normalized;
                bool inFront = Vector3.Dot(toHand, head.forward) > 0.5f;
                bool facingAway = Vector3.Dot(normal, head.forward) > 0.6f;
                bool fingersUp = (indexTip - wrist).normalized.y > 0.3f;
                if (inFront && facingAway && fingersUp) return Gesture.PalmPush;
            }
            return Gesture.None;
        }

        private static bool Pos(XRHand hand, XRHandJointID id, out Vector3 p)
        {
            if (hand.GetJoint(id).TryGetPose(out Pose pose))
            {
                // Joint poses are in XR-origin space; convert to world if an origin exists.
                p = OriginToWorld(pose.position);
                return true;
            }
            p = default;
            return false;
        }

        private static Transform origin;
        private static Vector3 OriginToWorld(Vector3 local)
        {
            if (origin == null)
            {
                var xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (xrOrigin != null) origin = xrOrigin.CameraFloorOffsetObject != null ? xrOrigin.CameraFloorOffsetObject.transform : xrOrigin.transform;
            }
            return origin != null ? origin.TransformPoint(local) : local;
        }
    }
}
