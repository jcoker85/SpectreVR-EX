using System.Collections.Generic;
using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // The original's de-res (_DRAW_POLY's second argument, p): every polygon is filled one screen row at a
    // time, and with p set each row is slid sideways by a whole number of pixels before it's drawn, so the
    // shape itself bends and tears along a wave. Here each face of the model is cut into bands of the
    // original's screen rows and each band moved across the view by that row's step, in the original's
    // pixels at the reference window's 530 rows, keeping its face's colour, texture and lighting. Textured
    // and movie faces ripple the same way: _DRAW_OBJS pushes the same p to _POLYTEXTURE as to _DRAW_POLY
    // (1028:1E8F, 1028:1EF9), and _POLYTEXTURE slides its rows by the same steps.
    //   p < 0 (a hit, -1 to -6 and back): step = Cos[((row - 4p - (p >> 4)) * 0x20) & 0xFF] * p >> 8
    //   p > 0 (going): step = Cos[((row + 4p) * 0x20) & 0xFF] * (p + width >> 3) >> 8, then
    //                  step += rand * step / 0x8000 - step / 2, width being the face's row in pixels
    // so a hit ripples with a wave every 8 rows, and a dying object shakes apart.
    [DefaultExecutionOrder(10000)]   // after the camera has moved this frame: the rows are the screen's
    public class DeRes : MonoBehaviour
    {
        const float ReferenceRows = 530f;   // the reference window's height (_TY: 290 of 530 rows down)
        const float BehindStill = 0.003f;   // how far back the ripple sits, as a share of its distance

        class Part
        {
            public Transform transform;
            public Renderer renderer;
            public Vector3[] verts;
            public Vector2[] uvs;
            public int[] tris;
            public int[] faceOf;   // per triangle: the face it belongs to, its coplanar neighbours
            public Vector2? uv;    // one colour for the whole part (a dump's ramp)
            public Material material;
        }

        static readonly Dictionary<GameObject, DeRes> playing = new Dictionary<GameObject, DeRes>();   // one at a time on a craft

        GameObject source;
        readonly List<Part> parts = new List<Part>();
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Material> materials = new List<Material>();
        readonly List<List<int>> tris = new List<List<int>>();
        readonly List<Material> drawn = new List<Material>();   // the materials with something in them this frame
        Mesh mesh;
        MeshRenderer own;
        bool isPlayer;
        float startTick;
        int ticks;
        int peak;
        bool returns;
        bool world;      // the cockpit's hit ripple on something other than the craft
        bool wreck;      // the player's craft going, on the _DEAD_TIMER curve
        int from;        // the de-res the wreck starts from: a hit ripple it cut short
        int amount;      // p this tick
        int seed;

        // The cockpit ripple running now, for anything that appears during it
        static float rippleStart = float.NegativeInfinity;
        static int rippleTicks;
        static int ripplePeak;

        // Ticks of SimClock, drawn between them
        static float Now => SimClock.Ticks + SimClock.TickFraction;

        public static void Play(GameObject source, int peak, int ticks, bool returns, float elapsed = 0f, bool world = false)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            // Going for good takes over from a hit's ripple still running, and carries on from where it got to
            int from = 0;
            if (playing.TryGetValue(source, out var running))
            {
                if (returns) return;
                from = running.amount;
                running.Stop();
            }
            var go = new GameObject("DeRes");
            var d = go.AddComponent<DeRes>();
            playing[source] = d;
            d.world = world;
            d.from = from;
            d.peak = peak;
            d.ticks = ticks;
            d.returns = returns;
            d.source = source;
            d.seed = source.GetInstanceID();
            d.startTick = Now - elapsed;
            var dump = source.GetComponent<AmmoDump>();
            foreach (var f in source.GetComponentsInChildren<MeshFilter>(true))
            {
                // Every filled face the source shows: textures and movies ripple too, and with them off their
                // faces are plain ("Flat", and the colour under a movie in "Panels")
                bool shown = f.name == "Panels" || (dump != null && f.name == "Tile")
                    || (f.name == "Textured" && GameSettings.DisplayTextures) || (f.name == "Flat" && !GameSettings.DisplayTextures)
                    || (f.name == "Movie" && GameSettings.DisplayMovies);
                if (!shown || f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
                var r = f.GetComponent<Renderer>();
                if (returns && !r.enabled) continue;   // a cloaked robot stays that way
                go.layer = f.gameObject.layer;
                var part = new Part
                {
                    transform = f.transform,
                    renderer = r,
                    verts = f.sharedMesh.vertices,
                    uvs = f.sharedMesh.uv,
                    tris = WithoutMovieFaces(f),
                    uv = dump != null ? SpectrePalette.Uv(SpectrePalette.Entry(dump.Ramp)) : (Vector2?)null,
                };
                // Plain faces in the palette's material, textures and movies in their own (a movie's plays on)
                bool picture = f.name == "Textured" || f.name == "Movie";
                part.material = picture && r != null && r.sharedMaterial != null ? r.sharedMaterial : gm.obstacleLook.shapes;
                if (!d.materials.Contains(part.material)) { d.materials.Add(part.material); d.tris.Add(new List<int>()); }
                part.faceOf = Faces(part.verts, part.tris, d.parts.Count);
                d.parts.Add(part);
                if (returns) r.enabled = false;
            }
            d.mesh = new Mesh { name = "DeRes", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            d.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = d.mesh;
            d.isPlayer = source.GetComponent<PlayerTank>() != null;
            d.wreck = d.isPlayer && !returns;
            d.own = go.AddComponent<MeshRenderer>();
            if (d.materials.Count == 0) { d.materials.Add(gm.obstacleLook.shapes); d.tris.Add(new List<int>()); }
            d.own.sharedMaterials = d.materials.ToArray();
            // The source's own shadow, and in the ray-traced reflections, rebuilt every frame
            d.own.shadowCastingMode = d.parts.Count > 0 && d.parts[0].renderer != null ? d.parts[0].renderer.shadowCastingMode : UnityEngine.Rendering.ShadowCastingMode.Off;
            d.own.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.DynamicGeometry;
            d.own.enabled = false;   // until it has something to draw: an empty mesh crashes the ray tracing build
            if (d.isPlayer && returns && ViewCamera.CraftHidden) PlayWorld(gm, peak, ticks);
        }

        // A movie face is drawn a hair in front of its plain colour, which is what shows with movies off.
        // With them on, the original draws only the movie there (_POLYTEXTURE), so the plain copy stays out
        // of the ripple, or it would fight the movie's own rippling rows.
        static int[] WithoutMovieFaces(MeshFilter panels)
        {
            int[] tris = panels.sharedMesh.triangles;
            if (panels.name != "Panels" || !GameSettings.DisplayFilled || !GameSettings.DisplayMovies || panels.transform.parent == null) return tris;
            var movies = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 n)>();
            foreach (Transform sibling in panels.transform.parent)
            {
                if (sibling.name != "Movie") continue;
                var mf = sibling.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var mv = mf.sharedMesh.vertices;
                var mt = mf.sharedMesh.triangles;
                for (int i = 0; i + 2 < mt.Length; i += 3)
                {
                    Vector3 a = mv[mt[i]], b = mv[mt[i + 1]], c = mv[mt[i + 2]];
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    if (n.sqrMagnitude < 1e-12f) continue;
                    movies.Add((a, b, c, n.normalized));
                }
            }
            if (movies.Count == 0) return tris;

            var v = panels.sharedMesh.vertices;
            var kept = new List<int>(tris.Length);
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 centre = (v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f;
                if (!UnderMovie(centre, movies)) { kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]); }
            }
            return kept.ToArray();
        }

        // Within a movie triangle's outline and no more than a few centimetres behind or in front of it
        static bool UnderMovie(Vector3 p, List<(Vector3 a, Vector3 b, Vector3 c, Vector3 n)> movies)
        {
            foreach (var m in movies)
            {
                float off = Vector3.Dot(p - m.a, m.n);
                if (Mathf.Abs(off) > 0.03f) continue;
                Vector3 q = p - m.n * off;
                Vector3 v0 = m.b - m.a, v1 = m.c - m.a, v2 = q - m.a;
                float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1);
                float d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
                float den = d00 * d11 - d01 * d01;
                if (Mathf.Abs(den) < 1e-12f) continue;
                float y = (d11 * d20 - d01 * d21) / den, z = (d00 * d21 - d01 * d20) / den;
                const float Eps = 1e-3f;
                if (y >= -Eps && z >= -Eps && y + z <= 1f + Eps) return true;
            }
            return false;
        }

        // The meshes are fans of triangles; the original slides each polygon's row as one, so triangles in
        // the same plane are taken as one face
        static int[] Faces(Vector3[] v, int[] t, int partIndex)
        {
            var ids = new int[t.Length / 3];
            var seen = new Dictionary<(int, int, int, int), int>();
            for (int i = 0; i < ids.Length; i++)
            {
                Vector3 a = v[t[i * 3]], b = v[t[i * 3 + 1]], c = v[t[i * 3 + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                // Both sides of a panel are one polygon's two windings: the same face, so the same jitter
                if (n.x < -1e-4f || (Mathf.Abs(n.x) <= 1e-4f && (n.y < -1e-4f || (Mathf.Abs(n.y) <= 1e-4f && n.z < 0f)))) n = -n;
                var key = (Mathf.RoundToInt(n.x * 100f), Mathf.RoundToInt(n.y * 100f), Mathf.RoundToInt(n.z * 100f), Mathf.RoundToInt(Vector3.Dot(n, a) * 500f));
                if (!seen.TryGetValue(key, out int id)) seen[key] = id = partIndex * 4096 + seen.Count;
                ids[i] = id;
            }
            return ids;
        }

        // _DRAW_OBJS: from the cockpit the player's de-res goes to every filled shape in the view instead
        static void PlayWorld(GameManager gm, int peak, int ticks)
        {
            if (!GameSettings.DisplayFilled) return;
            rippleStart = Now;
            rippleTicks = ticks;
            ripplePeak = peak;
            foreach (var o in gm.Obstacles) if (o != null) Play(o, peak, ticks, true, 0f, true);
            foreach (var f in gm.Flags) if (f != null) Play(f.gameObject, peak, ticks, true, 0f, true);
            foreach (var d in gm.Dumps) if (d != null) Play(d.gameObject, peak, ticks, true, 0f, true);
            foreach (var p in gm.JumpPads) if (p != null) Play(p.gameObject, peak, ticks, true, 0f, true);
            foreach (var r in gm.Robots) if (r != null && !r.IsDead) Play(r.gameObject, peak, ticks, true, 0f, true);
            foreach (var s in Object.FindObjectsByType<OrbitShield>(FindObjectsSortMode.None)) Play(s.gameObject, peak, ticks, true, 0f, true);
            gm.GetComponent<RobotDirector>().Shots.ForEachVisual(v => Play(v, peak, ticks, true, 0f, true));
        }

        // A shot fired mid-ripple joins it where it has got to
        public static void JoinRipple(GameObject source)
        {
            float elapsed = Now - rippleStart;
            if (elapsed < rippleTicks && ViewCamera.CraftHidden) Play(source, ripplePeak, rippleTicks, true, elapsed, true);
        }

        bool Dead
        {
            get
            {
                var health = source != null ? source.GetComponent<Health>() : null;
                return health != null && health.IsDead;
            }
        }

        // Ends it now, the source drawn whole again if it comes back
        void Stop()
        {
            if (playing.TryGetValue(source, out var d) && d == this) playing.Remove(source);
            if (returns)
                foreach (var p in parts)
                    if (p.renderer != null && !Dead)
                    {
                        p.renderer.enabled = true;
                        GameManager.ApplyFilled(p.renderer);
                    }
            Destroy(mesh);
            Destroy(gameObject);
        }

        void LateUpdate()
        {
            float age = Now - startTick;
            // _DRAW_OBJS gives the rest of the world the craft's de-res only while the view is the cockpit
            if (age >= ticks || (world && !ViewCamera.CraftHidden))
            {
                Stop();
                return;
            }

            int tick = Mathf.FloorToInt(age) + 1;   // ticks of it so far, this one included
            if (wreck) amount = WreckAmount(from, tick - 1);
            // _D_P_DE_RES -1: down a step a tick past -5, then back up to 0
            else if (returns) amount = -(tick <= peak ? tick : Mathf.Max(0, ticks - tick));
            // A dying robot's count (+0xD21) goes up one a tick; a fading shot's the same way to its peak
            else amount = Mathf.Clamp(Mathf.FloorToInt((float)peak * tick / ticks), 1, peak);
            Build(amount, SimClock.Ticks);
        }

        // _PLAY_LEVEL: while _DEAD_TIMER runs, each tick adds _DEAD_TIMER >> 7 to _PLAYER_DE_RES (the timer
        // as the last tick left it, 0x32 more each tick), up to 0x5A: nothing for two ticks, then faster
        // and faster, at the top after about 23. It carries on from a hit ripple it cut short, which may
        // still be below 0.
        static int WreckAmount(int from, int ticksDone)
        {
            int total = from;
            for (int j = 1; j <= ticksDone && total < 0x5A; j++) total += (0x32 * j) >> 7;
            return Mathf.Min(total, 0x5A);
        }

        // ---------- drawing ----------

        // One band of a face: its corners in world space and clip space, the row it's on, its face
        struct Corner { public Vector3 world; public Vector4 clip; public Vector2 uv; }
        readonly List<Corner> poly = new List<Corner>(8), lower = new List<Corner>(8), upper = new List<Corner>(8), bandPoly = new List<Corner>(8);
        readonly List<Corner> bandCorners = new List<Corner>();
        readonly List<(int start, int count, int row, int face, Vector3 normal, Material material)> bands = new List<(int, int, int, int, Vector3, Material)>();
        readonly Dictionary<(int face, int row), (float min, float max)> rowExtent = new Dictionary<(int, int), (float, float)>();

        void Build(int p, long tick)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Matrix4x4 toClip = cam.projectionMatrix * cam.worldToCameraMatrix;
            float near = cam.nearClipPlane;
            // Rows of whole screen pixels, as near the reference window's 530 as the screen allows: rows of
            // 530ths would land as 2 and 3 pixels in turn and beat into stripes. Steps are in these rows'
            // pixels, as the original's are in its own.
            int rowPixels = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight / ReferenceRows));
            float rows = (float)cam.pixelHeight / rowPixels;
            float columns = (float)cam.pixelWidth / rowPixels;
            float overlap = 0.5f / rowPixels;

            bands.Clear();
            bandCorners.Clear();
            rowExtent.Clear();
            foreach (var part in parts)
            {
                if (part.transform == null) continue;
                var m = part.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < part.tris.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(part.verts[part.tris[i]]);
                    Vector3 b = m.MultiplyPoint3x4(part.verts[part.tris[i + 1]]);
                    Vector3 c = m.MultiplyPoint3x4(part.verts[part.tris[i + 2]]);
                    Vector4 ca = toClip * new Vector4(a.x, a.y, a.z, 1f), cb = toClip * new Vector4(b.x, b.y, b.z, 1f), cc = toClip * new Vector4(c.x, c.y, c.z, 1f);
                    if (Outside(ca, cb, cc)) continue;
                    Vector2 UvOf(int corner) => part.uv ?? (part.uvs.Length > 0 ? part.uvs[part.tris[i + corner]] : Vector2.zero);
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    int face = part.faceOf[i / 3];

                    // In front of the eye only
                    poly.Clear();
                    poly.Add(new Corner { world = a, clip = ca, uv = UvOf(0) });
                    poly.Add(new Corner { world = b, clip = cb, uv = UvOf(1) });
                    poly.Add(new Corner { world = c, clip = cc, uv = UvOf(2) });
                    Split(poly, new Vector4(0f, 0f, 0f, 1f), -near, upper, lower);
                    if (upper.Count < 3) continue;
                    poly.Clear();
                    poly.AddRange(upper);

                    // Rows from the top of the window
                    float top = float.MaxValue, bottom = float.MinValue;
                    foreach (var k in poly)
                    {
                        float r = Row(k.clip, rows);
                        top = Mathf.Min(top, r);
                        bottom = Mathf.Max(bottom, r);
                    }
                    int first = Mathf.Max(Mathf.FloorToInt(top), -1);
                    int last = Mathf.Min(Mathf.CeilToInt(bottom) - 1, (int)rows);
                    if (first > Mathf.FloorToInt(top)) Split(poly, -RowLine(first, rows), 0f, upper, lower, true);
                    for (int row = first; row <= last && poly.Count >= 3; row++)
                    {
                        // The band above the next row's line (reaching half a pixel past it, so the seam between
                        // rows always closes under TAA's sub-pixel jitter), and what's left below the line
                        Split(poly, RowLine(row + 1 + overlap, rows), 0f, upper, lower);
                        bool hasBand = upper.Count >= 3;
                        if (hasBand) { bandPoly.Clear(); bandPoly.AddRange(upper); }
                        Split(poly, RowLine(row + 1, rows), 0f, upper, lower);
                        if (hasBand) { upper.Clear(); upper.AddRange(bandPoly); }
                        if (hasBand)
                        {
                            int start = bandCorners.Count;
                            float min = float.MaxValue, max = float.MinValue;
                            foreach (var k in upper)
                            {
                                bandCorners.Add(k);
                                float x = (k.clip.x / k.clip.w + 1f) * 0.5f * columns;
                                min = Mathf.Min(min, x);
                                max = Mathf.Max(max, x);
                            }
                            bands.Add((start, upper.Count, row, face, normal, part.material));
                            var key = (face, row);
                            rowExtent[key] = rowExtent.TryGetValue(key, out var e) ? (Mathf.Min(e.min, min), Mathf.Max(e.max, max)) : (min, max);
                        }
                        poly.Clear();
                        poly.AddRange(lower);
                    }
                }
            }

            verts.Clear();
            normals.Clear();
            uvs.Clear();
            foreach (var list in tris) list.Clear();
            Vector3 right = cam.transform.right;
            Vector3 eye = cam.transform.position;
            float p00 = cam.projectionMatrix.m00;
            foreach (var band in bands)
            {
                rowExtent.TryGetValue((band.face, band.row), out var e);
                int step = Step(p, band.row, Mathf.RoundToInt(e.max - e.min), tick, band.face);
                // Pixels across into the eye's x at each corner's depth, so the whole band moves the same
                // number of pixels and stays on its row
                float ndc = step * 2f / columns;
                int n = verts.Count;
                for (int j = 0; j < band.count; j++)
                {
                    var k = bandCorners[band.start + j];
                    // Pushed back along its own line of sight, so it stays on the same pixels but loses to any
                    // still face in its plane, which would otherwise fight the rows sliding across it
                    Vector3 moved = k.world + right * (ndc * k.clip.w / p00);
                    verts.Add(eye + (moved - eye) * (1f + BehindStill));
                    normals.Add(band.normal);
                    uvs.Add(k.uv);
                }
                var into = tris[materials.IndexOf(band.material)];
                for (int j = 1; j + 1 < band.count; j++) { into.Add(n); into.Add(n + j); into.Add(n + j + 1); }
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            // Only the materials with something on screen get a submesh: ray tracing can't build an empty one
            bool changed = false;
            int used = 0;
            for (int m = 0; m < tris.Count; m++)
            {
                if (tris[m].Count == 0) continue;
                if (used >= drawn.Count || drawn[used] != materials[m]) changed = true;
                used++;
            }
            changed |= used != drawn.Count;
            if (changed)
            {
                drawn.Clear();
                for (int m = 0; m < tris.Count; m++) if (tris[m].Count > 0) drawn.Add(materials[m]);
            }
            mesh.subMeshCount = Mathf.Max(1, used);
            for (int m = 0, sub = 0; m < tris.Count; m++) if (tris[m].Count > 0) mesh.SetTriangles(tris[m], sub++, false);
            mesh.RecalculateBounds();
            if (changed && used > 0) own.sharedMaterials = drawn.ToArray();
            // Off while there's nothing in it (all off screen)
            own.enabled = used > 0 && !(isPlayer && ViewCamera.CraftHidden);
        }

        // _DRAW_POLY's step for one row of one face, in the original's pixels
        int Step(int p, int row, int width, long tick, int face)
        {
            if (p == 0) return 0;
            if (p < 0) return (OriginalMath.Cos[((row - 4 * p - (p >> 4)) * 0x20) & 0xFF] * p) >> 8;
            int step = (OriginalMath.Cos[((row + 4 * p) * 0x20) & 0xFF] * (p + (width >> 3))) >> 8;
            // _RAND, new each time the original draws: here once a tick, the same however fast the frames
            int rand = Hash(seed, face, row, (int)tick) & 0x7FFF;
            return step + (int)((long)rand * step / 0x8000) - (step >> 1);
        }

        static int Hash(int a, int b, int c, int d)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h = (h ^ (uint)d) * 16777619u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return (int)h;
            }
        }

        static float Row(Vector4 clip, float rows) => (1f - clip.y / clip.w) * 0.5f * rows;

        // Row r's line as a plane in clip space: positive above it (rows below r). With w > 0,
        // r - Row(clip) >= 0 is w (r - rows / 2) + y rows / 2 >= 0, which is linear, so cuts land exactly.
        static Vector4 RowLine(float r, float rows) => new Vector4(0f, rows * 0.5f, 0f, r - rows * 0.5f);

        // Wholly off one side of the view
        static bool Outside(Vector4 a, Vector4 b, Vector4 c)
        {
            const float Slack = 1.2f;
            if (a.x > a.w * Slack && b.x > b.w * Slack && c.x > c.w * Slack) return true;
            if (a.x < -a.w * Slack && b.x < -b.w * Slack && c.x < -c.w * Slack) return true;
            if (a.y > a.w * Slack && b.y > b.w * Slack && c.y > c.w * Slack) return true;
            if (a.y < -a.w * Slack && b.y < -b.w * Slack && c.y < -c.w * Slack) return true;
            return a.w <= 0f && b.w <= 0f && c.w <= 0f;
        }

        static bool Before(Vector3 a, Vector3 b) => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

        // Cuts a convex polygon along a plane in clip space: above gets the side >= 0, below the rest. Clip
        // space and world space are linear in each other, so both interpolate along the edge alike.
        static void Split(List<Corner> poly, Vector4 plane, float offset, List<Corner> above, List<Corner> below, bool keepAboveInPoly = false)
        {
            above.Clear();
            below.Clear();
            int count = poly.Count;
            for (int i = 0; i < count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % count];
                float sa = Vector4.Dot(plane, a.clip) + offset, sb = Vector4.Dot(plane, b.clip) + offset;
                if (sa >= 0f) above.Add(a); else below.Add(a);
                if ((sa >= 0f) != (sb >= 0f))
                {
                    // Always from the same end of the edge, so a neighbouring face (or the other side of this
                    // one) cutting the same edge gets exactly the same point: no hairline cracks or z-fighting
                    var from = a; var to = b; float sf = sa, st = sb;
                    if (Before(b.world, a.world)) { from = b; to = a; sf = sb; st = sa; }
                    float t = sf / (sf - st);
                    var cut = new Corner { world = Vector3.Lerp(from.world, to.world, t), clip = Vector4.Lerp(from.clip, to.clip, t), uv = Vector2.Lerp(from.uv, to.uv, t) };
                    above.Add(cut);
                    below.Add(cut);
                }
            }
            if (keepAboveInPoly)
            {
                poly.Clear();
                poly.AddRange(above);
            }
        }
    }
}
