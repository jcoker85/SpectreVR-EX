using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpectreEX.Matrix
{
    // Finds the matrices the game can play. The ones that shipped with Spectre VR (CENTRAL.MAT,
    // BASIC.MAT) live in StreamingAssets/Matrices; with Options "Use Custom Matrix Files" ticked, any
    // .MAT files in a Matrices folder next to the game (or in the user's persistent data folder) are
    // offered too - e.g. ones made with the original VRchitect editor.
    public static class MatrixLibrary
    {
        public const string DefaultMatrix = "CENTRAL.MAT";

        public static string BundledFolder => Path.Combine(Application.streamingAssetsPath, "Matrices");
        public static string CustomFolder => Path.Combine(Application.persistentDataPath, "Matrices");
        public static string GameFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Matrices");

        static readonly Dictionary<string, MatrixFile> cache = new Dictionary<string, MatrixFile>();

        // File paths of every playable matrix, bundled first
        public static List<string> Available(bool includeCustom)
        {
            var list = new List<string>();
            AddFrom(list, BundledFolder);
            if (includeCustom)
            {
                AddFrom(list, GameFolder);
                AddFrom(list, CustomFolder);
            }
            return list;
        }

        static void AddFrom(List<string> list, string folder)
        {
            if (!Directory.Exists(folder)) return;
            var files = new List<string>(Directory.GetFiles(folder, "*.MAT", SearchOption.TopDirectoryOnly));
            files.Sort(System.StringComparer.OrdinalIgnoreCase);
            // CENTRAL first among the bundled ones, as the original game defaults to it
            files.Sort((a, b) => IsDefault(b).CompareTo(IsDefault(a)));
            foreach (var f in files)
                if (!list.Exists(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(f), System.StringComparison.OrdinalIgnoreCase)))
                    list.Add(f);
        }

        static bool IsDefault(string path) => string.Equals(Path.GetFileName(path), DefaultMatrix, System.StringComparison.OrdinalIgnoreCase);

        public static MatrixFile Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (cache.TryGetValue(path, out var m)) return m;
            try
            {
                m = MatrixFile.Load(path);
                if (!m.HasLevels) { Debug.LogWarning("Spectre VR-EX: matrix has no single-player levels: " + path); m = null; }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Spectre VR-EX: couldn't read matrix {path}: {e.Message}");
                m = null;
            }
            cache[path] = m;
            return m;
        }

        // The bundled Central Systems matrix, whose shapes stand in for a matrix that has none
        public static MatrixFile Central() => Load(Path.Combine(BundledFolder, DefaultMatrix));

        // BASIC.MAT: under every matrix in the original, and the only place the demo's levels are
        public static MatrixFile Basic() => Load(Path.Combine(BundledFolder, "BASIC.MAT"));

        // _SELECT_MATRIX, once at startup: Central Systems unless custom matrices are on; then the only
        // one besides BASIC is taken at once, and with more the player chooses (returned here, BASIC left out)
        public static List<string> ChooseAtStartup()
        {
            GameSettings.SelectedMatrix = DefaultMatrix;
            if (!GameSettings.UseCustomMatrices) return null;
            var all = Available(true);
            var choices = all.FindAll(p => !string.Equals(Path.GetFileName(p), "BASIC.MAT", System.StringComparison.OrdinalIgnoreCase));
            if (all.Count > 2) return choices;
            if (all.Count == 2 && choices.Count > 0) GameSettings.SelectedMatrix = Path.GetFileName(choices[0]);
            return null;
        }

        static readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();

        // A matrix's own picture, beside its .MAT: ".big" for the main menu, ".pcx" for the chooser
        public static Texture2D Picture(string matrixFile, string extension)
        {
            string mat = Available(true).Find(p => string.Equals(Path.GetFileName(p), matrixFile, System.StringComparison.OrdinalIgnoreCase));
            if (mat == null) return null;
            string path = Path.ChangeExtension(mat, extension);
            if (pictures.TryGetValue(path, out var tex)) return tex;
            try { if (File.Exists(path)) tex = PcxImage.Load(File.ReadAllBytes(path)); }
            catch (System.Exception e) { Debug.LogWarning($"Spectre VR-EX: couldn't read picture {path}: {e.Message}"); }
            pictures[path] = tex;
            return tex;
        }

        // The matrix chosen at startup, falling back to Central Systems, then anything playable
        public static MatrixFile Current()
        {
            var available = Available(GameSettings.UseCustomMatrices);
            string chosen = available.Find(p => string.Equals(Path.GetFileName(p), GameSettings.SelectedMatrix, System.StringComparison.OrdinalIgnoreCase));
            foreach (var path in new[] { chosen, available.Find(IsDefault) })
            {
                var m = Load(path);
                if (m != null) return m;
            }
            foreach (var path in available)
            {
                var m = Load(path);
                if (m != null) return m;
            }
            return null;
        }
    }
}
