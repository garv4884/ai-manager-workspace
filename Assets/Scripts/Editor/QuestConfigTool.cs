using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Tools > AI Manager > Quest API Key.
    ///
    /// Paste the Anthropic API key once. It is saved to UserSettings/AIManagerConfig.json (git-ignored, never
    /// built into the APK; also used by Play mode) and pushed over USB to the headset at
    /// /sdcard/Android/data/&lt;package&gt;/files/config.json with Unity's own adb. The app re-reads that file
    /// every few seconds, so it switches to live AI without a restart.
    ///
    /// After every Android build the key is pushed again automatically as soon as the app's data folder
    /// exists on the connected headset (turn off with the toggle in the window).
    /// </summary>
    public class QuestConfigTool : EditorWindow
    {
        private const string AutoPushPref = "AIManager.AutoPushKey";
        private string key = "";
        private bool reveal;
        private string log = "";
        private Vector2 scroll;

        public static bool AutoPush
        {
            get => EditorPrefs.GetBool(AutoPushPref, true);
            set => EditorPrefs.SetBool(AutoPushPref, value);
        }

        private static string PackageId => PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
        private static string DeviceDir => $"/sdcard/Android/data/{PackageId}/files";
        private static string LegacyConfig => Path.Combine(Application.dataPath, "StreamingAssets", "config.json");

        [MenuItem("Tools/AI Manager/Quest API Key + Agent Bridge")]
        public static void Open()
        {
            var w = GetWindow<QuestConfigTool>(true, "Quest API Key");
            w.minSize = new Vector2(480, 470);
            w.key = ReadKey();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Anthropic API key", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Saved only on this PC (UserSettings, git-ignored) and on the headset. It is never built into the APK.\n" +
                "Model: " + AIServiceManager.DefaultModel, MessageType.Info);

            key = reveal ? EditorGUILayout.TextField("API key", key) : EditorGUILayout.PasswordField("API key", key);
            reveal = EditorGUILayout.Toggle("Show key", reveal);
            AutoPush = EditorGUILayout.Toggle(new GUIContent("Push after every build",
                "After an Android build, wait for the app on the connected headset and push the key."), AutoPush);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save + push to Quest", GUILayout.Height(30))) { SaveKey(key); RunPush(); }
                if (GUILayout.Button("Save only", GUILayout.Height(30))) { SaveKey(key); Append("Saved for Play mode."); }
                if (GUILayout.Button("Remove from Quest", GUILayout.Height(30))) RunRemove();
            }
            if (GUILayout.Button("Check headset connection")) RunCheck();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Agent bridge (real agents on this PC)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bridge/aim_bridge.py connects Claude Code sessions, scripts and MCP clients to the desks in VR. " +
                "Over USB the headset reaches it at 127.0.0.1:8787 through adb reverse (done after every build).",
                MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Start bridge", GUILayout.Height(26))) StartBridge();
                if (GUILayout.Button("Connect headset (USB)", GUILayout.Height(26))) RunReverse();
                if (GUILayout.Button("Open dashboard", GUILayout.Height(26))) Application.OpenURL("http://127.0.0.1:8787");
            }

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox(log.Length > 0 ? log.TrimEnd() :
                "Connect the Quest with a USB cable and allow USB debugging in the headset.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void Append(string line) { log += line + "\n"; Repaint(); }

        // ── Key file ──────────────────────────────────────────────────────────

        public static string ReadKey()
        {
            MigrateLegacyConfig();
            string path = AIServiceManager.ConfigPath;
            if (!File.Exists(path)) return "";
            try { return JsonUtility.FromJson<AIServiceManager.ConfigData>(File.ReadAllText(path))?.apiKey?.Trim() ?? ""; }
            catch { return ""; }
        }

        public static void SaveKey(string apiKey)
        {
            string path = AIServiceManager.ConfigPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AIServiceManager.ConfigData existing = null;
            try { if (File.Exists(path)) existing = JsonUtility.FromJson<AIServiceManager.ConfigData>(File.ReadAllText(path)); }
            catch { /* rewrite below */ }
            File.WriteAllText(path, JsonUtility.ToJson(WithClaudeKey(existing, apiKey), true));
        }

        /// <summary>Sets the Claude key on a config, keeping other providers, the default brain and the bridge settings.</summary>
        private static AIServiceManager.ConfigData WithClaudeKey(AIServiceManager.ConfigData existing, string apiKey)
        {
            var data = existing ?? new AIServiceManager.ConfigData();
            apiKey = (apiKey ?? "").Trim();
            data.apiKey = apiKey;
            data.model = AIServiceManager.DefaultModel;
            var list = new System.Collections.Generic.List<AIServiceManager.Backend>(data.backends ?? new AIServiceManager.Backend[0]);
            var claude = list.Find(b => b != null && b.id == "anthropic");
            if (claude == null) { claude = new AIServiceManager.Backend { id = "anthropic", model = AIServiceManager.DefaultModel }; list.Add(claude); }
            claude.apiKey = apiKey;
            data.backends = list.ToArray();
            if (string.IsNullOrEmpty(data.defaultBackend)) data.defaultBackend = AIServiceManager.Simulation;
            return data;
        }

        // ── Bridge ────────────────────────────────────────────────────────────

        private static string BridgeDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Bridge"));

        private void StartBridge()
        {
            string script = Path.Combine(BridgeDir, "aim_bridge.py");
            try
            {
                // Its own console window, so it keeps running and you can see what it does.
                var psi = Application.platform == RuntimePlatform.WindowsEditor
                    ? new ProcessStartInfo("cmd.exe", $"/k python \"{script}\"") { UseShellExecute = true, WorkingDirectory = BridgeDir }
                    : new ProcessStartInfo("python3", $"\"{script}\"") { UseShellExecute = true, WorkingDirectory = BridgeDir };
                Process.Start(psi);
                Append("Bridge starting in its own window. Dashboard: http://127.0.0.1:8787");
            }
            catch (Exception e) { Append($"Could not start the bridge ({e.Message}). Run: python Bridge/aim_bridge.py"); }
        }

        private async void RunReverse()
        {
            _ = AdbPath;
            Append(await Task.Run(() => Reverse(null)));
        }

        /// <summary>adb reverse: the headset's 127.0.0.1:8787 reaches this PC's bridge over the USB cable.</summary>
        private static string Reverse(string onlyDevice)
        {
            string err = null;
            string device = onlyDevice ?? Device(out err);
            if (device == null) return err;
            string output = Adb($"-s {device} reverse tcp:8787 tcp:8787", out int code);
            return code == 0 ? $"Headset {device} now reaches the bridge over USB (127.0.0.1:8787)." : $"adb reverse failed: {output.Trim()}";
        }

        private static string ConfigJson(string apiKey) =>
            JsonUtility.ToJson(new AIServiceManager.ConfigData { apiKey = (apiKey ?? "").Trim(), model = AIServiceManager.DefaultModel }, true);

        /// <summary>The old Assets/StreamingAssets/config.json would ship inside the APK; move its key to UserSettings.</summary>
        [InitializeOnLoadMethod]
        private static void MigrateOnLoad() => EditorApplication.delayCall += MigrateLegacyConfig;

        private static void MigrateLegacyConfig()
        {
            if (!File.Exists(LegacyConfig)) return;
            try
            {
                var legacy = JsonUtility.FromJson<AIServiceManager.ConfigData>(File.ReadAllText(LegacyConfig));
                if (!File.Exists(AIServiceManager.ConfigPath) && legacy != null && !string.IsNullOrWhiteSpace(legacy.apiKey))
                    SaveKey(legacy.apiKey);
                AssetDatabase.DeleteAsset("Assets/StreamingAssets/config.json");
                Debug.Log("[QuestConfig] Moved the API key out of StreamingAssets (it would have been built into the APK) " +
                          "to UserSettings/AIManagerConfig.json.");
            }
            catch (Exception e) { Debug.LogWarning($"[QuestConfig] Could not migrate StreamingAssets/config.json: {e.Message}"); }
        }

        // ── adb ───────────────────────────────────────────────────────────────

        // Unity APIs are main-thread only: resolve paths and ids here, then run adb on a worker thread.

        private async void RunPush()
        {
            Append("Pushing to the headset...");
            string apiKey = ReadKey(), dir = DeviceDir;
            _ = AdbPath;
            Append(await Task.Run(() => Push(apiKey, dir)));
        }

        private async void RunRemove()
        {
            Append("Removing the key from the headset...");
            string dir = DeviceDir;
            _ = AdbPath;
            string result = await Task.Run(() => Push("", dir));
            Append(result.Replace("Key pushed", "Empty key pushed (offline simulation)"));
        }

        private async void RunCheck()
        {
            string package = PackageId;
            _ = AdbPath;
            string result = await Task.Run(() =>
            {
                string device = Device(out string err);
                if (device == null) return err;
                bool installed = Adb($"-s {device} shell pm list packages {package}", out _).Contains(package);
                return $"Connected: {device}. App {package} {(installed ? "is installed" : "is not installed yet (build it once)")}.";
            });
            Append(result);
        }

        /// <summary>Writes the config to a temp file and pushes it. Runs on a worker thread.</summary>
        private static string Push(string apiKey, string deviceDir, string onlyDevice = null)
        {
            string err = null;
            string device = onlyDevice ?? Device(out err);
            if (device == null) return err;
            string tmp = Path.Combine(Path.GetTempPath(), $"aimanager_config_{Guid.NewGuid():N}.json");
            try
            {
                // Keep what the headset already has (bridge address / token set in VR).
                AIServiceManager.ConfigData existing = null;
                string current = Adb($"-s {device} shell cat {deviceDir}/config.json", out int catCode);
                if (catCode == 0 && current.TrimStart().StartsWith("{"))
                    try { existing = JsonUtility.FromJson<AIServiceManager.ConfigData>(current); } catch { /* replace */ }
                File.WriteAllText(tmp, JsonUtility.ToJson(WithClaudeKey(existing, apiKey), true));
                Adb($"-s {device} shell mkdir -p {deviceDir}", out _);
                string output = Adb($"-s {device} push \"{tmp}\" {deviceDir}/config.json", out int code);
                return code == 0
                    ? $"Key pushed to {device}. The app picks it up within a few seconds."
                    : $"adb push failed: {output.Trim()}";
            }
            finally { try { File.Delete(tmp); } catch { /* ignore */ } }
        }

        private static string Device(out string error)
        {
            error = null;
            string adb = AdbPath;
            if (adb == null) { error = "adb not found. Install Android support for this Unity version."; return null; }
            string list = Adb("devices", out _);
            foreach (string raw in list.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("List of")) continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 2) continue;
                if (parts[1] == "device") return parts[0];
                if (parts[1] == "unauthorized") { error = "Headset found but not authorised: put it on and allow USB debugging."; return null; }
            }
            error = "No headset connected. Plug in the Quest over USB (developer mode on).";
            return null;
        }

        private static string adbPath;

        private static string AdbPath
        {
            get
            {
                if (adbPath != null) return adbPath;
                string sdk = null;
                var settings = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
                if (settings != null) sdk = settings.GetProperty("sdkRootPath")?.GetValue(null) as string;
                if (string.IsNullOrEmpty(sdk))
                    sdk = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK");
                string exe = Path.Combine(sdk, "platform-tools", Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb");
                adbPath = File.Exists(exe) ? exe : null;
                return adbPath;
            }
        }

        private static string Adb(string args, out int exitCode)
        {
            exitCode = -1;
            try
            {
                var psi = new ProcessStartInfo(AdbPath, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { /* gone */ } return "adb timed out"; }
                exitCode = p.ExitCode;
                return stdout.Result + stderr.Result;
            }
            catch (Exception e) { return e.Message; }
        }

        // ── After a build: push automatically once the app is on the headset ───

        private class AutoPushAfterBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                MigrateLegacyConfig();
                if (File.Exists(LegacyConfig))
                    throw new BuildFailedException("Assets/StreamingAssets/config.json would put your API key inside the APK. " +
                                                   "Remove it and use Tools > AI Manager > Quest API Key instead.");
            }

            public void OnPostprocessBuild(BuildReport report)
            {
                if (report.summary.platform != BuildTarget.Android || !AutoPush || AdbPath == null) return;
                string apiKey = ReadKey();
                bool pushKey = !string.IsNullOrEmpty(apiKey);
                Debug.Log(pushKey ? "[QuestConfig] Will connect the bridge and push the API key once the app is on the headset."
                                  : "[QuestConfig] Will connect the agent bridge (adb reverse) once the headset is found.");
                string package = PackageId, dir = DeviceDir;
                Task.Run(async () =>
                {
                    // Build & Run installs and launches the app after this callback; wait for its data folder.
                    for (int i = 0; i < 60; i++)
                    {
                        string device = Device(out _);
                        if (device != null)
                        {
                            Debug.Log($"[QuestConfig] {Reverse(device)}");
                            if (!pushKey) return;
                            bool installed = Adb($"-s {device} shell pm list packages {package}", out _).Contains(package);
                            Adb($"-s {device} shell ls {dir}", out int code);
                            if (installed && code == 0)
                            {
                                string r = Push(apiKey, dir, device);
                                Debug.Log($"[QuestConfig] {r}");
                                return;
                            }
                        }
                        await Task.Delay(3000);
                    }
                    Debug.Log("[QuestConfig] Headset/app not found within 3 minutes; use Tools > AI Manager > Quest API Key to push the key.");
                });
            }
        }
    }
}
