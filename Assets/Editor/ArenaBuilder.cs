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
    // reproducible: Spectre VR-EX > Build Arena Scene. Re-running overwrites Assets/Scenes/Arena.unity,
    // the prefabs in Assets/Prefabs and the generated meshes/textures.
    //
    // Look: Spectre VR's black void with tall glowing gradient walls (recoloured per level at runtime)
    // and a dotted floor, on a ray-traced glossy floor. The craft, robots and obstacles are built at
    // runtime from the player's own game data (GameData), so nothing here comes from the original game:
    // the scene and prefabs it writes can be committed and built into a release.
    //
    // It also writes Assets/Scenes/Setup.unity, the build's first scene (Setup.FirstRunSetup), which
    // builds that game data from the player's copy of Spectre VR on first run.
    public static class ArenaBuilder
    {
        const string MaterialDir = "Assets/Materials";
        const string MeshDir = "Assets/Meshes";
        const string TextureDir = "Assets/Textures";
        const string PrefabDir = "Assets/Prefabs";
        const string ScenePath = "Assets/Scenes/Arena.unity";
        public const string SetupScenePath = "Assets/Scenes/Setup.unity";
        const string VolumePath = "Assets/Settings/ArenaVolume.asset";
        const string PlayerLayer = "PlayerModel";
        const string HudFontPath = "Assets/Fonts/DSEG14Classic-Light.ttf";

        // The arena is exactly an original game grid: 1024 units at GameManager.GridScale metres each,
        // so matrix layouts land where their designers put them. Floor dots every 32 units.
        public const float ArenaHalfSize = 512f * GameManager.GridScale;
        const float DotSpacing = 32f * GameManager.GridScale;
        const float HorizonRadius = 900f;   // inside the camera's far plane of 1000
        const float HorizonHeight = 260f;
        const int HorizonSides = 64;

        static readonly Color Cyan = new Color(0.1f, 0.85f, 1f);
        static readonly Color Magenta = new Color(0.85f, 0.1f, 0.9f);
        static readonly Color RoverPink = new Color(1f, 0.32f, 0.45f);
        static readonly Color Red = new Color(1f, 0.08f, 0.06f);
        static readonly Color OrbiterBlue = new Color(0.15f, 0.25f, 1f);
        static readonly Color SlicerOrange = new Color(1f, 0.3f, 0.05f);

        const float EdgeThickness = 0.07f;
        // The craft's box until PlayerModel builds it from the matrix: the size of Central Systems' craft
        static readonly Bounds PlayerPlaceholder = new Bounds(new Vector3(0f, 0.75f, 0.75f), new Vector3(4.5f, 1.5f, 4.5f));

        static Material shapeMat, floorMat, bodyMat, wallMat, blockMat, dotMat, cyanMat, flagMat, poleMat, dumpMat;
        static Material roverMat, turretMat, orbiterMat, slicerMat, enemyBoltMat;
        static Material acidMat, teleportMat, pusherMat, transportMat;

        [MenuItem("Spectre VR-EX/Build Arena Scene")]
        public static void Build()
        {
            foreach (var d in new[] { MaterialDir, MeshDir, TextureDir, PrefabDir, "Assets/Scenes" }) Directory.CreateDirectory(d);
            EnsureLayer(PlayerLayer);
            CreateMaterials();

            var prefabs = BuildPrefabs();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;   // black void; no legacy skybox
            var arena = new GameObject("Arena").transform;
            BuildFloor(arena);
            var horizon = BuildHorizon(arena);
            BuildLightingAndVolume();

            var player = BuildPlayer(new Vector3(0f, 0.1f, 0f));

            var gmGo = new GameObject("GameManager");
            var gm = gmGo.AddComponent<GameManager>();
            gm.player = player;
            gm.horizon = horizon;
            gm.flagPrefab = prefabs.flag;
            gm.ammoDumpPrefab = prefabs.dump;
            // Robots too (RobotFactory), each edged in its own colour
            gm.robotLook = new RobotLook
            {
                shapes = shapeMat, tank = roverMat, superTank = turretMat, turret = turretMat, slicer = slicerMat,
                flyer = orbiterMat, acid = acidMat, wall = pusherMat, mud = transportMat, orbiter = orbiterMat, bolt = enemyBoltMat,
            };
            // Obstacles are built at runtime from each matrix's own shapes (Matrix.ObstacleFactory)
            gm.obstacleLook = new Matrix.ObstacleLook
            {
                shapes = shapeMat, edges = cyanMat, acid = acidMat, ice = teleportMat, teleport = teleportMat,
                pusher = pusherMat, transport = transportMat, edgeThickness = EdgeThickness,
            };
            gmGo.AddComponent<MusicPlayer>();
            var hudFont = AssetDatabase.LoadAssetAtPath<Font>(HudFontPath);
            gmGo.AddComponent<SpectreHud>().segmentFont = hudFont;
            gmGo.AddComponent<SpectreEX.Menu.MainMenu>();
            gm.floorDots = GameObject.Find("FloorDots").GetComponent<Renderer>();

            BuildCamera(player.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            BuildSetupScene();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SetupScenePath, true), new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("Spectre VR-EX: scenes built at " + SetupScenePath + " and " + ScenePath);
        }

        // The first-run setup screen: a black camera for HDRP to draw to, and FirstRunSetup's IMGUI over it
        static void BuildSetupScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Color.black;
            camGo.AddComponent<AudioListener>();
            new GameObject("Setup").AddComponent<SpectreEX.Setup.FirstRunSetup>();
            EditorSceneManager.SaveScene(scene, SetupScenePath);
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
            Debug.LogWarning("Spectre VR-EX: no free layer for " + name);
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
            shapeMat = ShapeMaterial();
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

            // Craft panels take the craft's own colour (Options "Filled"), dimmer than the edges

            // Matrix floor features
            acidMat = GlowMaterial("GlowAcid", new Color(0.3f, 1f, 0.15f), 12f);
            teleportMat = GlowMaterial("GlowTeleport", new Color(0.3f, 0.5f, 1f), 12f);
            pusherMat = GlowMaterial("GlowPusher", new Color(1f, 0.6f, 0.1f), 12f);
            transportMat = GlowMaterial("GlowTransport", new Color(0.75f, 0.3f, 1f), 12.5f);
            AssetDatabase.SaveAssets();
        }

        // A solid face in a craft's colour: lit, slightly self-lit so it reads in the dark arena
        // Ship-coloured panels are only made once: the owner tunes them by hand, so a rebuild keeps them
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

        const float ShapeGlowEv100 = 11.5f;
        const float ShapeLightLux = 1500f;

        // Every original shape's solid faces. GameManager puts the level's palette in both maps at runtime;
        // the texture here only makes HDRP compile the emissive map in. The faces glow a little so they
        // keep their colour in the dark arena, and the lights and reflections do the rest.
        static Material ShapeMaterial()
        {
            var mat = LitMaterial("ShapePalette", Color.white, 0.6f, 0f);
            var placeholder = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/WallGradient.png");
            mat.SetTexture("_BaseColorMap", placeholder);
            mat.SetTexture("_EmissiveColorMap", placeholder);
            HDMaterial.SetUseEmissiveIntensity(mat, true);
            HDMaterial.SetEmissiveColor(mat, Color.white);
            HDMaterial.SetEmissiveIntensity(mat, ShapeGlowEv100, EmissiveIntensityUnit.EV100);
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
        }

        static Prefabs BuildPrefabs()
        {

            var p = new Prefabs();
            p.flag = SavePrefab(BuildFlag()).GetComponent<Flag>();
            p.dump = SavePrefab(BuildAmmoDump()).GetComponent<AmmoDump>();
            return p;
        }

        static GameObject BuildFlag()
        {
            var f = new GameObject("Flag");
            // The flag's own extent, 0x800 units out and 0x1200 high (_CUBE_COLLIDE)
            var trigger = f.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0x1000, 0x1200, 0x1000) * PlayerMotor.UnitsToMetres;
            trigger.center = new Vector3(0f, 0x900 * PlayerMotor.UnitsToMetres, 0f);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(f.transform, false);

            var flag = f.AddComponent<Flag>();
            flag.visual = visual;
            return f;
        }

        static GameObject BuildAmmoDump()
        {
            var d = new GameObject("AmmoDump");
            var trigger = d.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            // _FLAT_CUBE_COLLIDE: the square's own extent, at any height
            float side = 0xC00 * PlayerMotor.UnitsToMetres;
            trigger.size = new Vector3(side, 200f, side);

            // The built-in fuel shape: one flat square, 0xC00 units across
            var tiles = new List<Transform> { Box("Tile", d.transform, new Vector3(0f, 0.05f, 0f), new Vector3(side, 0.02f, side), dumpMat, false).transform };
            NoShadows(d);
            d.AddComponent<AmmoDump>().tiles = tiles.ToArray();
            return d;
        }

        static BoxCollider FitCollider(GameObject root, Bounds b)
        {
            var col = root.AddComponent<BoxCollider>();
            col.center = b.center;
            col.size = Vector3.Max(b.size, new Vector3(0.6f, 0.6f, 0.6f));
            return col;
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
            // Runs on to the horizon wherever the camera is; the dots mark the part in bounds
            float size = (ArenaHalfSize + HorizonRadius) * 2f;
            Box("Floor", arena, new Vector3(0f, -0.5f, 0f), new Vector3(size, 1f, size), floorMat);
            var dots = new GameObject("FloorDots");
            dots.transform.SetParent(arena, false);
            dots.AddComponent<MeshFilter>().sharedMesh = FloorDots();
            var r = dots.AddComponent<MeshRenderer>();
            r.sharedMaterial = dotMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // The original has no walls: the bounds are the motors' clamp, and past them is only the sky colour's
        // band at the horizon. So this is a ring with no collider, far enough off that nothing gets behind it.
        static Renderer BuildHorizon(Transform arena)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= HorizonSides; i++)
            {
                float a = i * Mathf.PI * 2f / HorizonSides;
                var edge = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * HorizonRadius;
                verts.Add(edge);
                verts.Add(edge + Vector3.up * HorizonHeight);
                uvs.Add(new Vector2(0f, 0f));   // bright end at the floor
                uvs.Add(new Vector2(0f, 1f));
                if (i == HorizonSides) break;
                int b = i * 2;
                tris.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
            }
            var mesh = new Mesh { name = "Horizon" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Horizon");
            go.transform.SetParent(arena, false);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, "Horizon");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = wallMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<Horizon>();
            return r;
        }

        // ---------- player ----------

        static PlayerTank BuildPlayer(Vector3 pos)
        {
            var root = new GameObject("Player");
            root.transform.position = pos;

            // PlayerMotor moves the craft itself, the original's way; the body is only for contacts
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 50f;
            rb.isKinematic = true;

            // The craft's shape comes from the matrix at runtime (PlayerModel); until then it's an empty box
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            FitCollider(root, PlayerPlaceholder).sharedMaterial = NoFriction();
            int layer = LayerMask.NameToLayer(PlayerLayer);
            if (layer >= 0) model.layer = layer;

            // "Balance" Spectre (the original's _SET_TANK_SETTINGS): speed 5, 60 damage points, 40 rounds
            root.AddComponent<PlayerMotor>().speedPoints = 5;
            var weapon = root.AddComponent<TankWeapon>();
            weapon.projectileMaterial = cyanMat;
            weapon.maxAmmo = 40;
            var health = root.AddComponent<Health>();
            health.team = Team.Player;
            health.maxShields = 60;
            health.hitMultiplier = 10;
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
            cam.fieldOfView = 76f;
            camGo.AddComponent<AudioListener>();
            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Color.black;
            // DLSS runs through HDRP dynamic resolution
            cam.allowDynamicResolution = true;
            hd.allowDynamicResolution = true;
            var view = camGo.AddComponent<ViewCamera>();
            view.target = player;
            // Sit just above the canopy, a little forward of the hull's middle
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
            light.shadows = LightShadows.None;   // HDRP allows one directional shadow: ShadeLight's
            lightGo.AddComponent<HDAdditionalLightData>();

            // The light _SHADE shades faces by (x forward, y side, z up in the game's axes). The faces' glow
            // is the middle of the original's shade range; this light moves them up and down from there.
            var shadeGo = new GameObject("ShadeLight");
            shadeGo.transform.rotation = Quaternion.LookRotation(-new Vector3(0x99, 0xC0, 0x47).normalized);
            var shade = shadeGo.AddComponent<Light>();
            shade.type = LightType.Directional;
            shade.color = Color.white;
            shade.intensity = ShapeLightLux;
            shade.shadows = LightShadows.Soft;
            var shadeData = shadeGo.AddComponent<HDAdditionalLightData>();
            shadeData.useRayTracedShadows = true;
            shadeData.affectSpecular = false;   // it shades faces; on the glossy floor it's only a glare

            // The arena profile is hand-tuned in the editor (ray tracing quality, samples, DLSS-friendly
            // settings), so only create it when it's missing - never overwrite an existing one.
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null) profile = CreateDefaultVolumeProfile();

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        static VolumeProfile CreateDefaultVolumeProfile()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumePath);

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

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
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
