using UnityEngine;
using UnityEngine.UI;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // _DRAW_GOD_VIEW: the hidden map, on the original's 760 x 530 window scaled to the screen's height, at
    // its left edge and under the HUD. From row 50 down is erased to black, and the rows above stay as the
    // last view left them. Every object is one pixel around (160, 120), a pixel to 8 grid steps, inside a
    // frame of entry 15, which puts the map in the top left. The skybox stays off while it's up.
    [RequireComponent(typeof(ViewCamera))]
    public class MapView : MonoBehaviour
    {
        // The remake's HUD reaches further down than the original's 50 rows, so the map sits half its own
        // height lower to clear it
        const int HudDrop = 65;
        const int Width = 760, Height = 530, EraseFrom = 50, CentreX = 0xA0, CentreY = 0x78 + HudDrop;
        const int FrameLeft = 0x5F, FrameRight = 0xE1, FrameTop = 0x37 + HudDrop, FrameBottom = 0xB9 + HudDrop;
        const int FrameEntry = 0x0F, FlagEntry = 0x0E, RampStep = 0x0C;
        const int PlayerType = 6;
        const float GridPerPixel = 8f * GameManager.GridScale;

        ViewCamera view;
        Canvas canvas;
        RectTransform picture;
        Texture2D texture;
        readonly Color32[] pixels = new Color32[Width * Height];
        bool showing;

        void Awake()
        {
            view = GetComponent<ViewCamera>();
            var go = new GameObject("SpectreMap", typeof(Canvas));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;   // the HUD is at 10
            canvas.enabled = false;

            var black = new GameObject("Erased", typeof(RectTransform)).AddComponent<Image>();
            black.transform.SetParent(go.transform, false);
            black.color = Color.black;
            black.raycastTarget = false;
            var area = black.rectTransform;
            area.anchorMin = Vector2.zero;
            area.anchorMax = new Vector2(1f, 1f - (float)EraseFrom / Height);
            area.offsetMin = area.offsetMax = Vector2.zero;

            texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var image = new GameObject("Map", typeof(RectTransform)).AddComponent<RawImage>();
            image.transform.SetParent(go.transform, false);
            image.texture = texture;
            image.raycastTarget = false;
            picture = image.rectTransform;
            picture.anchorMin = Vector2.zero;
            picture.anchorMax = Vector2.up;
            picture.pivot = new Vector2(0f, 0.5f);
            picture.anchoredPosition = Vector2.zero;
        }

        void LateUpdate()
        {
            var gm = GameManager.Instance;
            bool show = view.mode == ViewMode.Map && gm != null && gm.InGame;
            canvas.enabled = show;
            if (show != showing)
            {
                showing = show;
                GraphicsOptions.HideSky(show);
            }
            if (!show) return;
            picture.sizeDelta = new Vector2((float)Screen.height * Width / Height, 0f);
            Draw(gm);
            texture.SetPixels32(pixels);
            texture.Apply();
        }

        void Draw(GameManager gm)
        {
            System.Array.Clear(pixels, 0, pixels.Length);
            Color frame = SpectrePalette.Colour(FrameEntry);
            for (int x = FrameLeft; x <= FrameRight; x++)
            {
                Point(x, FrameTop, frame);
                Point(x, FrameBottom, frame);
            }
            for (int y = FrameTop; y <= FrameBottom; y++)
            {
                Point(FrameLeft, y, frame);
                Point(FrameRight, y, frame);
            }

            Color Spot(int colour) => SpectrePalette.Colour(colour + RampStep);
            foreach (var o in gm.Obstacles)
            {
                var obstacle = o != null ? o.GetComponent<ObstacleBehaviour>() : null;
                if (obstacle != null) Object(o.transform.position, Spot(BuiltinShapes.TypeColour(obstacle.type)));
            }
            foreach (var d in gm.Dumps) if (d != null) Object(d.transform.position, Spot(d.Ramp));
            foreach (var r in gm.Robots)
                if (r != null && !r.IsDead) Object(r.transform.position, Spot(BuiltinShapes.TypeColour(r.GetComponent<EnemyBase>().matrixType)));
            foreach (var f in gm.Flags) if (f != null) Object(f.transform.position, SpectrePalette.Colour(FlagEntry));
            gm.GetComponent<RobotDirector>().Shots.ForEachShot((position, colour) => Object(position, Spot(colour)));
            Object(gm.player.transform.position, Spot(BuiltinShapes.TypeColour(PlayerType)));
        }

        // The original's axes are the grid's, swapped and negated (_LOAD_WRLD)
        void Object(Vector3 world, Color colour) =>
            Point(CentreX + Mathf.FloorToInt(world.z / GridPerPixel), CentreY + Mathf.FloorToInt(world.x / GridPerPixel), colour);

        void Point(int x, int y, Color colour)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height) pixels[(Height - 1 - y) * Width + x] = colour;
        }
    }
}
