# Game internals

What we've learned about Nuclear Option's current build while building Nuclear Triad, in our own words, for anyone modding the game. No decompiled code or pasted snippets go in this file or anywhere else in the repo. Class and member names are the game's own, as of the build below.

Sources: the plugin's runtime dumps (`dump-cameras` and the other control-channel commands in [developing.md](developing.md)) in the main menu, the cockpit and the external view, and a read of the game's and URP's assemblies.

## Build under study

| Item | Value | Source |
| --- | --- | --- |
| Game version | 0.34.1 | `Application.version`, logged by the plugin |
| Steam build ID | 24724372 | `appmanifest_2168680.acf` (updated 2026-08-30 00:23 UTC) |
| Build hash | cb745d6c44f1 | `build-hash.txt` in the game root |
| Unity | 2022.3.62f2 (7670c08855a9) | `UnityPlayer.dll` version; `globalgamemanagers` header |
| URP | 14.0.12 (Forward+, TAA and FSR 1 present; render graph compiled in but forced off) | Package path embedded in the shipped Core RP assembly |
| Scripting backend | Mono: no `GameAssembly.dll`, `MonoBleedingEdge` present | Game folder |
| Graphics API | Direct3D 11.0, feature level 11.1. The game's launch dialog also offers Direct3D 12 and Vulkan. Nuclear Triad is developed and verified on Direct3D 11 only; the other two are untested | `Player.log` |
| `Assembly-CSharp.dll` | 3,025,408 bytes, written 2026-08-29 | Game folder |

**Last verified build:** 24724372 / cb745d6c44f1. The plugin's version guard warns when the running build differs.

## Baseline before modding

- The game root holds only `MonoBleedingEdge/`, `NuclearOption_Data/`, `build-hash.txt`, `NuclearOption.exe`, `steam_appid.txt`, `UnityCrashHandler64.exe` and `UnityPlayer.dll`. Anything else in the root was added by a mod; `tools/uninstall-mods.ps1` takes out what `tools/install-mods.ps1` added.
- `steam_appid.txt` ships with the game, so `NuclearOption.exe` can be launched directly while Steam is running.
- Steam keeps the game updated by default (`AutoUpdateBehavior` 0), so expect new builds under you. The plugin's version guard flags them in the log.
- Display prefs live in Unity PlayerPrefs under `HKCU\Software\Shockfront\NuclearOption`. On the development machine before modding: borderless fullscreen window, monitor 0, native 2560 × 1440, quality level 3 ("High"). The game uses one screen only.

## BepInEx in this game

- **`[Chainloader] HideManagerGameObject = true` is required.** Without it, the game destroys BepInEx's manager object during startup, so plugins load (`Awake` runs) but `Update` never does. Seen on the first launch, 2026-10-16: the control channel stayed silent with the game focused, and started at once after the switch. `tools/install-mods.ps1` sets it. BepInEx creates the object and loads every plugin onto it before the game's first scene, so hiding it in a plugin's `Awake` saves the start just as well: since 0.1.28 Nuclear Triad does that, and turns the setting on, when it's off. Seen on 0.1.28's first start with the setting off, 2026-10-17.
- `Application.runInBackground` is true, so the control channel keeps working while the game is unfocused.
- The game targets 60 fps (`Application.targetFrameRate`), with vsync off.

## Camera rig

**Ownership.** `CameraStateManager` is a scene singleton in the `GameWorld` scene, which every map and the mission editor load into. The same GameObject carries the main `Camera` and the floating-origin component. It exists only in a mission, never in the menus; between a scene unload and the next game reset its static instance can point at a destroyed object, so null-check it the Unity way.

**States.** There are nine camera states (cockpit, orbit, TV/flyby, chase, free, controlled, selection, relative, encyclopedia). `SwitchState` raises an instance event, `onSwitchCamera`, that a mod can hook. The static `cameraMode` is never reset between scenes and doesn't cover every state, so compare the current state object instead.

**Hierarchy.** In the cockpit: the pilot's `helmetCamPoint`, then a `cameraPivot`, then `Main Camera`. In the external view: the aircraft, then `cameraPivot`, then `Main Camera`. In the cockpit, free-look and TrackIR are the main camera's own local rotation and position; the pivot carries only G-force inertia and shake. The game reads the camera's local angles back every cockpit frame for smoothing, so a mod must never write the main camera's transform. Tobii head tracking doesn't reach the camera in this build; TrackIR does.

**The stack, from the dumps.**

| Camera | Type | Renders | Notes |
| --- | --- | --- | --- |
| `Main Camera` | Base, renderer 0 | Everything except Cockpit, PP, TargetCamPP, PreviewRender, GrassBlockerProxy | Near 1, far 80,000, vertical FOV 50, SMAA (medium), post-processing **off**, no volume mask, no occlusion culling, MSAA and HDR on |
| `cockpitRenderer` | Overlay, renderer 2, first in the stack | Cockpit, CockpitAndExternal | Near 0.01, far 5. Enabled only in the cockpit view; the game copies the main camera's FOV onto it every cockpit frame |
| `postProcessingRenderer` | Overlay, renderer 1, last in the stack | PP only, which holds nothing of note | Post-processing **on**, volume mask PP. Its job is to apply post-processing to the whole stack |

Both overlays are children of the main camera at zero offset. Other cameras: `screenCam` renders the cockpit TacScreen into a 1024 × 512 texture; `TargetCamera` and its UI overlay render the targeting view into a 360 × 240 texture and stay off until used; `selectionCam` (overlay, off) and the reflection-probe camera are idle.

**What the game writes.** Game code never writes the main or cockpit camera's rect, pixel rect, projection matrix, aspect, target texture, far plane, culling mask, depth or clear flags, never calls `ResetProjectionMatrix`, and never touches URP's camera data. It writes `fieldOfView` every `LateUpdate` in every state (easing toward the desired FOV plus zoom), the near plane on state changes, and the cockpit camera's `enabled` and `fieldOfView`. Everything camera-related runs in `CameraStateManager.LateUpdate`.

**FOV.** Defaults are 50° vertical for both cockpit and external views. They're saved in PlayerPrefs (`DefaultFoV`, `DefaultExternalFoV`) only once the player applies the Gameplay settings page. Zoom isn't saved. Code that reads `fieldOfView`: the HUD pitch ladder's scale, tracer size, the laser-guided aiming circle and look sensitivity. So the rig sets `fieldOfView` to its own equivalent vertical FOV rather than leaving the game's value.

**What that means for extra cameras.**

- Each side camera needs clones of **both** overlays: the cockpit one for the canopy and the post-processing one for tonemapping, bloom and colour. Without the post-processing clone, side screens render without tonemapping and change colour at the seams.
- Give each overlay clone the same rect as its base camera; URP rescales an overlay's projection when the two differ.
- Don't tag side cameras `MainCamera`: several systems read `Camera.main`.
- Side cameras parented to the main camera survive the floating origin (every root object of the active scene shifts in 64 m steps once the camera passes 1,024 m) and are destroyed with it when `GameWorld` unloads.

## Render path

No game code renders the main view into a texture, adds command buffers or renderer features to it, or blits to the screen. URP decides on intermediate textures itself. A base camera with a viewport rect smaller than the screen always renders through one, sized to its own viewport. The final blit then writes only that camera's pixel rect and keeps what is already in the back buffer, so three base cameras with separate rects composite into one frame. URP never clears outside a camera's viewport, so window areas no camera covers keep stale contents.

## URP rules for the rig

From a read of the shipped URP 14.0.12:

1. **Timing.** For a base camera, URP reads its rect and projection after `beginCameraRendering`, so changes made there apply the same frame to the viewport, culling, shadows and motion vectors. Stack membership, overlay enabled state, which overlay resolves to the screen and the stack's post-processing flag are read before that callback. Change those in `beginContextRendering`; set only rects and matrices per camera.
2. **Overlays.** An overlay always renders into its base camera's viewport and target, whatever its own rect says, with its own projection and view. But if its own pixel rect differs from the base camera's, URP rescales the horizontal scale term (`m00`) of its projection by the aspect difference, which breaks an off-axis frustum. So every overlay gets exactly its base camera's rect: our clones, and the game's own two once the main camera's rect shrinks to the center.
3. **Clip planes.** URP derives the depth-buffer constants, Forward+ light binning, SSAO and the shadow distance from the camera's `nearClipPlane` and `farClipPlane` fields, not from the matrix. Each custom frustum must use that camera's own near and far planes, so the cockpit overlay (0.01 to 5) needs its own matrix.
4. **Camera data.** A camera without URP's additional camera data renders with MSAA, post-processing and anti-aliasing off. Every rig camera needs it. The renderer index is private (set it with `SetRenderer`, reading the source's index by reflection), and `clearDepth` has no setter.
5. **Render targets.** Intermediate textures follow each camera's pixel size. Where the center (landscape) and sides (portrait) differ, URP swaps its shared targets twice between sizes each frame, through a small pool. Watch for it in the frame-time numbers.
6. **TAA** is unavailable to any camera with a stack, and the game uses SMAA, so keep SMAA everywhere. TAA jitter would sit correctly on an off-axis matrix anyway.
7. **Shadows** render once per base camera, from its own culling frustum. Cascade splits differ between cameras, so shadow edges may not line up exactly at a seam.
8. **Post effects** (vignette, lens distortion, chromatic aberration, bloom) are computed in each camera's own image, so nothing bleeds across a seam and vignette repeats per screen.
9. Nothing in URP or Core resets or overwrites a custom projection matrix. Panini projection and lens flares read `fieldOfView`; neither is in the active profile.

## Shader globals

`ShaderGlobalManager` only sets process-wide shader globals, written once per frame or less, never per camera, and it hooks nothing in the render pipeline:

- the map extent, at start;
- the floating-origin offset, after each origin shift;
- six frustum planes, the camera position and a camera "target" pushed up to about 384 m ahead along the heading, every frame from the vegetation renderer's `LateUpdate`, for the camera that renderer cached from `Camera.main` at start;
- a map-wide blast texture, once.

The view-dependent globals are read only by the grass and tree culling compute shaders; the terrain and other material shaders don't read them. Nothing in game code would turn an extra camera magenta: an extra camera that renders magenta is most likely set up wrong. Look for shader-variant errors in `Player.log`.

## Vegetation and terrain

- Terrain is ordinary static meshes, with no Unity Terrain and no streaming, so every camera renders it correctly in any heading.
- Grass and trees are culled on the GPU once per frame against the **main camera's** frustum (taken from its current projection), then drawn with no camera specified, so every camera draws that same set. Left alone, side screens miss the trees and grass outside the main camera's view.
- The cull pads each kind by its own radius: trees by 10 m, smaller plants by less. So with a rig of extra cameras, foliage on the side screens survives some way past the center screen's edge, further for near objects than far ones, and pops at a different angle for trees than for grass. It shows on takeoff over forest. Both kinds read the same global planes.
- Grass also only exists in a 1,024 m height-map window centered up to 384 m ahead of the main camera, so the side views thin out toward the rear.
- Nuclear Triad's fix (0.1.7, `Patches/VegetationCullPatch.cs`) replaces those global planes with planes holding every screen's lit area plus a margin, rather than re-running the cull per camera. The center cull uses whatever projection the main camera holds during `LateUpdate`, so the rig keeps its projection in place through the frame rather than restoring it between renders.

## Other systems tied to the main camera

- Clouds and lightning spawn ahead of the main camera, so the side views are sparser. Cosmetic.
- The ocean recenters under `Camera.main`; the skybox, stars, sun and time-of-day lighting key off its position. All position-based, so fine for cameras that share its position.
- Exposure is one global value. G-LOC drives vignette and saturation on the post-processing profile.

## Post-processing and anti-aliasing

- The active profile is `OutdoorPostProcessing` on `LevelInfo/PostProcessing` (global, priority 0.5, layer PP): ACES tonemapping, bloom (threshold 1.3, intensity 0.5, lens dirt 0.75), **vignette 0.4**, colour adjustments. The night-vision and targeting-camera volumes stay off until used; a UI bloom volume sits on the HUD layer.
- Anti-aliasing: SMAA (medium) on the main camera and MSAA 2× in the URP asset. Render scale 1 with the upscaling filter on Auto (no upscaling), HDR on, depth and opaque textures on.
- Shadows: distance 2,000, three cascades, 2,048 px main-light map, no additional-light shadows. The external views override shadow distance on entry.
- Seams: vignette is applied per view, so it darkens both sides of every seam, and lens dirt repeats per view. A mod-owned, higher-priority volume that zeroes vignette would remove it; Nuclear Triad doesn't do that yet.

## HUD placement

- World-anchored HUD symbols (unit and hit markers, the selected-target arrow, objective and airbase markers, RWR icons, the velocity vector, the nose reference that carries every weapon HUD, gun and bomb pippers) are all placed through the main camera's `WorldToScreenPoint`, written straight into overlay-canvas positions. Two spots use `Camera.main` instead, so side cameras must not carry the `MainCamera` tag.
- No game code reads a camera's pixel rect, pixel size, aspect or projection. The HUD assumes the main camera fills the whole window.
- Off-screen tests use the window: the target arrow logic (`HUDFunctions.PinToScreenEdge`) and its separate copy in the objective overlay both measure from the window center against half the window size, and pin to the window border. Unselected markers have no off-screen test; they hide only when behind the camera.
- Three pieces take the window's middle to be straight ahead. That stops being true once the center screen draws straight ahead somewhere else, as it does with an eye height set (`Geometry.EyeHeightMm`). The airbase capture display of cargo and troop weapons hangs from a fixed spot in the frame that also holds the map (`HMDCenter`), not from the nose-anchored group. The shared off-screen pin measures from the middle. So does the objective markers' own copy of it, which also turns a point behind the eye back round through the middle. Everything else is placed through the main camera's projection or anchored to the screen. Since 0.1.12 Nuclear Triad re-pins both arrows from straight ahead (`Patches/OffScreenArrowPatch.cs` and `Patches/ObjectiveArrowPatch.cs`). It moved the capture display too, until moving it up by the eye height pushed it off the top of the window; since 0.1.19 that stays where the game lays it out.
- Several HUD scales read `fieldOfView` and assume it spans the canvas height: the pitch ladder, the AoA bracket, the laser-guided circle and the objective range ring. Tracer width reads it too.
- The HUD projects in `Update` and `LateUpdate`, between renders. So the rig must leave its center rect and projection on the main camera for the whole frame; restoring them after each render would hand the HUD the vanilla projection.
- With the main camera shrunk to the center screen and nothing else done: anything inside the center frustum lands correctly on the center screen. Targets outside it are extrapolated on the center camera's image plane and drawn on the side monitors at the wrong place, and anything more than 90° off the center heading is hidden. The arrow pins to the outer edges of the side monitors. Since 0.1.6 Nuclear Triad answers for the side screens through their own cameras (`Patches/MainCameraScreenPointPatch.cs`).
- The HUD canvases scale with window height (reference 1,080 px). A spanned window taller than the center screen enlarges the HUD and breaks the FOV-to-canvas-height assumptions above. That only happens on rigs with portrait side screens, like the development rig: its bounding box is 2,576 px tall against the center's 1,440.

## UI

Every game canvas is Screen Space Overlay except the TacScreen's (Screen Space Camera, into its render texture). That includes `HUDCanvas`, whose camera field points at `postProcessingRenderer` but is ignored in overlay mode. Overlay canvases cover the whole window, so once the window spans three screens, menus, map and HUD panels stretch across all of them; Nuclear Triad pins them back to the center screen (0.1.21 and 0.1.22). HUD markers are placed through the main camera's `WorldToScreenPoint`, which honors its pixel rect, so they land on the center screen.

## Screen resolution and the window

- The `[GameResolution] ResetGameResolution` line in `Player.log` is the **mission outcome** (ongoing, victory, defeat) being reset. It has nothing to do with the screen.
- The game sets the screen resolution only at startup, if a saved resolution exists (the `ScreenResolution` PlayerPref), and from the Graphics menu. It uses borderless fullscreen or windowed mode, never moves the window and never picks a monitor. The resolution menu lists only the current monitor's modes.
- So a window span set by a plugin won't be undone at mission load, only at startup with a saved resolution or from the Graphics menu.
- Unity sees every display and activates only display 0. The process is per-monitor DPI aware, and the window is a borderless popup exactly covering one monitor.

## The first questions, answered

| Question | Answer |
| --- | --- |
| Is `cockpitRenderer` a URP overlay in the main camera's stack or a second base camera? Which layers does each camera render? | Overlay, first in the main camera's stack, followed by a post-processing overlay. Layers in "Camera rig" |
| Does the main camera render through an intermediate render texture, and do viewport rects work on that path? | No game-made texture. URP uses its own intermediate texture, sized per viewport, and viewport rects work through its final blit |
| Where does game code write the main camera's FOV, rect or projection? | FOV every `LateUpdate` in every state; rect and projection never |
| What does `ShaderGlobalManager` set, from which callback, and does any of it depend on view direction? | Process-wide globals, at most once per frame; the view-dependent ones feed only vegetation culling |
| How does `DetailRenderer` cull and draw? | GPU cull once per frame against the main camera, drawn for every camera |
| How does `CombatHUD` place markers and off-screen arrows? | Through the main camera's `WorldToScreenPoint`, with off-screen tests against the whole window. See "HUD placement" |
| Which canvases exist, and what render mode does each use? | All Screen Space Overlay except the TacScreen's |
| What does the active Volume profile contain, and which anti-aliasing or upscaler is active? | See "Post-processing and anti-aliasing" |
| What are the Unity, URP and game versions, and the Steam build ID? | Game 0.34.1, Unity 2022.3.62f2, URP 14.0.12, Steam build 24724372 |

## The mouse cursor

Found 2026-10-16 while designing the on-screen tuner. The game's cursor manager shows the cursor whenever any of its reasons is set (game menu, map, selection menu, dialogue, chat, loading, not in game, empty scene, camera-control UI) and locks it otherwise. It caches that answer and only touches the cursor when the answer changes or the window regains focus, so a plugin that shows the cursor every frame, as ConfigurationManager does, isn't fought. In the cockpit, mouse look only runs while the cursor is hidden. The pilot's Fire input is ignored while the cursor shows, as long as the player's "menu weapon safety" setting is on, which is the default (PlayerPrefs `MenuWeaponSafety`). With the virtual joystick, holding Free Look makes the manager re-apply its own answer every frame.

## HUD markers

Found 2026-10-16, when markers drifted off their targets on the side screens. Every element of the window HUD that marks something in the world asks the main camera where a world point lands on screen: unit markers, hit markers, the flight path marker, radar-warning and threat indicators, objective and airbase overlays, bombing and boresight cues, and the turret crosshair. About two dozen call sites across 13 classes. Off-screen arrows use the same projection and test the result against the whole window. The cockpit's own screens, the targeting and landing displays, project through their own cameras.

With the rig on, the main camera is the center screen's off-axis camera. A point that shows on a side screen therefore gets placed where a flat extension of the center screen would put it. Markers are right on the center screen and drift on the side screens, more so further out and with more toe-in.

## Free look moves the eye

Found 2026-10-16: the HUD's W stays on its target while the cockpit shifts under it. Up to about 33° of pan, cockpit free look only rotates the camera. Past that, the game also slides the eye sideways toward the side being looked at, along the aircraft's left-right axis, like leaning. The slide grows steadily to 20 cm at 100° of pan and stays there. A separate, user-adjustable offset raises the eye and moves it forward by up to 15 cm. With a head tracker, its offsets are used instead, clamped. The flight HUD aims at infinity (its anchor is the nose, 4 km out), so it stays on the world while the near cockpit shifts against it: parallax. On one screen the nose leaves the edge at about 40° of pan, where the slide is about 2 cm, so it never shows. On a triple rig the nose stays in view on a wing out to about 77°, where the slide is about 13 cm.

## Floating origin and the HUD canvas

Found 2026-10-17 chasing a HUD flash. Whenever the camera is more than 1,024 m from the origin on any axis, the camera manager's LateUpdate shifts the scene. Every root object of the active scene moves back by the camera's position rounded to whole 64 m steps, then the physics transforms are synced. At speed that's every two to four seconds. The HUD's screen-overlay canvas sits under one of those roots, so it's carried by the same vector, and it's back over the window before it draws. The game places its own HUD in Update, before the shift, so the carry is harmless to it. Anything that writes a screen point as a UI world position later in that frame lands a shift away. The local aircraft's rigidbodies are interpolated.

## The view-center diamond (target designator)

The combat HUD's target designator is an image at the HUD canvas's middle, the view's center. The game never moves it; each weapon display sets its scale, color and whether it shows. Targeting takes the markers nearest it on screen. With a missile or no weapon, it fades near the flight HUD's anchor (the W). With a gun, it fades near the gun's cross, which the game puts where that gun's mounting angle points, 3 km out. The missile, rocket, bomb and no-weapon displays switch it on when the gear is up and off when it's down; the gun display leaves it as it was.

The helmet display (`HMDCenter`) holds heading, roll, radar-altitude and speed panels laid out around the canvas middle, plus the map and the panels in the window's corners. It hides each of the four panels while the flight HUD's anchor is within a distance of it, measured in 3D, so a HUD that's moved off, or given a depth, shows them all.

## Objective markers

An objective or waypoint shows as a dot while it's within 10° of the view direction, and as an arrow beyond that, drawn at the objective and pointing away from the view center. Anything off the window, or more than 90° off the view, pins to the window's edge. On a triple rig, objectives on the wing screens are always more than 10° off the view, so they show arrows.

## Airbase capture panel

With a cargo or troop weapon selected, the HUD shows the nearest airbase's name and capture status: who's capturing or defending, and the percentage. It appears whenever the aircraft is slower than 20 m/s inside the base's area, rechecked once a second and faded in. The game lays it out near the top of the window, centered, under the helmet frame.

## Menu canvases

Found 2026-10-17 with `dump-ui`. Every menu is a screen-overlay canvas that covers the whole window. In the main menu scene that's `MainCanvas`, scaled with the screen against 1920 × 1080 in Expand mode. Its button column hangs from the left edge, the logo from the top-right corner, a news panel from the bottom left, and popups from the middle. The settings screen goes on a full-window overlay layer of the same canvas. In missions, `MenuCanvas` holds the pause menu and the leaderboard. `GameplayUICanvas` holds the loadout screen (`AircraftSelectionMenu`), a top ribbon split into thirds of the window's width (messages, faction and donate), dialogues and the virtual MFDs. `ChatCanvas` splits its top panel into thirds too, and `ExtraControlsCanvas` hangs the camera controls from the right edge. Those all scale with the screen against 1920 × 1080, matched on height. Rewired's control mapper has its own canvas, scaled with the screen against 2110 × 600, matched on width. The loadout screen's 3D backdrop is a camera, not the canvas.

## HUD corner panels

The helmet frame (`HMDCenter`, the `HeadMountedDisplay`) is a HUD-canvas piece the game sizes from its own HMD width setting, which defaults to the window's width at a 1,080-unit height. It holds the four helmet panels around its middle, plus two corner panels. `LowerLeftPanel` holds the overhead map and the threat list; `TopRightPanel` holds power, weapon and countermeasure status. The damage display is `LowerRightPanel`, directly on the HUD canvas, which the flight HUD exposes as its status anchor.
