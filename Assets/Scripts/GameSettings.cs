using System.Collections.Generic;
using UnityEngine;

namespace SpectreEX
{
    // A Spectre craft's build: points of speed, shields and ammo (the original's "Custom Spectre"
    // dialog shares 15 points between them; shields 1-10, ammo 1-5 = 10-50 rounds).
    [System.Serializable]
    public struct VehicleSpec
    {
        public const int TotalPoints = 15;
        public string name;
        public int speed, shields, ammo;

        public VehicleSpec(string name, int speed, int shields, int ammo)
        {
            this.name = name; this.speed = speed; this.shields = shields; this.ammo = ammo;
        }

        // The original's units (_SET_TANK_SETTINGS / _EDIT_CUSTOM_TANK): speed 1-9, damage capacity
        // 10-100 in steps of 10, ammo 10-50 in steps of 10
        public int Speed => Mathf.Clamp(speed, 1, 9);
        public int DamagePoints => Mathf.Clamp(shields, 1, 10) * 10;
        public int MaxAmmo => Mathf.Clamp(ammo, 1, 5) * 10;

        public static readonly VehicleSpec Balance = new VehicleSpec("BALANCE", 5, 6, 4);
        public static readonly VehicleSpec Speedy = new VehicleSpec("SPEEDY", 8, 4, 3);
        public static readonly VehicleSpec Strong = new VehicleSpec("STRONG", 3, 8, 4);
    }

    public enum Difficulty { Easy, Normal, Hard }
    public enum SoundCard { PCSpeaker, AdLib, SoundBlaster }

    // Everything the Options dialog controls, persisted in PlayerPrefs.
    public static class GameSettings
    {
        // Music styles from the original's Options dialog; their tracks follow the original's own song
        // groupings (Matrix.MatrixMusic), as the matrices name them
        public static readonly string[] MusicStyles = { "Cyber/Techno", "New Age", "Rock/Rave", "VRBach" };

        public static bool Joystick = true;
        public static SoundCard Sound = SoundCard.SoundBlaster;
        public static bool[] Music = { true, false, false, false };
        public static Difficulty Difficulty = Difficulty.Easy;
        public static bool DisplayComplex = true;    // the detailed shape set; off is the plain one, from the next level
        public static bool DisplayFilled = true;     // solid faces; off is the wireframe view
        public static bool DisplayTextures = true;   // textured faces
        public static bool DisplayMovies = true;     // movie faces; the original has these off until you ask
        public static int DrawLevel = 1;             // floor dots: 0 none, 1-3 ever finer (_DRAW_LEVEL)
        public static bool SoundMuted;               // the in-game sound key
        public static VehicleSpec Custom = new VehicleSpec("CUSTOM", 5, 5, 5);
        public static int SelectedVehicle;           // 0 Balance, 1 Speedy, 2 Strong, 3 Custom
        public static string SelectedMatrix = "CENTRAL.MAT";   // file name chosen in the MATRIX dialog
        public static bool UseCustomMatrices;        // also offer .MAT files from the Matrices folders

        public static bool SoundEffectsOn => Sound != SoundCard.AdLib;
        public static bool MusicOn => Sound != SoundCard.PCSpeaker;

        public static float DifficultyMultiplier => Difficulty switch
        {
            Difficulty.Easy => 0.75f,
            Difficulty.Hard => 1.3f,
            _ => 1f,
        };

        public static VehicleSpec Vehicle(int index) => index switch
        {
            1 => VehicleSpec.Speedy,
            2 => VehicleSpec.Strong,
            3 => Custom,
            _ => VehicleSpec.Balance,
        };

        public static List<string> Playlist()
        {
            var list = new List<string>();
            for (int i = 0; i < Music.Length; i++)
                if (Music[i]) list.AddRange(Matrix.MatrixMusic.StyleTracks(i));
            return list;
        }

        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            Joystick = PlayerPrefs.GetInt("opt.joystick", 1) == 1;
            Sound = (SoundCard)PlayerPrefs.GetInt("opt.sound", (int)SoundCard.SoundBlaster);
            for (int i = 0; i < Music.Length; i++) Music[i] = PlayerPrefs.GetInt("opt.music" + i, 1) == 1;
            Difficulty = (Difficulty)PlayerPrefs.GetInt("opt.difficulty", (int)Difficulty.Easy);
            DisplayComplex = PlayerPrefs.GetInt("opt.complex", 1) == 1;
            DisplayFilled = PlayerPrefs.GetInt("opt.filled", 1) == 1;
            DisplayTextures = PlayerPrefs.GetInt("opt.textures", 1) == 1;
            DisplayMovies = PlayerPrefs.GetInt("opt.movies", 1) == 1;
            Custom = new VehicleSpec("CUSTOM", PlayerPrefs.GetInt("custom.speed", 5), PlayerPrefs.GetInt("custom.shields", 5), PlayerPrefs.GetInt("custom.ammo", 5));
            SelectedVehicle = PlayerPrefs.GetInt("vehicle", 0);
            SelectedMatrix = PlayerPrefs.GetString("matrix", "CENTRAL.MAT");
            UseCustomMatrices = PlayerPrefs.GetInt("opt.custommatrix", 0) == 1;
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("opt.joystick", Joystick ? 1 : 0);
            PlayerPrefs.SetInt("opt.sound", (int)Sound);
            for (int i = 0; i < Music.Length; i++) PlayerPrefs.SetInt("opt.music" + i, Music[i] ? 1 : 0);
            PlayerPrefs.SetInt("opt.difficulty", (int)Difficulty);
            PlayerPrefs.SetInt("opt.complex", DisplayComplex ? 1 : 0);
            PlayerPrefs.SetInt("opt.filled", DisplayFilled ? 1 : 0);
            PlayerPrefs.SetInt("opt.textures", DisplayTextures ? 1 : 0);
            PlayerPrefs.SetInt("opt.movies", DisplayMovies ? 1 : 0);
            PlayerPrefs.SetInt("custom.speed", Custom.speed);
            PlayerPrefs.SetInt("custom.shields", Custom.shields);
            PlayerPrefs.SetInt("custom.ammo", Custom.ammo);
            PlayerPrefs.SetInt("vehicle", SelectedVehicle);
            PlayerPrefs.SetString("matrix", SelectedMatrix);
            PlayerPrefs.SetInt("opt.custommatrix", UseCustomMatrices ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // Top-ten table, persisted in PlayerPrefs.
    public static class HighScores
    {
        public const int Count = 10;

        public struct Entry { public string name; public int score; public int level; }

        public static List<Entry> Load()
        {
            var list = new List<Entry>();
            for (int i = 0; i < Count; i++)
            {
                if (!PlayerPrefs.HasKey("hs.score" + i)) break;
                list.Add(new Entry
                {
                    name = PlayerPrefs.GetString("hs.name" + i, "---"),
                    score = PlayerPrefs.GetInt("hs.score" + i),
                    level = PlayerPrefs.GetInt("hs.level" + i, 1),
                });
            }
            return list;
        }

        public static bool Qualifies(int score)
        {
            if (score <= 0) return false;
            var list = Load();
            return list.Count < Count || score > list[list.Count - 1].score;
        }

        public static void Add(string name, int score, int level)
        {
            var list = Load();
            list.Add(new Entry { name = name, score = score, level = level });
            list.Sort((a, b) => b.score.CompareTo(a.score));
            if (list.Count > Count) list.RemoveRange(Count, list.Count - Count);
            for (int i = 0; i < list.Count; i++)
            {
                PlayerPrefs.SetString("hs.name" + i, list[i].name);
                PlayerPrefs.SetInt("hs.score" + i, list[i].score);
                PlayerPrefs.SetInt("hs.level" + i, list[i].level);
            }
            PlayerPrefs.Save();
        }
    }
}
