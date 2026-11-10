using System;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpectreEX.Setup
{
    // The Setup scene, the build's first: if the player's game data is in place it goes straight on to
    // the Arena (the startup movie and main menu). Otherwise - the first run, or after an update that
    // changes the data - it asks for the player's Spectre VR folder, builds the data from it
    // (OriginalImporter, on a worker thread) and then goes on. Run the game with -setup to come here again.
    public class FirstRunSetup : MonoBehaviour
    {
        public const string SetupScene = "Setup", GameScene = "Arena";

        enum Stage { Ask, Importing, Failed, Done }

        Stage stage = Stage.Ask;
        string folder = "", problem, found;
        string checkedFolder;
        OriginalImporter importer;
        Thread worker;
        CancellationTokenSource cancel;
        Exception failure;
        volatile string picked;
        volatile bool picking;
        float doneAt;
        bool musicTools;
        string fluidSynth, soundFont;

        GUIStyle title, heading, body, small, field, button, primary, error, ok;
        Texture2D white;

        void Start()
        {
            bool forced = Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-setup", StringComparison.OrdinalIgnoreCase));
            if (GameData.IsReady && !forced)
            {
                SceneManager.LoadScene(GameScene);
                enabled = false;
                return;
            }
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            folder = GameData.LastSource ?? Guess() ?? "";
            FindMusicTools();
        }

        // Places a copy of Spectre VR is often found, tried before the player is asked
        static string Guess()
        {
            string exeDir = Path.GetDirectoryName(Application.dataPath) ?? ".";
            foreach (var p in new[]
            {
                Path.Combine(exeDir, "Spectre VR"), Path.Combine(exeDir, "SpectreVR"), Path.Combine(exeDir, "SPECTRE"),
                Path.Combine(exeDir, "SpectreV"), Path.Combine(exeDir, "Spectre_VR_1993"),   // the Internet Archive's zip
                @"C:\SPECTRE", @"C:\SPECTRVR", @"C:\SPECTRE.VR", @"C:\GAMES\SPECTRE", @"C:\GAMES\SPECTRVR",
            })
            {
                try { if (OriginalImporter.FindGameFolder(p) is string f) return f; }
                catch (Exception) { }
            }
            return null;
        }

        // FluidSynth and the SoundFont ship beside the game, in Setup\ (BuildTools copies them there);
        // in the editor they're the ones in Tools/fluidsynth and Tools/GeneralUser-GS
        void FindMusicTools()
        {
            string root = Path.GetDirectoryName(Application.dataPath) ?? ".";
            bool editor = Application.isEditor;
            fluidSynth = FindFile(Path.Combine(root, editor ? "Tools/fluidsynth" : "Setup/FluidSynth"), "fluidsynth.exe");
            soundFont = FindFile(Path.Combine(root, editor ? "Tools/GeneralUser-GS" : "Setup"), "*.sf2");
            musicTools = fluidSynth != null && soundFont != null;
        }

        static string FindFile(string folder, string pattern)
        {
            try
            {
                if (!Directory.Exists(folder)) return null;
                var hits = Directory.GetFiles(folder, pattern, SearchOption.AllDirectories);
                return hits.Length > 0 ? hits[0] : null;
            }
            catch (Exception) { return null; }
        }

        void Update()
        {
            if (picked != null)
            {
                folder = picked;
                picked = null;
            }

            switch (stage)
            {
                case Stage.Ask:
                    if (folder != checkedFolder) Validate();
                    break;
                case Stage.Importing:
                    if (worker != null && !worker.IsAlive)
                    {
                        worker = null;
                        if (failure is OperationCanceledException) { stage = Stage.Ask; checkedFolder = null; }
                        else if (failure != null) { stage = Stage.Failed; problem = failure.Message; Debug.LogException(failure); }
                        else { stage = Stage.Done; doneAt = Time.unscaledTime; GameData.Reload(); }
                    }
                    break;
                case Stage.Done:
                    if (Time.unscaledTime - doneAt > 0.75f) SceneManager.LoadScene(GameScene);
                    break;
            }
        }

        void Validate()
        {
            checkedFolder = folder;
            found = null;
            problem = null;
            if (string.IsNullOrWhiteSpace(folder)) return;
            var check = NewImporter();
            try { problem = check.Check(); }
            catch (Exception e) { problem = e.Message; }
            if (problem == null) found = check.Game;
        }

        OriginalImporter NewImporter() => new OriginalImporter
        {
            Game = folder,
            Output = GameData.Root,
            LayoutJson = GameData.ShapeLayout(),
            FluidSynth = fluidSynth,
            SoundFont = soundFont,
            SkipMusic = !musicTools,
        };

        void Begin()
        {
            importer = NewImporter();
            cancel = new CancellationTokenSource();
            failure = null;
            var token = cancel.Token;
            worker = new Thread(() =>
            {
                try { importer.Run(token); }
                catch (Exception e) { failure = e; }
            }) { IsBackground = true, Name = "Spectre VR-EX import" };
            stage = Stage.Importing;
            worker.Start();
        }

        void OnDestroy() => cancel?.Cancel();
        void OnApplicationQuit() => cancel?.Cancel();

        // ---------- drawing ----------

        const float W = 1280f, H = 720f;
        // The blue of "CENTRAL SYSTEMS MATRIX" and the copyright line on the matrix picture (palette 85),
        // as the main menu's own dialogs use
        static readonly Color Blue = new Color32(0x99, 0x99, 0xCC, 0xFF);
        static readonly Color Dim = new Color(0.62f, 0.68f, 0.72f);

        // The SPECTRE VR logo on the main menu picture (SPCP25, 522x332), in its pixels: the word SPECTRE and
        // the VR under it, which shares its bottom rows with the robot's hand, so only the part right of x 230
        const int LogoX = 121, LogoY = 38, LogoW = 225, LogoH = 63, HandY = 95, VrX = 230;
        const float LogoScale = 2.2f;
        Texture2D logo;
        string logoFrom;

        // The menu picture from the game data, or straight from the chosen Spectre VR folder before there is any
        void FindLogo()
        {
            string from = GameData.IsReady ? GameData.Pictures : found;
            if (from == logoFrom) return;
            logoFrom = from;
            if (logo != null && !GameData.IsReady) Destroy(logo);
            logo = null;
            if (from == null) return;
            if (GameData.IsReady) { logo = GameData.Picture("MainMenu"); return; }
            try
            {
                var d = File.ReadAllBytes(Path.Combine(from, "DATA", "SPCP25.DAT"));
                if (d.Length > 128 + 768 && d[0] == 0x0A) logo = Matrix.PcxImage.Load(d);
            }
            catch (Exception) { }
            if (logo != null && (logo.width < LogoX + LogoW || logo.height < LogoY + LogoH)) logo = null;
        }

        // Draws a piece of the logo given in picture pixels (top-down) at its place in the scaled logo
        void LogoPart(Rect at, int px, int py, int pw, int ph)
        {
            float tw = logo.width, th = logo.height;
            var uv = new Rect(px / tw, 1f - (py + ph) / th, pw / tw, ph / th);
            var r = new Rect(at.x + (px - LogoX) * LogoScale, at.y + (py - LogoY) * LogoScale, pw * LogoScale, ph * LogoScale);
            GUI.DrawTextureWithTexCoords(r, logo, uv);
        }

        void Styles()
        {
            if (title != null) return;
            white = Texture2D.whiteTexture;
            title = new GUIStyle(GUI.skin.label) { font = Resources.Load<Font>("DSEG14Classic-Bold"), fontSize = 40, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Blue;
            heading = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            heading.normal.textColor = Color.white;
            body = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
            body.normal.textColor = new Color(0.85f, 0.88f, 0.9f);
            small = new GUIStyle(body) { fontSize = 13 };
            small.normal.textColor = Dim;
            error = new GUIStyle(body) { fontSize = 15 };
            error.normal.textColor = new Color(1f, 0.45f, 0.4f);
            ok = new GUIStyle(body) { fontSize = 15 };
            ok.normal.textColor = Blue;
            field = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 4, 4) };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            primary = new GUIStyle(button) { fontStyle = FontStyle.Bold };
            primary.normal.textColor = primary.hover.textColor = Blue;
        }

        void OnGUI()
        {
            Styles();
            float scale = Mathf.Min(Screen.width / W, Screen.height / H);
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
            GUI.color = Color.white;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - W * scale) / 2f, (Screen.height - H * scale) / 2f, 0f), Quaternion.identity, Vector3.one * scale);

            float x = 240f, w = 800f, y = 36f;
            FindLogo();
            if (logo != null)
            {
                var at = new Rect((W - LogoW * LogoScale) / 2f, y, LogoW * LogoScale, LogoH * LogoScale);
                LogoPart(at, LogoX, LogoY, LogoW, HandY - LogoY);
                LogoPart(at, VrX, HandY, LogoX + LogoW - VrX, LogoY + LogoH - HandY);
            }
            else GUI.Label(new Rect(x, y + 40, w, 60), "SPECTRE!VR-EX", title);   // until a Spectre VR folder is found
            y += LogoH * LogoScale + 14;
            GUI.Label(new Rect(x, y, w, 30), stage == Stage.Importing ? "Building game data" : stage == Stage.Done ? "Ready" : "First-time setup", heading);
            y += 50;

            switch (stage)
            {
                case Stage.Ask: DrawAsk(x, y, w); break;
                case Stage.Importing: DrawProgress(x, y, w); break;
                case Stage.Failed: DrawFailed(x, y, w); break;
                case Stage.Done: DrawProgress(x, y, w); break;
            }

            GUI.Label(new Rect(x, H - 60, w, 40),
                $"Version {Application.version}.  Game data is saved to {GameData.Root}", small);
        }

        void DrawAsk(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, w, 110),
                "Spectre VR-EX plays the levels, pictures, sounds and music of the original Spectre VR (1993), " +
                "read from <b>your own copy</b> of the game. None of it is included.\n\n" +
                "Choose your Spectre VR folder: the one with <b>SVRWIN.EXE</b> and <b>CENTRAL.MAT</b> in it. " +
                "Spectre VR-EX builds its game data from it once, then starts.", body);
            y += 130;

            GUI.SetNextControlName("folder");
            folder = GUI.TextField(new Rect(x, y, w - 130, 36), folder ?? "", field);
            GUI.enabled = !picking && FolderPicker.Supported;
            if (GUI.Button(new Rect(x + w - 120, y, 120, 36), picking ? "..." : "Browse...", button))
            {
                picking = true;
                FolderPicker.Pick("Choose your Spectre VR folder", Directory.Exists(folder) ? folder : null, f =>
                {
                    if (f != null) picked = f;
                    picking = false;
                });
            }
            GUI.enabled = true;
            y += 48;

            if (found != null) GUI.Label(new Rect(x, y, w, 26), "Found Spectre VR in " + found, ok);
            else if (problem != null) GUI.Label(new Rect(x, y, w, 50), problem, error);
            y += 56;

            if (!musicTools)
                GUI.Label(new Rect(x, y - 4, w, 26), "FluidSynth or the SoundFont is missing from the Setup folder, so the game will have no music.", error);
            y += 34;

            GUI.enabled = found != null;
            if (GUI.Button(new Rect(x + w / 2 - 150, y, 300, 44), "Build game data and play", primary)) Begin();
            GUI.enabled = true;
            if (GUI.Button(new Rect(x + w / 2 - 60, y + 56, 120, 32), "Quit", button)) Quit();
        }

        void DrawProgress(float x, float y, float w)
        {
            float p = importer != null ? Mathf.Clamp01(importer.Progress) : 0f;
            GUI.Label(new Rect(x, y, w, 26), stage == Stage.Done ? "Starting Spectre VR-EX..." : (importer?.Status ?? "") + "...", body);
            y += 36;
            GUI.color = new Color(0.15f, 0.18f, 0.2f);
            GUI.DrawTexture(new Rect(x, y, w, 18), white);
            GUI.color = Blue;
            GUI.DrawTexture(new Rect(x, y, w * p, 18), white);
            GUI.color = Color.white;
            y += 30;
            GUI.Label(new Rect(x, y, w, 26), $"{Mathf.RoundToInt(p * 100)}%", small);
            y += 50;
            if (stage == Stage.Importing)
            {
                GUI.Label(new Rect(x, y, w, 26), "This takes about half a minute. The music is rendered from the game's MIDI files.", small);
                if (GUI.Button(new Rect(x + w / 2 - 60, y + 50, 120, 32), "Cancel", button)) cancel?.Cancel();
            }
        }

        void DrawFailed(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y, w, 26), "The game data couldn't be built:", body);
            GUI.Label(new Rect(x, y + 32, w, 120), problem ?? "", error);
            y += 170;
            if (GUI.Button(new Rect(x + w / 2 - 130, y, 120, 36), "Try again", button)) { stage = Stage.Ask; checkedFolder = null; }
            if (GUI.Button(new Rect(x + w / 2 + 10, y, 120, 36), "Quit", button)) Quit();
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
