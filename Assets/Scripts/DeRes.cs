using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
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
    // The original got the slide for nothing, filling its own spans row by row. Here every band is real
    // geometry, cut again each frame, so the cutting runs as a Burst job per object, all of them at once
    // on the worker threads (BuildJob), and DeResRunner collects them before the frame is drawn.
    [DefaultExecutionOrder(10000)]   // after the camera has moved this frame: the rows are the screen's
    public class DeRes : MonoBehaviour
    {
        const float ReferenceRows = 530f;   // the reference window's height (_TY: 290 of 530 rows down)
        const float BehindStill = 0.003f;   // how far back the ripple sits, as a share of its distance

        class Part
        {
            public Transform transform;
            public Renderer renderer;
            public Material material;
        }

        // A mesh's corners, triangles and faces, worked out once and kept: a hit seen from the cockpit
        // starts a ripple on every shape in view at once, and doing this each time stalled the frame
        class Prepared
        {
            public Vector3[] verts;
            public Vector2[] uvs;
            public int[] tris;
            public int[] faceOf;
        }

        // Keyed by the mesh, and by the filter too where its siblings' movies cut faces out of it
        static readonly Dictionary<(MeshFilter, Mesh, bool), Prepared> prepared = new Dictionary<(MeshFilter, Mesh, bool), Prepared>();

        // At a level's start: everything a cockpit hit ripples, prepared ahead
        public static void Warm(GameManager gm)
        {
            foreach (var o in gm.Obstacles) if (o != null) WarmOne(o);
            foreach (var f in gm.Flags) if (f != null) WarmOne(f.gameObject);
            foreach (var d in gm.Dumps) if (d != null) WarmOne(d.gameObject);
            foreach (var p in gm.JumpPads) if (p != null) WarmOne(p.gameObject);
            foreach (var r in gm.Robots) if (r != null) WarmOne(r.gameObject);
        }

        static void WarmOne(GameObject go)
        {
            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
            {
                bool rippled = f.name == "Panels" || f.name == "Tile" || f.name == "Textured" || f.name == "Flat" || f.name == "Movie";
                if (rippled && f.sharedMesh != null && f.sharedMesh.isReadable) Prepare(f);
            }
        }

        static Prepared Prepare(MeshFilter f)
        {
            bool dropMovies = f.name == "Panels" && GameSettings.DisplayFilled && GameSettings.DisplayMovies && f.transform.parent != null;
            var key = (dropMovies ? f : null, f.sharedMesh, dropMovies);
            if (prepared.TryGetValue(key, out var p)) return p;
            if (prepared.Count >= 1024)
            {
                // Drop what belonged to levels gone by
                var gone = new List<(MeshFilter, Mesh, bool)>();
                foreach (var k in prepared.Keys) if (k.Item2 == null || (k.Item3 && k.Item1 == null)) gone.Add(k);
                foreach (var k in gone) prepared.Remove(k);
            }
            var verts = f.sharedMesh.vertices;
            var tris = WithoutMovieFaces(f);
            p = new Prepared { verts = verts, uvs = f.sharedMesh.uv, tris = tris, faceOf = Faces(verts, tris) };
            prepared[key] = p;
            return p;
        }

        static readonly Dictionary<GameObject, DeRes> playing = new Dictionary<GameObject, DeRes>();   // one at a time on a craft

        GameObject source;
        readonly List<Part> parts = new List<Part>();
        readonly List<Material> materials = new List<Material>();
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
            var preps = new List<(Prepared prep, Vector2? uv, int sub)>();
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
                var part = new Part { transform = f.transform, renderer = r };
                // Plain faces in the palette's material, textures and movies in their own (a movie's plays on)
                bool picture = f.name == "Textured" || f.name == "Movie";
                part.material = picture && r != null && r.sharedMaterial != null ? r.sharedMaterial : gm.obstacleLook.shapes;
                if (!d.materials.Contains(part.material)) d.materials.Add(part.material);
                // One colour for the whole part (a dump's ramp)
                Vector2? uv = dump != null ? SpectrePalette.Uv(SpectrePalette.Entry(dump.Ramp)) : (Vector2?)null;
                preps.Add((Prepare(f), uv, d.materials.IndexOf(part.material)));
                d.parts.Add(part);
                if (returns) r.enabled = false;
            }
            d.mesh = new Mesh { name = "DeRes", indexFormat = IndexFormat.UInt32 };
            d.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = d.mesh;
            d.isPlayer = source.GetComponent<PlayerTank>() != null;
            d.wreck = d.isPlayer && !returns;
            d.own = go.AddComponent<MeshRenderer>();
            if (d.materials.Count == 0) d.materials.Add(gm.obstacleLook.shapes);
            d.own.sharedMaterials = d.materials.ToArray();
            // The source's own shadow, and in the ray-traced reflections, rebuilt every frame
            d.own.shadowCastingMode = d.parts.Count > 0 && d.parts[0].renderer != null ? d.parts[0].renderer.shadowCastingMode : ShadowCastingMode.Off;
            d.own.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.DynamicGeometry;
            d.own.enabled = false;   // until it has something to draw: an empty mesh crashes the ray tracing build
            d.Allocate(preps);
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
        static int[] Faces(Vector3[] v, int[] t)
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
                if (!seen.TryGetValue(key, out int id)) seen[key] = id = seen.Count;
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
            Dispose();
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

        void OnDestroy() => Dispose();

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
            Schedule(amount, SimClock.Ticks);
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

        // The source's faces, gathered from all its parts into one set for the job
        NativeArray<Vector3> srcVerts;
        NativeArray<Vector2> srcUvs;
        NativeArray<int> srcTris;         // into its own part's run of srcVerts
        NativeArray<int> srcFaces;        // per triangle: its face within its part
        NativeArray<int4> partInfo;       // per part: its run of srcTris, where its corners start, its first face
        NativeArray<float3> partUv;       // per part: one uv for every corner (z = 1), or each its own (z = 0)
        NativeArray<int> partSub;         // per part: its material's index in materials
        NativeArray<float4x4> partMatrices;
        NativeArray<byte> partAlive;
        NativeArray<int> cosTable;
        // What the job makes, and its working space
        NativeList<Corner> bandCorners;
        NativeList<Band> bands;
        NativeHashMap<long, float2> rowExtent;
        NativeList<float3> outVerts, outNormals;
        NativeList<float2> outUvs;
        NativeList<int> outTris;
        NativeArray<int2> subRanges;      // per material: its run of outTris
        NativeArray<float3> outBounds;
        bool allocated, scheduled;
        JobHandle handle;

        void Allocate(List<(Prepared prep, Vector2? uv, int sub)> preps)
        {
            int nv = 0, nt = 0;
            foreach (var x in preps) { nv += x.prep.verts.Length; nt += x.prep.tris.Length; }
            const NativeArrayOptions Raw = NativeArrayOptions.UninitializedMemory;
            srcVerts = new NativeArray<Vector3>(nv, Allocator.Persistent, Raw);
            srcUvs = new NativeArray<Vector2>(nv, Allocator.Persistent, Raw);
            srcTris = new NativeArray<int>(nt, Allocator.Persistent, Raw);
            srcFaces = new NativeArray<int>(nt / 3, Allocator.Persistent, Raw);
            partInfo = new NativeArray<int4>(preps.Count, Allocator.Persistent);
            partUv = new NativeArray<float3>(preps.Count, Allocator.Persistent);
            partSub = new NativeArray<int>(preps.Count, Allocator.Persistent);
            partMatrices = new NativeArray<float4x4>(preps.Count, Allocator.Persistent);
            partAlive = new NativeArray<byte>(preps.Count, Allocator.Persistent);
            // Whole arrays copied as they are (one copy each, not corner by corner): the job adds each part's
            // offsets as it reads them
            int vo = 0, to = 0;
            for (int k = 0; k < preps.Count; k++)
            {
                var (prep, uv, sub) = preps[k];
                NativeArray<Vector3>.Copy(prep.verts, 0, srcVerts, vo, prep.verts.Length);
                bool ownUvs = uv == null && prep.uvs.Length == prep.verts.Length;
                if (ownUvs) NativeArray<Vector2>.Copy(prep.uvs, 0, srcUvs, vo, prep.uvs.Length);
                NativeArray<int>.Copy(prep.tris, 0, srcTris, to, prep.tris.Length);
                NativeArray<int>.Copy(prep.faceOf, 0, srcFaces, to / 3, prep.faceOf.Length);
                // Faces of different parts never match
                partInfo[k] = new int4(to, to + prep.tris.Length, vo, k * 4096);
                var one = uv ?? Vector2.zero;
                partUv[k] = new float3(one.x, one.y, ownUvs ? 0f : 1f);
                partSub[k] = sub;
                vo += prep.verts.Length;
                to += prep.tris.Length;
            }
            cosTable = new NativeArray<int>(OriginalMath.Cos, Allocator.Persistent);
            bandCorners = new NativeList<Corner>(1024, Allocator.Persistent);
            bands = new NativeList<Band>(256, Allocator.Persistent);
            rowExtent = new NativeHashMap<long, float2>(256, Allocator.Persistent);
            outVerts = new NativeList<float3>(1024, Allocator.Persistent);
            outNormals = new NativeList<float3>(1024, Allocator.Persistent);
            outUvs = new NativeList<float2>(1024, Allocator.Persistent);
            outTris = new NativeList<int>(2048, Allocator.Persistent);
            subRanges = new NativeArray<int2>(materials.Count, Allocator.Persistent);
            outBounds = new NativeArray<float3>(2, Allocator.Persistent);
            allocated = true;
            DeResRunner.Ensure();
        }

        void Dispose()
        {
            if (!allocated) return;
            if (scheduled) { handle.Complete(); scheduled = false; }
            DeResRunner.Remove(this);
            srcVerts.Dispose(); srcUvs.Dispose(); srcTris.Dispose(); srcFaces.Dispose();
            partInfo.Dispose(); partUv.Dispose(); partSub.Dispose(); partMatrices.Dispose(); partAlive.Dispose(); cosTable.Dispose();
            bandCorners.Dispose(); bands.Dispose(); rowExtent.Dispose();
            outVerts.Dispose(); outNormals.Dispose(); outUvs.Dispose(); outTris.Dispose();
            subRanges.Dispose(); outBounds.Dispose();
            allocated = false;
        }

        // Starts this frame's cut on a worker thread; DeResRunner finishes it after every DeRes has started
        // _P_TO_EYE's test: a point's depth, doubled outside the overhead view, below 1 unit
        bool AnyPointBehindEye(Camera cam)
        {
            Vector3 eye = cam.transform.position, ahead = cam.transform.forward;
            float limit = 0.5f * OriginalMath.UnitsToMetres;
            for (int k = 0; k < parts.Count; k++)
            {
                var t = parts[k].transform;
                if (t == null) continue;
                var m = t.localToWorldMatrix;
                int start = partInfo[k].z, end = k + 1 < parts.Count ? partInfo[k + 1].z : srcVerts.Length;
                for (int i = start; i < end; i++)
                    if (Vector3.Dot(m.MultiplyPoint3x4(srcVerts[i]) - eye, ahead) < limit) return true;
            }
            return false;
        }

        void Schedule(int p, long tick)
        {
            if (!allocated) return;
            if (scheduled) Finish();   // last frame's, if the runner missed it
            // The craft isn't drawn from the cockpit, and it's the nearest thing there, so the most rows to cut
            if (isPlayer && ViewCamera.CraftHidden)
            {
                own.enabled = false;
                return;
            }
            var cam = Camera.main;
            if (cam == null) return;
            for (int k = 0; k < parts.Count; k++)
            {
                bool alive = parts[k].transform != null;
                partAlive[k] = (byte)(alive ? 1 : 0);
                partMatrices[k] = alive ? (float4x4)parts[k].transform.localToWorldMatrix : float4x4.identity;
            }
            // _P_TO_EYE drops an object with any point at or behind the eye. The swing round the wreck starts
            // with the eye inside the craft, so the original draws no wreck until the eye is clear of it. Drawn
            // from inside, the wreck filled the view right against the eye and stalled the GPU.
            if (wreck && AnyPointBehindEye(cam))
            {
                own.enabled = false;
                return;
            }
            // Rows of whole screen pixels, as near the reference window's 530 as the screen allows: rows of
            // 530ths would land as 2 and 3 pixels in turn and beat into stripes. Steps are in these rows'
            // pixels, as the original's are in its own.
            int rowPixels = Mathf.Max(1, Mathf.RoundToInt(cam.pixelHeight / ReferenceRows));
            var job = new BuildJob
            {
                verts = srcVerts.Reinterpret<float3>(), uvs = srcUvs.Reinterpret<float2>(), tris = srcTris, faces = srcFaces,
                partInfo = partInfo, partUv = partUv, partSub = partSub, matrices = partMatrices, alive = partAlive, cos = cosTable,
                toClip = cam.projectionMatrix * cam.worldToCameraMatrix,
                near = cam.nearClipPlane,
                rows = (float)cam.pixelHeight / rowPixels,
                columns = (float)cam.pixelWidth / rowPixels,
                overlap = 0.5f / rowPixels,
                p00 = cam.projectionMatrix.m00,
                right = cam.transform.right,
                eye = cam.transform.position,
                p = p, tick = (int)tick, seed = seed,
                bandCorners = bandCorners, bands = bands, rowExtent = rowExtent,
                outVerts = outVerts, outNormals = outNormals, outUvs = outUvs, outTris = outTris,
                subRanges = subRanges, bounds = outBounds,
            };
            handle = job.Schedule();
            scheduled = true;
            DeResRunner.Add(this);
        }

        // Puts the job's bands in the mesh
        internal void Finish()
        {
            if (!scheduled) return;
            handle.Complete();
            scheduled = false;

            mesh.Clear();
            int nv = outVerts.Length;
            // Only the materials with something on screen get a submesh: ray tracing can't build an empty one
            bool changed = false;
            int used = 0;
            for (int s = 0; s < materials.Count; s++)
            {
                if (subRanges[s].y == 0) continue;
                if (used >= drawn.Count || drawn[used] != materials[s]) changed = true;
                used++;
            }
            changed |= used != drawn.Count;
            if (changed)
            {
                drawn.Clear();
                for (int s = 0; s < materials.Count; s++) if (subRanges[s].y > 0) drawn.Add(materials[s]);
            }
            if (used > 0)
            {
                const MeshUpdateFlags Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
                mesh.SetVertices(outVerts.AsArray());
                mesh.SetNormals(outNormals.AsArray());
                mesh.SetUVs(0, outUvs.AsArray());
                mesh.SetIndexBufferParams(outTris.Length, IndexFormat.UInt32);
                mesh.SetIndexBufferData(outTris.AsArray(), 0, 0, outTris.Length, Flags);
                var bounds = new Bounds();
                bounds.SetMinMax(outBounds[0], outBounds[1]);
                mesh.subMeshCount = used;
                for (int s = 0, sub = 0; s < materials.Count; s++)
                {
                    var r = subRanges[s];
                    if (r.y == 0) continue;
                    mesh.SetSubMesh(sub++, new SubMeshDescriptor(r.x, r.y) { bounds = bounds, firstVertex = 0, vertexCount = nv }, Flags);
                }
                mesh.bounds = bounds;
            }
            else mesh.subMeshCount = 1;
            if (changed && used > 0) own.sharedMaterials = drawn.ToArray();
            // Off while there's nothing in it (all off screen)
            own.enabled = used > 0 && !(isPlayer && ViewCamera.CraftHidden);
        }

        // One band of a face: its corners in world space and clip space, the row it's on, its face
        internal struct Corner { public float3 world; public float4 clip; public float2 uv; }
        internal struct Band { public int start, count, row, face, sub, vstart; public float3 normal; }

        [BurstCompile(CompileSynchronously = true)]
        struct BuildJob : IJob
        {
            [ReadOnly] public NativeArray<float3> verts;
            [ReadOnly] public NativeArray<float2> uvs;
            [ReadOnly] public NativeArray<int> tris;
            [ReadOnly] public NativeArray<int> faces;
            [ReadOnly] public NativeArray<int4> partInfo;
            [ReadOnly] public NativeArray<float3> partUv;
            [ReadOnly] public NativeArray<int> partSub;
            [ReadOnly] public NativeArray<float4x4> matrices;
            [ReadOnly] public NativeArray<byte> alive;
            [ReadOnly] public NativeArray<int> cos;
            public float4x4 toClip;
            public float near, rows, columns, overlap, p00;
            public float3 right, eye;
            public int p, tick, seed;
            public NativeList<Corner> bandCorners;
            public NativeList<Band> bands;
            public NativeHashMap<long, float2> rowExtent;
            public NativeList<float3> outVerts, outNormals;
            public NativeList<float2> outUvs;
            public NativeList<int> outTris;
            public NativeArray<int2> subRanges;
            public NativeArray<float3> bounds;

            // A triangle gains at most two corners at the eye and two at each row's cut: these never fill
            const int Cap = 32;

            public void Execute()
            {
                bandCorners.Clear();
                bands.Clear();
                rowExtent.Clear();
                outVerts.Clear();
                outNormals.Clear();
                outUvs.Clear();
                outTris.Clear();
                // Only a dying object's steps depend on how wide its face's row is
                bool widths = p > 0;
                var eyePlane = new float4(0f, 0f, 0f, 1f);
                var s = new NativeArray<Corner>(Cap * 4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                int cur = 0, up = Cap, low = Cap * 2, bnd = Cap * 3;

                for (int k = 0; k < partInfo.Length; k++)
                {
                    if (alive[k] == 0) continue;
                    float4x4 m = matrices[k];
                    int sub = partSub[k];
                    int4 info = partInfo[k];
                    float3 pu = partUv[k];
                    bool oneUv = pu.z != 0f;
                    for (int i = info.x; i + 2 < info.y; i += 3)
                    {
                        int ia = tris[i] + info.z, ib = tris[i + 1] + info.z, ic = tris[i + 2] + info.z;
                        float3 a = math.transform(m, verts[ia]), b = math.transform(m, verts[ib]), c = math.transform(m, verts[ic]);
                        float4 ca = math.mul(toClip, new float4(a, 1f)), cb = math.mul(toClip, new float4(b, 1f)), cc = math.mul(toClip, new float4(c, 1f));
                        if (Outside(ca, cb, cc)) continue;

                        // In front of the eye only
                        s[cur] = new Corner { world = a, clip = ca, uv = oneUv ? pu.xy : uvs[ia] };
                        s[cur + 1] = new Corner { world = b, clip = cb, uv = oneUv ? pu.xy : uvs[ib] };
                        s[cur + 2] = new Corner { world = c, clip = cc, uv = oneUv ? pu.xy : uvs[ic] };
                        int n = 3;
                        if (ca.w < near || cb.w < near || cc.w < near)
                        {
                            Split(s, cur, n, eyePlane, -near, up, out n, -1, out _);
                            if (n < 3) continue;
                            Swap(ref cur, ref up);
                        }

                        // Rows from the top of the window
                        float top = float.MaxValue, bottom = float.MinValue;
                        for (int j = 0; j < n; j++)
                        {
                            float r = Row(s[cur + j].clip);
                            top = math.min(top, r);
                            bottom = math.max(bottom, r);
                        }
                        int first = math.max((int)math.floor(top), -1);
                        int last = math.min((int)math.ceil(bottom) - 1, (int)rows);
                        if (first > last) continue;
                        if (first > (int)math.floor(top))
                        {
                            Split(s, cur, n, -RowLine(first), 0f, up, out n, -1, out _);
                            Swap(ref cur, ref up);
                        }

                        float3 normal = math.normalizesafe(math.cross(b - a, c - a));
                        int face = info.w + faces[i / 3];
                        for (int row = first; row <= last && n >= 3; row++)
                        {
                            // The band above the next row's line (reaching half a pixel past it, so the seam
                            // between rows always closes under TAA's sub-pixel jitter), and what's left below
                            Split(s, cur, n, RowLine(row + 1 + overlap), 0f, bnd, out int nb, -1, out _);
                            int nl = 0;
                            if (row < last) Split(s, cur, n, RowLine(row + 1), 0f, -1, out _, low, out nl);
                            if (nb >= 3)
                            {
                                int start = bandCorners.Length;
                                float min = float.MaxValue, max = float.MinValue;
                                for (int j = 0; j < nb; j++)
                                {
                                    var kc = s[bnd + j];
                                    bandCorners.Add(kc);
                                    if (widths)
                                    {
                                        float x = (kc.clip.x / kc.clip.w + 1f) * 0.5f * columns;
                                        min = math.min(min, x);
                                        max = math.max(max, x);
                                    }
                                }
                                bands.Add(new Band { start = start, count = nb, row = row, face = face, sub = sub, normal = normal });
                                if (widths)
                                {
                                    long key = ExtentKey(face, row);
                                    if (rowExtent.TryGetValue(key, out var e)) rowExtent[key] = new float2(math.min(e.x, min), math.max(e.y, max));
                                    else rowExtent.Add(key, new float2(min, max));
                                }
                            }
                            Swap(ref cur, ref low);
                            n = nl;
                        }
                    }
                }

                float3 bmin = new float3(float.MaxValue), bmax = new float3(float.MinValue);
                for (int bi = 0; bi < bands.Length; bi++)
                {
                    var band = bands[bi];
                    int width = 0;
                    if (widths && rowExtent.TryGetValue(ExtentKey(band.face, band.row), out var e)) width = (int)math.round(e.y - e.x);
                    int step = Step(band.row, width, band.face);
                    // Pixels across into the eye's x at each corner's depth, so the whole band moves the same
                    // number of pixels and stays on its row
                    float ndc = step * 2f / columns;
                    band.vstart = outVerts.Length;
                    bands[bi] = band;
                    for (int j = 0; j < band.count; j++)
                    {
                        var kc = bandCorners[band.start + j];
                        // Pushed back along its own line of sight, so it stays on the same pixels but loses to
                        // any still face in its plane, which would otherwise fight the rows sliding across it
                        float3 moved = kc.world + right * (ndc * kc.clip.w / p00);
                        float3 v = eye + (moved - eye) * (1f + BehindStill);
                        outVerts.Add(v);
                        outNormals.Add(band.normal);
                        outUvs.Add(kc.uv);
                        bmin = math.min(bmin, v);
                        bmax = math.max(bmax, v);
                    }
                }
                // Each material's triangles together, in the order of the materials
                for (int m = 0; m < subRanges.Length; m++)
                {
                    int start = outTris.Length;
                    for (int bi = 0; bi < bands.Length; bi++)
                    {
                        var band = bands[bi];
                        if (band.sub != m) continue;
                        for (int j = 1; j + 1 < band.count; j++) { outTris.Add(band.vstart); outTris.Add(band.vstart + j); outTris.Add(band.vstart + j + 1); }
                    }
                    subRanges[m] = new int2(start, outTris.Length - start);
                }
                bounds[0] = bmin;
                bounds[1] = bmax;
            }

            static long ExtentKey(int face, int row) => ((long)face << 20) | (uint)(row + 2);

            static void Swap(ref int a, ref int b) { int t = a; a = b; b = t; }

            // _DRAW_POLY's step for one row of one face, in the original's pixels
            int Step(int row, int width, int face)
            {
                if (p == 0) return 0;
                if (p < 0) return (cos[((row - 4 * p - (p >> 4)) * 0x20) & 0xFF] * p) >> 8;
                int step = (cos[((row + 4 * p) * 0x20) & 0xFF] * (p + (width >> 3))) >> 8;
                // _RAND, new each time the original draws: here once a tick, the same however fast the frames
                int rand = Hash(seed, face, row, tick) & 0x7FFF;
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

            float Row(float4 clip) => (1f - clip.y / clip.w) * 0.5f * rows;

            // Row r's line as a plane in clip space: positive above it (rows below r). With w > 0,
            // r - Row(clip) >= 0 is w (r - rows / 2) + y rows / 2 >= 0, which is linear, so cuts land exactly.
            float4 RowLine(float r) => new float4(0f, rows * 0.5f, 0f, r - rows * 0.5f);

            // Wholly off one side of the view
            static bool Outside(float4 a, float4 b, float4 c)
            {
                const float Slack = 1.2f;
                if (a.x > a.w * Slack && b.x > b.w * Slack && c.x > c.w * Slack) return true;
                if (a.x < -a.w * Slack && b.x < -b.w * Slack && c.x < -c.w * Slack) return true;
                if (a.y > a.w * Slack && b.y > b.w * Slack && c.y > c.w * Slack) return true;
                if (a.y < -a.w * Slack && b.y < -b.w * Slack && c.y < -c.w * Slack) return true;
                return a.w <= 0f && b.w <= 0f && c.w <= 0f;
            }

            static bool Before(float3 a, float3 b) => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

            static float Side(float4 plane, float4 clip, float offset) => plane.x * clip.x + plane.y * clip.y + plane.z * clip.z + plane.w * clip.w + offset;

            // Cuts a convex polygon (count corners from src in s) along a plane in clip space: the side >= 0
            // goes to above, the rest to below (each a start in s, or -1 when it isn't wanted). Clip space and
            // world space are linear in each other, so both interpolate along the edge alike.
            static void Split(NativeArray<Corner> s, int src, int count, float4 plane, float offset, int above, out int na, int below, out int nb)
            {
                na = nb = 0;
                float s0 = Side(plane, s[src].clip, offset);
                float sa = s0;
                for (int i = 0; i < count; i++)
                {
                    var a = s[src + i];
                    var b = s[src + (i + 1 < count ? i + 1 : 0)];
                    float sb = i + 1 < count ? Side(plane, b.clip, offset) : s0;
                    if (sa >= 0f) { if (above >= 0) s[above + na++] = a; }
                    else if (below >= 0) s[below + nb++] = a;
                    if ((sa >= 0f) != (sb >= 0f))
                    {
                        // Always from the same end of the edge, so a neighbouring face (or the other side of
                        // this one) cutting the same edge gets exactly the same point: no hairline cracks
                        var from = a; var to = b; float sf = sa, st = sb;
                        if (Before(b.world, a.world)) { from = b; to = a; sf = sb; st = sa; }
                        float t = math.saturate(sf / (sf - st));
                        var cut = new Corner { world = math.lerp(from.world, to.world, t), clip = math.lerp(from.clip, to.clip, t), uv = math.lerp(from.uv, to.uv, t) };
                        if (above >= 0) s[above + na++] = cut;
                        if (below >= 0) s[below + nb++] = cut;
                    }
                    sa = sb;
                }
            }
        }
    }

    // Waits for every DeRes's job of the frame, after they've all started, and puts the results in the meshes
    [DefaultExecutionOrder(10001)]
    public class DeResRunner : MonoBehaviour
    {
        static DeResRunner instance;
        static readonly List<DeRes> pending = new List<DeRes>();

        internal static void Ensure()
        {
            if (instance == null) instance = new GameObject("DeRes Runner").AddComponent<DeResRunner>();
        }

        internal static void Add(DeRes d)
        {
            Ensure();
            pending.Add(d);
        }

        internal static void Remove(DeRes d) => pending.Remove(d);

        void LateUpdate()
        {
            JobHandle.ScheduleBatchedJobs();
            // Finish leaves the list alone
            for (int i = 0; i < pending.Count; i++) pending[i].Finish();
            pending.Clear();
        }
    }
}
