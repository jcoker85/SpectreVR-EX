using UnityEngine;

namespace SpectreEX
{
    public class TankHealth : MonoBehaviour
    {
        public float maxShields = 100f;
        public float shieldRegenPerSecond = 4f;
        public bool isPlayer;
        public Material derezMaterial;

        public float Shields { get; private set; }
        public bool IsDead { get; private set; }

        Rigidbody rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            Shields = maxShields;
        }

        void Update()
        {
            if (!IsDead) Shields = Mathf.Min(maxShields, Shields + shieldRegenPerSecond * Time.deltaTime);
        }

        public void TakeDamage(float amount)
        {
            if (IsDead) return;
            Shields -= amount;
            if (Shields <= 0f) Die();
        }

        void Die()
        {
            IsDead = true;
            Shields = 0f;
            Derez.Burst(transform.position + Vector3.up, derezMaterial, 60, 12f);
            SetAlive(false);
            if (GameManager.Instance != null) GameManager.Instance.OnTankDestroyed(this);
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
                if (!alive) rb.linearVelocity = Vector3.zero;
                rb.isKinematic = !alive;
            }
        }
    }
}
