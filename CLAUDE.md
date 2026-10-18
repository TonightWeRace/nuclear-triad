# Working on Nuclear Triad with Claude

Nuclear Triad was built with Claude Code (Opus 5.5 for code, Fable 5.1 for security review): a person at the rig flying, measuring and deciding, and Claude instrumenting the game and writing the code. The CLAUDE.md we work from stays with us, because it's full of our own machine, our people and our plans. This one is what we'd hand anyone who picks the project up or forks it, with Claude or any other agent: what we learned that mattered, what we'd always do, and what we'd never do again. Take what fits into your own.

## Read these first

- [docs/architecture.md](docs/architecture.md): how it works and why. Our own sessions load it every time.
- [docs/developing.md](docs/developing.md): the build, the dev loop, the control channel, and how we check seams.
- [docs/game-internals.md](docs/game-internals.md): what the game does underneath, in our own words.

## How we worked

- **One person flies; the agent instruments.** The control channel lets Claude dump cameras, canvases and the HUD, take captures, time frames, and read and change settings in the running game while the pilot keeps the controls. Most of what we know came from that loop: ask for a specific flight ("level over water", "a steady bank"), capture, measure, change one thing, fly it again.
- **One item at a time.** The person calls for the next piece of work. The agent proposes the rest instead of building it.
- **A tracker in the repo.** Status, decisions, findings, every change made to the machine with how to undo it, and a short log per session: what got done, the evidence, what's broken, what's next.
- **Small commits,** one change each.

## Lessons we paid for

Most of these cost us a build or more.

- **Support Ugly Ducklings.** Sim racing software demands hardware uniformity, but living in sim racing discords demonstrates very few drivers start there. This project does its best to make a coherent visual scene whether your monitors match or not.
- **Never bake rotation into the projection.** The camera's transform faces its screen; only the off-axis frustum goes into `projectionMatrix`.
- **Handedness.** Unity's view space is right-handed, and world and local space are left-handed. A sign slip mirrors the side views. Keep the maths in plain C# with no UnityEngine types, and write tests that would catch it; they run without the game.
- **Judging seams.** A correct render with toed-in screens kinks at the seams in the framebuffer. Judge seams in eye-space reprojections, and the physical result from the seat. Read crops, not full frames: a downscaled full frame hides a pixel's error.
- **Leave the main camera's transform alone.** The game reads its local angles back every frame. Parent your cameras to it instead.
- **Copy, don't hard-code.** Copy the main camera's settings onto your cameras before each render, and give every URP overlay its base camera's exact rect.
- **Late in, early out.** The game places its flight HUD in Update and moves the camera in LateUpdate, so the HUD trails a frame. Place things late in the frame (order 10000), and put everything back early in the next (order −10000), exactly as the game had it.
- **The floating origin moves canvases.** It carries them a world shift away and back before they draw. Never write a screen point as a UI world position; work out a piece's place in its canvas from where the canvas draws.
- **Place absolutely.** Never move a piece relative to where it sits: relative moves stacked on copies of an already moved HUD at a respawn. The aircraft can change under the same HUD, too, so read it fresh every frame.
- **`HideManagerGameObject`.** Nuclear Option needs BepInEx's `[Chainloader] HideManagerGameObject` on, or plugins load and never update. If nothing you write seems to run, check that first.
- **Builds that depend on the source alone.** Map paths to `/_/` and keep the commit out of the version and the PDB. Then a hash tells you whether a machine runs a given release.
- **Don't crash on a bad setting.** A layout the rig can't draw keeps the last one that worked, says so on screen, and recovers when the setting changes.

## Consider putting in your always

- Every behavior behind a config flag and a runtime toggle, with toggle-off equal to vanilla.
- One Harmony patch per file, each behind its own flag. Prefer a postfix, then a prefix; a transpiler last.
- Ask the person at the rig for measurements and decisions. Never guess geometry.
- Record a baseline before changing a Windows or Steam setting, and log every change with how to undo it.
- Ask before closing or restarting the game. Whoever's flying may be mid-flight, and a close looks like a crash from the cockpit.
- Keep tools inside the repo folder, pinned to versions and checksums, with no machine-wide installs.
- Read a release zip back before shipping it: every entry, byte for byte against its source.

## Consider putting in your nevers

- Never commit game binaries, game assets or decompiled code, in any form, snippets in notes included. Write down what you learn in your own words.
- Never add network code, or anything that changes gameplay or what other players see.
- Never copy code from another mod without reading its license. MIT code can be copied with its notice kept; GPL or unlicensed code is for learning the approach, not for copying.
- Never trust a full-frame screenshot to judge a seam.
- Never ship the control channel switched on.

## If you publish your own build

Give it your own name and BepInEx plugin GUID, so it can't be mistaken for this one or collide with it in a player's plugins folder. The license asks one thing in return: keep our copyright notice, and with it the credit to Tonight We Race! (see [LICENSE](LICENSE)).

Blue skies,
Tonight We Race! and Claude
