using UnityEngine;
using UnityEngine.Events;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Connects hand gestures from HandGestureRecognizer to gameplay systems
    /// (QueueManager, AgentManager, OutputPageRenderer, audio/VFX feedback).
    /// </summary>
    [RequireComponent(typeof(HandGestureRecognizer))]
    public class GestureInteractionManager : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Gesture Source")]
        [SerializeField] private HandGestureRecognizer gestureRecognizer;

        [Header("Gameplay References")]
        [SerializeField] private QueueManager         queueManager;
        [SerializeField] private AgentManager         agentManager;
        [SerializeField] private OutputPageRenderer   outputPagePrefab;

        [Header("Approval Feedback")]
        [SerializeField] private AudioSource   approvalSound;
        [SerializeField] private ParticleSystem approvalVFX;

        [Header("Rejection Feedback")]
        [SerializeField] private AudioSource   rejectionSound;
        [SerializeField] private ParticleSystem rejectionVFX;

        [Header("Raycast")]
        [SerializeField] private LayerMask agentLayerMask = ~0;

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (gestureRecognizer == null)
                gestureRecognizer = GetComponent<HandGestureRecognizer>();
        }

        private void OnEnable()
        {
            if (gestureRecognizer == null) return;

            gestureRecognizer.OnThumbsUp.AddListener(HandleThumbsUp);
            gestureRecognizer.OnThumbsDown.AddListener(HandleThumbsDown);
            gestureRecognizer.OnPalmOpen.AddListener(HandlePalmOpen);
            gestureRecognizer.OnPinch.AddListener(HandlePinch);
            gestureRecognizer.OnPointGesture.AddListener(HandlePoint);
        }

        private void OnDisable()
        {
            if (gestureRecognizer == null) return;

            gestureRecognizer.OnThumbsUp.RemoveListener(HandleThumbsUp);
            gestureRecognizer.OnThumbsDown.RemoveListener(HandleThumbsDown);
            gestureRecognizer.OnPalmOpen.RemoveListener(HandlePalmOpen);
            gestureRecognizer.OnPinch.RemoveListener(HandlePinch);
            gestureRecognizer.OnPointGesture.RemoveListener(HandlePoint);
        }

        // ── Gesture Handlers ──────────────────────────────────────────────────

        /// <summary>Thumbs-Up → Approve current presenter.</summary>
        private void HandleThumbsUp()
        {
            AgentController presenter = GetCurrentPresenter();
            if (presenter == null) return;

            presenter.HandleApproval();
            PlayFeedback(approvalSound, approvalVFX);
            queueManager.OnManagerDoneWithPresenter();
        }

        /// <summary>Thumbs-Down → Reject current presenter.</summary>
        private void HandleThumbsDown()
        {
            AgentController presenter = GetCurrentPresenter();
            if (presenter == null) return;

            presenter.HandleRejection();
            PlayFeedback(rejectionSound, rejectionVFX);
            queueManager.OnManagerDoneWithPresenter();
        }

        /// <summary>Palm-Open → Dismiss (same as reject), or log if nobody is presenting.</summary>
        private void HandlePalmOpen()
        {
            AgentController presenter = GetCurrentPresenter();
            if (presenter == null)
            {
                Debug.Log("[GestureInteractionManager] Palm open — no current presenter to dismiss.");
                return;
            }

            presenter.HandleRejection();
            PlayFeedback(rejectionSound, rejectionVFX);
            queueManager.OnManagerDoneWithPresenter();
        }

        /// <summary>Pinch → Raycast from pinch world position; if an agent collider is hit, show its detail panel.</summary>
        private void HandlePinch(Vector3 pinchWorldPos)
        {
            // Cast a small sphere at the pinch point and also try a forward ray from it
            Collider[] hits = Physics.OverlapSphere(pinchWorldPos, 0.05f, agentLayerMask);
            if (hits.Length > 0)
            {
                TryShowAgentDetail(hits[0].gameObject);
                return;
            }

            // Fallback: ray from pinch pos toward camera forward
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = new Ray(pinchWorldPos, cam.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, 10f, agentLayerMask))
            {
                TryShowAgentDetail(hit.collider.gameObject);
            }
        }

        /// <summary>Point gesture — currently reserved for future use.</summary>
        private void HandlePoint()
        {
            Debug.Log("[GestureInteractionManager] Point gesture detected.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private AgentController GetCurrentPresenter()
        {
            if (queueManager == null) return null;
            return queueManager.CurrentPresenter;
        }

        private static void PlayFeedback(AudioSource audio, ParticleSystem vfx)
        {
            if (audio != null && !audio.isPlaying)
                audio.Play();

            if (vfx != null)
            {
                vfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                vfx.Play();
            }
        }

        private void TryShowAgentDetail(GameObject obj)
        {
            if (agentManager == null) return;

            AgentController agent = obj.GetComponentInParent<AgentController>();
            if (agent == null) return;

            // Delegate to AgentManager to open the detail panel
            agentManager.ShowAgentDetailPanel(agent);
        }
    }
}
