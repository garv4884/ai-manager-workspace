using System;
using UnityEngine;

namespace AIManagerWorkspace
{
    public enum InputMode { Hands, Controllers }
    public enum GestureTiming { Quick, Normal, Careful }
    public enum RoomTheme { DaylightStudio, GoldenHour, NightLoft, NordicCalm }
    public enum WorkSpeed { Fast, Normal, Realistic }
    public enum ArrivalPace { Brisk, Calm, Relaxed }
    public enum UiSize { Compact, Standard, Large }
    public enum WalkSpeed { Relaxed, Normal, Brisk }

    /// <summary>Where a window was left: a snap preset or a free pose (platform space), its size and whether minimized.</summary>
    [Serializable]
    public class WindowState
    {
        public string id;
        public int preset;            // WindowChrome.Preset
        public Vector3 position;      // used when preset == Free
        public float yaw, pitch;
        public float scale = 1f;
        public bool minimized;
        public float width, height;   // resizable windows (0 = default)
    }

    /// <summary>
    /// Per-device preferences (PlayerPrefs, JSON). Separate from the session (team and work) and from
    /// config.json (API key). Change a field, then call <see cref="Save"/>; listeners re-apply on <see cref="Changed"/>.
    /// </summary>
    [Serializable]
    public class AppSettings
    {
        private const string Key = "aim.settings.v1";

        public InputMode inputMode = InputMode.Hands;
        public bool gestures = true;
        public bool palmGesture = true;              // palm out = reject / drop
        public bool gesturesNeedFacing = true;       // gestures only count while you look at the page on your desk
        public GestureTiming gestureTiming = GestureTiming.Normal;
        public bool menuOnRightHand;            // false: wrist menu on the left palm (poked by the right hand)
        public RoomTheme theme = RoomTheme.DaylightStudio;
        public bool mixedReality;
        public float volume = 0.8f;
        public bool uiClicks = true;
        public bool chimes = true;
        public bool autoKeyboardForQuestions = true;
        public WorkSpeed workSpeed = WorkSpeed.Normal;
        public bool tutorialDone;

        // Review flow
        public int maxInLine = 2;                       // agents waiting in line (plus the one presenting)
        public ArrivalPace arrivalPace = ArrivalPace.Calm;

        // Comfort & view
        public bool seated;
        public bool autoHeight = true;                  // fix the eye height when tracking loses the floor (e.g. after sleep)
        public bool standingViewWhenRoaming = true;     // off the platform, lift a seated / short view to standing eye height
        public float eyeNudgeDesk;                      // metres, wrist-menu HEIGHT +/- while on the platform
        public float eyeNudgeRoom;                      // metres, the same while walking around the office
        public float deskHeightOffset;                  // metres, fine-tune on top of the automatic fit
        public UiSize uiSize = UiSize.Standard;
        public bool teleport = true;
        public WalkSpeed walkSpeed = WalkSpeed.Normal;
        public bool showBoard = true;

        public System.Collections.Generic.List<WindowState> windows = new System.Collections.Generic.List<WindowState>();

        public WindowState Window(string id)
        {
            var w = windows.Find(x => x.id == id);
            if (w == null) { w = new WindowState { id = id }; windows.Add(w); }
            return w;
        }

        public float ArrivalGapSeconds => arrivalPace switch { ArrivalPace.Brisk => 3f, ArrivalPace.Relaxed => 12f, _ => 7f };
        public float UiScale => uiSize switch { UiSize.Compact => 0.88f, UiSize.Large => 1.15f, _ => 1f };
        public float WalkSpeedMetres => walkSpeed switch { WalkSpeed.Relaxed => 1.1f, WalkSpeed.Brisk => 1.9f, _ => 1.45f };

        private static AppSettings current;

        public static AppSettings Current => current ??= Load();

        /// <summary>Raised after <see cref="Save"/>.</summary>
        public static event Action Changed;

        public static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Current));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static AppSettings Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception e) { Debug.LogWarning($"[Settings] Reset to defaults: {e.Message}"); }
            return new AppSettings();
        }

        /// <summary>Back to defaults (keeps whether the tutorial was seen).</summary>
        public static void ResetToDefaults()
        {
            bool seen = Current.tutorialDone;
            current = new AppSettings { tutorialDone = seen };
            Save();
        }

        public float GestureHoldSeconds => gestureTiming switch
        {
            GestureTiming.Quick => 0.3f,
            GestureTiming.Careful => 0.8f,
            _ => 0.45f
        };

        public Vector2 SimulatedWorkSeconds => workSpeed switch
        {
            WorkSpeed.Fast => new Vector2(5f, 9f),
            WorkSpeed.Realistic => new Vector2(20f, 35f),
            _ => new Vector2(10f, 18f)
        };

        public static string ThemeName(RoomTheme t) => t switch
        {
            RoomTheme.GoldenHour => "Golden Hour",
            RoomTheme.NightLoft => "Night Loft",
            RoomTheme.NordicCalm => "Nordic Calm",
            _ => "Daylight Studio"
        };
    }
}
