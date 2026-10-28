using System.Collections.Generic;

namespace SpectreEX.Matrix
{
    // Matrix song ids (from VRchitect's song list) mapped to the rendered MIDI tracks in Resources/Music.
    // The thousands digit picks the style, matching the Options music checkboxes:
    // 20xxx Cyber/Techno, 21xxx New Age, 22xxx Rock/Rave, 23xxx VRBach, 24xxx game themes.
    public static class MatrixMusic
    {
        static readonly Dictionary<int, string> Tracks = new Dictionary<int, string>
        {
            { 20001, "LEVEL1" }, { 20002, "LEVEL2" }, { 20003, "LEVEL3" }, { 20004, "JAWS" }, { 20005, "JAWS22" },
            { 20006, "MARCH11" }, { 20007, "CW" }, { 20008, "GUITAR33" }, { 20009, "MARCH11" }, { 20010, "JAWS22" }, { 20011, "LEVEL3" },
            { 21001, "CHANT" }, { 21002, "4FLYING1" }, { 21003, "SUBMERG1" }, { 21004, "SPACEWA" }, { 21005, "MELLOW1" },
            { 21006, "CHANT" }, { 21007, "4FLYING1" }, { 21008, "SUBMERG1" }, { 21009, "SPACEWA" }, { 21010, "MELLOW1" }, { 21011, "4FLYING1" },
            { 22001, "VRGUITA" }, { 22002, "HENDRIX" }, { 22003, "RAVER20" }, { 22004, "SITARSH" }, { 22005, "CW" },
            { 22006, "FUNKY0" }, { 22007, "MARKY" }, { 22008, "HOUSEVR" }, { 22009, "HENDRIX" }, { 22010, "SITARSH" }, { 22011, "FUNKY0" },
            { 23001, "JBSON" }, { 23002, "FUGUE1" }, { 23003, "FUGUE2" }, { 23004, "FUGUE9" }, { 23005, "FUGUE3" },
            { 23006, "FUGUE7" }, { 23007, "FUGUE5" }, { 23008, "FUGUE8" }, { 23009, "FUGUE4" }, { 23010, "FUGUE4" },
            { 24001, "SPECTRE" }, { 24002, "AWARD" }, { 24003, "HIGAME" }, { 24004, "5THEME" },
        };

        // Every track in each style, for levels without a matrix song
        public static List<string> StyleTracks(int style)
        {
            var list = new List<string>();
            foreach (var kv in Tracks)
                if (kv.Key / 1000 - 20 == style && !list.Contains(kv.Value)) list.Add(kv.Value);
            return list;
        }

        // The track to play for a level's song id, honouring the ticked music styles.
        // Returns null for "no music".
        public static string TrackFor(int songId)
        {
            if (songId <= 0) return null;
            int style = songId / 1000 - 20, index = songId % 1000;
            if (style == 4) return Tracks.TryGetValue(songId, out var theme) ? theme : null;
            if (style >= 0 && style < 4 && GameSettings.Music[style] && Tracks.TryGetValue(songId, out var exact)) return exact;

            // Same song number in the first ticked style (wrapping if that style has fewer songs)
            for (int s = 0; s < 4; s++)
            {
                if (!GameSettings.Music[s]) continue;
                int count = 0;
                while (Tracks.ContainsKey(20000 + s * 1000 + count + 1)) count++;
                if (count == 0) continue;
                int id = 20000 + s * 1000 + ((index - 1) % count + count) % count + 1;
                return Tracks[id];
            }
            return null;
        }
    }
}
