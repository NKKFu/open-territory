using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class CitySunRays : MonoBehaviour
    {
        static readonly int SunPosition = Shader.PropertyToID("_SunPosition");
        static readonly int RayColor = Shader.PropertyToID("_RayColor");
        static readonly int BlurStep = Shader.PropertyToID("_BlurStep");
        static readonly int RayTexture = Shader.PropertyToID("_RayTexture");
        static readonly int CloudProjection = Shader.PropertyToID("_CitySunViewProjection");
        readonly Plane[] frustum = new Plane[6];
        Camera view;
        Material material;
        CommandBuffer cloudOcclusion;
        CityAtmosphere atmosphere;
        bool requestedDepth;
        public float ActiveStrength { get; private set; }
        public int BufferWidth { get; private set; }
        public int CloudDraws { get; private set; }

        void OnEnable()
        {
            view = GetComponent<Camera>();
            var shader = Resources.Load<Shader>("Shaders/CitySunRays");
            if (!SystemInfo.supportsImageEffects || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth) ||
                !shader || !shader.isSupported) { enabled = false; return; }
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            cloudOcclusion = new CommandBuffer { name = "Nuvens • oclusão dos raios solares" };
            CityCameraDepth.Acquire(view); requestedDepth = true;
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            ActiveStrength = 0; BufferWidth = 0; CloudDraws = 0;
            var ctx = CityContext.Instance;
            if (!material || !ctx || !ctx.Ready || ctx.Camera != view || view.orthographic ||
                ctx.Systems.Sky == null || !ctx.Systems.Sky.Available)
            { Graphics.Blit(source, destination); return; }
            Vector3 direction = ctx.Systems.Sky.SunDirection;
            Vector3 sun = view.WorldToViewportPoint(view.transform.position + direction * 1000);
            float outside = Mathf.Max(0, Mathf.Max(Mathf.Max(-sun.x, sun.x - 1), Mathf.Max(-sun.y, sun.y - 1)));
            var weather = ctx.Systems.Weather;
            float daylight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.01f, .10f, direction.y)) * (1 - ctx.Night);
            float strength = .72f * daylight * (1 - Mathf.SmoothStep(0, .22f, outside)) *
                Mathf.Lerp(1, .45f, weather.Cloud) * Mathf.Lerp(1, .08f, weather.Rain);
            if (sun.z <= 0 || strength < .001f) { Graphics.Blit(source, destination); return; }
            ActiveStrength = strength;
            float scale = ctx.UI.Quality == 2 ? 1f / 3 : .25f;
            scale = Mathf.Min(scale, (ctx.UI.Quality == 2 ? 640f : 480f) / Mathf.Max(source.width, source.height));
            int width = Mathf.Max(1, Mathf.CeilToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.CeilToInt(source.height * scale));
            BufferWidth = width;
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            var mask = RenderTexture.GetTemporary(width, height, 0, format, RenderTextureReadWrite.Linear);
            var blur = RenderTexture.GetTemporary(width, height, 0, format, RenderTextureReadWrite.Linear);
            mask.filterMode = blur.filterMode = FilterMode.Bilinear;
            mask.wrapMode = blur.wrapMode = TextureWrapMode.Clamp;
            var previous = RenderTexture.active;
            try
            {
                material.SetVector(SunPosition, new Vector4(sun.x, sun.y, (float)source.width / source.height, .23f));
                float warmth = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.02f, .42f, direction.y));
                var color = Color.Lerp(new Color(1, .91f, .67f), new Color(1, .58f, .28f), warmth);
                color.a = strength; material.SetColor(RayColor, color);
                Graphics.Blit(source, mask, material, 0);
                // Transparent cloud volumes do not belong to Unity's opaque
                // depth texture. Draw only those existing meshes into the mask.
                if (!atmosphere) atmosphere = FindFirstObjectByType<CityAtmosphere>();
                if (atmosphere && atmosphere.gameObject.activeInHierarchy)
                {
                    GeometryUtility.CalculateFrustumPlanes(view, frustum);
                    cloudOcclusion.Clear(); cloudOcclusion.SetRenderTarget(mask);
                    cloudOcclusion.SetViewport(new Rect(0, 0, width, height));
                    cloudOcclusion.SetGlobalMatrix(CloudProjection, GL.GetGPUProjectionMatrix(view.projectionMatrix, true) * view.worldToCameraMatrix);
                    CloudDraws = atmosphere.DrawSunOccluders(cloudOcclusion, view, frustum);
                    Graphics.ExecuteCommandBuffer(cloudOcclusion);
                }
                material.SetFloat(BlurStep, .72f); Graphics.Blit(mask, blur, material, 1);
                material.SetFloat(BlurStep, 1.05f); Graphics.Blit(blur, mask, material, 1);
                material.SetTexture(RayTexture, mask); Graphics.Blit(source, destination, material, 2);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(mask); RenderTexture.ReleaseTemporary(blur);
            }
        }

        void OnDisable()
        {
            if (requestedDepth) CityCameraDepth.Release(view); requestedDepth = false;
            cloudOcclusion?.Release(); cloudOcclusion = null;
            if (material) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); material = null; }
            ActiveStrength = 0; BufferWidth = 0; CloudDraws = 0;
        }
    }
}
