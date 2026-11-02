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
            Damaged?.Invoke(points);
            // The craft goes only past its limit (_MAX_DAMAGE < _DAMAGE), so exactly at it you're still flying
            if (Shields < (team == Team.Player ? 0 : 1)) { Die(); return; }
            if (team == Team.Player) DeRes.Play(gameObject, RipplePeak, RippleTicks / PlayerMotor.TickRate, true);   // _D_P_DE_RES: any damage at all
            if (!sound) return;
            if (team == Team.Player) Sfx.Play(PlayerHitSound);
            else Sfx.PlayAt(hitSound, transform.position);
        }

        public string PlayerHitSound => Shields <= DangerPoints && hitMultiplier > 1 ? Snd.HeavyDamage : hitSound;

        // Every hit, the killing one included (robots score per hit and are stunned by it)
        public System.Action<int> Damaged;

        // _D_P_DE_RES -1 runs _PLAYER_DE_RES down past -5 and back up
        public const int RipplePeak = 6, RippleTicks = 12;

        // The original swaps the hit sound (3) for the heavy-damage one (9) within 20 points of destruction
        const int DangerPoints = 20;

        void Die()
        {
            IsDead = true;
            Shields = 0;
            // The craft's own sound comes from GameManager (0xD), and the last robot plays ALLBOTS instead (0x2C)
            bool last = GameManager.Instance != null && GameManager.Instance.LastRobot;
            if (team != Team.Player && !last) Sfx.PlayAt(deathSound, transform.position);
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
            // The player's wreck still stops shots until its time is up
            if (team != Team.Player)
                foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = alive;
            if (rb != null)
            {
                if (!alive && !rb.isKinematic) rb.linearVelocity = Vector3.zero;
            }
        }
    }
}
