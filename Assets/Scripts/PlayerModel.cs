using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // The craft as the original draws it: its world type 6 shape from the matrix (Central Systems:
    // multi-part 2013). PlayerMotor's yaw already carries the original's model heading (+0x80), so
    // the shape sits unturned. Parts whose shape id ends in 0x5F / 0x60 are the wings (_MOVE_OBJECT):
    // they turn by -/+ _WING_ANGLE from the body - tucked at rest, spread while floating. The hull
    // collider is left as it is.
    public class PlayerModel : MonoBehaviour
    {
        readonly List<(Transform part, int sign)> wings = new List<(Transform, int)>();
        PlayerMotor motor;

        void Awake() => motor = GetComponent<PlayerMotor>();

        public void Rebuild(ShapeSet shapes, float edgeThickness)
        {
            var parts = shapes?.ForType(6);
            var model = transform.Find("Model");
            if (parts == null || parts.Count == 0 || model == null) return;

            // Keep the craft's own look (tuned materials, layer) and swap the geometry
            Material panel = null, glow = null;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name == "Panels" && panel == null) panel = r.sharedMaterial;
                if (r.name == "Edges" && glow == null) glow = r.sharedMaterial;
            }
            int layer = model.gameObject.layer;
            for (int i = model.childCount - 1; i >= 0; i--) Destroy(model.GetChild(i).gameObject);

            wings.Clear();
            var root = new GameObject("Craft").transform;
            root.SetParent(model, false);
            foreach (var p in parts)
            {
                var t = new GameObject("Shape").transform;
                t.SetParent(root, false);
                t.localPosition = p.offset;
                AddMesh("Panels", t, p.shape.Body, panel, layer);
                AddMesh("Edges", t, p.shape.Edges(edgeThickness), glow, layer);
                int tag = p.id & 0xFF;
                if (tag == 0x5F) wings.Add((t, -1));
                else if (tag == 0x60) wings.Add((t, 1));
            }
            root.gameObject.layer = layer;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) GameManager.ApplyFilled(r);
        }

        static void AddMesh(string name, Transform parent, Mesh mesh, Material mat, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            parent.gameObject.layer = layer;
        }

        void LateUpdate()
        {
            if (motor == null || wings.Count == 0) return;
            // The model's frame is mirrored against the original's, so +n heading steps are +n of yaw here
            float deg = motor.WingAngle * OriginalMath.StepsToDegrees;
            foreach (var (part, sign) in wings) part.localRotation = Quaternion.Euler(0f, sign * deg, 0f);
        }
    }
}
