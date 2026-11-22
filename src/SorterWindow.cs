using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSP.UI;
using KSP.UI.Screens;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PhilsToolbarSorter
{
    // The window: the toolbar's buttons as rows, in the bar's order, KSP's divider
    // between the two sides. Drag a row (anywhere but its eye) to move its button
    // within its side; click the eye to hide or show it. Pointing at a row lights up
    // its button on the bar. Built from plain Unity UI pieces in the look Phil's mods
    // share (see Look and Ui).
    public class SorterWindow
    {
        const float W = 386, TitleH = 46, SubH = 36, RowH = 36, SepH = 18, Pad = 6, ScrollW = 10, MaxListH = 560;
        const float RowW = W - 2 - ScrollW - 2 * Pad;
        const string LockId = "PhilsToolbarSorterWindow";

        // While the mouse is over the window: no clicking things behind it, and the
        // wheel scrolls the list, not the camera.
        const ControlTypes WindowLock = ControlTypes.EDITOR_PAD_PICK_PLACE | ControlTypes.EDITOR_PAD_PICK_COPY |
            ControlTypes.EDITOR_GIZMO_TOOLS | ControlTypes.EDITOR_ROOT_REFLOW | ControlTypes.CAMERACONTROLS | ControlTypes.KSC_FACILITIES;

        class RowView
        {
            public ToolbarSorter.Row Row;
            public RectTransform Rt;
            public Image Bg;
            public RawImage Grip, PicRaw;
            public Image PicSprite;
            public Text Name;
            public Btn Eye;
            public HoverRelay Hover;
            public float Y;
        }

        readonly ToolbarSorter sorter;
        RectTransform root, listArea, viewport, content, ghost, dropLine, ring;
        Text pillText, eyebrow, emptyText;
        Btn close;
        DragMove move;
        readonly List<RowView> views = new List<RowView>();
        string shownKey;
        bool upright, uiHidden, locked;
        float winH;
        Sprite rowHover;

        // A row being dragged, and where it would land (null: at the end of its side).
        RowView dragging;
        RowView dropBefore;
        bool dropAtEnd;

        public bool IsOpen => root != null;

        public SorterWindow(ToolbarSorter sorter) { this.sorter = sorter; }

        public void SetUiHidden(bool hidden)
        {
            uiHidden = hidden;
            if (root != null) root.gameObject.SetActive(!hidden);
            if (hidden) Light(null);
        }

        // ---- Opening and closing ----------------------------------------------------------

        public void Open()
        {
            if (root != null || UIMasterController.Instance == null) return;
            try
            {
                Build();
                shownKey = null;
                LateUpdate();
                Debug.Log($"[PhilsToolbarSorter] Window open in {SceneName()}: {views.Count} rows.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PhilsToolbarSorter] Couldn't open the window: {e}");
                Close();
            }
        }

        public void Close()
        {
            EndDragNow(apply: false);
            Light(null);
            if (ring != null) UnityEngine.Object.Destroy(ring.gameObject);
            ring = null;
            SetLock(false);
            if (root != null)
            {
                sorter.Setup.SetWindowPosition(root.anchoredPosition);
                UnityEngine.Object.Destroy(root.gameObject);
            }
            root = null;
            views.Clear();
            shownKey = null;
        }

        void Build()
        {
            Look.Init();
            rowHover = Look.Round(Look.Panel3, Look.Panel3, 7);
            Canvas canvas = UIMasterController.Instance.dialogCanvas;
            root = Ui.Node(canvas.transform, "PhilsToolbarSorter");
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            winH = TitleH + SubH + 200;
            root.sizeDelta = new Vector2(W, winH);
            Ui.Box(root, Look.Round(Look.Panel, Look.Line, 10), raycast: true);

            // Title bar: drags the window.
            Ui.Slab(root, "TitleBar", 1, 1, W - 2, TitleH - 1, Look.Panel2, 9, Ui.Corners.Top);
            Ui.Solid(Ui.Node(root, "TitleLine").At(1, TitleH - 1, W - 2, 1), Look.Line);
            RectTransform handle = Ui.Node(root, "Drag").At(0, 0, W, TitleH);
            Ui.Solid(handle, Color.clear, raycast: true);
            move = handle.gameObject.AddComponent<DragMove>();
            move.Target = root;
            Ui.Picture(Ui.Node(root, "Logo").At(13, 11, 24, 24), Look.Logo, Color.white);
            Ui.Label(Ui.Node(root, "Title").At(45, 0, 230, TitleH),
                $"<color={Look.Html(Look.Ink3)}>Phil's</color> <b>Toolbar Sorter</b>", Look.Display, 18, Look.Ink);
            RectTransform pill = Ui.Node(root, "Count").At(W - 48 - 84, 11, 84, 24);
            Ui.Box(pill, Look.Round(Look.Panel3, Look.Panel3, 11));
            pillText = Ui.Label(Ui.Node(pill, "Text").Stretch(), "", Look.Display, 14, Look.Ink2, TextAnchor.MiddleCenter);
            close = Btn.Make(root, "Close", W - 38, 8, 30, 30, () => sorter.CloseFromWindow())
                .Skin(null, Look.Round(Look.Panel3, Look.Panel3, 6)).Colors(Look.Ink2, Look.Ink, Look.Ink).WithGlyph(Look.Cross, 12);

            // Which scene, and the two things you can do.
            eyebrow = Ui.Label(Ui.Node(root, "Scene").At(14, TitleH, W - 28 - 170, SubH), "", Look.Body, 11, Look.Ink3);
            Ui.Label(Ui.Node(root, "Hint").At(W - 14 - 170, TitleH, 170, SubH), "Drag to move · eye to hide", Look.Body, 12, Look.Ink3, TextAnchor.MiddleRight);
            Ui.Solid(Ui.Node(root, "SubLine").At(1, TitleH + SubH - 1, W - 2, 1), Look.Line);

            BuildList();
            root.anchoredPosition = Clamped(sorter.Setup.WindowPosition ?? DefaultPosition());
            root.SetAsLastSibling();
            if (uiHidden) root.gameObject.SetActive(false);
        }

        void BuildList()
        {
            listArea = Ui.Node(root, "List").At(1, TitleH + SubH, W - 2, 100);
            ScrollRect scroll = listArea.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 30;

            viewport = Ui.Node(listArea, "Viewport").Stretch(0, 0, ScrollW, 0);
            Ui.Solid(viewport, Color.clear, raycast: true);    // so the wheel scrolls over gaps too
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Ui.Node(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            RectTransform bar = Ui.Node(listArea, "Scrollbar");
            bar.anchorMin = new Vector2(1, 0);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(1, 0.5f);
            bar.offsetMin = new Vector2(-8, 8);
            bar.offsetMax = new Vector2(-2, -8);
            Ui.Box(bar, Look.Round(Look.Panel2, Look.Panel2, 2), raycast: true);
            RectTransform knob = Ui.Node(bar, "Handle");
            knob.offsetMin = knob.offsetMax = Vector2.zero;
            Image knobImg = Ui.Box(knob, Look.Round(Look.Line2, Look.Line2, 2), raycast: true);
            Scrollbar sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.transition = Selectable.Transition.None;
            sb.targetGraphic = knobImg;
            sb.handleRect = knob;
            sb.direction = Scrollbar.Direction.BottomToTop;
            var nav = sb.navigation;
            nav.mode = Navigation.Mode.None;
            sb.navigation = nav;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            emptyText = Ui.Label(Ui.Node(listArea, "Empty").Stretch(20, 20, 20, 20), "", Look.Body, 14, Look.Ink3, TextAnchor.MiddleCenter);
        }

        // Near the right-hand edge, where the toolbar is.
        Vector2 DefaultPosition()
        {
            Rect area = ((RectTransform)root.parent).rect;
            return new Vector2(area.width / 2 - W / 2 - 70, 30);
        }

        // Keeps the window on screen.
        Vector2 Clamped(Vector2 p)
        {
            Rect area = ((RectTransform)root.parent).rect;
            float x = Mathf.Max(0, (area.width - W) / 2), y = Mathf.Max(0, (area.height - winH) / 2);
            return new Vector2(Mathf.Clamp(p.x, -x, x), Mathf.Clamp(p.y, -y, y));
        }

        // ---- Every frame -------------------------------------------------------------------

        public void LateUpdate()
        {
            if (root == null) { SetLock(false); return; }
            try
            {
                List<ToolbarSorter.Row> rows = sorter.Rows(out bool nowUpright);
                string key = Key(rows, nowUpright);
                if (key != shownKey && dragging == null)
                {
                    upright = nowUpright;
                    Fill(rows);
                    shownKey = key;
                }
                RowView hovered = null;
                foreach (RowView v in views)
                {
                    if (v.Row.Seen.Button == null) continue;
                    bool hover = v.Hover.Over && dragging == null || v == dragging;
                    if (hover) hovered = v;
                    Sprite bg = hover ? rowHover : null;
                    if (v.Bg.sprite != bg) Ui.SetSprite(v.Bg, bg);
                    v.Grip.color = hover ? Look.Ink2 : Look.Ink3;
                    ShowPicture(v.Row.Seen.Button, v.PicRaw, v.PicSprite, v.Row.Hidden ? 0.3f : 1f);
                    v.Eye.Refresh();
                }
                close.Refresh();
                Light(hovered != null && !hovered.Row.Hidden ? hovered.Row.Seen.Button : null);
                if (dragging != null) Dragging(Input.mousePosition, autoScroll: true);
                if (!move.Dragging) sorter.Setup.SetWindowPosition(root.anchoredPosition);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PhilsToolbarSorter] Window update failed, closing it: {e}");
                sorter.CloseFromWindow();
                return;
            }
            SetLock(MouseOver() || dragging != null || move.Dragging);
        }

        static string Key(List<ToolbarSorter.Row> rows, bool upright)
        {
            var sb = new StringBuilder(SceneName()).Append(upright ? "|U" : "|F");
            foreach (ToolbarSorter.Row r in rows)
                sb.Append('|').Append(r.Seen.Item.GetInstanceID()).Append(r.Seen.StockSide ? 'k' : 'm')
                  .Append(r.Hidden ? 'h' : 's').Append(r.New ? 'n' : '-').Append(r.Seen.Name);
            return sb.ToString();
        }

        // ---- The rows -------------------------------------------------------------------------

        void Fill(List<ToolbarSorter.Row> rows)
        {
            foreach (RowView v in views) if (v.Rt != null) UnityEngine.Object.Destroy(v.Rt.gameObject);
            for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            views.Clear();

            float y = Pad;
            bool? side = null;
            foreach (ToolbarSorter.Row r in rows)
            {
                if (side.HasValue && side.Value != r.Seen.StockSide)
                {
                    RawImage dash = Ui.Picture(Ui.Node(content, "Divider").At(Pad + 8, y + SepH / 2 - 1, RowW - 16, 2), Look.Dash, Color.white);
                    dash.uvRect = new Rect(0, 0, (RowW - 16) / 10f, 1);
                    y += SepH;
                }
                side = r.Seen.StockSide;
                views.Add(MakeRow(r, y));
                y += RowH;
            }
            float contentH = y + Pad;
            content.sizeDelta = new Vector2(0, contentH);
            float listH = Mathf.Min(contentH, MaxListH, ((RectTransform)root.parent).rect.height - TitleH - SubH - 40);
            listH = Mathf.Max(listH, 80);
            listArea.At(1, TitleH + SubH, W - 2, listH);
            winH = TitleH + SubH + listH + 1;
            root.sizeDelta = new Vector2(W, winH);
            root.anchoredPosition = Clamped(root.anchoredPosition);

            int hidden = rows.Count(r => r.Hidden);
            pillText.text = $"<color={Look.Html(Look.Ink)}><b>{hidden}</b></color> hidden";
            // Which scene's setup this is: what you change here changes only there.
            eyebrow.text = $"{Setup.DisplayName(Setup.SceneOf(HighLogic.LoadedScene)).ToUpperInvariant()}  ·  {rows.Count} BUTTONS";
            emptyText.text = rows.Count == 0 ? "No toolbar buttons here." : "";
        }

        RowView MakeRow(ToolbarSorter.Row r, float y)
        {
            var v = new RowView { Row = r, Y = y };
            v.Rt = Ui.Node(content, "Row").At(Pad, y, RowW, RowH);
            v.Bg = Ui.Box(v.Rt, null, raycast: true);
            v.Bg.color = Color.clear;
            v.Hover = v.Rt.gameObject.AddComponent<HoverRelay>();
            RowDrag drag = v.Rt.gameObject.AddComponent<RowDrag>();
            drag.Began = p => BeginDrag(v, p);
            drag.Moved = p => Dragging(p, autoScroll: false);
            drag.Ended = () => EndDragNow(apply: true);

            v.Grip = Ui.Picture(Ui.Node(v.Rt, "Grip").At(6, 10, 12, 16), Look.Grip, Look.Ink3);
            RectTransform tile = Ui.Node(v.Rt, "Tile").At(24, 3, 31, 30);
            Ui.Box(tile, Look.Round(Look.KspButton, Look.KspButton, 4));
            v.PicRaw = Ui.Picture(Ui.Node(tile, "Picture").Stretch(), null, Color.white);
            v.PicSprite = Ui.Node(tile, "Sprite").Stretch().gameObject.AddComponent<Image>();    // for a button drawn from a sprite
            v.PicSprite.preserveAspect = true;
            v.PicSprite.raycastTarget = false;
            v.PicSprite.enabled = false;

            // One line; a name too long for it is cut off at the end of a word.
            float nameW = RowW - 64 - 40 - (r.New ? 46 : 0);
            v.Name = Ui.Label(Ui.Node(v.Rt, "Name").At(64, 0, nameW, RowH), r.Seen.Name, Look.Body, 14, r.Hidden ? Look.Ink3 : Look.Ink);
            if (r.New)
            {
                RectTransform tag = Ui.Node(v.Rt, "New").At(64 + nameW + 4, 10, 40, 17);
                Ui.Box(tag, Look.Round(Color.clear, Look.AmberLine, 4));
                Ui.Label(Ui.Node(tag, "Text").Stretch(), "NEW", Look.Display, 11, Look.Amber, TextAnchor.MiddleCenter);
            }

            ToolbarSorter.Seen seen = r.Seen;
            bool hide = !r.Hidden;
            v.Eye = Btn.Make(v.Rt, "Eye", RowW - 34, 3, 30, 30, () => { sorter.SetHidden(seen, hide); shownKey = null; })
                .Skin(null, Look.Round(Look.Panel, Look.Panel, 6)).Colors(Look.Ink2, Look.Amber, Look.Amber)
                .WithGlyph(r.Hidden ? Look.EyeSlash : Look.Eye, 18);
            v.Eye.On = r.Hidden;
            v.Eye.Enabled = !r.Own;        // this window's own button always stays on the bar
            return v;
        }

        // A button's picture as the bar shows it now: its texture, or for an animated
        // button (KSP's own apps) what its animation shows. KSP makes the plain picture
        // see-through and puts the animation on top of it (ApplicationLauncherButton.SetSprite).
        static void ShowPicture(ApplicationLauncherButton b, RawImage raw, Image sprite, float alpha)
        {
            if (b.spriteAnim != null && (b.sprite == null || b.sprite.color.a < 0.01f))
            {
                foreach (Graphic g in b.spriteAnim.GetComponentsInChildren<Graphic>(true))
                {
                    if (!g.enabled) continue;
                    if (g is Image img && img.sprite != null)
                    {
                        sprite.sprite = img.sprite;
                        sprite.color = new Color(1, 1, 1, alpha);
                        sprite.enabled = true;
                        raw.enabled = false;
                        return;
                    }
                    if (g is RawImage ri && ri.texture != null)
                    {
                        raw.texture = ri.texture;
                        raw.uvRect = ri.uvRect;
                        raw.color = new Color(1, 1, 1, alpha);
                        raw.enabled = true;
                        sprite.enabled = false;
                        return;
                    }
                }
            }
            RawImage src = b.sprite;
            raw.texture = src != null ? src.texture : null;
            if (src != null) raw.uvRect = src.uvRect;
            raw.color = new Color(1, 1, 1, alpha);
            raw.enabled = raw.texture != null;
            sprite.enabled = false;
        }

        // ---- Dragging a row -----------------------------------------------------------------------

        void BeginDrag(RowView v, Vector2 pointer)
        {
            if (root == null || v == null) return;
            dragging = v;
            dropBefore = null;
            dropAtEnd = false;
            // The row being moved floats with the pointer; a line shows where it will land.
            ghost = Ui.Node(root, "Moving").At(Pad, 0, RowW, RowH);
            Ui.Box(ghost, Look.Round(Look.Panel3, Look.AmberLine, 7));
            Ui.Picture(Ui.Node(ghost, "Grip").At(6, 10, 12, 16), Look.Grip, Look.Ink2);
            RectTransform tile = Ui.Node(ghost, "Tile").At(24, 3, 31, 30);
            Ui.Box(tile, Look.Round(Look.KspButton, Look.KspButton, 4));
            RawImage pr = Ui.Picture(Ui.Node(tile, "Picture").Stretch(), v.PicRaw.texture, Color.white);
            pr.uvRect = v.PicRaw.uvRect;
            pr.enabled = v.PicRaw.enabled;
            if (v.PicSprite.enabled)
            {
                Image ps = Ui.Node(tile, "Sprite").Stretch().gameObject.AddComponent<Image>();
                ps.sprite = v.PicSprite.sprite;
                ps.preserveAspect = true;
                ps.raycastTarget = false;
            }
            Ui.Label(Ui.Node(ghost, "Name").At(64, 0, RowW - 110, RowH), v.Row.Seen.Name, Look.Body, 14, Look.Ink);
            CanvasGroup cg = ghost.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            ghost.SetAsLastSibling();
            dropLine = Ui.Node(content, "DropLine").At(Pad + 4, 0, RowW - 8, 2);
            Ui.Solid(dropLine, Look.Amber);
            foreach (Graphic g in v.Rt.GetComponentsInChildren<Graphic>()) g.CrossFadeAlpha(0.35f, 0, true);
            Dragging(pointer, autoScroll: false);
        }

        void Dragging(Vector2 pointer, bool autoScroll)
        {
            if (dragging == null || root == null) return;
            Camera cam = UiCamera();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, pointer, cam, out Vector2 p))
                ghost.At(Pad, Mathf.Clamp(winH / 2 - p.y - RowH / 2, TitleH, winH - RowH), RowW, RowH);

            // Keep scrolling while the pointer rests near the top or bottom of the list.
            if (autoScroll && RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, pointer, cam, out Vector2 vp))
            {
                Rect r = viewport.rect;
                float step = 0;
                if (vp.y > r.yMax - 24) step = -1;
                else if (vp.y < r.yMin + 24) step = 1;
                if (step != 0)
                {
                    float max = Mathf.Max(0, content.sizeDelta.y - r.height);
                    content.anchoredPosition = new Vector2(0, Mathf.Clamp(content.anchoredPosition.y + step * 600 * Time.unscaledDeltaTime, 0, max));
                }
            }

            // Where it lands: before the first row on its side whose middle is below the pointer.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(content, pointer, cam, out Vector2 c)) return;
            float py = -c.y;
            List<RowView> side = views.Where(v => v.Row.Seen.StockSide == dragging.Row.Seen.StockSide).ToList();
            dropBefore = side.FirstOrDefault(v => py < v.Y + RowH / 2);
            dropAtEnd = dropBefore == null;
            float lineY = dropBefore != null ? dropBefore.Y - 1 : side.Last().Y + RowH - 1;
            dropLine.At(Pad + 4, lineY, RowW - 8, 2);
        }

        void EndDragNow(bool apply)
        {
            if (dragging == null) return;
            RowView moved = dragging;
            dragging = null;
            if (ghost != null) UnityEngine.Object.Destroy(ghost.gameObject);
            if (dropLine != null) UnityEngine.Object.Destroy(dropLine.gameObject);
            ghost = dropLine = null;
            if (apply && root != null && (dropBefore != null || dropAtEnd) && dropBefore != moved)
            {
                List<ToolbarSorter.Seen> order = views.Where(v => v.Row.Seen.StockSide == moved.Row.Seen.StockSide && v != moved)
                                                      .Select(v => v.Row.Seen).ToList();
                int at = dropBefore != null ? order.IndexOf(dropBefore.Row.Seen) : order.Count;
                order.Insert(at < 0 ? order.Count : at, moved.Row.Seen);
                sorter.Reorder(order, upright);
                sorter.MarkSeen(moved.Row.Seen);
            }
            shownKey = null;    // redraw the rows, in the new order
        }

        // ---- Lighting up a button on the bar ----------------------------------------------------

        void Light(ApplicationLauncherButton b)
        {
            if (b == null || !b.gameObject.activeInHierarchy || uiHidden)
            {
                if (ring != null) ring.gameObject.SetActive(false);
                return;
            }
            if (ring == null)
            {
                var go = new GameObject("PhilsToolbarSorterRing", typeof(RectTransform));
                ring = (RectTransform)go.transform;
                Image img = go.AddComponent<Image>();
                img.sprite = Look.Round(Look.Fade(Look.Amber, 0.15f), Look.Amber, 6);
                img.type = Image.Type.Sliced;
                img.raycastTarget = false;
                go.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            if (ring.parent != b.transform)
            {
                ring.gameObject.layer = b.gameObject.layer;
                ring.SetParent(b.transform, false);
                ring.Stretch(-2, -2, -2, -2);
            }
            ring.SetAsLastSibling();
            ring.gameObject.SetActive(true);
        }

        // ---- Small things ---------------------------------------------------------------------------

        public static string SceneName()
        {
            switch (HighLogic.LoadedScene)
            {
                case GameScenes.SPACECENTER: return "Space Center";
                case GameScenes.EDITOR: return EditorDriver.editorFacility == EditorFacility.SPH ? "SPH" : "VAB";
                case GameScenes.FLIGHT: return MapView.MapIsEnabled ? "Map view" : "Flight";
                case GameScenes.TRACKSTATION: return "Tracking Station";
                default: return HighLogic.LoadedScene.ToString();
            }
        }

        Camera UiCamera()
        {
            Canvas canvas = root != null ? root.GetComponentInParent<Canvas>() : null;
            return canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        }

        public bool MouseOver() =>
            root != null && root.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(root, Input.mousePosition, UiCamera());

        void SetLock(bool want)
        {
            if (want == locked) return;
            if (want) InputLockManager.SetControlLock(WindowLock, LockId);
            else InputLockManager.RemoveControlLock(LockId);
            locked = want;
        }

        // ---- For the in-game check ------------------------------------------------------------------

        // The rows as shown: name, hidden or not, and which side of the divider.
        public List<string> ShownRows() =>
            views.Select(v => $"{(v.Row.Seen.StockSide ? "KSP" : "mod")} {(v.Row.Hidden ? "hidden" : "shown ")} {v.Row.Seen.Name}").ToList();
    }

    // Passes a row's drag to the window. (A row is dragged from anywhere but its eye.)
    public class RowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Action<Vector2> Began, Moved;
        internal Action Ended;
        public void OnBeginDrag(PointerEventData e) => Began?.Invoke(e.position);
        public void OnDrag(PointerEventData e) => Moved?.Invoke(e.position);
        public void OnEndDrag(PointerEventData e) => Ended?.Invoke();
    }
}
