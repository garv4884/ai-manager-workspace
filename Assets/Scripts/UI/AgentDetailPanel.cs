using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AIManagerWorkspace
{
    public class AgentDetailPanel : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI roleText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI taskDescText;
        [SerializeField] private TextMeshProUGUI outputPreviewText;
        [SerializeField] private Image statusColorBar;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private float fadeDuration = 0.2f;

        private AgentController trackedAgent;
        private Coroutine activeFade;

        private static readonly Color[] StatusColors = new Color[]
        {
            Color.green,         // Free
            Color.yellow,        // Working
            new Color(1f, 0.5f, 0f), // WaitingForApproval (orange)
            Color.cyan,          // WaitingForInput
            Color.blue,          // InQueue
            Color.magenta,       // Presenting
            Color.gray,          // Walking
            Color.red,           // Rejected
            Color.white          // Completed / default
        };

        private void Awake()
        {
            if (panelGroup != null)
            {
                panelGroup.alpha = 0f;
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
            }
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows the panel populated with data from the given agent and starts live tracking.
        /// </summary>
        public void ShowForAgent(AgentController agent)
        {
            if (agent == null) return;

            trackedAgent = agent;
            PopulateFields(agent.data);

            gameObject.SetActive(true);

            if (activeFade != null) StopCoroutine(activeFade);
            activeFade = StartCoroutine(FadeIn());

            // Start periodic refresh every 0.5 seconds
            CancelInvoke(nameof(Refresh));
            InvokeRepeating(nameof(Refresh), 0.5f, 0.5f);
        }

        /// <summary>
        /// Hides the panel and clears tracked agent.
        /// </summary>
        public void Hide()
        {
            CancelInvoke(nameof(Refresh));
            trackedAgent = null;

            if (activeFade != null) StopCoroutine(activeFade);
            activeFade = StartCoroutine(FadeOut());
        }

        /// <summary>
        /// Refreshes all displayed fields from the tracked agent's current data.
        /// Called every 0.5s via InvokeRepeating.
        /// </summary>
        private void Refresh()
        {
            if (trackedAgent == null)
            {
                Hide();
                return;
            }
            PopulateFields(trackedAgent.data);
        }

        private void PopulateFields(AgentData data)
        {
            if (data == null) return;

            if (nameText != null)
                nameText.text = data.agentName;

            if (roleText != null)
                roleText.text = data.role;

            if (statusText != null)
                statusText.text = data.currentStatus.ToString();

            if (taskDescText != null)
                taskDescText.text = data.currentTask != null ? data.currentTask.description : "—";

            if (outputPreviewText != null)
                outputPreviewText.text = data.currentTask != null && !string.IsNullOrEmpty(data.currentTask.output)
                    ? TruncateOutput(data.currentTask.output, 200)
                    : "No output yet.";

            if (statusColorBar != null)
            {
                int statusIndex = (int)data.currentStatus;
                statusColorBar.color = (statusIndex >= 0 && statusIndex < StatusColors.Length)
                    ? StatusColors[statusIndex]
                    : Color.white;
            }
        }

        private string TruncateOutput(string text, int maxLength)
        {
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "…";
        }

        private IEnumerator FadeIn()
        {
            if (panelGroup == null) yield break;

            panelGroup.interactable = true;
            panelGroup.blocksRaycasts = true;

            float startAlpha = panelGroup.alpha;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                panelGroup.alpha = Mathf.Lerp(startAlpha, 1f, Mathf.Clamp01(elapsed / fadeDuration));
                yield return null;
            }

            panelGroup.alpha = 1f;
            activeFade = null;
        }

        private IEnumerator FadeOut()
        {
            if (panelGroup == null)
            {
                gameObject.SetActive(false);
                yield break;
            }

            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            float startAlpha = panelGroup.alpha;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                panelGroup.alpha = Mathf.Lerp(startAlpha, 0f, Mathf.Clamp01(elapsed / fadeDuration));
                yield return null;
            }

            panelGroup.alpha = 0f;
            gameObject.SetActive(false);
            activeFade = null;
        }
    }
}
