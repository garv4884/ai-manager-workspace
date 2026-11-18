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
        [SerializeField] private RectTransform viewport;      // masked area the body scrolls inside
        [SerializeField] private TextMeshProUGUI moreHint;   // "more below" cue
        [SerializeField] private float carriedScale = 0.32f;
        [SerializeField] private float flySeconds = 0.7f;

        private AgentController agent;
        private Transform holdPoint;
        private Vector3 fullScale;

        public bool IsOut { get; private set; }

        /// <summary>Scrolls the body (console ▲ / ▼ buttons). delta in canvas units, positive = down.</summary>
        public void Scroll(float delta)
        {
            if (viewport == null || bodyText == null) return;
            RectTransform c = bodyText.rectTransform;
            float max = Mathf.Max(0f, bodyText.preferredHeight - viewport.rect.height + 12f);
            float y = Mathf.Clamp(c.anchoredPosition.y + delta, 0f, max);
            c.anchoredPosition = new Vector2(c.anchoredPosition.x, y);
            UpdateHint();
        }

        public bool CanScroll => viewport != null && bodyText != null && bodyText.preferredHeight > viewport.rect.height + 4f;

        private void UpdateHint()
        {
            if (moreHint == null || viewport == null) return;
            float max = Mathf.Max(0f, bodyText.preferredHeight - viewport.rect.height + 12f);
            float y = bodyText.rectTransform.anchoredPosition.y;
            moreHint.text = max <= 1f ? "" : y < max - 1f ? "More below  ·  scroll on the console" : "End";
        }

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
                else switch (t.request)
                {
                    case RequestKind.Question:
                        bodyText.text = $"<color=#B07A00><b>QUESTION</b></color>\n{t.question}";
                        break;
                    case RequestKind.Choice:
                        var sb = new System.Text.StringBuilder($"<color=#B07A00><b>PLEASE CHOOSE</b></color>\n{t.question}\n");
                        for (int i = 0; i < t.options.Count; i++) sb.Append($"\n<b>{i + 1}.</b> {t.options[i]}");
                        bodyText.text = sb.ToString();
                        break;
                    case RequestKind.Error:
                        bodyText.text = $"<color=#C0392B><b>BLOCKED</b></color>\n{t.error}";
                        break;
                    case RequestKind.Report:
                        bodyText.text = $"<color=#1F8A80><b>UPDATE</b></color>\n{t.output}";
                        break;
                    default:
                        bodyText.text = t.output;
                        break;
                }
            }

            if (promptText != null)
                promptText.text = ManagerAction.GestureHint(ManagerAction.For(t, d.agentName));

            if (viewport != null && bodyText != null)
            {
                // Body grows with its text and scrolls inside the viewport.
                bodyText.ForceMeshUpdate();
                var rt = bodyText.rectTransform;
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, Mathf.Max(viewport.rect.height, bodyText.preferredHeight + 8f));
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 0f);
                UpdateHint();
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
