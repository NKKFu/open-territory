using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityDrivingProbe
    {
        IEnumerator SidewallCases()
        {
            Vector3 roadStart = start; Quaternion roadHeading = heading;
            var fixtures = new List<GameObject>();
            int contacts = 0;
            var recorder = car.Root.gameObject.AddComponent<CityDrivingImpactContact>();
            recorder.Record = collision => { if (collision.collider.name.StartsWith("Sidewall QA segment")) contacts++; };
            recorder.RecordStay = recorder.Record;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(floor);
            floor.name = "Sidewall QA floor"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = new Vector3(0, 199, 0); floor.transform.localScale = new Vector3(100, 2, 120);
            start = new Vector3(0, 200.07f, -20); heading = Quaternion.identity;
            ctx.Systems.Weather.Request("clear", true);
            try
            {
                Phase = "sidewall-warmup"; ResetCar(0); yield return new WaitForSeconds(1);
                CheckResult("QA-storage-is-isolated", CityCommerce.IsReviewRun);
                CheckResult("all-body-contact-materials-slide", car.BodyColliders.All(c => c.sharedMaterial &&
                    c.sharedMaterial.staticFriction == 0 && c.sharedMaterial.dynamicFriction == 0 && c.sharedMaterial.frictionCombine == PhysicsMaterialCombine.Minimum));
                foreach (int side in new[] { -1, 1 })
                {
                    var walls = new List<GameObject>();
                    for (int i = 0; i < 12; i++)
                    {
                        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); walls.Add(wall); fixtures.Add(wall);
                        wall.name = "Sidewall QA segment " + side + "/" + i; wall.layer = CityContext.WorldLayer;
                        wall.transform.position = new Vector3(side * 1.95f, 201.5f, -40 + i * 6);
                        wall.transform.localScale = new Vector3(.5f, 3, 6.01f);
                    }
                    foreach (bool reverse in new[] { false, true })
                    {
                        Phase = "sidewall-neutral-setup";
                        heading = Quaternion.Euler(0, side * (reverse ? -8 : 8), 0);
                        ResetCar(0); yield return new WaitForSeconds(.25f);
                        Phase = "sidewall-" + side + (reverse ? "-reverse" : "-forward");
                        ResetCar(reverse ? -6 : 12); contacts = 0;
                        move = reverse ? Vector2.down : new Vector2(0, .35f);
                        yield return new WaitForSeconds(1.3f);
                        float travel = (car.Position.z - start.z) * (reverse ? -1 : 1);
                        CheckResult(Phase + "/real-side-contact", contacts > 0, contacts);
                        CheckResult(Phase + "/keeps-sliding-across-seams", travel > (reverse ? 4 : 10) && Mathf.Abs(car.Speed) > (reverse ? 3 : 6), travel, "metres along the wall; speed=" + car.Speed);
                        float penetration = 0;
                        foreach (var box in car.BodyColliders) foreach (var wall in walls)
                            if (Physics.ComputePenetration(box, box.transform.position, box.transform.rotation,
                                wall.GetComponent<Collider>(), wall.transform.position, wall.transform.rotation, out _, out float depth)) penetration = Mathf.Max(penetration, depth);
                        CheckResult(Phase + "/wall-stays-solid", penetration < .055f, penetration);
                        float across = car.Position.x * side;
                        move = new Vector2(side * (reverse ? .8f : -.8f), reverse ? -1 : .7f);
                        yield return new WaitForSeconds(1.2f);
                        CheckResult(Phase + "/steers-away-without-sticking", car.Position.x * side < across - .45f, across - car.Position.x * side);
                        ctx.Follow.enabled = false;
                        ctx.Camera.transform.SetPositionAndRotation(car.Position + new Vector3(-side * 8, 5, -8), Quaternion.LookRotation(car.Position + Vector3.up - (car.Position + new Vector3(-side * 8, 5, -8))));
                        yield return Capture("wall-" + side + (reverse ? "-reverse" : "-forward"));
                        ctx.Follow.enabled = true;
                    }
                    foreach (var wall in walls) wall.SetActive(false);
                }
                heading = Quaternion.identity; ResetCar(12); move = Vector2.up;
                var front = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(front); front.name = "Sidewall QA head-on wall"; front.layer = CityContext.WorldLayer;
                front.transform.position = start + new Vector3(0, 1.5f, 10); front.transform.localScale = new Vector3(16, 3, .5f);
                Physics.SyncTransforms(); yield return new WaitForSeconds(1.2f);
                CheckResult("head-on-wall-still-stops-the-car", car.Position.z - start.z < 8 && Mathf.Abs(car.Speed) < 1, car.Speed);
                var cornerSide = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(cornerSide);
                cornerSide.name = "Sidewall QA corner approach"; cornerSide.layer = CityContext.WorldLayer;
                cornerSide.transform.position = new Vector3(-1.95f, 201.5f, -20); cornerSide.transform.localScale = new Vector3(.5f, 3, 60);
                heading = Quaternion.Euler(0, -8, 0); ResetCar(12); move = Vector2.up;
                yield return new WaitForSeconds(1.2f);
                CheckResult("side-slide-does-not-bypass-corner-wall", car.Position.z - start.z < 8 && Mathf.Abs(car.Speed) < 1, car.Speed);
                cornerSide.SetActive(false); heading = Quaternion.identity;
                front.SetActive(false); ResetCar(12); move = Vector2.up; yield return new WaitForSeconds(.3f);
                brake = true; move = Vector2.zero; yield return new WaitForSeconds(1.4f);
                CheckResult("service-brake-retains-tyre-grip", Mathf.Abs(car.Speed) < .2f, car.Speed);
            }
            finally
            {
                ClearInput(); recorder.Record = recorder.RecordStay = null; Destroy(recorder);
                foreach (var fixture in fixtures) if (fixture) { fixture.SetActive(false); Destroy(fixture); }
                start = roadStart; heading = roadHeading; ResetCar(0); ctx.Follow.enabled = true;
            }
        }

        IEnumerator EntrancePlanterCases()
        {
            var damage = ctx.Systems.Destruction;
            var plants = damage.Props.Where(p => p.category == "entrance-planter").ToArray();
            var catalog = FindFirstObjectByType<CityHomeCatalog>();
            var apartments = new HashSet<int>(catalog.homes.Where(h => h.units > 1).Select(h => h.place));
            // Donor positions provide an inventory independent of registration.
            // Renovations can retire an entrance or move its original collider.
            var donor = JsonUtility.FromJson<WorldData>(Resources.Load<TextAsset>("world").text);
            var live = new HashSet<int>(ctx.Data.solids.Where(s => s.active).Select(s => s.index));
            var visibleBuildings = new HashSet<string>(FindObjectsByType<CityObjectLod>(FindObjectsSortMode.None)
                .Where(l => l.category == "building").Select(l => l.name));
            var physical = new HashSet<int>();
            foreach (var shape in FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (shape.enabled && shape.gameObject.activeInHierarchy && shape.name.StartsWith("Colisão-") && int.TryParse(shape.name.Substring(8), out int id)) physical.Add(id);
            int expected = 0, expectedFire = 0;
            foreach (var solid in donor.solids)
            {
                if (!live.Contains(solid.index) || !physical.Contains(solid.index) || Mathf.Abs(solid.w - .7f) > .001f || Mathf.Abs(solid.d - .7f) > .001f) continue;
                for (int i = 0; i < donor.places.Length; i++)
                {
                    if (apartments.Contains(i)) continue;
                    string building = i < 17 ? "building:Reused building-" + i : "building:Reused residential interior " + (i - 17);
                    if (!visibleBuildings.Contains(building)) continue;
                    var place = donor.places[i];
                    var local = CityLighting.LocalPoint(place, new Vector3(solid.x, solid.bottom, solid.z));
                    if (Mathf.Abs(Mathf.Abs(local.x) - 3.5f) > .02f || Mathf.Abs(local.z - place.lotDepth * .5f - 1.05f) > .02f) continue;
                    expected++; if (place.kind == "fire") expectedFire++; break;
                }
            }
            CheckResult("entrance-plants-registered", plants.Length == expected && damage.EntrancePlanterCount == plants.Length, plants.Length, "expected=" + expected);
            CheckResult("pot-and-foliage-owned-together", plants.All(p => p.batchSection != null && p.batchSection.TriangleCount == 24 && p.shapes.Length > 0));
            CheckResult("current-fire-station-plants-are-covered", plants.Count(p => ctx.Data.places[p.place].kind == "fire") == expectedFire, expectedFire);
            if (plants.Length < 3) yield break;
            var pair = plants.Where(p => ctx.Data.places[p.place].name == "#52C3EA").OrderBy(p => p.transform.position.x).ToArray();
            CheckResult("pictured-house-has-two-breakable-plants", pair.Length == 2);
            if (pair.Length != 2) yield break;
            Vector3 savedStart = start; Quaternion savedHeading = heading;
            var home = ctx.Data.places[pair[0].place];
            Vector3 outward = Quaternion.Euler(0, home.yaw * Mathf.Rad2Deg, 0) * Vector3.forward;
            var target = (pair[0].batchSection.bounds.center + pair[1].batchSection.bounds.center) * .5f;
            ctx.Follow.enabled = false;
            var eye = target + outward * 13 + Vector3.up * 5;
            ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            yield return new WaitForSeconds(.25f); // Let the normal camera LOD pass catch up after the QA warp.
            Phase = "pictured-plants-intact"; yield return Capture("plants-01-intact");
            var sourceMeshes = plants.SelectMany(p => p.batchSection.pieces).Select(p => p.batch.source).Distinct().ToArray();
            var originals = sourceMeshes.ToDictionary(m => m, m => m.triangles);
            var neighbour = pair[1].batchSection.pieces.Select(p => (int[])p.batch.indices.Clone()).ToArray();
            var shot = pair[0]; var box = shot.shapes[0];
            Vector3 aim = shot.batchSection.bounds.center;
            bool hit = Physics.Raycast(aim + outward * 2, -outward, out var ray, 3, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            CheckResult("visible-plant-resolves-from-real-raycast", hit && damage.Resolve(ray.collider) == shot);
            ctx.UI.SetSettings(true); float health = shot.Health; float clock = ctx.Clock;
            bool rejected = !damage.Hit(box, 1000, aim, -outward * 3, CityPropDamageKind.Bullet, false);
            yield return new WaitForSecondsRealtime(.25f);
            CheckResult("Esc-freezes-plant-damage", rejected && shot.Health == health && ctx.Clock == clock);
            ctx.UI.SetSettings(false);
            damage.Hit(box, 18, aim, -outward * 3, CityPropDamageKind.Bullet, false);
            CheckResult("bullet-damages-pot-without-premature-removal", !shot.Broken && shot.Health > 0 && shot.Health < health);
            damage.Hit(box, 18, aim, -outward * 3, CityPropDamageKind.Bullet, false);
            CheckResult("bullets-break-pot-and-foliage", shot.Broken && shot.LastDamage == CityPropDamageKind.Bullet && shot.shapes.All(c => !c.enabled));
            CheckResult("broken-plant-hidden-in-every-LOD", shot.batchSection.pieces.All(p => p.offsets.All(i => p.batch.indices[i] == p.batch.indices[i + 1] && p.batch.indices[i] == p.batch.indices[i + 2])));
            CheckResult("other-entrance-plant-is-preserved", !pair[1].Broken && pair[1].batchSection.pieces.Select((p, j) => p.offsets.All(i => p.batch.indices[i + 1] == neighbour[j][i + 1] && p.batch.indices[i + 2] == neighbour[j][i + 2])).All(v => v));
            Vector3 low = shot.batchSection.bounds.center; low.y = shot.batchSection.bounds.min.y + .35f;
            bool wallHit = Physics.Raycast(low + outward * 2, -outward, out var wall, 5, 1 << CityContext.WorldLayer, QueryTriggerInteraction.Ignore);
            CheckResult("facade-behind-broken-plant-remains-solid", wallHit && !damage.Resolve(wall.collider), wallHit ? wall.distance : -1);
            Phase = "pictured-plant-shot"; yield return Capture("plants-02-after-bullets");
            var melee = plants.First(p => p.place != shot.place && !p.Broken);
            damage.Hit(melee.shapes[0], 40, melee.batchSection.bounds.center, Vector3.right * 3, CityPropDamageKind.Melee, false);
            CheckResult("melee-breaks-entrance-plant", melee.Broken && melee.LastDamage == CityPropDamageKind.Melee);
            var driven = pair[1]; Vector3 center = driven.batchSection.bounds.center;
            start = center + outward * 7; start.y = ctx.Ground(start.x, start.z) + .07f; heading = Quaternion.LookRotation(-outward);
            ResetCar(6); move = new Vector2(0, .3f); Phase = "pictured-plant-vehicle-impact";
            yield return new WaitForSeconds(1.2f); ClearInput(); brake = true;
            CheckResult("car-breaks-the-pictured-entrance-plant", driven.Broken && driven.LastDamage == CityPropDamageKind.Vehicle, driven.Health);
            CheckResult("broken-plants-leave-no-invisible-blockers", pair.All(p => p.Broken && p.shapes.All(c => !c.enabled)));
            CheckResult("fragment-pool-remains-bounded", damage.FragmentCount > 0 && damage.FragmentCount <= CityDestruction.DebrisLimit, damage.FragmentCount);
            CheckResult("shared-building-source-meshes-unchanged", originals.All(p => p.Value.SequenceEqual(p.Key.triangles)));
            ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            yield return Capture("plants-03-after-car");
            start = savedStart; heading = savedHeading; ResetCar(0); ctx.Follow.enabled = true;
        }
    }
}
