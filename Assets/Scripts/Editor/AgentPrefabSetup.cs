using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using static AIManagerWorkspace.BuilderKit;

namespace AIManagerWorkspace
{
    /// <summary>Builds Assets/Prefabs/AgentNPC.prefab: stylised avatar, head tag, carried page.</summary>
    public static class AgentPrefabSetup
    {
        public const string PrefabPath = "Assets/Prefabs/AgentNPC.prefab";

        [MenuItem("Tools/AI Manager/Create Agent Prefab")]
        public static GameObject CreateAgentPrefab()
        {
            Material shirt = Mat("Agent_Shirt", new Color(0.6f, 0.6f, 0.65f), 0.2f);
            Material trousers = Mat("Agent_Trousers", new Color(0.22f, 0.23f, 0.27f), 0.2f);
            Material skin = Mat("Agent_Skin", Palette.Skin, 0.35f);
            Material visor = Mat("Agent_Visor", new Color(0.08f, 0.09f, 0.11f), 0.9f, 0.2f);
            Material accent = Mat("Agent_Accent", new Color(0.4f, 0.4f, 0.45f), 0.4f);
            Material ring = Emissive("Agent_SelectionRing", Palette.Teal, 2.2f);

            var root = new GameObject("AgentNPC");

            // ── Avatar rig ───────────────────────────────────────────────────
            var visual = Empty("Visual", root.transform, Vector3.zero);
            var body = Empty("Body", visual.transform, Vector3.zero);

            Transform Leg(string n, float x)
            {
                var pivot = Empty(n, body.transform, new Vector3(x, 0.92f, 0f)).transform;
                Capsule($"{n}_Mesh", pivot, new Vector3(0f, -0.45f, 0f), new Vector3(0.15f, 0.45f, 0.15f), trousers);
                Box($"{n}_Shoe", pivot, new Vector3(0f, -0.88f, 0.05f), new Vector3(0.13f, 0.07f, 0.24f), visor, false);
                return pivot;
            }
            Transform legL = Leg("LegL", -0.1f);
            Transform legR = Leg("LegR", 0.1f);

            var torso = Capsule("Torso", body.transform, new Vector3(0f, 1.23f, 0f), new Vector3(0.38f, 0.34f, 0.25f), shirt);
            var collar = Box("Collar", body.transform, new Vector3(0f, 1.5f, 0.0f), new Vector3(0.22f, 0.05f, 0.16f), accent, false);
            var badge = Box("Badge", body.transform, new Vector3(0.09f, 1.36f, 0.122f), new Vector3(0.07f, 0.05f, 0.01f), accent, false);

            Transform Arm(string n, float x)
            {
                var pivot = Empty(n, body.transform, new Vector3(x, 1.45f, 0f)).transform;
                Capsule($"{n}_Mesh", pivot, new Vector3(0f, -0.27f, 0f), new Vector3(0.1f, 0.27f, 0.1f), shirt);
                Ball($"{n}_Hand", pivot, new Vector3(0f, -0.56f, 0f), Vector3.one * 0.09f, skin);
                return pivot;
            }
            Transform armL = Arm("ArmL", -0.25f);
            Transform armR = Arm("ArmR", 0.25f);

            var head = Empty("Head", body.transform, new Vector3(0f, 1.56f, 0f)).transform;
            Capsule("Neck", head, new Vector3(0f, 0.02f, 0f), new Vector3(0.09f, 0.05f, 0.09f), skin);
            Ball("Skull", head, new Vector3(0f, 0.16f, 0f), new Vector3(0.25f, 0.27f, 0.25f), skin);
            Box("Visor", head, new Vector3(0f, 0.18f, 0.105f), new Vector3(0.2f, 0.06f, 0.06f), visor, false);
            var antenna = Ball("Antenna", head, new Vector3(0f, 0.33f, 0f), Vector3.one * 0.06f, accent);

            var hold = Empty("HoldPoint", body.transform, new Vector3(0f, 1.02f, 0.36f), new Vector3(30f, 180f, 0f)).transform;

            var avatar = visual.AddComponent<AgentAvatar>();
            Wire(avatar, "body", body.transform);
            Wire(avatar, "legL", legL);
            Wire(avatar, "legR", legR);
            Wire(avatar, "armL", armL);
            Wire(avatar, "armR", armR);
            Wire(avatar, "head", head);
            Wire(avatar, "holdPoint", hold);
            WireArray(avatar, "shirtRenderers", new Object[]
            {
                torso.GetComponent<Renderer>(), armL.GetChild(0).GetComponent<Renderer>(), armR.GetChild(0).GetComponent<Renderer>()
            });
            WireArray(avatar, "accentRenderers", new Object[]
            {
                collar.GetComponent<Renderer>(), badge.GetComponent<Renderer>(), antenna.GetComponent<Renderer>()
            });

            var selection = Cyl("SelectionRing", root.transform, new Vector3(0f, 0.012f, 0f), 0.5f, 0.012f, ring, false);
            NoShadows(selection);

            // ── Head tag ─────────────────────────────────────────────────────
            var tag = BuildStatusTag(root.transform);

            // ── Carried page ─────────────────────────────────────────────────
            var page = BuildPage(hold);

            // ── Components ───────────────────────────────────────────────────
            var nav = root.AddComponent<NavMeshAgent>();
            nav.radius = 0.28f;
            nav.height = 1.8f;
            nav.speed = 1.5f;
            nav.angularSpeed = 600f;
            nav.acceleration = 10f;
            nav.stoppingDistance = 0.05f;
            nav.autoBraking = true;
            nav.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            nav.enabled = false;

            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.9f, 0f);
            col.height = 1.8f;
            col.radius = 0.3f;

            var controller = root.AddComponent<AgentController>();
            Wire(controller, "avatar", avatar);
            Wire(controller, "statusTag", tag);
            Wire(controller, "page", page);
            Wire(controller, "selectionRing", selection);

            SceneBuilder.AddRayTarget(root, col);
            var drag = root.AddComponent<FloorDragInteractable>();
            Wire(drag, "agent", controller);

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AgentPrefabSetup] Saved {PrefabPath}");
            return prefab;
        }

        private static StatusTagUI BuildStatusTag(Transform parent)
        {
            var canvas = WorldCanvas("StatusTag", parent, new Vector3(0f, 2.1f, 0f), Vector3.zero, new Vector2(440f, 150f), 0.001f);
            Transform t = canvas.transform;
            Panel("Background", t, Vector2.zero, new Vector2(440f, 150f), new Color(0.09f, 0.10f, 0.13f, 0.93f));
            var accentBar = Panel("Accent", t, new Vector2(-208f, 0f), new Vector2(12f, 126f), Color.white);
            var name = Text("Name", t, "Agent", 50f, Palette.UiText, new Vector2(-30f, 34f), new Vector2(330f, 64f), TextAlignmentOptions.Left, true);
            var role = Text("Role", t, "Role", 26f, Palette.UiMuted, new Vector2(118f, 34f), new Vector2(170f, 40f), TextAlignmentOptions.Right);
            var pill = Panel("Pill", t, new Vector2(-88f, -36f), new Vector2(214f, 50f), Color.green);
            var status = Text("Status", pill.transform, "FREE", 26f, new Color(0.07f, 0.08f, 0.1f), Vector2.zero, new Vector2(214f, 50f), TextAlignmentOptions.Center, true);
            var detail = Text("Detail", t, "", 24f, Palette.UiMuted, new Vector2(116f, -36f), new Vector2(190f, 50f), TextAlignmentOptions.Left);
            detail.enableAutoSizing = true;
            detail.fontSizeMin = 16f;
            detail.fontSizeMax = 24f;

            var ui = canvas.gameObject.AddComponent<StatusTagUI>();
            Wire(ui, "pill", pill);
            Wire(ui, "accentBar", accentBar);
            Wire(ui, "nameText", name);
            Wire(ui, "roleText", role);
            Wire(ui, "statusText", status);
            Wire(ui, "detailText", detail);
            return ui;
        }

        public static OutputPage BuildPage(Transform hold)
        {
            var canvas = WorldCanvas("Page", hold, Vector3.zero, Vector3.zero, new Vector2(500f, 660f), 0.00085f);
            Transform t = canvas.transform;
            Panel("Paper", t, Vector2.zero, new Vector2(500f, 660f), Palette.Paper);
            var header = Panel("Header", t, new Vector2(0f, 312f), new Vector2(500f, 36f), Palette.Amber);
            var title = Text("Title", t, "Task", 34f, new Color(0.12f, 0.13f, 0.15f), new Vector2(0f, 258f), new Vector2(450f, 56f), TextAlignmentOptions.Left, true);
            title.enableAutoSizing = true; title.fontSizeMin = 24f; title.fontSizeMax = 38f;
            var meta = Text("Meta", t, "", 21f, new Color(0.38f, 0.40f, 0.44f), new Vector2(0f, 222f), new Vector2(450f, 30f));
            Panel("Rule", t, new Vector2(0f, 202f), new Vector2(450f, 2f), new Color(0.82f, 0.80f, 0.76f), false);
            var bodyText = Text("Body", t, "", 22f, new Color(0.14f, 0.15f, 0.17f), new Vector2(0f, -30f), new Vector2(450f, 440f), TextAlignmentOptions.TopLeft);
            bodyText.enableAutoSizing = true; bodyText.fontSizeMin = 16f; bodyText.fontSizeMax = 28f;
            bodyText.lineSpacing = 4f;
            Panel("PromptBand", t, new Vector2(0f, -294f), new Vector2(480f, 52f), new Color(0.90f, 0.88f, 0.84f));
            var prompt = Text("Prompt", t, "", 19f, new Color(0.30f, 0.32f, 0.36f), new Vector2(0f, -294f), new Vector2(460f, 48f), TextAlignmentOptions.Center, true);

            var page = canvas.gameObject.AddComponent<OutputPage>();
            Wire(page, "titleText", title);
            Wire(page, "metaText", meta);
            Wire(page, "bodyText", bodyText);
            Wire(page, "promptText", prompt);
            Wire(page, "headerBar", header);
            return page;
        }
    }
}
