using UnityEngine;

namespace OpenTerritory
{
    // A skybox keeps the celestial bodies at infinity, behind clouds and solid
    // geometry, without adding colliders or depending on the camera far plane.
    public sealed class CitySky
    {
        readonly Camera camera;
        readonly Light light;
        readonly Material previousSky;
        readonly Light previousSun;
        readonly CameraClearFlags previousClear;
        Material material;
        float night, veil;
        public Vector3 SunDirection { get; private set; }
        public Vector3 MoonDirection => -SunDirection;
        public bool Available => material;

        public CitySky(Camera camera, Light light)
        {
            this.camera = camera; this.light = light;
            previousSky = RenderSettings.skybox; previousSun = RenderSettings.sun;
            previousClear = camera.clearFlags;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var shader = Resources.Load<Shader>("Shaders/CitySky");
            if (!shader || !shader.isSupported) { Debug.LogError("City sky shader missing or unsupported"); return; }
            material = new Material(shader) { name = "Sol e lua de Baía do Sol" };
            RenderSettings.skybox = material; RenderSettings.sun = light;
            camera.clearFlags = CameraClearFlags.Skybox;
        }

        public static Vector3 SunAt(float clock)
        {
            float angle = Mathf.Repeat(clock / 300 + .19f, 1) * Mathf.PI * 2;
            float height = Mathf.Sin(angle);
            // A 42-degree arc fits within the existing upward camera orbit.
            return Quaternion.Euler(0, -34, 0) * new Vector3(Mathf.Cos(angle), height * .6691306f, height * .7431448f);
        }

        public void SetWeather(Color sky, Color horizon, float darkness, CityWeather weather)
        {
            night = darkness;
            veil = Mathf.Clamp01(weather.Fog * .72f + weather.Cloud * .38f + weather.Rain * .25f);
            if (!material) return;
            material.SetColor("_SkyColor", sky);
            material.SetColor("_HorizonColor", horizon);
            material.SetFloat("_Veil", veil);
            material.SetFloat("_Night", night);
        }

        public void Advance(float clock)
        {
            SunDirection = SunAt(clock);
            // Reuse the existing directional source for moonlight at night.
            // The visible disc and the shadows always use the same direction.
            Vector3 source = SunDirection.y >= 0 ? SunDirection : MoonDirection;
            if (light) light.transform.rotation = Quaternion.LookRotation(-source, Vector3.up);
            if (!material) return;
            float warmth = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .38f, SunDirection.y));
            Color sun = Color.Lerp(new Color(1, .96f, .72f), new Color(1, .40f, .16f), warmth);
            sun.a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.045f, .035f, SunDirection.y)) * Mathf.Lerp(1, .12f, veil);
            Color moon = new Color(.77f, .86f, .94f,
                Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.045f, .035f, MoonDirection.y)) * night * Mathf.Lerp(1, .16f, veil));
            material.SetVector("_SunDirection", SunDirection);
            material.SetVector("_MoonDirection", MoonDirection);
            material.SetColor("_SunColor", sun); material.SetColor("_MoonColor", moon);
        }

        public void Dispose()
        {
            if (!material) return;
            if (RenderSettings.skybox == material) RenderSettings.skybox = previousSky;
            if (RenderSettings.sun == light) RenderSettings.sun = previousSun;
            if (camera && camera.clearFlags == CameraClearFlags.Skybox) camera.clearFlags = previousClear;
            Object.Destroy(material); material = null;
        }
    }
}
