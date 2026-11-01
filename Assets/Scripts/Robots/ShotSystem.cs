using System.Collections.Generic;
using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // The original's shot slots (_DO_SHOOT, _MOVE_SHOTS), for the player and the robots alike: each
    // shot has a life in ticks, a speed and a weapon, moves on the 18.2 Hz tick, and hits whatever
    // tank (the player or a robot) it touches other than the one that fired it.
    public class ShotSystem
    {
        const int MaxShots = 45;
        const int Arena = 0x40000;
        const int Debris = 0x7F;

        class Shot
        {
            public long x, y, z, vx, vy, vz;
            public int heading, life, weapon, speed, spin, roll, rollRate = 0x10;
            public int fading;           // ticks left of its de-res; it can no longer hit anything
            public Robot owner;          // the robot that fired it, or null
            public bool fromPlayer;
            public Transform visual;
            public int colour;
            public Vector3 prevPos, curPos;
            public Vector3 Position => new Vector3(x, z, y) * OriginalMath.UnitsToMetres;
        }

        readonly RobotDirector director;
        readonly List<Shot> shots = new List<Shot>();
        readonly Collider[] hits = new Collider[16];
        readonly List<Collider> struck = new List<Collider>();
        readonly Collider[] pathHits = new Collider[32];
        bool hitFades;   // set by HitSomething: the shot de-reses where it struck, rather than just going

        public ShotSystem(RobotDirector director) => this.director = director;

        public void Clear()
        {
            foreach (var s in shots) if (s.visual != null) Object.Destroy(s.visual.gameObject);
            shots.Clear();
        }

        // The live shots, for comparing traces with the running original (Tools/win31/traceview.py)
        public IEnumerable<(long x, long y, long z, int heading, int life, int weapon, Robot owner, bool fromPlayer, int slot)> Trace()
        {
            for (int i = 0; i < shots.Count; i++)
            {
                var s = shots[i];
                if (s.fading == 0 && s.life > 0) yield return (s.x, s.y, s.z, s.heading, s.life, s.weapon, s.owner, s.fromPlayer, s.GetHashCode());
            }
        }

        // The level's shots and hits, for the recap's Accuracy and Avoidance; dying doesn't reset them
        public int MyShots, MyHits, RobotShots, RobotHits;

        public void ResetCounts() => MyShots = MyHits = RobotShots = RobotHits = 0;

        // _MY_SHOOT: from the craft, at its height, along its heading (weapon 5 three ways)
        public void PlayerShoot(int weapon)
        {
            var motor = director.Motor;
            if (motor == null) return;
            Vector3 p = motor.SimPosition;
            long x = OriginalMath.ToUnits(p.x), y = OriginalMath.ToUnits(p.z), z = motor.ZUnits;
            int h = motor.OriginalHeading;
            if (weapon == 5)
            {
                Fire(null, true, x, y, z, h, 0);
                Fire(null, true, x, y, z, h - 0x10, 0);
                Fire(null, true, x, y, z, h + 0x10, 0);
            }
            else Fire(null, true, x, y, z, h, weapon);
        }

        // _TANK_SHOOT
        public void RobotShoot(Robot r, int weapon)
        {
            long z = r.IsTurret ? r.z : r.z + 0x400;
            if (weapon == 5)
            {
                Fire(r, false, r.x, r.y, z, r.heading, 0);
                Fire(r, false, r.x, r.y, z, r.heading - 0x10, 0);
                Fire(r, false, r.x, r.y, z, r.heading + 0x10, 0);
            }
            else if (weapon >= 0) Fire(r, false, r.x, r.y, z, r.heading, weapon);
        }

        // _EXPLODE_TANK: five pieces thrown out of a destroyed craft, in its colour
        public void Explode(Robot r)
        {
            debrisColour = DebrisColour(r.matrixType);
            for (int i = 0; i < 0x500; i += 0x100)
                Fire(r, false, r.x, r.y, r.z, r.heading + i / 5 + (10 - Random.Range(0, 20)), Debris);
        }

        public void ExplodePlayer()
        {
            var motor = director.Motor;
            if (motor == null) return;
            debrisColour = BuiltinShapes.TypeColour(6);
            Vector3 p = motor.SimPosition;
            long x = OriginalMath.ToUnits(p.x), y = OriginalMath.ToUnits(p.z);
            for (int i = 0; i < 0x500; i += 0x100)
                Fire(null, false, x, y, motor.ZUnits, motor.OriginalHeading + i / 5 + (10 - Random.Range(0, 20)), Debris);
        }

        int debrisColour;

        // A craft's colour, or plain grey for one drawn in a texture, which has no one colour to match
        int DebrisColour(int type)
        {
            var parts = GameSettings.DisplayTextures ? GameManager.Instance.Shapes?.ForType(type) : null;
            if (parts != null)
                foreach (var part in parts)
                    if (part.shape.TexturedFaces.Count > 0) return 0;
            return BuiltinShapes.TypeColour(type);
        }

        // The colour _DO_SHOOT gives each weapon's shot; most are red
        int ShotColour(int weapon) => weapon switch
        {
            0 => 0x20,
            7 or 16 => 0x40,
            9 => 0xB0,
            11 => 0x60,
            Debris => debrisColour,
            _ => 0x10,
        };

        // _DO_SHOOT. The player's own fire sounds come from its weapon (the original's later call wins).
        void Fire(Robot owner, bool fromPlayer, long x, long y, long z, int heading, int weapon)
        {
            if (weapon != Debris)
            {
                if (fromPlayer) MyShots++;
                else RobotShots++;
            }
            if (shots.Count >= MaxShots) return;
            var s = new Shot { x = x, y = y, z = z, heading = heading & 0xFF, owner = owner, fromPlayer = fromPlayer, weapon = weapon, life = 0x12, speed = 0x2000 };
            string sound = Snd.RobotShot;
            int shape = 0;
            switch (weapon)
            {
                case 0: sound = Snd.EnemyShot; break;
                case 2: s.life = 0x10E; s.spin = 7; s.rollRate = 0; s.speed = 0; sound = null; break;
                case 9: s.life = 0x19; s.speed = 0x1800; sound = null; break;
                case 10: s.life = 0xB4; s.speed = 0x1000; shape = 10001; sound = null; break;
                case 11: s.life = 0xB4; s.speed = 0x1000; s.spin = 8; s.rollRate = 0; shape = 10087; sound = null; break;
                case 12: s.life = 0x13; s.vz = 0x400; s.weapon = 0; break;
                case 13: s.life = 0x13; s.vz = 0x600; s.rollRate = 0; sound = null; break;
                case 15:
                case 16:
                    s.life = 0x36; s.vz = 0x900; s.speed = 0x200; s.spin = 4; s.rollRate = 0; shape = 10083; sound = null;
                    s.x -= OriginalMath.Cos[s.heading] * 0xC;
                    s.y -= OriginalMath.Sin[s.heading] * 0xC;
                    break;
                case 17: s.life = 0x36; s.speed = 0x600; s.vz = 0x300; s.spin = 4; s.rollRate = 0; shape = 10081; sound = null; break;
                case Debris: s.life = 0x36; s.vz = 0x900; s.speed = 0x700; shape = 10102; sound = null; break;
            }
            s.vx = (long)OriginalMath.Cos[s.heading] * s.speed >> 8;
            s.vy = (long)OriginalMath.Sin[s.heading] * s.speed >> 8;
            s.x += s.vx >> 1;
            s.y += s.vy >> 1;
            s.colour = ShotColour(weapon);
            s.visual = MakeVisual(shape, s.colour);
            s.curPos = s.prevPos = s.Position;
            Place(s, s.curPos);
            shots.Add(s);
            if (sound != null && owner != null) Sfx.PlayAt(sound, owner.transform.position);
        }

        // A shot's own shape, or the built-in shot for the plain ones, in solid colour
        Transform MakeVisual(int shape, int colour)
        {
            var gm = GameManager.Instance;
            ShapeGeometry geometry = BuiltinShapes.Named("SHOT");
            if (shape != 0)
            {
                var central = MatrixLibrary.Central();
                if (central != null) central.Shapes.TryGetValue(shape, out geometry);
            }
            var root = new GameObject("Shot").transform;
            if (geometry == null) return root;
            var go = new GameObject("Panels");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = geometry.Coloured(colour);
            go.AddComponent<MeshRenderer>().sharedMaterial = gm.obstacleLook.shapes;
            return root;
        }

        static void Place(Shot s, Vector3 pos)
        {
            if (s.visual == null) return;
            s.visual.SetPositionAndRotation(pos, Quaternion.Euler(0f, OriginalMath.Yaw(s.heading), s.roll * 360f / 256f));
        }

        public void Interpolate(float t)
        {
            foreach (var s in shots) Place(s, Vector3.Lerp(s.prevPos, s.curPos, t));
        }

        void Kill(Shot s)
        {
            if (s.visual != null) Object.Destroy(s.visual.gameObject);
            s.life = -1;
        }

        // A shot that runs out carries on as it fades (+0x5F from 4); one that strikes something stops
        // there (from 10). Either way it goes at 0x1E, rising by 3 a tick.
        void Fade(Shot s, bool struck)
        {
            s.fading = struck ? 7 : 9;
            if (struck) s.vx = s.vy = 0;
            if (s.visual == null) return;
            DeRes.Play(s.visual.gameObject, 10, s.fading / OriginalMath.TickRate, false);
            foreach (var r in s.visual.GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        // A plain shape swap on a flying shot (_CHANGE_SHAPE)
        void ChangeShape(Shot s, int shape)
        {
            var central = MatrixLibrary.Central();
            if (s.visual == null || central == null || !central.Shapes.TryGetValue(shape, out var geometry)) return;
            var filter = s.visual.GetComponentInChildren<MeshFilter>();
            if (filter != null) filter.sharedMesh = geometry.Coloured(s.colour);
        }

        static void Advance(Shot s)
        {
            if (s.spin != 0) s.heading = (s.heading + s.spin) & 0xFF;
            s.roll = (s.roll + s.rollRate) & 0xFF;
            s.x += s.vx;
            s.y += s.vy;
            s.z += s.vz;
            s.curPos = s.Position;
        }

        // _MOVE_SHOTS
        public void Tick()
        {
            for (int i = 0; i < shots.Count; i++)
            {
                var s = shots[i];
                s.prevPos = s.curPos;
                if (s.fading > 0)
                {
                    if (--s.fading == 0) Kill(s);
                    else Advance(s);
                    continue;
                }
                if (--s.life <= 0) { Fade(s, false); continue; }
                switch (s.weapon)
                {
                    case 15:
                    case 16:
                        s.vz -= 0x80;
                        if (s.vz < -0x300) { Fade(s, false); continue; }
                        break;
                    case 17:
                        s.vz -= 0x80;
                        if (s.z < 0) { Fade(s, false); continue; }
                        break;
                    case 9:
                    case 10:
                        Home(s);
                        break;
                    case 13:
                        // A pulse burst drops to the floor and skims along it as a spinning plain shot
                        s.vz -= 0x600;
                        if (s.z + s.vz < 0) { s.z = 0; s.vz = 0; s.weapon = 0; s.spin = 8; ChangeShape(s, 10096); }
                        break;
                    case Debris:
                        s.vz -= (s.vz >> 6) + 0x80;
                        if (s.z < 0 && s.vz < 0) s.vz = -s.vz;
                        ChangeShape(s, 10102 + s.life % 3);
                        break;
                }
                Advance(s);
                if (System.Math.Abs(s.x) > Arena || System.Math.Abs(s.y) > Arena)
                {
                    s.vz = 0;
                    Fade(s, true);
                    continue;
                }
                if (s.weapon == Debris) continue;

                bool splashes = s.weapon == 1 || s.weapon == 17;
                if (HitSomething(s) || (s.weapon == 17 && s.z < 1))
                {
                    if (splashes) Splash(s);
                    if (hitFades && !splashes) Fade(s, true);
                    else Kill(s);
                    continue;
                }
                if (HitObstacle(s)) Fade(s, true);
            }
            shots.RemoveAll(s => s.life < 0);
        }

        // Homing (weapons 9 and 10): turn 2 / 8 a tick toward the target - the closest robot for the
        // player's, the player for a robot's - keep to its height, and climb over anything in the way
        void Home(Shot s)
        {
            bool haveTarget;
            Vector3 target;
            long targetZ;
            if (s.fromPlayer)
            {
                var r = ClosestRobot(s);
                haveTarget = r != null;
                target = haveTarget ? r.Position : default;
                targetZ = haveTarget ? r.z + MidHeight(r.matrixType) : 0x500;   // the middle of its collision box
            }
            else
            {
                haveTarget = true;
                target = director.PlayerPos;
                targetZ = MidHeight(6) + director.PlayerZ - PlayerMotor.GroundUnits;
            }
            if (haveTarget)
            {
                long tx = OriginalMath.ToUnits(target.x) - s.x, ty = OriginalMath.ToUnits(target.z) - s.y;
                OriginalMath.FixHeading(ref s.heading, OriginalMath.Angle(tx, ty), s.weapon == 10 ? 8 : 2);
                s.vx = (long)OriginalMath.Cos[s.heading] * s.speed >> 8;
                s.vy = (long)OriginalMath.Sin[s.heading] * s.speed >> 8;
            }
            if (s.z < targetZ) { s.z = targetZ; s.vz = 0; }
            if (!PathBlocked(s))
            {
                if (targetZ < s.z) { if (s.vz > 0) s.vz = 0; s.vz -= 0x200; }
                else { s.z = targetZ; s.vz = 0; }
            }
            else if (s.z < 0x3200) s.vz += 0x200;
        }

        // _CHECK_MISSILE_PATH: a flat rectangle ahead of the shot, any height, against obstacles taller than
        // a floor pad. Robots, the player and the floor don't count.
        bool PathBlocked(Shot s)
        {
            Vector3 dir = OriginalMath.Direction(s.heading);
            Vector3 centre = s.Position + dir * (0x3C00 * 1.125f * OriginalMath.UnitsToMetres);
            centre.y = 0f;
            var half = new Vector3(0x1000, 0x10000, 0x3C00) * OriginalMath.UnitsToMetres;
            int n = Physics.OverlapBoxNonAlloc(centre, half, pathHits, Quaternion.LookRotation(dir), ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
                if (pathHits[i].GetComponentInParent<ObstacleBehaviour>() != null &&
                    pathHits[i].bounds.max.y > 0xFF * OriginalMath.UnitsToMetres) return true;
            return false;
        }

        // The middle of a craft's first shape, which is the object homing shots read (+0x2C, +0x2E)
        static long MidHeight(int type)
        {
            var parts = GameManager.Instance.Shapes?.ForType(type);
            if (parts == null || parts.Count == 0) return 0;
            int[] v = parts[0].shape.verts;
            int low = int.MaxValue, high = int.MinValue;
            for (int i = 2; i < v.Length; i += 3)
            {
                low = Mathf.Min(low, v[i]);
                high = Mathf.Max(high, v[i]);
            }
            return (low + high) / 2;
        }

        // _FIND_CLOSEST_TANK: nothing beyond 2000 counts
        Robot ClosestRobot(Shot s)
        {
            Robot best = null;
            int bestD = 2000;
            foreach (var r in director.Robots)
            {
                if (r == null || !r.visible || r.dying != 0) continue;
                int d = OriginalMath.QDist(r.x - s.x, r.y - s.y);
                if (d < bestD) { bestD = d; best = r; }
            }
            return best;
        }

        static int Damage(int weapon)
        {
            var m = GameManager.Instance.CurrentMatrix ?? MatrixLibrary.Central();
            return m != null && m.Weapons.TryGetValue(weapon, out var w) ? w.damage : 1;
        }

        // _CUBE_COLLIDE against the player and every robot but the shooter, here and half a step back.
        // A shot strikes everything it touches this tick, not just the first.
        bool HitSomething(Shot s)
        {
            struck.Clear();
            Overlap(s.curPos, s);
            Overlap(s.curPos - new Vector3(s.vx, s.vz, s.vy) * (0.5f * OriginalMath.UnitsToMetres), s);
            if (struck.Count == 0) return false;
            hitFades = true;
            foreach (var target in struck)
                if (target.GetComponent<OrbitShield>() != null) return true;   // a shield kills the shot and stays (weapon 7)

            bool hit = false;
            foreach (var target in struck) hit |= Strike(s, target.GetComponentInParent<Health>());
            return hit;
        }

        bool Strike(Shot s, Health health)
        {
            var robot = health.GetComponent<Robot>();
            if (robot != null && robot.dying != 0) return false;
            if (s.fromPlayer) MyHits++;
            else if (health.team == Team.Player) RobotHits++;
            if (s.weapon == 1 || s.weapon == 17) return true;   // splash instead
            if (robot == null || s.weapon == 11) hitFades = false;
            if (s.weapon == 11)
            {
                if (robot != null) robot.SpinFromHit();
                else GameManager.Instance.player.Motor.SpinFromHit();
            }
            if (s.weapon == 16 && health.team == Team.Player) GameManager.Instance.player.Motor.DrainFloating(0x10E);
            int damage = Damage(s.weapon);
            if (damage <= 0 && robot != null) robot.HitWithoutDamage();   // Spinners: still scores and stuns
            else health.TakeDamage(damage);
            if (health.team == Team.Player && !health.IsDead) DeRes.Play(health.gameObject, 5, 0.55f, true);   // _D_P_DE_RES
            return true;
        }

        void Overlap(Vector3 pos, Shot s)
        {
            var half = new Vector3(0x200, 0x200, 0x300) * OriginalMath.UnitsToMetres;
            int n = Physics.OverlapBoxNonAlloc(pos, half, hits, Quaternion.Euler(0f, OriginalMath.Yaw(s.heading), 0f), ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i];
                if (c.GetComponent<OrbitShield>() != null) { if (!s.fromPlayer) struck.Add(c); continue; }
                if (c.isTrigger) continue;
                var h = c.GetComponentInParent<Health>();
                if (h == null || h.IsDead) continue;
                if (s.fromPlayer && h.team == Team.Player) continue;
                if (s.owner != null && h.gameObject == s.owner.gameObject) continue;
                if (!struck.Exists(o => o.GetComponentInParent<Health>() == h)) struck.Add(c);
            }
        }

        // Obstacles stop shots, except the floor pads they fly over
        bool HitObstacle(Shot s)
        {
            int n = Physics.OverlapBoxNonAlloc(s.curPos, Vector3.one * (0x200 * OriginalMath.UnitsToMetres), hits, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var o = hits[i].GetComponentInParent<ObstacleBehaviour>();
                if (o == null) continue;
                if (o.kind == ObstacleBehaviour.Kind.Acid || o.kind == ObstacleBehaviour.Kind.Ice ||
                    o.kind == ObstacleBehaviour.Kind.Slow || o.kind == ObstacleBehaviour.Kind.Pusher) continue;
                if (s.weapon == 11) o.SpinFromHit();
                return true;
            }
            return false;
        }

        // Weapons 1 and 17: robots within 0x0E / 0x40 / 0x80 lose 5 / 2 / 2, the player 10 per band
        // (never the one who fired it). The original's loop also stuns and scores every robot out of range,
        // which reads as an oversight, so here a blast only touches what it reaches.
        void Splash(Shot s)
        {
            foreach (var r in director.Robots)
            {
                if (r == null || !r.visible || r.dying != 0 || r == s.owner) continue;
                int q = OriginalMath.QDist(r.x - s.x, r.y - s.y);
                int loss = (q < 0xE ? 5 : 0) + (q < 0x40 ? 2 : 0) + (q < 0x80 ? 2 : 0);
                if (loss > 0) r.Health.TakeDamage(loss);
            }
            if (s.fromPlayer || !director.PlayerAlive) return;
            var p = director.PlayerPos;
            int pq = OriginalMath.QDist(OriginalMath.ToUnits(p.x) - s.x, OriginalMath.ToUnits(p.z) - s.y);
            int bands = (pq < 0x40 ? 1 : 0) + (pq < 0x80 ? 1 : 0) + (pq < 0xC4 ? 1 : 0);
            if (bands > 0) GameManager.Instance.player.Health.TakeRawDamage(bands * 10, true);
        }
    }
}
