using UnityEngine;

namespace OpenTerritory
{
    // Global textures let the existing shared city materials retain their
    // references and pigments; resources are included in every player build.
    public static class CityTerrainMaterial
    {
        const string Root = "Environment/Terrain/";
        public const float NormalStrength = .65f;
        static Texture2D colorTexture, normalTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Bind()
        {
            colorTexture = Resources.Load<Texture2D>(Root + "GrassColor");
            normalTexture = Resources.Load<Texture2D>(Root + "GrassNormal");
            Shader.SetGlobalFloat("_CityGrassEnabled", colorTexture && normalTexture ? 1 : 0);
            Shader.SetGlobalFloat("_CityGrassNormalStrength", NormalStrength);
            if (colorTexture) Shader.SetGlobalTexture("_CityGrassColor", colorTexture);
            if (normalTexture) Shader.SetGlobalTexture("_CityGrassNormal", normalTexture);
        }
    }
}
