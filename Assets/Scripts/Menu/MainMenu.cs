using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpectreEX.Menu
{
    // The original Spectre VR main menu and its dialogs, rebuilt from the game's own artwork
    // (Assets/Resources/Menu, copied from the DATA folder) at the largest whole-number scale that
    // fits the screen, with point filtering so the 1993 pixels stay crisp.
    //
    //   PLAY      -> Choose a vehicle (Balance / Speedy / Strong / Custom, Edit Custom..., Cancel, Play)
    //   CYBERNET  -> network play notice (not available)
    //   OPTIONS   -> the original Options dialog, wired to GameSettings
    //   SCORE     -> high scores (and name entry after a qualifying game)
    //   HELP      -> the original "Welcome to Spectre VR" page, then the controls
    //   ABOUT     -> the original credits
    //   MATRIX    -> Choose Matrix (Central Systems is the only arena)
    //   QUIT      -> quit
    // Keys: the first letter of each button, D for demo mode, and holding Alt while starting a game
    // begins at a higher level, as in the original.
    public class MainMenu : MonoBehaviour
    {
        public Font segmentFont;
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
        }

        class MenuScreen
        {
            public string name;
            public RectTransform root;
            public Vector2 size;            // image pixels
            public readonly List<Hotspot> hotspots = new List<Hotspot>();
            public readonly List<(RectTransform rt, Rect r)> placed = new List<(RectTransform, Rect)>();
            public readonly List<(Text t, int size)> texts = new List<(Text, int)>();
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
        MenuScreen main, vehicles, custom, options, matrix, about, help, controls, scores, notice;
        Text noticeText;
        GameManager gm;

        // Name entry for the high-score table
        bool enteringName;
        string pendingName = "";
        int pendingScore, pendingLevel;
        readonly List<Text> scoreRows = new List<Text>();

        public bool IsOpen => canvas != null && canvas.enabled;

        void Start()
        {
            gm = GameManager.Instance;
            GameSettings.Load();
            if (segmentFont == null) segmentFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Sample the glyph atlas with point filtering so strokes stay solid black, with no grey fringe
            PointFilterFont(segmentFont);
            Font.textureRebuilt += PointFilterFont;
            buttonsTex = LoadTex("Buttons");
            dotSprite = MakeDotSprite();
            BuildCanvas();
            BuildScreens();
            if (gm != null) gm.ReturnedToMenu += OnReturnedToMenu;
            if (Keyboard.current != null) Keyboard.current.onTextInput += OnTextInput;
            Open();
        }

        void OnDestroy()
        {
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInput;
            Font.textureRebuilt -= PointFilterFont;
        }

        void PointFilterFont(Font f)
        {
            if (f == segmentFont && f.material != null && f.material.mainTexture != null)
                f.material.mainTexture.filterMode = FilterMode.Point;
        }

        // ---------- open / close ----------

        public void Open()
        {
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

        IEnumerable<MenuScreen> AllScreens() => new[] { main, vehicles, custom, options, matrix, about, help, controls, scores, notice };

        void BuildScreens()
        {
            BuildMain();
            BuildVehicles();
            BuildCustom();
            BuildOptions();
            BuildMatrix();
            about = ImageScreen("About", "About");
            about.onAnyClick = about.onEscape = about.onEnter = Pop;
            help = ImageScreen("Help", "Help");
            help.onAnyClick = help.onEnter = () => { Pop(); Push(controls); };
            help.onEscape = Pop;
            BuildControls();
            BuildScores();
            BuildNotice();
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

        void BuildMain()
        {
            main = ImageScreen("Main", "MainMenu");
            Tile(main, 377, 25, 1, 0, () => Push(vehicles), Key.P);
            Tile(main, 449, 25, 3, 0, () => ShowNotice("CYBERNET NETWORK PLAY\nIS NOT AVAILABLE\nIN THIS VERSION"), Key.C);
            Tile(main, 377, 93, 1, 1, () => Push(options), Key.O);
            Tile(main, 449, 93, 3, 1, () => { enteringName = false; Push(scores); }, Key.S);
            Tile(main, 377, 161, 1, 2, () => Push(help), Key.H);
            Tile(main, 449, 161, 3, 2, () => Push(about), Key.A);
            Tile(main, 377, 229, -1, -1, () => Push(matrix), Key.M);
            Tile(main, 449, 229, 3, 3, Quit, Key.Q);
            Button(main, new Rect(0, 0, 0, 0), () => StartGame(true), Key.D);   // demo: keyboard only, as in the original
            main.onEnter = () => Push(vehicles);
        }

        // A main-menu button: hovering swaps in the original's highlighted tile from Buttons.png
        void Tile(MenuScreen s, int x, int y, int col, int row, System.Action action, Key key)
        {
            var h = new Hotspot { rect = new Rect(x, y, 64, 64), action = action, key = key };
            if (col >= 0)
            {
                var uv = new Rect(col * 64f / buttonsTex.width, 1f - (row + 1) * 64f / buttonsTex.height, 64f / buttonsTex.width, 64f / buttonsTex.height);
                h.hover = AddRaw(s, buttonsTex, h.rect, uv);
                h.hover.enabled = false;
            }
            else
            {
                h.tint = AddRect(s, h.rect, new Color(1f, 1f, 1f, 0.18f));
                h.tint.enabled = false;
            }
            s.hotspots.Add(h);
        }

        // A dialog button: hovering darkens it slightly, like a pressed Mac button
        Hotspot Button(MenuScreen s, Rect r, System.Action action, Key key = Key.None)
        {
            var h = new Hotspot { rect = r, action = action, key = key };
            if (r.width > 0)
            {
                h.tint = AddRect(s, r, new Color(0f, 0f, 0f, 0.18f));
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
            var stats = new List<Text[]>();
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
                    stats[i][0].text = v.speed.ToString();
                    stats[i][1].text = v.shields.ToString();
                    stats[i][2].text = v.ammo.ToString();
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
                points.text = (VehicleSpec.TotalPoints - (c.speed + c.shields + c.ammo)).ToString();
                speed.text = c.speed.ToString();
                shields.text = c.shields.ToString();
                ammo.text = c.ammo.ToString();
            };
        }

        void BuildOptions()
        {
            options = ImageScreen("Options", "Options");
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
            Radio(19, 92, () => GameSettings.Sound == SoundCard.PCSpeaker, () => GameSettings.Sound = SoundCard.PCSpeaker);
            Radio(19, 112, () => GameSettings.Sound == SoundCard.AdLib, () => GameSettings.Sound = SoundCard.AdLib);
            Radio(19, 132, () => GameSettings.Sound == SoundCard.SoundBlaster, () => GameSettings.Sound = SoundCard.SoundBlaster);
            int[] musicY = { 174, 202, 230, 258 };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                Check(21, musicY[i], () => GameSettings.Music[idx], () => GameSettings.Music[idx] = !GameSettings.Music[idx]);
            }
            Radio(167, 38, () => GameSettings.Difficulty == Difficulty.Easy, () => GameSettings.Difficulty = Difficulty.Easy);
            Radio(167, 62, () => GameSettings.Difficulty == Difficulty.Normal, () => GameSettings.Difficulty = Difficulty.Normal);
            Radio(167, 86, () => GameSettings.Difficulty == Difficulty.Hard, () => GameSettings.Difficulty = Difficulty.Hard);
            Check(167, 110, () => false, null, enabled: false);   // Use Custom Matrix Files: only Central Systems exists
            Check(169, 174, () => GameSettings.DisplayComplex, () => GameSettings.DisplayComplex = !GameSettings.DisplayComplex);
            Check(169, 202, () => GameSettings.DisplayFilled, () => GameSettings.DisplayFilled = !GameSettings.DisplayFilled);
            Check(169, 230, () => GameSettings.DisplayTextures, () => GameSettings.DisplayTextures = !GameSettings.DisplayTextures);

            Button(options, new Rect(11, 287, 170, 41), () => ShowNotice("NETWORK PLAY\nIS NOT AVAILABLE\nIN THIS VERSION"));
            System.Action done = () =>
            {
                GameSettings.Save();
                if (gm != null) gm.ApplySettings();
                if (MusicPlayer.Instance != null) MusicPlayer.Instance.PlayMenuTheme();
                Pop();
            };
            Button(options, new Rect(199, 287, 170, 41), done);
            options.onEscape = options.onEnter = done;
            options.refresh = () => { foreach (var r in refreshers) r(); };
        }

        void BuildMatrix()
        {
            matrix = ImageScreen("Matrix", "Matrix");
            // Only the Central Systems matrix ships with SpectreEX, so Previous / Next have nowhere to go
            Button(matrix, new Rect(15, 83, 77, 29), () => Sfx.Play(Snd.Collision));
            Button(matrix, new Rect(203, 83, 77, 29), () => Sfx.Play(Snd.Collision));
            Button(matrix, new Rect(108, 136, 77, 29), Pop);
            matrix.onEscape = matrix.onEnter = Pop;
        }

        void BuildControls()
        {
            controls = Panel("Controls", new Vector2(510, 292));
            var t = AddText(controls, new Rect(22, 14, 470, 268), 9, Color.black, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.lineSpacing = 1.15f;
            t.text = Seg(
                "SPECTREEX CONTROLS\n\n" +
                "DRIVE        ARROWS, WASD OR KEYPAD\n" +
                "FIRE         SPACE, KEYPAD 0 OR MOUSE\n" +
                "HYPERSPACE   BACKSPACE OR TILDE\n" +
                "JUMP         KEYPAD 5 OR J\n" +
                "VIEW         TAB OR KEYPAD *\n" +
                "ZOOM         + AND -\n" +
                "PAUSE        P\n" +
                "MAIN MENU    ESC\n" +
                "MUSIC        M ON-OFF, N NEXT TRACK\n" +
                "GAMEPAD      STICK, RT OR A FIRE\n" +
                "             Y HYPERSPACE, B JUMP\n\n" +
                "COLLECT EVERY FLAG TO FINISH A LEVEL.\n" +
                "DRIVE OVER AMMO DUMPS TO REPAIR AND REARM.\n" +
                "WAIT FOR THE H GAUGE BEFORE HYPERSPACE.\n\n" +
                "MAIN MENU: D FOR DEMO, HOLD ALT WHILE\n" +
                "STARTING TO BEGIN AT A HIGHER LEVEL.");
            controls.onAnyClick = controls.onEscape = controls.onEnter = Pop;
        }

        void BuildScores()
        {
            // Drawn over the robot artwork on the main menu, in the HUD's green segment display style
            scores = NewScreen("Scores", new Vector2(MainW, MainH));
            AddRect(scores, new Rect(9, 9, 362, 283), Color.black);
            var title = AddText(scores, new Rect(9, 16, 362, 24), 15, new Color(0.25f, 0.78f, 0.27f), TextAnchor.MiddleCenter, segmentFont);
            title.text = Seg("HIGH SCORES");
            for (int i = 0; i < HighScores.Count; i++)
                scoreRows.Add(AddText(scores, new Rect(24, 50 + i * 22, 332, 20), 11, new Color(0.25f, 0.78f, 0.27f), TextAnchor.MiddleLeft, segmentFont));
            var footer = AddText(scores, new Rect(9, 270, 362, 18), 8, new Color(0.2f, 0.6f, 0.22f), TextAnchor.MiddleCenter, segmentFont);
            scores.refresh = () =>
            {
                var list = HighScores.Load();
                int insertAt = -1;
                if (enteringName)
                {
                    insertAt = list.FindIndex(e => pendingScore > e.score);
                    if (insertAt < 0) insertAt = list.Count;
                    list.Insert(insertAt, new HighScores.Entry { name = pendingName + (Time.unscaledTime % 1f < 0.5f ? "_" : " "), score = pendingScore, level = pendingLevel });
                }
                for (int i = 0; i < scoreRows.Count; i++)
                {
                    if (i < list.Count)
                    {
                        var e = list[i];
                        scoreRows[i].text = Seg($"{i + 1,2} {e.name,-9} {e.score,7} L{e.level:00}");
                        scoreRows[i].color = i == insertAt ? new Color(0.95f, 0.16f, 0.12f) : new Color(0.25f, 0.78f, 0.27f);
                    }
                    else scoreRows[i].text = "";
                }
                footer.text = Seg(enteringName ? "TYPE YOUR NAME AND PRESS ENTER" : "PRESS ANY KEY");
            };
            scores.onAnyClick = () => { if (!enteringName) Pop(); };
            scores.onEscape = () => { enteringName = false; Pop(); };
            scores.onEnter = () =>
            {
                if (enteringName)
                {
                    HighScores.Add(pendingName.Length > 0 ? pendingName : "SPECTRE", pendingScore, pendingLevel);
                    enteringName = false;
                    scores.refresh();
                }
                else Pop();
            };
        }

        void OnTextInput(char c)
        {
            if (!enteringName || stack.Count == 0 || stack[stack.Count - 1] != scores) return;
            if (char.IsLetterOrDigit(c) && pendingName.Length < 8) pendingName += char.ToUpperInvariant(c);
        }

        void BuildNotice()
        {
            notice = Panel("Notice", new Vector2(300, 110));
            noticeText = AddText(notice, new Rect(14, 14, 272, 52), 9, Color.black, TextAnchor.MiddleCenter);
            noticeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ok = new Rect(110, 72, 80, 26);
            AddRect(notice, ok, Color.black);
            AddRect(notice, new Rect(ok.x + 2, ok.y + 2, ok.width - 4, ok.height - 4), new Color(0.75f, 0.75f, 0.75f));
            AddText(notice, ok, 10, Color.black, TextAnchor.MiddleCenter).text = "OK";
            Button(notice, ok, Pop);
            notice.onAnyClick = null;
            notice.onEscape = notice.onEnter = Pop;
        }

        void ShowNotice(string text)
        {
            noticeText.text = Seg(text);
            Push(notice);
        }

        // A plain Mac-style dialog panel: white with a black and purple border like the originals
        MenuScreen Panel(string name, Vector2 size)
        {
            var s = NewScreen(name, size);
            AddRect(s, new Rect(0, 0, size.x, size.y), new Color(0.25f, 0.1f, 0.45f));
            AddRect(s, new Rect(3, 3, size.x - 6, size.y - 6), Color.black);
            AddRect(s, new Rect(4, 4, size.x - 8, size.y - 8), Color.white);
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

        Text AddText(MenuScreen s, Rect r, int size, Color c, TextAnchor anchor, Font font = null)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(s.root, false);
            var t = go.AddComponent<Text>();
            t.font = font != null ? font : segmentFont;
            t.fontStyle = FontStyle.Normal;
            t.color = c;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            s.placed.Add(((RectTransform)go.transform, r));
            s.texts.Add((t, size));
            return t;
        }

        // DSEG14: uppercase only, '!' is its full-width blank (a plain space is a narrow gap),
        // and '~' lights every segment, so never show a literal tilde
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
                s.root.sizeDelta = s.size * scale;
                foreach (var (rt, r) in s.placed)
                {
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(r.x, -r.y) * scale;
                    rt.sizeDelta = r.size * scale;
                }
                foreach (var (t, size) in s.texts) t.fontSize = EvenSegmentSize(size * scale);
            }
            // Corner to corner of the 11x11 interior: 11 * sqrt(2)
            foreach (var (stroke, box) in strokes)
                stroke.sizeDelta = new Vector2(15.5f * scale, Mathf.Max(1, scale));
        }

        // Pixel sizes at which DSEG14 rasterises with every segment the same thickness (measured with
        // FreeType: 1px strokes at 10-16, 2px at 20-26, 3px at 30-37, 4px at 41-48). Sizes in between,
        // such as 18, mix 1px and 2px segments and look uneven.
        static readonly int[] EvenSizes = { 10, 11, 12, 13, 14, 15, 16, 20, 21, 22, 23, 24, 26, 30, 31, 32, 34, 36, 37, 41, 42, 44, 46, 47, 48 };

        static int EvenSegmentSize(int wanted)
        {
            if (wanted > EvenSizes[EvenSizes.Length - 1]) return wanted;
            int best = EvenSizes[0];
            foreach (int s in EvenSizes)
                if (Mathf.Abs(s - wanted) <= Mathf.Abs(best - wanted)) best = s;   // ties go to the larger size
            return best;
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
            ignoreClickFrame = Time.frameCount;
        }

        int ignoreClickFrame;

        void Update()
        {
            if (!IsOpen || stack.Count == 0) return;
            if (Screen.width != lastScreen.x || Screen.height != lastScreen.y) Relayout();

            var top = stack[stack.Count - 1];
            if (top == scores && enteringName) scores.refresh();   // blink the cursor

            // Mouse position in the top screen's image pixels (y down)
            Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(-1, -1);
            Vector2 size = top.size * scale;
            Vector2 origin = new Vector2((Screen.width - size.x) / 2f, (Screen.height - size.y) / 2f);
            Vector2 local = (mouse - origin) / scale;
            Vector2 img = new Vector2(local.x, top.size.y - local.y);

            Hotspot hovered = null;
            foreach (var h in top.hotspots)
            {
                bool over = h.rect.width > 0 && h.rect.Contains(img) && h.action != null;
                if (over) hovered = h;
                if (h.hover != null) h.hover.enabled = over;
                if (h.tint != null) h.tint.enabled = over;
            }

            bool click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Time.frameCount != ignoreClickFrame;
            if (click)
            {
                if (hovered != null) { hovered.action(); return; }
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
            if (k.escapeKey.wasPressedThisFrame) { top.onEscape?.Invoke(); return; }
            if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) { top.onEnter?.Invoke(); return; }
            foreach (var h in top.hotspots)
                if (h.key != Key.None && h.action != null && k[h.key].wasPressedThisFrame) { h.action(); return; }
            if (top.onAnyClick != null && k.anyKey.wasPressedThisFrame) top.onAnyClick();
        }
    }
}
