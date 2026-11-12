using UnityEngine;
using UnityEngine.Events;
using TMPro;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Manages a world-space virtual keyboard for text input inside VR.
    /// Attach to the keyboard root GameObject and wire up key buttons in the Inspector.
    /// </summary>
    public class VirtualKeyboardManager : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("UI References")]
        [SerializeField] private Canvas          keyboardCanvas;
        [SerializeField] private TextMeshProUGUI previewText;
        [SerializeField] private TMP_InputField  hiddenInputField;

        [Header("Positioning")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, -0.3f, 0.5f);

        // ── Events ────────────────────────────────────────────────────────────
        [Header("Events")]
        public UnityEvent<string> OnTextSubmitted;
        public UnityEvent<string> OnTextChanged;

        // ── Private state ─────────────────────────────────────────────────────
        private string          currentText  = "";
        private AgentController targetAgent;

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            // Ensure keyboard is hidden at start
            if (keyboardCanvas != null)
                keyboardCanvas.gameObject.SetActive(false);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Position the keyboard in front of the camera and show it.
        /// </summary>
        /// <param name="agent">The agent awaiting input.</param>
        /// <param name="placeholder">Optional placeholder text shown before the user types.</param>
        public void ShowKeyboard(AgentController agent, string placeholder = "")
        {
            targetAgent  = agent;
            currentText  = "";

            // Position canvas relative to main camera
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 worldOffset = cam.transform.TransformDirection(spawnOffset);
                keyboardCanvas.transform.position = cam.transform.position + worldOffset;
                keyboardCanvas.transform.rotation = Quaternion.LookRotation(
                    keyboardCanvas.transform.position - cam.transform.position);
            }

            // Update UI
            if (previewText != null)
                previewText.text = string.IsNullOrEmpty(placeholder) ? "" : placeholder;

            if (hiddenInputField != null)
            {
                hiddenInputField.text = "";
                hiddenInputField.placeholder.GetComponent<TextMeshProUGUI>().text = placeholder;
            }

            if (keyboardCanvas != null)
                keyboardCanvas.gameObject.SetActive(true);
        }

        /// <summary>Hides the keyboard canvas.</summary>
        public void HideKeyboard()
        {
            if (keyboardCanvas != null)
                keyboardCanvas.gameObject.SetActive(false);

            targetAgent = null;
        }

        /// <summary>Called by each key button with its character string.</summary>
        public void OnKeyPressed(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            currentText += key;
            RefreshPreview();
            OnTextChanged?.Invoke(currentText);
        }

        /// <summary>Removes the last character from the current text.</summary>
        public void OnBackspace()
        {
            if (currentText.Length == 0) return;

            currentText = currentText.Substring(0, currentText.Length - 1);
            RefreshPreview();
            OnTextChanged?.Invoke(currentText);
        }

        /// <summary>Submits the current text to the target agent and hides the keyboard.</summary>
        public void OnSubmit()
        {
            string submittedText = currentText;

            OnTextSubmitted?.Invoke(submittedText);

            if (targetAgent != null)
                targetAgent.HandleInput(submittedText);

            HideKeyboard();
        }

        /// <summary>Cancels input without submitting and hides the keyboard.</summary>
        public void OnCancel()
        {
            HideKeyboard();
        }

        /// <summary>Returns the text currently typed by the user.</summary>
        public string GetCurrentText() => currentText;

        // ── Private helpers ───────────────────────────────────────────────────

        private void RefreshPreview()
        {
            if (previewText != null)
                previewText.text = currentText;

            if (hiddenInputField != null)
                hiddenInputField.text = currentText;
        }
    }
}
