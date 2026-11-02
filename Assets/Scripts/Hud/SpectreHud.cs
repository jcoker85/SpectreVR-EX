using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SpectreEX.Matrix;

namespace SpectreEX.Hud
{
    // The Spectre VR instrument panel, rebuilt for widescreen: green DSEG14 segment-display text,
    // LIVES / DAMAGE / AMMO and SCORE / LEVEL / BONUS hugging the top-left, the H (hyperspace) and
    // F (flags) gauges and WEAPON box in the top-right corner, the tick-mark radar in the bottom-right
    // corner, the cockpit crosshair, and the big "LEVEL n" banner with its red tip line.
    //
    // Layout units are a 480-unit-tall screen (the original's height); widths follow the real aspect.
    public class SpectreHud : MonoBehaviour
    {
        public static readonly Color LabelGreen = new Color(0.2f, 0.6f, 0.22f);
        public static readonly Color DigitGreen = new Color(0.25f, 0.78f, 0.27f);
        public static readonly Color FrameWhite = new Color(0.9f, 0.9f, 0.9f);
        public static readonly Color AlertRed = new Color(0.95f, 0.16f, 0.12f);
        public static readonly Color DemoBlue = new Color(0.2f, 0.25f, 0.95f);
        public static readonly Color FlagBlue = new Color(0.45f, 0.45f, 1f);

        public Font segmentFont;   // DSEG14 Classic Light, assigned by the scene builder

        const float Margin = 12f;
        const int LabelSize = 9;
        const int ValueSize = 17;
        const float MaxAmmoBar = 27f;     // 50 rounds, the most a craft can carry
        const float RadarRange = 159f;   // _RADAR_W / _RADAR_DW: about 0x7C000 units either way, in a square
        const float RadarRadius = 56f;

        RectTransform screenRoot;
        Text livesValue, ammoValue, scoreValue, levelValue, timeValue;
        Image weaponIcon;
        int shownWeapon;
        RectTransform damageFill, ammoFill, hFill, fFill;
        Image ammoFillImg;
        RectTransform radarRoot;
        readonly List<Image> radarDots = new List<Image>();
        GameObject crosshair, bannerRoot, statsRoot, demoRoot;
        Text bannerTitle, bannerTip, message, gameOver;
        readonly List<Text> recapWords = new List<Text>();
        readonly List<RecapWedge> recapWedges = new List<RecapWedge>();
        readonly Dictionary<RectTransform, Vector2> fullSizes = new Dictionary<RectTransform, Vector2>();
        float messageUntil;
        RectTransform damageGroup, gameOverRoot, radarCornerRoot;
        Image hFillImg;
        Vector2 crosshairAt, radarAt;
        readonly List<Text> radarLetters = new List<Text>();

        GameManager gm;
        ViewCamera view;

        void Start()
        {
            gm = GameManager.Instance;
            if (Camera.main != null) view = Camera.main.GetComponent<ViewCamera>();
            if (segmentFont == null) segmentFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
            if (gm != null)
            {
                gm.ShowBanner += OnBanner;
                gm.ShowRecap += OnRecap;
                gm.HideOverlays += HideOverlays;
                gm.GameOverSpin += OnGameOverSpin;
            }
        }

        // ---------- construction ----------

        void Build()
        {
            var canvasGo = new GameObject("SpectreHUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvas.pixelPerfect = true;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(854f, 480f);
            scaler.matchWidthOrHeight = 1f;
            screenRoot = (RectTransform)canvasGo.transform;

            // Top-left: LIVES / DAMAGE / AMMO
            var left = Group("Left", new Vector2(0f, 1f), new Vector2(Margin, -Margin));
            float r1 = 8f, r2 = 30f, r3 = 52f;
            Label(left, "LIVES", 0, r1);
            Label(left, "DAMAGE", 0, r2);
            Label(left, "AMMO", 0, r3);
            livesValue = Value(left, 66, r1);
            // The bars are as long as the craft's capacity (_SETUP_SCN, _SHOW_SCORE)
            damageGroup = NewRect("Damage", left);
            damageGroup.anchoredPosition = new Vector2(66f, 0f);
            Frame(damageGroup, 0, r2 - 6, 50, 12, FrameWhite);
            damageFill = Fill(damageGroup, 1.5f, r2 - 4.5f, 47, 9, AlertRed, out _, horizontal: true);
            ammoFill = Fill(left, 66, r3 - 5, MaxAmmoBar, 10, DigitGreen, out ammoFillImg, horizontal: true);
            ammoValue = Value(left, 96, r3);

            // Beside it: SCORE / LEVEL / BONUS
            var mid = Group("Score", new Vector2(0f, 1f), new Vector2(Margin + 178f, -Margin));
            Label(mid, "SCORE", 0, r1);
            Label(mid, "LEVEL", 0, r2);
            Label(mid, "BONUS", 0, r3);
            scoreValue = Value(mid, 56, r1);
            levelValue = Value(mid, 56, r2);
            timeValue = Value(mid, 56, r3);

            // Bottom-right corner: radar (tick marks around an invisible ring, dots for everything nearby)
            var radarCorner = Group("RadarCorner", new Vector2(1f, 0f), new Vector2(-Margin, Margin));
            radarCornerRoot = radarCorner;
            radarAt = radarCorner.anchoredPosition;
            float rd = 2 * RadarRadius;
            radarRoot = NewRect("Radar", radarCorner);
            Place(radarRoot, -rd, -rd, rd, rd);
            float cx = -RadarRadius, cy = -RadarRadius, rr = RadarRadius;
            Line(radarCorner, cx, cy - rr, cx, cy - rr + 6, FrameWhite);
            Line(radarCorner, cx, cy + rr - 6, cx, cy + rr, FrameWhite);
            Line(radarCorner, cx - rr, cy, cx - rr + 6, cy, FrameWhite);
            Line(radarCorner, cx + rr - 6, cy, cx + rr, cy, FrameWhite);

            // Top-right corner: WEAPON box flush right, H (hyperspace) / F (floating) gauges to its left
            var right = Group("Right", new Vector2(1f, 1f), new Vector2(-Margin, -Margin));
            float wx = -38;
            MakeText(right, "WEAPON", 0, r1, LabelSize, LabelGreen, TextAnchor.MiddleRight);
            Frame(right, wx, 17, 38, 38, FrameWhite);
            var icon = NewRect("WeaponIcon", right);
            Place(icon, wx + 7, 24, 24, 24);
            var img = icon.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.sprite = LoadWeaponIcon(TankWeapon.IconCell[0]);
            img.color = img.sprite != null ? Color.white : Color.clear;
            weaponIcon = img;

            float fx = wx - 24, hx = wx - 44;
            Frame(right, hx, 2, 12, 46, FrameWhite);
            hFill = Fill(right, hx + 1.5f, 3.5f, 9, 43, DigitGreen, out hFillImg, horizontal: false);
            Frame(right, fx, 2, 12, 46, FrameWhite);
            fFill = Fill(right, fx + 1.5f, 3.5f, 9, 43, FlagBlue, out _, horizontal: false);
            MakeText(right, "H", hx + 6, r3 + 4, LabelSize, FrameWhite, TextAnchor.MiddleCenter);
            MakeText(right, "F", fx + 6, r3 + 4, LabelSize, FrameWhite, TextAnchor.MiddleCenter);

            // Centre: crosshair (cockpit view only), level banner, end-of-level stats
            var centre = Group("Centre", new Vector2(0.5f, 0.5f), Vector2.zero);
            centre.SetAsFirstSibling();   // under the readouts, which stay up over the recap's blank view
            var ch = NewRect("Crosshair", centre);
            crosshair = ch.gameObject;
            crosshairAt = new Vector2(0f, -ViewCamera.CentreDrop * 480f);   // _SHOW_RADAR draws it at _TY
            ch.anchoredPosition = crosshairAt;
            // 29-36 rows above the centre and 30-40 columns out, two rows up, on the 530-row window
            CrossLine(ch, new Vector2(0, 26), new Vector2(0, 33));
            CrossLine(ch, new Vector2(-36, 2), new Vector2(-27, 2));
            CrossLine(ch, new Vector2(27, 2), new Vector2(36, 2));

            var demo = NewRect("Demo", centre);
            demoRoot = demo.gameObject;
            MakeText(demo, "DEMO MODE", 0, -16, 16, DigitGreen, TextAnchor.MiddleCenter);
            MakeText(demo, "PRESS A KEY", 0, 8, 12, DemoBlue, TextAnchor.MiddleCenter);
            demoRoot.SetActive(false);

            var banner = NewRect("Banner", centre);
            bannerRoot = banner.gameObject;
            bannerTitle = MakeText(banner, "", 0, 50, 54, DigitGreen, TextAnchor.MiddleCenter);
            bannerTip = MakeText(banner, "", 0, 142, 16, AlertRed, TextAnchor.MiddleCenter);
            bannerRoot.SetActive(false);

            gameOverRoot = NewRect("GameOver", centre);
            gameOver = MakeText(gameOverRoot, "GAME OVER", 0, 0, 54, DigitGreen, TextAnchor.MiddleCenter);
            gameOverRoot.gameObject.SetActive(false);

            var stats = NewRect("Stats", centre);
            statsRoot = stats.gameObject;
            // _ERASE_BOTTOM: the recap replaces the view
            var blank = new GameObject("Blank", typeof(RectTransform)).AddComponent<Image>();
            blank.transform.SetParent(stats, false);
            blank.color = Color.black;
            blank.raycastTarget = false;
            blank.rectTransform.anchorMin = blank.rectTransform.anchorMax = blank.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            blank.rectTransform.sizeDelta = new Vector2(8000f, 8000f);

            string[] names = { "QUICKNESS", "ACCURACY", "AVOIDANCE" };
            for (int i = 0; i < names.Length; i++)
            {
                float y = RecapY(i);
                MakeText(stats, names[i], RecapX(6), y, 18, FrameWhite, TextAnchor.MiddleLeft);
                var wedge = new GameObject("Wedge", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<RecapWedge>();
                wedge.transform.SetParent(stats, false);
                wedge.color = RecapGrey;
                wedge.raycastTarget = false;
                var rt = wedge.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(RecapX(100), -y);
                recapWedges.Add(wedge);
                recapWords.Add(MakeText(stats, "", RecapX(246), y, 18, DigitGreen, TextAnchor.MiddleLeft));
            }
            statsRoot.SetActive(false);

            // Bottom: the weapon message
            var bottom = Group("Bottom", new Vector2(0.5f, 0f), Vector2.zero);
            message = MakeText(bottom, "", 0, -60, 13, AlertRed, TextAnchor.MiddleCenter);
        }

        RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        // A zero-size anchor point on the screen; children are laid out from it, y downwards
        RectTransform Group(string name, Vector2 anchor, Vector2 offset)
        {
            var rt = NewRect(name, screenRoot);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = offset;
            return rt;
        }

        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        Text MakeText(RectTransform parent, string s, float x, float yCentre, int size, Color c, TextAnchor anchor)
        {
            var rt = NewRect("Text", parent);
            float w = 600f, h = size * 2f;
            float left = anchor switch
            {
                TextAnchor.MiddleCenter => x - w / 2,
                TextAnchor.MiddleRight => x - w,
                _ => x,
            };
            Place(rt, left, yCentre - h / 2, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = segmentFont;
            t.fontSize = size;
            t.color = c;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = Seg(s);
            return t;
        }

        // DSEG draws ' ' as a narrow gap; '!' is its full-width blank, so words keep their spacing
        static string Seg(string s) => s?.Replace(' ', '!');

        Text Label(RectTransform parent, string s, float x, float y) => MakeText(parent, s, x, y, LabelSize, LabelGreen, TextAnchor.MiddleLeft);
        Text Value(RectTransform parent, float x, float y) => MakeText(parent, "", x, y, ValueSize, DigitGreen, TextAnchor.MiddleLeft);

        Image Rect(RectTransform parent, float x, float y, float w, float h, Color c)
        {
            var rt = NewRect("Rect", parent);
            Place(rt, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        void Frame(RectTransform parent, float x, float y, float w, float h, Color c, float t = 1f)
        {
            Rect(parent, x, y, w, t, c);
            Rect(parent, x, y + h - t, w, t, c);
            Rect(parent, x, y, t, h, c);
            Rect(parent, x + w - t, y, t, h, c);
        }

        void Line(RectTransform parent, float x1, float y1, float x2, float y2, Color c, float t = 1f)
        {
            if (Mathf.Approximately(x1, x2)) Rect(parent, x1 - t / 2, Mathf.Min(y1, y2), t, Mathf.Abs(y2 - y1), c);
            else Rect(parent, Mathf.Min(x1, x2), y1 - t / 2, Mathf.Abs(x2 - x1), t, c);
        }

        // Fill bars grow from the left (horizontal) or the bottom (vertical)
        RectTransform Fill(RectTransform parent, float x, float y, float w, float h, Color c, out Image img, bool horizontal)
        {
            img = Rect(parent, x, y, w, h, c);
            var rt = img.rectTransform;
            if (!horizontal)
            {
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(x, -(y + h));
            }
            rt.gameObject.name = "Fill";
            fullSizes[rt] = rt.sizeDelta;
            return rt;
        }

        void SetFill(RectTransform rt, float t, bool horizontal)
        {
            var full = fullSizes[rt];
            t = Mathf.Clamp01(t);
            rt.sizeDelta = horizontal ? new Vector2(full.x * t, full.y) : new Vector2(full.x, full.y * t);
        }

        void CrossLine(RectTransform parent, Vector2 a, Vector2 b)
        {
            var rt = NewRect("Line", parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = (a + b) * 0.5f;
            Vector2 d = b - a;
            rt.sizeDelta = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Abs(d.x), 1f) : new Vector2(1f, Mathf.Abs(d.y));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = FrameWhite;
            img.raycastTarget = false;
        }

        static Sprite LoadWeaponIcon(int index)
        {
            var tex = Resources.Load<Texture2D>("HUD/WeaponIcons");
            if (tex == null) return null;
            tex.filterMode = FilterMode.Point;
            int size = tex.height;
            // Each 16x16 cell carries its own white frame; trim 2px so only the symbol shows inside our box
            return Sprite.Create(tex, new UnityEngine.Rect(index * size + 2, 2, size - 4, size - 4), new Vector2(0.5f, 0.5f), 100f);
        }

        // ---------- overlays ----------

        void OnBanner(string title, string tip)
        {
            statsRoot.SetActive(false);
            bannerRoot.SetActive(true);
            bannerTitle.text = Seg(title);
            bannerTip.text = Seg(tip ?? "");
        }

        // _SPIN_UP: turning and growing to full size, its height swinging with a cosine, then at rest
        void OnGameOverSpin(int step)
        {
            bannerRoot.SetActive(false);
            statsRoot.SetActive(false);
            gameOverRoot.gameObject.SetActive(true);
            if (step < 0)
            {
                gameOverRoot.localRotation = Quaternion.identity;
                gameOverRoot.localScale = Vector3.one;
                return;
            }
            int turn = step & 0x1F;
            float size = step / 32f;
            gameOverRoot.localRotation = Quaternion.Euler(0f, 0f, turn * 8 * OriginalMath.StepsToDegrees);
            gameOverRoot.localScale = new Vector3(size, size * OriginalMath.Cos[(turn << 5) & 0xFF] / 256f, 1f);
        }

        // _SHOW_RECAP / _SHOW_TRIANGLE, measured off the running original: x in SCALE_X units on a 320-wide
        // screen (wedge from 100, value * step / 16 long, rating at 246), lines and wedge height in its
        // 532-wide window's pixels (30 apart from the middle of the screen, 20 high)
        const float RecapScale = 2.6f;
        const float RecapPixel = RecapScale * 320f / 532f;
        static readonly Color RecapGrey = new Color(0.75f, 0.75f, 0.75f);
        static float RecapX(float x) => (x - 160f) * RecapScale;
        static float RecapY(int line) => line * 30 * RecapPixel;

        void OnRecap(int[] rates, int step)
        {
            bannerRoot.SetActive(false);
            statsRoot.SetActive(true);
            for (int i = 0; i < recapWedges.Count; i++)
            {
                int rate = Mathf.Clamp(rates[i], 0, 0x80);
                recapWedges[i].rectTransform.sizeDelta = new Vector2(rate * step / 16f * RecapScale, 20f * RecapPixel);
                recapWords[i].text = Seg(rate == 0x80 ? "PERFECT" : rate > 100 ? "RADICAL" : rate > 64 ? "NOT BAD" : rate > 32 ? "MARGINAL" : "GET HELP");
                recapWords[i].color = rate > 100 ? DigitGreen : rate > 64 ? FrameWhite : AlertRed;
            }
        }

        // A triangle pointing left, filling its rect
        class RecapWedge : MaskableGraphic
        {
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                var r = rectTransform.rect;
                vh.Clear();
                vh.AddVert(new Vector2(r.xMin, r.center.y), color, Vector2.zero);
                vh.AddVert(new Vector2(r.xMax, r.yMin), color, Vector2.zero);
                vh.AddVert(new Vector2(r.xMax, r.yMax), color, Vector2.zero);
                vh.AddTriangle(0, 2, 1);
            }
        }

        void HideOverlays()
        {
            bannerRoot.SetActive(false);
            statsRoot.SetActive(false);
            gameOverRoot.gameObject.SetActive(false);
        }

        public void Flash(string text, float seconds = 1.5f)
        {
            message.text = Seg(text);
            messageUntil = Time.time + seconds;
        }

        // ---------- per-frame ----------

        void Update()
        {
            if (gm == null || gm.player == null) return;
            var p = gm.player;

            livesValue.text = Mathf.Max(0, gm.Lives).ToString();
            scoreValue.text = gm.Score.ToString();
            levelValue.text = gm.Level.ToString("00");
            timeValue.text = gm.Bonus.ToString("0000");   // the original shows the bonus counting down

            damageGroup.localScale = new Vector3(p.Health.maxShields / 60f, 1f, 1f);
            SetFill(damageFill, p.Health.Damage01, true);
            int ammo = p.Weapon.Ammo;
            SetFill(ammoFill, ammo / 50f, true);
            ammoFillImg.color = ammo < 10 ? AlertRed : DigitGreen;   // the digits stay as they are
            ammoValue.text = ammo.ToString("00");

            // _SHOW_SCORE: both in 25 steps and empty at the bottom; H is red until it's full
            int hyper = p.Motor.Hyper >> 8, floating = p.Motor.Floating / 0x36;
            SetFill(hFill, hyper > 1 ? hyper / 25f : 0f, false);
            hFillImg.color = hyper == 0x19 ? DigitGreen : AlertRed;
            SetFill(fFill, floating > 2 ? floating / 25f : 0f, false);

            // The bounce shake reaches the radar and crosshair, always the one way (_SHOW_RADAR)
            var shake = new Vector2(-Mathf.Abs(p.Motor.CenterX), -Mathf.Abs(p.Motor.CenterY)) * 2.4f;
            radarCornerRoot.anchoredPosition = radarAt + shake;
            ((RectTransform)crosshair.transform).anchoredPosition = crosshairAt + shake;

            // The heavy weapon's icon, and "<name> Online" for three seconds after a change, as the original shows
            if (weaponIcon != null && shownWeapon != p.Weapon.AltWeapon)
            {
                shownWeapon = p.Weapon.AltWeapon;
                weaponIcon.sprite = LoadWeaponIcon(TankWeapon.IconCell[shownWeapon]);
            }
            float banner = p.Weapon.BannerUntil - Time.time;
            if (banner > 0f && gm.Playing)
                Flash(p.Weapon.AltWeapon == 0 ? "HEAVY WEAPONS OFFLINE" : TankWeapon.Names[p.Weapon.AltWeapon].ToUpperInvariant() + " ONLINE", banner);

            demoRoot.SetActive(gm.Demo && gm.Playing);
            message.enabled = Time.time < messageUntil;
            radarRoot.parent.gameObject.SetActive(view == null || view.mode == ViewMode.Internal || view.mode == ViewMode.External);   // _SHOW_RADAR
            if (crosshair != null) crosshair.SetActive(view == null || view.mode == ViewMode.Internal);

            UpdateRadar(p.transform);
        }

        void UpdateRadar(Transform me)
        {
            int used = 0, letters = 0;
            Quaternion inv = Quaternion.Inverse(Quaternion.Euler(0f, me.eulerAngles.y, 0f));
            Color Ramp(int colour) => SpectrePalette.Colour(colour + 0x0C);

            // _SHOW_RADAR's order: dumps, the first level's pads, robots, gates and transports, flags
            foreach (var d in gm.Dumps)
                if (d != null) Dot(ref used, me, inv, d.transform.position, Ramp(d.Ramp), 3f);
            foreach (var p in gm.JumpPads)
                if (p != null) Dot(ref used, me, inv, p.transform.position, Ramp(0x30), 3f);

            // Five robot types show as letters. Cloaked Tanks don't show; a dying robot still does.
            foreach (var r in gm.GetComponent<RobotDirector>().Robots)
            {
                if (r == null || !r.visible || r.Health.hiddenFromRadar) continue;
                Color c = Ramp(BuiltinShapes.TypeColour(r.matrixType));
                char letter = r.matrixType switch { 20 => 'G', 21 => 'S', 22 => 'H', 23 => 'A', 24 => 'M', _ => ' ' };
                if (letter == ' ') Dot(ref used, me, inv, r.transform.position, c, 3f);
                else Letter(ref letters, me, inv, r.transform.position, c, letter);
            }
            foreach (var o in gm.Obstacles)
            {
                var gate = o != null ? o.GetComponent<ObstacleBehaviour>() : null;
                if (gate == null) continue;
                char letter = gate.kind switch
                {
                    ObstacleBehaviour.Kind.Teleport => 'T',
                    ObstacleBehaviour.Kind.UpTransport => 'U',
                    ObstacleBehaviour.Kind.DownTransport => 'D',
                    _ => ' ',
                };
                if (letter != ' ') Letter(ref letters, me, inv, o.transform.position, Ramp(0x30), letter);
            }
            // Flags go dark for 4 ticks in 16 (_LEVEL_TIMER & 0xC)
            if ((gm.LevelTicks & 0xC) != 0)
                foreach (var f in gm.Flags)
                    if (f != null) Dot(ref used, me, inv, f.transform.position, Ramp(0x50), 3f);
            Dot(ref used, me, inv, me.position, SpectrePalette.Colour(0x2F), 3f);

            for (int i = used; i < radarDots.Count; i++) radarDots[i].enabled = false;
            for (int i = letters; i < radarLetters.Count; i++) radarLetters[i].enabled = false;
        }

        // Inside the radar's square, or not shown at all
        static bool OnRadar(Transform me, Quaternion inv, Vector3 world, out Vector2 pos)
        {
            Vector3 rel = inv * (world - me.position);
            pos = new Vector2(rel.x, rel.z) / RadarRange * RadarRadius;
            return Mathf.Abs(pos.x) < RadarRadius && Mathf.Abs(pos.y) < RadarRadius;
        }

        void Letter(ref int used, Transform me, Quaternion inv, Vector3 world, Color c, char letter)
        {
            if (!OnRadar(me, inv, world, out var pos)) return;
            if (used >= radarLetters.Count)
            {
                var t = MakeText(radarRoot, "", 0f, 0f, 7, c, TextAnchor.MiddleCenter);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(12f, 12f);
                radarLetters.Add(t);
            }
            var text = radarLetters[used++];
            text.enabled = true;
            text.color = c;
            text.text = letter.ToString();
            text.rectTransform.anchoredPosition = pos;
        }

        void Dot(ref int used, Transform me, Quaternion inv, Vector3 world, Color c, float size)
        {
            if (!OnRadar(me, inv, world, out var pos)) return;

            if (used >= radarDots.Count)
            {
                var img = Rect(radarRoot, 0, 0, 3, 3, c);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                radarDots.Add(img);
            }
            var dot = radarDots[used++];
            dot.enabled = true;
            dot.color = c;
            dot.rectTransform.sizeDelta = new Vector2(size, size);
            dot.rectTransform.anchoredPosition = pos;
        }
    }
}
