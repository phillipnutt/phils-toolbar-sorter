// Phil's Toolbar Sorter
//
// Hide and reorder the buttons on KSP's own toolbar. A window from the mod's own
// toolbar button lists the buttons on the bar, in the bar's order: drag a row to
// move its button, click the eye to hide or show it. One setup for every scene and
// every save.
//
// How it works, in two rules applied every frame:
//   - The bar is in the player's order. KSP draws each side of the toolbar (mods,
//     and KSP's own) from a list it never re-sorts; when that list isn't in the
//     player's order, it is put back in order with KSP's own list calls.
//   - A button the player hid stays off. KSP decides each button's visibility in one
//     place (ApplicationLauncher.ShouldBeVisible); a Harmony postfix adds "and the
//     player hasn't hidden it", so KSP hides the button its own way and tells its
//     mod. A button some mod switches on by itself anyway is switched off again.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.UI;
using KSP.UI.Screens;
using UnityEngine;

[assembly: KSPAssembly("PhilsToolbarSorter", 0, 2)]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]
[assembly: AssemblyInformationalVersion("0.2.0")]
[assembly: AssemblyTitle("Phil's Toolbar Sorter")]

namespace PhilsToolbarSorter
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class ToolbarSorter : MonoBehaviour
    {
        public static ToolbarSorter Instance { get; private set; }
        public Setup Setup { get; private set; }

        // The setup for the scene KSP is in now (none at the main menu).
        public SceneSetup Here => Setup?.For(Setup.SceneOf(HighLogic.LoadedScene));
        public SorterWindow Window { get; private set; }
        public FolderBar Folders { get; private set; }
        public ApplicationLauncherButton OwnButton { get; private set; }

        // A toolbar button the mod has met, by the list item KSP keeps it in.
        public class Seen
        {
            public ApplicationLauncherButton Button;
            public UIListItem Item;
            public UIList List;         // the side of the toolbar it is on
            public string Id, Name;
            public string LegacyId;     // what 0.2.0 called it, if that was different
            public bool StockSide;      // right of the divider, with KSP's own buttons
            public bool WasOnBar;       // shown at some point in this scene
        }

        // One row of the window.
        public class Row
        {
            public Seen Seen;
            public bool Hidden, New, Own;
            public Folder Folder;       // this row is a folder
            public Folder InFolder;     // this row's button is in a folder
        }

        const ApplicationLauncher.AppScenes OwnScenes =
            ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW |
            ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.TRACKSTATION;

        readonly Dictionary<UIListItem, Seen> seen = new Dictionary<UIListItem, Seen>();
        readonly Dictionary<ApplicationLauncherButton, bool> kspSays = new Dictionary<ApplicationLauncherButton, bool>();   // KSP's own verdict, this scene
        bool patched;
        int sortsLogged, errorsLogged;
        SceneSetup lastHere;

        void Start()
        {
            if (Instance != null) { Destroy(this); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Setup = Setup.Load();
            Folders = new FolderBar(this);
            Window = new SorterWindow(this);
            Patch();
            GameEvents.onGUIApplicationLauncherReady.Add(AddOwnButton);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneLeaving);
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
            if (ApplicationLauncher.Ready) AddOwnButton();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            GameEvents.onGUIApplicationLauncherReady.Remove(AddOwnButton);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneLeaving);
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
            Window?.Close();
            Setup?.SaveIfChanged();
            Instance = null;
        }

        void OnApplicationQuit() => Setup?.SaveIfChanged();

        void OnHideUI() => Window.SetUiHidden(true);
        void OnShowUI() => Window.SetUiHidden(false);

        // ---- Its own button --------------------------------------------------------------

        void AddOwnButton()
        {
            if (OwnButton != null || ApplicationLauncher.Instance == null) return;
            Look.Init();
            OwnButton = ApplicationLauncher.Instance.AddModApplication(
                OpenWindow, CloseWindow, () => { }, () => { }, () => { }, () => { }, OwnScenes, Look.ToolbarIcon);
        }

        void OpenWindow() => Window.Open();
        void CloseWindow() => Window.Close();

        // The close button on the window: un-press the toolbar button too.
        public void CloseFromWindow()
        {
            if (OwnButton != null) OwnButton.SetFalse(true);
            else Window.Close();
        }

        void OnSceneLeaving(GameScenes next)
        {
            Window.Close();
            Folders.RemoveAll();
            ButtonNames.Forget();
            if (OwnButton != null && ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(OwnButton);
            OwnButton = null;
            kspSays.Clear();
            foreach (UIListItem gone in seen.Keys.Where(k => k == null).ToList()) seen.Remove(gone);
            foreach (Seen s in seen.Values) s.WasOnBar = false;
            Setup.SaveIfChanged();
        }

        // ---- The two rules -----------------------------------------------------------------

        void Patch()
        {
            try
            {
                MethodInfo original = typeof(ApplicationLauncher).GetMethod("ShouldBeVisible",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(ApplicationLauncherButton) }, null);
                MethodInfo postfix = typeof(ToolbarSorter).GetMethod(nameof(AfterShouldBeVisible), BindingFlags.Static | BindingFlags.NonPublic);
                if (original == null)
                {
                    Debug.LogWarning("[PhilsToolbarSorter] ApplicationLauncher.ShouldBeVisible(button) not found; hidden buttons are switched off directly instead.");
                    return;
                }
                new Harmony("PhilsToolbarSorter").Patch(original, postfix: new HarmonyMethod(postfix));
                patched = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[PhilsToolbarSorter] Couldn't patch ApplicationLauncher.ShouldBeVisible; hidden buttons are switched off directly instead: {e}");
            }

            // Which mod puts each button on the bar (to name a button that calls nothing of its mod's).
            try
            {
                var harmony = new Harmony("PhilsToolbarSorter.Adders");
                var after = new HarmonyMethod(typeof(ToolbarSorter).GetMethod(nameof(AfterAdd), BindingFlags.Static | BindingFlags.NonPublic));
                foreach (string name in new[] { "AddApplication", "AddModApplication" })
                {
                    MethodInfo add = typeof(ApplicationLauncher).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(ApplicationLauncherButton) }, null);
                    if (add != null) harmony.Patch(add, postfix: after);
                    else Debug.LogWarning($"[PhilsToolbarSorter] ApplicationLauncher.{name}(button) not found.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PhilsToolbarSorter] Couldn't watch buttons being added: {e.Message}");
            }
        }

        static void AfterAdd(ApplicationLauncherButton __0) => ButtonNames.NoteAdder(__0);

        // KSP asks whether a button should show here. Remember KSP's answer (the window
        // lists what KSP would show), then add the player's own "no".
        static void AfterShouldBeVisible(ApplicationLauncherButton __0, ref bool __result)
        {
            ToolbarSorter me = Instance;
            if (me == null || __0 == null) return;
            me.kspSays[__0] = __result;
            if (__result && me.HiddenByPlayer(__0)) __result = false;
        }

        void LateUpdate()
        {
            try
            {
                ApplauncherLayout layout = ApplicationLauncher.Instance != null ? ApplicationLauncher.Instance.CurrentLayout : null;
                SceneSetup here = Here;
                Folders.Tend(layout != null ? here : null, Setup.SceneOf(HighLogic.LoadedScene));
                if (layout != null && here != null)
                {
                    // Into a scene with its own setup: buttons that stay on the bar from the
                    // last scene get KSP to decide again whether they show here.
                    if (here != lastHere)
                    {
                        lastHere = here;
                        foreach (Seen s in seen.Values.ToList())
                            if (s.Button != null) ApplyVisibility(s);
                    }
                    Tend(layout.GetModList(), false, here);
                    Tend(layout.GetStockList(), true, here);
                }
            }
            catch (Exception e)
            {
                if (errorsLogged++ < 5) Debug.LogError($"[PhilsToolbarSorter] Toolbar update failed: {e}");
            }
            Setup.SaveIfChanged();
            Window.LateUpdate();
        }

        // One side of the toolbar: meet new buttons, keep hidden ones off, keep the order.
        void Tend(UIList list, bool stockSide, SceneSetup here)
        {
            if (list == null) return;
            int count = list.Count, highest = -1;
            bool inOrder = true, complete = true;
            Func<ApplicationLauncherButton, UIApp> appOf = null;
            for (int i = 0; i < count; i++)
            {
                UIListItem item = list.GetUilistItemAt(i);
                if (item == null) { complete = false; continue; }
                Seen s = Meet(item, stockSide, list, ref appOf);
                if (s == null) continue;
                s.List = list;
                if (s.LegacyId != null) here.Rename(s.LegacyId, s.Id);     // keeps its place under its new name
                here.Add(s.Id, s.Name);     // new to this scene: on the end
                KeepOffIfHidden(s);
                int rank = here.Rank(s.Id);
                if (rank < highest) inOrder = false;
                else highest = rank;
            }
            // A list with a destroyed button still in it is left alone: moving items
            // makes KSP walk the whole list, and it complains about the dead one. So is a
            // side with a button lent to an open folder (KSP's moves would take it back).
            if (!inOrder && complete && !Folders.AnyLentFrom(list)) PutInOrder(list, here);
        }

        Seen Meet(UIListItem item, bool stockSide, UIList list, ref Func<ApplicationLauncherButton, UIApp> appOf)
        {
            if (seen.TryGetValue(item, out Seen s) && s.Button != null)
            {
                s.StockSide = stockSide;
                return s;
            }
            ApplicationLauncherButton b = item.GetComponentInChildren<ApplicationLauncherButton>(true) ?? item.GetComponentInParent<ApplicationLauncherButton>();
            if (b == null) return null;

            // KSP's own apps, found once per frame that meets new buttons.
            if (appOf == null)
            {
                var apps = new Dictionary<ApplicationLauncherButton, UIApp>();
                foreach (UIApp app in FindObjectsOfType<UIApp>())
                    if (app != null && app.appLauncherButton != null) apps[app.appLauncherButton] = app;
                appOf = x => apps.TryGetValue(x, out UIApp a) ? a : null;
            }
            string id, name;
            if (Folders.IsFolderButton(b, out string folderId))
            {
                id = folderId;
                name = Here?.FolderById(folderId)?.Name ?? "Folder";
            }
            else ButtonNames.Identify(b, appOf, out id, out name);
            // Two buttons from the same place in the same mod, both on the bar: number them
            // in KSP's order. (A button its mod has just replaced, on its way out, doesn't count.)
            string baseId = id;
            for (int n = 2; seen.Values.Any(o => o.Id == id && o.Button != null && o.Button != b && o.List != null && o.List.Contains(o.Item)); n++)
                id = baseId + "#" + n;

            string legacy = folderId == null ? ButtonNames.LegacyId(b) : null;
            s = new Seen { Button = b, Item = item, Id = id, Name = name, StockSide = stockSide, List = list, LegacyId = legacy != id ? legacy : null };
            seen[item] = s;
            Debug.Log($"[PhilsToolbarSorter] Button: {name} [{id}] ({(stockSide ? "KSP's side" : "mod side")})");
            if (HiddenByPlayer(s)) ApplyVisibility(s);
            return s;
        }

        void KeepOffIfHidden(Seen s)
        {
            GameObject go = s.Button.gameObject;
            if (!go.activeSelf || Folders.IsLent(s.Item)) return;
            if (!HiddenByPlayer(s))
            {
                s.WasOnBar = true;
                return;
            }
            s.WasOnBar = true;
            if (patched) s.Button.VisibleInScenes = s.Button.VisibleInScenes;    // KSP hides it its own way
            if (go.activeSelf) go.SetActive(false);                              // a mod switched it on by itself
        }

        void PutInOrder(UIList list, SceneSetup here)
        {
            var items = new List<UIListItem>();
            for (int i = 0; i < list.Count; i++) items.Add(list.GetUilistItemAt(i));
            var wanted = items.Select((item, i) => new { item, i, rank = seen.TryGetValue(item, out Seen s) ? here.Rank(s.Id) : int.MaxValue })
                              .OrderBy(x => x.rank).ThenBy(x => x.i).Select(x => x.item).ToList();
            int moved = 0;
            for (int i = 0; i < wanted.Count; i++)
            {
                UIListItem item = wanted[i];
                if (list.GetUilistItemAt(i) == item) continue;
                // KSP's insert switches the item on; a hidden button goes straight back off.
                bool on = item.gameObject.activeSelf;
                list.RemoveItemAndMove(item, list.ListAnchor, true, false);
                list.InsertItem(item, i, true, false);
                if (item.gameObject.activeSelf != on) item.gameObject.SetActive(on);
                moved++;
            }
            if (moved > 0 && sortsLogged++ < 40) Debug.Log($"[PhilsToolbarSorter] Put {moved} of {wanted.Count} buttons back in order.");
        }

        // ---- Hidden or not -----------------------------------------------------------------

        // Off the bar: hidden with the eye, or in a folder (unless lent to its open strip).
        bool HiddenByPlayer(Seen s)
        {
            SceneSetup here = Here;
            if (here == null || s.Button == OwnButton || Folders.IsLent(s.Item)) return false;
            return (here.Get(s.Id)?.Hidden ?? false) || here.FolderOf(s.Id) != null;
        }

        bool HiddenByPlayer(ApplicationLauncherButton b) =>
            b.container != null && seen.TryGetValue(b.container, out Seen s) && s.Button == b && HiddenByPlayer(s);

        // Let KSP decide again whether the button shows (the postfix adds the player's say).
        internal void ApplyVisibility(Seen s)
        {
            if (s.Button == null) return;
            if (patched) s.Button.VisibleInScenes = s.Button.VisibleInScenes;
            else if (HiddenByPlayer(s)) s.Button.gameObject.SetActive(false);
        }

        // ---- What the window works with ------------------------------------------------------

        // The rows for this scene, in the order they are on screen: left to right on a
        // flat bar, top to bottom on an upright one. A row is a button on the bar now,
        // or one the player hid that would otherwise be here.
        public List<Row> Rows(out bool upright)
        {
            var rows = new List<Row>();
            upright = false;
            ApplauncherLayout layout = ApplicationLauncher.Instance != null ? ApplicationLauncher.Instance.CurrentLayout : null;
            if (layout == null) return rows;
            upright = ApplicationLauncher.Instance.IsPositionedAtTop;
            UIList mods = layout.GetModList(), stock = layout.GetStockList();
            bool modsFirst = mods == null || stock == null || ScreenOrder(mods.transform.position, stock.transform.position, upright) <= 0;
            foreach (UIList list in modsFirst ? new[] { mods, stock } : new[] { stock, mods })
                if (list != null) rows.AddRange(RowsOf(list, upright));
            return rows;
        }

        List<Row> RowsOf(UIList list, bool upright)
        {
            var rows = new List<Row>();
            for (int i = 0; i < list.Count; i++)
            {
                UIListItem item = list.GetUilistItemAt(i);
                if (item == null || !seen.TryGetValue(item, out Seen s) || s.Button == null) continue;
                bool on = s.Button.gameObject.activeSelf, off = HiddenByPlayer(s);
                bool kspWould = kspSays.TryGetValue(s.Button, out bool k) && k;
                if (!on && !(off && (kspWould || s.WasOnBar))) continue;
                SceneSetup.Entry e = Here?.Get(s.Id);
                rows.Add(new Row
                {
                    Seen = s, Hidden = e != null && e.Hidden, New = e != null && e.New, Own = s.Button == OwnButton,
                    Folder = Here?.FolderById(s.Id), InFolder = Here?.FolderOf(s.Id),
                });
            }
            if (Backwards(list, upright)) rows.Reverse();
            return rows;
        }

        // Whether KSP's list runs against reading order on screen. It runs from the
        // right-hand end of a flat bar and from the top of an upright one; measured
        // from the first and last buttons showing, where there are two.
        bool Backwards(UIList list, bool upright)
        {
            UIListItem first = null, last = null;
            for (int i = 0; i < list.Count; i++)
            {
                UIListItem item = list.GetUilistItemAt(i);
                if (item == null || !item.gameObject.activeInHierarchy || Folders.IsLent(item)) continue;
                if (first == null) first = item;
                last = item;
            }
            if (first == null || first == last) return !upright;
            return ScreenOrder(first.transform.position, last.transform.position, upright) > 0;
        }

        // Negative when a comes first on screen (further left, or higher up).
        static int ScreenOrder(Vector3 a, Vector3 b, bool upright) => upright ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x);

        public void SetHidden(Seen s, bool hidden)
        {
            if (s == null || s.Button == OwnButton || Here == null) return;
            Here.SetHidden(s.Id, hidden);
            ApplyVisibility(s);
        }

        // The player dragged rows into this on-screen order (one side of the divider).
        public void Reorder(IList<Seen> onScreen, bool upright)
        {
            if (onScreen == null || onScreen.Count == 0) return;
            List<Seen> rows = onScreen.Where(s => s != null).ToList();
            UIList list = rows[0].List;
            if (list == null || Here == null) return;
            if (Backwards(list, upright)) rows.Reverse();
            Here.Reorder(rows.Select(s => s.Id).ToList());
        }

        public void MarkSeen(Seen s)
        {
            if (s != null) Here?.MarkSeen(s.Id);
        }

        // ---- Folders (from the window) -----------------------------------------------------------

        // A button on the bar in this scene, by its id.
        public Seen Present(string id) =>
            seen.Values.FirstOrDefault(s => s.Id == id && s.Button != null && s.List != null && s.List.Contains(s.Item));

        // A new folder in this scene: its button appears at the far end of the mod side.
        public Folder NewFolder()
        {
            Folder f = Here?.NewFolder();
            if (f != null) Debug.Log($"[PhilsToolbarSorter] New folder {f.Id} in {Setup.DisplayName(Here.Name)}.");
            return f;
        }

        // The folder goes; its buttons go back on the bar where they were.
        public void DeleteFolder(Folder f)
        {
            if (f == null || Here == null) return;
            Folders.CloseAll();
            var members = f.Members.ToList();
            Here.DeleteFolder(f.Id);
            foreach (string id in members) { Seen s = Present(id); if (s != null) ApplyVisibility(s); }
        }

        public void SetFolder(Folder f, string name = null, string color = null, bool? opensOnHover = null)
        {
            if (f == null || Here == null) return;
            Here.SetFolder(f.Id, name, color, opensOnHover);
            Seen s = Present(f.Id);
            if (s != null) s.Name = f.Name;
        }

        // Into a folder at this place among its buttons (this window's own button stays on the bar).
        public void PutInFolder(Seen s, Folder f, int index)
        {
            if (s == null || f == null || Here == null || s.Button == OwnButton || Here.FolderById(s.Id) != null) return;
            Folders.CloseAll();
            Here.PutInFolder(s.Id, f.Id, index);
            ApplyVisibility(s);
        }

        // Out of its folder: back on the bar where it was.
        public void TakeOutOfFolder(Seen s)
        {
            if (s == null || Here == null) return;
            Folders.CloseAll();
            Here.TakeOutOfFolder(s.Id);
            ApplyVisibility(s);
        }

        // Every button met so far, and whether KSP itself would show one here (for the in-game check).
        public IEnumerable<Seen> AllSeen() => seen.Values;
        public bool KspWouldShow(ApplicationLauncherButton b) => b != null && kspSays.TryGetValue(b, out bool k) && k;
    }
}
