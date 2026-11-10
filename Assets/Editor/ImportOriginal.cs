using System;
using System.IO;
using System.Threading;
using SpectreEX.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpectreEX.EditorTools
{
    // Spectre VR-EX > Import Original Game: builds the game data from a copy of Spectre VR, as the
    // release's first-run setup does, into the editor's GameData.Root (the project's git-ignored GameData
    // folder). Music needs FluidSynth in Tools/fluidsynth and a General MIDI SoundFont in Tools/GeneralUser-GS.
    //
    // Play mode always starts from the Setup scene, so pressing Play in the Arena without game data
    // asks for it first; with data in place Setup goes straight on to the Arena.
    [InitializeOnLoad]
    public static class ImportOriginal
    {
        static ImportOriginal()
        {
            EditorApplication.delayCall += () =>
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ArenaBuilder.SetupScenePath);
        }

        [MenuItem("Spectre VR-EX/Import Original Game...")]
        public static void Import()
        {
            string start = GameData.LastSource ?? Path.Combine(Directory.GetCurrentDirectory(), "Tools", "SpectreVR");
            string folder = EditorUtility.OpenFolderPanel("Choose your Spectre VR folder (with SVRWIN.EXE)", Directory.Exists(start) ? start : "", "");
            if (string.IsNullOrEmpty(folder)) return;
            Run(folder);
        }

        // Also for scripted use: SpectreEX.EditorTools.ImportOriginal.Run("path/to/Spectre VR")
        public static bool Run(string folder)
        {
            string tools = Path.Combine(Directory.GetCurrentDirectory(), "Tools");
            var importer = new OriginalImporter
            {
                Game = folder,
                Output = GameData.Root,
                LayoutJson = GameData.ShapeLayout(),
                FluidSynth = Find(Path.Combine(tools, "fluidsynth"), "fluidsynth.exe"),
                SoundFont = Find(Path.Combine(tools, "GeneralUser-GS"), "*.sf2"),
            };
            importer.SkipMusic = importer.FluidSynth == null || importer.SoundFont == null;
            if (importer.SkipMusic) Debug.LogWarning("Spectre VR-EX: FluidSynth or the SoundFont isn't in Tools/, so the music is left out");

            string problem = importer.Check();
            if (problem != null)
            {
                EditorUtility.DisplayDialog("Spectre VR-EX", problem, "OK");
                return false;
            }

            var cancel = new CancellationTokenSource();
            Exception failure = null;
            var worker = new Thread(() =>
            {
                try { importer.Run(cancel.Token); }
                catch (Exception e) { failure = e; }
            });
            worker.Start();
            try
            {
                while (worker.IsAlive)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Spectre VR-EX: importing", importer.Status, importer.Progress)) cancel.Cancel();
                    Thread.Sleep(100);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            GameData.Reload();
            if (failure is OperationCanceledException) { Debug.Log("Spectre VR-EX: import cancelled"); return false; }
            if (failure != null) { Debug.LogException(failure); EditorUtility.DisplayDialog("Spectre VR-EX", "The import failed: " + failure.Message, "OK"); return false; }
            Debug.Log("Spectre VR-EX: game data imported from " + importer.Game + " into " + GameData.Root);
            return true;
        }

        static string Find(string folder, string pattern)
        {
            if (!Directory.Exists(folder)) return null;
            var hits = Directory.GetFiles(folder, pattern, SearchOption.AllDirectories);
            return hits.Length > 0 ? hits[0] : null;
        }

        [MenuItem("Spectre VR-EX/Open Game Data Folder")]
        public static void OpenFolder()
        {
            Directory.CreateDirectory(GameData.Root);
            EditorUtility.RevealInFinder(GameData.Root);
        }
    }
}
