using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Runtime UI kit for panels that build their own content (settings window, tutorial): Horizon-style
    /// dark panels, pill buttons, switches and segmented controls, all with Meta's Roboto font.
    /// Assets come from <see cref="UiKitAssets"/> in the scene.
    /// </summary>
    public static class UiKit
    {
        public static TMP_FontAsset Medium, Bold;
        public static Sprite Rounded;
        public static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();

        public static readonly Color Panel = new Color(0.075f, 0.08f, 0.095f, 0.96f);
        public static readonly Color Raised = new Color(0.12f, 0.13f, 0.155f, 1f);
        public static readonly Color Control = new Color(0.19f, 0.2f, 0.235f, 1f);
        public static readonly Color Text = new Color(0.95f, 0.96f, 0.97f);
        public static readonly Color Muted = new Color(0.62f, 0.66f, 0.72f);
        public static readonly Color Accent = new Color(0.31f, 0.82f, 0.77f);
        public static readonly Color Primary = new Color(0.12f, 0.56f, 0.52f);
        public static readonly Color Danger = new Color(0.5f, 0.19f, 0.21f);
        public static readonly Color Divider = new Color(1f, 1f, 1f, 0.08f);

        public static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        // Box radius is Image.pixelsPerUnitMultiplier on the 48 px rounded sprite: 2 = 24 units, 3 = 16 units.

        /// <summary>Top-left anchored rect (for flowing rows inside a content area).</summary>
        public static RectTransform TopLeft(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var rt = Rect(name, parent, pos, size);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            return rt;
        }

        public static Image Box(Transform parent, Vector2 pos, Vector2 size, Color color, float radius = 2.4f, string name = "Box", bool topLeft = false)
        {
            var rt = topLeft ? TopLeft(name, parent, pos, size) : Rect(name, parent, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (Rounded != null && radius > 0f)
            {
                img.sprite = Rounded;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = radius;
            }
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, Vector2 pos, Vector2 box,
            TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false, bool topLeft = false)
        {
            var rt = topLeft ? TopLeft("Label", parent, pos, box) : Rect("Label", parent, pos, box);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var font = bold ? Bold : Medium;
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string label, Vector2 pos, Vector2 size, Color color, Action onClick,
            out TextMeshProUGUI text, float fontSize = 18f, bool topLeft = false, string icon = null)
        {
            var img = Box(parent, pos, size, Color.white, 2.2f, "Button_" + label, topLeft);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            Tint(b, color);
            text = Label(img.transform, label, fontSize, Text, Vector2.zero, size - new Vector2(16f, 4f), TextAlignmentOptions.Center, true);
            if (!string.IsNullOrEmpty(icon) && Icons.TryGetValue(icon, out var tex) && tex != null)
            {
                float s = Mathf.Min(size.y * 0.5f, 26f);
                float textW = Mathf.Min(text.GetPreferredValues(label).x, size.x - s - 30f);
                float total = s + 8f + textW;
                var ic = Rect("Icon", img.transform, new Vector2(-total * 0.5f + s * 0.5f, 0f), new Vector2(s, s)).gameObject.AddComponent<RawImage>();
                ic.texture = tex;
                ic.raycastTarget = false;
                text.rectTransform.sizeDelta = new Vector2(textW + 6f, size.y);
                text.rectTransform.anchoredPosition = new Vector2(-total * 0.5f + s + 8f + textW * 0.5f, 0f);
            }
            b.onClick.AddListener(() =>
            {
                var gm = GameManager.Instance;
                if (gm != null && gm.Queue != null && gm.Queue.Desk != null) gm.Queue.Desk.Play(ReviewDesk.Sfx.Click);
                onClick?.Invoke();
            });
            return b;
        }

        public static void Tint(Button b, Color c)
        {
            var colors = b.colors;
            colors.normalColor = c;
            colors.selectedColor = c;
            colors.highlightedColor = Color.Lerp(c, Color.white, 0.14f);
            colors.pressedColor = Color.Lerp(c, Color.black, 0.2f);
            colors.disabledColor = new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 0.55f);
            colors.fadeDuration = 0.08f;
            b.colors = colors;
        }

        public static RawImage Icon(Transform parent, string icon, Vector2 pos, float size, Color color)
        {
            var img = Rect("Icon", parent, pos, new Vector2(size, size)).gameObject.AddComponent<RawImage>();
            if (Icons.TryGetValue(icon ?? "", out var tex)) img.texture = tex;
            img.color = color;
            img.raycastTarget = false;
            img.enabled = img.texture != null;
            return img;
        }

        /// <summary>
        /// Dropdown list that opens under (or over) a button: one row per option, the current one highlighted.
        /// Returns the popup; picking an option or calling Destroy on it closes it.
        /// </summary>
        public static GameObject Popup(Transform parent, Vector2 topCenter, float width, string[] options, int current, Action<int> pick)
        {
            float h = 44f, gap = 4f, pad = 8f;
            float total = options.Length * (h + gap) - gap + pad * 2f;
            var root = Rect("Popup", parent, topCenter + new Vector2(0f, -total * 0.5f), new Vector2(width, total));
            root.SetAsLastSibling();
            Box(root, Vector2.zero, root.sizeDelta, new Color(0.05f, 0.055f, 0.065f, 0.99f), 2.4f, "Back").raycastTarget = true;
            float y = total * 0.5f - pad - h * 0.5f;
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                Button(root, options[i], new Vector2(0f, y), new Vector2(width - pad * 2f, h), i == current ? Primary : Control, () =>
                {
                    UnityEngine.Object.Destroy(root.gameObject);
                    pick(index);
                }, out _, 16f);
                y -= h + gap;
            }
            return root.gameObject;
        }

        /// <summary>A switch: a pill showing ON/OFF that flips a setting.</summary>
        public static Action Switch(Transform parent, Vector2 pos, Func<bool> get, Action<bool> set, bool topLeft = true)
        {
            Button b = null;
            TextMeshProUGUI t = null;
            b = Button(parent, "OFF", pos, new Vector2(96f, 40f), Control, () => set(!get()), out t, 15f, topLeft);
            return () =>
            {
                bool on = get();
                t.text = on ? "ON" : "OFF";
                Tint(b, on ? Primary : Control);
            };
        }

        /// <summary>Segmented control: one button per option, the current one highlighted.</summary>
        public static Action Segmented(Transform parent, Vector2 pos, float width, string[] options, Func<int> get, Action<int> set,
            float height = 40f, float fontSize = 15f)
        {
            var buttons = new List<Button>();
            float w = (width - (options.Length - 1) * 6f) / options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                buttons.Add(Button(parent, options[i], pos + new Vector2(i * (w + 6f), 0f), new Vector2(w, height), Control,
                    () => set(index), out _, fontSize, true));
            }
            return () =>
            {
                int cur = get();
                for (int i = 0; i < buttons.Count; i++) Tint(buttons[i], i == cur ? Primary : Control);
            };
        }
    }
}
