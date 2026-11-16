// Assets/Editor/ControlsDiagramImport.cs
// Import settings for the controls diagrams (Assets/Resources/ROVR/Controls/, see ControlsDiagram), so
// their small labels stay sharp in the headset: mipmaps with trilinear filtering for reading at an angle,
// high-quality compression, clamped edges and see-through rounded corners.
using UnityEditor;
using UnityEngine;

public class ControlsDiagramImport : AssetPostprocessor
{
    const string Folder = "Assets/Resources/ROVR/Controls/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.maxTextureSize = 2048;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
