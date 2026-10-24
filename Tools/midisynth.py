"""Tiny General-MIDI-ish software synth: renders Spectre VR's .MID files to OGG.

Not a soundfont renderer - each GM program family maps to a simple synthesized
voice (additive, detuned saws, Karplus-Strong plucks, noise drums). Usage:
    python midisynth.py <in_dir> <out_dir> [names...]
"""
import os
import sys
import glob
import numpy as np
import mido
import soundfile as sf
from scipy.signal import lfilter, butter

SR = 32000
rng = np.random.default_rng(1993)


def midi_hz(n):
    return 440.0 * 2 ** ((n - 69) / 12.0)


def env_adsr(n, a, d, s, r, hold):
    """Sample envelope of length hold+release; a,d,r in seconds, s level."""
    total = hold + int(r * SR)
    e = np.empty(total)
    na, nd = max(1, int(a * SR)), max(1, int(d * SR))
    t = np.arange(total)
    e[:] = s
    e[:na] = np.linspace(0, 1, na)[: min(na, total)] if total >= na else np.linspace(0, 1, na)[:total]
    if total > na:
        k = min(nd, total - na)
        e[na:na + k] = 1 - (1 - s) * (np.arange(k) / nd)
    if hold < total:
        lvl = e[min(hold, total - 1)] if hold > 0 else 0
        rel = np.linspace(1, 0, total - hold)
        e[hold:] = np.minimum(e[hold:], lvl) * rel
    return e


def lowpass(x, cutoff):
    cutoff = min(cutoff, SR * 0.45)
    b, a = butter(2, cutoff / (SR / 2))
    return lfilter(b, a, x)


def highpass(x, cutoff):
    b, a = butter(2, cutoff / (SR / 2), btype="high")
    return lfilter(b, a, x)


def saw(f, n, phase=0.0):
    t = np.arange(n) / SR
    return 2 * ((t * f + phase) % 1.0) - 1


def square(f, n, duty=0.5):
    t = np.arange(n) / SR
    return np.where((t * f) % 1.0 < duty, 1.0, -1.0)


def sine(f, n):
    return np.sin(2 * np.pi * f * np.arange(n) / SR)


def karplus(f, n, decay=0.996, bright=0.5):
    period = max(2, int(SR / f))
    buf = rng.uniform(-1, 1, period)
    buf = lowpass(buf, 2000 + bright * 8000) if period > 8 else buf
    out = np.empty(n)
    # vectorised in chunks of one period
    for start in range(0, n, period):
        k = min(period, n - start)
        out[start:start + k] = buf[:k]
        buf = decay * 0.5 * (buf + np.roll(buf, -1))
    return out


def voice(program, note, hold):
    """Return mono samples for a melodic note held for `hold` samples."""
    f = midi_hz(note)
    fam = program // 8
    if fam == 0:  # piano
        n = hold + int(0.4 * SR)
        x = sum(sine(f * h, n) / h ** 1.3 for h in range(1, 7))
        e = np.exp(-np.arange(n) / (SR * (1.6 - note / 127)))
        e[hold:] *= np.linspace(1, 0, n - hold)
        return x * e * 0.5
    if fam == 1:  # chromatic percussion
        n = hold + int(0.6 * SR)
        x = sine(f, n) + 0.4 * sine(f * 2.76, n) + 0.2 * sine(f * 5.4, n)
        return x * np.exp(-np.arange(n) / (SR * 0.5)) * 0.45
    if fam == 2:  # organ
        x = sum(sine(f * h, hold + int(0.08 * SR)) / h for h in (1, 2, 3, 4, 6, 8))
        return x * env_adsr(0, 0.01, 0.05, 1.0, 0.08, hold) * 0.3
    if fam == 3:  # guitar
        n = hold + int(0.3 * SR)
        x = karplus(f, n, decay=0.998 if program < 29 else 0.999, bright=0.6)
        if program in (29, 30):  # overdrive / distortion
            x = np.tanh(x * 6) * 0.5
            x = lowpass(x, 4500)
        e = np.ones(n)
        e[hold:] = np.linspace(1, 0, n - hold)
        return x * e * 0.6
    if fam == 4:  # bass
        n = hold + int(0.1 * SR)
        x = 0.6 * saw(f, n) + 0.4 * square(f, n)
        x = lowpass(x, 300 + 1200 * np.exp(-0.0) )
        return x * env_adsr(0, 0.005, 0.25, 0.7, 0.1, hold) * 0.55
    if fam in (5, 6):  # strings, ensemble, choir
        n = hold + int(0.35 * SR)
        if program in (52, 53, 54):  # choir: vibrato sines
            vib = 1 + 0.004 * np.sin(2 * np.pi * 5 * np.arange(n) / SR)
            ph = 2 * np.pi * np.cumsum(f * vib) / SR
            x = np.sin(ph) + 0.3 * np.sin(2 * ph) + 0.15 * np.sin(3 * ph)
        else:
            x = saw(f * 0.997, n) + saw(f * 1.003, n, 0.3) + 0.5 * saw(f * 2.001, n, 0.6)
            x = lowpass(x, min(5000, f * 6))
        return x * env_adsr(0, 0.12, 0.2, 0.85, 0.35, hold) * 0.22
    if fam == 7:  # brass
        n = hold + int(0.15 * SR)
        x = saw(f, n) + saw(f * 1.005, n, 0.5)
        x = lowpass(x, min(6000, f * 8))
        return x * env_adsr(0, 0.04, 0.15, 0.8, 0.15, hold) * 0.25
    if fam in (8, 9):  # reed, pipe
        n = hold + int(0.12 * SR)
        x = sine(f, n) + 0.25 * sine(f * 2, n) + 0.04 * lowpass(rng.uniform(-1, 1, n), f * 2)
        return x * env_adsr(0, 0.05, 0.1, 0.9, 0.12, hold) * 0.35
    if fam == 10:  # synth lead
        n = hold + int(0.1 * SR)
        x = square(f, n, 0.5) * 0.6 + saw(f * 1.004, n) * 0.4
        x = lowpass(x, min(7000, f * 10))
        return x * env_adsr(0, 0.01, 0.1, 0.8, 0.1, hold) * 0.22
    if fam == 11:  # pads
        n = hold + int(0.6 * SR)
        x = saw(f * 0.995, n) + saw(f * 1.005, n, 0.5) + sine(f * 0.5, n)
        x = lowpass(x, min(3000, f * 4))
        return x * env_adsr(0, 0.35, 0.3, 0.9, 0.6, hold) * 0.18
    if fam == 13:  # ethnic (sitar etc)
        n = hold + int(0.5 * SR)
        x = karplus(f, n, decay=0.9985, bright=1.0)
        x = x + 0.3 * np.tanh(x * 3)
        return x * 0.5
    # fx, percussive, misc: soft saw
    n = hold + int(0.2 * SR)
    x = lowpass(saw(f, n), min(4000, f * 5))
    return x * env_adsr(0, 0.02, 0.1, 0.7, 0.2, hold) * 0.2


def drum(note, vel):
    if note in (35, 36):  # kick
        n = int(0.35 * SR)
        t = np.arange(n) / SR
        f = 45 + 110 * np.exp(-t * 30)
        x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 9)
        return x * 1.1
    if note in (38, 40, 37, 39):  # snare / clap / rim
        n = int(0.25 * SR)
        t = np.arange(n) / SR
        noise = highpass(rng.uniform(-1, 1, n), 1200) * np.exp(-t * 18)
        tone = np.sin(2 * np.pi * 190 * t) * np.exp(-t * 25)
        return (noise * 0.8 + tone * 0.5) * 0.8
    if note in (42, 44):  # closed / pedal hat
        n = int(0.06 * SR)
        return highpass(rng.uniform(-1, 1, n), 7000) * np.exp(-np.arange(n) / (SR * 0.015)) * 0.35
    if note == 46:  # open hat
        n = int(0.3 * SR)
        return highpass(rng.uniform(-1, 1, n), 6000) * np.exp(-np.arange(n) / (SR * 0.09)) * 0.3
    if note in (49, 52, 55, 57):  # crash
        n = int(1.4 * SR)
        return highpass(rng.uniform(-1, 1, n), 4000) * np.exp(-np.arange(n) / (SR * 0.45)) * 0.3
    if note in (51, 53, 59):  # ride
        n = int(0.7 * SR)
        t = np.arange(n) / SR
        return (highpass(rng.uniform(-1, 1, n), 5000) * 0.5 + 0.2 * np.sin(2 * np.pi * 3200 * t)) * np.exp(-t * 5) * 0.3
    if 41 <= note <= 50:  # toms
        n = int(0.35 * SR)
        t = np.arange(n) / SR
        base = 80 + (note - 41) * 18
        f = base * (1 + 0.5 * np.exp(-t * 20))
        return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 8) * 0.8
    n = int(0.12 * SR)  # misc percussion
    return highpass(rng.uniform(-1, 1, n), 2500) * np.exp(-np.arange(n) / (SR * 0.03)) * 0.3


def render(path):
    mid = mido.MidiFile(path)
    length = mid.length + 3.0
    out = np.zeros((int(length * SR), 2))
    program = [0] * 16
    volume = [100] * 16
    expression = [127] * 16
    pan = [64] * 16
    sustain = [False] * 16
    active = {}   # (ch, note) -> (start_sample, vel)
    held = {}     # notes released while sustain down: (ch,note) -> (start, vel)
    events = []   # (ch, note, vel, start, end)
    t = 0.0
    for msg in mid:
        t += msg.time
        s = int(t * SR)
        if msg.type == "program_change":
            program[msg.channel] = msg.program
        elif msg.type == "control_change":
            if msg.control == 7:
                volume[msg.channel] = msg.value
            elif msg.control == 11:
                expression[msg.channel] = msg.value
            elif msg.control == 10:
                pan[msg.channel] = msg.value
            elif msg.control == 64:
                down = msg.value >= 64
                if sustain[msg.channel] and not down:
                    for key in [k for k in held if k[0] == msg.channel]:
                        st, vel = held.pop(key)
                        events.append((msg.channel, key[1], vel, st, s, program[msg.channel], volume[msg.channel] * expression[msg.channel] / 127, pan[msg.channel]))
                sustain[msg.channel] = down
        elif msg.type == "note_on" and msg.velocity > 0:
            key = (msg.channel, msg.note)
            if key in active:
                st, vel = active.pop(key)
                events.append((msg.channel, msg.note, vel, st, s, program[msg.channel], volume[msg.channel] * expression[msg.channel] / 127, pan[msg.channel]))
            active[key] = (s, msg.velocity)
        elif msg.type in ("note_off", "note_on"):
            key = (msg.channel, msg.note)
            if key in active:
                st, vel = active.pop(key)
                if sustain[msg.channel] and msg.channel != 9:
                    held[key] = (st, vel)
                else:
                    events.append((msg.channel, msg.note, vel, st, s, program[msg.channel], volume[msg.channel] * expression[msg.channel] / 127, pan[msg.channel]))
    end = int(t * SR)
    for d in (active, held):
        for (ch, note), (st, vel) in d.items():
            events.append((ch, note, vel, st, end, program[ch], volume[ch] * expression[ch] / 127, pan[ch]))

    cache = {}
    for ch, note, vel, st, en, prog, vol, pn in events:
        hold = max(int(0.03 * SR), en - st)
        if ch == 9:
            key = ("d", note)
            if key not in cache:
                cache[key] = drum(note, vel)
            x = cache[key]
        else:
            # cache by quantised duration so repeated notes don't re-synthesise
            q = int(round(hold / (SR * 0.05)))
            key = (prog, note, q)
            if key not in cache:
                cache[key] = voice(prog, note, max(1, q) * int(SR * 0.05))
            x = cache[key]
        g = (vel / 127.0) ** 1.5 * (vol / 127.0)
        p = pn / 127.0
        L, R = np.cos(p * np.pi / 2), np.sin(p * np.pi / 2)
        a = st
        b = min(len(out), a + len(x))
        if b <= a:
            continue
        seg = x[: b - a] * g
        out[a:b, 0] += seg * L
        out[a:b, 1] += seg * R

    # light stereo room: two short feedback delays
    for d, fb in ((int(0.031 * SR), 0.25), (int(0.047 * SR), 0.2)):
        wet = np.zeros_like(out)
        wet[d:] = out[:-d] * fb
        out[:, 0] += wet[:, 1]
        out[:, 1] += wet[:, 0]

    peak = np.max(np.abs(out)) or 1.0
    out = np.tanh(out / peak * 1.2) * 0.89
    # trim trailing silence
    nz = np.where(np.max(np.abs(out), axis=1) > 1e-3)[0]
    if len(nz):
        out = out[: nz[-1] + int(0.2 * SR)]
    return out


def main():
    src, dst = sys.argv[1], sys.argv[2]
    names = sys.argv[3:]
    os.makedirs(dst, exist_ok=True)
    files = sorted(glob.glob(os.path.join(src, "*.MID")))
    for f in files:
        base = os.path.splitext(os.path.basename(f))[0]
        if names and base not in names:
            continue
        clean = base.replace("&", "")
        audio = render(f)
        # libsndfile's Vorbis encoder overflows the stack on big single writes; feed it in blocks
        with sf.SoundFile(os.path.join(dst, clean + ".ogg"), "w", SR, 2, format="OGG", subtype="VORBIS") as fh:
            for i in range(0, len(audio), 4096):
                fh.write(audio[i:i + 4096])
        print(f"{base} -> {clean}.ogg ({len(audio) / SR:.1f}s)", flush=True)


if __name__ == "__main__":
    main()
