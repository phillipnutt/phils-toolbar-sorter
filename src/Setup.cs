using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PhilsToolbarSorter
{
    // The player's setup, one per scene: the Space Center, the VAB and SPH (one), flight
    // and map view (one), and the Tracking Station. Each holds every toolbar button met
    // there, in order, and which are hidden. The same for every save, kept in
    // GameData/PhilsToolbarSorter/PluginData/Toolbar.cfg.
    //
    // Version 0.1.0 kept one setup for every scene. Such a file is copied into each scene
    // the first time it loads (and the old file kept beside it), so nothing set up is lost.
    public class Setup
    {
        public const string SpaceCenter = "SpaceCenter", Editors = "Editors", Flight = "Flight", TrackingStation = "TrackingStation";
        public static readonly string[] Scenes = { SpaceCenter, Editors, Flight, TrackingStation };

        const string NodeName = "TOOLBAR_SORTER";

        readonly Dictionary<string, SceneSetup> scenes = new Dictionary<string, SceneSetup>();
        bool dirty;

        public Vector2? WindowPosition;

        public static string FilePath =>
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData/PhilsToolbarSorter/PluginData/Toolbar.cfg");

        // Which setup a scene uses; none at the main menu.
        public static string SceneOf(GameScenes scene)
        {
            switch (scene)
            {
                case GameScenes.SPACECENTER: return SpaceCenter;
                case GameScenes.EDITOR: return Editors;
                case GameScenes.FLIGHT: return Flight;
                case GameScenes.TRACKSTATION: return TrackingStation;
                default: return null;
            }
        }

        // What the window calls the setup it is showing.
        public static string DisplayName(string scene)
        {
            switch (scene)
            {
                case SpaceCenter: return "Space Center";
                case Editors: return "VAB and SPH";
                case Flight: return "Flight and map view";
                case TrackingStation: return "Tracking Station";
                default: return scene ?? "";
            }
        }

        public SceneSetup For(string scene) => scene != null && scenes.TryGetValue(scene, out SceneSetup s) ? s : null;

        public static Setup Load()
        {
            string path = FilePath;
            ConfigNode root = File.Exists(path) ? ConfigNode.Load(path) : null;
            ConfigNode node = root?.GetNode(NodeName);
            if (File.Exists(path) && node == null)
            {
                // Unreadable: keep it aside rather than overwrite it, and start again.
                string aside = path + ".unreadable-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
                try { File.Move(path, aside); } catch (Exception e) { Debug.LogWarning($"[PhilsToolbarSorter] Couldn't move the unreadable setup aside: {e.Message}"); }
                Debug.LogWarning($"[PhilsToolbarSorter] {path} couldn't be read; kept as {Path.GetFileName(aside)} and started a new setup.");
            }

            var setup = new Setup();
            string how;
            if (node == null)
            {
                how = "first run";
            }
            else if (node.HasNode("SCENE"))
            {
                foreach (ConfigNode sn in node.GetNodes("SCENE"))
                {
                    string name = sn.GetValue("name");
                    if (Scenes.Contains(name) && !setup.scenes.ContainsKey(name))
                        setup.scenes[name] = SceneSetup.From(name, sn.GetNodes("BUTTON"), startedFresh: false);
                }
                how = "per scene";
            }
            else
            {
                // 0.1.0's single setup: every scene starts as a copy of it.
                foreach (string name in Scenes)
                    setup.scenes[name] = SceneSetup.From(name, node.GetNodes("BUTTON"), startedFresh: false);
                try { File.Copy(path, path + ".before-per-scene", true); } catch { }
                setup.dirty = true;
                how = "copied the one setup from before into each scene";
            }
            foreach (string name in Scenes)
                if (!setup.scenes.ContainsKey(name))
                {
                    setup.scenes[name] = SceneSetup.Fresh(name);
                    setup.dirty = true;
                }
            Vector2 pos = Vector2.zero;
            if (node != null && node.TryGetValue("windowPosition", ref pos)) setup.WindowPosition = pos;

            Debug.Log($"[PhilsToolbarSorter] Setup ({how}): " +
                      string.Join(", ", Scenes.Select(n => $"{DisplayName(n)} {setup.scenes[n].Count} buttons, {setup.scenes[n].Hidden().Count()} hidden").ToArray()) + ".");
            return setup;
        }

        public void SetWindowPosition(Vector2 p)
        {
            if (WindowPosition == p) return;
            WindowPosition = p;
            dirty = true;
        }

        public void SaveIfChanged()
        {
            if (!dirty && !scenes.Values.Any(s => s.Dirty)) return;
            dirty = false;
            var node = new ConfigNode(NodeName);
            if (WindowPosition.HasValue) node.AddValue("windowPosition", ConfigNode.WriteVector(WindowPosition.Value));
            foreach (string name in Scenes)
                scenes[name].WriteTo(node.AddNode("SCENE"));
            var root = new ConfigNode();
            root.AddNode(node);
            string path = FilePath, temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                root.Save(temp);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PhilsToolbarSorter] Couldn't save the setup to {path}: {e.Message}");
            }
        }
    }

    // One scene's setup: its toolbar buttons in KSP's list order (the first sits at the
    // end of each side nearest the divider's right: the right-hand end of a flat bar, the
    // top of the upright one in flight), and which are hidden. A button met here for the
    // first time goes on the end, where KSP puts new buttons anyway.
    public class SceneSetup
    {
        public class Entry
        {
            public string Id, Name;
            public bool Hidden;
            public bool New;    // met after this scene's setup began; cleared once the player moves or hides it
        }

        // A scene's first setup hides KSP's KSPedia and alarm clock buttons.
        static readonly string[] HiddenAtFirst = { "KSP:KSPediaApp", "KSP:AlarmClockApp" };

        readonly List<Entry> entries = new List<Entry>();
        readonly Dictionary<string, int> rank = new Dictionary<string, int>();
        bool startedFresh;      // made this session: buttons met now aren't "new"

        public string Name { get; private set; }
        internal bool Dirty;

        public int Count => entries.Count;

        internal static SceneSetup Fresh(string name)
        {
            var s = new SceneSetup { Name = name, startedFresh = true, Dirty = true };
            foreach (string id in HiddenAtFirst) s.entries.Add(new Entry { Id = id, Name = id.Substring(4), Hidden = true });
            s.Reindex();
            return s;
        }

        internal static SceneSetup From(string name, IEnumerable<ConfigNode> buttons, bool startedFresh)
        {
            var s = new SceneSetup { Name = name, startedFresh = startedFresh };
            foreach (ConfigNode b in buttons)
            {
                string id = b.GetValue("id");
                if (string.IsNullOrEmpty(id) || s.entries.Any(e => e.Id == id)) continue;
                bool hidden = false, isNew = false;
                b.TryGetValue("hidden", ref hidden);
                b.TryGetValue("new", ref isNew);
                s.entries.Add(new Entry { Id = id, Name = b.GetValue("name") ?? "", Hidden = hidden, New = isNew });
            }
            s.Reindex();
            return s;
        }

        internal void WriteTo(ConfigNode node)
        {
            node.AddValue("name", Name);
            foreach (Entry e in entries)
            {
                ConfigNode b = node.AddNode("BUTTON");
                b.AddValue("id", e.Id);
                b.AddValue("name", Safe(e.Name));
                b.AddValue("hidden", e.Hidden);
                if (e.New) b.AddValue("new", true);
            }
            Dirty = false;
        }

        public Entry Get(string id) => id != null && rank.TryGetValue(id, out int i) ? entries[i] : null;

        public IEnumerable<string> Hidden() => entries.Where(e => e.Hidden).Select(e => e.Id);

        public IEnumerable<string> Order() => entries.Select(e => e.Id);

        // Where a button goes: lower comes first in KSP's list. Unknown buttons go last.
        public int Rank(string id) => id != null && rank.TryGetValue(id, out int i) ? i : int.MaxValue;

        // A button met here for the first time: it goes on the end.
        public Entry Add(string id, string name)
        {
            Entry e = Get(id);
            if (e != null) return e;
            e = new Entry { Id = id, Name = name ?? "", New = !startedFresh };
            rank[id] = entries.Count;
            entries.Add(e);
            Dirty = true;
            return e;
        }

        public void SetHidden(string id, bool hidden)
        {
            Entry e = Get(id);
            if (e == null || (e.Hidden == hidden && !e.New)) return;
            e.Hidden = hidden;
            e.New = false;
            Dirty = true;
        }

        // Puts these buttons in this order (KSP's list order), using the places they
        // already hold. Buttons not named keep their places.
        public void Reorder(IList<string> ids)
        {
            var known = ids.Where(id => id != null && rank.ContainsKey(id)).Distinct().ToList();
            var slots = known.Select(id => rank[id]).OrderBy(i => i).ToList();
            var moving = known.Select(id => entries[rank[id]]).ToList();
            bool changed = false;
            for (int k = 0; k < slots.Count; k++)
            {
                if (entries[slots[k]] != moving[k]) changed = true;
                entries[slots[k]] = moving[k];
            }
            if (!changed) return;
            Reindex();
            Dirty = true;
        }

        public void MarkSeen(string id)
        {
            Entry e = Get(id);
            if (e == null || !e.New) return;
            e.New = false;
            Dirty = true;
        }

        void Reindex()
        {
            rank.Clear();
            for (int i = 0; i < entries.Count; i++) rank[entries[i].Id] = i;
        }

        // Names are only there to make the file readable; keep them to one plain line.
        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("{", "(").Replace("}", ")").Replace("//", "/");
            return s.Trim();
        }
    }
}
