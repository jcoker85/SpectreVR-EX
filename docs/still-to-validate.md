# Still to validate on the original

Gameplay is mostly done. These need someone playing the original in DOSBox, because they can't be settled from the code or the level data. Measured results are at the end of [original-mechanics.md](original-mechanics.md).

To record a real game from memory while playing, run `python Tools/win31/memtrace.py <seconds> <out.jsonl>`, then `python Tools/win31/traceview.py <out.jsonl>`.

## Most likely to find real differences

The rules below are ported line by line from the decompile, so what's left is seeing them run. All of these appear in the bundled levels.

- [ ] **Later robot types:** how Flyers, Cyber Mud, Smart Acid, Smart Walls, Orbiters, Invisible Tanks and Cloaked Tanks move and feel. None of these has been measured moving.
- [ ] **Firing rhythm:** how often each robot type fires. There were too few shots to compare. Robot Tank, Super Tank and Slicer movement already match.

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
