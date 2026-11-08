using UnityEngine;

namespace OpenTerritory
{
    // Built-in only: one half-resolution depth pass and a bilateral composite.
    // No normal buffer, noise texture, history, or per-frame managed allocations.
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class CityAmbientOcclusion : MonoBehaviour
    {
        const float Radius = .65f, Intensity = 2.2f, MaxDistance = 55f;
        static readonly int Projection = Shader.PropertyToID("_AOProjection");
        static readonly int Parameters = Shader.PropertyToID("_AOParameters");
        static readonly int BufferSize = Shader.PropertyToID("_AOBufferSize");
        static readonly int Buffer = Shader.PropertyToID("_AOTexture");
        Camera view;
        Material material;
        bool requestedDepth;

        void OnEnable()
        {
            view = GetComponent<Camera>();
            var shader = Resources.Load<Shader>("Shaders/CityAmbientOcclusion");
            if (!SystemInfo.supportsImageEffects || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf) ||
                !shader || !shader.isSupported)
            {
                enabled = false;
                return;
            }
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            CityCameraDepth.Acquire(view); requestedDepth = true;
        }

        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!material || view.orthographic)
            {
                Graphics.Blit(source, destination);
                return;
            }
            // Cap the working buffer at 960 px even on a 4K display.
            float scale = Mathf.Min(.5f, 960f / Mathf.Max(source.width, source.height));
            int width = Mathf.Max(1, Mathf.CeilToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.CeilToInt(source.height * scale));
            var ao = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
            ao.filterMode = FilterMode.Point;
            ao.wrapMode = TextureWrapMode.Clamp;
            var projection = view.projectionMatrix;
            material.SetVector(Projection, new Vector4(projection.m00, projection.m11, 1 / projection.m00, 1 / projection.m11));
            material.SetVector(Parameters, new Vector4(Radius, Intensity, MaxDistance, .35f));
            material.SetVector(BufferSize, new Vector4(1f / width, 1f / height, width, height));
            try
            {
                Graphics.Blit(source, ao, material, 0);
                material.SetTexture(Buffer, ao);
                Graphics.Blit(source, destination, material, 1);
            }
            finally { RenderTexture.ReleaseTemporary(ao); }
        }

        void OnDisable()
        {
            // Release only the depth flag requested by this effect, retaining
            // pre-existing depth, depth-normals and motion-vector requests.
            if (requestedDepth) CityCameraDepth.Release(view);
            requestedDepth = false;
            if (material)
            {
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
                material = null;
            }
        }
    }
}
