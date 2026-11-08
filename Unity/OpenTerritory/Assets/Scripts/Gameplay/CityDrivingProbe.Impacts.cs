using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityDrivingProbe
    {
        [Serializable] public sealed class ImpactSample
        {
            public string phase, obstacle;
            public float mass, speedAfterContact;
            public Vector3 relativeVelocity, impulse, velocityAfterContact;
            public int contacts;
        }

        IEnumerator ImpactCases()
        {
            // Real car, input and PhysX contacts on an opt-in isolated test pad.
            Vector3 roadStart = start; Quaternion roadHeading = heading; float mass = car.Body.mass;
            var fixtures = new List<GameObject>();
            var recorder = car.Root.gameObject.AddComponent<CityDrivingImpactContact>();
            recorder.Record = collision => report.impacts.Add(new ImpactSample {
                phase = Phase, obstacle = collision.collider.name, mass = car.Body.mass,
                speedAfterContact = car.Speed, relativeVelocity = collision.relativeVelocity,
                impulse = collision.impulse, velocityAfterContact = car.Body.linearVelocity, contacts = collision.contactCount });
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(floor);
            floor.name = "Vehicle impact QA floor · opt-in"; floor.layer = CityContext.GroundLayer;
            floor.transform.position = new Vector3(0, 199, 0); floor.transform.localScale = new Vector3(80, 2, 100);
            start = new Vector3(0, 200.07f, -20); heading = Quaternion.identity;
            ctx.Systems.Weather.Request("clear", true);
            try
            {
                // Active is set before the door/seat transition completes.
                // Let that production parking brake finish before coasting.
                Phase = "impact-warmup"; ResetCar(0); yield return new WaitForSeconds(.8f);
                Phase = "impact-coast"; ResetImpactCar(fixtures, 12); yield return new WaitForSeconds(.75f);
                float coast = car.Speed;
                CheckResult("impact-coast-reference", coast > 11 && coast < 12.1f, coast);

                Phase = "impact-light-prop"; ResetImpactCar(fixtures, 12);
                var light = ImpactProp(fixtures, 36, new Vector3(0, 1, 6));
                yield return new WaitForSeconds(.75f); float lightSpeed = car.Speed;
                CheckResult("broken-light-prop-preserves-momentum", light.Broken && lightSpeed > coast * .8f && lightSpeed < coast, lightSpeed);
                CheckResult("broken-prop-removes-all-colliders", light.shapes.All(s => !s.enabled));
                yield return Capture("07-light-prop-momentum");

                Phase = "impact-lamp"; ResetImpactCar(fixtures, 12);
                var lamp = ImpactProp(fixtures, 95, new Vector3(0, 1, 6));
                yield return new WaitForSeconds(.75f); float lampSpeed = car.Speed;
                CheckResult("resistant-prop-costs-more-momentum", lamp.Broken && lampSpeed > coast * .6f && lampSpeed < lightSpeed - .2f, lampSpeed);

                Phase = "impact-heavy-car"; car.Body.mass = mass * 2; ResetImpactCar(fixtures, 12);
                var heavy = ImpactProp(fixtures, 95, new Vector3(0, 1, 6));
                yield return new WaitForSeconds(.75f);
                CheckResult("heavier-car-loses-less-speed", heavy.Broken && car.Speed > lampSpeed + .3f && car.Speed < coast + .1f, car.Speed);
                car.Body.mass = mass;

                Phase = "impact-authored-lamp"; ResetImpactCar(fixtures, 12);
                var authored = ctx.Systems.Destruction.Props.FirstOrDefault(p => p && !p.Broken && p.category == "lamp" &&
                    p.gameObject.activeInHierarchy && p.shapes.Any(s => s && s.enabled && !s.isTrigger));
                CheckResult("authored-street-lamp-available", authored);
                if (authored)
                {
                    var shape = authored.shapes.First(s => s && s.enabled && !s.isTrigger);
                    var bounds = shape.bounds;
                    authored.transform.position += start + new Vector3(0, .02f, 6) - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                    fixtures.Add(authored.gameObject); Physics.SyncTransforms();
                    yield return new WaitForSeconds(.75f);
                    CheckResult("authored-street-lamp-retains-momentum", authored.Broken && car.Speed > 7 && car.Speed < coast + .1f, car.Speed);
                    yield return Capture("09-authored-street-lamp");
                }

                Phase = "impact-compound-prop"; ResetImpactCar(fixtures, 12);
                var compound = ImpactProp(fixtures, 36, new Vector3(-.35f, 1, 6));
                var second = GameObject.CreatePrimitive(PrimitiveType.Cube); second.layer = CityContext.WorldLayer;
                second.transform.SetParent(compound.transform, false); second.transform.localPosition = Vector3.right * 1.15f;
                ctx.Systems.Destruction.Register(compound); Physics.SyncTransforms();
                yield return new WaitForSeconds(.75f);
                CheckResult("compound-prop-does-not-stop-car", compound.Broken && car.Speed > coast * .75f && car.Speed < coast + .1f, car.Speed);

                Phase = "impact-chain"; ResetImpactCar(fixtures, 12);
                var chain = new[] { ImpactProp(fixtures, 36, new Vector3(0, 1, 5)), ImpactProp(fixtures, 36, new Vector3(0, 1, 8)), ImpactProp(fixtures, 36, new Vector3(0, 1, 11)) };
                yield return new WaitForSeconds(1.4f);
                CheckResult("successive-props-cost-speed-without-freezing", chain.All(p => p.Broken) && car.Speed > 6 && car.Speed < lightSpeed - .5f, car.Speed);

                Phase = "impact-unbroken"; ResetImpactCar(fixtures, 12);
                var solid = ImpactProp(fixtures, 5000, new Vector3(0, 1, 6));
                yield return new WaitForSeconds(.8f);
                CheckResult("surviving-prop-still-stops-car", !solid.Broken && solid.Health < solid.durability && Mathf.Abs(car.Speed) < .5f, car.Speed);
                solid.gameObject.SetActive(false);

                Phase = "impact-low-speed"; ResetImpactCar(fixtures, 1.8f);
                var slow = ImpactProp(fixtures, 95, new Vector3(0, 1, 3));
                yield return new WaitForSeconds(1.2f);
                CheckResult("parking-speed-does-not-break-or-pass-prop", !slow.Broken && Mathf.Abs(car.Speed) < .4f, car.Speed);
                slow.gameObject.SetActive(false);

                Phase = "impact-prop-before-wall"; ResetImpactCar(fixtures, 12);
                var beforeWall = ImpactProp(fixtures, 36, new Vector3(0, 1, 6));
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(wall); wall.layer = CityContext.WorldLayer;
                wall.name = "Impact QA solid wall"; wall.transform.position = start + new Vector3(0, 1.4f, 7);
                wall.transform.localScale = new Vector3(12, 3, .4f); Physics.SyncTransforms();
                yield return new WaitForSeconds(1);
                CheckResult("broken-prop-does-not-bypass-wall", beforeWall.Broken && car.Position.z - start.z < 6 && Mathf.Abs(car.Speed) < .5f, car.Speed);
                yield return Capture("08-prop-before-solid-wall"); wall.SetActive(false);

                Phase = "impact-simultaneous-wall"; ResetImpactCar(fixtures, 12);
                var touchingWall = ImpactProp(fixtures, 36, new Vector3(0, 1, 6));
                wall.transform.position = start + new Vector3(0, 1.4f, 6); wall.SetActive(true); Physics.SyncTransforms();
                yield return new WaitForSeconds(.85f);
                CheckResult("simultaneous-solid-contact-still-blocks", touchingWall.Broken && car.Position.z - start.z < 4.5f && car.Speed < .5f && car.Body.linearVelocity.magnitude < 4,
                    car.Speed, "wall blocks forward travel; a bounded physical rebound is allowed");
                wall.SetActive(false);

                Phase = "impact-glancing"; ResetImpactCar(fixtures, 12);
                var glance = ImpactProp(fixtures, 36, new Vector3(car.Data.width * .5f, 1, 6));
                yield return new WaitForSeconds(.75f);
                float forward = Vector3.Dot(car.Body.linearVelocity, Vector3.forward);
                CheckResult("glancing-prop-preserves-travel", glance.Broken && forward > coast * .7f && car.Body.linearVelocity.magnitude < coast + .2f, forward);

                Phase = "impact-reverse"; ResetImpactCar(fixtures, -12);
                var reverse = ImpactProp(fixtures, 36, new Vector3(0, 1, -6));
                yield return new WaitForSeconds(.75f);
                CheckResult("reverse-impact-retains-reverse-momentum", reverse.Broken && car.Speed < -8 && car.Speed > -12.1f, car.Speed);

                Phase = "impact-pause"; ResetImpactCar(fixtures, 12);
                var pauseProp = ImpactProp(fixtures, 36, new Vector3(0, 1, 6));
                ctx.UI.SetSettings(true); Vector3 pausedPosition = car.Position;
                yield return new WaitForSecondsRealtime(.25f);
                CheckResult("pause-freezes-approach-and-prop", !pauseProp.Broken && (car.Position - pausedPosition).sqrMagnitude < .0001f);
                ctx.UI.SetSettings(false); yield return new WaitForSeconds(.75f);
                CheckResult("resume-keeps-impact-momentum", pauseProp.Broken && car.Speed > 8, car.Speed);
                brake = true; yield return new WaitForSeconds(1.3f);
                CheckResult("service-brake-still-stops-after-prop", Mathf.Abs(car.Speed) < .2f, car.Speed);
            }
            finally
            {
                car.Body.mass = mass; ClearInput(); recorder.Record = null; Destroy(recorder);
                foreach (var fixture in fixtures) if (fixture) { fixture.SetActive(false); Destroy(fixture); }
                start = roadStart; heading = roadHeading; ResetCar(0);
            }
        }

        void ResetImpactCar(List<GameObject> fixtures, float speed)
        {
            foreach (var fixture in fixtures) if (fixture && fixture.GetComponent<CityDestructible>()) fixture.SetActive(false);
            ResetCar(speed);
        }

        CityDestructible ImpactProp(List<GameObject> fixtures, float durability, Vector3 offset)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube); fixtures.Add(root);
            root.name = "Impact QA prop " + Phase; root.layer = CityContext.WorldLayer;
            root.transform.position = start + offset; root.transform.localScale = new Vector3(.6f, 2, .4f);
            var prop = root.AddComponent<CityDestructible>(); prop.durability = durability;
            ctx.Systems.Destruction.Register(prop); Physics.SyncTransforms(); return prop;
        }
    }

    public sealed class CityDrivingImpactContact : MonoBehaviour
    {
        public Action<Collision> Record;
        public Action<Collision> RecordStay;
        void OnCollisionEnter(Collision collision) { Record?.Invoke(collision); }
        void OnCollisionStay(Collision collision) { RecordStay?.Invoke(collision); }
    }
}
