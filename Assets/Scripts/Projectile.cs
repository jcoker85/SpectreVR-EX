using UnityEngine;

namespace SpectreEX
{
    // Kinematic bolt that sweeps a raycast each frame so it can't tunnel through thin walls.
    public class Projectile : MonoBehaviour
    {
        const float Lifetime = 3f;

        Vector3 velocity;
        float damage;
        GameObject owner;
        float dieAt;

        public static void Spawn(Vector3 position, Vector3 direction, float speed, float damage, Material material, GameObject owner)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Bolt";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(0.25f, 0.6f, 0.25f);
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;

            var p = go.AddComponent<Projectile>();
            p.velocity = direction.normalized * speed;
            p.damage = damage;
            p.owner = owner;
            p.dieAt = Time.time + Lifetime;
        }

        void Update()
        {
            if (Time.time >= dieAt) { Destroy(gameObject); return; }

            Vector3 step = velocity * Time.deltaTime;
            var hits = Physics.RaycastAll(transform.position, step.normalized, step.magnitude, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (owner != null && hit.collider.transform.IsChildOf(owner.transform)) continue;

                var health = hit.collider.GetComponentInParent<TankHealth>();
                if (health != null) health.TakeDamage(damage);
                Derez.Burst(hit.point, GetComponent<MeshRenderer>().sharedMaterial, 6, 4f);
                Destroy(gameObject);
                return;
            }
            transform.position += step;
        }
    }
}
