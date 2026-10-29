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

        // The arena is exactly an original game grid: 1024 units at GameManager.GridScale metres each,
        // so matrix layouts land where their designers put them. Floor dots every 32 units.
        public const float ArenaHalfSize = 512f * GameManager.GridScale;
        const float DotSpacing = 32f * GameManager.GridScale;
        const float WallHeight = 24f;

        static readonly Color Cyan = new Color(0.1f, 0.85f, 1f);
        static readonly Color Magenta = new Color(0.85f, 0.1f, 0.9f);
        static readonly Color RoverPink = new Color(1f, 0.32f, 0.45f);
        static readonly Color Red = new Color(1f, 0.08f, 0.06f);
        static readonly Color OrbiterBlue = new Color(0.15f, 0.25f, 1f);
        static readonly Color SlicerOrange = new Color(1f, 0.3f, 0.05f);

        // Original Spectre VR shape ids (CENTRAL.MAT) and the scale that maps their units to metres
        const int PlayerShape = 10011, FlagShape = 10086;
        const float EdgeThickness = 0.07f;
        const float FlagHeightStretch = 3f;
        const float FlagBaseScale = 0.35f;   // keeps the base pyramid's footprint small next to the craft
        const float FlagBaseHeight = 0.4f;   // and its height low

        static Material floorMat, bodyMat, wallMat, blockMat, dotMat, cyanMat, flagMat, poleMat, dumpMat;
        static Material roverMat, turretMat, orbiterMat, slicerMat, enemyBoltMat;
        static Material playerPanel, roverPanel, turretPanel, orbiterPanel, slicerPanel;
        static Material acidMat, teleportMat, pusherMat, transportMat;
        static Bounds playerBounds;

        [MenuItem("Spectre VR-EX/Build Arena Scene")]
        public static void Build()
        {
            foreach (var d in new[] { MaterialDir, MeshDir, TextureDir, PrefabDir, "Assets/Scenes" }) Directory.CreateDirectory(d);
            EnsureLayer(PlayerLayer);
            ConfigurePixelArt("Assets/Resources/Menu");
            ConfigurePixelArt("Assets/Resources/HUD");
            CreateMaterials();

            var prefabs = BuildPrefabs();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;   // black void; no legacy skybox
            var arena = new GameObject("Arena").transform;
            BuildFloor(arena);
            var walls = BuildWalls(arena);
            BuildLightingAndVolume();

            var player = BuildPlayer(new Vector3(0f, 0.1f, 0f));

            var gmGo = new GameObject("GameManager");
            var gm = gmGo.AddComponent<GameManager>();
            gm.player = player;
            gm.wallRenderers = walls;
            gm.arenaHalfSize = ArenaHalfSize;
            gm.flagPrefab = prefabs.flag;
            gm.ammoDumpPrefab = prefabs.dump;
            // Robots too (RobotFactory), each edged in its own colour
            gm.robotLook = new RobotLook
            {
                panels = blockMat, tank = roverMat, superTank = turretMat, turret = turretMat, slicer = slicerMat,
                flyer = orbiterMat, acid = acidMat, wall = pusherMat, mud = transportMat, orbiter = orbiterMat, bolt = enemyBoltMat,
                tankPanel = roverPanel, turretPanel = turretPanel, slicerPanel = slicerPanel, orbiterPanel = orbiterPanel,
            };
            // Obstacles are built at runtime from each matrix's own shapes (Matrix.ObstacleFactory)
            gm.obstacleLook = new Matrix.ObstacleLook
            {
                panels = blockMat, edges = cyanMat, acid = acidMat, ice = teleportMat, teleport = teleportMat,
                pusher = pusherMat, transport = transportMat, edgeThickness = EdgeThickness,
            };
            gmGo.AddComponent<MusicPlayer>();
            var hudFont = AssetDatabase.LoadAssetAtPath<Font>(HudFontPath);
            gmGo.AddComponent<SpectreHud>().segmentFont = hudFont;
            gmGo.AddComponent<SpectreEX.Menu.MainMenu>();
            gm.floorDots = GameObject.Find("FloorDots").GetComponent<Renderer>();

            BuildCamera(player.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Spectre VR-EX: arena scene built at " + ScenePath);
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
            playerPanel = PanelMaterial("PanelCyan", Cyan);
            roverPanel = PanelMaterial("PanelPink", RoverPink);
            turretPanel = PanelMaterial("PanelRed", Red);
            orbiterPanel = PanelMaterial("PanelBlue", OrbiterBlue);
            slicerPanel = PanelMaterial("PanelOrange", SlicerOrange);

            // Matrix floor features
            acidMat = GlowMaterial("GlowAcid", new Color(0.3f, 1f, 0.15f), 12f);
            teleportMat = GlowMaterial("GlowTeleport", new Color(0.3f, 0.5f, 1f), 12f);
            pusherMat = GlowMaterial("GlowPusher", new Color(1f, 0.6f, 0.1f), 12f);
            transportMat = GlowMaterial("GlowTransport", new Color(0.75f, 0.3f, 1f), 12.5f);
            AssetDatabase.SaveAssets();
        }

        // A solid face in a craft's colour: lit, slightly self-lit so it reads in the dark arena
        // Ship-coloured panels are only made once: the owner tunes them by hand, so a rebuild keeps them
        static Material PanelMaterial(string name, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{name}.mat");
            if (existing != null) return existing;
            var mat = GlowMaterial(name, color, 8.5f);
            mat.SetColor("_BaseColor", color * 0.35f);
            mat.SetFloat("_Smoothness", 0.6f);
            mat.SetFloat("_Metallic", 0.3f);
            EditorUtility.SetDirty(mat);
            return mat;
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
            var pennant = Pennant();

            var p = new Prefabs();
            p.flag = SavePrefab(BuildFlag(pennant)).GetComponent<Flag>();
            p.dump = SavePrefab(BuildAmmoDump()).GetComponent<AmmoDump>();
            return p;
        }

        // An original Spectre VR shape in the Spectre VR-EX style: solid panels (black, or a craft's
        // own colour) with glowing edges.
        static Transform SpectreModel(string name, Transform parent, int shapeId, float scale, Material glow, out Bounds bounds,
                                      System.Func<Vector3, Vector3> warp = null, string variant = "", Material panel = null)
        {
            string key = $"Spectre_{shapeId}_{Mathf.RoundToInt(scale * 1000)}{variant}";
            var body = SaveMesh(ShapeLibrary.BodyMesh(shapeId, scale, warp), key + "_Body");
            var edges = SaveMesh(ShapeLibrary.EdgeMesh(shapeId, scale, EdgeThickness, warp), key + "_Edges");
            var model = new GameObject(name).transform;
            model.SetParent(parent, false);
            MeshObj("Panels", model, body, panel != null ? panel : bodyMat, Vector3.zero, Vector3.one, Quaternion.identity);
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
            // Shape 10086: base plate y 0-1, pyramid up to y 5, pole to y 16, tip at 17 (shape units).
            // Squash the plate and pyramid, stretch the pole so its top stays FlagHeightStretch x the
            // original, and narrow the footprint - all baked into the mesh so edge lines stay even.
            const float pyramidTop = 5f, shapeTop = 17f;
            float poleStretch = (shapeTop * FlagHeightStretch - pyramidTop * FlagBaseHeight) / (shapeTop - pyramidTop);
            System.Func<Vector3, Vector3> warp = p => new Vector3(
                p.x * FlagBaseScale,
                p.y <= pyramidTop ? p.y * FlagBaseHeight : pyramidTop * FlagBaseHeight + (p.y - pyramidTop) * poleStretch,
                p.z * FlagBaseScale);
            SpectreModel("Stand", visual, FlagShape, 0.15f, poleMat, out var b, warp, "_flag");
            float poleTop = b.max.y;
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

        // ---------- player ----------

        static PlayerTank BuildPlayer(Vector3 pos)
        {
            var root = new GameObject("Player");
            root.transform.position = pos;

            // PlayerMotor moves the craft itself, the original's way; the body is only for contacts
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 50f;
            rb.isKinematic = true;

            // Default "Balance" Spectre: the original's low wedge hull with its raised canopy
            var model = SpectreModel("Model", root.transform, PlayerShape, 0.25f, cyanMat, out var b, panel: playerPanel);
            FitCollider(root, b).sharedMaterial = NoFriction();
            NoShadows(model.gameObject);
            int layer = LayerMask.NameToLayer(PlayerLayer);
            if (layer >= 0) SetLayerRecursive(model.gameObject, layer);
            playerBounds = b;

            // "Balance" Spectre (the original's _SET_TANK_SETTINGS): speed 5, 60 damage points, 40 rounds
            root.AddComponent<PlayerMotor>().speedPoints = 5;
            var weapon = root.AddComponent<TankWeapon>();
            weapon.projectileMaterial = cyanMat;
            weapon.maxAmmo = 40;
            var health = root.AddComponent<Health>();
            health.team = Team.Player;
            health.maxShields = 60;
            health.hitMultiplier = 10;
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
            // DLSS runs through HDRP dynamic resolution
            cam.allowDynamicResolution = true;
            hd.allowDynamicResolution = true;
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
