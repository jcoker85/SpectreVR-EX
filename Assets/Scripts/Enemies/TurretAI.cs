using UnityEngine;

namespace SpectreEX
{
    // Gun turret: stationary and heavily shielded; swivels to track you and fires when it has a shot.
    [RequireComponent(typeof(TankWeapon))]
    public class TurretAI : EnemyBase
    {
        public Transform head;
        public float turnSpeed = 60f;
        public float range = 90f;

        TankWeapon weapon;
        float baseCooldown;

        protected override void Awake()
        {
            base.Awake();
            matrixType = 20;
            weapon = GetComponent<TankWeapon>();
            baseCooldown = weapon.cooldown;
        }

        void Update()
        {
            if (!Active || !PlayerAlive || head == null) return;
            weapon.cooldown = Cooldown(baseCooldown);

            Vector3 to = Flat(Player.transform.position - head.position);
            if (to.sqrMagnitude < 0.01f) return;
            Quaternion want = Quaternion.LookRotation(to);
            head.rotation = Quaternion.RotateTowards(head.rotation, want, turnSpeed * difficulty * Time.deltaTime);

            if (to.magnitude < range && Quaternion.Angle(head.rotation, want) < 4f && HasLineOfSight(weapon.muzzle.position, Player.transform))
                weapon.TryFire();
        }
    }
}
