using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Animates workstations into a layout template. Desks carve the NavMesh with
    /// NavMeshObstacles, so nothing needs rebaking when they move, and seated agents
    /// ride along because they are parented to their seats.
    /// </summary>
    public class WorkspaceLayoutManager : MonoBehaviour
    {
        [SerializeField] private AgentManager agentManager;
        [SerializeField] private Transform center;
        [SerializeField] private float transitionSeconds = 1.6f;
        [SerializeField] private LayoutType startLayout = LayoutType.Grid;

        [Header("Placement limits")]
        [SerializeField] private Vector2 roomMin = new Vector2(-11.0f, -8.2f);
        [SerializeField] private Vector2 roomMax = new Vector2(11.0f, 13.4f);
        [SerializeField] private float platformClearRadius = 3.6f;
        [SerializeField] private float laneHalfWidth = 1.7f;
        [SerializeField] private float laneLength = 9.5f;
        [SerializeField] private Rect[] keepOut = { new Rect(6.6f, -9.2f, 6.0f, 6.6f) };   // meeting room

        private Coroutine transition;

        public LayoutType Current { get; private set; }
        public event Action Changed;

        private void Start() => ApplyLayout(startLayout, instant: true);

        public void ApplyLayout(LayoutType type) => ApplyLayout(type, false);

        public void ApplyLayout(LayoutType type, bool instant)
        {
            if (type == LayoutType.Custom) return;
            var desks = ActiveWorkstations();
            List<DeskPose> poses = WorkspaceLayout.Generate(type, desks.Count, center.position);
            Current = type;
            Changed?.Invoke();
            if (transition != null) StopCoroutine(transition);
            transition = StartCoroutine(Animate(desks, poses, instant ? 0f : transitionSeconds));
        }

        public void MarkCustom()
        {
            Current = LayoutType.Custom;
            Changed?.Invoke();
        }

        public Quaternion FacingFor(Vector3 position) => WorkspaceLayout.Facing(position, center.position);

        /// <summary>Keeps a dragged desk inside the room and off the platform, queue lane and meeting room.</summary>
        public Vector3 ClampPosition(Vector3 p)
        {
            p.y = 0f;
            p.x = Mathf.Clamp(p.x, roomMin.x, roomMax.x);
            p.z = Mathf.Clamp(p.z, roomMin.y, roomMax.y);

            Vector3 c = center.position; c.y = 0f;
            Vector3 fromC = p - c;
            if (fromC.magnitude < platformClearRadius)
                p = c + (fromC.sqrMagnitude > 0.001f ? fromC.normalized : Vector3.right) * platformClearRadius;

            float localZ = p.z - c.z, localX = p.x - c.x;
            if (localZ > 0f && localZ < laneLength && Mathf.Abs(localX) < laneHalfWidth)
                p.x = c.x + Mathf.Sign(localX == 0f ? 1f : localX) * laneHalfWidth;

            foreach (Rect r in keepOut)
            {
                if (!r.Contains(new Vector2(p.x, p.z))) continue;
                float left = p.x - r.xMin, right = r.xMax - p.x, bottom = p.z - r.yMin, top = r.yMax - p.z;
                float m = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
                if (m == left) p.x = r.xMin; else if (m == right) p.x = r.xMax;
                else if (m == bottom) p.z = r.yMin; else p.z = r.yMax;
            }
            return p;
        }

        private List<Workstation> ActiveWorkstations()
        {
            var list = new List<Workstation>();
            foreach (var w in agentManager.Workstations) if (w != null && w.gameObject.activeSelf) list.Add(w);
            return list;
        }

        private IEnumerator Animate(List<Workstation> desks, List<DeskPose> poses, float seconds)
        {
            int n = Mathf.Min(desks.Count, poses.Count);
            var p0 = new Vector3[n];
            var r0 = new Quaternion[n];
            for (int i = 0; i < n; i++) { p0[i] = desks[i].transform.position; r0[i] = desks[i].transform.rotation; }

            float t = seconds <= 0f ? 1f : 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / seconds;
                float k = Mathf.SmoothStep(0f, 1f, t);
                for (int i = 0; i < n; i++)
                {
                    // Lift each desk slightly mid-flight so paths crossing each other read clearly.
                    Vector3 hop = Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.25f;
                    desks[i].transform.SetPositionAndRotation(Vector3.Lerp(p0[i], poses[i].position, k) + hop,
                        Quaternion.Slerp(r0[i], poses[i].rotation, k));
                }
                yield return null;
            }
            for (int i = 0; i < n; i++)
                desks[i].transform.SetPositionAndRotation(poses[i].position, poses[i].rotation);
            transition = null;
        }
    }
}
