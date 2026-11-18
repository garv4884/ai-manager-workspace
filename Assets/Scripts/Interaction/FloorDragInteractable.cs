using Oculus.Interaction;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Meta Interaction SDK ray target on an agent or a desk. Point (hand ray or controller ray)
    /// and pinch / trigger:
    ///   - quick release            -> select the agent (its card opens on the desk)
    ///   - hold and sweep the ray   -> slide the whole workstation along the floor; the seated
    ///                                 agent rides along and the desk turns to face the manager.
    /// Requires a <see cref="RayInteractable"/> (with a ColliderSurface) on the same object.
    /// </summary>
    [RequireComponent(typeof(RayInteractable))]
    public class FloorDragInteractable : MonoBehaviour
    {
        [SerializeField] private AgentController agent;
        [SerializeField] private Workstation workstation;
        [SerializeField] private float dragThreshold = 0.35f;
        [SerializeField] private float followSharpness = 14f;

        private RayInteractable interactable;
        private RayInteractor activeInteractor;
        private Vector3 startPoint;
        private Vector3 grabOffset;
        private bool dragging;

        private Workstation Target => workstation != null ? workstation : (agent != null ? agent.Workstation : null);
        private AgentController Owner => agent != null ? agent : (workstation != null ? workstation.Owner : null);

        private void Awake() => interactable = GetComponent<RayInteractable>();
        private void OnEnable() => interactable.WhenPointerEventRaised += OnPointer;
        private void OnDisable() => interactable.WhenPointerEventRaised -= OnPointer;

        private void OnPointer(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Hover:
                    if (Owner != null) Owner.SetHovered(true);
                    break;
                case PointerEventType.Unhover:
                    if (Owner != null) Owner.SetHovered(false);
                    break;
                case PointerEventType.Select:
                    activeInteractor = FindInteractor(evt.Identifier);
                    dragging = false;
                    if (activeInteractor != null && Target != null && TryFloorPoint(out Vector3 p))
                    {
                        startPoint = p;
                        grabOffset = Target.transform.position - p;
                        grabOffset.y = 0f;
                    }
                    break;
                case PointerEventType.Unselect:
                    if (!dragging) GameManager.Instance.Agents.Select(Owner);
                    else GameManager.Instance.Layout.MarkCustom();
                    activeInteractor = null;
                    dragging = false;
                    break;
                case PointerEventType.Cancel:
                    activeInteractor = null;
                    dragging = false;
                    break;
            }
        }

        private void Update()
        {
            Workstation ws = Target;
            if (activeInteractor == null || ws == null || !TryFloorPoint(out Vector3 p)) return;
            // Pinching an agent that is away from its desk (in line, presenting, walking) only selects it.
            if (agent != null && !agent.IsSeated) return;

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
            ws.transform.SetPositionAndRotation(pos, Quaternion.Slerp(ws.transform.rotation, layout.FacingFor(pos), k));
        }

        private bool TryFloorPoint(out Vector3 point)
        {
            point = default;
            Ray ray = activeInteractor.Ray;
            if (ray.direction.y > -0.02f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t > 40f) return false;
            point = ray.origin + ray.direction * t;
            return true;
        }

        private static RayInteractor FindInteractor(int id)
        {
            foreach (var r in FindObjectsByType<RayInteractor>(FindObjectsInactive.Exclude))
                if (r.Identifier == id) return r;
            return null;
        }
    }
}
