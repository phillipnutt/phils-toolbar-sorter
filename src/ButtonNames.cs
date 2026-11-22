using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using KSP.UI.Screens;
using UnityEngine;

namespace PhilsToolbarSorter
{
    // Gives every toolbar button an id that stays the same in every scene and every
    // session, and a name the player recognises. In order:
    //   1. Toolbar Controller's buttons: its own public IsStockButtonManaged (namespace,
    //      id and tooltip), the supported way to ask which mod a button belongs to.
    //   2. KSP's own apps (KSPedia, Contracts, Messages...): the app behind the button.
    //   3. Any other mod's button: the mod's class and method the button calls.
    // Nothing here is guessed from position or order.
    static class ButtonNames
    {
        public static void Identify(ApplicationLauncherButton b, Func<ApplicationLauncherButton, UIApp> appOf, out string id, out string name)
        {
            if (ToolbarControllerKnows(b, out string ns, out string tcId, out string tip))
            {
                id = "TC:" + ns + ":" + tcId;
                // No tooltip: the mod's namespace, without the "_NS" many of them end in.
                string bare = ns ?? tcId ?? "";
                if (bare.EndsWith("_NS", StringComparison.OrdinalIgnoreCase)) bare = bare.Substring(0, bare.Length - 3);
                name = OneLine(tip) ?? Spaced(bare);
                return;
            }

            UIApp app = AppBehind(b) ?? appOf?.Invoke(b);
            if (app != null)
            {
                string type = app.GetType().Name;
                id = "KSP:" + type;
                name = StockNames.TryGetValue(type, out string n) ? n : Spaced(type.EndsWith("App") ? type.Substring(0, type.Length - 3) : type);
                return;
            }

            MethodInfo m = FirstCall(b);
            if (m != null)
            {
                Type owner = m.DeclaringType;
                while (owner.DeclaringType != null && owner.Name.StartsWith("<")) owner = owner.DeclaringType;    // lambdas live in hidden nested classes
                Assembly asm = owner.Assembly;
                id = asm.GetName().Name + ":" + owner.FullName + "." + MethodName(m.Name);
                name = ModName(asm);
                return;
            }

            // A button that calls nothing at all: only its picture is left to go by.
            string tex = b.sprite != null && b.sprite.texture != null ? b.sprite.texture.name : "";
            id = "Icon:" + tex;
            name = string.IsNullOrEmpty(tex) ? "Unnamed button" : Spaced(tex.Split('/').Last());
        }

        // ---- 1. Toolbar Controller ----------------------------------------------------

        static bool tcLooked;
        static MethodInfo tcIsManaged;

        static bool ToolbarControllerKnows(ApplicationLauncherButton b, out string ns, out string id, out string tip)
        {
            ns = id = tip = null;
            if (!tcLooked)
            {
                tcLooked = true;
                Type tc = AssemblyLoader.loadedAssemblies.Select(a => a.assembly.GetType("ToolbarControl_NS.ToolbarControl", false)).FirstOrDefault(t => t != null);
                tcIsManaged = tc?.GetMethod("IsStockButtonManaged", BindingFlags.Public | BindingFlags.Static);
                if (tc != null && tcIsManaged == null)
                    Debug.LogWarning("[PhilsToolbarSorter] Toolbar Controller has no IsStockButtonManaged; its buttons are named by their class instead.");
            }
            if (tcIsManaged == null) return false;
            try
            {
                object[] args = { b, null, null, null };
                if (!(bool)tcIsManaged.Invoke(null, args)) return false;
                ns = args[1] as string;
                id = args[2] as string;
                tip = args[3] as string;
                return !string.IsNullOrEmpty(ns) || !string.IsNullOrEmpty(id);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PhilsToolbarSorter] Toolbar Controller couldn't name a button: {e.InnerException?.Message ?? e.Message}");
                return false;
            }
        }

        // ---- 2. KSP's own apps --------------------------------------------------------

        static readonly Dictionary<string, string> StockNames = new Dictionary<string, string>
        {
            { "KSPediaApp", "KSPedia" },
            { "AlarmClockApp", "Alarm Clock (KSP)" },
            { "ContractsApp", "Contracts" },
            { "MessageSystem", "Messages" },
            { "ResourceDisplay", "Resources" },
            { "EngineersReport", "Engineer's Report" },
            { "CurrencyWidgetsApp", "Funds, Science and Reputation" },
            { "DeltaVApp", "Delta-V" },
            { "ManeuverTool", "Maneuver Planner" },
            { "ActionGroupsApp", "Action Groups" },
            { "CargoApp", "Inventory" },
            { "ConstructionApp", "Construction" },
            { "MissionsApp", "Missions" },
        };

        static UIApp AppBehind(ApplicationLauncherButton b)
        {
            foreach (Delegate d in Calls(b))
                if (d.Target is UIApp app) return app;
            return null;
        }

        // ---- 3. Other mods --------------------------------------------------------------

        // The first thing the button calls that its mod wrote (KSP fills unset calls with
        // its own empty ones).
        static MethodInfo FirstCall(ApplicationLauncherButton b)
        {
            foreach (Delegate d in Calls(b))
            {
                MethodInfo m = d.Method;
                if (m == null || m.DeclaringType == null) continue;
                bool kspFiller = false;
                for (Type t = m.DeclaringType; t != null; t = t.DeclaringType)
                    if (t == typeof(ApplicationLauncherButton)) kspFiller = true;
                if (!kspFiller) return m;
            }
            return null;
        }

        // Everything the button calls. KSP adds a mod's calls after its own empty ones
        // (Delegate.Combine in ApplicationLauncherButton.Setup), so look at all of them.
        static IEnumerable<Delegate> Calls(ApplicationLauncherButton b)
        {
            Delegate[] all = { b.onTrue, b.onFalse, b.onLeftClick, b.onRightClick, b.onHover, b.onHoverOut, b.onEnable, b.onDisable,
                               b.onLeftClickBtn, b.onHoverBtn, b.onHoverOutBtn, b.onHoverBtnActive };
            foreach (Delegate d in all)
            {
                if (d == null) continue;
                foreach (Delegate part in d.GetInvocationList()) yield return part;
            }
        }

        // "<AddButton>b__39_0" (a lambda) → "AddButton": the compiler's numbers change
        // whenever the mod is rebuilt, the method it sits in doesn't.
        static string MethodName(string raw)
        {
            if (!raw.StartsWith("<")) return raw;
            int end = raw.IndexOf('>');
            return end > 1 ? raw.Substring(1, end - 1) : raw;
        }

        // A few mods' files carry names players wouldn't know them by.
        static readonly Dictionary<string, string> ModNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "MechJeb2", "MechJeb" },
            { "Scale", "TweakScale" },
            { "ContractsWindow", "Contracts Window +" },
            { "DynamicBatteryStorage", "Dynamic Battery Storage" },
            { "EVEManager", "EVE" },
            { "DistantObject", "Distant Object Enhancement" },
            { "ParallaxContinued", "Parallax" },
            { "KerbalEngineer", "Kerbal Engineer Redux" },
        };

        static readonly Dictionary<Assembly, string> modNameCache = new Dictionary<Assembly, string>();

        static string ModName(Assembly asm)
        {
            if (modNameCache.TryGetValue(asm, out string cached)) return cached;
            string file = asm.GetName().Name, name;
            if (!ModNames.TryGetValue(file, out name))
            {
                string title = null;
                try { title = (asm.GetCustomAttributes(typeof(AssemblyTitleAttribute), false).FirstOrDefault() as AssemblyTitleAttribute)?.Title; }
                catch { }
                name = !string.IsNullOrEmpty(title) && title.Any(char.IsLetter) && !title.Contains("_") ? title.Trim() : file;
                if (!name.Contains(" ")) name = Spaced(name);
            }
            return modNameCache[asm] = name;
        }

        // "TransferWindowPlanner" → "Transfer Window Planner"; "scatterer" → "Scatterer".
        // Runs of capitals stay together ("SCANsat", "HUDReplacer").
        public static string Spaced(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(s[i - 1])) sb.Append(' ');
                sb.Append(c == '_' ? ' ' : c);
            }
            string r = sb.ToString().Trim();
            return r.Length > 0 ? char.ToUpperInvariant(r[0]) + r.Substring(1) : r;
        }

        static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = string.Join(" ", s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            return s.Length > 0 ? s : null;
        }
    }
}
