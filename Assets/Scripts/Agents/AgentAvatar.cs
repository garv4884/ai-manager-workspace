using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Procedural pose animation for the stylised primitive avatar: walk cycle, sitting,
    /// typing at the desk, and holding a page out to the manager. No Animator needed.
    /// </summary>
    public class AgentAvatar : MonoBehaviour
    {
        public enum Pose { Standing, Walking, Sitting, Typing, Holding }

        [Header("Rig (built by AgentPrefabSetup)")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform legL;
        [SerializeField] private Transform legR;
        [SerializeField] private Transform armL;
        [SerializeField] private Transform armR;
        [SerializeField] private Transform head;
        [SerializeField] private Transform holdPoint;

        [Header("Tinting")]
        [SerializeField] private Renderer[] shirtRenderers;
        [SerializeField] private Renderer[] accentRenderers;

        [Header("Tuning")]
        [SerializeField] private float sitDrop = 0.45f;
        [SerializeField] private float walkCycleSpeed = 7.5f;

        private Pose pose = Pose.Standing;
        private bool carrying;
        private float sitBlend;
        private float carryBlend;
        private float clock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public Transform Head => head;
        public Transform HoldPoint => holdPoint;
        public Pose CurrentPose => pose;

        public void SetPose(Pose p) => pose = p;
        public void SetCarrying(bool c) => carrying = c;

        public void SetColors(Color shirt)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, shirt);
            foreach (var r in shirtRenderers) if (r != null) r.SetPropertyBlock(block);

            var accent = new MaterialPropertyBlock();
            accent.SetColor(BaseColorId, Color.Lerp(shirt, Color.black, 0.45f));
            foreach (var r in accentRenderers) if (r != null) r.SetPropertyBlock(accent);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            clock += dt;

            bool sitting = pose == Pose.Sitting || pose == Pose.Typing;
            sitBlend = Mathf.MoveTowards(sitBlend, sitting ? 1f : 0f, dt * 3.5f);
            carryBlend = Mathf.MoveTowards(carryBlend, (carrying || pose == Pose.Holding) ? 1f : 0f, dt * 4f);

            float swing = pose == Pose.Walking ? Mathf.Sin(clock * walkCycleSpeed) * 28f : 0f;
            float bob = pose == Pose.Walking ? Mathf.Abs(Mathf.Sin(clock * walkCycleSpeed)) * 0.03f : 0f;
            float breathe = Mathf.Sin(clock * 1.6f) * 0.006f;

            if (body != null)
                body.localPosition = new Vector3(0f, -sitDrop * sitBlend + bob + breathe, 0f);

            // Legs: swing when walking, horizontal when seated.
            if (legL != null) legL.localRotation = Quaternion.Euler(Mathf.Lerp(swing, -88f, sitBlend), 0f, 0f);
            if (legR != null) legR.localRotation = Quaternion.Euler(Mathf.Lerp(-swing, -88f, sitBlend), 0f, 0f);

            // Arms: counter-swing, rest on the desk, type, or hold the page forward.
            float baseL = -swing * 0.7f, baseR = swing * 0.7f;
            if (pose == Pose.Typing)
            {
                baseL = -62f + Mathf.Sin(clock * 17f) * 5f;
                baseR = -62f + Mathf.Sin(clock * 15f + 1.3f) * 5f;
            }
            else if (sitting)
            {
                baseL = baseR = -30f;
            }
            const float holdAngle = -42f;

            float left = Mathf.Lerp(baseL, holdAngle, carryBlend);
            float right = Mathf.Lerp(baseR, holdAngle, carryBlend);

            if (armL != null) armL.localRotation = Quaternion.Euler(left, 0f, Mathf.Lerp(-6f, -14f, carryBlend));
            if (armR != null) armR.localRotation = Quaternion.Euler(right, 0f, Mathf.Lerp(6f, 14f, carryBlend));

            if (head != null)
            {
                float nod = pose == Pose.Typing ? 12f + Mathf.Sin(clock * 0.9f) * 3f : 0f;
                float look = pose == Pose.Standing ? Mathf.Sin(clock * 0.5f) * 8f : 0f;
                head.localRotation = Quaternion.Euler(nod, look, 0f);
            }
        }
    }
}
