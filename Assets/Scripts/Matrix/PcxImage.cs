using UnityEngine;

namespace SpectreEX.Matrix
{
    // Reads the original's 8-bit PCX pictures (a matrix's .BIG and .PCX) into a texture
    public static class PcxImage
    {
        public static Texture2D Load(byte[] d)
        {
            int w = d[8] + (d[9] << 8) - d[4] - (d[5] << 8) + 1;
            int h = d[10] + (d[11] << 8) - d[6] - (d[7] << 8) + 1;
            int stride = d[66] + (d[67] << 8);
            int palette = d.Length - 768;
            var pixels = new Color32[w * h];
            int src = 128;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < stride;)
                {
                    int value = d[src++], run = 1;
                    if (value >= 0xC0) { run = value & 0x3F; value = d[src++]; }
                    for (; run > 0; run--, x++)
                        if (x < w)
                            pixels[(h - 1 - y) * w + x] = new Color32(d[palette + value * 3], d[palette + value * 3 + 1], d[palette + value * 3 + 2], 255);
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
