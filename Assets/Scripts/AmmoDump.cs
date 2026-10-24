using UnityEngine;

namespace SpectreEX
{
    // "Those undulating squares on the ground are called ammo dumps. They will repair your
    // vehicle and resupply ammo." Tiles ripple; driving over refills shields and cannon.
    public class AmmoDump : MonoBehaviour
    {
        public Transform[] tiles;
        public float rippleHeight = 0.35f;
        public float rippleSpeed = 3f;

        float nextUse;

        void Update()
        {
            if (tiles == null) return;
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                Vector3 p = t.localPosition;
                float phase = (p.x + p.z) * 0.9f;
                p.y = 0.05f + (Mathf.Sin(Time.time * rippleSpeed + phase) * 0.5f + 0.5f) * rippleHeight;
                t.localPosition = p;
            }
        }

        void OnTriggerStay(Collider other)
        {
            if (Time.time < nextUse) return;
            var player = other.GetComponentInParent<PlayerTank>();
            if (player == null || player.Health.IsDead) return;

            bool ammo = player.Weapon.NeedsAmmo;
            bool shields = player.Health.Restore(player.Health.maxShields);
            if (ammo) player.Weapon.Refill();
            if (ammo) Sfx.Play(Snd.AmmoPickup);
            if (shields) Sfx.Play(Snd.ShieldPickup);
            if (ammo || shields) nextUse = Time.time + 1f;
        }
    }
}
