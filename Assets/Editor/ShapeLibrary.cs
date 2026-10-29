using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpectreEX.EditorTools
{
    // The original Spectre VR 3D shapes (exported from CENTRAL.MAT by Tools/export_shapes.py) for the
    // builder's robots, player and flag, turned into Spectre VR-EX's look: solid black panels plus
    // glowing strips along every polygon edge. Matrix obstacles are built at runtime instead.
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

        // Mesh building is shared with the runtime obstacles (Matrix.ShapeMeshes)
        public static Mesh BodyMesh(int id, float scale, Func<Vector3, Vector3> warp = null)
        {
            var s = Get(id);
            return Matrix.ShapeMeshes.Body(Verts(s, scale, warp), Faces(s), $"Spectre_{id}_Body");
        }

        public static Mesh EdgeMesh(int id, float scale, float thickness, Func<Vector3, Vector3> warp = null)
        {
            var s = Get(id);
            return Matrix.ShapeMeshes.Edges(Verts(s, scale, warp), Faces(s), thickness, $"Spectre_{id}_Edges");
        }

        static IEnumerable<int[]> Faces(ShapeDef s)
        {
            foreach (var f in s.faces) yield return f.idx;
        }
    }
}
