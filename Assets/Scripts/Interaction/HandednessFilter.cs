using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Interactor filter for Interaction SDK interactables: lets only interactors that belong to the
    /// allowed hand through. The wrist menu sits on the left palm, so its buttons only accept the right
    /// hand (poke or ray); otherwise curling the left fingers would press them by accident.
    /// </summary>
    public class HandednessFilter : MonoBehaviour, IGameObjectFilter
    {
        [SerializeField] private Handedness allowed = Handedness.Right;

        public Handedness Allowed { get => allowed; set => allowed = value; }

        public bool Filter(GameObject interactor)
        {
            if (interactor == null) return false;
            var handRef = interactor.GetComponentInParent<HandRef>();
            if (handRef != null) return Safe(() => handRef.Handedness);
            var hand = interactor.GetComponentInParent<Hand>();
            if (hand != null) return Safe(() => hand.Handedness);
            var controllerRef = interactor.GetComponentInParent<ControllerRef>();
            if (controllerRef != null) return Safe(() => controllerRef.Handedness);

            // Fallback: rig naming ("LeftHand", "HandInteractorsLeft", ...).
            for (Transform t = interactor.transform; t != null; t = t.parent)
                if (t.name.Contains("Left")) return allowed == Handedness.Left;
            return true;
        }

        private bool Safe(System.Func<Handedness> get)
        {
            try { return get() == allowed; }
            catch { return true; }   // hand data not ready yet: don't block
        }
    }
}
