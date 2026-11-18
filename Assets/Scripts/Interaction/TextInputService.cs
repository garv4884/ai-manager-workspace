using System;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// One place for every text request (answers, feedback, tasks, team briefs, roles, API key).
    ///
    /// The poke keyboard on the desk is the default: it stays inside the app, so hand tracking and
    /// review gestures keep working while you type (thumbs up sends, palm closes).
    ///
    /// The Meta system keyboard (with voice dictation) is one tap away on the desk keyboard. It is an
    /// OS overlay: while it is open, Horizon OS takes hand input away from the app, so gestures pause.
    /// Closing it, or tapping "Done" with nothing typed, simply cancels; there is no re-open loop.
    ///
    /// Note: Meta's experimental Surface Keyboard currently only works in the Horizon OS home
    /// space; there is no app API for it yet. When Meta exposes it through the system keyboard,
    /// the META KEYBOARD button picks it up with no code change.
    /// </summary>
    public class TextInputService : MonoBehaviour
    {
        [SerializeField] private VirtualKeyboardManager deskKeyboard;

        private TouchScreenKeyboard system;
        private Action<string> onSubmit;
        private Func<bool> stillValid;
        private bool allowEmpty;
        private object owner;
        private string prompt;
        private bool usingSystem;
        private bool secret;

        public bool IsOpen => usingSystem ? system != null : deskKeyboard.IsOpen;
        public bool IsOpenFor(object o) => IsOpen && ReferenceEquals(owner, o);
        public bool UsingSystemKeyboard => usingSystem;
        public string CurrentText => usingSystem ? (system != null ? system.text : "") : deskKeyboard.Text;

        public static bool SystemKeyboardAvailable =>
            !Application.isEditor && Application.platform == RuntimePlatform.Android && TouchScreenKeyboard.isSupported;

        /// <param name="secretText">Mask the text (API keys): secure system keyboard, stars on the desk display.</param>
        public void Open(string promptText, object requestOwner, bool allowEmptyText, Action<string> submit, Func<bool> valid = null,
            bool secretText = false)
        {
            Close();
            secret = secretText;
            owner = requestOwner;
            prompt = promptText;
            allowEmpty = allowEmptyText;
            onSubmit = submit;
            stillValid = valid;
            OpenDesk("");
        }

        /// <summary>Desk "META KEYBOARD" button: hand the current request to the system keyboard (voice dictation included).</summary>
        public void SwitchToSystem()
        {
            if (usingSystem || !SystemKeyboardAvailable || !deskKeyboard.IsOpen) return;
            string typed = deskKeyboard.Text;
            system = TouchScreenKeyboard.Open(typed, TouchScreenKeyboardType.Default, !secret, false, secret, false, prompt);
            if (system == null) return;
            usingSystem = true;
            deskKeyboard.ShowDisplayOnly(prompt, secret);
            deskKeyboard.SetPreview(typed);
        }

        /// <summary>Desk "TYPE ON THE DESK INSTEAD" button: drop the system overlay, keep what was typed.</summary>
        public void SwitchToDesk()
        {
            if (!usingSystem) return;
            string typed = system != null ? system.text : "";
            if (system != null) { system.active = false; system = null; }
            OpenDesk(typed);
        }

        private void OpenDesk(string initial)
        {
            usingSystem = false;
            system = null;
            deskKeyboard.Open(prompt, owner, allowEmpty, Finish, stillValid, secret, initial);
        }

        /// <summary>Sends what has been typed so far (thumbs up while typing). False if empty and empty isn't allowed.</summary>
        public bool SubmitCurrent()
        {
            if (!IsOpen) return false;
            string text = CurrentText ?? "";
            if (!allowEmpty && string.IsNullOrWhiteSpace(text)) return false;
            Finish(text);
            return true;
        }

        public void Close()
        {
            if (system != null) { system.active = false; system = null; }
            usingSystem = false;
            deskKeyboard.Close();
            onSubmit = null;
            stillValid = null;
            owner = null;
        }

        private void Finish(string text)
        {
            Action<string> cb = onSubmit;
            Close();
            cb?.Invoke(text);
        }

        private void Update()
        {
            if (!usingSystem || system == null) return;
            if (stillValid != null && !stillValid()) { Close(); return; }

            deskKeyboard.SetPreview(system.text);
            switch (system.status)
            {
                case TouchScreenKeyboard.Status.Done:
                    if (!allowEmpty && string.IsNullOrWhiteSpace(system.text)) Close();   // closed without typing: cancel
                    else Finish(system.text);
                    break;
                case TouchScreenKeyboard.Status.Canceled:
                    Close();
                    break;
                case TouchScreenKeyboard.Status.LostFocus:
                    SwitchToDesk();   // user went back to the app: carry on with the desk keyboard
                    break;
            }
        }
    }
}
