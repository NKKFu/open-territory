using UnityEngine;

namespace OpenTerritory
{
    public partial class CityVehicles
    {
        Vehicle remoteCar;
        public Vehicle RemoteVehicle => remoteCar;
        public bool CanRemoteDrive(Vehicle car) => car != null && car.Root && car.Root.gameObject.activeInHierarchy &&
            car != active && car != transitionVehicle && !car.Controlled && !car.Passenger && !car.Commanded && !car.TrafficHold &&
            car.Data.kind != "bus" && car.Data.kind != "bike" && !ctx.Motor.Riding && !ctx.Motor.Swimming && !ctx.Combat.Down && !ctx.Police.Custody && ctx.Incidents.Get(car) == null;
        public bool BeginRemoteDrive(Transform root)
        {
            var car = FindHackVehicle(root); if (!CanRemoteDrive(car)) return false;
            EndRemoteDrive(); remoteCar = car; car.RemoteUntil = 0; car.JamUntil = 0; car.LastUsed = ctx.Clock; car.Body.WakeUp(); return true;
        }
        public void EndRemoteDrive()
        {
            if (remoteCar == null) return;
            remoteCar.JamUntil = Mathf.Max(remoteCar.JamUntil, ctx.Clock + 1.2f); remoteCar.LastUsed = ctx.Clock; remoteCar.Body.WakeUp(); remoteCar = null;
        }
        public Vehicle FindHackVehicle(Transform root)
        {
            if (!root) return null;
            foreach (var car in fleet) if (car.Root.gameObject.activeInHierarchy && (root == car.Root || root.IsChildOf(car.Root))) return car;
            return null;
        }
        public bool CanHackMove(Vehicle car) => car != null && car.Root && car.Root.gameObject.activeInHierarchy &&
            car != active && car != remoteCar && car != transitionVehicle && !car.Controlled && !car.Passenger && !car.Commanded && !car.TrafficHold &&
            car.Data.kind != "bus" && car.Data.kind != "bike" && Mathf.Abs(car.Speed) < 1.5f && ctx.Incidents.Get(car) == null;
        public Vector3 HackPoint(Transform root)
        {
            var car = FindHackVehicle(root); if (car == null) return root.position + Vector3.up * 2.8f;
            float height = car.Data.kind == "bike" ? 1.65f : car.Data.kind == "bus" ? 3.2f : 2.4f;
            foreach (var surface in car.Data.surfaces) height = Mathf.Max(height, surface.top);
            return root.TransformPoint(new Vector3(0, height + .25f, car.Data.centerOffset));
        }
        public bool HackHorn(Transform root)
        {
            var car = FindHackVehicle(root); if (car == null) return false;
            ctx.Sound(car.Data.kind == "bike" ? "bell" : "horn", car.Position, .8f);
            ctx.Residents.ReactToHack(HackPoint(car.Root), null, false, car.Data.kind == "bike" ? 7 : 12);
            car.LastHorn = ctx.Clock; car.LastUsed = ctx.Clock; return true;
        }
        public bool HackAlarm(Transform root)
        {
            var car = FindHackVehicle(root); if (car == null) return false;
            bool on = ctx.Clock >= car.AlarmUntil; car.AlarmUntil = on ? ctx.Clock + 7 : 0; car.NextAlarm = ctx.Clock;
            car.LastUsed = ctx.Clock;
            if (on) ctx.Residents.ReactToHack(HackPoint(car.Root), "De quem é esse alarme?", true, 15);
            return true;
        }
        public bool HackMove(Transform root, float direction)
        {
            var car = FindHackVehicle(root); if (!CanHackMove(car)) return false;
            car.RemoteDirection = Mathf.Sign(direction); car.RemoteUntil = ctx.Clock + 3; car.JamUntil = 0;
            car.LastUsed = ctx.Clock; car.Body.WakeUp();
            ctx.Residents.ReactToHack(HackPoint(car.Root), null, false, 10); return true;
        }
        void TickRemoteAlarm(Vehicle car)
        {
            if (ctx.Clock >= car.AlarmUntil || ctx.Clock < car.NextAlarm) return;
            car.NextAlarm = ctx.Clock + .85f; ctx.Sound(car.Data.kind == "bike" ? "bell" : "horn", car.Position, .55f);
        }
        void ApplyRemoteMotion(Vehicle car, ref float throttle, ref float steer, ref float brake)
        {
            if (car.RemoteUntil <= 0) return;
            if (ctx.Clock >= car.RemoteUntil || car == active || car == transitionVehicle || car.Controlled || car.TrafficHold || car.Commanded)
            { car.RemoteUntil = 0; car.JamUntil = Mathf.Max(car.JamUntil, ctx.Clock + .8f); return; }
            car.Body.WakeUp();
            float direction = car.RemoteDirection, speed = Mathf.Abs(car.Speed);
            Vector3 forward = car.Root.forward * direction;
            float nose = car.Data.length * .5f + Mathf.Abs(car.Data.centerOffset);
            Vector3 origin = car.Position + forward * (nose + .12f) + Vector3.up * 1.05f;
            float stopping = 1.8f + speed * .8f + speed * speed / 4;
            int mask = (ctx.AllMask | (1 << CityContext.PlayerLayer)) & ~(1 << CityContext.GroundLayer);
            bool blocked = CityPhysics.Cast(origin, Mathf.Min(.9f, car.Data.width * .45f), forward, stopping, out _, car.Root, null, mask);
            foreach (var person in ctx.Residents.People)
            {
                Vector3 delta = person.Root.position - car.Position; float along = Vector3.Dot(delta, forward);
                if (along > -1 && along < nose + stopping + 1 && Mathf.Abs(Vector3.Dot(delta, car.Root.right)) < car.Data.width * .5f + 1.0f) blocked = true;
            }
            Vector3 player = ctx.Player.position - car.Position;
            if (Vector3.Dot(player, forward) > -1 && Vector3.Dot(player, forward) < nose + stopping + 1 && Mathf.Abs(Vector3.Dot(player, car.Root.right)) < car.Data.width * .5f + .9f) blocked = true;
            if (blocked)
            {
                throttle = 0; brake = 1; car.StoppedReason = "hack-obstaculo"; return;
            }
            if (car.StoppedReason == "hack-obstaculo") car.StoppedReason = "";
            // Traffic cars keep their lane and obey the AI's stop decision. Parked cars
            // make a short straight manoeuvre. Neither mode teleports the rigidbody.
            if (car.Data.auto && direction > 0)
            { throttle = Mathf.Min(throttle, speed < 2.5f ? .35f : 0); if (speed >= 2.8f) brake = Mathf.Max(brake, .5f); }
            else
            { steer = 0; throttle = speed < 2.5f ? direction * .35f : 0; brake = speed >= 2.8f ? .65f : 0; }
        }
    }
}
