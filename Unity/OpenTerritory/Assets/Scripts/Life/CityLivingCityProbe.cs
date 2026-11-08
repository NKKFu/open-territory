using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Explicit integration session; never installed in ordinary gameplay.
    [DefaultExecutionOrder(-1200)]
    public sealed class CityLivingCityProbe : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name, detail; public bool passed; }
        [Serializable] public sealed class Sample
        {
            public float time; public int id; public Vector3 position, destination;
            public string state, activity, room; public float speed, lateral, crossTrack, roadClearance, yaw;
        }
        [Serializable] public sealed class Report
        {
            public bool passed = true; public List<Check> checks = new List<Check>();
            public List<Sample> people = new List<Sample>(), cars = new List<Sample>();
            public List<string> errors = new List<string>();
        }
        readonly Report report = new Report();
        CityContext ctx; string directory; bool finished;
        public bool Finished => finished;
        public string ReportDirectory;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-living-qa") >= 0)
                new GameObject("QA da circulação e interiores • opt-in").AddComponent<CityLivingCityProbe>();
        }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string text, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(text + " " + trace); }
        void CheckResult(string name, bool passed, string detail = "")
        {
            report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("LIVING_QA " + name + " " + passed + " " + detail);
            File.WriteAllText(Path.Combine(directory, "living-city-qa.json"), JsonUtility.ToJson(report, true));
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--city-report-dir");
            directory = !string.IsNullOrEmpty(ReportDirectory) ? ReportDirectory : index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "living-city-qa");
            Directory.CreateDirectory(directory);
            float timeout = Time.realtimeSinceStartup + 100;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < timeout) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { CheckResult("scene-ready", false); Finish(); yield break; }
            ctx.Systems.Weather.Request("clear", true);
            CheckResult("diverse-street-circuits-loaded", ctx.LifeNavigation.Circuits.Length >= 12, "circuits=" + ctx.LifeNavigation.Circuits.Length);
            var interior = ctx.Residents.InteriorLocations.Select(i => ctx.Data.locations[i]).ToArray();
            int homes = ctx.Data.places.Count(p => (p.kind == "home" || p.kind == "residence") && interior.Any(l => p.Contains(new Vector3(l.x, p.elevation, l.z), .5f)));
            int shops = ctx.Data.places.Count(p => p.kind != "home" && p.kind != "residence" && interior.Any(l => p.Contains(new Vector3(l.x, p.elevation, l.z), .5f)));
            CheckResult("clear-visitable-homes-and-shops", homes >= 150 && shops >= 9, "homes=" + homes + " shops=" + shops + " pockets=" + interior.Length);
            CheckResult("outdoor-stops-spread-across-neighborhoods", ctx.Residents.OutdoorLocations.Count >= 200, "outdoor=" + ctx.Residents.OutdoorLocations.Count);
            yield return new WaitForSeconds(8);
            var starts = ctx.Residents.People.ToDictionary(p => p.Data.id, p => p.Root.position);
            var destinations = new HashSet<string>(); int visitors = 0; float crowd = 0;
            for (int second = 0; second < 38; second++)
            {
                foreach (var p in ctx.Residents.People)
                {
                    var room = ctx.Data.places.FirstOrDefault(r => r.Contains(p.Root.position, .4f));
                    if (room != null && !p.Data.indoor && !p.Data.stationary && !p.Data.sitting) visitors++;
                    destinations.Add(p.Destination.x.ToString("F1") + "/" + p.Destination.z.ToString("F1"));
                    report.people.Add(new Sample { time = ctx.Clock, id = p.Data.id, position = p.Root.position, destination = p.Destination,
                        state = p.State, activity = p.Activity, room = room?.name, speed = p.Velocity.magnitude });
                    crowd = Mathf.Max(crowd, ctx.Residents.People.Count(other => CityPhysics.Flat(other.Root.position - p.Root.position).magnitude < 6));
                }
                foreach (var car in ctx.Vehicles.Fleet.Where(c => c.Data.auto))
                    report.cars.Add(new Sample { time = ctx.Clock, id = car.Data.id, position = car.Position, speed = car.Speed,
                        lateral = car.LaneOffset, state = car.Traffic.Phase, activity = car.StoppedReason, yaw = car.Root.eulerAngles.y,
                        roadClearance = ctx.LifeNavigation.RoadClearance(car.Position),
                        crossTrack = CityPhysics.Flat(car.Position - ctx.Vehicles.RoutePoint(car, ctx.Vehicles.RouteDistance(car, car.Position))).magnitude });
                yield return new WaitForSeconds(1);
            }
            int moved = ctx.Residents.People.Count(p => starts.TryGetValue(p.Data.id, out var at) && CityPhysics.Flat(p.Root.position - at).magnitude > 8);
            CheckResult("residents-move-to-distinct-destinations", moved >= 6 && destinations.Count >= 12, "moved=" + moved + " destinations=" + destinations.Count);
            CheckResult("ordinary-residents-occupy-interiors", visitors > 10, "visitor samples=" + visitors);
            CheckResult("population-not-clumped-on-one-point", crowd <= 8, "max-neighbours-within-six-metres=" + crowd);
            var movingCars = ctx.Vehicles.Fleet.Where(c => c.Data.auto && c.Data.kind == "car").ToArray();
            CheckResult("drivers-use-different-circuits-and-paces", movingCars.Select(c => c.Route).Distinct().Count() >= 2 &&
                movingCars.Select(c => c.CruiseSpeed).Distinct().Count() >= 2, "cars=" + movingCars.Length);
            CheckResult("lateral-variation-bounded-and-present", report.cars.All(c => Mathf.Abs(c.lateral) <= .351f) &&
                report.cars.Any(c => Mathf.Abs(c.lateral) > .025f), "peak=" + report.cars.Max(c => Mathf.Abs(c.lateral)));
            var ordinaryTraffic = report.cars.Where(c => ctx.Data.vehicles.First(v => v.id == c.id).kind == "car").ToArray();
            int offRoad = ordinaryTraffic.Count(c => c.roadClearance < ctx.Data.vehicles.First(v => v.id == c.id).width * .5f - .12f);
            CheckResult("natural-traffic-remains-on-physical-road", offRoad == 0, "off-road samples=" + offRoad + "/" + ordinaryTraffic.Length);
            var positions = ctx.Residents.People.ToDictionary(p => p.Data.id, p => p.Root.position);
            float clock = ctx.Clock; ctx.UI.SetSettings(true);
            yield return new WaitForSecondsRealtime(.55f);
            CheckResult("Esc-freezes-people-and-traffic", ctx.Paused && clock == ctx.Clock &&
                ctx.Residents.People.All(p => positions.TryGetValue(p.Data.id, out var at) && Vector3.Distance(at, p.Root.position) < .001f));
            ctx.UI.SetSettings(false);
            var person = ctx.Residents.People.FirstOrDefault(p => !p.Down && !p.Data.sitting && !p.Data.stationary && !p.Data.indoor && !p.Data.performance && p.Bus == null);
            CheckResult("resident-still-supports-world-conversation", person != null && ctx.Residents.AvailableForTalk(person));
            if (person == null) { Finish(); yield break; }
            ctx.Residents.Talk(person);
            CheckResult("conversation-produces-world-speech", ctx.Speech.Contains(person.Data.name));
            person.TalkUntil = 0; ctx.Residents.Control(person, true); ctx.Population.enabled = false;
            var frontages = JsonUtility.FromJson<CityFrontageProbe.Layout>(Resources.Load<TextAsset>("building-frontages").text);
            var choices = frontages.entrances.Where(e => {
                var place = ctx.Data.places.FirstOrDefault(p => p.door != null && p.door.id == e.door);
                return place != null && e.samples != null && e.samples.Length > 0 &&
                    CityPhysics.Flat(new Vector3(place.door.x, 0, place.door.z) - e.samples.Last()).magnitude < 3 &&
                    interior.Any(l => place.Contains(new Vector3(l.x, e.inside.y, l.z), .5f)) && ctx.Free(e.sidewalk + Vector3.up * .04f);
            }).ToArray();
            var homesToVisit = choices.Where(e => { var r = ctx.Data.places.First(p => p.door != null && p.door.id == e.door); return r.kind == "home" || r.kind == "residence"; }).ToArray();
            var shopsToVisit = choices.Except(homesToVisit).ToArray();
            var selected = new List<CityFrontageProbe.Entrance>();
            if (homesToVisit.Length > 0) { selected.Add(homesToVisit[homesToVisit.Length / 3]); selected.Add(homesToVisit[homesToVisit.Length * 2 / 3]); }
            selected.AddRange(shopsToVisit.Take(3));
            foreach (var entry in selected) yield return Visit(person, entry);
            if (movingCars.Length > 0) yield return DriverSafety(person, movingCars[0]);
            CheckResult("no-runtime-errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            Finish();
        }
        void PlacePerson(CityResidents.Resident p, Vector3 at)
        {
            p.Controller.enabled = false; p.Root.position = at; p.Controller.enabled = true;
            p.LastPosition = at; p.Velocity = Vector3.zero; p.Steering = Vector3.zero; p.MoveSpeed = 0;
            p.Path.Clear(); p.PathIndex = 0; p.Doorway = null;
        }
        IEnumerator Visit(CityResidents.Resident person, CityFrontageProbe.Entrance entry)
        {
            var room = ctx.Data.places.First(p => p.door != null && p.door.id == entry.door);
            var goal = ctx.Residents.InteriorLocations.Select(i => ctx.Data.locations[i]).Where(l => room.Contains(new Vector3(l.x, room.elevation, l.z), .5f))
                .OrderBy(l => new Vector2(l.x - entry.inside.x, l.z - entry.inside.z).sqrMagnitude).First();
            Vector3 inside = new Vector3(goal.x, ctx.Ground(goal.x, goal.z) + .04f, goal.z);
            Vector3 outside = entry.sidewalk + Vector3.up * .05f;
            ctx.Motor.Warp(outside + CityPhysics.Flat(outside - inside).normalized * 5);
            PlacePerson(person, outside);
            float end = ctx.Clock + 22; bool reached = false;
            while (ctx.Clock < end && !(reached = ctx.Residents.MoveToward(person, inside, Time.deltaTime, 1.9f))) yield return null;
            CheckResult("walk-through-door-" + entry.door, reached && room.Contains(person.Root.position, .5f), room.kind + " " + room.name + " gap=" + CityPhysics.Flat(person.Root.position - inside).magnitude);
            if (reached && (room.kind == "home" || room.kind == "market" || room.kind == "bakery"))
            {
                var normal = Quaternion.Euler(0, room.yaw * Mathf.Rad2Deg, 0) * Vector3.forward;
                Capture("interior-" + entry.door, inside - normal * 1.8f + Vector3.up * 2.1f, person.Root.position + Vector3.up * 1.1f);
            }
            end = ctx.Clock + 22; reached = false;
            while (ctx.Clock < end && !(reached = ctx.Residents.MoveToward(person, outside, Time.deltaTime, 1.9f))) yield return null;
            CheckResult("walk-back-to-sidewalk-" + entry.door, reached && !room.Contains(person.Root.position, .1f), room.name + " gap=" + CityPhysics.Flat(person.Root.position - outside).magnitude);
        }
        IEnumerator DriverSafety(CityResidents.Resident person, CityVehicles.Vehicle car)
        {
            foreach (var other in ctx.Vehicles.Fleet) if (other != car) ctx.Vehicles.SetTrafficHold(other, true, "QA isolado");
            // Choose a surveyed straight stretch with no furniture or parked car.
            int chosen = -1;
            for (int i = 0; i < car.Route.Length; i++)
            {
                float at = car.Cumulative[i]; Vector3 before = ctx.Vehicles.RoutePoint(car, at), after = ctx.Vehicles.RoutePoint(car, at + 35);
                if (CityPhysics.Flat(after - before).magnitude < 33) continue;
                Vector3 middle = ctx.Vehicles.RoutePoint(car, at + 17);
                if (CityPhysics.Flat(middle - (before + after) * .5f).magnitude > .6f ||
                    ctx.Vehicles.Fleet.Any(other => other != car && CityPhysics.Flat(other.Position - middle).magnitude < 30)) continue;
                chosen = i; break;
            }
            if (chosen < 0) { CheckResult("clear-straight-stretch-for-safety-test", false); yield break; }
            var start = car.Route[chosen]; var direction = CityPhysics.Flat(ctx.Vehicles.RoutePoint(car, car.Cumulative[chosen] + 1) - start).normalized;
            car.Progress = car.Cumulative[chosen];
            car.Body.position = start; car.Body.rotation = Quaternion.LookRotation(direction); car.Root.SetPositionAndRotation(start, car.Body.rotation);
            car.Body.linearVelocity = direction * 4; car.Body.angularVelocity = Vector3.zero; car.Motion.Speed = car.TargetSpeed = 4;
            car.JamUntil = 0; car.LastImpact = -1000; car.TrafficHold = false;
            var pedestrian = start + direction * 12; pedestrian.y = ctx.Ground(pedestrian.x, pedestrian.z) + .04f;
            PlacePerson(person, pedestrian); float health = person.Health;
            ctx.Motor.Warp(start - direction * 8 + Vector3.Cross(Vector3.up, direction) * 7);
            float end = ctx.Clock + 8, closest = float.MaxValue;
            while (ctx.Clock < end)
            {
                closest = Mathf.Min(closest, CityPhysics.Flat(person.Root.position - car.Position).magnitude);
                yield return null;
            }
            CheckResult("driver-stops-for-pedestrian-in-lane", Mathf.Abs(car.Speed) < .5f && closest > car.Data.length * .5f + .45f && person.Health == health,
                "speed=" + car.Speed + " closest=" + closest + " health=" + person.Health);
            Capture("transito-respeita-pedestre", car.Position - direction * 10 + Vector3.up * 6 + Vector3.Cross(Vector3.up, direction) * 7, car.Position + direction * 6);
            PlacePerson(person, pedestrian + Vector3.Cross(Vector3.up, direction) * 5);
            end = ctx.Clock + 10; float maxError = 0, minOffset = 1, maxOffset = -1;
            while (ctx.Clock < end)
            {
                float at = ctx.Vehicles.RouteDistance(car, car.Position);
                maxError = Mathf.Max(maxError, CityPhysics.Flat(car.Position - ctx.Vehicles.RoutePoint(car, at)).magnitude);
                minOffset = Mathf.Min(minOffset, car.LaneOffset); maxOffset = Mathf.Max(maxOffset, car.LaneOffset);
                yield return null;
            }
            CheckResult("car-resumes-and-stays-within-lane", car.Speed > 2 && maxError < 1.15f, "speed=" + car.Speed + " peakCrossTrack=" + maxError);
            CheckResult("position-changes-smoothly-inside-lane", maxOffset - minOffset > .02f && Mathf.Abs(minOffset) <= .351f && Mathf.Abs(maxOffset) <= .351f,
                "offsetRange=" + minOffset + ".." + maxOffset);
        }
        void Capture(string name, Vector3 eye, Vector3 target)
        {
            var camera = ctx.Camera; var oldPosition = camera.transform.position; var oldRotation = camera.transform.rotation;
            camera.transform.position = eye; camera.transform.LookAt(target);
            var previous = RenderTexture.active; var output = new RenderTexture(1200, 800, 24); output.Create(); camera.targetTexture = output;
            camera.Render(); RenderTexture.active = output;
            var texture = new Texture2D(1200, 800, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous; output.Release(); Destroy(output); Destroy(texture);
            camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
        }
        void Finish()
        {
            finished = true; File.WriteAllText(Path.Combine(directory, "living-city-qa.json"), JsonUtility.ToJson(report, true));
            Debug.Log("LIVING_QA_FINISHED " + report.passed + " checks=" + report.checks.Count);
            if (!Application.isEditor) Application.Quit(report.passed ? 0 : 2);
        }
    }
}
