using UnityEngine;
using UnityEngine.UI;

namespace SpectreEX.Hud
{
    // uGUI graphic that draws text in SegmentFont, LED/vector-display style: every stroke is a
    // quad snapped to whole screen pixels, with an integer stroke width and an integer gap at each
    // end, so all segments of a glyph come out exactly the same thickness and (with the default
    // half-width cell) the same length - like the original's readouts.
    [RequireComponent(typeof(CanvasRenderer))]
    public class SegmentText : MaskableGraphic
    {
        public enum Align { Left, Center, Right }

        [SerializeField] string text = "";
        public float charHeight = 10f;
        public float charWidth = 0.5f;      // relative to height; 0.5 makes horizontal and vertical segments equal
        public float spacing = 0.4f;        // gap between characters, relative to height
        public float thickness = 1f;        // stroke width in layout units (rounded to whole pixels, min 1)
        public float gap = 0.08f;           // segment end shortening, relative to height (min 1 pixel)
        public float glow = 0f;             // alpha of an optional soft glow pass
        public Align align = Align.Left;

        Vector3 lastScale, lastPos;

        public string Text
        {
            get => text;
            set { if (text != value) { text = value; SetVerticesDirty(); } }
        }

        public float PreferredWidth
        {
            get
            {
                int n = text.Length;
                return n == 0 ? 0f : n * charHeight * charWidth + (n - 1) * charHeight * spacing;
            }
        }

        // Pixel snapping depends on where we sit on screen; rebuild when that changes
        void LateUpdate()
        {
            if (transform.lossyScale != lastScale || transform.position != lastPos)
            {
                lastScale = transform.lossyScale;
                lastPos = transform.position;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (string.IsNullOrEmpty(text)) return;

            // Overlay canvas: world units are screen pixels, so lossyScale is pixels per layout unit
            float k = Mathf.Max(0.0001f, transform.lossyScale.x);
            Vector2 origin = transform.position;

            int thickPx = Mathf.Max(1, Mathf.RoundToInt(thickness * k));
            int gapPx = Mathf.Max(1, Mathf.RoundToInt(gap * charHeight * k));
            int hPx = Mathf.Max(4, Mathf.RoundToInt(charHeight * k));
            if (hPx % 2 == 1) hPx++;                              // even height keeps the middle bar on the grid
            int wPx = Mathf.Max(2, Mathf.RoundToInt(hPx * charWidth));
            int advPx = wPx + Mathf.Max(1, Mathf.RoundToInt(charHeight * spacing * k));
            int totalPx = text.Length * advPx - (advPx - wPx);

            Rect r = GetPixelAdjustedRect();
            float leftLocal = align switch
            {
                Align.Center => r.center.x - totalPx / k * 0.5f,
                Align.Right => r.xMax - totalPx / k,
                _ => r.xMin,
            };
            // Snap the text origin to a pixel (offset by half a pixel for odd stroke widths so lines sit on pixel centres)
            float half = (thickPx % 2 == 1) ? 0.5f : 0f;
            float x0 = Mathf.Round(leftLocal * k + origin.x) + half;
            float y0 = Mathf.Round((r.center.y - charHeight * 0.5f) * k + origin.y) + half;

            if (glow > 0f)
            {
                var gc = color; gc.a *= glow;
                Emit(vh, k, origin, x0, y0, wPx, hPx, advPx, gapPx, thickPx * 3, gc);
            }
            Emit(vh, k, origin, x0, y0, wPx, hPx, advPx, gapPx, thickPx, color);
        }

        void Emit(VertexHelper vh, float k, Vector2 origin, float x0, float y0, int wPx, int hPx, int advPx, int gapPx, int thickPx, Color32 col)
        {
            for (int i = 0; i < text.Length; i++)
            {
                float cx = x0 + i * advPx;
                foreach (var s in SegmentFont.Get(text[i]))
                {
                    // Endpoints in whole screen pixels
                    Vector2 a = new Vector2(cx + Mathf.Round(s.a.x * wPx), y0 + Mathf.Round(s.a.y * hPx));
                    Vector2 b = new Vector2(cx + Mathf.Round(s.b.x * wPx), y0 + Mathf.Round(s.b.y * hPx));
                    Vector2 d = b - a;
                    float len = d.magnitude;
                    if (len < 0.001f) continue;
                    Vector2 dir = d / len;
                    float shrink = Mathf.Min(gapPx, len * 0.3f);
                    a += dir * shrink;
                    b -= dir * shrink;
                    Vector2 n = new Vector2(-dir.y, dir.x) * (thickPx * 0.5f);

                    int v = vh.currentVertCount;
                    vh.AddVert(ToLocal(a - n, k, origin), col, Vector2.zero);
                    vh.AddVert(ToLocal(a + n, k, origin), col, Vector2.zero);
                    vh.AddVert(ToLocal(b + n, k, origin), col, Vector2.zero);
                    vh.AddVert(ToLocal(b - n, k, origin), col, Vector2.zero);
                    vh.AddTriangle(v, v + 1, v + 2);
                    vh.AddTriangle(v, v + 2, v + 3);
                }
            }
        }

        static Vector3 ToLocal(Vector2 screenPx, float k, Vector2 origin) => (screenPx - origin) / k;
    }
}
