using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Pick an object up with your hand: bring your fingers to it and pinch. It follows the pinch while you
    /// hold, and glides back to its spot on the desk a moment after you let go (so it is always there next time).
    /// Subclasses add what the object does.
    /// </summary>
    public class NearGrabbable : MonoBehaviour
    {
        [SerializeField] protected float grabRadius = 0.09f;
        [SerializeField] protected float returnDelay = 1.2f;
        [SerializeField] protected Renderer[] highlight;

        protected Transform home;
        protected Vector3 homePos;
        protected Quaternion homeRot;
        protected Oculus.Interaction.Input.Hand holder;
        private Vector3 grabOffset;
        private Quaternion grabRotOffset;
        private float releasedAt = -1f;
        private bool hover;
        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public bool Held => holder != null;

        protected virtual void Start()
        {
            homePos = transform.localPosition;
            homeRot = transform.localRotation;
        }

        protected virtual void Update()
        {
            var hands = HandProbe.Hands;
            if (holder == null)
            {
                bool near = false;
                foreach (var h in hands)
                {
                    if (!h.tracked) continue;
                    float d = Vector3.Distance(h.pinchPoint, transform.position);
                    if (d < grabRadius) near = true;
                    if (h.pinchStarted && d < grabRadius)
                    {
                        holder = h.hand;
                        grabOffset = Quaternion.Inverse(h.rotation) * (transform.position - h.pinchPoint);
                        grabRotOffset = Quaternion.Inverse(h.rotation) * transform.rotation;
                        releasedAt = -1f;
                        OnGrabbed();
                        break;
                    }
                }
                SetHover(near);
            }
            else
            {
                bool found = false;
                foreach (var h in hands)
                {
                    if (h.hand != holder) continue;
                    found = true;
                    if (!h.pinching || !h.tracked) { Release(); break; }
                    transform.SetPositionAndRotation(h.pinchPoint + h.rotation * grabOffset, h.rotation * grabRotOffset);
                    WhileHeld();
                }
                if (!found) Release();
            }

            // Glide home after a moment.
            if (holder == null && releasedAt > 0f && Time.time - releasedAt > returnDelay)
            {
                float k = 1f - Mathf.Exp(-Time.deltaTime * 8f);
                transform.localPosition = Vector3.Lerp(transform.localPosition, homePos, k);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, homeRot, k);
                if ((transform.localPosition - homePos).sqrMagnitude < 0.00001f) { transform.localPosition = homePos; transform.localRotation = homeRot; releasedAt = -1f; }
            }
        }

        protected void Release()
        {
            if (holder == null) return;
            holder = null;
            releasedAt = Time.time;
            OnReleased();
        }

        /// <summary>Send it home right away (after it has done its job).</summary>
        protected void ReturnNow() { holder = null; releasedAt = Time.time - returnDelay; }

        private void SetHover(bool on)
        {
            if (on == hover || highlight == null) return;
            hover = on;
            block ??= new MaterialPropertyBlock();
            foreach (var r in highlight)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                if (on) block.SetColor(BaseColorId, Color.Lerp(r.sharedMaterial.GetColor(BaseColorId), Color.white, 0.35f));
                else block.Clear();
                r.SetPropertyBlock(block);
            }
        }

        protected virtual void OnGrabbed() { }
        protected virtual void WhileHeld() { }
        protected virtual void OnReleased() { }
    }
}
