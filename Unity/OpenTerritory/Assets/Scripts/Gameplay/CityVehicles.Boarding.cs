using UnityEngine;

namespace OpenTerritory
{
    public partial class CityVehicles
    {
        // The body, door and limb contacts share this clock. All anchors belong
        // to the vehicle, so suspension and a small physical nudge stay coherent.
        sealed class BoardingMotion
        {
            public Vector3 Start, Door, Sill, Handle, Hips;
            public Quaternion Rotation, HipRotation, Chest, Head;
            public readonly Vector3[] Feet = new Vector3[2];
            public float Duration;
        }
        BoardingMotion boarding;
        public string BoardingPhase => boarding == null || exiting ? "" : transition < .12f ? "approach" :
            transition < .30f ? "opening" : transition < .46f ? "ducking" : transition < .72f ? "seating" :
            transition < .86f ? "feet-in" : "closing";

        static float Blend(float time, float start, float end) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, end, time));
        int DoorIndex => entrySide < 0 ? 0 : 1;
        Vector3 DoorContact(Vehicle car, Vector3 closed)
        {
            int i = DoorIndex;
            if (i >= car.Doors.Length || !car.Doors[i]) return car.Root.TransformPoint(closed);
            var door = car.Doors[i];
            var local = Quaternion.Inverse(car.DoorRotations[i]) *
                (door.parent.InverseTransformPoint(car.Root.TransformPoint(closed)) - car.DoorHomes[i]);
            local = new Vector3(local.x / door.localScale.x, local.y / door.localScale.y, local.z / door.localScale.z);
            return door.TransformPoint(local);
        }
        void BeginBoarding(Vehicle car)
        {
            entrySide = ApproachSide(car); entryDoorStart = car.DoorOpen;
            var rig = ctx.Actors.PlayerRig;
            boarding = new BoardingMotion {
                Start = car.Root.InverseTransformPoint(ctx.Player.position),
                Rotation = Quaternion.Inverse(car.Root.rotation) * ctx.Player.rotation,
                Door = new Vector3(entrySide * (car.Data.width * .5f + .53f), 0, car.Data.seatMount.z - .46f),
                Sill = new Vector3(entrySide * (car.Data.width * .5f - .34f), car.Data.seatMount.y, car.Data.seatMount.z - .08f),
                Handle = new Vector3(entrySide * (car.Data.width * .5f + .02f), 1.12f + car.Data.seatMount.y, car.Data.seatMount.z - .28f),
                Hips = rig.Hips.localPosition, HipRotation = rig.Hips.localRotation,
                Chest = rig.Chest.localRotation, Head = rig.Head.localRotation
            };
            boarding.Door = car.Root.InverseTransformPoint(RemovalFeet(car, boarding.Door));
            boarding.Duration = 1.8f + Mathf.Max(0, Mathf.Abs(boarding.Door.x - car.Data.seatMount.x) - 1.4f) * .18f;
            for (int i = 0; i < 2; i++) boarding.Feet[i] = car.Root.InverseTransformPoint(rig.Feet[i].position);
            transitionVehicle = car; transition = 0; exiting = false;
            ctx.Motor.SetInputLock(true);
        }
        void TickBoarding(float dt)
        {
            var car = transitionVehicle; var b = boarding;
            float previous = transition; transition = Mathf.Min(1, transition + dt / b.Duration);
            float t = transition, approach = Blend(t, 0, .16f), enter = Blend(t, .35f, .56f), sit = Blend(t, .54f, .76f);
            car.DoorOpen = Mathf.Lerp(entryDoorStart, 1, Blend(t, .12f, .30f)) * (1 - Blend(t, .86f, 1));
            if (previous < .12f && t >= .12f && entryDoorStart < .8f) ctx.Sound("door", car.Position);
            if (previous < .94f && t >= .94f) ctx.Sound("door", car.Position, .7f);
            var point = Vector3.Lerp(b.Start, b.Door, approach);
            point = Vector3.Lerp(point, b.Sill, enter);
            point = Vector3.Lerp(point, car.Data.seatMount, sit);
            var inward = Quaternion.Euler(0, -entrySide * 75, 0);
            var rotation = Quaternion.Slerp(b.Rotation, inward, approach);
            rotation = Quaternion.Slerp(rotation, Quaternion.identity, Blend(t, .30f, .72f));
            ctx.Motor.SetCarriedPose(car.Root.TransformPoint(point), car.Root.rotation * rotation, 0, "car-door");
            if (t < 1) return;
            boarding = null; transitionVehicle = null;
            ctx.Motor.SetInputLock(false); ctx.Motor.SetVisible(true);
            if (car.Driver) car.Driver.gameObject.SetActive(false);
        }

        Vector3 DoorFoot(Vehicle car, CityRig rig, int i)
        {
            var p = boarding.Door + Quaternion.Euler(0, -entrySide * 75, 0) * new Vector3(rig.Legs[i].localPosition.x, 0, 0);
            p.y += rig.Ankle; return p;
        }
        void PoseEntry(CityRig rig)
        {
            var car = transitionVehicle; var b = boarding; float t = transition;
            float settle = Blend(t, 0, .12f), crouch = Blend(t, .27f, .50f) * (1 - Blend(t, .70f, .90f));
            float sit = Blend(t, .40f, .76f), close = Mathf.Sin(Blend(t, .83f, 1) * Mathf.PI);
            rig.Hips.localScale = Vector3.Lerp(rig.HipScaleRest, rig.HipScaleRest * CityDrivingPose.ModelScale, Blend(t, .30f, .76f));
            rig.Hips.localPosition = Vector3.Lerp(b.Hips, new Vector3(entrySide * (.045f * crouch + .06f * close),
                Mathf.Lerp(rig.HipHeight, CityDrivingPose.Hips.y, sit) - .15f * crouch,
                Mathf.Lerp(.08f * crouch, CityDrivingPose.Hips.z, sit)), settle);
            rig.Hips.localRotation = Quaternion.Slerp(b.HipRotation, Quaternion.Euler(3 + 9 * crouch, -entrySide * 10 * crouch, 0), settle);
            rig.Chest.localRotation = Quaternion.Slerp(b.Chest, Quaternion.Euler(30 * crouch - 1.4f * sit, entrySide * 14 * close, -entrySide * 5 * crouch), settle);
            rig.Head.localRotation = Quaternion.Slerp(b.Head, Quaternion.Euler(12 * crouch - 2 * sit, entrySide * 15 * close, 0), settle);
            int lead = entrySide < 0 ? 1 : 0, doorArm = entrySide < 0 ? 0 : 1;
            for (int i = 0; i < 2; i++)
            {
                float step = Blend(t, i == lead ? .30f : .57f, i == lead ? .55f : .86f);
                var foot = Vector3.Lerp(b.Feet[i], DoorFoot(car, rig, i), Blend(t, i == lead ? 0 : .04f, i == lead ? .13f : .19f));
                var seatedFoot = CityDrivingPose.FootPoint(car.Data, i);
                if (i == lead)
                {
                    var sill = b.Sill + new Vector3(0, rig.Ankle + .13f, .30f);
                    sill.y = Mathf.Max(sill.y, CityDrivingPose.FloorTop + rig.Ankle + .10f);
                    foot = Vector3.Lerp(foot, sill, step) + Vector3.up * (.18f * Mathf.Sin(step * Mathf.PI));
                    foot = Vector3.Lerp(foot, seatedFoot, Blend(t, .53f, .77f));
                }
                else foot = Vector3.Lerp(foot, seatedFoot, step) + Vector3.up * (.23f * Mathf.Sin(step * Mathf.PI));
                CityActors.PlaceFoot(rig, i, car.Root.TransformPoint(foot));
                float side = i == 0 ? -1 : 1;
                rig.Arms[i].localRotation = Quaternion.Euler(-18 - 48 * crouch, 0, side * 9);
                rig.Elbows[i].localRotation = Quaternion.Euler(-25 - 38 * crouch, 0, 0);
                if (i < car.Hands.Length && car.Hands[i]) CityActors.Reach(rig, i, car.Hands[i].position, Blend(t, i == doorArm ? .94f : .61f, 1));
            }
            float handle = Blend(t, .04f, .16f) * (1 - Blend(t, .31f, .43f));
            CityActors.Reach(rig, doorArm, DoorContact(car, b.Handle), handle);
            var frame = car.Root.TransformPoint(new Vector3(entrySide * (car.Data.width * .5f - .12f), 1.24f + car.Data.seatMount.y, car.Data.seatMount.z + .18f));
            CityActors.Reach(rig, 1 - doorArm, frame, Blend(t, .28f, .41f) * (1 - Blend(t, .62f, .77f)));
            var support = car.Root.TransformPoint(new Vector3(entrySide * (car.Data.width * .5f - .08f), 1.03f + car.Data.seatMount.y, car.Data.seatMount.z - .24f));
            CityActors.Reach(rig, doorArm, support, Blend(t, .35f, .46f) * (1 - Blend(t, .76f, .88f)));
            CityActors.Reach(rig, doorArm, DoorContact(car, b.Handle - new Vector3(entrySide * .12f, .10f, 0)), close * .85f);
            float drive = Blend(t, .90f, 1);
            rig.Hips.localRotation = Quaternion.Slerp(rig.Hips.localRotation, CityDrivingPose.HipRotation, drive);
            rig.Chest.localRotation = Quaternion.Slerp(rig.Chest.localRotation, CityDrivingPose.ChestRotation, drive);
            rig.Head.localRotation = Quaternion.Slerp(rig.Head.localRotation, CityDrivingPose.HeadRotation, drive);
            // Upper-body settling changes the leg and hand anchors. Resolve
            // the final contacts again in the same pose used while driving.
            if (drive > 0) for (int i = 0; i < 2; i++)
            {
                CityActors.PlaceFoot(rig, i, car.Root.TransformPoint(CityDrivingPose.FootPoint(car.Data, i)), drive);
                if (i < car.Hands.Length && car.Hands[i]) CityActors.Reach(rig, i, car.Hands[i].position, drive);
            }
        }

        void PoseExit(CityRig rig)
        {
            var car = transitionVehicle; float t = transition, stand = Blend(t, .34f, .94f);
            rig.Hips.localScale = Vector3.Lerp(rig.HipScaleRest * CityDrivingPose.ModelScale, rig.HipScaleRest, stand);
            rig.Hips.localPosition = Vector3.Lerp(CityDrivingPose.Hips, new Vector3(0, rig.HipHeight, 0), stand);
            float duck = Mathf.Sin(Blend(t, 0, .82f) * Mathf.PI);
            rig.Hips.localPosition -= Vector3.up * (.16f * duck);
            rig.Hips.localRotation = Quaternion.Slerp(CityDrivingPose.HipRotation, Quaternion.identity, stand);
            rig.Chest.localRotation = Quaternion.Slerp(CityDrivingPose.ChestRotation, Quaternion.identity, stand) * Quaternion.Euler(18 * duck, 0, 0);
            rig.Head.localRotation = Quaternion.Slerp(CityDrivingPose.HeadRotation, Quaternion.identity, stand);
            int lead = entrySide < 0 ? 0 : 1;
            for (int i = 0; i < 2; i++)
            {
                float step = Blend(t, i == lead ? .10f : .35f, i == lead ? .65f : .94f);
                var from = car.Root.TransformPoint(CityDrivingPose.FootPoint(car.Data, i));
                var to = transitionTo + car.Root.right * rig.Legs[i].localPosition.x + Vector3.up * rig.Ankle;
                var foot = Vector3.Lerp(from, to, step) + car.Root.up * (.16f * Mathf.Sin(step * Mathf.PI));
                CityActors.PlaceFoot(rig, i, foot);
                rig.Arms[i].localRotation = Quaternion.Euler(-18, 0, i == 0 ? -9 : 9);
                rig.Elbows[i].localRotation = Quaternion.Euler(-25, 0, 0);
                if (i < car.Hands.Length && car.Hands[i]) CityActors.Reach(rig, i, car.Hands[i].position, 1 - Blend(t, .04f, .25f));
            }
        }
    }
}
