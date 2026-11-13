using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.AI;
using TMPro;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Creates a complete Agent NPC prefab with all components and UI child elements.
    /// Run via Tools > AI Manager > Create Agent Prefab
    /// </summary>
    public static class AgentPrefabSetup
    {
        [MenuItem("Tools/AI Manager/Create Agent Prefab")]
        public static void CreateAgentPrefab()
        {
            // ── 1. Root GameObject ─────────────────────────────────────────────
            var agentGO = new GameObject("AgentNPC");

            // ── 2. Body Visual (human proportion avatar) ──────────────────────
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(agentGO.transform);
            body.transform.localPosition = new Vector3(0, 0.65f, 0);
            body.transform.localScale = new Vector3(0.35f, 0.55f, 0.35f);
            SetMaterialURP(body, new Color(0.2f, 0.45f, 0.85f));

            // ── 3. Head Visual ─────────────────────────────────────────────────
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(agentGO.transform);
            head.transform.localPosition = new Vector3(0, 1.35f, 0);
            head.transform.localScale = Vector3.one * 0.22f;
            SetMaterialURP(head, new Color(0.92f, 0.78f, 0.65f));

            // ── 4. Page Hold Point (where output page floats) ──────────────────
            var pageHold = new GameObject("PageHoldPoint");
            pageHold.transform.SetParent(agentGO.transform);
            pageHold.transform.localPosition = new Vector3(0.20f, 0.85f, 0.30f);
            pageHold.transform.localRotation = Quaternion.Euler(15f, -15f, 0);

            // ── 5. Status Tag Anchor ───────────────────────────────────────────
            var tagAnchor = new GameObject("StatusTagAnchor");
            tagAnchor.transform.SetParent(agentGO.transform);
            tagAnchor.transform.localPosition = new Vector3(0, 1.58f, 0);

            // ── 6. Status Tag UI (World Space Billboard) ───────────────────────
            var tagCanvasGO = new GameObject("StatusTagCanvas");
            tagCanvasGO.transform.SetParent(tagAnchor.transform);
            tagCanvasGO.transform.localPosition = Vector3.zero;

            var tagCanvas = tagCanvasGO.AddComponent<Canvas>();
            tagCanvas.renderMode = RenderMode.WorldSpace;
            tagCanvasGO.AddComponent<CanvasScaler>();
            tagCanvasGO.AddComponent<GraphicRaycaster>();

            var tagRect = tagCanvasGO.GetComponent<RectTransform>();
            tagRect.sizeDelta = new Vector2(240, 80);
            tagRect.localScale = Vector3.one * 0.0011f;

            // Background
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(tagCanvasGO.transform, false);
            bgGO.transform.localScale = Vector3.one;
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.color = new Color(0.1f, 0.12f, 0.18f, 0.9f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Dot
            var dotGO = new GameObject("StatusDot");
            dotGO.transform.SetParent(tagCanvasGO.transform, false);
            dotGO.transform.localScale = Vector3.one;
            var dotImg = dotGO.AddComponent<Image>();
            dotImg.color = Color.green;
            var dotRect = dotGO.GetComponent<RectTransform>();
            dotRect.anchorMin = new Vector2(0, 0.5f);
            dotRect.anchorMax = new Vector2(0, 0.5f);
            dotRect.sizeDelta = new Vector2(16, 16);
            dotRect.anchoredPosition = new Vector2(24, 0);

            // Agent Name text
            var nameGO = new GameObject("AgentNameText");
            nameGO.transform.SetParent(tagCanvasGO.transform, false);
            nameGO.transform.localScale = Vector3.one;
            var nameTmp = nameGO.AddComponent<TextMeshProUGUI>();
            nameTmp.text = "Agent Name";
            nameTmp.fontSize = 24;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.color = Color.white;
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0, 0.5f);
            nameRect.anchorMax = new Vector2(1, 1);
            nameRect.offsetMin = new Vector2(45, 0);
            nameRect.offsetMax = new Vector2(-15, -10);

            // Role text
            var roleGO = new GameObject("AgentRoleText");
            roleGO.transform.SetParent(tagCanvasGO.transform, false);
            roleGO.transform.localScale = Vector3.one;
            var roleTmp = roleGO.AddComponent<TextMeshProUGUI>();
            roleTmp.text = "Code Specialist";
            roleTmp.fontSize = 16;
            roleTmp.color = new Color(0.7f, 0.8f, 0.95f);
            var roleRect = roleGO.GetComponent<RectTransform>();
            roleRect.anchorMin = new Vector2(0, 0.25f);
            roleRect.anchorMax = new Vector2(1, 0.55f);
            roleRect.offsetMin = new Vector2(45, 0);
            roleRect.offsetMax = new Vector2(-15, 0);

            // Status text
            var statusGO = new GameObject("StatusText");
            statusGO.transform.SetParent(tagCanvasGO.transform, false);
            statusGO.transform.localScale = Vector3.one;
            var statusTmp = statusGO.AddComponent<TextMeshProUGUI>();
            statusTmp.text = "Free";
            statusTmp.fontSize = 18;
            statusTmp.fontStyle = FontStyles.Bold;
            statusTmp.color = Color.green;
            var statusRect = statusGO.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0, 0);
            statusRect.anchorMax = new Vector2(1, 0.28f);
            statusRect.offsetMin = new Vector2(45, 8);
            statusRect.offsetMax = new Vector2(-15, 0);

            var statusTagUI = tagCanvasGO.AddComponent<StatusTagUI>();
            // Use SerializedObject to wire private fields
            var tagSO = new SerializedObject(statusTagUI);
            tagSO.FindProperty("canvas").objectReferenceValue = tagCanvas;
            tagSO.FindProperty("backgroundImage").objectReferenceValue = bgImg;
            tagSO.FindProperty("statusIndicatorDot").objectReferenceValue = dotImg;
            tagSO.FindProperty("agentNameText").objectReferenceValue = nameTmp;
            tagSO.FindProperty("agentRoleText").objectReferenceValue = roleTmp;
            tagSO.FindProperty("statusText").objectReferenceValue = statusTmp;
            tagSO.ApplyModifiedProperties();

            // ── 7. Output Page (A4 Paper World Space UI) ──────────────────────
            var pageGO = new GameObject("OutputPage");
            pageGO.transform.SetParent(pageHold.transform);
            pageGO.transform.localPosition = Vector3.zero;
            pageGO.transform.localRotation = Quaternion.identity;

            var pageCanvas = pageGO.AddComponent<Canvas>();
            pageCanvas.renderMode = RenderMode.WorldSpace;
            pageGO.AddComponent<CanvasScaler>();
            pageGO.AddComponent<GraphicRaycaster>();

            var pageRect = pageGO.GetComponent<RectTransform>();
            pageRect.sizeDelta = new Vector2(280, 396);
            pageRect.localScale = Vector3.one * 0.00075f;

            // Paper Background
            var paperBgGO = new GameObject("PaperBackground");
            paperBgGO.transform.SetParent(pageGO.transform, false);
            paperBgGO.transform.localScale = Vector3.one;
            var paperBgImg = paperBgGO.AddComponent<Image>();
            paperBgImg.color = new Color(0.96f, 0.95f, 0.92f);
            var paperBgRect = paperBgGO.GetComponent<RectTransform>();
            paperBgRect.anchorMin = Vector2.zero;
            paperBgRect.anchorMax = Vector2.one;
            paperBgRect.offsetMin = Vector2.zero;
            paperBgRect.offsetMax = Vector2.zero;

            // Banner
            var bannerGO = new GameObject("StatusBanner");
            bannerGO.transform.SetParent(pageGO.transform, false);
            bannerGO.transform.localScale = Vector3.one;
            var bannerImg = bannerGO.AddComponent<Image>();
            bannerImg.color = new Color(0.15f, 0.6f, 1.0f);
            var bannerRect = bannerGO.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0, 0.92f);
            bannerRect.anchorMax = Vector2.one;
            bannerRect.offsetMin = Vector2.zero;
            bannerRect.offsetMax = Vector2.zero;

            // Header Title
            var titleGO = new GameObject("HeaderTitle");
            titleGO.transform.SetParent(pageGO.transform, false);
            titleGO.transform.localScale = Vector3.one;
            var titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "Task Output";
            titleTmp.fontSize = 22;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = new Color(0.1f, 0.1f, 0.15f);
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.05f, 0.82f);
            titleRect.anchorMax = new Vector2(0.95f, 0.91f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            // Agent signature
            var sigGO = new GameObject("Signature");
            sigGO.transform.SetParent(pageGO.transform, false);
            sigGO.transform.localScale = Vector3.one;
            var sigTmp = sigGO.AddComponent<TextMeshProUGUI>();
            sigTmp.text = "Agent: Claude";
            sigTmp.fontSize = 15;
            sigTmp.fontStyle = FontStyles.Italic;
            sigTmp.color = new Color(0.4f, 0.4f, 0.45f);
            var sigRect = sigGO.GetComponent<RectTransform>();
            sigRect.anchorMin = new Vector2(0.05f, 0.76f);
            sigRect.anchorMax = new Vector2(0.95f, 0.82f);
            sigRect.offsetMin = Vector2.zero;
            sigRect.offsetMax = Vector2.zero;

            // Body content
            var bodyGO = new GameObject("BodyContent");
            bodyGO.transform.SetParent(pageGO.transform, false);
            bodyGO.transform.localScale = Vector3.one;
            var bodyTmp = bodyGO.AddComponent<TextMeshProUGUI>();
            bodyTmp.text = "Awaiting instructions...";
            bodyTmp.fontSize = 16;
            bodyTmp.color = new Color(0.12f, 0.12f, 0.15f);
            var bodyTextRect = bodyGO.GetComponent<RectTransform>();
            bodyTextRect.anchorMin = new Vector2(0.05f, 0.18f);
            bodyTextRect.anchorMax = new Vector2(0.95f, 0.75f);
            bodyTextRect.offsetMin = Vector2.zero;
            bodyTextRect.offsetMax = Vector2.zero;

            // Action prompt
            var promptGO = new GameObject("ActionPrompt");
            promptGO.transform.SetParent(pageGO.transform, false);
            promptGO.transform.localScale = Vector3.one;
            var promptTmp = promptGO.AddComponent<TextMeshProUGUI>();
            promptTmp.text = "Thumbs Up to Approve | Thumbs Down to Reject";
            promptTmp.fontSize = 14;
            promptTmp.fontStyle = FontStyles.Bold;
            promptTmp.color = new Color(0.9f, 0.45f, 0.05f);
            var promptRect = promptGO.GetComponent<RectTransform>();
            promptRect.anchorMin = new Vector2(0.05f, 0.02f);
            promptRect.anchorMax = new Vector2(0.95f, 0.15f);
            promptRect.offsetMin = Vector2.zero;
            promptRect.offsetMax = Vector2.zero;

            var pageRenderer = pageGO.AddComponent<OutputPageRenderer>();
            var pageSO = new SerializedObject(pageRenderer);
            pageSO.FindProperty("headerTitleText").objectReferenceValue = titleTmp;
            pageSO.FindProperty("agentSignatureText").objectReferenceValue = sigTmp;
            pageSO.FindProperty("bodyContentText").objectReferenceValue = bodyTmp;
            pageSO.FindProperty("actionPromptText").objectReferenceValue = promptTmp;
            pageSO.FindProperty("statusBanner").objectReferenceValue = bannerImg;
            pageSO.FindProperty("worldCanvas").objectReferenceValue = pageCanvas;
            pageSO.ApplyModifiedProperties();

            pageGO.SetActive(false); // Hidden by default until presenting

            // ── 8. Navigation & Interaction Components ─────────────────────────
            var nav = agentGO.AddComponent<NavMeshAgent>();
            nav.height = 1.55f;
            nav.radius = 0.22f;
            nav.speed = 2.2f;
            nav.angularSpeed = 240f;
            nav.acceleration = 8f;
            nav.stoppingDistance = 0.3f;

            var col = agentGO.AddComponent<CapsuleCollider>();
            col.height = 1.55f;
            col.radius = 0.22f;
            col.center = new Vector3(0, 0.775f, 0);

            // AgentController
            var controller = agentGO.AddComponent<AgentController>();
            var ctlSO = new SerializedObject(controller);
            ctlSO.FindProperty("navAgent").objectReferenceValue = nav;
            ctlSO.FindProperty("statusTag").objectReferenceValue = statusTagUI;
            ctlSO.FindProperty("outputPage").objectReferenceValue = pageRenderer;
            ctlSO.FindProperty("pageHoldPoint").objectReferenceValue = pageHold.transform;
            ctlSO.ApplyModifiedProperties();

            // AgentGrabInteractable for picking up agents & rearranging
            var grab = agentGO.AddComponent<AgentGrabInteractable>();
            var grabSO = new SerializedObject(grab);
            grabSO.FindProperty("agentController").objectReferenceValue = controller;
            grabSO.FindProperty("agentRenderer").objectReferenceValue = body.GetComponent<MeshRenderer>();
            grabSO.ApplyModifiedProperties();

            // ── 9. Save as Prefab ──────────────────────────────────────────────
            // Enforce (1, 1, 1) scale on every object under every canvas in prefab
            var allCanvases = agentGO.GetComponentsInChildren<Canvas>(true);
            foreach (var c in allCanvases)
            {
                var allTransforms = c.GetComponentsInChildren<Transform>(true);
                foreach (var t in allTransforms)
                {
                    if (t != c.transform)
                    {
                        t.localScale = Vector3.one;
                    }
                }
            }

            const string prefabFolder = "Assets/Prefabs";
            if (!AssetDatabase.IsValidFolder(prefabFolder))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            string prefabPath = $"{prefabFolder}/AgentNPC.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(agentGO, prefabPath);
            Object.DestroyImmediate(agentGO);

            if (prefab != null)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[AgentPrefabSetup] ✅ Complete AgentNPC prefab saved to {prefabPath}");
                Selection.activeObject = prefab;
            }
            else
            {
                Debug.LogError("[AgentPrefabSetup] Failed to save prefab!");
            }
        }

        private static void SetMaterialURP(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            if (shader == null) return;
            var mat = new Material(shader) { color = color };
            renderer.sharedMaterial = mat;
        }
    }
}
