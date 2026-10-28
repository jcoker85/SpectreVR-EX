using UnityEngine;

namespace SpectreEX
{
    // Orbiter: a hovering spinning top that circles you at a distance, taking potshots.
    [RequireComponent(typeof(Rigidbody), typeof(TankWeapon))]
    public class OrbiterAI : EnemyBase
    {
        public float orbitRadius = 28f;
        public float orbitSpeed = 25f;     // degrees per second around the player
        public float moveSpeed = 14f;
        public float hoverHeight = 3f;
        public float spinSpeed = 300f;
        public Transform body;

        Rigidbody rb;
        TankWeapon weapon;
        float angle;
        float baseCooldown;

        protected override void Awake()
        {
            base.Awake();
            matrixType = 35;
            collideDamage = 6;
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            weapon = GetComponent<TankWeapon>();
            baseCooldown = weapon.cooldown;
            angle = Random.Range(0f, 360f);
        }

        void Update()
        {
            if (body != null && !health.IsDead) body.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        }

        void FixedUpdate()
        {
            if (!Active || !PlayerAlive) return;
            weapon.cooldown = Cooldown(baseCooldown);

            angle += orbitSpeed * difficulty * Time.fixedDeltaTime;
            Vector3 centre = Player.transform.position;
            Vector3 goal = centre + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * orbitRadius;
            float h = GameManager.Instance.arenaHalfSize - 6f;
            goal.x = Mathf.Clamp(goal.x, -h, h);
            goal.z = Mathf.Clamp(goal.z, -h, h);
            goal.y = hoverHeight + Mathf.Sin(Time.time * 1.7f) * 0.6f;

            Vector3 next = Vector3.MoveTowards(rb.position, goal, moveSpeed * Time.fixedDeltaTime);
            rb.MovePosition(next);

            Vector3 aim = Player.transform.position + Vector3.up * 0.8f - weapon.muzzle.position;
            weapon.muzzle.rotation = Quaternion.LookRotation(aim);
            if (HasLineOfSight(weapon.muzzle.position, Player.transform)) weapon.TryFire();
        }
    }
}
