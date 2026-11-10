using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpectreEX.Matrix
{
    // The textures some faces use: face colours 6000-6015 name a PCX in the game's TEXTURES folder,
    // imported to GameData/Textures. Each gets the shape material with the image in place of
    // the palette.
    public static class ShapeTextures
    {
        static readonly Dictionary<int, Texture2D> images = new Dictionary<int, Texture2D>();
        static readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();

        static Texture2D Image(int id)
        {
            if (images.TryGetValue(id, out var tex)) return tex;
            string file = BuiltinShapes.TextureFile(id);
            string path = Path.Combine(GameData.Textures, file);
            try { if (file != "" && File.Exists(path)) tex = PcxImage.Load(File.ReadAllBytes(path)); }
            catch (System.Exception e) { Debug.LogWarning($"Spectre VR-EX: couldn't read texture {path}: {e.Message}"); }
            images[id] = tex;
            return tex;
        }

        public static bool Has(int faceColour) => faceColour >= 6000 && faceColour <= 6015 && Image(faceColour) != null;

        public static Material Material(int id, Material shapes)
        {
            if (materials.TryGetValue(id, out var m) && m != null) return m;
            m = new Material(shapes);
            m.SetTexture("_BaseColorMap", Image(id));
            m.SetTexture("_EmissiveColorMap", Image(id));
            materials[id] = m;
            return m;
        }
    }
}
