using UnityEngine;

namespace SpectreEX
{
    // Rover: a robot tank. Turns toward the player, closes in, fires when lined up with clear sight.
    // Wanders when the player is down and steers around anything blocking the way.
    [RequireComponent(typeof(TankMotor), typeof(TankWeapon))]
    public class RoverAI : EnemyBase
    {
        public float fireRange = 80f;
        public float attackRange = 1000f;   // starts hunting the player inside this distance (matrix enemy data)
        public float aimTolerance = 5f;
        public float preferredDistance = 18f;

        TankMotor motor;
        TankWeapon weapon;
        Vector3 wanderPoint;
        float avoidUntil;
        float avoidDir = 1f;
        float baseCooldown;

        protected override void Awake()
        {
            base.Awake();
            matrixType = 7;
            motor = GetComponent<TankMotor>();
            weapon = GetComponent<TankWeapon>();
            baseCooldown = weapon.cooldown;
            PickWanderPoint();
        }

        void PickWanderPoint()
        {
            float h = GameManager.Instance != null ? GameManager.Instance.arenaHalfSize * 0.8f : 90f;
            wanderPoint = new Vector3(Random.Range(-h, h), 0f, Random.Range(-h, h));
        }

        void Update()
        {
            if (!Active) { motor.SetInput(0f, 0f); return; }

            motor.speedMultiplier = Mathf.Lerp(0.7f, 1.1f, Mathf.InverseLerp(1f, 3f, difficulty));
            weapon.cooldown = Cooldown(baseCooldown);

            bool hunting = PlayerAlive && Vector3.Distance(Player.transform.position, transform.position) < attackRange;
            Vector3 goal = hunting ? Player.transform.position : wanderPoint;
            Vector3 to = Flat(goal - transform.position);
            float dist = to.magnitude;
            float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);

            if (!hunting && dist < 8f) PickWanderPoint();

            float turn = Mathf.Clamp(angle / 25f, -1f, 1f);
            float throttle;
            if (Mathf.Abs(angle) > 70f) throttle = 0.15f;
            else if (hunting && dist < preferredDistance) throttle = -0.3f;
            else throttle = 1f;

            Vector3 eye = transform.position + Vector3.up * 0.8f;
            if (Time.time < avoidUntil)
            {
                turn = avoidDir;
                throttle = 0.35f;
            }
            else if (Physics.Raycast(eye, transform.forward, out var block, 9f, ~0, QueryTriggerInteraction.Ignore)
                     && block.collider.GetComponentInParent<Health>() == null)
            {
                avoidDir = Vector3.SignedAngle(transform.forward, block.normal, Vector3.up) >= 0f ? 1f : -1f;
                avoidUntil = Time.time + 0.6f;
                turn = avoidDir;
                throttle = 0.35f;
            }

            motor.SetInput(throttle, turn);

            if (hunting && Mathf.Abs(angle) < aimTolerance && dist < fireRange && HasLineOfSight(weapon.muzzle.position, Player.transform))
                weapon.TryFire();
        }
    }
}
