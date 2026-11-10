using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpectreEX.EditorTools
{
    // Spectre VR-EX > Build Windows EXE: a standalone 64-bit Windows build into Builds/Windows (git-ignored),
    // ready to ship: the game, plus Setup/ (FluidSynth and the GeneralUser GS SoundFont, which the first-run
    // setup renders the soundtrack with, copied from Tools/), Licenses/, LICENSE.txt and the README. It's zipped as
    // Builds/SpectreVR-EX-<version>-win64.zip. The result is written to the console and Builds/Windows/build.log.
    //
    // A release must hold nothing from the original game, so the build stops if any of the old importer's
    // folders are still inside Assets, and checks the finished build for the original's files.
    public static class BuildTools
    {
        public const string OutputDir = "Builds/Windows";
        public const string ExePath = OutputDir + "/Spectre VR-EX.exe";
        static readonly string[] Scenes = { ArenaBuilder.SetupScenePath, "Assets/Scenes/Arena.unity" };

        // Where the old Python importer (Tools/import_original.py, now replaced by Setup.OriginalImporter)
        // put the original game's data, inside Assets
        static readonly string[] OldImportFolders =
        {
            "Assets/Imported", "Assets/StreamingAssets/Matrices", "Assets/StreamingAssets/Textures",
            "Assets/StreamingAssets/Movies", "Assets/Editor/Data",
        };

        [MenuItem("Spectre VR-EX/Build Windows EXE")]
        public static void BuildWindows()
        {
            string problem = OriginalDataInAssets();
            if (problem != null) { Finish("Spectre VR-EX build refused: " + problem, false); return; }
            string sky = EnsureSkybox();

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string line = $"Spectre VR-EX {Application.version} build {s.result}: {s.outputPath}  size {s.totalSize / (1024 * 1024)} MB  " +
                          $"time {s.totalTime.TotalMinutes:F1} min  errors {s.totalErrors}  warnings {s.totalWarnings}";
            if (s.result != BuildResult.Succeeded) { Finish(line, false); return; }

            try
            {
                foreach (var dir in Directory.GetDirectories(OutputDir, "*DoNotShip"))
                    Directory.Delete(dir, true);
                var notes = new List<string> { line, sky };
                notes.Add(CopyExtras());
                problem = OriginalDataInBuild();
                if (problem != null) { Finish(string.Join("\n", notes) + "\nRelease check FAILED: " + problem, false); return; }
                notes.Add("Release check: no original game files in the build");
                notes.Add("Zipped: " + Zip());
                Finish(string.Join("\n", notes), true);
            }
            catch (Exception e) { Finish(line + "\nPackaging failed: " + e, false); }
        }

        static void Finish(string text, bool ok)
        {
            Directory.CreateDirectory(OutputDir);
            File.WriteAllText(OutputDir + "/build.log", text + "\n");
            if (ok) Debug.Log(text); else Debug.LogError(text);
        }

        static string OriginalDataInAssets()
        {
            var found = OldImportFolders.Where(Directory.Exists).ToList();
            if (Directory.Exists("Assets/Meshes")) found.AddRange(Directory.GetFiles("Assets/Meshes", "Spectre_*.asset"));
            return found.Count == 0 ? null :
                "these hold data imported from the original game and would be built in; move them out of Assets: " + string.Join(", ", found);
        }

        // The star skybox (GraphicsOptions loads Resources/Skybox/Stars) is the plain-stars cubemap from the
        // free Unity Asset Store package Real Stars Skybox Lite. The Asset Store licence lets a game ship it but
        // not a public repository, so it's git-ignored: import the package and the build copies the cubemap in.
        // Without it the game builds and runs with the skybox setting doing nothing.
        public const string SkyboxPath = "Assets/Resources/Skybox/Stars.cubemap";
        const string SkyboxPackageCubemap = "Assets/Real Stars Skybox/StarSkybox04/CubemapStars.cubemap";

        static string EnsureSkybox()
        {
            if (File.Exists(SkyboxPath)) return "Skybox: " + SkyboxPath;
            if (File.Exists(SkyboxPackageCubemap))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SkyboxPath));
                AssetDatabase.Refresh();
                if (AssetDatabase.CopyAsset(SkyboxPackageCubemap, SkyboxPath)) return "Skybox: copied from Real Stars Skybox Lite";
            }
            const string none = "WARNING: no star skybox (import the free Real Stars Skybox Lite package from the Unity Asset Store)";
            Debug.LogWarning("Spectre VR-EX: " + none);
            return none;
        }

        // FluidSynth and the SoundFont the first-run setup uses, and the licences
        static string CopyExtras()
        {
            string setup = Path.Combine(OutputDir, "Setup"), licences = Path.Combine(OutputDir, "Licenses");
            if (Directory.Exists(setup)) Directory.Delete(setup, true);
            if (Directory.Exists(licences)) Directory.Delete(licences, true);
            Directory.CreateDirectory(licences);

            string note;
            var fluid = Directory.Exists("Tools/fluidsynth") ? Directory.GetFiles("Tools/fluidsynth", "fluidsynth.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
            var font = Directory.Exists("Tools/GeneralUser-GS") ? Directory.GetFiles("Tools/GeneralUser-GS", "*.sf2", SearchOption.AllDirectories).FirstOrDefault() : null;
            if (fluid != null && font != null)
            {
                string bin = Path.Combine(setup, "FluidSynth");
                Directory.CreateDirectory(bin);
                foreach (var f in Directory.GetFiles(Path.GetDirectoryName(fluid)))
                    File.Copy(f, Path.Combine(bin, Path.GetFileName(f)));
                File.Copy(font, Path.Combine(setup, Path.GetFileName(font)));
                note = "Setup: " + Path.GetFileName(fluid) + " and " + Path.GetFileName(font);
            }
            else
            {
                note = "WARNING: Tools/fluidsynth or Tools/GeneralUser-GS is missing, so this build will have no music";
                Debug.LogWarning("Spectre VR-EX: " + note);
            }

            foreach (var f in Directory.GetFiles("Licenses"))
                File.Copy(f, Path.Combine(licences, Path.GetFileName(f)));
            File.Copy("Assets/Fonts/DSEG-LICENSE.txt", Path.Combine(licences, "DSEG-LICENSE.txt"));
            File.Copy("README.md", Path.Combine(OutputDir, "README.md"), true);
            File.Copy("LICENSE", Path.Combine(OutputDir, "LICENSE.txt"), true);
            return note;
        }

        // What running the build makes beside the exe (GameData.Home): the player's own, never shipped
        static readonly string[] PlayerFolders = { "GameData", "GameData.importing", "UserData", "Matrices" };

        static IEnumerable<string> Shipped(string pattern)
        {
            string root = Path.GetFullPath(OutputDir);
            return Directory.GetFiles(root, pattern, SearchOption.AllDirectories).Where(file =>
            {
                string rel = file.Substring(root.Length + 1).Replace('\\', '/');
                return rel != "build.log" && !PlayerFolders.Any(f => rel.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase));
            });
        }

        // The original's own files, by name, anywhere in what ships
        static string OriginalDataInBuild()
        {
            var patterns = new[] { "*.MAT", "*.PCX", "*.BIG", "*.MID", "*.MOV", "SPCP*.DAT", "SVRWIN.EXE", "*.frames" };
            var hits = patterns.SelectMany(Shipped).ToList();
            return hits.Count == 0 ? null : string.Join(", ", hits);
        }

        static string Zip()
        {
            string zip = $"Builds/SpectreVR-EX-{Application.version}-win64.zip";
            if (File.Exists(zip)) File.Delete(zip);
            string root = Path.GetFullPath(OutputDir);
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (var file in Shipped("*"))
                {
                    string rel = file.Substring(root.Length + 1).Replace('\\', '/');
                    archive.CreateEntryFromFile(file, "Spectre VR-EX/" + rel, System.IO.Compression.CompressionLevel.Optimal);
                }
            }
            return zip;
        }

        // Kick off the build on the next editor tick, so whatever asked for it can return immediately
        public static void BuildWindowsDeferred() => EditorApplication.delayCall += BuildWindows;
    }
}
