using UnityEngine;

namespace SpectreEX
{
    // Shared plumbing for robots: health, difficulty, the player target and the "is the game running" gate.
    [RequireComponent(typeof(Health))]
    public abstract class EnemyBase : MonoBehaviour
    {
        public float difficulty = 1f;
        public float fireInterval = -1f;   // seconds between shots from the matrix's enemy data (-1: prefab default)
        // Contact rules (PlayerMotor, after the original's _DO_COLLIDE_DAMAGE): the player takes
        // collideDamage raw points every tick it touches the robot, and is blocked or drives through
        // depending on the robot's matrix type. These defaults stand in when no matrix supplies them.
        public int matrixType = 7;          // VRchitect enemy type (7 Robot Tank, 20 Turret, 21 Slicer, 35 Orbiter...)
        public int collideDamage;           // raw damage points per tick of contact (matrix enemy data)
        public bool hasMatrixData = true;   // false: plain obstacle as far as contact goes

        protected Health health;
        public Health Health => health;

        protected virtual void Awake() => health = GetComponent<Health>();

        // Shot spacing: the matrix's firing interval if given, else the weapon's own, faster on harder levels
        protected float Cooldown(float baseCooldown) => (fireInterval > 0f ? fireInterval : baseCooldown) / Mathf.Max(0.1f, difficulty);

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
