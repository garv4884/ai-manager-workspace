using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>Soft click on every panel button (keyboard keys have their own tick), like Horizon OS UI.</summary>
    public class UiFeedback : MonoBehaviour
    {
        [SerializeField] private ReviewDesk desk;

        private void Start()
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include))
            {
                if (b.GetComponent<VirtualKeyboardButton>() != null) continue;
                b.onClick.AddListener(() => desk.Play(ReviewDesk.Sfx.Click));
            }
        }
    }
}
