using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpectreEX.Hud
{
    // The Spectre VR instrument panel, rebuilt from the original's layout: green segmented
    // labels and readouts across the top (LIVES / DAMAGE / AMMO, SCORE / LEVEL / TIME), the
    // H (hyperspace) and F (flags) gauges, the WEAPON box, the tick-mark radar, the cockpit
    // crosshair, and the big "LEVEL n" banner with its red tip line.
    //
    // Everything is laid out in the original's 640x480 coordinates inside a 4:3 panel centred
    // at the top of the screen, so it keeps the 1993 proportions at any resolution.
    public class SpectreHud : MonoBehaviour
    {
        public static readonly Color LabelGreen = new Color(0.2f, 0.6f, 0.22f);
        public static readonly Color DigitGreen = new Color(0.25f, 0.78f, 0.27f);
        public static readonly Color FrameWhite = new Color(0.9f, 0.9f, 0.9f);
        public static readonly Color AlertRed = new Color(0.95f, 0.16f, 0.12f);
        public static readonly Color FlagBlue = new Color(0.45f, 0.45f, 1f);

        const float DesignScale = 540f / 480f;
        const float RadarRange = 140f;
        const float RadarRadius = 60f;

        RectTransform panel, screenRoot;
        SegmentText livesValue, ammoValue, scoreValue, levelValue, timeValue;
        RectTransform damageFill, ammoFill, hFill, fFill;
        Image damageFillImg, ammoFillImg;
        RectTransform radarRoot;
        readonly List<Image> radarDots = new List<Image>();
        GameObject crosshair;
        GameObject bannerRoot;
        SegmentText bannerTitle, bannerTip;
        GameObject statsRoot;
        readonly List<SegmentText> statLabels = new List<SegmentText>(), statValues = new List<SegmentText>();
        SegmentText message, hint;
        float messageUntil, hintUntil;

        GameManager gm;
        ViewCamera view;

        void Start()
        {
            gm = GameManager.Instance;
            if (Camera.main != null) view = Camera.main.GetComponent<ViewCamera>();
            Build();
            if (gm != null)
            {
                gm.ShowBanner += OnBanner;
                gm.ShowStats += OnStats;
                gm.HideOverlays += HideOverlays;
            }
            hintUntil = Time.time + 12f;
        }

        // ---------- construction ----------

        void Build()
        {
            var canvasGo = new GameObject("SpectreHUD", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960f, 540f);
            scaler.matchWidthOrHeight = 1f;

            screenRoot = (RectTransform)canvasGo.transform;

            panel = NewRect("Panel", screenRoot);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.sizeDelta = new Vector2(640f, 480f);
            panel.anchoredPosition = Vector2.zero;
            panel.localScale = Vector3.one * DesignScale;

            // Left block
            Label("LIVES", 64, 39);
            Label("DAMAGE", 64, 62);
            Label("AMMO", 64, 86);
            livesValue = Digits(128, 39);
            Frame(128, 56, 50, 11, FrameWhite, 1f);
            damageFill = Fill(129.5f, 57.5f, 47, 8, AlertRed, out damageFillImg, horizontal: true);
            ammoFill = Fill(128, 81, 22, 10, DigitGreen, out ammoFillImg, horizontal: true);
            ammoValue = Digits(154, 86);

            // Middle block
            Label("SCORE", 220, 39);
            Label("LEVEL", 220, 62);
            Label("TIME", 220, 86);
            scoreValue = Digits(268, 39);
            levelValue = Digits(268, 62);
            timeValue = Digits(268, 86);

            // H / F gauges
            Frame(359, 37, 12, 43, FrameWhite, 1f);
            hFill = Fill(360.5f, 38.5f, 9, 40, DigitGreen, out _, horizontal: false);
            Frame(379, 37, 12, 43, FrameWhite, 1f);
            fFill = Fill(380.5f, 38.5f, 9, 40, FlagBlue, out _, horizontal: false);
            Small("H", 365, 92, FrameWhite, SegmentText.Align.Center);
            Small("F", 385, 92, FrameWhite, SegmentText.Align.Center);

            // Weapon box with the original cannon icon (its own drawn border trimmed off)
            Label("WEAPON", 401, 39);
            Frame(407, 48, 36, 36, FrameWhite, 1f);
            var icon = NewRect("WeaponIcon", panel);
            Place(icon, 413, 54, 24, 24);
            var img = icon.gameObject.AddComponent<Image>();
            img.sprite = LoadWeaponIcon(0);
            img.color = img.sprite != null ? Color.white : Color.clear;

            // Radar: four tick marks around an invisible ring, dots for everything nearby
            radarRoot = NewRect("Radar", panel);
            Place(radarRoot, 515 - RadarRadius, 210 - RadarRadius, RadarRadius * 2, RadarRadius * 2);
            float r = RadarRadius;
            Line(515, 210 - r, 515, 210 - r + 6, FrameWhite, 1.2f);
            Line(515, 210 + r - 6, 515, 210 + r, FrameWhite, 1.2f);
            Line(515 - r, 210, 515 - r + 6, 210, FrameWhite, 1.2f);
            Line(515 + r - 6, 210, 515 + r, 210, FrameWhite, 1.2f);

            // Crosshair, centred on the screen (cockpit view only)
            var ch = NewRect("Crosshair", screenRoot);
            ch.anchorMin = ch.anchorMax = new Vector2(0.5f, 0.5f);
            ch.sizeDelta = Vector2.zero;
            ch.localScale = Vector3.one * DesignScale;
            crosshair = ch.gameObject;
            CrossLine(ch, new Vector2(0, 38), new Vector2(0, 52));
            CrossLine(ch, new Vector2(-58, 0), new Vector2(-42, 0));
            CrossLine(ch, new Vector2(42, 0), new Vector2(58, 0));

            // Level banner
            var banner = NewRect("Banner", panel);
            Place(banner, 0, 0, 640, 480);
            bannerRoot = banner.gameObject;
            bannerTitle = Text("", 320, 290, 64f, DigitGreen, SegmentText.Align.Center, 1.6f, banner);
            bannerTitle.spacing = 0.5f;
            bannerTip = Text("", 320, 382, 18f, AlertRed, SegmentText.Align.Center, 1.2f, banner);
            bannerRoot.SetActive(false);

            // End-of-level stats ("Time Allowed / Time Taken / Time Bonus / Score")
            var stats = NewRect("Stats", panel);
            Place(stats, 0, 0, 640, 480);
            statsRoot = stats.gameObject;
            for (int i = 0; i < 4; i++)
            {
                statLabels.Add(Small("", 330, 220 + i * 30, LabelGreen, SegmentText.Align.Right, stats, 14f));
                statValues.Add(Small("", 350, 220 + i * 30, DigitGreen, SegmentText.Align.Left, stats, 16f));
            }
            statsRoot.SetActive(false);

            message = Small("", 320, 420, AlertRed, SegmentText.Align.Center, null, 14f);
            hint = Small("ARROWS DRIVE   SPACE FIRE   TAB VIEW   BKSP HYPERSPACE   J JUMP", 320, 466, LabelGreen, SegmentText.Align.Center, null, 7f);
        }

        RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // Top-left based placement in the 640x480 design space
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        SegmentText Text(string s, float x, float yCentre, float height, Color c, SegmentText.Align align, float thickness, RectTransform parent = null)
        {
            var rt = NewRect("Text", parent != null ? parent : panel);
            float w = 600f;
            float left = align switch { SegmentText.Align.Center => x - w / 2, SegmentText.Align.Right => x - w, _ => x };
            Place(rt, left, yCentre - height, w, height * 2f);
            var t = rt.gameObject.AddComponent<SegmentText>();
            t.raycastTarget = false;
            t.charHeight = height;
            t.thickness = thickness;
            t.color = c;
            t.align = align;
            t.Text = s;
            return t;
        }

        // Labels use the same pixel-snapped segment lettering as the readouts, just smaller and dimmer
        SegmentText Label(string s, float x, float y) => Small(s, x, y, LabelGreen, SegmentText.Align.Left);

        SegmentText Small(string s, float x, float y, Color c, SegmentText.Align align, RectTransform parent = null, float height = 8f)
        {
            var t = Text(s, x, y, height, c, align, 1f, parent);
            t.gap = 0.04f;
            t.spacing = 0.25f;
            return t;
        }

        // All readouts share one size, thin equal segments, like the original's digits
        SegmentText Digits(float x, float y)
        {
            var t = Text("", x, y, 16f, DigitGreen, SegmentText.Align.Left, 1.35f);
            t.gap = 0.04f;
            return t;
        }

        Image Rect(float x, float y, float w, float h, Color c, RectTransform parent = null)
        {
            var rt = NewRect("Rect", parent != null ? parent : panel);
            Place(rt, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        void Frame(float x, float y, float w, float h, Color c, float t)
        {
            Rect(x, y, w, t, c);
            Rect(x, y + h - t, w, t, c);
            Rect(x, y, t, h, c);
            Rect(x + w - t, y, t, h, c);
        }

        void Line(float x1, float y1, float x2, float y2, Color c, float t)
        {
            if (Mathf.Approximately(x1, x2)) Rect(x1 - t / 2, Mathf.Min(y1, y2), t, Mathf.Abs(y2 - y1), c);
            else Rect(Mathf.Min(x1, x2), y1 - t / 2, Mathf.Abs(x2 - x1), t, c);
        }

        // Fill bars grow from the left (horizontal) or the bottom (vertical)
        RectTransform Fill(float x, float y, float w, float h, Color c, out Image img, bool horizontal)
        {
            img = Rect(x, y, w, h, c);
            var rt = img.rectTransform;
            if (!horizontal)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(x, -(y + h));
            }
            rt.gameObject.name = "Fill";
            rt.SetSiblingIndex(rt.parent.childCount - 1);
            var full = rt.sizeDelta;
            fullSizes[rt] = full;
            return rt;
        }

        readonly Dictionary<RectTransform, Vector2> fullSizes = new Dictionary<RectTransform, Vector2>();

        void SetFill(RectTransform rt, float t, bool horizontal)
        {
            var full = fullSizes[rt];
            t = Mathf.Clamp01(t);
            rt.sizeDelta = horizontal ? new Vector2(full.x * t, full.y) : new Vector2(full.x, full.y * t);
        }

        void CrossLine(RectTransform parent, Vector2 a, Vector2 b)
        {
            var rt = NewRect("Line", parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = (a + b) * 0.5f;
            Vector2 d = b - a;
            rt.sizeDelta = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Abs(d.x), 1.3f) : new Vector2(1.3f, Mathf.Abs(d.y));
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
            bannerTitle.Text = title;
            bannerTip.Text = tip ?? "";
        }

        void OnStats(string[] labels, string[] values)
        {
            bannerRoot.SetActive(false);
            statsRoot.SetActive(true);
            for (int i = 0; i < statLabels.Count; i++)
            {
                statLabels[i].Text = i < labels.Length ? labels[i] + ":" : "";
                statValues[i].Text = i < values.Length ? values[i] : "";
            }
        }

        void HideOverlays()
        {
            bannerRoot.SetActive(false);
            statsRoot.SetActive(false);
        }

        public void Flash(string text, float seconds = 1.5f)
        {
            message.Text = text;
            messageUntil = Time.time + seconds;
        }

        // ---------- per-frame ----------

        void Update()
        {
            if (gm == null || gm.player == null) return;
            var p = gm.player;

            livesValue.Text = Mathf.Max(0, gm.Lives).ToString();
            scoreValue.Text = gm.Score.ToString();
            levelValue.Text = gm.Level.ToString("00");
            timeValue.Text = Mathf.CeilToInt(gm.TimeRemaining).ToString("000");

            SetFill(damageFill, p.Health.Damage01, true);
            int ammo = p.Weapon.Ammo;
            float ammoT = p.Weapon.maxAmmo > 0 ? (float)ammo / p.Weapon.maxAmmo : 0f;
            bool lowAmmo = ammo <= p.Weapon.lowAmmoWarning;
            SetFill(ammoFill, ammoT, true);
            ammoFillImg.color = lowAmmo ? AlertRed : DigitGreen;
            ammoValue.Text = ammo.ToString("00");
            ammoValue.color = lowAmmo ? AlertRed : DigitGreen;

            SetFill(hFill, p.HyperspaceEnergy, false);
            SetFill(fFill, gm.FlagsTotal > 0 ? (float)gm.FlagsCollected / gm.FlagsTotal : 0f, false);

            if (ammo == 0 && gm.Playing && !p.Health.IsDead) Flash("OUT OF AMMO - FIND AN AMMO DUMP", 0.2f);

            message.enabled = Time.time < messageUntil;
            hint.enabled = Time.time < hintUntil;
            if (crosshair != null) crosshair.SetActive(view == null || view.mode == ViewMode.Internal);

            UpdateRadar(p.transform);
        }

        void UpdateRadar(Transform me)
        {
            int used = 0;
            Quaternion inv = Quaternion.Inverse(Quaternion.Euler(0f, me.eulerAngles.y, 0f));
            bool blink = (Time.time * 4f) % 1f < 0.55f;

            foreach (var h in gm.Robots)
                if (h != null && !h.IsDead) Dot(ref used, me, inv, h.transform.position, AlertRed, 3f);
            foreach (var f in gm.Flags)
                if (f != null && blink) Dot(ref used, me, inv, f.transform.position, new Color(1f, 0.9f, 0.3f), 3f);
            foreach (var d in gm.Dumps)
                if (d != null) Dot(ref used, me, inv, d.transform.position, DigitGreen, 3f);
            Dot(ref used, me, inv, me.position, FrameWhite, 2f);

            for (int i = used; i < radarDots.Count; i++) radarDots[i].enabled = false;
        }

        void Dot(ref int used, Transform me, Quaternion inv, Vector3 world, Color c, float size)
        {
            Vector3 rel = inv * (world - me.position);
            Vector2 pos = new Vector2(rel.x, rel.z) / RadarRange * RadarRadius;
            if (pos.magnitude > RadarRadius) return;

            if (used >= radarDots.Count)
            {
                var img = Rect(0, 0, 3, 3, c, radarRoot);
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
