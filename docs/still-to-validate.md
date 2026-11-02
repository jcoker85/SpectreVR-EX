# Still to validate on the original

Gameplay is mostly done. These need someone playing the original in DOSBox, because they can't be settled from the code or the level data. Measured results are at the end of [original-mechanics.md](original-mechanics.md).

To record a real game from memory while playing, run `python Tools/win31/memtrace.py <seconds> <out.jsonl>`, then `python Tools/win31/traceview.py <out.jsonl>`.

## Most likely to find real differences

The rules below are ported line by line from the decompile, so what's left is seeing them run. All of these appear in the bundled levels.

- [ ] **Later robot types:** Cyber Mud and Orbiters are still unmeasured. Measured on the running original (Hard level 49, 2026-11-02) and matching the ported rules: Flyers (speed, climb to 0xF00, dive to 0x200-0x400, leaving and chasing), Smart Acid and Smart Walls (speed, standing still more than half the time), Invisible and Cloaked Tanks (speed).
- [x] **Firing rhythm:** measured on the same run. Super Tanks, Robot Tanks, Cloaked Tanks, Flyers (Seekers) and Invisible Tanks (mines) fire on the rule's minimum gap or one tick after it, which is the sampling.
- [ ] **Hard level 49 loads seven robots, not ten:** the original added only the first seven of the level's list (no Orbiter, Cyber Mud or Slicer) on two runs. The matrix file is byte-identical to the remake's, the tank tables hold 12 and the object limit would stop the game instead, so the cause is still unknown. The remake loads all ten. Worth finding: it may be a limit the remake should share.

## Quick side-by-side looks

- [ ] **Complex off:** the plain shape set has only been loaded, never looked at. It's the only thing here the code can't settle, because it comes down to how the shapes look.

## Settled, no check needed

- **Teleport facing:** read from `_HIT_A_GATE`. It comes from the same working as the pusher's direction, and that one was seen on the running original.
- **Floor dots:** `_DRAW_DOTS` gives lines 0x10000 apart and dots 0x2000 apart at level 1, halving each level, none at 0, and the remake builds the same.
- **Death camera:** `_PLAY_LEVEL` gives heading `_DEAD_TIMER` >> 4, distance (timer + 15) * 32 and height timer * 0.75 * 16, and the remake uses the same numbers.
- **Hidden map:** colours are in the machine code of `_DRAW_GOD_VIEW`, and typing G, O, D is in `_PLAY_LEVEL`.
- **Under-floor transports:** up transports (107) and down transports (71) are in the bundled levels, and a down transport was scripted sinking to -0xF00. Nothing about them is unknown, they just get checked as part of normal play.
- **Bouncer and tank type 6 as enemies:** neither is used by any of the 192 levels in the bundled matrix, so they never appear. A custom matrix would be the only way to meet one.
- **Fresh-clone build errors:** a NullReferenceException and a "Pipe is broken" line were logged once. They haven't appeared since, and the Windows build now completes with no errors.
