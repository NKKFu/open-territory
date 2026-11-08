using UnityEngine;

namespace OpenTerritory
{
    public partial class CityVehicles
    {
        const float WalkToDoor = .32f, OpenDoor = .38f, TakeHold = .42f, PullOut = 1.0f, RegainFeet = .48f;
        const float RemovalDuration = WalkToDoor + OpenDoor + TakeHold + PullOut + RegainFeet;
        sealed class Carjacking
        {
            public Vehicle Car;
            public CityDriverIncidents.Incident Driver;
            public Vector3 Start, Door, Grip, Retreat, Outside, Recovery, Seat, Head, Handle;
            public Quaternion StartRotation, ReturnRotation;
            public float Time, ReturnTime, ReturnDoor, ReturnSeat;
            public Vector3 ReturnPlayer, ReturnDriver;
            public bool Aligned, Returning, WasAutomatic;
        }
        Carjacking carjacking;
        public bool RemovingDriver => carjacking != null;
        public CityResidents.Resident RemovedDriver => carjacking?.Driver.Actor;
        public string CarjackingPhase => carjacking == null ? "" : carjacking.Returning ? "returning" :
            carjacking.Time < WalkToDoor ? "approach" : carjacking.Time < WalkToDoor + OpenDoor ? "opening" :
            carjacking.Time < WalkToDoor + OpenDoor + TakeHold ? "grabbing" :
            carjacking.Time < WalkToDoor + OpenDoor + TakeHold + PullOut ? "pulling" : "recovering";

        static float Phase(float time, float start, float duration) => Mathf.Clamp01((time - start) / duration);
        Vector3 RemovalFeet(Vehicle car, Vector3 local)
        { var p = car.Root.TransformPoint(local); p.y = ctx.Ground(p.x, p.z) + .035f; return p; }
        bool RemovalSpace(Vehicle car, Vector3 from, Vector3 to)
        {
            var a = RemovalFeet(car, from); var b = RemovalFeet(car, to);
            if (Mathf.Abs(a.y - b.y) > .32f || Mathf.Abs(b.y - car.Position.y) > .75f) return false;
            return CityPhysics.StandClear(b, ctx.Player, car.Root) && CityPhysics.SweepClear(a, b, ctx.Player, car.Root);
        }
        bool BeginCarjacking(Vehicle car)
        {
            int side = ApproachSide(car); float half = car.Data.width * .5f, z = car.Data.seatMount.z;
            var j = new Carjacking { Car = car, Start = car.Root.InverseTransformPoint(ctx.Player.position),
                StartRotation = Quaternion.Inverse(car.Root.rotation) * ctx.Player.rotation, WasAutomatic = car.Data.auto,
                Door = new Vector3(side * (half + .85f), 0, z - .65f), Grip = new Vector3(side * (half + .54f), 0, z - .62f),
                Retreat = new Vector3(side * (half + 1.95f), 0, z - 1.22f), Outside = new Vector3(side * (half + 1.02f), 0, z - .24f),
                Recovery = new Vector3(side * (half + 1.88f), 0, z + .72f) };
            var edge = new Vector3(side * (half + .43f), 0, z - .16f);
            if (!RemovalSpace(car, j.Start, j.Door) || !RemovalSpace(car, j.Door, j.Grip) ||
                !RemovalSpace(car, j.Grip, j.Retreat) || !RemovalSpace(car, edge, j.Outside) || !RemovalSpace(car, j.Outside, j.Recovery)) return false;
            j.Driver = ctx.Incidents.BeginRemoval(car, side, j.Recovery); if (j.Driver == null) return false;
            int door = side < 0 ? 0 : 1;
            var handle = car.Root.TransformPoint(new Vector3(side * (half + .02f), 1.12f + car.Data.seatMount.y, z - .28f));
            j.Handle = door < car.Doors.Length && car.Doors[door] ? car.Doors[door].InverseTransformPoint(handle) : car.Root.InverseTransformPoint(handle);
            carjacking = j; active = transitionVehicle = car; transition = 0; exiting = false; entrySide = side;
            car.LastUsed = ctx.Clock; car.Controlled = true; car.Commanded = false; car.Passenger = car.ExitRequested = false;
            car.RemoteUntil = car.JamUntil = car.TargetSpeed = 0; car.Body.WakeUp();
            ctx.Combat.Holster(); ctx.Combat.CancelMelee(); ctx.Motor.SetInputLock(true); ctx.Motor.SetVisible(true);
            ctx.Motor.Controller.enabled = false; return true;
        }
        void BeginRemovalReturn()
        {
            var j = carjacking; if (j == null || j.Returning) return;
            j.Returning = true; j.ReturnPlayer = j.Car.Root.InverseTransformPoint(ctx.Player.position);
            j.ReturnDriver = j.Driver.Actor != null ? j.Car.Root.InverseTransformPoint(j.Driver.Actor.Root.position) : j.Seat;
            j.ReturnRotation = j.Driver.Actor != null ? Quaternion.Inverse(j.Car.Root.rotation) * j.Driver.Actor.Root.rotation : Quaternion.identity;
            j.ReturnDoor = j.Car.DoorOpen;
            j.ReturnSeat = 1 - Mathf.SmoothStep(0, 1, Phase(j.Time, WalkToDoor + OpenDoor + TakeHold + .22f, PullOut * .78f));
        }
        void EndRemovalReturn()
        {
            var j = carjacking; if (j == null) return;
            ctx.Incidents.CancelRemoval(j.Driver); j.Car.Controlled = false; j.Car.Data.auto = j.WasAutomatic;
            active = transitionVehicle = null; carjacking = null; transition = 0;
            ctx.Motor.SetInputLock(false); ctx.Motor.SetRide(null); ctx.Motor.SetVisible(true);
            if (ctx.Combat.Down) ctx.Motor.Controller.enabled = false;
            else ctx.Motor.Warp(RemovalFeet(j.Car, j.Start));
        }
        void TickCarjacking(float dt)
        {
            var j = carjacking; var car = j.Car; var actor = j.Driver.Actor;
            if (!car.Root.gameObject.activeInHierarchy || ctx.Combat.Down || ctx.Police.Custody)
            { EndRemovalReturn(); return; }
            if (j.Returning)
            {
                j.ReturnTime += dt; float p = Mathf.SmoothStep(0, 1, j.ReturnTime / .65f);
                var returningPlayer = Vector3.Lerp(j.ReturnPlayer, j.Start, p);
                ctx.Motor.SetCarriedPose(RemovalFeet(car, returningPlayer), car.Root.rotation * j.StartRotation, 0, "carjack");
                if (actor != null)
                {
                    actor.Root.SetPositionAndRotation(car.Root.TransformPoint(Vector3.Lerp(j.ReturnDriver, j.Seat, p)),
                        car.Root.rotation * Quaternion.Slerp(j.ReturnRotation, Quaternion.identity, p));
                }
                car.DoorOpen = j.ReturnDoor * (1 - Phase(j.ReturnTime, .35f, .3f));
                if (p >= 1) EndRemovalReturn(); return;
            }
            if (car.Body.linearVelocity.magnitude > 1.8f || Vector3.Dot(car.Root.up, Vector3.up) < .8f)
            { BeginRemovalReturn(); return; }
            j.Time += dt; transition = Mathf.Clamp01(j.Time / RemovalDuration);
            float walk = Phase(j.Time, 0, WalkToDoor), open = Phase(j.Time, WalkToDoor, OpenDoor);
            float hold = Phase(j.Time, WalkToDoor + OpenDoor, TakeHold);
            float pull = Phase(j.Time, WalkToDoor + OpenDoor + TakeHold, PullOut);
            float recover = Phase(j.Time, RemovalDuration - RegainFeet, RegainFeet);
            Vector3 player = Vector3.Lerp(j.Start, j.Door, CityPhysics.Ease(walk));
            if (hold > 0) player = Vector3.Lerp(j.Door, j.Grip, CityPhysics.Ease(hold));
            if (pull > 0) player = Vector3.Lerp(j.Grip, j.Retreat, CityPhysics.Ease(pull));
            if (recover > 0) player = Vector3.Lerp(j.Retreat, j.Grip, CityPhysics.Ease(recover));
            if (!RemovalSpace(car, car.Root.InverseTransformPoint(ctx.Player.position), player))
            { BeginRemovalReturn(); return; }
            Quaternion inward = Quaternion.Euler(0, -entrySide * 90, 0);
            ctx.Motor.SetCarriedPose(RemovalFeet(car, player), car.Root.rotation * Quaternion.Slerp(j.StartRotation, inward, CityPhysics.Ease(walk)), 0, "carjack");
            car.DoorOpen = CityPhysics.Ease(open);
            if (open > 0 && open - dt / OpenDoor <= 0) ctx.Sound("door", car.Position);
            if (hold > 0 && actor == null)
            {
                var head = car.Driver.GetComponentsInChildren<Transform>(true);
                foreach (var node in head) if (node.name.IndexOf("driver-head", System.StringComparison.OrdinalIgnoreCase) >= 0)
                { j.Head = car.Root.InverseTransformPoint(node.position); break; }
                ctx.Incidents.MaterializeRemoval(j.Driver); actor = j.Driver.Actor;
                j.Seat = car.Root.InverseTransformPoint(actor.Root.position);
            }
            if (actor != null && j.Aligned)
            {
                Vector3 near = j.Seat; near.x = entrySide * (car.Data.width * .5f - .52f); near.z -= .06f;
                var root = Vector3.Lerp(j.Seat, near, CityPhysics.Ease(hold));
                var outside = car.Root.InverseTransformPoint(RemovalFeet(car, j.Outside));
                if (pull > 0)
                {
                    var next = Vector3.Lerp(near, outside, CityPhysics.Ease(pull));
                    if (Mathf.Abs(next.x) > car.Data.width * .5f + .38f && !RemovalSpace(car, car.Root.InverseTransformPoint(actor.Root.position), next))
                    { BeginRemovalReturn(); return; }
                    root = next;
                }
                if (recover > 0)
                {
                    if (!RemovalSpace(car, j.Outside, j.Recovery)) { BeginRemovalReturn(); return; }
                    root = Vector3.Lerp(outside, car.Root.InverseTransformPoint(RemovalFeet(car, j.Recovery)), CityPhysics.Ease(recover));
                }
                actor.Root.SetPositionAndRotation(car.Root.TransformPoint(root), car.Root.rotation * Quaternion.Euler(0, entrySide * 90 * CityPhysics.Ease(hold), 0));
            }
            if (pull > 0 && pull - dt / PullOut <= 0) ctx.Sound("shove", car.Position, .45f);
            if (j.Time >= RemovalDuration)
            {
                float door = car.DoorOpen; ctx.Incidents.CompleteRemoval(j.Driver); car.DoorOpen = door;
                Crime?.Invoke(car.Data.role == "police" ? "furto-policial" : "furto", car.Position);
                carjacking = null; Board(car);
            }
        }
        static void SeatedPose(CityRig rig, float seated, float lean)
        {
            rig.Hips.localPosition = new Vector3(0, Mathf.Lerp(rig.HipHeight, .61f, seated), 0);
            rig.Hips.localRotation = Quaternion.Euler(lean, 0, 0); rig.Chest.localRotation = Quaternion.Euler(-lean * .25f, 0, 0);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1 : 1;
                rig.Legs[i].localRotation = Quaternion.Slerp(rig.Legs[i].localRotation, Quaternion.Euler(-70, 0, side * 7), seated);
                rig.Knees[i].localRotation = Quaternion.Slerp(rig.Knees[i].localRotation, Quaternion.Euler(76, 0, 0), seated);
                rig.Feet[i].localRotation = Quaternion.Slerp(rig.Feet[i].localRotation, Quaternion.Euler(-12, 0, 0), seated);
            }
        }
        // Called after the ordinary rigs and combat poses, before rendering.
        public void PoseBoarding()
        {
            if (!ctx || !ctx.Ready || ctx.Paused || ctx.Combat.Down) return;
            var rig = ctx.Actors.PlayerRig; if (rig == null) return;
            var j = carjacking;
            if (j == null)
            {
                if (transitionVehicle == null || transitionVehicle.Data.kind == "bike" || transitionVehicle.Data.kind == "bus") return;
                float seated = exiting ? 1 - Mathf.SmoothStep(0, 1, transition / .78f) : Mathf.SmoothStep(0, 1, (transition - .18f) / .78f);
                SeatedPose(rig, seated, 14 * Mathf.Sin(transition * Mathf.PI));
                if (!exiting) for (int i = 0; i < 2 && i < transitionVehicle.Hands.Length; i++)
                    if (transitionVehicle.Hands[i]) CityActors.Reach(rig, i, transitionVehicle.Hands[i].position, seated);
                return;
            }
            float hold = Phase(j.Time, WalkToDoor + OpenDoor, TakeHold), pull = Phase(j.Time, WalkToDoor + OpenDoor + TakeHold, PullOut);
            float recovery = Phase(j.Time, RemovalDuration - RegainFeet, RegainFeet);
            float grab = CityPhysics.Ease(hold) * (1 - Mathf.SmoothStep(0, 1, (pull - .72f) / .28f));
            var actor = j.Driver.Actor; var victim = actor?.Rig;
            if (victim != null)
            {
                float seated = 1 - Mathf.SmoothStep(0, 1, (pull - .18f) / .82f);
                if (j.Returning) { seated = Mathf.Lerp(j.ReturnSeat, 1, CityPhysics.Ease(j.ReturnTime / .65f)); grab *= 1 - CityPhysics.Ease(j.ReturnTime / .65f); }
                ctx.Actors.Animate(victim, new CityPose { Mode = "idle", Time = ctx.Clock }, Mathf.Max(.001f, Time.deltaTime));
                SeatedPose(victim, seated, seated * 12 + Mathf.Sin(pull * Mathf.PI) * 18);
                victim.Head.localRotation = Quaternion.Euler(-3 - 9 * Mathf.Sin(pull * Mathf.PI), 0, 0);
                if (!j.Aligned)
                {
                    victim.Root.position += j.Car.Root.TransformPoint(j.Head) - victim.Head.position;
                    j.Seat = j.Car.Root.InverseTransformPoint(victim.Root.position); j.Aligned = true;
                }
                float stagger = Mathf.Sin(recovery * Mathf.PI);
                victim.Hips.localPosition -= Vector3.up * (.18f * stagger);
                for (int i = 0; i < 2; i++)
                {
                    victim.Arms[i].localRotation = Quaternion.Euler(-32 - 28 * stagger, 0, (i == 0 ? -1 : 1) * (20 + 22 * stagger));
                    victim.Elbows[i].localRotation = Quaternion.Euler(-40, 0, 0);
                }
                if (grab > .01f) CityActors.Reach(victim, entrySide < 0 ? 0 : 1, rig.Elbows[1].position, grab * .65f);
            }
            float lean = 10 * Phase(j.Time, 0, WalkToDoor) + 17 * grab;
            rig.Hips.localPosition += Vector3.forward * (.12f * grab);
            rig.Chest.localRotation = Quaternion.Euler(lean, 0, -entrySide * 5 * grab);
            if (victim != null && grab > 0)
            {
                int near = entrySide < 0 ? 0 : 1;
                CityActors.Reach(rig, 1, victim.Arms[near].position + Vector3.down * .10f, grab);
                CityActors.Reach(rig, 0, victim.Elbows[near].position, grab);
            }
            int index = entrySide < 0 ? 0 : 1;
            var handle = index < j.Car.Doors.Length && j.Car.Doors[index] ? j.Car.Doors[index].TransformPoint(j.Handle) : j.Car.Root.TransformPoint(j.Handle);
            CityActors.Reach(rig, 0, handle, (1 - grab) * (1 - pull) * Phase(j.Time, .10f, .24f));
        }
        void OnDisable()
        {
            if (carjacking != null && ctx && ctx.Incidents && ctx.Motor && ctx.Combat) EndRemovalReturn();
        }
    }
}
