using UnityEngine;

namespace SpectreEX
{
    public class TankWeapon : MonoBehaviour
    {
        public Transform muzzle;
        public Material projectileMaterial;
        public float cooldown = 0.35f;
        public float projectileSpeed = 70f;
        public float damage = 25f;

        float nextFireTime;

        public bool TryFire()
        {
            if (Time.time < nextFireTime || muzzle == null) return false;
            nextFireTime = Time.time + cooldown;
            Projectile.Spawn(muzzle.position, muzzle.forward, projectileSpeed, damage, projectileMaterial, gameObject);
            return true;
        }
    }
}
