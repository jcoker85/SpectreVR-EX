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

**Shapes are data-driven:** each matrix has a `shps` resource indexed by type, three words each
(textured shape, plain shape, spare). Ids < 1000 are built into the EXE (procedural table at
DS:0x9B8 - not decoded yet), 1000-1999 random-pick `rshp` lists, 2000-2999 multi-part `mshp`
shapes, 10000+ `shap` meshes. CENTRAL.MAT:

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

Our `ArenaBuilder` obstacle prefabs use guessed shapes and a guessed type list (it treats 17 as
teleport, 19 as windmill, 4 as rock, 39 as a wall) - to be rebuilt from this table.

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
exist as editor obstacles. Robot AI lives in `_MOVE_TANKS`, `_CHASE_*`, `_FLYERDIVE`, `_DO_BOUNCE`
etc. - not read yet.

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
4. Ammo dump kinds and relocation; floating.
5. Shapes from `shps` (decode the built-in shapes at DS:0x9B8).
6. Robots (all 13), then weapons (7 alt weapons), then the remaining fidelity pass.

DOSBox-X (`Tools/dosbox-x/bin/x64/Release/dosbox-x.exe`) is unpacked but not yet launched; the
DOS `SVR.EXE` in `Tools/SpectreVR` is the one to run for side-by-side checks.
