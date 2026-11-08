using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CitySystems
    {
        readonly List<DeviceData> hackDevices = new List<DeviceData>();
        readonly Dictionary<string, float> hackCooldowns = new Dictionary<string, float>();
        sealed class HackSound { public DeviceData device; public string sound; public float until, next; }
        readonly List<HackSound> hackSounds = new List<HackSound>();
        public IReadOnlyList<DeviceData> HackDevices => hackDevices;

        void InitializeHacking()
        {
            hackDevices.AddRange(ctx.Data.devices);
            foreach (var person in ctx.Data.residents)
                if (!person.child) hackDevices.Add(new DeviceData { id = "resident-phone-" + person.id, type = "resident-phone", node = person.id, target = person.id, range = 24 });
            if (lighting)
                for (int i = 0; i < lighting.fixtures.Length; i++)
                {
                    var fixture = lighting.fixtures[i]; if (!fixture.source || !fixture.anchor) continue;
                    hackDevices.Add(new DeviceData { id = "light-circuit-" + i, type = "light", target = i, position = fixture.source.transform.position, range = 24 });
                }
            hackDevices.Add(new DeviceData { id = "fountain-control", type = "fountain", position = ctx.Data.fountain + Vector3.up * 1.2f, range = 24 });
        }
        public bool HackDevicePresent(DeviceData device)
        {
            if (device == null) return false;
            if (device.type == "light") return lighting && device.target >= 0 && device.target < lighting.fixtures.Length &&
                lighting.fixtures[device.target].source && lighting.fixtures[device.target].anchor && lighting.fixtures[device.target].anchor.gameObject.activeInHierarchy &&
                (!lighting.fixtures[device.target].anchor.GetComponentInParent<CityDestructible>() || !lighting.fixtures[device.target].anchor.GetComponentInParent<CityDestructible>().Broken);
            var root = ctx.Node(device.node);
            if (root && !root.gameObject.activeInHierarchy) return false;
            if (device.type == "resident-phone")
            {
                var person = ctx.Residents.Find(device.node);
                return person != null && root && !person.Down && person.Bus == null && !person.Data.child;
            }
            if (device.type == "vehicle" || device.type == "car" || device.type == "brake") return ctx.Vehicles.FindHackVehicle(root) != null;
            return true;
        }
        public Vector3 HackLightPoint(DeviceData device, Vector3 eye)
        {
            if (!lighting || device.target < 0 || device.target >= lighting.fixtures.Length) return device.position;
            var source = lighting.fixtures[device.target].source;
            Vector3 point = source ? source.transform.position : device.position;
            return point + (eye - point).normalized * .25f;
        }
        Door HackDoor(DeviceData device)
        {
            foreach (var door in doors) if (door.data.id == device.node || door.data.id == device.target) return door;
            return null;
        }
        public Vector3 HackDoorPoint(DeviceData device, Vector3 eye)
        {
            var door = HackDoor(device); if (door == null) return device.position;
            // The electronic control belongs to the frame, not to the moving leaf.
            Vector3 point = door.origin + door.rotation * new Vector3(-door.data.width * .5f, 2.6f, 0);
            Vector3 normal = door.rotation * Vector3.forward;
            return point + normal * (Vector3.Dot(eye - point, normal) >= 0 ? .20f : -.20f);
        }
        public string HackDeviceName(DeviceData device)
        {
            switch (device.type)
            {
                case "resident-phone": return "CELULAR";
                case "vehicle": case "car": case "brake": return "VEÍCULO";
                case "light": return lighting.fixtures[device.target].place >= 0 ? "CIRCUITO" : "ILUMINAÇÃO";
                case "phone": return "TELEFONE";
                case "door": return "PORTA";
                case "camera": return "CÂMERA";
                case "signal": return "SEMÁFORO";
                case "harbor-crane": return "GUINDASTE";
                case "harbor-radio": return "RÁDIO";
                case "cinema": return "PROJETOR";
                case "fountain": case "fonte": return "FONTE";
                case "fire-water": return "ÁGUA";
                default: return device.label ?? "DISPOSITIVO";
            }
        }
        public void GetHackOptions(DeviceData device, List<CityHackOption> options)
        {
            options.Clear(); if (!HackDevicePresent(device)) return;
            switch (device.type)
            {
                case "camera": options.Add(new CityHackOption("camera-view", "Acessar", "")); break;
                case "vehicle": case "car": case "brake":
                    var car = ctx.Vehicles.FindHackVehicle(ctx.Node(device.node)); if (car == null) break;
                    options.Add(new CityHackOption("vehicle-brake", "Frear", "4 s", 4));
                    if (car.Data.kind != "bike" && car.Data.kind != "bus")
                        options.Add(new CityHackOption("vehicle-drive", "Dirigir", ctx.Vehicles.CanRemoteDrive(car) ? "" : "Indisponível", 1, ctx.Vehicles.CanRemoteDrive(car)));
                    options.Add(new CityHackOption("vehicle-horn", car.Data.kind == "bike" ? "Tocar campainha" : "Buzinar", "", 2));
                    bool alarming = ctx.Clock < car.AlarmUntil;
                    options.Add(new CityHackOption("vehicle-alarm", alarming ? "Desligar alarme" : "Disparar alarme", alarming ? "" : "7 s", .7f));
                    if (car.Data.kind != "bike" && car.Data.kind != "bus")
                    {
                        bool movable = ctx.Vehicles.CanHackMove(car);
                        string effect = movable ? "3 s" : "Pare o veículo";
                        options.Add(new CityHackOption("vehicle-forward", "Avançar", effect, 3, movable));
                        options.Add(new CityHackOption("vehicle-reverse", "Dar ré", effect, 3, movable));
                    }
                    break;
                case "door":
                    var door = HackDoor(device); if (door == null) break;
                    bool locked = ctx.Clock < door.lockedUntil;
                    if (locked) options.Add(new CityHackOption("door-lock", "Destrancar", "", .7f));
                    options.Add(new CityHackOption("door-open", "Abrir", locked ? "Trancada" : "10 s", 1, !locked));
                    options.Add(new CityHackOption("door-close", "Fechar", "8 s", 1));
                    if (!locked) options.Add(new CityHackOption("door-lock", "Trancar", "10 s", .7f));
                    break;
                case "light":
                    bool dark = lighting.CircuitOff(device.target);
                    options.Add(new CityHackOption("light-off", "Apagar", dark ? "Sem energia" : "14 s", 2, !dark));
                    options.Add(new CityHackOption("light-on", "Religar", dark ? "" : "Já ligado", .7f, dark)); break;
                case "resident-phone":
                    var person = ctx.Residents.Find(device.node);
                    if (person.Calling) options.Add(new CityHackOption("phone-jam", "Interromper denúncia", "", 3,
                        ctx.Police.CanInterruptCallFrom(ctx.Remote.HackOrigin, ctx.Remote.HackRange)));
                    bool freePhone = !person.Controlled && !person.Crossing && person.Health >= 99.99f && person.FearUntil <= ctx.Clock && !person.Calling;
                    options.Add(new CityHackOption("resident-ring", "Tocar", freePhone ? "" : "Ocupado", 6, freePhone));
                    break;
                case "phone":
                    options.Add(new CityHackOption("phone-ring", "Tocar", "", 3));
                    options.Add(new CityHackOption("phone-silence", "Silenciar", "", .7f)); break;
                case "signal":
                    options.Add(new CityHackOption("signal-stop", "Parar trânsito", "7 s", 2));
                    options.Add(new CityHackOption("signal-restore", "Restaurar", "", 1)); break;
                case "harbor-crane":
                    options.Add(new CityHackOption("crane-up", "Içar", "", 2, craneTarget < .5f));
                    options.Add(new CityHackOption("crane-down", "Baixar", "", 2, craneTarget > .5f)); break;
                case "harbor-radio":
                    options.Add(new CityHackOption("radio-tune", "Sintonizar", "", 1));
                    options.Add(new CityHackOption("radio-lure", "Interferência", "", 5)); break;
                case "cinema":
                    options.Add(new CityHackOption("cinema-toggle", CinemaPlaying ? "Desligar" : "Ligar", "", 1));
                    options.Add(new CityHackOption("cinema-lure", "Acionar motor", "", 5)); break;
                case "fountain": case "fonte":
                    options.Add(new CityHackOption("fountain-toggle", FountainOn ? "Desligar" : "Ligar", "", 1));
                    options.Add(new CityHackOption("fountain-lure", "Acionar bomba", "", 5)); break;
                case "fire-water":
                    options.Add(new CityHackOption("water-on", "Ligar", "", 1));
                    options.Add(new CityHackOption("water-off", "Desligar", "", 1)); break;
            }
        }
        public float HackCooldown(DeviceData device, string action) =>
            device != null && hackCooldowns.TryGetValue(device.id + "/" + action, out float until) ? Mathf.Max(0, until - ctx.Clock) : 0;
        readonly List<CityHackOption> executingOptions = new List<CityHackOption>(6);
        public bool ExecuteHack(DeviceData device, string action)
        {
            if (ctx.Paused || !HackDevicePresent(device) || HackCooldown(device, action) > 0) return false;
            GetHackOptions(device, executingOptions); bool found = false; CityHackOption option = default;
            foreach (var candidate in executingOptions) if (candidate.Id == action && candidate.Available) { found = true; option = candidate; break; }
            if (!found) return false;
            var root = ctx.Node(device.node); var door = device.type == "door" ? HackDoor(device) : null;
            bool success = true;
            switch (action)
            {
                case "vehicle-brake": success = ctx.Vehicles.RemoteBrake(root, 4); break;
                case "vehicle-drive": success = ctx.Remote.ConnectVehicle(root); break;
                case "vehicle-horn": success = ctx.Vehicles.HackHorn(root); break;
                case "vehicle-alarm": success = ctx.Vehicles.HackAlarm(root); break;
                case "vehicle-forward": case "vehicle-reverse": success = ctx.Vehicles.HackMove(root, action == "vehicle-forward" ? 1 : -1); break;
                case "door-open": door.closedUntil = 0; door.forced = door.hold = ctx.Clock + 10; PlaySound("door-slide", device.position); break;
                case "door-close": door.forced = door.hold = 0; door.closedUntil = ctx.Clock + 8; break;
                case "door-lock":
                    bool locked = ctx.Clock < door.lockedUntil;
                    door.lockedUntil = locked ? 0 : ctx.Clock + 10; door.closedUntil = 0; door.forced = door.hold = 0;
                    if (!locked) ctx.Residents.ReactToHack(device.position, "Trancou?", false, 7);
                    break;
                case "light-off":
                    success = lighting.Blackout(device.target, 14);
                    if (success) ctx.Residents.ReactToHack(device.position, "Caiu a luz?", true, 16);
                    break;
                case "light-on": success = lighting.RestoreCircuit(device.target); break;
                case "resident-ring": success = ctx.Residents.HackPhone(ctx.Residents.Find(device.node)); if (success) PlaySound("phone", device.position); break;
                case "phone-jam": success = ctx.Police.InterruptCallFrom(ctx.Remote.HackOrigin, ctx.Remote.HackRange); break;
                case "phone-ring":
                    StartHackSound(device, "phone", 5); ctx.Residents.ReactToHack(device.position, null, true, 13); break;
                case "phone-silence": StopHackSound(device); break;
                case "signal-stop": signalOverride[device.target] = ctx.Clock + 7; break;
                case "signal-restore": signalOverride.Remove(device.target); break;
                case "crane-up": case "crane-down":
                    craneTarget = action == "crane-up" ? 1 : 0; PlaySound("harbor-winch", device.position);
                    ctx.Residents.ReactToHack(device.position, null, false, 15); break;
                case "radio-tune": ToggleRadio(); break;
                case "radio-lure": StartHackSound(device, "register", 4); ctx.Residents.ReactToHack(device.position, null, true, 13); break;
                case "cinema-toggle": ToggleCinema(); ctx.Residents.ReactToHack(device.position, null, false, 10); break;
                case "cinema-lure": StartHackSound(device, "film-reel", 3); ctx.Residents.ReactToHack(device.position, null, true, 12); break;
                case "fountain-toggle": FountainOn = !FountainOn; ctx.Residents.ReactToHack(device.position, null, false, 10); break;
                case "fountain-lure": PlaySound("water", device.position); ctx.Residents.ReactToHack(device.position, null, true, 12); break;
                case "water-on": case "water-off": foreach (var jet in waterJets) jet.gameObject.SetActive(action == "water-on"); break;
                default: success = false; break;
            }
            if (!success) return false;
            hackCooldowns[device.id + "/" + action] = ctx.Clock + option.Cooldown;
            return true;
        }
        void StartHackSound(DeviceData device, string sound, float seconds)
        {
            StopHackSound(device); hackSounds.Add(new HackSound { device = device, sound = sound, until = ctx.Clock + seconds, next = ctx.Clock + 1.1f });
            PlaySound(sound, device.position, .7f);
        }
        void StopHackSound(DeviceData device)
        {
            for (int i = hackSounds.Count - 1; i >= 0; i--) if (hackSounds[i].device == device) hackSounds.RemoveAt(i);
            if (Audio) Audio.StopHackSound(device.position);
        }
        void TickHackEffects()
        {
            for (int i = hackSounds.Count - 1; i >= 0; i--)
            {
                var sound = hackSounds[i]; if (ctx.Clock >= sound.until || !HackDevicePresent(sound.device)) { if(Audio)Audio.StopHackSound(sound.device.position);hackSounds.RemoveAt(i); continue; }
                if (ctx.Clock < sound.next) continue; sound.next = ctx.Clock + 1.1f; PlaySound(sound.sound, sound.device.position, .7f);
            }
        }
        public float HackVisibility(Vector3 point) => ctx.Night > .25f && lighting && lighting.BlackoutAt(point) ? .55f : 1;
    }
}
