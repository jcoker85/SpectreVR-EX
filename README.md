# Spectre VR-EX

A modern engine for Spectre VR (1993), built in Unity HDRP. It plays the original game's content with
ray-traced graphics, a widescreen HUD and frame-rate independent physics.

**This repository contains none of the original game.** You need your own copy, and the game's data is
generated from it locally. Nothing imported is ever committed.

## Setup

1. Install Python 3 with `pip install pillow numpy soundfile`.
2. For music, get [FluidSynth](https://www.fluidsynth.org/) and a General MIDI SoundFont. The importer looks
   in `Tools/fluidsynth` and `Tools/GeneralUser-GS`, or pass `--fluidsynth` and `--soundfont`.
3. Import from your Spectre VR folder (the one with `SVRWIN.EXE` and `CENTRAL.MAT`):

   ```
   python Tools/import_original.py "path/to/Spectre VR"
   ```

   Add `--skip-music` to leave the music out.
4. Open the project in Unity 6.3 and run **Spectre VR-EX > Build Arena Scene**.

The built-in shapes are read using a layout made from one specific `SVRWIN.EXE`; the importer stops if yours
is a different release.
