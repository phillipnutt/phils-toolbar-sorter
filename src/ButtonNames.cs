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
    //   4. A mod's button that calls nothing of the mod's (Kerbalism's vessel list watches
    //      the pointer itself): the mod's method that put it on the bar.
    // Nothing here is guessed from position or order.
    static class ButtonNames
    {
        // Version 0.2.0 named a button that calls nothing of its mod's by KSP's own call on it;
        // that old id, so a setup made then keeps the button's place.
        public static string LegacyId(ApplicationLauncherButton b)
        {
            MethodInfo m = FirstCall(b, skipKsp: false);
            return m == null || m.DeclaringType.Assembly != KspAssembly ? null : IdOf(m);
        }

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

            MethodInfo m = FirstCall(b, skipKsp: true);
            if (m != null)
            {
                id = IdOf(m);
                name = ModName(Owner(m).Assembly);
                return;
            }

            if (adders.TryGetValue(b, out MethodBase added) && added != null)
            {
                id = Owner(added).Assembly.GetName().Name + ":added by " + Owner(added).FullName + "." + MethodName(added.Name);
                name = ModName(Owner(added).Assembly);
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

        static readonly Assembly KspAssembly = typeof(ApplicationLauncherButton).Assembly;

        // The first thing the button calls that its mod wrote. KSP fills unset calls with its
        // own empty ones, and adds calls of its own to buttons on its side (the toolbar's
        // "one open at a time" handling); with skipKsp, none of KSP's count.
        static MethodInfo FirstCall(ApplicationLauncherButton b, bool skipKsp)
        {
            foreach (Delegate d in Calls(b))
            {
                MethodInfo m = d.Method;
                if (m == null || m.DeclaringType == null) continue;
                bool kspFiller = false;
                for (Type t = m.DeclaringType; t != null; t = t.DeclaringType)
                    if (t == typeof(ApplicationLauncherButton)) kspFiller = true;
                if (kspFiller || skipKsp && m.DeclaringType.Assembly == KspAssembly) continue;
                return m;
            }
            return null;
        }

        static Type Owner(MethodBase m)
        {
            Type owner = m.DeclaringType;
            while (owner.DeclaringType != null && owner.Name.StartsWith("<")) owner = owner.DeclaringType;    // lambdas live in hidden nested classes
            return owner;
        }

        static string IdOf(MethodBase m) => Owner(m).Assembly.GetName().Name + ":" + Owner(m).FullName + "." + MethodName(m.Name);

        // ---- 4. The mod that put the button on the bar ----------------------------------------

        static readonly Dictionary<ApplicationLauncherButton, MethodBase> adders = new Dictionary<ApplicationLauncherButton, MethodBase>();

        // Called as KSP adds a button (a Harmony postfix on ApplicationLauncher.AddApplication /
        // AddModApplication): the first method up the call stack that belongs to a mod.
        public static void NoteAdder(ApplicationLauncherButton b)
        {
            if (b == null) return;
            try
            {
                Assembly own = typeof(ButtonNames).Assembly, harmony = typeof(HarmonyLib.Harmony).Assembly, core = typeof(object).Assembly;
                foreach (System.Diagnostics.StackFrame f in new System.Diagnostics.StackTrace(1, false).GetFrames() ?? new System.Diagnostics.StackFrame[0])
                {
                    MethodBase m = f.GetMethod();
                    Type t = m?.DeclaringType;
                    if (t == null) continue;
                    Assembly a = t.Assembly;
                    if (a == KspAssembly || a == own || a == harmony || a == core || a.GetName().Name.StartsWith("UnityEngine")) continue;
                    adders[b] = m;
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PhilsToolbarSorter] Couldn't tell which mod added a button: {e.Message}");
            }
        }

        // Buttons gone with their scene.
        public static void Forget()
        {
            foreach (ApplicationLauncherButton gone in adders.Keys.Where(k => k == null).ToList()) adders.Remove(gone);
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
