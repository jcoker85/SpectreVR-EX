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
    // Keys: the first letter of each button. As in the original, Alt+P asks for a starting level and
    // Alt+R loads a saved game; Alt+S on a level's banner saves one.
    public class MainMenu : MonoBehaviour
    {
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
            public System.Func<bool> live;  // optional: when false the spot is ignored (a hidden section)
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
        FullScreenMode lastMode;
        readonly List<MenuScreen> stack = new List<MenuScreen>();
        const float SplashSeconds = 2.5f, SplashFadeSeconds = 0.5f;
        MenuScreen splash;
        Image splashFade;
        MenuScreen main, vehicles, custom, options, matrix, about, help, scores, notice, levelPrompt, slots;

        // What Play will start: level 1 afresh, unless Alt+P or Alt+R chose otherwise
        int startAt = 1, startScore, startLives = -1;

        // The number or name being typed into levelPrompt or slots; the first key replaces what's there
        string typed = "";
        bool typedFresh;

        // The slots dialog: loading, or saving (the slot first, then a name)
        bool slotsSaving, slotsNaming;
        int slotPicked, saveLevel, saveScore, saveLives;
        PixelSegmentText levelField, slotsTitle;
        readonly List<PixelSegmentText[]> slotRows = new List<PixelSegmentText[]>();
        PixelSegmentText noticeText;
        GameManager gm;
        List<string> matrixChoices;

        // Name entry for the high-score table
        bool enteringName;
        string pendingName = "";
        int pendingScore, pendingLevel;
        readonly List<PixelSegmentText[]> scoreRows = new List<PixelSegmentText[]>();

        public bool IsOpen => canvas != null && canvas.enabled;
        bool startingUp = true;   // the splash and the startup movie, before the menu first opens

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
                gm.SaveRequested += OpenSave;
            }
            if (Keyboard.current != null) Keyboard.current.onTextInput += OnTextInput;
            GraphicsOptions.Load();
            GameKeys.Load();
            matrixChoices = Matrix.MatrixLibrary.ChooseAtStartup();
            main.refresh();
            StartCoroutine(FirstOpen());
        }

        // The startup movie, then the menu with its theme, so neither talks over the other
        IEnumerator FirstOpen()
        {
            yield return ShowSplash();
            canvas.enabled = false;
            Sfx.Play(Snd.Logo);
            yield return Matrix.StartupMovie.Play(this);
            Open();
            startingUp = false;
            if (matrixChoices != null) Push(matrix);
        }

        // _SHOW_VELOCITY: the logo while the game loads, fading out before the logo sound
        IEnumerator ShowSplash()
        {
            canvas.enabled = true;
            foreach (var s in AllScreens()) s.root.gameObject.SetActive(false);
            Push(splash);
            yield return new WaitForSecondsRealtime(SplashSeconds);
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / SplashFadeSeconds)
            {
                splashFade.color = new Color(0f, 0f, 0f, t);
                yield return null;
            }
            stack.Clear();
            splash.root.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInput;
        }

        // ---------- open / close ----------

        public void Open()
        {
            idleSince = Time.unscaledTime;
            canvas.enabled = true;
            stack.Clear();
            foreach (var s in AllScreens()) s.root.gameObject.SetActive(false);
            Push(main);
            if (MusicPlayer.Instance != null) MusicPlayer.Instance.PlayMenuTheme();
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
            GameSettings.Save();
            Close();
            var vehicle = demo ? new VehicleSpec("DEMO", 9, 6, 4) : GameSettings.Vehicle(GameSettings.SelectedVehicle);   // _MAIN_MENU's own for the demo
            bool started = demo ? gm.StartGame(vehicle, 1, true) : gm.StartGame(vehicle, startAt, false, startScore, startLives);
            if (!started)
            {
                Open();
                ShowNotice("UNABLE TO LOAD MATRIX");
            }
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

        IEnumerable<MenuScreen> AllScreens() => new[] { splash, main, vehicles, custom, options, graphics, controls, pick, matrix, about, help, scores, notice, levelPrompt, slots };

        void BuildScreens()
        {
            splash = BlackScreen("Splash", "Splash");
            splashFade = AddRect(splash, new Rect(-2000, -2000, 4368, 4121), new Color(0f, 0f, 0f, 0f));
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
            BuildLevelPrompt();
            BuildSlots();
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
            var t = LoadTex(tex) ?? Texture2D.blackTexture;
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
            Tile(main, 377, 25, 1, 0, PlayFromStart, Key.P);
            Tile(main, 449, 25, 3, 0, () => ShowNotice("CYBERNET NETWORK PLAY\nIS NOT AVAILABLE\nIN THIS VERSION"), Key.C);
            Tile(main, 377, 93, 1, 1, () => Push(ShiftHeld ? settingsPage : options), Key.O);
            optionsHue = AddRect(main, new Rect(377, 93, 64, 64), new Color(0.55f, 0.1f, 1f, 0.5f));
            optionsHue.enabled = false;
            Tile(main, 449, 93, 3, 1, () => { enteringName = false; Push(scores); }, Key.S);
            Tile(main, 377, 161, 1, 2, () => Push(help), Key.H);
            Tile(main, 449, 161, 3, 2, () => Push(about), Key.A);
            Tile(main, 377, 229, 1, 3, () => StartGame(true), Key.D);
            Tile(main, 449, 229, 3, 3, Quit, Key.Q);
            main.onEnter = PlayFromStart;
            main.onEscape = Quit;   // _MAIN_MENU: Esc leaves, as Q does
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

            // _EDIT_CUSTOM_TANK: up wraps past the top, and going over the 15 points also falls back to the
            // lowest; down stops at the lowest
            static int Step(int value, int delta, int top, bool over)
            {
                if (delta < 0) return Mathf.Max(1, value + delta);
                return value + delta > top || over ? 1 : value + delta;
            }

            void Adjust(int field, int delta)
            {
                var c = GameSettings.Custom;
                bool over = c.speed + c.shields + c.ammo + delta > VehicleSpec.TotalPoints;
                switch (field)
                {
                    case 0: c.speed = Step(c.speed, delta, 9, over); break;
                    case 1: c.shields = Step(c.shields, delta, 10, over); break;
                    case 2: c.ammo = Step(c.ammo, delta, 5, over); break;
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
            System.Action done = () => { GameSettings.Save(); Pop(); };
            Button(custom, new Rect(113, 249, 113, 33), done, Key.D);
            custom.onEscape = custom.onEnter = done;

            // The underlined letters step their setting up
            Key[] fieldKeys = { Key.S, Key.H, Key.A };
            for (int i = 0; i < fieldKeys.Length; i++)
            {
                int field = i;
                Button(custom, new Rect(0, 0, 0, 0), () => Adjust(field, 1), fieldKeys[i]);
            }
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

            // (x, y) is the top-left of each control's 11x11 interior, measured from Options.png.
            // Each key is the letter underlined beside it in the art.
            void Check(int x, int y, System.Func<bool> get, System.Action toggle, Key key = Key.None, bool enabled = true)
            {
                var mark = CheckMark(options, x, y);
                Button(options, new Rect(x - 3, y - 3, 128, 17), enabled ? () => { toggle(); options.refresh(); } : (System.Action)null, key);
                refreshers.Add(() => mark.SetActive(get()));
            }
            void Radio(int x, int y, System.Func<bool> get, System.Action select, Key key)
            {
                var dot = AddImage(options, new Rect(x + 3, y + 3, 5, 5), Color.black, dotSprite);
                Button(options, new Rect(x - 3, y - 3, 128, 17), () => { select(); options.refresh(); }, key);
                refreshers.Add(() => dot.enabled = get());
            }

            Radio(19, 38, () => GameSettings.Joystick, () =>
            {
                // Ticking it with nothing to use is refused
                if (!GameSettings.Joystick && Gamepad.current == null) Sfx.Play(Snd.AccessDenied);
                else GameSettings.Joystick = !GameSettings.Joystick;
            }, Key.J);
            // Music is off when all four styles below are
            void Sound(int y, bool on, Key key) => Radio(19, y, () => GameSettings.SoundEffectsOn == on, () =>
            {
                GameSettings.SoundEffectsOn = on;
                Sfx.Enabled = on && !GameSettings.SoundMuted;
                if (Sfx.Enabled) Sfx.Play(Snd.Welcome);
            }, key);
            Sound(98, true, Key.O);
            Sound(122, false, Key.F);
            int[] musicY = { 174, 202, 230, 258 };
            Key[] musicKeys = { Key.Y, Key.A, Key.K, Key.B };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                Check(21, musicY[i], () => GameSettings.Music[idx], () => GameSettings.Music[idx] = !GameSettings.Music[idx], musicKeys[idx]);
            }
            Radio(167, 38, () => GameSettings.Difficulty == Difficulty.Easy, () => GameSettings.Difficulty = Difficulty.Easy, Key.E);
            Radio(167, 62, () => GameSettings.Difficulty == Difficulty.Normal, () => GameSettings.Difficulty = Difficulty.Normal, Key.N);
            Radio(167, 86, () => GameSettings.Difficulty == Difficulty.Hard, () => GameSettings.Difficulty = Difficulty.Hard, Key.H);
            Check(167, 110, () => GameSettings.UseCustomMatrices, () => GameSettings.UseCustomMatrices = !GameSettings.UseCustomMatrices, Key.U);
            Check(169, 174, () => GameSettings.DisplayComplex, () => GameSettings.DisplayComplex = !GameSettings.DisplayComplex, Key.C);
            Check(169, 202, () => GameSettings.DisplayFilled, () => GameSettings.DisplayFilled = !GameSettings.DisplayFilled, Key.L);
            Check(169, 230, () => GameSettings.DisplayTextures, () => GameSettings.DisplayTextures = !GameSettings.DisplayTextures, Key.T);
            Check(169, 258, () => GameSettings.DisplayMovies, () => GameSettings.DisplayMovies = !GameSettings.DisplayMovies, Key.M);

            Button(options, new Rect(11, 287, 170, 41), () => ShowNotice("NETWORK PLAY\nIS NOT AVAILABLE\nIN THIS VERSION"), Key.R);
            System.Action done = () =>
            {
                GameSettings.Save();
                if (gm != null) gm.ApplySettings();
                // Restart the theme, as music settings may have changed
                if (MusicPlayer.Instance != null) MusicPlayer.Instance.PlayMenuTheme();
                Pop();
            };
            Button(options, new Rect(199, 287, 170, 41), done, Key.D);
            options.onEscape = options.onEnter = done;
            options.refresh = () => { foreach (var r in refreshers) r(); };
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
            System.Func<bool> taa = () => GraphicsOptions.Aa == GraphicsOptions.AaTaa;
            System.Func<bool> dlss = () => GraphicsOptions.Aa == GraphicsOptions.AaDlss;
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

                Drop("ANTI-ALIASING", () => GraphicsOptions.AaNames[GraphicsOptions.Aa], GraphicsOptions.AaChoices,
                    () => GraphicsOptions.Aa, i => GraphicsOptions.Set(GraphicsOptions.AntiAliasing, i)),
                When(taa, Choice("TAA QUALITY", GraphicsOptions.TaaQuality, GraphicsOptions.TaaNames)),
                When(dlss, Number("DLSS SCALE", GraphicsOptions.DlssScale, 25, 100, 5, "%")),
                When(dlss, Number("DLSS SHARPNESS", GraphicsOptions.DlssSharpness, 0, 100, 5, "%")),
                Number("MOTION BLUR", GraphicsOptions.MotionBlurAmount, 0, 100, 5, "%"),

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
                    controls.refresh();
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

        const float KeyBoxX = 156, KeyBoxW = 98, KeyBoxStep = 101;
        int keysTop;
        bool padSection, mouseSection;
        GameKey capturing;
        int capturingSlot, capturingFrame;

        void BuildControls()
        {
            var glow = new Color(SegGreen.r, SegGreen.g, SegGreen.b, 0.22f);
            controls = SettingsScreen("Controls");
            var labels = new List<PixelSegmentText>();
            var boxes = new List<PixelSegmentText[]>();
            var keyFrames = new List<Image>();
            System.Func<bool> keysLive = () => !mouseSection;
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
                    keyFrames.AddRange(Frame(controls, r, SegGreen, 1f));
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
                        GameKeys.Clear(GameKeys.All[keysTop + row], slot, padSection);
                        controls.refresh();
                    };
                    box.live = keysLive;
                }
                boxes.Add(line);
            }

            // The MOUSE section: settings rows in the same places, a value box with - and + on each
            var mouseRows = MouseRows();
            var mouseFrames = new List<Image[]>();
            var mouseValues = new List<PixelSegmentText>();
            var mouseMinus = new List<PixelSegmentText>();
            var mousePlus = new List<PixelSegmentText>();
            System.Func<bool> mouseLive = () => mouseSection;
            for (int i = 0; i < mouseRows.Count; i++)
            {
                int row = i;
                float y = 44 + i * 32;
                mouseFrames.Add(Frame(controls, new Rect(266, y - 2, 192, 26), SegGreen, 1f));
                mouseValues.Add(AddText(controls, new Rect(300, y, 124, 22), 9, SegGreen, TextAnchor.MiddleCenter));
                mouseMinus.Add(AddText(controls, new Rect(266, y, 34, 22), 10, SegGreen, TextAnchor.MiddleCenter));
                mousePlus.Add(AddText(controls, new Rect(424, y, 34, 22), 10, SegGreen, TextAnchor.MiddleCenter));
                void Press(int sign)
                {
                    var setting = mouseRows[row];
                    if (setting.step != null) setting.step(sign == 0 ? 1 : sign);
                    else setting.open();
                    controls.refresh();
                }
                Button(controls, new Rect(266, y - 2, 34, 26), () => Press(-1), Key.None, glow).live = mouseLive;
                Button(controls, new Rect(300, y - 2, 124, 26), () => Press(0), Key.None, glow).live = mouseLive;
                Button(controls, new Rect(424, y - 2, 34, 26), () => Press(1), Key.None, glow).live = mouseLive;
            }

            var more = AddText(controls, new Rect(8, 270, SettingsW - 16, 14), 8, DimGreen, TextAnchor.MiddleCenter);
            SettingsButton(controls, new Rect(276, 292, 80, 28), "DONE", Pop);
            SettingsButton(controls, new Rect(366, 292, 92, 28), "DEFAULTS", () =>
            {
                if (mouseSection) ResetMouse();
                else GameKeys.Reset(padSection);
                controls.refresh();
            });

            // The sets of bindings share the rows; these pick which one they show
            var sections = new List<(PixelSegmentText title, Image[] edges, int which)>();
            int Shown() => mouseSection ? 2 : padSection ? 1 : 0;
            void Section(string title, float x, int which)
            {
                var r = new Rect(x, 292, 80, 28);
                var t = AddText(controls, new Rect(r.x, r.y + 3, r.width, 22), 9, SegGreen, TextAnchor.MiddleCenter);
                t.Text = Seg(title);
                sections.Add((t, Frame(controls, r, SegGreen, 1f), which));
                Button(controls, r, () =>
                {
                    padSection = which == 1;
                    mouseSection = which == 2;
                    capturing = null;
                    controls.refresh();
                }, Key.None, glow);
            }
            Section("KEYBOARD", 22, 0);
            Section("GAMEPAD", 104, 1);
            Section("MOUSE", 186, 2);

            controls.refresh = () =>
            {
                for (int i = 0; i < SettingRows; i++)
                {
                    if (mouseSection)
                    {
                        labels[i].Text = i < mouseRows.Count ? Seg(mouseRows[i].label) : "";
                        for (int j = 0; j < GameKeys.Slots; j++) boxes[i][j].Text = "";
                        continue;
                    }
                    var action = GameKeys.All[keysTop + i];
                    labels[i].Text = Seg(action.label);
                    for (int j = 0; j < GameKeys.Slots; j++)
                        boxes[i][j].Text = Seg(action == capturing && j == capturingSlot ? "PRESS"
                            : padSection ? GameKeys.PadName(action.pads[j]) : GameKeys.Name(action.slots[j]));
                }
                foreach (var edge in keyFrames) edge.enabled = !mouseSection;
                for (int i = 0; i < mouseRows.Count; i++)
                {
                    var setting = mouseRows[i];
                    foreach (var edge in mouseFrames[i]) edge.enabled = mouseSection;
                    mouseValues[i].Text = mouseSection ? Seg(setting.value()) : "";
                    mouseMinus[i].Text = mouseSection && setting.step != null ? "-" : "";
                    mousePlus[i].Text = mouseSection && setting.step != null ? "+" : "";
                }
                foreach (var (title, edges, which) in sections)
                {
                    title.color = which == Shown() ? SegGreen : DimGreen;
                    foreach (var edge in edges) edge.enabled = which == Shown();
                }
                more.Text = Seg(mouseSection ? MouseModeHelp[(int)MouseLook.Mode]
                    : capturing != null ? (padSection ? "PRESS A GAMEPAD INPUT  ESC CANCELS" : "PRESS A KEY OR BUTTON  ESC CANCELS")
                    : padSection && !GameSettings.Joystick ? "TICK JOYSTICK IN OPTIONS TO USE THE GAMEPAD"
                    : "CLICK CHANGES  RIGHT CLICK CLEARS  WHEEL FOR MORE");
            };
            controls.onEscape = controls.onEnter = Pop;
        }

        // One line under the MOUSE rows on what the chosen mode does
        static readonly string[] MouseModeHelp =
        {
            "THE MOUSE ONLY FIRES AND JUMPS",
            "TURNS THE CRAFT  NO FASTER THAN THE KEYS",
            "TURNS THE CRAFT  AS FAST AS THE MOUSE",
            "TURNS THE CRAFT AND AIMS UP AND DOWN",
            "LOOKS AROUND  THE KEYS DRIVE",
        };

        List<SettingRow> MouseRows() => new List<SettingRow>
        {
            Drop("MOUSE CONTROLS", () => MouseLook.ModeNames[(int)MouseLook.Mode], () => new List<string>(MouseLook.ModeNames),
                () => (int)MouseLook.Mode, i => MouseLook.Mode = (MouseMode)i),
            new SettingRow { label = "MOUSE SPEED", value = () => MouseLook.Speed.ToString(), step = sign => MouseLook.Speed += sign },
            new SettingRow { label = "STICK SPEED", value = () => MouseLook.StickSpeed.ToString(), step = sign => MouseLook.StickSpeed += sign },
            Drop("INVERT UP DOWN", () => MouseLook.Invert ? "On" : "Off", () => new List<string> { "On", "Off" },
                () => MouseLook.Invert ? 0 : 1, i => MouseLook.Invert = i == 0),
        };

        static void ResetMouse()
        {
            foreach (var key in new[] { MouseLook.ModeKey, MouseLook.SpeedKey, MouseLook.StickKey, MouseLook.InvertKey }) PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        // Waiting for the key to set: nothing else in the menu hears it, Esc included
        void CaptureKey()
        {
            if (Time.frameCount == capturingFrame) return;
            var k = Keyboard.current;
            bool cancel = k != null && k.escapeKey.wasPressedThisFrame;
            int code = cancel ? 0 : padSection ? GameKeys.JustPressedPad() : GameKeys.JustPressed();
            if (!cancel && code == 0) return;
            if (code != 0) GameKeys.Bind(capturing, capturingSlot, code, padSection);
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
                if (mouseSection) return;
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
            if (stack.Count == 0) return;
            var top = stack[stack.Count - 1];
            if (top == levelPrompt || top == slots)
            {
                bool wordy = top == slots && slotsNaming;
                if (wordy ? (c < ' ' || c >= 127 || c == '|') : !char.IsDigit(c)) return;
                if (typedFresh) typed = "";
                typedFresh = false;
                if (typed.Length < (wordy ? 12 : 2)) typed += char.ToUpperInvariant(c);
                return;
            }
            if (!enteringName || top != scores) return;
            if (c >= ' ' && c < 127 && pendingName.Length < 8) pendingName += char.ToUpperInvariant(c);
        }

        void PlayFromStart()
        {
            startAt = 1;
            startScore = 0;
            startLives = -1;
            Push(vehicles);
        }

        void Type(string first)
        {
            typed = first;
            typedFresh = true;
        }

        string Cursor => Time.unscaledTime % 1f < 0.5f ? "_" : "";

        // _GET_START_LEVEL: 1 to 50, then the vehicle as usual
        void BuildLevelPrompt()
        {
            levelPrompt = NewScreen("StartLevel", new Vector2(284, 220));
            AddRect(levelPrompt, new Rect(-2000, -2000, 4284, 4220), Color.black);
            Frame(levelPrompt, new Rect(0, 0, 284, 220), SegGreen, 4f);
            AddText(levelPrompt, new Rect(30, 104, 190, 14), 8, SegGreen, TextAnchor.MiddleLeft).Text = Seg("Enter Starting Level:");
            levelField = AddText(levelPrompt, new Rect(224, 104, 40, 14), 8, SegGreen, TextAnchor.MiddleLeft);
            levelPrompt.refresh = () => levelField.Text = Seg(typed + Cursor);
            levelPrompt.onEscape = Pop;
            levelPrompt.onEnter = () =>
            {
                if (!int.TryParse(typed, out int level) || level < 1 || level > 50) { Type("1"); return; }
                startAt = level;
                startScore = 0;
                startLives = -1;
                Pop();
                Push(vehicles);
            };
        }

        void OpenLevelPrompt()
        {
            Type("1");
            Push(levelPrompt);
        }

        // _SHOW_SAVED_GAMES, for _SAVE_GAME and _RESTORE_GAME: the slots, and a number to type
        void BuildSlots()
        {
            slots = NewScreen("Slots", new Vector2(284, 220));
            AddRect(slots, new Rect(-2000, -2000, 4284, 4220), Color.black);
            Frame(slots, new Rect(0, 0, 284, 220), SegGreen, 4f);
            slotsTitle = AddText(slots, new Rect(8, 16, 268, 14), 8, SegGreen, TextAnchor.MiddleCenter);
            for (int i = 0; i < SavedGames.Slots; i++)
            {
                float y = 40 + i * 16;
                slotRows.Add(new[]
                {
                    AddText(slots, new Rect(8, y, 28, 12), 8, SegGreen, TextAnchor.MiddleRight),
                    AddText(slots, new Rect(42, y, 130, 12), 8, SegGreen, TextAnchor.MiddleLeft),
                    AddText(slots, new Rect(172, y, 24, 12), 8, SegGreen, TextAnchor.MiddleRight),
                    AddText(slots, new Rect(198, y, 24, 12), 8, SegGreen, TextAnchor.MiddleRight),
                    AddText(slots, new Rect(224, y, 52, 12), 8, SegGreen, TextAnchor.MiddleRight),
                });
            }
            slots.refresh = () =>
            {
                slotsTitle.Text = Seg((slotsNaming ? "Name: " : slotsSaving ? "Save game in slot (1-10): " : "Load game from slot (1-10): ") + typed + Cursor);
                for (int i = 0; i < SavedGames.Slots; i++)
                {
                    var g = SavedGames.Load(i);
                    var row = slotRows[i];
                    row[0].Text = Seg($"{i + 1}.");
                    row[1].Text = Seg(g.name);
                    row[2].Text = g.used ? Seg((g.level + 1).ToString()) : "";
                    row[3].Text = g.used ? Seg(g.lives.ToString()) : "";
                    row[4].Text = g.used ? Seg(g.score.ToString()) : "";
                }
            };
            slots.onEscape = () =>
            {
                if (slotsSaving) FinishSave();
                else Pop();
            };
            slots.onEnter = () =>
            {
                if (slotsNaming)
                {
                    SavedGames.Save(slotPicked, typed, saveLevel, saveScore, saveLives);
                    FinishSave();
                    return;
                }
                if (!int.TryParse(typed, out int slot) || slot < 1 || slot > SavedGames.Slots) { Type("1"); return; }
                var game = SavedGames.Load(slot - 1);
                if (slotsSaving)
                {
                    slotPicked = slot - 1;
                    slotsNaming = true;
                    Type(game.name);
                    return;
                }
                if (!game.used) return;   // a slot never saved to would load no lives at all
                startAt = game.level + 1;
                startScore = game.score;
                startLives = game.lives;
                Pop();
                Push(vehicles);
            };
        }

        void OpenLoad()
        {
            slotsSaving = slotsNaming = false;
            Type("1");
            Push(slots);
        }

        // Alt+S on a level's banner: the dialog over the game, which waits for it
        void OpenSave(int level, int score, int lives)
        {
            saveLevel = level;
            saveScore = score;
            saveLives = lives;
            slotsSaving = true;
            slotsNaming = false;
            Type("1");
            canvas.enabled = true;
            stack.Clear();
            foreach (var s in AllScreens()) s.root.gameObject.SetActive(false);
            Push(slots);
        }

        void FinishSave()
        {
            Close();
            gm.SaveFinished();
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
            if (now == main && MusicPlayer.Instance != null) MusicPlayer.Instance.PlayMenuTheme();
            ignoreClickFrame = Time.frameCount;
        }

        int ignoreClickFrame;
        float idleSince;

        // _MOUSE_KEY: left alone, the main menu runs the demo, sooner once it has run
        bool IdleDemo(MenuScreen top)
        {
            bool pressed = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
                           (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
            if (top != main || pressed) { idleSince = Time.unscaledTime; return false; }
            int ticks = GameManager.DemoHasRun ? 0x10E : 0x21C;
            if (Time.unscaledTime - idleSince < ticks / PlayerMotor.TickRate) return false;
            idleSince = Time.unscaledTime;
            StartGame(true);
            return true;
        }

        // Everything this draws covers the screen, so the arena isn't rendered behind it
        void LateUpdate() => GraphicsOptions.MenuShown(IsOpen || startingUp);

        void Update()
        {
            if (!IsOpen || stack.Count == 0) return;
            // Screen changes land a frame or more after they're asked for, so the settings list shows them then
            if (Screen.width != lastScreen.x || Screen.height != lastScreen.y) { Relayout(); graphics.refresh(); }
            if (Screen.fullScreenMode != lastMode) { lastMode = Screen.fullScreenMode; graphics.refresh(); }
            optionsHue.enabled = ShiftHeld;

            var top = stack[stack.Count - 1];
            if (capturing != null) { CaptureKey(); return; }
            if (IdleDemo(top)) return;
            if (top == pick || top == graphics || top == controls) ScrollLists(top);
            if (top == scores && enteringName) scores.refresh();   // blink the cursor
            if (top == levelPrompt || top == slots) top.refresh();
            if (top == scores && scoresCloseAt > 0f && Time.unscaledTime >= scoresCloseAt) { CloseScores(); return; }

            // Mouse position in the top screen's image pixels (y down)
            Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(-1, -1);
            Vector2 origin = ScreenOrigin(top);
            Vector2 local = (mouse - origin) / top.scale;
            Vector2 img = new Vector2(local.x, top.size.y - local.y);

            Hotspot hovered = null;
            foreach (var h in top.hotspots)
            {
                bool over = h.rect.width > 0 && h.rect.Contains(img) && h.action != null && (h.live == null || h.live());
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
            if (top == levelPrompt || top == slots)
            {
                if (k.backspaceKey.wasPressedThisFrame)
                {
                    if (typedFresh) typed = "";
                    else if (typed.Length > 0) typed = typed.Substring(0, typed.Length - 1);
                    typedFresh = false;
                }
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) top.onEnter?.Invoke();
                else if (k.escapeKey.wasPressedThisFrame) top.onEscape?.Invoke();
                return;
            }
            if (top == main && (k.leftAltKey.isPressed || k.rightAltKey.isPressed))
            {
                if (k.pKey.wasPressedThisFrame) { Sfx.Play(Snd.TimeTick); OpenLevelPrompt(); return; }
                if (k.rKey.wasPressedThisFrame) { Sfx.Play(Snd.TimeTick); OpenLoad(); return; }
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
