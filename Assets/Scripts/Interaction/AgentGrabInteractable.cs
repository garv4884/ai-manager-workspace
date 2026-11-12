using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.Interaction.Toolkit;

namespace AIManagerWorkspace
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class AgentGrabInteractable : XRGrabInteractable
    {
        [SerializeField] private AgentController agentController;
        [SerializeField] private AgentManager agentManager;
        [SerializeField] private float snapRadius = 1.5f;
        [SerializeField] private Material highlightMaterial;
        [SerializeField] private Material defaultMaterial;
        [SerializeField] private MeshRenderer agentRenderer;

        private NavMeshAgent navAgent;
        private Vector3 originalPosition;
        private Quaternion originalRotation;

        protected override void Awake()
        {
            base.Awake();
            navAgent = GetComponent<NavMeshAgent>();
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);

            // Disable nav agent so physics/XR can move the object freely
            if (navAgent != null)
            {
                navAgent.enabled = false;
            }

            // Store original transform before grab
            originalPosition = transform.position;
            originalRotation = transform.rotation;

            // Apply highlight material to show grabbed state
            if (agentRenderer != null && highlightMaterial != null)
            {
                agentRenderer.material = highlightMaterial;
            }

            Debug.Log($"[AgentGrabInteractable] Agent {gameObject.name} grabbed.");
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);

            // Restore default material
            if (agentRenderer != null && defaultMaterial != null)
            {
                agentRenderer.material = defaultMaterial;
            }

            // Find nearest desk via AgentManager and snap, or return to original position
            Vector3 releasePosition = transform.position;
            Transform nearestDesk = agentManager != null
                ? agentManager.GetNearestDesk(releasePosition, snapRadius)
                : null;

            Vector3 targetPosition;
            Quaternion targetRotation;

            if (nearestDesk != null)
            {
                targetPosition = nearestDesk.position;
                targetRotation = nearestDesk.rotation;
                Debug.Log($"[AgentGrabInteractable] Agent {gameObject.name} snapped to desk at {targetPosition}.");
            }
            else
            {
                targetPosition = originalPosition;
                targetRotation = originalRotation;
                Debug.Log($"[AgentGrabInteractable] Agent {gameObject.name} returned to original position.");
            }

            // Move to target position
            transform.position = targetPosition;
            transform.rotation = targetRotation;

            // Re-enable NavMeshAgent and warp to new position
            if (navAgent != null)
            {
                navAgent.enabled = true;
                navAgent.Warp(targetPosition);
            }
        }
    }
}
