using UnityEngine;
using UnityEngine.UI;

namespace SpectreEX.Menu
{
    // DSEG14 lettering drawn as true pixel art: each character lights the same segments it does in
    // DSEG14 Classic (sampled from the font by Tools/), but every segment is drawn as exact pixel
    // blocks on a 7x11 grid and magnified by a whole number. So every stroke is the same width and
    // length at any scale - which rasterising the TrueType font can't guarantee at small sizes.
    [RequireComponent(typeof(CanvasRenderer))]
    public class PixelSegmentText : MaskableGraphic
    {
        public const int GlyphW = 7, GlyphH = 11, Gap = 2, LineGap = 2;

        [SerializeField] string text = "";
        public int pixelScale = 1;
        public TextAnchor alignment = TextAnchor.MiddleLeft;

        public string Text
        {
            get => text;
            set { if (text != value) { text = value ?? ""; SetVerticesDirty(); } }
        }

        public int PixelScale
        {
            get => pixelScale;
            set { if (pixelScale != value) { pixelScale = Mathf.Max(1, value); SetVerticesDirty(); } }
        }

        // Segment bits, in DSEG14's layout
        const int A = 1 << 0, B = 1 << 1, C = 1 << 2, D = 1 << 3, E = 1 << 4, F = 1 << 5, G1 = 1 << 6, G2 = 1 << 7,
                  H = 1 << 8, I = 1 << 9, J = 1 << 10, K = 1 << 11, L = 1 << 12, M = 1 << 13;

        // Which segments each character lights in DSEG14 Classic
        static int Mask(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case '0': return A | B | C | D | E | F | J | K;
                case '1': return B | C;
                case '2': return A | B | D | E | G1 | G2;
                case '3': return A | B | C | D | G1 | G2;
                case '4': return B | C | F | G1 | G2;
                case '5': return A | C | D | F | G1 | G2;
                case '6': return A | C | D | E | F | G1 | G2;
                case '7': return A | B | C | F;
                case '8': return A | B | C | D | E | F | G1 | G2;
                case '9': return A | B | C | D | F | G1 | G2;
                case 'A': return A | B | C | E | F | G1 | G2;
                case 'B': return A | B | C | D | G2 | I | L;
                case 'C': return A | D | E | F;
                case 'D': return A | B | C | D | I | L;
                case 'E': return A | D | E | F | G1 | G2;
                case 'F': return A | E | F | G1 | G2;
                case 'G': return A | C | D | E | F | G2;
                case 'H': return B | C | E | F | G1 | G2;
                case 'I': return A | D | I | L;
                case 'J': return B | C | D | E;
                case 'K': return E | F | G1 | J | M;
                case 'L': return D | E | F;
                case 'M': return B | C | E | F | H | J | L;
                case 'N': return B | C | E | F | H | M;
                case 'O': return A | B | C | D | E | F;
                case 'P': return A | B | E | F | G1 | G2;
                case 'Q': return A | B | C | D | E | F | M;
                case 'R': return A | B | E | F | G1 | G2 | M;
                case 'S': return A | C | D | F | G1 | G2 | H | M;
                case 'T': return A | I | L;
                case 'U': return B | C | D | E | F;
                case 'V': return E | F | J | K;
                case 'W': return B | C | E | F | I | K | M;
                case 'X': return H | J | K | M;
                case 'Y': return H | J | L;
                case 'Z': return A | D | J | K;
                case '-': return G1 | G2;
                case '+': return G1 | G2 | I | L;
                case '*': return G1 | G2 | H | I | J | K | L | M;
                case '/': return J | K;
                case '\\': return H | M;
                case '\'': return I;
                case '"': return F | I;
                case '?': return A | B | F | G2 | L;
                case '=': return D | G1 | G2;
                case '<': return D | J | K;
                case '>': return D | H | M;
                case '(': return J | M;
                case ')': return H | K;
                case '_': return D;
                case '|': return I | L;
                case '$': return A | C | D | F | G1 | G2 | I | L;
                case '%': return C | F | G1 | G2 | H | J | K | M;
                case '&': return A | C | D | H | J | K | M;
                case '@': return A | B | C | D | E | F | G2 | L;
                case '^': return F | H;
                default: return 0;   // space, '!' (DSEG's full-width blank) and anything unsupported
            }
        }

        // Pixel rectangles (x, y, w, h) for each segment on the 7x11 grid, y down from the top.
        // Corners are left empty so segments stay separate, like an LED display.
        static readonly RectInt[][] SegmentPixels =
        {
            new[] { new RectInt(1, 0, 5, 1) },                                                      // A
            new[] { new RectInt(6, 1, 1, 4) },                                                      // B
            new[] { new RectInt(6, 6, 1, 4) },                                                      // C
            new[] { new RectInt(1, 10, 5, 1) },                                                     // D
            new[] { new RectInt(0, 6, 1, 4) },                                                      // E
            new[] { new RectInt(0, 1, 1, 4) },                                                      // F
            new[] { new RectInt(1, 5, 2, 1) },                                                      // G1
            new[] { new RectInt(4, 5, 2, 1) },                                                      // G2
            new[] { new RectInt(1, 1, 1, 2), new RectInt(2, 3, 1, 2) },                             // H
            new[] { new RectInt(3, 1, 1, 4) },                                                      // I
            new[] { new RectInt(5, 1, 1, 2), new RectInt(4, 3, 1, 2) },                             // J
            new[] { new RectInt(2, 6, 1, 2), new RectInt(1, 8, 1, 2) },                             // K
            new[] { new RectInt(3, 6, 1, 4) },                                                      // L
            new[] { new RectInt(4, 6, 1, 2), new RectInt(5, 8, 1, 2) },                             // M
        };

        // Narrow punctuation drawn directly (DSEG's dots sit outside the 14 segments)
        static RectInt[] Punctuation(char c, out int width)
        {
            switch (c)
            {
                case '.': width = 1; return new[] { new RectInt(0, 10, 1, 1) };
                case ',': width = 2; return new[] { new RectInt(1, 9, 1, 1), new RectInt(0, 10, 1, 1) };
                case ':': width = 1; return new[] { new RectInt(0, 3, 1, 1), new RectInt(0, 7, 1, 1) };
                case ';': width = 2; return new[] { new RectInt(1, 3, 1, 1), new RectInt(1, 7, 1, 1), new RectInt(0, 8, 1, 1) };
                default: width = GlyphW; return null;
            }
        }

        static int CharWidth(char c)
        {
            Punctuation(c, out int w);
            return w;
        }

        // Width of one line in 1x pixels
        public static int LineWidth(string line)
        {
            int w = 0;
            for (int i = 0; i < line.Length; i++) w += CharWidth(line[i]) + (i < line.Length - 1 ? Gap : 0);
            return w;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (string.IsNullOrEmpty(text)) return;

            int s = Mathf.Max(1, pixelScale);
            var lines = text.Split('\n');
            int blockH = lines.Length * GlyphH + (lines.Length - 1) * LineGap;
            Rect r = rectTransform.rect;

            // Vertical placement, snapped to whole pixels
            float top = alignment switch
            {
                TextAnchor.UpperLeft or TextAnchor.UpperCenter or TextAnchor.UpperRight => r.yMax,
                TextAnchor.LowerLeft or TextAnchor.LowerCenter or TextAnchor.LowerRight => r.yMin + blockH * s,
                _ => r.center.y + blockH * s * 0.5f,
            };
            top = Mathf.Round(top);

            Color32 col = color;
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                int w = LineWidth(line) * s;
                float left = alignment switch
                {
                    TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter => r.center.x - w * 0.5f,
                    TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight => r.xMax - w,
                    _ => r.xMin,
                };
                float x = Mathf.Round(left);
                float y = top - li * (GlyphH + LineGap) * s;

                foreach (char c in line)
                {
                    var punct = Punctuation(c, out int cw);
                    if (punct != null)
                        foreach (var p in punct) Block(vh, x, y, p, s, col);
                    else
                    {
                        int mask = Mask(c);
                        for (int bit = 0; bit < SegmentPixels.Length; bit++)
                            if ((mask & (1 << bit)) != 0)
                                foreach (var p in SegmentPixels[bit]) Block(vh, x, y, p, s, col);
                    }
                    x += (cw + Gap) * s;
                }
            }
        }

        static void Block(VertexHelper vh, float originX, float originTop, RectInt p, int s, Color32 col)
        {
            float x0 = originX + p.x * s, x1 = x0 + p.width * s;
            float y1 = originTop - p.y * s, y0 = y1 - p.height * s;
            int v = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), col, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), col, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), col, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), col, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v, v + 2, v + 3);
        }
    }
}
