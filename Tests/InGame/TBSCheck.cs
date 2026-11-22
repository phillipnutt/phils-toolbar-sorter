// TBSCheck: throwaway in-game check of Phil's Toolbar Sorter. Not a mod; installed only for the test runs.
// Does nothing unless <KSP>/TBSCheck/run.flag exists. Its text is the phase:
//   1: makes the career save "zz_TBSCheck"; at the Space Center hides one button with the window's eye, drags one
//      row with the window, moves two more through the mod's API; then checks the bar in the Space Center, VAB,
//      flight, map view, Tracking Station and Space Center again; saves what it expects; quits.
//   2: loads that save after a relaunch and checks the same at the Space Center and Tracking Station; quits.
// Checks (each scene): every side of the bar is in the setup's order (KSP's list order follows the setup, and the
// buttons on screen follow KSP's list); hidden buttons are off; the moved buttons keep the order set at the start.
// Output: <KSP>/TBSCheck/results.log and *.png (taken with the interface showing).
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

    // ---- Phase 1 -------------------------------------------------------------------------------

    IEnumerator Phase1()
    {
        yield return new WaitForSeconds(3f);
        W("Phase 1 started. Sorter present: " + (S != null) + ", setup file: " + File.Exists(Setup.FilePath));
        GameParameters gp = GameParameters.GetDefaultParameters(Game.Modes.CAREER, GameParameters.Preset.Normal);
        Game g = GamePersistence.CreateNewGame(SaveName, Game.Modes.CAREER, gp, "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.VAB);
        HighLogic.CurrentGame = g;
        HighLogic.SaveFolder = SaveName;
        g.startScene = GameScenes.SPACECENTER;
        g.Start();
        W("Created career save " + SaveName);

        yield return Arrive(GameScenes.SPACECENTER, 14f);
        Report("ksc-before");
        CheckScene("Space Center, before");
        yield return Shot("ksc-before");

        // The window: open it, hide one button with its eye, drag the button nearest the divider to the far end.
        S.Window.Open();
        yield return new WaitForSeconds(1f);
        List<ToolbarSorter.Row> rows = S.Rows(out bool upright);
        List<ToolbarSorter.Row> mods = rows.Where(r => !r.Seen.StockSide).ToList();
        List<ToolbarSorter.Row> ksp = rows.Where(r => r.Seen.StockSide).ToList();
        W("Window rows: " + string.Join(" / ", S.Window.ShownRows().ToArray()));
        ToolbarSorter.Row hide = mods.First(r => !r.Own && !r.Hidden);
        ToolbarSorter.Row move = mods.Last(r => !r.Own && r != hide);
        W($"Hiding with the eye: {hide.Seen.Name} [{hide.Seen.Id}]");
        Check(ClickEye(hide.Seen.Name), "eye button found for " + hide.Seen.Name);
        yield return new WaitForSeconds(0.5f);
        Check(S.Setup.Get(hide.Seen.Id).Hidden, "eye hid " + hide.Seen.Name + " in the setup");
        Check(!hide.Seen.Button.gameObject.activeSelf, "eye switched off " + hide.Seen.Name + " on the bar");
        W($"Dragging in the window: {move.Seen.Name} [{move.Seen.Id}] to before the first mod row");
        Check(DragRow(move.Seen.Name, 0), "drag rows found");
        yield return new WaitForSeconds(0.5f);
        yield return Shot("ksc-window");
        Check(S.Window.ShownRows().Count > 0, "window shows rows: " + S.Window.ShownRows().Count);
        S.CloseFromWindow();
        yield return new WaitForSeconds(0.5f);
        Check(!S.Window.IsOpen, "window closes from its close call");

        // Through the API: this mod's own button to the divider end; KSP's first shown button to its far end.
        rows = S.Rows(out upright);
        var modOrder = rows.Where(r => !r.Seen.StockSide).Select(r => r.Seen).ToList();
        ToolbarSorter.Seen own = modOrder.FirstOrDefault(s => s.Button == S.OwnButton);
        Check(own != null, "own button has a row");
        if (own != null) { modOrder.Remove(own); modOrder.Add(own); S.Reorder(modOrder, upright); }
        var kspOrder = rows.Where(r => r.Seen.StockSide).Select(r => r.Seen).ToList();
        ToolbarSorter.Seen firstKsp = rows.Where(r => r.Seen.StockSide && !r.Hidden).Select(r => r.Seen).FirstOrDefault();
        if (firstKsp != null) { kspOrder.Remove(firstKsp); kspOrder.Add(firstKsp); S.Reorder(kspOrder, upright); W("Moved KSP's " + firstKsp.Name + " to the far end"); }
        if (own != null) S.SetHidden(own, true);
        yield return new WaitForSeconds(1f);
        Check(own != null && !S.Setup.Get(own.Id).Hidden && S.OwnButton.gameObject.activeSelf, "own button can't be hidden");

        // What the window should show now.
        var expMods = mods.Select(r => r.Seen).Where(s => s != move.Seen && s != own).ToList();
        expMods.Insert(0, move.Seen);
        if (own != null) expMods.Add(own);
        var expKsp = ksp.Select(r => r.Seen).ToList();
        if (firstKsp != null) { expKsp.Remove(firstKsp); expKsp.Add(firstKsp); }
        rows = S.Rows(out upright);
        Check(Same(rows.Where(r => !r.Seen.StockSide).Select(r => r.Seen.Id), expMods.Select(s => s.Id)), "mod side in the order set: " + Names(rows, false));
        Check(Same(rows.Where(r => r.Seen.StockSide).Select(r => r.Seen.Id), expKsp.Select(s => s.Id)), "KSP side in the order set: " + Names(rows, true));
        CheckScene("Space Center, after");
        yield return Shot("ksc-after");

        // Remember KSP's list order of every button here, and which are hidden, for every later check.
        expected = new Dictionary<bool, List<string>> { { false, ListOrder(false) }, { true, ListOrder(true) } };
        hiddenIds = S.Setup.Hidden().ToList();
        var save = new ConfigNode("EXPECT");
        foreach (string id in expected[false]) save.AddValue("mod", id);
        foreach (string id in expected[true]) save.AddValue("ksp", id);
        foreach (string id in hiddenIds) save.AddValue("hidden", id);
        save.Save(Path.Combine(Root, "expected.cfg"));
        W("Expected mod list order: " + string.Join(", ", expected[false].ToArray()));
        W("Expected KSP list order: " + string.Join(", ", expected[true].ToArray()));
        W("Hidden: " + string.Join(", ", hiddenIds.ToArray()));
        CheckKept("Space Center");

        GamePersistence.SaveGame("persistent", SaveName, SaveMode.OVERWRITE);
        EditorDriver.StartEditor(EditorFacility.VAB);
        yield return Arrive(GameScenes.EDITOR, 12f);
        Report("vab"); CheckScene("VAB"); CheckKept("VAB");
        yield return Shot("vab");

        // Flight: a probe core alone on the pad.
        string craft = WriteCraft();
        Check(craft != null, "test craft written");
        if (craft != null)
        {
            ConfigNode cn = ConfigNode.Load(craft);
            FlightDriver.StartWithNewLaunch(craft, "Squad/Flags/default", "LaunchPad", VesselCrewManifest.FromConfigNode(cn));
            float t0 = Time.realtimeSinceStartup;
            while (!(HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed)
                   && Time.realtimeSinceStartup - t0 < 180f) yield return null;
            yield return new WaitForSeconds(12f);
            Report("flight"); CheckScene("Flight"); CheckKept("Flight");
            Check(ApplicationLauncher.Instance.IsPositionedAtTop, "flight bar stands upright");
            yield return Shot("flight");
            S.Window.Open();
            yield return new WaitForSeconds(1f);
            W("Flight window rows: " + string.Join(" / ", S.Window.ShownRows().ToArray()));
            yield return Shot("flight-window");
            S.CloseFromWindow();
            MapView.EnterMapView();
            yield return new WaitForSeconds(4f);
            Report("map"); CheckScene("Map view"); CheckKept("Map view");
            yield return Shot("map");
            MapView.ExitMapView();
            yield return new WaitForSeconds(2f);
            GamePersistence.SaveGame("persistent", SaveName, SaveMode.OVERWRITE);
        }

        HighLogic.LoadScene(GameScenes.TRACKSTATION);
        yield return Arrive(GameScenes.TRACKSTATION, 12f);
        Report("trackingstation"); CheckScene("Tracking Station"); CheckKept("Tracking Station");
        yield return Shot("trackingstation");

        HighLogic.LoadScene(GameScenes.SPACECENTER);
        yield return Arrive(GameScenes.SPACECENTER, 12f);
        Report("ksc-again"); CheckScene("Space Center, again"); CheckKept("Space Center, again");
        yield return Shot("ksc-again");
        GamePersistence.SaveGame("persistent", SaveName, SaveMode.OVERWRITE);
    }

    // ---- Phase 2: after a relaunch ----------------------------------------------------------

    IEnumerator Phase2()
    {
        yield return new WaitForSeconds(3f);
        W("Phase 2 started. Setup file: " + File.Exists(Setup.FilePath));
        ConfigNode exp = ConfigNode.Load(Path.Combine(Root, "expected.cfg"));
        expected = new Dictionary<bool, List<string>> { { false, exp.GetValues("mod").ToList() }, { true, exp.GetValues("ksp").ToList() } };
        hiddenIds = exp.GetValues("hidden").ToList();
        foreach (string id in hiddenIds) Check(S.Setup.Get(id) != null && S.Setup.Get(id).Hidden, "still hidden after relaunch: " + id);

        Game g = GamePersistence.LoadGame("persistent", SaveName, true, false);
        Check(g != null, "test save loads");
        if (g == null) yield break;
        HighLogic.CurrentGame = g;
        HighLogic.SaveFolder = SaveName;
        g.Start();
        yield return Arrive(GameScenes.SPACECENTER, 14f);
        Report("relaunch-ksc"); CheckScene("Space Center after relaunch"); CheckKept("Space Center after relaunch");
        yield return Shot("relaunch-ksc");
        HighLogic.LoadScene(GameScenes.TRACKSTATION);
        yield return Arrive(GameScenes.TRACKSTATION, 12f);
        Report("relaunch-ts"); CheckScene("Tracking Station after relaunch"); CheckKept("Tracking Station after relaunch");
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

    Dictionary<bool, List<string>> expected;
    List<string> hiddenIds;

    IEnumerator Arrive(GameScenes scene, float settle)
    {
        float t0 = Time.realtimeSinceStartup;
        while (HighLogic.LoadedScene != scene && Time.realtimeSinceStartup - t0 < 180f) yield return null;
        yield return new WaitForSeconds(settle);    // mods add their buttons a few frames to seconds after the scene starts
        W("=== " + SceneName());
    }

    static string SceneName() => SorterWindow.SceneName();

    // Every side: KSP's list in the setup's order; the buttons showing on screen in KSP's list order; hidden ones off.
    void CheckScene(string where)
    {
        ApplauncherLayout layout = ApplicationLauncher.Instance.CurrentLayout;
        bool upright = ApplicationLauncher.Instance.IsPositionedAtTop;
        foreach (var side in new[] { new { stock = false, list = layout.GetModList() }, new { stock = true, list = layout.GetStockList() } })
        {
            string name = side.stock ? "KSP side" : "mod side";
            var items = Items(side.list);
            var ranks = items.Select(i => S.Setup.Rank(IdOf(i))).ToList();
            bool ordered = true;
            for (int k = 1; k < ranks.Count; k++) if (ranks[k] < ranks[k - 1]) ordered = false;
            Check(ordered, $"{where}: {name} list follows the setup ({items.Count} buttons)");
            var shown = items.Where(i => i.gameObject.activeInHierarchy).ToList();
            // One direction all the way along (along the bar: x when flat, y when upright).
            var steps = new List<float>();
            for (int k = 1; k < shown.Count; k++)
            {
                Vector3 a = shown[k - 1].transform.position, b = shown[k].transform.position;
                steps.Add(upright ? b.y - a.y : b.x - a.x);
            }
            bool monotonic = steps.All(d => d > 0.01f) || steps.All(d => d < -0.01f);
            Check(monotonic, $"{where}: {name} on screen follows the list ({shown.Count} showing, " +
                             (steps.Count == 0 ? "-" : steps[0] < 0 ? (upright ? "downward" : "leftward") : (upright ? "upward" : "rightward")) + ")");
        }
        foreach (ToolbarSorter.Seen s in S.AllSeen().Where(x => x.Button != null && x.Button != S.OwnButton && S.Setup.Get(x.Id) != null && S.Setup.Get(x.Id).Hidden))
            Check(!s.Button.gameObject.activeSelf, $"{where}: hidden {s.Name} is off");
        Check(S.OwnButton != null && S.OwnButton.gameObject.activeSelf, $"{where}: this mod's own button shows");
        // Names come from the mods, not from pictures; this mod's own button has the same id everywhere.
        List<ToolbarSorter.Row> rows = S.Rows(out bool _);
        var byPicture = rows.Where(r => r.Seen.Id.StartsWith("Icon:")).Select(r => r.Seen.Name + " [" + r.Seen.Id + "]").ToList();
        Check(byPicture.Count == 0, $"{where}: every button named by its mod" + (byPicture.Count > 0 ? " (by picture: " + string.Join(", ", byPicture.ToArray()) + ")" : ""));
        string ownId = rows.FirstOrDefault(r => r.Own)?.Seen.Id;
        Check(ownId == "PhilsToolbarSorter:PhilsToolbarSorter.ToolbarSorter.OpenWindow", $"{where}: own button's id is {ownId}");
    }

    // The buttons present here keep the list order set at the start.
    void CheckKept(string where)
    {
        if (expected == null) return;
        foreach (bool stock in new[] { false, true })
        {
            List<string> now = ListOrder(stock);
            var both = now.Where(id => expected[stock].Contains(id)).ToList();
            var want = expected[stock].Where(id => both.Contains(id)).ToList();
            Check(Same(both, want), $"{where}: {(stock ? "KSP" : "mod")} side keeps the set order ({both.Count} of them here)");
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

    static bool Same(IEnumerable<string> a, IEnumerable<string> b) => a.SequenceEqual(b);
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
