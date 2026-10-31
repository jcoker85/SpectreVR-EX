# Still to validate on the original

Gameplay is mostly done. These need someone playing the original in DOSBox, because they couldn't be measured or judged from the code. Measured results are at the end of [original-mechanics.md](original-mechanics.md).

To record a real game from memory while playing, run `python Tools/win31/memtrace.py <seconds> <out.jsonl>`, then `python Tools/win31/traceview.py <out.jsonl>`.

## Most likely to find real differences

- [ ] **Later robot types:** how Flyers, Cyber Mud, Smart Acid, Smart Walls, Orbiters, Invisible Tanks and Cloaked Tanks move and feel. None of these has been measured moving.
- [ ] **Firing rhythm:** how often each robot type fires. There were too few shots to compare. Robot Tank, Super Tank and Slicer movement already match.
- [ ] **Heavy weapons:** speed, arc and look of Grenades, Scatter Shots, Seekers, Spinners, Mines, Bombs and Pulse Bursts, and how far Seekers curve. Missiles and the cannon are measured. On purpose: Grenades and Bombs throw no debris, and their blasts only reach robots in range.

## Quick side-by-side looks

These match the code but haven't been compared with the original on screen.

- [ ] **Teleport facing:** which way you face coming out of a gate.
- [ ] **Textured faces:** now turned the way `_POLY_2D` sets them. Billboards are confirmed by their movie pictures; check the Super Tank's steel and the other textures look right.
- [ ] **Movies:** the startup movie, and wall movies on billboards. Their timing and brightness against the original.
- [ ] **Floor dots:** spacing of the rebuilt pattern at each F3 setting.
- [ ] **Death camera:** the swing around the wreck.
- [ ] **Hidden map:** G, O, then F3. Its colours are a guess, because the decompile doesn't show them.
- [ ] **Complex off:** the plain shape set has only been loaded, never looked at.

## Could not be tested

- **Tank type 6 as an enemy:** in the original this slot is the player's own ship. No bundled level uses it.
- **Fresh-clone build errors:** a NullReferenceException and a "Pipe is broken" line were logged once. That log is gone, and they haven't appeared since.
