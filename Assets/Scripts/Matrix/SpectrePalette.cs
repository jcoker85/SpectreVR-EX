using UnityEngine;

namespace SpectreEX.Matrix
{
    // The original's 256 colours (_MAKE_COLORS, _LOADUP_PALLET) as a strip texture that shape meshes
    // index by their UVs. Ramps of 16 run dim to bright; two change with the level: the world objects'
    // colour at 0x50 and the sky's at 0x70. The original shades a face by moving along its ramp; here
    // HDRP's lights do that, so faces take a fixed point on the ramp.
    public static class SpectrePalette
    {
        const int Lit = 10;   // the point on a ramp a shaded face uses

        static Texture2D texture;
        static readonly Color32[] colours = new Color32[256];

        // Which palette entry a face shows: 0, 255 and textures take the object's own colour
        public static int Entry(int faceColour, int objectColour)
        {
            if (faceColour == 0 || faceColour == 0xFF || faceColour > 0xFF) return Shaded(objectColour);
            return (faceColour & 0xF) == 0 ? Shaded(faceColour) : faceColour;
        }

        static int Shaded(int ramp) => ramp >= 0xFE ? 0xB0 + Lit : (ramp & 0xF0) + Lit;

        public static Vector2 Uv(int entry) => new Vector2((entry + 0.5f) / 256f, 0.5f);

        // A level's colours on the shape material. level is the original's, from 0.
        public static void Apply(Material material, int level)
        {
            if (material == null) return;
            Build(level);
            if (texture == null)
                texture = new Texture2D(256, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(colours);
            texture.Apply();
            material.SetTexture("_BaseColorMap", texture);
            material.SetTexture("_EmissiveColorMap", texture);
        }

        public static Color Sky(int level) => BuiltinShapes.SkyColour(level);

        static void Build(int level)
        {
            for (int i = 0; i < 16; i++)
            {
                byte ramp = (byte)(0x80 + i * 8);
                colours[i] = Grey(i * 16);
                colours[0x10 + i] = new Color32(ramp, 0, 0, 255);
                colours[0x20 + i] = new Color32(0, ramp, 0, 255);
                colours[0x30 + i] = new Color32(0, 0, ramp, 255);
                colours[0x40 + i] = new Color32(ramp, (byte)(ramp >> 1), 0, 255);
                colours[0x60 + i] = new Color32(0, ramp, ramp, 255);
                colours[0x90 + i] = new Color32(ramp, 0, ramp, 255);
                colours[0xA0 + i] = new Color32(ramp, (byte)(ramp >> 2), (byte)(ramp >> 1), 255);
                colours[0xB0 + i] = Grey(ramp);
            }
            for (int i = 0; i < 64; i++) colours[0xC0 + i] = Grey(i * 4);

            Color32 obj = BuiltinShapes.ObjectColour(level);
            for (int i = 0; i < 16; i++)
            {
                int k = (i * 20 >> 4) + 12;
                colours[0x50 + i] = new Color32((byte)(obj.r * k / 32), (byte)(obj.g * k / 32), (byte)(obj.b * k / 32), 255);
            }
            Color32 sky = BuiltinShapes.SkyColour(level);
            for (int i = 0; i < 32; i++)
                colours[0x70 + i] = new Color32((byte)(sky.r * i / 48), (byte)(sky.g * i / 48), (byte)(sky.b * i / 48), 255);
        }

        static Color32 Grey(int v) => new Color32((byte)v, (byte)v, (byte)v, 255);
    }
}
