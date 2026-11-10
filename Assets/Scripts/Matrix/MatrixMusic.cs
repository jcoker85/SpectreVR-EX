using System.Collections.Generic;

namespace SpectreEX.Matrix
{
    // Matrix song ids (from VRchitect's song list) mapped to the rendered MIDI tracks in GameData/Music.
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
            { 23001, "JBSON" }, { 23002, "BACHIN" }, { 23003, "FUGUE1" }, { 23004, "FUGUE2" }, { 23005, "FUGUE9" },
            { 23006, "FUGUE3" }, { 23007, "FUGUE7" }, { 23008, "FUGUE5" }, { 23009, "FUGUE8" }, { 23010, "FUGUE4" }, { 23011, "FUGUE4" },
            { 24001, "5THEME" }, { 24002, "AWARD" }, { 24003, "HIGAME" }, { 24004, "5THEME" },
        };

        // The track to play for a level's song id, honouring the ticked music styles.
        // Returns null for "no music".
        public static string TrackFor(int songId)
        {
            if (songId <= 0) return null;
            int style = songId / 1000 - 20, index = songId % 1000;
            if (style == 4) return Tracks.TryGetValue(songId, out var theme) ? theme : null;
            if (style >= 0 && style < 4 && GameSettings.Music[style] && Tracks.TryGetValue(songId, out var exact)) return exact;

            // _PICK_SONG: the same song number in the next ticked style round
            for (int i = 1; i <= 4; i++)
            {
                int s = (style + i) % 4;
                if (GameSettings.Music[s] && Tracks.TryGetValue(20000 + s * 1000 + index, out var next)) return next;
            }
            return null;
        }
    }
}
