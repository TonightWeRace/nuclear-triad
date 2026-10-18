# Nuclear Triad

Nuclear Triad is a BepInEx plugin for Nuclear Option that gives each screen of a triple-screen rig its own view, projected for where that screen sits in front of you, instead of one picture stretched across all three. The horizon runs unbroken across both gaps, level or banked.

**Version 0.1.29** · Direct3D 11 · tested on Nuclear Option 0.34.1 (Steam build 24724372) with BepInEx 5.4.23.5 and ConfigurationManager 19.0

## Features

- One view per screen, worked out from your rig: each screen's width, the gap at each seam, and how far each side screen turns toward you. Side screens can differ from the center one in size, resolution and orientation.
- An on-screen tuner that sets the rig up by eye, parked in the cockpit.
- Menus and the HUD's corner panels stay on the center screen. HUD markers sit on their targets on the side screens, and the flight HUD follows onto them.
- Trees and grass on every screen.
- Client-only: no network code, and nothing that changes gameplay or what other players see. Right Ctrl+T switches it off, back to the game's own view, and on again.

## Requirements

- Nuclear Option on Steam, on Windows, started with Direct3D 11.
- Three screens, each its own display in Windows: Settings > System > Display shows three.
- BepInEx 5.4.23.5 and ConfigurationManager 19.0. They aren't part of this download; the steps below install them.

## Install

Close Nuclear Option first. The game folder is the one with `NuclearOption.exe`: in Steam, right-click Nuclear Option, then Manage > Browse local files. To extract a zip into it, right-click the zip, choose Extract All, and pick the game folder as the destination.

1. **BepInEx 5.4.23.5.** Download `BepInEx_win_x64_5.4.23.5.zip` from [BepInEx's 5.4.23.5 release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) and extract it into the game folder. `winhttp.dll` and a `BepInEx` folder now sit beside `NuclearOption.exe`.
2. **ConfigurationManager 19.0**, the F1 settings window. Download `BepInEx.ConfigurationManager_BepInEx5_v19.0.zip` from [its 19.0 release](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases/tag/v19.0) and extract it into the game folder.
3. **Nuclear Triad.** Download `NuclearTriad-0.1.29.zip` from [Nuclear Triad's releases](https://github.com/TonightWeRace/nuclear-triad/releases) and extract it into the game folder. It adds `BepInEx\plugins\NuclearTriad\`.
4. **Start the game** (see below). The window spans all three screens, with the menus on the center one.

Nuclear Option needs one BepInEx setting that a new BepInEx leaves off, `[Chainloader] HideManagerGameObject`: without it, the game removes BepInEx's manager as it starts, and no plugin runs. Nuclear Triad turns it on in `BepInEx\config\BepInEx.cfg` as it first loads, in time for that first start, so there's nothing to edit. It stays on for every plugin, even after Nuclear Triad is removed.

To check a download, run `Get-FileHash <file>` in PowerShell. It should print these SHA-256 hashes. Nuclear Triad's own is in its release notes.

| File | SHA-256 |
| --- | --- |
| `BepInEx_win_x64_5.4.23.5.zip` | `82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4` |
| `BepInEx.ConfigurationManager_BepInEx5_v19.0.zip` | `eed83f6e6accbdd0c3771f3061a6ae05b979b9b9b8a00dead69aabc1bf5fe950` |

### Starting the game

In Steam, press Play. When Steam asks which version to start, pick **Nuclear Option (DirectX 11)**. Nuclear Triad is tested only with Direct3D 11.

## Set up your rig

Do this once per rig. You need a tape measure.

1. **Measure each side screen's gap**, in millimetres: straight across, from its nearest lit pixel to the center screen's nearest lit pixel. It takes in both frames and any space between them.
2. **Check the screens in F1.** In the game's main menu, press F1 and open Nuclear Triad's settings. Under Geometry.Left, Geometry.Center and Geometry.Right:
   - `Monitor`: leave it blank. Nuclear Triad then takes the three screens from Windows' display arrangement (Settings > System > Display), left to right, however Windows numbers them. If Windows has more than three displays side by side, set each screen's display here, such as DISPLAY2: the tuner's amber note lists them left to right.
   - `WidthMm`: starts at the picture width that screen's monitor reports to Windows. The tuner's first step checks it.
   - `GapMm`, on the side screens: the gap you measured. The tuner fine-tunes it.

   Leave `AngleDeg` and `VerticalOffsetMm` to the tuner.
3. **Tune on screen.** Start a mission and stay parked. Press Right Ctrl+U, or the Open the tuner button at the top of F1. The tuner's window opens on the center screen, with lines drawn over your view, and names the Windows display each screen uses. There's a yellow line at eye level with a ring straight ahead, and cyan and green lines through each gap. Sit as you fly, look straight ahead, and follow its five steps: screen widths, eye height, vertical position, gap and angle. Each step says what to look at and which button to press:
   - **Screen widths** come first, because every other step is worked out from them. Each screen's width is the width of its picture, from the first lit pixel to the last, not the frame. Measure it, or measure the yellow bar the tuner draws on each screen: if your tape reads longer than the bar says, press +; shorter, press −. Beside each width is what the monitor reports to Windows, which is usually right to a few millimetres; Use it takes that.
   - The − and + buttons nudge the value, in small or large steps.
   - "Move both side screens together" changes both sides at once. Untick it to set them one at a time.
   - Changes apply and save as you go. Revert puts back the values from when you opened the tuner, and Close, or Done on the last step, closes it.
4. **Check it in flight.** Right Ctrl+G shows the sky grid: lines fixed in the sky that must run straight across both gaps, level and in a bank.

Optionally, size the flight HUD for the side screens. When the nose is on a side screen, the flight HUD is drawn there as big as it looks on the center one. In F1, tick Advanced settings. Set `Geometry.EyeDistanceMm` to your eyes' distance from the center screen, and each side screen's `PhysicalAngleDeg` to its real angle. The tuner's `AngleDeg` settles well below the real angle.

## Using it

| Key | What it does |
| --- | --- |
| Right Ctrl+T | Switches the rig off, back to the game's own view, and on again, until the game closes |
| Right Ctrl+U | Opens or closes the tuner |
| Right Ctrl+G | Shows or hides the sky grid |
| F1 | Opens the settings |

Every setting applies as soon as you change it in F1, and is saved in `BepInEx\config\nuclear-triad.cfg`. F1 shows the settings a rig needs; the rest are behind its Advanced settings box.

One advanced setting is for development only: `Dev.CommandChannel`, off by default. While it's on, the plugin runs commands from a file in `BepInEx\config` once a second, so any program on the PC could change its settings. Leave it off.

## Uninstall

Close the game, then delete `BepInEx\plugins\NuclearTriad\` and `BepInEx\config\nuclear-triad.cfg`. Each time the game quits, the plugin writes back the game's own screen setting from before it spanned the window, so the game starts as it did before. If it ever starts stretched across the screens, set the display mode again in the game's graphics settings. BepInEx's `HideManagerGameObject` stays on, as every plugin needs it in Nuclear Option.

To remove BepInEx and ConfigurationManager too, delete `BepInEx\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt` from the game folder.

## Troubleshooting

- **Nothing changes, and F1 doesn't open.** Check that `winhttp.dll` sits beside `NuclearOption.exe`, and that `BepInEx\LogOutput.log` has a line `Loading [Nuclear Triad 0.1.29]`. If the log says `HideManagerGameObject` is still off, set `HideManagerGameObject = true` under `[Chainloader]` in `BepInEx\config\BepInEx.cfg` yourself, with the game closed. Nuclear Triad leaves it off only when its advanced `Setup.FixBepInExManager` setting is off.
- **F1 opens, but the game stays on one screen, or shows one picture stretched across all three.** Open the tuner (Right Ctrl+U). An amber note under its heading says why the rig isn't up and what to do. For example, there's no mission yet, or a `Monitor` matches no display.
- **Views on the wrong screens.** Open the tuner (Right Ctrl+U). Its Screens line names the Windows display each screen uses, and an amber note says when they're out of order in Windows' arrangement. Clear the three `Monitor` settings in F1, and Nuclear Triad follows Windows' arrangement. If they're blank already, Windows' arrangement doesn't match your screens: in Settings > System > Display, press Identify, then drag the displays to sit as the screens do.
- **A notice says Nuclear Triad can't draw these geometry settings.** A setting describes a rig it can't draw, such as an eye position past a gap. It keeps the last layout that worked until you change the setting.
- **HUD pieces split for a moment as they cross a gap.** This happens on rigs whose side screens differ from the center one, when Windows' display arrangement doesn't match where the screens physically sit. The tuner's vertical position step says how many pixels to move each side screen in Windows' display settings.
- **The game hangs, and parts of the screens stay black.** Those are Nuclear Triad's curtains, which black out the parts of the screens the game window doesn't cover. End Nuclear Option in Task Manager, and they go with it.
- **After a game update,** `BepInEx\LogOutput.log` warns that the game build isn't the one Nuclear Triad was tested on. If something misbehaves, Right Ctrl+T switches the rig off. Deleting `BepInEx\cache\harmony_interop_cache.dat` can also help.

## Known limitations

- Direct3D 11 only. Direct3D 12 has had only a first look, and Vulkan is untested.
- Three flat screens, each a display of its own in Windows. A single wide display, such as NVIDIA Surround or AMD Eyefinity, is untested; it needs each screen's `PixelRect`, an advanced setting.
- The loadout screen's rotating aircraft and the main menu's background picture spread onto the side screens.

## Credits

- The projection follows Robert Kooima's *Generalized Perspective Projection*.
- Built on [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX). The F1 window is [BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager).
- The geometry settings follow iRacing's and Assetto Corsa's triple-screen settings.
- Earlier Nuclear Option mods showed the way: [MissileCamera](https://github.com/Mursisru/MissileCamera), [no-missile-cam](https://github.com/Talamander/no-missile-cam), [NOVR](https://github.com/InfernoSuperNova/novr) and [ATF-Wyvern-Mod](https://github.com/Endar728/ATF-Wyvern-Mod).

## Disclaimer

Nuclear Triad is an unofficial mod. It isn't made, endorsed or supported by Shockfront Studios, the developer of Nuclear Option.

## License

MIT license. Copyright © 2026 Tonight We Race! Nuclear Triad comes as is, with no warranty: see the LICENSE file beside this guide in the download, or [in the repository](https://github.com/TonightWeRace/nuclear-triad/blob/main/LICENSE).
