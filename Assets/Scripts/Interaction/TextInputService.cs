using System;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// One place for every text request (answers, feedback, tasks, team briefs, roles).
    ///
    /// On Quest it opens the Meta system keyboard (the OS keyboard overlay, which also follows
    /// the user's keyboard preferences). The prompt and the text being typed are mirrored on
    /// the desk display. If the system keyboard is unavailable, or in the Editor, the poke
    /// keyboard lying on the desk is used instead (the PC keyboard also types into it).
    ///
    /// Note: Meta's experimental Surface Keyboard currently only works in the Horizon OS home
    /// space; there is no app API for it yet. When Meta exposes it through the system keyboard,
    /// this service picks it up with no code change.
    /// </summary>
    public class TextInputService : MonoBehaviour
    {
        [SerializeField] private VirtualKeyboardManager deskKeyboard;
        [SerializeField] private bool preferSystemKeyboard = true;

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

        private static bool SystemKeyboardAvailable =>
            !Application.isEditor && Application.platform == RuntimePlatform.Android && TouchScreenKeyboard.isSupported;

        /// <param name="secretText">Mask the text (API keys): secure system keyboard, dots on the desk display.</param>
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

            if (preferSystemKeyboard && SystemKeyboardAvailable)
            {
                system = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default, !secret, false, secret, false, promptText);
                if (system != null)
                {
                    usingSystem = true;
                    deskKeyboard.ShowDisplayOnly(promptText, secret);
                    return;
                }
            }
            OpenDesk();
        }

        /// <summary>Desk "type here instead" button: drop the system overlay and use the desk keyboard.</summary>
        public void SwitchToDesk()
        {
            if (!usingSystem) return;
            if (system != null) { system.active = false; system = null; }
            OpenDesk();
        }

        private void OpenDesk()
        {
            usingSystem = false;
            system = null;
            deskKeyboard.Open(prompt, owner, allowEmpty, Finish, stillValid, secret);
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
                    if (!allowEmpty && string.IsNullOrWhiteSpace(system.text))
                    {
                        system = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default, !secret, false, secret, false, prompt);
                        return;
                    }
                    Finish(system.text);
                    break;
                case TouchScreenKeyboard.Status.Canceled:
                case TouchScreenKeyboard.Status.LostFocus:
                    Close();
                    break;
                default:
                    break;
            }
        }
    }
}
