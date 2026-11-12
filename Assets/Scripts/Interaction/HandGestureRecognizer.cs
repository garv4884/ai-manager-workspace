using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Detects hand gestures via UnityEngine.XR.Hands and raises UnityEvents.
    /// Requires XR Hands 1.7.3 package.
    /// </summary>
    public class HandGestureRecognizer : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [SerializeField] private float gestureCooldown = 0.8f;
        [SerializeField] private XRHandedness dominantHand = XRHandedness.Right;

        // ── Events ────────────────────────────────────────────────────────────
        [Header("Gesture Events")]
        public UnityEvent OnThumbsUp;
        public UnityEvent OnThumbsDown;
        public UnityEvent OnPalmOpen;
        public UnityEvent<Vector3> OnPinch;
        public UnityEvent OnPointGesture;

        // ── Private ───────────────────────────────────────────────────────────
        private XRHandSubsystem _handSubsystem;
        private float _cooldownTimer;

        // Thresholds
        private const float CurlThreshold      = 0.05f;  // tip-to-proximal distance for "curled"
        private const float ExtendThreshold    = 0.08f;  // tip-to-proximal distance for "extended"
        private const float ThumbVerticalDelta = 0.06f;  // thumb-tip vs wrist height difference
        private const float PinchDistance      = 0.025f; // metres

        // Track which gesture is currently active to avoid repeat firing
        private enum ActiveGesture { None, ThumbsUp, ThumbsDown, PalmOpen, Pinch, Point }
        private ActiveGesture _activeGesture = ActiveGesture.None;

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        private void OnEnable()
        {
            AcquireSubsystem();
        }

        private void OnDisable()
        {
            _handSubsystem = null;
        }

        private void Update()
        {
            // Re-acquire if lost
            if (_handSubsystem == null || !_handSubsystem.running)
            {
                AcquireSubsystem();
                if (_handSubsystem == null) return;
            }

            // Tick cooldown
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                return;
            }

            XRHand hand = dominantHand == XRHandedness.Right
                ? _handSubsystem.rightHand
                : _handSubsystem.leftHand;

            if (!hand.isTracked) return;

            EvaluateGestures(hand);
        }

        // ── Subsystem ─────────────────────────────────────────────────────────

        private void AcquireSubsystem()
        {
            var subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            _handSubsystem = subsystems.Count > 0 ? subsystems[0] : null;
        }

        // ── Gesture Detection ─────────────────────────────────────────────────

        private void EvaluateGestures(XRHand hand)
        {
            // Collect joint world positions
            if (!TryGetJointPosition(hand, XRHandJointID.Wrist,          out Vector3 wrist))       return;
            if (!TryGetJointPosition(hand, XRHandJointID.ThumbTip,        out Vector3 thumbTip))    return;
            if (!TryGetJointPosition(hand, XRHandJointID.IndexTip,        out Vector3 indexTip))    return;
            if (!TryGetJointPosition(hand, XRHandJointID.MiddleTip,       out Vector3 middleTip))   return;
            if (!TryGetJointPosition(hand, XRHandJointID.RingTip,         out Vector3 ringTip))     return;
            if (!TryGetJointPosition(hand, XRHandJointID.LittleTip,       out Vector3 littleTip))   return;

            if (!TryGetJointPosition(hand, XRHandJointID.ThumbProximal,   out Vector3 thumbProx))   return;
            if (!TryGetJointPosition(hand, XRHandJointID.IndexProximal,   out Vector3 indexProx))   return;
            if (!TryGetJointPosition(hand, XRHandJointID.MiddleProximal,  out Vector3 middleProx))  return;
            if (!TryGetJointPosition(hand, XRHandJointID.RingProximal,    out Vector3 ringProx))    return;
            if (!TryGetJointPosition(hand, XRHandJointID.LittleProximal,  out Vector3 littleProx))  return;

            // Finger extension metrics: distance from tip to its proximal joint
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

            // ── Pinch ──────────────────────────────────────────────────────
            float pinchDist = Vector3.Distance(thumbTip, indexTip);
            if (pinchDist < PinchDistance)
            {
                if (_activeGesture != ActiveGesture.Pinch)
                {
                    _activeGesture = ActiveGesture.Pinch;
                    _cooldownTimer = gestureCooldown;
                    Vector3 pinchPos = (thumbTip + indexTip) * 0.5f;
                    OnPinch?.Invoke(pinchPos);
                }
                return;
            }

            // ── Thumbs Up ─────────────────────────────────────────────────
            // Thumb tip significantly above wrist; other fingers curled
            bool thumbAboveWrist = (thumbTip.y - wrist.y) > ThumbVerticalDelta;
            if (thumbAboveWrist && indexCurled && middleCurled && ringCurled && littleCurled)
            {
                if (_activeGesture != ActiveGesture.ThumbsUp)
                {
                    _activeGesture = ActiveGesture.ThumbsUp;
                    _cooldownTimer = gestureCooldown;
                    OnThumbsUp?.Invoke();
                }
                return;
            }

            // ── Thumbs Down ───────────────────────────────────────────────
            bool thumbBelowWrist = (wrist.y - thumbTip.y) > ThumbVerticalDelta;
            if (thumbBelowWrist && indexCurled && middleCurled && ringCurled && littleCurled)
            {
                if (_activeGesture != ActiveGesture.ThumbsDown)
                {
                    _activeGesture = ActiveGesture.ThumbsDown;
                    _cooldownTimer = gestureCooldown;
                    OnThumbsDown?.Invoke();
                }
                return;
            }

            // ── Point ─────────────────────────────────────────────────────
            // Index extended, all others curled
            if (indexExtended && middleCurled && ringCurled && littleCurled && thumbCurled)
            {
                if (_activeGesture != ActiveGesture.Point)
                {
                    _activeGesture = ActiveGesture.Point;
                    _cooldownTimer = gestureCooldown;
                    OnPointGesture?.Invoke();
                }
                return;
            }

            // ── Palm Open ─────────────────────────────────────────────────
            // All five fingertips extended away from palm center
            Vector3 palmCenter = (indexProx + middleProx + ringProx + littleProx) * 0.25f;
            float thumbAwayFromPalm  = Vector3.Distance(thumbTip,  palmCenter);
            float indexAwayFromPalm  = Vector3.Distance(indexTip,  palmCenter);
            float middleAwayFromPalm = Vector3.Distance(middleTip, palmCenter);
            float ringAwayFromPalm   = Vector3.Distance(ringTip,   palmCenter);
            float littleAwayFromPalm = Vector3.Distance(littleTip, palmCenter);

            bool palmOpenDetected = indexExtended && middleExtended && ringExtended && littleExtended
                                  && thumbAwayFromPalm  > ExtendThreshold
                                  && indexAwayFromPalm  > ExtendThreshold
                                  && middleAwayFromPalm > ExtendThreshold
                                  && ringAwayFromPalm   > ExtendThreshold
                                  && littleAwayFromPalm > ExtendThreshold;

            if (palmOpenDetected)
            {
                if (_activeGesture != ActiveGesture.PalmOpen)
                {
                    _activeGesture = ActiveGesture.PalmOpen;
                    _cooldownTimer = gestureCooldown;
                    OnPalmOpen?.Invoke();
                }
                return;
            }

            // No gesture — reset tracker so the same gesture can re-fire after cooldown
            _activeGesture = ActiveGesture.None;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool TryGetJointPosition(XRHand hand, XRHandJointID jointId, out Vector3 position)
        {
            XRHandJoint joint = hand.GetJoint(jointId);
            if (joint.TryGetPose(out Pose pose))
            {
                position = pose.position;
                return true;
            }
            position = Vector3.zero;
            return false;
        }
    }
}
