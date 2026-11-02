using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // Spectre VR session rules: lives, score, levels, the level clock and its time bonus, and respawns.
    // Levels come from the chosen matrix (.MAT, as in the original game): each level's game grid
    // (obstacles and flags), enemy set and enemy data, tip and song. Without a matrix it falls back to
    // generated levels (LevelPlan). Games are started from the main menu; Esc pauses to the menu and
    // Esc again resumes, P pauses in place, and demo mode ends on any key.
    public class GameManager : MonoBehaviour
    {
        // Re-found on demand so a script recompile during Play mode doesn't strand everything
        static GameManager instance;
        public static GameManager Instance
        {
            get
            {
                if (instance == null) instance = FindFirstObjectByType<GameManager>();
                return instance;
            }
            private set => instance = value;
        }

        // Original grids are 1024 units square; this many metres per unit (the scale the shapes use)
        public const float GridScale = 0.16f;

        [Header("Scene")]
        public PlayerTank player;
        public Renderer horizon;
        public Renderer floorDots;
        public float arenaHalfSize = 82f;

        [Header("Prefabs")]
        public Flag flagPrefab;
        public AmmoDump ammoDumpPrefab;
        public ObstacleLook obstacleLook = new ObstacleLook();
        public RobotLook robotLook = new RobotLook();
        public int fallbackObstacleType = 15;   // cube

        // Obstacle shapes for the level being built: the matrix's own, else Central Systems'
        ShapeSet shapes;

        [Header("Rules")]
        public int startingLives = 3;
        public int flagPoints = 20;   // the original's +20 a flag

        const int MaxLives = 9;
        const int DeadTicks = 50;          // _DEAD_TIMER: 0x9C3 at 0x32 a tick
        const int DeadWaitTicks = DeadTicks - 1;   // its first 0x32 comes on the tick the craft goes
        const int LevelEndTicks = 18;      // _END_LEVEL_TIMER
        const int RepairTicks = 0x21C;     // _DAMAGE_TIMER: 10 points of damage mended every ~30 s

        public int Score { get; private set; }
        public int Lives { get; private set; }
        public int Level { get; private set; }
        // _BONUS: (flags + tanks) x 10 + 30 "seconds" x 4 at the start of each life, falling by 4 every
        // 18 ticks; what's left is added to the score when the level is won. There's no time limit.
        public int Bonus { get; private set; }
        public int TimeAllowed { get; private set; }
        public int FlagsTotal { get; private set; }
        public int FlagsCollected { get; private set; }
        public bool Playing { get; private set; }
        public bool InGame { get; private set; }
        public bool Paused { get; private set; }
        public bool Suspended { get; private set; }   // paused behind the main menu (Esc)
        int resumedFrame = -1, demoStartFrame = -1;
        public bool Demo { get; private set; }
        public LevelPlan Plan { get; private set; }
        public MatrixFile CurrentMatrix { get; private set; }

        // One slot per flag, null once collected: robots belong to a slot, and the spots stay in use
        // after a flag has gone (_FLAG_OBJ, _PLACE_NEAR_FLAG)
        public readonly List<Flag> Flags = new List<Flag>();
        readonly List<Vector3> flagSpots = new List<Vector3>();
        public readonly List<AmmoDump> Dumps = new List<AmmoDump>();
        public readonly List<Health> Robots = new List<Health>();
        public readonly List<GameObject> Obstacles = new List<GameObject>();
        public int AcidDamage { get; private set; } = 10;   // raw points every third tick in acid

        public System.Action<string, string> ShowBanner;           // big title, red tip
        public System.Action<int[], int> ShowRecap;                 // Quickness, Accuracy, Avoidance out of 128; animation step
        public System.Action HideOverlays;
        public System.Action<int> GameOverSpin;                     // a step of _SPIN_UP, or -1 for the words at rest
        public System.Action<int, int> ReturnedToMenu;              // final score and level (0 if abandoned)
        public System.Action Suspend;                               // Esc in game: show the menu over the paused game

        // The robots run on the original's tick (RobotDirector); _TANKS_LEFT counts every robot still to
        // be destroyed, regenerations included, and the destroyed robots' bonuses are paid when it hits 0
        RobotDirector director;
        int robotsLeft, bankedBonus;
        readonly int[] flagGuards = new int[64];   // _FLAG_TURRETS: robots placed by each flag
        public ShapeSet Shapes => shapes;
        float horizonNits = -1f;
        Color horizonColor;
        Material horizonSolid, horizonBand;
        Coroutine session, down;
        float downAt;
        DemoPilot demoPilot;

        // What animations go by: P stops the things _MOVE_THINGS moves, though the craft carries on
        public static float GameDelta => Instance != null && Instance.Paused ? 0f : Time.deltaTime;
        public bool LastRobot => robotsLeft == 1;
        public static bool DemoHasRun { get; private set; }   // _ATTRACT_MODE stays set: the next idle demo comes sooner
        bool levelFinished;   // _SPECTRE_MAIN's bVar2: the score table wants a finished level

        void Awake()
        {
            Instance = this;
            GameSettings.Load();
            director = GetComponent<RobotDirector>();
            if (director == null) director = gameObject.AddComponent<RobotDirector>();
            if (GetComponent<SimClock>() == null) gameObject.AddComponent<SimClock>();
            // The level's palette goes on a copy, not the material asset
            obstacleLook.shapes = robotLook.shapes = new Material(obstacleLook.shapes);
            demoPilot = player.GetComponent<DemoPilot>();
            if (demoPilot == null) demoPilot = player.gameObject.AddComponent<DemoPilot>();
            demoPilot.enabled = false;
        }

        void Start()
        {
            ApplySettings();
            ClearLevel();
        }

        // ---------- session control (called by the main menu) ----------

        // A restored game passes the score and lives it was saved with
        public void StartGame(VehicleSpec vehicle, int startLevel, bool demo, int score = 0, int lives = -1)
        {
            StopAllCoroutines();
            session = down = null;
            saving = false;
            Time.timeScale = 1f;
            Paused = false;
            Suspended = false;
            Demo = demo;
            if (demo) DemoHasRun = true;
            levelFinished = false;
            demoPilot.enabled = demo;
            demoStartFrame = Time.frameCount;
            if (demo) startLevel = NextDemoLevel();
            CurrentMatrix = MatrixLibrary.Current();
            ApplyVehicle(vehicle);
            ApplySettings();
            InGame = true;
            session = StartCoroutine(NewGame(Mathf.Max(1, startLevel), score, lives));
        }

        // The original's _LEVEL, and how the demo picks up from it: it carries on from the level the
        // last game reached, back to the first once past the fourth or after that level's fourth run
        static int lastLevel, demoLevel, demoRuns;

        static int NextDemoLevel()
        {
            if (lastLevel > 3) lastLevel = demoRuns = demoLevel = 0;
            if (demoLevel == lastLevel)
            {
                if (demoRuns++ > 2) lastLevel = 0;
            }
            else demoRuns = 0;
            demoLevel = lastLevel;
            return lastLevel + 1;
        }

        public void QuitToMenu()
        {
            Sfx.Play(Snd.Leave);
            EndSession(0, 0);
        }

        // Back from the menu to the paused game (Esc again, or the menu's resume)
        public void Resume()
        {
            if (!InGame || !Suspended) return;
            Suspended = false;
            Paused = false;
            resumedFrame = Time.frameCount;   // the Esc that resumed us mustn't re-suspend this frame
            Time.timeScale = 1f;
            ApplySettings();
            if (MusicPlayer.Instance != null)
            {
                // The song again, in case Options changed which styles are on
                if (CurrentMatrix != null) levelSong = MatrixMusic.TrackFor(levelSongId);
                MusicPlayer.Instance.ResumeGameMusic(CurrentMatrix != null ? levelSong : null);
            }
        }

        void EndSession(int finalScore, int finalLevel)
        {
            StopAllCoroutines();
            session = null;
            Time.timeScale = 1f;
            Paused = false;
            Suspended = false;
            Playing = false;
            InGame = false;
            Demo = false;
            demoPilot.enabled = false;
            player.Motor.SetInput(0f, 0f);
            ClearLevel();
            HideOverlays?.Invoke();
            ReturnedToMenu?.Invoke(finalScore, finalLevel);
        }

        void ApplyVehicle(VehicleSpec v)
        {
            player.Motor.speedPoints = v.Speed;
            player.Health.hitMultiplier = 10;
            player.Health.SetMaxShields(v.DamagePoints);
            player.Weapon.maxAmmo = v.MaxAmmo;
            player.Weapon.Refill();
        }

        // Options-dialog settings that affect the running scene
        public void ApplySettings()
        {
            Sfx.Enabled = GameSettings.SoundEffectsOn && !GameSettings.SoundMuted;
            ShowFloorDots();
            ApplyComplex();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) ApplyFilled(r);
            foreach (var r in director.Robots)
                if (r != null && (r.matrixType == 26 || !r.visible))
                    foreach (var part in r.GetComponentsInChildren<Renderer>()) part.enabled = false;
        }

        // Complex changed during a level: everything built from a shape takes the other column where it stands
        void ApplyComplex()
        {
            if (!InGame || shapes == null || !shapes.ComplexChanged) return;
            shapes.FollowComplex();
            var craft = player.GetComponent<PlayerModel>();
            if (craft != null) craft.Rebuild(shapes, obstacleLook.shapes, obstacleLook.edgeThickness);
            foreach (var r in director.Robots)
                if (r != null) RobotFactory.Reshape(r, shapes, robotLook, obstacleLook.edgeThickness);
            for (int i = 0; i < Obstacles.Count; i++)
            {
                var old = Obstacles[i];
                var was = old != null ? old.GetComponent<ObstacleBehaviour>() : null;
                if (was == null) continue;
                var go = ObstacleFactory.Build(was.type, shapes.ForType(was.type), obstacleLook);
                if (go == null) continue;
                go.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
                var now = go.GetComponent<ObstacleBehaviour>();
                now.heading = was.heading;
                player.Motor.GateReplaced(was, now);
                Obstacles[i] = go;
                Destroy(old);
            }
            Physics.SyncTransforms();
        }


        // Options "Filled": the solid faces of craft and obstacles (named "Panels"); off draws their edges insteads
        public static void ApplyFilled(Renderer r)
        {
            if (r == null) return;
            if (r.name == "Panels") r.enabled = GameSettings.DisplayFilled;
            else if (r.name == "Edges") r.enabled = !GameSettings.DisplayFilled;
            else if (r.name == "Textured") r.enabled = GameSettings.DisplayFilled && GameSettings.DisplayTextures;
            else if (r.name == "Flat") r.enabled = GameSettings.DisplayFilled && !GameSettings.DisplayTextures;
            else if (r.name == "Movie") r.enabled = GameSettings.DisplayFilled && GameSettings.DisplayMovies;
        }

        static void ApplyFilled(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) ApplyFilled(r);
        }

        IEnumerator NewGame(int startLevel, int score, int lives)
        {
            Score = score;
            Lives = lives > 0 ? lives : startingLives;
            Level = startLevel - 1;
            // Heavy weapons: none to start with, plus whatever the levels skipped would have given
            // (_CATCHUP_HINT)
            player.Weapon.ResetHeavyWeapons();
            var catchUp = MatrixLibrary.Current();
            if (catchUp != null)
            {
                var list = catchUp.Levels[Mathf.Clamp((int)GameSettings.Difficulty, 0, 2)];
                for (int i = 0; i < startLevel - 1 && list.Count > 0; i++) player.Weapon.Unlock(list[i % list.Count].tip);
            }
            yield return NextLevel();
        }

        IEnumerator NextLevel()
        {
            Level = Level >= 100 ? 1 : Level + 1;
            lastLevel = Level - 1;
            levelStartScore = Score;
            if (CurrentMatrix != null) levelTip = BuildMatrixLevel(Level);
            else
            {
                Plan = LevelPlan.For(Level);
                BuildGeneratedLevel(Plan);
                levelTip = Plan.tip;
            }
            yield return LevelBanner();
        }

        // _START_LEVEL, for every life: everything stops, the banner shows with its sound five ticks
        // in, and the level's song starts from the top as the banner goes
        IEnumerator LevelBanner()
        {
            Paused = false;   // P belongs to one run of _PLAY_LEVEL
            Sfx.Stop();
            if (MusicPlayer.Instance != null) MusicPlayer.Instance.Stop();
            ShowBanner?.Invoke($"LEVEL {Level}", levelTip);
            bannerSkipped = false;
            yield return BannerTicks(5);
            if (!bannerSkipped) Sfx.Play(Snd.LevelStart);
            yield return BannerTicks(27);
            HideOverlays?.Invoke();
            if (MusicPlayer.Instance != null)
            {
                if (CurrentMatrix != null) MusicPlayer.Instance.PlayTrackOrStop(levelSong);
                else MusicPlayer.Instance.PlayForLevel(Level);
            }
            Playing = true;
            player.Weapon.Announce();   // _ALT_WEAPON_TIMER, every life
        }

        bool bannerSkipped, saving;
        int levelStartScore;

        // Alt+S on the banner (_SAVE_GAME): the menu takes it from here, level, score and lives in hand
        public System.Action<int, int, int> SaveRequested;
        public void SaveFinished() => saving = false;

        // Space ends the banner early, and so does saving a game
        IEnumerator BannerTicks(int ticks)
        {
            for (float t = 0f; t < ticks / PlayerMotor.TickRate && !bannerSkipped; t += Time.deltaTime)
            {
                var keys = Keyboard.current;
                if (keys != null && keys.spaceKey.wasPressedThisFrame) bannerSkipped = true;
                if (keys != null && !Demo && keys.sKey.wasPressedThisFrame && (keys.leftAltKey.isPressed || keys.rightAltKey.isPressed))
                {
                    bannerSkipped = saving = true;
                    SaveRequested?.Invoke(Level - 1, levelStartScore, Lives);
                    while (saving) yield return null;
                }
                yield return null;
            }
        }

        readonly Dictionary<int, Mesh> dotMeshes = new Dictionary<int, Mesh>();

        // _DRAW_LEVEL: dotted lines 0x10000 apart both ways, the dots 0x2000 apart and halving each level
        void ShowFloorDots()
        {
            if (floorDots == null) return;
            int level = GameSettings.DrawLevel;
            floorDots.enabled = level > 0;
            if (level <= 0) return;
            if (!dotMeshes.TryGetValue(level, out var mesh) || mesh == null) dotMeshes[level] = mesh = BuildDots(0x4000 >> level);
            floorDots.GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        static Mesh BuildDots(int spacing)
        {
            const int Line = 0x10000, Edge = 0x40000;
            const float Size = 0.15f, Height = 0.012f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Dot(int x, int y)
            {
                var c = new Vector3(x, 0f, y) * PlayerMotor.UnitsToMetres;
                int b = verts.Count;
                verts.Add(new Vector3(c.x - Size, Height, c.z - Size));
                verts.Add(new Vector3(c.x - Size, Height, c.z + Size));
                verts.Add(new Vector3(c.x + Size, Height, c.z + Size));
                verts.Add(new Vector3(c.x + Size, Height, c.z - Size));
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            for (int a = -Edge; a <= Edge; a += Line)
                for (int d = -Edge; d <= Edge; d += spacing)
                {
                    Dot(a, d);
                    if (d % Line != 0) Dot(d, a);   // the crossings are already there
                }
            var mesh = new Mesh { name = "FloorDots", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        int godKeys;   // how much of G, O, D has been typed

        // The original's other keys while playing. Its S, D are taken by the modern driving keys, so those
        // two live on F2 and F3; the rest are where it had them.
        // _PLAY_LEVEL's _GOD: G arms it, O takes it a step on or clears it, and D opens the map view if
        // both came first. Other keys between them don't matter.
        void TypeGod(Keyboard k)
        {
            if (k.gKey.wasPressedThisFrame) godKeys = 1;
            if (k.oKey.wasPressedThisFrame) godKeys = godKeys == 1 ? 2 : 0;
            if (k.dKey.wasPressedThisFrame)
            {
                if (godKeys == 2) FindFirstObjectByType<ViewCamera>().mode = ViewMode.Map;
                godKeys = 0;
            }
        }

        void DisplayKeys(Keyboard k)
        {
            bool changed = false;
            if (GameKeys.Sound.Pressed) { GameSettings.SoundMuted = !GameSettings.SoundMuted; changed = true; }
            TypeGod(k);
            if (GameKeys.FloorDots.Pressed) { GameSettings.DrawLevel = (GameSettings.DrawLevel + 1) % 4; changed = true; }
            if (GameKeys.Textures.Pressed) { GameSettings.DisplayTextures = !GameSettings.DisplayTextures; changed = true; }
            if (GameKeys.Movies.Pressed) { GameSettings.DisplayMovies = !GameSettings.DisplayMovies; changed = true; }
            if (GameKeys.Wire.Pressed && !Demo) { GameSettings.DisplayFilled = !GameSettings.DisplayFilled; changed = true; }
            if (changed) ApplySettings();
        }

        void Update()
        {
            if (!InGame || Suspended || saving) return;
            var k = Keyboard.current;
            if (k != null) DisplayKeys(k);
            MovieTextures.Advance(Time.unscaledDeltaTime);

            if (Demo)
            {
                bool anyKey = (k != null && k.anyKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
                if (anyKey && Time.frameCount != demoStartFrame) { QuitToMenu(); return; }   // not the click that started it
            }
            else if (k != null)
            {
                if (k.escapeKey.wasPressedThisFrame && Time.frameCount != resumedFrame) { SuspendToMenu(); return; }
                if (GameKeys.Pause.Pressed) TogglePause();
            }

        }

        // One original tick, from SimClock (_PLAY_LEVEL): the bonus count-down and the slow self-repair
        public void SimTick()
        {
            if (Demo) demoPilot.SimTick();
            if (!Playing || Paused) return;
            if (++LevelTicks % 18 == 0) Bonus = Mathf.Max(0, Bonus - 4);
            if (++repairTimer > RepairTicks)
            {
                repairTimer = 0;
                player.Health.Restore(10);
                Sfx.Play(Snd.TimeTick);   // full health or not
            }
        }

        public int LevelTicks { get; private set; }
        string levelSong, levelTip;
        int levelSongId;
        int repairTimer;

        // _TIME_ALLOWED and the bonus, at the start of a level and of every life
        void StartBonus()
        {
            TimeAllowed = FlagsTotal * 10 + (director.Robots.Count + 1) * 10 + 30;
            Bonus = TimeAllowed * 4;
            LevelTicks = 0;
        }

        // Esc: freeze the game and bring up the main menu over it; Esc again resumes
        void SuspendToMenu()
        {
            if (Paused) HideOverlays?.Invoke();
            Suspended = true;
            Paused = true;
            Time.timeScale = 0f;
            player.Motor.SetInput(0f, 0f);
            if (MusicPlayer.Instance != null) MusicPlayer.Instance.PauseGameMusic();
            Suspend?.Invoke();
        }

        void TogglePause()
        {
            // Not a freeze: _PLAY_LEVEL stops the bonus, the robots and the shots, while the craft keeps
            // falling and floating, pickups still work and the death and level-end timers run on
            Paused = !Paused;
            if (Paused) ShowBanner?.Invoke("PAUSED", $"PRESS {GameKeys.First(GameKeys.Pause)} TO CONTINUE");
            else HideOverlays?.Invoke();
        }

        // ---------- level construction ----------

        void ClearLevel()
        {
            foreach (var f in Flags) if (f != null) Destroy(f.gameObject);
            foreach (var d in Dumps) if (d != null) Destroy(d.gameObject);
            foreach (var r in Robots) if (r != null) Destroy(r.gameObject);
            foreach (var o in Obstacles) if (o != null) Destroy(o);
            foreach (var j in jumpPads) if (j != null) Destroy(j.gameObject);
            jumpPads.Clear();
            Flags.Clear(); flagSpots.Clear(); Dumps.Clear(); Robots.Clear(); Obstacles.Clear();
            if (director != null) director.Clear();
            System.Array.Clear(flagGuards, 0, flagGuards.Length);
            robotsLeft = bankedBonus = 0;
            FlagsTotal = FlagsCollected = 0;
        }

        // Grid units (0..1024, y down the map) to arena metres
        static Vector3 GridToWorld(int x, int y) => new Vector3((x - 512) * GridScale, 0f, (512 - y) * GridScale);

        string BuildMatrixLevel(int levelNumber)
        {
            var m = CurrentMatrix;
            int diff = (int)GameSettings.Difficulty;
            // The demo has its own levels, wave 778 in BASIC.MAT, played with this matrix's shapes
            var source = Demo ? MatrixLibrary.Basic() ?? m : m;
            var levels = source.Levels[Mathf.Clamp(diff, 0, 2)];
            int index = (levelNumber - 1) % levels.Count;
            int lap = (levelNumber - 1) / levels.Count;   // after the last level the list repeats, its robots stronger
            var lv = levels[index];
            // The level's tip number also unlocks a heavy weapon, the first time round (_LOAD_WAVE)
            if (lap == 0) player.Weapon.Unlock(lv.tip);

            ClearLevel();
            director.Shots.ResetCounts();
            SpectrePalette.Apply(obstacleLook.shapes, levelNumber - 1);
            SetHorizonColor(SpectrePalette.Sky(levelNumber - 1));
            UseShapes(m);

            // The grid: its first entries are the flags (_LOAD_WRLD), as many as _SETUP_LEVEL allows -
            // 3 on the first two levels, one more every two levels, 6 at most - whatever their type;
            // the rest are obstacles where the designer put them (spare flag entries have no shape)
            var flagSpots = new List<Vector3>();
            int maxFlags = Mathf.Min(6, ((levelNumber - 1) >> 1) + 3);
            if (source.Grids.TryGetValue(lv.grid, out var grid))
            {
                foreach (var o in grid.obstacles)
                {
                    var pos = GridToWorld(o.x, o.y);
                    if (flagSpots.Count < maxFlags) { flagSpots.Add(pos); continue; }
                    SpawnObstacle(o.type, pos, o.heading);
                }
            }
            Physics.SyncTransforms();

            foreach (var p in flagSpots) Flags.Add(Spawn(flagPrefab, p, Quaternion.identity));
            if (Flags.Count == 0)
                for (int i = 0; i < 5 + levelNumber; i++)
                    if (TryFindSpawnPoint(out var p, 25f)) Flags.Add(Spawn(flagPrefab, p, Quaternion.identity));

            FlagsTotal = Flags.Count;
            FlagsCollected = 0;
            foreach (var f in Flags) flagSpots.Add(f.transform.position);
            Plan = new LevelPlan { level = levelNumber, robotShields = 1, wallColor = LevelPlan.WallColor(levelNumber) };

            // The enemy set (_ADD_ENEMY): a count between its two numbers when the first is the smaller,
            // else exactly the second; each robot comes back 'regen' more times. Strength rises by one a lap.
            if (source.EnemySets.TryGetValue(lv.enemySet, out var set))
            {
                foreach (var e in set)
                {
                    int count = e.max < e.min ? Random.Range(e.max, e.min + 1) : e.min;
                    var data = source.TryGetEnemyData(lv.enemyData, e.type, out var d) ? Robot.Data.From(d, lap) : DefaultRobotData(e.type, lap);
                    for (int i = 0; i < count; i++) SpawnRobot(e.type, data, Mathf.Max(0, e.regen));
                }
            }

            // Ten ammo dumps, their kinds weighted by the enemy data's type-5 entry (_LOAD_BVAL). The robots
            // are all still at the middle, so the dumps keep clear of it as they do in the original.
            DumpOdds = source.TryGetEnemyData(lv.enemyData, 5, out var fuel)
                ? new[] { fuel.strength, fuel.collideDamage, fuel.attackRange / 7, fuel.firingRange / 7 }
                : DefaultDumpOdds;
            for (int i = 0; i < DumpsPerLevel; i++) SpawnDump();
            // The first level's shortcuts to levels 5 and 10 (_BUILD_WORLD)
            if (levelNumber == 1 && !Demo)
            {
                jumpPads.Add(JumpPad.Create(5, 5000, PlaceXY(), obstacleLook.transport, obstacleLook.edgeThickness));
                jumpPads.Add(JumpPad.Create(10, 10000, PlaceXY(), obstacleLook.transport, obstacleLook.edgeThickness));
            }

            PlacePlayerAtStart();
            PlaceRobotsAgain();

            // Acid burns for the level's Smart Acid collision damage, or 10 (_LOAD_BVAL)
            AcidDamage = source.TryGetEnemyData(lv.enemyData, 23, out var acid) ? acid.collideDamage : 10;
            StartBonus();

            levelSongId = lv.song;
            levelSong = MatrixMusic.TrackFor(lv.song);
            return lap == 0 ? m.Tip(lv.tip) : "";   // _LOAD_WAVE: no tip once the list repeats
        }

        void BuildGeneratedLevel(LevelPlan plan)
        {
            ClearLevel();
            director.Shots.ResetCounts();
            SetHorizonColor(plan.wallColor);
            UseShapes(null);

            var rng = new System.Random(1991);
            for (int i = 0; i < 14; i++)
            {
                var pos = new Vector3((float)(rng.NextDouble() * 2 - 1) * (arenaHalfSize - 15f), 0f, (float)(rng.NextDouble() * 2 - 1) * (arenaHalfSize - 15f));
                if (pos.magnitude > 20f) SpawnObstacle(fallbackObstacleType, pos, rng.Next(0, 90));
            }
            Physics.SyncTransforms();

            for (int i = 0; i < plan.flags; i++)
                if (TryFindSpawnPoint(out var p, 25f)) Flags.Add(Spawn(flagPrefab, p, Quaternion.identity));
            DumpOdds = DefaultDumpOdds;
            for (int i = 0; i < plan.ammoDumps; i++) SpawnDump();

            FlagsTotal = Flags.Count;
            FlagsCollected = 0;
            foreach (var f in Flags) flagSpots.Add(f.transform.position);

            // Without a matrix: Robot Tanks, Turrets, Orbiters and Slicers with Central Systems' enemy data
            void Add(int type, int n) { for (int i = 0; i < n; i++) SpawnRobot(type, DefaultRobotData(type, 0), 99); }
            Add(7, plan.rovers);
            Add(20, plan.turrets);
            Add(35, plan.orbiters);
            Add(21, plan.slicers);
            PlacePlayerAtStart();
            PlaceRobotsAgain();
            StartBonus();
        }

        static Robot.Data DefaultRobotData(int type, int lap)
        {
            var central = MatrixLibrary.Central();
            if (central != null && central.TryGetEnemyData(10000, type, out var d)) return Robot.Data.From(d, lap);
            return new Robot.Data { strength = 1, chaseDist = 1750, firingRange = 560, interval = 120, speed = 5, turn = 1, score = 25, weapon = 0 };
        }

        // The original starts every level 15 grid units south of the middle, facing north, dropping in
        // ---------- ammo dumps ----------

        const int DumpsPerLevel = 10;
        // Out of ten: green (ammo), white (repair), orange (shield), blue (wings) - the original's defaults
        static readonly int[] DefaultDumpOdds = { 5, 2, 1, 2 };
        int[] DumpOdds = DefaultDumpOdds;

        void SpawnDump()
        {
            var d = Spawn(ammoDumpPrefab, PlaceXY(), Quaternion.identity);
            d.SetKind(PickDumpKind());
            Dumps.Add(d);
        }

        // _PICK_FUEL_TYPE: a roll of 0-9 against the green / white / orange counts, blue otherwise
        AmmoDump.Kind PickDumpKind()
        {
            int roll = Random.Range(0, 10);
            for (int k = 0; k < 3; k++)
            {
                if (roll < DumpOdds[k]) return (AmmoDump.Kind)k;
                roll -= DumpOdds[k];
            }
            return AmmoDump.Kind.Blue;
        }

        // A used dump re-rolls its kind and moves somewhere out of sight
        public void RelocateDump(AmmoDump d)
        {
            d.transform.position = HiddenSpot();
            d.SetKind(PickDumpKind());
        }

        void PlacePlayerAtStart()
        {
            // _PLAY_LEVEL: every life starts at a random clear spot (_PLACE_XY), dropping in
            var start = PlaceXY();
            if (player.Health.IsDead) player.Health.Revive(start, Quaternion.identity);
            player.Motor.Place(start, 0f, dropIn: true);
            player.Motor.FaceRandomly();
            player.Shields.Clear();
            player.Health.Restore(player.Health.maxShields);
            player.ResetForLife();
        }

        void UseShapes(MatrixFile m)
        {
            var central = MatrixLibrary.Central();
            if (central == null) Debug.LogWarning("Spectre VR-EX: Central Systems matrix not found - matrices without shapes will have no obstacles");
            shapes = new ShapeSet(m, central);
            shapes.NewLevel();
            MovieTextures.NewLevel();
            var craft = player.GetComponent<PlayerModel>();
            if (craft != null) craft.Rebuild(shapes, obstacleLook.shapes, obstacleLook.edgeThickness);
        }

        // Types with no shape (as in the original) aren't placed at all
        void SpawnObstacle(int type, Vector3 pos, int heading)
        {
            var go = ObstacleFactory.Build(type, shapes?.ForType(type), obstacleLook);
            if (go == null) return;
            // The original's heading byte; its shapes face the opposite way to how its tanks travel,
            // which puts the model at yaw -degrees here
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, -heading, 0f));
            go.GetComponent<ObstacleBehaviour>().heading = Mathf.RoundToInt(heading * 256f / 360f) & 0xFF;
            ApplyFilled(go);
            Obstacles.Add(go);
        }

        T Spawn<T>(T prefab, Vector3 pos, Quaternion rot) where T : Component
        {
            var c = Instantiate(prefab, pos, rot);
            ApplyFilled(c.gameObject);
            return c;
        }

        // _ADD_ENEMY + _PLACE_TANKS: build the robot from its shape, place it, and hand it to the director
        void SpawnRobot(int type, Robot.Data data, int regen)
        {
            var robot = RobotFactory.Build(type, shapes, robotLook, obstacleLook.edgeThickness);
            if (robot == null) return;
            robot.Setup(type, data);
            robot.index = director.Robots.Count + 1;
            robot.regenLeft = regen;
            robot.Place(Vector3.zero);   // _ADD_ENEMY: the middle, until _PLACE_TANKS
            ApplyFilled(robot.gameObject);
            if (type == 26) foreach (var r in robot.GetComponentsInChildren<Renderer>()) r.enabled = false;
            director.Robots.Add(robot);
            Robots.Add(robot.Health);
            robotsLeft += regen + 1;
        }

        // Turrets, Orbiters and Bouncers start by a flag; everything else somewhere out of sight
        Vector3 RobotSpot(Robot robot)
        {
            return robot.IsFlagGuard ? PlaceNearFlag() : HiddenSpot();
        }

        // _PLACE_NEAR_FLAG: the flag with the fewest robots so far, collected or not, 0x3C00 in from it
        // toward the middle
        Vector3 PlaceNearFlag()
        {
            int n = Mathf.Min(flagSpots.Count, flagGuards.Length);
            if (n == 0) return Vector3.zero;
            int best = 0;
            for (int i = 1; i < n; i++) if (flagGuards[i] < flagGuards[best]) best = i;
            flagGuards[best]++;
            Vector3 f = flagSpots[best];
            float step = 0x3C00 * PlayerMotor.UnitsToMetres;
            return new Vector3(f.x < 0.0001f ? f.x + step : f.x - step, 0f, f.z < 0.0001f ? f.z + step : f.z - step);
        }

        const int PlaceTries = 4000;

        // _PLACE_XY: a random point on a 0x800 grid out to the arena's edge, clear of every active object
        // by a margin that starts at 0x3200 and shrinks with each try
        public Vector3 PlaceXY()
        {
            Physics.SyncTransforms();
            long x = 0, y = 0;
            int margin = 0x3200;
            for (int i = 0; i < PlaceTries; i++)
            {
                x = GridSpot();
                y = GridSpot();
                int test;
                if (margin - 0x100 < 0x301)
                {
                    margin -= 0x100;
                    test = 0x300;
                }
                else test = margin -= 0x200;
                if (!NearAnything(x, y, test)) break;
            }
            return Spot(x, y);
        }

        // _INVISIBLE_PLACE_XY: at least 0x26000 from the player on both axes, clear of obstacles, dumps,
        // flags and jump pads. The original measures from where the player's object last was, which
        // at the start of a life is the wreck, not the new start; this measures from the craft.
        public Vector3 HiddenSpot()
        {
            Physics.SyncTransforms();
            long meX = OriginalMath.ToUnits(player.transform.position.x), meY = OriginalMath.ToUnits(player.transform.position.z);
            long x = 0, y = 0;
            int margin = 0x3200;
            for (int i = 0; i < PlaceTries; i++)
            {
                x = GridSpot();
                y = GridSpot();
                long away = System.Math.Min(System.Math.Abs(meX - x), System.Math.Abs(meY - y)) >> 8;
                if (away > 0x260 && (margin -= 0x100) < 0x301) margin = 0x300;
                if (away >= 0x260 && !NearObstacle(x, y, margin) && !NearDump(x, y, margin) && !NearFlag(x, y, margin) && !NearPad(x, y, margin)) break;
            }
            return Spot(x, y);
        }

        static long GridSpot() => (0x7F - Random.Range(0, 0xFE)) * 0x800L;
        static Vector3 Spot(long x, long y) => new Vector3(OriginalMath.ToMetres(x), 0f, OriginalMath.ToMetres(y));

        // The largest of |dx| and |dy|, as the original's _CHECK_NEAR_ routines measure
        static bool Within(long x, long y, Vector3 at, long reach) =>
            System.Math.Max(System.Math.Abs(OriginalMath.ToUnits(at.x) - x), System.Math.Abs(OriginalMath.ToUnits(at.z) - y)) <= reach;

        // Object +0x61: how far its shape reaches from its centre
        static long Reach(GameObject go)
        {
            float r = 0f;
            Vector3 at = go.transform.position;
            foreach (var c in go.GetComponentsInChildren<Collider>())
            {
                Bounds b = c.bounds;
                r = Mathf.Max(r, Mathf.Max(Mathf.Abs(b.min.x - at.x), Mathf.Abs(b.max.x - at.x)));
                r = Mathf.Max(r, Mathf.Max(Mathf.Abs(b.min.z - at.z), Mathf.Abs(b.max.z - at.z)));
            }
            return OriginalMath.ToUnits(r);
        }

        // _CHECK_NEAR_SOMETHING: everything in play, at the size of its shape
        bool NearAnything(long x, long y, int margin)
        {
            if (NearObstacle(x, y, margin) || Within(x, y, player.transform.position, margin + Reach(player.gameObject))) return true;
            foreach (var f in Flags) if (f != null && Within(x, y, f.transform.position, margin + Reach(f.gameObject))) return true;
            foreach (var d in Dumps) if (d != null && Within(x, y, d.transform.position, margin + Reach(d.gameObject))) return true;
            foreach (var p in jumpPads) if (p != null && Within(x, y, p.transform.position, margin + Reach(p.gameObject))) return true;
            foreach (var r in director.Robots) if (r != null && r.visible && Within(x, y, r.transform.position, margin + Reach(r.gameObject))) return true;
            return false;
        }

        bool NearObstacle(long x, long y, int margin)
        {
            foreach (var o in Obstacles) if (o != null && Within(x, y, o.transform.position, margin + Reach(o))) return true;
            return false;
        }

        bool NearDump(long x, long y, int margin)
        {
            foreach (var d in Dumps) if (d != null && Within(x, y, d.transform.position, margin + 0x500)) return true;
            return false;
        }

        bool NearFlag(long x, long y, int margin)
        {
            foreach (var f in Flags) if (f != null && Within(x, y, f.transform.position, margin + 0xF00)) return true;
            return false;
        }

        bool NearPad(long x, long y, int margin)
        {
            foreach (var p in jumpPads) if (p != null && Within(x, y, p.transform.position, margin + 0x500)) return true;
            return false;
        }

        // _REPLACE_TANK, ten ticks after a robot is destroyed: back at once, if it has regenerations left
        public void ReplaceRobot(Robot robot)
        {
            if (robot.regenLeft <= 0 || !Playing) return;
            robot.regenLeft--;
            robot.Replaced(RobotSpot(robot));
            ApplyFilled(robot.gameObject);
        }

        // Random open spot on the arena floor, clear of obstacles and at least minDistance from the player.
        public bool TryFindSpawnPoint(out Vector3 point, float minDistanceFromPlayer, bool nearCentre = false)
        {
            float range = nearCentre ? 20f : arenaHalfSize - 8f;
            for (int i = 0; i < 300; i++)
            {
                var p = new Vector3(Random.Range(-range, range), 0.1f, Random.Range(-range, range));
                if (minDistanceFromPlayer > 0f && player != null && Vector3.Distance(p, player.transform.position) < minDistanceFromPlayer) continue;
                if (Physics.CheckBox(p + Vector3.up * 2f, new Vector3(3.5f, 1.5f, 3.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Collide)) continue;
                point = p;
                return true;
            }
            point = Vector3.zero + Vector3.up * 0.1f;
            return false;
        }

        // With a skybox behind it the band has to blend over the picture, or its fade cuts a black curve
        public void UseSkyBand(bool on)
        {
            if (horizon == null) return;
            if (horizonSolid == null) horizonSolid = horizon.material;
            if (on && horizonBand == null)
            {
                horizonBand = new Material(Resources.Load<Material>("HorizonBand"));
                horizonBand.SetTexture("_UnlitColorMap", horizonSolid.GetTexture("_EmissiveColorMap"));
                horizonBand.SetColor("_UnlitColor", horizonColor);
            }
            horizon.sharedMaterial = on ? horizonBand : horizonSolid;
        }

        void SetHorizonColor(Color c)
        {
            if (horizon == null) return;
            horizonColor = c;
            if (horizonBand != null) horizonBand.SetColor("_UnlitColor", c);
            if (horizonSolid == null) horizonSolid = horizon.material;
            var m = horizonSolid;
            if (horizonNits < 0f)
            {
                Color cur = m.GetColor("_EmissiveColor");
                horizonNits = Mathf.Max(cur.r, Mathf.Max(cur.g, cur.b));
            }
            m.SetColor("_EmissiveColor", c * horizonNits);
        }

        // ---------- events ----------

        // Extra tanks the way SVRWIN.EXE gives them: one every 5000 points below 10000, every 10000
        // below 60000, then every 20000, nine at most (sound NEWMAN). It never reads the matrix's sstf.
        public void AddScore(int points)
        {
            int before = Score;
            Score += points;
            int step = before < 10000 ? 5000 : before < 60000 ? 10000 : 20000;
            if (before / step != Score / step && Lives < MaxLives)
            {
                Lives++;
                Sfx.Play(Snd.NewLife);
            }
        }

        public void OnFlagCollected(Flag flag)
        {
            int slot = Flags.IndexOf(flag);
            if (slot < 0) return;
            Flags[slot] = null;
            FlagsCollected++;
            AddScore(flagPoints);
            int left = FlagsTotal - FlagsCollected;
            Sfx.Play(left == 0 ? Snd.Warp : left == 1 ? Snd.OneMoreFlag : Snd.FlagPickup);
            if (left == 0 && Playing) FinishLevel();
        }

        // The last flag or a jump pad, even for a wreck: if the level ends first no life is lost. If the
        // wreck's time will run out first the life goes, and the level ends just after it restarts.
        void FinishLevel()
        {
            player.Motor.Warp();
            if (down != null)
            {
                if (Time.time - downAt > (DeadWaitTicks - LevelEndTicks) / PlayerMotor.TickRate) return;
                StopCoroutine(down);
                down = null;
            }
            session = StartCoroutine(LevelComplete());
        }

        // A first-level jump pad (_PLAY_LEVEL): the score becomes 5000 / 10000, the skipped levels'
        // heavy weapons are unlocked (_CATCHUP_HINT, up to the pad's level - 2), damage is mended and
        // the level ends at once
        public void TakeJumpPad(JumpPad pad)
        {
            if (!Playing || FlagsCollected >= FlagsTotal) return;   // the level is already ending
            AddScore(pad.score - Score);
            var m = CurrentMatrix;
            if (m != null)
            {
                var list = m.Levels[Mathf.Clamp((int)GameSettings.Difficulty, 0, 2)];
                for (int i = 0; i < pad.toLevel - 2 && list.Count > 0; i++) player.Weapon.Unlock(list[i % list.Count].tip);
            }
            Level = pad.toLevel - 1;
            Sfx.Play(Snd.Warp);
            player.Health.Restore(player.Health.maxShields);
            foreach (var j in jumpPads) if (j != null) Destroy(j.gameObject);
            jumpPads.Clear();
            FlagsCollected = FlagsTotal;
            FinishLevel();
        }

        readonly List<JumpPad> jumpPads = new List<JumpPad>();
        public List<JumpPad> JumpPads => jumpPads;

        // _END_LEVEL, 18 ticks after the last flag: the bonus left goes on the score
        IEnumerator LevelComplete()
        {
            yield return new WaitForSeconds(LevelEndTicks / PlayerMotor.TickRate);
            Playing = false;
            levelFinished = true;
            Sfx.Play(Snd.Award);

            AddScore(Bonus);

            // _SHOW_RECAP: the bars grow over 16 ticks, then 18 more or a key press. The original divides
            // seconds taken by seconds allowed, which rewards being slow; here it's the other way up, so
            // finishing inside the time allowed is Perfect and the rate falls after that.
            var shots = director.Shots;
            var rates = new[]
            {
                Mathf.Min(Rate(TimeAllowed, LevelTicks / 18), 0x80),
                Rate(shots.MyHits, shots.MyShots),
                Rate(shots.RobotShots - shots.RobotHits, shots.RobotShots),
            };
            var tick = new WaitForSeconds(1f / PlayerMotor.TickRate);
            for (int step = 0; step < 16; step++)
            {
                ShowRecap?.Invoke(rates, step);
                yield return tick;
            }
            for (float t = 0f; t < 18f / PlayerMotor.TickRate; t += Time.deltaTime)
            {
                var keys = UnityEngine.InputSystem.Keyboard.current;
                if (keys != null && keys.anyKey.wasPressedThisFrame) break;
                yield return null;
            }
            HideOverlays?.Invoke();
            // The demo stops after the fourth level
            if (Demo && Level >= 4)
            {
                lastLevel = Level;
                EndSession(0, 0);
                yield break;
            }
            yield return NextLevel();
        }

        static int Rate(int part, int whole) => whole == 0 ? 0x80 : part * 0x80 / whole;

        public void OnDestroyed(Health h)
        {
            if (!InGame) return;
            if (h.team == Team.Player)
            {
                director.Shots.ExplodePlayer();
                DeRes.Play(player.gameObject, 0x5A, DeadTicks / PlayerMotor.TickRate, false);
                down = StartCoroutine(PlayerDown());
                downAt = Time.time;
                return;
            }

            // A robot (_MOVE_SHOTS): its bonus is banked, all of them paid out with the last robot; it
            // throws debris, and the director brings it back ten ticks later if it has regenerations left
            var robot = h.GetComponent<Robot>();
            if (robot == null) return;
            robot.Killed();
            director.Shots.Explode(robot);
            DeRes.Play(robot.gameObject, 10, 10f / PlayerMotor.TickRate, false);
            bankedBonus += Mathf.Max(0, robot.data.bonus);
            if (--robotsLeft == 0)
            {
                Sfx.Play(Snd.AllRobots);
                AddScore(bankedBonus);
                bankedBonus = 0;
            }
        }

        // Destroyed (_PLAY_LEVEL): 50 ticks later a life goes, and the level starts again with its banner
        // and song from a new spot, the flags and robots as they were, shots cleared and the bonus restarted
        IEnumerator PlayerDown()
        {
            Sfx.Play(Snd.PlayerDestroyed);
            yield return new WaitForSeconds(DeadWaitTicks / PlayerMotor.TickRate);
            down = null;
            Lives--;

            Playing = false;
            if (Lives <= 0)
            {
                // The demo just stops: no Game Over, nothing for the score table
                if (Demo)
                {
                    EndSession(0, Level);
                    yield break;
                }
                yield return SpinGameOver();
                EndSession(levelFinished ? Score : 0, Level);   // _SPECTRE_MAIN: no finished level, no score table
                yield break;
            }

            director.Shots.Clear();
            PlacePlayerAtStart();
            PlaceRobotsAgain();
            StartBonus();
            yield return LevelBanner();
            if (FlagsTotal > 0 && FlagsCollected >= FlagsTotal) FinishLevel();
        }

        // _GAME_OVER / _SPIN_UP: the words spin in over 33 ticks, then hold for 15
        IEnumerator SpinGameOver()
        {
            var tick = new WaitForSeconds(1f / PlayerMotor.TickRate);
            for (int step = 0; step < 0x21; step++)
            {
                GameOverSpin?.Invoke(step);
                yield return tick;
            }
            GameOverSpin?.Invoke(-1);
            yield return new WaitForSeconds(15f / PlayerMotor.TickRate);
            HideOverlays?.Invoke();
        }

        // _PLACE_TANKS, at the start of every life: each robot still in play goes somewhere new
        void PlaceRobotsAgain()
        {
            System.Array.Clear(flagGuards, 0, flagGuards.Length);
            foreach (var r in director.Robots)
                if (r != null && r.visible) r.Place(RobotSpot(r));
            ResetRobotChase();
        }

        public void ResetRobotChase()
        {
            foreach (var r in director.Robots) if (r != null) r.ResetChase();
        }
    }
}
