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

        public void TakeDamage(int amount)
        {
            if (IsDead) return;
            Shields -= amount;
            if (Shields <= 0) { Die(); return; }
            if (team == Team.Player) Sfx.Play(hitSound);
            else Sfx.PlayAt(hitSound, transform.position);
        }

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
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = alive;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = alive;
            if (rb != null)
            {
                if (!alive && !rb.isKinematic) rb.linearVelocity = Vector3.zero;
                if (team == Team.Player) rb.isKinematic = !alive;
            }
        }
    }
}
