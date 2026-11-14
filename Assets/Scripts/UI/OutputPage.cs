using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// The physical page an agent carries to the manager. It is small in the agent's hands,
    /// flies to the review spot on the manager's desk at full reading size, and is either
    /// handed back or filed in the "done" tray.
    /// </summary>
    public class OutputPage : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI metaText;
        [SerializeField] private TextMeshProUGUI bodyText;
        [SerializeField] private TextMeshProUGUI promptText;
        [SerializeField] private Image headerBar;
        [SerializeField] private float carriedScale = 0.32f;
        [SerializeField] private float flySeconds = 0.7f;

        private AgentController agent;
        private Transform holdPoint;
        private Vector3 fullScale;

        public bool IsOut { get; private set; }

        public void Bind(AgentController owner, Transform hold)
        {
            agent = owner;
            holdPoint = hold;
            if (fullScale == Vector3.zero) fullScale = transform.localScale;
            AttachToHands();
            gameObject.SetActive(false);
        }

        public void SetVisible(bool visible)
        {
            if (IsOut) return;
            gameObject.SetActive(visible);
            if (visible) Refresh();
        }

        public void Refresh()
        {
            if (agent == null) return;
            AgentData d = agent.Data;
            TaskData t = agent.CurrentTask;

            if (headerBar != null) headerBar.color = d.StatusColor;
            if (titleText != null) titleText.text = t != null ? t.title : "Update";
            if (metaText != null)
                metaText.text = $"<b>{d.agentName}</b>  ·  {d.agentRole}" + (t != null && t.revision > 1 ? $"  ·  Revision {t.revision}" : "");

            if (bodyText != null)
            {
                if (t == null) bodyText.text = "";
                else if (d.status == AgentStatus.WaitingForInput)
                    bodyText.text = $"<color=#B07A00><b>QUESTION</b></color>\n{t.question}";
                else if (d.status == AgentStatus.Error)
                    bodyText.text = $"<color=#C0392B><b>BLOCKED</b></color>\n{t.error}\n\nApprove to retry, or dismiss the task.";
                else
                    bodyText.text = t.output;
            }

            if (promptText != null)
            {
                promptText.text = d.status switch
                {
                    AgentStatus.WaitingForInput => "Type your answer on the desk keyboard",
                    AgentStatus.Error => "Thumbs up: retry      Palm push: dismiss",
                    _ => "Thumbs up: approve      Thumbs down: changes      Palm push: dismiss"
                };
            }
        }

        private void AttachToHands()
        {
            if (holdPoint == null) return;
            transform.SetParent(holdPoint, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = fullScale * carriedScale;
        }

        public IEnumerator FlyTo(Transform anchor)
        {
            Refresh();
            gameObject.SetActive(true);
            IsOut = true;
            transform.SetParent(null, true);
            yield return Animate(anchor.position, anchor.rotation, fullScale);
            transform.SetParent(anchor, true);
        }

        public IEnumerator FlyBack()
        {
            if (holdPoint == null) yield break;
            transform.SetParent(null, true);
            yield return Animate(holdPoint.position, holdPoint.rotation, fullScale * carriedScale);
            IsOut = false;
            AttachToHands();
        }

        public IEnumerator FileTo(Transform tray)
        {
            transform.SetParent(null, true);
            Quaternion flat = tray.rotation * Quaternion.Euler(90f, 0f, 0f);
            yield return Animate(tray.position + Vector3.up * 0.02f, flat, fullScale * 0.28f);
            IsOut = false;
            AttachToHands();
            gameObject.SetActive(false);
        }

        public IEnumerator Discard()
        {
            transform.SetParent(null, true);
            Vector3 start = transform.position;
            yield return Animate(start + Vector3.down * 0.25f, transform.rotation * Quaternion.Euler(0f, 0f, 25f), fullScale * 0.05f);
            IsOut = false;
            AttachToHands();
            gameObject.SetActive(false);
        }

        private IEnumerator Animate(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            Vector3 p0 = transform.position;
            Quaternion r0 = transform.rotation;
            Vector3 s0 = transform.localScale;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / flySeconds;
                float k = Mathf.SmoothStep(0f, 1f, t);
                Vector3 arc = Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.15f;
                transform.SetPositionAndRotation(Vector3.Lerp(p0, pos, k) + arc, Quaternion.Slerp(r0, rot, k));
                transform.localScale = Vector3.Lerp(s0, scale, k);
                yield return null;
            }
            transform.SetPositionAndRotation(pos, rot);
            transform.localScale = scale;
        }
    }
}
