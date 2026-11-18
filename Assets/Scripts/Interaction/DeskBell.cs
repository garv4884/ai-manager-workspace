using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// A service bell on the desk. Tap it with a fingertip: it rings and pauses the queue, tap again to call
    /// the next agent up (resume). The bell's ring glows amber while the queue is on hold.
    /// </summary>
    public class DeskBell : MonoBehaviour
    {
        [SerializeField] private Transform button;        // the plunger on top
        [SerializeField] private Renderer ring;            // glow ring
        [SerializeField] private float tapRadius = 0.045f;

        private float cooldown;
        private float press;
        private Vector3 buttonHome;
        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Start() { if (button != null) buttonHome = button.localPosition; }

        private void Update()
        {
            if (cooldown > 0f) cooldown -= Time.deltaTime;
            press = Mathf.MoveTowards(press, 0f, Time.deltaTime * 6f);
            if (button != null) button.localPosition = buttonHome + Vector3.down * 0.008f * press;

            GameManager gm = GameManager.Instance;
            if (ring != null)
            {
                block ??= new MaterialPropertyBlock();
                bool held = gm.Queue.Hold;
                block.SetColor(BaseColorId, held ? new Color(1f, 0.7f, 0.28f) * (0.7f + 0.3f * Mathf.Sin(Time.time * 4f)) : new Color(0.31f, 0.82f, 0.77f));
                ring.SetPropertyBlock(block);
            }
            if (cooldown > 0f || button == null) return;

            foreach (var h in HandProbe.Hands)
            {
                if (!h.tracked) continue;
                if (Vector3.Distance(h.indexTip, button.position) < tapRadius) { Ring(); break; }
            }
        }

        public void Ring()
        {
            cooldown = 0.8f;
            press = 1f;
            GameManager gm = GameManager.Instance;
            gm.Queue.Hold = !gm.Queue.Hold;
            gm.Queue.Desk.Play(ReviewDesk.Sfx.Arrive);
        }
    }
}
