using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static AIManagerWorkspace.BuilderKit;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Builds the whole AI Manager Workspace scene from code, on top of Meta XR building blocks.
    ///
    /// Layout (metres, +Z is "in front of the manager"):
    ///   - Manager platform: round, 0.9 m high, centred at the origin, stairs at the back.
    ///   - Review desk on the platform's front edge; lift just beyond it at z = 2.95.
    ///   - Queue lane from z = 4.2 outward along +Z.
    ///   - Workstations around the platform (layout templates are relative to the origin).
    ///   - Room 24 x 23.5 m: daylight windows (west), oak slat wall (east), team board (north),
    ///     glass discussion room (south-east corner), lounge (south-west corner).
    ///
    /// XR: Meta "Interactions Rig" building block (OVRCameraRig + hands + controllers with ray,
    /// poke, grab and locomotion), "Passthrough" building block, Interaction SDK quick actions for
    /// ray + poke on every desk canvas and NavMesh teleport.
    /// </summary>
    public static class SceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/AIManagerWorkspace.unity";
        private const float PlatformHeight = 0.9f;
        private const float PlatformRadius = 2.3f;
        private const float DeskHeight = 0.78f;
        private const float DeskTopY = PlatformHeight + DeskHeight;
        private static readonly Vector3 LiftCenter = new Vector3(0f, 0f, 2.95f);
        private static readonly Vector3 QueueStart = new Vector3(0f, 0f, 4.2f);
        private static readonly Vector3 ManagerStand = new Vector3(0f, PlatformHeight, 0.5f);
        private const int WorkstationCount = 10;   // team can grow to 10; unused desks stay packed away

        [MenuItem("Tools/AI Manager/Build Main Scene")]
        public static async void BuildMainSceneMenu() => await BuildMainScene();

        public static async Task BuildMainScene()
        {
            MetaSetup.ConfigureProject();
            SetNavAgentRadius(0.28f);
            AgentPrefabSetup.CreateAgentPrefab();
            var agentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AgentPrefabSetup.PrefabPath);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var shell = new GameObject("Shell").transform;               // hidden in mixed reality
            var decor = new GameObject("Decor").transform;               // hidden in mixed reality
            var platform = new GameObject("ManagerPlatform").transform;
            var desksRoot = new GameObject("Workstations").transform;
            var systems = new GameObject("Systems").transform;

            BuildRoom(shell);
            BuildDecor(decor);
            var meetingWalls = new GameObject("MeetingRoomWalls").transform;
            var meeting = BuildMeetingRoom(meetingWalls);
            var lift = BuildLift();
            var queueStart = BuildQueueLane();
            var deskParts = BuildPlatform(platform);
            var workstations = BuildWorkstations(desksRoot);
            var sun = BuildLighting();

            MarkStatic(shell.gameObject);
            MarkStatic(decor.gameObject);
            MarkStatic(platform.gameObject);
            MarkStatic(meetingWalls.gameObject);

            // NavMesh: bake walkable floor; desks are excluded and carve at runtime instead.
            var nav = new GameObject("NavMesh").AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            nav.transform.SetParent(systems);
            nav.collectObjects = CollectObjects.All;
            nav.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            nav.BuildNavMesh();

            // ── Meta building blocks ────────────────────────────────────────
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(PointableCanvasModule));
            es.transform.SetParent(systems);
            await MetaSetup.InstallBlock(MetaSetup.InteractionsRigBlock);   // Camera Rig + hands/controllers interactions
            await MetaSetup.InstallBlock(MetaSetup.PassthroughBlock);
            var cameraRig = Object.FindAnyObjectByType<OVRCameraRig>();
            cameraRig.transform.SetPositionAndRotation(ManagerStand, Quaternion.identity);
            // Passthrough runs as an underlay: Meta requires the eye camera to clear to transparent black.
            // The office shell encloses the view, so this changes nothing visible in the virtual office.
            foreach (var cam in cameraRig.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                EditorUtility.SetDirty(cam);
            }
            var ovrManager = cameraRig.GetComponent<OVRManager>();
            if (ovrManager != null) ovrManager.isInsightPassthroughEnabled = true;
            var ptLayer = Object.FindAnyObjectByType<OVRPassthroughLayer>();
            if (ptLayer != null) { ptLayer.hidden = true; ptLayer.enabled = false; }

            // ── Systems ─────────────────────────────────────────────────────
            var gmGO = new GameObject("GameManager");
            gmGO.transform.SetParent(systems);
            var gm = gmGO.AddComponent<GameManager>();
            var ai = gmGO.AddComponent<AIServiceManager>();
            var dispatcher = gmGO.AddComponent<TaskDispatcher>();
            var planner = gmGO.AddComponent<TeamPlanner>();
            var agents = gmGO.AddComponent<AgentManager>();
            var queue = gmGO.AddComponent<QueueManager>();
            var layout = gmGO.AddComponent<WorkspaceLayoutManager>();
            var passthrough = gmGO.AddComponent<PassthroughToggle>();
            var recognizer = gmGO.AddComponent<HandGestureRecognizer>();
            var gestures = gmGO.AddComponent<GestureInteractionManager>();
            var textInput = gmGO.AddComponent<TextInputService>();
            var station = gmGO.AddComponent<ManagerStation>();
            var session = gmGO.AddComponent<SessionStore>();
            var bridge = gmGO.AddComponent<AgentBridgeClient>();
            var uiFeedback = gmGO.AddComponent<UiFeedback>();
            var inputMode = gmGO.AddComponent<InputModeManager>();
            var room = gmGO.AddComponent<RoomEnvironment>();
            BuildUiKitAssets(gmGO);

            var reviewDesk = deskParts.desk;
            var deskRoot = deskParts.root;
            var keyboard = BuildKeyboard(deskRoot, reviewDesk, textInput);
            var settingsCanvas = BuildSettingsWindow(deskRoot, passthrough, out var settingsWindow);
            var tutorialCanvas = BuildTutorial(deskRoot, out var tutorial);
            var canvases = new List<Canvas>
            {
                keyboard.GetComponentInChildren<Canvas>(true),
                BuildConsole(deskRoot, recognizer),
                BuildLayoutPanel(deskRoot, passthrough, settingsWindow),
                BuildDetailPanel(deskRoot),
                settingsCanvas,
                tutorialCanvas,
            };
            var handMenu = BuildHandMenu(passthrough);

            // Done tray: point and pinch to open the work history on the workspace panel.

            var trayHit = new GameObject("TrayClick");
            trayHit.transform.SetParent(deskParts.tray.transform, false);
            var trayCol = trayHit.AddComponent<BoxCollider>();
            trayCol.size = new Vector3(1.1f, 6f, 1.1f);           // tray is thin; a taller box is easier to hit
            trayCol.center = new Vector3(0f, 2.5f, 0f);
            AddRayTarget(trayHit, trayCol);
            var trayClick = trayHit.AddComponent<RayClickTarget>();
            Wire(trayClick, "highlight", deskParts.tray.transform);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(trayClick.OnClick, settingsWindow.OpenHistory);
            BuildTeamBoard(shell.parent);

            // Ray + poke on every interactive canvas (Interaction SDK quick actions).
            canvases.AddRange(WindowBars);
            WindowBars.Clear();
            foreach (var c in canvases.Append(handMenu))
            {
                bool wasActive = c.gameObject.activeSelf;
                c.gameObject.SetActive(true);
                MetaSetup.RunWizard("Oculus.Interaction.Editor.QuickActions.RayCanvasWizard", c.gameObject);
                MetaSetup.RunWizard("Oculus.Interaction.Editor.QuickActions.PokeCanvasWizard", c.gameObject);
                c.gameObject.SetActive(wasActive);
            }
            RestrictToHand(handMenu.transform.root.gameObject, Oculus.Interaction.Input.Handedness.Right);

            // Teleport anywhere walkable (floor, platform, stairs) with hands or controllers.
            var teleport = new GameObject("TeleportArea");
            teleport.transform.SetParent(systems);
            MetaSetup.RunWizard("Oculus.Interaction.Editor.QuickActions.TeleportWizard", teleport,
                ("_interactableType", 2));   // NavMesh

            Wire(gm, "agentManager", agents);
            Wire(gm, "queueManager", queue);
            Wire(gm, "aiService", ai);
            Wire(gm, "dispatcher", dispatcher);
            Wire(gm, "planner", planner);
            Wire(gm, "layoutManager", layout);
            Wire(gm, "keyboard", textInput);
            Wire(gm, "station", station);
            Wire(gm, "session", session);
            Wire(gm, "bridge", bridge);
            Wire(uiFeedback, "desk", deskParts.desk);
            Wire(inputMode, "rigRoot", cameraRig.transform);
            BuildControllerNotice(inputMode);
            WireArray(room, "roots", new Object[] { shell, decor, meetingWalls, desksRoot });
            Wire(room, "sun", sun);
            Wire(tutorial, "handMenu", handMenu.GetComponentInParent<HandMenu>(true));
            Wire(tutorial, "gestures", recognizer);
            BuildGestureRing(recognizer);
            Wire(textInput, "deskKeyboard", keyboard);

            Wire(station, "deskAssembly", deskRoot);
            Wire(station, "standPoint", Empty("StandPoint", platform, ManagerStand - platform.position).transform);
            Wire(station, "rigRoot", cameraRig.transform);

            Wire(agents, "agentPrefab", agentPrefab);
            WireArray(agents, "workstations", workstations.Cast<Object>().ToArray());
            Wire(agents, "meetingRoom", meeting);

            Wire(queue, "queueStart", queueStart);
            Wire(queue, "lift", lift);
            Wire(queue, "reviewDesk", reviewDesk);

            Wire(layout, "agentManager", agents);
            Wire(layout, "center", platform);

            WireArray(passthrough, "hideInMixedReality", new Object[] { shell.gameObject, decor.gameObject, meetingWalls.gameObject });
            Wire(gestures, "recognizer", recognizer);

            // ── Save ────────────────────────────────────────────────────────
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] AI Manager Workspace scene built and saved.");
        }

        /// <summary>Adds a HandednessFilter to every Interaction SDK interactable under root (e.g. wrist menu: right hand only).</summary>
        private static void RestrictToHand(GameObject root, Oculus.Interaction.Input.Handedness hand)
        {
            var filter = root.AddComponent<HandednessFilter>();
            var fso = new SerializedObject(filter);
            fso.FindProperty("allowed").enumValueIndex = (int)hand;
            fso.ApplyModifiedPropertiesWithoutUndo();
            int n = 0;
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var so = new SerializedObject(mb);
                var list = so.FindProperty("_interactorFilters");
                if (list == null || !list.isArray) continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = filter;
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
            Debug.Log($"[SceneBuilder] {root.name}: {n} interactables accept the {hand} hand only.");
        }

        /// <summary>Makes a collider pointable by the Interaction SDK hand/controller rays.</summary>
        public static RayInteractable AddRayTarget(GameObject go, Collider collider)
        {
            var surface = go.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(collider);
            var ray = go.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(surface);
            EditorUtility.SetDirty(surface);
            EditorUtility.SetDirty(ray);
            return ray;
        }

        // ══ Room shell ═══════════════════════════════════════════════════════

        private static void BuildRoom(Transform shell)
        {
            Material floor = Mat("Env_Floor", Palette.Floor, 0.35f);
            Material wall = Mat("Env_Wall", Palette.Wall, 0.1f);
            Material ceiling = Mat("Env_Ceiling", Palette.Ceiling, 0.05f);
            Material oak = Mat("Env_Oak", Palette.Oak, 0.35f);
            Material charcoal = Mat("Env_Charcoal", Palette.Charcoal, 0.3f);
            Material daylight = Emissive("Env_Daylight", Palette.Daylight, 1.35f);
            Material panelLight = Emissive("Env_CeilingPanel", new Color(1f, 0.97f, 0.92f), 1.6f);
            Material rug = Mat("Env_Rug", Palette.RugTeal, 0.05f);
            Material rugSoft = Mat("Env_RugSoft", Palette.RugSoft, 0.05f);

            var floorGO = Box("Floor", shell, new Vector3(0f, -0.05f, 2.75f), new Vector3(24.4f, 0.1f, 23.9f), floor);
            Box("WorkRug", shell, new Vector3(0f, 0.006f, 7.6f), new Vector3(18f, 0.012f, 9f), rug, false);
            Cyl("PlatformRug", shell, new Vector3(0f, 0.011f, 0f), 7.6f, 0.022f, rugSoft, false);

            var walls = Empty("Walls", shell, Vector3.zero).transform;
            Box("WallNorth", walls, new Vector3(0f, 2.8f, 14.6f), new Vector3(24.4f, 5.6f, 0.2f), wall);
            Box("WallSouth", walls, new Vector3(0f, 2.8f, -9.1f), new Vector3(24.4f, 5.6f, 0.2f), wall);
            Box("WallWest", walls, new Vector3(-12.1f, 2.8f, 2.75f), new Vector3(0.2f, 5.6f, 23.9f), wall);
            Box("WallEast", walls, new Vector3(12.1f, 2.8f, 2.75f), new Vector3(0.2f, 5.6f, 23.9f), wall);
            Box("Ceiling", shell, new Vector3(0f, 5.7f, 2.75f), new Vector3(24.4f, 0.2f, 24.2f), ceiling, false);
            Box("SkirtingNorth", walls, new Vector3(0f, 0.06f, 14.48f), new Vector3(24f, 0.12f, 0.04f), oak, false);
            Box("SkirtingSouth", walls, new Vector3(0f, 0.06f, -8.98f), new Vector3(24f, 0.12f, 0.04f), oak, false);
            Box("SkirtingEast", walls, new Vector3(11.98f, 0.06f, 2.75f), new Vector3(0.04f, 0.12f, 23.4f), oak, false);

            // West wall: tall daylight windows with mullions and an oak sill.
            var windows = Empty("Windows", shell, Vector3.zero).transform;
            for (int i = 0; i < 6; i++)
            {
                float z = -6.2f + i * 3.4f;
                Box($"Pane_{i}", windows, new Vector3(-11.98f, 2.95f, z), new Vector3(0.03f, 3.6f, 3.3f), daylight, false);
                Box($"Mullion_{i}", windows, new Vector3(-11.95f, 2.95f, z + 1.7f), new Vector3(0.08f, 3.7f, 0.12f), charcoal, false);
                Box($"Transom_{i}", windows, new Vector3(-11.95f, 3.7f, z), new Vector3(0.06f, 0.05f, 3.3f), charcoal, false);
            }
            Box("Mullion_Start", windows, new Vector3(-11.95f, 2.95f, -7.9f), new Vector3(0.08f, 3.7f, 0.12f), charcoal, false);
            Box("Sill", windows, new Vector3(-11.86f, 1.12f, 2.3f), new Vector3(0.3f, 0.05f, 20.6f), oak, false);
            Box("Header", windows, new Vector3(-11.95f, 4.82f, 2.3f), new Vector3(0.1f, 0.1f, 20.6f), charcoal, false);

            // East wall: oak slat feature wall.
            var slats = Empty("SlatWall", shell, Vector3.zero).transform;
            for (int i = 0; i < 40; i++)
                Box($"Slat_{i}", slats, new Vector3(11.93f, 2.5f, -1.2f + i * 0.3f), new Vector3(0.06f, 4.2f, 0.12f), oak, false);

            // Ceiling light panels and a halo above the manager platform.
            var lights = Empty("CeilingLights", shell, Vector3.zero).transform;
            for (int x = -2; x <= 2; x++)
                for (int z = 0; z < 5; z++)
                {
                    if (x == 0 && (z == 1 || z == 2)) continue;
                    Box($"Panel_{x}_{z}", lights, new Vector3(x * 4.6f, 5.58f, -6.5f + z * 4.2f), new Vector3(2.4f, 0.03f, 0.55f), panelLight, false);
                }
            Cyl("PlatformHalo", lights, new Vector3(0f, 5.585f, 0f), 2.4f, 0.03f, panelLight, false);
            Cyl("PlatformHaloTrim", lights, new Vector3(0f, 5.59f, 0f), 2.55f, 0.02f, charcoal, false);

            // Structural columns.
            Material concrete = Mat("Env_Concrete", new Color(0.72f, 0.70f, 0.67f), 0.2f);
            foreach (var p in new[] { new Vector3(-8.8f, 0f, -1f), new Vector3(8.8f, 0f, -1f), new Vector3(-8.8f, 0f, 9f), new Vector3(8.8f, 0f, 9f) })
            {
                Cyl("Column", shell, p + Vector3.up * 2.8f, 0.34f, 5.6f, concrete);
                Cyl("ColumnBase", shell, p + Vector3.up * 0.06f, 0.4f, 0.12f, oak, false);
            }

            foreach (Transform t in shell.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<Renderer>() != null && (t.name.StartsWith("Wall") || t.name == "Ceiling" || t.parent == lights))
                    t.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void BuildDecor(Transform decor)
        {
            Material pot = Mat("Decor_Pot", Palette.Terracotta, 0.2f);
            Material potDark = Mat("Decor_PotDark", Palette.Graphite, 0.4f);
            Material leaf = Mat("Decor_Leaf", Palette.Leaf, 0.15f);
            Material leafLight = Mat("Decor_LeafLight", Palette.LeafLight, 0.15f);
            Material fabric = Mat("Decor_Fabric", Palette.Fabric, 0.05f);
            Material oak = Mat("Env_Oak", Palette.Oak, 0.35f);
            Material charcoal = Mat("Env_Charcoal", Palette.Charcoal, 0.3f);

            void Plant(Vector3 p, float s, bool dark)
            {
                var root = Empty("Plant", decor, p).transform;
                Cyl("Pot", root, new Vector3(0f, 0.28f * s, 0f), 0.27f * s, 0.56f * s, dark ? potDark : pot);
                Ball("Leaves", root, new Vector3(0f, 0.95f * s, 0f), new Vector3(0.75f, 0.8f, 0.75f) * s, leaf);
                Ball("Leaves2", root, new Vector3(0.18f * s, 1.3f * s, 0.08f * s), new Vector3(0.5f, 0.6f, 0.5f) * s, leafLight);
                Ball("Leaves3", root, new Vector3(-0.15f * s, 1.15f * s, -0.12f * s), new Vector3(0.55f, 0.55f, 0.55f) * s, leaf);
            }
            Plant(new Vector3(-11.0f, 0f, 13.6f), 1.3f, false);
            Plant(new Vector3(11.0f, 0f, 13.6f), 1.3f, true);
            Plant(new Vector3(-5.6f, 0f, 13.8f), 1.0f, true);
            Plant(new Vector3(5.6f, 0f, 13.8f), 1.0f, false);
            Plant(new Vector3(11.1f, 0f, -2.2f), 1.1f, false);
            Plant(new Vector3(-11.0f, 0f, -8.2f), 1.2f, true);
            Plant(new Vector3(-3.6f, 0f, -8.4f), 0.9f, false);
            Plant(new Vector3(4.4f, 0f, -8.4f), 0.9f, true);
            Plant(new Vector3(-11.1f, 0f, 12.0f), 0.8f, true);

            // Lounge in the south-west corner.
            var lounge = Empty("Lounge", decor, new Vector3(-8.0f, 0f, -6.9f)).transform;
            Box("SofaSeat", lounge, new Vector3(0f, 0.22f, 0f), new Vector3(2.6f, 0.44f, 0.95f), fabric);
            Box("SofaBack", lounge, new Vector3(0f, 0.62f, -0.4f), new Vector3(2.6f, 0.5f, 0.2f), fabric, false);
            Box("SofaArmL", lounge, new Vector3(-1.38f, 0.42f, 0f), new Vector3(0.18f, 0.5f, 0.95f), fabric, false);
            Box("SofaArmR", lounge, new Vector3(1.38f, 0.42f, 0f), new Vector3(0.18f, 0.5f, 0.95f), fabric, false);
            Cyl("CoffeeTable", lounge, new Vector3(0f, 0.38f, 1.2f), 0.5f, 0.05f, oak);
            Cyl("CoffeeTableLeg", lounge, new Vector3(0f, 0.18f, 1.2f), 0.06f, 0.36f, charcoal, false);
        }

        // ══ Manager platform & review desk ═══════════════════════════════════

        private struct DeskParts { public ReviewDesk desk; public Transform root; public GameObject tray; }

        private static DeskParts BuildPlatform(Transform platform)
        {
            Material plinth = Mat("Platform_Plinth", Palette.Charcoal, 0.35f);
            Material top = Mat("Platform_Top", new Color(0.33f, 0.31f, 0.29f), 0.15f);
            Material oak = Mat("Env_Oak", Palette.Oak, 0.35f);
            Material walnut = Mat("Platform_Walnut", Palette.Walnut, 0.45f);
            Material glass = Glass("Glass_Clear", new Color(0.85f, 0.93f, 0.97f), 0.18f);
            Material glow = Emissive("Accent_TealGlow", Palette.Teal, 1.6f);
            Material paper = Mat("Paper", Palette.Paper, 0.1f);

            Cyl("Base", platform, new Vector3(0f, PlatformHeight * 0.5f, 0f), PlatformRadius, PlatformHeight, plinth);
            Cyl("Kick", platform, new Vector3(0f, 0.05f, 0f), PlatformRadius + 0.12f, 0.1f, plinth, false);
            Cyl("Deck", platform, new Vector3(0f, PlatformHeight - 0.02f, 0f), PlatformRadius, 0.04f, top);
            Cyl("EdgeGlow", platform, new Vector3(0f, PlatformHeight - 0.07f, 0f), PlatformRadius + 0.02f, 0.03f, glow, false);

            // Low glass balustrade with an oak handrail, open at the front so nothing blocks the view down.
            const int segments = 20;
            for (int i = 0; i < segments; i++)
            {
                float deg = i * 360f / segments + 9f;
                if (deg < 70f || deg > 290f) continue;          // front: the desk and the view of the team
                if (deg > 162f && deg < 198f) continue;          // back: stairs
                float a = deg * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (PlatformRadius - 0.06f);
                float width = 2f * Mathf.PI * PlatformRadius / segments;
                Box($"Glass_{i}", platform, p + Vector3.up * (PlatformHeight + 0.4f), new Vector3(width, 0.75f, 0.02f), glass, true, new Vector3(0f, deg, 0f));
                Box($"Rail_{i}", platform, p + Vector3.up * (PlatformHeight + 0.8f), new Vector3(width + 0.02f, 0.04f, 0.06f), oak, false, new Vector3(0f, deg, 0f));
            }

            // Stairs down the back.
            const int steps = 5;
            float rise = PlatformHeight / steps;
            for (int i = 0; i < steps; i++)
            {
                float h = PlatformHeight - rise * (i + 1);
                float z = -PlatformRadius + 0.12f - 0.3f * i - 0.15f;
                if (h <= 0.001f) break;
                Box($"Step_{i}", platform, new Vector3(0f, h * 0.5f, z), new Vector3(1.3f, h, 0.3f), i % 2 == 0 ? walnut : top);
            }

            // Review desk: a slim slab on one narrow column so the view of the floor stays open.
            // Everything on the desk lives under DeskAssembly, which "Fit desk to me" moves up and down.
            var deskRoot = Empty("DeskAssembly", platform, Vector3.zero).transform;
            // Curved glass desk: a half ring around the manager, edge-lit, on slim chrome legs.
            Material deskGlass = Glass("Glass_Desk", new Color(0.72f, 0.84f, 0.9f), 0.34f);
            Material frost = Glass("Glass_Frost", new Color(0.86f, 0.92f, 0.96f), 0.13f);
            // Draw the glass before UI (queue 3000), so panels, keyboard and pages lying on it always show on top.
            deskGlass.renderQueue = 2950;
            frost.renderQueue = 2940;
            EditorUtility.SetDirty(deskGlass);
            EditorUtility.SetDirty(frost);
            Material chrome = Mat("Desk_Chrome", new Color(0.78f, 0.8f, 0.83f), 0.88f, 0.9f);
            Vector3 ring = new Vector3(0f, DeskTopY - 0.016f, 0.45f);
            const float inner = 0.58f, outer = 1.2f, from = -100f, to = 100f;
            var deskTop = Arc("DeskTop", deskRoot, ring, inner, outer, from, to, 0.026f, deskGlass);
            NoShadows(deskTop);
            NoShadows(Arc("EdgeGlow", deskRoot, ring + Vector3.down * 0.004f, outer - 0.004f, outer + 0.008f, from, to, 0.02f, glow, false));
            NoShadows(Arc("InnerTrim", deskRoot, ring, inner - 0.012f, inner + 0.004f, from, to, 0.03f, chrome, false));
            NoShadows(Arc("Modesty", deskRoot, ring + Vector3.down * 0.3f, outer - 0.03f, outer - 0.02f, from + 6f, to - 6f, 0.52f, frost, false));
            float legBottom = PlatformHeight - 0.45f, legTop = DeskTopY - 0.03f;   // runs into the plinth, so the desk can rise
            foreach (float deg in new[] { -88f, -44f, 0f, 44f, 88f })
            {
                float rad = deg * Mathf.Deg2Rad;
                Vector3 p = ring + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * (outer - 0.09f);
                Cyl("Leg", deskRoot, new Vector3(p.x, (legBottom + legTop) * 0.5f, p.z), 0.018f, legTop - legBottom, chrome, false);
            }

            BuildDeskObjects(deskRoot);

            // Done tray (approved pages stack up here).
            var tray = Box("DoneTray", deskRoot, new Vector3(0.5f, DeskTopY + 0.01f, 1.45f), new Vector3(0.28f, 0.02f, 0.34f), plinth, false);
            var stackRoot = Empty("DoneStackRoot", deskRoot, new Vector3(0.5f, DeskTopY + 0.02f, 1.45f)).transform;
            var stack = Box("DoneStack", stackRoot, new Vector3(0f, 0.001f, 0f), new Vector3(0.22f, 0.002f, 0.3f), paper, false);

            var reviewAnchor = Empty("ReviewAnchor", deskRoot, new Vector3(0f, DeskTopY + 0.53f, 1.47f), new Vector3(16f, 0f, 0f));
            var trayAnchor = Empty("TrayAnchor", deskRoot, new Vector3(0.5f, DeskTopY + 0.03f, 1.45f));

            var desk = deskRoot.gameObject.AddComponent<ReviewDesk>();
            Wire(desk, "reviewAnchor", reviewAnchor.transform);
            Wire(desk, "doneTray", trayAnchor.transform);
            Wire(desk, "doneStack", stack.transform);

            // Warm key light over the desk.
            var key = new GameObject("DeskLight").AddComponent<Light>();
            key.transform.SetParent(platform, false);
            key.transform.localPosition = new Vector3(0f, 4.6f, 0.8f);
            key.type = LightType.Point;
            key.range = 5.5f;
            key.intensity = 1.6f;
            key.color = new Color(1f, 0.92f, 0.82f);
            key.shadows = LightShadows.None;

            return new DeskParts { desk = desk, root = deskRoot, tray = tray };
        }

        private static LiftPad BuildLift()
        {
            Material plinth = Mat("Platform_Plinth", Palette.Charcoal, 0.35f);
            Material steel = Mat("Lift_Steel", new Color(0.55f, 0.57f, 0.6f), 0.7f, 0.8f);
            Material glow = Emissive("Accent_AmberGlow", Palette.Amber, 1.8f);
            Material top = Mat("Platform_Top", new Color(0.33f, 0.31f, 0.29f), 0.15f);

            var root = Empty("PresentationLift", null, LiftCenter).transform;
            var housing = Cyl("Housing", root, new Vector3(0f, 0.025f, 0f), 0.6f, 0.05f, plinth, false);
            Cyl("HousingGlow", root, new Vector3(0f, 0.03f, 0f), 0.63f, 0.04f, glow, false);
            var piston = Cyl("Piston", root, new Vector3(0f, 0.025f, 0f), 0.2f, 0.05f, steel, false);
            var platform = Empty("Platform", root, new Vector3(0f, 0.05f, 0f)).transform;
            Cyl("Disc", platform, new Vector3(0f, -0.03f, 0f), 0.48f, 0.06f, top, false);
            Cyl("DiscGlow", platform, new Vector3(0f, -0.035f, 0f), 0.5f, 0.03f, glow, false);

            var faceTarget = Empty("FaceTarget", null, ManagerStand + Vector3.up * 1.5f).transform;
            faceTarget.SetParent(root, true);

            var lift = root.gameObject.AddComponent<LiftPad>();
            Wire(lift, "platform", platform);
            Wire(lift, "piston", piston.transform);
            Wire(lift, "faceTowards", faceTarget);
            var so = new SerializedObject(lift);
            so.FindProperty("highY").floatValue = PlatformHeight;
            so.ApplyModifiedPropertiesWithoutUndo();

            var mod = root.gameObject.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true;
            return lift;
        }

        private static Transform BuildQueueLane()
        {
            Material amber = Emissive("Accent_AmberGlow", Palette.Amber, 1.8f);
            Material marker = Emissive("Queue_Marker", new Color(1f, 0.8f, 0.45f), 0.9f);
            var root = Empty("QueueLane", null, Vector3.zero).transform;
            var start = Empty("QueueStart", root, QueueStart).transform;
            for (int i = 0; i < 5; i++)
                Cyl($"Slot_{i}", root, QueueStart + new Vector3(0f, 0.014f, i * 1.1f), 0.24f, 0.012f, marker, false);
            Box("LaneEdgeL", root, new Vector3(-0.7f, 0.012f, 6.2f), new Vector3(0.04f, 0.01f, 6.4f), amber, false);
            Box("LaneEdgeR", root, new Vector3(0.7f, 0.012f, 6.2f), new Vector3(0.04f, 0.01f, 6.4f), amber, false);
            NoShadows(root.gameObject);
            return start;
        }

        // ══ Workstations ═════════════════════════════════════════════════════

        private static List<Workstation> BuildWorkstations(Transform parent)
        {
            Material oak = Mat("Env_Oak", Palette.Oak, 0.35f);
            Material charcoal = Mat("Env_Charcoal", Palette.Charcoal, 0.3f);
            Material chair = Mat("Desk_Chair", Palette.Fabric, 0.1f);
            Material screen = Emissive("Desk_Screen", new Color(0.35f, 0.6f, 0.95f), 0.8f);
            Material bar = Emissive("Desk_StatusBar", Palette.Teal, 1.6f);
            Material mug = Mat("Desk_Mug", new Color(0.95f, 0.95f, 0.93f), 0.6f);

            var poses = WorkspaceLayout.Generate(LayoutType.Grid, WorkstationCount, Vector3.zero);
            var list = new List<Workstation>();
            for (int i = 0; i < WorkstationCount; i++)
            {
                var root = Empty($"Workstation_{i + 1}", parent, poses[i].position).transform;
                root.rotation = poses[i].rotation;

                Box("DeskTop", root, new Vector3(0f, 0.74f, 0f), new Vector3(1.4f, 0.04f, 0.72f), oak);
                Box("SideL", root, new Vector3(-0.67f, 0.36f, 0f), new Vector3(0.04f, 0.72f, 0.66f), charcoal);
                Box("SideR", root, new Vector3(0.67f, 0.36f, 0f), new Vector3(0.04f, 0.72f, 0.66f), charcoal);
                Box("Modesty", root, new Vector3(0f, 0.47f, 0.33f), new Vector3(1.3f, 0.44f, 0.02f), charcoal);
                Cyl("MonitorStand", root, new Vector3(0f, 0.9f, 0.22f), 0.03f, 0.3f, charcoal, false);
                Box("Monitor", root, new Vector3(0f, 1.13f, 0.24f), new Vector3(0.68f, 0.41f, 0.035f), charcoal);
                var scr = Box("Screen", root, new Vector3(0f, 1.13f, 0.221f), new Vector3(0.64f, 0.37f, 0.004f), screen, false);
                var statusBar = Box("StatusBar", root, new Vector3(0f, 1.355f, 0.24f), new Vector3(0.68f, 0.035f, 0.05f), bar, false);
                Box("Keyboard", root, new Vector3(0f, 0.765f, -0.12f), new Vector3(0.42f, 0.015f, 0.14f), charcoal, false);
                Cyl("Mug", root, new Vector3(0.5f, 0.81f, -0.08f), 0.04f, 0.1f, mug, false);

                var cushions = BuildChair(root, new Vector3(0f, 0f, -0.62f), Quaternion.identity, chair, charcoal);

                var plateCanvas = WorldCanvas("Nameplate", root, new Vector3(0f, 0.6f, 0.345f), new Vector3(0f, 180f, 0f), new Vector2(1000f, 160f), 0.001f);
                var plate = Text("Name", plateCanvas.transform, "AGENT", 110f, new Color(0.95f, 0.95f, 0.95f), Vector2.zero, new Vector2(1000f, 160f), TextAlignmentOptions.Center, true);
                plate.characterSpacing = 12f;

                var seatPoint = Empty("SeatPoint", root, new Vector3(0f, 0f, -0.62f));
                var approach = Empty("ApproachPoint", root, new Vector3(0f, 0f, -1.3f));

                var ws = root.gameObject.AddComponent<Workstation>();
                Wire(ws, "seatPoint", seatPoint.transform);
                Wire(ws, "approachPoint", approach.transform);
                Wire(ws, "screen", scr.GetComponent<Renderer>());
                Wire(ws, "statusBar", statusBar.GetComponent<Renderer>());
                WireArray(ws, "chairRenderers", cushions.Cast<Object>().ToArray());
                Wire(ws, "nameplate", plate);

                var obstacle = root.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = new Vector3(0f, 0.5f, 0f);
                obstacle.size = new Vector3(1.42f, 1f, 0.74f);
                obstacle.carving = true;
                root.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

                var hit = root.gameObject.AddComponent<BoxCollider>();
                hit.center = new Vector3(0f, 0.55f, -0.1f);
                hit.size = new Vector3(1.45f, 1.1f, 1.2f);
                AddRayTarget(root.gameObject, hit);
                var drag = root.gameObject.AddComponent<FloorDragInteractable>();
                Wire(drag, "workstation", ws);
                list.Add(ws);
            }
            return list;
        }

        /// <summary>
        /// Task chair: five-star base with casters, gas lift, cushioned seat, curved back and armrests.
        /// Origin on the floor under the seat centre; the sitter faces +Z. Returns the cushion renderers (tinted per agent).
        /// </summary>
        private static Renderer[] BuildChair(Transform parent, Vector3 localPos, Quaternion localRot, Material fabric, Material frame)
        {
            var root = Empty("Chair", parent, localPos).transform;
            root.localRotation = localRot;
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Box($"Leg_{i}", root, dir * 0.15f + Vector3.up * 0.06f, new Vector3(0.04f, 0.03f, 0.3f), frame, false, new Vector3(0f, a, 0f));
                Ball($"Caster_{i}", root, dir * 0.29f + Vector3.up * 0.03f, Vector3.one * 0.05f, frame);
            }
            Cyl("GasLift", root, new Vector3(0f, 0.24f, 0f), 0.025f, 0.34f, frame, false);
            Cyl("SeatPlate", root, new Vector3(0f, 0.41f, 0f), 0.16f, 0.03f, frame, false);
            var seat = Ball("Seat", root, new Vector3(0f, 0.45f, 0.01f), new Vector3(0.5f, 0.09f, 0.48f), fabric);
            Box("Spine", root, new Vector3(0f, 0.55f, -0.25f), new Vector3(0.05f, 0.2f, 0.03f), frame, false, new Vector3(-8f, 0f, 0f));
            var back = Ball("Back", root, new Vector3(0f, 0.8f, -0.27f), new Vector3(0.46f, 0.5f, 0.09f), fabric);
            back.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            foreach (int side in new[] { -1, 1 })
            {
                Box(side < 0 ? "ArmPostL" : "ArmPostR", root, new Vector3(side * 0.23f, 0.53f, -0.02f), new Vector3(0.03f, 0.16f, 0.03f), frame, false);
                Box(side < 0 ? "ArmPadL" : "ArmPadR", root, new Vector3(side * 0.23f, 0.62f, 0.0f), new Vector3(0.06f, 0.025f, 0.24f), frame, false);
            }
            NoShadows(root.gameObject);
            return new[] { seat.GetComponent<Renderer>(), back.GetComponent<Renderer>() };
        }

        // ══ Discussion room ══════════════════════════════════════════════════

        private static MeetingRoom BuildMeetingRoom(Transform walls)
        {
            Material glass = Glass("Glass_Clear", new Color(0.85f, 0.93f, 0.97f), 0.18f);
            Material frame = Mat("Env_Charcoal", Palette.Charcoal, 0.3f);
            Material oak = Mat("Env_Oak", Palette.Oak, 0.35f);
            Material chair = Mat("Desk_Chair", Palette.Fabric, 0.1f);
            Material screenMat = Emissive("Meeting_Screen", new Color(0.3f, 0.5f, 0.8f), 0.7f);

            const float x0 = 7.2f, x1 = 11.95f, z0 = -8.95f, z1 = -3.2f;
            const float h = 2.7f;
            // West glass wall.
            Box("GlassWest", walls, new Vector3(x0, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.03f, h, z1 - z0), glass);
            // North glass wall with a door gap between x = 8.9 and 10.4.
            Box("GlassNorthA", walls, new Vector3((9.3f + x1) * 0.5f, h * 0.5f, z1), new Vector3(x1 - 9.3f, h, 0.03f), glass);
            Box("GlassNorthB", walls, new Vector3((x0 + 7.9f) * 0.5f, h * 0.5f, z1), new Vector3(7.9f - x0, h, 0.03f), glass);
            Box("DoorHeader", walls, new Vector3(8.6f, h - 0.1f, z1), new Vector3(1.4f, 0.2f, 0.06f), frame, false);
            foreach (var p in new[] { new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0 + 0.1f), new Vector3(7.9f, 0f, z1), new Vector3(9.3f, 0f, z1), new Vector3(x1 - 0.05f, 0f, z1) })
                Box("Post", walls, p + Vector3.up * h * 0.5f, new Vector3(0.07f, h, 0.07f), frame, false);
            Box("TopRailWest", walls, new Vector3(x0, h, (z0 + z1) * 0.5f), new Vector3(0.07f, 0.07f, z1 - z0), frame, false);
            Box("TopRailNorth", walls, new Vector3((x0 + x1) * 0.5f, h, z1), new Vector3(x1 - x0, 0.07f, 0.07f), frame, false);

            var room = Empty("DiscussionRoom", null, new Vector3(9.6f, 0f, -6.1f)).transform;
            Cyl("TableTop", room, new Vector3(0f, 0.74f, 0f), 1.05f, 0.05f, oak);
            Cyl("TablePedestal", room, new Vector3(0f, 0.36f, 0f), 0.16f, 0.72f, frame);
            Cyl("TableFoot", room, new Vector3(0f, 0.02f, 0f), 0.5f, 0.04f, frame, false);
            Box("WallScreen", room, new Vector3(2.3f, 1.6f, 0f), new Vector3(0.04f, 1.1f, 1.9f), screenMat, false);

            var label = WorldCanvas("Sign", room, new Vector3(-1.0f, 2.95f, 2.94f), new Vector3(0f, 180f, 0f), new Vector2(1600f, 220f), 0.001f);
            Text("Text", label.transform, "DISCUSSION ROOM", 150f, Palette.Charcoal, Vector2.zero, new Vector2(1600f, 220f), TextAlignmentOptions.Center, true).characterSpacing = 10f;

            var seats = new List<Object>();
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f + 30f) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.6f;
                Quaternion face = Quaternion.LookRotation(-p.normalized);
                var seatRoot = Empty($"Seat_{i}", room, p).transform;
                seatRoot.localRotation = face;
                BuildChair(seatRoot, Vector3.zero, Quaternion.identity, chair, frame);
                seats.Add(seatRoot);
            }

            var meeting = room.gameObject.AddComponent<MeetingRoom>();
            WireArray(meeting, "seats", seats.ToArray());
            return meeting;
        }

        // ══ Lighting ═════════════════════════════════════════════════════════

        private static Light BuildLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(42f, 64f, 0f);
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.95f, 0.96f, 1.0f);
            RenderSettings.ambientEquatorColor = new Color(0.84f, 0.81f, 0.77f);
            RenderSettings.ambientGroundColor = new Color(0.70f, 0.66f, 0.62f);
            RenderSettings.sun = sun;
            RenderSettings.fog = false;
            return sun;
        }

        // ══ Desk UI ══════════════════════════════════════════════════════════

        private static VirtualKeyboardManager BuildKeyboard(Transform deskRoot, ReviewDesk desk, TextInputService service)
        {
            var root = Empty("SurfaceKeyboard", deskRoot, new Vector3(0f, DeskTopY + 0.07f, 1.13f), new Vector3(68f, 0f, 0f)).transform;
            var canvas = WorldCanvas("KeyboardCanvas", root, Vector3.zero, Vector3.zero, new Vector2(680f, 420f), 0.001f);
            Transform t = canvas.transform;
            Panel("Backplate", t, Vector2.zero, new Vector2(680f, 420f), new Color(0.07f, 0.08f, 0.1f, 0.96f));
            var prompt = Text("Prompt", t, "", 19f, Palette.Teal, new Vector2(-90f, 186f), new Vector2(460f, 36f), TextAlignmentOptions.Left, true);
            var metaKb = Button("MetaKeyboard", t, "META KEYBOARD", new Vector2(240f, 186f), new Vector2(160f, 36f), Palette.UiBlue, out _, 14f);
            var hint = Text("Hint", t, "", 14f, Palette.UiMuted, new Vector2(0f, 107f), new Vector2(640f, 20f), TextAlignmentOptions.Center);
            prompt.enableAutoSizing = true; prompt.fontSizeMin = 13f; prompt.fontSizeMax = 19f;
            Panel("InputBox", t, new Vector2(0f, 140f), new Vector2(640f, 52f), new Color(0.15f, 0.17f, 0.21f));
            var input = Text("Input", t, "", 22f, Palette.UiText, new Vector2(0f, 140f), new Vector2(620f, 48f), TextAlignmentOptions.Left);

            var keysRoot = new GameObject("Keys", typeof(RectTransform)).transform;
            keysRoot.SetParent(t, false);
            var useDesk = Button("UseDeskKeyboard", t, "TYPE ON THE DESK INSTEAD", new Vector2(0f, 40f), new Vector2(420f, 60f), new Color(0.25f, 0.28f, 0.34f), out _, 18f);

            var kb = root.gameObject.AddComponent<VirtualKeyboardManager>();
            var keys = new List<Object>();
            string[] rows = { "1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm,.?" };
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    string ch = rows[r][c].ToString();
                    keys.Add(Key(keysRoot, ch, ch, new Vector2(-283f + c * 63f + (r == 2 ? 10f : 0f), 76f - r * 56f), new Vector2(57f, 50f),
                        VirtualKeyboardButton.ActionType.Character, Palette.UiButton));
                }
            float y = 76f - 4 * 56f;
            keys.Add(Key(keysRoot, "SHIFT", "", new Vector2(-262f, y), new Vector2(100f, 50f), VirtualKeyboardButton.ActionType.Shift, new Color(0.25f, 0.28f, 0.34f)));
            keys.Add(Key(keysRoot, "SPACE", "", new Vector2(-62f, y), new Vector2(286f, 50f), VirtualKeyboardButton.ActionType.Space, Palette.UiButton));
            keys.Add(Key(keysRoot, "DEL", "", new Vector2(128f, y), new Vector2(80f, 50f), VirtualKeyboardButton.ActionType.Backspace, new Color(0.25f, 0.28f, 0.34f)));
            keys.Add(Key(keysRoot, "ESC", "", new Vector2(214f, y), new Vector2(76f, 50f), VirtualKeyboardButton.ActionType.Cancel, new Color(0.45f, 0.22f, 0.22f)));
            keys.Add(Key(keysRoot, "SEND", "", new Vector2(296f, y), new Vector2(76f, 50f), VirtualKeyboardButton.ActionType.Submit, new Color(0.16f, 0.55f, 0.48f)));

            Wire(kb, "visualRoot", canvas.gameObject);
            Wire(kb, "keysRoot", keysRoot.gameObject);
            Wire(kb, "useDeskButton", useDesk);
            Wire(kb, "metaKeyboardButton", metaKb);
            Wire(kb, "hintText", hint);
            Wire(kb, "service", service);
            Wire(kb, "promptText", prompt);
            Wire(kb, "inputText", input);
            Wire(kb, "desk", desk);
            WireArray(kb, "keys", keys.ToArray());
            return kb;
        }

        private static VirtualKeyboardButton Key(Transform parent, string label, string ch, Vector2 pos, Vector2 size,
            VirtualKeyboardButton.ActionType action, Color color)
        {
            var b = Button($"Key_{label}", parent, label, pos, size, color, out var text, label.Length > 1 ? 17f : 24f);
            var key = b.gameObject.AddComponent<VirtualKeyboardButton>();
            var so = new SerializedObject(key);
            so.FindProperty("character").stringValue = ch;
            so.FindProperty("action").enumValueIndex = (int)action;
            so.FindProperty("label").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();
            return key;
        }

        private static Canvas BuildConsole(Transform deskRoot, HandGestureRecognizer recognizer)
        {
            var canvas = WorldCanvas("ReviewConsole", deskRoot, new Vector3(0f, DeskTopY + 0.075f, 1.56f), new Vector3(28f, 0f, 0f), new Vector2(740f, 170f), 0.001f);
            Transform t = canvas.transform;
            Panel("Background", t, Vector2.zero, new Vector2(740f, 170f), Palette.UiPanel);
            var scrollUp = Button("ScrollUp", t, "", new Vector2(330f, 20f), new Vector2(56f, 48f), Palette.UiButton, out _);
            Icon("Up", scrollUp.transform, "Icons/chevron-right", Vector2.zero, 24f, Color.white).rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var scrollDown = Button("ScrollDown", t, "", new Vector2(330f, -32f), new Vector2(56f, 48f), Palette.UiButton, out _);
            Icon("Down", scrollDown.transform, "Icons/chevron-right", Vector2.zero, 24f, Color.white).rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            var headline = Text("Headline", t, "All clear", 26f, Palette.UiText, new Vector2(-90f, 56f), new Vector2(440f, 40f), TextAlignmentOptions.Left, true);
            var subline = Text("Subline", t, "", 18f, Palette.UiMuted, new Vector2(200f, 56f), new Vector2(220f, 40f), TextAlignmentOptions.Right);

            var buttons = new List<Object>();
            var labels = new List<Object>();
            for (int i = 0; i < 5; i++)
            {
                var b = Button($"Action_{i}", t, "ACTION", new Vector2(-240f + i * 120f, -4f), new Vector2(112f, 60f), Palette.UiButton, out var label, 18f);
                label.enableAutoSizing = true; label.fontSizeMin = 12f; label.fontSizeMax = 19f;
                buttons.Add(b);
                labels.Add(label);
            }

            Panel("GestureTrack", t, new Vector2(0f, -58f), new Vector2(600f, 10f), new Color(0.2f, 0.22f, 0.26f));
            var fill = Panel("GestureFill", t, new Vector2(0f, -58f), new Vector2(600f, 10f), Palette.Teal, false);
            fill.sprite = Rounded;   // Filled images need a sprite, or the bar always draws full
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            var gestureLabel = Text("GestureLabel", t, "", 16f, Palette.Teal, new Vector2(0f, -74f), new Vector2(600f, 22f), TextAlignmentOptions.Center);

            var ui = canvas.gameObject.AddComponent<ManagerConsoleUI>();
            Wire(ui, "headline", headline);
            Wire(ui, "subline", subline);
            WireArray(ui, "actionButtons", buttons.ToArray());
            WireArray(ui, "actionLabels", labels.ToArray());
            Wire(ui, "gestureFill", fill);
            Wire(ui, "gestureLabel", gestureLabel);
            Wire(ui, "gestures", recognizer);
            Wire(ui, "scrollUp", scrollUp);
            Wire(ui, "scrollDown", scrollDown);
            return canvas;
        }

        private static Canvas BuildLayoutPanel(Transform deskRoot, PassthroughToggle passthrough, SettingsWindow settings)
        {
            var monitor = Monitor("WorkspaceMonitor", deskRoot, new Vector3(-0.84f, DeskTopY + 0.4f, 1.1f), new Vector3(6f, -34f, 0f));
            var canvas = WorldCanvas("WorkspacePanel", monitor, Vector3.zero, Vector3.zero, new Vector2(480f, 560f), 0.001f);
            Bezel(canvas);
            AddWindowChrome(monitor.gameObject, canvas, "workspace", "Workspace", -0.335f, false, true);
            Panel("Background", canvas.transform, Vector2.zero, new Vector2(480f, 560f), Palette.UiPanel);
            Transform t = View("MainView", canvas.transform);
            Text("Title", t, "WORKSPACE", 22f, Palette.Teal, new Vector2(0f, 250f), new Vector2(440f, 32f), TextAlignmentOptions.Left, true).characterSpacing = 6f;
            var current = Text("Current", t, "Layout: Classroom", 21f, Palette.UiText, new Vector2(0f, 217f), new Vector2(440f, 32f));

            var brief = Button("Brief", t, "BRIEF THE TEAM", new Vector2(0f, 158f), new Vector2(440f, 64f), Palette.UiPrimary, out _, 23f);
            AddIcon(brief, "Icons/command-center", 30f);

            // Layout and room theme as compact dropdown rows.
            Button Dropdown(string name, string label, float y, out TextMeshProUGUI value)
            {
                Text(name + "Label", t, label, 19f, Palette.UiMuted, new Vector2(-155f, y), new Vector2(130f, 40f), TextAlignmentOptions.Left);
                var b = Button(name, t, "", new Vector2(70f, y), new Vector2(300f, 48f), Palette.UiPanelRaised, out value, 18f);
                value.alignment = TextAlignmentOptions.Left;
                value.rectTransform.offsetMin = new Vector2(18f, 4f);
                var chev = Icon("Chevron", b.transform, "Icons/chevron-right", new Vector2(126f, 0f), 20f, Palette.UiMuted);
                chev.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
                return b;
            }
            var layoutDrop = Dropdown("LayoutDropdown", "Layout", 90f, out var layoutValue);
            var themeDrop = Dropdown("ThemeDropdown", "Room theme", 34f, out var themeValue);

            Button Cell(string name, string label, int col, float y, out TextMeshProUGUI l) =>
                Button(name, t, label, new Vector2(col == 0 ? -113f : 113f, y), new Vector2(214f, 50f), Palette.UiButton, out l, 17f);
            var addAgent = Cell("AddAgent", "ADD AGENT", 0, -30f, out var addAgentLabel);
            var meeting = Cell("Meeting", "DISCUSSION", 1, -30f, out var meetingLabel);
            var hold = Cell("HoldQueue", "HOLD QUEUE", 0, -88f, out var holdLabel);
            var auto = Cell("DemoMode", "DEMO: ON", 1, -88f, out var autoLabel);
            var mr = Cell("Passthrough", "MIXED REALITY", 0, -146f, out var mrLabel);
            var history = Cell("History", "HISTORY", 1, -146f, out _);
            var tour = Cell("Tour", "TUTORIAL", 0, -204f, out _);
            var settingsBtn = Cell("Settings", "SETTINGS", 1, -204f, out _);
            AddIcon(settingsBtn, "Icons/settings", 22f);
            AddIcon(history, "Icons/recently-played", 22f);

            var ui = canvas.gameObject.AddComponent<LayoutSelectorUI>();
            Wire(ui, "layoutDropdown", layoutDrop);
            Wire(ui, "layoutValue", layoutValue);
            Wire(ui, "themeDropdown", themeDrop);
            Wire(ui, "themeValue", themeValue);
            Wire(ui, "historyButton", history);
            Wire(ui, "tourButton", tour);
            Wire(ui, "currentLabel", current);
            Wire(ui, "briefButton", brief);
            Wire(ui, "meetingButton", meeting);
            Wire(ui, "meetingLabel", meetingLabel);
            Wire(ui, "autoTasksButton", auto);
            Wire(ui, "autoTasksLabel", autoLabel);
            Wire(ui, "passthroughButton", mr);
            Wire(ui, "passthroughLabel", mrLabel);
            Wire(ui, "passthrough", passthrough);
            Wire(ui, "holdButton", hold);
            Wire(ui, "holdLabel", holdLabel);
            Wire(ui, "settingsButton", settingsBtn);
            Wire(ui, "settings", settings);
            Wire(ui, "addAgentButton", addAgent);
            Wire(ui, "addAgentLabel", addAgentLabel);
            return canvas;
        }

        // ══ Grabbable desk objects ═════════════════════════════════════════════

        private static TextMeshPro Label3D(Transform parent, string text, Vector3 pos, float size, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);   // lies back, readable from the manager's side
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(0.2f, 0.04f);
            if (UiFont.Bold != null) t.font = UiFont.Bold;
            return t;
        }

        /// <summary>A queue bell on the desk: tap it to pause / resume the line.</summary>
        private static void BuildDeskObjects(Transform deskRoot)
        {
            Material brass = Mat("Bell_Brass", new Color(0.86f, 0.68f, 0.32f), 0.85f, 0.9f);
            Material dark = Mat("Bell_Base", new Color(0.1f, 0.1f, 0.11f), 0.6f, 0.3f);
            Material glow = Emissive("Bell_Glow", Palette.Teal, 1.6f);

            var bell = Empty("QueueBell", deskRoot, new Vector3(-0.5f, DeskTopY, 1.42f)).transform;
            Cyl("Base", bell, new Vector3(0f, 0.008f, 0f), 0.05f, 0.016f, dark, false);
            var ring = Cyl("GlowRing", bell, new Vector3(0f, 0.017f, 0f), 0.052f, 0.003f, glow, false);
            Ball("Dome", bell, new Vector3(0f, 0.03f, 0f), new Vector3(0.085f, 0.05f, 0.085f), brass);
            var plunger = Cyl("Plunger", bell, new Vector3(0f, 0.062f, 0f), 0.007f, 0.018f, brass, false).transform;
            Label3D(bell, "QUEUE", new Vector3(0f, 0.012f, -0.065f), 0.14f, Palette.UiMuted);
            var b = bell.gameObject.AddComponent<DeskBell>();
            Wire(b, "button", plunger);
            Wire(b, "ring", ring.GetComponent<Renderer>());
            NoShadows(ring);
        }

        // ══ Windows (desk monitors and the settings window) ═══════════════════

        private static readonly List<Canvas> WindowBars = new List<Canvas>();

        /// <summary>A desk monitor: a holder you can move, with a slim dark bezel behind the screen.</summary>
        private static Transform Monitor(string name, Transform deskRoot, Vector3 pos, Vector3 euler)
        {
            return Empty(name, deskRoot, pos, euler).transform;
        }

        /// <summary>Slim dark bezel behind a monitor screen; lives inside the canvas so it slides with it.</summary>
        private static void Bezel(Canvas canvas)
        {
            Material bezel = Mat("Monitor_Bezel", new Color(0.05f, 0.055f, 0.06f), 0.6f, 0.2f);
            var frame = Box("Bezel", canvas.transform, new Vector3(0f, 0f, 12f), new Vector3(500f, 580f, 16f), bezel, false);
            NoShadows(frame);
        }

        /// <summary>Adds the Horizon-style window bar (move / size / snap / minimize / close) under a window.</summary>
        private static void AddWindowChrome(GameObject holder, Canvas content, string id, string title, float barY, bool closable, bool slide)
        {
            var bar = WorldCanvas("WindowBar", holder.transform, new Vector3(0f, barY, -0.012f), Vector3.zero, new Vector2(440f, 150f), 0.001f);
            var grab = new GameObject("GrabHandle");
            grab.transform.SetParent(bar.transform, false);
            grab.transform.localPosition = new Vector3(0f, 0f, -6f);
            var col = grab.AddComponent<BoxCollider>();
            col.size = new Vector3(140f, 44f, 6f);
            AddRayTarget(grab, col);
            var chrome = holder.AddComponent<WindowChrome>();
            var so = new SerializedObject(chrome);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("title").stringValue = title;
            so.FindProperty("barOffsetY").floatValue = barY;
            so.FindProperty("closable").boolValue = closable;
            so.FindProperty("slideIntoDesk").boolValue = slide;
            so.ApplyModifiedPropertiesWithoutUndo();
            Wire(chrome, "content", content.gameObject);
            Wire(chrome, "bar", bar);
            Wire(chrome, "grabHandle", grab.transform);
            WindowBars.Add(bar);

            // Desk monitors resize freely in width and height (the settings window reflows on its own).
            if (slide)
            {
                var resp = holder.AddComponent<ResponsiveWindow>();
                var rso = new SerializedObject(resp);
                rso.FindProperty("id").stringValue = id;
                rso.FindProperty("canvas").objectReferenceValue = content.GetComponent<RectTransform>();
                rso.FindProperty("minSize").vector2Value = new Vector2(380f, 440f);
                rso.FindProperty("maxSize").vector2Value = new Vector2(960f, 1000f);
                rso.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ══ Settings window, tutorial, notices ═══════════════════════════════

        private static Canvas BuildSettingsWindow(Transform deskRoot, PassthroughToggle passthrough, out SettingsWindow window)
        {
            var holder = Empty("SettingsWindow", deskRoot, new Vector3(0f, DeskTopY + 0.5f, 1.2f), new Vector3(10f, 0f, 0f));
            var canvas = WorldCanvas("SettingsCanvas", holder.transform, Vector3.zero, Vector3.zero, new Vector2(1100f, 680f), 0.001f);
            window = holder.AddComponent<SettingsWindow>();
            Wire(window, "root", canvas.gameObject);
            Wire(window, "passthrough", passthrough);
            AddWindowChrome(holder, canvas, "settings", "Settings", -0.385f, true, false);
            return canvas;
        }

        private static Canvas BuildTutorial(Transform deskRoot, out TutorialManager tutorial)
        {
            var holder = Empty("Tutorial", deskRoot, new Vector3(0f, DeskTopY, 0f));
            var panel = Empty("Panel", holder.transform, new Vector3(0f, 0.36f, 0.98f), new Vector3(18f, 0f, 0f));
            var canvas = WorldCanvas("TutorialCanvas", panel.transform, Vector3.zero, Vector3.zero, new Vector2(780f, 470f), 0.001f);
            tutorial = holder.AddComponent<TutorialManager>();
            Wire(tutorial, "root", canvas.gameObject);
            Wire(tutorial, "panel", panel.transform);
            return canvas;
        }

        private static void BuildControllerNotice(InputModeManager manager)
        {
            var holder = new GameObject("HandsOnlyNotice");
            var canvas = WorldCanvas("Canvas", holder.transform, Vector3.zero, Vector3.zero, new Vector2(620f, 300f), 0.001f);
            Transform t = canvas.transform;
            Panel("Background", t, Vector2.zero, new Vector2(620f, 300f), Palette.UiPanel);
            Icon("Hand", t, "Icons/UserIcon", new Vector2(-250f, 92f), 44f, Palette.Teal);
            Text("Title", t, "This workspace is built for your hands", 26f, Palette.UiText, new Vector2(30f, 92f), new Vector2(500f, 40f), TextAlignmentOptions.Left, true);
            Text("Body", t,
                "Put your controllers down: hand tracking takes over in a moment.\n\n" +
                "Prefer controllers? Hold <b>both triggers</b> for 2 seconds, or turn on <b>Controller mode</b> in Settings > Hands & Input.\n" +
                "<color=#9AA3B2>(If hands don't appear, enable \"Hand and body tracking\" with auto-switch in the Quest settings.)</color>",
                17f, Palette.UiMuted, new Vector2(0f, -14f), new Vector2(560f, 150f), TextAlignmentOptions.TopLeft);
            Panel("Track", t, new Vector2(0f, -122f), new Vector2(560f, 8f), new Color(1f, 1f, 1f, 0.08f));
            var fill = Panel("Fill", t, new Vector2(0f, -122f), new Vector2(560f, 8f), Palette.Teal);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            Wire(manager, "notice", holder);
            Wire(manager, "holdFill", fill);
        }

        private static void BuildGestureRing(HandGestureRecognizer recognizer)
        {
            var holder = new GameObject("GestureRing");
            var canvas = WorldCanvas("Canvas", holder.transform, Vector3.zero, Vector3.zero, new Vector2(120f, 120f), 0.0006f);
            var group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Image Circle(string n, float size, Color c)
            {
                var img = Panel(n, canvas.transform, Vector2.zero, new Vector2(size, size), c, false);
                img.sprite = knob;
                return img;
            }
            Circle("Back", 120f, new Color(0.05f, 0.06f, 0.07f, 0.75f));
            var fill = Circle("Fill", 120f, Palette.Teal);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillAmount = 0f;
            Circle("Hole", 100f, new Color(0.07f, 0.08f, 0.1f, 0.95f));
            var icon = Icon("Icon", canvas.transform, "ThumbsUp", Vector2.zero, 64f, Color.white);
            var ring = holder.AddComponent<GestureRing>();
            Wire(ring, "recognizer", recognizer);
            Wire(ring, "group", group);
            Wire(ring, "fill", fill);
            Wire(ring, "icon", icon);
            Wire(ring, "thumbsUp", IconTexture("ThumbsUp"));
            Wire(ring, "thumbsDown", IconTexture("ThumbsDown"));
            Wire(ring, "palm", IconTexture("HandStop"));
        }

        private static void BuildUiKitAssets(GameObject host)
        {
            var kit = host.AddComponent<UiKitAssets>();
            var so = new SerializedObject(kit);
            so.FindProperty("medium").objectReferenceValue = UiFont.Medium;
            so.FindProperty("bold").objectReferenceValue = UiFont.Bold;
            so.FindProperty("rounded").objectReferenceValue = Rounded;
            var map = new (string name, string path)[]
            {
                ("settings", "Icons/settings"), ("user", "Icons/UserIcon"), ("UserIcon", "Icons/UserIcon"), ("chat", "Icons/chat"),
                ("command-center", "Icons/command-center"), ("category-all", "Icons/category-all"), ("volume-on", "Icons/volume-on"),
                ("recently-played", "Icons/recently-played"), ("more-horizontal", "Icons/more-horizontal"), ("check-alt", "Icons/check-alt"),
                ("Arrow", "Icons/Arrow"), ("warning", "Icons/warning"), ("ThumbsUp", "ThumbsUp"), ("ThumbsDown", "ThumbsDown"),
                ("HandStop", "HandStop"), ("Teleport-Hands", "Teleport-Hands"), ("Plus", "Icons/Plus"), ("Minus", "Icons/Minus"),
                ("chevron-right", "Icons/chevron-right")
            };
            var list = so.FindProperty("icons");
            list.arraySize = map.Length;
            for (int i = 0; i < map.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = map[i].name;
                e.FindPropertyRelative("texture").objectReferenceValue = IconTexture(map[i].path);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Full-size child group of a panel, so a panel can swap between pages.</summary>
        private static Transform View(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt);
            return rt;
        }

        private static Canvas BuildDetailPanel(Transform deskRoot)
        {
            var monitor = Monitor("AgentMonitor", deskRoot, new Vector3(0.84f, DeskTopY + 0.4f, 1.1f), new Vector3(6f, 34f, 0f));
            var canvas = WorldCanvas("AgentCard", monitor, Vector3.zero, Vector3.zero, new Vector2(480f, 560f), 0.001f);
            Bezel(canvas);
            AddWindowChrome(monitor.gameObject, canvas, "agents", "Agents", -0.335f, false, true);
            Transform t = canvas.transform;
            Panel("Background", t, Vector2.zero, new Vector2(480f, 560f), Palette.UiPanel);
            var accent = Panel("Accent", t, new Vector2(0f, 271f), new Vector2(480f, 18f), Palette.Teal);
            var picker = Button("AgentPicker", t, "Choose an agent", new Vector2(0f, 232f), new Vector2(440f, 46f), Palette.UiPanelRaised, out var pickerLabel, 18f);
            pickerLabel.alignment = TextAlignmentOptions.Left;
            pickerLabel.rectTransform.offsetMin = new Vector2(18f, 4f);
            var chevron = Icon("Chevron", picker.transform, "Icons/chevron-right", new Vector2(196f, 0f), 22f, Palette.UiMuted);
            chevron.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            var name = Text("Name", t, "", 34f, Palette.UiText, new Vector2(-60f, 190f), new Vector2(320f, 50f), TextAlignmentOptions.Left, true);
            var pill = Panel("StatusPill", t, new Vector2(150f, 190f), new Vector2(150f, 40f), Palette.Teal);
            var status = Text("Status", pill.transform, "", 16f, new Color(0.07f, 0.08f, 0.1f), Vector2.zero, new Vector2(150f, 40f), TextAlignmentOptions.Center, true);
            var body = Text("Body", t, "", 21f, Palette.UiText, new Vector2(0f, 35f), new Vector2(440f, 240f), TextAlignmentOptions.TopLeft);
            body.enableAutoSizing = true; body.fontSizeMin = 14f; body.fontSizeMax = 21f;
            var assign = Button("Assign", t, "ASSIGN TASK", new Vector2(-50f, -140f), new Vector2(340f, 58f), new Color(0.16f, 0.55f, 0.48f), out var assignLabel);
            var close = Button("Close", t, "X", new Vector2(176f, -140f), new Vector2(88f, 58f), new Color(0.3f, 0.32f, 0.37f), out _);
            var role = Button("Role", t, "ROLE", new Vector2(-168f, -210f), new Vector2(104f, 54f), Palette.UiButton, out _, 15f);
            var brain = Button("Brain", t, "BRAIN", new Vector2(-56f, -210f), new Vector2(112f, 54f), Palette.UiBlue, out var brainLabel, 13f);
            brainLabel.enableAutoSizing = true; brainLabel.fontSizeMin = 9f; brainLabel.fontSizeMax = 13f;
            var next = Button("ReviewNext", t, "REVIEW NEXT", new Vector2(56f, -210f), new Vector2(104f, 54f), Palette.UiWarn, out _, 13f);
            var remove = Button("Remove", t, "REMOVE", new Vector2(168f, -210f), new Vector2(104f, 54f), Palette.UiDanger, out var removeLabel, 15f);

            var ui = canvas.gameObject.AddComponent<AgentDetailPanel>();
            Wire(ui, "accentBar", accent);
            Wire(ui, "statusPill", pill);
            Wire(ui, "nameText", name);
            Wire(ui, "statusText", status);
            Wire(ui, "bodyText", body);
            Wire(ui, "assignButton", assign);
            Wire(ui, "assignLabel", assignLabel);
            Wire(ui, "roleButton", role);
            Wire(ui, "nextButton", next);
            Wire(ui, "closeButton", close);
            var pickerList = new GameObject("AgentList", typeof(RectTransform)).GetComponent<RectTransform>();
            pickerList.SetParent(t, false);
            pickerList.anchoredPosition = new Vector2(0f, -22f);
            pickerList.sizeDelta = new Vector2(456f, 470f);
            pickerList.SetAsLastSibling();
            Wire(ui, "pickerButton", picker);
            Wire(ui, "pickerLabel", pickerLabel);
            Wire(ui, "pickerList", pickerList);
            Wire(ui, "removeButton", remove);
            Wire(ui, "removeLabel", removeLabel);
            Wire(ui, "brainButton", brain);
            Wire(ui, "brainLabel", brainLabel);
            return canvas;
        }

        private static Canvas BuildHandMenu(PassthroughToggle passthrough)
        {
            var root = new GameObject("WristMenu");
            var canvas = WorldCanvas("Panel", root.transform, Vector3.zero, Vector3.zero, new Vector2(320f, 376f), 0.0007f);
            Transform t = canvas.transform;
            Panel("Background", t, Vector2.zero, new Vector2(320f, 376f), Palette.UiPanel);
            Text("Title", t, "AI MANAGER", 18f, Palette.Teal, new Vector2(0f, 166f), new Vector2(290f, 28f), TextAlignmentOptions.Center, true).characterSpacing = 6f;
            Button Cell(string name, string label, int col, int row, Color c, out TextMeshProUGUI l) =>
                Button(name, t, label, new Vector2(col == 0 ? -76f : 76f, 112f - row * 76f), new Vector2(144f, 66f), c, out l, 17f);
            var down = Cell("HeightDown", "HEIGHT  -", 0, 0, Palette.UiButton, out _);
            var up = Cell("HeightUp", "HEIGHT  +", 1, 0, Palette.UiButton, out _);
            var auto = Cell("AutoHeight", "AUTO HEIGHT", 0, 1, Palette.UiPrimary, out _);
            var desk = Cell("Desk", "BACK TO DESK", 1, 1, Palette.UiButton, out _);
            var mr = Cell("MR", "MIXED REALITY", 0, 2, Palette.UiButton, out _);
            var fit = Cell("Settings", "SETTINGS", 1, 2, Palette.UiButton, out _);
            var holdBtn = Button("Hold", t, "HOLD QUEUE", new Vector2(0f, 112f - 3 * 76f), new Vector2(296f, 66f), Palette.UiButton, out var holdText, 17f);

            var menu = root.AddComponent<HandMenu>();
            Wire(menu, "panel", canvas.gameObject);
            Wire(menu, "heightDownButton", down);
            Wire(menu, "heightUpButton", up);
            Wire(menu, "autoHeightButton", auto);
            Wire(menu, "mrButton", mr);
            Wire(menu, "deskButton", desk);
            Wire(menu, "settingsButton", fit);
            Wire(menu, "holdButton", holdBtn);
            Wire(menu, "holdLabel", holdText);
            Wire(menu, "passthrough", passthrough);
            return canvas;
        }

        private static void BuildTeamBoard(Transform parent)
        {
            Material backing = Mat("Env_Charcoal", Palette.Charcoal, 0.3f);
            var root = Empty("TeamBoard", parent, new Vector3(0f, 3.1f, 14.42f)).transform;
            Box("Backing", root, new Vector3(0f, 0f, 0.05f), new Vector3(8.6f, 3.45f, 0.08f), backing, false);
            var canvas = WorldCanvas("BoardCanvas", root, new Vector3(0f, 0f, -0.01f), Vector3.zero, new Vector2(1840f, 720f), 0.0045f);
            Transform t = canvas.transform;
            Panel("Background", t, Vector2.zero, new Vector2(1840f, 720f), new Color(0.08f, 0.09f, 0.11f, 1f));
            var header = Text("Header", t, "", 58f, Palette.UiText, new Vector2(0f, 300f), new Vector2(1760f, 80f), TextAlignmentOptions.Left, true);
            Panel("Rule", t, new Vector2(0f, 252f), new Vector2(1760f, 4f), new Color(0.25f, 0.27f, 0.3f), false);
            var roster = Text("Roster", t, "", 50f, Palette.UiText, new Vector2(0f, -10f), new Vector2(1760f, 500f), TextAlignmentOptions.TopLeft);
            roster.lineSpacing = 12f;
            roster.overflowMode = TextOverflowModes.Truncate;
            var footer = Text("Footer", t, "", 36f, Palette.UiMuted, new Vector2(0f, -310f), new Vector2(1760f, 60f), TextAlignmentOptions.Left);
            footer.enableAutoSizing = true; footer.fontSizeMin = 24f; footer.fontSizeMax = 36f;

            var ui = canvas.gameObject.AddComponent<SessionDashboardUI>();
            Wire(ui, "headerText", header);
            Wire(ui, "rosterText", roster);
            Wire(ui, "footerText", footer);
        }

        // ══ Utilities ════════════════════════════════════════════════════════

        private static void MarkStatic(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.GetComponentInParent<ReviewDesk>(true) == null)
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>The default humanoid agent (radius 0.5) cannot fit the lift gap or desk aisles.</summary>
        private static void SetNavAgentRadius(float radius)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (asset == null || asset.Length == 0) return;
            var so = new SerializedObject(asset[0]);
            var settings = so.FindProperty("m_Settings");
            if (settings == null || settings.arraySize == 0) return;
            var first = settings.GetArrayElementAtIndex(0);
            first.FindPropertyRelative("agentRadius").floatValue = radius;
            first.FindPropertyRelative("agentHeight").floatValue = 1.8f;
            first.FindPropertyRelative("agentClimb").floatValue = 0.25f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/AI Manager/Configure Android Build (Quest 3)")]
        public static void ConfigureAndroidBuild()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.garvchopra.aimanagerworkspace");
            PlayerSettings.productName = "AI Manager Workspace";
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Debug.Log("[SceneBuilder] Android / Quest 3 build settings configured.");
        }
    }
}
