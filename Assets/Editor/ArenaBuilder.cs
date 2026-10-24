using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace SpectreEX.EditorTools
{
    // Builds the prototype arena scene from scratch so the whole look is reproducible:
    // SpectreEX > Build Arena Scene. Re-running overwrites Assets/Scenes/Arena.unity.
    public static class ArenaBuilder
    {
        const string MaterialDir = "Assets/Materials";
        const string ScenePath = "Assets/Scenes/Arena.unity";
        const string VolumePath = "Assets/Settings/ArenaVolume.asset";

        public const float ArenaHalfSize = 120f;
        const float GridSpacing = 12f;
        const float WallHeight = 6f;

        static readonly Color Cyan = new Color(0.1f, 0.85f, 1f);
        static readonly Color Orange = new Color(1f, 0.35f, 0.05f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.2f);

        static Material floorMat, bodyMat, wallMat, gridMat, cyanMat, orangeMat, goldMat;
        static readonly List<Vector3> obstacleCentres = new List<Vector3>();

        [MenuItem("SpectreEX/Build Arena Scene")]
        public static void Build()
        {
            CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var arena = new GameObject("Arena").transform;
            BuildFloorAndGrid(arena);
            BuildWalls(arena);
            BuildObstacles(arena);
            BuildLightingAndVolume();

            var player = BuildTank("Player", new Vector3(0f, 0.1f, -20f), Quaternion.identity, cyanMat, isPlayer: true);
            var spawns = BuildEnemySpawns();
            BuildTank("Enemy 1", spawns[0].position, spawns[0].rotation, orangeMat, isPlayer: false);
            BuildTank("Enemy 2", spawns[2].position, spawns[2].rotation, orangeMat, isPlayer: false);
            BuildFlags();

            var gm = new GameObject("GameManager").AddComponent<GameManager>();
            gm.player = player.GetComponent<TankHealth>();
            gm.enemySpawns = spawns;

            BuildCamera(player.transform);

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("SpectreEX: arena scene built at " + ScenePath);
        }

        // ---------- materials ----------

        static void CreateMaterials()
        {
            System.IO.Directory.CreateDirectory(MaterialDir);
            floorMat = LitMaterial("Floor", new Color(0.01f, 0.012f, 0.018f), smoothness: 0.96f, metallic: 0.3f);
            bodyMat = LitMaterial("TankBody", new Color(0.02f, 0.025f, 0.035f), smoothness: 0.85f, metallic: 0.8f);
            wallMat = LitMaterial("Wall", new Color(0.015f, 0.018f, 0.025f), smoothness: 0.9f, metallic: 0.5f);
            gridMat = GlowMaterial("GlowGrid", Cyan, 10.5f);
            cyanMat = GlowMaterial("GlowCyan", Cyan, 12.5f);
            orangeMat = GlowMaterial("GlowOrange", Orange, 12.5f);
            goldMat = GlowMaterial("GlowGold", Gold, 12.5f);
            AssetDatabase.SaveAssets();
        }

        static Material GetOrCreate(string name)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        static Material LitMaterial(string name, Color baseColor, float smoothness, float metallic)
        {
            var mat = GetOrCreate(name);
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Emissive intensity is in EV100 so it stays consistent with the scene's fixed exposure.
        static Material GlowMaterial(string name, Color color, float ev100)
        {
            var mat = GetOrCreate(name);
            mat.SetColor("_BaseColor", Color.black);
            mat.SetFloat("_Smoothness", 0.5f);
            HDMaterial.SetUseEmissiveIntensity(mat, true);
            HDMaterial.SetEmissiveColor(mat, color);
            HDMaterial.SetEmissiveIntensity(mat, ev100, EmissiveIntensityUnit.EV100);
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------- geometry helpers ----------

        static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // Thin glowing strips along all 12 edges of a box: the core of the vector look.
        static void AddEdges(Transform parent, Vector3 center, Vector3 size, float thickness, Material mat)
        {
            var edges = new GameObject("Edges").transform;
            edges.SetParent(parent, false);
            Vector3 h = size * 0.5f;
            float t = thickness;
            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            {
                Box("EdgeX", edges, center + new Vector3(0f, a * h.y, b * h.z), new Vector3(size.x + t, t, t), mat, false);
                Box("EdgeY", edges, center + new Vector3(a * h.x, 0f, b * h.z), new Vector3(t, size.y + t, t), mat, false);
                Box("EdgeZ", edges, center + new Vector3(a * h.x, b * h.y, 0f), new Vector3(t, t, size.z + t), mat, false);
            }
        }

        static void NoShadows(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                if (r.sharedMaterial == gridMat || r.sharedMaterial == cyanMat || r.sharedMaterial == orangeMat || r.sharedMaterial == goldMat)
                    r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------- arena ----------

        static void BuildFloorAndGrid(Transform arena)
        {
            float size = ArenaHalfSize * 2f;
            Box("Floor", arena, new Vector3(0f, -0.5f, 0f), new Vector3(size, 1f, size), floorMat);

            var grid = new GameObject("Grid").transform;
            grid.SetParent(arena, false);
            int lines = Mathf.RoundToInt(size / GridSpacing);
            for (int i = 0; i <= lines; i++)
            {
                float p = -ArenaHalfSize + i * GridSpacing;
                Box("GridX", grid, new Vector3(0f, 0.01f, p), new Vector3(size, 0.02f, 0.12f), gridMat, false);
                Box("GridZ", grid, new Vector3(p, 0.01f, 0f), new Vector3(0.12f, 0.02f, size), gridMat, false);
            }
            NoShadows(grid.gameObject);
        }

        static void BuildWalls(Transform arena)
        {
            var walls = new GameObject("Walls").transform;
            walls.SetParent(arena, false);
            float len = ArenaHalfSize * 2f + 2f;
            var defs = new[]
            {
                (new Vector3(0f, WallHeight / 2f, ArenaHalfSize + 1f), new Vector3(len, WallHeight, 2f)),
                (new Vector3(0f, WallHeight / 2f, -ArenaHalfSize - 1f), new Vector3(len, WallHeight, 2f)),
                (new Vector3(ArenaHalfSize + 1f, WallHeight / 2f, 0f), new Vector3(2f, WallHeight, len)),
                (new Vector3(-ArenaHalfSize - 1f, WallHeight / 2f, 0f), new Vector3(2f, WallHeight, len)),
            };
            foreach (var (pos, size) in defs)
            {
                var w = new GameObject("Wall").transform;
                w.SetParent(walls, false);
                w.localPosition = pos;
                Box("Body", w, Vector3.zero, size, wallMat);
                AddEdges(w, Vector3.zero, size, 0.18f, cyanMat);
            }
            NoShadows(walls.gameObject);
        }

        static void BuildObstacles(Transform arena)
        {
            var root = new GameObject("Obstacles").transform;
            root.SetParent(arena, false);
            var rng = new System.Random(1991);
            var placed = obstacleCentres;
            placed.Clear();
            int attempts = 0;
            while (placed.Count < 16 && attempts++ < 500)
            {
                var pos = new Vector3(Range(rng, -95f, 95f), 0f, Range(rng, -95f, 95f));
                if (pos.magnitude < 30f) continue; // keep the player start clear
                bool crowded = false;
                foreach (var p in placed) if (Vector3.Distance(p, pos) < 26f) { crowded = true; break; }
                if (crowded) continue;
                placed.Add(pos);

                var size = new Vector3(Range(rng, 4f, 12f), Range(rng, 3f, 14f), Range(rng, 4f, 12f));
                var o = new GameObject("Block").transform;
                o.SetParent(root, false);
                o.localPosition = pos + Vector3.up * size.y / 2f;
                o.localRotation = Quaternion.Euler(0f, Range(rng, 0f, 90f), 0f);
                Box("Body", o, Vector3.zero, size, wallMat);
                AddEdges(o, Vector3.zero, size, 0.15f, cyanMat);
            }
            NoShadows(root.gameObject);
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        // ---------- actors ----------

        static GameObject BuildTank(string name, Vector3 pos, Quaternion rot, Material glow, bool isPlayer)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(pos, rot);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 50f;
            rb.linearDamping = 0f;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.75f, 0f);
            col.size = new Vector3(3f, 1.5f, 4.4f);
            var physMat = new PhysicsMaterial("TankNoFriction") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
            col.sharedMaterial = physMat;

            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);

            var hullSize = new Vector3(3f, 0.9f, 4.4f);
            var hullPos = new Vector3(0f, 0.55f, 0f);
            Box("Hull", model, hullPos, hullSize, bodyMat, false);
            AddEdges(model, hullPos, hullSize, 0.09f, glow);

            var turretSize = new Vector3(1.8f, 0.6f, 2f);
            var turretPos = new Vector3(0f, 1.3f, -0.3f);
            Box("Turret", model, turretPos, turretSize, bodyMat, false);
            AddEdges(model, turretPos, turretSize, 0.07f, glow);

            Box("Barrel", model, new Vector3(0f, 1.3f, 1.7f), new Vector3(0.22f, 0.22f, 2.4f), glow, false);
            NoShadows(model.gameObject);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0f, 1.3f, 3.2f);

            root.AddComponent<TankMotor>();
            var weapon = root.AddComponent<TankWeapon>();
            weapon.muzzle = muzzle;
            weapon.projectileMaterial = glow;
            var health = root.AddComponent<TankHealth>();
            health.isPlayer = isPlayer;
            health.derezMaterial = glow;

            if (isPlayer)
            {
                root.AddComponent<PlayerTankInput>();
            }
            else
            {
                weapon.cooldown = 1.4f;
                weapon.projectileSpeed = 55f;
                weapon.damage = 20f;
                health.maxShields = 75f;
                var ai = root.AddComponent<EnemyTankAI>();
                ai.arenaHalfSize = ArenaHalfSize;
            }
            return root;
        }

        static Transform[] BuildEnemySpawns()
        {
            var root = new GameObject("EnemySpawns").transform;
            float d = ArenaHalfSize - 15f;
            var corners = new[] { new Vector3(d, 0.1f, d), new Vector3(-d, 0.1f, d), new Vector3(-d, 0.1f, -d), new Vector3(d, 0.1f, -d) };
            var result = new Transform[corners.Length];
            for (int i = 0; i < corners.Length; i++)
            {
                var s = new GameObject($"Spawn {i + 1}").transform;
                s.SetParent(root, false);
                s.position = corners[i];
                s.rotation = Quaternion.LookRotation(-corners[i].normalized);
                result[i] = s;
            }
            return result;
        }

        static void BuildFlags()
        {
            var root = new GameObject("Flags").transform;
            var spots = new[]
            {
                new Vector3(0f, 0f, 60f), new Vector3(70f, 0f, 10f), new Vector3(-65f, 0f, -15f),
                new Vector3(45f, 0f, -75f), new Vector3(-50f, 0f, 80f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var f = new GameObject($"Flag {i + 1}");
                f.transform.SetParent(root, false);
                f.transform.position = ClearOfObstacles(spots[i]);

                var trigger = f.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = 3f;
                trigger.center = new Vector3(0f, 1.5f, 0f);

                var visual = new GameObject("Visual").transform;
                visual.SetParent(f.transform, false);
                visual.localPosition = new Vector3(0f, 2.2f, 0f);
                var gem = Box("Gem", visual, Vector3.zero, Vector3.one * 1.3f, goldMat, false);
                gem.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
                Box("Pole", f.transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.12f, 1.2f, 0.12f), goldMat, false);
                Box("Pad", f.transform, new Vector3(0f, 0.03f, 0f), new Vector3(3.5f, 0.04f, 3.5f), goldMat, false);
                NoShadows(f);

                f.AddComponent<Flag>().visual = visual;
            }

        }

        // Push a flag spot away from any obstacle centre it would sit inside or against.
        static Vector3 ClearOfObstacles(Vector3 spot)
        {
            foreach (var o in obstacleCentres)
            {
                Vector3 away = spot - o;
                away.y = 0f;
                if (away.magnitude < 14f)
                    spot = o + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 14f;
            }
            return spot;
        }

        // ---------- camera, lights, volume ----------

        static void BuildCamera(Transform player)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            cam.fieldOfView = 65f;
            camGo.AddComponent<AudioListener>();
            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Color.black;
            var chase = camGo.AddComponent<ChaseCamera>();
            chase.target = player;
            camGo.transform.position = player.position + player.rotation * chase.chaseOffset;
            camGo.transform.LookAt(player.position + player.forward * chase.lookAhead + Vector3.up * 1.2f);
        }

        static void BuildLightingAndVolume()
        {
            // Faint cool key light so hull shapes read; the neon does the rest through RTGI.
            var lightGo = new GameObject("Moonlight");
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.55f, 0.65f, 1f);
            light.intensity = 0.5f;
            light.shadows = LightShadows.Soft;
            var hdLight = lightGo.AddComponent<HDAdditionalLightData>();
            hdLight.useRayTracedShadows = true;

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            foreach (var c in profile.components.ToArray()) Object.DestroyImmediate(c, true);
            profile.components.Clear();

            var env = Add<VisualEnvironment>(profile);
            env.skyType.Override(0); // no sky: pure black void
            env.skyAmbientMode.Override(SkyAmbientMode.Static);

            var exposure = Add<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(9f);

            var bloom = Add<Bloom>(profile);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.75f);
            bloom.threshold.Override(0.5f);

            var tonemap = Add<Tonemapping>(profile);
            tonemap.mode.Override(TonemappingMode.ACES);

            var ssr = Add<ScreenSpaceReflection>(profile);
            ssr.enabled.Override(true);
            ssr.tracing.Override(RayCastingMode.RayTracing);

            var gi = Add<GlobalIllumination>(profile);
            gi.enable.Override(true);
            gi.tracing.Override(RayCastingMode.RayTracing);

            var ao = Add<ScreenSpaceAmbientOcclusion>(profile);
            ao.rayTracing.Override(true);
            ao.intensity.Override(1f);

            Add<RayTracingSettings>(profile);

            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.3f);

            var chroma = Add<ChromaticAberration>(profile);
            chroma.intensity.Override(0.15f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }
    }
}
