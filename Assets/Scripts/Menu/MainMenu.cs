using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpectreEX.Menu
{
    // The original Spectre VR main menu and its dialogs, rebuilt from the game's own artwork
    // (imported from the DATA folder by Tools/import_original.py) at the largest whole-number scale that
    // fits the screen, with point filtering so the 1993 pixels stay crisp.
    //
    //   PLAY      -> Choose a vehicle (Balance / Speedy / Strong / Custom, Edit Custom..., Cancel, Play)
    //   CYBERNET  -> network play notice (not available)
    //   OPTIONS   -> the original Options dialog, wired to GameSettings
    //   SCORE     -> high scores (and name entry after a qualifying game)
    //   HELP      -> the original "Welcome to Spectre VR" page
    //   ABOUT     -> the original credits
    //   DEMO      -> demo mode
    //   QUIT      -> quit
    // The big panel shows the matrix's own picture; the matrix is settled at startup, before the menu
    // (with custom matrices on and several to pick from, the Choose Matrix dialog comes up first).
    // Keys: the first letter of each button, and holding Alt while starting a game
    // begins at a higher level, as in the original.
    public class MainMenu : MonoBehaviour
    {
        public int altStartLevel = 5;

        const int MainW = 522, MainH = 332;

        // ---------- small screen model: an image plus hotspots, in the image's own pixels ----------

        class Hotspot
        {
            public Rect rect;               // image pixels, y down from the top
            public System.Action action;
            public RawImage hover;          // optional replacement tile shown on hover
            public Image tint;              // optional darkening shown on hover
            public Key key = Key.None;
            public System.Action rightClick;
        }

        class MenuScreen
        {
            public string name;
            public RectTransform root;
            public Vector2 size;            // image pixels
            public int scale = 1;           // screen pixels to each image pixel
            public readonly List<Hotspot> hotspots = new List<Hotspot>();
            public readonly List<(RectTransform rt, Rect r)> placed = new List<(RectTransform, Rect)>();
            public readonly List<(PixelSegmentText t, int size)> texts = new List<(PixelSegmentText, int)>();
            public System.Action onEscape, onEnter, onAnyClick;
            public System.Action refresh;
        }

        Canvas canvas;
        RectTransform canvasRoot;
        Texture2D buttonsTex;
        Sprite dotSprite;
        int scale = 1;
        Vector2Int lastScreen;
        readonly List<MenuScreen> stack = new List<MenuScreen>();
        MenuScreen main, vehicles, custom, options, matrix, about, help, scores, notice;
        PixelSegmentText noticeText;
        GameManager gm;
        List<string> matrixChoices;

        // Name entry for the high-score table
        bool enteringName;
        string pendingName = "";
        int pendingScore, pendingLevel;
        readonly List<PixelSegmentText[]> scoreRows = new List<PixelSegmentText[]>();

        public bool IsOpen => canvas != null && canvas.enabled;

        void Start()
        {
            if (LoadTex("MainMenu") == null)
            {
                Debug.LogError("Spectre VR-EX: no imported game data. Run Tools/import_original.py on your copy of Spectre VR.");
                enabled = false;
                return;
            }
            gm = GameManager.Instance;
            GameSettings.Load();
            buttonsTex = LoadTex("Buttons");
            dotSprite = MakeDotSprite();
            BuildCanvas();
            BuildScreens();
            if (gm != null)
            {
                gm.ReturnedToMenu += OnReturnedToMenu;
                gm.Suspend += () => Open(keepMusic: true);
            }
            if (Keyboard.current != null) Keyboard.current.onTextInput += OnTextInput;
            GraphicsOptions.Load();
            GameKeys.Load();
            Sfx.Play(Snd.Logo);
            matrixChoices = Matrix.MatrixLibrary.ChooseAtStartup();
            main.refresh();
            StartCoroutine(FirstOpen());
        }

        // The startup movie, then the menu with its theme, so neither talks over the other
        IEnumerator FirstOpen()
        {
            canvas.enabled = false;
            yield return Matrix.StartupMovie.Play(this);
            Open();
            if (matrixChoices != null) Push(matrix);
        }

        void OnDestroy()
        {
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInput;
        }

        // ---------- open / close ----------

        // keepMusic: opened over a paused game (Esc), so leave the level's song paused rather than switching
        public void Open(bool keepMusic = false)
        {
            canvas.enabled = true;
            stack.Clear();
            foreach (var s in AllScreens()) s.root.gameObject.SetActive(false);
            Push(main);
            if (!keepMusic && MusicPlayer.Instance != null) MusicPlayer.Instance.PlayMenuTheme();
        }

        bool GameSuspended => gm != null && gm.InGame && gm.Suspended;

        // Esc on the main menu while a game is paused behind it: straight back into the game
        void ResumeGame()
        {
            if (!GameSuspended) return;
            Close();
            gm.Resume();
        }

        // Jump straight to a dialog by name ("Vehicles", "Options", "Scores", ...), over the main menu
        public void OpenScreen(string name)
        {
            if (!IsOpen) Open();
            foreach (var s in AllScreens())
                if (s.name == name && s != main) { Push(s); return; }
        }

        void Close()
        {
            canvas.enabled = false;
            stack.Clear();
        }

        void OnReturnedToMenu(int score, int level)
        {
            Open();
            if (HighScores.Qualifies(score))
            {
                // _CHECK_HIGHSCORES: its own song for the best score of all, the award song otherwise
                var best = HighScores.Load();
                if (MusicPlayer.Instance != null) MusicPlayer.Instance.PlayTrack(best.Count == 0 || score > best[0].score ? "HIGAME" : "AWARD");
                enteringName = true;
                pendingName = "";
                pendingScore = score;
                pendingLevel = level;
                Push(scores);
            }
        }

        void StartGame(bool demo)
        {
            var k = Keyboard.current;
            bool alt = k != null && (k.leftAltKey.isPressed || k.rightAltKey.isPressed);
            GameSettings.Save();
            Close();
            gm.StartGame(GameSettings.Vehicle(GameSettings.SelectedVehicle), alt ? altStartLevel : 1, demo);
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- construction ----------

        void BuildCanvas()
        {
            var go = new GameObject("SpectreMenu", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvas.pixelPerfect = true;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            canvasRoot = (RectTransform)go.transform;

            // Black surround for widescreen
            var bg = new GameObject("Backdrop", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(canvasRoot, false);
            bg.color = Color.black;
            bg.raycastTarget = false;
            var rt = bg.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        IEnumerable<MenuScreen> AllScreens() => new[] { main, vehicles, custom, options, graphics, controls, pick, matrix, about, help, scores, notice };

        void BuildScreens()
        {
            BuildMain();
            BuildVehicles();
            BuildCustom();
            BuildOptions();
            BuildMatrix();
            about = BlackScreen("About", "About");
            about.onAnyClick = about.onEscape = about.onEnter = Pop;
            help = BlackScreen("Help", "Help");
            help.onAnyClick = help.onEscape = help.onEnter = Pop;
            BuildScores();
            BuildNotice();
            BuildGraphics();
            BuildControls();
            Relayout();
        }

        MenuScreen NewScreen(string name, Vector2 size)
        {
            var s = new MenuScreen { name = name, size = size };
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            s.root = (RectTransform)go.transform;
            s.root.anchorMin = s.root.anchorMax = s.root.pivot = new Vector2(0.5f, 0.5f);
            go.SetActive(false);
            return s;
        }

        MenuScreen ImageScreen(string name, string tex)
        {
            var t = LoadTex(tex);
            var s = NewScreen(name, new Vector2(t.width, t.height));
            AddRaw(s, t, new Rect(0, 0, t.width, t.height), new Rect(0, 0, 1, 1));
            return s;
        }

        // A picture on black, not over the menu
        MenuScreen BlackScreen(string name, string tex)
        {
            var t = LoadTex(tex);
            var s = NewScreen(name, new Vector2(t.width, t.height));
            AddRect(s, new Rect(-2000, -2000, 4000 + t.width, 4000 + t.height), Color.black);
            AddRaw(s, t, new Rect(0, 0, t.width, t.height), new Rect(0, 0, 1, 1));
            return s;
        }

        void BuildMain()
        {
            main = ImageScreen("Main", "MainMenu");
            var picture = AddRaw(main, null, new Rect(7, 13, 368, 280), new Rect(0, 0, 1, 1));
            main.refresh = () =>
            {
                picture.texture = Matrix.MatrixLibrary.Picture(GameSettings.SelectedMatrix, ".big")
                    ?? Matrix.MatrixLibrary.Picture(Matrix.MatrixLibrary.DefaultMatrix, ".big");
                picture.enabled = picture.texture != null;
            };
            AddRaw(main, buttonsTex, new Rect(377, 229, 64, 64), TileUv(0, 3));   // the menu art has MATRIX here
            Tile(main, 377, 25, 1, 0, () => Push(vehicles), Key.P);
            Tile(main, 449, 25, 3, 0, () => ShowNotice("CYBERNET NETWORK PLAY\nIS NOT AVAILABLE\nIN THIS VERSION"), Key.C);
            Tile(main, 377, 93, 1, 1, () => Push(ShiftHeld ? settingsPage : options), Key.O);
            optionsHue = AddRect(main, new Rect(377, 93, 64, 64), new Color(0.55f, 0.1f, 1f, 0.5f));
            optionsHue.enabled = false;
            Tile(main, 449, 93, 3, 1, () => { enteringName = false; Push(scores); }, Key.S);
            Tile(main, 377, 161, 1, 2, () => Push(help), Key.H);
            Tile(main, 449, 161, 3, 2, () => Push(about), Key.A);
            Tile(main, 377, 229, 1, 3, () => StartGame(true), Key.D);
            Tile(main, 449, 229, 3, 3, Quit, Key.Q);
            main.onEnter = () => Push(vehicles);
            main.onEscape = ResumeGame;
        }

        static bool ShiftHeld
        {
            get
            {
                var k = Keyboard.current;
                return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
            }
        }

        // A main-menu button: hovering swaps in the original's highlighted tile from Buttons.png
        void Tile(MenuScreen s, int x, int y, int col, int row, System.Action action, Key key)
        {
            var h = new Hotspot { rect = new Rect(x, y, 64, 64), action = action, key = key };
            h.hover = AddRaw(s, buttonsTex, h.rect, TileUv(col, row));
            h.hover.enabled = false;
            s.hotspots.Add(h);
        }

        Rect TileUv(int col, int row) => new Rect(col * 64f / buttonsTex.width, 1f - (row + 1) * 64f / buttonsTex.height, 64f / buttonsTex.width, 64f / buttonsTex.height);

        // A dialog button: hovering darkens it slightly, like a pressed Mac button (or brightens it, on our dark panels)
        Hotspot Button(MenuScreen s, Rect r, System.Action action, Key key = Key.None, Color? tint = null)
        {
            var h = new Hotspot { rect = r, action = action, key = key };
            if (r.width > 0)
            {
                h.tint = AddRect(s, r, tint ?? new Color(0f, 0f, 0f, 0.18f));
                h.tint.enabled = false;
            }
            s.hotspots.Add(h);
            return h;
        }

        void BuildVehicles()
        {
            vehicles = ImageScreen("Vehicles", "Vehicles");
            int[] rowY = { 40, 104, 168, 232 };
            Key[] keys = { Key.B, Key.S, Key.T, Key.U };
            var frames = new List<Image[]>();
            var stats = new List<PixelSegmentText[]>();
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                Button(vehicles, new Rect(15, rowY[i], 270, 53), () => { GameSettings.SelectedVehicle = idx; vehicles.refresh(); }, keys[i]);
                frames.Add(Frame(vehicles, new Rect(12, rowY[i] - 3, 81, 59), new Color(1f, 0.1f, 0.1f), 2));
                stats.Add(new[]
                {
                    AddText(vehicles, new Rect(352, rowY[i] + 2, 40, 16), 12, Color.black, TextAnchor.MiddleLeft),
                    AddText(vehicles, new Rect(352, rowY[i] + 18, 40, 16), 12, Color.black, TextAnchor.MiddleLeft),
                    AddText(vehicles, new Rect(352, rowY[i] + 34, 40, 16), 12, Color.black, TextAnchor.MiddleLeft),
                });
            }
            Button(vehicles, new Rect(22, 297, 124, 27), () => { GameSettings.SelectedVehicle = 3; Push(custom); }, Key.E);
            Button(vehicles, new Rect(205, 297, 70, 27), Pop);
            Button(vehicles, new Rect(303, 297, 70, 27), () => StartGame(false));
            vehicles.onEscape = Pop;
            vehicles.onEnter = () => StartGame(false);
            vehicles.refresh = () =>
            {
                for (int i = 0; i < 4; i++)
                {
                    foreach (var f in frames[i]) f.enabled = GameSettings.SelectedVehicle == i;
                    var v = GameSettings.Vehicle(i);
                    stats[i][0].Text = v.speed.ToString();
                    stats[i][1].Text = v.shields.ToString();
                    stats[i][2].Text = v.ammo.ToString();
                }
            };
        }

        void BuildCustom()
        {
            custom = ImageScreen("Custom", "Custom");
            var points = AddText(custom, new Rect(160, 49, 40, 18), 14, Color.black, TextAnchor.MiddleLeft);
            var speed = AddText(custom, new Rect(170, 97, 40, 18), 14, Color.black, TextAnchor.MiddleLeft);
            var shields = AddText(custom, new Rect(170, 144, 40, 18), 14, Color.black, TextAnchor.MiddleLeft);
            var ammo = AddText(custom, new Rect(170, 191, 40, 18), 14, Color.black, TextAnchor.MiddleLeft);

            void Adjust(int field, int delta)
            {
                var c = GameSettings.Custom;
                int left = VehicleSpec.TotalPoints - (c.speed + c.shields + c.ammo);
                if (delta > 0 && left <= 0) return;
                switch (field)
                {
                    case 0: c.speed = Mathf.Clamp(c.speed + delta, 1, 10); break;
                    case 1: c.shields = Mathf.Clamp(c.shields + delta, 1, 10); break;
                    case 2: c.ammo = Mathf.Clamp(c.ammo + delta, 1, 5); break;
                }
                GameSettings.Custom = c;
                custom.refresh();
            }

            Button(custom, new Rect(231, 89, 36, 16), () => Adjust(0, 1));
            Button(custom, new Rect(231, 105, 36, 16), () => Adjust(0, -1));
            Button(custom, new Rect(231, 136, 36, 16), () => Adjust(1, 1));
            Button(custom, new Rect(231, 152, 36, 16), () => Adjust(1, -1));
            Button(custom, new Rect(231, 182, 36, 16), () => Adjust(2, 1));
            Button(custom, new Rect(231, 198, 36, 16), () => Adjust(2, -1));
            Button(custom, new Rect(113, 249, 113, 33), () => { GameSettings.Save(); Pop(); });
            custom.onEscape = custom.onEnter = () => { GameSettings.Save(); Pop(); };
            custom.refresh = () =>
            {
                var c = GameSettings.Custom;
                points.Text = (VehicleSpec.TotalPoints - (c.speed + c.shields + c.ammo)).ToString();
                speed.Text = c.speed.ToString();
                shields.Text = c.shields.ToString();
                ammo.Text = c.ammo.ToString();
            };
        }

        void BuildOptions()
        {
            options = BlackScreen("Options", "Options");
            var refreshers = new List<System.Action>();

            // (x, y) is the top-left of each control's 11x11 interior, measured from Options.png
            void Check(int x, int y, System.Func<bool> get, System.Action toggle, bool enabled = true)
            {
                var mark = CheckMark(options, x, y);
                Button(options, new Rect(x - 3, y - 3, 128, 17), enabled ? () => { toggle(); options.refresh(); } : (System.Action)null);
                refreshers.Add(() => mark.SetActive(get()));
            }
            void Radio(int x, int y, System.Func<bool> get, System.Action select)
            {
                var dot = AddImage(options, new Rect(x + 3, y + 3, 5, 5), Color.black, dotSprite);
                Button(options, new Rect(x - 3, y - 3, 128, 17), () => { select(); options.refresh(); });
                refreshers.Add(() => dot.enabled = get());
            }

            Check(19, 38, () => GameSettings.Joystick, () => GameSettings.Joystick = !GameSettings.Joystick);
            // In place of the original's three sound cards: on/off rows made from the dialog's own box and headings
            var art = LoadTex("Options");
            Rect Art(float x, float y, float w, float h) => new Rect(x / art.width, 1f - (y + h) / art.height, w / art.width, h / art.height);
            AddRect(options, new Rect(12, 89, 126, 62), Color.white);
            void SoundRow(int y, Rect label, System.Func<bool> get, System.Action toggle)
            {
                AddRaw(options, art, new Rect(18, y - 1, 13, 13), Art(18, 37, 13, 13));
                AddRaw(options, art, new Rect(36, y - 1, label.width, label.height), Art(label.x, label.y, label.width, label.height));
                Check(19, y, get, toggle);
            }
            SoundRow(100, new Rect(19, 75, 50, 13), () => GameSettings.SoundEffectsOn, () => GameSettings.SoundEffectsOn = !GameSettings.SoundEffectsOn);
            SoundRow(124, new Rect(19, 154, 42, 13), () => GameSettings.MusicOn, () => GameSettings.MusicOn = !GameSettings.MusicOn);
            int[] musicY = { 174, 202, 230, 258 };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                Check(21, musicY[i], () => GameSettings.Music[idx], () => GameSettings.Music[idx] = !GameSettings.Music[idx]);
            }
            Radio(167, 38, () => GameSettings.Difficulty == Difficulty.Easy, () => GameSettings.Difficulty = Difficulty.Easy);
            Radio(167, 62, () => GameSettings.Difficulty == Difficulty.Normal, () => GameSettings.Difficulty = Difficulty.Normal);
            Radio(167, 86, () => GameSettings.Difficulty == Difficulty.Hard, () => GameSettings.Difficulty = Difficulty.Hard);
            Check(167, 110, () => GameSettings.UseCustomMatrices, () => GameSettings.UseCustomMatrices = !GameSettings.UseCustomMatrices);
            Check(169, 174, () => GameSettings.DisplayComplex, () => GameSettings.DisplayComplex = !GameSettings.DisplayComplex);
            Check(169, 202, () => GameSettings.DisplayFilled, () => GameSettings.DisplayFilled = !GameSettings.DisplayFilled);
            Check(169, 230, () => GameSettings.DisplayTextures, () => GameSettings.DisplayTextures = !GameSettings.DisplayTextures);
            AddRaw(options, MoviesLabel(), new Rect(187, 259, MoviesRows[0].Length, MoviesRows.Length), new Rect(0, 0, 1, 1));
            Check(169, 258, () => GameSettings.DisplayMovies, () => GameSettings.DisplayMovies = !GameSettings.DisplayMovies);

            Button(options, new Rect(11, 287, 170, 41), () => ShowNotice("NETWORK PLAY\nIS NOT AVAILABLE\nIN THIS VERSION"));
            System.Action done = () =>
            {
                GameSettings.Save();
                if (gm != null) gm.ApplySettings();
                // Over a paused game the level's song stays paused; otherwise restart the theme (music settings may have changed)
                if (!GameSuspended && MusicPlayer.Instance != null) MusicPlayer.Instance.PlayMenuTheme();
                Pop();
            };
            Button(options, new Rect(199, 287, 170, 41), done);
            options.onEscape = options.onEnter = done;
            options.refresh = () => { foreach (var r in refreshers) r(); };
        }

        // The original greys its Movies option out because it had no switch for them. Ours works, so the word
        // is redrawn solid, stroke for stroke over the dithered one.
        static readonly string[] MoviesRows =
        {
            "##......##...................##...............",
            "###....###...................##...............",
            "####..####....................................",
            "##.####.##...####..##....##..##...####...####.",
            "##..##..##..##..##.##....##..##..##..##.##..##",
            "##..##..##..##..##..##..##...##..######.##....",
            "##......##..##..##..##..##...##..##......####.",
            "##......##..##..##...####....##..##.........##",
            "##......##..##..##...####....##..##..##.##..##",
            "##......##...####.....##.....##...####...####.",
            "..............................................",
            "#########.....................................",
            "#########....................................."
        };

        static Texture2D MoviesLabel()
        {
            int w = MoviesRows[0].Length, h = MoviesRows.Length;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, h - 1 - y, MoviesRows[y][x] == '#' ? Color.black : Color.white);
            tex.Apply();
            return tex;
        }

        // _SELECT_MATRIX's dialog, at startup only: Previous / Next step through the matrices, each with
        // its own small picture, and Choose settles it - there is no way out without choosing
        void BuildMatrix()
        {
            matrix = ImageScreen("Matrix", "Matrix");
            var name = AddText(matrix, new Rect(20, 29, 256, 12), 9, Color.black, TextAnchor.MiddleCenter);
            var picture = AddRaw(matrix, null, new Rect(116, 52, 64, 64), new Rect(0, 0, 1, 1));
            int index = 0;

            void Show()
            {
                string file = System.IO.Path.GetFileName(matrixChoices[index]);
                var m = Matrix.MatrixLibrary.Load(matrixChoices[index]);
                name.Text = Seg(m != null ? m.Name : System.IO.Path.GetFileNameWithoutExtension(file) + " (UNREADABLE)");
                picture.texture = Matrix.MatrixLibrary.Picture(file, ".pcx");
                picture.enabled = picture.texture != null;
            }
            void Step(int delta)
            {
                index = Mathf.Clamp(index + delta, 0, matrixChoices.Count - 1);
                Show();
            }
            void Choose()
            {
                if (Matrix.MatrixLibrary.Load(matrixChoices[index]) == null) { Sfx.Play(Snd.Collision); return; }
                GameSettings.SelectedMatrix = System.IO.Path.GetFileName(matrixChoices[index]);
                GameSettings.Save();
                main.refresh();
                Pop();
            }

            Button(matrix, new Rect(15, 83, 77, 29), () => Step(-1), Key.P);
            Button(matrix, new Rect(203, 83, 77, 29), () => Step(1), Key.N);
            Button(matrix, new Rect(108, 136, 77, 29), Choose, Key.C);
            matrix.onEnter = Choose;
            matrix.refresh = Show;
        }

        // _SHOW_HIGHSCORES: all ten rows, blank ones as score 0, level 1, laid out as on the original's
        // panel but in the HUD's green on black
        // ---------- the modern settings: Shift + Options ----------

        // One line of the settings list. A new option is one more entry in Settings(): a drop-down, an
        // on/off choice or a number, and shown can hide it until something else makes it apply.
        class SettingRow
        {
            public string label;
            public System.Func<string> value;
            public System.Action open;        // a drop-down
            public System.Action<int> step;   // a number
            public System.Func<bool> shown;   // leave null for always
        }

        const int PickRows = 10, SettingRows = 7;
        const int SettingsW = 480, SettingsH = 340;
        MenuScreen graphics, controls, pick, settingsPage;
        Image optionsHue;
        List<SettingRow> settingRows, shownRows = new List<SettingRow>();
        int settingsTop;
        List<string> pickChoices = new List<string>();
        System.Action<int> pickChosen;
        int pickTop, pickCurrent;

        SettingRow Drop(string label, System.Func<string> value, System.Func<List<string>> choices, System.Func<int> current, System.Action<int> chosen) =>
            new SettingRow { label = label, value = value, open = () => OpenPick(choices(), current(), chosen) };

        SettingRow Choice(string label, string key, string[] names) =>
            Drop(label, () => names[GraphicsOptions.Get(key)], () => new List<string>(names), () => GraphicsOptions.Get(key), i => GraphicsOptions.Set(key, i));

        SettingRow Toggle(string label, string key) =>
            Drop(label, () => GraphicsOptions.On(key) ? "On" : "Off", () => new List<string> { "On", "Off" },
                () => GraphicsOptions.On(key) ? 0 : 1, i => GraphicsOptions.Set(key, i == 0 ? 1 : 0));

        SettingRow Number(string label, string key, int min, int max, int by, string unit = "") => new SettingRow
        {
            label = label,
            value = () => GraphicsOptions.Get(key) + unit,
            step = sign => GraphicsOptions.Set(key, Mathf.Clamp(GraphicsOptions.Get(key) + sign * by, min, max)),
        };

        static SettingRow When(System.Func<bool> shown, SettingRow row)
        {
            row.shown = shown;
            return row;
        }

        List<SettingRow> Settings()
        {
            List<Vector2Int> sizes = null;
            System.Func<bool> tracing = () => GraphicsOptions.On(GraphicsOptions.RayTracing);
            System.Func<bool> reflecting = () => tracing() && GraphicsOptions.On(GraphicsOptions.Reflections);
            System.Func<bool> lighting = () => tracing() && GraphicsOptions.On(GraphicsOptions.Lighting);
            System.Func<bool> occluding = () => tracing() && GraphicsOptions.On(GraphicsOptions.Occlusion);
            System.Func<bool> taa = () => GraphicsOptions.Get(GraphicsOptions.AntiAliasing) == GraphicsOptions.AaTaa;
            System.Func<bool> dlss = () => GraphicsOptions.Get(GraphicsOptions.AntiAliasing) == GraphicsOptions.AaDlss;
            return new List<SettingRow>
            {
                Drop("RESOLUTION", () => $"{Screen.width} X {Screen.height}",
                    () => { sizes = GraphicsOptions.Resolutions(); return sizes.ConvertAll(r => $"{r.x} X {r.y}"); },
                    () => sizes.FindIndex(r => r.x == Screen.width && r.y == Screen.height),
                    i => GraphicsOptions.SetResolution(sizes[i])),
                Drop("DISPLAY MODE", () => GraphicsOptions.ModeNames[GraphicsOptions.ModeIndex],
                    () => new List<string>(GraphicsOptions.ModeNames), () => GraphicsOptions.ModeIndex, GraphicsOptions.SetMode),
                Drop("QUALITY", () => GraphicsOptions.QualityName, GraphicsOptions.QualityNames,
                    () => GraphicsOptions.QualityNames().IndexOf(GraphicsOptions.QualityName), i => GraphicsOptions.SetQuality(GraphicsOptions.QualityNames()[i])),
                Toggle("SKYBOX", GraphicsOptions.Skybox),
                When(() => GraphicsOptions.On(GraphicsOptions.Skybox), new SettingRow
                {
                    label = "SKY BRIGHTNESS",
                    value = () => (GraphicsOptions.Get(GraphicsOptions.SkyBrightness) / 10f).ToString("0.0"),
                    step = sign => GraphicsOptions.Set(GraphicsOptions.SkyBrightness, Mathf.Clamp(GraphicsOptions.Get(GraphicsOptions.SkyBrightness) + sign * 5, 60, 120)),
                }),

                Choice("ANTI-ALIASING", GraphicsOptions.AntiAliasing, GraphicsOptions.AaNames),
                When(taa, Choice("TAA QUALITY", GraphicsOptions.TaaQuality, GraphicsOptions.TaaNames)),
                When(dlss, Choice("DLSS MODE", GraphicsOptions.DlssMode, GraphicsOptions.DlssNames)),
                When(dlss, Number("DLSS SHARPNESS", GraphicsOptions.DlssSharpness, 0, 100, 5, "%")),

                Toggle("RAY TRACING", GraphicsOptions.RayTracing),
                When(tracing, Toggle("RT SHADOWS", GraphicsOptions.Shadows)),
                When(tracing, Toggle("RT REFLECTIONS", GraphicsOptions.Reflections)),
                When(reflecting, Number("REFLECT DISTANCE", GraphicsOptions.ReflectionDistance, 25, 500, 25, " M")),
                When(reflecting, Number("REFLECT SAMPLES", GraphicsOptions.ReflectionSamples, 1, 8, 1)),
                When(reflecting, Number("REFLECT BOUNCES", GraphicsOptions.ReflectionBounces, 1, 4, 1)),
                When(tracing, Toggle("RT LIGHTING", GraphicsOptions.Lighting)),
                When(lighting, Number("LIGHT DISTANCE", GraphicsOptions.LightingDistance, 10, 200, 10, " M")),
                When(lighting, Number("LIGHT SAMPLES", GraphicsOptions.LightingSamples, 1, 8, 1)),
                When(lighting, Number("LIGHT BOUNCES", GraphicsOptions.LightingBounces, 1, 4, 1)),
                When(tracing, Toggle("RT OCCLUSION", GraphicsOptions.Occlusion)),
                When(occluding, Number("OCCLUDE DISTANCE", GraphicsOptions.OcclusionDistance, 5, 100, 5, " M")),
                When(occluding, Number("OCCLUDE SAMPLES", GraphicsOptions.OcclusionSamples, 1, 16, 1)),
            };
        }

        void BuildGraphics()
        {
            var green = new Color(0.25f, 0.78f, 0.27f);
            var dim = new Color(0.2f, 0.6f, 0.22f);
            var glow = new Color(0.25f, 0.78f, 0.27f, 0.22f);
            settingRows = Settings();
            graphics = settingsPage = SettingsScreen("Graphics");

            // Only a page of rows exists on screen; the list scrolls through them, however long it grows
            var labels = new List<PixelSegmentText>();
            var values = new List<PixelSegmentText>();
            var minus = new List<PixelSegmentText>();
            var plus = new List<PixelSegmentText>();
            var boxes = new List<Image[]>();
            for (int i = 0; i < SettingRows; i++)
            {
                int row = i;
                float y = 44 + i * 32;
                labels.Add(AddText(graphics, new Rect(22, y, 240, 22), 9, green, TextAnchor.MiddleLeft));
                boxes.Add(Frame(graphics, new Rect(266, y - 2, 192, 26), green, 1f));
                values.Add(AddText(graphics, new Rect(300, y, 124, 22), 9, green, TextAnchor.MiddleCenter));
                minus.Add(AddText(graphics, new Rect(266, y, 34, 22), 10, green, TextAnchor.MiddleCenter));
                plus.Add(AddText(graphics, new Rect(424, y, 34, 22), 10, green, TextAnchor.MiddleCenter));
                void Press(int sign)
                {
                    if (settingsTop + row >= shownRows.Count) return;
                    var setting = shownRows[settingsTop + row];
                    if (setting.step != null) setting.step(sign == 0 ? 1 : sign);
                    else setting.open();
                    graphics.refresh();
                }
                Button(graphics, new Rect(266, y - 2, 34, 26), () => Press(-1), Key.None, glow);
                Button(graphics, new Rect(300, y - 2, 124, 26), () => Press(0), Key.None, glow);
                Button(graphics, new Rect(424, y - 2, 34, 26), () => Press(1), Key.None, glow);
            }
            var more = AddText(graphics, new Rect(8, 270, SettingsW - 16, 14), 8, dim, TextAnchor.MiddleCenter);
            SettingsButton(graphics, new Rect(190, 292, 100, 28), "DONE", Pop);

            graphics.refresh = () =>
            {
                shownRows = settingRows.FindAll(r => r.shown == null || r.shown());
                settingsTop = Mathf.Clamp(settingsTop, 0, Mathf.Max(0, shownRows.Count - SettingRows));
                for (int i = 0; i < SettingRows; i++)
                {
                    bool used = settingsTop + i < shownRows.Count;
                    var setting = used ? shownRows[settingsTop + i] : null;
                    labels[i].Text = used ? Seg(setting.label) : "";
                    values[i].Text = used ? Seg(setting.value()) : "";
                    minus[i].Text = used && setting.step != null ? "-" : "";
                    plus[i].Text = used && setting.step != null ? "+" : "";
                    foreach (var edge in boxes[i]) edge.enabled = used;
                }
                more.Text = shownRows.Count > SettingRows ? Seg("WHEEL OR ARROWS FOR MORE") : "";
            };
            graphics.onEscape = graphics.onEnter = Pop;

            // The drop-down list: one for every setting above
            pick = NewScreen("Pick", new Vector2(240, 44 + PickRows * 22));
            AddRect(pick, new Rect(-2000, -2000, 4240, 4300), Color.black);
            Frame(pick, new Rect(0, 0, 240, 44 + PickRows * 22), green, 4f);
            var pickMore = AddText(pick, new Rect(8, 10, 224, 16), 8, dim, TextAnchor.MiddleCenter);
            var rows = new List<PixelSegmentText>();
            for (int i = 0; i < PickRows; i++)
            {
                int row = i;
                rows.Add(AddText(pick, new Rect(18, 32 + i * 22, 204, 20), 9, green, TextAnchor.MiddleLeft));
                Button(pick, new Rect(10, 31 + i * 22, 220, 22), () =>
                {
                    int chosen = pickTop + row;
                    if (chosen >= pickChoices.Count) return;
                    Pop();
                    pickChosen(chosen);
                    graphics.refresh();
                }, Key.None, glow);
            }
            pick.refresh = () =>
            {
                for (int i = 0; i < PickRows; i++)
                {
                    int at = pickTop + i;
                    rows[i].Text = at < pickChoices.Count ? Seg((at == pickCurrent ? "> " : "  ") + pickChoices[at]) : "";
                }
                pickMore.Text = pickChoices.Count > PickRows ? Seg("WHEEL OR ARROWS FOR MORE") : "";
            };
            pick.onEscape = pick.onEnter = Pop;
        }

        // The frame both settings pages share, with a tab for each at the top
        MenuScreen SettingsScreen(string name)
        {
            var s = NewScreen(name, new Vector2(SettingsW, SettingsH));
            AddRect(s, new Rect(-2000, -2000, 4000 + SettingsW, 4000 + SettingsH), Color.black);
            Frame(s, new Rect(0, 0, SettingsW, SettingsH), SegGreen, 4f);
            void Tab(string title, Rect r, System.Func<MenuScreen> page)
            {
                bool here = title == name.ToUpperInvariant();
                AddText(s, r, 12, here ? SegGreen : DimGreen, TextAnchor.MiddleCenter).Text = Seg(title);
                if (here) Frame(s, r, SegGreen, 1f);
                Button(s, r, () => ShowPage(page()), Key.None, new Color(SegGreen.r, SegGreen.g, SegGreen.b, 0.22f));
            }
            Tab("GRAPHICS", new Rect(100, 10, 130, 26), () => graphics);
            Tab("CONTROLS", new Rect(250, 10, 130, 26), () => controls);
            return s;
        }

        void SettingsButton(MenuScreen s, Rect r, string title, System.Action action)
        {
            Frame(s, r, SegGreen, 1f);
            AddText(s, new Rect(r.x, r.y + 3, r.width, 22), 9, SegGreen, TextAnchor.MiddleCenter).Text = Seg(title);
            Button(s, r, action, Key.None, new Color(SegGreen.r, SegGreen.g, SegGreen.b, 0.22f));
        }

        void ShowPage(MenuScreen page)
        {
            settingsPage = page;
            var top = stack[stack.Count - 1];
            if (top == page) return;
            top.root.gameObject.SetActive(false);
            stack[stack.Count - 1] = page;
            page.root.gameObject.SetActive(true);
            page.root.SetAsLastSibling();
            page.refresh?.Invoke();
            ignoreClickFrame = Time.frameCount;
        }

        // ---------- the Controls page: a row for every action, a box for each of its keys ----------

        const float KeyBoxX = 156, KeyBoxW = 72, KeyBoxStep = 76;
        int keysTop;
        GameKey capturing;
        int capturingSlot, capturingFrame;

        void BuildControls()
        {
            var glow = new Color(SegGreen.r, SegGreen.g, SegGreen.b, 0.22f);
            controls = SettingsScreen("Controls");
            var labels = new List<PixelSegmentText>();
            var boxes = new List<PixelSegmentText[]>();
            for (int i = 0; i < SettingRows; i++)
            {
                int row = i;
                float y = 44 + i * 32;
                labels.Add(AddText(controls, new Rect(22, y, 130, 22), 9, SegGreen, TextAnchor.MiddleLeft));
                var line = new PixelSegmentText[GameKeys.Slots];
                for (int j = 0; j < GameKeys.Slots; j++)
                {
                    int slot = j;
                    var r = new Rect(KeyBoxX + j * KeyBoxStep, y - 2, KeyBoxW, 26);
                    Frame(controls, r, SegGreen, 1f);
                    line[j] = AddText(controls, new Rect(r.x, y, r.width, 22), 9, SegGreen, TextAnchor.MiddleCenter);
                    var box = Button(controls, r, () =>
                    {
                        capturing = GameKeys.All[keysTop + row];
                        capturingSlot = slot;
                        capturingFrame = Time.frameCount;
                        controls.refresh();
                    }, Key.None, glow);
                    box.rightClick = () =>
                    {
                        GameKeys.Clear(GameKeys.All[keysTop + row], slot);
                        controls.refresh();
                    };
                }
                boxes.Add(line);
            }
            var more = AddText(controls, new Rect(8, 270, SettingsW - 16, 14), 8, DimGreen, TextAnchor.MiddleCenter);
            SettingsButton(controls, new Rect(190, 292, 100, 28), "DONE", Pop);
            SettingsButton(controls, new Rect(358, 292, 100, 28), "DEFAULTS", () => { GameKeys.Reset(); controls.refresh(); });

            controls.refresh = () =>
            {
                for (int i = 0; i < SettingRows; i++)
                {
                    var action = GameKeys.All[keysTop + i];
                    labels[i].Text = Seg(action.label);
                    for (int j = 0; j < GameKeys.Slots; j++)
                        boxes[i][j].Text = Seg(action == capturing && j == capturingSlot ? "PRESS" : GameKeys.Name(action.slots[j]));
                }
                more.Text = Seg(capturing != null ? "PRESS A KEY OR BUTTON  ESC CANCELS" : "CLICK CHANGES  RIGHT CLICK CLEARS  WHEEL FOR MORE");
            };
            controls.onEscape = controls.onEnter = Pop;
        }

        // Waiting for the key to set: nothing else in the menu hears it, Esc included
        void CaptureKey()
        {
            if (Time.frameCount == capturingFrame) return;
            var k = Keyboard.current;
            bool cancel = k != null && k.escapeKey.wasPressedThisFrame;
            int code = cancel ? 0 : GameKeys.JustPressed();
            if (!cancel && code == 0) return;
            if (code != 0) GameKeys.Bind(capturing, capturingSlot, code);
            capturing = null;
            controls.refresh();
        }

        void OpenPick(List<string> choices, int current, System.Action<int> chosen)
        {
            pickChoices = choices;
            pickCurrent = current;
            pickChosen = chosen;
            pickTop = Mathf.Clamp(current - PickRows / 2, 0, Mathf.Max(0, choices.Count - PickRows));
            Push(pick);
        }

        // The wheel or arrow keys, as a step through a list
        static int ListStep()
        {
            var k = Keyboard.current;
            var m = Mouse.current;
            if (k != null && k.upArrowKey.wasPressedThisFrame) return -1;
            if (k != null && k.downArrowKey.wasPressedThisFrame) return 1;
            float wheel = m != null ? m.scroll.ReadValue().y : 0f;
            return wheel > 0f ? -1 : wheel < 0f ? 1 : 0;
        }

        void ScrollLists(MenuScreen top)
        {
            int step = ListStep();
            if (step == 0) return;
            if (top == pick)
            {
                pickTop = Mathf.Clamp(pickTop + step, 0, Mathf.Max(0, pickChoices.Count - PickRows));
                pick.refresh();
            }
            else if (top == controls)
            {
                keysTop = Mathf.Clamp(keysTop + step, 0, GameKeys.All.Count - SettingRows);
                controls.refresh();
            }
            else
            {
                settingsTop = Mathf.Clamp(settingsTop + step, 0, Mathf.Max(0, shownRows.Count - SettingRows));
                graphics.refresh();
            }
        }

        void BuildScores()
        {
            scores = NewScreen("Scores", new Vector2(284, 220));
            AddRect(scores, new Rect(-2000, -2000, 4284, 4220), Color.black);   // on black, not over the menu
            var ink = new Color(0.25f, 0.78f, 0.27f);
            Frame(scores, new Rect(0, 0, 284, 220), ink, 4f);
            var title = AddText(scores, new Rect(8, 8, 268, 14), 9, ink, TextAnchor.MiddleCenter);
            AddText(scores, new Rect(46, 28, 100, 12), 8, ink, TextAnchor.MiddleLeft).Text = Seg("Name");
            AddText(scores, new Rect(140, 28, 74, 12), 8, ink, TextAnchor.MiddleRight).Text = Seg("Score");
            AddText(scores, new Rect(216, 28, 48, 12), 8, ink, TextAnchor.MiddleRight).Text = Seg("Level");
            for (int i = 0; i < HighScores.Count; i++)
            {
                float y = 47 + i * 15;
                scoreRows.Add(new[]
                {
                    AddText(scores, new Rect(10, y, 30, 12), 8, ink, TextAnchor.MiddleRight),
                    AddText(scores, new Rect(46, y, 100, 12), 8, ink, TextAnchor.MiddleLeft),
                    AddText(scores, new Rect(140, y, 74, 12), 8, ink, TextAnchor.MiddleRight),
                    AddText(scores, new Rect(226, y, 26, 12), 8, ink, TextAnchor.MiddleRight),
                });
            }
            var footer = AddText(scores, new Rect(8, 197, 268, 14), 8, ink, TextAnchor.MiddleCenter);
            scores.refresh = () =>
            {
                var list = HighScores.Load();
                int typing = -1;
                if (enteringName)
                {
                    typing = HighScores.Place(pendingScore, list);
                    list.Insert(typing, new HighScores.Entry { name = pendingName + (Time.unscaledTime % 1f < 0.5f ? "_" : ""), score = pendingScore, level = pendingLevel });
                }
                for (int i = 0; i < scoreRows.Count; i++)
                {
                    var e = i < list.Count ? list[i] : new HighScores.Entry { name = "", score = 0, level = 1 };
                    var row = scoreRows[i];
                    row[0].Text = Seg($"{i + 1}.");
                    row[1].Text = Seg(e.name);
                    row[2].Text = Seg(e.score.ToString());
                    row[3].Text = Seg(e.level.ToString());
                }
                title.Text = Seg(enteringName ? "Enter your name:" : "VR Virtuosos");
                footer.Text = enteringName ? "" : Seg("Press a key to Continue");
            };
            scores.onAnyClick = () => { if (!enteringName) CloseScores(); };
            scores.onEscape = () => { if (!enteringName) CloseScores(); };
            scores.onEnter = () =>
            {
                if (!enteringName) { CloseScores(); return; }
                HighScores.Add(pendingName, pendingScore, pendingLevel);
                enteringName = false;
                scoresCloseAt = Time.unscaledTime + 182f / PlayerMotor.TickRate;
                scores.refresh();
            };
        }

        float scoresCloseAt = -1f;

        void CloseScores()
        {
            scoresCloseAt = -1f;
            Pop();
        }

        void OnTextInput(char c)
        {
            if (!enteringName || stack.Count == 0 || stack[stack.Count - 1] != scores) return;
            if (c >= ' ' && c < 127 && pendingName.Length < 8) pendingName += char.ToUpperInvariant(c);
        }

        void BuildNotice()
        {
            notice = Panel("Notice", new Vector2(300, 110));
            noticeText = AddText(notice, new Rect(14, 14, 272, 52), 9, SegGreen, TextAnchor.MiddleCenter);
            var ok = new Rect(110, 72, 80, 26);
            Frame(notice, ok, DimGreen, 1);
            AddText(notice, ok, 10, SegGreen, TextAnchor.MiddleCenter).Text = "OK";
            Button(notice, ok, Pop, tint: new Color(SegGreen.r, SegGreen.g, SegGreen.b, 0.2f));
            notice.onAnyClick = null;
            notice.onEscape = notice.onEnter = Pop;
        }

        void ShowNotice(string text)
        {
            noticeText.Text = Seg(text);
            Push(notice);
        }

        // Our own dialogs (notices) use the high-score table's look: black with green segment text
        static readonly Color SegGreen = new Color(0.25f, 0.78f, 0.27f);
        static readonly Color DimGreen = new Color(0.2f, 0.6f, 0.22f);

        MenuScreen Panel(string name, Vector2 size)
        {
            var s = NewScreen(name, size);
            AddRect(s, new Rect(0, 0, size.x, size.y), Color.black);
            Frame(s, new Rect(3, 3, size.x - 6, size.y - 6), DimGreen, 1);
            return s;
        }

        // ---------- element helpers (all in image pixels, y down) ----------

        RawImage AddRaw(MenuScreen s, Texture tex, Rect r, Rect uv)
        {
            var go = new GameObject("Raw", typeof(RectTransform));
            go.transform.SetParent(s.root, false);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.uvRect = uv;
            img.raycastTarget = false;
            s.placed.Add(((RectTransform)go.transform, r));
            return img;
        }

        Image AddImage(MenuScreen s, Rect r, Color c, Sprite sprite = null)
        {
            var go = new GameObject("Img", typeof(RectTransform));
            go.transform.SetParent(s.root, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = c;
            img.raycastTarget = false;
            s.placed.Add(((RectTransform)go.transform, r));
            return img;
        }

        Image AddRect(MenuScreen s, Rect r, Color c) => AddImage(s, r, c);

        Image[] Frame(MenuScreen s, Rect r, Color c, float t)
        {
            return new[]
            {
                AddRect(s, new Rect(r.x, r.y, r.width, t), c),
                AddRect(s, new Rect(r.x, r.yMax - t, r.width, t), c),
                AddRect(s, new Rect(r.x, r.y, t, r.height), c),
                AddRect(s, new Rect(r.xMax - t, r.y, t, r.height), c),
            };
        }

        // Mac checkbox "X" across the 11x11 interior whose top-left is (x, y)
        GameObject CheckMark(MenuScreen s, int x, int y)
        {
            var holder = new GameObject("Check", typeof(RectTransform));
            holder.transform.SetParent(s.root, false);
            s.placed.Add(((RectTransform)holder.transform, new Rect(x, y, 11, 11)));
            for (int i = 0; i < 2; i++)
            {
                var line = new GameObject("Stroke", typeof(RectTransform)).AddComponent<Image>();
                line.transform.SetParent(holder.transform, false);
                line.color = Color.black;
                line.raycastTarget = false;
                var rt = line.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
                strokes.Add((rt, holder.transform as RectTransform));
            }
            return holder;
        }

        readonly List<(RectTransform stroke, RectTransform box)> strokes = new List<(RectTransform, RectTransform)>();

        // Pixel-grid DSEG14 text. 'size' is a rough point size: 15 and up draws the glyphs at twice
        // the artwork's pixel size (titles), anything smaller at the artwork's own pixel size.
        PixelSegmentText AddText(MenuScreen s, Rect r, int size, Color c, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(s.root, false);
            var t = go.AddComponent<PixelSegmentText>();
            t.color = c;
            t.alignment = anchor;
            t.raycastTarget = false;
            s.placed.Add(((RectTransform)go.transform, r));
            s.texts.Add((t, size >= 15 ? 2 : 1));
            return t;
        }

        // Uppercase, and DSEG's full-width blank for spaces so columns line up
        static string Seg(string s) => s.ToUpperInvariant().Replace(' ', '!');

        static Texture2D LoadTex(string name)
        {
            var t = Resources.Load<Texture2D>("Menu/" + name);
            if (t != null) t.filterMode = FilterMode.Point;
            return t;
        }

        static Sprite MakeDotSprite()
        {
            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(n / 2f - d)));
            }
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        // ---------- layout ----------

        void Relayout()
        {
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            scale = Mathf.Max(1, Mathf.Min(Screen.width / MainW, Screen.height / MainH));
            foreach (var s in AllScreens())
            {
                // Pin each screen's top-left to a whole screen pixel so every art and text pixel lands on the grid
                s.scale = ScaleOf(s);
                Vector2 size = s.size * s.scale;
                s.root.anchorMin = s.root.anchorMax = Vector2.zero;
                s.root.pivot = Vector2.zero;
                s.root.anchoredPosition = ScreenOrigin(s);
                s.root.sizeDelta = size;
                foreach (var (rt, r) in s.placed)
                {
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(r.x, -r.y) * s.scale;
                    rt.sizeDelta = r.size * s.scale;
                }
                foreach (var (t, mult) in s.texts) t.PixelScale = mult * s.scale;
            }
            // Corner to corner of the 11x11 interior: 11 * sqrt(2)
            foreach (var (stroke, box) in strokes)
                stroke.sizeDelta = new Vector2(15.5f * scale, Mathf.Max(1, scale));
        }

        // Screens share the main menu's scale, but the high scores fill the screen
        int ScaleOf(MenuScreen s) =>
            s == scores ? Mathf.Max(1, Mathf.Min(Screen.width / (int)s.size.x, Screen.height / (int)s.size.y)) : scale;

        // Bottom-left of a screen, centred and rounded down to whole pixels
        Vector2 ScreenOrigin(MenuScreen s)
        {
            Vector2 size = s.size * s.scale;
            return new Vector2(Mathf.Floor((Screen.width - size.x) / 2f), Mathf.Floor((Screen.height - size.y) / 2f));
        }

        // ---------- navigation and input ----------

        void Push(MenuScreen s)
        {
            if (stack.Count > 0 && stack[stack.Count - 1] != main) stack[stack.Count - 1].root.gameObject.SetActive(false);
            stack.Add(s);
            s.root.gameObject.SetActive(true);
            s.root.SetAsLastSibling();
            s.refresh?.Invoke();
            ignoreClickFrame = Time.frameCount;
        }

        void Pop()
        {
            if (stack.Count <= 1) return;
            var top = stack[stack.Count - 1];
            top.root.gameObject.SetActive(false);
            stack.RemoveAt(stack.Count - 1);
            var now = stack[stack.Count - 1];
            now.root.gameObject.SetActive(true);
            now.refresh?.Invoke();
            if (now == main && !GameSuspended && MusicPlayer.Instance != null) MusicPlayer.Instance.PlayMenuTheme();
            ignoreClickFrame = Time.frameCount;
        }

        int ignoreClickFrame;

        void Update()
        {
            if (!IsOpen || stack.Count == 0) return;
            if (Screen.width != lastScreen.x || Screen.height != lastScreen.y) Relayout();
            optionsHue.enabled = ShiftHeld;

            var top = stack[stack.Count - 1];
            if (capturing != null) { CaptureKey(); return; }
            if (top == pick || top == graphics || top == controls) ScrollLists(top);
            if (top == scores && enteringName) scores.refresh();   // blink the cursor
            if (top == scores && scoresCloseAt > 0f && Time.unscaledTime >= scoresCloseAt) { CloseScores(); return; }

            // Mouse position in the top screen's image pixels (y down)
            Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(-1, -1);
            Vector2 origin = ScreenOrigin(top);
            Vector2 local = (mouse - origin) / top.scale;
            Vector2 img = new Vector2(local.x, top.size.y - local.y);

            Hotspot hovered = null;
            foreach (var h in top.hotspots)
            {
                bool over = h.rect.width > 0 && h.rect.Contains(img) && h.action != null;
                if (over) hovered = h;
                if (h.hover != null) h.hover.enabled = over;
                if (h.tint != null) h.tint.enabled = over;
            }

            if (hovered?.rightClick != null && Mouse.current.rightButton.wasPressedThisFrame) { hovered.rightClick(); return; }

            bool click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Time.frameCount != ignoreClickFrame;
            if (click)
            {
                if (hovered != null) { Sfx.Play(Snd.TimeTick); hovered.action(); return; }
                if (top.onAnyClick != null) { top.onAnyClick(); return; }
            }

            var k = Keyboard.current;
            if (k == null) return;
            if (enteringName && top == scores)
            {
                if (k.backspaceKey.wasPressedThisFrame && pendingName.Length > 0) pendingName = pendingName.Substring(0, pendingName.Length - 1);
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) top.onEnter?.Invoke();
                if (k.escapeKey.wasPressedThisFrame) top.onEscape?.Invoke();
                return;
            }
            // (not on the frame a screen opened: the in-game Esc that brought the menu up mustn't also close it)
            if (k.escapeKey.wasPressedThisFrame && Time.frameCount != ignoreClickFrame) { top.onEscape?.Invoke(); return; }
            if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) { top.onEnter?.Invoke(); return; }
            foreach (var h in top.hotspots)
                if (h.key != Key.None && h.action != null && k[h.key].wasPressedThisFrame) { Sfx.Play(Snd.TimeTick); h.action(); return; }
            if (top.onAnyClick != null && k.anyKey.wasPressedThisFrame) top.onAnyClick();
        }
    }
}
