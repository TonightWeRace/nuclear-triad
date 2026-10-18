# Developing Nuclear Triad

How to build, run, inspect and release the plugin from source. What it does and why is in [architecture.md](architecture.md); what we learned about the game is in [game-internals.md](game-internals.md).

## Repo layout

```
README.md                     what it is, features, install, usage
CLAUDE.md                     notes for working on it with Claude Code or another agent
CHANGELOG.md                  what changed in each release
CONTRIBUTING.md               issues and pull requests
LICENSE                       the license, which goes in every release zip
Directory.Build.props         game path from NUCLEAR_OPTION_GAME_DIR
src/NuclearTriad/             plugin: net48, BepInEx 5
  Geometry/                   panel model + Kooima; own double-precision math, no UnityEngine types
  Rig/                        cameras, settings copy, viewports, lifecycle, flight HUD bend, eye hold
  Patches/                    every Harmony patch, one file each, each behind a config flag
  Dev/                        control channel, captures, timing, sky grid, HUD dumps and checks
  Tuning/                     the on-screen tuner, its view-fixed lines, and F1's Start here box
tests/NuclearTriad.Geometry.Tests/   plain dotnet test, no game required
tools/                        dev scripts: display probe, build and deploy helpers, release zip, the logo
docs/                         the install guide (the zip's README), architecture, this guide, game internals
branding/                     the project mark as SVG, from tools/logo.py; PNG renders in png/ (gitignored)
decompiled/, captures/        gitignored; never committed
.tools/                       gitignored: downloaded tools, NuGet packages, build output of the release zip
```

## Build, deploy, inspect

Everything stays inside the repo folder: tools and downloads in `.tools/`, NuGet packages in `.tools/nuget/`, decompiled output in `decompiled/`, captures in `captures/`, git config repo-local. Nothing installs machine-wide, so the folder moves to another machine as one piece.

You need the .NET 8 SDK or later, and Nuclear Option installed: the plugin compiles against the game's own DLLs and BepInEx's `core/`, as compile-only references. None are copied or committed. The game path comes from `NUCLEAR_OPTION_GAME_DIR`, defaulting to `C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option`; `Directory.Build.props` reads it.

Run scripts with `powershell -NoProfile -ExecutionPolicy Bypass -File tools\<script>.ps1`:

| Script | What it does |
| --- | --- |
| `fetch-tools.ps1` | Downloads the pinned third-party tools into `.tools/downloads/`, checks each against its publisher's checksum, unpacks ilspycmd |
| `install-mods.ps1` | Puts BepInEx 5.4.23.5 (console on), UnityExplorer and ConfigurationManager into the game, for a development install. Refuses to overwrite; writes `.tools/installed.json` |
| `uninstall-mods.ps1` | One-step rollback: moves everything the install added, plus anything created under BepInEx since, into `.tools/uninstalled/<time>/` |
| `build.ps1` | Builds, runs the tests, deploys `NuclearTriad.dll` to `BepInEx/plugins/NuclearTriad/`. The game must be closed; `-NoDeploy` skips the copy |
| `package.ps1` | Builds and tests, then writes the release zip to `.tools/release/`: `BepInEx/plugins/NuclearTriad/` with the DLL, `docs/install.md` as `README.md`, and `LICENSE`, plus its SHA-256 and release notes. Refuses uncommitted sources, a guide that names another version, and a DLL holding a local path. Says whether this machine runs the same DLL. Publishes nothing |
| `decompile.ps1` | Decompiles the game's assemblies and URP into `decompiled/<Steam build>/` (gitignored), for reading |
| `display-probe.ps1` | Prints the display arrangement and the panel sizes the monitors report |
| `align-screens.ps1` | Lines the Windows display arrangement up with the physical screens by eye, once per rig, with the game closed. Tick marks run along every edge where two screens meet; the arrow keys slide a side screen's ticks until they line up across the bezel, and Enter writes every display's position to `.tools/display-undo/` and then moves the side screens vertically. `-DryRun` prints the screens, the tick rows and the calls Enter would make; `-Snapshot <dir>` saves the pictures as PNGs; `-Undo <file>` restores an undo file (with `-DryRun`, it only prints). Run it yourself: an agent should stick to the read-only modes |
| `seam-lines.py` | Measures the sky grid in a capture's eye-space seam views: every grid line visible on both sides of a gap must continue straight across it. Python 3, standard library only: `python tools\seam-lines.py <png>...` |
| `logo.py` | Draws the project mark, three screens seen from the cockpit with a banked horizon cut straight across them and a bare HUD (pitch ladder and two tapes) on the centre screen, as SVGs in `branding/`: the bare mark and a square app icon. `--png` also writes PNGs to `branding/png/`: Edge renders 1024 px, smaller icons are averaged down from that (64, 32 and 16 px redrawn on whole pixels first, without the HUD; 16 px also leaves the horizon out), and every render is checked for cropping. Python 3, standard library only: `python tools\logo.py [--png]` |
| `send-command.ps1` | Sends one command to the running plugin through the control channel and prints its result line |

- A build depends on the source alone (`Directory.Build.props`): paths inside the binaries read `/_/`, and no git commit goes into the version or the PDB, whose ID the DLL holds. So `build.ps1` deploys the same bytes `package.ps1` zips from the same source, and anyone building the same source files (byte for byte, line endings included) with the same SDK against the same game build gets the same DLL. The DLL's file version is `Plugin.Version`.
- BepInEx logs to `<game>\BepInEx\LogOutput.log`, and a development install turns its console window on.
- After a game update, if patches misbehave, delete `BepInEx\cache\harmony_interop_cache.dat`.
- Nuclear Triad targets Direct3D 11 (feature level 11.1) and is tested only there. Steam's launch dialog offers "Nuclear Option (DirectX 11)", the default with no arguments, "(DirectX 12)" and "(Vulkan)", so pick DirectX 11. With Steam running, `NuclearOption.exe` can also be launched directly.
- The version guard warns in the log when the game build changes; re-verify the hooks then ([game-internals.md](game-internals.md) records the build under study).

## Control channel

`Dev.CommandChannel` is off by default (0.1.27), so releases ship without it. A development install turns it on: `install-mods.ps1` writes that line. While it's on and the game runs, the plugin claims `<game>\BepInEx\config\nuclear-triad.cmd` once a second, runs each line as a command, and appends one tab-separated result line per command to `nuclear-triad.out` beside it: UTC time, frame, ok/error, command, result. It polls only while the game is updating, so a minimized or paused game holds commands. `tools\send-command.ps1 <command> [args]` does the write-and-wait. While it's on, any program on the PC can change the plugin's settings through that file, so leave it off anywhere but a development machine.

It's how an agent works on the plugin while a person flies: the agent dumps, captures and changes settings, and the pilot keeps the controls.

| Command | Effect |
| --- | --- |
| `ping`, `help`, `version` | Liveness, the command list, the build line |
| `dump-cameras [label]` | Every camera in the loaded scenes: rect, target, clip planes, projection (custom or from FOV), masks, URP data and stack, plus screen and pipeline settings |
| `dump-volumes [label]` | Every Volume, highest priority first, with its overrides |
| `dump-canvases [label]` | Every root UI canvas: render mode, camera, sorting, scaler |
| `dump-displays [label]` | Monitors and the game window's client rect as the game process sees them, its DPI mode, and the camera rect each monitor would get |
| `dump-geometry [label]` | The panels the current geometry config builds, with each camera's frustum, FOV and heading. Until `Geometry.Measured` is set, that's the desktop-plane test layout |
| `toggle [on\|off]` | The rig off and on. Off destroys the side cameras and gives the game's cameras back exactly; the window span stays, so off is the stretched single camera |
| `span [band\|full\|explicit\|off]` | Sets the window span mode (saved to config) and re-applies it; with no argument, retries |
| `capture [label]` | Full-window PNG, 512 px seam crops at the horizon and mid-frame, and a JSON sidecar with every camera's rect, frustum, panel and matrix, in `<OutputDir>\captures\`. With the rig on, also `-seamN-eye-<row>.png`: each seam re-projected as the eye sees it through the panels (30° wide, bezel gap in dark grey), where great circles must be straight |
| `grid [on\|off]` | The sky grid of world-fixed great circles. Same as F1's `Geometry.SkyGrid` checkbox and Right Ctrl+G; off at every game start |
| `timing start [uncapped]`, `timing stop` | Frame time in 10-second windows, to the log and `<OutputDir>\timing\*.csv`. `uncapped` lifts the game's frame cap for the run |
| `tune [on\|off\|next\|back\|revert\|linked\|unlinked]` | The on-screen tuner: open or close it, change step, revert, link or unlink the sides. Same as the button in F1's Start here box (`Geometry.Tuner`) and Right Ctrl+U |
| `dump-ui [label]` | Every active screen-overlay canvas, for planning the menu pinning: its scaler and size, then its tree a few levels deep, with each piece's anchors, pivot, offsets and its span across the window in pixels |
| `dump-hud [label]` | The flight HUD's layout: where each part sits in the hierarchy, what's inside the nose-anchored group (`HUDCenter`), and that group's tree with components; then the target designator and the helmet display's panels (where each sits, and its parent's scale and rotation), and the helmet frame's (`HMDCenter`) tree |
| `hud-check` | For every unit marker the game is showing, per screen: how far it's drawn from the corrected spot, and how far off the game's own placement would be |
| `rig-status` | One line: rig, span, window, curtains, grid, layout, the display each screen uses (and whether they're out of order), and in band mode where the band sits on each side screen against level |
| `get <Section.Key>`, `set <Section.Key> <value>` | Read or change any setting live, saved to the cfg; e.g. `set Geometry.Left.AngleDeg 15`, `set Geometry.CenterFraming EyeDistance`. `set` keeps a number within its setting's range and says when it had to |
| `reload-config` | Re-reads the cfg file |

`toggle`, `tune` and `grid` with no argument flip that state in the running game; to read state, use `rig-status` or `get`.

Dumps go to `<Dev.OutputDir>\dumps\<time>-<label>-<kind>-b<build>.txt`. On a development machine, point `Dev.OutputDir` (in `BepInEx\config\nuclear-triad.cfg`) at the repo's `captures\` folder, which is gitignored. Hotkeys: Right Ctrl+T toggles the rig, Right Ctrl+C captures, Right Ctrl+G toggles the grid, Right Ctrl+U opens the tuner.

## Verification

- Geometry unit tests run under `dotnet test` with no game. A flat layout with zero bezels must equal one wide symmetric frustum split in three. A toe-in equal to one panel's angular width must give symmetric side frusta.
- Raw framebuffer seam crops come out straight only for flat, zero-bezel layouts. With toe-in, a correct render kinks at the seams in the framebuffer. Judge seams in the eye-space seam views the capture writes (`-seamN-eye-*.png`, built by `Geometry/EyeReprojection`; `tools/seam-lines.py` measures the grid lines in them), and judge the physical result by eye or with a photo from the eye point. The eye views check the render against the geometry model; only the physical look checks the model against the rig.
- Read seam crops, not full frames, because full frames get downscaled when viewed.

## Releases

A release is a zip laid out like the game folder: `BepInEx/plugins/NuclearTriad/` holding `NuclearTriad.dll`, `README.md` (the install guide, [install.md](install.md)) and `LICENSE`. People extract it into the game folder. BepInEx 5.4.23.5 and ConfigurationManager 19.0 are prerequisites the guide has them install; since 0.1.28 the plugin turns on BepInEx's `[Chainloader] HideManagerGameObject` itself, so there's no file to edit. Nothing third-party is bundled, and there's no installer.

To cut one: bump `Plugin.Version`, bring the guide's version line, the zip's name, the `Loading [Nuclear Triad <version>]` line and the tested game build up to date (`package.ps1` checks them), add the release to [CHANGELOG.md](../CHANGELOG.md), and commit. Then run `tools\package.ps1`. Releases are published on this repository's Releases page, each with the zip's SHA-256 in its notes.
