using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Drives the Meta XR SDK editor tooling from code: installs Building Blocks (the same as
    /// dragging them in from Meta XR Tools > Building Blocks), runs Interaction SDK Quick Actions
    /// (the "Add Ray/Poke Interaction to Canvas", "Add Teleport" wizards) and applies the project
    /// settings a hand-tracked Quest app needs. Those Meta APIs are internal, hence reflection.
    /// </summary>
    public static class MetaSetup
    {
        public const string InteractionsRigBlock = "Packages/com.meta.xr.sdk.interaction.ovr/Editor/Blocks/Interactions/Interactions.asset";
        public const string PassthroughBlock = "Packages/com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockData/Passthrough/Passthrough.asset";

        /// <summary>Installs a Building Block (and its dependencies) into the open scene.</summary>
        public static async Task InstallBlock(string assetPath)
        {
            var block = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (block == null) { Debug.LogError($"[MetaSetup] Building block not found: {assetPath}"); return; }
            MethodInfo install = null;
            for (Type t = block.GetType(); t != null && install == null; t = t.BaseType)
                install = t.GetMethod("InstallWithDependencies", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, new[] { typeof(GameObject) }, null);
            if (install == null) { Debug.LogError($"[MetaSetup] InstallWithDependencies not found on {block.GetType().Name}"); return; }
            await (Task)install.Invoke(block, new object[] { null });
            Debug.Log($"[MetaSetup] Installed building block {block.name}");
        }

        /// <summary>Runs an Interaction SDK Quick Action wizard headless, like its menu item with default settings.</summary>
        public static bool RunWizard(string typeName, GameObject target, params (string field, object value)[] fields)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName, false)).FirstOrDefault(t => t != null);
            if (type == null) { Debug.LogError($"[MetaSetup] Wizard type {typeName} not found"); return false; }

            var wizard = ScriptableObject.CreateInstance(type);
            try
            {
                SetMember(wizard, "Target", target);
                foreach (var (field, value) in fields) SetMember(wizard, field, value);
                Call(wizard, "InitializeFields");
                Call(wizard, "FixMissingDependencies", true);
                if (!(bool)Call(wizard, "CanCreate"))
                {
                    Debug.LogError($"[MetaSetup] {type.Name} cannot run on {(target ? target.name : "scene")}");
                    return false;
                }
                Call(wizard, "Create");
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wizard);
            }
        }

        /// <summary>Hands + controllers, system keyboard, passthrough; XRI's device simulator off.</summary>
        public static void ConfigureProject()
        {
            var config = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Oculus/OculusProjectConfig.asset");
            if (config != null)
            {
                var so = new SerializedObject(config);
                SetInt(so, "handTrackingSupport", 1);         // Controllers And Hands
                SetInt(so, "handTrackingFrequency", 1);       // High
                SetBool(so, "requiresSystemKeyboard", true);  // Meta system keyboard overlay
                SetInt(so, "_insightPassthroughSupport", 1);  // Supported
                SetInt(so, "anchorSupport", 0);               // not used (MR Utility Kit's rule would turn these on)
                SetInt(so, "sceneSupport", 0);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
            }

            // The agent bridge on the PC is plain http on 127.0.0.1 / the LAN; Android blocks cleartext unless allowed.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.forceInternetPermission = true;

            // The XRI device simulator draws two controllers in the Editor; it is not part of this app.
            var sim = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset");
            if (sim != null)
            {
                var so = new SerializedObject(sim);
                SetBool(so, "m_AutomaticallyInstantiateSimulatorPrefab", false);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(sim);
            }

            // Quest only: the Android XR (Samsung) feature must not run alongside Meta Quest support.
            var settings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings != null)
                foreach (var f in settings.GetFeatures())
                {
                    string n = f.GetType().Name;
                    if (n == "AndroidXRSupportFeature") f.enabled = false;
                    if (n == "MetaQuestFeature" || n == "MetaXRFeature") f.enabled = true;
                }
            AssetDatabase.SaveAssets();
        }

        private static void SetInt(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; else Debug.LogWarning($"[MetaSetup] {name} missing"); }
        private static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else Debug.LogWarning($"[MetaSetup] {name} missing"); }

        private static object Call(object obj, string method, params object[] args)
        {
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
                if (m != null) return m.Invoke(obj, args);
            }
            throw new MissingMethodException(obj.GetType().Name, method);
        }

        private static void SetMember(object obj, string name, object value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, flags);
                if (f != null)
                {
                    f.SetValue(obj, f.FieldType.IsEnum && !(value is Enum) ? Enum.ToObject(f.FieldType, value) : value);
                    return;
                }
                var p = t.GetProperty(name, flags);
                if (p != null) { p.GetSetMethod(true).Invoke(obj, new[] { value }); return; }
            }
            throw new MissingMemberException(obj.GetType().Name, name);
        }
    }
}
