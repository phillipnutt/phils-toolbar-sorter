using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI;
using KSP.UI.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace PhilsToolbarSorter
{
    // Folders on the bar. Each folder in this scene's setup is a toolbar button of the mod's
    // own, drawn as a folder in its color, with its place in the bar's order like any button.
    // Opening it (on hover or on click, the folder's choice) shows a strip next to it holding
    // the buttons that are in it: the real buttons, borrowed from the bar while the strip is
    // open, so they work exactly as they do on the bar (their windows open beside them, their
    // own hover menus work, they show on/off). KSP keeps them in its lists the whole time;
    // they are only moved on screen, and put back (and kept off the bar) when the strip closes.
    public class FolderBar
    {
        const float CloseAfter = 0.4f;     // seconds away from a hover folder before it closes
        const float Pad = 4, Gap = 2, TitleH = 18;

        class Borrowed
        {
            public ToolbarSorter.Seen Seen;
            public RectTransform Rt;
            public Transform Parent;
            public int Sibling;
            public bool WasOn, ButtonWasOn;
            public Vector2 AnchorMin, AnchorMax, Pivot, Size, Position;
            public Vector3 Scale;
        }

        class Live
        {
            public string Id;
            public ApplicationLauncherButton Button;
            public string Color;
            public bool OverButton;
            public RectTransform Strip;
            public HoverRelay StripHover;
            public readonly List<Borrowed> Lent = new List<Borrowed>();
            public float Away;
        }

        readonly ToolbarSorter sorter;
        readonly Dictionary<string, Live> live = new Dictionary<string, Live>();
        readonly Dictionary<ApplicationLauncherButton, string> byButton = new Dictionary<ApplicationLauncherButton, string>();
        string scene;

        public FolderBar(ToolbarSorter sorter) { this.sorter = sorter; }

        // ---- What the rest of the mod asks ----------------------------------------------------

        public bool IsFolderButton(ApplicationLauncherButton b, out string folderId)
        {
            folderId = null;
            return b != null && byButton.TryGetValue(b, out folderId);
        }

        // In an open strip right now.
        public bool IsLent(UIListItem item) => item != null && live.Values.Any(l => l.Lent.Any(x => x.Rt == (RectTransform)item.transform));

        public bool AnyLentFrom(UIList list) => list != null && live.Values.Any(l => l.Lent.Any(x => x.Seen.List == list));

        public bool IsOpen(string folderId) => live.TryGetValue(folderId, out Live l) && l.Strip != null;

        // ---- Keeping the folder buttons in step with the setup -------------------------------------

        // Every frame: a button for each folder in this scene's setup (and none for folders
        // that are gone); hover folders close when the pointer has been away a moment.
        public void Tend(SceneSetup here, string sceneName)
        {
            if (sceneName != scene) { RemoveAll(); scene = sceneName; }
            if (here == null || ApplicationLauncher.Instance == null || !ApplicationLauncher.Ready) return;

            foreach (string gone in live.Keys.Where(id => here.FolderById(id) == null).ToList()) Remove(gone);
            foreach (Folder f in here.Folders)
            {
                if (!live.TryGetValue(f.Id, out Live l) || l.Button == null)
                {
                    if (l != null) Remove(f.Id);
                    l = Add(f, sceneName);
                    if (l == null) continue;
                }
                if (l.Color != f.Color)
                {
                    l.Color = f.Color;
                    l.Button.SetTexture(Look.FolderIcon(f.Color));
                }
                if (l.Strip != null)
                {
                    bool away = !l.OverButton && !(l.StripHover != null && l.StripHover.Over);
                    l.Away = away ? l.Away + Time.unscaledDeltaTime : 0;
                    if (f.OpensOnHover && l.Away > CloseAfter) Close(l);
                    else if (l.Lent.Any(x => x.Seen.Button == null)) Close(l);     // a button went away while lent
                    else Place(l);
                }
            }
        }

        Live Add(Folder f, string sceneName)
        {
            var l = new Live { Id = f.Id, Color = f.Color };
            string id = f.Id;
            l.Button = ApplicationLauncher.Instance.AddModApplication(
                () => OnClick(id, true), () => OnClick(id, false),
                () => OnHover(id, true), () => OnHover(id, false),
                () => { }, () => { }, ScenesOf(sceneName), Look.FolderIcon(f.Color));
            if (l.Button == null) return null;
            live[id] = l;
            byButton[l.Button] = id;
            return l;
        }

        void Remove(string id)
        {
            if (!live.TryGetValue(id, out Live l)) return;
            Close(l);
            live.Remove(id);
            if (l.Button != null)
            {
                byButton.Remove(l.Button);
                if (ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(l.Button);
            }
        }

        public void RemoveAll()
        {
            foreach (string id in live.Keys.ToList()) Remove(id);
            byButton.Clear();
        }

        public void CloseAll()
        {
            foreach (Live l in live.Values) Close(l);
        }

        static ApplicationLauncher.AppScenes ScenesOf(string scene)
        {
            switch (scene)
            {
                case Setup.SpaceCenter: return ApplicationLauncher.AppScenes.SPACECENTER;
                case Setup.Editors: return ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH;
                case Setup.Flight: return ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW;
                case Setup.TrackingStation: return ApplicationLauncher.AppScenes.TRACKSTATION;
                default: return ApplicationLauncher.AppScenes.NEVER;
            }
        }

        // ---- Opening and closing ------------------------------------------------------------------

        void OnHover(string id, bool over)
        {
            if (!live.TryGetValue(id, out Live l)) return;
            l.OverButton = over;
            Folder f = sorter.Here?.FolderById(id);
            if (over && f != null && f.OpensOnHover && l.Strip == null) Open(l);
        }

        void OnClick(string id, bool on)
        {
            if (!live.TryGetValue(id, out Live l)) return;
            Folder f = sorter.Here?.FolderById(id);
            if (f == null) return;
            if (f.OpensOnHover)
            {
                // A hover folder is already open under the pointer; the click just leaves it be.
                if (l.Strip == null) Open(l);
                if (on && l.Button != null) l.Button.SetFalse(false);
                return;
            }
            if (on) Open(l);
            else Close(l);
        }

        void Open(Live l)
        {
            if (l.Strip != null || l.Button == null) return;
            SceneSetup here = sorter.Here;
            Folder f = here?.FolderById(l.Id);
            if (f == null) return;
            CloseAll();
            sorter.Window.CloseFolderEditing();

            Canvas canvas = l.Button.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            Look.Init();
            l.Strip = Ui.Node(canvas.rootCanvas.transform, "PhilsToolbarSorterFolder");
            l.Strip.anchorMin = l.Strip.anchorMax = new Vector2(0.5f, 0.5f);
            l.Strip.pivot = new Vector2(0, 1);
            Ui.Box(l.Strip, Look.Round(Look.KspBar, Look.FolderColor(f.Color), 6), raycast: true);
            l.StripHover = l.Strip.gameObject.AddComponent<HoverRelay>();
            Text title = Ui.Label(Ui.Node(l.Strip, "Name").At(Pad + 2, 1, 200, TitleH), f.Name, Look.Display, 13, Look.FolderColor(f.Color));
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform items = Ui.Node(l.Strip, "Buttons");

            // Borrow the buttons that are in the folder and belong in this scene, in the folder's order.
            Vector2 cell = new Vector2(40, 40);
            foreach (string id in f.Members)
            {
                ToolbarSorter.Seen s = sorter.Present(id);
                if (s == null || !sorter.KspWouldShow(s.Button)) continue;
                var rt = (RectTransform)s.Item.transform;
                if (rt.rect.width > 8) cell = rt.rect.size;
                l.Lent.Add(new Borrowed
                {
                    Seen = s, Rt = rt, Parent = rt.parent, Sibling = rt.GetSiblingIndex(),
                    WasOn = rt.gameObject.activeSelf, ButtonWasOn = s.Button.gameObject.activeSelf,
                    AnchorMin = rt.anchorMin, AnchorMax = rt.anchorMax, Pivot = rt.pivot, Size = rt.sizeDelta, Position = rt.anchoredPosition, Scale = rt.localScale,
                });
            }

            // A column going up from a flat bar, a row going left from the upright one.
            bool upright = ApplicationLauncher.Instance != null && ApplicationLauncher.Instance.IsPositionedAtTop;
            int n = Mathf.Max(1, l.Lent.Count);
            float w = upright ? n * cell.x + (n - 1) * Gap : cell.x, h = upright ? cell.y : n * cell.y + (n - 1) * Gap;
            float stripW = Mathf.Max(w + 2 * Pad, title.preferredWidth + 2 * Pad + 4), stripH = TitleH + h + 2 * Pad;
            l.Strip.sizeDelta = new Vector2(stripW, stripH);
            items.At((stripW - w) / 2, TitleH + Pad, w, h);
            for (int i = 0; i < l.Lent.Count; i++)
            {
                Borrowed b = l.Lent[i];
                b.Rt.SetParent(items, false);
                b.Rt.anchorMin = b.Rt.anchorMax = b.Rt.pivot = new Vector2(0, 1);
                b.Rt.sizeDelta = cell;
                b.Rt.localScale = Vector3.one;
                b.Rt.anchoredPosition = upright ? new Vector2(i * (cell.x + Gap), 0) : new Vector2(0, -(l.Lent.Count - 1 - i) * (cell.y + Gap));
                b.Rt.gameObject.SetActive(true);
                b.Seen.Button.gameObject.SetActive(true);
            }
            if (l.Lent.Count == 0)
                Ui.Label(Ui.Node(items, "Empty").Stretch(), "Empty", Look.Body, 11, Look.Ink3, TextAnchor.MiddleCenter);
            l.Away = 0;
            Place(l);
            l.Strip.SetAsLastSibling();
        }

        // Next to the folder button: above it on a flat bar, to its left on the upright one,
        // kept on screen.
        void Place(Live l)
        {
            if (l.Strip == null || l.Button == null) return;
            var parent = (RectTransform)l.Strip.parent;
            var btn = (RectTransform)l.Button.transform;
            Vector3[] c = new Vector3[4];
            btn.GetWorldCorners(c);       // bottom-left, top-left, top-right, bottom-right
            Vector2 bl = parent.InverseTransformPoint(c[0]), tr = parent.InverseTransformPoint(c[2]);
            Vector2 size = l.Strip.sizeDelta;
            bool upright = ApplicationLauncher.Instance != null && ApplicationLauncher.Instance.IsPositionedAtTop;
            Vector2 topLeft = upright
                ? new Vector2(bl.x - 4 - size.x, tr.y)
                : new Vector2((bl.x + tr.x) / 2 - size.x / 2, tr.y + 4 + size.y);
            Rect area = parent.rect;
            topLeft.x = Mathf.Clamp(topLeft.x, area.xMin + 2, area.xMax - 2 - size.x);
            topLeft.y = Mathf.Clamp(topLeft.y, area.yMin + 2 + size.y, area.yMax - 2);
            l.Strip.anchoredPosition = topLeft;
        }

        void Close(Live l)
        {
            if (l == null || (l.Strip == null && l.Lent.Count == 0)) return;     // not open
            foreach (Borrowed b in l.Lent)
            {
                if (b.Rt == null) continue;
                b.Rt.SetParent(b.Parent, false);
                if (b.Parent != null) b.Rt.SetSiblingIndex(Mathf.Min(b.Sibling, b.Parent.childCount - 1));
                b.Rt.anchorMin = b.AnchorMin;
                b.Rt.anchorMax = b.AnchorMax;
                b.Rt.pivot = b.Pivot;
                b.Rt.sizeDelta = b.Size;
                b.Rt.anchoredPosition = b.Position;
                b.Rt.localScale = b.Scale;
                b.Rt.gameObject.SetActive(b.WasOn);
                if (b.Seen.Button != null) b.Seen.Button.gameObject.SetActive(b.ButtonWasOn);
            }
            var back = l.Lent.Select(b => b.Seen).ToList();
            l.Lent.Clear();
            if (l.Strip != null) UnityEngine.Object.Destroy(l.Strip.gameObject);
            l.Strip = null;
            l.StripHover = null;
            // Off the bar again, the way KSP hides a button.
            foreach (ToolbarSorter.Seen s in back) sorter.ApplyVisibility(s);
            if (l.Button != null && sorter.Here?.FolderById(l.Id) is Folder f && !f.OpensOnHover) l.Button.SetFalse(false);
        }
    }
}
