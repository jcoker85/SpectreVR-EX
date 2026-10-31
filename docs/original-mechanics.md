# Spectre VR original mechanics (from SVRWIN.EXE)

Reference for the feature-parity port. Everything here was read from the decompiled Windows
build (`Tools/ghidra_out/SVRWIN.EXE.c`, real function names from the Borland debug info) unless
marked *unverified*. Hex values are the original's own units.

## Tooling

| Step | How |
|---|---|
| Debug symbols | `python -I Tools/ghidra_scripts/tds_symbols.py Tools/SpectreVR/SVRWIN.EXE Tools/ghidra_out/symbols.tsv` (Borland TDS 3.10 inside an NB02 wrapper; 1612 symbols) |
| Label in Ghidra | headless `-process SVRWIN.EXE -noanalysis -postScript ApplyTdsSymbols.java <symbols.tsv>` (TDS segment N = Ghidra block N: Code1-17 at 1000:-1080:, Data18-41 at 1088:-1140:) |
| Decompile dump | `-postScript DumpDecompiled.java Tools/ghidra_out` -> `SVRWIN.EXE.c` + `SVRWIN.EXE.functions.txt` |
| Call arguments | `-postScript DumpRefs.java <n> <symbol>` prints the instructions before each reference (Win16 Pascal imports lose their args in the decompiler). Imports are external, so it finds nothing for SETTIMER etc. |
| Run headless | `JAVA_HOME=Tools/jdk25`, `Tools/ghidra/support/analyzeHeadless.bat Tools/ghidra_projects SpectreVR ...` (add `-readOnly` for read-only scripts). In PowerShell never use `< $null`. |
| Read a function | `awk -v n=_NAME '/^\/\/ ==== /{p=($3==n)} p' Tools/ghidra_out/SVRWIN.EXE.c` |

`Tools/ghidra/support/launch.properties` points Ghidra's settings/cache/temp at `Tools/ghidra_home`
because this app sandboxes `AppData\Local`, which breaks Ghidra's Felix bundle cache.

`cv_publics.py` is a dead end (assumed CodeView; the data is Borland TDS) and can be deleted.

## Units and timing

- **Tick:** the main loop (`_PLAY_LEVEL`, ~line 18970) runs `_GAME_TIME` fixed ticks per frame.
  Ticks come from a Windows timer callback (`inc [0x49be]` at file offset 0x25903). Rate taken
  as the PC timer's **18.2065 Hz** (the code waits 0x12 = 18 ticks for one second, and
  `_LEVEL_TIMER % 0x12` drives a once-a-second bonus decay). *SetTimer interval not read directly.*
- **Position:** 32-bit, arena is **±0x40000** (clamped, `_DO_BOUNCE(..,0x12)` on the wall).
  **1 matrix grid unit = 0x200**; with `GameManager.GridScale = 0.16 m` that is
  **0.0003125 m per unit**.
- **Heading:** one byte, **256 steps per turn**.
- **Trig tables** (`_BUILDSIN`, built at startup; zeros in the file): `cos[i] = (int)(256*cos(i*2π/256))`
  at DS:0x000, `sin[i]` at DS:0x400 (long entries; truncating FTOL).
- **wrld coordinates** (`_LOAD_WRLD`): `X = (y - 0x200) * -0x200`, `Y = (x - 0x200) * -0x200`
  (axes swapped and negated), heading = `deg * 256 / 360`. Movement axis A follows grid **y**,
  axis B follows grid **x**; at heading h the move is `(Δgridx, Δgridy) ∝ (sin h, cos h)`.
  In Unity terms (X = gridx, Z = -gridy): forward = `(sin h, -cos h)`, **yaw = 180° - deg**.
  `GameManager.SpawnObstacle` currently uses `yaw = deg` (wrong for directional obstacles).

## Player movement (`_DO_PLAYER_MOVE` @1050:5663, main loop in `_PLAY_LEVEL`)

- **Speed:** `_THIS_SPEED = _MAX_SPEED * 8 / 5` (integer), applied instantly - **no acceleration**.
  Up arrow = forward, down = reverse, same magnitude. Per tick the move is `cos/sin[h] * SPEED`,
  i.e. `256 * _THIS_SPEED` units.
  - Balance (speed 5): 8 -> 2048 units/tick = 4 grid/tick ≈ **11.6 m/s**
  - Speed (8): 12 -> ≈ **17.5 m/s**; Strength (3): 4 -> ≈ **5.8 m/s**
  - Floating (wings): speed × 1.25.
- **Turning** (`_TURN_SPEED`): 1 step/tick for the first 2 ticks of a turn, then 2 steps/tick
  (≈ 51°/s). Left arrow increases the heading. `_TURNING_TIMER` resets when not turning. Corner
  keys (numpad 7/9/1/3) turn and move together. Mouse and joystick: dead-zone thresholds, same speeds.
- **Vehicles** (`_SET_TANK_SETTINGS`): Balance ammo 40 / damage 60 / speed 5; Speed 30/40/8;
  Strength 40/80/3; Custom = `_CUSTOM_*`.
  Custom editor (`_EDIT_CUSTOM_TANK`): speed 1-9, damage 10-100 step 10, ammo 10-50 step 10,
  `speed + damage/10 + ammo/10 <= 15`.
  **Our `VehicleSpec` presets are wrong:** Speedy should be (8, 4, 3), Strong (3, 8, 4); Balance (5, 6, 4) is right.
- **Start of level:** position A = 0x1E00 (15 grid units south of centre), B = 0, height 0x5000
  (drops in), heading 0x80 (facing north).
- **Vertical:** height `z += vz`; `vz -= 0x100` per tick in the air. Ground `_MIN_Z` = 0x500
  (the object sits at z - 0x500 = 0). Landing with `vz < -0xF00` plays the bounce sound
  (`_DO_BOUNCE(vz, 0x16)`); landing zeroes vz.
- **Jump** (numpad 5, or the fire-2 mouse button): only on the ground (`z == _MIN_Z`), not
  under, needs `_HYPER >= 0x640`; costs 0x640, sets `vz = 0xC00` (≈ 5.8 m high).
- **Hyperspace** (` ~ ` / Backspace, single player only): succeeds if
  `rand(0..0x1900) <= _HYPER`, i.e. **probability = charge**. On success: random spot
  (`_PLACE_XY`), random heading, but facing the centre if beyond 0x38000 on both axes; height
  0x5000 (drops in); vz 0; `_HYPER = 0`; sound 0x3C; enemies re-pick their chase targets.
  **On failure nothing happens** - no explosion, no energy spent. The manual (SVR.TXT) says an
  under-charged hyperspace "might explode"; this build doesn't. **Decision (owner, 2026-10-28):
  follow the manual** - in Spectre VR-EX a failed roll destroys the ship (`PlayerMotor.HyperspaceFailed`).
  The DOS `SVR.EXE` hasn't been checked.
- **Hyperspace charge:** max 0x1900 (6400), +0xB per tick (full in 582 ticks ≈ 32 s).
  The H gauge shows it.
- **Fire:** cannon on Space / numpad 0; holding fires every `_MAX_FIRE_TIMER + 1` ticks
  (`_MAX_FIRE_TIMER = 4 - (level >> 4)`); releasing resets the timer so taps fire at once.
  Out of ammo plays sound 6; under 6 rounds sound 10. Alt weapon on Alt (cycles with Ctrl
  through `_ALLOW_ALT_WEAPONS`), number keys 1-8 pick weapons (`_KEY_WEAPON`). T toggles textures.

## Collisions (main loop after `_MOVE_OBJECT`)

Order each tick: move -> `_CHECK_TANK_COLLIDE` (robots) -> `_CHECK_OBJ_COLLIDE` (world objects)
-> gates/damage -> flags -> fuel (ammo dumps) -> arena clamp -> bounce hop -> gravity.

- **Blocked** (`_DO_COLLIDE_DAMAGE` returns 0 and not floating): the whole tick's move is undone
  **including the heading change**; if on the ground, `_DO_BOUNCE(vz, 7)` and pushed away from the
  object's centre by **1/32 of the offset** (the code subtracts in its negated coordinates; in
  plain coordinates `pos += (pos - obj) >> 5`).
- **`_DO_COLLIDE_DAMAGE`** (object type at +0x5C, enemy data at +0x67, collide damage at data+4):
  - no enemy data (plain obstacle): block.
  - type 7 Robot Tank, 8 Super Tank, 25 Cyber Mud, 27 Cloaked Tank: damage **and block**.
  - type 24 Smart Wall with 0 damage: pass through.
  - type 21 Slicer: damage, sound 0x19, pass through.
  - anything else with data: damage, sound 3 (or 9 when within 0x14 of max), pass through.
  - Damage is **raw collide damage, every tick** of contact.
- **Floating** craft are never blocked.
- **Under** (after a down transport): only transports collide.
- **Bounce hop** (`_DO_BOUNCE`): starts only if not already bouncing and vz ≤ 0; plays the sound,
  sets `_BOUNCE_I = 0x200`. Each tick `vz += max(0, cos[_BOUNCE_I & 0xFF] * _BOUNCE_I >> 8)`,
  `_BOUNCE_I -= 0x20` (16 ticks). Kicks: 512, 339, 0×5, 203, 256, 158, 0×4, 22.
- **Arena edge:** position clamped to ±0x40000 with `_DO_BOUNCE(vz, 0x12)` - no wall collider
  needed; the clamp is the wall.

## Damage units

- Player `_DAMAGE` counts up to `_MAX_DAMAGE` (60 for Balance); 0x14 below max switches the hit
  sound to the danger one (9).
- **Shots hitting the player add weapon damage × 10** (cannon = 1 -> 10, so Balance dies to 6).
  Collision and acid damage are added raw.
- Robots: `strength` from bval vs raw weapon damage (cannon 1).
- Matrix `wval` (CENTRAL): id 0 cannon dmg 1 ammo 1; 1: 2/10; 2: 15/3; 7: 8/0; 10: 3/6;
  14,15: 5/5; 16: 0/5; 17: 2/10; others 1/1 or 1/2.

## Obstacles (`_HIT_A_GATE` @1020:292F, `_LOAD_WRLD`, matrix `shps`)

The world object type **is the wrld type**. Behaviours:

| Type | What it is | Behaviour |
|---|---|---|
| 4 | windmill | base shape + spinning fans object (`_WINDMILL_FANS`, offset 8×cos/sin along heading, z 0x1D00) |
| 16, 23 | acid, Smart Acid robot | pass through; every 3rd tick `_DAMAGE += _ACID_DAMAGE` (default 10, or from enemy data) ≈ 60/s; sound 0x1D |
| 17 | **ice** | on entry store the move delta and heading; while on it you keep sliding by that delta with the heading locked (sound 0x24) |
| 18, 24 | slow pad, Smart Wall robot | net move becomes **¼ speed**; sound 0x23 every 9 ticks |
| 19 | **teleport gate** | jump to the **next** type-19 object in world order (wrapping), exactly onto it, facing gate heading + 0x40; sound 8; `_LAST_GATE` stops re-triggering until you leave |
| 36 (`$`) | up transport | leave the under-level (`_UNDER = 0`); placed sunk by 0x1400; sound 0x0C |
| 37 (`%`) | down transport | `_UNDER = 1`: ground sinks 0x100/tick to -0xF00 (≈1.6 m below the floor), only transports collide, no jumping/floating; sound 0x29 |
| 39 | **pusher** | pushed `18 × 256` units/tick (≈26 m/s) along gate heading + 0x40 (sign: see note below) |
| others | solid | block (see Collisions) |

Pusher/teleport direction note: the tank model heading is stored as move heading + 0x80, and the
code works in negated coordinates, so in move terms the teleport exit faces gate deg + 90° and the
pusher shoves toward gate deg - 90° (worked through by hand - verify in game).

**Flags:** the first `_MAX_FLAGS` wrld entries are flags whatever their type (we use type 14).
Picking one up: +20 score, sound 0x11 (0x17 for one left, 0x3C for the last).

**Shapes are data-driven** (`_ADD_SHAPE` @1020:0127): each matrix has a `shps` resource: a type
count, then three words per type (textured shape, plain shape, spare). With the default
`_TEXTURES = 3` the textured column is used. Before lookup 16 → 10030, 17 → 10027, 20 → 10087.
- 1-999: built into the EXE. `_OT_TO_SHAPE` (DS:0x9B8) has 10-byte entries (id, type, shap id,
  far pointer to a template object). The templates (`_BIG_BOX`, `_PYRAMID`, `_WINDMILL`,
  `_WINDMILL_FANS`, `_BASE`, `_FLAG`, `_TURRET`, ...) are filled at startup by `_BUILD_OBJECTS` /
  `_SET_FACES` from constant stores; `Tools/export_builtin_shapes.py` replays them →
  `Assets/Resources/Shapes/BuiltinShapes.json`. Points are x forward, y side, z up, world units.
- 1000-1999: `rshp` random list, **one pick per list per level** (`_LOAD_WRLD` clears the picks),
  so e.g. every type-3 billboard in a level is the same one.
- 2000-2999: `mshp` multi-part: count, then (shape, distance ×0xA00, angle in degrees, height
  ×0x28) per part; part 0 is the root, the rest follow it (`_MOVE_OBJECT`: parent + distance
  along heading - angle, + height).
- 10000-10999: `shap` meshes. The loader (jump table at 1020:08ED, handler 1020:106B) stores each
  16.16 point >> 7, less 1/64, file x/y/z → world y/z/x: 1.0 = 504 units = 0.1575 m.
- A matrix without `shps` gets no world objects at all (`_SHPSP == 0`); the EXE loads `Basic.mat`
  first, then the chosen matrix. BASIC.MAT has no shapes. We fall back to CENTRAL's.
- `_LOAD_WRLD` spin per tick (+0x3A): type 2 -2, type 36 +3, type 37 -3. Windmill fans: a child
  object 0x800 forward, 0x1D00 up, roll +2/tick from a random start, never collides (type 0xF8).
- Collision is `_CUBE_COLLIDE`: the exact point bounds (`_SIZE_OBJECT`, +0x24..+0x2E), in 3D,
  for gates too. The up transport's zmin is lowered by 0x1400 so a craft under the floor meets it.
  Type 40 (VRchitect walls, a flat 10026 strip) isn't a gate, so it's solid.

CENTRAL.MAT:

| Type | Textured | Plain |
|---|---|---|
| 0 | built-in 1 | 1 |
| 1 | built-in 2 | 2 |
| 2 | rshp 1000 (10034/10004/10086) | built-in 3 |
| 3 | rshp 1001 (billboards) | 10097 |
| 4 | built-in 5 | 5 |
| 6 Tank | mshp 2013 | 10011 |
| 7 Robot Tank | mshp 2006 | 10010 |
| 8 Super Tank | mshp 2001 | built-in 8 |
| 13 | 10088 | 13 |
| 15 | rshp 1002 (cubes) | 10013 |
| 16 / 23 | 10024 | 10024 |
| 17 | 10094 | 10094 |
| 18 / 24 | 10095 | 10095 |
| 19 | built-in 16 | 10030 |
| 20 Turret | built-in 17 | 10027 |
| 21 Slicer | 10002 | 10002 |
| 22 Flyer | mshp 2008 | 10028 |
| 25 Cyber Mud | rshp 1001 | 10097 |
| 26 Invisible | 10010 | 10010 |
| 27 Cloaked | 10101 | 10012 |
| 35 Orbiter, 45 Bouncer | 10015 | 10029 |
| 36 / 37 | 10017 / 10016 | same |
| 39 / 40 | 10025 / 10026 | same |

Built-ins in that column: type 0 → 1 `_BIG_BOX`, 1 → 2 `_PYRAMID` (a 3-sided tetrahedron),
4 → 5 `_WINDMILL`, 5 → 6 `_FUEL`, 9 → `_SHOT`, 11 → `_BASE`, 12 → `_FLAG`, 32 → 19 `_TURRET`;
10, 14 and 42 have no shape.

## Ammo dumps ("fuel", `_CHECK_FUEL_COLLIDE`)

On touch the dump is used once and **moves to a new hidden spot** (`_INVISIBLE_PLACE_XY`,
`_PICK_FUEL_TYPE` picks its new kind). Kinds (fuel object +0x2B):
- 0x20 normal: damage -10, ammo +10 (cap), sound 0x0B.
- 0x40 shield: adds an orbiting shield (up to 4), sound 0x1A.
- 0x60 wings: `_FLOATING += 0x10E` (max 0x546), ammo/damage as normal, may grant alt weapon 8; sound 0x1E.
- 0xB0 full repair: damage 0, ammo full, sound 0x1E.

Floating: hovers toward height 0x2D00, `_FLOATING` counts down each tick, wings animate.

## Robot types (VRchitect names)

6 Tank, 7 Robot Tank, 8 Super Tank, 20 Turret, 21 Slicer, 22 Flyer, 23 Smart Acid, 24 Smart Wall,
25 Cyber Mud, 26 Invisible Tank, 27 Cloaked Tank, 35 Orbiter, 45 Bouncer. Cyber Mud and Ice also
exist as editor obstacles. Type 6 is the player's own tank; matrices use 7, 8, 20-27 and 35
(Central Systems), and BASIC also 45 in its data.

### Enemy data in memory (`_LOAD_BVAL`, loader 1020:0A02)

File entries are 14 words; memory is 0x20 bytes: +0 type, +2 strength (+ a difficulty add if
non-zero), +4 collide damage, +6 attack range ×7 (`_CHASE_DIST`), +8 firing range ×7, +0xA long
(firing range ×7)², +0xE firing arc (unused by the AI), +0x10 firing interval, +0x12 speed,
+0x14 turn speed, +0x16 long score, +0x1A long bonus, +0x1E weapon (-1 = never fires).

### Robot records and the main loop (`_ADD_ENEMY`, `_PLACE_TANKS`, `_MOVE_TANKS` @1068:0277)

Each robot (`_TANKS` + i×0x22; tank 0 is the player) has: +0 object, +8 mode, +0xC fire counter,
+0x10 regenerations left, +0x12 hits left (= strength), +0x14 path heading, +0x18 path timer,
+0x1A path blocker, +0x1E stun timer, +0x20 a per-type timer. Spawned with a random heading.
- Placement: Turrets (20), Orbiters (35) and Bouncers (45) go next to a flag (round-robin over the
  flags, 0x3C00 in from it towards the centre on both axes); the rest `_INVISIBLE_PLACE_XY`: random
  in ±0x3F800, at least 0x26000 from the player on both axes, clear of objects, dumps and flags.
- Mode: Robot Tanks (7) start in 0 (patrol), the rest in 3 (chase). Each tick, if no robot is in
  mode 3 the first one processed (not a Flyer) is switched to 3.
- Stunned (+0x1E > 0): counts down, doesn't move (Super Tanks and Robot Tanks flash).
- Dispatch: 35/45 `_CIRCLE_SPOT`; a Robot Tank among the first five robots with a live flag of its
  own index `_PATROL`s that flag; Flyers (22) `_PATROL` the player; everyone else `_CHASE_PLAYER`.
- After moving: clamp to ±0x40000.
- Dying (+0x5F counting to 10): then `_REPLACE_TANK` - if regenerations are left, one is used and
  the robot comes back at once at a new hidden spot (flag spot for 20/35/45) with full strength.

### Steering

- `_ANGLE(dx, dy)`: atan2 as a 0-255 heading (0 = +x, 0x40 = +y). `_FIX_HEADING`: turn toward a
  heading by at most `limit` steps (the AI passes turn speed ×4).
- `_FIND_CLEAR_PATH`: every 9 ticks probe the target heading, then the current heading, then
  target ±4, ±8, ... up to ±0x80 (`_CHECK_TANK_PATH`: a box 0x5000 long / 0x1400 half-wide whose
  centre is 1.125×0x5000 ahead, against obstacles other than fans, acid, up transports and
  pushers, then against other robots). No clear heading: the robot is moved to a hidden spot.
  While turning toward a new path heading the robot doesn't advance.
- Distances: `_FIND_DIST` is (dx>>8)² + (dy>>8)²; the AI's "d" is that >> 8. `_QFIND_DIST` is
  max(|dx|, |dy|) >> 8.

### `_CHASE_PLAYER` (@1068:0A76)

- Speed = enemy speed; moves speed × cos/sin table (±256) units a tick, so speed 5 ≈ 0.4 m/tick.
- Target heading: straight at the player, except Super Tanks lead their aim (`_GUESS_AT`: the
  player's position minus d/0x2000 × cos/sin of the player's model heading × its speed, i.e. ahead
  of it given the model's +0x80) and Cyber Mud aims at a point 0x5A00 along the player's model
  heading (`_BLOCK_AT` adds, so behind the craft), facing the player once within d 0x19.
- Back off: robots with no collide damage reverse (and steer by heading + 0x80) when d < 0x40;
  they stop once d < 0x60 and aligned within 8.
- Smart Acid (23) / Smart Wall (24): never back off; a timer (+0x6F) reloads to 0x36-0x47 with
  sound 0x13 (Wall) / 0x14 (Acid); while it's below 0x24 they stand still.
- Flyer (22): timer +0x20 runs from 0xA2 (or 0x5A when d > 0x2F8); on expiry mode 1 (fly away and
  climb, `_LEAVE_THING`) for another 0xA2, then back to 3 with sound 0x27. Within d 0x1C8 it dives
  to the player's height + 0x300 (sound 0x27 while above 0xF00), else climbs to 0xF00. Pitch =
  heading - path heading.
- Slicer (21): hops. Timer +0x20 = max(d/0x28, 14), on expiry plays 0x20 (d < 0x15E) / 0x1F
  (d < 800) or waits 12, or 0x21 in the air; jumps vz 0xC00 when d < 0x40 and the player is
  floating; gravity 0x100; burrows (z -0x1400, shape 10087) while the player is under the floor.
- Firing: counter +0xC counts up (cap 0x708); fires when counter > max(d/8 - level/2, 16 - level/8)
  × interval / 120, d² < firing range² (as above), the robot isn't backing off (or level > 14).
  Weapon 0 becomes 12 when the player is in the air, 12 becomes 0 on the ground.
- `_CIRCLE_SPOT` (35, 45): heading += turn ×2 a tick, move 4 × speed (a circle). Bouncers bounce:
  vz 0xC00 on touching z 0, gravity 0x100.
- `_PATROL` (Robot Tank → its flag, Flyer → the player): mode 0 drives to the flag (switch to 2
  within `_QFIND_DIST` 0x50), mode 2 points at the player and goes back to 0 when the flag is
  further than the attack range; either switches to 3 (chase) when the player is on the ground and
  in firing range.

### Shots (`_DO_SHOOT` @1028:7324, `_TANK_SHOOT`, `_MOVE_SHOTS` @1028:7BBA)

45 shot slots; each has a life in ticks, a speed, the shooter and a weapon. Velocity = cos/sin
× speed >> 8 per tick; the shot starts half a step out. Robot shots start at robot z + 0x400
(Turret: its own z). Weapon 5 fires three at heading, ±0x10.

| Weapon | Life | Speed | Other |
|---|---|---|---|
| 0 / default | 0x12 | 0x2000 | sound 2 (player) / 0x18 |
| 2 | 0x10E | 0 | spins 7, no z drift (a mine) |
| 7 | 30000 | - | orbiting shield |
| 9 / 10 | 0x19 / 0xB4 | 0x1800 / 0x1000 | homing: turn 2 / 8 a tick toward the closest robot (player's) or the player; climbs over obstacles (shape 10001 for 10) |
| 11 | 0xB4 | 0x1000 | shape 10087, spins 8; a hit spins the target (+0x6B 0x10 for 0xB4 ticks) |
| 12 | 0x13 | 0x2000 | rises 0x400 a tick (then acts as 0); sound 0x18 |
| 13 | 0x13 | 0x2000 | vz 0x600, falls 0x600/tick, becomes a mine (spin 8) on landing |
| 15 / 16 | 0x36 | 0x200 | lobbed blob: vz 0x900, -0x80 a tick, dies below -0x300; starts 12 steps behind; 16 drains 0x10E of wings on hitting the player |
| 17 | 0x36 | 0x600 | vz 0x300, -0x80 a tick; splash on landing |
| 0x7F | 0x36 | 0x700 | explosion debris, vz 0x900, bounces |

Hits (`_CUBE_COLLIDE` against every robot and the player except the shooter - robots can hit each
other): damage = wval damage of the weapon. Player: `_DAMAGE += damage × 10`, sound 3 (9 when
low). Robot: hits left -= damage, the player scores the robot's score **on every hit**, sound 4
and an 18-tick stun; at 0 it dies (sound 5, or 0x2C for the last robot), its bonus is banked and
paid when the last one dies, `_EXPLODE_TANK` throws 5 debris shots. Weapons 1 and 17 splash:
robots within `_QFIND_DIST` 0x0E / 0x40 / 0x80 lose 5 / 2 / 2, the player takes 10 per band
(0x40 / 0x80 / 0xC4). A shot touching an obstacle dies; a weapon-11 shot spins that obstacle.

### The player's weapons (`_DO_PLAYER_MOVE` @1050:5663, `_KEY_WEAPON`, `_ADD_ALT_WEAPON`)

- Cannon (Space / Ins / fire button): fires when pressed, then while held every
  `_MAX_FIRE_TIMER` + 1 ticks (`4 - level/16`, level 0-based: 5 ticks at first). Costs wval 0's ammo
  (1); sound PLYRSHT, LOONSHT with under 6 rounds, OUTOFAM when empty.
- Eight heavy weapons (`_ALT_WEAPON` 1-8): Grenades (shot type 1, GRSHOT), Scatter Shots (5,
  TRPLSHT), Seekers (9, SEKRSHT), Missiles (10, MISSL2), Spinners (11, SPNSHT), Mines (2, MINDRP),
  Bombs (17, MINBM), Pulse Bursts (13, DBLSTCY). Ctrl (scancode 0x1D) cycles to the next unlocked
  one (CHGWEPN; past the last unlocked comes 0, none); Alt (0x38) fires the selected one; keys 1-8
  select that weapon and fire it at once (CHGWEPN if short of ammo). Each costs its wval ammo
  (Central Systems: 10 / 1 / 2 / 6 / 1 / 3 / 10 / 1). Selecting shows "<name> Online" (or "Heavy
  Weapons Offline") for 0x36 ticks; the HUD icon is cell `_WEAPON_X[w] / 16` of SPCP36
  (cannon 6, then 0, 3, 2, 1, 5, 4, 7, 8).
- Unlocking: a game starts with the cannon only (`_ALLOW_ALT_WEAPONS = 1`). Each level's third wave
  word - the same number that picks its tip from STR# 1007 - is passed to `_ADD_ALT_WEAPON` the
  first time round (not on later laps): `_ALLOW_ALT_WEAPON_STRS` maps 3/12 → Missiles, 4/13 → Mines,
  10 → Grenades, 11 → Seekers, 14 → Spinners, 15 → Scatter, 16 → all, 17 → Bombs, 18 → Pulse;
  the tips themselves describe the weapon. Starting at a later level unlocks every earlier
  level's weapon (`_CATCHUP_HINT`). A wings dump selects Pulse Bursts if nothing is selected and
  they're unlocked.
- `_MY_SHOOT`: shots start at the craft, at its height, along its heading (Scatter: ±0x10). The
  player's homing shots chase the robot closest to the shot (`_FIND_CLOSEST_TANK`), held at the
  middle of its collision box; Seekers turn only 2 a tick, so they curve wide.

## Port plan (single player)

1. **Done:** `PlayerMotor` simulates the player in the original's units at 18.2065 ticks/s and
   interpolates the transform between ticks; kinematic Rigidbody; collisions by
   `Physics.OverlapBox` at the candidate pose (roots in `GameManager.Obstacles` = obstacles,
   `EnemyBase` = robots, walls/floor ignored - the clamp is the wall).
   Measured in Play mode: Balance 11.4 m/s, turn 49°/s, jump 6.2 m, hyperspace drop-in, solid
   obstacles block with the hop, acid 18 points/s at damage 3, slow pad, ice, pusher, teleport.
2. **Done:** player damage in original units (`Health.hitMultiplier` 10, capacity shields × 10,
   collisions/acid raw via `TakeRawDamage`); per-tick robot contact in the motor
   (`EnemyBase.matrixType` / `collideDamage` / `hasMatrixData`; robots default to 7 / 20 / 21+3 /
   35+6 without a matrix). Vehicle presets fixed. Hyperspace: success chance = charge, and a
   failed roll destroys the ship (the manual's rule, by the owner's choice).
   Deviation: robot contact sounds play at most every 4 ticks and only when damage > 0.
3. **Done:** obstacle behaviours by the corrected types (`ObstacleBehaviour.Kind` Ice/Slow/
   Teleport/Pusher...), heading byte stored per obstacle, model yaw = -degrees (shapes face
   opposite their travel direction, as the tank's +0x80 shows). Windmill tower/sails are a
   stand-in (built-in shapes 5 and the fans aren't decoded). Under-floor transports are wired
   but not seen in a real matrix yet.
4. **Done:** 10 dumps per matrix level (all ten `_FUEL_OBJ` slots are placed by `_BUILD_WORLD`),
   kind rolled 0-9 against green/white/orange counts (default 5/2/1, blue the rest; a level's
   type-5 enemy data overrides: strength, collideDamage, attackRange/7, firingRange/7), used up on
   touch and moved ≥0x26000 from the player on both axes with a new kind. Wings in `PlayerMotor`
   (`Floating`, hover toward 0x2D00, speed ×1.25, never blocked); the HUD F gauge shows it, as the
   original does (it showed flags before). Orbit shields (`PlayerShields`/`OrbitShield`): 4 slots
   at 4.8 m, spin after a fifth pickup, hit robots for wval 7 damage (8), absorb enemy bolts.
   Measured: green +10/+10, white full, orange +1 shield, blue ~15 s of hover at ~2-2.6 m.
   Not done: wings visuals on the model, alt weapon 8 from blue dumps, level-0 "jump" pads
   (`_CHECK_JUMP_COLLIDE`: skip to level 3 or 8), the original's bonus counter on the HUD
   (ours shows TIME; `_BONUS` falls by 4 a second).
5. **Done:** obstacles are built at runtime from the playing matrix's own `shps`/`shap`/`rshp`/
   `mshp` (`MatrixFile`, `Matrix.ShapeSet`, `Matrix.ObstacleFactory`), with CENTRAL's as the
   fallback, the 16 EXE built-ins from `BuiltinShapes.json`, one random pick per list per level,
   the spins and windmill fans above, exact-bounds colliders (min 0.6 m, as before), and the up
   transport's extended reach. The builder's guessed obstacle prefabs are gone (`ObstacleLook`
   holds the materials). Checked: all 47 CENTRAL types resolve; level 1 (towers, billboards),
   level 3 (windmills with rolling fans) and level 5 (pyramids block the craft) in Play mode.
   Not done: robots and the player still use the builder's guessed shapes at 0.13 m per unit
   (the original is 0.1575) - item 6; the `mshp` part angle sign is unverified (every CENTRAL
   multi-part shape is symmetric); spinning obstacles keep an unrotated collider; the original
   collides obstacles by their z range against the craft's, ours uses the craft hull as before.
6. **Robots done:** `Robot` (one robot, ported line by line from `_MOVE_TANKS` / `_CHASE_PLAYER` /
   `_PATROL` / `_CIRCLE_SPOT` / `_FIND_CLEAR_PATH`), `RobotDirector` (ticks robots in spawn order,
   then `RobotShots`: weapons 0, 2, 5, 9-13, 15-17 and kill debris, friendly fire, splash) and
   `RobotFactory` (shape from the matrix's `shps`, exact-bounds collider). Spawning follows
   `_ADD_ENEMY` / `_PLACE_TANKS` (counts, `regen` extra lives each, flag guards, hidden spots),
   strength + lap; `_REPLACE_TANK` brings a robot back ten ticks after it dies. Score: the robot's
   score on every hit, bonuses banked and paid with the last robot (ALLBOTS); flags now +20.
   The old RoverAI / TurretAI / OrbiterAI / SlicerAI / TankMotor and their prefabs are gone.
   Measured in Play mode: all 11 Central Systems robot types run (levels 1, 3, 7, 8, 9, 10, 13,
   22); a Super Tank closes to ~12 m and fires; Robot Tanks guard their flags; Turrets stay put;
   Flyers climb to 0xF00; Robot Tanks score 25 a hit and regenerate 3 times at hidden spots.
   Not done: weapons for the player (7 alt weapons, weapon pickups) - next; per-frame shape
   changes (Slicer hop frames, burrowed shape); robot-on-robot bumping (+0xE); the Super/Robot
   Tank stun flash; weapon-11 hits spinning obstacles; Bouncer (45) and Tank (6) aren't in any
   bundled enemy set so are untested; model facing vs travel checked only roughly.
   **Weapons done:** the player now fires through the same tick-based `ShotSystem` as the robots
   (Projectile is gone); `TankWeapon` runs the cannon's autofire, the eight heavy weapons, their
   unlocks, costs, keys (Alt / Ctrl / 1-8; gamepad X / shoulders) and sounds; the HUD icon follows
   `_WEAPON_X` and shows "<name> Online". Measured: cannon 8 rounds in 2 s held (expected ~8.3);
   Medium levels 1-4 unlock Grenades, Spinners, Missiles, Mines; every heavy weapon fires and
   charges its wval ammo; Grenades and Bombs splash, Missiles kill, Spinners score and stun.
   Not done: the player's orbit shields are still our own OrbitShield objects rather than weapon-7
   shots (they now behave like them - see 7). "Mines blow up when hit" means robots driving into
   them, which works; nothing shoots mines in the original.
7. **Fidelity pass, first round:**
   - Level flow (`_SETUP_LEVEL`, `_PLAY_LEVEL`, `_END_LEVEL`): flags are the grid's first
     `min(6, level/2 + 3)` entries (level 0-based), whatever their type - not every type-14 entry.
     No time limit: `_TIME_ALLOWED` = flags × 10 + tanks (robots + the player) × 10 + 30, `_BONUS` =
     that × 4, falling by 4 every 18 ticks; 18 ticks after the last flag the level ends and the
     bonus is added (stats: time allowed, time taken, bonus, score). The HUD shows BONUS, as the
     original does. Flag sounds: FLGPUL, ONEMFLG with one left, WARP for the last.
   - Extra lives: SVRWIN.EXE never reads `sstf`; it gives one every 5000 points below 10000, every
     10000 below 60000, then every 20000, nine at most, sound NEWMAN.
   - Every 0x21C ticks (~30 s) 10 points of damage are mended (TMTICK).
   - Death: CYBOVRL, a life goes 50 ticks later, and the level carries on from the start position
     (drop-in) with flags as they were, shots cleared, ammo full, the bonus restarted. Robots still in play are re-placed (`_PLACE_TANKS`) and their chase states reset; hyperspace resets chase too.
   - First level only: two spinning pads labelled Level 5 / Level 10 (`_JUMP_OBJ`, the fuel shape,
     random spots); driving onto one sets the score to 5000 / 10000, unlocks the heavy weapons of
     levels before (pad level - 2), mends damage and ends the level.
   - Orbit shields kill enemy shots and stay, and are lost on touching an obstacle (not floor
     pads); Robot / Super Tanks flash while stunned; a Spinner shot hitting an obstacle spins it
     16 steps a tick for 0xB4 ticks.
   - Sounds: acid and hits at low strength play HVDAMG (9), not DANGER; TNKUP / TNKDWN are the
     transport sounds (renamed), not respawn / death.
   Measured: level 1 has 3 flags, time allowed 100, bonus 400 → 388 in 3 s; level 2 ended with
   436 bonus paid 1 s after the last flag; level 3 has 4 flags; death returns the craft to the
   start with the bonus reset and one life fewer; the Level 5 pad gives level 5 (5 flags), score
   5000 + bonus, an extra life and the earlier levels' weapons.
   Second round:
   - The craft is drawn from the matrix's own type-6 shape (Central Systems: multi-part 2013) at
     the original's scale (`PlayerModel`; the hull collider is unchanged). Its parts with shape
     ids ending 0x5F / 0x60 (10079 / 10080) are the wings: they turn -/+ `_WING_ANGLE` from the body,
     which moves a step a tick toward 0x19 (tucked) or, while floating, -5 (spread).
   - Slicers change shape as they hop (`_CHANGE_SHAPE`): 10099 rising fast, 10100 falling fast,
     10098 otherwise in the air, 10002 on landing, 10023 while burrowed under the floor.
   - Idle reminder: TMTICK after 0x870 ticks (~2 min) without driving or turning.
   - Robot bumping: the record's +0xE is only ever cleared in SVRWIN.EXE, never set - nothing to port.
   Measured: wings tucked at rest and spread on a wings dump (external view); a Slicer next to a
   floating craft went through 10002 / 10099 / 10098 / 10100.
   Still open: the start-of-level and game-over
   sounds (`_START_LEVEL` / `_GAME_OVER` pass them in registers Ghidra lost), Bouncer (45) untested,
   pusher direction / model facing / `mshp` sign checked only by reasoning, spinning obstacles'
   unrotated colliders, and the DOSBox-X side-by-side check.

**Sound numbers** (`_SOUNDTABLE`, filled from `_SAMPLETABLE` at startup): 0/4 OPPHIT, 1 PLYRSHT,
2 OPSHOT, 3 PLYRHT, 5 EXPLO, 6 OUTOFAM, 7 COLLSN, 8 GATE, 9 HVDAMG, 0xA LOONSHT, 0xB AMMPCK,
0xC TNKUP, 0xD CYBOVRL, 0xE TMTICK, 0xF ECYBSHT, 0x10 LTRDUDE, 0x11 FLGPUL, 0x12 HTEDGE,
0x13/0x14 ACIDMV, 0x15 SPNSHT, 0x16 PLYRLND, 0x17 ONEMFLG, 0x18 GRSHOT, 0x19 SHKDAM, 0x1A SHLDPCK,
0x1B MISSL2, 0x1C SEKRSHT, 0x1D ACIDD, 0x1E REPPCKP, 0x1F SHKLWR, 0x20 SHKLOW, 0x21 SHARK,
0x22 CHGWEPN, 0x23 GOOSQ, 0x24 ICESLD, 0x25 MINDRP, 0x26 TRPLSHT, 0x27 FLYRDV, 0x28 MINBM,
0x29 TNKDWN, 0x2A NEWMAN, 0x2B AWARD, 0x2C ALLBOTS, 0x2D DANGER, 0x2E KICK1, 0x2F APPL2,
0x30 AWWWW, 0x31/0x32 WHISTLE, 0x33 YOURIT, 0x34/0x35 TMTICK, 0x36-0x38 EXPLO, 0x39 ACCDEN,
0x3A SPCLOG, 0x3B WELCOME, 0x3C WARP, 0x3D-0x40 ONE..FOUR, 0x41 COVRME, 0x42 DBLSTCY, 0x43 DISCUM,
0x44 GO4IT, 0x45 HELP, 0x46 RETREAT, 0x47 UCNRUN, 0x48 URDMD. (Our `Snd` names for a few older
guesses, e.g. LowAmmo = SHKLOW, may not match the original's use - check in the fidelity pass.)

DOSBox-X (`Tools/dosbox-x/bin/x64/Release/dosbox-x.exe`) is unpacked but not yet launched; the
DOS `SVR.EXE` in `Tools/SpectreVR` is the one to run for side-by-side checks.

## Main menu and matrix choice (SVRWIN.EXE)

- `_MAIN_MENU`: SPCP25 at 522x332, the matrix picture (`MATRIX_PIC`) at (7,13), tiles from SPCP35 at x = 377 / 449, y = 25 / 93 / 161 / 229. Column pairs on the sheet: normal, highlighted. DEMO takes the slot the SPCP25 art draws MATRIX in.
- `_SELECT_MATRIX` runs once at startup, before the menu. Custom matrices off: central.mat / central.big. On: fewer than 2 .MAT files, keep what is loaded; exactly 2, take the one that isn't basic.mat; more, the SPCP34 chooser (N, P, Enter or C, BASIC skipped, no cancel) with `<name>.pcx` (64x64) as the small picture and `<name>.big` (368x280) for the menu.

## End-of-level recap (_END_LEVEL, _SHOW_RECAP, _SHOW_TRIANGLE)

- The bonus goes on the score, the view is blanked, the three lines animate over 16 ticks, then 18 ticks or a key press.
- Rates out of 128, clamped: Quickness = seconds taken * 128 / TIME_ALLOWED; Accuracy = my hits * 128 / my shots; Avoidance = (robot shots - robot hits on the player) * 128 / robot shots. A zero divisor gives 128.
- Quickness comes from the disassembly (DI = `_LEVEL_TIMER` / 18 at [BP+C], `_TIME_ALLOWED` at [BP+A]): it rises the longer a level takes. Seen on the running original: a demo level finished with Bonus 0 (over the allowed time) rated PERFECT, which fits this formula. Whether the designers meant it is unknown; no fast finish has been observed yet.
- `_LEVEL_TIMER` restarts with each life; the shot counts only with each level. Debris (weapon 0x7F) isn't counted; each of a triple shot's three is.
- Words: 128 Perfect, 101-127 Radical, 65-100 Not Bad, 33-64 Marginal, 0-32 Get Help (colours 0x2F, 0x2F, 0xF, 0x1F, 0x1F; wedge 0xC).
- Layout measured on the original (532-wide game window): labels at the left edge in white, wedge from about 0.31 to 0.69 of the width in light grey, words at about 0.77, lines 30 pixels apart from the middle of the screen, wedge 20 high. PERFECT and RADICAL green, MARGINAL and GET HELP red.
- In the demo the recap stayed up for over 3 minutes although `_END_LEVEL` reads as 18 ticks or a key. Not explained: a hold in demo mode or a hang under emulation. A normal level finish hasn't been timed; the remake keeps 18 ticks.

## Remake timing (a deliberate difference from the original)

`SimClock` is the only clock: fixed steps of 1/(18.2065 x 8) s, about 146 Hz, whatever the frame rate. Every 8th step is an original tick, when robots, shots and the level timers advance; the player moves on every step and each frame draws between the last two steps.

- Movement per tick is shared out with the remainder carried, so a straight run ends each tick where the original's would.
- Height eases gravity across the tick (speed vz + g(1/2 - t) over 0..1), so the arc is smooth and matches the original's height at every tick. On the ground gravity doesn't run.
- Heading has 8 parts to a step, with the sine and cosine tables read between entries.
- Damage, sounds and timers still go by whole ticks. A blocked step undoes only that step.
- Robots and shots stay at 18.2 Hz with display interpolation.
- Catch-up is limited by Unity's maximumDeltaTime, so a stall longer than that loses time rather than speeding up afterwards.

Measured with collisions off, Balance craft, at 10 / 30 / 59 / 117 / 128 fps: speed 11.64-11.67 m/s (calculated 11.65), jump 6.25 m in about 1.38 s (calculated 6.24), about 36 heading steps a second.

## Colours (_MAKE_COLORS, _LOADUP_PALLET, _MOVE_OBJECT's face loop)

- A shap resource's face colour is not a palette entry. The loader (the shap handler at 1020:106B, reached through a jump table Ghidra missed) maps it through `_SHAPE_COLORS` (DS:0x8FC): 0xFF becomes 0, 0xFE becomes 1, above 0xFF is a texture, and anything else is looked up, with results 0xFF and 0xFE treated the same way. Built-in templates already hold the mapped value.
- At draw time a value of 0, or a texture, takes the object's base colour plus a 0-15 shade; other values are fixed palette entries, shaded only if the low nibble is 0.
- Base colour by object type comes from the same table. Palette: 16-entry ramps at 0x00 grey, 0x10 red, 0x20 green, 0x30 blue, 0x40 orange, 0x50 level object colour, 0x60 cyan, 0x70-0x8F sky, 0x90 magenta, 0xA0 pink, 0xB0 white, 0xC0-0xFF grey.
- `_OBJ_COLORS` and `_SKY_COLORS` hold 30 levels of RGB. Level 1 is yellow objects under a magenta sky.
- Face colours 6000-6015 name a PCX in TEXTURES (`_TEXTURE_FILE_NAMES`; 1000-1999 are movie faces). `_SIZE_TEXTURE_POINTS` stretches the whole image over the face's extent in its plane. The original's textured-object option is off by default; the remake's Textures option defaults on.
- `_SHADE`: dot of the face normal with the light (0x47, 0x99, 0xC0; x forward, y side, z up), minus distance >> 10, plus 0x100, clamped to 0-0x1E0, over 0x20: a shade of 0-15, 8 for a side-on face. The remake has a real directional light from that direction (ShadeLight, 1500 lux) over a glow at about the middle of the range (EV 11.5). Distance darkening isn't reproduced.
- The remake reads these from SVRWIN.EXE at import and uses a fixed point on each ramp. Movie faces stay flat. The wireframe option draws the old edge strips.

## Compared with the running original (Easy, textures off)

- Matches: yellow boxes and pyramids, red robots with white wings, five red debris pieces, green craft with white wings, cyan wings dump filling the F gauge.
- Flags are the built-in FLAG template in the object colour, about the craft's height. Dumps are one still flat square 0xC00 units across; the manual's "undulating" isn't in the code or on screen.
- De-res: object +0xD21 (+0x5F, the dying count) or `_PLAYER_DE_RES` is `_DRAW_POLY`'s second argument. Each scanline slides by a sine of the row times (amount + width / 8), with jitter. DeRes.cs does this with lines across the view. The player reaches 0x5A on death; `_D_P_DE_RES` -1 takes it to -5 and back after a teleport or hyperspace.
- Pickups are box overlaps: `_CUBE_COLLIDE` for flags, `_FLAT_CUBE_COLLIDE` (no height) for dumps and jump pads, at their true sizes.
- Demo: "Demo Mode / Press A Key" stays on screen and the view moves on every 126 ticks.
- With bit 4 of `_TEXTURES` on, the demo stayed on a black screen for 40+ seconds under DOSBox-X. Not investigated; the harness needs textures off.

- Every life starts at a random clear spot (`_PLACE_XY`) at a random heading, facing the middle near a corner, as hyperspace does.
- `_LOAD_WRLD`: object X = (512 - file y) * 0x200, Y = (512 - file x) * 0x200, and wrld headings are in degrees. The remake's world is that frame turned a quarter (Unity x = -Y, z = X); a frame of the running original matched its layout, and its pusher arrow matches at heading 32. Unity's robot code uses its own frame, consistent within the remake.
- Pushers move the craft along the way the arrow points: seen on the running original (Hard level 1), where driving into an arrow facing you shoves you back in jerks. `_HIT_A_GATE`'s (-sin h, cos h) is in object coordinates, and the player's simulation runs in their negative (`*(+0xC) = -local_12`), so the motor flips it. Teleports already convert their heading.

## Music and sound flow

- Songs: ids 20001-20011, 21001-, 22001-, 23001- index 11-entry style tables, 24001-24004 are 5theme, award, higame, 5theme (table at DS:0x33B6, slot 0 unused). SPECTRE.MID and CANON.MID are never played. `_PICK_SONG` keeps the song number and moves to the next ticked style. All four styles are on by default.
- `_START_LEVEL`: sound 0 stops the music, 0xF plays five ticks in, the banner lasts about 32 ticks, and the level's song (`DS:0x8F6`) starts as it ends.
- Level end plays the AWARD sample over the level's song. There is no game-over sound. `_CHECK_HIGHSCORES` plays higame for a new best score, award for any other table entry.
- `_SOUNDTABLE` entries are (priority, kind, sample). The original plays one sample at a time by priority; the remake lets them overlap.

## Robot audit against the decompile

Checked line by line: `_MOVE_TANKS`, `_CHASE_PLAYER`, `_FIND_CLEAR_PATH`, `_PATROL`, `_CIRCLE_SPOT`, `_LEAVE_THING`, `_FIX_HEADING` and `_MOVE_SHOTS`'s direct hits. Differences fixed: firing used the 1-based level (`_LEVEL` counts from 0); `_PLACE_TANKS` and `_RESET_TANKS_CHASE` run at every life, the reset also after hyperspace; lives drop from 0xC800 and hyperspace from 0x5000, after the banner; a stunned robot's object colour swaps (orange and red) rather than flashing, visible only on faces in the object colour; Slicers burrow only in cockpit view; hits on the player set `_D_P_DE_RES`. `_DZ` at a hit is a camera value overwritten each frame, so it isn't ported.

## Weapon audit against the decompile

Checked line by line: `_DO_SHOOT`, `_MY_SHOOT`, `_MOVE_SHOTS`, `_KEY_WEAPON`, the firing part of `_DO_PLAYER_MOVE`, `_CHECK_MISSILE_PATH`, `_FIND_CLOSEST_TANK`. Differences fixed:

- `_CHECK_MISSILE_PATH` is a flat rectangle against obstacles taller than a floor pad, at any height. The remake's box also caught the floor, so homing shots near it kept climbing.
- Homing shots ignore robots at `_QFIND_DIST` 2000 or more, and aim at the middle of the player's box too.
- A splash's robot loop only uses the range for the loss. Every other visible robot still takes the 18-tick stun, sound 4 and a score, however far off, even from a robot's shot. A splash throws no debris of its own.
- Shots end in a de-res: +0x5F rises 3 a tick and the shot goes above 0x1E. One that runs out starts at 4 and keeps moving (9 ticks); one that strikes a robot, an obstacle, a shield or the arena edge starts at 10 and stops there (7 ticks). A hit on the player, a Spinner hit and a splash remove the shot at once.
- Shots draw flat at their heading, never pitched. +0x3A turns the heading (grenade-type lobs and bombs 4), +0x3E rolls them (0x10 by default).
- Debris cycles shapes 10102-10104 by life; a landed Pulse Burst becomes shape 10096.
- A Spinner hit spins the player as well as robots: 0x10 a tick for 0xB4 ticks, with no steering or thrust.

Left alone: one non-splash shot can hit two overlapping tanks in the same tick in the original (the remake stops at the first); a splash's sound uses a fixed `_DAMAGE < 0x1E` test.
