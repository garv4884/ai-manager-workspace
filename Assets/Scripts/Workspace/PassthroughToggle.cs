using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Toggles between Virtual Office mode (full VR studio walls & floor)
    /// and Mixed Reality Passthrough mode (real room visible with floating desks & agents).
    /// </summary>
    public class PassthroughToggle : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject environmentRoot;
        [SerializeField] private Camera xrCamera;

        [Header("State")]
        [SerializeField] private bool isPassthroughActive = false;

        private CameraClearFlags originalClearFlags = CameraClearFlags.Skybox;
        private Color originalBackgroundColor = Color.black;

        private void Start()
        {
            if (xrCamera == null)
                xrCamera = Camera.main;

            if (xrCamera != null)
            {
                originalClearFlags = xrCamera.clearFlags;
                originalBackgroundColor = xrCamera.backgroundColor;
            }
        }

        /// <summary>
        /// Toggles between VR office and Mixed Reality Passthrough.
        /// </summary>
        public void ToggleMode()
        {
            SetPassthrough(!isPassthroughActive);
        }

        public void SetPassthrough(bool active)
        {
            isPassthroughActive = active;

            if (xrCamera == null)
                xrCamera = Camera.main;

            if (xrCamera != null)
            {
                if (isPassthroughActive)
                {
                    // Clear with solid transparent black for Quest Passthrough underlay
                    xrCamera.clearFlags = CameraClearFlags.SolidColor;
                    xrCamera.backgroundColor = new Color(0, 0, 0, 0);
                }
                else
                {
                    xrCamera.clearFlags = originalClearFlags;
                    xrCamera.backgroundColor = originalBackgroundColor;
                }
            }

            // Hide/unhide room walls (keep desks, platform, and queue visible in both modes)
            if (environmentRoot != null)
            {
                var wallBack = environmentRoot.transform.Find("WallBack");
                var wallLeft = environmentRoot.transform.Find("WallLeft");
                var wallRight = environmentRoot.transform.Find("WallRight");

                if (wallBack != null) wallBack.gameObject.SetActive(!isPassthroughActive);
                if (wallLeft != null) wallLeft.gameObject.SetActive(!isPassthroughActive);
                if (wallRight != null) wallRight.gameObject.SetActive(!isPassthroughActive);
            }

            // Also synchronize with Meta OVRPassthroughLayer if present in scene
            try
            {
                var allMBs = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
                foreach (var mb in allMBs)
                {
                    if (mb != null && mb.GetType().Name == "OVRPassthroughLayer")
                    {
                        var hiddenProp = mb.GetType().GetProperty("hidden");
                        if (hiddenProp != null && hiddenProp.CanWrite)
                        {
                            hiddenProp.SetValue(mb, !isPassthroughActive);
                        }
                        else
                        {
                            ((Behaviour)mb).enabled = isPassthroughActive;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PassthroughToggle] Optional OVRPassthroughLayer sync notice: {ex.Message}");
            }

            Debug.Log($"[PassthroughToggle] Mode switched to: {(isPassthroughActive ? "Mixed Reality (Passthrough)" : "Virtual Studio")}");
        }
    }
}
