using UnityEngine;

namespace SpectreEX
{
    // Shared plumbing for robots: health, difficulty, the player target and the "is the game running" gate.
    [RequireComponent(typeof(Health))]
    public abstract class EnemyBase : MonoBehaviour
    {
        public float difficulty = 1f;

        protected Health health;

        protected virtual void Awake() => health = GetComponent<Health>();

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
