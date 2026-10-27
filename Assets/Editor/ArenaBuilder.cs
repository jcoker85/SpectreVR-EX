using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using SpectreEX.Hud;

namespace SpectreEX.EditorTools
{
    // Builds the arena scene and the robot / pickup prefabs from scratch so the whole look is
    // reproducible: SpectreEX > Build Arena Scene. Re-running overwrites Assets/Scenes/Arena.unity,
    // the prefabs in Assets/Prefabs and the generated meshes/textures.
    //
    // Look: Spectre VR's black void with tall glowing gradient walls (recoloured per level at runtime)
    // and a dotted floor, on a ray-traced glossy floor. The player craft and robots use the original
    // game's 3D shapes (see ShapeLibrary) rendered as black panels with neon edges, like the obstacles.
    public static class ArenaBuilder
    {
        const string MaterialDir = "Assets/Materials";
        const string MeshDir = "Assets/Meshes";
        const string TextureDir = "Assets/Textures";
        const string PrefabDir = "Assets/Prefabs";
        const string ScenePath = "Assets/Scenes/Arena.unity";
        const string VolumePath = "Assets/Settings/ArenaVolume.asset";
        const string PlayerLayer = "PlayerModel";
        const string HudFontPath = "Assets/Fonts/DSEG14Classic-Light.ttf";

        public const float ArenaHalfSize = 120f;
        const float DotSpacing = 6f;
        const float WallHeight = 24f;

        static readonly Color Cyan = new Color(0.1f, 0.85f, 1f);
        static readonly Color Magenta = new Color(0.85f, 0.1f, 0.9f);
        static readonly Color RoverPink = new Color(1f, 0.32f, 0.45f);
        static readonly Color Red = new Color(1f, 0.08f, 0.06f);
        static readonly Color OrbiterBlue = new Color(0.15f, 0.25f, 1f);
        static readonly Color SlicerOrange = new Color(1f, 0.3f, 0.05f);

        // Original Spectre VR shape ids (CENTRAL.MAT) and the scale that maps their units to metres
        const int PlayerShape = 10011, RoverShape = 10010, TurretShape = 10034, OrbiterShape = 10017, SlicerShape = 10039, FlagShape = 10086;
        const float ShapeScale = 0.13f;
        const float EdgeThickness = 0.07f;
        const float FlagHeightStretch = 3f;
        const float FlagBaseScale = 0.35f;   // keeps the base pyramid small next to the craft

        static Material floorMat, bodyMat, wallMat, blockMat, dotMat, cyanMat, flagMat, poleMat, dumpMat;
        static Material roverMat, turretMat, orbiterMat, slicerMat, enemyBoltMat;
        static readonly List<Vector3> obstacleCentres = new List<Vector3>();
        static Bounds playerBounds;

        [MenuItem("SpectreEX/Build Arena Scene")]
        public static void Build()
        {
            foreach (var d in new[] { MaterialDir, MeshDir, TextureDir, PrefabDir, "Assets/Scenes" }) Directory.CreateDirectory(d);
            EnsureLayer(PlayerLayer);
            ConfigurePixelArt("Assets/Resources/Menu");
            ConfigurePixelArt("Assets/Resources/HUD");
            CreateMaterials();

            var prefabs = BuildPrefabs();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var arena = new GameObject("Arena").transform;
            BuildFloor(arena);
            var walls = BuildWalls(arena);
            BuildObstacles(arena);
            BuildLightingAndVolume();

            var player = BuildPlayer(new Vector3(0f, 0.1f, 0f));

            var gmGo = new GameObject("GameManager");
            var gm = gmGo.AddComponent<GameManager>();
            gm.player = player;
            gm.wallRenderers = walls;
            gm.arenaHalfSize = ArenaHalfSize;
            gm.flagPrefab = prefabs.flag;
            gm.ammoDumpPrefab = prefabs.dump;
            gm.roverPrefab = prefabs.rover;
            gm.turretPrefab = prefabs.turret;
            gm.orbiterPrefab = prefabs.orbiter;
            gm.slicerPrefab = prefabs.slicer;
            gmGo.AddComponent<MusicPlayer>();
            var hudFont = AssetDatabase.LoadAssetAtPath<Font>(HudFontPath);
            gmGo.AddComponent<SpectreHud>().segmentFont = hudFont;
            gmGo.AddComponent<SpectreEX.Menu.MainMenu>().segmentFont = hudFont;
            gm.floorDots = GameObject.Find("FloorDots").GetComponent<Renderer>();

            BuildCamera(player.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("SpectreEX: arena scene built at " + ScenePath);
        }

        // ---------- project setup ----------

        static void EnsureLayer(string name)
        {
            if (LayerMask.NameToLayer(name) >= 0) return;
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var sp = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(sp.stringValue))
                {
                    sp.stringValue = name;
                    tagManager.ApplyModifiedProperties();
                    return;
                }
            }
            Debug.LogWarning("SpectreEX: no free layer for " + name);
        }

        // Original 1993 artwork: keep exact pixels (no resize to power of two, no mips, no compression)
        static void ConfigurePixelArt(string folder)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                if (imp == null) continue;
                bool changed = imp.npotScale != TextureImporterNPOTScale.None || imp.mipmapEnabled || imp.filterMode != FilterMode.Point
                               || imp.textureCompression != TextureImporterCompression.Uncompressed || imp.wrapMode != TextureWrapMode.Clamp;
                if (!changed) continue;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.mipmapEnabled = false;
                imp.filterMode = FilterMode.Point;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }

        // ---------- materials ----------

        static void CreateMaterials()
        {
            floorMat = LitMaterial("Floor", new Color(0.008f, 0.009f, 0.012f), smoothness: 0.96f, metallic: 0.3f);
            bodyMat = LitMaterial("TankBody", new Color(0.02f, 0.025f, 0.035f), smoothness: 0.85f, metallic: 0.8f);
            blockMat = LitMaterial("Block", new Color(0.015f, 0.018f, 0.025f), smoothness: 0.9f, metallic: 0.5f);
            wallMat = GlowMaterial("WallGradient", Magenta, 10.5f, WallGradientTexture());
            dotMat = GlowMaterial("FloorDots", Color.white, 11.5f);
            cyanMat = GlowMaterial("GlowCyan", Cyan, 12.5f);
            flagMat = GlowMaterial("FlagRed", Red, 12f);
            poleMat = GlowMaterial("FlagPole", Color.white, 10.5f);
            dumpMat = GlowMaterial("AmmoDumpWhite", Color.white, 11f);
            // Robots share the player's look: black panels, edges glowing in their own colour
            roverMat = GlowMaterial("RoverPink", RoverPink, 12.5f);
            turretMat = GlowMaterial("TurretRed", Red, 12.5f);
            orbiterMat = GlowMaterial("OrbiterBlue", OrbiterBlue, 13f);
            slicerMat = GlowMaterial("SlicerRed", SlicerOrange, 12.5f);
            enemyBoltMat = GlowMaterial("EnemyBolt", new Color(1f, 0.3f, 0.2f), 12.5f);
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
        static Material GlowMaterial(string name, Color color, float ev100, Texture2D emissiveMap = null)
        {
            var mat = GetOrCreate(name);
            mat.SetColor("_BaseColor", Color.black);
            mat.SetFloat("_Smoothness", 0.5f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetTexture("_EmissiveColorMap", emissiveMap);
            mat.SetFloat("_DoubleSidedEnable", 1f);
            HDMaterial.SetUseEmissiveIntensity(mat, true);
            HDMaterial.SetEmissiveColor(mat, color);
            HDMaterial.SetEmissiveIntensity(mat, ev100, EmissiveIntensityUnit.EV100);
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Bright at the floor, fading to black at the top, like the original's horizon bands.
        static Texture2D WallGradientTexture()
        {
            string path = $"{TextureDir}/WallGradient.png";
            var tex = new Texture2D(4, 256, TextureFormat.RGBA32, false);
            for (int y = 0; y < 256; y++)
            {
                float t = y / 255f;
                float v = Mathf.Pow(1f - t, 3f);
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, new Color(v, v, v, 1f));
            }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- meshes ----------

        // Flat-shaded mesh: every triangle gets its own vertices so facets read as polygons.
        static Mesh FlatMesh(string name, Vector3[] v, int[] tris)
        {
            var verts = new Vector3[tris.Length];
            var idx = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++) { verts[i] = v[tris[i]]; idx[i] = i; }
            var mesh = new Mesh { name = name, vertices = verts, triangles = idx };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return SaveMesh(mesh, name);
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = $"{MeshDir}/{name}.asset";
            mesh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Mesh Pennant()
        {
            var v = new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1.6f, 0.5f, 0) };
            return FlatMesh("Pennant", v, new[] { 0, 1, 2, 0, 2, 1 });
        }

        // All the floor dots as one mesh: one renderer instead of thousands.
        static Mesh FloorDots()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float s = 0.15f;
            int n = Mathf.RoundToInt(ArenaHalfSize * 2f / DotSpacing);
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
            {
                float x = -ArenaHalfSize + i * DotSpacing, z = -ArenaHalfSize + j * DotSpacing;
                int b = verts.Count;
                verts.Add(new Vector3(x - s, 0.012f, z - s));
                verts.Add(new Vector3(x - s, 0.012f, z + s));
                verts.Add(new Vector3(x + s, 0.012f, z + s));
                verts.Add(new Vector3(x + s, 0.012f, z - s));
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var mesh = new Mesh { name = "FloorDots", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return SaveMesh(mesh, "FloorDots");
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

        static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
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
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static GameObject SavePrefab(GameObject go)
        {
            string path = $"{PrefabDir}/{go.name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---------- prefabs ----------

        struct Prefabs
        {
            public Flag flag;
            public AmmoDump dump;
            public GameObject rover, turret, orbiter, slicer;
        }

        static Prefabs BuildPrefabs()
        {
            var pennant = Pennant();

            var p = new Prefabs();
            p.flag = SavePrefab(BuildFlag(pennant)).GetComponent<Flag>();
            p.dump = SavePrefab(BuildAmmoDump()).GetComponent<AmmoDump>();
            p.rover = SavePrefab(BuildRover());
            p.turret = SavePrefab(BuildTurret());
            p.orbiter = SavePrefab(BuildOrbiter());
            p.slicer = SavePrefab(BuildSlicer());
            return p;
        }

        // An original Spectre VR shape in the SpectreEX style: black panels with glowing edges.
        static Transform SpectreModel(string name, Transform parent, int shapeId, float scale, Material glow, out Bounds bounds)
        {
            string key = $"Spectre_{shapeId}_{Mathf.RoundToInt(scale * 1000)}";
            var body = SaveMesh(ShapeLibrary.BodyMesh(shapeId, scale), key + "_Body");
            var edges = SaveMesh(ShapeLibrary.EdgeMesh(shapeId, scale, EdgeThickness), key + "_Edges");
            var model = new GameObject(name).transform;
            model.SetParent(parent, false);
            MeshObj("Panels", model, body, bodyMat, Vector3.zero, Vector3.one, Quaternion.identity);
            var e = MeshObj("Edges", model, edges, glow, Vector3.zero, Vector3.one, Quaternion.identity);
            e.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            bounds = body.bounds;
            return model;
        }

        static GameObject BuildFlag(Mesh pennant)
        {
            var f = new GameObject("Flag");
            var trigger = f.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 3f;
            trigger.center = new Vector3(0f, 1.5f, 0f);

            // Original flag stand (pole on a base plate), stretched tall so flags stand well above
            // the craft as in the original, with a red pennant at the top of the pole
            var visual = new GameObject("Visual").transform;
            visual.SetParent(f.transform, false);
            visual.localPosition = Vector3.zero;
            var stand = SpectreModel("Stand", visual, FlagShape, 0.15f, poleMat, out var b);
            stand.localScale = new Vector3(FlagBaseScale, FlagHeightStretch, FlagBaseScale);
            float poleTop = b.max.y * FlagHeightStretch;
            MeshObj("Pennant", visual, pennant, flagMat, new Vector3(0.05f, poleTop - 1.7f, 0f), new Vector3(1.5f, 1.5f, 1f), Quaternion.identity);
            NoShadows(f);

            var flag = f.AddComponent<Flag>();
            flag.visual = visual;
            flag.bobHeight = 0f;
            return f;
        }

        static GameObject BuildAmmoDump()
        {
            var d = new GameObject("AmmoDump");
            var trigger = d.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(5.5f, 3f, 5.5f);
            trigger.center = new Vector3(0f, 1.5f, 0f);

            var tiles = new List<Transform>();
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                tiles.Add(Box("Tile", d.transform, new Vector3(i * 1.7f, 0.05f, j * 1.7f), new Vector3(1.45f, 0.08f, 1.45f), dumpMat, false).transform);
            NoShadows(d);
            d.AddComponent<AmmoDump>().tiles = tiles.ToArray();
            return d;
        }

        static Health AddHealth(GameObject go, int shields, int score, Material derez)
        {
            var h = go.AddComponent<Health>();
            h.team = Team.Enemy;
            h.maxShields = shields;
            h.scoreValue = score;
            h.derezMaterial = derez;
            return h;
        }

        static TankWeapon AddEnemyWeapon(GameObject go, Transform muzzle, float cooldown, float speed)
        {
            var w = go.AddComponent<TankWeapon>();
            w.muzzle = muzzle;
            w.projectileMaterial = enemyBoltMat;
            w.team = Team.Enemy;
            w.cooldown = cooldown;
            w.projectileSpeed = speed;
            w.infiniteAmmo = true;
            w.fireSound = Snd.EnemyShot;
            return w;
        }

        static Transform Muzzle(Transform parent, Vector3 pos)
        {
            var m = new GameObject("Muzzle").transform;
            m.SetParent(parent, false);
            m.localPosition = pos;
            return m;
        }

        static BoxCollider FitCollider(GameObject root, Bounds b)
        {
            var col = root.AddComponent<BoxCollider>();
            col.center = b.center;
            col.size = Vector3.Max(b.size, new Vector3(0.6f, 0.6f, 0.6f));
            return col;
        }

        // Rover: the original's finned robot tank
        static GameObject BuildRover()
        {
            var root = new GameObject("Rover");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 50f;
            SpectreModel("Model", root.transform, RoverShape, ShapeScale, roverMat, out var b);
            FitCollider(root, b).sharedMaterial = NoFriction();

            var muzzle = Muzzle(root.transform, new Vector3(0f, b.center.y + 0.2f, b.max.z + 0.4f));
            root.AddComponent<TankMotor>().maxSpeed = 16f;
            AddEnemyWeapon(root, muzzle, 1.5f, 55f);
            AddHealth(root, 1, 250, roverMat);
            root.AddComponent<RoverAI>();
            return root;
        }

        // Gun turret: the original column, widened; the whole column swivels to aim
        static GameObject BuildTurret()
        {
            var root = new GameObject("Turret");
            var head = SpectreModel("Head", root.transform, TurretShape, ShapeScale, turretMat, out var b);
            head.localScale = new Vector3(1.8f, 1f, 1.8f);
            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, b.size.y * 0.5f, 0f);
            col.height = b.size.y;
            col.radius = Mathf.Max(b.extents.x, b.extents.z) * 1.8f;
            var muzzle = Muzzle(head, new Vector3(0f, b.max.y * 0.8f, b.max.z + 0.6f));

            AddEnemyWeapon(root, muzzle, 1.8f, 60f);
            AddHealth(root, 3, 500, turretMat);
            root.AddComponent<TurretAI>().head = head;
            return root;
        }

        // Orbiter: the original spinning-top gem, hovering
        static GameObject BuildOrbiter()
        {
            var root = new GameObject("Orbiter");
            root.AddComponent<Rigidbody>();
            var body = SpectreModel("Body", root.transform, OrbiterShape, 0.18f, orbiterMat, out var b);
            body.localPosition = -b.center;   // spin about its own middle
            var col = root.AddComponent<SphereCollider>();
            col.radius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
            var muzzle = Muzzle(root.transform, Vector3.zero);
            AddEnemyWeapon(root, muzzle, 2.6f, 50f);
            AddHealth(root, 2, 400, orbiterMat);
            var ai = root.AddComponent<OrbiterAI>();
            ai.body = body;
            return root;
        }

        // Slicer: two of the original's long blades crossed, spinning flat like a rotor
        static GameObject BuildSlicer()
        {
            var root = new GameObject("Slicer");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 20f;

            var blades = new GameObject("Blades").transform;
            blades.SetParent(root.transform, false);
            blades.localPosition = new Vector3(0f, 1.2f, 0f);
            Bounds b = default;
            for (int i = 0; i < 2; i++)
            {
                var pivot = new GameObject("BladePivot").transform;
                pivot.SetParent(blades, false);
                pivot.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                var blade = SpectreModel("Blade", pivot, SlicerShape, ShapeScale, slicerMat, out b);
                blade.localPosition = -b.center;
            }
            var col = root.AddComponent<SphereCollider>();
            col.center = blades.localPosition;
            col.radius = b.extents.z;
            col.sharedMaterial = NoFriction();

            AddHealth(root, 1, 300, slicerMat);
            var ai = root.AddComponent<SlicerAI>();
            ai.blades = blades;
            return root;
        }

        static PhysicsMaterial noFriction;
        static PhysicsMaterial NoFriction()
        {
            if (noFriction != null) return noFriction;
            string path = $"{MaterialDir}/NoFriction.physicMaterial";
            noFriction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (noFriction == null)
            {
                noFriction = new PhysicsMaterial("NoFriction") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
                AssetDatabase.CreateAsset(noFriction, path);
            }
            return noFriction;
        }

        // ---------- arena ----------

        static void BuildFloor(Transform arena)
        {
            float size = ArenaHalfSize * 2f;
            Box("Floor", arena, new Vector3(0f, -0.5f, 0f), new Vector3(size, 1f, size), floorMat);
            var dots = new GameObject("FloorDots");
            dots.transform.SetParent(arena, false);
            dots.AddComponent<MeshFilter>().sharedMesh = FloorDots();
            var r = dots.AddComponent<MeshRenderer>();
            r.sharedMaterial = dotMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Renderer[] BuildWalls(Transform arena)
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
            // The cube primitive's UVs run upside down on some faces, flipping the gradient. So each wall is
            // an invisible box collider plus an inward-facing quad whose UVs always put the bright end at the floor.
            var faceYaw = new[] { 0f, 180f, 90f, -90f };   // quad front (-Z) turned to face the arena centre
            var renderers = new List<Renderer>();
            for (int i = 0; i < defs.Length; i++)
            {
                var (pos, size) = defs[i];
                var w = Box("Wall", walls, pos, size, wallMat);
                Object.DestroyImmediate(w.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(w.GetComponent<MeshFilter>());

                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Glow";
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(walls, false);
                Vector3 inward = -new Vector3(Mathf.Sign(pos.x) * (Mathf.Abs(pos.x) > 1f ? 1f : 0f), 0f, Mathf.Sign(pos.z) * (Mathf.Abs(pos.z) > 1f ? 1f : 0f));
                q.transform.localPosition = new Vector3(pos.x, WallHeight / 2f, pos.z) + inward * 1.01f;
                q.transform.localRotation = Quaternion.Euler(0f, faceYaw[i], 0f);
                q.transform.localScale = new Vector3(len, WallHeight, 1f);
                var r = q.GetComponent<MeshRenderer>();
                r.sharedMaterial = wallMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                renderers.Add(r);
            }
            return renderers.ToArray();
        }

        static void BuildObstacles(Transform arena)
        {
            var root = new GameObject("Obstacles").transform;
            root.SetParent(arena, false);
            var rng = new System.Random(1991);
            obstacleCentres.Clear();
            int attempts = 0;
            while (obstacleCentres.Count < 14 && attempts++ < 500)
            {
                var pos = new Vector3(Range(rng, -95f, 95f), 0f, Range(rng, -95f, 95f));
                if (pos.magnitude < 30f) continue; // keep the player start clear
                bool crowded = false;
                foreach (var p in obstacleCentres) if (Vector3.Distance(p, pos) < 28f) { crowded = true; break; }
                if (crowded) continue;
                obstacleCentres.Add(pos);

                var size = new Vector3(Range(rng, 4f, 12f), Range(rng, 3f, 14f), Range(rng, 4f, 12f));
                var o = new GameObject("Block").transform;
                o.SetParent(root, false);
                o.localPosition = pos + Vector3.up * size.y / 2f;
                o.localRotation = Quaternion.Euler(0f, Range(rng, 0f, 90f), 0f);
                Box("Body", o, Vector3.zero, size, blockMat);
                AddEdges(o, Vector3.zero, size, 0.15f, cyanMat);
                NoShadows(o.Find("Edges").gameObject);
            }
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        // ---------- player ----------

        static PlayerTank BuildPlayer(Vector3 pos)
        {
            var root = new GameObject("Player");
            root.transform.position = pos;

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 50f;

            // Default "Balance" Spectre: the original's low wedge hull with its raised canopy
            var model = SpectreModel("Model", root.transform, PlayerShape, 0.25f, cyanMat, out var b);
            FitCollider(root, b).sharedMaterial = NoFriction();
            NoShadows(model.gameObject);
            int layer = LayerMask.NameToLayer(PlayerLayer);
            if (layer >= 0) SetLayerRecursive(model.gameObject, layer);
            playerBounds = b;

            var muzzle = Muzzle(root.transform, new Vector3(0f, b.center.y + 0.2f, b.max.z + 0.4f));

            // "Balance" Spectre: middling speed, 5 shields, 30 rounds
            root.AddComponent<TankMotor>().maxSpeed = 18f;
            var weapon = root.AddComponent<TankWeapon>();
            weapon.muzzle = muzzle;
            weapon.projectileMaterial = cyanMat;
            weapon.team = Team.Player;
            weapon.cooldown = 0.3f;
            weapon.projectileSpeed = 80f;
            weapon.infiniteAmmo = false;
            weapon.maxAmmo = 30;
            weapon.fireSound = Snd.PlayerShot;
            var health = root.AddComponent<Health>();
            health.team = Team.Player;
            health.maxShields = 5;
            health.derezMaterial = cyanMat;
            health.hitSound = Snd.PlayerHit;
            return root.AddComponent<PlayerTank>();
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
            var view = camGo.AddComponent<ViewCamera>();
            view.target = player;
            // Sit just above the canopy, a little forward of the hull's middle
            view.cockpitOffset = new Vector3(0f, playerBounds.max.y + 0.35f, playerBounds.center.z + 0.5f);
            camGo.transform.SetPositionAndRotation(player.position + view.cockpitOffset, player.rotation);
        }

        static void BuildLightingAndVolume()
        {
            // Faint cool key light so hull shapes read; the glow does the rest through RTGI.
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
            bloom.intensity.Override(0.3f);
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
