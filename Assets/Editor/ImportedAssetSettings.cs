using UnityEditor;

namespace SpectreEX.EditorTools
{
    // The pictures Tools/import_original.py writes into Assets/Imported have no tracked .meta files,
    // so their import settings are set here: exact 1993 pixels, nothing resized or compressed.
    public class ImportedAssetSettings : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Imported/")) return;
            var t = (TextureImporter)assetImporter;
            t.npotScale = TextureImporterNPOTScale.None;
            t.mipmapEnabled = false;
            t.filterMode = UnityEngine.FilterMode.Point;
            t.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
