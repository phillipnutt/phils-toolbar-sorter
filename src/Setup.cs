using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PhilsToolbarSorter
{
    // The player's setup: every toolbar button the mod has met, in order, and which
    // ones are hidden. One list for every scene and every save, kept in
    // GameData/PhilsToolbarSorter/PluginData/Toolbar.cfg.
    //
    // The order is KSP's own list order: the first entry sits at the end of the bar
    // nearest the divider's right (the right-hand end at the Space Center, the top in
    // flight). A button met for the first time goes on the end, where KSP puts new
    // buttons anyway, so the bar looks the same until the player moves something.
    public class Setup
    {
        public class Entry
        {
            public string Id, Name;
            public bool Hidden;
            public bool New;    // met after the first session; cleared once the player moves or hides it
        }

        const string NodeName = "TOOLBAR_SORTER";

        // The first time the mod runs, KSP's KSPedia and alarm clock buttons start hidden.
        // (The Definitive Edition pack hid them before this mod; nothing changes on the bar.)
        static readonly string[] HiddenAtFirst = { "KSP:KSPediaApp", "KSP:AlarmClockApp" };

        readonly List<Entry> entries = new List<Entry>();
        readonly Dictionary<string, int> rank = new Dictionary<string, int>();
        readonly bool firstSession;
        bool dirty;

        public Vector2? WindowPosition;
        public int Version { get; private set; }     // goes up with every change

        public static string FilePath =>
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData/PhilsToolbarSorter/PluginData/Toolbar.cfg");

        Setup(bool firstSession) { this.firstSession = firstSession; }

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

            var setup = new Setup(firstSession: node == null);
            if (node == null)
            {
                foreach (string id in HiddenAtFirst)
                    setup.entries.Add(new Entry { Id = id, Name = id.Substring(4), Hidden = true });
                setup.dirty = true;
            }
            else
            {
                foreach (ConfigNode b in node.GetNodes("BUTTON"))
                {
                    string id = b.GetValue("id");
                    if (string.IsNullOrEmpty(id) || setup.entries.Any(e => e.Id == id)) continue;
                    bool hidden = false, isNew = false;
                    b.TryGetValue("hidden", ref hidden);
                    b.TryGetValue("new", ref isNew);
                    setup.entries.Add(new Entry { Id = id, Name = b.GetValue("name") ?? "", Hidden = hidden, New = isNew });
                }
                Vector2 pos = Vector2.zero;
                if (node.TryGetValue("windowPosition", ref pos)) setup.WindowPosition = pos;
            }
            setup.Reindex();
            Debug.Log($"[PhilsToolbarSorter] Setup: {setup.entries.Count} buttons known, {setup.entries.Count(e => e.Hidden)} hidden" +
                      (setup.firstSession ? " (first run)." : "."));
            return setup;
        }

        public Entry Get(string id) => id != null && rank.TryGetValue(id, out int i) ? entries[i] : null;

        public IEnumerable<string> Hidden() => entries.Where(e => e.Hidden).Select(e => e.Id);

        // Where a button goes: lower comes first in KSP's list. Unknown buttons go last.
        public int Rank(string id) => id != null && rank.TryGetValue(id, out int i) ? i : int.MaxValue;

        // A button met for the first time: it goes on the end.
        public Entry Add(string id, string name)
        {
            Entry e = Get(id);
            if (e != null) return e;
            e = new Entry { Id = id, Name = name ?? "", New = !firstSession };
            rank[id] = entries.Count;
            entries.Add(e);
            Changed();
            return e;
        }

        public void SetHidden(string id, bool hidden)
        {
            Entry e = Get(id);
            if (e == null || (e.Hidden == hidden && !e.New)) return;
            e.Hidden = hidden;
            e.New = false;
            Changed();
        }

        // Puts these buttons in this order (KSP's list order), using the places they
        // already hold. Buttons not named keep their places, so arranging one scene's
        // buttons leaves other scenes' buttons where they were.
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
            Changed();
        }

        public void MarkSeen(string id)
        {
            Entry e = Get(id);
            if (e == null || !e.New) return;
            e.New = false;
            Changed();
        }

        public void SetWindowPosition(Vector2 p)
        {
            if (WindowPosition == p) return;
            WindowPosition = p;
            dirty = true;
        }

        void Changed()
        {
            dirty = true;
            Version++;
        }

        void Reindex()
        {
            rank.Clear();
            for (int i = 0; i < entries.Count; i++) rank[entries[i].Id] = i;
        }

        public void SaveIfChanged()
        {
            if (!dirty) return;
            dirty = false;
            var node = new ConfigNode(NodeName);
            if (WindowPosition.HasValue) node.AddValue("windowPosition", ConfigNode.WriteVector(WindowPosition.Value));
            foreach (Entry e in entries)
            {
                ConfigNode b = node.AddNode("BUTTON");
                b.AddValue("id", e.Id);
                b.AddValue("name", Safe(e.Name));
                b.AddValue("hidden", e.Hidden);
                if (e.New) b.AddValue("new", true);
            }
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

        // Names are only there to make the file readable; keep them to one plain line.
        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("{", "(").Replace("}", ")").Replace("//", "/");
            return s.Trim();
        }
    }
}
