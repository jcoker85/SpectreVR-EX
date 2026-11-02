using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // The built-in flag, as tall as the craft and all in the level's object colour, turning two
    // steps a tick (_BUILD_WORLD).
    public class Flag : MonoBehaviour
    {
        const float PoleThickness = 0.06f;

        public Transform visual;

        void Start()
        {
            var gm = GameManager.Instance;
            var shape = BuiltinShapes.Named("FLAG");
            if (visual == null || gm == null || shape == null) return;
            var look = gm.obstacleLook.shapes;
            int colour = SpectrePalette.Entry(0x50);
            AddMesh("Panels", shape.Coloured(0x50), look, -1);
            AddMesh("Pole", shape.Edges(PoleThickness), look, colour);   // its pole is a two-point face, a line
        }

        void AddMesh(string name, Mesh mesh, Material material, int paint)
        {
            var go = new GameObject(name);
            go.transform.SetParent(visual, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            if (paint >= 0) SpectrePalette.Paint(r, paint, material);
        }

        void Update()
        {
            if (visual != null) visual.Rotate(0f, 2f * OriginalMath.StepsToDegrees * OriginalMath.TickRate * Time.deltaTime, 0f, Space.World);
        }

        void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<PlayerTank>();
            if (player == null) return;
            if (GameManager.Instance != null) GameManager.Instance.OnFlagCollected(this);
            Destroy(gameObject);
        }
    }
}
