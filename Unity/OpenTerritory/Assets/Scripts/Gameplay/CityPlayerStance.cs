using UnityEngine;

namespace OpenTerritory
{
    public partial class CityPlayer
    {
        public const float CrouchHeight = 2.10f, CompactCrouchHeight = 1.70f, CrouchSpeed = 1.35f;
        // The head and hair reach 2.49 m in the standing pose; the movement
        // capsule alone is too short to decide whether the model can stand.
        const float StandingClearance = 2.52f;
        bool wantsCrouch, crouching;
        float crouchBlend, compactCrouchBlend;
        public bool Crouched => crouching;
        public float CrouchBlend => Mathf.SmoothStep(0, 1, crouchBlend);
        public float CompactCrouchBlend => Mathf.SmoothStep(0, 1, compactCrouchBlend);
        float CrouchedCapsuleHeight => Mathf.Lerp(CrouchHeight, CompactCrouchHeight, CompactCrouchBlend);
        public bool CanStand => ctx && CityPhysics.StandClear(ctx.Player.position, ctx.Player, null, StandingClearance, .38f);

        void SetCapsuleHeight(float height)
        {
            if (!controller) return;
            if (Mathf.Abs(controller.height - height) > .0001f) controller.height = height;
            var center = Vector3.up * (height * .5f);
            if ((controller.center - center).sqrMagnitude > .000001f) controller.center = center;
        }
        void ResetCrouch(bool fitSpace = false)
        {
            wantsCrouch = false; crouching = fitSpace && !CanStand;
            crouchBlend = crouching ? 1 : 0;
            compactCrouchBlend = crouching && NeedsCompactCrouch(ctx.Player.position) ? 1 : 0;
            SetCapsuleHeight(crouching ? CrouchedCapsuleHeight : StandingHeight);
        }
        bool NeedsCompactCrouch(Vector3 feet)
        {
            // Full-height walls must not cause a duck. Only lower the torso
            // when the compact capsule fits underneath an overhead obstacle.
            return !CityPhysics.StandClear(feet, ctx.Player, null, CrouchHeight, .38f)
                && CityPhysics.StandClear(feet, ctx.Player, null, CompactCrouchHeight, .38f);
        }
        void UpdateCrouch(float dt)
        {
            if (ctx.Input.CrouchPressed && Grounded)
            {
                wantsCrouch = !wantsCrouch; jumpWindup = -1; jumpUntil = -10;
            }
            RestoreStandingCapsule();
            // A stand request stays pending until the whole capsule fits.
            // Never expand through a roof or change the feet origin.
            bool lowered = wantsCrouch && Grounded || compactRecovery;
            if (!lowered && crouching && !CanStand) lowered = true;
            crouching = lowered;
            bool compact = crouching && (NeedsCompactCrouch(ctx.Player.position)
                || MoveDirection.sqrMagnitude > .01f && NeedsCompactCrouch(ctx.Player.position + MoveDirection.normalized * .65f));
            compactCrouchBlend = Mathf.MoveTowards(compactCrouchBlend, compact ? 1 : 0, dt * 5);
            SetCapsuleHeight(crouching ? CrouchedCapsuleHeight : StandingHeight);
            crouchBlend = Mathf.MoveTowards(crouchBlend, crouching ? 1 : 0, dt * 4.5f);
        }
        bool StandForJump()
        {
            if (!CanStand) { jumpUntil = -10; return false; }
            wantsCrouch = crouching = compactRecovery = false;
            compactCrouchBlend = 0;
            SetCapsuleHeight(StandingHeight); return true;
        }
    }
}
