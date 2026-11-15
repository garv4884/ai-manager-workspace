using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Text display plus poke keyboard lying on the manager's desk. Used directly in the Editor
    /// and as the fallback on device; when the Meta system keyboard is in use it only shows the
    /// prompt and a live preview of what is being typed. In the Editor the PC keyboard also types
    /// into it (Enter sends, Esc cancels).
    /// </summary>
    public class VirtualKeyboardManager : MonoBehaviour
    {
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private GameObject keysRoot;
        [SerializeField] private Button useDeskButton;
        [SerializeField] private TextInputService service;
        [SerializeField] private TextMeshProUGUI promptText;
        [SerializeField] private TextMeshProUGUI inputText;
        [SerializeField] private List<VirtualKeyboardButton> keys = new List<VirtualKeyboardButton>();
        [SerializeField] private ReviewDesk desk;
        [SerializeField] private int maxLength = 400;

        private string text = "";
        private bool shift = true;
        private bool allowEmpty;
        private Action<string> onSubmit;
        private Func<bool> stillValid;
        private object owner;
        private bool displayOnly;

        public bool IsOpen => visualRoot != null && visualRoot.activeSelf && !displayOnly;
        public bool IsOpenFor(object o) => IsOpen && ReferenceEquals(owner, o);

        private void Awake()
        {
            foreach (var k in keys) if (k != null) k.Bind(this);
            if (useDeskButton != null) useDeskButton.onClick.AddListener(() => service.SwitchToDesk());
            if (visualRoot != null) visualRoot.SetActive(false);
        }

        private bool masked;

        public void Open(string prompt, object requestOwner, bool allowEmptyText, Action<string> submit, Func<bool> valid = null,
            bool secret = false)
        {
            masked = secret;
            owner = requestOwner;
            allowEmpty = allowEmptyText;
            onSubmit = submit;
            stillValid = valid;
            displayOnly = false;
            text = "";
            shift = true;
            Show(prompt, true);
            RefreshKeys();
            RefreshText();

            if (Keyboard.current != null)
            {
                Keyboard.current.onTextInput -= OnHardwareText;
                Keyboard.current.onTextInput += OnHardwareText;
            }
        }

        /// <summary>Prompt and preview only (the Meta system keyboard is doing the typing).</summary>
        public void ShowDisplayOnly(string prompt, bool secret = false)
        {
            masked = secret;
            displayOnly = true;
            text = "";
            Show(prompt, false);
            RefreshText();
        }

        public void SetPreview(string value)
        {
            if (!displayOnly || value == text) return;
            text = value ?? "";
            RefreshText();
        }

        private void Show(string prompt, bool withKeys)
        {
            if (promptText != null) promptText.text = prompt;
            if (keysRoot != null) keysRoot.SetActive(withKeys);
            if (useDeskButton != null) useDeskButton.gameObject.SetActive(!withKeys);
            if (visualRoot != null) visualRoot.SetActive(true);
        }

        public void Close()
        {
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnHardwareText;
            if (visualRoot != null) visualRoot.SetActive(false);
            displayOnly = false;
            onSubmit = null;
            stillValid = null;
            owner = null;
        }

        private void OnDisable()
        {
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnHardwareText;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (stillValid != null && !stillValid()) { Close(); return; }

            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Submit();
            else if (kb.escapeKey.wasPressedThisFrame) Close();
        }

        private void OnHardwareText(char c)
        {
            if (!IsOpen) return;
            if (c == '\b') { Backspace(); return; }
            if (char.IsControl(c)) return;
            Append(c.ToString(), true);
        }

        // ── Key actions ───────────────────────────────────────────────────────

        public void Type(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            Append(shift ? key.ToUpperInvariant() : key.ToLowerInvariant(), false);
            if (shift) { shift = false; RefreshKeys(); }
        }

        public void Space() => Append(" ", false);

        public void Backspace()
        {
            if (text.Length == 0) return;
            text = text.Substring(0, text.Length - 1);
            Click();
            RefreshText();
        }

        public void ToggleShift()
        {
            shift = !shift;
            Click();
            RefreshKeys();
        }

        public void Submit()
        {
            if (!allowEmpty && string.IsNullOrWhiteSpace(text)) return;
            Action<string> cb = onSubmit;
            string value = text;
            Close();
            cb?.Invoke(value);
        }

        public void Cancel()
        {
            if (displayOnly) service.Close();
            else Close();
        }

        private void Append(string s, bool fromHardware)
        {
            if (text.Length + s.Length > maxLength) return;
            text += s;
            if (!fromHardware) Click();
            RefreshText();
        }

        private void Click()
        {
            if (desk != null) desk.Play(ReviewDesk.Sfx.Key);
        }

        private void RefreshText()
        {
            if (inputText == null) return;
            inputText.text = text.Length == 0
                ? "<color=#8A93A3>" + (displayOnly ? "Type on the Meta keyboard..." : "Type here...") + "</color><color=#4FD1C5>|</color>"
                : $"{(masked ? new string('*', Mathf.Min(text.Length, 40)) : text)}<color=#4FD1C5>|</color>";
        }

        private void RefreshKeys()
        {
            foreach (var k in keys) if (k != null) k.RefreshLabel(shift);
        }
    }
}
