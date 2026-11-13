using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AIManagerWorkspace
{
    public class LayoutSelectorUI : MonoBehaviour
    {
        [SerializeField] private WorkspaceLayoutManager layoutManager;
        [SerializeField] private Button[] layoutButtons; // 6 buttons, one per LayoutType
        [SerializeField] private TextMeshProUGUI currentLayoutLabel;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private float fadeDuration = 0.25f;

        private bool isVisible = false;
        private Coroutine activeFade;

        private static readonly string[] LayoutNames = new string[]
        {
            "Grid", "Circle", "U-Shape", "Hub & Spoke", "Coworking", "War Room"
        };

        private void Start()
        {
            // Wire up each button to its corresponding LayoutType index
            for (int i = 0; i < layoutButtons.Length; i++)
            {
                int index = i; // capture for closure
                if (layoutButtons[i] != null)
                    layoutButtons[i].onClick.AddListener(() => OnLayoutButtonPressed(index));
            }

            // Initialize panel state: visible by default as an in-world desk console
            if (panelGroup != null)
            {
                panelGroup.alpha = 1f;
                panelGroup.interactable = true;
                panelGroup.blocksRaycasts = true;
            }

            gameObject.SetActive(true);
            isVisible = true;

            UpdateLayoutLabel();
        }

        /// <summary>
        /// Toggles the panel visibility.
        /// </summary>
        public void TogglePanel()
        {
            if (isVisible)
                StartFade(false);
            else
                StartFade(true);
        }

        private void StartFade(bool fadeIn)
        {
            if (activeFade != null)
                StopCoroutine(activeFade);

            activeFade = StartCoroutine(fadeIn ? FadeIn() : FadeOut());
        }

        private IEnumerator FadeIn()
        {
            isVisible = true;
            gameObject.SetActive(true);

            if (panelGroup != null)
            {
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
            }

            activeFade = null;
        }

        private IEnumerator FadeOut()
        {
            isVisible = false;

            if (panelGroup != null)
            {
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
            }

            gameObject.SetActive(false);
            activeFade = null;
        }

        /// <summary>
        /// Called when a layout button is pressed. Applies the layout and closes the panel.
        /// </summary>
        public void OnLayoutButtonPressed(int index)
        {
            if (layoutManager == null) return;

            LayoutType selectedLayout = (LayoutType)index;
            layoutManager.ApplyLayout(selectedLayout);

            if (currentLayoutLabel != null)
            {
                string labelName = (index >= 0 && index < LayoutNames.Length) ? LayoutNames[index] : selectedLayout.ToString();
                currentLayoutLabel.text = $"Layout: {labelName}";
            }

            StartFade(false);
        }

        private void UpdateLayoutLabel()
        {
            if (currentLayoutLabel != null && layoutManager != null)
            {
                int idx = (int)layoutManager.GetCurrentLayout();
                string labelName = (idx >= 0 && idx < LayoutNames.Length) ? LayoutNames[idx] : layoutManager.GetCurrentLayout().ToString();
                currentLayoutLabel.text = $"Layout: {labelName}";
            }
        }
    }
}
