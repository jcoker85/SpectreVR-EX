"""Build the game's data from your own copy of Spectre VR. Nothing from the original game is kept in
this repository: everything here is generated into git-ignored folders.

    python Tools/import_original.py <your Spectre VR folder> [--fluidsynth EXE] [--soundfont SF2] [--skip-music]

Writes Assets/Imported/Resources (menus, HUD, sounds, music, built-in shapes),
Assets/StreamingAssets/Matrices (the .MAT files and their pictures), Assets/StreamingAssets/Textures and
Assets/Editor/Data/SpectreShapes.json. Afterwards open the project in Unity and run
Spectre VR-EX > Build Arena Scene. Needs Python with Pillow, numpy and soundfile; music also needs
FluidSynth and a General MIDI SoundFont.
"""
import argparse
import glob
import io
import os
import shutil
import subprocess
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOLS = os.path.join(ROOT, "Tools")
RESOURCES = os.path.join(ROOT, "Assets", "Imported", "Resources")
MATRICES = os.path.join(ROOT, "Assets", "StreamingAssets", "Matrices")
TEXTURES = os.path.join(ROOT, "Assets", "StreamingAssets", "Textures")
MOVIES = os.path.join(ROOT, "Assets", "StreamingAssets", "Movies")
SHAPES_JSON = os.path.join(ROOT, "Assets", "Editor", "Data", "SpectreShapes.json")

# The game's pictures are PCX files named .DAT
PICTURES = {
    "Menu/MainMenu": "SPCP25", "Menu/Buttons": "SPCP35", "Menu/Matrix": "SPCP34", "Menu/About": "SPCP10",
    "Menu/Help": "SPCP14", "Menu/Custom": "SPCP13", "Menu/Options": "SPCP20", "Menu/Vehicles": "SPCP21",
    "HUD/WeaponIcons": "SPCP36",
}
MATRIX_FILES = ["CENTRAL.MAT", "CENTRAL.BIG", "CENTRAL.PCX", "BASIC.MAT"]


def find(pattern):
    hits = glob.glob(pattern, recursive=True)
    return hits[0] if hits else None


def require(path, what):
    if not os.path.exists(path):
        sys.exit(f"Missing {what}: {path}\nIs this a complete Spectre VR folder?")
    return path


def import_pictures(game):
    for name, dat in PICTURES.items():
        src = require(os.path.join(game, "DATA", dat + ".DAT"), "picture")
        out = os.path.join(RESOURCES, name + ".png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        with open(src, "rb") as f:
            Image.open(io.BytesIO(f.read())).convert("RGB").save(out)
    print(len(PICTURES), "pictures")


def import_sounds(game):
    out = os.path.join(RESOURCES, "Sounds")
    os.makedirs(out, exist_ok=True)
    wavs = glob.glob(os.path.join(require(os.path.join(game, "SOUNDS"), "sound folder"), "*.WAV"))
    for wav in wavs:
        shutil.copyfile(wav, os.path.join(out, os.path.basename(wav).replace("&", "")))
    print(len(wavs), "sounds")


def import_matrices(game):
    os.makedirs(MATRICES, exist_ok=True)
    for name in MATRIX_FILES:
        shutil.copyfile(require(os.path.join(game, name), "matrix file"), os.path.join(MATRICES, name))
    print(len(MATRIX_FILES), "matrix files")


def import_textures(game):
    os.makedirs(TEXTURES, exist_ok=True)
    pcx = glob.glob(os.path.join(require(os.path.join(game, "TEXTURES"), "texture folder"), "*.PCX"))
    for path in pcx:
        shutil.copyfile(path, os.path.join(TEXTURES, os.path.basename(path).upper()))
    print(len(pcx), "textures")


def import_movies(game):
    os.makedirs(MOVIES, exist_ok=True)
    for path in glob.glob(os.path.join(game, "MOVIES", "*.MOV")):
        subprocess.run([sys.executable, os.path.join(TOOLS, "qtmovie.py"), path, MOVIES], check=True)


def import_shapes(game):
    run = lambda script, *args: subprocess.run([sys.executable, os.path.join(TOOLS, script), *args], check=True)
    os.makedirs(os.path.dirname(SHAPES_JSON), exist_ok=True)
    run("export_shapes.py", os.path.join(game, "CENTRAL.MAT"), SHAPES_JSON)
    out = os.path.join(RESOURCES, "Shapes", "BuiltinShapes.json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    run("export_builtin_shapes.py", require(os.path.join(game, "SVRWIN.EXE"), "game program"), out)


def import_music(game, fluidsynth, soundfont):
    if not fluidsynth or not os.path.exists(fluidsynth):
        sys.exit("FluidSynth not found: pass --fluidsynth, or --skip-music to import everything else.")
    if not soundfont or not os.path.exists(soundfont):
        sys.exit("SoundFont not found: pass --soundfont, or --skip-music to import everything else.")
    subprocess.run([sys.executable, os.path.join(TOOLS, "fluid_render.py"), fluidsynth, soundfont,
                    os.path.join(game, "SOUNDS"), os.path.join(RESOURCES, "Music")], check=True)


def main():
    ap = argparse.ArgumentParser(description="Build the game's data from your own copy of Spectre VR.")
    ap.add_argument("game", help="your Spectre VR folder (the one with SVRWIN.EXE and CENTRAL.MAT)")
    ap.add_argument("--fluidsynth", default=find(os.path.join(TOOLS, "fluidsynth", "**", "fluidsynth.exe")))
    ap.add_argument("--soundfont", default=find(os.path.join(TOOLS, "GeneralUser-GS", "*.sf2")))
    ap.add_argument("--skip-music", action="store_true")
    a = ap.parse_args()
    import_pictures(a.game)
    import_sounds(a.game)
    import_matrices(a.game)
    import_textures(a.game)
    import_movies(a.game)
    import_shapes(a.game)
    if not a.skip_music:
        import_music(a.game, a.fluidsynth, a.soundfont)
    print("Done. Now open the project in Unity and run Spectre VR-EX > Build Arena Scene.")


if __name__ == "__main__":
    main()
