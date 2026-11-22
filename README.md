# Phil's Toolbar Sorter

> **This mod was written by an AI.** Every line of code and these docs were
> written by Claude, Anthropic's AI, at Phil Nutt's direction. Phil designed it
> and play-tests it, but he isn't a programmer and no human has reviewed the
> code.

A small Kerbal Space Program 1.12 mod for big mod lists: choose which buttons
show on KSP's own toolbar, in what order, and which go together in folders. No
second toolbar and no bar that hides itself. It's one of Phil's mods, alongside
[Phil's Part Hider](https://github.com/phillipnutt/phils-part-hider), and shares
its look.

- **The window:** the mod's toolbar button (orange up-and-down arrows, with the
  small rocket P all of Phil's mods carry) opens a list of the buttons on the
  bar, in the bar's own order: left to right at the Space Center, VAB, SPH and
  Tracking Station, top to bottom in flight. Each row shows the button's icon
  and its mod's name.
- **Move a button:** drag its row up or down. The bar changes as you drop it.
- **Hide a button:** click its eye. It leaves the bar, but its row stays in
  the list, dimmed, in its place. Click the eye again to bring it back where it
  was.
- **Folders:** **New folder** puts a folder button on the bar. Drag rows onto
  the folder to put their buttons in it; the arrow on a row in a folder takes it
  back out (or drag it out). The pencil on a folder's row sets its name, its
  color, whether it opens when you point at it or when you click it, and
  deletes it. Opened, a folder shows its buttons in a strip beside it: the real
  buttons, so they work exactly as they do on the bar. A folder can hold buttons
  from either side of the divider.
- **Find a button:** point at a row and its button lights up on the bar (or its
  folder, if it's in one).
- **One setup per scene:** the Space Center, the VAB and SPH, flight and map
  view, and the Tracking Station each keep their own order, hidden buttons and
  folders. What you change in one scene changes only that scene. The window's
  header says which one you're arranging. The same for every save, and kept
  after a restart.

Things to know:

- KSP keeps mod buttons left of the dashed divider (above it in flight) and its
  own on the other side. Some mods put theirs on KSP's side. You can order each
  side however you like, but a button can't cross the divider (a folder can hold
  buttons from both sides).
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
For each scene it lists every toolbar button the mod has met there, in KSP's
list order (the first one sits nearest the right-hand end of each side at the
Space Center), with its name and whether it's hidden, and that scene's folders.
To start over, delete the file with KSP closed.

Each scene's first setup hides KSP's own KSPedia and alarm clock buttons; click
their eyes to bring them back. A setup from version 0.1.0 (one for every scene)
is copied into each scene the first time this version loads; the old file is
kept as `Toolbar.cfg.before-per-scene`.

## How it works

KSP draws each side of the toolbar from a list it never re-sorts, and decides
whether each button shows in one place (`ApplicationLauncher.ShouldBeVisible`).
Every frame, if a side isn't in your order, the mod puts it back in order with
KSP's own list calls. A Harmony postfix on `ShouldBeVisible` adds "and you
haven't hidden it (or put it in a folder)", so KSP hides the button its own way
and tells its mod.

A folder is a toolbar button of the mod's own. Opening it moves the real buttons
in it into a strip beside it; KSP keeps them in its lists the whole time, so the
mods and KSP see their buttons as always. Closing it puts them back in their
places, off the bar.

Buttons are told apart the same way every session: Toolbar Controller's buttons
by its own `IsStockButtonManaged` (namespace and id), KSP's apps by the app,
any other mod's button by the class and method it calls, and a button that calls
nothing of its mod's (Kerbalism's vessel list) by the mod method that put it on
the bar.

Each row's picture is a copy of the button's own, kept in step: KSP's own apps
draw an animated sprite there, and the row shows the frame it's on.

## Building

MSBuild on `PhilsToolbarSorter.csproj`. It expects KSP at the default Steam
location; pass `/p:KspDir="..."` for another install, and `/p:InstallToGame=true`
to copy the build into the game (only with KSP closed). Everything in the window
is drawn in code. The toolbar icon is `GameData/PhilsToolbarSorter/Textures/ToolbarIcon.png`,
made from `art/ToolbarIcon.svg`.

## Testing

`Tests/InGame` holds an unattended in-game check. `Run-Check.ps1` installs the
mod, starts KSP twice and drives it in a throwaway career save, starting from
the install's own setup (put back exactly afterwards). Each scene gets its own
changes: at the Space Center the window's eye and a drag, in the VAB a hidden
button, a move and a folder (made, named, colored, filled by dragging, opened on
hover and on click, a button taken out), in flight a hidden button and a move,
and the Tracking Station left alone. In every scene it checks that the bar is in
that scene's order (by where the buttons are on screen), that what's hidden here
is off and everything else is on, that other scenes' changes don't show, and
that every button is named by its mod. After a relaunch it checks every scene
again, opens the folder and deletes it. It writes PASS/FAIL lines and
screenshots to `Tests/InGame/out`, then removes its save.

## License

MIT. See [LICENSE](LICENSE).
