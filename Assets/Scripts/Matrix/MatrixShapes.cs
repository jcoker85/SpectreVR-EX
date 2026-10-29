using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpectreEX.Matrix
{
    // One original 3D shape: points in the game's world units (x forward, y side, z up) and faces as
    // 0-based point lists (two-point faces are lines).
    public class ShapeGeometry
    {
        public readonly int[] verts;
        public readonly int[][] faces;

        public ShapeGeometry(int[] verts, int[][] faces)
        {
            this.verts = verts;
            this.faces = faces;
        }

        Mesh body, edges;
        float edgeThickness;

        // In Unity terms: x = world side, y = up, z = forward
        public Vector3[] UnityPoints()
        {
            var v = new Vector3[verts.Length / 3];
            for (int i = 0; i < v.Length; i++)
                v[i] = new Vector3(verts[i * 3 + 1], verts[i * 3 + 2], verts[i * 3]) * PlayerMotor.UnitsToMetres;
            return v;
        }

        public Mesh Body => body != null ? body : body = ShapeMeshes.Body(UnityPoints(), faces, "Shape_Body");

        public Mesh Edges(float thickness)
        {
            if (edges == null || !Mathf.Approximately(edgeThickness, thickness))
            {
                edges = ShapeMeshes.Edges(UnityPoints(), faces, thickness, "Shape_Edges");
                edgeThickness = thickness;
            }
            return edges;
        }
    }

    // Spectre VR-EX's look for an original shape: solid panels plus glowing strips along every polygon edge.
    public static class ShapeMeshes
    {
        // Panels: every face fan-triangulated, emitted with both windings and flat normals,
        // so the original's mixed face orientation never leaves holes.
        public static Mesh Body(Vector3[] v, IEnumerable<int[]> faces, string name)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var f in faces)
            {
                if (f.Length < 3) continue;
                for (int k = 1; k + 1 < f.Length; k++)
                {
                    int a = f[0], b = f[k], c = f[k + 1];
                    if (a < 0 || b < 0 || c < 0 || a >= v.Length || b >= v.Length || c >= v.Length) continue;
                    int n = verts.Count;
                    verts.Add(v[a]); verts.Add(v[b]); verts.Add(v[c]);
                    verts.Add(v[a]); verts.Add(v[c]); verts.Add(v[b]);
                    tris.AddRange(new[] { n, n + 1, n + 2, n + 3, n + 4, n + 5 });
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (verts.Count == 0) mesh.bounds = Bounds(v);   // line-only shapes
            return mesh;
        }

        // Glow lines: one thin square prism per unique polygon edge, merged into one mesh.
        public static Mesh Edges(Vector3[] v, IEnumerable<int[]> faces, float thickness, string name)
        {
            var seen = new HashSet<long>();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var f in faces)
            {
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    if (a == b || a < 0 || b < 0 || a >= v.Length || b >= v.Length) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!seen.Add(key)) continue;
                    AddPrism(verts, tris, v[a], v[b], thickness);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Bounds Bounds(Vector3[] v)
        {
            if (v.Length == 0) return default;
            var b = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) b.Encapsulate(p);
            return b;
        }

        static void AddPrism(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, float t)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            d /= len;
            // Extend slightly past each end so corners close up
            a -= d * t * 0.5f;
            b += d * t * 0.5f;
            Vector3 up = Mathf.Abs(Vector3.Dot(d, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Cross(d, up).normalized * (t * 0.5f);
            Vector3 w = Vector3.Cross(d, u).normalized * (t * 0.5f);
            int n = verts.Count;
            verts.Add(a - u - w); verts.Add(a + u - w); verts.Add(a + u + w); verts.Add(a - u + w);
            verts.Add(b - u - w); verts.Add(b + u - w); verts.Add(b + u + w); verts.Add(b - u + w);
            int[] q =
            {
                0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7, // sides
                0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7,                                     // caps
            };
            foreach (int i in q) tris.Add(n + i);
        }
    }

    // The shapes built into SVRWIN.EXE (ids under 1000) and its _OT_TO_SHAPE table, exported by
    // Tools/export_builtin_shapes.py into Resources/Shapes/BuiltinShapes.json.
    public static class BuiltinShapes
    {
        [Serializable] class Face { public int[] idx; }
        [Serializable] class Shape { public string name; public int[] verts; public Face[] faces; }
        [Serializable] class Entry { public int id, type, shap; public string shape; }
        [Serializable] class File { public Entry[] table; public Shape[] shapes; }

        static Dictionary<string, ShapeGeometry> byName;
        static Dictionary<int, Entry> byId;

        static void Load()
        {
            if (byName != null) return;
            byName = new Dictionary<string, ShapeGeometry>();
            byId = new Dictionary<int, Entry>();
            var text = Resources.Load<TextAsset>("Shapes/BuiltinShapes");
            if (text == null) { Debug.LogWarning("Spectre VR-EX: Resources/Shapes/BuiltinShapes.json is missing"); return; }
            var file = JsonUtility.FromJson<File>(text.text);
            foreach (var s in file.shapes)
                byName[s.name] = new ShapeGeometry(s.verts, Array.ConvertAll(s.faces, f => f.idx));
            // As _ADD_SHAPE reads it: the first entry for an id naming a shap resource wins,
            // otherwise the last one's template
            foreach (var e in file.table)
            {
                if (e.id <= 0) continue;
                if (byId.TryGetValue(e.id, out var had) && had.shap != 0) continue;
                byId[e.id] = e;
            }
        }

        public static ShapeGeometry Named(string name)
        {
            Load();
            return byName.TryGetValue(name, out var g) ? g : null;
        }

        // A built-in id resolves to a shap resource id (> 0), or to a template (returned), or to nothing
        public static ShapeGeometry Lookup(int id, out int shapResource)
        {
            Load();
            shapResource = 0;
            if (!byId.TryGetValue(id, out var e)) return null;
            if (e.shap != 0) { shapResource = e.shap; return null; }
            return string.IsNullOrEmpty(e.shape) ? null : Named(e.shape);
        }
    }

    // The shape(s) for a world object type, picked as SVRWIN.EXE's _ADD_SHAPE does from a matrix's
    // shps table: ids 1-999 are built in, 1000-1999 random lists (one pick per list per level),
    // 2000-2999 multi-part shapes, 10000-10999 the matrix's own shap meshes. Shape tables and shapes
    // a matrix lacks come from Central Systems (the original has no shapes at all for a matrix
    // without a shps table).
    public class ShapeSet
    {
        public struct Part
        {
            public ShapeGeometry shape;
            public Vector3 offset;   // from the first part, in the object's own frame (metres)
            public int id;           // the resolved shape id (for the original's special parts)
        }

        readonly MatrixFile matrix, central;
        readonly Dictionary<int, int> picks = new Dictionary<int, int>();

        public ShapeSet(MatrixFile matrix, MatrixFile central)
        {
            this.matrix = matrix != null && matrix.HasShapeTable ? matrix : central;
            this.central = central;
        }

        // _LOAD_WRLD clears every random list's pick at the start of a level
        public void NewLevel() => picks.Clear();

        int[] Table => matrix?.ShapeTable ?? central?.ShapeTable;

        // The textured column, which the game uses with its default settings (_TEXTURES = 3)
        public List<Part> ForType(int type)
        {
            var parts = new List<Part>();
            var table = Table;
            if (table == null || type < 0 || type * 3 >= table.Length) return parts;
            int id = table[type * 3];
            id = id switch { 16 => 10030, 17 => 10027, 20 => 10087, _ => id };

            if (id >= 2000 && id < 3000)
            {
                var multi = Find(m => m.MultiShapes, id);
                if (multi == null) return parts;
                for (int i = 0; i < multi.Length; i++)
                {
                    var shape = Resolve(multi[i].shape, out int resolved);
                    if (shape == null) break;   // the original stops at the first missing part
                    var p = multi[i];
                    float dist = i == 0 ? 0f : p.distance * 0xA00 * PlayerMotor.UnitsToMetres;
                    float height = i == 0 ? 0f : p.height * 0x28 * PlayerMotor.UnitsToMetres;
                    // Unverified sign: every multi-part shape in Central Systems is left-right symmetric
                    var dir = Quaternion.Euler(0f, p.angle, 0f) * Vector3.forward;
                    parts.Add(new Part { shape = shape, offset = dir * dist + Vector3.up * height, id = resolved });
                }
                return parts;
            }

            var single = Resolve(id, out int r);
            if (single != null) parts.Add(new Part { shape = single, id = r });
            return parts;
        }

        // One shape by its id, as _CHANGE_SHAPE looks it up
        public ShapeGeometry ShapeById(int id) => Resolve(id, out _);

        ShapeGeometry Resolve(int id, out int resolved)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                resolved = id;
                if (id >= 1000 && id < 2000)
                {
                    if (!picks.TryGetValue(id, out int pick))
                    {
                        var list = Find(m => m.RandomShapes, id);
                        if (list == null || list.Length == 0) return null;
                        pick = list[UnityEngine.Random.Range(0, list.Length)];
                        picks[id] = pick;
                    }
                    id = pick;
                    continue;
                }
                if (id > 0 && id < 1000)
                {
                    var template = BuiltinShapes.Lookup(id, out int shap);
                    if (template != null || shap == 0) return template;
                    id = shap;
                    continue;
                }
                if (id >= 10000 && id < 11000) return Find(m => m.Shapes, id);
                return null;
            }
            resolved = id;
            return null;
        }

        T Find<T>(Func<MatrixFile, Dictionary<int, T>> pick, int id) where T : class
        {
            if (matrix != null && pick(matrix).TryGetValue(id, out var v)) return v;
            if (central != null && pick(central).TryGetValue(id, out v)) return v;
            return null;
        }
    }
}
