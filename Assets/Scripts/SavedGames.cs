using UnityEngine;

namespace SpectreEX
{
    // _SAVED_GAMES: ten named slots holding the level, the score it started with and the lives left
    public static class SavedGames
    {
        public const int Slots = 10;

        public struct Game
        {
            public bool used;
            public string name;
            public int level, score, lives;   // level counts from 0, as _LEVEL does
        }

        public static Game Load(int slot)
        {
            var parts = PlayerPrefs.GetString("save." + slot, "").Split(new[] { '|' }, 4);
            if (parts.Length < 4) return new Game { name = "" };
            int.TryParse(parts[0], out int level);
            int.TryParse(parts[1], out int score);
            int.TryParse(parts[2], out int lives);
            return new Game { used = true, name = parts[3], level = level, score = score, lives = lives };
        }

        public static void Save(int slot, string name, int level, int score, int lives)
        {
            PlayerPrefs.SetString("save." + slot, $"{level}|{score}|{lives}|{name}");
            PlayerPrefs.Save();
        }
    }
}
