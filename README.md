<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="branding/nuclear-triad-mark-white.svg">
    <img src="branding/nuclear-triad-mark.svg" alt="Nuclear Triad" width="320">
  </picture>
</p>

# Nuclear Triad

**Nuclear Triad is designed to answer the question: what do I do if I have triple screens?** Triple screens are common in high-end racing simulators and also high-end flight simulation rigs, but their software support isn't necessarily universal even in their own medium, and it is unrealistic to expect an early access game to implement such a niche feature so quickly. We, however, are impatient. So here you are.

**Nuclear Triad gives you perspective-correct triples.** Using our tuner dialog, we do our best to show you exactly what you are going to see as you tune your parameters in real time. There is an advanced settings section in our plugin config, but we hope you won't need it. Make sure your displays are aligned well in Windows and that they reflect the reality of your physical layout, and then let Nuclear Triad do the rest. Non-standard orientation? Heterogeneous sizes? We got you.

> **Status:** version 0.1.30 is the current release, on the [Releases page](https://github.com/TonightWeRace/nuclear-triad/releases). Tested on two rigs with Direct3D 11; Direct3D 12 ran on one of them early in development.

## Features

### Three correct views, not one stretched picture

- **Projection from your rig's real geometry.** Each screen is modelled as a rectangle in front of your eye, and Kooima's generalized perspective projection turns it into that screen's off-axis view.
- **Angled screens, bezel gaps, mixed panels.** Side screens can turn in at any angle, sit at any gap and height, and differ from the center screen in size, resolution and orientation.
- **The game's FOV and zoom still work.** By default the center screen is framed exactly as the game frames it, so it looks like vanilla and zoom keeps working.

### Spans the window itself

- **No third-party resize tool.** The plugin stretches the game's borderless window over the screens: as tall as the center screen (the default, which keeps the game's HUD and menus scaled exactly as in vanilla), the full height of every screen, or an explicit desktop rectangle.
- **Black curtains** optionally cover the parts of the side screens the window leaves uncovered, such as the strips above and below the center band.
- **Your screen setting survives.** The plugin keeps the game's own window setting and writes it back on quit, so a spanned window never becomes your saved setting, even after a crash.

### Setup and tuning on screen

- **Sane defaults.** Leave each screen's `Monitor` blank, and the plugin takes the left, center and right screens from Windows' display arrangement, however Windows numbers them.
- **Easy setup.** ConfigurationManager's F1 window opens on a Start here box, whose button opens the tuner. The settings a rig needs come next, in setup order, and the rest wait behind Advanced. Settings apply as you change them. Numeric settings are sliders with ranges, and every edit is clamped to them.
- **Guided on-screen tuner** (Right Ctrl+U). A mouse panel on the center screen walks you through the screens' widths, then your eye height, then each side screen's vertical position, gap and angle. For the widths, it shows what each monitor reports and draws a bar of a stated length to check with a tape measure. Its lines are fixed to your view: an eye-level line with a ring straight ahead, and lines rising and falling through each gap. Each step says which line to watch and which way to press. Both sides move together unless you unlink them, changes save as you go, and Revert restores where you started.
- **Screen alignment tool** (`tools/align-screens.ps1`, run once with the game closed). Do you hate the default Windows screen alignment tooling? So do we. Our tool lines the Windows display arrangement up with where the screens physically sit, by eye: tick marks run along every seam, the arrow keys slide them until they meet across the bezel, and Enter moves the screens. It writes an undo file first. It comes with the source, not the release zip.

### Menus and HUD

- **Menus on the center screen.** A spanned window would put the game's menus on the outer screens and stretch the control mapper too big to use. The plugin keeps every menu on the center screen at its single-screen size: the main menu, settings, control bindings, loadout, mission editor, encyclopedia, server browser, chat, messages, pause menu and map.
- **The HUD's corner panels in the center screen's corners.** The overhead map with its threat list, the weapons, power and countermeasures panel, and the damage display sit where they would on a single screen. They can go on the outer corners instead.
- **Markers land on their targets on the side screens.** Each HUD marker is placed through the camera of the screen it's on. A marker in a bezel gap waits at the seam, and off-screen arrows still work.
- **The flight HUD bends onto the side screens.** When free look puts the aircraft's nose on a side screen, the HUD turns to that screen's horizon and is drawn as big as it looks on the center screen, from your eye distance and the screen's real angle.
- **A steady cockpit in free look.** Past about 33° of pan, the game slides your eye sideways. The plugin holds it still while the nose is still on a screen, so the cockpit doesn't shift under the HUD, and lets the game's lean back in for checking six.

### World

- **Trees and grass on every screen.** The game culls foliage against the center screen alone, so it pops in and out on the side screens. The plugin culls against all three screens plus a margin (10° by default). Measured cost: 0.6 ms a frame with few trees in view, 1.5 ms over forest.

### Safe to install and run

- **Installs by unzipping.** A release is the plugin alone, extracted into the game folder. Nuclear Option needs a BepInEx setting that a new BepInEx leaves off, `HideManagerGameObject`, or no plugin runs. Nuclear Triad turns it on as it first loads, in time for that first start.
- **Client-only.** No network code, and nothing that changes gameplay or what other players see.
- **Off is vanilla.** Right Ctrl+T switches the rig off and on mid-flight, and off gives the game's cameras back exactly as they were. Every feature also has its own setting.
- **Measured cost.** 13.67 ms a frame with the rig on, against 13.34 ms for the game's single camera across the same window: about 2.5% (parked, uncapped, 5440 × 1440, High, RTX 4060 Ti).

### For developers

- **Control channel**, off by default. While `Dev.CommandChannel` is on, the running plugin reads commands from a file once a second: dumps of cameras, volumes, canvases, menus, displays, geometry and the HUD; captures; frame timing; `rig-status`; and live `get` and `set` of any setting. `tools/send-command.ps1` sends one and prints the result.
- **Captures built for checking seams** (Right Ctrl+C): the full window, crops across each seam, a JSON sidecar with every camera's frustum, and each seam re-projected as the eye sees it, where straight lines must stay straight. `tools/seam-lines.py` measures them.

## Requirements

- Windows, with Nuclear Option from Steam, started as Nuclear Option (DirectX 11). It runs in DX12 but hasn't been extensively tested. Tested on Nuclear Option 0.34.1 (Steam build 24724372, Unity 2022.3.62f2); the log warns after a game update.
- Three screens, each its own display in Windows.
- BepInEx 5.4.23.5 for Windows x64 (not BepInEx 6) and ConfigurationManager 19.0, the F1 settings window. Releases don't bundle them; the install guide installs both.
- Tested on two rigs: one a 32" TIE fighter orientation (a 2560 × 1440 landscape center between two 1440 × 2560 portrait sides), and one a single smooth 3x34" wrap (three 3440 × 1440 curved ultrawides).

## Installation

### From a release

Releases are zips holding just the plugin, on the [Releases page](https://github.com/TonightWeRace/nuclear-triad/releases). The install guide, [docs/install.md](docs/install.md), goes in every zip as its README. It covers each step of the install, and troubleshooting. In short, close the game, then extract each of these into the game folder, the one with `NuclearOption.exe`:

1. BepInEx 5.4.23.5: `BepInEx_win_x64_5.4.23.5.zip`, from [its release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
2. ConfigurationManager 19.0: `BepInEx.ConfigurationManager_BepInEx5_v19.0.zip`, from [its release](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases/tag/v19.0).
3. Nuclear Triad: `NuclearTriad-<version>.zip`, from the Releases page. It adds `BepInEx\plugins\NuclearTriad\`.

There's no BepInEx setting to edit: the plugin turns on the one Nuclear Option needs as it first loads.

### From source (development setup)

Needs the .NET 8 SDK or later, and Nuclear Option installed. Set `NUCLEAR_OPTION_GAME_DIR` if the game isn't in the default Steam library. From the repo folder, with the game closed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\fetch-tools.ps1    # checksum-pinned downloads into .tools\
powershell -NoProfile -ExecutionPolicy Bypass -File tools\install-mods.ps1   # BepInEx, ConfigurationManager, UnityExplorer
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1          # build, test, deploy the plugin
```

`install-mods.ps1` sets up the development install: UnityExplorer and the BepInEx console, `HideManagerGameObject`, and the control channel turned on. `tools\uninstall-mods.ps1` takes everything back out in one step. `tools\package.ps1` builds a release zip into `.tools\release\`. [docs/developing.md](docs/developing.md) has the rest of the dev loop.

## Usage

> Written against 0.1.30. Installing, and troubleshooting, are in the [install guide](docs/install.md).

### First run

Before you start, arrange the three displays in Windows to match your screens. In Settings > System > Display, press Identify, then drag them so they sit as your screens do: left to right, and at the heights they really are. Nuclear Triad takes its left, center and right screens from this arrangement. With the source, `tools\align-screens.ps1` does it by eye, with tick marks across each gap.

1. **Start the game and a mission.** In Steam, press Play and pick **Nuclear Option (DirectX 11)**. The window spans all three screens, with the menus on the center one. Start a mission and stay parked in the cockpit: the tuner's lines need the rig, which comes up in a mission.
2. **Run the tuner.** Press Right Ctrl+U, or press F1 and then Open the tuner in the Start here box. Work through its five steps in order: screen widths, eye height, vertical position, gap and angle. Each step says what to look at and which button to press, and changes save as you go.
   - **Screen widths** come first, because every other step is worked out from them. Each starts at what its monitor reports to Windows. Check it with a tape measure, edge pixel to edge pixel, or measure the yellow bar the tuner draws on each screen.
   - **The other four** are set by eye, against lines fixed to your view: an eye-level line with a ring straight ahead, and lines rising and falling through each gap.

   The tuner turns on `Geometry.Measured`, so the rig uses your settings.
3. **Check it in flight.** Right Ctrl+G shows the sky grid: lines fixed in the sky that must run straight across both gaps, level and in a bank.
4. **Optionally, size the flight HUD for the side screens.** Tick Advanced settings in F1, set `Geometry.EyeDistanceMm` to your eyes' distance from the center screen, and set each side screen's `PhysicalAngleDeg` to its real angle. The tuned `AngleDeg` settles well below the real angle.

If HUD pieces split as they cross a gap, Windows' arrangement is still off: the tuner's vertical position step says how many pixels to move each side screen in Windows' display settings.

### Hotkeys

| Key | Action |
| --- | --- |
| F1 | Settings window (ConfigurationManager) |
| Right Ctrl+T | Rig off and on, until the game closes |
| Right Ctrl+U | Open or close the tuner |
| Right Ctrl+G | Sky grid on and off |
| Right Ctrl+C | Capture, with seam crops (an advanced setting) |

The Right Ctrl keys can be changed under `Hotkeys`.

### Settings

Settings live in `BepInEx\config\nuclear-triad.cfg`, and in F1, which shows the advanced ones once "Advanced settings" is ticked. The ones F1 shows by default:

| Section | Setting | Default | What it does |
| --- | --- | --- | --- |
| `Geometry` | `Tuner` | off | The Start here box: its button opens the tuner. Off at every game start |
| `Geometry.Left`, `.Center`, `.Right` | `Monitor` | blank | The screen's Windows display, such as `DISPLAY2`; blank follows Windows' arrangement, left to right |
| `Geometry.Left`, `.Center`, `.Right` | `WidthMm` | the monitor's report | Width of the picture, edge pixel to edge pixel; the tuner's first step checks it |
| `Geometry.Left`, `.Right` | `GapMm` | 0 | Gap to the center screen's picture, edge pixel to edge pixel, straight across |
| `Geometry.Left`, `.Right` | `AngleDeg` | 0 | How far the screen turns toward you from the center screen's plane; set it with the tuner |
| `Geometry.Left`, `.Right` | `VerticalOffsetMm` | 0 | The screen's middle above the center screen's middle; the tuner sets it |
| `Geometry` | `Measured` | off | Use the measurements; off draws a flat test layout. The tuner turns it on |
| `Geometry` | `EyeHeightMm` | 0 | Your eyes above the middle of the center screen; the tuner's first step |
| `Geometry` | `SkyGrid` | off | Show the sky grid. Off at every game start |
| `Window` | `Span` | `CenterBand` | `CenterBand`, `FullHeight`, `Explicit` or `Off` |
| `Hotkeys` | `Tuner`, `SkyGrid`, `ToggleRig` | Right Ctrl+U, G, T | See Hotkeys |

Advanced: `Geometry.EyeDistanceMm`, `Geometry.CenterFraming` (`GameFov` or `EyeDistance`), `Geometry.EyeSideMm`, `Geometry.VerticalFromDesktop`, each side screen's `PhysicalAngleDeg`, each screen's `PixelRect`, `Window.Curtains`, `Window.MenusOnCenter`, `Window.MenuLogo`, `Window.ExplicitRect`, `Hotkeys.Capture`, `Rig.Enabled`, `Rig.HoldEyeInFreeLook`, `Rig.CockpitOnly`, `Hud.SideScreenMarkers`, `Hud.BendFlightHud`, `Hud.BendScale`, `Hud.FollowStraightAhead`, `Hud.CornerPanels`, `Vegetation.CullAllScreens`, `Vegetation.CullMarginDeg`, `Setup.FixBepInExManager`, and the `Dev` settings.

### Turning it off

- Right Ctrl+T switches the rig off, back to the game's own camera, until you press it again or the game closes. The window stays spanned, so you see the game's single picture stretched across the screens.
- `Window.Span` = `Off` gives the game its window back, at its own saved setting.
- To uninstall, close the game and delete `BepInEx\plugins\NuclearTriad\` and `BepInEx\config\nuclear-triad.cfg`. BepInEx's `HideManagerGameObject` stays on, since every plugin needs it in Nuclear Option. `tools\uninstall-mods.ps1` removes the whole development install.

### Developer commands

The control channel is off by default, and a development install turns it on. While it's on, any program on the PC can change the plugin's settings through its command file, so leave it off anywhere else. With it on and the game running, `tools\send-command.ps1` drives the plugin:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\send-command.ps1 rig-status
```

`toggle`, `tune` and `grid` with no argument flip that state in the running game; to read state, use `rig-status` or `get`. The full command list and the dev loop are in [docs/developing.md](docs/developing.md).

### Working on it

- [docs/architecture.md](docs/architecture.md): how it works and why.
- [docs/developing.md](docs/developing.md): the build, the dev loop, the control channel, and how seams are checked.
- [docs/game-internals.md](docs/game-internals.md): what we learned about the game underneath, for anyone modding it.
- [CLAUDE.md](CLAUDE.md): working on it with Claude Code or another agent, and the lessons we'd pass on.
- [CONTRIBUTING.md](CONTRIBUTING.md): issues and pull requests. [CHANGELOG.md](CHANGELOG.md) has what changed in each release.

## Known limitations

- Built and tested for Direct3D 11. Direct3D 12 ran on one rig early in development but hasn't been tested much since, and Vulkan is untested.
- Three flat screens, each its own display in Windows. Curved panels are drawn as flat ones: on a 34" 1500R ultrawide that's about 1.5° off at a panel's outer edge, and the tuner absorbs the seams. A single wide display, such as NVIDIA Surround or AMD Eyefinity, is untested and needs each screen's `PixelRect`. No head tracking or VR.
- HUD pieces that straddle a gap split briefly when the Windows display arrangement doesn't match where the screens physically sit. Moving the side screens in Windows fixes it; drawing the HUD per screen, planned, would remove the dependence.
- The loadout screen's rotating aircraft and the main menu's background picture spread onto the side screens.
- The game's vignette is applied per screen, so it shades each screen's edges, seams included.

## Credits

- The projection follows Robert Kooima's *Generalized Perspective Projection*.
- Built on [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX). The F1 settings window is [BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager).
- The geometry controls follow iRacing's and Assetto Corsa's triple-screen settings.
- Earlier Nuclear Option mods mapped the ground, and we learned from their code and notes:
  - [MissileCamera](https://github.com/Mursisru/MissileCamera), the first extra camera in the game, and what one needs: URP settings, shader globals, and care with the vegetation and the HUD markers;
  - [no-missile-cam](https://github.com/Talamander/no-missile-cam), the build setup, with the game's DLLs as compile-only references;
  - [NOVR](https://github.com/InfernoSuperNova/novr), several views injected into the game, and the `NUCLEAR_OPTION_GAME_DIR` build setting;
  - [ATF-Wyvern-Mod](https://github.com/Endar728/ATF-Wyvern-Mod), the cockpit camera, and how to tell the cockpit view apart.

## License

MIT. Copyright © 2026 Tonight We Race! See [LICENSE](LICENSE), which goes in every release zip.

Tonight We Race! is Seattle's premier sim racing venue, and if you're in the area, check us out: [tonightwerace.gg](https://www.tonightwerace.gg).

## Disclaimer

This project is an unofficial community modification and is not affiliated with, sponsored by, or endorsed by Shockfront Studios Pty Ltd. Original Nuclear Option assets, vehicle designs, audio, and code are Copyright (c) 2026 Shockfront Studios Pty Ltd. All rights reserved. Nuclear Option and Shockfront Studios are trademarks or registered trademarks of Shockfront Studios Pty Ltd. Original mod content and all other trademarks belong to their respective owners.
