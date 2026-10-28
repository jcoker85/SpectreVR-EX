using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpectreEX.Matrix
{
    // A Spectre VR "matrix" (.MAT): the game's level files, as made by Velocity or with the VRchitect
    // editor. The file is a big-endian resource container: "SSWF", a count, then a directory of
    // (4-char type, id, offset, size) entries. The resource types used here:
    //
    //   wave  level list for one difficulty: per level 8 words
    //         (grid id, enemy set id, tip number, enemy data id, special weapon, ?, song id, 999)
    //   wrld  game grid: count, then (obstacle type, x, y, heading) on a 1024 x 1024 grid; flags first
    //   enem  enemy set: count, then (robot type, max at start, min at start, regeneration multiplier)
    //   bval  enemy data: count, then 14 words per robot type (see EnemyData)
    //   wval  weapons: count, then (weapon id, damage, ammo used)
    //   sstf  extra lives: 7 header words, 10 score thresholds (32-bit), then the increment after those
    //   STR#  string lists: 1007 = level tips, 5000 = matrix name / author / description
    public class MatrixFile
    {
        public string Path { get; private set; }
        public string Name { get; private set; }
        public string Author { get; private set; }
        public string Description { get; private set; }

        // Levels per difficulty (0 Easy, 1 Medium, 2 Hard)
        public readonly List<Level>[] Levels = { new List<Level>(), new List<Level>(), new List<Level>() };
        public readonly Dictionary<int, Grid> Grids = new Dictionary<int, Grid>();
        public readonly Dictionary<int, List<EnemyCount>> EnemySets = new Dictionary<int, List<EnemyCount>>();
        public readonly Dictionary<int, Dictionary<int, EnemyData>> EnemyDataSets = new Dictionary<int, Dictionary<int, EnemyData>>();
        public readonly Dictionary<int, Weapon> Weapons = new Dictionary<int, Weapon>();
        public readonly List<string> Tips = new List<string>();
        public readonly ExtraLives[] Lives = new ExtraLives[3];

        public struct Level
        {
            public int grid, enemySet, tip, enemyData, special, song;
        }

        public struct Obstacle
        {
            public int type, x, y, heading;
        }

        public class Grid
        {
            public int id;
            public readonly List<Obstacle> obstacles = new List<Obstacle>();
        }

        public struct EnemyCount
        {
            public int type, max, min, regen;
        }

        // VRchitect's "Enemy Data" fields
        public struct EnemyData
        {
            public int type, strength, collideDamage, attackRange, firingRange, firingArc, firingInterval, speed, turnSpeed;
            public int score, bonus, weapon;
        }

        public struct Weapon
        {
            public int id, damage, ammo;
        }

        public struct ExtraLives
        {
            public int[] thresholds;   // score for the 1st..10th extra tank
            public int increment;      // then one more every this many points
        }

        // ---------- loading ----------

        public static MatrixFile Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var m = new MatrixFile { Path = path };
            m.Parse(bytes);
            if (string.IsNullOrEmpty(m.Name)) m.Name = System.IO.Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
            return m;
        }

        void Parse(byte[] d)
        {
            if (d.Length < 6 || d[0] != 'S' || d[1] != 'S' || d[2] != 'W' || d[3] != 'F')
                throw new InvalidDataException("Not a Spectre matrix (missing SSWF header)");

            int n = U16(d, 4);
            var waves = new SortedDictionary<int, byte[]>();
            for (int i = 0; i < n; i++)
            {
                int o = 6 + i * 14;
                string type = System.Text.Encoding.ASCII.GetString(d, o, 4);
                int id = U16(d, o + 4);
                int off = (int)U32(d, o + 6);
                int size = (int)U32(d, o + 10);
                if (off < 0 || size < 0 || off + size > d.Length) continue;
                var res = new byte[size];
                Buffer.BlockCopy(d, off, res, 0, size);
                switch (type)
                {
                    case "wave": waves[id] = res; break;
                    case "wrld": Grids[id] = ParseGrid(id, res); break;
                    case "enem": EnemySets[id] = ParseEnemySet(res); break;
                    case "bval": EnemyDataSets[id] = ParseEnemyData(res); break;
                    case "wval": ParseWeapons(res); break;
                    case "sstf": if (id - 10000 >= 0 && id - 10000 < 3) Lives[id - 10000] = ParseLives(res); break;
                    case "STR#":
                        var strings = ParseStrings(res);
                        if (id == 1007) { Tips.Clear(); Tips.AddRange(strings); }
                        if (id == 5000)
                        {
                            if (strings.Count > 0) Name = strings[0].Trim();
                            if (strings.Count > 1) Author = strings[1].Trim();
                            if (strings.Count > 2) Description = strings[2].Trim();
                        }
                        break;
                }
            }

            // Single-player level lists are the ones with robots in them (BASIC.MAT also has a robot-free
            // list, the network flag games). In id order they are Easy, Medium, Hard; with fewer lists
            // the last one is reused for the harder settings.
            var lists = new List<List<Level>>();
            foreach (var kv in waves)
            {
                var levels = ParseLevels(kv.Value);
                if (levels.Exists(l => l.enemySet != 0)) lists.Add(levels);
            }
            if (lists.Count == 0)
                foreach (var kv in waves) lists.Add(ParseLevels(kv.Value));
            for (int diff = 0; diff < 3 && lists.Count > 0; diff++)
                Levels[diff].AddRange(lists[Mathf.Min(diff, lists.Count - 1)]);
        }

        public bool HasLevels => Levels[0].Count > 0;

        static List<Level> ParseLevels(byte[] b)
        {
            var w = Words(b);
            var list = new List<Level>();
            for (int i = 1; i + 7 < w.Length; i += 8)
                list.Add(new Level { grid = w[i], enemySet = w[i + 1], tip = w[i + 2], enemyData = w[i + 3], special = w[i + 4], song = w[i + 6] });
            return list;
        }

        static Grid ParseGrid(int id, byte[] b)
        {
            var w = Words(b);
            var g = new Grid { id = id };
            for (int i = 1; i + 3 < w.Length; i += 4)
                g.obstacles.Add(new Obstacle { type = w[i], x = w[i + 1], y = w[i + 2], heading = w[i + 3] });
            return g;
        }

        static List<EnemyCount> ParseEnemySet(byte[] b)
        {
            var w = Words(b);
            var list = new List<EnemyCount>();
            for (int i = 1; i + 3 < w.Length; i += 4)
                list.Add(new EnemyCount { type = w[i], max = w[i + 1], min = w[i + 2], regen = w[i + 3] });
            return list;
        }

        static Dictionary<int, EnemyData> ParseEnemyData(byte[] b)
        {
            var w = Words(b);
            var dict = new Dictionary<int, EnemyData>();
            for (int i = 1; i + 13 < w.Length; i += 14)
            {
                var e = new EnemyData
                {
                    type = w[i], strength = w[i + 1], collideDamage = w[i + 2], attackRange = w[i + 3], firingRange = w[i + 4],
                    firingArc = w[i + 5], firingInterval = w[i + 6], speed = w[i + 7], turnSpeed = w[i + 8],
                    score = Long(w[i + 9], w[i + 10]), bonus = Long(w[i + 11], w[i + 12]), weapon = w[i + 13],
                };
                dict[e.type] = e;
            }
            return dict;
        }

        void ParseWeapons(byte[] b)
        {
            var w = Words(b);
            for (int i = 1; i + 2 < w.Length; i += 3)
                Weapons[w[i]] = new Weapon { id = w[i], damage = w[i + 1], ammo = w[i + 2] };
        }

        static ExtraLives ParseLives(byte[] b)
        {
            var w = Words(b);
            var lives = new ExtraLives { thresholds = new int[10] };
            for (int i = 0; i < 10 && 8 + i * 2 < w.Length; i++) lives.thresholds[i] = Long(w[7 + i * 2], w[8 + i * 2]);
            lives.increment = w.Length > 27 ? (ushort)w[27] : 30000;
            return lives;
        }

        static List<string> ParseStrings(byte[] b)
        {
            var list = new List<string>();
            if (b.Length < 2) return list;
            int count = U16(b, 0), o = 2;
            for (int i = 0; i < count && o < b.Length; i++)
            {
                int len = b[o];
                if (o + 1 + len > b.Length) break;
                var chars = new char[len];
                for (int c = 0; c < len; c++) chars[c] = (char)b[o + 1 + c];   // Mac Roman / Latin-1: ASCII is all that's used
                list.Add(new string(chars));
                o += 1 + len;
            }
            return list;
        }

        // ---------- helpers ----------

        static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
        static uint U32(byte[] d, int o) => (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
        static int Long(int hi, int lo) => (hi << 16) | (lo & 0xFFFF);

        static short[] Words(byte[] b)
        {
            var w = new short[b.Length / 2];
            for (int i = 0; i < w.Length; i++) w[i] = (short)((b[i * 2] << 8) | b[i * 2 + 1]);
            return w;
        }

        // The extra-life score after which the n-th (1-based) extra tank is awarded
        public int ExtraLifeScore(int difficulty, int n)
        {
            var l = Lives[Mathf.Clamp(difficulty, 0, 2)];
            if (l.thresholds == null) return n * 10000;
            if (n <= 10) return l.thresholds[n - 1];
            return l.thresholds[9] + (n - 10) * Mathf.Max(1, l.increment);
        }

        public string Tip(int number) => number >= 1 && number <= Tips.Count ? Tips[number - 1] : "";

        // Matrices like BASIC.MAT carry no enemy data of their own for some ids; fall back to any set
        public bool TryGetEnemyData(int setId, int type, out EnemyData data)
        {
            if (EnemyDataSets.TryGetValue(setId, out var set) && set.TryGetValue(type, out data)) return true;
            foreach (var s in EnemyDataSets.Values)
                if (s.TryGetValue(type, out data)) return true;
            data = default;
            return false;
        }
    }
}
