using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Point at an agent (or its desk) and pinch:
    ///   - release quickly        -> select the agent (opens its detail card)
    ///   - pinch and sweep the ray -> slide the whole workstation across the floor;
    ///     the seated agent rides along, and the desk turns to face the manager.
    /// Works from the elevated desk with the hand ray, the usual way to reach far objects.
    /// </summary>
    public class FloorDragInteractable : XRBaseInteractable
    {
        [SerializeField] private AgentController agent;
        [SerializeField] private Workstation workstation;
        [SerializeField] private float dragThreshold = 0.35f;
        [SerializeField] private float followSharpness = 14f;

        private IXRSelectInteractor activeInteractor;
        private Vector3 startPoint;
        private Vector3 grabOffset;
        private bool dragging;

        private Workstation Target => workstation != null ? workstation : (agent != null ? agent.Workstation : null);
        private AgentController Owner => agent != null ? agent : (workstation != null ? workstation.Owner : null);

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            activeInteractor = args.interactorObject;
            dragging = false;
            if (TryFloorPoint(out Vector3 p) && Target != null)
            {
                startPoint = p;
                grabOffset = Target.transform.position - p;
                grabOffset.y = 0f;
            }
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (!dragging) GameManager.Instance.Agents.Select(Owner);
            else GameManager.Instance.Layout.MarkCustom();
            activeInteractor = null;
            dragging = false;
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || activeInteractor == null) return;
            Workstation ws = Target;
            if (ws == null || !TryFloorPoint(out Vector3 p)) return;

            if (!dragging)
            {
                Vector3 d = p - startPoint; d.y = 0f;
                if (d.magnitude < dragThreshold) return;
                dragging = true;
            }

            WorkspaceLayoutManager layout = GameManager.Instance.Layout;
            Vector3 goal = layout.ClampPosition(p + grabOffset);
            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            Vector3 pos = Vector3.Lerp(ws.transform.position, goal, k);
            ws.transform.SetPositionAndRotation(pos,
                Quaternion.Slerp(ws.transform.rotation, layout.FacingFor(pos), k));
        }

        private bool TryFloorPoint(out Vector3 point)
        {
            point = default;
            if (activeInteractor == null) return false;

            Transform origin = activeInteractor is IXRRayProvider ray
                ? ray.GetOrCreateRayOrigin()
                : activeInteractor.transform;
            if (origin == null) return false;

            Vector3 o = origin.position;
            Vector3 dir = origin.forward;
            if (dir.y > -0.02f) return false;          // pointing at or above the horizon
            float t = -o.y / dir.y;
            if (t > 40f) return false;
            point = o + dir * t;
            return true;
        }
    }
}
