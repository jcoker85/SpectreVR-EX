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
            public int heading, life, weapon, speed, spin;
            public Robot owner;          // the robot that fired it, or null
            public bool fromPlayer;
            public Transform visual;
            public Vector3 prevPos, curPos;
            public Vector3 Position => new Vector3(x, z, y) * OriginalMath.UnitsToMetres;
        }

        readonly RobotDirector director;
        readonly List<Shot> shots = new List<Shot>();
        readonly Collider[] hits = new Collider[16];

        public ShotSystem(RobotDirector director) => this.director = director;

        public void Clear()
        {
            foreach (var s in shots) if (s.visual != null) Object.Destroy(s.visual.gameObject);
            shots.Clear();
        }

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

        // _EXPLODE_TANK: five pieces thrown out of a destroyed robot
        public void Explode(Robot r)
        {
            for (int i = 0; i < 0x500; i += 0x100)
                Fire(r, false, r.x, r.y, r.z, r.heading + i / 5 + (10 - Random.Range(0, 20)), Debris);
        }

        // _DO_SHOOT. The player's own fire sounds come from its weapon (the original's later call wins).
        void Fire(Robot owner, bool fromPlayer, long x, long y, long z, int heading, int weapon)
        {
            if (shots.Count >= MaxShots) return;
            var s = new Shot { x = x, y = y, z = z, heading = heading & 0xFF, owner = owner, fromPlayer = fromPlayer, weapon = weapon, life = 0x12, speed = 0x2000 };
            string sound = Snd.RobotShot;
            int shape = 0;
            switch (weapon)
            {
                case 0: sound = Snd.EnemyShot; break;
                case 2: s.life = 0x10E; s.spin = 7; s.speed = 0; sound = null; break;
                case 9: s.life = 0x19; s.speed = 0x1800; sound = null; break;
                case 10: s.life = 0xB4; s.speed = 0x1000; shape = 10001; sound = null; break;
                case 11: s.life = 0xB4; s.speed = 0x1000; s.spin = 8; shape = 10087; sound = null; break;
                case 12: s.life = 0x13; s.vz = 0x400; s.weapon = 0; break;
                case 13: s.life = 0x13; s.vz = 0x600; sound = null; break;
                case 15:
                case 16:
                    s.life = 0x36; s.vz = 0x900; s.speed = 0x200; shape = 10083; sound = null;
                    s.x -= OriginalMath.Cos[s.heading] * 0xC;
                    s.y -= OriginalMath.Sin[s.heading] * 0xC;
                    break;
                case 17: s.life = 0x36; s.speed = 0x600; s.vz = 0x300; shape = 10081; sound = null; break;
                case Debris: s.life = 0x36; s.vz = 0x900; s.speed = 0x700; shape = 10102; sound = null; break;
            }
            s.vx = (long)OriginalMath.Cos[s.heading] * s.speed >> 8;
            s.vy = (long)OriginalMath.Sin[s.heading] * s.speed >> 8;
            s.x += s.vx >> 1;
            s.y += s.vy >> 1;
            s.visual = MakeVisual(shape, fromPlayer);
            s.curPos = s.prevPos = s.Position;
            Place(s, s.curPos);
            shots.Add(s);
            if (sound != null && owner != null) Sfx.PlayAt(sound, owner.transform.position);
        }

        Transform MakeVisual(int shape, bool fromPlayer)
        {
            var gm = GameManager.Instance;
            var material = fromPlayer && gm.player.Weapon.projectileMaterial != null ? gm.player.Weapon.projectileMaterial : gm.robotLook.bolt;
            ShapeGeometry geometry = null;
            if (shape != 0)
            {
                var central = MatrixLibrary.Central();
                if (central != null) central.Shapes.TryGetValue(shape, out geometry);
            }
            if (geometry == null)
            {
                var bolt = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                bolt.name = "Bolt";
                Object.Destroy(bolt.GetComponent<Collider>());
                bolt.transform.localScale = new Vector3(0.25f, 0.6f, 0.25f);
                bolt.GetComponent<MeshRenderer>().sharedMaterial = material;
                return bolt.transform;
            }
            var root = new GameObject("Shot").transform;
            var go = new GameObject("Edges");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = geometry.Edges(gm.obstacleLook.edgeThickness);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return root;
        }

        static void Place(Shot s, Vector3 pos)
        {
            if (s.visual == null) return;
            var dir = new Vector3(s.vx, s.vz, s.vy);
            var rot = dir.sqrMagnitude > 1f ? Quaternion.LookRotation(dir) : Quaternion.Euler(0f, OriginalMath.Yaw(s.heading), 0f);
            if (s.visual.name == "Bolt") rot *= Quaternion.Euler(90f, 0f, 0f);   // capsules stand on Y
            s.visual.SetPositionAndRotation(pos, rot);
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

        // _MOVE_SHOTS
        public void Tick()
        {
            for (int i = 0; i < shots.Count; i++)
            {
                var s = shots[i];
                s.prevPos = s.curPos;
                if (--s.life <= 0) { Kill(s); continue; }
                switch (s.weapon)
                {
                    case 15:
                    case 16:
                        s.vz -= 0x80;
                        if (s.vz < -0x300) { Kill(s); continue; }
                        break;
                    case 17:
                        s.vz -= 0x80;
                        if (s.z < 0) { Kill(s); continue; }
                        break;
                    case 9:
                    case 10:
                        Home(s);
                        break;
                    case 13:
                        // A pulse burst drops to the floor and skims along it as a spinning plain shot
                        s.vz -= 0x600;
                        if (s.z + s.vz < 0) { s.z = 0; s.vz = 0; s.weapon = 0; s.spin = 8; }
                        break;
                    case Debris:
                        s.vz -= (s.vz >> 6) + 0x80;
                        if (s.z < 0 && s.vz < 0) s.vz = -s.vz;
                        break;
                }
                if (s.spin != 0) s.heading = (s.heading + s.spin) & 0xFF;

                s.x += s.vx;
                s.y += s.vy;
                s.z += s.vz;
                s.curPos = s.Position;
                if (System.Math.Abs(s.x) > Arena || System.Math.Abs(s.y) > Arena) { Kill(s); continue; }
                if (s.weapon == Debris) continue;

                bool splashes = s.weapon == 1 || s.weapon == 17;
                if (HitSomething(s) || (s.weapon == 17 && s.z < 1))
                {
                    if (splashes) Splash(s);
                    Kill(s);
                    continue;
                }
                if (HitObstacle(s)) Kill(s);
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
                targetZ = haveTarget ? r.z + MidHeight(r) : 0x500;   // the middle of its collision box
            }
            else
            {
                haveTarget = true;
                target = director.PlayerPos;
                targetZ = director.PlayerZ;
            }
            if (haveTarget)
            {
                long tx = OriginalMath.ToUnits(target.x) - s.x, ty = OriginalMath.ToUnits(target.z) - s.y;
                OriginalMath.FixHeading(ref s.heading, OriginalMath.Angle(tx, ty), s.weapon == 10 ? 8 : 2);
                s.vx = (long)OriginalMath.Cos[s.heading] * s.speed >> 8;
                s.vy = (long)OriginalMath.Sin[s.heading] * s.speed >> 8;
            }
            if (s.z < targetZ) { s.z = targetZ; s.vz = 0; }
            Vector3 dir = OriginalMath.Direction(s.heading);
            bool blocked = Physics.CheckBox(s.Position + dir * (0x3C00 * OriginalMath.UnitsToMetres),
                new Vector3(0x1000, 0x800, 0x3C00) * OriginalMath.UnitsToMetres, Quaternion.LookRotation(dir),
                ~0, QueryTriggerInteraction.Ignore);
            if (!blocked)
            {
                if (targetZ < s.z) { if (s.vz > 0) s.vz = 0; s.vz -= 0x200; }
                else { s.z = targetZ; s.vz = 0; }
            }
            else if (s.z < 0x3200) s.vz += 0x200;
        }

        static long MidHeight(Robot r)
        {
            var box = r.GetComponent<BoxCollider>();
            return box != null ? OriginalMath.ToUnits(box.center.y) : 0;
        }

        // _FIND_CLOSEST_TANK
        Robot ClosestRobot(Shot s)
        {
            Robot best = null;
            long bestD = long.MaxValue;
            foreach (var r in director.Robots)
            {
                if (r == null || !r.visible || r.dying != 0) continue;
                long d = OriginalMath.Dist(r.x - s.x, r.y - s.y);
                if (d < bestD) { bestD = d; best = r; }
            }
            return best;
        }

        static int Damage(int weapon)
        {
            var m = GameManager.Instance.CurrentMatrix ?? MatrixLibrary.Central();
            return m != null && m.Weapons.TryGetValue(weapon, out var w) ? w.damage : 1;
        }

        // _CUBE_COLLIDE against the player and every robot but the shooter, here and half a step back
        bool HitSomething(Shot s)
        {
            var target = Overlap(s.curPos, s) ?? Overlap(s.curPos - new Vector3(s.vx, s.vz, s.vy) * (0.5f * OriginalMath.UnitsToMetres), s);
            if (target == null) return false;
            if (target.GetComponent<OrbitShield>() != null) return true;   // a shield kills the shot and stays (weapon 7)

            var health = target.GetComponentInParent<Health>();
            var robot = health.GetComponent<Robot>();
            if (robot != null && robot.dying != 0) return false;
            if (s.weapon == 1 || s.weapon == 17) return true;   // splash instead
            if (s.weapon == 11 && robot != null) robot.SpinFromHit();
            if (s.weapon == 16 && health.team == Team.Player) GameManager.Instance.player.Motor.DrainFloating(0x10E);
            int damage = Damage(s.weapon);
            if (damage <= 0 && robot != null) robot.HitWithoutDamage();   // Spinners: still scores and stuns
            else health.TakeDamage(damage);
            return true;
        }

        Collider Overlap(Vector3 pos, Shot s)
        {
            var half = new Vector3(0x200, 0x200, 0x300) * OriginalMath.UnitsToMetres;
            int n = Physics.OverlapBoxNonAlloc(pos, half, hits, Quaternion.Euler(0f, OriginalMath.Yaw(s.heading), 0f), ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i];
                if (c.GetComponent<OrbitShield>() != null) { if (!s.fromPlayer) return c; continue; }
                if (c.isTrigger) continue;
                var h = c.GetComponentInParent<Health>();
                if (h == null || h.IsDead) continue;
                if (s.fromPlayer && h.team == Team.Player) continue;
                if (s.owner != null && h.gameObject == s.owner.gameObject) continue;
                return c;
            }
            return null;
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
        // (never the one who fired it)
        void Splash(Shot s)
        {
            Derez.Burst(s.curPos, GameManager.Instance.robotLook.bolt, 20, 8f);
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
