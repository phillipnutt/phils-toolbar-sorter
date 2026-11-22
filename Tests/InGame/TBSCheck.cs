// TBSCheck: throwaway in-game check of Phil's Toolbar Sorter. Not a mod; installed only for the test runs.
// Does nothing unless <KSP>/TBSCheck/run.flag exists. Its text is the phase:
//   1: makes the career save "zz_TBSCheck". If the install had a 0.1.0 setup (one for every scene), checks that every
//      scene starts as a copy of it. Then, each scene with its own changes: at the Space Center hides a button with the
//      window's eye, drags a row with the window and moves two more through the mod's API; in the VAB hides one and
//      moves one; in flight the same (map view shares flight's setup); the Tracking Station is left alone. Checks every
//      scene after each (Space Center again at the end), saves what it expects for each scene, quits.
//   2: loads that save after a relaunch and checks every scene's setup and bar again; quits.
//   icons: a sandbox save, the toolbar at the Space Center and in the VAB, screenshots; quits.
// Checks (each scene): each side of the bar is in this scene's order (KSP's list follows the setup, and the buttons on
// screen follow KSP's list); buttons hidden here are off; buttons KSP would show here and not hidden here are ON (a
// button hidden in another scene doesn't stay hidden); every button is named by its mod; changes made in one scene
// leave the others as they were.
// Output: <KSP>/TBSCheck/results-phase<N>.log and *.png (taken with the interface showing).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP.UI;
using KSP.UI.Screens;
using PhilsToolbarSorter;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[KSPAddon(KSPAddon.Startup.MainMenu, true)]
public class TBSCheck : MonoBehaviour
{
    const string SaveName = "zz_TBSCheck";
    static string Root => Path.Combine(KSPUtil.ApplicationRootPath, "TBSCheck");
    StreamWriter log;
    int pass, fail;
    readonly List<string> errors = new List<string>();
    string phase;

    void Start()
    {
        string flag = Path.Combine(Root, "run.flag");
        if (!File.Exists(flag)) { Destroy(this); return; }
        phase = File.ReadAllText(flag).Trim();
        DontDestroyOnLoad(gameObject);
        log = new StreamWriter(Path.Combine(Root, "results-phase" + phase + ".log"), false) { AutoFlush = true };
        Application.logMessageReceived += OnLog;
        StartCoroutine(Guarded(phase == "icons" ? PhaseIcons() : phase == "2" ? Phase2() : Phase1()));
    }

    void OnLog(string cond, string stack, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;
        string first = string.IsNullOrEmpty(stack) ? "" : "  @ " + string.Join(" | ", stack.Split('\n').Take(3).Select(l => l.Trim()).ToArray());
        errors.Add("[" + HighLogic.LoadedScene + "] " + type + ": " + cond + first);
    }

    void W(string s) => log.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s);
    void Check(bool ok, string what) { if (ok) pass++; else fail++; W((ok ? "PASS " : "FAIL ") + what); }

    IEnumerator Guarded(IEnumerator inner)
    {
        while (true)
        {
            object cur;
            try { if (!inner.MoveNext()) break; cur = inner.Current; }
            catch (Exception ex) { W("FATAL CHECK EXCEPTION: " + ex); fail++; break; }
            yield return cur;
        }
        W("Errors and exceptions logged during the check: " + errors.Count);
        foreach (string e in errors) W("  " + e);
        W($"[TBS-CHECK-DONE] phase {phase}: {pass} pass, {fail} fail");
        yield return new WaitForSeconds(2f);
        Application.Quit();
    }

    ToolbarSorter S => ToolbarSorter.Instance;
    SceneSetup Here => S.Here;

    // What each scene's setup held when the run began, and what it should hold after the run's changes.
    readonly Dictionary<string, List<string>> startOrder = new Dictionary<string, List<string>>();
    readonly Dictionary<string, HashSet<string>> startHidden = new Dictionary<string, HashSet<string>>();
    readonly Dictionary<string, List<string>> wantOrder = new Dictionary<string, List<string>>();
    readonly Dictionary<string, HashSet<string>> wantHidden = new Dictionary<string, HashSet<string>>();

    // ---- Phase 1 -------------------------------------------------------------------------------

    IEnumerator Phase1()
    {
        yield return new WaitForSeconds(3f);
        W("Phase 1 started. Sorter present: " + (S != null) + ", setup file: " + File.Exists(Setup.FilePath));
        CheckCopyOver();
        foreach (string sc in Setup.Scenes)
        {
            startOrder[sc] = S.Setup.For(sc).Order().ToList();
            startHidden[sc] = new HashSet<string>(S.Setup.For(sc).Hidden());
        }

        GameParameters gp = GameParameters.GetDefaultParameters(Game.Modes.CAREER, GameParameters.Preset.Normal);
        Game g = GamePersistence.CreateNewGame(SaveName, Game.Modes.CAREER, gp, "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.VAB);
        HighLogic.CurrentGame = g;
        HighLogic.SaveFolder = SaveName;
        g.startScene = GameScenes.SPACECENTER;
        g.Start();
        W("Created career save " + SaveName);

        // Space Center: the window's eye and a drag, then two moves through the API.
        yield return Arrive(GameScenes.SPACECENTER, 14f);
        Report("ksc-before");
        CheckScene("Space Center, before");
        CheckUntouched("Space Center, before", Setup.SpaceCenter);
        yield return Shot("ksc-before");
        S.Window.Open();
        yield return new WaitForSeconds(1f);
        List<ToolbarSorter.Row> rows = S.Rows(out bool upright);
        var mods = rows.Where(r => !r.Seen.StockSide).ToList();
        var ksp = rows.Where(r => r.Seen.StockSide).ToList();
        ToolbarSorter.Row hide = mods.First(r => !r.Own && !r.Hidden);
        ToolbarSorter.Row move = mods.Last(r => !r.Own && r != hide);
        W($"Space Center: hiding with the eye {hide.Seen.Name} [{hide.Seen.Id}]; dragging {move.Seen.Name} to the far end");
        Check(ClickEye(hide.Seen.Name), "eye button found for " + hide.Seen.Name);
        yield return new WaitForSeconds(0.5f);
        Check(Here.Get(hide.Seen.Id).Hidden, "eye hid " + hide.Seen.Name + " in the Space Center setup");
        Check(!hide.Seen.Button.gameObject.activeSelf, "eye switched off " + hide.Seen.Name + " on the bar");
        Check(DragRow(move.Seen.Name, 0), "drag rows found");
        yield return new WaitForSeconds(0.5f);
        yield return Shot("ksc-window");
        S.CloseFromWindow();
        yield return new WaitForSeconds(0.5f);
        Check(!S.Window.IsOpen, "window closes from its close call");
        rows = S.Rows(out upright);
        var modOrder = rows.Where(r => !r.Seen.StockSide).Select(r => r.Seen).ToList();
        ToolbarSorter.Seen own = modOrder.FirstOrDefault(s => s.Button == S.OwnButton);
        Check(own != null, "own button has a row");
        if (own != null) { modOrder.Remove(own); modOrder.Add(own); S.Reorder(modOrder, upright); S.SetHidden(own, true); }
        var kspOrder = rows.Where(r => r.Seen.StockSide).Select(r => r.Seen).ToList();
        ToolbarSorter.Seen firstKsp = rows.Where(r => r.Seen.StockSide && !r.Hidden).Select(r => r.Seen).FirstOrDefault();
        if (firstKsp != null) { kspOrder.Remove(firstKsp); kspOrder.Add(firstKsp); S.Reorder(kspOrder, upright); }
        yield return new WaitForSeconds(1f);
        Check(own != null && !Here.Get(own.Id).Hidden && S.OwnButton.gameObject.activeSelf, "own button can't be hidden");
        var expMods = mods.Select(r => r.Seen).Where(s => s != move.Seen && s != own).ToList();
        expMods.Insert(0, move.Seen);
        if (own != null) expMods.Add(own);
        var expKsp = ksp.Select(r => r.Seen).ToList();
        if (firstKsp != null) { expKsp.Remove(firstKsp); expKsp.Add(firstKsp); }
        rows = S.Rows(out upright);
        Check(rows.Where(r => !r.Seen.StockSide).Select(r => r.Seen.Id).SequenceEqual(expMods.Select(s => s.Id)), "Space Center mod side in the order set: " + Names(rows, false));
        Check(rows.Where(r => r.Seen.StockSide).Select(r => r.Seen.Id).SequenceEqual(expKsp.Select(s => s.Id)), "Space Center KSP side in the order set: " + Names(rows, true));
        CheckScene("Space Center, after");
        Remember(Setup.SpaceCenter);
        yield return Shot("ksc-after");

        // VAB: the Space Center's changes must not show here. Then its own: hide one, move one.
        GamePersistence.SaveGame("persistent", SaveName, SaveMode.OVERWRITE);
        EditorDriver.StartEditor(EditorFacility.VAB);
        yield return Arrive(GameScenes.EDITOR, 12f);
        Report("vab");
        CheckScene("VAB");
        CheckUntouched("VAB", Setup.Editors);
        yield return ChangeHere("VAB");
        CheckScene("VAB, after");
        Remember(Setup.Editors);
        yield return Shot("vab");

        // Flight (map view shares its setup): untouched by the others, then its own changes.
        string craft = WriteCraft();
        Check(craft != null, "test craft written");
        if (craft != null)
        {
            yield return Launch(craft);
            Report("flight");
            CheckScene("Flight");
            CheckUntouched("Flight", Setup.Flight);
            Check(ApplicationLauncher.Instance.IsPositionedAtTop, "flight bar stands upright");
            yield return ChangeHere("Flight");
            CheckScene("Flight, after");
            Remember(Setup.Flight);
            yield return Shot("flight");
            S.Window.Open();
            yield return new WaitForSeconds(1f);
            W("Flight window rows: " + string.Join(" / ", S.Window.ShownRows().ToArray()));
            yield return Shot("flight-window");
            S.CloseFromWindow();
            MapView.EnterMapView();
            yield return new WaitForSeconds(4f);
            Report("map");
            CheckScene("Map view");
            CheckKept("Map view", Setup.Flight);
            yield return Shot("map");
            MapView.ExitMapView();
            yield return new WaitForSeconds(2f);
            GamePersistence.SaveGame("persistent", SaveName, SaveMode.OVERWRITE);
        }

        // Tracking Station: nothing changed anywhere touches it.
        HighLogic.LoadScene(GameScenes.TRACKSTATION);
        yield return Arrive(GameScenes.TRACKSTATION, 12f);
        Report("trackingstation");
        CheckScene("Tracking Station");
        CheckUntouched("Tracking Station", Setup.TrackingStation);
        Remember(Setup.TrackingStation);
        yield return Shot("trackingstation");

        // Space Center again: its own changes still there.
        HighLogic.LoadScene(GameScenes.SPACECENTER);
        yield return Arrive(GameScenes.SPACECENTER, 12f);
        Report("ksc-again");
        CheckScene("Space Center, again");
        CheckKept("Space Center, again", Setup.SpaceCenter);
        yield return Shot("ksc-again");
        GamePersistence.SaveGame("persistent", SaveName, SaveMode.OVERWRITE);

        var save = new ConfigNode("EXPECT");
        foreach (string sc in wantOrder.Keys)
        {
            ConfigNode n = save.AddNode("SCENE");
            n.AddValue("name", sc);
            foreach (string id in wantOrder[sc]) n.AddValue("order", id);
            foreach (string id in wantHidden[sc]) n.AddValue("hidden", id);
        }
        save.Save(Path.Combine(Root, "expected.cfg"));
    }

    // ---- Phase 2: after a relaunch ----------------------------------------------------------

    IEnumerator Phase2()
    {
        yield return new WaitForSeconds(3f);
        W("Phase 2 started. Setup file: " + File.Exists(Setup.FilePath));
        ConfigNode exp = ConfigNode.Load(Path.Combine(Root, "expected.cfg"));
        foreach (ConfigNode n in exp.GetNodes("SCENE"))
        {
            string sc = n.GetValue("name");
            wantOrder[sc] = n.GetValues("order").ToList();
            wantHidden[sc] = new HashSet<string>(n.GetValues("hidden"));
            SceneSetup s = S.Setup.For(sc);
            Check(s != null && new HashSet<string>(s.Hidden()).SetEquals(wantHidden[sc]), $"after relaunch, {Setup.DisplayName(sc)} hides the same buttons ({wantHidden[sc].Count})");
            var order = s != null ? s.Order().Where(id => wantOrder[sc].Contains(id)).ToList() : new List<string>();
            Check(order.SequenceEqual(wantOrder[sc]), $"after relaunch, {Setup.DisplayName(sc)} keeps its order ({wantOrder[sc].Count})");
        }

        Game g = GamePersistence.LoadGame("persistent", SaveName, true, false);
        Check(g != null, "test save loads");
        if (g == null) yield break;
        HighLogic.CurrentGame = g;
        HighLogic.SaveFolder = SaveName;
        g.Start();
        yield return Arrive(GameScenes.SPACECENTER, 14f);
        Report("relaunch-ksc"); CheckScene("Space Center after relaunch"); CheckKept("Space Center after relaunch", Setup.SpaceCenter);
        yield return Shot("relaunch-ksc");
        GamePersistence.SaveGame("persistent", SaveName, SaveMode.OVERWRITE);
        EditorDriver.StartEditor(EditorFacility.VAB);
        yield return Arrive(GameScenes.EDITOR, 12f);
        Report("relaunch-vab"); CheckScene("VAB after relaunch"); CheckKept("VAB after relaunch", Setup.Editors);
        yield return Shot("relaunch-vab");
        string craft = WriteCraft();
        if (craft != null)
        {
            yield return Launch(craft);
            Report("relaunch-flight"); CheckScene("Flight after relaunch"); CheckKept("Flight after relaunch", Setup.Flight);
            yield return Shot("relaunch-flight");
            GamePersistence.SaveGame("persistent", SaveName, SaveMode.OVERWRITE);
        }
        HighLogic.LoadScene(GameScenes.TRACKSTATION);
        yield return Arrive(GameScenes.TRACKSTATION, 12f);
        Report("relaunch-ts"); CheckScene("Tracking Station after relaunch"); CheckKept("Tracking Station after relaunch", Setup.TrackingStation);
        yield return Shot("relaunch-ts");
    }

    // ---- Phase "icons": the toolbar at the Space Center and in the VAB, nothing changed ------------

    IEnumerator PhaseIcons()
    {
        const string save = "zz_TBSIcons";
        yield return new WaitForSeconds(3f);
        W("Icons check started. Sorter present: " + (S != null));
        GameParameters gp = GameParameters.GetDefaultParameters(Game.Modes.SANDBOX, GameParameters.Preset.Normal);
        Game g = GamePersistence.CreateNewGame(save, Game.Modes.SANDBOX, gp, "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.VAB);
        HighLogic.CurrentGame = g;
        HighLogic.SaveFolder = save;
        g.startScene = GameScenes.SPACECENTER;
        g.Start();
        W("Created sandbox save " + save);

        yield return Arrive(GameScenes.SPACECENTER, 14f);
        IconReport("Space Center");
        yield return Shot("icons-ksc");
        GamePersistence.SaveGame("persistent", save, SaveMode.OVERWRITE);
        EditorDriver.StartEditor(EditorFacility.VAB);
        yield return Arrive(GameScenes.EDITOR, 12f);
        IconReport("VAB");
        yield return Shot("icons-vab");
    }

    // Each mod button on the bar, its picture's name and size; this mod's own icon read from its file.
    void IconReport(string where)
    {
        Report(where);
        foreach (ToolbarSorter.Row r in S.Rows(out bool _).Where(x => !x.Seen.StockSide))
        {
            Texture t = r.Seen.Button.sprite != null ? r.Seen.Button.sprite.texture : null;
            W($"  icon: {r.Seen.Name} -> '{(t != null ? t.name : "none")}' {(t != null ? t.width + "x" + t.height : "")}");
        }
        Texture own = S.OwnButton != null && S.OwnButton.sprite != null ? S.OwnButton.sprite.texture : null;
        Check(own != null && own.width == 76 && own.height == 76, $"{where}: own toolbar icon is the 76 px file ({(own != null ? own.width + "x" + own.height : "none")})");
        Check(S.OwnButton != null && S.OwnButton.gameObject.activeInHierarchy, $"{where}: own button on the bar");
    }

    // ---- Checks --------------------------------------------------------------------------------

    // A 0.1.0 setup (one for every scene) becomes a copy in each scene, the old file kept beside it.
    void CheckCopyOver()
    {
        string old = Setup.FilePath + ".before-per-scene";
        if (!File.Exists(old)) { W("No 0.1.0 setup to copy over (fresh start)."); return; }
        ConfigNode node = ConfigNode.Load(old)?.GetNode("TOOLBAR_SORTER");
        var oldOrder = node.GetNodes("BUTTON").Select(b => b.GetValue("id")).ToList();
        var oldHidden = new HashSet<string>(node.GetNodes("BUTTON").Where(b => b.GetValue("hidden") == "True").Select(b => b.GetValue("id")));
        W($"0.1.0 setup found: {oldOrder.Count} buttons, {oldHidden.Count} hidden.");
        foreach (string sc in Setup.Scenes)
        {
            SceneSetup s = S.Setup.For(sc);
            Check(s.Order().Take(oldOrder.Count).SequenceEqual(oldOrder), $"{Setup.DisplayName(sc)} starts with the old order");
            Check(new HashSet<string>(s.Hidden()).SetEquals(oldHidden), $"{Setup.DisplayName(sc)} starts hiding the old {oldHidden.Count}");
        }
    }

    IEnumerator Arrive(GameScenes scene, float settle)
    {
        float t0 = Time.realtimeSinceStartup;
        while (HighLogic.LoadedScene != scene && Time.realtimeSinceStartup - t0 < 180f) yield return null;
        yield return new WaitForSeconds(settle);    // mods add their buttons a few frames to seconds after the scene starts
        W("=== " + SorterWindow.SceneName() + " (setup: " + Setup.DisplayName(Setup.SceneOf(HighLogic.LoadedScene)) + ")");
    }

    IEnumerator Launch(string craft)
    {
        ConfigNode cn = ConfigNode.Load(craft);
        FlightDriver.StartWithNewLaunch(craft, "Squad/Flags/default", "LaunchPad", VesselCrewManifest.FromConfigNode(cn));
        float t0 = Time.realtimeSinceStartup;
        while (!(HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed)
               && Time.realtimeSinceStartup - t0 < 180f) yield return null;
        yield return new WaitForSeconds(12f);
        W("=== Flight (setup: " + Setup.DisplayName(Setup.Flight) + ")");
    }

    // This scene's own change: hide the first visible mod button (not this mod's), and move the
    // last mod button to the first place on screen.
    IEnumerator ChangeHere(string where)
    {
        List<ToolbarSorter.Row> rows = S.Rows(out bool upright);
        var mods = rows.Where(r => !r.Seen.StockSide).ToList();
        ToolbarSorter.Row hide = mods.FirstOrDefault(r => !r.Own && !r.Hidden);
        ToolbarSorter.Row move = mods.LastOrDefault(r => !r.Own && r != hide);
        if (hide == null || move == null) { W(where + ": not enough mod buttons to change"); yield break; }
        W($"{where}: hiding {hide.Seen.Name} [{hide.Seen.Id}], moving {move.Seen.Name} [{move.Seen.Id}] to the first place");
        S.SetHidden(hide.Seen, true);
        var order = mods.Select(r => r.Seen).ToList();
        order.Remove(move.Seen);
        order.Insert(0, move.Seen);
        S.Reorder(order, upright);
        yield return new WaitForSeconds(1f);
        Check(!hide.Seen.Button.gameObject.activeSelf, $"{where}: {hide.Seen.Name} hidden");
        var now = S.Rows(out upright).Where(r => !r.Seen.StockSide).Select(r => r.Seen.Id).ToList();
        Check(now.SequenceEqual(order.Select(s => s.Id)), $"{where}: mod side in the order set");
    }

    // Each side: KSP's list in this scene's order; buttons on screen in KSP's list order; hidden ones off;
    // buttons KSP would show here and not hidden here are on; names from mods; this mod's own button showing.
    void CheckScene(string where)
    {
        ApplauncherLayout layout = ApplicationLauncher.Instance.CurrentLayout;
        bool upright = ApplicationLauncher.Instance.IsPositionedAtTop;
        foreach (var side in new[] { new { stock = false, list = layout.GetModList() }, new { stock = true, list = layout.GetStockList() } })
        {
            string name = side.stock ? "KSP side" : "mod side";
            var items = Items(side.list);
            var ranks = items.Select(i => Here.Rank(IdOf(i))).ToList();
            bool ordered = true;
            for (int k = 1; k < ranks.Count; k++) if (ranks[k] < ranks[k - 1]) ordered = false;
            Check(ordered, $"{where}: {name} list follows this scene's setup ({items.Count} buttons)");
            var shown = items.Where(i => i.gameObject.activeInHierarchy).ToList();
            var steps = new List<float>();
            for (int k = 1; k < shown.Count; k++)
            {
                Vector3 a = shown[k - 1].transform.position, b = shown[k].transform.position;
                steps.Add(upright ? b.y - a.y : b.x - a.x);
            }
            bool monotonic = steps.All(d => d > 0.01f) || steps.All(d => d < -0.01f);
            Check(monotonic, $"{where}: {name} on screen follows the list ({shown.Count} showing)");
        }
        var here = S.AllSeen().Where(x => x.Button != null && x.List != null && x.List.Contains(x.Item)).ToList();
        var stuckOn = here.Where(x => x.Button != S.OwnButton && Here.Get(x.Id) != null && Here.Get(x.Id).Hidden && x.Button.gameObject.activeSelf).Select(x => x.Name).ToList();
        Check(stuckOn.Count == 0, $"{where}: buttons hidden here are off" + (stuckOn.Count > 0 ? " (on: " + string.Join(", ", stuckOn.ToArray()) + ")" : ""));
        var stuckOff = here.Where(x => S.KspWouldShow(x.Button) && !(Here.Get(x.Id)?.Hidden ?? false) && !x.Button.gameObject.activeSelf).Select(x => x.Name).ToList();
        Check(stuckOff.Count == 0, $"{where}: buttons not hidden here are on" + (stuckOff.Count > 0 ? " (off: " + string.Join(", ", stuckOff.ToArray()) + ")" : ""));
        Check(S.OwnButton != null && S.OwnButton.gameObject.activeSelf, $"{where}: this mod's own button shows");
        List<ToolbarSorter.Row> rows = S.Rows(out bool _);
        var byPicture = rows.Where(r => r.Seen.Id.StartsWith("Icon:")).Select(r => r.Seen.Name + " [" + r.Seen.Id + "]").ToList();
        Check(byPicture.Count == 0, $"{where}: every button named by its mod" + (byPicture.Count > 0 ? " (by picture: " + string.Join(", ", byPicture.ToArray()) + ")" : ""));
    }

    // Arriving in a scene whose setup nothing in this run has changed: it is as it was at the start
    // (plus any buttons met here for the first time, on the end), whatever other scenes did.
    void CheckUntouched(string where, string scene)
    {
        SceneSetup s = S.Setup.For(scene);
        Check(s.Order().Take(startOrder[scene].Count).SequenceEqual(startOrder[scene]), $"{where}: this scene's order is as it was at the start");
        Check(new HashSet<string>(s.Hidden()).SetEquals(startHidden[scene]), $"{where}: this scene hides what it did at the start ({startHidden[scene].Count})");
    }

    // After this scene's changes: remember its order and hidden buttons, for later visits and the relaunch.
    void Remember(string scene)
    {
        wantOrder[scene] = S.Setup.For(scene).Order().ToList();
        wantHidden[scene] = new HashSet<string>(S.Setup.For(scene).Hidden());
        W($"{Setup.DisplayName(scene)} now hides: " + string.Join(", ", wantHidden[scene].ToArray()));
    }

    // Back in a scene: its setup and KSP's list order are as remembered.
    void CheckKept(string where, string scene)
    {
        if (!wantOrder.ContainsKey(scene)) return;
        SceneSetup s = S.Setup.For(scene);
        Check(new HashSet<string>(s.Hidden()).SetEquals(wantHidden[scene]), $"{where}: hides the same buttons as before");
        Check(s.Order().Take(wantOrder[scene].Count).SequenceEqual(wantOrder[scene]), $"{where}: setup order kept");
        foreach (bool stock in new[] { false, true })
        {
            List<string> now = ListOrder(stock);
            var both = now.Where(id => wantOrder[scene].Contains(id)).ToList();
            var want = wantOrder[scene].Where(id => both.Contains(id)).ToList();
            Check(both.SequenceEqual(want), $"{where}: {(stock ? "KSP" : "mod")} side on the bar in the kept order ({both.Count})");
        }
    }

    List<UIListItem> Items(UIList list)
    {
        var r = new List<UIListItem>();
        for (int i = 0; i < list.Count; i++) { UIListItem it = list.GetUilistItemAt(i); if (it != null) r.Add(it); }
        return r;
    }

    string IdOf(UIListItem item) => S.AllSeen().FirstOrDefault(s => s.Item == item)?.Id;

    List<string> ListOrder(bool stock)
    {
        ApplauncherLayout layout = ApplicationLauncher.Instance.CurrentLayout;
        return Items(stock ? layout.GetStockList() : layout.GetModList()).Select(IdOf).Where(id => id != null).ToList();
    }

    void Report(string tag)
    {
        List<ToolbarSorter.Row> rows = S.Rows(out bool upright);
        W($"Bar ({tag}, {(upright ? "upright" : "flat")}), in screen order:");
        foreach (ToolbarSorter.Row r in rows)
            W($"  {(r.Seen.StockSide ? "KSP" : "mod")} {(r.Hidden ? "HIDDEN" : r.Seen.Button.gameObject.activeSelf ? "shown " : "off   ")} {r.Seen.Name} [{r.Seen.Id}]{(r.Own ? " (own)" : "")}{(r.New ? " (new)" : "")}");
    }

    static string Names(List<ToolbarSorter.Row> rows, bool stock) => string.Join(", ", rows.Where(r => r.Seen.StockSide == stock).Select(r => r.Seen.Name).ToArray());

    // ---- Driving the window like a player ------------------------------------------------------

    Transform WindowContent()
    {
        Transform w = UIMasterController.Instance.dialogCanvas.transform.Find("PhilsToolbarSorter");
        return w != null ? w.Find("List/Viewport/Content") : null;
    }

    List<RectTransform> RowObjects()
    {
        Transform c = WindowContent();
        var r = new List<RectTransform>();
        if (c == null) return r;
        foreach (Transform t in c) if (t.name == "Row") r.Add((RectTransform)t);
        return r;
    }

    static string RowName(RectTransform row) => row.Find("Name")?.GetComponent<Text>()?.text;

    bool ClickEye(string name)
    {
        RectTransform row = RowObjects().FirstOrDefault(r => RowName(r) == name);
        Button eye = row != null ? row.Find("Eye")?.GetComponent<Button>() : null;
        if (eye == null) return false;
        eye.onClick.Invoke();
        return true;
    }

    // Drags the named row and drops it on the upper half of row number `before` (in window order).
    bool DragRow(string name, int before)
    {
        List<RectTransform> rows = RowObjects();
        RectTransform row = rows.FirstOrDefault(r => RowName(r) == name);
        if (row == null || before >= rows.Count) return false;
        Canvas canvas = row.GetComponentInParent<Canvas>();
        Camera cam = canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector2 start = RectTransformUtility.WorldToScreenPoint(cam, row.TransformPoint(new Vector3(40, -18, 0)));
        Vector2 drop = RectTransformUtility.WorldToScreenPoint(cam, rows[before].TransformPoint(new Vector3(40, -5, 0)));
        var e = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = start, pressPosition = start };
        ExecuteEvents.Execute(row.gameObject, e, ExecuteEvents.beginDragHandler);
        e.position = drop;
        ExecuteEvents.Execute(row.gameObject, e, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(row.gameObject, e, ExecuteEvents.endDragHandler);
        return true;
    }

    // ---- Small things --------------------------------------------------------------------------

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(Root, name + ".png"));
        yield return new WaitForSeconds(1.5f);
        W("Screenshot " + name + ".png");
    }

    string WriteCraft()
    {
        AvailablePart ap = new[] { "probeCoreOcto.v2", "probeCoreOcto", "probeCoreSphere.v2", "probeCoreSphere", "probeCoreCube" }
            .Select(PartLoader.getPartInfoByName).FirstOrDefault(p => p != null);
        if (ap == null) { W("No probe core found"); return null; }
        var ship = new ConfigNode();
        ship.AddValue("ship", "TBS Check Probe");
        ship.AddValue("version", "1.12.5");
        ship.AddValue("description", "TBSCheck test craft");
        ship.AddValue("type", "VAB");
        ship.AddValue("size", "1,1,1");
        var n = ship.AddNode("PART");
        n.AddValue("part", ap.name + "_4100000001");
        n.AddValue("partName", "Part");
        n.AddValue("pos", "0,0,0"); n.AddValue("attPos", "0,0,0"); n.AddValue("attPos0", "0,0,0");
        n.AddValue("rot", "0,0,0,1"); n.AddValue("attRot", "0,0,0,1"); n.AddValue("attRot0", "0,0,0,1");
        n.AddValue("mir", "1,1,1"); n.AddValue("symMethod", "Radial"); n.AddValue("autostrutMode", "Off");
        n.AddValue("rigidAttachment", "False"); n.AddValue("istg", "0"); n.AddValue("resPri", "0"); n.AddValue("dstg", "0");
        n.AddValue("sidx", "-1"); n.AddValue("sqor", "-1"); n.AddValue("sepI", "0"); n.AddValue("attm", "0");
        n.AddValue("sameVesselCollision", "False"); n.AddValue("modCost", "0"); n.AddValue("modMass", "0"); n.AddValue("modSize", "0,0,0");
        n.AddNode("EVENTS"); n.AddNode("ACTIONS"); n.AddNode("PARTDATA");
        string dir = Path.Combine(KSPUtil.ApplicationRootPath, "saves/" + SaveName + "/Ships/VAB");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "TBS Check Probe.craft");
        ship.Save(path);
        W("Test craft: " + ap.name);
        return path;
    }
}
