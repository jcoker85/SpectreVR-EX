using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpectreEX.Matrix
{
    // One original 3D shape: points in the game's world units (x forward, y side, z up) and faces as
    // 0-based point lists (two-point faces are lines), each face with the original's colour value.
    public class ShapeGeometry
    {
        public readonly int[] verts;
        public readonly int[][] faces;
        public readonly int[] colours;

        public ShapeGeometry(int[] verts, int[][] faces, int[] colours)
        {
            this.verts = verts;
            this.faces = faces;
            this.colours = colours;
        }

        readonly Dictionary<int, Mesh> bodies = new Dictionary<int, Mesh>();
        readonly Dictionary<int, Mesh> wires = new Dictionary<int, Mesh>();

        // The wireframe view's lines, in each face's own colour
        public Mesh Wires(float thickness, int objectColour)
        {
            if (!wires.TryGetValue(objectColour, out var mesh) || mesh == null)
                wires[objectColour] = mesh = ShapeMeshes.Edges(UnityPoints(), faces, thickness, "Shape_Wires", colours, objectColour);
            return mesh;
        }
        Mesh edges;
        float edgeThickness;

        // In Unity terms: x = world side, y = up, z = forward
        public Vector3[] UnityPoints()
        {
            var v = new Vector3[verts.Length / 3];
            for (int i = 0; i < v.Length; i++)
                v[i] = new Vector3(verts[i * 3 + 1], verts[i * 3 + 2], verts[i * 3]) * PlayerMotor.UnitsToMetres;
            return v;
        }

        public Mesh Body => Coloured(0);

        // The plain faces, coloured for an object of this colour (UVs into SpectrePalette). Textured faces
        // come in two forms the Options "Textures" setting switches between: TexturedFaces and FlatFaces.
        public Mesh Coloured(int objectColour) => Faces(objectColour, false);

        // The textured faces in plain colour
        public Mesh FlatFaces(int objectColour) => Faces(objectColour, true);

        Mesh Faces(int objectColour, bool texturedOnes)
        {
            int key = objectColour * 2 + (texturedOnes ? 1 : 0);
            if (!bodies.TryGetValue(key, out var mesh))
                bodies[key] = mesh = ShapeMeshes.Body(UnityPoints(), faces, colours, objectColour, texturedOnes, "Shape_Body");
            return mesh;
        }

        readonly Dictionary<int, Mesh> textured = new Dictionary<int, Mesh>();

        // One mesh per texture this shape uses
        public Dictionary<int, Mesh> TexturedFaces
        {
            get
            {
                if (textured.Count == 0 && colours != null)
                    foreach (int id in new HashSet<int>(colours))
                        if (ShapeTextures.Has(id)) textured[id] = ShapeMeshes.Textured(UnityPoints(), faces, colours, id);
                return textured;
            }
        }

        readonly Dictionary<int, Mesh> movieFaces = new Dictionary<int, Mesh>();

        // The faces showing a movie, a hair in front of their plain colour, which is what shows with movies off
        public Dictionary<int, Mesh> MovieFaces
        {
            get
            {
                if (movieFaces.Count == 0 && colours != null)
                    foreach (int id in new HashSet<int>(colours))
                        if (MovieTextures.Has(id)) movieFaces[id] = ShapeMeshes.Textured(UnityPoints(), faces, colours, id, 0.01f);
                return movieFaces;
            }
        }

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

    // An original shape's meshes: its solid faces, and the strips along every polygon edge that Options
    // "Filled" off shows instead, as the original's wireframe mode does.
    public static class ShapeMeshes
    {
        // Panels: every face fan-triangulated, emitted with both windings and flat normals,
        // so the original's mixed face orientation never leaves holes.
        public static Mesh Body(Vector3[] v, int[][] faces, int[] colours, int objectColour, bool texturedOnes, string name)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var backed = BackToBack(faces);
            for (int i = 0; i < faces.Length; i++)
            {
                var f = faces[i];
                if (f.Length < 3) continue;
                int colour = colours != null && i < colours.Length ? colours[i] : 0;
                if (ShapeTextures.Has(colour) != texturedOnes) continue;
                var uv = SpectrePalette.Uv(SpectrePalette.Entry(colour, objectColour));
                for (int k = 1; k + 1 < f.Length; k++)
                {
                    int a = f[0], b = f[k], c = f[k + 1];
                    if (a < 0 || b < 0 || c < 0 || a >= v.Length || b >= v.Length || c >= v.Length) continue;
                    int n = verts.Count;
                    verts.Add(v[a]); verts.Add(v[b]); verts.Add(v[c]);
                    for (int u = 0; u < 3; u++) uvs.Add(uv);
                    tris.AddRange(new[] { n, n + 1, n + 2 });
                    if (backed.Contains(i)) continue;
                    verts.Add(v[a]); verts.Add(v[c]); verts.Add(v[b]);
                    for (int u = 0; u < 3; u++) uvs.Add(uv);
                    tris.AddRange(new[] { n + 3, n + 4, n + 5 });
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.bounds = Bounds(v);   // the whole shape, whatever faces this mesh leaves out
            return mesh;
        }

        // Faces that share their corners with another face: the two sides of one panel, as on the billboards.
        // Each shows only from its own side, or they'd fight over the same plane.
        static HashSet<int> BackToBack(int[][] faces)
        {
            var seen = new Dictionary<string, int>();
            var backed = new HashSet<int>();
            for (int i = 0; i < faces.Length; i++)
            {
                if (faces[i].Length < 3) continue;
                var corners = (int[])faces[i].Clone();
                Array.Sort(corners);
                string key = string.Join(",", corners);
                if (seen.TryGetValue(key, out int other)) { backed.Add(i); backed.Add(other); }
                else seen[key] = i;
            }
            return backed;
        }

        // The faces showing one texture: as _SIZE_TEXTURE_POINTS does it, the image is stretched over
        // each face's own extent in the face's plane. _POLY_2D runs the image's height up the face's first
        // edge and its width across that.
        public static Mesh Textured(Vector3[] v, int[][] faces, int[] colours, int texture, float lift = 0f)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var backed = BackToBack(faces);
            for (int i = 0; i < faces.Length; i++)
            {
                var f = faces[i];
                if (f.Length < 3 || colours[i] != texture) continue;
                var u = (v[f[1]] - v[f[0]]).normalized;
                var outward = Vector3.Cross(v[f[1]] - v[f[0]], v[f[2]] - v[f[0]]).normalized * lift;
                var w = Vector3.Cross(Vector3.Cross(u, v[f[2]] - v[f[0]]), u).normalized;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (int p in f)
                {
                    var q = new Vector2(Vector3.Dot(v[p], u), Vector3.Dot(v[p], w));
                    min = Vector2.Min(min, q);
                    max = Vector2.Max(max, q);
                }
                Vector2 Uv(int p) => new Vector2(Mathf.InverseLerp(min.y, max.y, Vector3.Dot(v[p], w)), Mathf.InverseLerp(min.x, max.x, Vector3.Dot(v[p], u)));
                for (int k = 1; k + 1 < f.Length; k++)
                {
                    int n = verts.Count;
                    var corners = backed.Contains(i) ? new[] { f[0], f[k], f[k + 1] } : new[] { f[0], f[k], f[k + 1], f[0], f[k + 1], f[k] };
                    foreach (int p in corners)
                    {
                        verts.Add(v[p] + outward);
                        uvs.Add(Uv(p));
                    }
                    for (int c = 0; c < corners.Length; c++) tris.Add(n + c);
                }
            }
            var mesh = new Mesh { name = "Shape_Textured" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.bounds = Bounds(v);
            return mesh;
        }

        // Glow lines: one thin square prism per unique polygon edge, merged into one mesh.
        // With colours, each line takes its face's colour, as _POLYWIRE draws them.
        public static Mesh Edges(Vector3[] v, int[][] faces, float thickness, string name, int[] colours = null, int objectColour = 0)
        {
            var seen = new HashSet<long>();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < faces.Length; i++)
            {
                var f = faces[i];
                var uv = colours != null ? SpectrePalette.Uv(SpectrePalette.Entry(i < colours.Length ? colours[i] : 0, objectColour)) : Vector2.zero;
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    if (a == b || a < 0 || b < 0 || a >= v.Length || b >= v.Length) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!seen.Add(key)) continue;
                    AddPrism(verts, tris, v[a], v[b], thickness);
                    while (uvs.Count < verts.Count) uvs.Add(uv);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            if (colours != null) mesh.SetUVs(0, uvs);
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
    // Tools/import_original.py into Resources/Shapes/BuiltinShapes.json.
    public static class BuiltinShapes
    {
        [Serializable] class Face { public int[] idx; public int col; }
        [Serializable] class Shape { public string name; public int[] verts; public Face[] faces; }
        [Serializable] class Entry { public int id, type, shap; public string shape; }
        [Serializable] class File { public Entry[] table; public Shape[] shapes; public int[] typeColors, objColors, skyColors; public string[] textures; }

        static Dictionary<string, ShapeGeometry> byName;
        static Dictionary<int, Entry> byId;
        static File data;

        // The colour an object of this type is drawn in (_SHAPE_COLORS, read by _ADD_SHAPE)
        public static int TypeColour(int type)
        {
            Load();
            return data != null && type >= 0 && type < data.typeColors.Length ? data.typeColors[type] : 0;
        }

        // A shap resource's face colour as the loader turns it into a palette value: an index into the
        // same table, with 0xFE and 0xFF the special values. Above 0xFF is a texture, kept as it is.
        public static int FaceColour(int value)
        {
            if (value > 0xFF) return value;
            int v = value == 0xFE ? 0xFE : value == 0xFF ? 0xFF : TypeColour(value);
            return v == 0xFF ? 0 : v == 0xFE ? 1 : v;
        }

        // The file behind a texture face colour (6000-6015), or "" if there is none
        public static string TextureFile(int colour)
        {
            Load();
            int i = colour - 6000;
            return data != null && i >= 0 && i < data.textures.Length ? data.textures[i] : "";
        }

        public static Color32 ObjectColour(int level) => LevelColour(data => data.objColors, level);
        public static Color32 SkyColour(int level) => LevelColour(data => data.skyColors, level);

        // The tables hold 30 levels, then repeat
        static Color32 LevelColour(Func<File, int[]> table, int level)
        {
            Load();
            if (data == null) return new Color32(255, 255, 255, 255);
            int[] t = table(data);
            int at = (Mathf.Max(0, level) % 30) * 3;
            return new Color32((byte)t[at], (byte)t[at + 1], (byte)t[at + 2], 255);
        }

        static void Load()
        {
            if (byName != null) return;
            byName = new Dictionary<string, ShapeGeometry>();
            byId = new Dictionary<int, Entry>();
            var text = Resources.Load<TextAsset>("Shapes/BuiltinShapes");
            if (text == null) { Debug.LogWarning("Spectre VR-EX: Resources/Shapes/BuiltinShapes.json is missing"); return; }
            var file = data = JsonUtility.FromJson<File>(text.text);
            foreach (var s in file.shapes)
                byName[s.name] = new ShapeGeometry(s.verts, Array.ConvertAll(s.faces, f => f.idx), Array.ConvertAll(s.faces, f => f.col));
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

        // _LOAD_WRLD clears every random list's pick at the start of a level, and shapes load then, so
        // the Complex option applies from the next level
        public void NewLevel()
        {
            picks.Clear();
            plain = !GameSettings.DisplayComplex;
        }

        bool plain = !GameSettings.DisplayComplex;

        int[] Table => matrix?.ShapeTable ?? central?.ShapeTable;

        // Complex picks the textured column, off the plain one (_TEXTURES bit 1)
        public List<Part> ForType(int type)
        {
            var parts = new List<Part>();
            var table = Table;
            if (table == null || type < 0 || type * 3 >= table.Length) return parts;
            int id = table[type * 3 + (plain ? 1 : 0)];
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
