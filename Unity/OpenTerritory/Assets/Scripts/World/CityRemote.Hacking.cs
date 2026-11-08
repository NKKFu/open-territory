using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityRemote
    {
        readonly List<CityHackOption> hackOptions = new List<CityHackOption>(6);
        readonly Dictionary<string, string> preferredHacks = new Dictionary<string, string>();
        LineRenderer hackMarker;
        readonly Vector3[] markerPoints = new Vector3[4];
        int hackIndex;
        float lastHackFeedback;
        public bool HackerMode { get; private set; }
        public DeviceData SelectedDevice => linked;
        public int ActionCount => hackOptions.Count;
        public string SelectedHack => hackOptions.Count > hackIndex ? hackOptions[hackIndex].Id : null;
        public string HackPreview { get; private set; } = "";
        public bool PreviewVisible { get; private set; }
        public CityHackOption CurrentHackOption => hackOptions.Count > hackIndex ? hackOptions[hackIndex] : default;
        public int CurrentHackIndex => hackIndex;
        public string SelectedDeviceName => linked != null ? ctx.Systems.HackDeviceName(linked) : "";
        public float SelectedCooldown => linked != null ? ctx.Systems.HackCooldown(linked, SelectedHack) : 0;
        public int SuccessfulHacks { get; private set; }
        public float HackRange => DroneActive ? 28 : cameraDevice != null ? 42 : 24;
        public Vector3 HackOrigin => VehicleActive ? ctx.Vehicles.HackPoint(ctx.Vehicles.RemoteVehicle.Root) : Active ? ctx.Camera.transform.position : ctx.Player.position + Vector3.up * 1.68f;
        public Vector3 TargetPoint(DeviceData device, Vector3 eye) => DevicePoint(device, eye);

        void BuildHackMarker()
        {
            var marker = new GameObject("Alvo da conexão"); marker.transform.SetParent(transform, false);
            hackMarker = marker.AddComponent<LineRenderer>(); hackMarker.useWorldSpace = true;
            hackMarker.loop = true; hackMarker.positionCount = 4; hackMarker.widthMultiplier = .025f;
            var material = new Material(Shader.Find("Sprites/Default")); ownedMaterials.Add(material);
            hackMarker.sharedMaterial = material; hackMarker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hackMarker.receiveShadows = false; hackMarker.enabled = false;
        }
        public void SetHackerMode(bool enabled)
        {
            HackerMode = enabled;
            if (!enabled) { Scanning = false; HideHackPreview(); }
        }
        void HideHackPreview()
        {
            PreviewVisible = false;
            if (hackMarker) hackMarker.enabled = false;
        }
        bool CanUseHacking => !ctx.Paused && !ctx.Combat.Down && !ctx.Police.Custody &&
            !ctx.Motor.Swimming && !ctx.Combat.IsThreatening();

        void UpdateHacking()
        {
            if (ctx.Input.HackerToggle) SetHackerMode(!HackerMode);
            Scanning = HackerMode && CanUseHacking;
            if (!CanUseHacking) { linked = null; hackOptions.Clear(); HideHackPreview(); return; }
            if (!Scanning && !ctx.Input.HackPressed) { HideHackPreview(); return; }

            // Both modes resolve through this same selector. Only the F down edge executes.
            SelectHackTarget();
            if (Scanning && ctx.Input.HackNext && hackOptions.Count > 1)
            {
                hackIndex = (hackIndex + 1) % hackOptions.Count;
                preferredHacks[linked.id] = hackOptions[hackIndex].Id;
            }
            if (ctx.Input.HackPressed) ExecuteSelectedHack();
            if (Scanning) DrawHackPreview(); else HideHackPreview();
        }
        void SelectHackTarget()
        {
            Vector3 eye = HackOrigin;
            float range = HackRange;
            DeviceData best = null; float score = float.MaxValue;
            foreach (var device in ctx.Systems.HackDevices)
            {
                if (device == cameraDevice || VehicleActive && device.node == ctx.Vehicles.RemoteVehicle.Data.id || !ctx.Systems.HackDevicePresent(device)) continue;
                Vector3 point = DevicePoint(device, eye);
                float distance = Vector3.Distance(eye, point);
                if (distance > Mathf.Min(range, device.range)) continue;
                var screen = ctx.Camera.WorldToViewportPoint(point);
                if (screen.z < .1f || screen.x < .03f || screen.x > .97f || screen.y < .04f || screen.y > .96f) continue;
                float offCenter = new Vector2((screen.x - .5f) * ctx.Camera.aspect, screen.y - .5f).sqrMagnitude;
                if (offCenter > .32f * .32f) continue;
                float rank = offCenter + distance * .00035f;
                if (device == linked) rank *= .86f;
                if (rank >= score || !ctx.Visible(eye, point) || !ctx.Visible(ctx.Camera.transform.position, point)) continue;
                best = device; score = rank; device.position = point;
            }
            linked = best; hackOptions.Clear(); hackIndex = 0;
            if (linked == null) return;
            ctx.Systems.GetHackOptions(linked, hackOptions);
            for (int i = 0; i < hackOptions.Count; i++) if (hackOptions[i].Available) { hackIndex = i; break; }
            if (preferredHacks.TryGetValue(linked.id, out var preferred))
                for (int i = 0; i < hackOptions.Count; i++) if (hackOptions[i].Id == preferred) { hackIndex = i; break; }
            if (!Scanning && hackOptions.Count > 0 && !hackOptions[hackIndex].Available)
                for (int i = 0; i < hackOptions.Count; i++) if (hackOptions[i].Available) { hackIndex = i; break; }
        }
        void ExecuteSelectedHack()
        {
            if (linked == null || hackOptions.Count == 0)
            {
                if (!HackerMode && ctx.Clock > lastHackFeedback + .6f) ctx.Say("Sem alvo", ctx.Player.position + Vector3.up * 2.5f, 1.3f);
                lastHackFeedback = ctx.Clock; return;
            }
            var option = hackOptions[hackIndex];
            float cooldown = ctx.Systems.HackCooldown(linked, option.Id);
            if (!option.Available || cooldown > 0)
            {
                if (!HackerMode) ctx.Say(cooldown > 0 ? Mathf.CeilToInt(cooldown) + " s" : string.IsNullOrEmpty(option.Effect) ? "Indisponível" : option.Effect, linked.position + Vector3.up * .4f, 1.4f); return;
            }
            bool success;
            if (option.Id == "camera-view") { Connect(linked); success = true; }
            else success = ctx.Systems.ExecuteHack(linked, option.Id);
            if (!success) return;
            SuccessfulHacks++; ctx.Sound("hack", linked != null ? linked.position : Position, .45f);
            // Connect changes the viewpoint and clears the old target.
            if (HackerMode) SelectHackTarget();
        }
        void DrawHackPreview()
        {
            PreviewVisible = true;
            if (linked == null || hackOptions.Count == 0)
            {
                HackPreview = "Buscando…\n[Z]";
                hackMarker.enabled = false; return;
            }
            var action = hackOptions[hackIndex]; float cooldown = ctx.Systems.HackCooldown(linked, action.Id);
            bool ready = action.Available && cooldown <= 0;
            var color = ready ? new Color(.52f, 1, .80f) : new Color(1, .76f, .39f);
            string status = cooldown > 0 ? Mathf.CeilToInt(cooldown) + " s" : action.Effect;
            HackPreview = ctx.Systems.HackDeviceName(linked) + "\n[F] " + action.Label + "\n" + status +
                (hackOptions.Count > 1 ? "\n[Tab] " + (hackIndex + 1) + "/" + hackOptions.Count + "   [Z]" : "\n[Z]");
            float scale = Mathf.Clamp(Vector3.Distance(ctx.Camera.transform.position, linked.position) * .12f, .45f, 7);
            Vector3 center = linked.position, right = ctx.Camera.transform.right * .21f * scale, up = ctx.Camera.transform.up * .21f * scale;
            markerPoints[0] = center + up; markerPoints[1] = center + right; markerPoints[2] = center - up; markerPoints[3] = center - right;
            hackMarker.SetPositions(markerPoints); hackMarker.startColor = hackMarker.endColor = color; hackMarker.enabled = true;
        }
        public bool ConnectVehicle(Transform root)
        {
            var car = ctx.Vehicles.FindHackVehicle(root); if (!ctx.Vehicles.CanRemoteDrive(car)) return false;
            if (Active) RecallOrExit();
            if (!ctx.Vehicles.BeginRemoteDrive(root)) return false;
            ctx.Motor.SetRemote(true); ctx.Combat.Holster(); ctx.Follow.ResetTracking(); ctx.Follow.Face(car.Root.eulerAngles.y);
            ctx.Residents.ReactToHack(ctx.Vehicles.HackPoint(car.Root), null, false, 12);
            linked = null; hackOptions.Clear(); HideHackPreview(); return true;
        }
    }
}
