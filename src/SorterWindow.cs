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
    // The window: this scene's toolbar buttons as rows, in the bar's order, KSP's divider
    // between the two sides. Drag a row (anywhere but its buttons) to move its button within
    // its side, or onto a folder to put it in; drag it out of a folder to put it back on the
    // bar. The eye hides or shows a button. Folders: "New folder" makes one; the arrow on a
    // folder row shows the buttons in it; the pencil opens its settings (name, color, opens
    // on hover or on click, delete). Pointing at a row lights up its button on the bar.
    // Built from plain Unity UI pieces in the look Phil's mods share (see Look and Ui).
    public class SorterWindow
    {
        const float W = 386, TitleH = 46, SubH = 40, FootH = 28, RowH = 36, SepH = 18, EmptyH = 30, EditH = 118, Pad = 6, ScrollW = 10, MaxListH = 560, Indent = 24;
        const float RowW = W - 2 - ScrollW - 2 * Pad;
        const string LockId = "PhilsToolbarSorterWindow", TypingLockId = "PhilsToolbarSorterTyping";

        // While the mouse is over the window: no clicking things behind it, and the
        // wheel scrolls the list, not the camera.
        const ControlTypes WindowLock = ControlTypes.EDITOR_PAD_PICK_PLACE | ControlTypes.EDITOR_PAD_PICK_COPY |
            ControlTypes.EDITOR_GIZMO_TOOLS | ControlTypes.EDITOR_ROOT_REFLOW | ControlTypes.CAMERACONTROLS | ControlTypes.KSC_FACILITIES;

        enum Kind { Button, Folder, Member, EmptyFolder }

        class RowView
        {
            public ToolbarSorter.Row Row;   // none for an empty folder's hint
            public Kind Kind;
            public Folder Folder;           // the folder this row is, or is in
            public RectTransform Rt;
            public Image Bg;
            public RawImage Grip, PicRaw;
            public Image PicSprite;
            public HoverRelay Hover;
            public float Y, H, BlockEnd;    // BlockEnd: below a folder's open contents
            public bool TopLevel => Kind == Kind.Button || Kind == Kind.Folder;
        }

        // Where a dragged row would land.
        class Target
        {
            public bool IntoFolder;
            public Folder Folder;
            public int Index;
            public RowView Before;          // reorder: before this top-level row (null: at the end of its side)
            public RowView FolderRow;       // lit while a row hovers over it
        }

        readonly ToolbarSorter sorter;
        RectTransform root, listArea, viewport, content, ghost, dropLine, ring;
        Text pillText, eyebrow, emptyText;
        Btn close, newFolder;
        DragMove move;
        readonly List<RowView> views = new List<RowView>();
        readonly List<Btn> buttons = new List<Btn>();
        readonly HashSet<string> expanded = new HashSet<string>();
        string editing;                     // the folder whose settings are open
        bool confirmDelete;
        InputField nameField;
        string shownKey;
        bool upright, uiHidden, locked, typingLocked;
        float winH;
        Sprite rowHover, folderLit;

        RowView dragging;
        Target target;

        public bool IsOpen => root != null;

        public SorterWindow(ToolbarSorter sorter) { this.sorter = sorter; }

        public void SetUiHidden(bool hidden)
        {
            uiHidden = hidden;
            if (root != null) root.gameObject.SetActive(!hidden);
            if (hidden) Light(null);
        }

        // A folder strip opening from the bar closes any folder settings being edited here.
        public void CloseFolderEditing()
        {
            if (editing == null) return;
            CommitName();
            editing = null;
            confirmDelete = false;
            shownKey = null;
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
            CommitName();
            Light(null);
            if (ring != null) UnityEngine.Object.Destroy(ring.gameObject);
            ring = null;
            SetLock(false);
            SetTypingLock(false);
            if (root != null)
            {
                sorter.Setup.SetWindowPosition(root.anchoredPosition);
                UnityEngine.Object.Destroy(root.gameObject);
            }
            root = null;
            views.Clear();
            buttons.Clear();
            nameField = null;
            editing = null;
            confirmDelete = false;
            shownKey = null;
        }

        void Build()
        {
            Look.Init();
            rowHover = Look.Round(Look.Panel3, Look.Panel3, 7);
            folderLit = Look.Round(Look.Wash(Look.Panel), Look.Amber, 7);
            Canvas canvas = UIMasterController.Instance.dialogCanvas;
            root = Ui.Node(canvas.transform, "PhilsToolbarSorter");
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            winH = TitleH + SubH + 200 + FootH;
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

            // Which scene's setup this is, and a new folder.
            eyebrow = Ui.Label(Ui.Node(root, "Scene").At(14, TitleH, W - 28 - 124, SubH), "", Look.Body, 11, Look.Ink3);
            newFolder = Btn.Make(root, "NewFolder", W - 14 - 118, TitleH + 6, 118, 28, MakeFolder)
                .Skin(Look.Round(Look.Panel2, Look.Line2, 7), Look.Round(Look.Panel3, Look.AmberLine, 7))
                .Colors(Look.Ink2, Look.Ink, Look.Ink).WithLabel("New folder", Look.Body, 13);
            newFolder.Label.rectTransform.Stretch(22, 0, 0, 0);
            Ui.Picture(Ui.Node(newFolder.Rt, "Plus").At(12, 9, 10, 10), Look.Plus, Look.Amber);
            Ui.Solid(Ui.Node(root, "SubLine").At(1, TitleH + SubH - 1, W - 2, 1), Look.Line);

            BuildList();

            // What you can do, along the bottom.
            Ui.Solid(Ui.Node(root, "FootLine").At(1, 0, W - 2, 1), Look.Line).rectTransform.name = "FootLine";
            Ui.Label(Ui.Node(root, "Hint").At(14, 0, W - 28, FootH), "Drag to move, or onto a folder  ·  eye to hide", Look.Body, 12, Look.Ink3);

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
            if (root == null) { SetLock(false); SetTypingLock(false); return; }
            try
            {
                List<ToolbarSorter.Row> rows = sorter.Rows(out bool nowUpright);
                string key = Key(rows, nowUpright);
                bool typing = nameField != null && nameField.isFocused;
                if (key != shownKey && dragging == null && !typing)
                {
                    upright = nowUpright;
                    Fill(rows);
                    shownKey = key;
                }
                RowView hovered = null;
                foreach (RowView v in views)
                {
                    if (v.Row != null && v.Row.Seen.Button == null) continue;
                    bool hover = v.Hover != null && v.Hover.Over && dragging == null || v == dragging;
                    if (hover) hovered = v;
                    Sprite bg = target != null && target.FolderRow == v ? folderLit : hover && v.Kind != Kind.EmptyFolder ? rowHover : null;
                    if (v.Bg.sprite != bg) Ui.SetSprite(v.Bg, bg);
                    if (v.Grip != null) v.Grip.color = hover ? Look.Ink2 : Look.Ink3;
                    if (v.Kind == Kind.Button || v.Kind == Kind.Member)
                        ShowPicture(v.Row.Seen.Button, v.PicRaw, v.PicSprite, v.Row.Hidden ? 0.3f : 1f);
                }
                foreach (Btn b in buttons) b.Refresh();
                close.Refresh();
                newFolder.Enabled = sorter.Here != null;
                newFolder.Refresh();
                Light(LitButton(hovered));
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
            SetTypingLock(nameField != null && nameField.isFocused);
        }

        // The bar button a hovered row lights up: its own, or its folder's while it's in one.
        ApplicationLauncherButton LitButton(RowView v)
        {
            if (v == null || v.Row == null) return null;
            if (v.Kind == Kind.Member && !sorter.Folders.IsLent(v.Row.Seen.Item))
                return sorter.Present(v.Folder.Id)?.Button;
            return v.Row.Hidden ? null : v.Row.Seen.Button;
        }

        string Key(List<ToolbarSorter.Row> rows, bool upright)
        {
            var sb = new StringBuilder(SceneName()).Append(upright ? "|U" : "|F").Append('|').Append(editing).Append(confirmDelete ? "!" : "");
            foreach (string e in expanded) sb.Append('+').Append(e);
            foreach (ToolbarSorter.Row r in rows)
            {
                sb.Append('|').Append(r.Seen.Item.GetInstanceID()).Append(r.Seen.StockSide ? 'k' : 'm')
                  .Append(r.Hidden ? 'h' : 's').Append(r.New ? 'n' : '-').Append(r.Seen.Name).Append(r.InFolder?.Id);
                if (r.Folder != null) sb.Append(r.Folder.Name).Append(r.Folder.Color).Append(r.Folder.OpensOnHover).Append(string.Join(",", r.Folder.Members.ToArray()));
            }
            return sb.ToString();
        }

        // ---- The rows -------------------------------------------------------------------------

        void Fill(List<ToolbarSorter.Row> rows)
        {
            for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            views.Clear();
            buttons.Clear();
            nameField = null;

            float y = Pad;
            bool? side = null;
            foreach (ToolbarSorter.Row r in rows.Where(x => x.InFolder == null))
            {
                if (side.HasValue && side.Value != r.Seen.StockSide)
                {
                    RawImage dash = Ui.Picture(Ui.Node(content, "Divider").At(Pad + 8, y + SepH / 2 - 1, RowW - 16, 2), Look.Dash, Color.white);
                    dash.uvRect = new Rect(0, 0, (RowW - 16) / 10f, 1);
                    y += SepH;
                }
                side = r.Seen.StockSide;
                if (r.Folder == null)
                {
                    RowView v = MakeRow(r, Kind.Button, null, y);
                    y += RowH;
                    v.BlockEnd = y;
                    continue;
                }
                Folder f = r.Folder;
                RowView fv = MakeFolderRow(r, y);
                y += RowH;
                if (editing == f.Id) { MakeEditor(f, y); y += EditH; }
                if (expanded.Contains(f.Id))
                {
                    var members = f.Members.Select(id => rows.FirstOrDefault(x => x.InFolder == f && x.Seen.Id == id)).Where(x => x != null).ToList();
                    if (members.Count == 0) { MakeEmptyHint(f, y); y += EmptyH; }
                    foreach (ToolbarSorter.Row m in members) { MakeRow(m, Kind.Member, f, y); y += RowH; }
                }
                fv.BlockEnd = y;
            }
            if (editing != null && !rows.Any(r => r.Folder != null && r.Folder.Id == editing)) editing = null;

            float contentH = y + Pad;
            content.sizeDelta = new Vector2(0, contentH);
            float room = ((RectTransform)root.parent).rect.height - TitleH - SubH - FootH - 40;
            float listH = Mathf.Max(Mathf.Min(contentH, MaxListH, room), 80);
            listArea.At(1, TitleH + SubH, W - 2, listH);
            winH = TitleH + SubH + listH + FootH;
            root.sizeDelta = new Vector2(W, winH);
            root.Find("FootLine")?.GetComponent<RectTransform>()?.At(1, winH - FootH, W - 2, 1);
            root.Find("Hint")?.GetComponent<RectTransform>()?.At(14, winH - FootH, W - 28, FootH);
            root.anchoredPosition = Clamped(root.anchoredPosition);

            int hidden = rows.Count(r => r.Hidden);
            pillText.text = $"<color={Look.Html(Look.Ink)}><b>{hidden}</b></color> hidden";
            // Which scene's setup this is: what you change here changes only there.
            eyebrow.text = $"{Setup.DisplayName(Setup.SceneOf(HighLogic.LoadedScene)).ToUpperInvariant()}  ·  {rows.Count(r => r.Folder == null)} BUTTONS";
            emptyText.text = rows.Count == 0 ? "No toolbar buttons here." : "";
        }

        RowView MakeRow(ToolbarSorter.Row r, Kind kind, Folder folder, float y)
        {
            float x0 = kind == Kind.Member ? Indent : 0;
            var v = Shell(r, kind, folder, y, RowH, x0);

            v.Grip = Ui.Picture(Ui.Node(v.Rt, "Grip").At(6, 10, 12, 16), Look.Grip, Look.Ink3);
            RectTransform tile = Ui.Node(v.Rt, "Tile").At(24, 3, 31, 30);
            Ui.Box(tile, Look.Round(Look.KspButton, Look.KspButton, 4));
            v.PicRaw = Ui.Picture(Ui.Node(tile, "Picture").Stretch(), null, Color.white);
            v.PicSprite = Ui.Node(tile, "Sprite").Stretch().gameObject.AddComponent<Image>();    // for a button drawn from a sprite
            v.PicSprite.preserveAspect = true;
            v.PicSprite.raycastTarget = false;
            v.PicSprite.enabled = false;

            float w = v.Rt.sizeDelta.x;
            float nameW = w - 64 - 40 - (r.New ? 46 : 0);
            Ui.Label(Ui.Node(v.Rt, "Name").At(64, 0, nameW, RowH), r.Seen.Name, Look.Body, 14, r.Hidden ? Look.Ink3 : Look.Ink);
            if (r.New)
            {
                RectTransform tag = Ui.Node(v.Rt, "New").At(64 + nameW + 4, 10, 40, 17);
                Ui.Box(tag, Look.Round(Color.clear, Look.AmberLine, 4));
                Ui.Label(Ui.Node(tag, "Text").Stretch(), "NEW", Look.Display, 11, Look.Amber, TextAnchor.MiddleCenter);
            }

            ToolbarSorter.Seen seen = r.Seen;
            if (kind == Kind.Member)
            {
                // In a folder: the arrow takes it out, back onto the bar.
                buttons.Add(Btn.Make(v.Rt, "Out", w - 34, 3, 30, 30, () => { sorter.TakeOutOfFolder(seen); shownKey = null; })
                    .Skin(null, Look.Round(Look.Panel, Look.Panel, 6)).Colors(Look.Ink3, Look.Amber, Look.Amber).WithGlyph(Look.TakeOut, 15));
            }
            else
            {
                bool hide = !r.Hidden;
                Btn eye = Btn.Make(v.Rt, "Eye", w - 34, 3, 30, 30, () => { sorter.SetHidden(seen, hide); shownKey = null; })
                    .Skin(null, Look.Round(Look.Panel, Look.Panel, 6)).Colors(Look.Ink2, Look.Amber, Look.Amber)
                    .WithGlyph(r.Hidden ? Look.EyeSlash : Look.Eye, 18);
                eye.On = r.Hidden;
                eye.Enabled = !r.Own;        // this window's own button always stays on the bar
                buttons.Add(eye);
            }
            return v;
        }

        RowView MakeFolderRow(ToolbarSorter.Row r, float y)
        {
            Folder f = r.Folder;
            var v = Shell(r, Kind.Folder, f, y, RowH, 0);
            v.Grip = Ui.Picture(Ui.Node(v.Rt, "Grip").At(6, 10, 12, 16), Look.Grip, Look.Ink3);
            RectTransform tile = Ui.Node(v.Rt, "Tile").At(24, 3, 31, 30);
            Ui.Box(tile, Look.Round(Look.KspButton, Look.KspButton, 4));
            Ui.Picture(Ui.Node(tile, "Folder").At(5, 5, 21, 20), Look.FolderGlyph, Look.FolderColor(f.Color));

            bool open = expanded.Contains(f.Id);
            string id = f.Id;
            buttons.Add(Btn.Make(v.Rt, "Expand", 58, 7, 22, 22, () => { if (!expanded.Remove(id)) expanded.Add(id); shownKey = null; })
                .Skin(null, Look.Round(Look.Panel, Look.Panel, 5)).Colors(Look.Ink2, Look.Ink, Look.Ink)
                .WithGlyph(open ? Look.ChevronDown : Look.ChevronRight, 12));
            int count = f.Members.Count(m => sorter.Present(m) != null);
            Ui.Label(Ui.Node(v.Rt, "Name").At(84, 0, RowW - 84 - 72, RowH),
                $"<b>{Escape(f.Name)}</b>  <color={Look.Html(Look.Ink3)}>{count}</color>", Look.Body, 14, r.Hidden ? Look.Ink3 : Look.Ink);

            Btn edit = Btn.Make(v.Rt, "Edit", RowW - 68, 3, 30, 30, () => ToggleEditing(id))
                .Skin(null, Look.Round(Look.Panel, Look.Panel, 6)).Colors(Look.Ink2, Look.Amber, Look.Amber).WithGlyph(Look.Pencil, 15);
            edit.On = editing == id;
            buttons.Add(edit);
            ToolbarSorter.Seen seen = r.Seen;
            bool hide = !r.Hidden;
            Btn eye = Btn.Make(v.Rt, "Eye", RowW - 34, 3, 30, 30, () => { sorter.SetHidden(seen, hide); shownKey = null; })
                .Skin(null, Look.Round(Look.Panel, Look.Panel, 6)).Colors(Look.Ink2, Look.Amber, Look.Amber)
                .WithGlyph(r.Hidden ? Look.EyeSlash : Look.Eye, 18);
            eye.On = r.Hidden;
            buttons.Add(eye);
            return v;
        }

        void MakeEmptyHint(Folder f, float y)
        {
            var v = Shell(null, Kind.EmptyFolder, f, y, EmptyH, Indent);
            Ui.Label(Ui.Node(v.Rt, "Hint").Stretch(14, 0, 10, 0), "Empty: drag buttons onto the folder", Look.Body, 12, Look.Ink3);
        }

        // The row's frame: placed, hoverable, draggable (all but an empty folder's hint).
        RowView Shell(ToolbarSorter.Row r, Kind kind, Folder folder, float y, float h, float x0)
        {
            var v = new RowView { Row = r, Kind = kind, Folder = folder, Y = y, H = h };
            // Named after the button it stands for (the in-game check finds rows by these).
            string objName = kind == Kind.Folder ? "FolderRow:" + folder.Id : kind == Kind.EmptyFolder ? "Empty:" + folder.Id : "Row:" + r.Seen.Id;
            v.Rt = Ui.Node(content, objName).At(Pad + x0, y, RowW - x0, h);
            v.Bg = Ui.Box(v.Rt, null, raycast: true);
            v.Bg.color = Color.clear;
            v.Hover = v.Rt.gameObject.AddComponent<HoverRelay>();
            if (kind != Kind.EmptyFolder)
            {
                RowDrag drag = v.Rt.gameObject.AddComponent<RowDrag>();
                drag.Began = p => BeginDrag(v, p);
                drag.Moved = p => Dragging(p, autoScroll: false);
                drag.Ended = () => EndDragNow(apply: true);
            }
            views.Add(v);
            return v;
        }

        static string Escape(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        // ---- Folder settings --------------------------------------------------------------------

        void MakeFolder()
        {
            Folder f = sorter.NewFolder();
            if (f == null) return;
            CommitName();
            expanded.Add(f.Id);
            editing = f.Id;
            confirmDelete = false;
            shownKey = null;
        }

        void ToggleEditing(string id)
        {
            CommitName();
            editing = editing == id ? null : id;
            confirmDelete = false;
            shownKey = null;
        }

        void CommitName()
        {
            if (nameField == null || editing == null) return;
            Folder f = sorter.Here?.FolderById(editing);
            if (f != null && nameField.text != f.Name) sorter.SetFolder(f, name: nameField.text);
        }

        // Under the folder's row: its name, its color, how it opens, and delete.
        void MakeEditor(Folder f, float y)
        {
            float x = Pad + Indent / 2, w = RowW - Indent / 2;
            RectTransform panel = Ui.Node(content, "FolderSettings").At(x, y + 2, w, EditH - 8);
            Ui.Box(panel, Look.Round(Look.Panel2, Look.Line2, 8), raycast: true);

            // Name.
            Ui.Label(Ui.Node(panel, "NameLabel").At(12, 8, 60, 28), "NAME", Look.Body, 11, Look.Ink3);
            RectTransform box = Ui.Node(panel, "Name").At(70, 8, w - 82, 28);
            box.gameObject.SetActive(false);    // so the field finds its text when it wakes
            Image boxBg = Ui.Box(box, Look.Round(Look.Void, Look.Line2, 7), raycast: true);
            Text text = Ui.Label(Ui.Node(box, "Text").Stretch(10, 0, 10, 0), "", Look.Body, 14, Look.Ink);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Text placeholder = Ui.Label(Ui.Node(box, "Placeholder").Stretch(10, 0, 10, 0), "Folder name", Look.Body, 14, Look.Ink3);
            nameField = box.gameObject.AddComponent<InputField>();
            nameField.textComponent = text;
            nameField.placeholder = placeholder;
            nameField.targetGraphic = boxBg;
            nameField.transition = Selectable.Transition.None;
            nameField.lineType = InputField.LineType.SingleLine;
            nameField.characterLimit = 32;
            nameField.customCaretColor = true;
            nameField.caretColor = Look.Amber;
            nameField.selectionColor = new Color(1f, 0.65f, 0.17f, 0.35f);
            var nav = nameField.navigation;
            nav.mode = Navigation.Mode.None;
            nameField.navigation = nav;
            nameField.text = f.Name;
            nameField.onEndEdit.AddListener(_ => { CommitName(); shownKey = null; });
            box.gameObject.SetActive(true);

            // Color.
            Ui.Label(Ui.Node(panel, "ColorLabel").At(12, 42, 60, 28), "COLOR", Look.Body, 11, Look.Ink3);
            for (int i = 0; i < Folder.Colors.Length; i++)
            {
                string c = Folder.Colors[i];
                bool on = f.Color == c;
                Btn sw = Btn.Make(panel, "Color" + c, 70 + i * 32, 43, 26, 26, () => { sorter.SetFolder(f, color: c); shownKey = null; })
                    .Skin(Look.Round(Look.FolderColor(c), on ? Look.Ink : Look.FolderColor(c), 13), Look.Round(Look.FolderColor(c), Look.Ink2, 13));
                sw.On = on;
                buttons.Add(sw);
            }

            // Opens on hover or on click; delete (asks first, in place).
            Ui.Label(Ui.Node(panel, "OpensLabel").At(12, 76, 60, 28), "OPENS", Look.Body, 11, Look.Ink3);
            Sprite segOff = Look.Round(Look.Void, Look.Line, 6), segOn = Look.Round(Look.Panel3, Look.Line2, 6), segHover = Look.Round(Look.Panel3, Look.Panel3, 6);
            Btn hover = Btn.Make(panel, "Hover", 70, 77, 64, 26, () => { sorter.SetFolder(f, opensOnHover: true); shownKey = null; })
                .Skin(segOff, segHover, segOn).Colors(Look.Ink2, Look.Ink, Look.Ink).WithLabel("Hover", Look.Body, 13);
            hover.On = f.OpensOnHover;
            Btn click = Btn.Make(panel, "Click", 136, 77, 64, 26, () => { sorter.SetFolder(f, opensOnHover: false); shownKey = null; })
                .Skin(segOff, segHover, segOn).Colors(Look.Ink2, Look.Ink, Look.Ink).WithLabel("Click", Look.Body, 13);
            click.On = !f.OpensOnHover;
            buttons.Add(hover);
            buttons.Add(click);
            if (!confirmDelete)
            {
                buttons.Add(Btn.Make(panel, "Delete", w - 12 - 96, 77, 96, 26, () => { confirmDelete = true; shownKey = null; })
                    .Skin(Look.Round(Look.Panel2, Look.Line2, 6), Look.Round(Look.Panel3, Look.AmberLine, 6))
                    .Colors(Look.Ink2, Look.Ink, Look.Ink).WithLabel("Delete folder", Look.Body, 12));
            }
            else
            {
                Ui.Label(Ui.Node(panel, "Sure").At(w - 12 - 150 - 64, 77, 60, 26), "Delete?", Look.Body, 12, Look.Ink2, TextAnchor.MiddleRight);
                buttons.Add(Btn.Make(panel, "Keep", w - 12 - 150, 77, 70, 26, () => { confirmDelete = false; shownKey = null; })
                    .Skin(Look.Round(Look.Panel2, Look.Line2, 6), Look.Round(Look.Panel3, Look.Line2, 6))
                    .Colors(Look.Ink2, Look.Ink, Look.Ink).WithLabel("Keep", Look.Body, 12));
                buttons.Add(Btn.Make(panel, "DeleteNow", w - 12 - 76, 77, 76, 26, () =>
                    {
                        editing = null;
                        confirmDelete = false;
                        expanded.Remove(f.Id);
                        sorter.DeleteFolder(f);
                        shownKey = null;
                    })
                    .Skin(Look.Round(Look.Amber, Look.Amber, 6), Look.Round(Look.AmberHover, Look.AmberHover, 6))
                    .Colors(Look.AmberInk, Look.AmberInk, Look.AmberInk).WithLabel("Delete", Look.Display, 13, FontStyle.Bold));
            }
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
            if (root == null || v == null || v.Row == null) return;
            CommitName();
            dragging = v;
            target = null;
            // The row being moved floats with the pointer; a line (or a lit folder) shows where it will land.
            ghost = Ui.Node(root, "Moving").At(Pad, 0, RowW, RowH);
            Ui.Box(ghost, Look.Round(Look.Panel3, Look.AmberLine, 7));
            Ui.Picture(Ui.Node(ghost, "Grip").At(6, 10, 12, 16), Look.Grip, Look.Ink2);
            RectTransform tile = Ui.Node(ghost, "Tile").At(24, 3, 31, 30);
            Ui.Box(tile, Look.Round(Look.KspButton, Look.KspButton, 4));
            if (v.Kind == Kind.Folder)
                Ui.Picture(Ui.Node(tile, "Folder").At(5, 5, 21, 20), Look.FolderGlyph, Look.FolderColor(v.Folder.Color));
            else
            {
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
            }
            Ui.Label(Ui.Node(ghost, "Name").At(64, 0, RowW - 110, RowH), v.Kind == Kind.Folder ? v.Folder.Name : v.Row.Seen.Name, Look.Body, 14, Look.Ink);
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
                ghost.At(Pad, Mathf.Clamp(winH / 2 - p.y - RowH / 2, TitleH, winH - FootH - RowH), RowW, RowH);

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
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(content, pointer, cam, out Vector2 c)) return;
            target = Aim(-c.y);
            dropLine.gameObject.SetActive(target.FolderRow == null);
        }

        // Where the dragged row lands with the pointer this far down the list.
        Target Aim(float py)
        {
            var t = new Target();
            bool canFolder = dragging.Kind != Kind.Folder && !dragging.Row.Own;
            RowView over = views.FirstOrDefault(v => py >= v.Y && py < v.Y + v.H);
            if (canFolder && over != null && over != dragging)
            {
                if (over.Kind == Kind.Folder && py > over.Y + over.H * 0.25f && py < over.Y + over.H * 0.75f)
                {
                    t.IntoFolder = true;
                    t.Folder = over.Folder;
                    t.Index = over.Folder.Members.Count;
                    t.FolderRow = over;
                    return t;
                }
                if (over.Kind == Kind.EmptyFolder)
                {
                    t.IntoFolder = true;
                    t.Folder = over.Folder;
                    t.Index = 0;
                    t.FolderRow = views.FirstOrDefault(v => v.Kind == Kind.Folder && v.Folder == over.Folder);
                    return t;
                }
                if (over.Kind == Kind.Member)
                {
                    bool below = py > over.Y + over.H / 2;
                    t.IntoFolder = true;
                    t.Folder = over.Folder;
                    t.Index = over.Folder.Members.IndexOf(over.Row.Seen.Id) + (below ? 1 : 0);
                    dropLine.At(Pad + Indent + 4, (below ? over.Y + over.H : over.Y) - 1, RowW - Indent - 8, 2);
                    return t;
                }
            }
            // Otherwise: a place among the top-level rows on its own side of the divider.
            bool stock = dragging.Row.Seen.StockSide;
            List<RowView> side = views.Where(v => v.TopLevel && v.Row.Seen.StockSide == stock).ToList();
            if (side.Count == 0) return t;
            t.Before = side.FirstOrDefault(v => py < v.Y + v.H / 2);
            float lineY = t.Before != null ? t.Before.Y - 1 : side.Last().BlockEnd - 1;
            dropLine.At(Pad + 4, lineY, RowW - 8, 2);
            return t;
        }

        void EndDragNow(bool apply)
        {
            if (dragging == null) return;
            RowView moved = dragging;
            Target t = target;
            dragging = null;
            target = null;
            if (ghost != null) UnityEngine.Object.Destroy(ghost.gameObject);
            if (dropLine != null) UnityEngine.Object.Destroy(dropLine.gameObject);
            ghost = dropLine = null;
            if (apply && root != null && t != null)
            {
                ToolbarSorter.Seen s = moved.Row.Seen;
                if (t.IntoFolder)
                {
                    sorter.PutInFolder(s, t.Folder, t.Index);
                    expanded.Add(t.Folder.Id);
                }
                else if (t.Before != moved)
                {
                    // Out of its folder first, if it was in one; then its place on the bar.
                    if (moved.Kind == Kind.Member) sorter.TakeOutOfFolder(s);
                    List<ToolbarSorter.Seen> order = views.Where(v => v.TopLevel && v.Row.Seen.StockSide == s.StockSide && v != moved)
                                                          .Select(v => v.Row.Seen).ToList();
                    int at = t.Before != null ? order.IndexOf(t.Before.Row.Seen) : order.Count;
                    order.Insert(at < 0 ? order.Count : at, s);
                    sorter.Reorder(order, upright);
                }
                sorter.MarkSeen(s);
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

        // While typing a folder's name, keys don't reach the game.
        void SetTypingLock(bool want)
        {
            if (want == typingLocked) return;
            if (want) InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT, TypingLockId);
            else InputLockManager.RemoveControlLock(TypingLockId);
            typingLocked = want;
        }

        // ---- For the in-game check ------------------------------------------------------------------

        // The rows as shown: name, hidden or not, which side of the divider, and folders.
        public List<string> ShownRows() =>
            views.Where(v => v.Row != null).Select(v =>
                v.Kind == Kind.Folder ? $"folder {v.Folder.Name} ({v.Folder.Color}, {(v.Folder.OpensOnHover ? "hover" : "click")})"
                : $"{(v.Kind == Kind.Member ? "  in " + v.Folder.Name + ":" : v.Row.Seen.StockSide ? "KSP" : "mod")} {(v.Row.Hidden ? "hidden" : "shown ")} {v.Row.Seen.Name}").ToList();
    }

    // Passes a row's drag to the window. (A row is dragged from anywhere but its buttons.)
    public class RowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Action<Vector2> Began, Moved;
        internal Action Ended;
        public void OnBeginDrag(PointerEventData e) => Began?.Invoke(e.position);
        public void OnDrag(PointerEventData e) => Moved?.Invoke(e.position);
        public void OnEndDrag(PointerEventData e) => Ended?.Invoke();
    }
}
