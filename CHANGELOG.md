# Changelog

Releases and their zips are on this repository's Releases page. All of them so far are pre-releases.

## 0.1.30 (2026-10-17)

- The Nuclear Triad mark, small and faint, in the main menu's bottom-right corner. It sits under the settings screen and popups and takes no clicks. `Window.MenuLogo`, an advanced setting, turns it off.
- The plugin's source is public, in this repository.
- A hand-edited `PixelRect`, `ExplicitRect` or `OriginalScreen` that isn't valid now counts as blank, with one warning in the log, instead of stopping the plugin.

## 0.1.29 (2026-10-17)

- Each screen's `WidthMm` starts at the picture width its monitor reports to Windows, so a new install is close before anyone measures. Up to 0.1.28 the defaults were the development rig's panels (392, 697 and 392 mm), which a first install on three 797 mm ultrawides got as values.
- The tuner's first step is now the three screen widths. It lists each screen's width beside what its monitor reports, with a Use it button, and draws a yellow bar of a stated length on every screen, so a tape measure checks the width without finding edge pixels.

## 0.1.28 (2026-10-17)

- The plugin turns on BepInEx's `[Chainloader] HideManagerGameObject` itself, in time for the first start, so the install guide no longer has a file to edit. Without it, Nuclear Option removes BepInEx's manager object and no plugin runs. `Setup.FixBepInExManager` turns this off.
- A blank `Monitor`, now the default, follows Windows' display arrangement: the left, center and right screens are taken from it, however Windows numbers them.
- The tuner names the Windows display each screen uses, and warns when the three are out of left-to-right order or two share a display.

## 0.1.27 (2026-10-17)

The first public release.

- One camera per screen, each projected for where its screen sits in front of the eye (Kooima's generalized perspective projection), with the game's own camera keeping the center screen.
- The game's window spans the screens, with black curtains over the parts it leaves uncovered.
- An on-screen tuner for the eye height and each side screen's vertical position, gap and angle, with lines fixed to the view; F1 opens on a Start here box.
- Menus and the HUD's corner panels on the center screen; HUD markers on their targets on the side screens; the flight HUD placed after the camera moves and bent onto the side screens.
- Trees and grass culled against every screen.
- The control channel is off by default.

Earlier builds, 0.1.1 to 0.1.26, were development builds and weren't released.
