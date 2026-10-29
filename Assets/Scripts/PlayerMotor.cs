using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // The player's craft, moved the way Spectre VR moves it (SVRWIN.EXE _DO_PLAYER_MOVE and the
    // _PLAY_LEVEL tick loop; see docs/original-mechanics.md). The simulation runs on the original's
    // fixed 18.2 Hz tick in its own units - instant speed, a two-step turn ramp, gravity, jump,
    // hyperspace, collisions that undo the move, and the obstacle "gates" - and the transform is
    // interpolated between ticks for display. The Rigidbody is kinematic: bolts and triggers still
    // find the craft, but nothing pushes it.
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerMotor : MonoBehaviour
    {
        public const float TickRate = 1193182f / 65536f;                   // PC timer, ~18.2065 Hz
        public const float UnitsToMetres = GameManager.GridScale / 0x200;   // a matrix grid unit is 0x200

        public const int HyperMax = 0x1900;
        const int HyperRecharge = 0xB, JumpCost = 0x640;
        const int Arena = 0x40000, FaceCentreBeyond = 0x38000;
        const int GroundZ = 0x500, UnderZ = -0xF00, DropZ = 0x5000;
        const int Gravity = 0x100, JumpVz = 0xC00, HardLanding = -0xF00;
        const int PushSpeed = 0x12;
        public const int FloatMax = 0x546;
        const int FloatGain = 0x10E, FloatHeight = 0x2D00;   // a wings dump: 270 ticks (~15 s) of hover, up to 1350
        const int MaxTicksPerFrame = 8;

        // The original's tables: 256 * cos / sin of heading * 2pi/256, truncated
        static readonly int[] Cos = OriginalMath.Cos, Sin = OriginalMath.Sin;

        [Tooltip("Vehicle speed points (1-9); the craft moves speed * 8 / 5 table units a tick")]
        public int speedPoints = 5;

        public int Hyper { get; private set; } = HyperMax;
        public float Hyper01 => Hyper / (float)HyperMax;
        public int Floating { get; private set; }
        public float Floating01 => Floating / (float)FloatMax;
        public bool OnGround => z <= minZ;
        public bool Airborne => z > minZ + 0x100;   // robots switch to rising shots (_CHASE_PLAYER)
        public bool Under => under;
        public long ZUnits => z;                     // height in original units, the floor at GroundZ
        public Vector3 SimPosition => curPos;        // this tick's position (the transform is interpolated)
        public Vector3 TickDelta => curPos - prevPos;

        // A weapon-16 blob (Smart Wall) takes wings time away
        public void DrainFloating(int amount) => Floating = Mathf.Max(0, Floating - amount);
        int ThisSpeed => Mathf.Clamp(speedPoints, 1, 9) * 8 / 5;

        // Simulation state, original units. x/y are the floor plane (x = Unity X, y = Unity Z),
        // z is height with the floor at GroundZ, heading is 0-255 with Unity yaw = 180 - heading * 360/256.
        long x, y, z = GroundZ, vz;
        int heading = 0x80;
        int minZ = GroundZ;
        int turningTimer, bounceI, acidTimer, contactSoundTimer;
        bool under, onIce;
        long iceDx, iceDy;
        int iceHeading;
        ObstacleBehaviour lastGate;

        float throttle, turn;
        bool jumpRequested, hyperRequested;

        Rigidbody rb;
        Health health;
        BoxCollider hull;
        readonly Collider[] overlaps = new Collider[32];
        float tickAccumulator;
        Vector3 prevPos, curPos, lastWritten;
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
            z = dropIn ? DropZ : GroundZ + System.Math.Max(0, ToUnits(position.y));
            vz = 0;
            Floating = 0;
            bounceI = turningTimer = acidTimer = 0;
            onIce = false;
            lastGate = null;
            jumpRequested = hyperRequested = false;
            Snap();
        }

        void Resync() => Place(transform.position, transform.eulerAngles.y, false);

        void Update()
        {
            // Something else moved us (respawn, editor): take that as the new position
            if ((transform.position - lastWritten).sqrMagnitude > 0.0001f) Resync();

            if (health != null && health.IsDead) return;

            tickAccumulator += Time.deltaTime * TickRate;
            int ticks = 0;
            while (tickAccumulator >= 1f && ticks < MaxTicksPerFrame)
            {
                tickAccumulator -= 1f;
                ticks++;
                prevPos = curPos;
                prevYaw = curYaw;
                Tick();
                curPos = Position();
                curYaw = Yaw();
                if (snapped) { prevPos = curPos; prevYaw = curYaw; snapped = false; }   // teleports cut, not sweep
            }
            if (ticks == MaxTicksPerFrame) tickAccumulator = 0f;   // hitch: don't try to catch up

            float t = Mathf.Clamp01(tickAccumulator);
            Write(Vector3.Lerp(prevPos, curPos, t), Mathf.LerpAngle(prevYaw, curYaw, t));
        }

        // ---------- one original tick ----------

        void Tick()
        {
            // The ground sinks while under the floor (down transport) and rises back afterwards
            if (!under) { if (minZ < GroundZ) minZ += 0x100; }
            else
            {
                if (minZ > UnderZ) minZ -= 0x100;
                Floating = 0;
            }

            int speed = throttle > 0.3f ? ThisSpeed : throttle < -0.3f ? -ThisSpeed : 0;
            if (Floating > 0) speed += speed >> 2;
            int turnStep = 0;
            if (Mathf.Abs(turn) > 0.3f)
            {
                turningTimer++;
                int step = turningTimer < 3 ? 1 : 2;
                turnStep = turn < 0f ? step : -step;    // left raises the heading
            }
            else turningTimer = 0;

            int startHeading = heading;
            heading = (heading + turnStep) & 0xFF;

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

            long dx = (long)Sin[heading] * speed;
            long dy = -(long)Cos[heading] * speed;
            long nx = x + dx, ny = y + dy;

            Classify(nx, ny, heading, out var robot, out var gate, out var solid);

            bool blocked = false;
            Vector3 blockerAt = default;
            if (robot != null)
            {
                blocked = !RobotContact(robot);
                blockerAt = robot.transform.position;
            }
            else if (gate != null)
            {
                switch (gate.kind)
                {
                    case ObstacleBehaviour.Kind.Teleport:
                        if (gate != lastGate && TeleportFrom(gate)) { nx = x; ny = y; }
                        break;
                    case ObstacleBehaviour.Kind.Pusher:
                    {
                        int h = (gate.heading - 0x40) & 0xFF;
                        nx = x + (long)Sin[h] * PushSpeed;
                        ny = y - (long)Cos[h] * PushSpeed;
                        onIce = false;
                        break;
                    }
                    case ObstacleBehaviour.Kind.Ice:
                        if (!onIce)
                        {
                            onIce = true;
                            iceDx = dx; iceDy = dy; iceHeading = heading;
                            Sfx.Play(Snd.IceSlide);
                        }
                        else
                        {
                            heading = iceHeading;
                            nx = x + iceDx; ny = y + iceDy;
                        }
                        break;
                    case ObstacleBehaviour.Kind.Slow:
                        nx = x + dx / 4; ny = y + dy / 4;
                        onIce = false;
                        if (acidTimer-- < 1) { acidTimer = 8; Sfx.Play(Snd.Goo); }
                        break;
                    case ObstacleBehaviour.Kind.Acid:
                        onIce = false;
                        if (acidTimer-- < 1)
                        {
                            acidTimer = 2;
                            var gm = GameManager.Instance;
                            if (health != null)
                            {
                                health.TakeRawDamage(gm != null ? gm.AcidDamage : 10, false);
                                if (!health.IsDead) Sfx.Play(health.Shields <= 20 ? Snd.Danger : Snd.Acid);
                            }
                        }
                        break;
                    case ObstacleBehaviour.Kind.DownTransport:
                        if (!under) Sfx.Play(Snd.PlayerDown);
                        under = true;
                        break;
                    case ObstacleBehaviour.Kind.UpTransport:
                        if (under) Sfx.Play(Snd.PlayerUp);
                        under = false;
                        break;
                }
            }
            else if (solid != null)
            {
                blocked = true;
                blockerAt = solid.position;
            }
            if (gate == null) { lastGate = null; onIce = false; acidTimer = 0; }
            if (Floating > 0) blocked = false;   // wings carry you over anything

            // Blocked: the whole tick's move is undone, turn included, and on the ground you're
            // nudged 1/32 of the way out from the thing you hit with a little hop
            if (blocked)
            {
                nx = x; ny = y;
                heading = startHeading;
                if (z <= minZ)
                {
                    Bounce(Snd.Collision);
                    nx += (x - ToUnits(blockerAt.x)) >> 5;
                    ny += (y - ToUnits(blockerAt.z)) >> 5;
                }
            }
            x = nx; y = ny;

            // The arena edge is a clamp, not a collider
            if (x > Arena) { x = Arena; Bounce(Snd.HitEdge); }
            if (x < -Arena) { x = -Arena; Bounce(Snd.HitEdge); }
            if (y > Arena) { y = Arena; Bounce(Snd.HitEdge); }
            if (y < -Arena) { y = -Arena; Bounce(Snd.HitEdge); }

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

            z += vz;
            if (z < minZ)
            {
                z = minZ;
                if (vz < HardLanding) Bounce(Snd.Land);
                vz = 0;
            }
            else vz -= Gravity;

            // Wings: lift toward hover height (a little lower while moving), with a wobble and drag
            if (Floating > 0)
            {
                long lift = ((FloatHeight - z) >> 4) - (speed != 0 ? 0x100 : 0);
                if (lift < 0) lift = 0;
                vz += lift + Random.Range(0, 0x60);
                vz -= vz >> 4;
                Floating--;
            }

            if (Hyper < HyperMax) Hyper = Mathf.Min(HyperMax, Hyper + HyperRecharge);
            if (contactSoundTimer > 0) contactSoundTimer--;
        }

        void Bounce(string sound)
        {
            if (bounceI >= 1 || vz >= 1) return;
            bounceI = 0x200;
            Sfx.Play(sound);
        }

        // _DO_COLLIDE_DAMAGE for robots: true = drive through, false = blocked
        bool RobotContact(EnemyBase e)
        {
            if (!e.hasMatrixData) return false;
            int type = e.matrixType, damage = e.collideDamage;
            bool blocks = type == 7 || type == 8 || type == 25 || type == 27;   // robot / super tanks, cyber mud, cloaked tank
            if (type == 24 && damage == 0) return true;                        // harmless smart wall
            if (blocks && damage == 0) return false;
            if (damage > 0) DamageSelf(damage, true);
            return !blocks;
        }

        // Raw damage points (collisions and acid count in the original's units, unlike weapon hits)
        void DamageSelf(int points, bool sound)
        {
            if (health == null || points <= 0) return;
            bool play = sound && contactSoundTimer == 0;
            if (play) contactSoundTimer = 4;   // the original re-plays each tick; one every few is plenty
            health.TakeRawDamage(points, play);
        }

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

                Derez.Burst(transform.position + Vector3.up, health != null ? health.derezMaterial : null, 20, 5f);
                x = ToUnits(o.transform.position.x);
                y = ToUnits(o.transform.position.z);
                heading = (target.heading + 0x40) & 0xFF;
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
            if (gm == null || !gm.TryFindSpawnPoint(out var p, 0f)) return;
            var mat = health != null ? health.derezMaterial : null;
            Derez.Burst(transform.position + Vector3.up, mat, 30, 6f);

            x = ToUnits(p.x);
            y = ToUnits(p.z);
            heading = Random.Range(0, 0xFF);
            // Out near a corner you come in facing the middle
            if (System.Math.Abs(x) > FaceCentreBeyond && System.Math.Abs(y) > FaceCentreBeyond)
                heading = HeadingToward(-x, -y);
            under = false;
            minZ = GroundZ;
            z = DropZ;
            vz = 0;
            Hyper = 0;
            onIce = false;
            lastGate = null;
            Sfx.Play(Snd.Warp);
            Snap();
            Derez.Burst(Position() + Vector3.up, mat, 30, 6f);
        }

        void HyperspaceFailed()
        {
            Hyper = 0;
            Sfx.Play(Snd.Warp);
            if (health != null) health.TakeRawDamage(health.Shields, true);
        }

        // ---------- collision query ----------

        void Classify(long nx, long ny, int h, out EnemyBase robot, out ObstacleBehaviour gate, out Transform solid)
        {
            robot = null; gate = null; solid = null;
            if (hull == null) return;

            Vector3 pos = new Vector3(nx, z - GroundZ, ny) * UnitsToMetres;
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
                if (ob != null && ob.IsGate)
                {
                    // Under the floor only the transports are there to touch
                    if (under && ob.kind != ObstacleBehaviour.Kind.UpTransport && ob.kind != ObstacleBehaviour.Kind.DownTransport) continue;
                    if (gate == null) gate = ob;
                    continue;
                }
                if (c.isTrigger || under) continue;

                var e = c.GetComponentInParent<EnemyBase>();
                if (e != null)
                {
                    if (robot == null && e.Health != null && !e.Health.IsDead) robot = e;
                    continue;
                }
                var root = c.transform.root;
                if (solid == null && gm != null && gm.Obstacles.Contains(root.gameObject)) solid = root;
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
        float Yaw() => YawFromHeading(heading);

        void Snap()
        {
            snapped = true;
            curPos = prevPos = Position();
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
