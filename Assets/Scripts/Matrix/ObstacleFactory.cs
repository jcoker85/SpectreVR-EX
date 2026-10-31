using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpectreEX.Matrix
{
    // Materials for obstacles in the Spectre VR-EX style, set up by the arena builder
    [System.Serializable]
    public class ObstacleLook
    {
        public Material shapes;                                                  // solid faces, the level's palette
        public Material edges, acid, ice, teleport, pusher, transport;           // wireframe; pusher also colours slow pads
        public float edgeThickness = 0.07f;
    }

    // Builds a matrix world object from its original shape(s), with the collider and gate rules its
    // type has in SVRWIN.EXE (_HIT_A_GATE, _LOAD_WRLD).
    public static class ObstacleFactory
    {
        const float StepsToDegrees = 360f / 256f;

        public static ObstacleBehaviour.Kind KindOf(int type) => type switch
        {
            16 or 23 => ObstacleBehaviour.Kind.Acid,
            17 => ObstacleBehaviour.Kind.Ice,
            18 or 24 => ObstacleBehaviour.Kind.Slow,
            19 => ObstacleBehaviour.Kind.Teleport,
            39 => ObstacleBehaviour.Kind.Pusher,
            36 => ObstacleBehaviour.Kind.UpTransport,
            37 => ObstacleBehaviour.Kind.DownTransport,
            4 => ObstacleBehaviour.Kind.Windmill,
            _ => ObstacleBehaviour.Kind.Static,
        };

        // Heading change per tick _LOAD_WRLD gives a type (the object turns on the spot)
        static int SpinSteps(int type) => type switch { 2 => -2, 36 => 3, 37 => -3, _ => 0 };

        public static GameObject Build(int type, List<ShapeSet.Part> parts, ObstacleLook look)
        {
            if (parts == null || parts.Count == 0) return null;
            var kind = KindOf(type);
            bool pad = kind == ObstacleBehaviour.Kind.Acid || kind == ObstacleBehaviour.Kind.Ice ||
                       kind == ObstacleBehaviour.Kind.Slow || kind == ObstacleBehaviour.Kind.Pusher;
            var glow = kind switch
            {
                ObstacleBehaviour.Kind.Acid => look.acid,
                ObstacleBehaviour.Kind.Ice => look.ice,
                ObstacleBehaviour.Kind.Slow or ObstacleBehaviour.Kind.Pusher => look.pusher,
                ObstacleBehaviour.Kind.Teleport => look.teleport,
                ObstacleBehaviour.Kind.UpTransport or ObstacleBehaviour.Kind.DownTransport => look.transport,
                _ => look.edges,
            };
            int colour = BuiltinShapes.TypeColour(type);

            var root = new GameObject($"Obstacle_{type}");
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            if (pad) model.localPosition = Vector3.up * 0.04f;   // drawn just above the floor

            Bounds bounds = default;
            bool first = true;
            foreach (var part in parts)
            {
                AddShape(model, part.shape, part.offset, colour, look.shapes, glow, look.edgeThickness);
                var b = part.shape.Body.bounds;
                b.center += part.offset;
                if (first) { bounds = b; first = false; }
                else bounds.Encapsulate(b);
            }

            var beh = root.AddComponent<ObstacleBehaviour>();
            beh.kind = kind;
            beh.type = type;
            int spin = SpinSteps(type);
            if (spin != 0)
            {
                // Heading counts the other way to Unity yaw here (model yaw = -degrees)
                beh.spinner = model;
                beh.spinSpeed = -spin * StepsToDegrees * PlayerMotor.TickRate;
            }

            if (kind == ObstacleBehaviour.Kind.Windmill) AddFans(root.transform, beh, colour, look.shapes, glow, look.edgeThickness);

            var col = root.AddComponent<BoxCollider>();
            if (pad)
            {
                col.isTrigger = true;
                col.center = new Vector3(bounds.center.x, 1.5f, bounds.center.z);
                col.size = new Vector3(Mathf.Max(1.5f, bounds.size.x), 3f, Mathf.Max(1.5f, bounds.size.z));
            }
            else
            {
                col.isTrigger = beh.IsGate;
                col.center = bounds.center;
                // Billboards are flat; the floor-line walls (type 40) have no height at all, so the craft
                // (whose box reaches the floor) is stopped but shots fly over, as in the original
                col.size = beh.IsGate ? Vector3.Max(bounds.size, Vector3.one * 1.5f) : Vector3.Max(bounds.size, new Vector3(0.6f, 0.1f, 0.6f));
                // The up transport's collision reaches 0x1400 below it, so a craft under the floor can touch it
                if (kind == ObstacleBehaviour.Kind.UpTransport)
                {
                    float extra = 0x1400 * PlayerMotor.UnitsToMetres;
                    col.center -= Vector3.up * extra * 0.5f;
                    col.size += Vector3.up * extra;
                }
            }
            return root;
        }

        static Transform AddShape(Transform parent, ShapeGeometry shape, Vector3 offset, int colour, Material panel, Material glow, float thickness)
        {
            var t = new GameObject("Shape").transform;
            t.SetParent(parent, false);
            t.localPosition = offset;
            MeshObject("Panels", t, shape.Coloured(colour), panel, ShadowCastingMode.On);
            MeshObject("Edges", t, shape.Edges(thickness), glow, ShadowCastingMode.Off);
            return t;
        }

        static void MeshObject(string name, Transform parent, Mesh mesh, Material mat, ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows;
        }

        // The windmill's fans are a second object: 8 table steps (0x800 units) out along its heading,
        // 0x1D00 up, rolling 2 steps a tick from a random start
        static void AddFans(Transform root, ObstacleBehaviour beh, int colour, Material panel, Material glow, float thickness)
        {
            var fans = BuiltinShapes.Named("WINDMILL_FANS");
            if (fans == null) return;
            var pivot = new GameObject("Fans").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0f, 0x1D00, 0x800) * PlayerMotor.UnitsToMetres;
            pivot.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0, 256) * StepsToDegrees);
            AddShape(pivot, fans, Vector3.zero, colour, panel, glow, thickness);
            beh.spinner = pivot;
            beh.spinAxis = Vector3.forward;
            beh.spinSpeed = 2 * StepsToDegrees * PlayerMotor.TickRate;
        }
    }
}
