using UnityEngine;

namespace SpectreEX
{
    // Shared plumbing for robots: health, difficulty, the player target and the "is the game running" gate.
    [RequireComponent(typeof(Health))]
    public abstract class EnemyBase : MonoBehaviour
    {
        public float difficulty = 1f;
        public float fireInterval = -1f;   // seconds between shots from the matrix's enemy data (-1: prefab default)
        public int collideDamage;          // shields lost by the player on contact (matrix enemy data)

        protected Health health;

        protected virtual void Awake() => health = GetComponent<Health>();

        // Shot spacing: the matrix's firing interval if given, else the weapon's own, faster on harder levels
        protected float Cooldown(float baseCooldown) => (fireInterval > 0f ? fireInterval : baseCooldown) / Mathf.Max(0.1f, difficulty);

        // Ramming damage for robots with collision damage in their enemy data (slicers handle their own)
        protected virtual void OnCollisionEnter(Collision c)
        {
            if (collideDamage <= 0 || health.IsDead) return;
            var victim = c.collider.GetComponentInParent<PlayerTank>();
            if (victim != null) victim.Health.TakeDamage(collideDamage);
        }

        protected PlayerTank Player => GameManager.Instance != null ? GameManager.Instance.player : null;
        protected bool Active => !health.IsDead && GameManager.Instance != null && GameManager.Instance.Playing;
        protected bool PlayerAlive => Player != null && !Player.Health.IsDead;

        protected static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        protected bool HasLineOfSight(Vector3 from, Transform target)
        {
            Vector3 to = target.position + Vector3.up * 0.8f - from;
            if (!Physics.Raycast(from, to.normalized, out var hit, to.magnitude + 1f, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.transform.IsChildOf(target) || hit.collider.transform.IsChildOf(transform);
        }
    }
}
