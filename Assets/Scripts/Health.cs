using UnityEngine;

namespace SpectreEX
{
    public enum Team { Player, Enemy }

    // Spectre-style shields: each hit removes whole shields; zero means destroyed.
    public class Health : MonoBehaviour
    {
        public Team team = Team.Enemy;
        public int maxShields = 1;
        public int scoreValue = 250;
        public Material derezMaterial;
        public string hitSound = Snd.EnemyHit;
        public string deathSound = Snd.Explosion;
        public bool hiddenFromRadar;   // Cloaked Tanks
        public int hitMultiplier = 1;  // player: 10 (weapon damage counts x 10 against its points)

        public int Shields { get; private set; }
        public bool IsDead { get; private set; }
        public float Damage01 => maxShields > 0 ? 1f - (float)Shields / maxShields : 1f;

        Rigidbody rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            Shields = maxShields;
        }

        public void SetMaxShields(int value)
        {
            maxShields = Mathf.Max(1, value);
            Shields = maxShields;
        }

        public bool Restore(int amount)
        {
            if (IsDead || Shields >= maxShields) return false;
            Shields = Mathf.Min(maxShields, Shields + amount);
            return true;
        }

        // Weapon hits. The player counts damage in the original's points: a hit costs weapon damage x 10
        // against a capacity of shield points x 10 (Balance: 60, so six cannon rounds).
        public void TakeDamage(int amount) => TakeRawDamage(amount * hitMultiplier, true);

        // Damage in raw points - collisions and acid, which the original doesn't scale
        public void TakeRawDamage(int points, bool sound)
        {
            if (IsDead || points <= 0) return;
            Shields -= points;
            if (Shields <= 0) { Die(); return; }
            if (!sound) return;
            if (team == Team.Player) Sfx.Play(Shields <= DangerPoints && hitMultiplier > 1 ? Snd.Danger : hitSound);
            else Sfx.PlayAt(hitSound, transform.position);
        }

        // The original swaps the hit sound for the warning once you're within 20 points of destruction
        const int DangerPoints = 20;

        void Die()
        {
            IsDead = true;
            Shields = 0;
            Derez.Burst(transform.position + Vector3.up, derezMaterial, team == Team.Player ? 80 : 50, 12f);
            if (team == Team.Player) Sfx.Play(deathSound); else Sfx.PlayAt(deathSound, transform.position);
            SetAlive(false);
            if (GameManager.Instance != null) GameManager.Instance.OnDestroyed(this);
        }

        public void Revive(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (rb != null) { rb.position = position; rb.rotation = rotation; }
            Shields = maxShields;
            IsDead = false;
            SetAlive(true);
        }

        void SetAlive(bool alive)
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.enabled = alive;
                if (alive) GameManager.ApplyFilled(r);   // honour the Options "Filled" setting
            }
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = alive;
            if (rb != null)
            {
                if (!alive && !rb.isKinematic) rb.linearVelocity = Vector3.zero;
            }
        }
    }
}
