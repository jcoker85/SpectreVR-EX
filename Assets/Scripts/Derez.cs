using UnityEngine;

namespace SpectreEX
{
    // Glowing voxel shards that scatter and shrink away: tank deaths and bolt impacts.
    public class Derez : MonoBehaviour
    {
        float life, age;
        Vector3 startScale;

        public static void Burst(Vector3 position, Material material, int count, float force)
        {
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Shard";
                Destroy(go.GetComponent<Collider>());
                go.transform.position = position + Random.insideUnitSphere * 0.5f;
                go.transform.rotation = Random.rotation;
                go.transform.localScale = Vector3.one * Random.Range(0.1f, 0.35f);
                if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;

                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.linearVelocity = (Random.insideUnitSphere + Vector3.up * 0.8f) * force;
                rb.angularVelocity = Random.insideUnitSphere * 10f;

                var d = go.AddComponent<Derez>();
                d.life = Random.Range(0.6f, 1.6f);
                d.startScale = go.transform.localScale;
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = age / life;
            if (t >= 1f) { Destroy(gameObject); return; }
            transform.localScale = startScale * (1f - t);
        }
    }
}
