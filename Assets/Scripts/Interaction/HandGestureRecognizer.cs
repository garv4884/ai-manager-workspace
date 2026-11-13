using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Detects hand gestures via UnityEngine.XR.Hands and raises UnityEvents.
    /// Requires XR Hands package (com.unity.xr.hands 1.7.3+).
    /// Fallback: in Editor without a headset, use keyboard keys U/D/P/I/O to simulate gestures.
    /// </summary>
    public class HandGestureRecognizer : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        public enum HandChoice { Right, Left }
        [SerializeField] private float gestureCooldown = 0.8f;
        [SerializeField] private HandChoice dominantHand = HandChoice.Right;

        // ── Events ────────────────────────────────────────────────────────────
        [Header("Gesture Events")]
        public UnityEvent OnThumbsUp    = new UnityEvent();
        public UnityEvent OnThumbsDown  = new UnityEvent();
        public UnityEvent OnPalmOpen    = new UnityEvent();
        public UnityEvent<Vector3> OnPinch = new UnityEvent<Vector3>();
        public UnityEvent OnPointGesture = new UnityEvent();

        // ── Private ───────────────────────────────────────────────────────────
        private XRHandSubsystem _handSubsystem;
        private float _cooldownTimer;

        // Thresholds (metres)
        private const float CurlThreshold      = 0.05f;
        private const float ExtendThreshold    = 0.08f;
        private const float ThumbVerticalDelta = 0.06f;
        private const float PinchDistance      = 0.025f;

        private enum ActiveGesture { None, ThumbsUp, ThumbsDown, PalmOpen, Pinch, Point }
        private ActiveGesture _activeGesture = ActiveGesture.None;

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        private void OnEnable()  => AcquireSubsystem();
        private void OnDisable() => _handSubsystem = null;

        private void AcquireSubsystem()
        {
            var list = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(list);
            _handSubsystem = list.Count > 0 ? list[0] : null;
        }

        private void Update()
        {
            // Editor keyboard simulation (U=ThumbUp, D=ThumbDown, P=PalmOpen, I=Pinch, O=Point)
            if (Application.isEditor && Application.isPlaying)
            {
                if (_cooldownTimer <= 0f)
                {
                    var kb = UnityEngine.InputSystem.Keyboard.current;
                    if (kb != null)
                    {
                        if (kb.uKey.wasPressedThisFrame) { Fire(ActiveGesture.ThumbsUp);   OnThumbsUp?.Invoke();                _cooldownTimer = gestureCooldown; return; }
                        if (kb.dKey.wasPressedThisFrame) { Fire(ActiveGesture.ThumbsDown); OnThumbsDown?.Invoke();              _cooldownTimer = gestureCooldown; return; }
                        if (kb.pKey.wasPressedThisFrame) { Fire(ActiveGesture.PalmOpen);   OnPalmOpen?.Invoke();                _cooldownTimer = gestureCooldown; return; }
                        if (kb.iKey.wasPressedThisFrame) { Fire(ActiveGesture.Pinch);      OnPinch?.Invoke(transform.position); _cooldownTimer = gestureCooldown; return; }
                        if (kb.oKey.wasPressedThisFrame) { Fire(ActiveGesture.Point);      OnPointGesture?.Invoke();            _cooldownTimer = gestureCooldown; return; }
                    }
                }
            }

            // Re-acquire hand subsystem if lost
            if (_handSubsystem == null || !_handSubsystem.running)
            {
                AcquireSubsystem();
                if (_handSubsystem == null) { _cooldownTimer -= Time.deltaTime; return; }
            }

            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                return;
            }

            XRHand hand = dominantHand == HandChoice.Right
                ? _handSubsystem.rightHand
                : _handSubsystem.leftHand;

            if (!hand.isTracked) return;

            EvaluateGestures(hand);
        }

        // ── Gesture Evaluation ────────────────────────────────────────────────

        private void EvaluateGestures(XRHand hand)
        {
            if (!GetJointPos(hand, XRHandJointID.Wrist,          out Vector3 wrist))       return;
            if (!GetJointPos(hand, XRHandJointID.ThumbTip,        out Vector3 thumbTip))    return;
            if (!GetJointPos(hand, XRHandJointID.IndexTip,        out Vector3 indexTip))    return;
            if (!GetJointPos(hand, XRHandJointID.MiddleTip,       out Vector3 middleTip))   return;
            if (!GetJointPos(hand, XRHandJointID.RingTip,         out Vector3 ringTip))     return;
            if (!GetJointPos(hand, XRHandJointID.LittleTip,       out Vector3 littleTip))   return;
            if (!GetJointPos(hand, XRHandJointID.ThumbProximal,   out Vector3 thumbProx))   return;
            if (!GetJointPos(hand, XRHandJointID.IndexProximal,   out Vector3 indexProx))   return;
            if (!GetJointPos(hand, XRHandJointID.MiddleProximal,  out Vector3 middleProx))  return;
            if (!GetJointPos(hand, XRHandJointID.RingProximal,    out Vector3 ringProx))    return;
            if (!GetJointPos(hand, XRHandJointID.LittleProximal,  out Vector3 littleProx))  return;

            float indexExt  = Vector3.Distance(indexTip,  indexProx);
            float middleExt = Vector3.Distance(middleTip, middleProx);
            float ringExt   = Vector3.Distance(ringTip,   ringProx);
            float littleExt = Vector3.Distance(littleTip, littleProx);
            float thumbExt  = Vector3.Distance(thumbTip,  thumbProx);

            bool indexCurled  = indexExt  < CurlThreshold;
            bool middleCurled = middleExt < CurlThreshold;
            bool ringCurled   = ringExt   < CurlThreshold;
            bool littleCurled = littleExt < CurlThreshold;
            bool thumbCurled  = thumbExt  < CurlThreshold;

            bool indexExtended  = indexExt  > ExtendThreshold;
            bool middleExtended = middleExt > ExtendThreshold;
            bool ringExtended   = ringExt   > ExtendThreshold;
            bool littleExtended = littleExt > ExtendThreshold;

            // ── Pinch (thumb-index close) ─────────────────────────────────────
            if (Vector3.Distance(thumbTip, indexTip) < PinchDistance)
            {
                if (_activeGesture != ActiveGesture.Pinch)
                {
                    Fire(ActiveGesture.Pinch);
                    OnPinch?.Invoke((thumbTip + indexTip) * 0.5f);
                }
                return;
            }

            // ── Thumbs Up (thumb above wrist, all other fingers curled) ───────
            if ((thumbTip.y - wrist.y) > ThumbVerticalDelta && indexCurled && middleCurled && ringCurled && littleCurled)
            {
                if (_activeGesture != ActiveGesture.ThumbsUp)
                {
                    Fire(ActiveGesture.ThumbsUp);
                    OnThumbsUp?.Invoke();
                }
                return;
            }

            // ── Thumbs Down (thumb below wrist, all other fingers curled) ─────
            if ((wrist.y - thumbTip.y) > ThumbVerticalDelta && indexCurled && middleCurled && ringCurled && littleCurled)
            {
                if (_activeGesture != ActiveGesture.ThumbsDown)
                {
                    Fire(ActiveGesture.ThumbsDown);
                    OnThumbsDown?.Invoke();
                }
                return;
            }

            // ── Point (index extended, rest curled) ───────────────────────────
            if (indexExtended && middleCurled && ringCurled && littleCurled && thumbCurled)
            {
                if (_activeGesture != ActiveGesture.Point)
                {
                    Fire(ActiveGesture.Point);
                    OnPointGesture?.Invoke();
                }
                return;
            }

            // ── Palm Open (all fingers spread) ────────────────────────────────
            Vector3 palmCenter = (indexProx + middleProx + ringProx + littleProx) * 0.25f;
            bool palmOpen = indexExtended && middleExtended && ringExtended && littleExtended
                         && Vector3.Distance(thumbTip,  palmCenter) > ExtendThreshold
                         && Vector3.Distance(indexTip,  palmCenter) > ExtendThreshold
                         && Vector3.Distance(middleTip, palmCenter) > ExtendThreshold
                         && Vector3.Distance(ringTip,   palmCenter) > ExtendThreshold
                         && Vector3.Distance(littleTip, palmCenter) > ExtendThreshold;

            if (palmOpen)
            {
                if (_activeGesture != ActiveGesture.PalmOpen)
                {
                    Fire(ActiveGesture.PalmOpen);
                    OnPalmOpen?.Invoke();
                }
                return;
            }

            // No gesture matched — reset so gestures can re-fire next time
            _activeGesture = ActiveGesture.None;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void Fire(ActiveGesture g)
        {
            _activeGesture = g;
            _cooldownTimer = gestureCooldown;
        }

        private static bool GetJointPos(XRHand hand, XRHandJointID id, out Vector3 position)
        {
            XRHandJoint joint = hand.GetJoint(id);
            if (joint.TryGetPose(out Pose pose))
            {
                position = pose.position;
                return true;
            }
            position = Vector3.zero;
            return false;
        }

        // ── Public API (manual triggers for UI buttons / testing) ─────────────
        public void TriggerThumbsUp()   { Fire(ActiveGesture.ThumbsUp);   OnThumbsUp?.Invoke(); }
        public void TriggerThumbsDown() { Fire(ActiveGesture.ThumbsDown); OnThumbsDown?.Invoke(); }
        public void TriggerPalmOpen()   { Fire(ActiveGesture.PalmOpen);   OnPalmOpen?.Invoke(); }
        public void TriggerPinch()      { Fire(ActiveGesture.Pinch);      OnPinch?.Invoke(transform.position); }
        public void TriggerPoint()      { Fire(ActiveGesture.Point);      OnPointGesture?.Invoke(); }
    }
}
