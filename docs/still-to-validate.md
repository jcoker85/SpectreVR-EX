# Still to validate on the original

Gameplay is mostly done. These need someone playing the original in DOSBox, because they can't be settled from the code or the level data. Measured results are at the end of [original-mechanics.md](original-mechanics.md).

To record a real game from memory while playing, run `python Tools/win31/memtrace.py <seconds> <out.jsonl>`, then `python Tools/win31/traceview.py <out.jsonl>`.

## Most likely to find real differences

The rules below are ported line by line from the decompile, so what's left is seeing them run. All of these appear in the bundled levels.

- [x] **Later robot types:** all measured on the running original (Hard levels 5, 13 and 49, 2026-11-02) and matching the ported rules: Flyers (speed, climb to 0xF00, dive to 0x200-0x400, leaving and chasing), Smart Acid and Smart Walls (speed, standing still more than half the time), Invisible and Cloaked Tanks (speed), Cyber Mud (speed; it points nearer the spot ahead of the player than behind it, as `_BLOCK_AT` says) and Orbiters (speed x 4, turning 2 steps a tick). The robot counts on all three levels match the enem loader's limit, which the remake now has.
- [x] **Firing rhythm:** measured on the same run. Super Tanks, Robot Tanks, Cloaked Tanks, Flyers (Seekers) and Invisible Tanks (mines) fire on the rule's minimum gap or one tick after it, which is the sampling.
- [x] **Hard level 49 loads seven robots, not ten:** explained. The enem loader keeps seven entries and eight tanks with the player (see "Full audit" in original-mechanics.md); the remake now does the same.

## Quick side-by-side looks

- [x] **Complex off:** checked by the owner (2026-11-02): the plain shape set works.

## Settled, no check needed

- **Teleport facing:** read from `_HIT_A_GATE`. It comes from the same working as the pusher's direction, and that one was seen on the running original.
- **Floor dots:** `_DRAW_DOTS` gives lines 0x10000 apart and dots 0x2000 apart at level 1, halving each level, none at 0, and the remake builds the same.
- **Death camera:** `_PLAY_LEVEL` gives heading `_DEAD_TIMER` >> 4, distance (timer + 15) * 32 and height timer * 0.75 * 16, and the remake uses the same numbers.
- **Hidden map:** colours are in the machine code of `_DRAW_GOD_VIEW`, and typing G, O, D is in `_PLAY_LEVEL`.
- **Under-floor transports:** up transports (107) and down transports (71) are in the bundled levels, and a down transport was scripted sinking to -0xF00. Nothing about them is unknown, they just get checked as part of normal play.
- **Bouncer and tank type 6 as enemies:** neither is used by any of the 192 levels in the bundled matrix, so they never appear. A custom matrix would be the only way to meet one.
- **Fresh-clone build errors:** a NullReferenceException and a "Pipe is broken" line were logged once. They haven't appeared since, and the Windows build now completes with no errors.
