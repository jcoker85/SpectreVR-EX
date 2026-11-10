# Spectre VR-EX

A modern engine for **Spectre VR** (1993), built in Unity 6.3 with the High Definition Render Pipeline. It
plays the original game's own levels, robots, pictures, sounds and music with ray-traced graphics, a
widescreen HUD and smooth, frame-rate independent movement, while keeping the original's rules. Those rules
are taken from the original Windows program itself, not from memory or the manual.

**Spectre VR-EX contains none of the original game.** You need your own copy of Spectre VR. The first time
you run Spectre VR-EX, it builds its game data from that copy on your machine.

Version 0.1 is the first release. See [Known limits in 0.1](#known-limits-in-01).

## Contents

- [Download and install](#download-and-install)
- [The game](#the-game)
- [Controls](#controls)
- [Menus and settings](#menus-and-settings)
- [Differences from the original](#differences-from-the-original)
- [Building it yourself](#building-it-yourself)
- [AI disclosure](#ai-disclosure)
- [Credits and licences](#credits-and-licences)

## Download and install

### What you need

- Windows 10 or 11, 64-bit.
- A DirectX 12 graphics card. Ray tracing needs one that supports DirectX Raytracing (NVIDIA RTX, or AMD
  Radeon RX 6000 and newer). Ray tracing can be turned off in the settings, but 0.1 hasn't been tested on a
  card without it.
- Your copy of **Spectre VR, Windows edition**: the folder with `SVRWIN.EXE`, `CENTRAL.MAT` and the `DATA`,
  `SOUNDS`, `TEXTURES` and `MOVIES` folders in it. Copy it off the CD onto your hard drive first if you
  like. Spectre VR-EX reads it once and never changes it.

  If you don't have it, the Internet Archive has the game:
  [Spectre VR (1993)](https://archive.org/details/msdos_Spectre_VR_1993). Download `Spectre_VR_1993.zip`
  and unzip it. Its `SpectreV` folder is the one setup wants (it has `SVRWIN.EXE` in it, despite the "MS-DOS"
  in the page's name), and it's the release Spectre VR-EX was made from.

Spectre VR-EX reads the shapes built into `SVRWIN.EXE` from where they sit in that program, so it needs
the same release of `SVRWIN.EXE` it was made from. If yours is a different release, setup tells you so and
stops.

### Install

1. Download `SpectreVR-EX-0.1.0-win64.zip` from the
   [Releases page](https://github.com/jcoker85/SpectreVR-EX/releases).
2. Unzip it anywhere you can write to, for example `C:\Games\Spectre VR-EX` (not Program Files). Keep the
   `Setup` folder beside the game: it holds what the first run uses to make the music.
3. Run `Spectre VR-EX.exe`.

### First run

The first time, Spectre VR-EX opens a setup screen instead of the game:

1. Click **Browse...** and choose your Spectre VR folder (the one with `SVRWIN.EXE` in it), or type or
   paste its path. Picking the folder just above it, or the CD's root, works too. Setup looks one or two
   folders down.
2. When setup says **Found Spectre VR**, click **Build game data and play**.
3. Setup copies the pictures, sounds, levels and textures, decodes the movies, reads the shapes built into
   `SVRWIN.EXE`, and renders the MIDI soundtrack to audio with FluidSynth and the GeneralUser GS SoundFont.
   This takes about half a minute.
4. Then the game starts with the original's startup movie and main menu.

After that, Spectre VR-EX starts straight into the game.

Spectre VR-EX is portable: everything it makes stays in its own folder, beside `Spectre VR-EX.exe`.

| Folder | What's in it |
|---|---|
| `GameData` | The game data setup built from your copy of Spectre VR |
| `UserData` | `Settings.txt`: your settings, controls, high scores and saved games |
| `Matrices` | Your own `.MAT` matrices, if you add any (see below) |

To move the game, or take it to another PC, move the whole folder. If the folder can't be written to (in
Program Files, for example), Spectre VR-EX keeps these in
`%USERPROFILE%\AppData\LocalLow\Spectre VR-EX\Spectre VR-EX` instead. Unity itself also writes its log,
`Player.log`, to that folder.

To run setup again, for example after moving your copy of Spectre VR, start the game with `-setup`:

```bash
"Spectre VR-EX.exe" -setup
```

To uninstall, delete the folder you unzipped. To remove Unity's log too, delete
`%USERPROFILE%\AppData\LocalLow\Spectre VR-EX`.

## The game

You drive a Spectre, a hover tank, around a walled cyberspace arena. **On each level, collect every flag
while the robots hunt you.** Once the last flag is taken, the level ends with a recap and a bonus for the
time left. Then comes the next level, with more and tougher robots and new terrain. The game lasts as long
as your lives do. Extra lives come with points.

- **Shields and ammo:** hits wear down your shields. When they're gone, you lose a life. Your cannon uses
  ammo. Drive through an **ammo dump** to restock. Some dumps also repair you, add an orbiting shield, or give
  you wings so you float for a while. A used dump moves to a new hidden spot.
- **Heavy weapons:** grenades, scatter shots, seekers, missiles, spinners, mines, bombs and pulse bursts
  come online level by level. Each one costs ammo.
- **Robots:** tanks, super tanks, turrets, slicers, flyers, smart acid, smart walls, cyber mud, orbiters and
  more. Each has its own behaviour, taken from the original.
- **Terrain:** jump pads, teleports, ice, acid, pushers and transports, as each level's matrix defines them.
- **Vehicles:** before a game, choose Balance, Speedy or Strong, or design a Custom Spectre with your own
  mix of speed, shields and ammo.
- **Difficulty:** Easy, Normal or Hard, in Options.
- **Matrices:** the game plays Central Systems, the matrix that came with Spectre VR. With **Use Custom
  Matrix Files** ticked in Options, it also offers `.MAT` files from a `Matrices` folder next to the game,
  such as ones made with the original VRchitect editor.

Leave the main menu alone and the demo plays itself, as in the original.

## Controls

Every binding below can be changed. Hold **Shift** and choose **Options** on the main menu, then **Controls**.
Each action takes up to three keys or mouse buttons, plus gamepad buttons.

| Action | Keyboard | Mouse | Gamepad |
|---|---|---|---|
| Drive forward / back | W / S, Num 8 / Num 2 | | Left stick |
| Turn left / right | A / D, Num 4 / Num 6 | (see Mouse mode) | Left stick |
| Diagonals | Q, E, Z, C, Num 7, 9, 1, 3 | | Left stick |
| Fire cannon | Space, Num 0 | Left button | RT, A |
| Fire heavy weapon | Tab | | X |
| Next heavy weapon | Ctrl | | LB, RB |
| Pick and fire heavy weapon 1-8 | 1-8 | | |
| Jump | F, Num 5 | Right button | B |
| Hyperspace | Left Alt | | Y |
| Look left / right | R / T | | |
| Next view (cockpit, behind, overhead) | V, Num * | | |
| Zoom in / out | = / -, Num + / Num - | | |
| Pause | P | | Start |
| Quit to the menu | Esc | | Select |
| Music on or off | M | | |
| Sound on or off | F2 | | |
| Floor dots | F3 | | |
| Wireframe or filled | F4 | | |
| Textures / movie faces | F5 / F6 | | |

**Mouse mode** (Controls page, MOUSE) is off by default, so the mouse only fires and jumps. Set it to
**Capped** to turn with the mouse at no more than the turn keys' rate, **Uncapped** to turn as fast as the
mouse moves, **Free** to also aim and look up and down, or **Spectator** to look anywhere while driving. The
gamepad's right stick works the same way. Mouse speed, stick speed and invert are on the same page.

The original's hidden map still works: type G, O, D while playing.

## Menus and settings

The main menu is the original's own artwork. Its **Options** page has the original settings, each with its
original key:

- **Sound** on or off, and **Music** styles: Cyber/Techno, New Age, Rock/Rave and VRBach. Each level plays
  its matrix's song if its style is ticked.
- **Difficulty:** Easy, Normal or Hard.
- **Use Custom Matrix Files.**
- **Display:** Complex (detailed shapes), Filled (solid faces or wireframe), Textures and Movies (the
  animated faces).

**Shift + Options** opens the modern settings:

- **Graphics:** resolution, display mode (windowed, borderless or exclusive full screen), quality preset,
  star skybox and its brightness, anti-aliasing (TAA or DLSS, with DLSS scale and sharpness), motion blur,
  and ray tracing. Shadows, reflections, global illumination and ambient occlusion each have their own
  switch, quality, distance and sample settings. Global illumination and reflections each have a Full or
  Mixed trace mode. On a slower card, Mixed, fewer samples or fewer bounces cost much less.
- **Controls:** rebinding, mouse mode, sensitivity and invert, as above.

## Differences from the original

Spectre VR-EX keeps every rule and detail of the original it can, apart from these deliberate changes.

**Modern enhancements**

- Ray-traced HDRP graphics with DLSS, TAA, motion blur and a star skybox, and the HUD laid out for 16:9.
- Smooth movement: the game runs the original's fixed ticks (18.2 a second, with the player's own movement
  at 8 steps a tick), and each frame draws between them, so speed and results never depend on the frame
  rate.
- 3D, layered sound: robots, shots and explosions sound from where they are, and sounds overlap. The
  original plays one sound at a time with no position.
- Modern controls: rebinding for keyboard, mouse and gamepad, and the mouse modes above. The original has
  no mouse look.
- The soundtrack is the original's MIDI files, rendered with the GeneralUser GS SoundFont.
- The modern settings menu (Shift + Options).
- Network play (CyberNet) isn't available.

**Fixes to the original's evident intent**

Where the original looks like an oversight rather than a design, Spectre VR-EX does what it evidently meant.
For example:

- The level bonus and timer run through the whole level, so dying doesn't refill the bonus.
- The Quickness rating rewards a fast finish. The original's formula rated a slow finish Perfect.
- Robots start out of sight of where you actually start, not where your wreck was.
- A wreck is out of the game: it can't keep driving, firing or collecting flags.
- Grenade and bomb blasts stun only the robots in range, not the whole map.
- Robots hitting robots score nothing for you.
- Collision boxes turn with the object, so turned obstacles don't block at empty corners.
- Turrets don't overflow into firing every tick at the edge of their range.

The full list, with the original's code behind each one, is in
[docs/original-mechanics.md](docs/original-mechanics.md) under "Modern enhancements" and "Deliberate fixes
to the original's intent".

## Known limits in 0.1

- Single player only.
- Windows only.
- Needs the same release of `SVRWIN.EXE` it was made from (see [What you need](#what-you-need)).
- Not yet tested on graphics cards without ray tracing.
- Some details can only be settled by playing the original side by side; they're listed in
  [docs/still-to-validate.md](docs/still-to-validate.md).

## Building it yourself

> **Note:** a build made from this repository has no star skybox. The skybox in the release comes from an
> Asset Store package that can't be shared here, so the Skybox setting does nothing unless you import the
> package yourself (step 1 below). Everything else is the same as the release.

### What you need

- **Unity 6.3** (6000.3.28f1) with Windows build support, from the Unity Hub.
- **Your copy of Spectre VR**, Windows edition, as above.
- **For music:** [FluidSynth](https://github.com/FluidSynth/fluidsynth/releases) (the Windows x64 release,
  unzipped into `Tools/fluidsynth`) and the
  [GeneralUser GS](https://schristiancollins.com/generaluser.php) SoundFont (unzipped into
  `Tools/GeneralUser-GS`). Without them, the game builds and plays with no music. A release build copies
  both into its `Setup` folder.
- **For the star skybox:** the free [Real Stars Skybox Lite](https://assetstore.unity.com/packages/3d/environments/sci-fi/real-stars-skybox-lite-116333)
  package from the Unity Asset Store, imported into the project. Its licence doesn't allow it in a public
  repository, so it isn't included. **Build Windows EXE** copies its plain-stars cubemap into
  `Assets/Resources/Skybox`, which git ignores. Without it, the Skybox setting does nothing.

### Steps

1. Clone the repository and open it in Unity 6.3.
   - For the star skybox, add [Real Stars Skybox Lite](https://assetstore.unity.com/packages/3d/environments/sci-fi/real-stars-skybox-lite-116333)
     to your Unity account. Then, in **Window > Package Manager > My Assets**, download it and import it. It
     goes into `Assets/Real Stars Skybox`, which git ignores. **Build Windows EXE** copies
     `StarSkybox04/CubemapStars.cubemap` from it to `Assets/Resources/Skybox/Stars.cubemap`, where the game
     loads it. To see the skybox in the editor before your first build, copy that file there yourself. If
     the package isn't imported, everything still builds and runs, and the Skybox setting does nothing.
2. Run **Spectre VR-EX > Build Arena Scene** once. It generates the `Setup` and `Arena` scenes, prefabs,
   materials and meshes. Re-run it whenever `Assets/Editor/ArenaBuilder.cs` changes.
3. Import your game data: run **Spectre VR-EX > Import Original Game...** and choose your Spectre VR folder.
   You can also just press Play, because play mode starts from the setup screen when there's no game data.
   In the editor the data goes into the project's `GameData` folder, which git ignores. **Spectre VR-EX >
   Open Game Data Folder** shows it.
4. Press Play.
5. To make a release, run **Spectre VR-EX > Build Windows EXE**. It builds into `Builds/Windows`, adds
   `Setup` (FluidSynth and the SoundFont), `Licenses`, `LICENSE.txt` and this README, checks that no original game files
   ended up in the build, and zips it all as `Builds/SpectreVR-EX-<version>-win64.zip`. The result is in
   `Builds/Windows/build.log`. The version comes from Player Settings.

You can also build from the command line, with Unity closed:

```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.28f1/Editor/Unity.exe" -batchmode -quit -projectPath "path/to/SpectreVR-EX" -executeMethod SpectreEX.EditorTools.BuildTools.BuildWindows -logFile build_batch.log
```

### How the project is laid out

- `Assets/Scripts`: the game. `GameManager` runs a session, `SimClock` is the game clock, and
  `PlayerMotor`, `Robots/` and `Matrix/` hold the original's rules. `Setup/` is the first-run import and the
  game data loaders.
- `Assets/Editor`: the scene builder, the build script and the editor import.
- `Assets/Resources/BuiltinShapesLayout.json`: where the shapes built into `SVRWIN.EXE` sit in that
  program. These are only addresses and counts; the shapes themselves are read from your copy.
- `docs/original-mechanics.md`: how the original behaves, what was measured, and why the remake differs.
- `Tools/`: the reverse-engineering scripts (Ghidra) used to read the original. You don't need them to
  build or play.

Original game data must never go under `Assets/`, because anything there can be built into a release. See
[CLAUDE.md](CLAUDE.md) for the conventions the code follows.

## AI disclosure

Spectre VR-EX is built with AI help. Most of its code, tools and documentation were written by
[Claude](https://www.anthropic.com/claude), Anthropic's AI model, working in
[Claude Code](https://claude.com/claude-code). Every commit in this repository was made that way, and each
one says so in a `Co-Authored-By: Claude` line.

The project's owner directs the work and makes the decisions. That covers what to build, how faithful to
stay, which differences from the original to allow, and how it should look, sound and feel. The owner also
reviews the changes and tests them by playing.

The game's rules aren't guessed from memory, by a person or by the AI. They're read from the decompiled
original, and where that isn't enough, measured by running the original game.
[docs/original-mechanics.md](docs/original-mechanics.md) records what was read, what was measured and what
is still unverified.

No AI image, sound or music generator made any of the game's art or audio. The pictures, sounds, music,
levels, shapes and movies are the original's, built from your own copy. The new parts, such as the settings
screens and the HUD's layout, are drawn by the game's code with the DSEG font. The other assets are credited
below.

## Credits and licences

Spectre VR-EX's own code is released under the [MIT licence](LICENSE). That covers this project only: not
the original game, and not the third-party works below, which keep their own licences.

- **Spectre VR** (1993) is the original game. Its content belongs to its rights holders. Spectre VR-EX isn't
  affiliated with them, and plays only the content from a copy you provide.
- **FluidSynth** (LGPL 2.1), with libsndfile (LGPL 2.1) and SDL3 (zlib), renders the soundtrack during
  setup.
- **GeneralUser GS** by S. Christian Collins is the SoundFont the soundtrack is rendered with.
- **DSEG** by Keshikan (SIL Open Font License 1.1) is the font of the HUD and menus.
- The **star skybox** in the release is from **Real Stars Skybox Lite**, a free package on the Unity Asset
  Store, used under the Asset Store licence. Its stars come from the HYG star database by David Nash
  ([astronexus.com](https://astronexus.com)), licensed under
  [CC BY-SA 2.5](https://creativecommons.org/licenses/by-sa/2.5/). It isn't in this repository: see
  [Building it yourself](#building-it-yourself).
- Built with **Unity** and the High Definition Render Pipeline. The Unity runtime includes **NVIDIA DLSS**
  and Microsoft **DirectML**.

The licence texts are in [Licenses](Licenses), and in the `Licenses` folder of a release.
