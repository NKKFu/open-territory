using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Opt-in only: --city-driving-qa / --city-vehicle-impact-qa,
    // or explicitly attached in Editor Play Mode.
    // Uses the production input poller, vehicle actions, tyres and physics solver.
    [DefaultExecutionOrder(-1200)]
    public sealed partial class CityDrivingProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; public float value; }
        [Serializable] public sealed class ModelSample
        {
            public string model;
            public float mass, speedAt2, speedAt4, zeroTo60, brakeDistance, wetBrakeDistance, turnDegrees, turnSpeed, handbrakeSlip, recoveredSlip;
            public float poweredTurnDegrees, poweredTurnSpeed, turnInDegrees, unassistedTurnInDegrees, highSpeedYaw;
        }
        [Serializable] public sealed class Sample
        {
            public string phase; public float time, speed, lateral, yaw, steering, slip, pitch, roll;
            public int contacts; public Vector3 position;
        }
        [Serializable] public sealed class Report
        {
            public bool passed = true, controlsOnly; public string unity, layout;
            public List<Check> checks = new List<Check>();
            public List<ModelSample> models = new List<ModelSample>();
            public List<Sample> samples = new List<Sample>();
            public List<ImpactSample> impacts = new List<ImpactSample>();
            public List<string> errors = new List<string>();
        }
        public string OutputDirectory, Phase = "waiting";
        public bool Finished { get; private set; }
        public bool ImpactOnly, ControlsOnly, WallPlanterOnly;
        readonly Report report = new Report();
        CityContext ctx; CityVehicles.Vehicle car; Vector3 start; Quaternion heading;
        readonly ParticleSystem.Particle[] smokeBuffer=new ParticleSystem.Particle[640];
        Vector2 move; bool brake, handbrake, interact, populationEnabled; float began, nextSample;
        GameObject barrier;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-driving-qa") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--city-vehicle-impact-qa") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--city-driving-controls-qa") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--city-wall-planter-qa") >= 0)
                new GameObject("QA direção · opt-in").AddComponent<CityDrivingProbe>();
        }
        void Awake() { ImpactOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "--city-vehicle-impact-qa") >= 0; ControlsOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "--city-driving-controls-qa") >= 0; WallPlanterOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "--city-wall-planter-qa") >= 0; report.controlsOnly = ControlsOnly; began = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; ClearInput(); if (barrier) Destroy(barrier); }
        void Log(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + trace); }
        void CheckResult(string name, bool passed, float value = 0, string detail = "")
        { AddCheck(report, name, passed, value, detail); Debug.Log("DRIVING QA " + (passed ? "PASS " : "FAIL ") + name + " " + value + " " + detail); }
        static void AddCheck(Report result, string name, bool passed, float value = 0, string detail = "")
        { result.passed &= passed; result.checks.Add(new Check { name = name, passed = passed, value = value, detail = detail }); }
        void ClearInput()
        { move = Vector2.zero; brake = handbrake = interact = false; if (ctx) { ctx.Input.TouchMove = ctx.Input.Drive = ctx.Input.Move = Vector2.zero; ctx.Input.Brake = ctx.Input.Handbrake = false; ctx.Input.TouchBrake = ctx.Input.TouchHandbrake = ctx.Input.TouchInteract = false; } }
        void Update()
        {
            if (Finished) return;
            if (Time.realtimeSinceStartup - began > 180) { CheckResult("probe-timeout", false); StopAllCoroutines(); Finish(); return; }
            if (!ctx) return;
            ctx.Input.TouchMove = move; ctx.Input.TouchBrake = brake; ctx.Input.TouchHandbrake = handbrake;
            if (interact) { ctx.Input.TouchInteract = true; interact = false; }
            if (car != null && Time.unscaledTime >= nextSample)
            {
                nextSample = Time.unscaledTime + .1f;
                report.samples.Add(new Sample { phase = Phase, time = ctx.Clock, speed = car.Speed, lateral = car.Motion.Lateral,
                    yaw = car.Body.angularVelocity.y, steering = car.Motion.Steering, slip = car.Motion.Slip,
                    pitch = Mathf.DeltaAngle(0, car.Root.eulerAngles.x), roll = Mathf.DeltaAngle(0, car.Root.eulerAngles.z),
                    contacts = car.WheelGrounded.Count(a => a), position = car.Position });
            }
        }
        static CityVehicleMath.Controls Controls(VehicleData definition)
        {
            return new CityVehicleMath.Controls { Throttle = 1, Wheelbase = definition.wheelbase, RearOffset = definition.rearOffset,
                MassScale = definition.massScale, DriveScale = definition.driveScale, SteeringLock = definition.maxSteering,
                FrontWeight = definition.frontWeight, FrontDrive = definition.frontDrive, TopSpeed = definition.kind == "bus" ? 16 : CityVehicleMath.DriverTopSpeed,
                FrontGrip = 1, RearGrip = 1, DriverAssist = true };
        }
        static float StopDistance(CityVehicleMath.Controls input, float grip)
        {
            input.Throttle = 0; input.Brake = 1; input.FrontGrip = input.RearGrip = grip;
            var state = new CityVehicleMath.State { Speed = 20, Direction = 1, Gear = 3 }; float distance = 0;
            for (int i = 0; i < 400 && state.Speed > .01f; i++) { distance += state.Speed * .01f; state = CityVehicleMath.Step(state, input, .01f); }
            return distance;
        }
        static CityVehicleMath.State Curve(CityVehicleMath.Controls input, float step, out float yaw, float speed = 15, float seconds = 2)
        {
            var state = new CityVehicleMath.State { Speed = speed, Direction = 1, Gear = 2 };
            yaw = 0; for (int i = 0; i < Mathf.RoundToInt(seconds / step); i++) { state = CityVehicleMath.Step(state, input, step); yaw += state.DeltaYaw; }
            return state;
        }
        public static string VerifyModel(WorldData world)
        { var result = new Report { unity = Application.unityVersion, layout = world.layoutId }; ModelChecks(result, world); return JsonUtility.ToJson(result, true); }
        static void ModelChecks(Report result, WorldData world)
        {
            foreach (var definition in world.vehicles.Where(v => v.kind == "car").GroupBy(v => v.model + ":" + v.mass).Select(g => g.First()))
            {
                string name = definition.model + "-" + definition.mass;
                var input = Controls(definition); var state = new CityVehicleMath.State { Direction = 1, Gear = 1 };
                var sample = new ModelSample { model = name, mass = definition.mass, zeroTo60 = -1 };
                for (int i = 1; i <= 600; i++)
                {
                    state = CityVehicleMath.Step(state, input, .02f);
                    if (i == 100) sample.speedAt2 = state.Speed; if (i == 200) sample.speedAt4 = state.Speed;
                    if (sample.zeroTo60 < 0 && state.Speed >= 16.667f) sample.zeroTo60 = i * .02f;
                }
                AddCheck(result, name + "/launch", sample.speedAt2 > 4 && sample.zeroTo60 > 0 && sample.zeroTo60 < 9, sample.zeroTo60, "seconds 0–60 km/h");
                AddCheck(result, name + "/stronger-arcade-launch", sample.speedAt2 > (definition.massScale > 1.6f ? 8 : 13) && sample.zeroTo60 < (definition.massScale > 1.6f ? 4 : 2.9f), sample.zeroTo60, "seconds 0–60 km/h");
                AddCheck(result, name + "/top-speed-bounded", state.Speed > (definition.massScale > 1.6f ? 32 : 37) && state.Speed <= CityVehicleMath.DriverTopSpeed+.1f, state.Speed);
                sample.brakeDistance = StopDistance(input, 1); sample.wetBrakeDistance = StopDistance(input, .71f);
                AddCheck(result, name + "/braking", sample.brakeDistance > 12 && sample.brakeDistance < 34, sample.brakeDistance, "metres from 72 km/h");
                AddCheck(result, name + "/wet-road-has-less-grip", sample.wetBrakeDistance > sample.brakeDistance * 1.12f, sample.wetBrakeDistance);
                input.Throttle = .3f; input.Steer = .5f;
                float yaw; state = Curve(input, .02f, out yaw); sample.turnDegrees = yaw * Mathf.Rad2Deg; sample.turnSpeed = state.Speed;
                AddCheck(result, name + "/responsive-curve", sample.turnDegrees > 28 && sample.turnDegrees < 115 && state.Speed > 9, sample.turnDegrees);
                input.Steer = -.5f; float oppositeYaw; var opposite = Curve(input, .02f, out oppositeYaw);
                AddCheck(result, name + "/left-right-symmetry", Mathf.Abs(yaw + oppositeYaw) < .002f && Mathf.Abs(state.Speed - opposite.Speed) < .002f, yaw + oppositeYaw);
                input.Steer = .5f; float fineYaw; var fine = Curve(input, .01f, out fineYaw);
                AddCheck(result, name + "/fixed-step-convergence", Mathf.Abs(fine.Speed - state.Speed) < .08f && Mathf.Abs(fineYaw - yaw) < .015f, Mathf.Abs(fineYaw - yaw));
                input.Throttle = 1; input.Steer = 1;
                float poweredYaw; var powered = Curve(input, .02f, out poweredYaw);
                sample.poweredTurnDegrees = poweredYaw * Mathf.Rad2Deg; sample.poweredTurnSpeed = powered.Speed;
                float immediateYaw; Curve(input, .02f, out immediateYaw, 15, .5f); sample.turnInDegrees = immediateYaw * Mathf.Rad2Deg;
                input.DriverAssist = false; float unassistedYaw; Curve(input, .02f, out unassistedYaw, 15, .5f);
                sample.unassistedTurnInDegrees = unassistedYaw * Mathf.Rad2Deg;
                AddCheck(result, name + "/powered-turn-in-at-least-50-percent-faster", immediateYaw > unassistedYaw * 1.5f, sample.turnInDegrees, "degrees in 0.5 s at 54 km/h");
                AddCheck(result, name + "/full-throttle-closes-curve", sample.poweredTurnDegrees > 65 && sample.poweredTurnDegrees < 125 && powered.Speed > 12, sample.poweredTurnDegrees);
                input.DriverAssist = true;
                float fastYaw; var fast = Curve(input, .02f, out fastYaw, 30, .8f); sample.highSpeedYaw = fast.YawRate;
                AddCheck(result, name + "/high-speed-steering-bounded", Mathf.Abs(fast.YawRate) < .7f && Mathf.Abs(fast.Lateral) < 2.5f && fast.Speed > 25, fast.YawRate);
                input.FrontGrip = input.RearGrip = .71f;
                float wetYaw; var wet = Curve(input, .02f, out wetYaw, 30, .8f);
                AddCheck(result, name + "/wet-corner-retains-weather-effect", Mathf.Abs(wet.YawRate) < Mathf.Abs(fast.YawRate) * .9f, wet.YawRate, "radians/s at 108 km/h");
                input.FrontGrip = input.RearGrip = 1;
                input.Throttle = .3f;
                input.Handbrake = 1; input.Steer = .65f; state = new CityVehicleMath.State { Speed = 15, Gear = 2, Direction = 1 };
                for (int i = 0; i < 35; i++) { state = CityVehicleMath.Step(state, input, .02f); sample.handbrakeSlip = Mathf.Max(sample.handbrakeSlip, state.Slip); }
                input.Handbrake = 0; input.Steer = 0; input.Throttle = .35f;
                for (int i = 0; i < 140; i++) state = CityVehicleMath.Step(state, input, .02f);
                sample.recoveredSlip = state.Slip;
                AddCheck(result, name + "/handbrake-slide-recovers", sample.handbrakeSlip > .12f && state.Slip < .12f && Mathf.Abs(state.YawRate) < .15f, state.Slip);
                input = Controls(definition); input.Throttle = -1; state = new CityVehicleMath.State { Speed = 12, Gear = 2, Direction = 1 };
                bool safeShift = true;
                for (int i = 0; i < 300; i++) { float speed = state.Speed; state = CityVehicleMath.Step(state, input, .02f); if (state.Direction < 0 && speed > .15f) safeShift = false; }
                AddCheck(result, name + "/reverse-brakes-before-shifting", safeShift && state.Direction == -1 && state.Speed < -2 && state.Speed > -7.1f, state.Speed);
                input.Throttle = 0; input.Steer = 0; input.FrontGrip = input.RearGrip = 0;
                var before = new CityVehicleMath.State { Speed = 20, Lateral = 2, YawRate = .5f, Direction = 1, Gear = 3 };
                var after = CityVehicleMath.Step(before, input, .02f); Vector2 change = CityVehicleMath.VelocityChange(before, after);
                AddCheck(result, name + "/world-impulse-does-not-double-rotation", Mathf.Abs(change.x) < .015f && change.y <= .001f && change.y > -.15f, change.x);
                result.models.Add(sample);
            }
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            if (string.IsNullOrEmpty(OutputDirectory)) OutputDirectory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/vehicles/editor"));
            Directory.CreateDirectory(OutputDirectory);
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup - began < 60) yield return null;
            ctx = CityContext.Instance; CheckResult("native-context-ready", ctx && ctx.Ready);
            if (!ctx || !ctx.Ready) { Finish(); yield break; }
            report.unity = Application.unityVersion; report.layout = ctx.Data.layoutId; ModelChecks(report, ctx.Data);
            populationEnabled = ctx.Population.enabled; ctx.Population.enabled = false; ctx.Combat.Holster(); ctx.UI.SetSettings(false);
            var road = ReferenceMap.Layout.roads.First(r => r.id == "101-inland"); int index = road.points.Length / 4;
            start = road.points[index]; heading = Quaternion.LookRotation(CityPhysics.Flat(road.points[index + 4] - road.points[index - 4]).normalized);
            start.y = ctx.Ground(start.x, start.z) + .07f;
            var definition = ctx.Data.vehicles.First(v => !v.auto && v.kind == "car" && v.model == "sedã");
            car = ctx.Vehicles.Fleet.FirstOrDefault(v => v.Data.id == definition.id) ?? ctx.Vehicles.Spawn(definition, start, heading);
            ResetCar(0); Phase = "enter";
            Vector3 entrance = car.Root.TransformPoint(new Vector3(-car.Data.width * .5f - .75f, 0, car.Data.seatMount.z)); entrance.y = ctx.Ground(entrance.x, entrance.z) + .05f;
            ctx.Motor.Warp(entrance); ctx.Follow.enabled = false;
            Vector3 eye = entrance + Vector3.up * 1.8f;
            ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(car.Root.TransformPoint(new Vector3(0, 1, car.Data.seatMount.z)) - eye));
            yield return new WaitForSeconds(.5f); ctx.RefreshAction();
            CheckResult("E-selects-vehicle", ctx.CurrentAction != null && ctx.CurrentAction.id == "vehicle-" + car.Data.id);
            interact = true; yield return null;
            float entryUntil = Time.time + 8;
            while ((interact || ctx.Input.TouchInteract || ctx.Motor.Busy) && Time.time < entryUntil) yield return null;
            CheckResult("E-enters-driver-seat", ctx.Vehicles.Active == car && ctx.Motor.Riding == car.Root && !ctx.Motor.Busy);
            ctx.Follow.enabled = true; ctx.Follow.Face(car.Root.eulerAngles.y); ctx.Follow.ResetTracking();
            if (ctx.Vehicles.Active != car) { Finish(); yield break; }
            if (WallPlanterOnly) { yield return SidewallCases(); yield return EntrancePlanterCases(); }
            else
            {
                if (!ImpactOnly) yield return DrivingCases();
                if (!ControlsOnly) yield return ImpactCases();
            }
            Phase = "exit"; ResetCar(0); yield return new WaitForSeconds(.4f); interact = true; yield return new WaitForSeconds(1.3f);
            CheckResult("E-exits-after-driving", ctx.Vehicles.Active == null && !ctx.Motor.Riding);
            CheckResult("no-runtime-errors", report.errors.Count == 0, report.errors.Count); Finish();
        }
        void ResetCar(float speed, float height = 0)
        {
            ClearInput(); car.Body.position = start + Vector3.up * height; car.Body.rotation = heading;
            car.Root.SetPositionAndRotation(car.Body.position, heading); car.Body.linearVelocity = heading * Vector3.forward * speed; car.Body.angularVelocity = Vector3.zero;
            car.Motion = new CityVehicleMath.State { Speed = speed, Direction = speed < 0 ? -1 : 1, Gear = speed < 0 ? -1 : speed > 9 ? 2 : 1 }; car.JamUntil = 0; car.LastUsed = ctx.Clock;
            car.WallNormal = Vector3.zero; car.WallContactAt = -1000;
            car.Body.WakeUp(); Physics.SyncTransforms(); ctx.Follow.Face(heading.eulerAngles.y); ctx.Follow.ResetTracking();
        }
        IEnumerator DrivingCases()
        {
            var effects=ctx.Vehicles.Effects;
            CheckResult("native-tyre-effects-ready",effects&&effects.Smoke&&effects.MarkMesh);
            int launchParticles=effects.LaunchParticles;
            Phase = "launch"; ResetCar(0); yield return new WaitForSeconds(.5f); move = Vector2.up;
            yield return new WaitForSeconds(.85f);
            CheckResult("hard-launch-produces-tyre-smoke",effects.LaunchParticles>launchParticles+2&&effects.ActiveParticles>0,effects.LaunchParticles-launchParticles);
            int particles=effects.Smoke.GetParticles(smokeBuffer);float behind=0;
            for(int i=0;i<particles;i++)behind=Mathf.Max(behind,Vector3.Dot(car.Position-smokeBuffer[i].position,car.Root.forward));
            CheckResult("smoke-leaves-a-world-space-trail",behind>.4f,behind);
            yield return Capture("01a-fumaca-na-arrancada");
            yield return new WaitForSeconds(2.15f); float launch = car.Speed;
            CheckResult("W-accelerates-native-body", launch > 20 && launch < 30, launch, "m/s after 3 s");
            CheckResult("four-wheels-supported", car.WheelGrounded.Count(a => a) >= 3, car.WheelGrounded.Count(a => a));
            CheckResult("body-weight-remains-controlled", Vector3.Angle(car.Root.up, Vector3.up) < 12, Vector3.Angle(car.Root.up, Vector3.up));
            yield return Capture("01-aceleracao");
            Phase = "braking"; move = Vector2.zero; brake = true; Vector3 stopping = car.Position;
            int brakeParticles=effects.BrakeParticles,marks=effects.MarkSegments;
            yield return new WaitForSeconds(.35f);
            CheckResult("hard-braking-produces-smoke-and-rubber",effects.BrakeParticles>brakeParticles&&effects.MarkSegments>marks,effects.MarkSegments-marks);
            float tyreVolume=0;
            foreach(var source in car.Root.GetComponentsInChildren<AudioSource>())
                if(source.clip&&source.clip.name.IndexOf("skid",StringComparison.OrdinalIgnoreCase)>=0&&source.isPlaying)tyreVolume=Mathf.Max(tyreVolume,source.volume);
            CheckResult("hard-braking-produces-tyre-sound",tyreVolume>.005f,tyreVolume);
            CheckEffectGeometry(effects);
            yield return Capture("02a-fumaca-e-marcas-na-frenagem");
            yield return new WaitForSeconds(2.05f);
            CheckResult("Space-stops-without-reversing", Mathf.Abs(car.Speed) < .2f && car.Motion.Direction > 0, car.Speed);
            CheckResult("service-brake-distance", CityPhysics.Flat(car.Position - stopping).magnitude < 28, CityPhysics.Flat(car.Position - stopping).magnitude);
            yield return Capture("02-frenagem");
            int idleEmissions=effects.LaunchParticles+effects.BrakeParticles+effects.SlideParticles;
            yield return new WaitForSeconds(1.7f);
            CheckResult("stopped-car-does-not-emit-smoke",idleEmissions==effects.LaunchParticles+effects.BrakeParticles+effects.SlideParticles&&effects.ActiveParticles==0,effects.ActiveParticles);
            Phase = "coast"; ResetCar(15); yield return new WaitForSeconds(.8f); float coast = car.Speed;
            CheckResult("release-W-preserves-momentum", coast > 12 && coast < 15.2f, coast);
            Phase = "combined-input"; ResetCar(12); move = Vector2.one; yield return new WaitForSeconds(.15f);
            CheckResult("throttle-and-steering-are-independent", ctx.Input.Drive.x > .99f && ctx.Input.Drive.y > .99f, ctx.Input.Drive.y);
            CheckResult("walking-diagonal-remains-normalised", ctx.Input.Move.magnitude <= 1.001f, ctx.Input.Move.magnitude);
            foreach (int direction in new[] { -1, 1 })
            {
                Phase = direction < 0 ? "left" : "right"; ResetCar(15); move = new Vector2(direction * .55f, .3f);
                yield return new WaitForSeconds(.65f);
                float turn = Mathf.DeltaAngle(heading.eulerAngles.y, car.Root.eulerAngles.y);
                CheckResult(direction < 0 ? "A-turns-left" : "D-turns-right", turn * direction > 22 && turn * direction < 38, turn);
                CheckResult(Phase + "-corner-retains-speed", car.Speed > 12, car.Speed);
                CheckResult(Phase + "-corner-grip", Mathf.Abs(car.Motion.Lateral) < 2.4f, car.Motion.Lateral);
                float cameraAlignment=Vector3.Dot(ctx.Follow.FlatForward,CityPhysics.Flat(car.Root.forward).normalized);
                CheckResult(Phase + "-camera-follows-corner", cameraAlignment > .92f, cameraAlignment);
                if (direction > 0) yield return Capture("03-curva");
            }
            Phase = "countersteer"; move = new Vector2(-.8f, .3f); yield return new WaitForSeconds(.4f);
            CheckResult("countersteer-responds-promptly", car.Motion.Steering < -.03f && car.Body.angularVelocity.y < .1f, car.Body.angularVelocity.y);
            foreach (float speed in new[] { 10f, 15f, 22f })
            {
                Phase = "powered-corner-" + speed; ResetCar(speed); move = Vector2.one;
                yield return new WaitForSeconds(.5f);
                float turned = Mathf.DeltaAngle(heading.eulerAngles.y, car.Root.eulerAngles.y);
                float minimum = speed < 12 ? 20 : speed < 20 ? 16 : 12;
                CheckResult(Phase + "-turns-with-W-and-D-held", turned > minimum && turned < 40, turned);
                CheckResult(Phase + "-keeps-momentum-and-support", car.Speed > speed * .88f && car.WheelGrounded.Count(a => a) >= 3 && Vector3.Angle(car.Root.up, Vector3.up) < 14, car.Speed);
                if (speed == 15) yield return Capture("03b-curva-acelerando");
            }
            Phase = "handbrake"; ResetCar(15); move = new Vector2(.65f, .25f); handbrake = true;
            int slideParticles=effects.SlideParticles;
            yield return new WaitForSeconds(.6f); float slide = car.Motion.Slip;
            CheckResult("Shift-releases-rear-grip", slide > .15f && car.Motion.Handbrake > .95f, slide);
            CheckResult("handbrake-illuminates-stop-lights", car.Motion.Braking);
            CheckResult("handbrake-leaves-a-smoke-trail",effects.SlideParticles>slideParticles&&effects.ActiveParticles>0,effects.SlideParticles-slideParticles);
            yield return Capture("04-freio-de-mao");
            handbrake = false; move = new Vector2(-.25f, .35f); yield return new WaitForSeconds(.4f); move = new Vector2(0, .35f);
            Phase = "slide-recovery"; yield return new WaitForSeconds(1.4f);
            CheckResult("release-Shift-recovers-grip", car.Motion.Slip < .16f && Mathf.Abs(car.Body.angularVelocity.y) < .25f, car.Motion.Slip);
            CheckResult("slide-does-not-roll-car", Vector3.Angle(car.Root.up, Vector3.up) < 16, Vector3.Angle(car.Root.up, Vector3.up));
            Phase = "high-speed-run"; ResetCar(0); move = Vector2.up; yield return new WaitForSeconds(6);
            CheckResult("six-second-straight-is-substantially-faster",car.Speed>34&&car.Speed<CityVehicleMath.DriverTopSpeed+1,car.Speed,"m/s after 6 s");
            CheckResult("fast-car-keeps-suspension-support",car.WheelGrounded.Count(a=>a)>=3&&Vector3.Angle(car.Root.up,Vector3.up)<14,Vector3.Angle(car.Root.up,Vector3.up));
            yield return Capture("06-aceleracao-na-reta");
            Phase = "reverse"; ResetCar(0); move = Vector2.down; yield return new WaitForSeconds(1.7f);
            CheckResult("S-selects-and-drives-reverse", car.Motion.Direction < 0 && car.Speed < -2 && car.Speed > -7.1f, car.Speed);
            move = new Vector2(.7f, -1); yield return new WaitForSeconds(.6f);
            CheckResult("reverse-steering-follows-wheel-direction", Mathf.DeltaAngle(heading.eulerAngles.y, car.Root.eulerAngles.y) < -4, Mathf.DeltaAngle(heading.eulerAngles.y, car.Root.eulerAngles.y));
            Phase = "pause"; ResetCar(12); move = Vector2.up; yield return new WaitForSeconds(.3f);
            ctx.UI.SetSettings(true); Vector3 paused = car.Position; float clock = ctx.Clock;
            float effectClock=effects.EffectClock;int pausedParticles=effects.Smoke.GetParticles(smokeBuffer);float lifetime=pausedParticles>0?smokeBuffer[0].remainingLifetime:0;
            yield return new WaitForSecondsRealtime(.3f);
            CheckResult("Esc-settings-freeze-vehicle", ctx.Paused && (car.Position - paused).magnitude < .01f && ctx.Clock == clock, (car.Position - paused).magnitude);
            int frozenParticles=effects.Smoke.GetParticles(smokeBuffer);
            CheckResult("Esc-freezes-smoke-and-mark-fading",effects.EffectClock==effectClock&&frozenParticles==pausedParticles&&(pausedParticles==0||Mathf.Abs(smokeBuffer[0].remainingLifetime-lifetime)<.001f),effects.EffectClock-effectClock);
            ctx.UI.SetSettings(false); yield return new WaitForSeconds(.2f);
            CheckResult("close-settings-resumes-driving", !ctx.Paused && car.Speed > 10, car.Speed);
            Phase = "map-input"; ResetCar(0); move = Vector2.one; ctx.Input.TouchMap = true; yield return new WaitForSeconds(.3f);
            CheckResult("map-suppresses-driver-input", ctx.UI.MapOpen && ctx.Input.Drive == Vector2.zero && Mathf.Abs(car.Speed) < .15f, car.Speed);
            ctx.Input.TouchMap = true; yield return new WaitForSeconds(.25f);
            CheckResult("close-map-restores-driver-input", !ctx.UI.MapOpen && ctx.Input.Drive.x > .99f && ctx.Input.Drive.y > .99f && car.Speed > .15f, car.Speed);
            Phase = "airborne"; ResetCar(8, 3); move = Vector2.one;
            int airEmissions=effects.LaunchParticles+effects.BrakeParticles+effects.SlideParticles,airMarks=effects.MarkSegments;
            yield return new WaitForSeconds(.25f);
            CheckResult("airborne-has-no-tyre-propulsion", car.WheelGrounded.All(a => !a) && Mathf.Abs(car.Speed - 8) < .4f && Mathf.Abs(car.Body.angularVelocity.y) < .05f, car.Speed);
            CheckResult("gravity-survives-vehicle-controller", car.Body.linearVelocity.y < -1.5f, car.Body.linearVelocity.y);
            CheckResult("airborne-wheels-leave-no-smoke-or-marks",airEmissions==effects.LaunchParticles+effects.BrakeParticles+effects.SlideParticles&&airMarks==effects.MarkSegments);
            Phase = "contact"; ResetCar(15); move = Vector2.up;
            barrier = GameObject.CreatePrimitive(PrimitiveType.Cube); barrier.name = "Driving QA wall · opt-in"; barrier.layer = CityContext.WorldLayer;
            barrier.transform.SetPositionAndRotation(start + heading * new Vector3(0, 1.3f, 12), heading); barrier.transform.localScale = new Vector3(12, 3, .35f);
            Physics.SyncTransforms(); yield return new WaitForSeconds(1.15f);
            float advance = Vector3.Dot(car.Position - start, heading * Vector3.forward);
            CheckResult("collision-solver-blocks-driven-car", advance < 11 && car.Speed < 2, advance);
            CheckResult("collision-body-stays-finite", !float.IsNaN(car.Speed) && car.Body.linearVelocity.magnitude < 25, car.Body.linearVelocity.magnitude);
            CheckResult("effect-pools-stay-bounded",effects.ActiveParticles<=640&&effects.MarkMesh.vertexCount==CityVehicleEffects.MarkCapacity*4,effects.ActiveParticles);
            yield return Capture("05-colisao"); Destroy(barrier); barrier = null; yield return null;
        }
        void CheckEffectGeometry(CityVehicleEffects effects)
        {
            var points=effects.MarkMesh.vertices;var colors=effects.MarkMesh.colors32;int checkedPoints=0;float lift=0,longest=0;
            for(int i=0;i<points.Length;i+=4)
            {
                if(colors[i].a==0)continue;
                longest=Mathf.Max(longest,(points[i+2]-points[i]).magnitude);
                if(checkedPoints>=80)continue;
                if(Physics.Raycast(points[i]+Vector3.up*.4f,Vector3.down,out var floor,1,1<<CityContext.GroundLayer,QueryTriggerInteraction.Ignore))
                {checkedPoints++;lift=Mathf.Max(lift,Mathf.Abs(points[i].y-floor.point.y));}
            }
            CheckResult("rubber-marks-follow-the-road",checkedPoints>0&&lift<.07f,lift);
            CheckResult("resetting-car-does-not-stretch-trails",longest<3.01f,longest);
        }
        IEnumerator Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            yield return new WaitForEndOfFrame();
            var target = new RenderTexture(1280, 720, 24); target.Create(); var previous = RenderTexture.active; var previousTarget = ctx.Camera.targetTexture;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try { ctx.Camera.targetTexture = target; ctx.Camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply(); File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG()); }
            finally { ctx.Camera.targetTexture = previousTarget; RenderTexture.active = previous; target.Release(); Destroy(target); Destroy(image); }
        }
        void Finish()
        {
            if (Finished) return; Finished = true; Phase = "finished"; ClearInput();
            if (ctx) { ctx.UI.SetSettings(false); ctx.Population.enabled = populationEnabled; ctx.Follow.enabled = true; }
            report.passed &= report.errors.Count == 0;
            if (string.IsNullOrEmpty(OutputDirectory)) OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports/vehicles/editor"));
            Directory.CreateDirectory(OutputDirectory); File.WriteAllText(Path.Combine(OutputDirectory, "driving-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("DRIVING_QA_" + (report.passed ? "PASS" : "FAIL") + " checks=" + report.checks.Count);
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 1);
        }
    }
}
