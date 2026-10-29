using System.Collections.Generic;
using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // Runs the robots and their shots on the original's 18.2 Hz tick, in SVRWIN.EXE's order: every
    // robot in spawn order (_MOVE_TANKS), then every shot (_MOVE_SHOTS). Lives on the GameManager.
    public class RobotDirector : MonoBehaviour
    {
        const int MaxTicksPerFrame = 8;

        public readonly List<Robot> Robots = new List<Robot>();
        public RobotShots Shots { get; private set; }

        GameManager gm;
        float accumulator;

        void Awake()
        {
            gm = GetComponent<GameManager>();
            Shots = new RobotShots(this);
        }

        public void Clear()
        {
            Robots.Clear();
            Shots.Clear();
        }

        void Update()
        {
            if (gm == null || !gm.Playing) return;
            accumulator += Time.deltaTime * OriginalMath.TickRate;
            int ticks = 0;
            while (accumulator >= 1f && ticks < MaxTicksPerFrame)
            {
                accumulator -= 1f;
                ticks++;
                Tick();
            }
            if (ticks == MaxTicksPerFrame) accumulator = 0f;
            foreach (var r in Robots) if (r != null) r.Interpolate(accumulator);
            Shots.Interpolate(accumulator);
        }

        void Tick()
        {
            bool needChaser = true;
            foreach (var r in Robots) if (r != null && r.mode == 3) needChaser = false;
            foreach (var r in Robots)
            {
                if (r == null) continue;
                r.BeginTick();
                r.Tick(this, ref needChaser);
                r.EndTick();
            }
            Shots.Tick();
        }

        // ---------- what the robots know about the player ----------

        PlayerMotor Motor => gm.player != null ? gm.player.Motor : null;
        public bool PlayerAlive => gm.player != null && !gm.player.Health.IsDead;
        public Vector3 PlayerPos => Motor != null ? Motor.SimPosition : Vector3.zero;
        public long PlayerZ => Motor != null ? Motor.ZUnits : 0;
        public bool PlayerOnGround => Motor == null || Motor.OnGround;
        public bool PlayerAirborne => Motor != null && Motor.Airborne;
        public bool PlayerUnder => Motor != null && Motor.Under;
        public bool PlayerFloating => Motor != null && Motor.Floating > 0;

        // _GUESS_AT: where the player will be, d2 / 0x2000 ticks on
        public Vector3 Lead(long d2) => PlayerPos + (Motor != null ? Motor.TickDelta : Vector3.zero) * (d2 / 0x2000);

        // _BLOCK_AT: a point this far along the player's (model) heading. The original keeps the
        // player's model heading at the move heading + 0x80, so this lands behind the craft.
        public Vector3 PointAhead(int units) => PlayerPos - (Motor != null ? Motor.transform.forward : Vector3.forward) * (units * OriginalMath.UnitsToMetres);

        public bool FlagFor(int i, out Vector3 position)
        {
            position = default;
            if (i < 0 || i >= gm.Flags.Count || gm.Flags[i] == null || !gm.Flags[i].isActiveAndEnabled) return false;
            position = gm.Flags[i].transform.position;
            return true;
        }
    }

    // The original's shot slots (_DO_SHOOT, _MOVE_SHOTS) for robot fire. The player's cannon still
    // uses Projectile; its special weapons come with the weapons step.
    public class RobotShots
    {
        const int MaxShots = 45;
        const int Arena = 0x40000;
        const int Debris = 0x7F;

        class Shot
        {
            public long x, y, z, vx, vy, vz;
            public int heading, life, weapon, speed, spin;
            public Robot owner;
            public Transform visual;
            public Vector3 prevPos, curPos;
            public Vector3 Position => new Vector3(x, z, y) * OriginalMath.UnitsToMetres;
        }

        readonly RobotDirector director;
        readonly List<Shot> shots = new List<Shot>();
        readonly Collider[] hits = new Collider[16];

        public RobotShots(RobotDirector director) => this.director = director;

        public void Clear()
        {
            foreach (var s in shots) if (s.visual != null) Object.Destroy(s.visual.gameObject);
            shots.Clear();
        }

        // _TANK_SHOOT
        public void RobotShoot(Robot r, int weapon)
        {
            long z = r.IsTurret ? r.z : r.z + 0x400;
            if (weapon == 5)
            {
                Fire(r, r.x, r.y, z, r.heading, 0);
                Fire(r, r.x, r.y, z, r.heading - 0x10, 0);
                Fire(r, r.x, r.y, z, r.heading + 0x10, 0);
            }
            else if (weapon >= 0) Fire(r, r.x, r.y, z, r.heading, weapon);
        }

        // _EXPLODE_TANK: five pieces thrown out of a destroyed robot
        public void Explode(Robot r)
        {
            for (int i = 0; i < 0x500; i += 0x100)
                Fire(r, r.x, r.y, r.z, r.heading + i / 5 + (10 - Random.Range(0, 20)), Debris);
        }

        // _DO_SHOOT
        void Fire(Robot owner, long x, long y, long z, int heading, int weapon)
        {
            if (shots.Count >= MaxShots) return;
            var s = new Shot { x = x, y = y, z = z, heading = heading & 0xFF, owner = owner, weapon = weapon, life = 0x12, speed = 0x2000 };
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
            s.visual = MakeVisual(shape, owner);
            s.curPos = s.prevPos = s.Position;
            Place(s, s.curPos);
            shots.Add(s);
            if (sound != null && owner != null) Sfx.PlayAt(sound, owner.transform.position);
        }

        Transform MakeVisual(int shape, Robot owner)
        {
            var gm = GameManager.Instance;
            var look = gm.robotLook;
            ShapeGeometry geometry = null;
            if (shape != 0)
            {
                var central = MatrixLibrary.Central();
                if (central != null) central.Shapes.TryGetValue(shape, out geometry);
            }
            if (geometry == null)
            {
                var bolt = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                bolt.name = "RobotBolt";
                Object.Destroy(bolt.GetComponent<Collider>());
                bolt.transform.localScale = new Vector3(0.25f, 0.6f, 0.25f);
                bolt.GetComponent<MeshRenderer>().sharedMaterial = look.bolt;
                return bolt.transform;
            }
            var root = new GameObject("RobotShot").transform;
            var go = new GameObject("Edges");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = geometry.Edges(gm.obstacleLook.edgeThickness);
            go.AddComponent<MeshRenderer>().sharedMaterial = look.bolt;
            return root;
        }

        static void Place(Shot s, Vector3 pos)
        {
            if (s.visual == null) return;
            var dir = new Vector3(s.vx, s.vz, s.vy);
            var rot = dir.sqrMagnitude > 1f ? Quaternion.LookRotation(dir) : Quaternion.Euler(0f, OriginalMath.Yaw(s.heading), 0f);
            if (s.visual.name == "RobotBolt") rot *= Quaternion.Euler(90f, 0f, 0f);   // capsules stand on Y
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
                        s.vz -= 0x600;
                        if (s.z + s.vz < 0) { s.z = 0; s.vz = 0; s.vx = s.vy = 0; s.weapon = 0; s.spin = 8; }
                        break;
                    case Debris:
                        s.vz -= (s.vz >> 6) + 0x80;
                        if (s.z < 0 && s.vz < 0) s.vz = -s.vz;
                        break;
                }
                if (s.life <= 0) continue;
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

        // Homing (weapons 9 and 10): turn 2 / 8 a tick toward the player, keep to its height, and climb
        // over anything in the way
        void Home(Shot s)
        {
            Vector3 target = director.PlayerPos;
            long tx = OriginalMath.ToUnits(target.x) - s.x, ty = OriginalMath.ToUnits(target.z) - s.y;
            OriginalMath.FixHeading(ref s.heading, OriginalMath.Angle(tx, ty), s.weapon == 10 ? 8 : 2);
            s.vx = (long)OriginalMath.Cos[s.heading] * s.speed >> 8;
            s.vy = (long)OriginalMath.Sin[s.heading] * s.speed >> 8;
            long targetZ = director.PlayerZ;
            if (s.z < targetZ) { s.z = targetZ; s.vz = 0; }
            bool blocked = Physics.CheckBox(s.Position + OriginalMath.Direction(s.heading) * (0x3C00 * OriginalMath.UnitsToMetres),
                new Vector3(0x1000, 0x800, 0x3C00) * OriginalMath.UnitsToMetres, Quaternion.LookRotation(OriginalMath.Direction(s.heading)),
                ~0, QueryTriggerInteraction.Ignore);
            if (!blocked)
            {
                if (targetZ < s.z) { if (s.vz > 0) s.vz = 0; s.vz -= 0x200; }
                else { s.z = targetZ; s.vz = 0; }
            }
            else if (s.z < 0x3200) s.vz += 0x200;
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
            if (target.GetComponent<OrbitShield>() is OrbitShield shield) { shield.Absorb(); return true; }

            var health = target.GetComponentInParent<Health>();
            var robot = health.GetComponent<Robot>();
            if (robot != null && robot.dying != 0) return false;
            if (s.weapon == 1 || s.weapon == 17) return true;   // splash instead
            if (s.weapon == 11 && robot != null) robot.SpinFromHit();
            if (s.weapon == 16 && health.team == Team.Player) GameManager.Instance.player.Motor.DrainFloating(0x10E);
            health.TakeDamage(Damage(s.weapon));
            return true;
        }

        Collider Overlap(Vector3 pos, Shot s)
        {
            var half = new Vector3(0x200, 0x200, 0x300) * OriginalMath.UnitsToMetres;
            int n = Physics.OverlapBoxNonAlloc(pos, half, hits, Quaternion.Euler(0f, OriginalMath.Yaw(s.heading), 0f), ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i];
                if (c.GetComponent<OrbitShield>() != null) return c;
                if (c.isTrigger) continue;
                var h = c.GetComponentInParent<Health>();
                if (h == null || h.IsDead) continue;
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
                return true;
            }
            return false;
        }

        // Weapons 1 and 17: robots within 0x0E / 0x40 / 0x80 lose 5 / 2 / 2, the player 10 per band
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
            var p = director.PlayerPos;
            int pq = OriginalMath.QDist(OriginalMath.ToUnits(p.x) - s.x, OriginalMath.ToUnits(p.z) - s.y);
            int bands = (pq < 0x40 ? 1 : 0) + (pq < 0x80 ? 1 : 0) + (pq < 0xC4 ? 1 : 0);
            if (bands > 0 && director.PlayerAlive) GameManager.Instance.player.Health.TakeRawDamage(bands * 10, true);
        }
    }
}
