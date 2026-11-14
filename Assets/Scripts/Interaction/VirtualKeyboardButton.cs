using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    [RequireComponent(typeof(Button))]
    public class VirtualKeyboardButton : MonoBehaviour
    {
        public enum ActionType { Character, Backspace, Submit, Cancel, Space, Shift }

        [SerializeField] private string character;
        [SerializeField] private ActionType action;
        [SerializeField] private TextMeshProUGUI label;

        private VirtualKeyboardManager keyboard;

        public void Bind(VirtualKeyboardManager kb)
        {
            keyboard = kb;
            var button = GetComponent<Button>();
            button.onClick.RemoveListener(Press);
            button.onClick.AddListener(Press);
        }

        public void RefreshLabel(bool shift)
        {
            if (action == ActionType.Character && label != null && character.Length == 1 && char.IsLetter(character[0]))
                label.text = shift ? character.ToUpperInvariant() : character.ToLowerInvariant();
        }

        private void Press()
        {
            if (keyboard == null) return;
            switch (action)
            {
                case ActionType.Character: keyboard.Type(character); break;
                case ActionType.Backspace: keyboard.Backspace(); break;
                case ActionType.Submit: keyboard.Submit(); break;
                case ActionType.Cancel: keyboard.Cancel(); break;
                case ActionType.Space: keyboard.Space(); break;
                case ActionType.Shift: keyboard.ToggleShift(); break;
            }
        }
    }
}
