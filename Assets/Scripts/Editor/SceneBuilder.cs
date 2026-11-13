using System.Collections.Generic;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Automated builder for the AI Manager Workspace VR scene.
    /// Builds environment, manager podium, desks, queue, UI panels, NavMesh, and wires all managers.
    /// Menu: Tools > AI Manager > Build Main Scene
    /// </summary>
    public static class SceneBuilder
    {
        [MenuItem("Tools/AI Manager/Build Main Scene")]
        public static void BuildMainScene()
        {
            Debug.Log("[SceneBuilder] Starting complete AI Manager Workspace scene build...");

            // ── 0. Create clean new empty scene ────────────────────────────────
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 1. Ensure Agent Prefab exists ──────────────────────────────────
            var agentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AgentNPC.prefab");
            if (agentPrefab == null)
            {
                Debug.Log("[SceneBuilder] Creating AgentNPC prefab first...");
                AgentPrefabSetup.CreateAgentPrefab();
                agentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AgentNPC.prefab");
            }

            // ── 2. Room Environment ───────────────────────────────────────────
            var envRoot = new GameObject("Environment");

            // Main Floor (ground for agents to walk on)
            var floor = CreatePrimitive(PrimitiveType.Cube, "Floor",
                new Vector3(0, -0.05f, 6f), new Vector3(26f, 0.1f, 24f));
            floor.transform.SetParent(envRoot.transform);
            SetURPMaterial(floor, new Color(0.12f, 0.14f, 0.18f)); // Dark sleek slate
            floor.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();

            // Boundary Walls (aesthetic office studio)
            var wallBack = CreateWall("WallBack", new Vector3(0, 2.5f, 18f), new Vector3(26f, 5f, 0.3f), new Color(0.08f, 0.09f, 0.12f));
            wallBack.transform.SetParent(envRoot.transform);

            var wallLeft = CreateWall("WallLeft", new Vector3(-13f, 2.5f, 6f), new Vector3(0.3f, 5f, 24f), new Color(0.09f, 0.10f, 0.13f));
            wallLeft.transform.SetParent(envRoot.transform);

            var wallRight = CreateWall("WallRight", new Vector3(13f, 2.5f, 6f), new Vector3(0.3f, 5f, 24f), new Color(0.09f, 0.10f, 0.13f));
            wallRight.transform.SetParent(envRoot.transform);

            // ── 3. Manager Platform (Elevated Executive Podium) ────────────────
            var platformRoot = new GameObject("ManagerPlatform");
            platformRoot.transform.position = new Vector3(0, 0, -2f);

            // Podium floor (elevated 0.40m)
            var podium = CreatePrimitive(PrimitiveType.Cube, "PodiumFloor",
                new Vector3(0, 0.20f, -1.8f), new Vector3(4.5f, 0.40f, 3.0f));
            podium.transform.SetParent(platformRoot.transform);
            SetURPMaterial(podium, new Color(0.06f, 0.07f, 0.10f));
            podium.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();

            // Steps leading up to podium
            var step1 = CreatePrimitive(PrimitiveType.Cube, "Step1",
                new Vector3(0, 0.10f, -0.20f), new Vector3(3.2f, 0.20f, 0.25f));
            step1.transform.SetParent(platformRoot.transform);
            SetURPMaterial(step1, new Color(0.1f, 0.11f, 0.15f));

            var step2 = CreatePrimitive(PrimitiveType.Cube, "Step2",
                new Vector3(0, 0.20f, -0.38f), new Vector3(3.0f, 0.20f, 0.20f));
            step2.transform.SetParent(platformRoot.transform);
            SetURPMaterial(step2, new Color(0.1f, 0.11f, 0.15f));

            // Executive Manager Desk (desk surface at y = 0.40 + 0.74 = 1.14m)
            var managerDesk = CreatePrimitive(PrimitiveType.Cube, "ManagerDesk",
                new Vector3(0, 1.115f, -1.15f), new Vector3(2.2f, 0.05f, 0.85f));
            managerDesk.transform.SetParent(platformRoot.transform);
            SetURPMaterial(managerDesk, new Color(0.18f, 0.14f, 0.10f)); // Rich dark walnut wood

            // Desk front modesty panel
            var modestyPanel = CreatePrimitive(PrimitiveType.Cube, "DeskFrontPanel",
                new Vector3(0, 0.76f, -0.73f), new Vector3(2.0f, 0.65f, 0.03f));
            modestyPanel.transform.SetParent(managerDesk.transform);
            SetURPMaterial(modestyPanel, new Color(0.08f, 0.09f, 0.12f));

            // ── 4. XR Origin Setup ─────────────────────────────────────────────
            GameObject xrOrigin = GameObject.Find("XR Origin (XR Rig)") ?? GameObject.Find("XR Origin");
            if (xrOrigin == null)
            {
                var originPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab")
                                ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/3.4.1/Hands Interaction Demo/Prefabs/XR Origin (XR Rig) - Hands Interaction Demo.prefab")
                                ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab");

                if (originPrefab != null)
                {
                    xrOrigin = (GameObject)PrefabUtility.InstantiatePrefab(originPrefab);
                    xrOrigin.name = "XR Origin";
                    Debug.Log($"[SceneBuilder] Instantiated XR Origin from {AssetDatabase.GetAssetPath(originPrefab)}");
                }
                else
                {
                    Debug.LogWarning("[SceneBuilder] XR Origin prefab not found in project. Creating fallback Camera Rig.");
                    xrOrigin = new GameObject("XR Origin");
                    var cameraOffset = new GameObject("Camera Offset");
                    cameraOffset.transform.SetParent(xrOrigin.transform);
                    var mainCam = new GameObject("Main Camera");
                    mainCam.transform.SetParent(cameraOffset.transform);
                    mainCam.tag = "MainCamera";
                    var cam = mainCam.AddComponent<Camera>();
                    cam.nearClipPlane = 0.05f;
                    mainCam.AddComponent<AudioListener>();
                }
            }

            if (xrOrigin != null)
            {
                // Position XR Origin directly on the manager podium overlooking the workspace
                xrOrigin.transform.position = new Vector3(0, 0.40f, -1.75f);
                xrOrigin.transform.rotation = Quaternion.identity;
            }

            // ── 5. Queue Zone & Markers ────────────────────────────────────────
            var queueRoot = new GameObject("QueueZone");
            queueRoot.transform.position = Vector3.zero;

            Vector3[] queuePositions = {
                new Vector3(0, 0, 1.6f),
                new Vector3(0, 0, 2.8f),
                new Vector3(0, 0, 4.0f),
                new Vector3(0, 0, 5.2f)
            };

            Transform[] queueSlotTransforms = new Transform[queuePositions.Length];
            for (int i = 0; i < queuePositions.Length; i++)
            {
                var slot = new GameObject($"QueueSlot_{i + 1}");
                slot.transform.position = queuePositions[i];
                slot.transform.SetParent(queueRoot.transform);
                queueSlotTransforms[i] = slot.transform;

                // Visual ground marker cylinder
                var marker = CreatePrimitive(PrimitiveType.Cylinder, $"SlotMarker_{i + 1}",
                    queuePositions[i] + Vector3.up * 0.015f, new Vector3(0.5f, 0.02f, 0.5f));
                marker.transform.SetParent(slot.transform);
                SetURPMaterialEmissive(marker, new Color(1.0f, 0.75f, 0.1f), 1.2f); // Amber glow ring
            }

            // Manager Presentation Point (directly in front of manager desk on the floor)
            var presentSlot = new GameObject("PresentingPosition");
            presentSlot.transform.position = new Vector3(0, 0f, 0.35f);
            presentSlot.transform.SetParent(queueRoot.transform);

            var presentMarker = CreatePrimitive(PrimitiveType.Cylinder, "PresentMarker",
                new Vector3(0, 0.015f, 0.35f), new Vector3(0.6f, 0.02f, 0.6f));
            presentMarker.transform.SetParent(presentSlot.transform);
            SetURPMaterialEmissive(presentMarker, new Color(0.1f, 0.95f, 0.65f), 1.8f); // Emerald/Cyan glow

            // ── 6. 6 Agent Workstations (Desks) ────────────────────────────────
            var desksRoot = new GameObject("AgentDesks");
            desksRoot.transform.position = Vector3.zero;

            Vector3[] deskPositions = {
                new Vector3(-3.8f, 0, 5.0f),
                new Vector3( 3.8f, 0, 5.0f),
                new Vector3(-3.8f, 0, 8.0f),
                new Vector3( 3.8f, 0, 8.0f),
                new Vector3(-3.8f, 0, 11.0f),
                new Vector3( 3.8f, 0, 11.0f)
            };

            Transform[] deskTransforms = new Transform[6];
            for (int i = 0; i < 6; i++)
            {
                var deskGO = new GameObject($"AgentDesk_{i + 1}");
                deskGO.transform.position = deskPositions[i];
                deskGO.transform.rotation = Quaternion.Euler(0, 180, 0); // Facing towards manager platform
                deskGO.transform.SetParent(desksRoot.transform);
                deskTransforms[i] = deskGO.transform;

                // Desk Surface (Standard 0.74m desk height)
                var surface = CreatePrimitive(PrimitiveType.Cube, "DeskSurface",
                    deskPositions[i] + new Vector3(0, 0.715f, 0), new Vector3(1.4f, 0.05f, 0.8f));
                surface.transform.SetParent(deskGO.transform);
                SetURPMaterial(surface, new Color(0.22f, 0.24f, 0.28f));

                // 4 Desk Legs (height 0.70m)
                float[] legX = { -0.62f, 0.62f };
                float[] legZ = { -0.32f, 0.32f };
                for (int lx = 0; lx < 2; lx++)
                {
                    for (int lz = 0; lz < 2; lz++)
                    {
                        var leg = CreatePrimitive(PrimitiveType.Cube, $"Leg_{lx}_{lz}",
                            deskPositions[i] + new Vector3(legX[lx], 0.35f, legZ[lz]), new Vector3(0.05f, 0.70f, 0.05f));
                        leg.transform.SetParent(deskGO.transform);
                        SetURPMaterial(leg, new Color(0.12f, 0.12f, 0.14f));
                    }
                }

                // Dual Monitors (elevated to eye level at desk)
                var monitorL = CreatePrimitive(PrimitiveType.Cube, "MonitorLeft",
                    deskPositions[i] + new Vector3(-0.32f, 1.05f, -0.28f), new Vector3(0.55f, 0.35f, 0.03f));
                monitorL.transform.localRotation = Quaternion.Euler(0, 12f, 0);
                monitorL.transform.SetParent(deskGO.transform);
                SetURPMaterialEmissive(monitorL, new Color(0.1f, 0.45f, 0.95f), 1.5f); // Code editor blue glow

                var monitorR = CreatePrimitive(PrimitiveType.Cube, "MonitorRight",
                    deskPositions[i] + new Vector3(0.32f, 1.05f, -0.28f), new Vector3(0.55f, 0.35f, 0.03f));
                monitorR.transform.localRotation = Quaternion.Euler(0, -12f, 0);
                monitorR.transform.SetParent(deskGO.transform);
                SetURPMaterialEmissive(monitorR, new Color(0.1f, 0.8f, 0.5f), 1.5f); // Terminal green glow

                // Ergonomic Chair (Standard chair seat height 0.44m)
                var chairSeat = CreatePrimitive(PrimitiveType.Cube, "ChairSeat",
                    deskPositions[i] + new Vector3(0, 0.44f, 0.55f), new Vector3(0.48f, 0.06f, 0.48f));
                chairSeat.transform.SetParent(deskGO.transform);
                SetURPMaterial(chairSeat, new Color(0.15f, 0.16f, 0.20f));

                var chairBack = CreatePrimitive(PrimitiveType.Cube, "ChairBack",
                    deskPositions[i] + new Vector3(0, 0.78f, 0.76f), new Vector3(0.45f, 0.60f, 0.05f));
                chairBack.transform.SetParent(deskGO.transform);
                SetURPMaterial(chairBack, new Color(0.15f, 0.16f, 0.20f));
            }

            // ── 7. Studio & Ambient Lighting ──────────────────────────────────
            var lightsRoot = new GameObject("Lighting");
            lightsRoot.transform.SetParent(envRoot.transform);

            // Ceiling ambient soft wash
            CreatePointLight("OfficeCeilingLight1", new Vector3(-5f, 4.0f, 6f), new Color(0.85f, 0.90f, 1.0f), 3.5f, 16f, lightsRoot.transform);
            CreatePointLight("OfficeCeilingLight2", new Vector3(5f, 4.0f, 6f), new Color(0.85f, 0.90f, 1.0f), 3.5f, 16f, lightsRoot.transform);
            CreatePointLight("OfficeCeilingLight3", new Vector3(0, 4.0f, 11f), new Color(0.85f, 0.90f, 1.0f), 3.5f, 16f, lightsRoot.transform);

            // Manager executive spotlight
            CreatePointLight("ManagerSpotlight", new Vector3(0, 2.8f, -1.4f), new Color(1.0f, 0.95f, 0.88f), 2.8f, 8f, lightsRoot.transform);

            // ── 8. NavMesh Baking ──────────────────────────────────────────────
            var navSurface = floor.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.All;
            navSurface.layerMask = LayerMask.GetMask("Default");
            navSurface.BuildNavMesh();
            Debug.Log("[SceneBuilder] NavMesh successfully baked on office floor.");

            // ── 9. World-Space Manager UI Dashboards ──────────────────────────
            var uiRoot = new GameObject("ManagerUIDashboards");
            uiRoot.transform.position = Vector3.zero;

            // 9A. Layout Selector UI (Floating on Left of Manager Desk)
            var layoutUIGO = CreateLayoutSelectorPanel(uiRoot.transform);

            // 9B. Agent Detail Inspector UI (Floating on Right of Manager Desk)
            var agentDetailUIGO = CreateAgentDetailPanel(uiRoot.transform);
            var agentDetailPanel = agentDetailUIGO.GetComponent<AgentDetailPanel>();

            // 9C. Virtual Keyboard UI (Center world space for text input)
            var keyboardGO = CreateVirtualKeyboard(uiRoot.transform);
            var keyboardManager = keyboardGO.GetComponent<VirtualKeyboardManager>();

            // 9D. XR Event System (enables Hand Rays, Pokes, and Controllers to interact with UI)
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();

            // ── 10. Core Systems Setup & Wiring ────────────────────────────────
            var gmGO = new GameObject("GameManager");
            var gameManager = gmGO.AddComponent<GameManager>();

            // PassthroughToggle (MR / VR switcher)
            var passthrough = gmGO.AddComponent<PassthroughToggle>();
            var ptSO = new SerializedObject(passthrough);
            ptSO.FindProperty("environmentRoot").objectReferenceValue = envRoot;
            if (xrOrigin != null)
            {
                var cam = xrOrigin.GetComponentInChildren<Camera>();
                if (cam != null) ptSO.FindProperty("xrCamera").objectReferenceValue = cam;
            }
            ptSO.ApplyModifiedProperties();

            // QueueManager
            var queueGO = new GameObject("QueueManager");
            queueGO.transform.SetParent(gmGO.transform);
            var queueManager = queueGO.AddComponent<QueueManager>();
            var queueSO = new SerializedObject(queueManager);
            var queueSlotsProp = queueSO.FindProperty("queueSlotTransforms");
            queueSlotsProp.arraySize = queueSlotTransforms.Length;
            for (int s = 0; s < queueSlotTransforms.Length; s++)
                queueSlotsProp.GetArrayElementAtIndex(s).objectReferenceValue = queueSlotTransforms[s];
            queueSO.FindProperty("presentingTransform").objectReferenceValue = presentSlot.transform;
            queueSO.ApplyModifiedProperties();

            // AgentManager
            var agentMgrGO = new GameObject("AgentManager");
            agentMgrGO.transform.SetParent(gmGO.transform);
            var agentManager = agentMgrGO.AddComponent<AgentManager>();
            var agentSO = new SerializedObject(agentManager);
            if (agentPrefab != null)
                agentSO.FindProperty("agentPrefab").objectReferenceValue = agentPrefab;
            var deskArrProp = agentSO.FindProperty("deskTransforms");
            deskArrProp.arraySize = deskTransforms.Length;
            for (int d = 0; d < deskTransforms.Length; d++)
                deskArrProp.GetArrayElementAtIndex(d).objectReferenceValue = deskTransforms[d];
            agentSO.FindProperty("managerPresentPoint").objectReferenceValue = presentSlot.transform;
            agentSO.FindProperty("queueManager").objectReferenceValue = queueManager;
            agentSO.FindProperty("agentDetailPanel").objectReferenceValue = agentDetailPanel;
            agentSO.ApplyModifiedProperties();

            // WorkspaceLayoutManager
            var layoutMgrGO = new GameObject("WorkspaceLayoutManager");
            layoutMgrGO.transform.SetParent(gmGO.transform);
            var layoutManager = layoutMgrGO.AddComponent<WorkspaceLayoutManager>();
            var layoutSO = new SerializedObject(layoutManager);
            var layoutDeskArr = layoutSO.FindProperty("deskTransforms");
            layoutDeskArr.arraySize = deskTransforms.Length;
            for (int ld = 0; ld < deskTransforms.Length; ld++)
                layoutDeskArr.GetArrayElementAtIndex(ld).objectReferenceValue = deskTransforms[ld];
            layoutSO.FindProperty("managerPlatformTransform").objectReferenceValue = platformRoot.transform;
            layoutSO.FindProperty("agentManager").objectReferenceValue = agentManager;
            layoutSO.FindProperty("navMeshSurface").objectReferenceValue = navSurface;
            layoutSO.ApplyModifiedProperties();

            // Wire LayoutSelectorUI to WorkspaceLayoutManager
            var layoutSelectorUI = layoutUIGO.GetComponent<LayoutSelectorUI>();
            var layoutSelectorSO = new SerializedObject(layoutSelectorUI);
            layoutSelectorSO.FindProperty("layoutManager").objectReferenceValue = layoutManager;
            layoutSelectorSO.ApplyModifiedProperties();

            // AIServiceManager
            var aiServiceGO = new GameObject("AIServiceManager");
            aiServiceGO.transform.SetParent(gmGO.transform);
            var aiService = aiServiceGO.AddComponent<AIServiceManager>();

            // AITaskGenerator
            var aiTaskGO = new GameObject("AITaskGenerator");
            aiTaskGO.transform.SetParent(gmGO.transform);
            var aiTaskGen = aiTaskGO.AddComponent<AITaskGenerator>();
            var aiTaskSO = new SerializedObject(aiTaskGen);
            aiTaskSO.FindProperty("aiService").objectReferenceValue = aiService;
            aiTaskSO.FindProperty("agentManager").objectReferenceValue = agentManager;
            aiTaskSO.ApplyModifiedProperties();

            // HandGestureRecognizer & GestureInteractionManager
            var gestureGO = new GameObject("GestureInteractionManager");
            gestureGO.transform.SetParent(gmGO.transform);
            var recognizer = gestureGO.AddComponent<HandGestureRecognizer>();
            var gestureMgr = gestureGO.AddComponent<GestureInteractionManager>();
            var gestureSO = new SerializedObject(gestureMgr);
            gestureSO.FindProperty("gestureRecognizer").objectReferenceValue = recognizer;
            gestureSO.FindProperty("queueManager").objectReferenceValue = queueManager;
            gestureSO.FindProperty("agentManager").objectReferenceValue = agentManager;
            gestureSO.FindProperty("agentDetailPanel").objectReferenceValue = agentDetailPanel;
            gestureSO.ApplyModifiedProperties();

            // ── 11. Save Scene and Register in Build Settings ─────────────────
            const string scenesDir = "Assets/Scenes";
            if (!AssetDatabase.IsValidFolder(scenesDir))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            string scenePath = $"{scenesDir}/AIManagerWorkspace.unity";
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);

            var currentScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            currentScenes.RemoveAll(s => s.path == scenePath);
            currentScenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = currentScenes.ToArray();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SceneBuilder] ✅ COMPLETE: AI Manager Workspace scene built, fully wired, saved to {scenePath}, and set at Build Settings index 0!");
        }

        // ── Helper: Layout Selector UI Panel ──────────────────────────────────
        private static GameObject CreateLayoutSelectorPanel(Transform parent)
        {
            var panelGO = new GameObject("LayoutSelectorPanel");
            panelGO.transform.SetParent(parent);
            // Angled on the left of manager's desk (ergonomic compact console)
            panelGO.transform.position = new Vector3(-0.75f, 1.30f, -0.92f);
            panelGO.transform.rotation = Quaternion.Euler(12f, 22f, 0);

            var canvas = panelGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            panelGO.AddComponent<CanvasScaler>();
            panelGO.AddComponent<GraphicRaycaster>();
            panelGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            var group = panelGO.AddComponent<CanvasGroup>();

            var rt = panelGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(320, 320);
            rt.localScale = Vector3.one * 0.00085f;

            // Background
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(panelGO.transform);
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.10f, 0.15f, 0.95f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Title
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform);
            var titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "WORKSPACE LAYOUTS";
            titleTmp.fontSize = 20;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.color = new Color(0.9f, 0.95f, 1.0f);
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.88f);
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            // Current Layout Label
            var curGO = new GameObject("CurrentLayoutLabel");
            curGO.transform.SetParent(panelGO.transform);
            var curTmp = curGO.AddComponent<TextMeshProUGUI>();
            curTmp.text = "Current: Grid";
            curTmp.fontSize = 15;
            curTmp.alignment = TextAlignmentOptions.Center;
            curTmp.color = new Color(0.3f, 0.85f, 0.5f);
            var curRect = curGO.GetComponent<RectTransform>();
            curRect.anchorMin = new Vector2(0, 0.77f);
            curRect.anchorMax = new Vector2(1, 0.88f);
            curRect.offsetMin = Vector2.zero;
            curRect.offsetMax = Vector2.zero;

            // 6 Layout Buttons in 2 columns x 3 rows
            string[] names = { "Grid", "Circle", "U-Shape", "Hub & Spoke", "Coworking", "War Room" };
            Button[] buttons = new Button[6];

            for (int b = 0; b < 6; b++)
            {
                int col = b % 2;
                int row = b / 2;

                var btnGO = new GameObject($"Btn_{names[b]}");
                btnGO.transform.SetParent(panelGO.transform);
                var btnImg = btnGO.AddComponent<Image>();
                btnImg.color = new Color(0.18f, 0.22f, 0.30f);
                var btn = btnGO.AddComponent<Button>();
                buttons[b] = btn;

                var btnRect = btnGO.GetComponent<RectTransform>();
                float xMin = col == 0 ? 0.06f : 0.52f;
                float xMax = col == 0 ? 0.48f : 0.94f;
                float yMax = 0.75f - (row * 0.18f);
                float yMin = yMax - 0.15f;

                btnRect.anchorMin = new Vector2(xMin, yMin);
                btnRect.anchorMax = new Vector2(xMax, yMax);
                btnRect.offsetMin = Vector2.zero;
                btnRect.offsetMax = Vector2.zero;

                var labelGO = new GameObject("Text");
                labelGO.transform.SetParent(btnGO.transform);
                var labelTmp = labelGO.AddComponent<TextMeshProUGUI>();
                labelTmp.text = names[b];
                labelTmp.fontSize = 16;
                labelTmp.alignment = TextAlignmentOptions.Center;
                labelTmp.color = Color.white;
                var labelRect = labelGO.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }

            // 7th Button: Mixed Reality Passthrough Toggle
            var mrBtnGO = new GameObject("Btn_ToggleMR");
            mrBtnGO.transform.SetParent(panelGO.transform);
            var mrBtnImg = mrBtnGO.AddComponent<Image>();
            mrBtnImg.color = new Color(0.12f, 0.45f, 0.78f);
            var mrBtn = mrBtnGO.AddComponent<Button>();
            var mrBtnRect = mrBtnGO.GetComponent<RectTransform>();
            mrBtnRect.anchorMin = new Vector2(0.06f, 0.03f);
            mrBtnRect.anchorMax = new Vector2(0.94f, 0.16f);
            mrBtnRect.offsetMin = Vector2.zero;
            mrBtnRect.offsetMax = Vector2.zero;

            var mrLabelGO = new GameObject("Text");
            mrLabelGO.transform.SetParent(mrBtnGO.transform);
            var mrLabelTmp = mrLabelGO.AddComponent<TextMeshProUGUI>();
            mrLabelTmp.text = "Toggle Mixed Reality (Passthrough)";
            mrLabelTmp.fontSize = 14;
            mrLabelTmp.fontStyle = FontStyles.Bold;
            mrLabelTmp.alignment = TextAlignmentOptions.Center;
            mrLabelTmp.color = Color.white;
            var mrLabelRect = mrLabelGO.GetComponent<RectTransform>();
            mrLabelRect.anchorMin = Vector2.zero;
            mrLabelRect.anchorMax = Vector2.one;
            mrLabelRect.offsetMin = Vector2.zero;
            mrLabelRect.offsetMax = Vector2.zero;

            mrBtn.onClick.AddListener(() => {
                var pt = Object.FindAnyObjectByType<PassthroughToggle>();
                pt?.ToggleMode();
            });

            var layoutUI = panelGO.AddComponent<LayoutSelectorUI>();
            var uiSO = new SerializedObject(layoutUI);
            var btnArr = uiSO.FindProperty("layoutButtons");
            btnArr.arraySize = 6;
            for (int i = 0; i < 6; i++)
                btnArr.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            uiSO.FindProperty("currentLayoutLabel").objectReferenceValue = curTmp;
            uiSO.FindProperty("panelGroup").objectReferenceValue = group;
            uiSO.ApplyModifiedProperties();

            return panelGO;
        }

        // ── Helper: Agent Detail Inspector UI Panel ───────────────────────────
        private static GameObject CreateAgentDetailPanel(Transform parent)
        {
            var panelGO = new GameObject("AgentDetailPanel");
            panelGO.transform.SetParent(parent);
            // Angled on the right of manager's desk (ergonomic compact console)
            panelGO.transform.position = new Vector3(0.75f, 1.30f, -0.92f);
            panelGO.transform.rotation = Quaternion.Euler(12f, -22f, 0);

            var canvas = panelGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            panelGO.AddComponent<CanvasScaler>();
            panelGO.AddComponent<GraphicRaycaster>();
            panelGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            var group = panelGO.AddComponent<CanvasGroup>();

            var rt = panelGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(340, 260);
            rt.localScale = Vector3.one * 0.00085f;

            // Background
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(panelGO.transform);
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.10f, 0.15f, 0.95f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Status Bar
            var barGO = new GameObject("StatusBar");
            barGO.transform.SetParent(panelGO.transform);
            var barImg = barGO.AddComponent<Image>();
            barImg.color = Color.green;
            var barRect = barGO.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0, 0.96f);
            barRect.anchorMax = Vector2.one;
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;

            // Agent Name
            var nameGO = new GameObject("AgentName");
            nameGO.transform.SetParent(panelGO.transform);
            var nameTmp = nameGO.AddComponent<TextMeshProUGUI>();
            nameTmp.text = "Select an Agent";
            nameTmp.fontSize = 22;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.color = Color.white;
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0.06f, 0.83f);
            nameRect.anchorMax = new Vector2(0.94f, 0.95f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;

            // Role Text
            var roleGO = new GameObject("AgentRole");
            roleGO.transform.SetParent(panelGO.transform);
            var roleTmp = roleGO.AddComponent<TextMeshProUGUI>();
            roleTmp.text = "Pinch to inspect an agent";
            roleTmp.fontSize = 15;
            roleTmp.color = new Color(0.6f, 0.75f, 0.95f);
            var roleRect = roleGO.GetComponent<RectTransform>();
            roleRect.anchorMin = new Vector2(0.06f, 0.74f);
            roleRect.anchorMax = new Vector2(0.94f, 0.83f);
            roleRect.offsetMin = Vector2.zero;
            roleRect.offsetMax = Vector2.zero;

            // Status Text
            var statusGO = new GameObject("StatusText");
            statusGO.transform.SetParent(panelGO.transform);
            var statusTmp = statusGO.AddComponent<TextMeshProUGUI>();
            statusTmp.text = "Status: Idle";
            statusTmp.fontSize = 16;
            statusTmp.color = Color.green;
            var statusRect = statusGO.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.06f, 0.64f);
            statusRect.anchorMax = new Vector2(0.94f, 0.73f);
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;

            // Task Description
            var taskGO = new GameObject("TaskDescription");
            taskGO.transform.SetParent(panelGO.transform);
            var taskTmp = taskGO.AddComponent<TextMeshProUGUI>();
            taskTmp.text = "Task: None assigned";
            taskTmp.fontSize = 14;
            taskTmp.color = new Color(0.85f, 0.85f, 0.9f);
            var taskRect = taskGO.GetComponent<RectTransform>();
            taskRect.anchorMin = new Vector2(0.06f, 0.40f);
            taskRect.anchorMax = new Vector2(0.94f, 0.63f);
            taskRect.offsetMin = Vector2.zero;
            taskRect.offsetMax = Vector2.zero;

            // Output Preview
            var outGO = new GameObject("OutputPreview");
            outGO.transform.SetParent(panelGO.transform);
            var outTmp = outGO.AddComponent<TextMeshProUGUI>();
            outTmp.text = "Output: Awaiting execution results...";
            outTmp.fontSize = 13;
            outTmp.color = new Color(0.7f, 0.7f, 0.75f);
            var outRect = outGO.GetComponent<RectTransform>();
            outRect.anchorMin = new Vector2(0.06f, 0.05f);
            outRect.anchorMax = new Vector2(0.94f, 0.38f);
            outRect.offsetMin = Vector2.zero;
            outRect.offsetMax = Vector2.zero;

            var detailComp = panelGO.AddComponent<AgentDetailPanel>();
            var dSO = new SerializedObject(detailComp);
            dSO.FindProperty("nameText").objectReferenceValue = nameTmp;
            dSO.FindProperty("roleText").objectReferenceValue = roleTmp;
            dSO.FindProperty("statusText").objectReferenceValue = statusTmp;
            dSO.FindProperty("taskDescText").objectReferenceValue = taskTmp;
            dSO.FindProperty("outputPreviewText").objectReferenceValue = outTmp;
            dSO.FindProperty("statusColorBar").objectReferenceValue = barImg;
            dSO.FindProperty("panelGroup").objectReferenceValue = group;
            dSO.ApplyModifiedProperties();

            return panelGO;
        }

        // ── Helper: Virtual Keyboard UI ───────────────────────────────────────
        private static GameObject CreateVirtualKeyboard(Transform parent)
        {
            var kbGO = new GameObject("VirtualKeyboard");
            kbGO.transform.SetParent(parent);
            // Resting ergonomically right in front of manager above desk surface
            kbGO.transform.position = new Vector3(0, 1.18f, -0.85f);
            kbGO.transform.rotation = Quaternion.Euler(22f, 0, 0);

            var canvas = kbGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            kbGO.AddComponent<CanvasScaler>();
            kbGO.AddComponent<GraphicRaycaster>();
            kbGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

            var rt = kbGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400, 140);
            rt.localScale = Vector3.one * 0.00080f;

            var previewGO = new GameObject("PreviewText");
            previewGO.transform.SetParent(kbGO.transform);
            var previewTmp = previewGO.AddComponent<TextMeshProUGUI>();
            previewTmp.text = "Type feedback or instruction...";
            previewTmp.fontSize = 18;
            previewTmp.alignment = TextAlignmentOptions.Center;
            previewTmp.color = Color.white;
            var prevRect = previewGO.GetComponent<RectTransform>();
            prevRect.anchorMin = new Vector2(0.05f, 0.65f);
            prevRect.anchorMax = new Vector2(0.95f, 0.95f);
            prevRect.offsetMin = Vector2.zero;
            prevRect.offsetMax = Vector2.zero;

            var kbComp = kbGO.AddComponent<VirtualKeyboardManager>();
            var kbSO = new SerializedObject(kbComp);
            kbSO.FindProperty("keyboardCanvas").objectReferenceValue = canvas;
            kbSO.FindProperty("previewText").objectReferenceValue = previewTmp;
            kbSO.ApplyModifiedProperties();

            return kbGO;
        }

        // ── Primitive & Material Helpers ──────────────────────────────────────
        private static GameObject CreatePrimitive(PrimitiveType type, string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            return go;
        }

        private static GameObject CreateWall(string name, Vector3 pos, Vector3 scale, Color color)
        {
            var wall = CreatePrimitive(PrimitiveType.Cube, name, pos, scale);
            SetURPMaterial(wall, color);
            return wall;
        }

        private static void SetURPMaterial(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            renderer.sharedMaterial = mat;
        }

        private static void SetURPMaterialEmissive(GameObject go, Color color, float emissiveIntensity)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * emissiveIntensity);
            renderer.sharedMaterial = mat;
        }

        private static void CreatePointLight(string name, Vector3 pos, Color color, float intensity, float range, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            go.transform.SetParent(parent);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
        }

        [MenuItem("Tools/AI Manager/Configure Android Build (Quest 3)")]
        public static void ConfigureAndroidBuild()
        {
            Debug.Log("[SceneBuilder] Configuring Android build settings for Quest 3...");

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.garvchopra.aimanagerworkspace");
            PlayerSettings.productName = "AI Manager Workspace";
            PlayerSettings.companyName = "Garv Chopra";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Trigger Meta OVRProjectSetup fixes for Android automatically via reflection
            try
            {
                var ovrSetupType = System.Type.GetType("OVRProjectSetup, Meta.XR.SDK.Core.Editor")
                                ?? System.Type.GetType("OVRProjectSetup, Oculus.VR.Editor");
                if (ovrSetupType != null)
                {
                    var fixMethod = ovrSetupType.GetMethod("FixTasks", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (fixMethod != null)
                    {
                        var parameters = fixMethod.GetParameters();
                        if (parameters.Length == 1)
                        {
                            fixMethod.Invoke(null, new object[] { BuildTargetGroup.Android });
                        }
                        else
                        {
                            fixMethod.Invoke(null, new object[] { BuildTargetGroup.Android, null, 0, true, null, false });
                        }
                        Debug.Log("[SceneBuilder] Meta OVRProjectSetup FixTasks triggered for Android.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SceneBuilder] OVRProjectSetup note: {ex.Message}");
            }

            Debug.Log("[SceneBuilder] ✅ Android/Quest 3 build settings successfully configured!");
        }

        [MenuItem("Tools/AI Manager/Inspect Active Scene")]
        public static void InspectActiveScene()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            Debug.Log($"[SceneInspection] Active Scene: '{activeScene.name}' path: '{activeScene.path}', isLoaded: {activeScene.isLoaded}");
            var roots = activeScene.GetRootGameObjects();
            Debug.Log($"[SceneInspection] Root GameObjects ({roots.Length}):");
            foreach (var r in roots)
            {
                Debug.Log($"  - {r.name} (children: {r.transform.childCount})");
            }
        }
    }
}
