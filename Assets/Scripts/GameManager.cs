using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace SpectreEX
{
    // Spectre VR session rules: lives, score, levels, the level clock and its time bonus,
    // spawning each level's flags, ammo dumps and robots, and respawns. Games are started from
    // the main menu (StartGame); Esc returns to it, P pauses, and demo mode ends on any key.
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

        [Header("Scene")]
        public PlayerTank player;
        public Renderer[] wallRenderers;
        public Renderer floorDots;
        public float arenaHalfSize = 120f;

        [Header("Prefabs")]
        public Flag flagPrefab;
        public AmmoDump ammoDumpPrefab;
        public GameObject roverPrefab, turretPrefab, orbiterPrefab, slicerPrefab;

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
        public bool Demo { get; private set; }
        public LevelPlan Plan { get; private set; }

        public readonly List<Flag> Flags = new List<Flag>();
        public readonly List<AmmoDump> Dumps = new List<AmmoDump>();
        public readonly List<Health> Robots = new List<Health>();

        public System.Action<string, string> ShowBanner;           // big title, red tip
        public System.Action<string[], string[]> ShowStats;         // label rows, value rows
        public System.Action HideOverlays;
        public System.Action<int, int> ReturnedToMenu;              // final score and level (0 if abandoned)

        readonly Dictionary<GameObject, GameObject> prefabOf = new Dictionary<GameObject, GameObject>();
        int nextExtraLife;
        float wallNits = -1f;
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
            if (session != null) StopCoroutine(session);
            Time.timeScale = 1f;
            Paused = false;
            Demo = demo;
            demoPilot.enabled = demo;
            ApplyVehicle(vehicle);
            ApplySettings();
            InGame = true;
            session = StartCoroutine(NewGame(Mathf.Max(1, startLevel)));
        }

        public void QuitToMenu() => EndSession(0, 0);

        void EndSession(int finalScore, int finalLevel)
        {
            StopAllCoroutines();
            session = null;
            Time.timeScale = 1f;
            Paused = false;
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
            player.Motor.maxSpeed = v.MaxSpeed;
            player.Motor.turnSpeed = v.TurnSpeed;
            player.Health.SetMaxShields(v.MaxShields);
            player.Weapon.maxAmmo = v.MaxAmmo;
            player.Weapon.Refill();
        }

        // Options-dialog settings that affect the running scene
        public void ApplySettings()
        {
            Sfx.Enabled = GameSettings.SoundEffectsOn;

            var vol = FindFirstObjectByType<Volume>();
            if (vol != null)
            {
                var profile = vol.profile;   // runtime instance, so the asset on disk is untouched
                if (profile.TryGet<ScreenSpaceReflection>(out var ssr)) ssr.active = GameSettings.DisplayComplex;
                if (profile.TryGet<GlobalIllumination>(out var gi)) gi.active = GameSettings.DisplayComplex;
                if (profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ao)) ao.active = GameSettings.DisplayComplex;
            }
            if (floorDots != null) floorDots.enabled = GameSettings.DisplayTextures;
            foreach (var r in wallRenderers) if (r != null) r.enabled = GameSettings.DisplayTextures;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) ApplyFilled(r);
        }

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
            nextExtraLife = extraLifeEvery;
            Level = startLevel - 1;
            if (!Demo) Sfx.Play(Snd.Welcome);
            yield return NextLevel();
        }

        IEnumerator NextLevel()
        {
            Level++;
            Plan = LevelPlan.For(Level);
            BuildLevel(Plan);
            if (MusicPlayer.Instance != null) MusicPlayer.Instance.PlayForLevel(Level);

            ShowBanner?.Invoke(Demo ? "DEMO" : $"LEVEL {Level}", Plan.tip);
            yield return new WaitForSeconds(3f);
            HideOverlays?.Invoke();
            Playing = true;
        }

        void Update()
        {
            if (!InGame) return;
            var k = Keyboard.current;

            if (Demo)
            {
                bool anyKey = (k != null && k.anyKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
                if (anyKey) { QuitToMenu(); return; }
            }
            else if (k != null)
            {
                if (k.escapeKey.wasPressedThisFrame) { QuitToMenu(); return; }
                if (k.pKey.wasPressedThisFrame) TogglePause();
            }

            if (!Playing || Paused) return;
            float before = TimeRemaining;
            TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
            if (before > 10f && TimeRemaining <= 10f) Sfx.Play(Snd.TimeTick);
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
            Flags.Clear(); Dumps.Clear(); Robots.Clear(); prefabOf.Clear();
            FlagsTotal = FlagsCollected = 0;
        }

        void BuildLevel(LevelPlan plan)
        {
            ClearLevel();
            SetWallColor(plan.wallColor);

            // Player starts each level near the middle, fully stocked
            if (TryFindSpawnPoint(out var start, 0f, nearCentre: true))
            {
                if (player.Health.IsDead) player.Health.Revive(start, Quaternion.identity);
                else
                {
                    player.transform.SetPositionAndRotation(start, Quaternion.identity);
                    var rb = player.GetComponent<Rigidbody>();
                    rb.position = start; rb.rotation = Quaternion.identity; rb.linearVelocity = Vector3.zero;
                }
            }
            player.Health.Restore(player.Health.maxShields);
            player.ResetForLife();

            for (int i = 0; i < plan.flags; i++)
                if (TryFindSpawnPoint(out var p, 25f)) Flags.Add(Spawn(flagPrefab, p, Quaternion.identity));
            for (int i = 0; i < plan.ammoDumps; i++)
                if (TryFindSpawnPoint(out var p, 25f)) Dumps.Add(Spawn(ammoDumpPrefab, p - Vector3.up * p.y, Quaternion.identity));

            FlagsTotal = Flags.Count;
            FlagsCollected = 0;
            TimeRemaining = plan.timeAllowed;

            for (int i = 0; i < plan.rovers; i++) SpawnRobot(roverPrefab, 50f);
            for (int i = 0; i < plan.turrets; i++) SpawnRobot(turretPrefab, 40f);
            for (int i = 0; i < plan.orbiters; i++) SpawnRobot(orbiterPrefab, 50f);
            for (int i = 0; i < plan.slicers; i++) SpawnRobot(slicerPrefab, 60f);
        }

        T Spawn<T>(T prefab, Vector3 pos, Quaternion rot) where T : Component
        {
            var c = Instantiate(prefab, pos, rot);
            ApplyFilled(c.gameObject);
            return c;
        }

        void SpawnRobot(GameObject prefab, float minDistance)
        {
            if (prefab == null || !TryFindSpawnPoint(out var p, minDistance)) return;
            var go = Instantiate(prefab, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            ApplyFilled(go);
            prefabOf[go] = prefab;
            var h = go.GetComponent<Health>();
            h.SetMaxShields(prefab.GetComponent<Health>().maxShields + Plan.robotShields - 1);
            var ai = go.GetComponent<EnemyBase>();
            if (ai != null) ai.difficulty = Plan.difficulty * GameSettings.DifficultyMultiplier;
            Robots.Add(h);
        }

        // Random open spot on the arena floor, clear of obstacles and at least minDistance from the player.
        public bool TryFindSpawnPoint(out Vector3 point, float minDistanceFromPlayer, bool nearCentre = false)
        {
            float range = nearCentre ? 20f : arenaHalfSize - 12f;
            for (int i = 0; i < 200; i++)
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

        public void AddScore(int points)
        {
            Score += points;
            while (extraLifeEvery > 0 && Score >= nextExtraLife)
            {
                Lives++;
                nextExtraLife += extraLifeEvery;
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
            prefabOf.TryGetValue(go, out var prefab);
            prefabOf.Remove(go);
            Destroy(go, 0.05f);
            if (prefab != null) StartCoroutine(RespawnRobot(prefab, Level));
        }

        IEnumerator RespawnRobot(GameObject prefab, int level)
        {
            yield return new WaitForSeconds(robotRespawnDelay);
            if (Level == level && Playing) SpawnRobot(prefab, 60f);
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

            TryFindSpawnPoint(out var p, 50f);
            player.Health.Revive(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            player.ResetForLife();
            Sfx.Play(Snd.PlayerUp);
        }
    }
}
