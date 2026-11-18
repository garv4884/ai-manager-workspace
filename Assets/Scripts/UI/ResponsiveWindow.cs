using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Makes a desk monitor resizable in width and height independently, like a Horizon OS window.
    /// At start every element is re-anchored proportionally to the panel (buttons, text boxes and groups
    /// stretch with it); text scales gently with the smaller dimension so it stays readable; the bezel
    /// follows. Sizes are clamped to the window type's limits and remembered.
    /// </summary>
    public class ResponsiveWindow : MonoBehaviour, IResizableWindow
    {
        [SerializeField] private string id = "window";
        [SerializeField] private RectTransform canvas;
        [SerializeField] private Vector2 minSize = new Vector2(380f, 440f);
        [SerializeField] private Vector2 maxSize = new Vector2(900f, 1000f);

        private Vector2 design;
        private readonly List<(TextMeshProUGUI text, float size, float min, float max)> texts = new List<(TextMeshProUGUI, float, float, float)>();
        private Transform bezel;

        public Vector2 Size => canvas.sizeDelta;
        public Vector2 MinSize => minSize;
        public Vector2 MaxSize => maxSize;
        public bool Live => true;
        public void ResizeLive(Vector2 size) => Apply(size);

        private void Awake()
        {
            design = canvas.sizeDelta;
            Proportional(canvas, design);
            foreach (var t in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
                texts.Add((t, t.fontSize, t.fontSizeMin, t.fontSizeMax));
            bezel = canvas.Find("Bezel");
        }

        private void Start()
        {
            var st = AppSettings.Current.Window(id);
            if (st.width > 0f && st.height > 0f) Apply(new Vector2(st.width, st.height));
        }

        /// <summary>Re-anchors point-anchored children of plain groups to proportional anchors (recursively for groups).</summary>
        private static void Proportional(RectTransform parent, Vector2 parentSize)
        {
            foreach (Transform c in parent)
            {
                if (!(c is RectTransform rt)) continue;
                if (rt.name.StartsWith("ISDK_")) continue;   // Interaction SDK surfaces size themselves
                bool pointAnchored = rt.anchorMin == rt.anchorMax;
                Vector2 size = pointAnchored ? rt.sizeDelta : rt.rect.size;
                if (pointAnchored && parentSize.x > 0f && parentSize.y > 0f)
                {
                    Vector2 anchorPoint = (rt.anchorMin - new Vector2(0.5f, 0.5f)) * parentSize;
                    Vector2 centre = anchorPoint + rt.anchoredPosition + (new Vector2(0.5f, 0.5f) - rt.pivot) * size;
                    Vector2 min = (centre - size * 0.5f) / parentSize + new Vector2(0.5f, 0.5f);
                    Vector2 max = (centre + size * 0.5f) / parentSize + new Vector2(0.5f, 0.5f);
                    rt.anchorMin = min;
                    rt.anchorMax = max;
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                }
                // Recurse into plain groups (no graphic, not a control): their children reflow too.
                if (rt.GetComponent<Graphic>() == null && rt.GetComponent<Selectable>() == null)
                    Proportional(rt, size);
            }
        }

        public void Resize(Vector2 size)
        {
            Apply(size);
            var st = AppSettings.Current.Window(id);
            st.width = canvas.sizeDelta.x;
            st.height = canvas.sizeDelta.y;
            AppSettings.Save();
        }

        private void Apply(Vector2 size)
        {
            size = new Vector2(Mathf.Clamp(size.x, minSize.x, maxSize.x), Mathf.Clamp(size.y, minSize.y, maxSize.y));
            canvas.sizeDelta = size;
            float k = Mathf.Clamp(Mathf.Min(size.x / design.x, size.y / design.y), 0.8f, 1.35f);
            foreach (var (text, s, mn, mx) in texts)
            {
                if (text == null) continue;
                text.fontSize = s * k;
                text.fontSizeMin = mn * k;
                text.fontSizeMax = mx * k;
            }
            if (bezel != null) bezel.localScale = new Vector3(size.x + 20f, size.y + 20f, bezel.localScale.z);
            var chrome = GetComponent<WindowChrome>();
            if (chrome != null) chrome.LayoutForSize(size);
        }
    }
}
