using TMPro;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Procedural animation for the robot agent: walk cycle with arm swing and bob, sitting, typing,
    /// holding a page, a wave when it reaches you, and an expressive screen face.
    ///
    /// Face: two glowing eyes that blink, glance around, narrow while working and widen when the agent
    /// needs you; glyph faces for moments (^ ^ approved, - - dropped, x x blocked). The antenna light
    /// shows the status colour, so you can read the room at a glance. No Animator needed.
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

        [Header("Face")]
        [SerializeField] private Transform eyeL;
        [SerializeField] private Transform eyeR;
        [SerializeField] private Renderer[] eyeRenderers;
        [SerializeField] private TextMeshPro faceGlyph;
        [SerializeField] private Renderer antennaLight;
        [SerializeField] private GameObject linkBadge;

        [Header("Tinting")]
        [SerializeField] private Renderer[] shirtRenderers;
        [SerializeField] private Renderer[] accentRenderers;

        [Header("Tuning")]
        [SerializeField] private float sitDrop = 0.45f;
        [SerializeField] private float walkCycleSpeed = 7.5f;

        private Pose pose = Pose.Standing;
        private bool carrying;
        private float sitBlend, carryBlend, walkBlend, waveTimer;
        private float clock;
        private AgentStatus status = AgentStatus.Free;
        private Color statusColor = AgentData.ColorFor(AgentStatus.Free);
        private float reactTimer;
        private bool reactHappy;
        private float nextBlink, blinkTimer;
        private Vector2 glance, glanceTarget;
        private float nextGlance;
        private Vector3 eyeScale;
        private Quaternion headLook = Quaternion.identity;
        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public Transform Head => head;
        public Transform HoldPoint => holdPoint;
        public Pose CurrentPose => pose;

        private void Awake()
        {
            if (eyeL != null) eyeScale = eyeL.localScale;
            nextBlink = Random.Range(1f, 4f);
            clock = Random.Range(0f, 10f);
        }

        public void SetPose(Pose p) => pose = p;
        public void SetCarrying(bool c) => carrying = c;
        public void Wave() => waveTimer = 1.6f;
        public void SetLinked(bool linked) { if (linkBadge != null) linkBadge.SetActive(linked); }

        public void SetStatus(AgentStatus s)
        {
            status = s;
            statusColor = AgentData.ColorFor(s);
        }

        /// <summary>Brief expression after a decision: happy (approved) or a sigh (dropped).</summary>
        public void React(bool happy)
        {
            reactHappy = happy;
            reactTimer = 2.2f;
        }

        public void SetColors(Color shirt)
        {
            var b = new MaterialPropertyBlock();
            b.SetColor(BaseColorId, shirt);
            foreach (var r in shirtRenderers) if (r != null) r.SetPropertyBlock(b);

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
            walkBlend = Mathf.MoveTowards(walkBlend, pose == Pose.Walking ? 1f : 0f, dt * 5f);   // eases in/out of the stride
            if (waveTimer > 0f) waveTimer -= dt;

            float phase = clock * walkCycleSpeed;
            float swing = Mathf.Sin(phase) * 30f * walkBlend;
            float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.035f * walkBlend;
            float sway = Mathf.Sin(phase) * 2.5f * walkBlend;
            float breathe = Mathf.Sin(clock * 1.6f) * 0.006f;

            if (body != null)
            {
                body.localPosition = new Vector3(0f, -sitDrop * sitBlend + bob + breathe, 0f);
                body.localRotation = Quaternion.Euler(4f * walkBlend, 0f, sway);   // lean into the walk
            }

            // Legs: swing when walking, bent at the hip when seated.
            if (legL != null) legL.localRotation = Quaternion.Euler(Mathf.Lerp(swing, -88f, sitBlend), 0f, 0f);
            if (legR != null) legR.localRotation = Quaternion.Euler(Mathf.Lerp(-swing, -88f, sitBlend), 0f, 0f);

            // Arms: counter-swing, rest on the desk, type, hold the page, or wave.
            float baseL = -swing * 0.8f, baseR = swing * 0.8f;
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
            float rightRoll = Mathf.Lerp(6f, 14f, carryBlend);

            if (waveTimer > 0f)
            {
                float w = Mathf.Clamp01(Mathf.Min(waveTimer, 1.6f - waveTimer) * 4f);
                right = Mathf.Lerp(right, -160f, w);
                rightRoll = Mathf.Lerp(rightRoll, 20f + Mathf.Sin(clock * 14f) * 22f, w);
            }

            if (armL != null) armL.localRotation = Quaternion.Euler(left, 0f, Mathf.Lerp(-6f, -14f, carryBlend));
            if (armR != null) armR.localRotation = Quaternion.Euler(right, 0f, rightRoll);

            UpdateHead(dt);
            UpdateFace(dt);
        }

        private void UpdateHead(float dt)
        {
            if (head == null) return;
            Quaternion target;
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            bool lookAtManager = cam != null && pose != Pose.Typing &&
                                 (status != AgentStatus.Free && status != AgentStatus.Working || pose == Pose.Holding || waveTimer > 0f);
            if (lookAtManager)
            {
                // Turn the head towards the manager (clamped, like a person glancing up).
                Vector3 local = head.parent.InverseTransformDirection(cam.position - head.position);
                float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -65f, 65f);
                float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -35f, 25f);
                target = Quaternion.Euler(pitch, yaw, 0f);
            }
            else
            {
                float nod = pose == Pose.Typing ? 14f + Mathf.Sin(clock * 0.9f) * 3f : 0f;
                float look = pose == Pose.Standing || pose == Pose.Sitting ? Mathf.Sin(clock * 0.5f) * 10f : 0f;
                target = Quaternion.Euler(nod, look, 0f);
            }
            headLook = Quaternion.Slerp(headLook, target, 1f - Mathf.Exp(-dt * 6f));
            head.localRotation = headLook;
        }

        private void UpdateFace(float dt)
        {
            block ??= new MaterialPropertyBlock();

            // Antenna: status colour, pulsing while the agent needs you.
            if (antennaLight != null)
            {
                bool needs = status == AgentStatus.WaitingForApproval || status == AgentStatus.WaitingForInput ||
                             status == AgentStatus.Error || status == AgentStatus.HasUpdate;
                float pulse = needs ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(clock * 4f)) : 0.85f;
                block.Clear();
                block.SetColor(BaseColorId, statusColor * pulse);
                antennaLight.SetPropertyBlock(block);
            }

            if (reactTimer > 0f) reactTimer -= dt;
            string glyph = reactTimer > 0f ? (reactHappy ? "^ ^" : "- -") : status == AgentStatus.Error ? "x x" : null;
            bool showGlyph = glyph != null;
            if (faceGlyph != null)
            {
                faceGlyph.gameObject.SetActive(showGlyph);
                if (showGlyph)
                {
                    faceGlyph.text = glyph;
                    faceGlyph.color = reactTimer > 0f ? (reactHappy ? new Color(0.45f, 1f, 0.6f) : new Color(0.7f, 0.75f, 0.85f)) : statusColor;
                }
            }
            if (eyeL == null || eyeR == null) return;
            eyeL.gameObject.SetActive(!showGlyph);
            eyeR.gameObject.SetActive(!showGlyph);
            if (showGlyph) return;

            // Blink.
            nextBlink -= dt;
            if (nextBlink <= 0f) { blinkTimer = 0.14f; nextBlink = Random.Range(2.2f, 5.5f); }
            if (blinkTimer > 0f) blinkTimer -= dt;
            float lid = blinkTimer > 0f ? 0.12f : 1f;

            // Expression: focused (narrow, looking down at the screen), alert (wide) or relaxed.
            float open = status == AgentStatus.Working ? 0.55f
                       : status == AgentStatus.Free ? 0.9f
                       : 1.15f;
            float width = status == AgentStatus.Working ? 1.15f : 1f;

            nextGlance -= dt;
            if (nextGlance <= 0f)
            {
                nextGlance = Random.Range(0.8f, 2.6f);
                glanceTarget = status == AgentStatus.Working
                    ? new Vector2(Random.Range(-0.35f, 0.35f), -0.35f)
                    : new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.2f, 0.25f));
                if (status != AgentStatus.Free && status != AgentStatus.Working) glanceTarget = Vector2.zero;   // eyes on you
            }
            glance = Vector2.Lerp(glance, glanceTarget, 1f - Mathf.Exp(-dt * 12f));

            Vector3 s = new Vector3(eyeScale.x * width, eyeScale.y * open * lid, eyeScale.z);
            eyeL.localScale = s;
            eyeR.localScale = s;
            Vector3 offset = new Vector3(glance.x * 0.012f, glance.y * 0.01f, 0f);
            eyeL.localPosition = new Vector3(-0.045f, 0f, 0f) + offset;
            eyeR.localPosition = new Vector3(0.045f, 0f, 0f) + offset;

            Color eyeColor = status == AgentStatus.Free || status == AgentStatus.Working
                ? new Color(0.75f, 0.95f, 1f)
                : Color.Lerp(statusColor, Color.white, 0.25f);
            block.Clear();
            block.SetColor(BaseColorId, eyeColor);
            foreach (var r in eyeRenderers) if (r != null) r.SetPropertyBlock(block);
        }
    }
}
