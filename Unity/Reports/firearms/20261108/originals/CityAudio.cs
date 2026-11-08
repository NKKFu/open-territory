using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    // Offline-authored clips, shared by all emitters. No synthesis or allocations
    // on the audio thread; inaudible engines and distant effects do not play.
    [DefaultExecutionOrder(100)]
    public sealed partial class CityAudio : MonoBehaviour
    {
        [Serializable] public class Entry { public string id, category, source, license; public bool loop; public float seconds, peak, rms, seam; public int channels; }
        [Serializable] public class Bank { public int version, sampleRate; public Entry[] clips; }
        sealed class Voice
        {
            public AudioSource source; public AudioLowPassFilter filter; public AudioReverbFilter room;
            public string kind, category; public float gain, obstruction, nextRay; public int importance;
        }
        sealed class CarVoice
        {
            public CityVehicles.Vehicle car; public Voice engine, rolling, skid, siren; public int gear; public bool indicator;
        }
        sealed class BoatVoice { public CityMarine.Boat boat; public Voice engine, wake; }
        sealed class Walker { public Transform root; public Vector3 previous; public float distance; public int step, contact; }
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, string> categories = new Dictionary<string, string>();
        readonly Dictionary<string, int> variants = new Dictionary<string, int>(), serials = new Dictionary<string, int>();
        readonly Dictionary<string, float> cooldowns = new Dictionary<string, float>();
        readonly List<Voice> shots = new List<Voice>(), loops = new List<Voice>();
        readonly List<CarVoice> cars = new List<CarVoice>();
        readonly List<BoatVoice> boats = new List<BoatVoice>();
        readonly List<Walker> walkers = new List<Walker>();
        readonly HashSet<CityVehicles.Vehicle> registeredCars = new HashSet<CityVehicles.Vehicle>();
        readonly HashSet<Transform> registeredWalkers = new HashSet<Transform>();
        readonly Dictionary<string, int> eventCounts = new Dictionary<string, int>();
        Voice sea, wind, rain, leaves, night, fountain, music, radio, projector, winch, drone;
        Walker player;
        CityContext ctx; PlaceData room;
        Vector3 coastPoint; float nextEnvironment, nextBird, cover, shoreDistance = 1000;
        int radioChannel = -1;
        public Bank Manifest { get; private set; }
        public float ShoreDistance => shoreDistance;
        public float Shelter => cover;
        public bool Indoors => room != null;
        public string LastFootstepSurface { get; private set; }
        public int PlayerSteps { get; private set; }
        public int ResidentSteps { get; private set; }
        public int VoiceCount => shots.Count;
        public int MissingClips { get; private set; }
        public int EventCount(string kind) => eventCounts.TryGetValue(kind, out int n) ? n : 0;
        public AudioSource LoopSource(string kind) { foreach (var v in loops) if (v.kind == kind) return v.source; return null; }
        public AudioSource LastSource { get; private set; }
        public float BusGain(string bus) => !ctx.UI ? 1 : bus == "ambience" ? ctx.UI.AmbienceVolume : bus == "music" ? ctx.UI.MusicVolume : ctx.UI.EffectsVolume;

        public void Initialize(CityContext context)
        {
            ctx = context;
            var manifest = Resources.Load<TextAsset>("Audio/bank");
            if (!manifest) { Debug.LogError("Audio bank missing. Run Unity/Tools/build-audio-bank.py."); return; }
            Manifest = JsonUtility.FromJson<Bank>(manifest.text);
            foreach (var entry in Manifest.clips)
            {
                var clip = Resources.Load<AudioClip>("Audio/" + entry.id);
                if (!clip) { MissingClips++; Debug.LogError("Audio clip missing: " + entry.id); continue; }
                clips[entry.id] = clip; categories[entry.id] = entry.category;
                int dash = entry.id.LastIndexOf('-');
                if (!entry.loop && dash > 0 && int.TryParse(entry.id.Substring(dash + 1), out int variant))
                { string key = entry.id.Substring(0, dash); variants[key] = Mathf.Max(variants.TryGetValue(key, out int n) ? n : 0, variant + 1); }
            }
            sea = Bed("sea"); wind = Bed("wind"); rain = Bed("rain"); leaves = Bed("leaves"); night = Bed("night");
            fountain = Loop("fountain", ctx.Data.fountain, "ambience", 28);
            music = Loop("music-street", ctx.Data.music.position + Vector3.up, "music", 38);
            radio = Loop("radio-0", ReferenceMap.Active ? ReferenceMap.Layout.harborRadio : ctx.Data.harbor.loadPosition, "music", 28);
            projector = Loop("projector", ctx.Data.cinema.projector, "effects", 18);
            winch = Loop("winch", ctx.Data.harbor.loadPosition + Vector3.up * 2, "effects", 35);
            drone = Loop("engine-drone", Vector3.zero, "effects", 42);
            foreach (var car in ctx.Vehicles.Fleet) RegisterCar(car);
            ctx.Vehicles.Spawned += RegisterCar;
            ctx.Residents.Spawned += RegisterWalker;
            foreach (var boat in ctx.Marine.Fleet)
                boats.Add(new BoatVoice { boat = boat, engine = Loop("engine-boat", boat.Root.position, "effects", 100, boat.Root), wake = Loop("wake", boat.Root.position, "effects", 45, boat.Root) });
            player = new Walker { root = ctx.Player, previous = ctx.Player.position };
            foreach (var person in ctx.Residents.People) RegisterWalker(person);
            for (int i = 0; i < 24; i++) shots.Add(Create("Efeito espacial " + (i + 1), false, 32));
            InitializeNpcVoices();
            nextBird = ctx.Clock + 6;
        }
        void RegisterCar(CityVehicles.Vehicle car)
        {
            if (!registeredCars.Add(car)) return;
            var c = new CarVoice { car = car, gear = car.Motion.Gear };
            c.engine = Loop(car.Data.kind == "bike" ? "bicycle" : car.Data.kind == "bus" ? "engine-bus" : "engine-car", car.Position, "effects", 90, car.Root);
            c.rolling = Loop("tire", car.Position, "effects", 65, car.Root);
            c.skid = Loop("skid", car.Position, "effects", 55, car.Root);
            if (car.Data.role == "police" || car.Data.role == "fire") c.siren = Loop("siren", car.Position, "effects", 170, car.Root);
            car.Engine = c.engine.source; car.SirenSource = c.siren?.source; cars.Add(c);
        }
        void RegisterWalker(CityResidents.Resident person)
        {
            if (registeredWalkers.Add(person.Root)) walkers.Add(new Walker { root = person.Root, previous = person.Root.position });
        }
        Voice Create(string label, bool isLoop, float range, Transform parent = null)
        {
            var go = new GameObject(label); go.transform.SetParent(parent ? parent : transform, false);
            var source = go.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = isLoop;
            source.spatialBlend = 1; source.dopplerLevel = .4f; source.spread = 35; source.minDistance = 2.5f; source.maxDistance = range;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(new Keyframe(0, 1), new Keyframe(.08f, .65f), new Keyframe(.3f, .17f), new Keyframe(1, 0)));
            source.volume = 0; source.priority = isLoop ? 150 : 100;
            var filter = go.AddComponent<AudioLowPassFilter>(); filter.cutoffFrequency = 18000; filter.lowpassResonanceQ = 1;
            var reverb = isLoop ? null : go.AddComponent<AudioReverbFilter>(); if (reverb) reverb.enabled = false;
            return new Voice { source = source, filter = filter, room = reverb };
        }
        Voice Loop(string kind, Vector3 at, string bus, float range, Transform parent = null)
        {
            var voice = Create("Som • " + kind, true, range, parent); voice.kind = kind; voice.category = bus;
            if (parent) voice.source.transform.localPosition = Vector3.up * .7f; else voice.source.transform.position = at;
            if (clips.TryGetValue(kind, out var clip)) voice.source.clip = clip;
            loops.Add(voice); return voice;
        }
        Voice Bed(string kind)
        {
            var voice = Loop(kind, Vector3.zero, "ambience", 1); voice.source.spatialBlend = 0; voice.source.dopplerLevel = 0; voice.source.priority = 220; return voice;
        }
        void Gain(Voice voice, float target, float dt, float cutoff = 17000)
        {
            if (voice == null) return;
            bool close = voice.source.spatialBlend < .1f || (voice.source.transform.position - ctx.Camera.transform.position).sqrMagnitude < voice.source.maxDistance * voice.source.maxDistance;
            if (close && voice.source.spatialBlend > .5f && ctx.Clock >= voice.nextRay)
            { voice.obstruction = Obstruction(voice.source.transform.position); voice.nextRay = ctx.Clock + .25f; }
            if (voice.obstruction > 0) { target *= .32f; cutoff = Mathf.Min(cutoff, 1400); }
            target = close ? Mathf.Max(0, target) * BusGain(voice.category) : 0;
            voice.gain = Mathf.Lerp(voice.gain, target, 1 - Mathf.Exp(-dt * 5));
            if (BusGain(voice.category) == 0) voice.gain = 0;
            voice.source.volume = voice.gain;
            voice.filter.cutoffFrequency = Mathf.Lerp(voice.filter.cutoffFrequency, ctx.Motor.Swimming && ctx.Camera.transform.position.y < CityMarine.SeaLevel ? 600 : cutoff, 1 - Mathf.Exp(-dt * 5));
            if (voice.gain > .001f && !voice.source.isPlaying && voice.source.clip && voice.source.gameObject.activeInHierarchy)
            { voice.source.Play(); voice.source.timeSamples = (int)((ctx.Clock * voice.source.clip.frequency) % Mathf.Max(1, voice.source.clip.samples - 1)); }
            else if (voice.gain < .0008f && target < .0008f && voice.source.isPlaying) voice.source.Stop();
        }
        public void TuneRadio(int channel)
        {
            radioChannel = Mathf.Clamp(channel, 0, 2); string key = "radio-" + radioChannel;
            radio.source.Stop(); radio.kind = key; radio.source.clip = clips[key]; Play("radio", radio.source.transform.position, .6f);
        }
        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused || Manifest == null) return;
            float dt = Time.deltaTime;
            Step(player, true);
            foreach (var walker in walkers) Step(walker, false);
            if (ctx.Clock >= nextEnvironment) { SampleEnvironment(); nextEnvironment = ctx.Clock + .22f; }
            float shelter = Mathf.Lerp(1, .22f, cover), vehicle = ctx.Vehicles.Active != null && ctx.Vehicles.Active.Data.kind != "bike" ? .52f : 1;
            float shore = Mathf.Exp(-shoreDistance / 78);
            Gain(sea, (.14f + ctx.Systems.Weather.SeaState * .12f) * shore * shelter * vehicle, dt, Mathf.Lerp(15000, 1400, cover));
            float breeze = CityWindField.Gust(ctx.Player.position, ctx.Clock, ctx.Systems.Weather.Wind);
            Gain(wind, (.075f + ctx.Systems.Weather.WindSpeed * .02f) * breeze * shelter * vehicle, dt, Mathf.Lerp(10000, 1100, cover));
            Gain(rain, ctx.Rain * .42f * Mathf.Lerp(1, .24f, cover) * vehicle, dt, Mathf.Lerp(15000, 1300, cover));
            Gain(leaves, (.045f + ctx.Systems.Weather.WindSpeed * .008f) * breeze * (1 - shore * .45f) * shelter * vehicle, dt, Mathf.Lerp(14000, 1800, cover));
            Gain(night, ctx.Night * .12f * (1 - ctx.Rain) * shelter, dt);
            Gain(fountain, ctx.Systems.FountainOn ? .3f : 0, dt, Indoors ? 1800 : 13000);
            float duck = ctx.Clock < ctx.SpeechUntil && (ctx.Player.position - ctx.SpeechPosition).sqrMagnitude < 15 * 15 ? .62f : 1;
            Gain(music, ctx.Systems.MusicPlaying ? .44f * duck : 0, dt, Indoors ? 2000 : 15000);
            Gain(radio, radioChannel >= 0 ? .4f * duck : 0, dt, Indoors ? 1800 : 12000);
            Gain(projector, ctx.Systems.CinemaPlaying ? .28f : 0, dt);
            Gain(winch, !ctx.Systems.CraneWaiting && Mathf.Abs(ctx.Systems.CraneProgress - lastCrane) > .0001f ? .38f : 0, dt); lastCrane = ctx.Systems.CraneProgress;
            if (ctx.Remote.Drone) drone.source.transform.position = ctx.Remote.DronePosition;
            Gain(drone, ctx.Remote.Drone && ctx.Remote.Drone.gameObject.activeInHierarchy ? .26f : 0, dt);
            UpdateCars(dt); UpdateBoats(dt); UpdateShots(dt); UpdateNpcVoices(dt);
            if (ctx.Clock >= nextBird)
            {
                nextBird = ctx.Clock + UnityEngine.Random.Range(9f, 18f);
                if (cover < .2f && ctx.Rain < .4f && ctx.Night < .55f && shoreDistance < 180)
                    Play("bird", ctx.Camera.transform.position + new Vector3(UnityEngine.Random.Range(-18f, 18f), 8, UnityEngine.Random.Range(-18f, 18f)), .22f);
            }
        }
        float lastCrane;
        void SampleEnvironment()
        {
            var at = ctx.Remote.Active ? ctx.Camera.transform.position : ctx.Player.position + Vector3.up * 1.6f;
            room = null;
            foreach (var place in ctx.Data.places)
                if (place.Contains(at, .15f) && at.y > place.elevation && at.y < place.elevation + 10) { room = place; break; }
            bool roof = Physics.Raycast(at, Vector3.up, 18, (1 << CityContext.WorldLayer) | (1 << CityContext.GroundLayer), QueryTriggerInteraction.Ignore);
            cover = room != null || roof ? 1 : 0;
            shoreDistance = float.MaxValue; var eye = ctx.Camera.transform.position;
            if (ReferenceMap.Active)
            {
                var outline = ReferenceMap.Layout.coast;
                for (int i = 0; i < outline.Length; i++)
                {
                    var a = outline[i]; var b = outline[(i + 1) % outline.Length];
                    var delta = b - a; float t = Mathf.Clamp01(Vector2.Dot(new Vector2(eye.x, eye.z) - a, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
                    var point = a + delta * t; float distance = Vector2.Distance(new Vector2(eye.x, eye.z), point);
                    if (distance < shoreDistance) { shoreDistance = distance; coastPoint = new Vector3(point.x, CityMarine.SeaLevel, point.y); }
                }
            }
            else { shoreDistance = Mathf.Abs(eye.z - CityMarine.Shore); coastPoint = new Vector3(eye.x, CityMarine.SeaLevel, CityMarine.Shore); }
            // Wide stereo wash plus a subtle directional breaking wave from the shore.
            sea.source.panStereo = Mathf.Clamp(Vector3.Dot((coastPoint - eye).normalized, ctx.Camera.transform.right) * .35f, -.35f, .35f);
        }
        void Step(Walker walker, bool isPlayer)
        {
            if (!walker.root || !walker.root.gameObject.activeInHierarchy) { if (walker.root) walker.previous = walker.root.position; walker.distance = 0; return; }
            var at = walker.root.position; float distance = CityPhysics.Flat(at - walker.previous).magnitude; walker.previous = at;
            var rig = ctx.Actors ? isPlayer ? ctx.Actors.PlayerRig : ctx.Actors.Rig(walker.root) : null;
            bool planted = rig != null && rig.FootContacts != walker.contact;
            if (rig != null) walker.contact = rig.FootContacts;
            bool allowed = isPlayer ? ctx.Motor.Grounded && (!ctx.Motor.Busy || ctx.Motor.ParkourApproaching) && !ctx.Motor.Riding && !ctx.Motor.Swimming && !ctx.Motor.SeatHeight.HasValue : (at - ctx.Player.position).sqrMagnitude < 20 * 20;
            if (!allowed || distance > 1.3f || distance < .001f) { if (!allowed || distance > 1.3f) walker.distance = 0; return; }
            walker.distance += distance;
            float speed = isPlayer ? ctx.Motor.Speed : distance / Mathf.Max(.001f, Time.deltaTime);
            if (rig != null ? !planted : walker.distance < (speed > 5 ? 2.15f : isPlayer ? 1.65f : 1.05f)) return;
            walker.distance = 0; walker.step++;
            if (rig != null) at = rig.FootAnchor[rig.LastContact];
            string surface = Surface(at);
            if (isPlayer) { LastFootstepSurface = surface; PlayerSteps++; } else ResidentSteps++;
            Vector3 source = at + Vector3.up * .12f + (rig == null ? walker.root.right * (walker.step % 2 == 0 ? .16f : -.16f) : Vector3.zero);
            Play("footstep-" + surface, source, isPlayer ? speed > 5 ? .65f : .45f : .24f);
        }
        public string Surface(Vector3 at)
        {
            if (ctx.Marine.DockAt(at, .1f) != null) return "wood";
            foreach (var place in ctx.Data.places) if (place.Contains(at, .12f) && Mathf.Abs(at.y - place.elevation) < 2) return place.kind == "house" ? "wood" : "concrete";
            if (ReferenceMap.Active)
            {
                foreach (var road in ReferenceMap.Layout.roads)
                    for (int i = 1; i < road.points.Length; i += 4)
                    {
                        var a = road.points[i - 1]; var b = road.points[Mathf.Min(road.points.Length - 1, i + 3)];
                        var d = CityPhysics.Flat(b - a); float t = Mathf.Clamp01(Vector3.Dot(CityPhysics.Flat(at - a), d) / Mathf.Max(.01f, d.sqrMagnitude));
                        if (CityPhysics.Flat(at - a - d * t).sqrMagnitude < Mathf.Pow(road.width * .5f + 1, 2)) return road.kind == "trail" ? "gravel" : "concrete";
                    }
            }
            return "grass";
        }
        void UpdateCars(float dt)
        {
            foreach (var item in cars)
            {
                if (!item.car.Root.gameObject.activeInHierarchy) continue;
                var car = item.car; bool active = ctx.Vehicles.Active == car; float speed = Mathf.Abs(car.Speed);
                bool running = (active || car.Data.auto || speed > .7f) && car.Root.gameObject.activeInHierarchy;
                float targetPitch = car.Data.kind == "bike" ? .7f + speed * .1f : Mathf.Clamp(.65f + Mathf.Max(0, car.Motion.Rpm - 820) / 2800, .65f, 2.35f);
                item.engine.source.pitch = Mathf.Lerp(item.engine.source.pitch, targetPitch, 1 - Mathf.Exp(-dt * 7));
                item.engine.source.dopplerLevel = active ? 0 : .55f;
                Gain(item.engine, running ? (car.Data.kind == "bike" ? Mathf.Min(.22f, speed * .025f) : .22f + Mathf.Abs(car.Motion.Pedal) * .16f) * (active ? 1 : .7f) : 0, dt, active ? 6000 : 13000);
                Gain(item.rolling, running ? Mathf.Clamp01(speed / 22) * (.18f + ctx.Wetness * .16f) : 0, dt, active ? 5500 : 13000);
                float tyreSlip=Mathf.Max(car.Motion.Slip,car.Motion.Wheelspin*.9f,car.Motion.BrakePressure*.34f);
                Gain(item.skid, speed > 3 && car.Data.kind != "bike" ? Mathf.Clamp01((tyreSlip - .11f) * 2) * .35f : 0, dt);
                Gain(item.siren, car.Siren ? .48f : 0, dt, Indoors ? 1800 : 17000);
                bool tick = (car.Hazards || car.Signal != 0) && Mathf.Repeat(ctx.Clock, 1) < .5f;
                if (active && tick != item.indicator) Play("empty", car.Position, .09f);
                item.indicator = tick;
                item.gear = car.Motion.Gear;
            }
        }
        void UpdateBoats(float dt)
        {
            foreach (var item in boats)
            {
                float speed = Mathf.Abs(item.boat.Speed); bool active = ctx.Marine.ActiveBoat == item.boat;
                item.engine.source.pitch = Mathf.Lerp(item.engine.source.pitch, .7f + speed * .085f, 1 - Mathf.Exp(-dt * 4));
                Gain(item.engine, active ? .24f + Mathf.Clamp01(speed / 10) * .14f : 0, dt);
                Gain(item.wake, speed > .3f ? Mathf.Clamp01(speed / 7) * .4f : 0, dt);
            }
        }
        public void StopHackSound(Vector3 at)
        {
            foreach (var voice in shots)
                if (voice.source && (voice.kind == "phone" || voice.kind == "register" || voice.kind == "film-reel") &&
                    (voice.source.transform.position - at).sqrMagnitude < 1) voice.source.Stop();
        }
        public bool Play(string kind, Vector3 at, float strength = 1)
        {
            if (string.IsNullOrEmpty(kind) || !ctx || ctx.Paused || Manifest == null) return false;
            if (kind == "footstep") kind = "footstep-" + Surface(at);
            string key = kind;
            if (variants.TryGetValue(kind, out int count))
            { int serial = serials.TryGetValue(kind, out int n) ? n : 0; serials[kind] = serial + 1; key = kind + "-" + serial % count; }
            if (!clips.TryGetValue(key, out var clip)) { key = "impact-0"; clip = clips[key]; }
            bool foot = kind.StartsWith("footstep"), loud = kind == "shot" || kind == "carbine" || kind == "siren";
            float range = foot ? 18 : loud ? 140 : kind == "horn" ? 85 : kind == "bird" ? 55 : kind.StartsWith("door") ? 22 : 38;
            float distance = Vector3.Distance(at, ctx.Camera.transform.position);
            if (distance > range) return false;
            string gate = kind + "/" + Mathf.RoundToInt(at.x / 2) + "/" + Mathf.RoundToInt(at.z / 2);
            float gap = kind == "phone" ? 2.4f : kind == "horn" ? .5f : foot ? .07f : .035f;
            if (cooldowns.TryGetValue(gate, out float last) && ctx.Clock - last < gap) return false;
            if (cooldowns.Count > 512) cooldowns.Clear();
            cooldowns[gate] = ctx.Clock;
            int importance = loud ? 30 : foot ? 150 : 75;
            Voice voice = null; float worst = float.MinValue;
            foreach (var candidate in shots)
            {
                if (!candidate.source.isPlaying) { voice = candidate; break; }
                float score = candidate.importance + (candidate.source.transform.position - ctx.Camera.transform.position).magnitude;
                if (candidate.importance >= importance && score > worst) { voice = candidate; worst = score; }
            }
            if (voice == null) return false;
            voice.source.Stop(); voice.kind = kind; voice.category = categories[key]; voice.importance = importance;
            voice.source.transform.position = at; voice.source.clip = clip; voice.source.maxDistance = range; voice.source.priority = importance;
            bool self = (at - ctx.Player.position).sqrMagnitude < 4;
            voice.source.spatialBlend = self && foot ? .35f : 1; voice.source.minDistance = self ? 4 : 2;
            voice.source.dopplerLevel = 0;
            voice.source.pitch = foot || variants.ContainsKey(kind) ? 1 + UnityEngine.Random.Range(-.045f, .045f) : 1;
            voice.gain = Mathf.Clamp01(strength) * (loud ? .66f : kind == "bird" ? .65f : .7f);
            voice.obstruction = Obstruction(at); voice.nextRay = ctx.Clock + .15f;
            voice.filter.cutoffFrequency = voice.obstruction > 0 ? 1400 : 18000;
            voice.room.enabled = room != null && room.Contains(at);
            if (voice.room.enabled) { voice.room.reverbPreset = AudioReverbPreset.Room; voice.room.reverbLevel = -1700; voice.room.reflectionsLevel = -1900; voice.room.decayTime = .55f; }
            voice.source.volume = voice.gain * BusGain(voice.category) * Mathf.Lerp(1, .28f, voice.obstruction);
            voice.source.Play(); LastSource = voice.source;
            eventCounts[kind] = EventCount(kind) + 1;
            return true;
        }
        float Obstruction(Vector3 at)
        {
            Vector3 eye = ctx.Camera.transform.position, delta = eye - at;
            return delta.sqrMagnitude > 1 && Physics.Raycast(at + Vector3.up * .15f, delta.normalized, Mathf.Max(0, delta.magnitude - .35f), 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore) ? 1 : 0;
        }
        void UpdateShots(float dt)
        {
            foreach (var voice in shots)
            {
                if (!voice.source.isPlaying) continue;
                if (ctx.Clock >= voice.nextRay) { voice.obstruction = Obstruction(voice.source.transform.position); voice.nextRay = ctx.Clock + .15f; }
                float target = voice.gain * BusGain(voice.category) * Mathf.Lerp(1, .28f, voice.obstruction);
                voice.source.volume = Mathf.Lerp(voice.source.volume, target, 1 - Mathf.Exp(-dt * 12));
                voice.filter.cutoffFrequency = Mathf.Lerp(voice.filter.cutoffFrequency, voice.obstruction > 0 ? 1400 : 18000, 1 - Mathf.Exp(-dt * 10));
            }
        }
        void OnDestroy()
        {
            if (ctx && ctx.Vehicles) ctx.Vehicles.Spawned -= RegisterCar;
            if (ctx && ctx.Residents) ctx.Residents.Spawned -= RegisterWalker;
            // Car and boat voices belong to imported roots, not to this object.
            foreach (var voice in loops) if (voice.source) Destroy(voice.source.gameObject);
            foreach (var voice in shots) if (voice.source) Destroy(voice.source.gameObject);
            DisposeNpcVoices();
        }
    }
}
