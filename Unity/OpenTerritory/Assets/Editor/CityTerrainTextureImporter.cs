using UnityEditor;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Keep the color and height-derived normal imports reproducible, including
    // when the city is rebuilt from its original geometry.
    sealed class CityTerrainTextureImporter : AssetPostprocessor
    {
        const string Root = "Assets/Resources/Environment/Terrain/";

        void OnPreprocessTexture()
        {
            if (assetPath != Root + "GrassColor.png" && assetPath != Root + "GrassNormal.png") return;
            var texture = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("GrassNormal.png");
            texture.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            texture.convertToNormalmap = normal;
            if (normal) texture.heightmapScale = .20f;
            texture.sRGBTexture = !normal;
            texture.mipmapEnabled = true;
            texture.npotScale = TextureImporterNPOTScale.None;
            texture.maxTextureSize = 2048;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
        }

        [InitializeOnLoadMethod]
        static void BindInEditor() => QueueBind();

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
                if (path.StartsWith(Root) || path == "Assets/Shaders/CitySurface.shader") { QueueBind(); break; }
        }

        static void QueueBind()
        {
            EditorApplication.update -= BindWhenReady;
            EditorApplication.update += BindWhenReady;
        }

        static void BindWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!Resources.Load<Texture2D>("Environment/Terrain/GrassColor") || !Resources.Load<Texture2D>("Environment/Terrain/GrassNormal")) return;
            CityTerrainMaterial.Bind();
            EditorApplication.update -= BindWhenReady;
        }
    }
}
