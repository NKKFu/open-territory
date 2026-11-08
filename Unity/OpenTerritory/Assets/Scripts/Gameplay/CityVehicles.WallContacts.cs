using UnityEngine;

namespace OpenTerritory
{
    public partial class CityVehicles
    {
        Vector3 SlideTyreForce(Vehicle car, Vector3 change)
        {
            if (ctx.Clock - car.WallContactAt > Time.fixedDeltaTime * 3 ||
                Mathf.Abs(Vector3.Dot(car.Root.forward, car.WallNormal)) > .55f) return change;
            // Do not continuously drive the side of the chassis into masonry.
            // Outward steering, braking and the whole along-wall force remain.
            return change - car.WallNormal * Mathf.Min(0, Vector3.Dot(change, car.WallNormal));
        }

        internal void WallContact(Vehicle car, Collision collision)
        {
            if (!ctx || !ctx.Ready || ctx.Paused || car == null || car.Data.kind == "bike" || collision.rigidbody ||
                collision.collider.gameObject.layer != CityContext.WorldLayer || Vector3.Dot(car.Root.up, Vector3.up) < .7f) return;
            if (ctx.Systems && ctx.Systems.Destruction && ctx.Systems.Destruction.Resolve(collision.collider)) return;
            bool grounded = false;
            foreach (bool wheel in car.WheelGrounded) grounded |= wheel;
            if (!grounded) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                if (Mathf.Abs(contact.normal.y) > .25f) continue;
                var normal = CityPhysics.Flat(contact.normal).normalized;
                if (Mathf.Abs(Vector3.Dot(car.Root.forward, normal)) > .55f) continue;
                car.WallNormal = normal; car.WallContactAt = ctx.Clock;
                // Flush wall boxes have internal end faces at their seams. A
                // small outward separation velocity clears their contact skin
                // before a bumper catches one. Never restore forward momentum,
                // move the body, ignore a solid, or modify a frontal contact.
                float outward = Vector3.Dot(car.Body.linearVelocity, normal);
                if (outward < .3f) car.Body.linearVelocity += normal * (.3f - outward);
                break;
            }
        }
    }
}
