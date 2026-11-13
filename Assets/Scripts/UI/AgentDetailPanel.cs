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
            PopulateFields(agent.Data, agent.CurrentTask);

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
            PopulateFields(trackedAgent.Data, trackedAgent.CurrentTask);
        }

        private void PopulateFields(AgentData data, TaskData task)
        {
            if (data == null) return;

            if (nameText != null)
                nameText.text = data.agentName;

            if (roleText != null)
                roleText.text = data.agentRole;

            if (statusText != null)
                statusText.text = data.GetStatusDisplayString();

            if (taskDescText != null)
                taskDescText.text = task != null ? task.description : (!string.IsNullOrEmpty(data.currentTaskDescription) ? data.currentTaskDescription : "â€”");

            if (outputPreviewText != null)
            {
                string outStr = task != null && !string.IsNullOrEmpty(task.outputResult)
                    ? task.outputResult
                    : (!string.IsNullOrEmpty(data.currentOutput) ? data.currentOutput : "No output yet.");
                outputPreviewText.text = TruncateOutput(outStr, 200);
            }

            if (statusColorBar != null)
            {
                statusColorBar.color = data.GetStatusColor();
            }
        }

        private string TruncateOutput(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "â€¦";
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
