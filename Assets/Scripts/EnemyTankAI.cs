using UnityEngine;

namespace SpectreEX
{
    // Hunts the player: turn toward them, close distance, fire when lined up with clear sight.
    // Wanders when the player is dead, and steers around anything blocking the way.
    [RequireComponent(typeof(TankMotor), typeof(TankWeapon), typeof(TankHealth))]
    public class EnemyTankAI : MonoBehaviour
    {
        public float difficulty = 1f;
        public float fireRange = 70f;
        public float aimTolerance = 5f;
        public float preferredDistance = 18f;
        public float arenaHalfSize = 110f;

        TankMotor motor;
        TankWeapon weapon;
        TankHealth health;
        Vector3 wanderPoint;
        float avoidUntil;
        float avoidDir = 1f;
        float baseCooldown;

        void Awake()
        {
            motor = GetComponent<TankMotor>();
            weapon = GetComponent<TankWeapon>();
            health = GetComponent<TankHealth>();
            baseCooldown = weapon.cooldown;
            PickWanderPoint();
        }

        void PickWanderPoint()
        {
            wanderPoint = new Vector3(Random.Range(-arenaHalfSize, arenaHalfSize) * 0.8f, 0f, Random.Range(-arenaHalfSize, arenaHalfSize) * 0.8f);
        }

        void Update()
        {
            if (health.IsDead) { motor.SetInput(0f, 0f); return; }

            motor.speedMultiplier = Mathf.Lerp(0.75f, 1.1f, Mathf.InverseLerp(1f, 3f, difficulty));
            weapon.cooldown = baseCooldown / difficulty;

            var player = GameManager.Instance != null ? GameManager.Instance.player : null;
            bool hunting = player != null && !player.IsDead;
            Vector3 goal = hunting ? player.transform.position : wanderPoint;

            Vector3 to = goal - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);

            if (!hunting && dist < 8f) PickWanderPoint();

            float turn = Mathf.Clamp(angle / 25f, -1f, 1f);
            float throttle;
            if (Mathf.Abs(angle) > 70f) throttle = 0.15f;
            else if (hunting && dist < preferredDistance) throttle = -0.3f;
            else throttle = 1f;

            // Obstacle avoidance: commit to a turn for a moment once something non-tank blocks the way
            Vector3 eye = transform.position + Vector3.up * 0.8f;
            if (Time.time < avoidUntil)
            {
                turn = avoidDir;
                throttle = 0.35f;
            }
            else if (Physics.Raycast(eye, transform.forward, out var block, 9f, ~0, QueryTriggerInteraction.Ignore)
                     && block.collider.GetComponentInParent<TankHealth>() == null)
            {
                avoidDir = Vector3.SignedAngle(transform.forward, block.normal, Vector3.up) >= 0f ? 1f : -1f;
                avoidUntil = Time.time + 0.6f;
                turn = avoidDir;
                throttle = 0.35f;
            }

            motor.SetInput(throttle, turn);

            if (hunting && Mathf.Abs(angle) < aimTolerance && dist < fireRange && HasLineOfSight(player.transform))
                weapon.TryFire();
        }

        bool HasLineOfSight(Transform target)
        {
            Vector3 from = weapon.muzzle.position;
            Vector3 to = target.position + Vector3.up * 0.8f - from;
            if (!Physics.Raycast(from, to.normalized, out var hit, to.magnitude + 1f, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.transform.IsChildOf(target);
        }
    }
}
