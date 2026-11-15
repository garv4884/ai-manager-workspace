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
        private const int WorkstationCount = 6;

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
            BuildLighting();

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

            var reviewDesk = deskParts.desk;
            var deskRoot = deskParts.root;
            var keyboard = BuildKeyboard(deskRoot, reviewDesk, textInput);
            var canvases = new List<Canvas>
            {
                keyboard.GetComponentInChildren<Canvas>(true),
                BuildConsole(deskRoot, recognizer),
                BuildLayoutPanel(deskRoot, passthrough),
                BuildDetailPanel(deskRoot),
            };
            var handMenu = BuildHandMenu(passthrough);

            // Done tray: point and pinch to open the work history on the workspace panel.
            var settingsUI = Object.FindAnyObjectByType<SettingsPanelUI>(FindObjectsInactive.Include);
            var trayHit = new GameObject("TrayClick");
            trayHit.transform.SetParent(deskParts.tray.transform, false);
            var trayCol = trayHit.AddComponent<BoxCollider>();
            trayCol.size = new Vector3(1.1f, 6f, 1.1f);           // tray is thin; a taller box is easier to hit
            trayCol.center = new Vector3(0f, 2.5f, 0f);
            AddRayTarget(trayHit, trayCol);
            var trayClick = trayHit.AddComponent<RayClickTarget>();
            Wire(trayClick, "highlight", deskParts.tray.transform);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(trayClick.OnClick, settingsUI.ShowHistory);
            BuildTeamBoard(shell.parent);

            // Ray + poke on every interactive canvas (Interaction SDK quick actions).
            foreach (var c in canvases.Append(handMenu))
            {
                bool wasActive = c.gameObject.activeSelf;
                c.gameObject.SetActive(true);
                MetaSetup.RunWizard("Oculus.Interaction.Editor.QuickActions.RayCanvasWizard", c.gameObject);
                MetaSetup.RunWizard("Oculus.Interaction.Editor.QuickActions.PokeCanvasWizard", c.gameObject);
                c.gameObject.SetActive(wasActive);
            }

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
            Box("DeskTop", deskRoot, new Vector3(0f, DeskTopY - 0.02f, 1.3f), new Vector3(1.4f, 0.04f, 0.62f), walnut);
            Box("DeskColumn", deskRoot, new Vector3(0f, DeskTopY - 0.6f, 1.25f), new Vector3(0.36f, 1.15f, 0.22f), plinth, false);
            Box("DeskFrontGlow", deskRoot, new Vector3(0f, DeskTopY - 0.035f, 1.61f), new Vector3(1.38f, 0.015f, 0.015f), glow, false);
            foreach (int side in new[] { -1, 1 })
            {
                float yaw = 35f * side;
                Vector3 wingPos = new Vector3(side * (0.7f + 0.38f * Mathf.Cos(35f * Mathf.Deg2Rad)), DeskTopY - 0.02f,
                    1.3f - 0.38f * Mathf.Sin(35f * Mathf.Deg2Rad));
                Box(side < 0 ? "WingLeft" : "WingRight", deskRoot, wingPos, new Vector3(0.78f, 0.04f, 0.56f), walnut, true, new Vector3(0f, yaw, 0f));
            }

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

                Cyl("ChairBase", root, new Vector3(0f, 0.03f, -0.62f), 0.28f, 0.04f, charcoal, false);
                Cyl("ChairPost", root, new Vector3(0f, 0.24f, -0.62f), 0.035f, 0.38f, charcoal, false);
                var seat = Box("ChairSeat", root, new Vector3(0f, 0.44f, -0.62f), new Vector3(0.5f, 0.08f, 0.48f), chair, false);
                var back = Box("ChairBack", root, new Vector3(0f, 0.78f, -0.88f), new Vector3(0.48f, 0.55f, 0.06f), chair, false, new Vector3(-8f, 0f, 0f));

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
                WireArray(ws, "chairRenderers", new Object[] { seat.GetComponent<Renderer>(), back.GetComponent<Renderer>() });
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
                Box("ChairSeat", seatRoot, new Vector3(0f, 0.44f, 0f), new Vector3(0.5f, 0.08f, 0.48f), chair, false);
                Box("ChairBack", seatRoot, new Vector3(0f, 0.78f, -0.26f), new Vector3(0.48f, 0.55f, 0.06f), chair, false);
                Cyl("ChairPost", seatRoot, new Vector3(0f, 0.22f, 0f), 0.035f, 0.4f, frame, false);
                seats.Add(seatRoot);
            }

            var meeting = room.gameObject.AddComponent<MeetingRoom>();
            WireArray(meeting, "seats", seats.ToArray());
            return meeting;
        }

        // ══ Lighting ═════════════════════════════════════════════════════════

        private static void BuildLighting()
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
        }

        // ══ Desk UI ══════════════════════════════════════════════════════════

        private static VirtualKeyboardManager BuildKeyboard(Transform deskRoot, ReviewDesk desk, TextInputService service)
        {
            var root = Empty("SurfaceKeyboard", deskRoot, new Vector3(0f, DeskTopY + 0.07f, 1.13f), new Vector3(68f, 0f, 0f)).transform;
            var canvas = WorldCanvas("KeyboardCanvas", root, Vector3.zero, Vector3.zero, new Vector2(680f, 420f), 0.001f);
            Transform t = canvas.transform;
            Panel("Backplate", t, Vector2.zero, new Vector2(680f, 420f), new Color(0.07f, 0.08f, 0.1f, 0.96f));
            var prompt = Text("Prompt", t, "", 19f, Palette.Teal, new Vector2(0f, 186f), new Vector2(640f, 36f), TextAlignmentOptions.Left, true);
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
            var canvas = WorldCanvas("ReviewConsole", deskRoot, new Vector3(0f, DeskTopY + 0.075f, 1.56f), new Vector3(28f, 0f, 0f), new Vector2(640f, 170f), 0.001f);
            Transform t = canvas.transform;
            Panel("Background", t, Vector2.zero, new Vector2(640f, 170f), Palette.UiPanel);
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
            return canvas;
        }

        private static Canvas BuildLayoutPanel(Transform deskRoot, PassthroughToggle passthrough)
        {
            var canvas = WorldCanvas("WorkspacePanel", deskRoot, new Vector3(-0.86f, DeskTopY + 0.3f, 1.06f), new Vector3(24f, -46f, 0f), new Vector2(480f, 560f), 0.001f);
            Panel("Background", canvas.transform, Vector2.zero, new Vector2(480f, 560f), Palette.UiPanel);
            Transform t = View("MainView", canvas.transform);
            Text("Title", t, "WORKSPACE", 22f, Palette.Teal, new Vector2(0f, 250f), new Vector2(440f, 32f), TextAlignmentOptions.Left, true).characterSpacing = 6f;
            var current = Text("Current", t, "Layout: Classroom", 21f, Palette.UiText, new Vector2(0f, 217f), new Vector2(440f, 32f));

            var brief = Button("Brief", t, "BRIEF THE TEAM", new Vector2(0f, 158f), new Vector2(440f, 64f), new Color(0.16f, 0.55f, 0.48f), out _, 24f);

            var buttons = new List<Object>();
            var highlights = new List<Object>();
            for (int i = 0; i < 6; i++)
            {
                int col = i % 2, row = i / 2;
                var b = Button($"Layout_{i}", t, WorkspaceLayout.DisplayName((LayoutType)i), new Vector2(col == 0 ? -113f : 113f, 82f - row * 64f),
                    new Vector2(214f, 56f), Palette.UiButton, out _);
                buttons.Add(b);
                highlights.Add(b.targetGraphic);
            }

            var meeting = Button("Meeting", t, "DISCUSSION", new Vector2(-113f, -132f), new Vector2(214f, 52f), new Color(0.32f, 0.36f, 0.55f), out var meetingLabel, 19f);
            var hold = Button("HoldQueue", t, "HOLD QUEUE", new Vector2(113f, -132f), new Vector2(214f, 52f), new Color(0.45f, 0.33f, 0.16f), out var holdLabel, 19f);
            var auto = Button("DemoMode", t, "DEMO: ON", new Vector2(-113f, -190f), new Vector2(214f, 52f), new Color(0.24f, 0.3f, 0.36f), out var autoLabel, 19f);
            var mr = Button("Passthrough", t, "MIXED REALITY", new Vector2(113f, -190f), new Vector2(214f, 52f), new Color(0.2f, 0.42f, 0.62f), out var mrLabel, 19f);
            var settingsBtn = Button("Settings", t, "SETTINGS & HISTORY", new Vector2(0f, -246f), new Vector2(440f, 48f), Palette.UiButton, out _, 19f);

            // Settings page (same panel, swapped in by SETTINGS & HISTORY or by pinching the done tray).
            Transform s = View("SettingsView", canvas.transform);
            Text("Title", s, "SETTINGS", 22f, Palette.Teal, new Vector2(0f, 250f), new Vector2(440f, 32f), TextAlignmentOptions.Left, true).characterSpacing = 6f;
            var mode = Text("Mode", s, "", 18f, Palette.UiText, new Vector2(0f, 214f), new Vector2(440f, 30f), TextAlignmentOptions.Left);
            mode.enableAutoSizing = true; mode.fontSizeMin = 13f; mode.fontSizeMax = 18f;
            var enterKey = Button("EnterKey", s, "ENTER API KEY", new Vector2(-124f, 168f), new Vector2(192f, 50f), new Color(0.16f, 0.55f, 0.48f), out _, 17f);
            var test = Button("Test", s, "TEST", new Vector2(42f, 168f), new Vector2(124f, 50f), Palette.UiButton, out _, 17f);
            var remove = Button("RemoveKey", s, "REMOVE", new Vector2(164f, 168f), new Vector2(112f, 50f), new Color(0.42f, 0.22f, 0.22f), out _, 17f);
            var status = Text("Status", s, "", 15f, Palette.UiMuted, new Vector2(0f, 120f), new Vector2(440f, 44f), TextAlignmentOptions.TopLeft);
            Panel("Divider", s, new Vector2(0f, 92f), new Vector2(440f, 2f), new Color(1f, 1f, 1f, 0.12f), false);
            var histTitle = Text("HistoryTitle", s, "WORK HISTORY", 18f, Palette.Teal, new Vector2(0f, 72f), new Vector2(440f, 28f), TextAlignmentOptions.Left, true);
            var histBody = Text("HistoryBody", s, "", 17f, Palette.UiText, new Vector2(0f, -45f), new Vector2(440f, 200f), TextAlignmentOptions.TopLeft);
            histBody.enableAutoSizing = true; histBody.fontSizeMin = 11f; histBody.fontSizeMax = 17f;
            histBody.overflowMode = TextOverflowModes.Ellipsis;
            var prev = Button("Older", s, "< OLDER", new Vector2(-113f, -172f), new Vector2(214f, 46f), Palette.UiButton, out _, 17f);
            var next = Button("Newer", s, "NEWER >", new Vector2(113f, -172f), new Vector2(214f, 46f), Palette.UiButton, out _, 17f);
            var reset = Button("Reset", s, "RESET SESSION", new Vector2(-113f, -236f), new Vector2(214f, 52f), new Color(0.48f, 0.2f, 0.2f), out var resetLabel, 17f);
            var back = Button("Back", s, "BACK", new Vector2(113f, -236f), new Vector2(214f, 52f), new Color(0.32f, 0.36f, 0.55f), out _, 19f);

            var settingsUI = canvas.gameObject.AddComponent<SettingsPanelUI>();
            Wire(settingsUI, "mainView", t.gameObject);
            Wire(settingsUI, "settingsView", s.gameObject);
            Wire(settingsUI, "modeText", mode);
            Wire(settingsUI, "statusText", status);
            Wire(settingsUI, "enterKeyButton", enterKey);
            Wire(settingsUI, "testButton", test);
            Wire(settingsUI, "removeKeyButton", remove);
            Wire(settingsUI, "historyTitle", histTitle);
            Wire(settingsUI, "historyBody", histBody);
            Wire(settingsUI, "prevButton", prev);
            Wire(settingsUI, "nextButton", next);
            Wire(settingsUI, "resetButton", reset);
            Wire(settingsUI, "resetLabel", resetLabel);
            Wire(settingsUI, "backButton", back);
            s.gameObject.SetActive(false);

            var ui = canvas.gameObject.AddComponent<LayoutSelectorUI>();
            WireArray(ui, "layoutButtons", buttons.ToArray());
            WireArray(ui, "layoutHighlights", highlights.ToArray());
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
            Wire(ui, "settings", settingsUI);
            return canvas;
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
            var canvas = WorldCanvas("AgentCard", deskRoot, new Vector3(0.86f, DeskTopY + 0.3f, 1.06f), new Vector3(24f, 46f, 0f), new Vector2(480f, 560f), 0.001f);
            Transform t = canvas.transform;
            Panel("Background", t, Vector2.zero, new Vector2(480f, 560f), Palette.UiPanel);
            var accent = Panel("Accent", t, new Vector2(0f, 271f), new Vector2(480f, 18f), Palette.Teal);
            Text("Title", t, "SELECTED AGENT", 22f, Palette.Teal, new Vector2(0f, 235f), new Vector2(440f, 32f), TextAlignmentOptions.Left, true).characterSpacing = 6f;
            var name = Text("Name", t, "", 34f, Palette.UiText, new Vector2(-60f, 190f), new Vector2(320f, 50f), TextAlignmentOptions.Left, true);
            var pill = Panel("StatusPill", t, new Vector2(150f, 190f), new Vector2(150f, 40f), Palette.Teal);
            var status = Text("Status", pill.transform, "", 16f, new Color(0.07f, 0.08f, 0.1f), Vector2.zero, new Vector2(150f, 40f), TextAlignmentOptions.Center, true);
            var body = Text("Body", t, "", 21f, Palette.UiText, new Vector2(0f, 35f), new Vector2(440f, 240f), TextAlignmentOptions.TopLeft);
            body.enableAutoSizing = true; body.fontSizeMin = 14f; body.fontSizeMax = 21f;
            var assign = Button("Assign", t, "ASSIGN TASK", new Vector2(-50f, -140f), new Vector2(340f, 58f), new Color(0.16f, 0.55f, 0.48f), out var assignLabel);
            var close = Button("Close", t, "X", new Vector2(176f, -140f), new Vector2(88f, 58f), new Color(0.3f, 0.32f, 0.37f), out _);
            var role = Button("Role", t, "CHANGE ROLE", new Vector2(-113f, -210f), new Vector2(214f, 54f), Palette.UiButton, out _, 19f);
            var next = Button("ReviewNext", t, "REVIEW NEXT", new Vector2(113f, -210f), new Vector2(214f, 54f), new Color(0.55f, 0.42f, 0.16f), out _, 19f);

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
            var brief = Cell("Brief", "BRIEF TEAM", 0, 0, new Color(0.16f, 0.55f, 0.48f), out _);
            var demo = Cell("Demo", "DEMO: ON", 1, 0, new Color(0.24f, 0.3f, 0.36f), out var demoLabel);
            var layout = Cell("Layout", "CLASSROOM", 0, 1, Palette.UiButton, out var layoutLabel);
            var mr = Cell("MR", "MIXED REALITY", 1, 1, new Color(0.2f, 0.42f, 0.62f), out _);
            var desk = Cell("Desk", "BACK TO DESK", 0, 2, new Color(0.32f, 0.36f, 0.55f), out _);
            var fit = Cell("Fit", "FIT DESK", 1, 2, Palette.UiButton, out _);
            var holdBtn = Button("Hold", t, "HOLD QUEUE", new Vector2(0f, 112f - 3 * 76f), new Vector2(296f, 66f), new Color(0.45f, 0.33f, 0.16f), out var holdText, 17f);

            var menu = root.AddComponent<HandMenu>();
            Wire(menu, "panel", canvas.gameObject);
            Wire(menu, "briefButton", brief);
            Wire(menu, "demoButton", demo);
            Wire(menu, "demoLabel", demoLabel);
            Wire(menu, "layoutButton", layout);
            Wire(menu, "layoutLabel", layoutLabel);
            Wire(menu, "mrButton", mr);
            Wire(menu, "deskButton", desk);
            Wire(menu, "fitButton", fit);
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
