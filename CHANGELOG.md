# Changelog

## 0.2.0 (not released yet)

- **One setup per scene:** the Space Center, the VAB and SPH, flight and map
  view, and the Tracking Station each keep their own order and hidden buttons.
  The window's header names the scene you're arranging. A 0.1.0 setup is copied
  into each scene the first time this version loads (the old file is kept).
- **Folders:** New folder puts a folder button on the bar. Drag buttons onto it
  to put them in, take them out with the arrow (or drag them out). Each folder
  has a name, a color, and opens on hover or on click; it can be deleted (its
  buttons go back on the bar). Opened, it shows the real buttons in a strip
  beside it. Folders can hold buttons from either side of the divider, and are
  per scene too.
- Pointing at a button that's in a folder lights up its folder on the bar.

## 0.1.0 (not released)

First version.

- A window from the mod's own toolbar button lists the toolbar's buttons in the
  bar's order, each with its icon and name.
- Drag a row to move its button; click the eye to hide or show it. Pointing at a
  row lights up its button on the bar.
- One setup for every scene and every save, kept after a restart, in
  `PluginData/Toolbar.cfg`.
- KSP's KSPedia and alarm clock buttons start hidden.
- The toolbar button uses the icon look Phil's mods share: orange up-and-down
  arrows with a small rocket P.
