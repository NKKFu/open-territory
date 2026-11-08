using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Sources are baked from the actual lamp meshes, not from a building's map AABB.
    public sealed partial class CityLighting : MonoBehaviour
    {
        [Serializable]
        public sealed class Fixture
        {
            public string kind;
            public int node, place = -1;
            public Transform anchor;
            public Vector3 localSurface, localEmitter;
            public Renderer lens;
            public Light source;
            public float intensity;
        }

        public Fixture[] fixtures = Array.Empty<Fixture>();
        [Serializable] public sealed class Room
        {
            public int place;
            public Renderer[] surfaces = Array.Empty<Renderer>();
            public float height = 5.12f;
            [NonSerialized] public float lastLevel = -1, lastDarkness = -1;
        }
        public Room[] rooms = Array.Empty<Room>();
        CityContext ctx;
        CityWindows windows;
        float[] roomLevels;
        int[] fixtureTotals,fixturePresent;
        readonly HashSet<Light> sources = new HashSet<Light>();
        MaterialPropertyBlock emission;

        public void Initialize(CityContext context)
        {
            emission = new MaterialPropertyBlock();
            ctx = context;
            roomLevels = new float[ctx.Data.places.Length];
            fixtureTotals=new int[roomLevels.Length];fixturePresent=new int[roomLevels.Length];
            windows = FindFirstObjectByType<CityWindows>();
            sources.Clear();
            foreach (var fixture in fixtures) if (fixture.source) sources.Add(fixture.source);
            // The donor's pools (including the detached petrol-station light) are obsolete.
            foreach (int id in ctx.Data.streetLightIds) DisableLegacy(id);
            foreach (int id in ctx.Data.shopLightIds) DisableLegacy(id);
            DisableLegacy(ctx.Data.canopyLightId);
        }

        void DisableLegacy(int id)
        {
            var node = ctx.Node(id);
            if (node && node.TryGetComponent<Light>(out var light)) { light.enabled = false; sources.Add(light); }
        }

        public bool Owns(Light light) => sources.Contains(light);
        readonly HashSet<Fixture> brokenFixtures = new HashSet<Fixture>();
        public void BreakFixture(Transform prop)
        {
            foreach(var fixture in fixtures)
                if(fixture.anchor&&(fixture.anchor==prop||fixture.anchor.IsChildOf(prop)))
                {brokenFixtures.Add(fixture);if(fixture.source)fixture.source.enabled=false;}
        }

        public static Vector3 LocalPoint(PlaceData place, Vector3 point) =>
            Quaternion.Euler(0, -place.yaw * Mathf.Rad2Deg, 0) * (point - new Vector3(place.x, place.elevation, place.z));

        public static bool Inside(PlaceData place, Vector3 point)
        {
            Vector3 local = LocalPoint(place, point);
            // w/d are the *world-aligned* bounds; lotWidth/Depth are the local floor plan.
            float width = place.lotWidth > 0 ? place.lotWidth : place.w;
            float depth = place.lotDepth > 0 ? place.lotDepth : place.d;
            return Mathf.Abs(local.x) < width * .5f - .2f && Mathf.Abs(local.z) < depth * .5f - .2f
                && local.y > -.3f && local.y < 5;
        }

        public static float DistanceTo(PlaceData place, Vector3 point)
        {
            Vector3 p = LocalPoint(place, point);
            float x = Mathf.Max(0, Mathf.Abs(p.x) - (place.lotWidth > 0 ? place.lotWidth : place.w) * .5f);
            float z = Mathf.Max(0, Mathf.Abs(p.z) - (place.lotDepth > 0 ? place.lotDepth : place.d) * .5f);
            float y = Mathf.Max(0, Mathf.Max(-p.y, p.y - 5.2f));
            return new Vector3(x, y, z).magnitude;
        }

        static float Fade(float distance, float start, float end) => 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, end, distance));

        public static float RoomLevel(PlaceData place, Vector3 player, Vector3 camera, float darkness)
        {
            float distance = Mathf.Min(DistanceTo(place, player), DistanceTo(place, camera));
            return Mathf.Max(Mathf.Clamp01(darkness), Fade(distance, 24, 44)) * Fade(distance, 44, 72);
        }

        public void Tick(float darkness)
        {
            if (!ctx || !ctx.Camera) return;
            lastHackDarkness = darkness; ExpireBlackouts();
            Vector3 focus = ctx.Player.position, camera = ctx.Camera.transform.position;
            for (int i = 0; i < roomLevels.Length; i++) roomLevels[i] = RoomCircuitOff(i) ? 0 : RoomLevel(ctx.Data.places[i], focus, camera, darkness);
            Array.Clear(fixtureTotals,0,fixtureTotals.Length);Array.Clear(fixturePresent,0,fixturePresent.Length);
            foreach(var fixture in fixtures)if(fixture.place>=0&&fixture.place<roomLevels.Length)
            {fixtureTotals[fixture.place]++;if(fixture.anchor&&fixture.anchor.gameObject.activeInHierarchy&&!brokenFixtures.Contains(fixture))fixturePresent[fixture.place]++;}
            for(int i=0;i<roomLevels.Length;i++)if(fixtureTotals[i]>0)roomLevels[i]*=(float)fixturePresent[i]/fixtureTotals[i];
            foreach (var fixture in fixtures)
            {
                if (!fixture.source || !fixture.anchor) continue;
                if(brokenFixtures.Contains(fixture)){fixture.source.enabled=false;continue;}
                bool interior = fixture.place >= 0 && fixture.place < ctx.Data.places.Length;
                float distance = Mathf.Min(Vector3.Distance(focus, fixture.source.transform.position), Vector3.Distance(camera, fixture.source.transform.position));
                float level = interior ? roomLevels[fixture.place] : Mathf.Max(Mathf.Clamp01(darkness), Fade(distance, 40, 65)) * Fade(distance, 65, 95);
                if (FixtureCircuitOff(fixture)) level = 0;
                bool present = fixture.anchor.gameObject.activeInHierarchy && (!fixture.lens || fixture.lens.enabled);
                fixture.source.intensity = fixture.intensity * level * Mathf.Lerp(.45f, 1, Mathf.Clamp01(darkness));
                fixture.source.enabled = present && fixture.source.intensity > .015f;
                // The bulb remains visible from afar; only its real-time illumination fades.
                if (fixture.lens)
                {
                    fixture.lens.GetPropertyBlock(emission);
                    emission.SetColor("_EmissionColor", fixture.source.color * level * (interior ? 1.35f : 1.6f));
                    fixture.lens.SetPropertyBlock(emission);
                }
            }
            foreach (var room in rooms)
            {
                float level=roomLevels[room.place];
                if(Mathf.Abs(room.lastLevel-level)<.015f&&Mathf.Abs(room.lastDarkness-darkness)<.025f)continue;
                room.lastLevel=level;room.lastDarkness=darkness;
                var place = ctx.Data.places[room.place];
                var rotation = Quaternion.Euler(0, place.yaw * Mathf.Rad2Deg, 0);
                foreach (var surface in room.surfaces)
                {
                    if (!surface) continue;
                    surface.GetPropertyBlock(emission);
                    // Analytic first bounce stays inside this room's rotated walls and ceiling.
                    emission.SetVector("_RoomOrigin", new Vector4(place.x, place.elevation, place.z, 0));
                    emission.SetVector("_RoomRight", rotation * Vector3.right);
                    emission.SetVector("_RoomForward", rotation * Vector3.forward);
                    emission.SetVector("_RoomSize", new Vector4((place.lotWidth > 0 ? place.lotWidth : place.w) * .5f,
                        (place.lotDepth > 0 ? place.lotDepth : place.d) * .5f, room.height, 1));
                    emission.SetColor("_RoomLight", new Color(1, .91f, .77f) * (roomLevels[room.place] * .20f * Mathf.Lerp(.65f, 1, Mathf.Clamp01(darkness))));
                    surface.SetPropertyBlock(emission);
                }
            }
            if (windows) windows.Tick(roomLevels);
        }
    }
}
