using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // One Spectre VR robot, simulated the way SVRWIN.EXE does it (_MOVE_TANKS, _CHASE_PLAYER, _PATROL,
    // _CIRCLE_SPOT, _FIND_CLEAR_PATH; see docs/original-mechanics.md) on the original's 18.2 Hz tick, in
    // its own units. RobotDirector ticks every robot in spawn order; the transform is interpolated
    // between ticks. Units: x = Unity X, y = Unity Z, z = height (0 = the floor); heading 0-255 with
    // 0 = +X and 0x40 = +Z.
    public class Robot : EnemyBase
    {
        // The enemy data as SVRWIN.EXE keeps it in memory (_LOAD_BVAL)
        public struct Data
        {
            public int strength, collideDamage, chaseDist, firingRange, interval, speed, turn, score, bonus, weapon;
            public long FiringRangeSq => (long)firingRange * firingRange;

            public static Data From(MatrixFile.EnemyData d, int lap) => new Data
            {
                strength = d.strength != 0 ? d.strength + lap : 0,
                collideDamage = d.collideDamage,
                chaseDist = d.attackRange * 7,
                firingRange = d.firingRange * 7,
                interval = d.firingInterval,
                speed = d.speed,
                turn = d.turnSpeed,
                score = d.score,
                bonus = d.bonus,
                weapon = d.weapon,
            };
        }

        const int Arena = 0x40000;
        const int PathLength = 0x5000, PathWidth = 0x1400;
        const int ModePatrol = 0, ModeLeave = 1, ModeGuard = 2, ModeChase = 3;

        public Data data;
        public int index;                 // 1-based spawn order (the original's tank number)
        public int regenLeft;             // times it comes back after being destroyed

        // The robot record and object fields the AI uses (offsets in the original)
        public int mode;                  // +8
        int fireCounter;                  // +0xC
        int pathHeading, pathTimer, pathBlock = -1;   // +0x14, +0x18, +0x1A
        public int stun;                  // +0x1E
        int timer;                        // +0x20 (Flyer, Slicer)
        int moveTimer;                    // object +0x6F (Smart Acid / Wall, Slicer)
        public int dying;                 // object +0x5F
        int spinRate, spinTimer;          // object +0x6B / +0x6D (weapon 11 hits)
        public bool visible = true;       // object +0x5D

        public long x, y, z, vz;
        public int heading, roll;           // object +0x38, +0x3C

        Transform model;
        Vector3 prevPos, curPos;
        float prevYaw, curYaw, prevRoll, curRoll;
        readonly Collider[] probe = new Collider[32];

        public bool IsTurret => matrixType == 20;
        public bool IsFlagGuard => matrixType == 20 || matrixType == 35 || matrixType == 45;
        public Vector3 Position => new Vector3(x, z, y) * OriginalMath.UnitsToMetres;

        protected override void Awake()
        {
            base.Awake();
            model = transform.Find("Model");
        }

        public void Setup(int type, Data d)
        {
            matrixType = type;
            data = d;
            collideDamage = Mathf.Max(0, d.collideDamage);
            hasMatrixData = true;
            health.scoreValue = 0;   // scored per hit and as a bonus, not on death
            health.SetMaxShields(Mathf.Max(1, d.strength));
            heading = Random.Range(0, 256);
            mode = type == 7 ? ModePatrol : ModeChase;
            if (type == 45) vz = Random.Range(0, 7) << 8;
            health.Damaged += OnDamaged;
        }

        // _MOVE_SHOTS: every hit scores the robot's points; one it survives stuns it for 18 ticks
        void OnDamaged(int points)
        {
            if (GameManager.Instance != null) GameManager.Instance.AddScore(Mathf.Max(0, data.score));
            if (health.Shields > 0) stun = 0x12;
        }

        // A hit from a weapon with no damage (Spinners) counts the same, minus the damage
        public void HitWithoutDamage()
        {
            OnDamaged(0);
            Sfx.PlayAt(Snd.EnemyHit, transform.position);
        }

        // Destroyed: count ten ticks, then _REPLACE_TANK (RobotDirector / GameManager.ReplaceRobot)
        public void Killed()
        {
            dying = 1;
            stun = 0;
        }

        public void Place(Vector3 position)
        {
            x = OriginalMath.ToUnits(position.x);
            y = OriginalMath.ToUnits(position.z);
            z = 0;
            Snap();
        }

        void Snap()
        {
            curPos = prevPos = Position;
            curYaw = prevYaw = OriginalMath.Yaw(heading);
            curRoll = prevRoll = RollDegrees();
            Write(curPos, curYaw, curRoll);
        }

        float RollDegrees() => OriginalMath.HeadingDelta(roll, 0) * OriginalMath.StepsToDegrees;

        // ---------- display ----------

        public void BeginTick()
        {
            prevPos = curPos;
            prevYaw = curYaw;
            prevRoll = curRoll;
        }

        public void EndTick()
        {
            Flash(stun > 0 && (stun & 1) == 1 && (matrixType == 7 || matrixType == 8));
            curPos = Position;
            curYaw = OriginalMath.Yaw(heading);
            curRoll = RollDegrees();
        }

        // _MOVE_TANKS: a stunned Robot / Super Tank swaps colour every tick
        MeshRenderer[] edges;
        Material[] edgeMats;
        bool flashing;

        void Flash(bool on)
        {
            if (on == flashing) return;
            var look = GameManager.Instance != null ? GameManager.Instance.robotLook : null;
            if (look == null || look.flash == null) return;
            if (edges == null)
            {
                edges = System.Array.FindAll(GetComponentsInChildren<MeshRenderer>(true), r => r.name == "Edges");
                edgeMats = System.Array.ConvertAll(edges, r => r.sharedMaterial);
            }
            flashing = on;
            for (int i = 0; i < edges.Length; i++) edges[i].sharedMaterial = on ? look.flash : edgeMats[i];
        }

        public void Interpolate(float t)
        {
            Write(Vector3.Lerp(prevPos, curPos, t), Mathf.LerpAngle(prevYaw, curYaw, t), Mathf.LerpAngle(prevRoll, curRoll, t));
        }

        void Write(Vector3 pos, float yaw, float rollDeg)
        {
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (model != null) model.localRotation = Quaternion.Euler(0f, 0f, rollDeg);
        }

        // ---------- _MOVE_TANKS, one robot ----------

        public void Tick(RobotDirector director, ref bool needChaser)
        {
            if (!visible) return;
            mode &= 0x7F;
            var gm = GameManager.Instance;
            if (stun == 0)
            {
                if (needChaser)
                {
                    needChaser = false;
                    if (matrixType != 22) mode = ModeChase;
                }
                if (spinRate == 0)
                {
                    if (matrixType == 35 || matrixType == 45) CircleSpot();
                    else if (matrixType == 7 && index < 6 && director.FlagFor(index - 1, out var flag)) Patrol(director, flag, false);
                    else if (matrixType == 22) Patrol(director, director.PlayerPos, true);
                    else ChasePlayer(director);
                }
                else heading = (heading + spinRate) & 0xFF;

                if (dying > 0 && dying++ > 10)
                {
                    dying = 0;
                    visible = false;
                    gm.ReplaceRobot(this);
                }
            }
            else stun--;

            // _MOVE_OBJECT: a weapon-11 hit keeps the robot spinning a while
            if (spinRate != 0)
            {
                heading = (heading + spinRate) & 0xFF;
                if (--spinTimer < 0) spinRate = spinTimer = 0;
            }
            x = System.Math.Clamp(x, -Arena, Arena);
            y = System.Math.Clamp(y, -Arena, Arena);
        }

        public void SpinFromHit()
        {
            spinRate = 0x10;
            spinTimer = 0xB4;
        }

        // Back after being destroyed: a new spot, full strength, as _REPLACE_TANK does
        public void Replaced(Vector3 position)
        {
            visible = true;
            dying = 0;
            timer = 0;
            mode = matrixType == 7 ? ModePatrol : ModeChase;
            Place(position);
            if (matrixType == 21) ChangeShape(10002);
            health.Revive(transform.position, transform.rotation);
            if (matrixType == 26)   // Invisible Tank: back on radar only
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        // ---------- behaviours ----------

        void Move(int speed)
        {
            x += OriginalMath.Cos[heading] * speed;
            y += OriginalMath.Sin[heading] * speed;
        }

        int AngleTo(Vector3 target) => OriginalMath.Angle(OriginalMath.ToUnits(target.x) - x, OriginalMath.ToUnits(target.z) - y);
        long DistTo(Vector3 target) => OriginalMath.Dist(OriginalMath.ToUnits(target.x) - x, OriginalMath.ToUnits(target.z) - y);
        int QDistTo(Vector3 target) => OriginalMath.QDist(OriginalMath.ToUnits(target.x) - x, OriginalMath.ToUnits(target.z) - y);

        void ChaseThing(Vector3 target)
        {
            OriginalMath.FixHeading(ref heading, AngleTo(target), data.turn << 2);
            Move(data.speed);
        }

        void PointAt(Vector3 target) => OriginalMath.FixHeading(ref heading, AngleTo(target), data.turn << 2);

        void LeaveThing(Vector3 target)
        {
            OriginalMath.FixHeading(ref heading, AngleTo(target) - 0x80, 3);
            Move(data.speed);
            if (matrixType == 22 && z < 0xF00) z += 0x100;
        }

        void CircleSpot()
        {
            heading = (heading + data.turn * 2) & 0xFF;
            Move(data.speed * 4);
            if (matrixType == 45)
            {
                if (z < 0) vz = 0xC00;
                vz -= 0x100;
                z += vz;
            }
        }

        void Patrol(RobotDirector director, Vector3 target, bool flyer)
        {
            int q = QDistTo(target);
            if (flyer && mode != ModeChase && mode != ModeLeave) mode = ModeChase;
            switch (mode)
            {
                case ModeLeave:
                    if (flyer)
                    {
                        LeaveThing(director.PlayerPos);
                        if (--timer < 0)
                        {
                            timer = 0xA2;
                            mode = ModeChase;
                            Sfx.PlayAt(Snd.FlyerDive, transform.position);
                        }
                        return;
                    }
                    if (q < 0x50) LeaveThing(target);
                    else mode = ModeGuard;
                    return;
                case ModeGuard:
                    if (data.chaseDist < q) mode = ModePatrol;
                    PointAt(director.PlayerPos);
                    if (DistTo(director.PlayerPos) < data.FiringRangeSq && director.PlayerOnGround) mode = ModeChase;
                    break;
                case ModeChase:
                    ChasePlayer(director);
                    return;
                default:
                    mode = ModePatrol;
                    if (q < 0x51) mode = ModeGuard;
                    else
                    {
                        if (DistTo(director.PlayerPos) < data.FiringRangeSq && director.PlayerOnGround && !flyer) mode = ModeChase;
                        ChaseThing(target);
                    }
                    break;
            }
            if (matrixType != 7 && matrixType != 22) mode = ModeChase;
        }

        void ChasePlayer(RobotDirector director)
        {
            if (!visible || dying != 0) return;
            Vector3 player = director.PlayerPos;
            int speed = data.speed;
            int reverse = 0;
            int backOff;
            if (matrixType == 24 || matrixType == 23)
            {
                backOff = 0;
                if (--moveTimer < 0)
                {
                    moveTimer = Random.Range(0, 0x12) + 0x36;
                    Sfx.PlayAt(Snd.SmartMove, transform.position);
                }
                if (moveTimer < 0x24) speed = 0;
            }
            else backOff = 0x40;

            int want, d, stopDist, path;
            long d2;
            if (matrixType == 25)
            {
                // Cyber Mud heads for a spot in front of the player, to block the way
                Vector3 block = director.PointAhead(0x5A00);
                d2 = DistTo(block);
                d = (int)(d2 >> 8);
                stopDist = 0x19;
                want = d < 0x19 ? AngleTo(player) : AngleTo(block);
                path = FindClearPath(director, want, false, false);
            }
            else
            {
                stopDist = 0x60;
                d2 = DistTo(player);
                d = (int)(d2 >> 8);
                want = matrixType == 8 ? AngleTo(director.Lead(d2)) : AngleTo(player);
                if (d < backOff && data.collideDamage == 0)
                {
                    reverse = -speed;
                    speed = reverse;
                }
                path = FindClearPath(director, want, reverse != 0, true);
            }

            OriginalMath.FixHeading(ref heading, pathHeading, data.turn << 2);
            if (path == -1)
            {
                if (d < stopDist && System.Math.Abs(pathHeading - want) < 8 && reverse == 0 && data.collideDamage == 0) speed = 0;
                reverse = 0;
            }
            else speed = 0;

            if (matrixType == 22) Flyer(director, d);
            if (matrixType == 21) Slicer(director, d);

            Move(speed);

            // Firing
            if (data.weapon < 0 || director.PlayerUnder) return;
            int level = GameManager.Instance.Level;
            int wait = Mathf.Max((d >> 3) - (level >> 1), 0x10 - (level >> 3));
            fireCounter = Mathf.Min(fireCounter + 1, 0x708);
            if (wait * data.interval / 0x78 < fireCounter && dying == 0 && (reverse == 0 || level > 14) && d2 < data.FiringRangeSq)
            {
                int weapon = data.weapon;
                if (director.PlayerAirborne) { if (weapon == 0) weapon = 12; }
                else if (weapon == 12) weapon = 0;
                director.Shots.RobotShoot(this, weapon);
                fireCounter = 0;
            }
        }

        void Flyer(RobotDirector director, int d)
        {
            if (--timer < 0)
            {
                timer = 0xA2;
                mode = ModeLeave;
            }
            if (d < 0x1C8)
            {
                if (z > 0xEFF) Sfx.PlayAt(Snd.FlyerDive, transform.position);
                z += z < director.PlayerZ + 0x300 ? 0x100 : -0x100;
            }
            else
            {
                if (z < 0xF00) z += 0x100;
                if (d > 0x2F8) timer = 0x5A;
            }
            roll = (heading - pathHeading) & 0xFF;   // banks into its turns
        }

        // Slicers hop toward you, and jump at you when you're on wings
        void Slicer(RobotDirector director, int d)
        {
            int span = Mathf.Max(d / 0x28, 0xE);
            timer = Mathf.Min(timer, span);
            if (--timer < 0)
            {
                timer = Mathf.Max(span, 8);
                if (z < 1)
                {
                    if (d < 0x15E) Sfx.PlayAt(Snd.SlicerNear, transform.position);
                    else if (d < 800) Sfx.PlayAt(Snd.SlicerFar, transform.position);
                    else timer = 0xC;
                }
                else Sfx.PlayAt(Snd.SlicerJump, transform.position);
            }
            if (moveTimer < 1 && d < 0x40 && z == 0 && director.PlayerFloating)
            {
                vz = 0xC00;
                moveTimer = 0x36;
            }
            if (z == 0 && moveTimer == 0)
            {
                if (director.PlayerUnder) { ChangeShape(10023); z = -0x1400; }   // burrows to follow you under the floor
                return;
            }
            if (moveTimer > 0) moveTimer--;
            if (z < 0)
            {
                if (!director.PlayerUnder) { ChangeShape(10002); z = 0; vz = 0; moveTimer = 0x24; }
                else { ChangeShape(10023); z = -0x1400; }
            }
            // The hop's frames: rising, falling, in between
            else if (vz > 0x300) ChangeShape(10099);
            else if (vz < -0x300) ChangeShape(10100);
            else if (z != 0) ChangeShape(10098);
            z += vz;
            if (z > 0) vz -= 0x100;
        }

        // _CHANGE_SHAPE: swap the model for another shape of the matrix (single-part robots)
        int shapeId;
        void ChangeShape(int id)
        {
            if (id == shapeId) return;
            var gm = GameManager.Instance;
            var geometry = gm != null ? gm.Shapes?.ShapeById(id) : null;
            if (geometry == null) return;
            shapeId = id;
            var part = model != null ? model.Find("Shape") : null;
            if (part == null) return;
            foreach (var f in part.GetComponentsInChildren<MeshFilter>(true))
                f.sharedMesh = f.name == "Panels" ? geometry.Body : geometry.Edges(gm.obstacleLook.edgeThickness);
            edges = null;   // the stun flash re-reads its renderers
        }

        // ---------- _FIND_CLEAR_PATH / _CHECK_TANK_PATH ----------

        int FindClearPath(RobotDirector director, int want, bool reverse, bool targetIsPlayer)
        {
            int current = heading;
            if (current != pathHeading && --pathTimer > 0) return -2;
            if (--pathTimer > 0) return pathBlock;

            if (reverse)
            {
                want = (want + 0x80) & 0xFF;
                current = want;
            }
            int flip = reverse ? 0x80 : 0;
            if (PathClear(director, want, targetIsPlayer)) return Chose(want + flip);
            if (PathClear(director, current, targetIsPlayer)) return Chose(current + flip);
            for (int off = 0; ;)
            {
                off = off >= 0 ? -(off + 4) : -off;
                if (System.Math.Abs(off) > 0x81) break;
                if (PathClear(director, (want + off) & 0xFF, targetIsPlayer)) return Chose(want + off + flip);
            }
            // Boxed in: the original simply moves the robot somewhere else
            GameManager.Instance.TryFindHiddenSpot(out var spot);
            Place(spot);
            pathBlock = -1;
            return -1;
        }

        int Chose(int h)
        {
            pathHeading = h & 0xFF;
            pathTimer = 9;
            pathBlock = -1;
            return -1;
        }

        // A box PathLength ahead (half length) and PathWidth to either side, centred 1.125 lengths out,
        // against obstacles (not fans, acid, up transports or pushers) and other robots
        bool PathClear(RobotDirector director, int h, bool targetIsPlayer)
        {
            Vector3 dir = OriginalMath.Direction(h);
            Vector3 centre = Position + dir * (PathLength * 1.125f * OriginalMath.UnitsToMetres) + Vector3.up * 2f;
            var half = new Vector3(PathWidth, 0x1800, PathLength) * OriginalMath.UnitsToMetres;
            int n = Physics.OverlapBoxNonAlloc(centre, half, probe, Quaternion.LookRotation(dir), ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = probe[i];
                if (c.transform.IsChildOf(transform)) continue;
                var obstacle = c.GetComponentInParent<ObstacleBehaviour>();
                if (obstacle != null)
                {
                    if (obstacle.type == 16 || obstacle.type == 36 || obstacle.type == 39) continue;
                    if (QDistTo(obstacle.transform.position) > data.chaseDist) continue;
                    return false;
                }
                var robot = c.GetComponentInParent<Robot>();
                if (robot != null) { if (robot.visible) return false; continue; }
                if (!targetIsPlayer && c.GetComponentInParent<PlayerTank>() != null) return false;
            }
            return true;
        }
    }
}
