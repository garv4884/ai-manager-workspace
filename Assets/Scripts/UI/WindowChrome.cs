using System.Collections;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>A window whose layout can reflow to any width / height (within limits).</summary>
    public interface IResizableWindow
    {
        Vector2 Size { get; }          // canvas units
        Vector2 MinSize { get; }
        Vector2 MaxSize { get; }
        void Resize(Vector2 size);
        /// <summary>True if the layout can update every frame while dragging (otherwise a preview frame shows).</summary>
        bool Live { get; }
        /// <summary>Cheap update during the drag (no saving).</summary>
        void ResizeLive(Vector2 size);
    }

    /// <summary>
    /// Horizon OS-style window controls for a desk panel. A slim bar floats under the window:
    ///
    ///   [ minimize ]  [ S / M / L ]  ═══ grab ═══  [ snap ]  [ close / dock ]
    ///
    /// - Grab: point at the pill and pinch, then move your hand; the window follows and keeps facing you.
    /// - Snap: Desk (docked) · Left · Right · Theater (big, high in front of you).
    /// - Minimize: desk monitors slide down into the desk and leave a small restore pill.
    /// - Size: compact / standard / large, on top of the global UI size in Settings.
    /// Window placement is remembered per window (AppSettings).
    /// </summary>
    public class WindowChrome : MonoBehaviour
    {
        public enum Preset { Desk, Left, Right, Theater, Free }

        [SerializeField] private string id = "window";
        [SerializeField] private string title = "Window";
        [SerializeField] private GameObject content;          // the window's canvas
        [SerializeField] private Canvas bar;                   // bar canvas (ISDK ray + poke set up by the builder)
        [SerializeField] private Transform grabHandle;        // collider + RayInteractable under the bar
        [SerializeField] private float barOffsetY = -0.33f;   // bar position under the window centre (metres, unscaled)
        [SerializeField] private bool closable;               // settings: close hides; monitors: minimize instead
        [SerializeField] private bool slideIntoDesk = true;   // monitors sink below the desk when minimized

        private Vector3 dockPos;
        private Quaternion dockRot;
        private Vector3 baseScale;
        private Preset preset;
        private float sizeFactor = 1f;
        private bool minimized;
        private Coroutine anim;
        private GameObject snapRow;
        private TextMeshProUGUI sizeLabel;
        private Button restorePill;
        private GameObject mainRow;

        public bool Minimized => minimized;
        public bool IsShown => content != null && content.activeSelf && !minimized;

        private void Awake()
        {
            dockPos = transform.localPosition;
            dockRot = transform.localRotation;
            baseScale = transform.localScale;
        }

        private void Start()
        {
            BuildBar();
            var st = AppSettings.Current.Window(id);
            preset = (Preset)Mathf.Clamp(st.preset, 0, (int)Preset.Free);
            sizeFactor = st.scale <= 0f ? 1f : st.scale;
            minimized = st.minimized && !closable;
            ApplyPose(true);
            ApplyMinimized(true);
            AppSettings.Changed += OnSettings;
            if (grabHandle != null)
            {
                var ri = grabHandle.GetComponent<RayInteractable>();
                if (ri != null) ri.WhenPointerEventRaised += OnGrab;
            }
            resizable = GetComponent<IResizableWindow>();
            if (resizable != null) BuildCorner();
        }

        private void OnDestroy() => AppSettings.Changed -= OnSettings;

        private float lastUiScale = -1f;
        private void OnSettings()
        {
            float s = AppSettings.Current.UiScale;
            if (Mathf.Approximately(s, lastUiScale)) return;
            lastUiScale = s;
            ApplyPose(false);
        }

        // ── Bar ───────────────────────────────────────────────────────────────

        private void BuildBar()
        {
            Transform t = bar.transform;
            mainRow = UiKit.Rect("Row", t, Vector2.zero, new Vector2(420f, 60f)).gameObject;
            Transform r = mainRow.transform;
            UiKit.Box(r, Vector2.zero, new Vector2(420f, 56f), new Color(0.06f, 0.065f, 0.08f, 0.92f), 2.2f, "BarBack");

            Round(r, "Minus", new Vector2(-176f, 0f), ToggleMinimize);
            var size = UiKit.Button(r, "M", new Vector2(-122f, 0f), new Vector2(44f, 44f), UiKit.Control, CycleSize, out sizeLabel, 16f);
            UiKit.Box(r, new Vector2(0f, 0f), new Vector2(120f, 12f), new Color(1f, 1f, 1f, 0.75f), 6f, "GrabPill");
            Round(r, "category-all", new Vector2(122f, 0f), ToggleSnapRow);
            if (closable) RoundX(r, new Vector2(176f, 0f), Close);
            else Round(r, "recently-played", new Vector2(176f, 0f), () => SetPreset(Preset.Desk));

            // Snap choices pop up above the bar.
            snapRow = UiKit.Rect("SnapRow", t, new Vector2(0f, 62f), new Vector2(420f, 50f)).gameObject;
            UiKit.Box(snapRow.transform, Vector2.zero, new Vector2(420f, 50f), new Color(0.06f, 0.065f, 0.08f, 0.95f), 2.2f, "SnapBack");
            string[] names = { "Desk", "Left", "Right", "Theater" };
            for (int i = 0; i < names.Length; i++)
            {
                var p = (Preset)i;
                UiKit.Button(snapRow.transform, names[i], new Vector2(-153f + i * 102f, 0f), new Vector2(96f, 38f), UiKit.Control,
                    () => { SetPreset(p); snapRow.SetActive(false); }, out _, 15f);
            }
            snapRow.SetActive(false);

            // Minimized: a single restore pill.
            restorePill = UiKit.Button(t, title, Vector2.zero, new Vector2(220f, 48f), UiKit.Raised, ToggleMinimize, out _, 16f, false, "Plus");
            restorePill.gameObject.SetActive(false);
            RefreshSizeLabel();
        }

        private void Round(Transform parent, string icon, Vector2 pos, System.Action click)
        {
            var b = UiKit.Button(parent, "", pos, new Vector2(44f, 44f), UiKit.Control, click, out _, 14f);
            UiKit.Icon(b.transform, icon, Vector2.zero, 22f, Color.white);
        }

        private void RoundX(Transform parent, Vector2 pos, System.Action click)
        {
            var b = UiKit.Button(parent, "", pos, new Vector2(44f, 44f), new Color(0.32f, 0.15f, 0.17f), click, out _, 14f);
            var x = UiKit.Icon(b.transform, "Plus", Vector2.zero, 22f, Color.white);
            x.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        private void RefreshSizeLabel()
        {
            if (sizeLabel != null) sizeLabel.text = sizeFactor < 0.95f ? "S" : sizeFactor > 1.05f ? "L" : "M";
        }

        // ── Actions ───────────────────────────────────────────────────────────

        public void Show()
        {
            if (content != null) content.SetActive(true);
            bar.gameObject.SetActive(true);
            if (grabHandle != null) grabHandle.gameObject.SetActive(!minimized);
            if (corner != null) corner.gameObject.SetActive(!minimized);
            if (minimized) { minimized = false; ApplyMinimized(false); }
        }

        public void Close()
        {
            var settings = GetComponent<SettingsWindow>();
            if (settings != null) { settings.Close(); return; }   // it hides the bar too
            if (content != null) content.SetActive(false);
            HideBar();
        }

        public void HideBar()
        {
            if (bar != null) bar.gameObject.SetActive(false);
            if (corner != null) corner.gameObject.SetActive(false);
            if (grabHandle != null) grabHandle.gameObject.SetActive(false);
        }

        public void ToggleMinimize()
        {
            minimized = !minimized;
            ApplyMinimized(false);
            Save();
        }

        private void ToggleSnapRow() => snapRow.SetActive(!snapRow.activeSelf);

        private void CycleSize()
        {
            sizeFactor = sizeFactor < 0.95f ? 1f : sizeFactor < 1.05f ? 1.25f : 0.85f;
            RefreshSizeLabel();
            ApplyPose(false);
            Save();
        }

        /// <summary>Back on the desk at standard size (Settings > Comfort & View > Reset windows).</summary>
        public void ResetLayout()
        {
            sizeFactor = 1f;
            RefreshSizeLabel();
            SetPreset(Preset.Desk);
        }

        public void SetPreset(Preset p)
        {
            preset = p;
            if (minimized) { minimized = false; ApplyMinimized(false); }
            ApplyPose(false);
            Save();
        }

        private void Save()
        {
            var st = AppSettings.Current.Window(id);
            st.preset = (int)preset;
            st.scale = sizeFactor;
            st.minimized = minimized;
            st.position = transform.localPosition;
            st.yaw = transform.localEulerAngles.y;
            st.pitch = transform.localEulerAngles.x;
            AppSettings.Save();
        }

        // ── Placement ─────────────────────────────────────────────────────────

        private void PoseFor(Preset p, out Vector3 pos, out Quaternion rot, out float scale)
        {
            float deskTop = dockPos.y;   // presets are relative to where the window docks
            switch (p)
            {
                case Preset.Left:
                    pos = new Vector3(-1.3f, deskTop + 0.45f, 1.5f); rot = Quaternion.Euler(4f, -42f, 0f); scale = 1.5f; break;
                case Preset.Right:
                    pos = new Vector3(1.3f, deskTop + 0.45f, 1.5f); rot = Quaternion.Euler(4f, 42f, 0f); scale = 1.5f; break;
                case Preset.Theater:
                    pos = new Vector3(0f, deskTop + 1.45f, 3.2f); rot = Quaternion.Euler(-6f, 0f, 0f); scale = 2.8f; break;
                case Preset.Free:
                    var st = AppSettings.Current.Window(id);
                    pos = st.position; rot = Quaternion.Euler(st.pitch, st.yaw, 0f); scale = 1f; break;
                default:
                    pos = dockPos; rot = dockRot; scale = 1f; break;
            }
        }

        private void ApplyPose(bool instant)
        {
            PoseFor(preset, out var pos, out var rot, out float scale);
            Vector3 targetScale = baseScale * scale * sizeFactor * AppSettings.Current.UiScale;
            if (anim != null) StopCoroutine(anim);
            if (instant) { transform.localPosition = pos; transform.localRotation = rot; transform.localScale = targetScale; }
            else anim = StartCoroutine(Move(pos, rot, targetScale, 0.35f));
        }

        private void ApplyMinimized(bool instant)
        {
            if (content == null) return;
            mainRow.SetActive(!minimized);
            snapRow.SetActive(false);
            if (grabHandle != null) grabHandle.gameObject.SetActive(!minimized);
            if (corner != null) corner.gameObject.SetActive(!minimized);
            restorePill.gameObject.SetActive(minimized);
            if (minimized)
            {
                if (slideIntoDesk && preset == Preset.Desk && !instant) StartCoroutine(SlideContent(true));
                else content.SetActive(false);
            }
            else
            {
                content.SetActive(true);
                if (slideIntoDesk && preset == Preset.Desk && !instant) StartCoroutine(SlideContent(false));
                else content.transform.localPosition = contentHome;
            }
        }

        private Vector3 contentHome;
        private bool homeKnown;

        /// <summary>The monitor sinks into the desk (or rises out of it), like a pop-up display.</summary>
        private IEnumerator SlideContent(bool down)
        {
            Transform c = content.transform;
            if (!homeKnown) { contentHome = c.localPosition; homeKnown = true; }
            Vector3 low = contentHome + Vector3.down * 0.62f / Mathf.Max(0.0001f, transform.localScale.y);
            Vector3 from = down ? contentHome : low, to = down ? low : contentHome;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.45f;
                c.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            c.localPosition = to;
            if (down) content.SetActive(false);
        }

        private IEnumerator Move(Vector3 pos, Quaternion rot, Vector3 scale, float seconds)
        {
            Vector3 p0 = transform.localPosition, s0 = transform.localScale;
            Quaternion r0 = transform.localRotation;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / seconds;
                float k = Mathf.SmoothStep(0f, 1f, t);
                transform.localPosition = Vector3.Lerp(p0, pos, k);
                transform.localRotation = Quaternion.Slerp(r0, rot, k);
                transform.localScale = Vector3.Lerp(s0, scale, k);
                yield return null;
            }
            anim = null;
        }

        // ── Corner resize (free width and height, reflowing layout) ──────────

        private IResizableWindow resizable;
        private readonly System.Collections.Generic.List<Transform> corners = new System.Collections.Generic.List<Transform>();
        private readonly System.Collections.Generic.List<Renderer> cornerRenderers = new System.Collections.Generic.List<Renderer>();
        private Transform corner;   // parent of both handles (visibility toggles use it)
        private RayInteractor sizer;
        private Vector2 pendingSize;
        private RectTransform preview;
        private bool hovered;
        private const float CanvasUnit = 0.001f;
        private const float CornerGap = 0.022f;

        /// <summary>Places the bar under the window and the two resize brackets at its bottom corners (canvas units).</summary>
        public void LayoutForSize(Vector2 size)
        {
            float halfH = size.y * 0.5f * CanvasUnit;
            barOffsetY = -(halfH + 0.045f);
            if (bar != null) bar.transform.localPosition = new Vector3(0f, barOffsetY, bar.transform.localPosition.z);
            PlaceCorners(size);
        }

        private void PlaceCorners(Vector2 size)
        {
            float x = size.x * 0.5f * CanvasUnit + CornerGap, y = -size.y * 0.5f * CanvasUnit - CornerGap;
            for (int i = 0; i < corners.Count; i++)
                corners[i].localPosition = new Vector3(i == 0 ? x : -x, y, -0.015f);
        }

        /// <summary>Two L-shaped brackets (bottom-right, bottom-left), like Horizon OS window corners. Pinch and drag to resize.</summary>
        private void BuildCorner()
        {
            var group = new GameObject("ResizeCorners").transform;
            group.SetParent(transform, false);
            corner = group;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? 1f : -1f;
                var handle = new GameObject(side == 0 ? "CornerRight" : "CornerLeft");
                handle.SetActive(false);
                handle.transform.SetParent(group, false);
                var col = handle.AddComponent<BoxCollider>();
                col.size = new Vector3(0.1f, 0.1f, 0.03f);
                col.center = new Vector3(-sx * 0.02f, 0.02f, 0f);
                var pieces = new[]
                {
                    (new Vector3(-sx * 0.03f, 0f, 0f), new Vector3(0.068f, 0.009f, 0.006f)),
                    (new Vector3(0f, 0.03f, 0f), new Vector3(0.009f, 0.068f, 0.006f))
                };
                foreach (var (pos, scale) in pieces)
                {
                    var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(piece.GetComponent<Collider>());
                    piece.transform.SetParent(handle.transform, false);
                    piece.transform.localPosition = pos;
                    piece.transform.localScale = scale;
                    var r = piece.GetComponent<Renderer>();
                    if (shader != null) r.material = new Material(shader);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    cornerRenderers.Add(r);
                }
                var surface = handle.AddComponent<Oculus.Interaction.Surfaces.ColliderSurface>();
                surface.InjectAllColliderSurface(col);
                var ri = handle.AddComponent<RayInteractable>();
                ri.InjectAllRayInteractable(surface);
                handle.SetActive(true);
                ri.WhenPointerEventRaised += OnCorner;
                corners.Add(handle.transform);
            }
            SetCornerColor(false);
            LayoutForSize(resizable.Size);
            group.gameObject.SetActive(bar != null && bar.gameObject.activeSelf && !minimized);
        }

        private void SetCornerColor(bool active)
        {
            Color c = active ? new Color(0.31f, 0.82f, 0.77f) : new Color(0.92f, 0.94f, 0.96f);
            foreach (var r in cornerRenderers) if (r != null) r.material.SetColor("_BaseColor", c);
            foreach (var t in corners) t.localScale = Vector3.one * (active ? 1.15f : 1f);
        }

        private void OnCorner(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Hover: hovered = true; SetCornerColor(true); break;
                case PointerEventType.Unhover: hovered = false; if (sizer == null) SetCornerColor(false); break;
                case PointerEventType.Select:
                    sizer = null;
                    foreach (var r in FindObjectsByType<RayInteractor>(FindObjectsInactive.Exclude))
                        if (r.Identifier == evt.Identifier) { sizer = r; break; }
                    pendingSize = resizable.Size;
                    if (sizer != null && content != null && !resizable.Live)
                    {
                        preview = UiKit.Box(content.transform, Vector2.zero, pendingSize, new Color(0.31f, 0.82f, 0.77f, 0.14f), 1.6f, "ResizePreview").rectTransform;
                        preview.SetAsLastSibling();
                    }
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    if (sizer != null)
                    {
                        if (preview != null) Destroy(preview.gameObject);
                        preview = null;
                        resizable.Resize(pendingSize);
                    }
                    sizer = null;
                    if (!hovered) SetCornerColor(false);
                    break;
            }
        }

        private void UpdateResize()
        {
            if (sizer == null) return;
            Ray ray = sizer.Ray;
            var plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out float d)) return;
            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(d)) / CanvasUnit;   // canvas units, window centre = 0
            float gap = CornerGap / CanvasUnit;
            Vector2 s = new Vector2((Mathf.Abs(local.x) - gap) * 2f, (Mathf.Abs(local.y) - gap) * 2f);
            pendingSize = new Vector2(Mathf.Clamp(s.x, resizable.MinSize.x, resizable.MaxSize.x),
                                      Mathf.Clamp(s.y, resizable.MinSize.y, resizable.MaxSize.y));
            if (resizable.Live) resizable.ResizeLive(pendingSize);   // monitors reflow as you drag
            if (preview != null) preview.sizeDelta = pendingSize;
            PlaceCorners(pendingSize);
        }

        // ── Grab to move ──────────────────────────────────────────────────────

        private RayInteractor grabber;
        private float grabDistance;
        private Vector3 grabOffset;

        private void OnGrab(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    grabber = null;
                    foreach (var r in FindObjectsByType<RayInteractor>(FindObjectsInactive.Exclude))
                        if (r.Identifier == evt.Identifier) { grabber = r; break; }
                    if (grabber == null) return;
                    if (anim != null) { StopCoroutine(anim); anim = null; }
                    Ray ray = grabber.Ray;
                    grabDistance = Vector3.Distance(ray.origin, grabHandle.position);
                    grabOffset = transform.position - (ray.origin + ray.direction * grabDistance);
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    if (grabber != null) { preset = Preset.Free; Save(); }
                    grabber = null;
                    break;
            }
        }

        // ── Direct grab: pinch the window's frame or its bar with your hand ──────

        private Oculus.Interaction.Input.Hand handHolder;
        private Vector3 handOffset;

        private void UpdateHandGrab()
        {
            if (content == null || !content.activeSelf || minimized) { handHolder = null; return; }
            var hands = HandProbe.Hands;
            if (handHolder == null)
            {
                Vector2 size = resizable != null ? resizable.Size : ((RectTransform)content.transform).sizeDelta;
                float halfW = size.x * 0.5f * CanvasUnit, halfH = size.y * 0.5f * CanvasUnit;
                foreach (var h in hands)
                {
                    if (!h.tracked || !h.pinchStarted) continue;
                    Vector3 local = transform.InverseTransformPoint(h.pinchPoint);
                    local = Vector3.Scale(local, transform.lossyScale);   // back to metres
                    bool nearPlane = Mathf.Abs(local.z) < 0.07f;
                    bool inBand = Mathf.Abs(local.x) < halfW + 0.07f && Mathf.Abs(local.y) < halfH + 0.07f &&
                                  (Mathf.Abs(local.x) > halfW - 0.015f || Mathf.Abs(local.y) > halfH - 0.015f);   // the frame edge
                    bool onBar = grabHandle != null && Vector3.Distance(h.pinchPoint, grabHandle.position) < 0.09f;
                    if (nearPlane && (inBand || onBar))
                    {
                        handHolder = h.hand;
                        handOffset = transform.position - h.pinchPoint;
                        if (anim != null) { StopCoroutine(anim); anim = null; }
                        break;
                    }
                }
                return;
            }
            foreach (var h in hands)
            {
                if (h.hand != handHolder) continue;
                if (!h.pinching || !h.tracked) { handHolder = null; preset = Preset.Free; Save(); return; }
                transform.position = Vector3.Lerp(transform.position, h.pinchPoint + handOffset, 1f - Mathf.Exp(-Time.deltaTime * 20f));
                Transform head = Camera.main != null ? Camera.main.transform : null;
                if (head != null)
                {
                    Vector3 look = transform.position - head.position;
                    if (look.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look.normalized, Vector3.up), 1f - Mathf.Exp(-Time.deltaTime * 12f));
                }
                return;
            }
            handHolder = null;
        }

        private void Update()
        {
            UpdateResize();
            if (sizer == null && grabber == null) UpdateHandGrab();
            if (grabber == null) return;
            Ray ray = grabber.Ray;
            Vector3 goal = ray.origin + ray.direction * grabDistance + grabOffset;
            float k = 1f - Mathf.Exp(-Time.deltaTime * 16f);
            transform.position = Vector3.Lerp(transform.position, goal, k);
            Transform head = Camera.main != null ? Camera.main.transform : null;
            if (head != null)
            {
                Vector3 look = transform.position - head.position;
                if (look.sqrMagnitude > 0.01f)
                {
                    Quaternion face = Quaternion.LookRotation(look.normalized, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, face, k);
                }
            }
        }
    }
}
