using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenTerritory
{
    // One world-space smoke pool and one bounded mesh serve the whole fleet.
    // Contacts come from the suspension, so effects cannot float behind a jump.
    [DefaultExecutionOrder(150)]
    public sealed class CityVehicleEffects : MonoBehaviour
    {
        public const int MarkCapacity = 2048;
        public const float MarkLifetime = 12;
        sealed class WheelTrail
        {
            public Vector3 point; public float budget, distance, intensity, lastSeen;
            public bool connected;
        }
        readonly Dictionary<CityVehicles.Vehicle, WheelTrail[]> trails = new Dictionary<CityVehicles.Vehicle, WheelTrail[]>();
        readonly Vector3[] vertices = new Vector3[MarkCapacity * 4];
        readonly Color32[] colors = new Color32[MarkCapacity * 4];
        readonly Vector2[] uv = new Vector2[MarkCapacity * 4], births = new Vector2[MarkCapacity * 4];
        CityContext ctx; CityVehicles vehicles; Material smokeMaterial, markMaterial; MeshRenderer markRenderer;
        int cursor, quality = -1; bool meshDirty; uint random = 3907123;
        public ParticleSystem Smoke { get; private set; }
        public Mesh MarkMesh { get; private set; }
        public int LaunchParticles { get; private set; }
        public int BrakeParticles { get; private set; }
        public int SlideParticles { get; private set; }
        public int MarkSegments { get; private set; }
        public int ActiveParticles => Smoke ? Smoke.particleCount : 0;
        public float EffectClock { get; private set; }
        public float LongestSegment { get; private set; }
        static readonly int EffectTime = Shader.PropertyToID("_EffectTime");

        public void Initialize(CityContext context, CityVehicles owner)
        {
            ctx = context; vehicles = owner;
            var smokeShader = Resources.Load<Shader>("Shaders/CityTyreSmoke");
            var markShader = Resources.Load<Shader>("Shaders/CityTyreMarks");
            if (!smokeShader || !markShader) { Debug.LogError("Vehicle effect shaders missing"); enabled = false; return; }
            smokeMaterial = new Material(smokeShader) { name = "Fumaça dos pneus" };
            markMaterial = new Material(markShader) { name = "Borracha no asfalto" };
            markMaterial.SetFloat("_Lifetime", MarkLifetime);
            var cloud = new GameObject("Fumaça e rastros dos pneus"); cloud.transform.SetParent(transform, false);
            Smoke = cloud.AddComponent<ParticleSystem>(); Smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = Smoke.main; main.playOnAwake = false; main.loop = true; main.startSpeed = 0;
            main.startLifetime = 1.4f; main.startSize = .28f; main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.useUnscaledTime = false;
            main.maxParticles = 480; main.gravityModifier = -.025f; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = Smoke.emission; emission.enabled = false; var shape = Smoke.shape; shape.enabled = false;
            var size = Smoke.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .7f), new Keyframe(.2f, 1.7f), new Keyframe(1, 4.4f)));
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.85f, .09f), new GradientAlphaKey(.55f, .5f), new GradientAlphaKey(0, 1) });
            var color = Smoke.colorOverLifetime; color.enabled = true; color.color = new ParticleSystem.MinMaxGradient(fade);
            var renderer = cloud.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = smokeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.maxParticleSize = .16f; ConfigureRenderer(renderer);
            Smoke.Play();

            var marks = new GameObject("Marcas de frenagem e derrapagem"); marks.transform.SetParent(transform, false);
            // Vertices are world positions; keep this renderer at the world origin.
            marks.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            MarkMesh = new Mesh { name = "Pneus • malha circular reutilizada" }; MarkMesh.MarkDynamic();
            int[] indices = new int[MarkCapacity * 6];
            for (int i = 0; i < MarkCapacity; i++)
            {
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
                indices[t + 3] = v + 1; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            MarkMesh.SetVertices(vertices); MarkMesh.SetColors(colors); MarkMesh.SetUVs(0, uv); MarkMesh.SetUVs(1, births); MarkMesh.triangles = indices;
            marks.AddComponent<MeshFilter>().sharedMesh = MarkMesh; markRenderer = marks.AddComponent<MeshRenderer>();
            markRenderer.sharedMaterial = markMaterial; ConfigureRenderer(markRenderer); markRenderer.enabled = false;
        }
        static void ConfigureRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        float Range(float low, float high)
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            return Mathf.Lerp(low, high, (random & 0xffffff) / 16777215f);
        }
        WheelTrail[] Tracks(CityVehicles.Vehicle car)
        {
            if (trails.TryGetValue(car, out var result)) return result;
            result = new WheelTrail[car.Data.wheels.Length];
            for (int i = 0; i < result.Length; i++) result[i] = new WheelTrail();
            trails.Add(car, result); return result;
        }
        void LateUpdate()
        {
            if (!ctx || !ctx.Ready || ctx.Paused || !Smoke || !markMaterial) return;
            float dt = Mathf.Min(Time.deltaTime, .05f); EffectClock = ctx.Clock;
            markMaterial.SetFloat(EffectTime, EffectClock);
            if (quality != ctx.UI.Quality)
            { quality = ctx.UI.Quality; var main = Smoke.main; main.maxParticles = quality == 0 ? 240 : quality == 2 ? 640 : 480; }
            foreach (var car in vehicles.Fleet)
            {
                if (!car.Root || car.Data.kind == "bike") continue;
                var wheels = Tracks(car); bool human = car.Controlled || car == vehicles.RemoteVehicle;
                bool near = human || (car.Position - ctx.Camera.transform.position).sqrMagnitude < 75 * 75;
                float speed = Mathf.Abs(car.Speed);
                for (int i = 0; i < wheels.Length; i++)
                {
                    var track = wheels[i];
                    if (!near || !car.WheelGrounded[i]) { track.connected = false; track.budget = 0; continue; }
                    if (ctx.Clock - track.lastSeen > .12f) track.connected = false;
                    track.lastSeen = ctx.Clock;
                    var wheel = car.Data.wheels[i]; Vector3 point = car.ContactPoints[i], normal = car.ContactNormals[i];
                    if (normal.y < .4f) { track.connected = false; continue; }
                    Vector3 wheelSide = Quaternion.AngleAxis(wheel.front ? car.Motion.Steering * Mathf.Rad2Deg : 0, car.Root.up) * car.Root.right;
                    float sideways = Mathf.Abs(Vector3.Dot(car.Body.GetPointVelocity(point), wheelSide)) / Mathf.Max(2.5f, speed);
                    float driven = wheel.front ? car.Data.frontDrive : 1 - car.Data.frontDrive;
                    float launch = human ? Mathf.Clamp01((car.Motion.Acceleration - 1.5f) / 6) * car.Motion.Pedal * Mathf.Clamp01((15 - speed) / 11) : 0;
                    launch = Mathf.Max(launch, human ? car.Motion.Wheelspin : Mathf.Clamp01((car.Motion.Wheelspin - .25f) * 1.3f)) * Mathf.Clamp01(driven * 2) * (1 - car.Motion.Handbrake);
                    float braking = Mathf.Clamp01((car.Motion.BrakePressure - .35f) / .65f) * Mathf.Clamp01((speed - 2) / 9);
                    float slide = Mathf.Clamp01((sideways - .085f) * 3.2f);
                    if (!wheel.front) slide = Mathf.Max(slide, car.Motion.Handbrake * Mathf.Clamp01(speed / 4));
                    float intensity = Mathf.Max(launch, braking * .8f, slide);
                    if (speed < .5f || intensity < .06f) { track.connected = false; track.budget = 0; continue; }
                    float density = quality == 0 ? .6f : quality == 2 ? 1.2f : 1;
                    track.budget = Mathf.Min(4, track.budget + dt * intensity * (40 + speed * .65f) * density);
                    while (track.budget >= 1)
                    {
                        track.budget -= 1; EmitSmoke(car, point, normal, intensity);
                        if (slide >= launch && slide >= braking * .8f) SlideParticles++;
                        else if (braking * .8f >= launch) BrakeParticles++; else LaunchParticles++;
                    }
                    if (speed > 1.8f && intensity > .14f) Mark(track, point, normal, wheel.width, intensity);
                    else track.connected = false;
                }
            }
            if (meshDirty)
            {
                meshDirty = false; MarkMesh.SetVertices(vertices); MarkMesh.SetColors(colors);
                MarkMesh.SetUVs(0, uv); MarkMesh.SetUVs(1, births); MarkMesh.RecalculateBounds(); markRenderer.enabled = true;
            }
        }
        void EmitSmoke(CityVehicles.Vehicle car, Vector3 point, Vector3 normal, float intensity)
        {
            float wet = ctx.Wetness; Vector3 wind = ctx.Systems ? ctx.Systems.Weather.Wind : Vector3.zero;
            var color = Color.Lerp(new Color(.72f, .70f, .67f, .78f), new Color(.76f, .83f, .87f, .52f), wet);
            color.a *= Mathf.Lerp(.65f, 1, intensity);
            var particle = new ParticleSystem.EmitParams {
                position = point + normal * .16f + car.Root.right * Range(-.045f, .045f),
                velocity = car.Body.linearVelocity * .065f + wind * .12f + normal * Range(.65f, 1.1f) + car.Root.right * Range(-.3f, .3f),
                startSize = Range(.38f, .58f) * Mathf.Lerp(.8f, 1.2f, intensity),
                startLifetime = Range(.9f, 1.55f) * Mathf.Lerp(1, .58f, wet), startColor = color, rotation = Range(0, 360)
            };
            Smoke.Emit(particle, 1);
        }
        void Mark(WheelTrail trail, Vector3 point, Vector3 normal, float width, float intensity)
        {
            point += normal * .018f;
            Vector3 delta = point - trail.point; float distance = delta.magnitude;
            if (!trail.connected || distance > 3 || Mathf.Abs(delta.y) > .7f)
            { trail.point = point; trail.distance = 0; trail.intensity = intensity; trail.connected = true; return; }
            if (distance < .14f) return;
            Vector3 side = Vector3.Cross(normal, delta / distance).normalized * Mathf.Clamp(width * .46f, .065f, .22f);
            int at = cursor * 4; cursor = (cursor + 1) % MarkCapacity;
            vertices[at] = trail.point - side; vertices[at + 1] = trail.point + side;
            vertices[at + 2] = point - side; vertices[at + 3] = point + side;
            float opacity = Mathf.Lerp(.22f, .64f, intensity) * (1 - ctx.Wetness * .35f);
            byte alpha = (byte)(opacity * 255), oldAlpha = (byte)(Mathf.Lerp(.22f, .64f, trail.intensity) * (1 - ctx.Wetness * .35f) * 255);
            colors[at] = colors[at + 1] = new Color32(16, 15, 14, oldAlpha);
            colors[at + 2] = colors[at + 3] = new Color32(16, 15, 14, alpha);
            float nextDistance = trail.distance + distance;
            uv[at] = new Vector2(0, trail.distance); uv[at + 1] = new Vector2(1, trail.distance);
            uv[at + 2] = new Vector2(0, nextDistance); uv[at + 3] = new Vector2(1, nextDistance);
            for (int i = 0; i < 4; i++) births[at + i] = new Vector2(ctx.Clock, 0);
            trail.distance = nextDistance; trail.point = point; trail.intensity = intensity;
            MarkSegments++; LongestSegment = Mathf.Max(LongestSegment, distance); meshDirty = true;
        }
        public void ClearTransientEffects()
        {
            if (Smoke) { Smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); Smoke.Play(); }
            Array.Clear(colors, 0, colors.Length); cursor = 0; meshDirty = false;
            if (MarkMesh) MarkMesh.SetColors(colors); if (markRenderer) markRenderer.enabled = false;
            foreach (var pair in trails) foreach (var trail in pair.Value) { trail.connected = false; trail.budget = 0; }
        }
        public void SetPaused(bool paused)
        {
            if(!Smoke)return;
            // Pause immediately, including the frame that opened settings.
            // timeScale alone can leave one final particle update queued.
            if(paused){if(!Smoke.isPaused)Smoke.Pause();}
            else if(Smoke.isPaused)Smoke.Play();
        }
        void OnDestroy()
        { if (smokeMaterial) Destroy(smokeMaterial); if (markMaterial) Destroy(markMaterial); if (MarkMesh) Destroy(MarkMesh); }
    }
}
