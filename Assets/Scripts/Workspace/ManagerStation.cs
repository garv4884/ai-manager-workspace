using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// The manager's spot on the platform. Fits the desk to the user's height (seated or
    /// standing) and brings the user back to the desk after teleporting around the office.
    /// </summary>
    public class ManagerStation : MonoBehaviour
    {
        [SerializeField] private Transform deskAssembly;
        [SerializeField] private Transform standPoint;
        [SerializeField] private Transform rigRoot;
        [SerializeField] private float platformTopY = 0.9f;
        [SerializeField] private float deskBelowEyes = 0.5f;
        [SerializeField] private float defaultDeskHeight = 0.78f;
        [SerializeField] private Vector2 deskHeightRange = new Vector2(0.62f, 1.12f);

        private float baseDeskY;
        private float fitted;          // desk height from the last fit (metres above the platform)

        private void Awake()
        {
            baseDeskY = deskAssembly.localPosition.y;
            fitted = defaultDeskHeight;
            AppSettings.Changed += OnSettingsChanged;
        }

        private void OnDestroy()
        {
            AppSettings.Changed -= OnSettingsChanged;
            OVRManager.HMDMounted -= OnMounted;
        }

        private void OnEnable() => OVRManager.HMDMounted += OnMounted;

        // After sleep (headset put down and picked up again) Horizon OS can resume with the floor lost, which
        // drops you to the ground. Re-assert floor tracking, then correct the height and re-fit the desk.
        private void OnMounted() { if (isActiveAndEnabled) StartCoroutine(AfterWake()); }
        private void OnApplicationPause(bool paused) { if (!paused && isActiveAndEnabled) StartCoroutine(AfterWake()); }

        private System.Collections.IEnumerator AfterWake()
        {
            yield return new WaitForSeconds(0.8f);
            if (OVRManager.instance != null) OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            yield return null;
            CorrectHeight(false);
            FitDeskToHead();
        }

        // ── Eye height ──────────────────────────────────────────────────────
        // The view height is one offset on TrackingSpace (so teleports, which move the rig root, keep it),
        // built from three parts:
        //   floorFix  automatic, only when tracking clearly lost the floor (after sleep): never fights you
        //   roomLift  off the platform, lifts a seated / short view to standing height so the team is to scale
        //   nudge     your own HEIGHT +/- from the wrist menu, remembered separately for the desk and the room
        private float floorFix;
        private float roomLift;
        private bool? onPlatform;
        private float nextCheck;

        private const float StandingEyes = 1.62f;
        private const float SeatedEyes = 1.2f;

        /// <summary>True while the rig stands on the raised platform (the manager's desk).</summary>
        public bool OnPlatform => rigRoot == null || rigRoot.position.y > platformTopY * 0.5f;

        private float Nudge => OnPlatform ? AppSettings.Current.eyeNudgeDesk : AppSettings.Current.eyeNudgeRoom;

        /// <summary>Head height above the tracking floor, without any of our offsets.</summary>
        private float RawEyes => TrackingSpace != null && Head != null ? TrackingSpace.InverseTransformPoint(Head.position).y : StandingEyes;

        private void Update()
        {
            if (!ready || (Application.isEditor && !simulateInEditor)) return;
            bool here = OnPlatform;
            if (onPlatform != here)
            {
                // Stepped on or off the platform (teleport): re-measure the room lift for the new zone.
                onPlatform = here;
                roomLift = here || !AppSettings.Current.standingViewWhenRoaming ? 0f : Mathf.Clamp(StandingEyes - (RawEyes + floorFix), 0f, 0.9f);
                ApplyEyes();
                if (here) FitDeskToHead();
            }
            if (Time.unscaledTime < nextCheck || !AppSettings.Current.autoHeight) return;
            nextCheck = Time.unscaledTime + 2f;
            CorrectHeight(false);
        }

        /// <summary>For editor tests only: run the zone / watchdog logic without a headset.</summary>
        [System.NonSerialized] public bool simulateInEditor;

        private bool ready;
        private bool roamSetting = true;

        private void OnSettingsChanged()
        {
            ApplyHeight();
            bool roam = AppSettings.Current.standingViewWhenRoaming;
            if (roam != roamSetting)
            {
                roamSetting = roam;
                if (!OnPlatform) roomLift = roam ? Mathf.Clamp(StandingEyes - (RawEyes + floorFix), 0f, 0.9f) : 0f;
            }
            if (ready) ApplyEyes();
        }

        private void ApplyEyes()
        {
            Transform ts = TrackingSpace;
            if (ts == null) return;
            float y = Mathf.Clamp(floorFix + roomLift + Nudge, -1.2f, 1.8f);
            if (Mathf.Abs(ts.localPosition.y - y) < 0.001f) return;
            ts.localPosition = new Vector3(ts.localPosition.x, y, ts.localPosition.z);
        }

        /// <summary>
        /// Watchdog / Reset: when tracking clearly lost the floor, snap the eyes to a typical height.
        /// force = always snap now (Settings > Reset height) and clear your manual nudges.
        /// Your own HEIGHT +/- choices are never undone by the watchdog.
        /// </summary>
        public void CorrectHeight(bool force)
        {
            if (Head == null || TrackingSpace == null) return;
            float raw = RawEyes;
            float typical = AppSettings.Current.seated ? SeatedEyes : StandingEyes;
            bool lost = raw < (AppSettings.Current.seated ? 0.5f : 0.7f) || raw > 2.25f;   // floor lost, or re-taken while seated
            float next = floorFix;
            if (force)
            {
                next = lost ? typical - raw : 0f;
                AppSettings.Current.eyeNudgeDesk = AppSettings.Current.eyeNudgeRoom = 0f;
                AppSettings.Save();
                if (!OnPlatform && AppSettings.Current.standingViewWhenRoaming) roomLift = Mathf.Clamp(StandingEyes - (raw + next), 0f, 0.9f);
            }
            else if (lost) next = typical - raw;
            else if (raw > 1.0f && raw < 2.1f) next = 0f;                     // tracking recovered: drop the fix
            if (!force && Mathf.Abs(next - floorFix) < 0.02f) return;
            floorFix = next;
            ApplyEyes();
            FitDeskToHead();
        }

        /// <summary>Wrist menu: raise or lower your view by a few centimetres (remembered for this zone).</summary>
        public void NudgeHeight(float metres)
        {
            AppSettings s = AppSettings.Current;
            if (OnPlatform) s.eyeNudgeDesk = Mathf.Clamp(s.eyeNudgeDesk + metres, -0.6f, 0.6f);
            else s.eyeNudgeRoom = Mathf.Clamp(s.eyeNudgeRoom + metres, -0.6f, 0.6f);
            AppSettings.Save();
            ApplyEyes();
            if (OnPlatform) FitDeskToHead();
        }

        /// <summary>
        /// Wrist menu AUTO HEIGHT: clears your nudge for where you stand and re-measures, without moving you.
        /// On the floor it lifts you to standing eye height; on the platform it trusts the headset.
        /// </summary>
        public void AutoHeight()
        {
            if (Head == null || TrackingSpace == null) return;
            AppSettings s = AppSettings.Current;
            if (OnPlatform) s.eyeNudgeDesk = 0f; else s.eyeNudgeRoom = 0f;
            AppSettings.Save();
            float raw = RawEyes;
            float typical = s.seated ? SeatedEyes : StandingEyes;
            floorFix = raw > 0.9f && raw < 2.1f ? 0f : typical - raw;
            roomLift = OnPlatform || !s.standingViewWhenRoaming ? 0f : Mathf.Clamp(StandingEyes - (raw + floorFix), 0f, 0.9f);
            ApplyEyes();
            if (OnPlatform) FitDeskToHead();
        }

        private Transform trackingSpace;
        private Transform TrackingSpace
        {
            get
            {
                if (trackingSpace == null && rigRoot != null)
                {
                    var rig = rigRoot.GetComponent<OVRCameraRig>();
                    trackingSpace = rig != null ? rig.trackingSpace : rigRoot.Find("TrackingSpace");
                }
                return trackingSpace;
            }
        }

        private System.Collections.IEnumerator Start()
        {
            // Wait for head tracking, then put the user behind the desk and size the desk to them.
            yield return new WaitForSeconds(1.5f);
            roamSetting = AppSettings.Current.standingViewWhenRoaming;
            ready = true;
            if (!Application.isEditor) { ReturnToDesk(); ApplyEyes(); FitDeskToHead(); }
            else ApplyHeight();
        }

        private Transform Head => Camera.main != null ? Camera.main.transform : null;

        /// <summary>Moves the whole desk (console, keyboard, panels, review spot) to suit the current head height.</summary>
        public void FitDeskToHead()
        {
            if (Head == null) return;
            // Eye height above whatever floor you stand on (works on the podium and when roaming the room).
            float eyeAbovePlatform = rigRoot != null ? Head.position.y - rigRoot.position.y : Head.position.y - platformTopY;
            float below = AppSettings.Current.seated ? 0.42f : deskBelowEyes;   // seated: desk a little closer to the eyes
            fitted = Mathf.Clamp(eyeAbovePlatform - below, deskHeightRange.x, deskHeightRange.y);
            ApplyHeight();
        }

        /// <summary>Last fit plus the fine-tune offset from Settings > Comfort & View.</summary>
        private void ApplyHeight()
        {
            float desk = Mathf.Clamp(fitted + AppSettings.Current.deskHeightOffset, deskHeightRange.x - 0.15f, deskHeightRange.y + 0.15f);
            Vector3 p = deskAssembly.localPosition;
            deskAssembly.localPosition = new Vector3(p.x, baseDeskY + (desk - defaultDeskHeight), p.z);
        }

        /// <summary>Re-centres the view on where you are looking now (same as holding the Meta button).</summary>
        public void Recenter()
        {
            if (OVRManager.display != null) OVRManager.display.RecenterPose();
            ReturnToDesk();
        }

        /// <summary>Teleports the user back behind the review desk, facing the team.</summary>
        public void ReturnToDesk()
        {
            if (rigRoot == null || Head == null) return;
            Vector3 headFlat = Head.position; headFlat.y = rigRoot.position.y;
            float yawDelta = standPoint.eulerAngles.y - Head.eulerAngles.y;
            rigRoot.RotateAround(headFlat, Vector3.up, yawDelta);
            Vector3 offset = rigRoot.position - new Vector3(Head.position.x, rigRoot.position.y, Head.position.z);
            rigRoot.position = new Vector3(standPoint.position.x, standPoint.position.y, standPoint.position.z) + offset;
        }
    }
}
