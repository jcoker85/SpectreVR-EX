using System.Collections.Generic;
using System.IO;
using SpectreEX.Matrix;
using SpectreEX.Setup;
using UnityEngine;

namespace SpectreEX
{
    // Where the game reads what it needs from the player's own copy of Spectre VR. None of it ships with
    // Spectre VR-EX: Setup.OriginalImporter makes it on first run (the Setup scene) into GameData in the
    // game's own folder, so the game is portable: GameData, UserData (settings, scores, saves) and Matrices
    // all sit beside the exe. The editor keeps them in the project folder, git-ignored
    // (Spectre VR-EX > Import Original Game). Only if the game's folder can't be written to (Program Files)
    // does it fall back to %USERPROFILE%\AppData\LocalLow\<company>\<product>.
    public static class GameData
    {
        static string home;

        public static string Home
        {
            get
            {
                if (home != null) return home;
                string own = Path.GetDirectoryName(Application.dataPath) ?? ".";
                home = Application.isEditor || CanWrite(own) ? own : Application.persistentDataPath;
                return home;
            }
        }

        static bool CanWrite(string folder)
        {
            try
            {
                string probe = Path.Combine(folder, ".write-test-" + System.Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (System.Exception) { return false; }
        }

        public static string Root => Path.Combine(Home, "GameData");
        public static string UserData => Path.Combine(Home, "UserData");
        public static string Pictures => Path.Combine(Root, "Pictures");
        public static string Sounds => Path.Combine(Root, "Sounds");
        public static string Music => Path.Combine(Root, "Music");
        public static string Matrices => Path.Combine(Root, "Matrices");
        public static string Textures => Path.Combine(Root, "Textures");
        public static string Movies => Path.Combine(Root, "Movies");
        public static string Shapes => Path.Combine(Root, "Shapes");

        static Dictionary<string, string> stamp;
        static bool stampRead;

        static Dictionary<string, string> Stamp
        {
            get
            {
                if (!stampRead) { stamp = OriginalImporter.ReadStamp(Root); stampRead = true; }
                return stamp;
            }
        }

        // Forget what was read, after an import
        public static void Reload()
        {
            stampRead = false;
            stamp = null;
            pictures.Clear();
        }

        // An import by this version of the importer is in place
        public static bool IsReady =>
            Stamp != null && Stamp.TryGetValue("version", out var v) && int.TryParse(v, out int n) && n == OriginalImporter.Version;

        // The Spectre VR folder last imported from (or null)
        public static string LastSource => Stamp != null && Stamp.TryGetValue("source", out var s) ? s : null;

        // Where a song's sound ends (sample frames), so it loops on time; 0 = play it all
        public static int MusicLength(string name) =>
            Stamp != null && Stamp.TryGetValue("length." + name, out var v) && int.TryParse(v, out int n) ? n : 0;

        // The importer's built-in shape layout (Assets/Resources), made from one release of SVRWIN.EXE
        public static string ShapeLayout()
        {
            var t = Resources.Load<TextAsset>("BuiltinShapesLayout");
            return t != null ? t.text : "";
        }

        static readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();

        // A menu or HUD picture (OriginalImporter.Pictures), exact 1993 pixels
        public static Texture2D Picture(string name)
        {
            if (pictures.TryGetValue(name, out var tex) && tex != null) return tex;
            string path = Path.Combine(Pictures, name + ".pcx");
            try { tex = File.Exists(path) ? PcxImage.Load(File.ReadAllBytes(path)) : null; }
            catch (System.Exception e) { Debug.LogWarning($"Spectre VR-EX: couldn't read picture {path}: {e.Message}"); tex = null; }
            if (tex == null) Debug.LogWarning("Spectre VR-EX: missing picture " + name);
            else tex.name = name;
            pictures[name] = tex;
            return tex;
        }

        // A sound effect by its original name ("&" dropped), or null
        public static AudioClip Sound(string name) => Wav.Load(Path.Combine(Sounds, name + ".WAV"));
    }

    // The original's sounds: plain PCM WAV, 8-bit unsigned or 16-bit signed
    public static class Wav
    {
        public static AudioClip Load(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var b = File.ReadAllBytes(path);
                if (b.Length < 12 || b[0] != 'R' || b[8] != 'W') return null;
                int channels = 1, rate = 11025, bits = 8, at = 12;
                while (at + 8 <= b.Length)
                {
                    int size = System.BitConverter.ToInt32(b, at + 4);
                    if (b[at] == 'f' && b[at + 1] == 'm' && b[at + 2] == 't' && b[at + 3] == ' ')
                    {
                        if (System.BitConverter.ToInt16(b, at + 8) != 1) return null;   // PCM only
                        channels = System.BitConverter.ToInt16(b, at + 10);
                        rate = System.BitConverter.ToInt32(b, at + 12);
                        bits = System.BitConverter.ToInt16(b, at + 22);
                    }
                    else if (b[at] == 'd' && b[at + 1] == 'a' && b[at + 2] == 't' && b[at + 3] == 'a')
                    {
                        int start = at + 8, bytes = Mathf.Min(size, b.Length - start);
                        int width = bits / 8, count = bytes / width;
                        if (count < channels) return null;
                        var samples = new float[count - count % channels];
                        for (int i = 0; i < samples.Length; i++)
                            samples[i] = width == 1 ? (b[start + i] - 128) / 128f : System.BitConverter.ToInt16(b, start + i * 2) / 32768f;
                        var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), samples.Length / channels, channels, rate, false);
                        clip.SetData(samples, 0);
                        return clip;
                    }
                    at += 8 + size + (size & 1);
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"Spectre VR-EX: couldn't read sound {path}: {e.Message}"); }
            return null;
        }
    }
}
