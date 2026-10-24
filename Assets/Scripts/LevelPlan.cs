using UnityEngine;

namespace SpectreEX
{
    // What each level contains. Collect every flag to clear a level; new robot types arrive
    // with a warning, and robots get tougher and quicker as you go.
    public struct LevelPlan
    {
        public int level;
        public int flags, ammoDumps;
        public int rovers, turrets, orbiters, slicers;
        public int robotShields;
        public float difficulty;
        public float timeAllowed;
        public Color wallColor;
        public string tip;

        static readonly Color[] WallColors =
        {
            new Color(0.85f, 0.1f, 0.9f),   // magenta
            new Color(0.4f, 0.4f, 1f),      // blue-violet
            new Color(0.25f, 0.9f, 0.35f),  // green
            new Color(0.1f, 0.8f, 1f),      // cyan
            new Color(1f, 0.45f, 0.1f),     // orange
            new Color(1f, 0.15f, 0.2f),     // red
            new Color(1f, 0.85f, 0.2f),     // gold
        };

        public static LevelPlan For(int level)
        {
            var p = new LevelPlan { level = level };
            p.flags = Mathf.Min(4 + level, 14);
            p.ammoDumps = level < 5 ? 3 : 2;
            p.rovers = Mathf.Min(1 + (level + 1) / 2, 7);
            p.turrets = level >= 2 ? Mathf.Min(1 + (level - 2) / 3, 4) : 0;
            p.orbiters = level >= 4 ? Mathf.Min(1 + (level - 4) / 3, 3) : 0;
            p.slicers = level >= 6 ? Mathf.Min(1 + (level - 6) / 2, 5) : 0;
            p.robotShields = 1 + (level - 1) / 5;
            p.difficulty = 1f + (level - 1) * 0.12f;
            p.timeAllowed = 60f + p.flags * 12f;
            p.wallColor = WallColors[(level - 1) % WallColors.Length];
            p.tip = level switch
            {
                1 => "COLLECT ALL THE FLAGS",
                2 => "WATCH OUT FOR GUN TURRETS",
                4 => "WATCH OUT FOR ORBITERS",
                6 => "WATCH OUT FOR SLICERS",
                _ when level % 5 == 1 => "ROBOTS ARE GETTING TOUGHER",
                _ => "",
            };
            return p;
        }
    }
}
