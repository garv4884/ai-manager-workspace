using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace AIManagerWorkspace
{
    /// <summary>Point at a 3D object with a hand or controller ray and pinch / pull the trigger to click it.</summary>
    [RequireComponent(typeof(RayInteractable))]
    public class RayClickTarget : MonoBehaviour
    {
        [SerializeField] private UnityEvent onClick = new UnityEvent();
        [SerializeField] private Transform highlight;
        [SerializeField] private float hoverScale = 1.06f;

        private RayInteractable interactable;
        private Vector3 baseScale;

        public UnityEvent OnClick => onClick;

        private void Awake()
        {
            interactable = GetComponent<RayInteractable>();
            if (highlight != null) baseScale = highlight.localScale;
        }

        private void OnEnable() => interactable.WhenPointerEventRaised += OnPointer;
        private void OnDisable() => interactable.WhenPointerEventRaised -= OnPointer;

        private void OnPointer(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Hover: SetHover(true); break;
                case PointerEventType.Unhover: SetHover(false); break;
                case PointerEventType.Unselect: onClick.Invoke(); break;
            }
        }

        private void SetHover(bool on)
        {
            if (highlight != null) highlight.localScale = on ? baseScale * hoverScale : baseScale;
        }
    }
}
