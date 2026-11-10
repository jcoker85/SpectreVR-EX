# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Spectre VR-EX is a modern engine for Spectre VR (1993), built in Unity 6.3 (6000.3.28f1) with HDRP. It plays the original game's content with ray-traced graphics, a widescreen HUD and frame-rate independent physics. The goal is feature parity with the original's rules, read from its decompiled Windows build, plus a short list of deliberate differences.

## Original game data is never committed

The repository and the release contain none of the original game. The game reads everything it takes from the original at runtime from a game data folder that the player's own copy is imported into: `GameData/` in the project for the editor (git-ignored), `GameData/` beside the exe for a build. The game is portable: `GameData.Home` is the exe's folder (the project folder in the editor), holding `GameData`, `UserData\Settings.txt` (settings, scores and saves, through `Prefs`; don't use `PlayerPrefs`, which writes the registry) and `Matrices`. Only if that folder can't be written to does it fall back to `persistentDataPath`. Nothing derived from the original may go under `Assets/`, because anything there can be built into the release; `BuildTools` refuses to build if the old importer's folders (`Assets/Imported/`, `Assets/StreamingAssets/{Matrices,Textures,Movies}/`, `Assets/Editor/Data/`, `Assets/Meshes/Spectre_*`) are back, and checks the finished build for the original's files. Under `Tools/`, the original game, Ghidra, DOSBox, FluidSynth and SoundFont folders are git-ignored too.

## Commands

Set up the game data: press Play (play mode always starts from the Setup scene, which asks for the Spectre VR folder when there's no game data), or run **Spectre VR-EX > Import Original Game...** and pick the folder with `SVRWIN.EXE`. Music needs FluidSynth in `Tools/fluidsynth` and the GeneralUser GS SoundFont in `Tools/GeneralUser-GS`; without them the import leaves music out. The import refuses an `SVRWIN.EXE` that's a different release from the one `Assets/Resources/BuiltinShapesLayout.json` was made from. **Spectre VR-EX > Open Game Data Folder** shows the result. No Python is needed except for the reverse-engineering tools.

In the Unity editor:

- **Spectre VR-EX > Build Arena Scene** (`ArenaBuilder.Build`): regenerates both scenes (`Setup`, `Arena`), the prefabs, materials, meshes and textures.
- **Spectre VR-EX > Build Windows EXE** (`BuildTools.BuildWindows`): builds to `Builds/Windows`, adds `Setup/` (FluidSynth and the SoundFont from `Tools/`), `Licenses/`, `LICENSE.txt` and `README.md`, and zips it all as `Builds/SpectreVR-EX-<version>-win64.zip`. The result is in `Builds/Windows/build.log`. The version is PlayerSettings' `bundleVersion`.

There are no automated tests, no lint and no CI. Changes are checked by compiling in Unity and playing, or by scripting checks in Play mode. There is no `.asmdef`, so game code compiles into `Assembly-CSharp` and editor code into `Assembly-CSharp-Editor`.

## Architecture

### The scene is generated

`Assets/Editor/ArenaBuilder.cs` builds `Assets/Scenes/Setup.unity`, `Assets/Scenes/Arena.unity` and `Assets/Prefabs` from scratch, and wires up `GameManager`'s references. Re-running it overwrites them, so a lasting scene, prefab or generated material change belongs in `ArenaBuilder`, not in a hand edit. `Setup` (build index 0, `Setup.FirstRunSetup`) goes straight on to `Arena` when the game data is in place, and otherwise runs the first-run import; run the exe with `-setup` to see it again. `Arena` is the game.

### Data flow

1. `Setup.OriginalImporter` turns the player's copy into the game data folder (`GameData.Root`), on a worker thread: menu and HUD pictures and textures as PCX, sounds as WAV, the matrices as they are, the movies decoded by `QuickTimeMovie`, the built-in shapes read from `SVRWIN.EXE` by `BuiltinShapeExporter`, and the MIDI soundtrack rendered to Ogg Vorbis by FluidSynth (twice: once to measure the peak and where the sound ends, which go into `import.txt`, then at the gain that peaks at 0.89). These files have no Unity types, so they can be compiled and checked outside Unity. Bump `OriginalImporter.Version` when the data layout changes, and players import again.
2. `GameData` finds and loads it at runtime: `Picture`, `Sound`, `Wav`, `MusicPlayer` (loads the Ogg, cuts it at the measured end), `BuiltinShapes`, `MatrixLibrary`, `MovieFile`, `ShapeTextures`.
3. `Matrix/` reads `.MAT` level files. `MatrixLibrary` finds them and `MatrixFile` parses them, then `ObstacleFactory` builds each level's obstacles from that matrix's own shapes, and `PlayerModel` the craft. `GameManager` falls back to generated levels (`LevelPlan`) when there is no matrix.

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

- Keep every original detail, however small, except the agreed modern items: HDRP and ray-traced graphics, the 16:9 HUD, smooth fixed-step physics, the modern options menu, modern controls and rebinding, 3D layered sound and the star skybox. The game must stay as close to the original as possible, so don't leave something out as unimportant.
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
- Build with `BuildTools.BuildWindows`. With Unity open, run it in the editor; a batch build needs Unity closed. Check `Builds/Windows/build.log` and the time on `Builds/Windows/Spectre VR-EX_Data/Managed/Assembly-CSharp.dll`. To test the first run, run the exe with `-setup`, or move `Builds/Windows/GameData` aside. Running the build from `Builds/Windows` leaves `GameData`, `UserData` and `Matrices` there; `BuildTools` leaves them out of the zip.

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:/Users/james/Unity/SpectreVR-EX" -executeMethod SpectreEX.EditorTools.BuildTools.BuildWindows -logFile "C:/Users/james/Unity/SpectreVR-EX/Temp/sx/build_batch.log"
```

Running the original (`Tools/win31/launch.ps1`, `drive.ps1`):

- Only `Press-DosKeys` (real key presses) reaches the game, and it refuses unless DOSBox-X is in front. The owner agreed to hands-off runs. Wait for the menu to appear before pressing keys.
- For demo runs set byte 534 of `Tools/win31/drive_c/SPECTRE/SPECTRVR.CFG` to 3 (textures off), keep a backup, and restore it afterwards.
- Mute it, and don't leave it running unattended.

Working style:

- Don't claim something works until it's been checked, and say plainly what hasn't been played. The owner tests by playing, so give them what to look for.
- Fix causes, and check that a change hasn't broken what sat on the old behaviour. Before removing a mechanism, look for what else relied on it.
