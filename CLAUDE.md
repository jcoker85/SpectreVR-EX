# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Spectre VR-EX is a modern engine for Spectre VR (1993), built in Unity 6.3 (6000.3.27f2) with HDRP. It plays the original game's content with ray-traced graphics, a widescreen HUD and frame-rate independent physics. The goal is feature parity with the original's rules, read from its decompiled Windows build, plus a short list of deliberate differences.

## Original game data is never committed

The repository contains none of the original game. Everything generated from a user's copy is git-ignored: `Assets/Imported/`, `Assets/StreamingAssets/{Matrices,Textures,Movies}/`, `Assets/Editor/Data/`, `Assets/Meshes/Spectre_*`, and under `Tools/`, the original game, Ghidra, DOSBox and FluidSynth folders. When adding imported content, write it to an ignored location and extend `.gitignore` if needed.

## Commands

Set up the game data (needs Python 3 with `pip install pillow numpy soundfile`; music also needs FluidSynth and a General MIDI SoundFont, looked for in `Tools/fluidsynth` and `Tools/GeneralUser-GS`):

```bash
python Tools/import_original.py "path/to/Spectre VR" [--fluidsynth EXE] [--soundfont SF2] [--skip-music]
```

The importer stops if `SVRWIN.EXE` is a different release from the one `Tools/builtin_shapes_layout.json` was made from.

In the Unity editor:

- **Spectre VR-EX > Build Arena Scene** (`ArenaBuilder.Build`): regenerates the scene, prefabs, materials, meshes and textures.
- **Spectre VR-EX > Build Windows EXE** (`BuildTools.BuildWindows`): builds to `Builds/Windows`, with the result in `Builds/Windows/build.log`.

There are no automated tests, no lint and no CI. Changes are checked by compiling in Unity and playing, or by scripting checks in Play mode. There is no `.asmdef`, so game code compiles into `Assembly-CSharp` and editor code into `Assembly-CSharp-Editor`.

## Architecture

### The scene is generated

`Assets/Editor/ArenaBuilder.cs` builds `Assets/Scenes/Arena.unity` and `Assets/Prefabs` from scratch, and wires up `GameManager`'s references. Re-running it overwrites them, so a lasting scene, prefab or generated material change belongs in `ArenaBuilder`, not in a hand edit. `Arena` is the only scene.

### Data flow

1. `Tools/import_original.py` turns the user's copy into Unity-readable files.
2. `ShapeLibrary` (editor) reads `SpectreShapes.json` to make the player, robot and flag meshes at build time.
3. `Matrix/` reads `.MAT` level files at runtime. `MatrixLibrary` finds them and `MatrixFile` parses them, then `ObstacleFactory` builds each level's obstacles from that matrix's own shapes. `GameManager` falls back to generated levels (`LevelPlan`) when there is no matrix.

### Simulation

`SimClock` is the only game clock, and the frame rate must never change speed or results. It runs fixed sub-steps of 1/(18.2065 x 8) s, about 146 Hz:

- The player (`PlayerMotor.SubStep`) moves on every sub-step.
- Every 8th sub-step is an original tick. `RobotDirector.SimTick` runs all robots in spawn order, then all shots (`ShotSystem`), then `GameManager.SimTick` runs the level timers. This order is the original's.
- Each frame draws between the last two steps (`Present` / `Interpolate`).

Don't put gameplay in `Update` or scale it by `Time.deltaTime`; hook into the tick. Damage, sounds and timers go by whole ticks.

`GameManager` holds session rules (lives, score, levels, bonus, respawns) and is the hub the other systems reach through `GameManager.Instance`. `MainMenu` starts games through `GameManager.StartGame`.

### Original units

Gameplay code works in the original's integer units, not metres, and keeps its fixed-point arithmetic and truncation: positions are 32-bit with the arena at ±0x40000, headings are one byte (256 steps), and 1 grid unit is 0x200 units, or 0.16 m (`GameManager.GridScale`). `OriginalMath` has the trig tables, `_ANGLE`, `_FIX_HEADING`, the distance measures and the conversions to Unity space. `PlayerMotor` and the robots use different heading frames; see `PlayerMotor.OriginalHeading`.

## Working against the original

`docs/original-mechanics.md` is the reference for how the original behaves, what was measured, and why the remake differs. Read the relevant section before changing gameplay. `docs/still-to-validate.md` lists what can only be settled by playing the original.

- Gameplay rules come from the decompile, `Tools/ghidra_out/SVRWIN.EXE.c`, which has the real function names. Don't guess a rule from the manual or from memory. To read one function:

  ```bash
  awk -v n=_NAME '/^\/\/ ==== /{p=($3==n)} p' Tools/ghidra_out/SVRWIN.EXE.c
  ```

- Comments cite the original's names and values (`_DAMAGE_TIMER: 0x9C3 at 0x32 a tick`). Keep doing this, and keep values in hex where the original's are, so code can be checked against the decompile.
- Differences from the original are the owner's decisions, not bugs. They are listed under "Modern enhancements" and "Deliberate fixes to the original's intent" in the mechanics doc, with others noted in place (a failed hyperspace destroys the ship, following the manual). Where the original looks like an oversight rather than a design, the remake does what it evidently meant. Add new differences to those lists.
- Update the mechanics doc when you learn something new about the original. Its early sections and "Port plan" predate some of the code, so check the code before trusting what they call wrong or unfinished.
- The Ghidra and DOSBox tooling is described in the doc's "Tooling" section and in `docs/still-to-validate.md`.
- Ghidra often drops arguments and can misread fixed-point code. When a call's arguments matter, read the machine code in `Tools/dosbox_run/SPECTRE.VR/SVRWIN.EXE`. It's an NE file: segment n = (0x1xx0 - 0x1000) / 8 + 1, and the autodata (segment 41) is at file offset 0x1A800. `Tools/ghidra_out/` also has the function list and symbols.
- The reference edition is the Windows one, `SVRWIN.EXE`. Check for a `*W.DAT` picture before redrawing any art, since Options is SPCP20W, not SPCP20.
- Prove a value before stating it. Reading the code isn't always enough: `_P_TO_EYE` reads as twice the focal length that's actually on screen. Where it matters, measure the running original: `Tools/win31/memtrace.py` has the helpers for reading its memory, and `Tools/win31/captures` holds screenshots. Say what's measured, what's only read from the code, and what's unverified.

## Owner's rules

What stays faithful:

- Keep every original detail, however small, except the agreed modern items: HDRP and ray-traced graphics, the 16:9 HUD, smooth fixed-step physics, QoL such as resuming from the menu, the modern options menu, modern controls and rebinding, 3D layered sound and the star skybox. The game must stay as close to the original as possible, so don't leave something out as unimportant.
- Where the manual and the decompile disagree, ask the owner.
- List each evident original bug you fix to its intent under "Deliberate fixes to the original's intent", and tell the owner.
- Don't re-raise settled decisions: harmless-robot contact is silent, flags stay lit on the hidden map, Skybox is on/off with stars only, and the Quickness rating is inverted on purpose. The mechanics doc has the others.

Git and files:

- Commit only when the owner asks, and push only when told to, each time. Work goes straight on `main`.
- Commit only your own files. Leave `Spectre VR DOS.lnk`, `Spectre VR-EX.lnk` and `Assets/Real Stars Skybox` uncommitted, and leave the owner's own settings changes alone unless told otherwise.
- Never regenerate `Assets/Settings/ArenaVolume.asset`, which is hand-tuned. At runtime, change a copy through `vol.profile`.
- `git status` before running Build Arena Scene: it can overwrite uncommitted generated assets.
- Back up before bulk or unfamiliar file operations. Ask before downloading any binary, and don't `pip install` without asking.
- This workspace may only read under `C:/Users/james/IdeaProjects`, `C:/Users/james/Unity` and `.claude`. The home folder is OneDrive on-demand. Never run an unscoped whole-drive search.
- Don't spawn agents or workflows unless the owner asks.

Unity:

- Check Play state in a separate step before editing scripts, because a mid-Play recompile wipes statics. Never batch the check with the edit.
- Unity in the background doesn't compile. Call `AssetDatabase.Refresh`, then wait until `Library/ScriptAssemblies/Assembly-CSharp.dll` is newer than the edited file before pressing Play. Entering Play mid-compile gives errors that look like real bugs. Compile errors are in `Library/Bee/tundra.log.json`.
- "Unity not detected" from the MCP is normal during a domain reload, and the first enter-Play after a compile is often dropped.
- Play stops when the owner is active, and InputSystem events queued inside Unity don't work without focus. Don't send synthetic OS keystrokes while the owner is active unless they've agreed.
- Build with `BuildTools.BuildWindows`. With Unity open, run it in the editor; a batch build needs Unity closed. Check `Builds/Windows/build.log` and the time on `Builds/Windows/Spectre VR-EX_Data/Managed/Assembly-CSharp.dll`.

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.27f2/Editor/Unity.exe" -batchmode -quit -projectPath "C:/Users/james/Unity/SpectreVR-EX" -executeMethod SpectreEX.EditorTools.BuildTools.BuildWindows -logFile "C:/Users/james/Unity/SpectreVR-EX/Temp/sx/build_batch.log"
```

Running the original (`Tools/win31/launch.ps1`, `drive.ps1`):

- Only `Press-DosKeys` (real key presses) reaches the game, and it refuses unless DOSBox-X is in front. The owner agreed to hands-off runs. Wait for the menu to appear before pressing keys.
- For demo runs set byte 534 of `Tools/win31/drive_c/SPECTRE/SPECTRVR.CFG` to 3 (textures off), keep a backup, and restore it afterwards.
- Mute it, and don't leave it running unattended.

Working style:

- Don't claim something works until it's been checked, and say plainly what hasn't been played. The owner tests by playing, so give them what to look for.
- Fix causes, and check that a change hasn't broken what sat on the old behaviour. Before removing a mechanism, look for what else relied on it.
