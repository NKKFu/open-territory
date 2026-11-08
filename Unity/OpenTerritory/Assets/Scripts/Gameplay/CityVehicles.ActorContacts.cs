using UnityEngine;

namespace OpenTerritory
{
    public partial class CityVehicles
    {
        readonly Collider[] actorContacts = new Collider[64];
        readonly RaycastHit[] actorSweeps = new RaycastHit[64];

        internal void ActorContacts(Vehicle car, bool sampled, Vector3 previousPosition, Quaternion previousRotation, Vector3 previousVelocity)
        {
            if (!ctx || !ctx.Ready || ctx.Paused || car.BodyColliders == null) return;
            var position = car.Body.position; var rotation = car.Body.rotation;
            var movement = position - previousPosition;
            // Only sweep movement that really happened. Never predict damage
            // beyond a wall, or sweep through actors when a car is teleported.
            bool continuous = sampled && movement.magnitude < Mathf.Max(2, previousVelocity.magnitude * Time.fixedDeltaTime * 3);
            bool sweep = continuous && movement.sqrMagnitude > .000001f;
            var velocity = CityPhysics.Flat(car.Body.linearVelocity);
            if (continuous && previousVelocity.sqrMagnitude > velocity.sqrMagnitude) velocity = CityPhysics.Flat(previousVelocity);
            float speed = velocity.magnitude;
            if (speed < 2) return;
            var scale = car.Root.lossyScale;
            int mask = (1 << CityContext.ActorLayer) | (1 << CityContext.PlayerLayer);
            foreach (var box in car.BodyColliders)
            {
                if (!box || !box.enabled || box.isTrigger) continue;
                var offset = Vector3.Scale(box.center, scale);
                var half = Vector3.Scale(box.size * .5f, scale);
                // CharacterController skin can stop a body just short of an
                // overlap, and doesn't reliably send OnCollisionEnter to it.
                int count = Physics.OverlapBoxNonAlloc(position + rotation * offset, half + Vector3.one * .04f,
                    actorContacts, rotation, mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++) ContactActor(car, actorContacts[i], speed);
                if (!sweep) continue;
                count = Physics.BoxCastNonAlloc(previousPosition + previousRotation * offset, half, movement.normalized,
                    actorSweeps, previousRotation, movement.magnitude, mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++) ContactActor(car, actorSweeps[i].collider, speed);
            }
        }

        void ContactActor(Vehicle car, Collider collider, float speed)
        {
            if (!collider || !collider.enabled || collider.transform.IsChildOf(car.Root)) return;
            var person = ctx.Residents.FromCollider(collider);
            if (person != null)
            {
                if (person.Down || person.Bus != null || ctx.Combat.IsDown(person.Root)) return;
            }
            else if (!collider.transform.IsChildOf(ctx.Player) || ctx.Motor.Riding || ctx.Combat.Down) return;
            // Audio has a per-car throttle; victims do not. Two people touched
            // during the same physics step must both receive their impact.
            if (ctx.Clock - car.LastImpact >= .2f)
            {
                car.LastImpact = ctx.Clock;
                ctx.Sound("impact", car.Position, Mathf.Clamp01(speed / 12));
            }
            Impact?.Invoke(car, speed, collider);
        }
    }
}
