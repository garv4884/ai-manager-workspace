using System.Collections;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Presentation lift in front of the elevated manager desk. An agent steps on at floor
    /// level and rides up to face the manager eye to eye.
    /// </summary>
    public class LiftPad : MonoBehaviour
    {
        [SerializeField] private Transform platform;
        [SerializeField] private Transform piston;
        [SerializeField] private Transform faceTowards;
        [SerializeField] private float lowY = 0.05f;
        [SerializeField] private float highY = 1.45f;
        [SerializeField] private float travelSeconds = 1.6f;
        [SerializeField] private AudioSource hum;

        public Transform Platform => platform;
        public bool IsDown { get; private set; } = true;
        public bool IsMoving { get; private set; }

        public Vector3 BoardPosition
        {
            get { Vector3 p = platform.position; return new Vector3(p.x, 0f, p.z); }
        }

        public Quaternion FacingRotation
        {
            get
            {
                Vector3 target = faceTowards != null ? faceTowards.position : transform.position - transform.forward;
                Vector3 dir = target - platform.position;
                dir.y = 0f;
                return dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : platform.rotation;
            }
        }

        private void Awake() => SetHeight(lowY);

        public IEnumerator Raise() { yield return Move(lowY, highY); IsDown = false; }
        public IEnumerator Lower() { yield return Move(highY, lowY); IsDown = true; }

        private IEnumerator Move(float from, float to)
        {
            IsMoving = true;
            if (hum != null) hum.Play();
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.05f, travelSeconds);
                SetHeight(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)));
                yield return null;
            }
            SetHeight(to);
            if (hum != null) hum.Stop();
            IsMoving = false;
        }

        private void SetHeight(float y)
        {
            Vector3 p = platform.localPosition;
            platform.localPosition = new Vector3(p.x, y, p.z);
            if (piston != null)
            {
                float h = Mathf.Max(0.01f, y);
                Vector3 s = piston.localScale;
                piston.localScale = new Vector3(s.x, h * 0.5f, s.z);
                Vector3 pp = piston.localPosition;
                piston.localPosition = new Vector3(pp.x, h * 0.5f, pp.z);
            }
        }
    }
}
