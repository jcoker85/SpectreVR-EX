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
        public int extraLifeEvery = 10000;
        public int flagPoints = 20;   // the original's +20 a flag
        public int timeBonusPerSecond = 10;
        public float playerRespawnDelay = 3f;

        public int Score { get; private set; }
        public int Lives { get; private set; }
        public int Level { get; private set; }
        public float TimeRemaining { get; private set; }
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
        public System.Action<string[], string[]> ShowStats;         // label rows, value rows
        public System.Action HideOverlays;
        public System.Action<int, int> ReturnedToMenu;              // final score and level (0 if abandoned)
        public System.Action Suspend;                               // Esc in game: show the menu over the paused game

        // The robots run on the original's tick (RobotDirector); _TANKS_LEFT counts every robot still to
        // be destroyed, regenerations included, and the destroyed robots' bonuses are paid when it hits 0
        RobotDirector director;
        int robotsLeft, bankedBonus;
        readonly int[] flagGuards = new int[64];   // _FLAG_TURRETS: robots placed by each flag
        public ShapeSet Shapes => shapes;
        int extraLivesAwarded;
        int nextExtraLife;
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

        public void QuitToMenu() => EndSession(0, 0);

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

        // Options "Filled": the solid faces of craft and obstacles (named "Panels"); off leaves wireframes
        public static void ApplyFilled(Renderer r)
        {
            if (r != null && r.name == "Panels") r.enabled = GameSettings.DisplayFilled;
        }

        static void ApplyFilled(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) ApplyFilled(r);
        }

        IEnumerator NewGame(int startLevel)
        {
            Score = 0;
            Lives = startingLives;
            extraLivesAwarded = 0;
            nextExtraLife = ExtraLifeScore(1);
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
            if (!Demo) Sfx.Play(Snd.Welcome);
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
                if (MusicPlayer.Instance != null) MusicPlayer.Instance.PlayForLevel(Level);
            }

            ShowBanner?.Invoke(Demo ? "DEMO" : $"LEVEL {Level}", tip);
            yield return new WaitForSeconds(3f);
            HideOverlays?.Invoke();
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

            if (!Playing || Paused) return;
            float before = TimeRemaining;
            TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
            if (before > 10f && TimeRemaining <= 10f) Sfx.Play(Snd.TimeTick);
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
            SetWallColor(LevelPlan.WallColor(levelNumber));
            PlacePlayerAtStart();
            UseShapes(m);

            // The grid: flags first (as VRchitect stores them), then every other obstacle where the designer put it
            var flagSpots = new List<Vector3>();
            if (m.Grids.TryGetValue(lv.grid, out var grid))
            {
                foreach (var o in grid.obstacles)
                {
                    var pos = GridToWorld(o.x, o.y);
                    if (o.type == 14) { flagSpots.Add(pos); continue; }
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

            FlagsTotal = Flags.Count;
            FlagsCollected = 0;
            TimeRemaining = 60f + FlagsTotal * 12f;
            Plan = new LevelPlan { level = levelNumber, timeAllowed = TimeRemaining, difficulty = levelDifficulty, robotShields = 1, wallColor = LevelPlan.WallColor(levelNumber) };

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

            if (MusicPlayer.Instance != null) MusicPlayer.Instance.PlayTrackOrStop(MatrixMusic.TrackFor(lv.song));
            return m.Tip(lv.tip);
        }

        void BuildGeneratedLevel(LevelPlan plan)
        {
            ClearLevel();
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
            TimeRemaining = plan.timeAllowed;

            // Without a matrix: Robot Tanks, Turrets, Orbiters and Slicers with Central Systems' enemy data
            void Add(int type, int n) { for (int i = 0; i < n; i++) SpawnRobot(type, DefaultRobotData(type, 0), 99); }
            Add(7, plan.rovers);
            Add(20, plan.turrets);
            Add(35, plan.orbiters);
            Add(21, plan.slicers);
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
            var start = new Vector3(0f, 0f, -15f * GridScale);
            if (player.Health.IsDead) player.Health.Revive(start, Quaternion.identity);
            player.Motor.Place(start, 0f, dropIn: true);
            player.Health.Restore(player.Health.maxShields);
            player.ResetForLife();
        }

        void UseShapes(MatrixFile m)
        {
            var central = MatrixLibrary.Central();
            if (central == null) Debug.LogWarning("Spectre VR-EX: Central Systems matrix not found - matrices without shapes will have no obstacles");
            shapes = new ShapeSet(m, central);
            shapes.NewLevel();
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

        // Extra tanks at the matrix's score thresholds (VRchitect "New Lives"), or every extraLifeEvery points
        int ExtraLifeScore(int n) => CurrentMatrix != null
            ? CurrentMatrix.ExtraLifeScore((int)GameSettings.Difficulty, n)
            : n * extraLifeEvery;

        public void AddScore(int points)
        {
            Score += points;
            while (Score >= nextExtraLife)
            {
                Lives++;
                extraLivesAwarded++;
                nextExtraLife = ExtraLifeScore(extraLivesAwarded + 1);
                Sfx.Play(Snd.ExtraLife);
            }
        }

        public void OnFlagCollected(Flag flag)
        {
            Flags.Remove(flag);
            FlagsCollected++;
            AddScore(flagPoints);
            Sfx.Play(Snd.FlagPickup);
            int left = FlagsTotal - FlagsCollected;
            if (left == 1) Sfx.Play(Snd.OneMoreFlag);
            if (left == 0 && Playing) session = StartCoroutine(LevelComplete());
        }

        IEnumerator LevelComplete()
        {
            Playing = false;
            if (MusicPlayer.Instance != null) MusicPlayer.Instance.PlayTrack("AWARD", loop: false);
            Sfx.Play(Snd.Award);

            int allowed = Mathf.RoundToInt(Plan.timeAllowed);
            int taken = Mathf.RoundToInt(Plan.timeAllowed - TimeRemaining);
            int bonus = Mathf.RoundToInt(TimeRemaining) * timeBonusPerSecond;
            AddScore(bonus);
            ShowStats?.Invoke(
                new[] { "TIME ALLOWED", "TIME TAKEN", "TIME BONUS", "SCORE" },
                new[] { allowed.ToString(), taken.ToString(), bonus.ToString(), Score.ToString() });
            yield return new WaitForSeconds(4.5f);
            HideOverlays?.Invoke();
            yield return NextLevel();
        }

        public void OnDestroyed(Health h)
        {
            if (!InGame) return;
            if (h.team == Team.Player)
            {
                StartCoroutine(PlayerDown());
                return;
            }

            // A robot (_MOVE_SHOTS): its bonus is banked, all of them paid out with the last robot; it
            // throws debris, and the director brings it back ten ticks later if it has regenerations left
            var robot = h.GetComponent<Robot>();
            if (robot == null) return;
            robot.Killed();
            director.Shots.Explode(robot);
            bankedBonus += Mathf.Max(0, robot.data.bonus);
            if (--robotsLeft == 0)
            {
                Sfx.Play(Snd.AllRobots);
                AddScore(bankedBonus);
                bankedBonus = 0;
            }
        }

        IEnumerator PlayerDown()
        {
            Lives--;
            Sfx.Play(Snd.PlayerDown);
            yield return new WaitForSeconds(playerRespawnDelay);

            if (Lives <= 0)
            {
                Playing = false;
                Sfx.Play(Snd.GameOver);
                ShowBanner?.Invoke("GAME OVER", $"FINAL SCORE {Score}");
                yield return new WaitForSeconds(5f);
                // Demo games don't go on the score table
                EndSession(Demo ? 0 : Score, Level);
                yield break;
            }

            TryFindSpawnPoint(out var p, 40f);
            player.Health.Revive(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            player.ResetForLife();
            Sfx.Play(Snd.PlayerUp);
        }
    }
}
