using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CitySystems : MonoBehaviour
    {
        sealed class Door
        {
            public DoorData data; public Transform root; public Vector3 origin; public Quaternion rotation;
            public float amount, target, hold, forced, closedUntil, lockedUntil; public Collider[] colliders;
        }
        CityLighting lighting;
        sealed class Lamp { public Light light; public float intensity; }
        sealed class SignalLamp { public SignalData signal; public Renderer renderer; public int axis, color, state = -1; }
        CityContext ctx;
        public CityWeather Weather { get; private set; } = new CityWeather();
        public bool MusicPlaying { get; private set; }
        public float MusicBeat { get; private set; }
        public bool CinemaPlaying { get; private set; }
        public float CraneProgress { get { return craneProgress; } }
        public bool CraneWaiting { get; private set; }
        public bool FountainOn { get; private set; } = true;
        readonly List<Door> doors = new List<Door>();
        readonly List<Lamp> lamps = new List<Lamp>();
        readonly List<SignalLamp> signalLamps = new List<SignalLamp>();
        MaterialPropertyBlock signalBlock;
        static readonly Color[] SignalLit = { new Color(.984f, .286f, .247f), new Color(1, .749f, .216f), new Color(.282f, .867f, .529f) };
        static readonly Color[] SignalDark = { new Color(.271f, .157f, .153f), new Color(.286f, .239f, .145f), new Color(.137f, .239f, .196f) };
        public CityDestruction Destruction { get; private set; }
        public CityAudio Audio { get; private set; }
        readonly Dictionary<int, float> signalOverride = new Dictionary<int, float>();
        readonly Collider[] overlap = new Collider[32];
        readonly ParticleSystem.Particle[] rainBuffer = new ParticleSystem.Particle[720];
        readonly List<ParticleSystem> waterJets = new List<ParticleSystem>();
        readonly List<Renderer> waterRenderers = new List<Renderer>();
        ParticleSystem rain, fountain;
        Light sunlight;
        public CitySky Sky { get; private set; }
        Material rainMaterial, cinemaMaterial;
        Texture2D particleTexture;
        Texture cinemaPoster;
        Texture2D cinemaTexture;
        Mesh muralMesh;
        Vector3[] muralVertices;
        Color32[] filmPixels = new Color32[256 * 112];
        Transform cargo, windsock, platter;
        readonly List<Transform> cables = new List<Transform>(), slats = new List<Transform>(), reels = new List<Transform>();
        readonly List<Quaternion> slatRotations = new List<Quaternion>();
        Vector3 cargoOrigin;
        float rainAccumulator, nextLights, musicElapsed, cinemaElapsed, nextFilmFrame;
        float craneProgress, craneTarget, muralAngle, muralTarget, muralFrom, muralAge = 2;
        int radioChannel;

        public void Initialize(CityContext context)
        {
            ctx = context; signalBlock = new MaterialPropertyBlock();
            foreach (var place in ctx.Data.places)
            {
                var data = place.door; if (data == null) continue; var root = ctx.Node(data.id); if (!root) continue;
                var frame = new Door { data = data, root = root, origin = root.position, rotation = root.rotation };
                frame.colliders = root.GetComponentsInChildren<Collider>();
                if (frame.colliders.Length == 0)
                {
                    var colliderObject = new GameObject("Colisão da folha"); colliderObject.transform.SetParent(root, false); colliderObject.layer = CityContext.WorldLayer;
                    var box = colliderObject.AddComponent<BoxCollider>(); box.center = new Vector3(-data.width * .5f, 1.65f, 0); box.size = new Vector3(data.width, 3.3f, .16f);
                    frame.colliders = new Collider[] { box };
                }
                doors.Add(frame);
            }
            lighting = FindFirstObjectByType<CityLighting>();
            if (lighting) lighting.Initialize(ctx);
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) { if (!sunlight) sunlight = light; continue; }
                if (light.GetComponentInParent<Rigidbody>()) continue;
                if ((!lighting || !lighting.Owns(light)) && light.type != LightType.Spot) lamps.Add(new Lamp { light = light, intensity = Mathf.Max(.7f, light.intensity) });
            }
            if (!sunlight) { var g = new GameObject("Sol da baía"); sunlight = g.AddComponent<Light>(); sunlight.type = LightType.Directional; sunlight.shadows = LightShadows.Soft; }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight; RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            foreach (var signal in ctx.Data.signals) for (int i = 0; i < signal.lamps.Length; i++)
            {
                var node = ctx.Node(signal.lamps[i]); var lens = node ? node.GetComponentInChildren<Renderer>() : null;
                if (lens) signalLamps.Add(new SignalLamp { signal = signal, renderer = lens, axis = signal.lampAxes.Length > i ? signal.lampAxes[i] : i < signal.lamps.Length / 2 ? 0 : 1, color = signal.lampColors.Length > i ? Mathf.Clamp(signal.lampColors[i], 0, 2) : i % 3 });
            }
            var homes = FindFirstObjectByType<CityHomeCatalog>();
            if (homes) foreach (var entry in homes.doors)
            {
                if (!entry.leaf) continue;
                doors.Add(new Door { data = new DoorData { id = entry.leaf.GetInstanceID(), width = entry.width },
                    root = entry.leaf, origin = entry.leaf.position, rotation = entry.leaf.rotation,
                    colliders = entry.leaf.GetComponentsInChildren<Collider>() });
            }
            cargo = ctx.Node(ctx.Data.harbor.load); if (cargo) cargoOrigin = cargo.position;
            windsock = ctx.Node(ctx.Data.harbor.sock); platter = ctx.Node(ctx.Data.harbor.platter);
            foreach (int id in ctx.Data.harbor.cables) { var t = ctx.Node(id); if (t) cables.Add(t); }
            foreach (int id in ctx.Data.harbor.slats) { var t = ctx.Node(id); if (t) { slats.Add(t); slatRotations.Add(t.localRotation); } }
            var mural = ctx.Node(ctx.Data.harbor.mural);
            if (mural && mural.TryGetComponent<MeshFilter>(out var muralFilter)) { muralMesh = muralFilter.mesh; muralVertices = muralMesh.vertices; muralMesh.MarkDynamic(); }
            foreach (int id in ctx.Data.cinema.spools) { var t = ctx.Node(id); if (t) reels.Add(t); }
            var screen = ctx.Node(ctx.Data.cinema.screen);
            if (screen)
            {
                var renderer = screen.GetComponent<Renderer>(); if (renderer)
                {
                    cinemaMaterial = renderer.material; cinemaPoster = cinemaMaterial.mainTexture;
                    cinemaTexture = new Texture2D(256, 112, TextureFormat.RGB24, false); cinemaTexture.filterMode = FilterMode.Point;
                }
            }
            rain = CreateParticles("Chuva marítima", 720, new Color(.71f, .84f, .88f, .54f), .018f);
            var main = rain.main; main.startLifetime = 1.6f; var render = rain.GetComponent<ParticleSystemRenderer>();
            render.renderMode = ParticleSystemRenderMode.Stretch; render.velocityScale = .055f; render.lengthScale = 2;
            fountain = CreateParticles("Jatos da fonte", 180, new Color(.64f, .89f, .91f, .8f), .06f);
            fountain.transform.position = ReferenceMap.Active?ctx.Data.fountain:new Vector3(-36, ctx.Ground(-36, 36) + 1.7f, 36);
            foreach (var service in ctx.Data.services)
            {
                var s = service;
                if (s.kind == "fire-water")
                {
                    var jet = CreateParticles("Mangueira dos bombeiros", 140, new Color(.72f, .9f, .95f, .85f), .055f);
                    jet.transform.position = s.anchor == Vector3.zero ? s.position + Vector3.up * 1.1f : s.anchor; jet.gameObject.SetActive(false); waterJets.Add(jet);
                    ctx.AddAction("water-" + s.id, () => ActionPoint(s), () => jet.gameObject.activeSelf ? "Desligar água" : "Ligar água", () => { jet.gameObject.SetActive(!jet.gameObject.activeSelf); PlaySound("water", s.position); }, SafeToInteract);
                }
                else if (s.kind == "harbor-weather") ctx.AddAction("weather-" + s.id, () => ActionPoint(s), () => "Consultar o tempo", () => ctx.Say(Weather.Forecast(), ActionPoint(s) + Vector3.up * .7f, 7));
                else if (s.kind == "harbor-crane") ctx.AddAction("crane-" + s.id, () => ActionPoint(s), () => craneTarget > .5f ? "Baixar carga" : "Içar carga", () => ToggleCrane(), SafeToInteract);
                else if (s.kind == "harbor-radio") ctx.AddAction("radio-" + s.id, () => ActionPoint(s), () => "Sintonizar", () => ToggleRadio(), SafeToInteract);
                else if (s.kind == "cinema") ctx.AddAction("cinema-" + s.id, () => ActionPoint(s), () => CinemaPlaying ? "Encerrar sessão" : "Projetar curta", () => ToggleCinema(), SafeToInteract);
                else if (s.kind == "street-music") ctx.AddAction("music-" + s.id, () => ActionPoint(s), () => MusicPlaying ? "Aplaudir" : "Conversar", Applaud, SafeToInteract, conversation: true, target: () => ctx.Node(s.node));
                else if (s.kind == "police-info") ctx.AddAction("notice-" + s.id, () => ActionPoint(s), () => "Ler", () => ctx.Say("Delegacia da Baía.", ActionPoint(s) + Vector3.up * .7f, 7));
                else if (s.kind == "district-guide") ctx.AddAction("map-" + s.id, () => ActionPoint(s), () => "Mapa", () => ctx.Say("Oficina: av. das Colinas. Peixaria e cais: orla leste.", ActionPoint(s) + Vector3.up * .7f, 7));
            }
            Audio = gameObject.AddComponent<CityAudio>(); Audio.Initialize(ctx);
            Destruction = gameObject.AddComponent<CityDestruction>(); Destruction.Initialize(ctx);
            InitializeHacking();
            Sky = new CitySky(ctx.Camera, sunlight);
            UpdateSky();
        }
        Vector3 ActionPoint(ServiceData s) { return s.anchor == Vector3.zero ? s.position + Vector3.up * 1.4f : s.anchor; }
        bool SafeToInteract() { return !ctx.Remote.Active && !ctx.Combat.Down && !ctx.Combat.IsThreatening(); }
        public bool Sheltered(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * .06f, Vector3.up, 45, ctx.SolidMask, QueryTriggerInteraction.Ignore);
        }
        public float SurfaceGrip(Vector3 point, float baseGrip = 1)
        {
            float loss = baseGrip >= .9f ? .29f : baseGrip > .5f ? .18f : .095f;
            // A vehicle does not dry the road below its own axles. Only permanent
            // cover excludes precipitation from the surface being sampled.
            bool cover = Physics.Raycast(point + Vector3.up * .16f, Vector3.up, 45,
                (1 << CityContext.GroundLayer) | (1 << CityContext.WorldLayer), QueryTriggerInteraction.Ignore);
            return baseGrip * (1 - loss * Weather.Wetness * (cover ? 0 : 1));
        }
        public bool TrafficGreen(Vector3 point, Vector3 heading)
        {
            int closest = -1; float distance = 15;
            for (int i = 0; i < ctx.Data.signals.Length; i++) { var s = ctx.Data.signals[i]; float d = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(s.x, s.z)); if (d < distance) { closest = i; distance = d; } }
            if (closest < 0) return true;
            return SignalState(ctx.Data.signals[closest], Mathf.Abs(heading.z) > Mathf.Abs(heading.x)) == 0;
        }
        public int SignalState(SignalData signal, bool northSouth)
            => SignalState(signal, northSouth ? 0 : 1);
        public int SignalState(SignalData signal, int axis)
        {
            int index = Array.IndexOf(ctx.Data.signals, signal);
            if (signalOverride.TryGetValue(index, out float until) && ctx.Clock < until) return 2;
            return CitySignalRules.State(signal, ctx.Clock, axis);
        }
        void UpdateSignalLights()
        {
            foreach (var lamp in signalLamps)
            {
                if (!lamp.renderer) continue;
                int state = 2 - SignalState(lamp.signal, lamp.axis);
                if (state == lamp.state) continue; lamp.state = state;
                bool lit = state == lamp.color; lamp.renderer.GetPropertyBlock(signalBlock);
                signalBlock.SetColor("_Color", lit ? SignalLit[lamp.color] : SignalDark[lamp.color]);
                signalBlock.SetColor("_EmissionColor", lit ? SignalLit[lamp.color] * 1.15f : Color.black);
                lamp.renderer.SetPropertyBlock(signalBlock);
            }
        }
        public bool ActivateDevice(DeviceData device)
        {
            if (device == null) return false;
            string type = device.type ?? "";
            if (type == "harbor-crane") { ToggleCrane(); return true; }
            if (type == "harbor-radio") { ToggleRadio(); return true; }
            if (type == "cinema") { ToggleCinema(); return true; }
            if (type == "fountain" || type == "fonte") { FountainOn = !FountainOn; return true; }
            if (type == "phone") { PlaySound("phone", device.position); ctx.Residents.ReactToSound(device.position, 10); return true; }
            if (type == "signal")
            {
                int index = Mathf.Clamp(device.target, 0, Mathf.Max(0, ctx.Data.signals.Length - 1));
                if (ctx.Data.signals.Length > 0) signalOverride[index] = ctx.Clock + 7;
                return true;
            }
            if (type == "fire-water") { foreach (var water in waterJets) water.gameObject.SetActive(!water.gameObject.activeSelf); return true; }
            foreach (var d in doors) if (d.data.id == device.node || d.data.id == device.target || type == "door" && Vector3.Distance(d.origin, device.position) < 4)
            { d.forced = ctx.Clock + 10; d.hold = ctx.Clock + 10; PlaySound("door", d.origin); return true; }
            var target = ctx.Node(device.target != 0 ? device.target : device.node);
            if (type == "car" || type == "vehicle" || type == "brake")
            {
                if (target) ctx.Vehicles.RemoteBrake(target, 4);
                PlaySound("register", device.position); return true;
            }
            return false;
        }
        public void ToggleCrane()
        {
            craneTarget = craneTarget > .5f ? 0 : 1; PlaySound("harbor-winch", ctx.Data.harbor.loadPosition+Vector3.up*1.3f);
        }
        public void ToggleRadio()
        {
            if (Mathf.Abs(muralAngle - muralTarget) > 1) return;
            radioChannel = (radioChannel + 1) % 3; muralFrom = muralTarget; muralTarget += 120; muralAge = 0;
            var radio=ReferenceMap.Active?ReferenceMap.Layout.harborRadio:new Vector3(145.1f,1.5f,-152.8f);
            Audio.TuneRadio(radioChannel);
            ctx.Say(new[] { "Garça da Baía", "Velas no Canal", "Onda Curta" }[radioChannel], radio+Vector3.up*1.7f, 4);
        }
        public void ToggleCinema()
        {
            CinemaPlaying = !CinemaPlaying; cinemaElapsed = 0; nextFilmFrame = 0;
            if (cinemaMaterial) cinemaMaterial.mainTexture = CinemaPlaying ? cinemaTexture : cinemaPoster;
            PlaySound("film-reel", ctx.Data.cinema.projector);
        }
        void Applaud()
        {
            if (!MusicPlaying) { ctx.Say(Weather.Rain > .6f ? "Hoje a chuva não deixa." : "Já volto a tocar.", ctx.Data.music.position + Vector3.up * 2.8f, 5); return; }
            ctx.Combat.Holster(); ctx.Actors.Applaud(1.5f); PlaySound("applause", ctx.Player.position);
            ctx.Say("Valeu!", ctx.Data.music.position + Vector3.up * 2.8f, 4);
        }
        void Update()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return; float dt = Time.deltaTime;
            Weather.Tick(dt); ctx.Rain = Weather.Rain; ctx.Wetness = Weather.Wetness;
            TickHackEffects();
            UpdateDoors(dt); UpdateHarbor(dt); UpdateMusic(dt); UpdateWater(dt);
            if (ctx.Clock >= nextLights) { UpdateSky(); nextLights = ctx.Clock + .16f; }
            if (CinemaPlaying)
            {
                cinemaElapsed += dt; foreach (var r in reels) if (r) r.Rotate(0, 0, dt * 218, Space.Self);
                if (ctx.Clock >= nextFilmFrame && Vector3.Distance(ctx.Camera.transform.position, ctx.Data.cinema.projector) < 75) { DrawFilm(); nextFilmFrame = ctx.Clock + .125f; }
            }
        }
        void LateUpdate()
        {
            if (!ctx || !ctx.Ready || ctx.Paused) return;
            Sky?.Advance(ctx.Clock);
            Vector3 eye = ctx.Camera.transform.position;
            UpdateRain(Time.deltaTime, eye);
        }
        void UpdateDoors(float dt)
        {
            int mask = (1 << CityContext.ActorLayer) | (1 << CityContext.PlayerLayer) | (1 << CityContext.VehicleLayer);
            foreach (var door in doors)
            {
                if (!door.root || !door.root.gameObject.activeInHierarchy) continue;
                float before = Mathf.Abs(door.amount);
                Vector3 middle = door.origin + door.rotation * Vector3.left * door.data.width * .5f;
                Vector3 delta = Quaternion.Inverse(door.rotation) * (ctx.Player.position - middle);
                bool playerNear = Mathf.Abs(delta.y) < 3 && new Vector2(delta.x, delta.z).magnitude < 2.9f;
                // Cover the whole doorway, including its edges. A moving player
                // gets enough lead for the leaf to clear before capsule contact.
                float toward = ctx.Motor && !ctx.Motor.Riding ? -Mathf.Sign(delta.z) * (Quaternion.Inverse(door.rotation) * ctx.Motor.Velocity).z : 0;
                float approach = Mathf.Clamp(.8f + Mathf.Max(0, toward) * .32f, 1.15f, 3.8f);
                bool pushing = Mathf.Abs(delta.y) < 3 && Mathf.Abs(delta.x) < door.data.width * .5f + .18f && Mathf.Abs(delta.z) < approach;
                int nearby = door.data.automatic
                    ? Physics.OverlapSphereNonAlloc(middle + Vector3.up, 2.5f, overlap, mask, QueryTriggerInteraction.Ignore)
                    : Physics.OverlapBoxNonAlloc(middle + Vector3.up * 1.1f, new Vector3(door.data.width * .5f + .15f, 1.1f, 1.15f), overlap, door.rotation,
                        mask & ~(1 << CityContext.VehicleLayer), QueryTriggerInteraction.Ignore);
                if (door.data.automatic)
                {
                    bool blocked = ctx.Clock < door.lockedUntil || ctx.Clock < door.closedUntil;
                    if (!blocked && (playerNear || nearby > 0)) door.hold = ctx.Clock + 1.1f;
                    door.target = !blocked && ctx.Clock < Mathf.Max(door.hold, door.forced) ? 1 : 0;
                    float next = Mathf.MoveTowards(door.amount, door.target, dt * 1.4f);
                    if (next < door.amount && nearby > 0) continue;
                    door.amount = next; door.root.position = door.origin + door.rotation * Vector3.right * door.data.width * door.amount * .96f;
                }
                else
                {
                    if (nearby > 0 || pushing || ctx.Clock < door.forced)
                    {
                        Vector3 pusher = ctx.Player.position;
                        if (!pushing && nearby > 0) pusher = overlap[0].bounds.center;
                        // Keep the opening direction while someone passes the
                        // threshold. Reversing it at z=0 swings the leaf back
                        // into the person and can trap them in the doorway.
                        float direction = Mathf.Abs(door.amount) > 1 ? Mathf.Sign(door.amount) : Mathf.Abs(door.target) > 1 ? Mathf.Sign(door.target) : ((Quaternion.Inverse(door.rotation)*(pusher-door.origin)).z>=0 ? -1 : 1);
                        door.target = direction * 90; door.hold = ctx.Clock + 1.2f;
                    }
                    if (ctx.Clock >= Mathf.Max(door.hold, door.forced)) door.target = 0;
                    float next = Mathf.MoveTowards(door.amount, door.target, dt * (Mathf.Abs(door.target) > .1f ? 220 : 34));
                    Quaternion rot = door.rotation * Quaternion.Euler(0, next, 0);
                    Vector3 center = door.origin + rot * new Vector3(-door.data.width * .5f, 1.65f, 0);
                    if (Mathf.Abs(next) < Mathf.Abs(door.amount) && Physics.OverlapBoxNonAlloc(center, new Vector3(door.data.width * .5f, 1.60f, .16f), overlap, rot, mask, QueryTriggerInteraction.Ignore) > 0) continue;
                    door.amount = next; door.root.rotation = rot;
                }
                float amount = Mathf.Abs(door.amount);
                if (before < .001f && amount > .001f) PlaySound(door.data.automatic ? "door-slide" : "door", middle + Vector3.up * 1.2f, .55f);
                if (before > .001f && amount < .001f) PlaySound("door-close", middle + Vector3.up * 1.2f, .45f);
            }
        }
        void UpdateHarbor(float dt)
        {
            if (cargo)
            {
                float next = Mathf.MoveTowards(craneProgress, craneTarget, dt * .34f), bottom = cargoOrigin.y + Mathf.Min(next, craneProgress) * 4.25f;
                float h = 2.78f + Mathf.Abs(next - craneProgress) * 4.25f;
                CraneWaiting = Physics.OverlapBoxNonAlloc(new Vector3(cargoOrigin.x, bottom + h * .5f, cargoOrigin.z), new Vector3(3.82f, h * .5f + .04f, 1.65f), overlap, Quaternion.identity,
                    (1 << CityContext.PlayerLayer) | (1 << CityContext.ActorLayer) | (1 << CityContext.VehicleLayer), QueryTriggerInteraction.Ignore) > 0;
                if (!CraneWaiting) craneProgress = next;
                cargo.position = cargoOrigin + Vector3.up * craneProgress * 4.25f;
                foreach (var cable in cables) { float low = cargo.position.y + 2.78f, high = cargoOrigin.y + 7.89f; var p = cable.position; p.y = (low + high) * .5f; cable.position = p; var scale = cable.localScale; scale.y = Mathf.Max(.05f, high - low); cable.localScale = scale; }
            }
            muralAge += dt; muralAngle = Mathf.Lerp(muralFrom, muralTarget, Mathf.SmoothStep(0, 1, Mathf.Clamp01(muralAge / 1.2f)));
            for (int i = 0; i < slats.Count; i++) if (slats[i]) slats[i].localRotation = slatRotations[i] * Quaternion.Euler(0, muralAngle, 0);
            if (muralMesh && muralVertices.Length == 15 * 18 && muralAge < 1.3f)
            {
                const int count = 15; const float width = 8.4f, height = 2.78f; float step = width / count, radius = step / Mathf.Sqrt(3);
                int vertex = 0; int[] us = { 0, 0, 1, 1, 0, 1 }, vs = { 1, 0, 1, 1, 0, 0 };
                for (int col = 0; col < count; col++)
                {
                    float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01((muralAge - col * .032f) / .73f)); float angle = Mathf.Lerp(muralFrom, muralTarget, blend) * Mathf.Deg2Rad;
                    for (int face = 0; face < 3; face++) for (int v = 0; v < 6; v++)
                    {
                        float a = angle - face * Mathf.PI * 2 / 3 + (us[v] != 0 ? Mathf.PI / 3 : -Mathf.PI / 3);
                        muralVertices[vertex++] = new Vector3((col + .5f) * step - width / 2 + Mathf.Sin(a) * radius, (vs[v] - .5f) * height, Mathf.Cos(a) * radius);
                    }
                }
                muralMesh.vertices = muralVertices; muralMesh.RecalculateNormals(); muralMesh.RecalculateBounds();
            }
            if (platter && radioChannel != 0) platter.Rotate(0, dt * 195, 0, Space.Self);
            if (windsock)
            {
                Quaternion target = Quaternion.Euler(12 + 45 / (1 + Weather.WindSpeed * .32f), Mathf.Atan2(Weather.Wind.x, Weather.Wind.z) * Mathf.Rad2Deg, Mathf.Sin(ctx.Clock * 3) * 4);
                windsock.rotation = Quaternion.Slerp(windsock.rotation, target, 1 - Mathf.Exp(-dt * 3));
            }
        }
        void UpdateMusic(float dt)
        {
            Transform musician = ctx.Node(ctx.Data.music.musician);
            MusicPlaying = musician && Weather.Rain < .62f && ctx.Residents.CanTalk(musician) &&
                !(ctx.Combat.IsThreatening() && Vector3.Distance(ctx.Player.position, musician.position) < 16);
            if (!MusicPlaying) return;
            musicElapsed += dt; MusicBeat = musicElapsed / .27f;
        }
        void UpdateSky()
        {
            float sun = Mathf.Sin((ctx.Clock / 300 + .19f) * Mathf.PI * 2); ctx.Night = Mathf.InverseLerp(.20f, -.17f, sun);
            Color day = Color.Lerp(new Color(.64f, .84f, .89f), new Color(.43f, .52f, .55f), Weather.Cloud * .84f);
            Color sky = Color.Lerp(day, new Color(.025f, .065f, .10f), ctx.Night);
            ctx.Camera.backgroundColor = sky;
            ctx.Camera.clearFlags = Sky != null && Sky.Available ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            RenderSettings.fogColor = Color.Lerp(sky, new Color(.63f, .72f, .72f), Weather.Fog * (1 - ctx.Night) * .48f);
            float coastView = ReferenceMap.Active?1:Mathf.SmoothStep(0, 1, (-ctx.Player.position.x - 180) / 130);
            RenderSettings.fogStartDistance = Mathf.Lerp(Mathf.Lerp(105, 240, coastView), 24, Weather.Fog);
            RenderSettings.fogEndDistance = Mathf.Lerp(Mathf.Lerp(370, 1050, coastView), 115, Weather.Fog);
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(.66f, .75f, .77f), new Color(.13f, .20f, .26f), ctx.Night);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(.55f, .63f, .57f), new Color(.09f, .14f, .17f), ctx.Night);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(.32f, .37f, .27f), new Color(.045f, .075f, .078f), ctx.Night);
            Sky?.SetWeather(sky, RenderSettings.fogColor, ctx.Night, Weather);
            Sky?.Advance(ctx.Clock);
            sunlight.intensity = Mathf.Lerp(1.18f, .15f, ctx.Night) * (1 - Weather.Cloud * .64f);
            sunlight.color = Color.Lerp(new Color(1, .93f, .77f), new Color(.53f, .68f, .89f), ctx.Night);
            foreach (var lamp in lamps) if (lamp.light) { lamp.light.intensity = lamp.intensity * Mathf.Max(ctx.Night, Weather.Fog * .55f, Weather.Rain * .5f); lamp.light.enabled = lamp.light.intensity > .04f; }
            UpdateSignalLights();
            if (lighting) lighting.Tick(Mathf.Max(ctx.Night, Mathf.Max(0, Weather.Cloud - .66f) * .66f));
            Shader.SetGlobalFloat("_CityWetness", Weather.Wetness); Shader.SetGlobalFloat("_CityRain", Weather.Rain); Shader.SetGlobalColor("_CitySkyColor", sky);
            Shader.SetGlobalFloat("_CityTime", ctx.Clock); Shader.SetGlobalFloat("_CitySeaState", Weather.SeaState); Shader.SetGlobalVector("_CityWind", Weather.Wind);
        }
        ParticleSystem CreateParticles(string name, int max, Color color, float size)
        {
            var go = new GameObject(name); var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = max;
            main.startLifetime = 1.1f; main.startSpeed = 0; main.startSize = size; main.startColor = color; main.gravityModifier = 0;
            var emission = ps.emission; emission.enabled = false; var shape = ps.shape; shape.enabled = false;
            if (!rainMaterial)
            {
                rainMaterial = new Material(Shader.Find("Particles/Standard Unlit")); rainMaterial.SetFloat("_Mode", 2);
                rainMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                rainMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); rainMaterial.SetInt("_ZWrite", 0);
                rainMaterial.DisableKeyword("_ALPHATEST_ON"); rainMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON"); rainMaterial.EnableKeyword("_ALPHABLEND_ON");
                rainMaterial.SetOverrideTag("RenderType", "Transparent"); rainMaterial.renderQueue = 3000; rainMaterial.SetColor("_Color", Color.white);
                particleTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false); particleTexture.wrapMode = TextureWrapMode.Clamp;
                var pixels = new Color[256]; for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                { float dx = (x + .5f - 8) / 8, dy = (y + .5f - 8) / 8; pixels[y * 16 + x] = new Color(1, 1, 1, Mathf.Clamp01((1 - dx * dx - dy * dy) * 4)); }
                particleTexture.SetPixels(pixels); particleTexture.Apply(); rainMaterial.mainTexture = particleTexture;
            }
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = rainMaterial; ps.Play(); return ps;
        }
        void UpdateRain(float dt, Vector3 eye)
        {
            if (Weather.Rain < .01f && rain.particleCount == 0) return;
            int budget = ctx.UI.Quality == 0 ? 230 : ctx.UI.Quality == 2 ? 690 : 450;
            rainAccumulator += dt * Weather.Rain * budget * .72f;
            int emit = Mathf.Min(28, Mathf.FloorToInt(rainAccumulator)); rainAccumulator -= emit;
            for (int i = 0; i < emit; i++)
            {
                Vector3 at = eye + new Vector3(UnityEngine.Random.Range(-17f, 17f), UnityEngine.Random.Range(3f, 15f), UnityEngine.Random.Range(-17f, 17f));
                if (Physics.Raycast(new Vector3(at.x, 64, at.z), Vector3.down, out RaycastHit roof, 110, ctx.SolidMask, QueryTriggerInteraction.Ignore) && roof.point.y >= at.y - .05f) continue;
                var p = new ParticleSystem.EmitParams { position = at, velocity = new Vector3(Weather.Wind.x * .6f, -19 - Weather.Rain * 4, Weather.Wind.z * .6f), startLifetime = 1.5f, startSize = .018f };
                rain.Emit(p, 1);
            }
            int count = rain.GetParticles(rainBuffer);
            for (int i = 0; i < count; i++)
            {
                var p = rainBuffer[i]; Vector3 travel = p.velocity * dt, previous = p.position - travel;
                bool hit = Physics.Raycast(previous, travel.normalized, travel.magnitude + .1f, ctx.SolidMask, QueryTriggerInteraction.Ignore);
                if (hit || p.position.y < -.55f || Vector3.SqrMagnitude(p.position - eye) > 39 * 39) { p.remainingLifetime = -1; rainBuffer[i] = p; }
            }
            rain.SetParticles(rainBuffer, count);
        }
        void UpdateWater(float dt)
        {
            if (FountainOn && Vector3.Distance(ctx.Camera.transform.position, fountain.transform.position) < 70)
                for (int i = 0; i < 3; i++) { var p = new ParticleSystem.EmitParams { position = fountain.transform.position + UnityEngine.Random.insideUnitSphere * .13f, velocity = new Vector3(Mathf.Sin(ctx.Clock * 3 + i) * .75f, 3.8f, Mathf.Cos(ctx.Clock * 3 + i) * .75f), startLifetime = .7f }; fountain.Emit(p, 1); }
            foreach (var jet in waterJets) if (jet.gameObject.activeSelf)
                for (int i = 0; i < 2; i++) { var p = new ParticleSystem.EmitParams { position = jet.transform.position, velocity = new Vector3(3.5f, 1.7f, 2) + UnityEngine.Random.insideUnitSphere * .25f, startLifetime = .6f }; jet.Emit(p, 1); }
            var main = fountain.main; main.gravityModifier = 1; foreach (var jet in waterJets) { var m = jet.main; m.gravityModifier = 1; }
        }
        void DrawFilm()
        {
            if (!cinemaTexture) return;
            float phase = cinemaElapsed % 22 / 22, sun = Mathf.Sin(phase * Mathf.PI); int boatX = (int)(-30 + phase * 310), boatY = 44 + (int)(Mathf.Sin(cinemaElapsed * 2.5f) * 2);
            Color32 sky = Weather.Rain > .35f ? new Color32(156, 179, 180, 255) : Color.Lerp(new Color(.79f, .64f, .61f), new Color(.93f, .74f, .52f), sun);
            for (int y = 0; y < 112; y++) for (int x = 0; x < 256; x++)
            {
                Color32 c = sky; int hill = 52 + (int)(Mathf.Sin(x * .045f) * 8);
                if (y < hill) c = new Color32(99, 129, 128, 255);
                if (y < 46) c = new Color32(49, 85, 93, 255);
                if (y < 42 && (y % 8 == 2) && ((x + (int)(cinemaElapsed * 9) + y * 3) % 29 < 11)) c = new Color32(117, 177, 166, 255);
                if ((x - (208 - phase * 27)) * (x - (208 - phase * 27)) + (y - (78 + sun * 17)) * (y - (78 + sun * 17)) < 14 * 14) c = new Color32(239, 215, 163, 255);
                if (Mathf.Abs(x - boatX) < 16 && y >= boatY - 5 && y <= boatY && Mathf.Abs(x - boatX) < 12 + y - (boatY - 5)) c = new Color32(191, 121, 93, 255);
                if (Mathf.Abs(x - boatX) < 7 && y > boatY && y < boatY + 8) c = new Color32(237, 218, 172, 255);
                if (x == boatX && y >= boatY + 8 && y < boatY + 18) c = new Color32(42, 61, 69, 255);
                if (x > 27 && x < 36 && y > 45 && y < 76) c = new Color32(192, 127, 101, 255);
                if (x > 25 && x < 38 && y > 72 && y < 77) c = new Color32(39, 60, 70, 255);
                filmPixels[y * 256 + x] = c;
            }
            cinemaTexture.SetPixels32(filmPixels); cinemaTexture.Apply(false, false);
        }
        public void PlaySound(string kind, Vector3 at, float strength = 1) => Audio?.Play(kind, at, strength);
        public bool Hit(Collider collider, Vector3 impulse, float damage = 36, Vector3 point = default,
            CityPropDamageKind kind = CityPropDamageKind.Bullet)
        {
            return Destruction && Destruction.Hit(collider, damage, point == Vector3.zero && collider ? collider.bounds.center : point, impulse, kind);
        }
        void OnDestroy()
        {
            Sky?.Dispose();
            if (rainMaterial) Destroy(rainMaterial); if (particleTexture) Destroy(particleTexture); if (cinemaTexture) Destroy(cinemaTexture); if (cinemaMaterial) Destroy(cinemaMaterial); if (muralMesh) Destroy(muralMesh);
            if (rain) Destroy(rain.gameObject); if (fountain) Destroy(fountain.gameObject); foreach (var jet in waterJets) if (jet) Destroy(jet.gameObject);
        }
    }

    [Serializable]
    public sealed class CityWeather
    {
        static readonly string[] Fronts = { "clear", "mist", "rain", "squall", "rain" };
        static readonly float[] Durations = { 78, 52, 68, 40, 47 };
        static readonly string[] Names = { "Céu aberto", "Bruma marítima", "Chuva", "Aguaceiro" };
        static readonly float[,] Values = {
            {0, .035f, .07f, .85f, 1.75f, .13f}, {0, .91f, .58f, .35f, 1.25f, .18f},
            {.62f, .46f, .86f, 2.65f, 3.8f, .53f}, {1, .70f, .99f, 6.7f, 5.8f, .93f}
        };
        public float Rain, Fog = .035f, Cloud = .07f, Wetness, SeaState = .13f, Transition = 1;
        public Vector3 Wind = new Vector3(.85f, 0, 1.75f);
        public float WindSpeed { get { return Wind.magnitude; } }
        public string Id { get { return Fronts[index]; } }
        public string Label { get { return Names[ClimateIndex(Id)]; } }
        public string NextLabel { get { return Names[ClimateIndex(Fronts[(index + 1) % Fronts.Length])]; } }
        public int SecondsToChange { get { return Mathf.CeilToInt(Mathf.Max(0, Durations[index] - age)); } }
        readonly float[] from = new float[6];
        int index;
        float age, elapsed, blend;
        static int ClimateIndex(string id) { return id == "mist" ? 1 : id == "rain" ? 2 : id == "squall" ? 3 : 0; }
        public CityWeather() { for (int i = 0; i < 6; i++) from[i] = Values[0, i]; }
        public bool Request(string id, bool immediate = false)
        {
            int next = Array.IndexOf(Fronts, id);
            if (next < 0) return false;
            if (next != index || immediate) Change(next);
            if (immediate) { blend = 0; Sample(); Wetness = Rain * .92f; }
            return true;
        }
        void Change(int next)
        {
            float gust = 1 + (.05f + Rain * .13f) * Mathf.Sin(elapsed * .43f) * Mathf.Sin(elapsed * .17f);
            from[0] = Rain; from[1] = Fog; from[2] = Cloud; from[3] = Wind.x / gust; from[4] = Wind.z / gust; from[5] = SeaState;
            index = next; age = 0; blend = Id == "squall" ? 14 : 20;
        }
        void Sample()
        {
            Transition = blend > 0 ? Mathf.Clamp01(age / blend) : 1;
            float t = Transition * Transition * (3 - 2 * Transition); int c = ClimateIndex(Id);
            Rain = Mathf.Lerp(from[0], Values[c, 0], t); Fog = Mathf.Lerp(from[1], Values[c, 1], t);
            Cloud = Mathf.Lerp(from[2], Values[c, 2], t); SeaState = Mathf.Lerp(from[5], Values[c, 5], t);
            float gust = 1 + (.05f + Rain * .13f) * Mathf.Sin(elapsed * .43f) * Mathf.Sin(elapsed * .17f);
            Wind = new Vector3(Mathf.Lerp(from[3], Values[c, 3], t), 0, Mathf.Lerp(from[4], Values[c, 4], t)) * gust;
        }
        public void Tick(float dt)
        {
            if (float.IsNaN(dt) || dt <= 0) return;
            while (dt > .00001f)
            {
                float step = Mathf.Min(dt, Mathf.Min(1, Durations[index] - age));
                age += step; elapsed += step; Sample();
                float target = Rain > .08f ? Mathf.Min(1, Rain * 1.34f) : Mathf.Max(0, Fog - .42f) * .07f;
                float rate = target > Wetness ? .051f : .008f * (1.12f - Cloud * .67f);
                Wetness = Mathf.Lerp(Wetness, target, 1 - Mathf.Exp(-rate * step));
                dt -= step; if (age >= Durations[index] - .00001f) Change((index + 1) % Fronts.Length);
            }
        }
        public string Forecast() { return Label + ". Vento de " + Mathf.RoundToInt(WindSpeed * 3.6f) + " km/h. A seguir, " + NextLabel.ToLowerInvariant() + "."; }
    }
}
