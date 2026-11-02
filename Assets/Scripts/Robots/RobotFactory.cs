using UnityEngine;
using UnityEngine.Rendering;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // Materials for robots in the Spectre VR-EX style, set up by the arena builder
    [System.Serializable]
    public class RobotLook
    {
        public Material shapes;   // solid faces, the level's palette
        public Material tank, superTank, turret, slicer, flyer, acid, wall, mud, orbiter, bolt;   // wireframe and debris

        public Material GlowFor(int type) => type switch
        {
            8 => superTank,
            20 => turret,
            21 => slicer,
            22 => flyer,
            23 => acid,
            24 => wall,
            25 => mud,
            35 or 45 => orbiter,
            _ => tank,
        } ?? tank;
    }

    // Builds a robot from its original shape(s) - the matrix's shps entry for its type, as _ADD_ENEMY
    // does with _ADD_SHAPE - with a collider round the exact bounds (_CUBE_COLLIDE).
    public static class RobotFactory
    {
        public static Robot Build(int type, ShapeSet shapes, RobotLook look, float edgeThickness)
        {
            var parts = Parts(type, shapes);
            if (parts == null) return null;

            var root = new GameObject($"Robot_{type}");
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            Fill(model, root.AddComponent<BoxCollider>(), type, parts, look, edgeThickness);

            var health = root.AddComponent<Health>();
            health.team = Team.Enemy;
            health.hitSound = Snd.EnemyHit;
            health.deathSound = Snd.Explosion;
            health.hiddenFromRadar = type == 27;   // Cloaked Tank: visible, not on radar

            var robot = root.AddComponent<Robot>();
            if (type == 26)   // Invisible Tank: on radar only
                foreach (var r in root.GetComponentsInChildren<Renderer>()) r.enabled = false;
            return robot;
        }

        static System.Collections.Generic.List<ShapeSet.Part> Parts(int type, ShapeSet shapes)
        {
            // No shape, no robot: _ADD_ENEMY gives up on it
            var parts = shapes?.ForType(type);
            return parts == null || parts.Count == 0 ? null : parts;
        }

        static void Fill(Transform model, BoxCollider col, int type, System.Collections.Generic.List<ShapeSet.Part> parts, RobotLook look, float edgeThickness)
        {
            var glow = look.GlowFor(type);
            int colour = BuiltinShapes.TypeColour(type);
            Bounds bounds = default;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var t = new GameObject("Shape").transform;
                t.SetParent(model, false);
                t.localPosition = part.offset;
                Mesh("Panels", t, part.shape.Coloured(colour), look.shapes, ShadowCastingMode.On);
                foreach (var face in part.shape.TexturedFaces) Mesh("Textured", t, face.Value, ShapeTextures.Material(face.Key, look.shapes), ShadowCastingMode.On);
                if (part.shape.TexturedFaces.Count > 0) Mesh("Flat", t, part.shape.FlatFaces(colour), look.shapes, ShadowCastingMode.On);
                Mesh("Edges", t, part.shape.Wires(edgeThickness, colour), look.shapes, ShadowCastingMode.Off);
                var b = part.shape.Body.bounds;
                b.center += part.offset;
                if (i == 0) bounds = b; else bounds.Encapsulate(b);
            }

            col.center = bounds.center;
            col.size = Vector3.Max(bounds.size, Vector3.one * 0.6f);
            // Slicers reach 0x1400 below their base, so they still catch a craft under the floor
            if (type == 21)
            {
                float extra = 0x1400 * OriginalMath.UnitsToMetres;
                col.center -= Vector3.up * extra * 0.5f;
                col.size += Vector3.up * extra;
            }
        }

        static void Mesh(string name, Transform parent, Mesh mesh, Material mat, ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows;
        }
    }
}
