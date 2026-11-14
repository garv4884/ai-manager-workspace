using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Switches between the virtual office and mixed reality. In MR the room shell
    /// (walls, floor, ceiling, decor) is hidden and the camera clears to transparent so the
    /// Quest passthrough feed shows through; the platform, desks and agents stay.
    /// Passthrough itself comes from AR Foundation's ARCameraManager with the
    /// Meta OpenXR "Camera (Passthrough)" feature enabled.
    /// </summary>
    public class PassthroughToggle : MonoBehaviour
    {
        [SerializeField] private GameObject[] hideInMixedReality;
        [SerializeField] private Camera xrCamera;

        private CameraClearFlags originalFlags;
        private Color originalColor;
        private ARCameraManager arCamera;

        public bool IsMixedReality { get; private set; }

        private void Start()
        {
            if (xrCamera == null) xrCamera = Camera.main;
            if (xrCamera != null)
            {
                originalFlags = xrCamera.clearFlags;
                originalColor = xrCamera.backgroundColor;
                arCamera = xrCamera.GetComponent<ARCameraManager>();
                if (arCamera != null) arCamera.enabled = false;
            }
        }

        public void Toggle() => SetMixedReality(!IsMixedReality);

        public void SetMixedReality(bool on)
        {
            IsMixedReality = on;
            foreach (var go in hideInMixedReality) if (go != null) go.SetActive(!on);

            if (xrCamera == null) return;
            if (arCamera != null) arCamera.enabled = on;
            xrCamera.clearFlags = on ? CameraClearFlags.SolidColor : originalFlags;
            xrCamera.backgroundColor = on ? new Color(0f, 0f, 0f, 0f) : originalColor;
        }
    }
}
