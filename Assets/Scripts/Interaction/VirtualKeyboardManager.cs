using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Poke keyboard lying on the manager's desk. One keyboard serves every text request
    /// (answer a question, give feedback, assign a task); each caller passes its own
    /// submit callback and a validity check that closes the keyboard if the request expires.
    /// In the Editor, the PC keyboard also types into it (Enter sends, Esc cancels).
    /// </summary>
    public class VirtualKeyboardManager : MonoBehaviour
    {
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private TextMeshProUGUI promptText;
        [SerializeField] private TextMeshProUGUI inputText;
        [SerializeField] private List<VirtualKeyboardButton> keys = new List<VirtualKeyboardButton>();
        [SerializeField] private ReviewDesk desk;
        [SerializeField] private int maxLength = 280;

        private string text = "";
        private bool shift = true;
        private bool allowEmpty;
        private Action<string> onSubmit;
        private Func<bool> stillValid;
        private object owner;

        public bool IsOpen => visualRoot != null && visualRoot.activeSelf;
        public bool IsOpenFor(object o) => IsOpen && ReferenceEquals(owner, o);

        private void Awake()
        {
            foreach (var k in keys) if (k != null) k.Bind(this);
            if (visualRoot != null) visualRoot.SetActive(false);
        }

        public void Open(string prompt, object requestOwner, bool allowEmpty, Action<string> onSubmit, Func<bool> stillValid = null)
        {
            owner = requestOwner;
            this.allowEmpty = allowEmpty;
            this.onSubmit = onSubmit;
            this.stillValid = stillValid;
            text = "";
            shift = true;
            if (promptText != null) promptText.text = prompt;
            if (visualRoot != null) visualRoot.SetActive(true);
            RefreshKeys();
            RefreshText();

            if (Keyboard.current != null)
            {
                Keyboard.current.onTextInput -= OnHardwareText;
                Keyboard.current.onTextInput += OnHardwareText;
            }
        }

        public void Close()
        {
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnHardwareText;
            if (visualRoot != null) visualRoot.SetActive(false);
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
            if (c == '\b') { Backspace(); return; }
            if (char.IsControl(c)) return;
            Append(c.ToString(), fromHardware: true);
        }

        // ── Key actions ───────────────────────────────────────────────────────

        public void Type(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            Append(shift ? key.ToUpperInvariant() : key.ToLowerInvariant(), fromHardware: false);
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
            Action<string> callback = onSubmit;
            string value = text;
            Close();
            callback?.Invoke(value);
        }

        public void Cancel() => Close();

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
                ? "<color=#8A93A3>Type here...</color><color=#4FD1C5>|</color>"
                : $"{text}<color=#4FD1C5>|</color>";
        }

        private void RefreshKeys()
        {
            foreach (var k in keys) if (k != null) k.RefreshLabel(shift);
        }
    }
}
