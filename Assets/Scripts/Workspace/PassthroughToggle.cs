using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Switches between the virtual office and mixed reality using the Meta Passthrough
    /// building block (OVRPassthroughLayer). In MR the room shell is hidden and the camera clears
    /// to transparent so your real room shows; the platform, desks and agents stay.
    /// </summary>
    public class PassthroughToggle : MonoBehaviour
    {
        [SerializeField] private GameObject[] hideInMixedReality;

        private OVRPassthroughLayer layer;
        private Camera cam;
        private CameraClearFlags originalFlags;
        private Color originalColor;

        public bool IsMixedReality { get; private set; }

        private void Start()
        {
            layer = FindAnyObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
            cam = Camera.main;
            if (cam != null) { originalFlags = cam.clearFlags; originalColor = cam.backgroundColor; }
            IsMixedReality = AppSettings.Current.mixedReality;
            Apply();
        }

        public void Toggle() => Set(!IsMixedReality);

        public void Set(bool mixedReality)
        {
            IsMixedReality = mixedReality;
            Apply();
            if (AppSettings.Current.mixedReality != mixedReality)
            {
                AppSettings.Current.mixedReality = mixedReality;
                AppSettings.Save();
            }
            Changed?.Invoke();
        }

        public event System.Action Changed;

        private void Apply()
        {
            foreach (var go in hideInMixedReality) if (go != null) go.SetActive(!IsMixedReality);
            if (layer != null)
            {
                layer.gameObject.SetActive(true);
                layer.enabled = IsMixedReality;
                layer.hidden = !IsMixedReality;
            }
            if (cam == null) cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = IsMixedReality ? CameraClearFlags.SolidColor : originalFlags;
                cam.backgroundColor = IsMixedReality ? new Color(0f, 0f, 0f, 0f) : originalColor;
            }
        }
    }
}
