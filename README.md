# Phil's Toolbar Sorter

> **This mod was written by an AI.** Every line of code and these docs were
> written by Claude, Anthropic's AI, at Phil Nutt's direction. Phil designed it
> and play-tests it, but he isn't a programmer and no human has reviewed the
> code.

A small Kerbal Space Program 1.12 mod for big mod lists: choose which buttons
show on KSP's own toolbar, and in what order. No second toolbar, no folders, no
bar that hides itself. It's one of Phil's mods, alongside
[Phil's Part Hider](https://github.com/phillipnutt/phils-part-hider), and shares
its look.

- **The window:** the mod's toolbar button (three squares, the middle one lifted
  in orange) opens a list of the buttons on the bar, in the bar's own order: left
  to right at the Space Center, VAB, SPH and Tracking Station, top to bottom in
  flight. Each row shows the button's icon and its mod's name.
- **Move a button:** drag its row up or down. The bar changes as you drop it.
- **Hide a button:** click its eye. It leaves the bar, but its row stays in
  the list, dimmed, in its place. Click the eye again to bring it back where it
  was.
- **Find a button:** point at a row and its button lights up on the bar.
- **One setup everywhere:** what you hide or move stays that way in every scene
  (Space Center, VAB and SPH, flight and map view, Tracking Station), in every
  save, and after a restart.

Things to know:

- KSP keeps mod buttons left of the dashed divider (above it in flight) and its
  own on the other side. Some mods put theirs on KSP's side. You can order each
  side however you like, but a button can't cross the divider.
- Each scene's list only shows the buttons that belong in that scene.
- A mod you add later puts its button at the far end, as KSP always does, and
  the list marks it **New** until you move it or hide it.
- The mod's own button always stays on the bar, so you can always get back in.
  You can move it like any other.
- Hiding only takes a button off the bar. Its mod keeps running.

## Install

Copy `GameData/PhilsToolbarSorter` from the release zip into your KSP `GameData`
folder. Needs [Harmony 2](https://github.com/KSPModdingLibs/HarmonyKSP) (on
CKAN as `Harmony2`, which most mod lists already have).

## The setup file

One file for every save: `GameData/PhilsToolbarSorter/PluginData/Toolbar.cfg`.
It lists every toolbar button the mod has met, in KSP's list order (the first
one sits nearest the right-hand end of each side at the Space Center), with its
name and whether it's hidden. To start over, delete the file with KSP closed.

The first time it runs, the mod hides KSP's own KSPedia and alarm clock
buttons; click their eyes to bring them back.

## How it works

KSP draws each side of the toolbar from a list it never re-sorts, and decides
whether each button shows in one place (`ApplicationLauncher.ShouldBeVisible`).
Every frame, if a side isn't in your order, the mod puts it back in order with
KSP's own list calls. A Harmony postfix on `ShouldBeVisible` adds "and you
haven't hidden it", so KSP hides the button its own way and tells its mod.

Buttons are told apart the same way every session: Toolbar Controller's buttons
by its own `IsStockButtonManaged` (namespace and id), KSP's apps by the app,
and any other mod's button by the class and method it calls.

## Building

MSBuild on `PhilsToolbarSorter.csproj`. It expects KSP at the default Steam
location; pass `/p:KspDir="..."` for another install, and `/p:InstallToGame=true`
to copy the build into the game (only with KSP closed). Everything in the window,
the toolbar icon included, is drawn in code.

## Testing

`Tests/InGame` holds an unattended in-game check. `Run-Check.ps1` installs the
mod, starts KSP twice and drives it: a throwaway career save, the window's eye
and a drag at the Space Center, then the VAB, flight, map view, Tracking Station
and, after a relaunch, the Space Center and Tracking Station again. In each it
checks that the bar is in the setup's order (by where the buttons are on
screen), that hidden buttons are off and that every button is named by its mod.
It writes PASS/FAIL lines and screenshots to `Tests/InGame/out`, then removes
its save and setup.

## Known gaps

- KSP's own Contracts and Messages buttons show a placeholder gear in the window
  instead of their icons.
- Kerbalism's vessel-list button is named "Assembly-CSharp": Kerbalism gives it
  no calls of its own, so only KSP's are there to name it by.

## License

MIT. See [LICENSE](LICENSE).
