using UnityEngine;

namespace SpectreEX
{
    // Demo mode (_MOVE_ATTRACT_TANK): runs for a dump when hurt or short of ammo, else fights any robot
    // close by, else takes the nearest flag, unless a dump is nearer and it needs one. It steers
    // around obstacles and fires on what it's chasing.
    [RequireComponent(typeof(PlayerTank))]
    public class DemoPilot : MonoBehaviour
    {
        const int CloseRobot = 500, BackOff = 0x50, Range = 0x244, MaxGoalTicks = 0x10E, OnGround = 0x500;

        PlayerTank tank;
        float avoidUntil;
        float avoidDir = 1f;
        Transform lastGoal;
        float goalSeconds;
        float nextShot;

        void Awake() => tank = GetComponent<PlayerTank>();

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.Playing || tank.Health.IsDead) { tank.Motor.SetInput(0f, 0f); return; }

            var robot = ChooseGoal(gm, out Transform goal);
            if (goal == null) { tank.Motor.SetInput(0f, 0f); return; }

            // _PLAYER_CHASE_THING: stuck on one thing too long, it just moves somewhere else
            if (goal != lastGoal) { lastGoal = goal; goalSeconds = 0f; }
            goalSeconds += Time.deltaTime;
            if (goalSeconds > MaxGoalTicks / PlayerMotor.TickRate)
            {
                goalSeconds = 0f;
                if (gm.TryFindSpawnPoint(out var p, 0f)) tank.Motor.Place(p, transform.eulerAngles.y, false);
            }

            Vector3 to = goal.position - transform.position;
            to.y = 0f;
            float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);
            float turn = Mathf.Clamp(angle / 25f, -1f, 1f);
            float throttle = Mathf.Abs(angle) > 70f ? 0.2f : 1f;
            int distance = Dist(goal);
            if (robot != null && distance < BackOff) throttle = -1f;

            Vector3 eye = transform.position + Vector3.up * 0.8f;
            if (Time.time < avoidUntil) { turn = avoidDir; throttle = 0.4f; }
            else if (Physics.Raycast(eye, transform.forward, out var block, 10f, ~0, QueryTriggerInteraction.Ignore)
                     && block.collider.GetComponentInParent<Health>() == null)
            {
                avoidDir = Vector3.SignedAngle(transform.forward, block.normal, Vector3.up) >= 0f ? 1f : -1f;
                avoidUntil = Time.time + 0.6f;
                turn = avoidDir;
                throttle = 0.4f;
            }
            tank.Motor.SetInput(throttle, turn);

            if (robot != null && Mathf.Abs(angle) < 3f && distance < Range && Time.time >= nextShot) Shoot(gm, robot, distance);
        }

        // The cannon for Robot Tanks from the ground or when short of ammo, otherwise a missile
        void Shoot(GameManager gm, Robot robot, int distance)
        {
            var shots = gm.GetComponent<RobotDirector>().Shots;
            bool cannon = (tank.Motor.ZUnits <= OnGround && robot.matrixType == 7) || tank.Weapon.Ammo < 10;
            if (cannon)
            {
                if (tank.Weapon.Ammo < 1) return;
                shots.PlayerShoot(0);
                tank.Weapon.AddAmmo(-1);
                Sfx.Play(Snd.PlayerShot);
            }
            else
            {
                shots.PlayerShoot(10);
                tank.Weapon.AddAmmo(-6);
                Sfx.Play(Snd.Missile);
            }
            int ticks = Mathf.Max(6, (distance >> 4) - ((gm.Level - 1) >> 1));
            nextShot = Time.time + ticks / PlayerMotor.TickRate;
        }

        Robot ChooseGoal(GameManager gm, out Transform goal)
        {
            var weapon = tank.Weapon;
            var health = tank.Health;
            bool hurt = health.Shields < health.maxShields;
            Transform dump = Nearest(gm.Dumps, out int dumpDist);
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

            Transform flag = Nearest(gm.Flags, out int flagDist);
            if (flag != null && (!(hurt || weapon.NeedsAmmo) || flagDist < dumpDist || dump == null)) goal = flag;
            return null;
        }

        int Dist(Transform t)
        {
            Vector3 d = t.position - transform.position;
            return OriginalMath.QDist(OriginalMath.ToUnits(d.x), OriginalMath.ToUnits(d.z));
        }

        Transform Nearest<T>(System.Collections.Generic.List<T> items, out int distance) where T : Component
        {
            Transform best = null;
            distance = 2000;
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
