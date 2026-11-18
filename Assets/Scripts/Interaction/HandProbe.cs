using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Shared, per-frame view of both tracked hands for direct (near) interaction: where each pinch is,
    /// whether it just started or ended, and where the index fingertip is. Used by window grabbing,
    /// and the desk bell.
    /// </summary>
    public static class HandProbe
    {
        public struct State
        {
            public Hand hand;
            public bool tracked;
            public bool pinching, pinchStarted, pinchEnded;
            public Vector3 pinchPoint;     // between thumb tip and index tip
            public Quaternion rotation;    // wrist rotation
            public Vector3 indexTip;
        }

        private static readonly List<Hand> hands = new List<Hand>();
        private static readonly Dictionary<Hand, bool> wasPinching = new Dictionary<Hand, bool>();
        private static readonly List<State> states = new List<State>();
        private static int frame = -1;
        private static float nextSearch;

        public static IReadOnlyList<State> Hands
        {
            get { Refresh(); return states; }
        }

        private static void Refresh()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            if (hands.Count < 2 && Time.unscaledTime > nextSearch)
            {
                nextSearch = Time.unscaledTime + 2f;
                foreach (var h in Object.FindObjectsByType<Hand>(FindObjectsInactive.Exclude))
                    if (!hands.Contains(h)) hands.Add(h);
            }
            states.Clear();
            foreach (var h in hands)
            {
                if (h == null || !h.isActiveAndEnabled) continue;
                var st = new State { hand = h };
                st.tracked = h.IsTrackedDataValid;
                bool pinch = false;
                if (st.tracked &&
                    h.GetJointPose(HandJointId.HandThumbTip, out Pose thumb) &&
                    h.GetJointPose(HandJointId.HandIndexTip, out Pose index))
                {
                    pinch = h.GetIndexFingerIsPinching();
                    st.pinchPoint = (thumb.position + index.position) * 0.5f;
                    st.indexTip = index.position;
                    st.rotation = h.GetRootPose(out Pose root) ? root.rotation : Quaternion.identity;
                }
                wasPinching.TryGetValue(h, out bool before);
                st.pinching = pinch;
                st.pinchStarted = pinch && !before;
                st.pinchEnded = !pinch && before;
                wasPinching[h] = pinch;
                states.Add(st);
            }
        }
    }
}
