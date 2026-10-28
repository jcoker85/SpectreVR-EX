using UnityEngine;

namespace SpectreEX
{
    // Cannon: one round = one shield of damage. The player has limited ammo (refilled at ammo dumps);
    // robots fire without limit.
    public class TankWeapon : MonoBehaviour
    {
        public Transform muzzle;
        public Material projectileMaterial;
        public Team team = Team.Enemy;
        public float cooldown = 0.3f;
        public float projectileSpeed = 80f;
        public int damage = 1;
        public bool infiniteAmmo = true;
        public int maxAmmo = 30;
        public int lowAmmoWarning = 5;
        public string fireSound = Snd.EnemyShot;

        public int Ammo { get; private set; }

        float nextFireTime;

        void Awake() => Ammo = maxAmmo;

        public void Refill() => Ammo = maxAmmo;
        public void AddAmmo(int rounds) => Ammo = Mathf.Clamp(Ammo + rounds, 0, maxAmmo);
        public bool NeedsAmmo => !infiniteAmmo && Ammo < maxAmmo;

        public bool TryFire()
        {
            if (Time.time < nextFireTime || muzzle == null) return false;
            nextFireTime = Time.time + cooldown;

            if (!infiniteAmmo)
            {
                if (Ammo <= 0) { Sfx.Play(Snd.OutOfAmmo); return false; }
                Ammo--;
                if (Ammo == lowAmmoWarning) Sfx.Play(Snd.LowAmmo);
            }

            Projectile.Spawn(muzzle.position, muzzle.forward, projectileSpeed, damage, team, projectileMaterial, gameObject);
            if (team == Team.Player) Sfx.Play(fireSound, 0.8f);
            else Sfx.PlayAt(fireSound, muzzle.position);
            return true;
        }
    }
}
