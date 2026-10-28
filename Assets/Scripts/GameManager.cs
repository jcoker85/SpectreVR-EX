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

        [System.Serializable]
        public class ObstacleKind
        {
            public int type;                 // matrix obstacle type
            public GameObject[] variants;    // picked at random, like the original's random shapes
        }

        [Header("Scene")]
        public PlayerTank player;
        public Renderer[] wallRenderers;
        public Renderer floorDots;
        public float arenaHalfSize = 82f;

        [Header("Prefabs")]
        public Flag flagPrefab;
        public AmmoDump ammoDumpPrefab;
        public GameObject roverPrefab, turretPrefab, orbiterPrefab, slicerPrefab;
        public ObstacleKind[] obstacleKinds;
        public int fallbackObstacleType = 15;   // cube

        [Header("Rules")]
        public int startingLives = 3;
        public int extraLifeEvery = 10000;
        public int flagPoints = 100;
        public int timeBonusPerSecond = 10;
        public float playerRespawnDelay = 3f;
        public float robotRespawnDelay = 12f;

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

        // What each robot was spawned as, so it can regenerate the way the matrix says
        class RobotSpawn
        {
            public GameObject prefab;
            public int type = -1;                      // matrix robot type, -1 for generated levels
            public MatrixFile.EnemyData data;
            public bool hasData;
            public int regenLeft;                      // times it comes back after being destroyed
        }

        readonly Dictionary<GameObject, RobotSpawn> spawnOf = new Dictionary<GameObject, RobotSpawn>();
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
            Flags.Clear(); Dumps.Clear(); Robots.Clear(); Obstacles.Clear(); spawnOf.Clear();
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

            ClearLevel();
            SetWallColor(LevelPlan.WallColor(levelNumber));
            PlacePlayerAtStart();

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
            for (int i = 0; i < 2; i++)
                if (TryFindSpawnPoint(out var p, 20f)) Dumps.Add(Spawn(ammoDumpPrefab, p - Vector3.up * p.y, Quaternion.identity));

            FlagsTotal = Flags.Count;
            FlagsCollected = 0;
            TimeRemaining = 60f + FlagsTotal * 12f;
            Plan = new LevelPlan { level = levelNumber, timeAllowed = TimeRemaining, difficulty = levelDifficulty, robotShields = 1, wallColor = LevelPlan.WallColor(levelNumber) };

            // The enemy set: between min and max of each robot type, each regenerating 'regen' times in all
            if (m.EnemySets.TryGetValue(lv.enemySet, out var set))
            {
                foreach (var e in set)
                {
                    int count = Random.Range(Mathf.Min(e.min, e.max), Mathf.Max(e.min, e.max) + 1);
                    for (int i = 0; i < count; i++)
                    {
                        var spawn = new RobotSpawn { prefab = PrefabForRobot(e.type), type = e.type, regenLeft = Mathf.Max(0, e.regen - 1) };
                        spawn.hasData = m.TryGetEnemyData(lv.enemyData, e.type, out spawn.data);
                        SpawnRobot(spawn, 45f);
                    }
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

            var rng = new System.Random(1991);
            for (int i = 0; i < 14; i++)
            {
                var pos = new Vector3((float)(rng.NextDouble() * 2 - 1) * (arenaHalfSize - 15f), 0f, (float)(rng.NextDouble() * 2 - 1) * (arenaHalfSize - 15f));
                if (pos.magnitude > 20f) SpawnObstacle(fallbackObstacleType, pos, rng.Next(0, 90));
            }
            Physics.SyncTransforms();

            for (int i = 0; i < plan.flags; i++)
                if (TryFindSpawnPoint(out var p, 25f)) Flags.Add(Spawn(flagPrefab, p, Quaternion.identity));
            for (int i = 0; i < plan.ammoDumps; i++)
                if (TryFindSpawnPoint(out var p, 25f)) Dumps.Add(Spawn(ammoDumpPrefab, p - Vector3.up * p.y, Quaternion.identity));

            FlagsTotal = Flags.Count;
            FlagsCollected = 0;
            TimeRemaining = plan.timeAllowed;

            void Add(GameObject prefab, int n) { for (int i = 0; i < n; i++) SpawnRobot(new RobotSpawn { prefab = prefab, regenLeft = 99 }, 45f); }
            Add(roverPrefab, plan.rovers);
            Add(turretPrefab, plan.turrets);
            Add(orbiterPrefab, plan.orbiters);
            Add(slicerPrefab, plan.slicers);
        }

        // The original starts every level 15 grid units south of the middle, facing north, dropping in
        void PlacePlayerAtStart()
        {
            var start = new Vector3(0f, 0f, -15f * GridScale);
            if (player.Health.IsDead) player.Health.Revive(start, Quaternion.identity);
            player.Motor.Place(start, 0f, dropIn: true);
            player.Health.Restore(player.Health.maxShields);
            player.ResetForLife();
        }

        void SpawnObstacle(int type, Vector3 pos, int heading)
        {
            var prefab = PickObstaclePrefab(type) ?? PickObstaclePrefab(fallbackObstacleType);
            if (prefab == null) return;
            // The original's heading byte; its shapes face the opposite way to how its tanks travel,
            // which puts the model at yaw -degrees here
            var go = Instantiate(prefab, pos, Quaternion.Euler(0f, -heading, 0f));
            var beh = go.GetComponent<ObstacleBehaviour>();
            if (beh != null)
            {
                beh.type = type;
                beh.heading = Mathf.RoundToInt(heading * 256f / 360f) & 0xFF;
            }
            ApplyFilled(go);
            Obstacles.Add(go);
        }

        GameObject PickObstaclePrefab(int type)
        {
            if (obstacleKinds == null) return null;
            foreach (var k in obstacleKinds)
                if (k.type == type && k.variants != null && k.variants.Length > 0)
                    return k.variants[Random.Range(0, k.variants.Length)];
            return null;
        }

        T Spawn<T>(T prefab, Vector3 pos, Quaternion rot) where T : Component
        {
            var c = Instantiate(prefab, pos, rot);
            ApplyFilled(c.gameObject);
            return c;
        }

        // Matrix robot types (VRchitect's names) to the robots Spectre VR-EX can field
        GameObject PrefabForRobot(int type) => type switch
        {
            20 => turretPrefab,                                   // Turret
            21 or 23 or 24 or 25 or 45 => slicerPrefab,           // Slicer, Smart Acid, Smart Wall, Cyber Mud, Bouncer
            22 or 35 => orbiterPrefab,                            // Flyer, Orbiter
            _ => roverPrefab,                                     // Tank, Robot Tank, Super Tank, Invisible / Cloaked Tank
        };

        void SpawnRobot(RobotSpawn spawn, float minDistance)
        {
            if (spawn.prefab == null || !TryFindSpawnPoint(out var p, minDistance)) return;
            var go = Instantiate(spawn.prefab, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            ApplyFilled(go);
            spawnOf[go] = spawn;
            var h = go.GetComponent<Health>();
            var ai = go.GetComponent<EnemyBase>();
            if (ai != null) ai.difficulty = levelDifficulty * GameSettings.DifficultyMultiplier;

            if (spawn.hasData) ApplyEnemyData(go, spawn.type, spawn.data);
            else if (ai != null && spawn.type > 0) { ai.matrixType = spawn.type; ai.hasMatrixData = false; }
            else if (CurrentMatrix == null) h.SetMaxShields(spawn.prefab.GetComponent<Health>().maxShields + Plan.robotShields - 1);

            if (spawn.type == 26)   // Invisible Tank: shows on radar only
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.enabled = false;
            if (spawn.type == 27) h.hiddenFromRadar = true;   // Cloaked Tank: visible, but not on radar
            Robots.Add(h);
        }

        // VRchitect "Enemy Data" applied to one robot. Distances are grid units; times are 60ths of a second.
        void ApplyEnemyData(GameObject go, int type, MatrixFile.EnemyData d)
        {
            var h = go.GetComponent<Health>();
            h.SetMaxShields(Mathf.Max(1, d.strength));
            h.scoreValue = Mathf.Max(0, d.score) * 10;

            var ai = go.GetComponent<EnemyBase>();
            if (ai != null)
            {
                ai.fireInterval = d.firingInterval > 0 ? d.firingInterval / 60f : -1f;
                ai.collideDamage = Mathf.Max(0, d.collideDamage);
                ai.matrixType = type;
                ai.hasMatrixData = true;
            }

            var weapon = go.GetComponent<TankWeapon>();
            if (weapon != null)
            {
                weapon.enabled = d.weapon >= 0;
                if (CurrentMatrix != null && CurrentMatrix.Weapons.TryGetValue(Mathf.Max(0, d.weapon), out var w)) weapon.damage = Mathf.Max(1, w.damage);
            }

            var motor = go.GetComponent<TankMotor>();
            if (motor != null && d.speed > 0)
            {
                motor.maxSpeed = d.speed * 2.2f;
                motor.turnSpeed = 60f + 30f * Mathf.Max(1, d.turnSpeed);
            }

            switch (ai)
            {
                case RoverAI rover:
                    rover.attackRange = d.attackRange > 0 ? Mathf.Max(30f, d.attackRange * GridScale * 1.5f) : 1000f;
                    rover.fireRange = Mathf.Max(25f, d.firingRange * GridScale * 4f);
                    rover.aimTolerance = Mathf.Clamp(d.firingArc * 0.5f, 3f, 20f);
                    break;
                case TurretAI turret:
                    turret.range = Mathf.Max(40f, d.firingRange * GridScale * 4f);
                    break;
                case SlicerAI slicer:
                    if (d.speed > 0) slicer.speed = d.speed * 2.2f;
                    break;
                case OrbiterAI orbiter:
                    if (d.speed > 0) orbiter.moveSpeed = d.speed * 2.2f;
                    break;
            }
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

            AddScore(h.scoreValue);
            Robots.Remove(h);
            var go = h.gameObject;
            spawnOf.TryGetValue(go, out var spawn);
            spawnOf.Remove(go);
            Destroy(go, 0.05f);
            // Each robot regenerates the number of times its enemy set says, then stays dead
            if (spawn != null && spawn.regenLeft > 0)
            {
                spawn.regenLeft--;
                StartCoroutine(RespawnRobot(spawn, Level));
            }
        }

        IEnumerator RespawnRobot(RobotSpawn spawn, int level)
        {
            yield return new WaitForSeconds(robotRespawnDelay);
            if (Level == level && Playing) SpawnRobot(spawn, 50f);
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
