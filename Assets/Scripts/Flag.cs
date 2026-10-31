using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    public class Flag : MonoBehaviour
    {
        public float spinSpeed = 60f;
        public float bobHeight = 0.25f;
        public Transform visual;

        Vector3 visualBase;

        void Start()
        {
            if (visual == null) return;
            visualBase = visual.localPosition;
            // The stand in the level's object colour, the pennant red
            var gm = GameManager.Instance;
            if (gm == null) return;
            foreach (var r in visual.GetComponentsInChildren<MeshRenderer>())
                SpectrePalette.Paint(r, SpectrePalette.Entry(r.name == "Pennant" ? 0x10 : 0x50), gm.obstacleLook.shapes);
        }

        void Update()
        {
            if (visual == null) return;
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.time * 2f + transform.position.x) * bobHeight);
        }

        void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<PlayerTank>();
            if (player == null || player.Health.IsDead) return;
            if (GameManager.Instance != null) GameManager.Instance.OnFlagCollected(this);
            Destroy(gameObject);
        }
    }
}
