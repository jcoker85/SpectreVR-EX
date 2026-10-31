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
        public Renderer[] wallRenderers;
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
        int resumedFrame = -1;
        public bool Demo { get; private set; }
        public LevelPlan Plan { get; private set; }
        public MatrixFile CurrentMatrix { get; private set; }

        public readonly List<Flag> Flags = new List<Flag>();
        public readonly List<AmmoDump> Dumps = new List<AmmoDump>();
        public readonly List<Health> Robots = new List<Health>();
        public readonly List<GameObject> Obstacles = new List<GameObject>();
        public int AcidDamage { get; private set; } = 10;   // raw points every third tick in acid

        public System.Action<string, string> ShowBanner;           // big title, red tip
        public System.Action<int[], int> ShowRecap;                 // Quickness, Accuracy, Avoidance out of 128; animation step
        public System.Action HideOverlays;
        public System.Action<int, int> ReturnedToMenu;              // final score and level (0 if abandoned)
        public System.Action Suspend;                               // Esc in game: show the menu over the paused game

        // The robots run on the original's tick (RobotDirector); _TANKS_LEFT counts every robot still to
        // be destroyed, regenerations included, and the destroyed robots' bonuses are paid when it hits 0
        RobotDirector director;
        int robotsLeft, bankedBonus;
        readonly int[] flagGuards = new int[64];   // _FLAG_TURRETS: robots placed by each flag
        public ShapeSet Shapes => shapes;
        float wallNits = -1f;
        float levelDifficulty = 1f;
        Coroutine session;
        DemoPilot demoPilot;

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

        public void StartGame(VehicleSpec vehicle, int startLevel, bool demo)
        {
            StopAllCoroutines();
            session = null;
            Time.timeScale = 1f;
            Paused = false;
            Suspended = false;
            Demo = demo;
            demoPilot.enabled = demo;
            CurrentMatrix = MatrixLibrary.Current();
            ApplyVehicle(vehicle);
            ApplySettings();
            InGame = true;
            session = StartCoroutine(NewGame(Mathf.Max(1, startLevel)));
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
            if (MusicPlayer.Instance != null) MusicPlayer.Instance.ResumeGameMusic();
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
            Sfx.Enabled = GameSettings.SoundEffectsOn;
            ApplyComplex(GameSettings.DisplayComplex);
            if (floorDots != null) floorDots.enabled = GameSettings.DisplayTextures;
            foreach (var r in wallRenderers) if (r != null) r.enabled = GameSettings.DisplayTextures;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) ApplyFilled(r);
        }

        float aoIntensity = -1f;

        // Options "Complex": ray-traced reflections, global illumination, ambient occlusion and shadows.
        // Off forces each effect off in the arena profile rather than deactivating it, because a
        // deactivated component falls back to HDRP's default profile (which has screen-space reflections on).
        void ApplyComplex(bool on)
        {
            var vol = FindFirstObjectByType<Volume>();
            if (vol != null)
            {
                var profile = vol.profile;   // runtime instance, so the hand-tuned asset on disk is untouched
                if (profile.TryGet<ScreenSpaceReflection>(out var ssr)) { ssr.active = true; ssr.enabled.Override(on); }
                if (profile.TryGet<GlobalIllumination>(out var gi)) { gi.active = true; gi.enable.Override(on); }
                if (profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ao))
                {
                    if (aoIntensity < 0f) aoIntensity = ao.intensity.value;
                    ao.active = true;
                    ao.intensity.Override(on ? aoIntensity : 0f);
                }
            }
            foreach (var light in FindObjectsByType<HDAdditionalLightData>(FindObjectsSortMode.None))
                light.useRayTracedShadows = on;
        }

        // Options "Filled": the solid faces of craft and obstacles (named "Panels"); off draws their edges insteads
        public static void ApplyFilled(Renderer r)
        {
            if (r == null) return;
            if (r.name == "Panels") r.enabled = GameSettings.DisplayFilled;
            else if (r.name == "Edges") r.enabled = !GameSettings.DisplayFilled;
        }

        static void ApplyFilled(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) ApplyFilled(r);
        }

        IEnumerator NewGame(int startLevel)
        {
            Score = 0;
            Lives = startingLives;
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
            Level++;
            string tip;
            if (CurrentMatrix != null) tip = BuildMatrixLevel(Level);
            else
            {
                Plan = LevelPlan.For(Level);
                levelDifficulty = Plan.difficulty * GameSettings.DifficultyMultiplier;
                BuildGeneratedLevel(Plan);
                tip = Plan.tip;
            }

            // _START_LEVEL: the music stops, the banner shows with its sound five ticks in, and the
            // level's song starts as the banner goes
            if (MusicPlayer.Instance != null) MusicPlayer.Instance.Stop();
            ShowBanner?.Invoke($"LEVEL {Level}", tip);
            yield return new WaitForSeconds(5f / PlayerMotor.TickRate);
            Sfx.Play(Snd.LevelStart);
            yield return new WaitForSeconds(27f / PlayerMotor.TickRate);
            HideOverlays?.Invoke();
            if (MusicPlayer.Instance != null)
            {
                if (CurrentMatrix != null) MusicPlayer.Instance.PlayTrackOrStop(levelSong);
                else MusicPlayer.Instance.PlayForLevel(Level);
            }
            Playing = true;
        }

        void Update()
        {
            if (!InGame || Suspended) return;
            var k = Keyboard.current;

            if (Demo)
            {
                bool anyKey = (k != null && k.anyKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
                if (anyKey) { QuitToMenu(); return; }
            }
            else if (k != null)
            {
                if (k.escapeKey.wasPressedThisFrame && Time.frameCount != resumedFrame) { SuspendToMenu(); return; }
                if (k.pKey.wasPressedThisFrame) TogglePause();
            }

        }

        // One original tick, from SimClock (_PLAY_LEVEL): the bonus count-down and the slow self-repair
        public void SimTick()
        {
            if (!Playing || Paused || player.Health.IsDead) return;
            if (++LevelTicks % 18 == 0) Bonus = Mathf.Max(0, Bonus - 4);
            if (++repairTimer > RepairTicks)
            {
                repairTimer = 0;
                if (player.Health.Restore(10)) Sfx.Play(Snd.TimeTick);
            }
        }

        public int LevelTicks { get; private set; }
        string levelSong;
        int repairTimer;

        // _TIME_ALLOWED and the bonus, at the start of a level and of every life
        void StartBonus()
        {
            TimeAllowed = FlagsTotal * 10 + (director.Robots.Count + 1) * 10 + 30;
            Bonus = TimeAllowed * 4;
            LevelTicks = 0;
            repairTimer = 0;
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
            Paused = !Paused;
            Time.timeScale = Paused ? 0f : 1f;
            if (Paused) ShowBanner?.Invoke("PAUSED", "PRESS P TO CONTINUE");
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
            Flags.Clear(); Dumps.Clear(); Robots.Clear(); Obstacles.Clear();
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
            var levels = m.Levels[Mathf.Clamp(diff, 0, 2)];
            int index = (levelNumber - 1) % levels.Count;
            int lap = (levelNumber - 1) / levels.Count;   // after the last level the matrix repeats, a little harder
            var lv = levels[index];
            levelDifficulty = 1f + lap * 0.25f;
            // The level's tip number also unlocks a heavy weapon, the first time round (_LOAD_WAVE)
            if (lap == 0) player.Weapon.Unlock(lv.tip);

            ClearLevel();
            director.Shots.ResetCounts();
            SpectrePalette.Apply(obstacleLook.shapes, levelNumber - 1);
            SetWallColor(SpectrePalette.Sky(levelNumber - 1));
            PlacePlayerAtStart();
            UseShapes(m);

            // The grid: its first entries are the flags (_LOAD_WRLD), as many as _SETUP_LEVEL allows -
            // 3 on the first two levels, one more every two levels, 6 at most - whatever their type;
            // the rest are obstacles where the designer put them (spare flag entries have no shape)
            var flagSpots = new List<Vector3>();
            int maxFlags = Mathf.Min(6, ((levelNumber - 1) >> 1) + 3);
            if (m.Grids.TryGetValue(lv.grid, out var grid))
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
            // Ten ammo dumps, their kinds weighted by the enemy data's type-5 entry (_LOAD_BVAL)
            DumpOdds = m.TryGetEnemyData(lv.enemyData, 5, out var fuel)
                ? new[] { fuel.strength, fuel.collideDamage, fuel.attackRange / 7, fuel.firingRange / 7 }
                : DefaultDumpOdds;
            for (int i = 0; i < DumpsPerLevel; i++) SpawnDump();
            // The first level's shortcuts to levels 5 and 10 (_BUILD_WORLD)
            if (levelNumber == 1 && !Demo)
            {
                if (TryFindSpawnPoint(out var j5, 25f)) jumpPads.Add(JumpPad.Create(5, 5000, j5 - Vector3.up * j5.y, obstacleLook.transport, obstacleLook.edgeThickness));
                if (TryFindSpawnPoint(out var j10, 25f)) jumpPads.Add(JumpPad.Create(10, 10000, j10 - Vector3.up * j10.y, obstacleLook.transport, obstacleLook.edgeThickness));
            }

            FlagsTotal = Flags.Count;
            FlagsCollected = 0;
            Plan = new LevelPlan { level = levelNumber, difficulty = levelDifficulty, robotShields = 1, wallColor = LevelPlan.WallColor(levelNumber) };

            // The enemy set (_ADD_ENEMY): a count between its two numbers when the first is the smaller,
            // else exactly the second; each robot comes back 'regen' more times. Strength rises by one a lap.
            if (m.EnemySets.TryGetValue(lv.enemySet, out var set))
            {
                foreach (var e in set)
                {
                    int count = e.max < e.min ? Random.Range(e.max, e.min + 1) : e.min;
                    var data = m.TryGetEnemyData(lv.enemyData, e.type, out var d) ? Robot.Data.From(d, lap) : DefaultRobotData(e.type, lap);
                    for (int i = 0; i < count; i++) SpawnRobot(e.type, data, Mathf.Max(0, e.regen));
                }
            }

            // Acid burns for the level's Smart Acid collision damage, or 10 (_LOAD_BVAL)
            AcidDamage = m.TryGetEnemyData(lv.enemyData, 23, out var acid) ? acid.collideDamage : 10;
            StartBonus();

            levelSong = MatrixMusic.TrackFor(lv.song);
            return m.Tip(lv.tip);
        }

        void BuildGeneratedLevel(LevelPlan plan)
        {
            ClearLevel();
            director.Shots.ResetCounts();
            SetWallColor(plan.wallColor);
            PlacePlayerAtStart();
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

            // Without a matrix: Robot Tanks, Turrets, Orbiters and Slicers with Central Systems' enemy data
            void Add(int type, int n) { for (int i = 0; i < n; i++) SpawnRobot(type, DefaultRobotData(type, 0), 99); }
            Add(7, plan.rovers);
            Add(20, plan.turrets);
            Add(35, plan.orbiters);
            Add(21, plan.slicers);
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
            if (!TryFindSpawnPoint(out var p, 0f)) return;
            var d = Spawn(ammoDumpPrefab, p - Vector3.up * p.y, Quaternion.identity);
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

        // A used dump re-rolls its kind and moves somewhere at least 0x26000 units (~49 m) from the
        // player on both axes, clear of everything else (_INVISIBLE_PLACE_XY)
        public void RelocateDump(AmmoDump d)
        {
            const float away = 0x26000 * PlayerMotor.UnitsToMetres;
            Vector3 me = player.transform.position;
            Vector3 spot = Vector3.zero;
            bool found = false;
            for (int i = 0; i < 200 && !found; i++)
            {
                if (!TryFindSpawnPoint(out spot, 0f)) continue;
                found = Mathf.Abs(spot.x - me.x) >= away && Mathf.Abs(spot.z - me.z) >= away;
            }
            if (!found && !TryFindSpawnPoint(out spot, away)) return;
            d.transform.position = spot - Vector3.up * spot.y;
            d.SetKind(PickDumpKind());
        }

        void PlacePlayerAtStart()
        {
            // _PLAY_LEVEL: every life starts at a random clear spot (_PLACE_XY), dropping in
            if (!TryFindSpawnPoint(out var start, 0f)) start = new Vector3(0f, 0f, -15f * GridScale);
            start.y = 0f;
            if (player.Health.IsDead) player.Health.Revive(start, Quaternion.identity);
            player.Motor.Place(start, 0f, dropIn: true);
            player.Motor.FaceRandomly();
            player.Health.Restore(player.Health.maxShields);
            player.ResetForLife();
        }

        void UseShapes(MatrixFile m)
        {
            var central = MatrixLibrary.Central();
            if (central == null) Debug.LogWarning("Spectre VR-EX: Central Systems matrix not found - matrices without shapes will have no obstacles");
            shapes = new ShapeSet(m, central);
            shapes.NewLevel();
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
            robot.Place(RobotSpot(robot));
            ApplyFilled(robot.gameObject);
            if (type == 26) foreach (var r in robot.GetComponentsInChildren<Renderer>()) r.enabled = false;
            director.Robots.Add(robot);
            Robots.Add(robot.Health);
            robotsLeft += regen + 1;
        }

        // Turrets, Orbiters and Bouncers start by a flag; everything else somewhere out of sight
        Vector3 RobotSpot(Robot robot)
        {
            if (robot.IsFlagGuard) return PlaceNearFlag();
            TryFindHiddenSpot(out var spot);
            return spot;
        }

        // _PLACE_NEAR_FLAG: the flag with the fewest robots so far, 0x3C00 in from it toward the middle
        Vector3 PlaceNearFlag()
        {
            int n = Mathf.Min(Flags.Count, flagGuards.Length);
            if (n == 0) return Vector3.zero;
            int best = 0;
            for (int i = 1; i < n; i++) if (flagGuards[i] < flagGuards[best]) best = i;
            flagGuards[best]++;
            Vector3 f = Flags[best] != null ? Flags[best].transform.position : Vector3.zero;
            float step = 0x3C00 * PlayerMotor.UnitsToMetres;
            return new Vector3(f.x < 0.0001f ? f.x + step : f.x - step, 0f, f.z < 0.0001f ? f.z + step : f.z - step);
        }

        // _INVISIBLE_PLACE_XY: at least 0x26000 from the player on both axes, clear of other things
        public bool TryFindHiddenSpot(out Vector3 spot)
        {
            const float away = 0x26000 * PlayerMotor.UnitsToMetres;
            Vector3 me = player.transform.position;
            spot = Vector3.zero;
            for (int i = 0; i < 200; i++)
            {
                if (!TryFindSpawnPoint(out spot, 0f)) continue;
                if (Mathf.Abs(spot.x - me.x) >= away && Mathf.Abs(spot.z - me.z) >= away) { spot.y = 0f; return true; }
            }
            bool ok = TryFindSpawnPoint(out spot, away);
            spot.y = 0f;
            return ok;
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

        void SetWallColor(Color c)
        {
            foreach (var r in wallRenderers)
            {
                if (r == null) continue;
                var m = r.material;
                if (wallNits < 0f)
                {
                    Color cur = m.GetColor("_EmissiveColor");
                    wallNits = Mathf.Max(cur.r, Mathf.Max(cur.g, cur.b));
                }
                m.SetColor("_EmissiveColor", c * wallNits);
            }
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
            Flags.Remove(flag);
            FlagsCollected++;
            AddScore(flagPoints);
            int left = FlagsTotal - FlagsCollected;
            Sfx.Play(left == 0 ? Snd.Warp : left == 1 ? Snd.OneMoreFlag : Snd.FlagPickup);
            if (left == 0 && Playing) session = StartCoroutine(LevelComplete());
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
            session = StartCoroutine(LevelComplete());
        }

        readonly List<JumpPad> jumpPads = new List<JumpPad>();

        // _END_LEVEL, 18 ticks after the last flag: the bonus left goes on the score
        IEnumerator LevelComplete()
        {
            yield return new WaitForSeconds(18f / PlayerMotor.TickRate);
            Playing = false;
            Sfx.Play(Snd.Award);

            AddScore(Bonus);

            // _SHOW_RECAP: the bars grow over 16 ticks, then 18 more or a key press. Quickness is as the
            // original has it: seconds taken over seconds allowed, so it rises the longer a level takes.
            var shots = director.Shots;
            var rates = new[]
            {
                Rate(LevelTicks / 18, TimeAllowed),
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
                StartCoroutine(PlayerDown());
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

        // Destroyed (_PLAY_LEVEL): 50 ticks later a life goes; the level carries on from the start
        // position with the flags and robots as they were, shots cleared and the bonus restarted
        IEnumerator PlayerDown()
        {
            Sfx.Play(Snd.PlayerDestroyed);
            yield return new WaitForSeconds(DeadTicks / PlayerMotor.TickRate);
            Lives--;

            if (Lives <= 0)
            {
                Playing = false;
                ShowBanner?.Invoke("GAME OVER", $"FINAL SCORE {Score}");
                yield return new WaitForSeconds(5f);
                // Demo games don't go on the score table
                EndSession(Demo ? 0 : Score, Level);
                yield break;
            }

            director.Shots.Clear();
            PlacePlayerAtStart();
            StartBonus();
        }
    }
}
