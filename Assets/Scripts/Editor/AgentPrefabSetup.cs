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
            // Friendly robot: glossy white shell, graphite joints, team-colour panels, a screen face.
            Material shell = Mat("Agent_Shell", new Color(0.93f, 0.94f, 0.96f), 0.72f);
            Material joint = Mat("Agent_Joint", new Color(0.19f, 0.2f, 0.23f), 0.45f, 0.3f);
            Material tint = Mat("Agent_Tint", new Color(0.6f, 0.6f, 0.65f), 0.5f);
            Material screen = Mat("Agent_FaceScreen", new Color(0.03f, 0.035f, 0.05f), 0.95f, 0.1f);
            Material eye = Emissive("Agent_Eye", new Color(0.75f, 0.95f, 1f), 1.4f);
            Material light = Emissive("Agent_Antenna", Palette.Teal, 2f);
            Material halo = Emissive("Agent_LinkHalo", Palette.Teal, 2.4f);
            Material ring = Emissive("Agent_SelectionRing", Palette.Teal, 2.2f);

            var root = new GameObject("AgentNPC");

            // ── Avatar rig ───────────────────────────────────────────────────
            var visual = Empty("Visual", root.transform, Vector3.zero);
            var body = Empty("Body", visual.transform, Vector3.zero);

            Transform Leg(string n, float x)
            {
                var pivot = Empty(n, body.transform, new Vector3(x, 0.92f, 0f)).transform;
                Ball($"{n}_Hip", pivot, Vector3.zero, Vector3.one * 0.13f, joint);
                Capsule($"{n}_Mesh", pivot, new Vector3(0f, -0.44f, 0f), new Vector3(0.14f, 0.42f, 0.14f), shell);
                Ball($"{n}_Shoe", pivot, new Vector3(0f, -0.87f, 0.04f), new Vector3(0.15f, 0.09f, 0.25f), joint);
                return pivot;
            }
            Transform legL = Leg("LegL", -0.1f);
            Transform legR = Leg("LegR", 0.1f);

            Cyl("Waist", body.transform, new Vector3(0f, 0.98f, 0f), 0.17f, 0.08f, joint, false);
            Capsule("Torso", body.transform, new Vector3(0f, 1.24f, 0f), new Vector3(0.42f, 0.33f, 0.3f), shell);
            var chest = Ball("ChestPanel", body.transform, new Vector3(0f, 1.3f, 0.115f), new Vector3(0.27f, 0.2f, 0.08f), tint);
            var core = Ball("CoreLight", body.transform, new Vector3(0f, 1.3f, 0.152f), new Vector3(0.05f, 0.05f, 0.02f), light);

            Transform Arm(string n, float x, out Renderer sleeve)
            {
                var pivot = Empty(n, body.transform, new Vector3(x, 1.45f, 0f)).transform;
                Ball($"{n}_Shoulder", pivot, Vector3.zero, Vector3.one * 0.105f, joint);
                sleeve = Capsule($"{n}_Mesh", pivot, new Vector3(0f, -0.26f, 0f), new Vector3(0.1f, 0.25f, 0.1f), tint).GetComponent<Renderer>();
                Ball($"{n}_Hand", pivot, new Vector3(0f, -0.55f, 0f), new Vector3(0.1f, 0.11f, 0.08f), shell);
                return pivot;
            }
            Transform armL = Arm("ArmL", -0.27f, out var sleeveL);
            Transform armR = Arm("ArmR", 0.27f, out var sleeveR);

            var head = Empty("Head", body.transform, new Vector3(0f, 1.56f, 0f)).transform;
            Cyl("Neck", head, new Vector3(0f, 0.03f, 0f), 0.05f, 0.08f, joint, false);
            Ball("Skull", head, new Vector3(0f, 0.18f, 0f), new Vector3(0.32f, 0.27f, 0.29f), shell);
            Ball("FaceScreen", head, new Vector3(0f, 0.18f, 0.105f), new Vector3(0.25f, 0.17f, 0.09f), screen);
            var earL = Cyl("EarL", head, new Vector3(-0.162f, 0.18f, 0f), 0.06f, 0.04f, tint, false, new Vector3(0f, 0f, 90f));
            var earR = Cyl("EarR", head, new Vector3(0.162f, 0.18f, 0f), 0.06f, 0.04f, tint, false, new Vector3(0f, 0f, 90f));
            Cyl("AntennaStalk", head, new Vector3(0f, 0.35f, 0f), 0.012f, 0.09f, joint, false);
            var antenna = Ball("AntennaLight", head, new Vector3(0f, 0.41f, 0f), Vector3.one * 0.055f, light);
            var linkHalo = Cyl("LinkHalo", head, new Vector3(0f, 0.47f, 0f), 0.13f, 0.008f, halo, false);
            linkHalo.SetActive(false);

            var face = Empty("Face", head, new Vector3(0f, 0.185f, 0.152f)).transform;
            var eyeL = Ball("EyeL", face, new Vector3(-0.045f, 0f, 0f), new Vector3(0.036f, 0.05f, 0.012f), eye);
            var eyeR = Ball("EyeR", face, new Vector3(0.045f, 0f, 0f), new Vector3(0.036f, 0.05f, 0.012f), eye);
            var glyphGO = new GameObject("FaceGlyph");
            glyphGO.transform.SetParent(face, false);
            glyphGO.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            glyphGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var glyph = glyphGO.AddComponent<TextMeshPro>();
            glyph.text = "^ ^";
            glyph.fontSize = 0.75f;
            glyph.fontStyle = FontStyles.Bold;
            glyph.alignment = TextAlignmentOptions.Center;
            glyph.textWrappingMode = TextWrappingModes.NoWrap;
            glyph.rectTransform.sizeDelta = new Vector2(0.22f, 0.12f);
            if (UiFont.Bold != null) glyph.font = UiFont.Bold;
            glyphGO.SetActive(false);
            foreach (var r in new[] { eyeL, eyeR, antenna, core, linkHalo }) NoShadows(r);

            var hold = Empty("HoldPoint", body.transform, new Vector3(0f, 1.02f, 0.36f), new Vector3(30f, 180f, 0f)).transform;

            var avatar = visual.AddComponent<AgentAvatar>();
            Wire(avatar, "body", body.transform);
            Wire(avatar, "legL", legL);
            Wire(avatar, "legR", legR);
            Wire(avatar, "armL", armL);
            Wire(avatar, "armR", armR);
            Wire(avatar, "head", head);
            Wire(avatar, "holdPoint", hold);
            Wire(avatar, "eyeL", eyeL.transform);
            Wire(avatar, "eyeR", eyeR.transform);
            WireArray(avatar, "eyeRenderers", new Object[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() });
            Wire(avatar, "faceGlyph", glyph);
            Wire(avatar, "antennaLight", antenna.GetComponent<Renderer>());
            Wire(avatar, "linkBadge", linkHalo);
            WireArray(avatar, "shirtRenderers", new Object[] { chest.GetComponent<Renderer>(), sleeveL, sleeveR });
            WireArray(avatar, "accentRenderers", new Object[] { earL.GetComponent<Renderer>(), earR.GetComponent<Renderer>() });

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
            // A wide sheet: title and meta on top, a scrolling body, the gesture hint at the bottom.
            const float W = 880f, H = 640f;
            var canvas = WorldCanvas("Page", hold, Vector3.zero, Vector3.zero, new Vector2(W, H), 0.00085f);
            Transform t = canvas.transform;
            Panel("Paper", t, Vector2.zero, new Vector2(W, H), Palette.Paper);
            var header = Panel("Header", t, new Vector2(0f, H * 0.5f - 16f), new Vector2(W, 32f), Palette.Amber);
            var title = Text("Title", t, "Task", 36f, new Color(0.12f, 0.13f, 0.15f), new Vector2(0f, 252f), new Vector2(W - 70f, 56f), TextAlignmentOptions.Left, true);
            title.enableAutoSizing = true; title.fontSizeMin = 28f; title.fontSizeMax = 42f;
            var meta = Text("Meta", t, "", 23f, new Color(0.38f, 0.40f, 0.44f), new Vector2(0f, 214f), new Vector2(W - 70f, 30f));
            Panel("Rule", t, new Vector2(0f, 194f), new Vector2(W - 70f, 2f), new Color(0.82f, 0.80f, 0.76f), false);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(t, false);
            viewport.anchoredPosition = new Vector2(0f, -36f);
            viewport.sizeDelta = new Vector2(W - 70f, 440f);
            var bodyText = Text("Body", viewport, "", 30f, new Color(0.14f, 0.15f, 0.17f), Vector2.zero, new Vector2(W - 70f, 440f), TextAlignmentOptions.TopLeft);
            var brt = bodyText.rectTransform;
            brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f);
            brt.offsetMin = new Vector2(0f, brt.offsetMin.y); brt.offsetMax = new Vector2(0f, brt.offsetMax.y);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(0f, 440f);
            bodyText.overflowMode = TextOverflowModes.Overflow;
            bodyText.lineSpacing = 4f;
            var more = Text("More", t, "", 15f, new Color(0.55f, 0.45f, 0.25f), new Vector2(0f, -263f), new Vector2(W - 70f, 22f), TextAlignmentOptions.Right, true);
            Panel("PromptBand", t, new Vector2(0f, -294f), new Vector2(W - 30f, 44f), new Color(0.90f, 0.88f, 0.84f));
            var prompt = Text("Prompt", t, "", 19f, new Color(0.30f, 0.32f, 0.36f), new Vector2(0f, -294f), new Vector2(W - 60f, 40f), TextAlignmentOptions.Center, true);

            var page = canvas.gameObject.AddComponent<OutputPage>();
            Wire(page, "titleText", title);
            Wire(page, "metaText", meta);
            Wire(page, "bodyText", bodyText);
            Wire(page, "promptText", prompt);
            Wire(page, "headerBar", header);
            Wire(page, "viewport", viewport);
            Wire(page, "moreHint", more);
            return page;
        }
    }
}
