using UnityEngine;

namespace SpectreEX
{
    // Demo mode ("press D at the main menu"): drives the player craft on its own - heads for the
    // nearest flag, steers around obstacles, and shoots robots that line up in front of it.
    [RequireComponent(typeof(PlayerTank))]
    public class DemoPilot : MonoBehaviour
    {
        PlayerTank tank;
        float avoidUntil;
        float avoidDir = 1f;

        void Awake() => tank = GetComponent<PlayerTank>();

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.Playing || tank.Health.IsDead) { tank.Motor.SetInput(0f, 0f); return; }

            Transform goal = Nearest(gm.Flags);
            if (goal == null) { tank.Motor.SetInput(0f, 0f); return; }

            Vector3 to = goal.position - transform.position;
            to.y = 0f;
            float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);
            float turn = Mathf.Clamp(angle / 25f, -1f, 1f);
            float throttle = Mathf.Abs(angle) > 70f ? 0.2f : 1f;

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

            // Fire at any robot roughly dead ahead
            foreach (var r in gm.Robots)
            {
                if (r == null || r.IsDead) continue;
                Vector3 d = r.transform.position - transform.position;
                if (d.magnitude < 70f && Vector3.Angle(transform.forward, new Vector3(d.x, 0f, d.z)) < 4f)
                {
                    if (tank.Weapon.Ammo <= 3) tank.Weapon.Refill();   // the demo never runs dry
                    tank.Weapon.TryFire();
                    break;
                }
            }
        }

        Transform Nearest<T>(System.Collections.Generic.List<T> items) where T : Component
        {
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var i in items)
            {
                if (i == null) continue;
                float d = (i.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i.transform; }
            }
            return best;
        }
    }
}
