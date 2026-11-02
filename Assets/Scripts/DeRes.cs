using System.Collections.Generic;
using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // The original's de-res (_DRAW_POLY's second argument): a shape drawn as horizontal lines, each slid
    // sideways along a sine wave by its amount plus an eighth of the line's width, with some jitter.
    // Dying objects go up to their peak; a craft that has just been hurt goes there and back.
    public class DeRes : MonoBehaviour
    {
        const float LineSpacing = 0.05f;
        const float LineHeight = 0.04f;
        const float MetresPerAmount = 0.05f;

        class Part
        {
            public Transform transform;
            public Renderer renderer;
            public Vector3[] verts;
            public Vector2[] uvs;
            public int[] tris;
            public Vector2? uv;   // one colour for the whole part, for textured faces
        }

        static readonly HashSet<GameObject> playing = new HashSet<GameObject>();   // one at a time on a craft

        GameObject source;
        readonly List<Part> parts = new List<Part>();
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();
        Mesh mesh;
        MeshRenderer own;
        bool isPlayer;
        float startTick;
        int ticks;
        int peak;
        bool returns;

        // The cockpit ripple running now, for anything that appears during it
        static float rippleStart = float.NegativeInfinity;
        static int rippleTicks;
        static int ripplePeak;

        // Ticks of SimClock, drawn between them
        static float Now => SimClock.Ticks + SimClock.TickFraction;

        public static void Play(GameObject source, int peak, int ticks, bool returns, float elapsed = 0f)
        {
            var gm = GameManager.Instance;
            if (gm == null || !playing.Add(source)) return;
            var go = new GameObject("DeRes");
            var d = go.AddComponent<DeRes>();
            d.peak = peak;
            d.ticks = ticks;
            d.returns = returns;
            d.source = source;
            d.startTick = Now - elapsed;
            var dump = source.GetComponent<AmmoDump>();
            foreach (var f in source.GetComponentsInChildren<MeshFilter>(true))
            {
                bool shown = f.name == "Panels" || f.name == (GameSettings.DisplayTextures ? "Textured" : "Flat") || (dump != null && f.name == "Tile");
                if (!shown || f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
                var r = f.GetComponent<Renderer>();
                if (returns && !r.enabled) continue;   // a cloaked robot stays that way
                go.layer = f.gameObject.layer;
                d.parts.Add(new Part
                {
                    transform = f.transform,
                    renderer = r,
                    verts = f.sharedMesh.vertices,
                    uvs = f.sharedMesh.uv,
                    tris = f.sharedMesh.triangles,
                    uv = dump != null ? SpectrePalette.Uv(SpectrePalette.Entry(dump.Ramp))
                        : r.sharedMaterial == gm.obstacleLook.shapes ? (Vector2?)null : SpectrePalette.Uv(SpectrePalette.Entry(0xB0)),
                });
                if (returns) r.enabled = false;
            }
            d.mesh = new Mesh { name = "DeRes" };
            d.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = d.mesh;
            d.isPlayer = source.GetComponent<PlayerTank>() != null;
            d.own = go.AddComponent<MeshRenderer>();
            d.own.sharedMaterial = gm.obstacleLook.shapes;
            d.own.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (d.isPlayer && returns && ViewCamera.CraftHidden) PlayWorld(gm, peak, ticks);
        }

        // _DRAW_OBJS: from the cockpit the player's de-res goes to every filled shape in the view instead
        static void PlayWorld(GameManager gm, int peak, int ticks)
        {
            if (!GameSettings.DisplayFilled) return;
            rippleStart = Now;
            rippleTicks = ticks;
            ripplePeak = peak;
            foreach (var o in gm.Obstacles) if (o != null) Play(o, peak, ticks, true);
            foreach (var f in gm.Flags) if (f != null) Play(f.gameObject, peak, ticks, true);
            foreach (var d in gm.Dumps) if (d != null) Play(d.gameObject, peak, ticks, true);
            foreach (var p in gm.JumpPads) if (p != null) Play(p.gameObject, peak, ticks, true);
            foreach (var r in gm.Robots) if (r != null && !r.IsDead) Play(r.gameObject, peak, ticks, true);
            foreach (var s in Object.FindObjectsByType<OrbitShield>(FindObjectsSortMode.None)) Play(s.gameObject, peak, ticks, true);
            gm.GetComponent<RobotDirector>().Shots.ForEachVisual(v => Play(v, peak, ticks, true));
        }

        // A shot fired mid-ripple joins it where it has got to
        public static void JoinRipple(GameObject source)
        {
            float elapsed = Now - rippleStart;
            if (elapsed < rippleTicks && ViewCamera.CraftHidden) Play(source, ripplePeak, rippleTicks, true, elapsed);
        }

        bool Dead
        {
            get
            {
                var health = source != null ? source.GetComponent<Health>() : null;
                return health != null && health.IsDead;
            }
        }

        void LateUpdate()
        {
            float age = Now - startTick;
            if (age >= ticks)
            {
                playing.Remove(source);
                if (returns)
                    foreach (var p in parts)
                        if (p.renderer != null && !Dead)
                        {
                            p.renderer.enabled = true;
                            GameManager.ApplyFilled(p.renderer);
                        }
                Destroy(mesh);
                Destroy(gameObject);
                return;
            }
            own.enabled = !(isPlayer && ViewCamera.CraftHidden);
            float t = age / ticks;
            Build(peak * (returns ? Mathf.Sin(t * Mathf.PI) : t));
        }

        void Build(float amount)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 right = cam.transform.right;
            right.y = 0f;
            right.Normalize();

            // Each line's extent across the view
            var lines = new Dictionary<int, (float min, float max, Vector2 uv)>();
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var p in parts)
            {
                if (p.transform == null) continue;
                var m = p.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < p.tris.Length; i += 6)
                {
                    Vector3 a = m.MultiplyPoint3x4(p.verts[p.tris[i]]), b = m.MultiplyPoint3x4(p.verts[p.tris[i + 1]]), c = m.MultiplyPoint3x4(p.verts[p.tris[i + 2]]);
                    var uv = p.uv ?? (p.uvs.Length > 0 ? p.uvs[p.tris[i]] : Vector2.zero);
                    sum += a + b + c;
                    count += 3;
                    float low = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), high = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    for (int row = Mathf.CeilToInt(low / LineSpacing); row * LineSpacing <= high; row++)
                    {
                        float y = row * LineSpacing, min = float.MaxValue, max = float.MinValue;
                        Cross(a, b, y, right, ref min, ref max);
                        Cross(b, c, y, right, ref min, ref max);
                        Cross(c, a, y, right, ref min, ref max);
                        if (min > max) continue;
                        if (lines.TryGetValue(row, out var l)) lines[row] = (Mathf.Min(l.min, min), Mathf.Max(l.max, max), l.uv);
                        else lines[row] = (min, max, uv);
                    }
                }
            }

            verts.Clear();
            uvs.Clear();
            tris.Clear();
            if (count > 0)
            {
                Vector3 centre = sum / count;
                float across = Vector3.Dot(centre, right);
                foreach (var line in lines)
                {
                    float width = line.Value.max - line.Value.min;
                    float wave = Mathf.Sin((line.Key * 32f + amount * 128f) * Mathf.PI * 2f / 256f);
                    float slide = wave * (amount * MetresPerAmount + width / 8f) * Random.Range(0.5f, 1.5f);
                    Vector3 origin = centre + right * (slide - across) + Vector3.up * (line.Key * LineSpacing - centre.y);
                    AddLine(origin + right * line.Value.min, origin + right * line.Value.max, line.Value.uv);
                }
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        static void Cross(Vector3 a, Vector3 b, float y, Vector3 right, ref float min, ref float max)
        {
            if ((a.y < y) == (b.y < y)) return;
            float x = Vector3.Dot(Vector3.Lerp(a, b, (y - a.y) / (b.y - a.y)), right);
            min = Mathf.Min(min, x);
            max = Mathf.Max(max, x);
        }

        // Front and back are separate faces: sharing corners would cancel their normals to nothing,
        // which HDRP's lighting turns into black patches
        void AddLine(Vector3 a, Vector3 b, Vector2 uv)
        {
            if ((b - a).sqrMagnitude < 1e-6f) return;
            Vector3 up = Vector3.up * LineHeight * 0.5f;
            AddFace(a - up, a + up, b + up, b - up, uv);
            AddFace(b - up, b + up, a + up, a - up, uv);
        }

        void AddFace(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector2 uv)
        {
            int n = verts.Count;
            verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
            for (int i = 0; i < 4; i++) uvs.Add(uv);
            tris.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }
    }
}
