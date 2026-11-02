using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // The player's craft, moved the way Spectre VR moves it (SVRWIN.EXE _DO_PLAYER_MOVE and the
    // _PLAY_LEVEL tick loop; see docs/original-mechanics.md), in its units - instant speed, a two-step
    // turn ramp, gravity, jump, hyperspace, collisions that undo the move, and the obstacle "gates" -
    // but sampled SubSteps times per 18.2 Hz tick, on SimClock's fixed steps. Movement per tick is
    // shared out exactly, so the craft is where the original's would be at the end of every tick;
    // timers, damage and sounds still go by whole ticks. The Rigidbody is kinematic: bolts and
    // triggers still find the craft, but nothing pushes it.
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerMotor : MonoBehaviour
    {
        public const float TickRate = 1193182f / 65536f;                   // PC timer, ~18.2065 Hz
        public const int SubSteps = 8;                                     // ~146 Hz; heading has this many parts to a step
        const int FineHeadings = 256 * SubSteps;
        public const float UnitsToMetres = GameManager.GridScale / 0x200;   // a matrix grid unit is 0x200

        public const int HyperMax = 0x1900;
        const int HyperRecharge = 0xB, JumpCost = 0x640;
        const int Arena = 0x40000, FaceCentreBeyond = 0x38000;
        public const int GroundUnits = GroundZ;
        const int BumpHopHeight = 0x600;
        const int GroundZ = 0x500, UnderZ = -0xF00, LifeDropZ = 0xC800, HyperDropZ = 0x5000;
        const int Gravity = 0x100, JumpVz = 0xC00, HardLanding = -0xF00, WarpClimb = 0x300;
        const int PushSpeed = 0x12;
        public const int FloatMax = 0x546;
        const int FloatGain = 0x10E, FloatHeight = 0x2D00;   // a wings dump: 270 ticks (~15 s) of hover, up to 1350

        // The original's tables: 256 * cos / sin of heading * 2pi/256, truncated
        static readonly int[] Cos = OriginalMath.Cos, Sin = OriginalMath.Sin;

        [Tooltip("Vehicle speed points (1-9); the craft moves speed * 8 / 5 table units a tick")]
        public int speedPoints = 5;

        public int Hyper { get; private set; } = HyperMax;
        public float Hyper01 => Hyper / (float)HyperMax;
        public int Floating { get; private set; }
        public float Floating01 => Floating / (float)FloatMax;
        public bool OnGround => z <= minZ;
        // Robots read the object's height, which is 0x500 less than this motor's: rising shots above
        // _MIN_Z + 0x100 (_CHASE_PLAYER), and "on the ground" up to _MIN_Z (_PATROL)
        public bool Airborne => z - GroundZ > minZ + 0x100;
        public bool LowForRobots => z - GroundZ <= minZ;
        public bool Under => under;
        public bool Spinning => spinRate != 0;       // a Spinner hit: no steering, thrust or firing
        public long ZUnits => z;                     // height in original units, the floor at GroundZ
        public int CommandSpeed { get; private set; }   // _SPEED, forward positive: what the craft is told to do, blocked or not
        public int CenterX { get; private set; }         // _CENTER_X / _CENTER_Y: the view's shake in a bounce, in 320x200 pixels
        public int CenterY { get; private set; }
        public Vector3 SimPosition => curPos;        // the latest step's position (the transform is interpolated)
        public Vector3 TickDelta => tickEndPos - lastTickEndPos;
        // The heading in the robots' frame (0 = +X, 0x40 = +Z): this motor's heading points along
        // (sin, -cos), which is the original's (cos, sin) a quarter turn on
        public int OriginalHeading => (heading - 0x40) & 0xFF;

        // A weapon-16 blob (Smart Wall) takes wings time away
        public void DrainFloating(int amount) => Floating = Mathf.Max(0, Floating - amount);

        // A Spinner hit: no steering or thrust while it lasts (+0x6B / +0x6D)
        public void SpinFromHit()
        {
            spinRate = 0x10;
            spinTimer = 0xB4;
        }
        int ThisSpeed => Mathf.Clamp(speedPoints, 1, 9) * 8 / 5;

        // Simulation state, original units. x/y are the floor plane (x = Unity X, y = Unity Z),
        // z is height with the floor at GroundZ, heading is 0-255 with Unity yaw = 180 - heading * 360/256.
        long x, y, z = GroundZ, vz;
        int spinRate, spinTimer;
        int fineHeading = 0x80 * SubSteps;
        int heading
        {
            get => fineHeading / SubSteps;
            set => fineHeading = (value & 0xFF) * SubSteps;
        }
        int minZ = GroundZ;
        bool steered, warping;
        int steerTurn, steerSpeed;
        int turningSteps, heightStep, bounceI, acidTimer;
        long moveLeftX, moveLeftY;
        bool under, onIce;
        long iceDx, iceDy;
        int iceFineHeading;
        ObstacleBehaviour lastGate;

        float throttle, turn;
        bool jumpRequested, hyperRequested;

        Rigidbody rb;
        Health health;
        BoxCollider hull;
        readonly Collider[] overlaps = new Collider[32];
        Vector3 prevPos, curPos, lastWritten, tickEndPos, lastTickEndPos;
        float prevYaw, curYaw;
        bool snapped;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            health = GetComponent<Health>();
            hull = GetComponent<BoxCollider>();
            Resync();
        }

        public void SetInput(float throttle, float turn)
        {
            this.throttle = throttle;
            this.turn = turn;
        }

        // The demo drives by the tick, the original's way: a turn in heading steps and a set speed
        public void Steer(int turn, int speed)
        {
            steered = true;
            steerTurn = turn;
            steerSpeed = speed;
        }

        public void ReleaseSteering() => steered = false;

        // A gate was rebuilt: standing in it shouldn't count as arriving at a new one
        public void GateReplaced(ObstacleBehaviour old, ObstacleBehaviour rebuilt)
        {
            if (lastGate == old) lastGate = rebuilt;
        }

        // The last flag or a level skip (_PLAY_LEVEL): the craft climbs away until the level ends
        public void Warp() => warping = true;

        // Tank record +0x1C: shots pass through a craft dropping in or warping away, until it lands
        public bool Untouchable { get; private set; }

        public void RequestJump() => jumpRequested = true;
        public void RequestHyperspace() => hyperRequested = true;
        public void ResetHyper() => Hyper = HyperMax;

        // Wings (blue ammo dump): hover a while, faster and over everything
        public void AddFloating()
        {
            if (!under) Floating = Mathf.Min(FloatMax, Floating + FloatGain);
        }

        // Put the craft somewhere (level start, respawn). dropIn starts it high up, falling, as the original does.
        public void Place(Vector3 position, float yaw, bool dropIn)
        {
            x = ToUnits(position.x);
            y = ToUnits(position.z);
            heading = HeadingFromYaw(yaw);
            under = false;
            minZ = GroundZ;
            z = dropIn ? LifeDropZ : GroundZ + System.Math.Max(0, ToUnits(position.y));
            Untouchable = dropIn;
            warping = false;
            vz = 0;
            Floating = 0;
            bounceI = turningSteps = heightStep = acidTimer = nothingTimer = spinRate = spinTimer = 0;
            moveLeftX = moveLeftY = 0;
            WingAngle = 0x19;
            onIce = false;
            lastGate = null;
            jumpRequested = hyperRequested = false;
            Snap();
        }

        void Resync() => Place(transform.position, transform.eulerAngles.y, false);

        // Something else moved us (respawn, editor): take that as the new position
        public void BeforeSim()
        {
            if ((transform.position - lastWritten).sqrMagnitude > 0.0001f) Resync();
        }

        // One fixed step of SimClock; tickEnds marks the last of an original tick's sub-steps
        public void SubStep(bool tickEnds)
        {
            prevPos = curPos;
            prevYaw = curYaw;
            Step(tickEnds);
            curPos = Position();
            curYaw = Yaw();
            if (snapped) { prevPos = curPos; prevYaw = curYaw; snapped = false; }   // teleports cut, not sweep
            if (tickEnds) { lastTickEndPos = tickEndPos; tickEndPos = curPos; }
        }

        // The moment between the last two steps, for this frame
        public void Present(float alpha)
        {
            Write(Vector3.Lerp(prevPos, curPos, alpha), Mathf.LerpAngle(prevYaw, curYaw, alpha));
        }

        // ---------- one step ----------

        void Step(bool tickEnds)
        {
            int speed = throttle > 0.3f ? ThisSpeed : throttle < -0.3f ? -ThisSpeed : 0;
            if (Floating > 0) speed -= -speed >> 2;   // forward is negative in the original, so this rounds its way
            int turnStep = 0;
            if (Mathf.Abs(turn) > 0.3f)
            {
                int ticksTurning = turningSteps++ / SubSteps + 1;
                int step = ticksTurning < 3 ? 1 : 2;
                turnStep = turn < 0f ? step : -step;    // left raises the heading
            }
            else turningSteps = 0;

            if (steered)
            {
                speed = steerSpeed;
                turnStep = steerTurn;
            }

            // A wreck is out of the game: it drops where it was and does nothing more
            bool wrecked = health != null && health.IsDead;
            if (wrecked)
            {
                speed = turnStep = 0;
                jumpRequested = hyperRequested = false;
            }

            if (spinRate != 0)
            {
                turnStep = spinRate;
                speed = 0;
                if (tickEnds && --spinTimer < 0) spinRate = spinTimer = 0;
            }

            CommandSpeed = speed;
            int startHeading = fineHeading;
            fineHeading = (fineHeading + turnStep + FineHeadings) % FineHeadings;

            if (jumpRequested)
            {
                jumpRequested = false;
                if (!under && z == minZ && Hyper >= JumpCost) { Hyper -= JumpCost; vz = JumpVz; }
            }
            if (hyperRequested)
            {
                hyperRequested = false;
                // Succeeds with a chance equal to the charge. A failed roll destroys the ship, as the
                // manual warns ("If you launch before fully charged the ship might explode"); the
                // Windows build's code just ignores it, but the owner chose the manual's rule.
                if (Random.Range(0, HyperMax + 1) <= Hyper) Hyperspace();
                else HyperspaceFailed();
            }

            // A whole tick's move at this heading; a step takes its share below
            long dx = TableAt(Sin) * speed;
            long dy = -TableAt(Cos) * speed;
            long moveX = dx, moveY = dy;
            long nx = x + dx / SubSteps, ny = y + dy / SubSteps;

            Classify(nx, ny, heading, out var robot, out var gate, out var solid);
            if (wrecked) gate = null;

            bool blocked = false;
            Vector3 blockerAt = default;
            if (robot != null)
            {
                blockerAt = robot.transform.position;
                // _HIT_A_GATE comes first and takes these two by type, as it does the pads they lay
                if (robot.matrixType == 23) AcidTouch(tickEnds);
                else if (robot.matrixType == 24)
                {
                    moveX = dx / 4; moveY = dy / 4;
                    SlowTouch(tickEnds);
                }
                else
                {
                    blocked = !RobotContact(robot, tickEnds);
                    DemoTouch(blockerAt, tickEnds);
                }
            }
            else if (gate != null)
            {
                switch (gate.kind)
                {
                    case ObstacleBehaviour.Kind.Teleport:
                        if (gate != lastGate && TeleportFrom(gate)) { moveX = moveY = 0; moveLeftX = moveLeftY = 0; }
                        break;
                    case ObstacleBehaviour.Kind.Pusher:
                    {
                        // The demo treats pushers as solid and jumps them (_PLAY_LEVEL)
                        if (GameManager.Instance != null && GameManager.Instance.Demo)
                        {
                            blocked = true;
                            blockerAt = gate.transform.position;
                            if (!under && z == minZ && Hyper >= JumpCost) { Hyper -= JumpCost; vz = JumpVz; }
                            DemoTouch(blockerAt, tickEnds);
                            break;
                        }
                        // _HIT_A_GATE moves the craft's object, whose coordinates are the negative of these: along the arrow
                        int h = (gate.heading - 0x40) & 0xFF;
                        moveX = -(long)Sin[h] * PushSpeed;
                        moveY = (long)Cos[h] * PushSpeed;
                        onIce = false;
                        break;
                    }
                    case ObstacleBehaviour.Kind.Ice:
                        // Arriving at rest (a landing) there's nothing to slide on yet, so the first
                        // move sets it; the original would hold the craft still and locked here
                        if (!onIce || (iceDx == 0 && iceDy == 0))
                        {
                            if (!onIce) Sfx.Play(Snd.IceSlide);
                            onIce = true;
                            iceDx = dx; iceDy = dy; iceFineHeading = fineHeading;
                        }
                        else
                        {
                            fineHeading = iceFineHeading;
                            moveX = iceDx; moveY = iceDy;
                        }
                        break;
                    case ObstacleBehaviour.Kind.Slow:
                        moveX = dx / 4; moveY = dy / 4;
                        SlowTouch(tickEnds);
                        break;
                    case ObstacleBehaviour.Kind.Acid:
                        AcidTouch(tickEnds);
                        break;
                    case ObstacleBehaviour.Kind.DownTransport:
                        if (!under) Sfx.Play(Snd.TransportDown);
                        under = true;
                        break;
                    case ObstacleBehaviour.Kind.UpTransport:
                        if (under) Sfx.Play(Snd.TransportUp);
                        under = false;
                        break;
                }
            }
            else if (solid != null)
            {
                blocked = true;
                blockerAt = solid.position;
                DemoTouch(blockerAt, tickEnds);
            }
            // _CLEAR_GATE_COLLIDE: touching nothing at all
            if (gate == null && robot == null && solid == null) { lastGate = null; onIce = false; acidTimer = 0; }
            if (Floating > 0) blocked = false;   // wings carry you over anything

            // Blocked: the move is undone, turn included, and on the ground you're nudged 1/32 of the
            // way out from the thing you hit, a tick's worth, with a little hop
            if (blocked)
            {
                fineHeading = startHeading;
                moveLeftX = moveLeftY = 0;
                if (z <= minZ)
                {
                    Bounce(Snd.Collision);
                    x += ((x - ToUnits(blockerAt.x)) >> 5) / SubSteps;
                    y += ((y - ToUnits(blockerAt.z)) >> 5) / SubSteps;
                }
            }
            else
            {
                x += Share(moveX, ref moveLeftX);
                y += Share(moveY, ref moveLeftY);
            }

            // The arena edge is a clamp, not a collider
            if (x > Arena) { x = Arena; Bounce(Snd.HitEdge); }
            if (x < -Arena) { x = -Arena; Bounce(Snd.HitEdge); }
            if (y > Arena) { y = Arena; Bounce(Snd.HitEdge); }
            if (y < -Arena) { y = -Arena; Bounce(Snd.HitEdge); }

            MoveHeight();
            if (tickEnds) EndTick(speed != 0 || turnStep != 0, speed != 0);
        }

        // The tick's share of a move; what's left over rounds into the next step, so nothing is lost
        static long Share(long perTick, ref long left)
        {
            left += perTick;
            long step = left / SubSteps;
            left -= step * SubSteps;
            return step;
        }

        // The original's sine or cosine table, read between its 256 headings
        long TableAt(int[] table)
        {
            int h = fineHeading / SubSteps, part = fineHeading % SubSteps;
            return (table[h] * (SubSteps - part) + table[(h + 1) & 0xFF] * part) / SubSteps;
        }

        // Height through a tick: speed eases by the tick's gravity as it goes rather than all at once at
        // the end, so a jump is a smooth arc that still passes through the original's height every tick
        void MoveHeight()
        {
            if (z <= minZ && vz <= 0)
            {
                z = minZ;
                vz = 0;
                heightStep = 0;
                Untouchable = false;
                return;
            }
            z += HeightAfter(heightStep + 1) - HeightAfter(heightStep);
            if (z < minZ)
            {
                z = minZ;
                if (vz < HardLanding) Bounce(Snd.Land);
                vz = 0;
                heightStep = 0;
                Untouchable = false;
                return;
            }
            if (++heightStep == SubSteps)
            {
                heightStep = 0;
                vz -= Gravity;
            }
        }

        long HeightAfter(int steps) => vz * steps / SubSteps + Gravity * steps * (SubSteps - steps) / (2 * SubSteps * SubSteps);

        // What the original does once a tick whatever the sampling rate
        void EndTick(bool active, bool moving)
        {
            if (warping)
            {
                Untouchable = true;
                vz += WarpClimb;
            }

            // The ground sinks while under the floor (down transport) and rises back afterwards
            if (!under) { if (minZ < GroundZ) minZ += 0x100; }
            else
            {
                if (minZ > UnderZ) minZ -= 0x100;
                Floating = 0;
            }

            // Bounce hop: a run of upward kicks shaped by the cosine table
            if (bounceI != 0)
            {
                if (bounceI < 1) bounceI = 0;
                else
                {
                    long kick = (long)Cos[bounceI & 0xFF] * bounceI >> 8;
                    if (kick > 0) vz += kick;
                    bounceI -= 0x20;
                }
            }

            // _PLAY_LEVEL shakes the view by this much while a bounce runs
            CenterX = (bounceI - Random.Range(0, bounceI * 2)) >> 6;
            CenterY = (bounceI - Random.Range(0, bounceI * 2)) >> 6;

            // Wings: lift toward hover height (a little lower while moving), with a wobble and drag
            if (Floating > 0)
            {
                bool demo = GameManager.Instance != null && GameManager.Instance.Demo;
                long lift = ((FloatHeight - z) >> 4) - (moving || demo ? 0x100 : 0);
                if (lift < 0) lift = 0;
                vz += lift + Random.Range(0, 0x60);
                vz -= vz >> 4;
                Floating--;
            }

            // The wings open a step a tick while floating (to -5) and fold back (to 0x19) after
            if (Floating > 0) { if (WingAngle > -5) WingAngle--; }
            else if (WingAngle < 0x19) WingAngle++;

            // Two minutes without driving or turning: a reminder tick (_NOTHING_TIMER)
            if (active) nothingTimer = 0;
            else if (++nothingTimer > 0x870)
            {
                nothingTimer = 0;
                Sfx.Play(Snd.TimeTick);
            }

            // Paused, the craft still falls and floats but this stops (_PLAY_LEVEL's local_34)
            bool paused = GameManager.Instance != null && GameManager.Instance.Paused;
            if (Hyper < HyperMax && !paused) Hyper = Mathf.Min(HyperMax, Hyper + HyperRecharge);
        }

        // _WING_ANGLE: the wings' turn from the body, in heading steps (0x19 folded, -5 spread)
        public int WingAngle { get; private set; } = 0x19;
        int nothingTimer;

        void Bounce(string sound)
        {
            if (bounceI >= 1 || vz >= 1) return;
            bounceI = 0x200;
            Sfx.Play(sound);
        }

        // _DO_COLLIDE_DAMAGE for robots: true = drive through, false = blocked
        bool RobotContact(EnemyBase e, bool tickEnds)
        {
            if (!e.hasMatrixData) return false;
            int type = e.matrixType, damage = e.collideDamage;
            bool blocks = type == 7 || type == 8 || type == 25 || type == 27;   // robot / super tanks, cyber mud, cloaked tank
            if (blocks && damage == 0) return false;
            if (tickEnds && health != null && damage > 0)   // _DO_COLLIDE_DAMAGE, every tick of contact
            {
                health.TakeRawDamage(damage, false);
                if (!health.IsDead)
                {
                    if (type == 21) Sfx.Restart(Snd.SlicerBite);
                    else if (!blocks) Sfx.Restart(health.PlayerHitSound);   // the blocking kinds only bounce you
                }
            }
            return !blocks;
        }

        // _HIT_A_GATE's slow pad and Smart Wall: a quarter speed, with a squelch every ninth tick
        void SlowTouch(bool tickEnds)
        {
            onIce = false;
            if (tickEnds && acidTimer-- < 1) { acidTimer = 8; Sfx.Play(Snd.Goo); }
        }

        // _HIT_A_GATE's acid and Smart Acid: burns every third tick
        void AcidTouch(bool tickEnds)
        {
            onIce = false;
            if (!tickEnds || acidTimer-- >= 1) return;
            acidTimer = 2;
            var gm = GameManager.Instance;
            if (health == null) return;
            health.TakeRawDamage(gm != null ? gm.AcidDamage : 10, false);
            if (!health.IsDead) Sfx.Restart(health.Shields <= 20 ? Snd.HeavyDamage : Snd.Acid);
        }

        // In the demo anything hit puts the robots back on the chase, and a craft right on top of it moves away
        void DemoTouch(Vector3 at, bool tickEnds)
        {
            var gm = GameManager.Instance;
            if (!tickEnds || gm == null || !gm.Demo) return;
            if (OriginalMath.QDist(x - ToUnits(at.x), y - ToUnits(at.z)) < 0xF)
            {
                var p = gm.PlaceXY();
                x = ToUnits(p.x);
                y = ToUnits(p.z);
                moveLeftX = moveLeftY = 0;
                Snap();
            }
            gm.ResetRobotChase();
        }

        // Raw damage points (collisions and acid count in the original's units, unlike weapon hits)
        // ---------- gates ----------

        // Teleport gates send you to the next gate in the matrix's order, wrapping round
        bool TeleportFrom(ObstacleBehaviour gate)
        {
            var gm = GameManager.Instance;
            if (gm == null) return false;
            var list = gm.Obstacles;
            int start = list.IndexOf(gate.gameObject);
            if (start < 0) return false;
            for (int i = 1; i < list.Count; i++)
            {
                var o = list[(start + i) % list.Count];
                if (o == null) continue;
                var target = o.GetComponent<ObstacleBehaviour>();
                if (target == null || target.kind != ObstacleBehaviour.Kind.Teleport) continue;

                x = ToUnits(o.transform.position.x);
                y = ToUnits(o.transform.position.z);
                heading = target.heading + 0x40;
                lastGate = target;
                Sfx.Play(Snd.Gate);
                Snap();
                return true;
            }
            return false;
        }

        void Hyperspace()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var p = gm.PlaceXY();
            x = ToUnits(p.x);
            y = ToUnits(p.z);
            FaceRandomly();
            z = HyperDropZ;
            under = false;    // a drop from above always lands on the floor
            minZ = GroundZ;
            vz = 0;
            Hyper = 0;
            onIce = false;
            lastGate = null;
            Sfx.Play(Snd.Warp);
            Snap();
            gm.ResetRobotChase();
        }

        // Any way round, but out near a corner you come in facing the middle
        public void FaceRandomly()
        {
            heading = Random.Range(0, 0xFF);
            if (System.Math.Abs(x) > FaceCentreBeyond && System.Math.Abs(y) > FaceCentreBeyond)
                heading = HeadingToward(-x, -y);
            Snap();
        }

        void HyperspaceFailed()
        {
            Hyper = 0;
            Sfx.Play(Snd.Warp);
            if (health != null) health.TakeRawDamage(health.Shields + 1, true);
        }

        // ---------- collision query ----------

        void Classify(long nx, long ny, int h, out EnemyBase robot, out ObstacleBehaviour gate, out Transform solid)
        {
            robot = null; gate = null; solid = null;
            if (hull == null) return;

            // _PLAY_LEVEL: low in a bump's hop, obstacles are tested from the ground, so the hop can't
            // carry you over a low wall. Robots are always tested from where you really are.
            bool hopping = bounceI != 0 && z - GroundZ < minZ + BumpHopHeight;
            Sweep(nx, ny, h, z, !hopping, ref robot, ref gate, ref solid);
            if (hopping) Sweep(nx, ny, h, GroundZ, true, ref robot, ref gate, ref solid);
        }

        void Sweep(long nx, long ny, int h, long height, bool obstacles, ref EnemyBase robot, ref ObstacleBehaviour gate, ref Transform solid)
        {
            Vector3 pos = new Vector3(nx, height - GroundZ, ny) * UnitsToMetres;
            var rot = Quaternion.Euler(0f, YawFromHeading(h), 0f);
            Vector3 scale = transform.lossyScale;
            Vector3 centre = pos + rot * Vector3.Scale(hull.center, scale);
            Vector3 half = Vector3.Scale(hull.size, scale) * 0.5f;

            int n = Physics.OverlapBoxNonAlloc(centre, half, overlaps, rot, ~0, QueryTriggerInteraction.Collide);
            var gm = GameManager.Instance;
            for (int i = 0; i < n; i++)
            {
                var c = overlaps[i];
                if (c.transform.IsChildOf(transform)) continue;

                var ob = c.GetComponentInParent<ObstacleBehaviour>();
                if (ob != null && !obstacles) continue;
                if (ob != null && ob.IsGate)
                {
                    // Under the floor only the transports are there to touch
                    if (under && ob.kind != ObstacleBehaviour.Kind.UpTransport && ob.kind != ObstacleBehaviour.Kind.DownTransport) continue;
                    if (gate == null) gate = ob;
                    continue;
                }
                if (c.isTrigger) continue;

                // Robots aren't filtered under the floor: that's how a Slicer reaches you there
                var e = c.GetComponentInParent<EnemyBase>();
                if (e != null)
                {
                    if (robot == null && e.Health != null && !e.Health.IsDead) robot = e;
                    continue;
                }
                if (under) continue;
                var root = c.transform.root;
                if (obstacles && solid == null && gm != null && gm.Obstacles.Contains(root.gameObject)) solid = root;
            }
        }

        // ---------- conversions ----------

        static long ToUnits(float metres) => (long)System.Math.Round(metres / UnitsToMetres);
        static float YawFromHeading(int h) => 180f - h * (360f / 256f);
        static int HeadingFromYaw(float yaw) => Mathf.RoundToInt((180f - yaw) * (256f / 360f)) & 0xFF;

        // The heading whose forward step points along (dx, dy)
        static int HeadingToward(long dx, long dy) =>
            Mathf.RoundToInt(Mathf.Atan2(dx, -dy) * (128f / Mathf.PI)) & 0xFF;

        Vector3 Position() => new Vector3(x, z - GroundZ, y) * UnitsToMetres;
        float Yaw() => 180f - fineHeading * (360f / FineHeadings);

        void Snap()
        {
            snapped = true;
            curPos = prevPos = tickEndPos = lastTickEndPos = Position();
            curYaw = prevYaw = Yaw();
            Write(curPos, curYaw);
        }

        void Write(Vector3 pos, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            transform.SetPositionAndRotation(pos, rot);
            if (rb != null) { rb.position = pos; rb.rotation = rot; }
            lastWritten = pos;
        }
    }
}
