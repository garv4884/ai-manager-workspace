using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Hands first. In Hands mode (the default) the controller rays, pokes and controller models of
    /// Meta's interaction rig are switched off, so a picked-up controller can't press anything. If
    /// controllers are in use, a calm notice floats in view: put them down (hand tracking takes over),
    /// or hold both triggers for 2 seconds / use Settings > Hands & Input to switch to Controller mode.
    /// </summary>
    public class InputModeManager : MonoBehaviour
    {
        [SerializeField] private Transform rigRoot;
        [SerializeField] private GameObject notice;
        [SerializeField] private Image holdFill;
        [SerializeField] private float holdSeconds = 2f;
        [SerializeField] private float noticeDistance = 0.9f;

        private static readonly string[] ControllerInteractorGroups = { "Controller", "Controller and No Hand", "Controller and Hand" };
        private float held;
        private Transform head;

        public bool ControllersInUse { get; private set; }
        public static InputModeManager Instance { get; private set; }

        private void Awake() => Instance = this;

        private void Start()
        {
            // Meta's rays stop at 5 m; agents sit 7-10 m away, so reach further.
            foreach (var ray in FindObjectsByType<Oculus.Interaction.RayInteractor>(FindObjectsInactive.Include))
                ray.MaxRayLength = 25f;
            AppSettings.Changed += Apply;
            Apply();
            if (notice != null)
            {
                var c = notice.GetComponentInChildren<Canvas>(true);
                if (c != null) c.sortingOrder = 50;   // always on top
                notice.SetActive(false);
            }
        }

        private void OnDestroy() => AppSettings.Changed -= Apply;

        public void SetMode(InputMode mode)
        {
            AppSettings.Current.inputMode = mode;
            AppSettings.Save();
        }

        private GameObject teleportArea, board;

        private void Apply()
        {
            // Teleport targets and the wall board (Settings > Comfort & View).
            if (teleportArea == null) teleportArea = GameObject.Find("TeleportArea");
            if (teleportArea != null) teleportArea.SetActive(AppSettings.Current.teleport);
            if (board == null)
            {
                var dash = FindAnyObjectByType<SessionDashboardUI>(FindObjectsInactive.Include);
                if (dash != null) board = dash.transform.root.gameObject;
            }
            if (board != null) board.SetActive(AppSettings.Current.showBoard);

            bool controllers = AppSettings.Current.inputMode == InputMode.Controllers;
            if (rigRoot == null) return;
            foreach (Transform t in rigRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("OVRControllerVisual")) t.gameObject.SetActive(controllers);
                else if (t.parent != null && t.parent.name == "Interactors" && System.Array.IndexOf(ControllerInteractorGroups, t.name) >= 0)
                    t.gameObject.SetActive(controllers);
            }
        }

        private void Update()
        {
            OVRInput.Controller active = OVRInput.GetActiveController();
            ControllersInUse = (active & OVRInput.Controller.Touch) != 0;
            bool show = ControllersInUse && AppSettings.Current.inputMode == InputMode.Hands;

            if (notice != null && notice.activeSelf != show)
            {
                notice.SetActive(show);
                held = 0f;
                if (show) PlaceNotice(true);
            }
            if (!show) return;

            PlaceNotice(false);
            bool both = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch) > 0.8f &&
                        OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) > 0.8f;
            held = both ? held + Time.deltaTime : 0f;
            if (holdFill != null) holdFill.fillAmount = Mathf.Clamp01(held / holdSeconds);
            if (held >= holdSeconds) SetMode(InputMode.Controllers);
        }

        /// <summary>Keeps the notice softly in front of the user's eyes.</summary>
        private void PlaceNotice(bool snap)
        {
            if (head == null) { if (Camera.main == null) return; head = Camera.main.transform; }
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 target = head.position + fwd * noticeDistance + Vector3.down * 0.08f;
            Transform t = notice.transform;
            float k = snap ? 1f : 1f - Mathf.Exp(-Time.deltaTime * 3f);
            t.position = Vector3.Lerp(t.position, target, k);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(t.position - head.position), k);
        }
    }
}
