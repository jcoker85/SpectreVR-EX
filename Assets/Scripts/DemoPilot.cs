using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // Demo mode (_MOVE_ATTRACT_TANK): runs for a dump when hurt or short of ammo, else fights any robot
    // close by, else takes the nearest flag, unless a dump is nearer and it needs one. Each original tick
    // _PLAYER_CHASE_THING steers for that goal: _FIND_CLEAR_PATH picks a heading, and it turns toward it
    // at 3 steps a tick, going slower or stopping when it has been stuck on the goal for a while.
    [RequireComponent(typeof(PlayerTank))]
    public class DemoPilot : MonoBehaviour
    {
        const int CloseRobot = 500, BackOff = 0x50, Range = 0x244, MaxGoalTicks = 0x10E, OnGround = 0x500;
        const int DumpLimit = 2000, FarLimit = 20000, TankShotLift = 0x400;
        const int FullSpeed = 10, SlowSpeed = 5, TurnLimit = 3, SlowUntil = 0x5B, StallAfter = 0x7E;
        const int PathLength = 0x6400, PathWidth = 0xA00, PathHeight = 0x1800, FlatTop = 0xFF;   // the player's probe

        PlayerTank tank;
        Transform lastGoal;
        int goalTicks, shotTimer, pathHeading, pathTimer, pathBlock = -1;
        readonly Collider[] probe = new Collider[32];

        void Awake() => tank = GetComponent<PlayerTank>();

        void OnDisable()
        {
            if (tank != null && tank.Motor != null) tank.Motor.ReleaseSteering();
        }

        // One original tick, from GameManager
        public void SimTick()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.Playing || tank.Health.IsDead) { tank.Motor.Steer(0, 0); return; }

            var robot = ChooseGoal(gm, out Transform goal);
            if (goal == null) { tank.Motor.Steer(0, 0); return; }

            if (goal != lastGoal) { lastGoal = goal; goalTicks = 0; }
            if (goalTicks++ > MaxGoalTicks)
            {
                goalTicks = 0;
                MoveElsewhere(gm);
            }

            int speed = FullSpeed, distance = Dist(goal);
            bool reverse = robot != null && distance < BackOff;
            if (reverse) speed = -FullSpeed;

            Vector3 to = goal.position - transform.position;
            int want = OriginalMath.Angle(OriginalMath.ToUnits(to.x), OriginalMath.ToUnits(to.z));
            int path = FindClearPath(gm, want, reverse, distance);

            int heading = tank.Motor.OriginalHeading;
            int turned = OriginalMath.FixHeading(ref heading, pathHeading, TurnLimit);
            if (path == -1)
            {
                if (Mathf.Abs(turned) == TurnLimit && goalTicks > StallAfter) speed = 0;
                reverse = false;
            }
            else speed = goalTicks < SlowUntil ? SlowSpeed : 0;
            tank.Motor.Steer(turned, speed);

            if (robot == null || tank.Weapon.Ammo < 1) return;
            shotTimer--;
            if (pathHeading == heading && shotTimer < 0 && !reverse && distance < Range) Shoot(gm, robot, distance);
        }

        void MoveElsewhere(GameManager gm)
        {
            tank.Motor.MoveTo(gm.PlaceXY());
        }

        // ---------- _FIND_CLEAR_PATH / _CHECK_TANK_PATH, for the player ----------

        int FindClearPath(GameManager gm, int want, bool reverse, int chaseDist)
        {
            int current = tank.Motor.OriginalHeading;
            if (current != pathHeading && --pathTimer > 0) return -2;
            if (--pathTimer > 0) return pathBlock;

            if (reverse)
            {
                want = (want + 0x80) & 0xFF;
                current = want;
            }
            int flip = reverse ? 0x80 : 0;
            if (PathClear(want, chaseDist)) return Chose(want + flip);
            if (PathClear(current, chaseDist)) return Chose(current + flip);
            for (int off = 0; ;)
            {
                off = off >= 0 ? -(off + 4) : -off;
                if (System.Math.Abs(off) > 0x81) break;
                if (PathClear((want + off) & 0xFF, chaseDist)) return Chose(want + off + flip);
            }
            // Boxed in: the original simply moves the craft somewhere else
            MoveElsewhere(gm);
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

        // A box ahead against the obstacles no further off than the goal. On the ground fans, acid, up
        // transports and pushers don't count; in the air everything does but the flat ones. Robots never do.
        bool PathClear(int h, int chaseDist)
        {
            Vector3 dir = OriginalMath.Direction(h);
            Vector3 centre = transform.position + dir * (PathLength * 1.125f * OriginalMath.UnitsToMetres);
            centre.y = 2f;
            var half = new Vector3(PathWidth, PathHeight, PathLength) * OriginalMath.UnitsToMetres;
            bool ground = tank.Motor.OnGround;
            int n = Physics.OverlapBoxNonAlloc(centre, half, probe, Quaternion.LookRotation(dir), ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var obstacle = probe[i].GetComponentInParent<ObstacleBehaviour>();
                if (obstacle == null) continue;
                if (ground ? obstacle.type == 16 || obstacle.type == 36 || obstacle.type == 39
                           : OriginalMath.ToUnits(probe[i].bounds.max.y) <= FlatTop) continue;
                if (Dist(obstacle.transform) > chaseDist) continue;
                return false;
            }
            return true;
        }

        // The cannon for Robot Tanks from the ground (not under it) or when short of ammo, otherwise a
        // missile, both through _TANK_SHOOT
        void Shoot(GameManager gm, Robot robot, int distance)
        {
            var shots = gm.GetComponent<RobotDirector>().Shots;
            bool cannon = (!tank.Motor.Under && tank.Motor.ZUnits <= OnGround && robot.matrixType == 7) || tank.Weapon.Ammo < 10;
            if (cannon)
            {
                shots.PlayerShoot(0, TankShotLift);
                tank.Weapon.AddAmmo(-1);
                Sfx.Play(Snd.EnemyShot);   // _TANK_SHOOT fires it as _DO_SHOOT does a robot's
            }
            else
            {
                shots.PlayerShoot(10, TankShotLift);
                tank.Weapon.AddAmmo(-6);
            }
            shotTimer = Mathf.Max(6, (distance >> 4) - ((gm.Level - 1) >> 1));
        }

        Robot ChooseGoal(GameManager gm, out Transform goal)
        {
            var weapon = tank.Weapon;
            var health = tank.Health;
            Transform dump = Nearest(gm.Dumps, int.MaxValue, out _);
            goal = dump;
            if (health.Shields < 0x14 || weapon.Ammo < 10) return null;

            Robot closest = null;
            int best = CloseRobot;
            foreach (var h in gm.Robots)
            {
                if (h == null || h.IsDead) continue;
                var r = h.GetComponent<Robot>();
                if (r == null || !r.visible) continue;
                int d = Dist(h.transform);
                if (d < best) { best = d; closest = r; }
            }
            if (closest != null) { goal = closest.transform; return closest; }

            if ((health.Shields < health.maxShields || weapon.NeedsAmmo) && !FlagCloserThanDump(gm)) return null;
            Transform target = tank.Motor.Under ? NearestWayUp(gm) : Nearest(gm.Flags, FarLimit, out _);
            if (target != null) goal = target;
            return null;
        }

        // _FLAG_CLOSER_THAN_FUEL: only dumps inside 2000 count against a flag
        bool FlagCloserThanDump(GameManager gm)
        {
            Nearest(gm.Dumps, DumpLimit, out int dumpDist);
            return Nearest(gm.Flags, dumpDist, out _) != null;
        }

        // _CHASE_UP: from under the floor, the nearest up transport
        Transform NearestWayUp(GameManager gm)
        {
            Transform best = null;
            int distance = FarLimit;
            foreach (var o in gm.Obstacles)
            {
                var gate = o != null ? o.GetComponent<ObstacleBehaviour>() : null;
                if (gate == null || gate.kind != ObstacleBehaviour.Kind.UpTransport) continue;
                int d = Dist(o.transform);
                if (d < distance) { distance = d; best = o.transform; }
            }
            return best;
        }

        int Dist(Transform t)
        {
            Vector3 d = t.position - transform.position;
            return OriginalMath.QDist(OriginalMath.ToUnits(d.x), OriginalMath.ToUnits(d.z));
        }

        Transform Nearest<T>(System.Collections.Generic.List<T> items, int limit, out int distance) where T : Component
        {
            Transform best = null;
            distance = limit;
            foreach (var i in items)
            {
                if (i == null) continue;
                int d = Dist(i.transform);
                if (d < distance) { distance = d; best = i.transform; }
            }
            return best;
        }
    }
}
