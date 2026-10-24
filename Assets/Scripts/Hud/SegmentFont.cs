using System.Collections.Generic;
using UnityEngine;

namespace SpectreEX.Hud
{
    // The thin segmented vector lettering from Spectre VR's HUD ("LIVES", "LEVEL 6",
    // "WATCH OUT FOR SLICERS"). Each glyph is a list of strokes on a unit cell
    // (x 0..1 left to right, y 0..1 bottom to top), drawn as short separated segments.
    public static class SegmentFont
    {
        static readonly Vector2 TL = new Vector2(0, 1), TM = new Vector2(0.5f, 1), TR = new Vector2(1, 1);
        static readonly Vector2 ML = new Vector2(0, 0.5f), C = new Vector2(0.5f, 0.5f), MR = new Vector2(1, 0.5f);
        static readonly Vector2 BL = new Vector2(0, 0), BM = new Vector2(0.5f, 0), BR = new Vector2(1, 0);

        public struct Stroke
        {
            public Vector2 a, b;
            public Stroke(Vector2 a, Vector2 b) { this.a = a; this.b = b; }
        }

        static readonly Dictionary<char, Stroke[]> glyphs = new Dictionary<char, Stroke[]>();

        static void G(char c, params Vector2[] pairs)
        {
            var s = new Stroke[pairs.Length / 2];
            for (int i = 0; i < s.Length; i++) s[i] = new Stroke(pairs[i * 2], pairs[i * 2 + 1]);
            glyphs[c] = s;
        }

        static SegmentFont()
        {
            // Seven-segment style digits, the way the original's numbers read
            G('0', TL, TR, TR, MR, MR, BR, BR, BL, BL, ML, ML, TL);
            G('1', TR, MR, MR, BR);
            G('2', TL, TR, TR, MR, MR, ML, ML, BL, BL, BR);
            G('3', TL, TR, TR, MR, MR, BR, BR, BL, ML, MR);
            G('4', TL, ML, ML, MR, TR, MR, MR, BR);
            G('5', TR, TL, TL, ML, ML, MR, MR, BR, BR, BL);
            G('6', TR, TL, TL, ML, ML, BL, BL, BR, BR, MR, MR, ML);
            G('7', TL, TR, TR, MR, MR, BR);
            G('8', TL, TR, TR, MR, MR, BR, BR, BL, BL, ML, ML, TL, ML, MR);
            G('9', MR, ML, ML, TL, TL, TR, TR, MR, MR, BR, BR, BL);

            G('A', BL, ML, ML, TL, TL, TR, TR, MR, MR, BR, ML, MR);
            G('B', TL, TR, TR, MR, MR, BR, BR, BL, BL, ML, ML, TL, C, MR);
            G('C', TR, TL, TL, ML, ML, BL, BL, BR);
            G('D', TL, TM, TM, MR, MR, BM, BM, BL, BL, ML, ML, TL);
            G('E', TR, TL, TL, ML, ML, BL, BL, BR, ML, C);
            G('F', TR, TL, TL, ML, ML, BL, ML, C);
            G('G', TR, TL, TL, ML, ML, BL, BL, BR, BR, MR, MR, C);
            G('H', TL, ML, ML, BL, TR, MR, MR, BR, ML, MR);
            G('I', TL, TR, TM, C, C, BM, BL, BR);
            G('J', TR, MR, MR, BR, BR, BL, BL, ML);
            G('K', TL, ML, ML, BL, ML, C, C, TR, C, BR);
            G('L', TL, ML, ML, BL, BL, BR);
            G('M', BL, ML, ML, TL, TL, C, C, TR, TR, MR, MR, BR);
            G('N', BL, ML, ML, TL, TL, C, C, BR, TR, MR, MR, BR);
            G('O', TL, TR, TR, MR, MR, BR, BR, BL, BL, ML, ML, TL);
            G('P', BL, ML, ML, TL, TL, TR, TR, MR, MR, ML);
            G('Q', TL, TR, TR, MR, MR, BR, BR, BL, BL, ML, ML, TL, C, BR);
            G('R', BL, ML, ML, TL, TL, TR, TR, MR, MR, ML, C, BR);
            G('S', TR, TL, TL, ML, ML, MR, MR, BR, BR, BL);
            G('T', TL, TR, TM, C, C, BM);
            G('U', TL, ML, ML, BL, BL, BR, BR, MR, MR, TR);
            G('V', TL, BM, BM, TR);
            G('W', TL, ML, ML, BL, BL, C, C, BR, BR, MR, MR, TR);
            G('X', TL, C, C, BR, TR, C, C, BL);
            G('Y', TL, C, TR, C, C, BM);
            G('Z', TL, TR, TR, C, C, BL, BL, BR);

            G('-', ML, MR);
            G('+', ML, MR, new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.2f));
            G(':', new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.28f));
            G('.', new Vector2(0.5f, 0f), new Vector2(0.5f, 0.08f));
            G('!', TM, C, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.08f));
            G('/', BL, TR);
            G('\'', TM, new Vector2(0.5f, 0.78f));
            G('?', TL, TR, TR, MR, MR, C, C, new Vector2(0.5f, 0.25f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.08f));
            G(' ');
        }

        public static Stroke[] Get(char c)
        {
            c = char.ToUpperInvariant(c);
            return glyphs.TryGetValue(c, out var s) ? s : glyphs[' '];
        }
    }
}
