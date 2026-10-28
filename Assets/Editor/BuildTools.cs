using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpectreEX.EditorTools
{
    // Spectre VR-EX > Build Windows EXE: a standalone 64-bit Windows build of the arena scene into
    // Builds/Windows (git-ignored). The result is written to the console and Builds/Windows/build.log.
    public static class BuildTools
    {
        public const string OutputDir = "Builds/Windows";
        public const string ExePath = OutputDir + "/Spectre VR-EX.exe";

        [MenuItem("Spectre VR-EX/Build Windows EXE")]
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Arena.unity" },
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string line = $"Spectre VR-EX build {s.result}: {s.outputPath}  size {s.totalSize / (1024 * 1024)} MB  " +
                          $"time {s.totalTime.TotalMinutes:F1} min  errors {s.totalErrors}  warnings {s.totalWarnings}";
            System.IO.Directory.CreateDirectory(OutputDir);
            System.IO.File.WriteAllText(OutputDir + "/build.log", line + "\n");
            if (s.result == BuildResult.Succeeded) Debug.Log(line); else Debug.LogError(line);
        }

        // Kick off the build on the next editor tick, so whatever asked for it can return immediately
        public static void BuildWindowsDeferred() => EditorApplication.delayCall += BuildWindows;
    }
}
