"""Render Spectre VR's .MID files to OGG with FluidSynth + a SoundFont (GeneralUser GS).

FluidSynth renders each file to a float WAV; this script then peak-normalises it,
trims trailing silence and encodes Vorbis OGG for Unity (Assets/Resources/Music).

    python fluid_render.py <fluidsynth.exe> <bank.sf2> <midi_dir> <out_dir> [names...]
"""
import os
import sys
import glob
import subprocess
import tempfile
import numpy as np
import soundfile as sf

SR = 44100


def main():
    exe, bank, src, dst = sys.argv[1:5]
    names = sys.argv[5:]
    os.makedirs(dst, exist_ok=True)
    tmpdir = tempfile.mkdtemp(prefix="fluid_")
    for f in sorted(glob.glob(os.path.join(src, "*.MID"))):
        base = os.path.splitext(os.path.basename(f))[0]
        if names and base not in names:
            continue
        clean = base.replace("&", "")
        wav = os.path.join(tmpdir, clean + ".wav")
        subprocess.run([exe, "-ni", "-q", "-g", "0.6", "-r", str(SR), "-O", "float", "-T", "wav",
                        "-F", wav, bank, f], check=True, stdout=subprocess.DEVNULL)
        audio, sr = sf.read(wav, dtype="float32")
        os.remove(wav)
        peak = float(np.max(np.abs(audio))) or 1.0
        audio = audio / peak * 0.89
        nz = np.where(np.max(np.abs(audio), axis=1) > 1e-3)[0]
        if len(nz):
            audio = audio[: nz[-1] + int(0.5 * sr)]
        out = os.path.join(dst, clean + ".ogg")
        # libsndfile's Vorbis encoder overflows the stack on big single writes; feed it in blocks
        with sf.SoundFile(out, "w", sr, audio.shape[1], format="OGG", subtype="VORBIS") as fh:
            for i in range(0, len(audio), 4096):
                fh.write(audio[i:i + 4096])
        print(f"{base} -> {clean}.ogg ({len(audio) / sr:.1f}s, peak was {peak:.2f})", flush=True)


if __name__ == "__main__":
    main()
