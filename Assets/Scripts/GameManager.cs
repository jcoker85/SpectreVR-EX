using System.Collections;
using UnityEngine;

namespace SpectreEX
{
    // Score, rounds (collect every flag to advance), respawns and the HUD.
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public TankHealth player;
        public Transform[] enemySpawns;
        public float respawnDelay = 3f;
        public float difficultyPerRound = 0.25f;

        public int Score { get; private set; }
        public int Round { get; private set; } = 1;

        Flag[] flags;
        Vector3 playerSpawnPos;
        Quaternion playerSpawnRot;
        string banner;
        float bannerUntil;
        GUIStyle hudStyle, bannerStyle;

        void Awake()
        {
            Instance = this;
            flags = FindObjectsByType<Flag>(FindObjectsSortMode.None);
            if (player != null)
            {
                playerSpawnPos = player.transform.position;
                playerSpawnRot = player.transform.rotation;
            }
        }

        void Start() => ShowBanner("ROUND 1 — COLLECT THE FLAGS");

        public int FlagsRemaining
        {
            get
            {
                int n = 0;
                foreach (var f in flags) if (f.gameObject.activeSelf) n++;
                return n;
            }
        }

        public void OnFlagCollected(Flag flag)
        {
            Score += 100;
            if (FlagsRemaining == 0) NextRound();
        }

        void NextRound()
        {
            Round++;
            Score += 500 * (Round - 1);
            foreach (var f in flags) f.Respawn();
            foreach (var e in FindObjectsByType<EnemyTankAI>(FindObjectsSortMode.None)) e.difficulty += difficultyPerRound;
            ShowBanner($"ROUND {Round}");
        }

        public void OnTankDestroyed(TankHealth tank)
        {
            if (tank.isPlayer)
            {
                Score = Mathf.Max(0, Score - 250);
                ShowBanner("DEREZZED");
            }
            else
            {
                Score += 250;
            }
            StartCoroutine(RespawnAfterDelay(tank));
        }

        IEnumerator RespawnAfterDelay(TankHealth tank)
        {
            yield return new WaitForSeconds(respawnDelay);
            if (tank.isPlayer)
            {
                tank.Revive(playerSpawnPos, playerSpawnRot);
            }
            else
            {
                // Spawn at whichever spawn point is farthest from the player
                Transform best = null;
                float bestDist = -1f;
                foreach (var s in enemySpawns)
                {
                    float d = player != null ? Vector3.Distance(s.position, player.transform.position) : Random.value;
                    if (d > bestDist) { bestDist = d; best = s; }
                }
                if (best != null) tank.Revive(best.position, best.rotation);
            }
        }

        void ShowBanner(string text)
        {
            banner = text;
            bannerUntil = Time.time + 2.5f;
        }

        void OnGUI()
        {
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                hudStyle.normal.textColor = new Color(0.3f, 0.95f, 1f);
                bannerStyle = new GUIStyle(hudStyle) { fontSize = 44, alignment = TextAnchor.MiddleCenter };
            }

            float shields = player != null ? player.Shields : 0f;
            GUI.Label(new Rect(24, 18, 600, 32), $"SCORE {Score:000000}    ROUND {Round}", hudStyle);
            GUI.Label(new Rect(24, 48, 600, 32), $"FLAGS {flags.Length - FlagsRemaining}/{flags.Length}    SHIELDS {Mathf.CeilToInt(shields)}", hudStyle);
            GUI.Label(new Rect(24, Screen.height - 44, 900, 32), "WASD drive · SPACE/LMB fire · V cockpit view", hudStyle);

            if (Time.time < bannerUntil)
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 60), banner, bannerStyle);
        }
    }
}
