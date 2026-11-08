using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityRemote : MonoBehaviour
    {
        enum DroneMode { Stowed, Flying, Returning, Landed }
        CityContext ctx;
        DroneMode mode;
        Transform drone, body;
        readonly List<Transform> rotors = new List<Transform>();
        readonly List<Vector3> route = new List<Vector3>();
        readonly List<Material> ownedMaterials = new List<Material>();
        Vector3 velocity, launchPoint;
        float droneYaw, dronePitch = 12, blockedTime;
        DeviceData cameraDevice, linked;
        Transform cameraHead;
        Quaternion cameraHome;
        float pan, tilt;
        public bool VehicleActive => ctx && ctx.Vehicles && ctx.Vehicles.RemoteVehicle != null;
        public bool Active { get { return DroneActive || cameraDevice != null || VehicleActive; } }
        public bool DroneActive { get { return mode == DroneMode.Flying; } }
        public bool OverridesCamera { get { return DroneActive || cameraDevice != null; } }
        public bool Scanning { get; private set; }
        public Transform Drone { get { return drone; } }
        public Vector3 DronePosition { get { return drone ? drone.position : Vector3.zero; } }
        public Vector3 Position { get { return VehicleActive ? ctx.Vehicles.RemoteVehicle.Position : DroneActive ? DronePosition : cameraDevice != null ? cameraDevice.position : ctx.Player.position; } }
        public Quaternion Rotation { get { return DroneActive ? Quaternion.Euler(dronePitch, droneYaw, 0) : cameraHead ? cameraHead.rotation : Quaternion.identity; } }

        public void Initialize(CityContext context)
        {
            ctx = context; drone = ctx.Node(ctx.Data.droneId);
            if (!drone) BuildDrone();
            else
            {
                foreach (Transform t in drone.GetComponentsInChildren<Transform>(true)) if (t.name.ToLowerInvariant().Contains("rotor") || t.name.ToLowerInvariant().Contains("propeller")) rotors.Add(t);
            }
            drone.gameObject.SetActive(false); body = drone;
            BuildHackMarker();
            ctx.AddAction("remote-return", ReturnPoint, () => DroneActive ? "Recolher drone" : "Voltar", RecallOrExit, () => Active, 1000, 1000, requiresLook: false);
            ctx.AddAction("drone-retrieve", () => DronePosition + Vector3.up * .6f, () => "Recolher drone", Stow, () => mode == DroneMode.Landed, 2.5f, 2, target: () => drone);
            foreach (var s in ctx.Data.services)
            {
                var service = s; if (service.kind != "harbor-drone") continue;
                ctx.AddAction("launch-" + service.id, () => service.anchor == Vector3.zero ? service.position + Vector3.up : service.anchor, () => "Lançar drone", ToggleDrone,
                    () => !Active && mode == DroneMode.Stowed && !ctx.Motor.Busy, target: () => ctx.Node(service.node));
            }
        }
        Vector3 ReturnPoint()
        {
            return DroneActive ? drone.position + Vector3.up * .45f : ctx.Camera.transform.position + ctx.Camera.transform.forward * 3.4f + Vector3.up * .5f;
        }
        void BuildDrone()
        {
            drone = new GameObject("Drone de exploração").transform;
            Box(drone, Vector3.zero, new Vector3(.28f, .12f, .38f), new Color(.20f, .27f, .29f));
            Box(drone, new Vector3(0, .08f, 0), new Vector3(.23f, .05f, .28f), new Color(.90f, .62f, .31f));
            Box(drone, new Vector3(0, -.06f, .22f), new Vector3(.14f, .10f, .11f), new Color(.22f, .36f, .39f));
            for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
            {
                var pos = new Vector3(x * .243f, .06f, z * .222f);
                var arm = Box(drone, pos * .5f, new Vector3(.045f, .05f, .35f), new Color(.24f, .31f, .31f)); arm.localRotation = Quaternion.Euler(0, Mathf.Atan2(pos.x, pos.z) * Mathf.Rad2Deg, 0);
                var rotor = new GameObject("Hélice").transform; rotor.SetParent(drone, false); rotor.localPosition = pos + Vector3.up * .04f; rotors.Add(rotor);
                Box(rotor, Vector3.zero, new Vector3(.30f, .015f, .055f), new Color(.87f, .88f, .77f));
                Box(drone, pos - Vector3.up * .10f, new Vector3(.036f, .075f, .05f), new Color(.88f, .65f, .30f));
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4; var guard = Box(drone, pos + new Vector3(Mathf.Sin(a) * .16f, .04f, Mathf.Cos(a) * .16f), new Vector3(.13f, .025f, .025f), new Color(.20f, .27f, .29f)); guard.localRotation = Quaternion.Euler(0, a * Mathf.Rad2Deg, 0);
                }
            }
        }
        Transform Box(Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            var m = new Material(Shader.Find("Standard")); m.color = color; m.SetFloat("_Glossiness", .35f); ownedMaterials.Add(m); g.GetComponent<Renderer>().sharedMaterial = m; return g.transform;
        }
        public void ToggleDrone()
        {
            if (DroneActive) { Recall(); return; }
            if (mode == DroneMode.Returning) { ctx.Say("Voltando…", ctx.Player.position + Vector3.up * 2.8f); return; }
            if (mode == DroneMode.Landed) { ctx.Say("Drone pousado.", drone.position + Vector3.up * .7f); return; }
            if (ctx.Paused || ctx.Combat.Down || ctx.Police.Custody || ctx.Motor.Busy || ctx.Motor.Riding || ctx.Motor.Swimming || ctx.Motor.SeatHeight.HasValue || cameraDevice != null) return;
            droneYaw = ctx.Camera.transform.eulerAngles.y; dronePitch = 12;
            Vector3 forward = Quaternion.Euler(0, droneYaw, 0) * Vector3.forward, right = Vector3.Cross(Vector3.up, forward);
            Vector3 basePoint = ctx.Player.position + Vector3.up * 1.86f; bool placed = false;
            foreach (float side in new[] { 0f, .66f, -.66f })
            {
                Vector3 p = basePoint + forward * .68f + right * side;
                if (!Physics.CheckSphere(p, .34f, ctx.AllMask, QueryTriggerInteraction.Ignore) && ctx.Visible(basePoint, p)) { drone.position = p; placed = true; break; }
            }
            if (!placed) { ctx.Say("Sem espaço.", basePoint + Vector3.up); return; }
            launchPoint = ctx.Player.position; route.Clear(); route.Add(drone.position); velocity = Vector3.zero; blockedTime = 0;
            drone.gameObject.SetActive(true); mode = DroneMode.Flying; ctx.Motor.SetRemote(true); ctx.Combat.Holster(); ctx.SpeechUntil = 0;
            ctx.Sound("drone-start", ctx.Player.position); Scanning = false;
        }
        public void RecallOrExit() { if (VehicleActive) { ctx.Vehicles.EndRemoteDrive(); StopControl(); } else if (cameraDevice != null) Disconnect(); else if (DroneActive) Recall(); }
        public void Recall()
        {
            if (!DroneActive) return; mode = DroneMode.Returning; velocity = Vector3.zero; blockedTime = 0; StopControl();
        }
        void StopControl()
        {
            ctx.Motor.SetRemote(false); ctx.Follow.ResetTracking(); Scanning = false; linked = null; HideHackPreview();
            ctx.Input.TouchMove = ctx.Input.TouchLook = Vector2.zero; ctx.Input.TouchHack = false;
        }
        void Stow() { drone.gameObject.SetActive(false); mode = DroneMode.Stowed; route.Clear(); velocity = Vector3.zero; }
        void Connect(DeviceData d)
        {
            if (VehicleActive) ctx.Vehicles.EndRemoteDrive();
            if (DroneActive) Recall();
            cameraDevice = d; cameraHead = ctx.Node(d.node); cameraHome = cameraHead ? cameraHead.rotation : Quaternion.identity;
            pan = 0; tilt = d.pitch * Mathf.Rad2Deg;
            if (d.lookAt != Vector3.zero) tilt = Mathf.Atan2(d.position.y - d.lookAt.y, new Vector2(d.lookAt.x - d.position.x, d.lookAt.z - d.position.z).magnitude) * Mathf.Rad2Deg;
            ctx.Motor.SetRemote(true); ctx.Combat.Holster(); ctx.SpeechUntil = 0; Scanning = false; linked = null;
        }
        public void Disconnect()
        {
            if (cameraHead) cameraHead.rotation = cameraHome; cameraHead = null; cameraDevice = null; StopControl();
        }
        void Update()
        {
            if (!ctx || !ctx.Ready) return;
            if (ctx.Paused) { Scanning = false; HideHackPreview(); return; }
            float dt = Time.deltaTime;
            if (ctx.Input.Drone) ToggleDrone();
            if (Active && (ctx.Combat.Down || ctx.Police.Custody || ctx.Motor.Riding)) RecallOrExit();
            if (VehicleActive && (Vector3.Distance(ctx.Player.position, ctx.Vehicles.RemoteVehicle.Position) > 120 || !ctx.Vehicles.RemoteVehicle.Root.gameObject.activeInHierarchy))
            { RecallOrExit(); ctx.Say("Sem sinal.", ctx.Player.position + Vector3.up * 2.5f, 3); }
            if (DroneActive) Fly(dt);
            else if (mode == DroneMode.Returning) Return(dt);
            if (cameraDevice != null)
            {
                float sensitivity = ctx.UI.Sensitivity;
                pan = Mathf.Clamp(pan + ctx.Input.Look.x * 2.1f * sensitivity + ctx.Input.Move.x * dt * 60, -85, 85);
                tilt = Mathf.Clamp(tilt - ctx.Input.Look.y * 1.8f * sensitivity * (ctx.UI.InvertY ? -1 : 1) - ctx.Input.Move.y * dt * 42, -8.6f, 63);
            }
            if (mode != DroneMode.Stowed) foreach (var rotor in rotors) if (rotor) rotor.Rotate(0, dt * (mode == DroneMode.Landed ? 0 : 3400), 0, Space.Self);
            UpdateHacking();
        }
        void Fly(float dt)
        {
            droneYaw += ctx.Input.Look.x * 2.1f * ctx.UI.Sensitivity;
            dronePitch = Mathf.Clamp(dronePitch - ctx.Input.Look.y * 1.8f * ctx.UI.Sensitivity * (ctx.UI.InvertY ? -1 : 1), -42, 65);
            Vector3 direction = Quaternion.Euler(0, droneYaw, 0) * new Vector3(ctx.Input.Move.x, 0, ctx.Input.Move.y);
            direction.y = (ctx.Input.Brake ? 1 : 0) - (ctx.Input.Run ? 1 : 0);
            Vector3 desired = Vector3.ClampMagnitude(direction, 1) * 6.8f + ctx.Systems.Weather.Wind * .085f;
            velocity = Vector3.Lerp(velocity, desired, 1 - Mathf.Exp(-dt * 7));
            Vector3 previous = drone.position; Move(velocity * dt);
            if (Vector3.Distance(route[route.Count - 1], drone.position) > .70f && route.Count < 220) route.Add(drone.position);
            if (Vector3.Distance(drone.position, launchPoint) > 47.6f) { Recall(); ctx.Say("Sem sinal.", drone.position + Vector3.up); }
            drone.rotation = Quaternion.Euler(velocity.z * 1.25f, droneYaw, -velocity.x * 1.4f);
            if (direction.sqrMagnitude > .05f && (drone.position - previous).sqrMagnitude < .00001f) velocity *= .6f;
        }
        bool Move(Vector3 delta)
        {
            bool moved = false; int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / .18f)); Vector3 step = delta / steps;
            for (int i = 0; i < steps; i++) for (int axis = 0; axis < 3; axis++)
            {
                Vector3 part = Vector3.zero; part[axis] = step[axis]; if (part.sqrMagnitude < .0000001f) continue;
                Vector3 next = drone.position + part; float ground = Mathf.Max(-.43f, ctx.Ground(next.x, next.z));
                if ((ReferenceMap.Active ? !ReferenceMap.Contains(next) : next.x < -209 || next.x > 209 || next.z < -249 || next.z > 219) || next.y < ground + .34f || next.y > launchPoint.y + 23) continue;
                if (Physics.SphereCast(drone.position, .32f, part.normalized, out var hit, part.magnitude + .015f, ctx.AllMask, QueryTriggerInteraction.Ignore) || Physics.CheckSphere(next, .31f, ctx.AllMask, QueryTriggerInteraction.Ignore)) continue;
                drone.position = next; moved = true;
            }
            return moved;
        }
        void Return(float dt)
        {
            Vector3 target = route.Count > 0 ? route[route.Count - 1] : ctx.Player.position + Vector3.up * 1.86f;
            Vector3 delta = target - drone.position;
            if (delta.magnitude < .16f)
            {
                if (route.Count > 0) { route.RemoveAt(route.Count - 1); return; }
                if (Vector3.Distance(drone.position, ctx.Player.position + Vector3.up * 1.86f) < 1.1f) { Stow(); return; }
            }
            bool moved = Move(Vector3.ClampMagnitude(delta, dt * 8)); blockedTime = moved ? 0 : blockedTime + dt;
            if (delta.sqrMagnitude > .02f) drone.rotation = Quaternion.Slerp(drone.rotation, Quaternion.LookRotation(delta.normalized), dt * 4);
            if (blockedTime > 2)
            {
                float floor = Mathf.Max(-.40f, ctx.Ground(drone.position.x, drone.position.z)) + .35f;
                Move(Vector3.down * dt * 1.8f);
                if (drone.position.y <= floor + .08f || blockedTime > 5) { mode = DroneMode.Landed; drone.rotation = Quaternion.Euler(0, droneYaw, 0); ctx.Say("Drone pousado.", drone.position + Vector3.up * .7f, 5); }
            }
        }
        Vector3 DevicePoint(DeviceData device, Vector3 eye)
        {
            if (device.type == "light") return ctx.Systems.HackLightPoint(device, eye);
            if (device.type == "door") return ctx.Systems.HackDoorPoint(device, eye);
            Transform target = ctx.Node(device.node);
            if (!target) return device.position;
            if (device.type == "resident-phone") return target.position + Vector3.up * 1.65f;
            if (device.type == "vehicle" || device.type == "car")
            {
                return ctx.Vehicles.HackPoint(target);
            }
            if (device.type == "signal")
            {
                Vector3 point = target.position + Vector3.up * 3.1f, towardEye = eye - point; towardEye.y = 0;
                return point + towardEye.normalized * .30f;
            }
            return device.position;
        }
        public void ApplyCamera(Camera camera)
        {
            if (!Active || !camera) return;
            if (DroneActive)
            {
                Quaternion orientation = Quaternion.Euler(dronePitch, droneYaw, 0); Vector3 forward = orientation * Vector3.forward;
                Vector3 target = drone.position + Vector3.up * .12f, wish = target - forward * 2.1f + Vector3.up * .5f;
                Vector3 delta = wish - target;
                if (Physics.SphereCast(target, .10f, delta.normalized, out var hit, delta.magnitude, ctx.SolidMask, QueryTriggerInteraction.Ignore)) wish = target + delta.normalized * Mathf.Max(.16f, hit.distance - .05f);
                camera.transform.SetPositionAndRotation(wish, Quaternion.LookRotation(target + forward * 7 - wish)); camera.fieldOfView = ctx.UI.Fov;
            }
            else
            {
                float homeYaw = cameraDevice.lookAt != Vector3.zero ? Mathf.Atan2(cameraDevice.lookAt.x - cameraDevice.position.x, cameraDevice.lookAt.z - cameraDevice.position.z) * Mathf.Rad2Deg : cameraDevice.yaw * Mathf.Rad2Deg;
                Quaternion rotation = Quaternion.Euler(tilt, homeYaw + pan, 0);
                if (cameraHead) cameraHead.rotation = rotation;
                Transform lens = ctx.Node(cameraDevice.target); Vector3 position = lens ? lens.position : cameraDevice.position + rotation * Vector3.forward * .68f;
                camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = ctx.UI.Fov;
            }
        }
        void OnDestroy() { foreach (var m in ownedMaterials) if (m) Destroy(m); }
    }
}
