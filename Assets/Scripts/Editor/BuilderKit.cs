using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>Shared helpers for the editor builders: saved URP materials, primitives, and world-space UI.</summary>
    public static class BuilderKit
    {
        public const string MaterialFolder = "Assets/Materials";

        // ── Palette ───────────────────────────────────────────────────────────
        public static class Palette
        {
            public static readonly Color Floor = new Color(0.80f, 0.77f, 0.72f);
            public static readonly Color Wall = new Color(0.93f, 0.91f, 0.87f);
            public static readonly Color Ceiling = new Color(0.96f, 0.95f, 0.93f);
            public static readonly Color Oak = new Color(0.70f, 0.52f, 0.34f);
            public static readonly Color Walnut = new Color(0.40f, 0.28f, 0.19f);
            public static readonly Color Charcoal = new Color(0.17f, 0.18f, 0.20f);
            public static readonly Color Graphite = new Color(0.26f, 0.27f, 0.30f);
            public static readonly Color RugTeal = new Color(0.27f, 0.39f, 0.40f);
            public static readonly Color RugSoft = new Color(0.36f, 0.46f, 0.46f);
            public static readonly Color Teal = new Color(0.31f, 0.82f, 0.77f);
            public static readonly Color Amber = new Color(1.00f, 0.70f, 0.25f);
            public static readonly Color Daylight = new Color(0.82f, 0.91f, 1.00f);
            public static readonly Color Leaf = new Color(0.27f, 0.50f, 0.30f);
            public static readonly Color LeafLight = new Color(0.40f, 0.62f, 0.35f);
            public static readonly Color Terracotta = new Color(0.72f, 0.42f, 0.30f);
            public static readonly Color Fabric = new Color(0.36f, 0.40f, 0.47f);
            public static readonly Color Paper = new Color(0.97f, 0.96f, 0.93f);
            public static readonly Color Skin = new Color(0.93f, 0.80f, 0.68f);
            // Horizon OS dark theme: near-black glassy panels, soft grey controls, one bright accent.
            public static readonly Color UiPanel = new Color(0.075f, 0.08f, 0.095f, 0.95f);
            public static readonly Color UiPanelRaised = new Color(0.13f, 0.14f, 0.165f, 1f);
            public static readonly Color UiButton = new Color(0.2f, 0.215f, 0.25f, 1f);
            public static readonly Color UiText = new Color(0.95f, 0.96f, 0.97f);
            public static readonly Color UiMuted = new Color(0.62f, 0.66f, 0.72f);
            public static readonly Color UiPrimary = new Color(0.11f, 0.52f, 0.48f);
            public static readonly Color UiBlue = new Color(0.16f, 0.3f, 0.5f);
            public static readonly Color UiDanger = new Color(0.44f, 0.17f, 0.19f);
            public static readonly Color UiWarn = new Color(0.4f, 0.31f, 0.15f);
        }

        // ── Materials (saved as assets so prefabs and builds keep them) ───────
        public static Material Mat(string name, Color color, float smoothness = 0.25f, float metallic = 0f)
        {
            Material m = LoadOrCreate(name);
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Glowing surfaces (windows, light panels, screens, accents). Uses URP Unlit: it is cheaper on
        /// Quest than Lit + emission, and needs no keyword (URP's material validation strips _EMISSION
        /// from script-created Lit materials).
        /// </summary>
        public static Material Emissive(string name, Color color, float intensity)
        {
            Material m = LoadOrCreate(name);
            m.shader = Shader.Find("Universal Render Pipeline/Unlit");
            float k = Mathf.Clamp(intensity, 0.4f, 1.15f);
            m.SetColor("_BaseColor", new Color(Mathf.Clamp01(color.r * k), Mathf.Clamp01(color.g * k), Mathf.Clamp01(color.b * k), 1f));
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material Glass(string name, Color tint, float alpha)
        {
            Material m = LoadOrCreate(name);
            m.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, alpha));
            m.SetFloat("_Smoothness", 0.92f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material LoadOrCreate(string name)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets", "Materials");
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ── Primitives ────────────────────────────────────────────────────────
        public static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat,
            bool collider = true, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Setup(go, name, parent, localPos, size, mat, euler);
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>Cylinder by radius and height. Uses a mesh collider (the default capsule is wrong for flat discs).</summary>
        public static GameObject Cyl(string name, Transform parent, Vector3 localPos, float radius, float height, Material mat,
            bool collider = true, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Setup(go, name, parent, localPos, new Vector3(radius * 2f, height * 0.5f, radius * 2f), mat, euler);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (collider) go.AddComponent<MeshCollider>().convex = true;
            return go;
        }

        /// <summary>
        /// Curved slab: an annulus sector (top, bottom, inner and outer walls, end caps). Angles are measured
        /// from +Z towards +X, so 0 is straight ahead. localPos is the ring centre at the slab's vertical middle.
        /// </summary>
        public static GameObject Arc(string name, Transform parent, Vector3 localPos, float inner, float outer, float fromDeg, float toDeg,
            float height, Material mat, bool collider = true, int segments = 64)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            float h = height * 0.5f;
            Vector3 P(float r, float deg, float y) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r); }
            Vector3 D(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                for (int k = 0; k < 4; k++) norms.Add(n);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
            for (int s = 0; s < segments; s++)
            {
                float a0 = Mathf.Lerp(fromDeg, toDeg, s / (float)segments), a1 = Mathf.Lerp(fromDeg, toDeg, (s + 1) / (float)segments);
                float am = (a0 + a1) * 0.5f;
                Quad(P(inner, a0, h), P(outer, a0, h), P(outer, a1, h), P(inner, a1, h), Vector3.up);                 // top
                Quad(P(inner, a1, -h), P(outer, a1, -h), P(outer, a0, -h), P(inner, a0, -h), Vector3.down);           // bottom
                Quad(P(outer, a0, h), P(outer, a0, -h), P(outer, a1, -h), P(outer, a1, h), D(am));                    // outer wall
                Quad(P(inner, a1, h), P(inner, a1, -h), P(inner, a0, -h), P(inner, a0, h), -D(am));                   // inner wall
            }
            Vector3 c0 = Vector3.Cross(Vector3.up, D(fromDeg)), c1 = Vector3.Cross(D(toDeg), Vector3.up);
            Quad(P(inner, fromDeg, h), P(inner, fromDeg, -h), P(outer, fromDeg, -h), P(outer, fromDeg, h), c0);       // end caps
            Quad(P(outer, toDeg, h), P(outer, toDeg, -h), P(inner, toDeg, -h), P(inner, toDeg, h), c1);
            var mesh = new Mesh { name = name + "_Arc" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        public static GameObject Ball(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Setup(go, name, parent, localPos, size, mat, null);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static GameObject Capsule(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Setup(go, name, parent, localPos, size, mat, null);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static GameObject Empty(string name, Transform parent, Vector3 localPos, Vector3? euler = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            return go;
        }

        private static void Setup(GameObject go, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, Vector3? euler)
        {
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        public static void NoShadows(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ── Meta UI kit assets (Interaction SDK sample: Roboto + icon set) ─────

        public static class UiFont
        {
            private const string Dir = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Fonts/";
            public static TMP_FontAsset Regular => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Dir + "Roboto-Regular SDF.asset");
            public static TMP_FontAsset Medium => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Dir + "Roboto-Medium SDF.asset");
            public static TMP_FontAsset Bold => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Dir + "Roboto-Bold SDF.asset");
        }

        /// <summary>Icon texture from the Interaction SDK sample set, e.g. "Icons/Plus" or "ThumbsUp".</summary>
        public static Texture2D IconTexture(string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Textures/{name}.png");

        public static RawImage Icon(string name, Transform parent, string icon, Vector2 pos, float size, Color color)
        {
            var go = new GameObject(name);
            Rect(go, parent, pos, new Vector2(size, size));
            var img = go.AddComponent<RawImage>();
            img.texture = IconTexture(icon);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Puts an icon to the left of a button's label.</summary>
        public static void AddIcon(Button button, string icon, float size = 26f)
        {
            var rt = (RectTransform)button.transform;
            float w = rt.sizeDelta.x;
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            float textW = label != null ? Mathf.Min(label.GetPreferredValues(label.text).x, w - size - 24f) : 0f;
            float total = size + 10f + textW;
            Icon("Icon", button.transform, icon, new Vector2(-total * 0.5f + size * 0.5f, 0f), size, Color.white);
            if (label != null)
            {
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                label.rectTransform.sizeDelta = new Vector2(textW + 8f, rt.sizeDelta.y);
                label.rectTransform.anchoredPosition = new Vector2(-total * 0.5f + size + 10f + textW * 0.5f, 0f);
            }
        }

        // ── World-space UI ────────────────────────────────────────────────────
        private static Sprite rounded;
        public static Sprite Rounded => rounded != null ? rounded : (rounded = RoundedSprite());

        private const string RoundedPath = "Assets/UI/RoundedRect.png";

        /// <summary>
        /// A crisp anti-aliased rounded rectangle (corner radius 48 px, 9-sliced). Unity's built-in UISprite
        /// has soft fading edges, which turn large panels partly see-through when its corners are scaled up.
        /// Image.pixelsPerUnitMultiplier sets the radius: 2 = 24 units.
        /// </summary>
        private static Sprite RoundedSprite()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedPath);
            if (existing != null) return existing;
            const int size = 128, r = 48;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    byte a = (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            if (!AssetDatabase.IsValidFolder("Assets/UI")) AssetDatabase.CreateFolder("Assets", "UI");
            System.IO.File.WriteAllBytes(RoundedPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(RoundedPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(RoundedPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spriteBorder = new Vector4(r + 4, r + 4, r + 4, r + 4);
            imp.spritePixelsPerUnit = 100f;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(RoundedPath);
        }

        public static Canvas WorldCanvas(string name, Transform parent, Vector3 localPos, Vector3 euler, Vector2 size, float metresPerUnit)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;   // UI draws over see-through 3D (the glass desk); special panels use higher orders
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            go.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localPosition = localPos;
            rt.localRotation = Quaternion.Euler(euler);
            rt.localScale = Vector3.one * metresPerUnit;
            return canvas;
        }

        public static RectTransform Rect(GameObject go, Transform parent, Vector2 pos, Vector2 size)
        {
            if (go.GetComponent<RectTransform>() == null) go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static Image Panel(string name, Transform parent, Vector2 pos, Vector2 size, Color color, bool roundedCorners = true)
        {
            var go = new GameObject(name);
            Rect(go, parent, pos, size);
            var img = go.AddComponent<Image>();
            img.color = color;
            if (roundedCorners)
            {
                img.sprite = Rounded;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 2.4f;   // ~20 unit corner radius
            }
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color color,
            Vector2 pos, Vector2 box, TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false)
        {
            var go = new GameObject(name);
            Rect(go, parent, pos, box);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Truncate;
            t.raycastTarget = false;
            var font = bold ? UiFont.Bold : UiFont.Medium;
            if (font != null) t.font = font;
            else if (bold) t.fontStyle = FontStyles.Bold;
            return t;
        }

        public static Button Button(string name, Transform parent, string label, Vector2 pos, Vector2 size, Color color,
            out TextMeshProUGUI labelText, float fontSize = 22f)
        {
            var go = new GameObject(name);
            Rect(go, parent, pos, size);
            var img = go.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 2.2f;   // pill-like buttons
            img.color = Color.white;
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
            colors.selectedColor = color;
            colors.disabledColor = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.6f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            labelText = Text("Label", go.transform, label, fontSize, Color.white, Vector2.zero, size, TextAlignmentOptions.Center, true);
            Stretch(labelText.rectTransform, 4f);
            return button;
        }

        public static void Wire(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"[Builder] {target.GetType().Name}.{field} not found"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void WireArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"[Builder] {target.GetType().Name}.{field} not found"); return; }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
