using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpectreEX.EditorTools
{
    // The original Spectre VR 3D shapes (exported from CENTRAL.MAT by Tools/export_shapes.py),
    // turned into SpectreEX's look: solid black panels plus glowing strips along every polygon edge.
    public static class ShapeLibrary
    {
        public const string JsonPath = "Assets/Editor/Data/SpectreShapes.json";

        [Serializable] class ShapeFace { public int[] idx; public int col; }
        [Serializable] class ShapeDef { public int id; public float[] verts; public ShapeFace[] faces; }
        [Serializable] class ShapeFile { public ShapeDef[] shapes; }

        static Dictionary<int, ShapeDef> shapes;

        static ShapeDef Get(int id)
        {
            if (shapes == null)
            {
                var file = JsonUtility.FromJson<ShapeFile>(System.IO.File.ReadAllText(JsonPath));
                shapes = new Dictionary<int, ShapeDef>();
                foreach (var s in file.shapes) shapes[s.id] = s;
            }
            if (!shapes.TryGetValue(id, out var def)) throw new ArgumentException("No Spectre shape " + id);
            return def;
        }

        // warp (optional) reshapes each vertex in the shape's own units before scaling, so the result
        // keeps uniform edge thickness - unlike stretching the finished model with its transform
        static Vector3[] Verts(ShapeDef s, float scale, Func<Vector3, Vector3> warp)
        {
            var v = new Vector3[s.verts.Length / 3];
            for (int i = 0; i < v.Length; i++)
            {
                var p = new Vector3(s.verts[i * 3], s.verts[i * 3 + 1], s.verts[i * 3 + 2]);
                v[i] = (warp != null ? warp(p) : p) * scale;
            }
            return v;
        }

        // Panels: every face fan-triangulated, emitted with both windings and flat normals,
        // so the original's mixed face orientation never leaves holes.
        public static Mesh BodyMesh(int id, float scale, Func<Vector3, Vector3> warp = null)
        {
            var s = Get(id);
            var v = Verts(s, scale, warp);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var f in s.faces)
            {
                if (f.idx.Length < 3) continue;
                for (int k = 1; k + 1 < f.idx.Length; k++)
                {
                    int a = f.idx[0], b = f.idx[k], c = f.idx[k + 1];
                    if (a < 0 || b < 0 || c < 0 || a >= v.Length || b >= v.Length || c >= v.Length) continue;
                    int n = verts.Count;
                    verts.Add(v[a]); verts.Add(v[b]); verts.Add(v[c]);
                    verts.Add(v[a]); verts.Add(v[c]); verts.Add(v[b]);
                    tris.AddRange(new[] { n, n + 1, n + 2, n + 3, n + 4, n + 5 });
                }
            }
            var mesh = new Mesh { name = $"Spectre_{id}_Body" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Glow lines: one thin square prism per unique polygon edge, merged into one mesh.
        public static Mesh EdgeMesh(int id, float scale, float thickness, Func<Vector3, Vector3> warp = null)
        {
            var s = Get(id);
            var v = Verts(s, scale, warp);
            var seen = new HashSet<long>();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var f in s.faces)
            {
                for (int k = 0; k < f.idx.Length; k++)
                {
                    int a = f.idx[k], b = f.idx[(k + 1) % f.idx.Length];
                    if (a == b || a < 0 || b < 0 || a >= v.Length || b >= v.Length) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!seen.Add(key)) continue;
                    AddPrism(verts, tris, v[a], v[b], thickness);
                }
            }
            var mesh = new Mesh { name = $"Spectre_{id}_Edges" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
}
