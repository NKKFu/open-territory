using UnityEngine;

namespace OpenTerritory
{
    public enum CityEmote { None = -1, Happy, Sad, Dance, Wave, Laugh, Clap }

    // Q is resolved on release: a tap talks, a hold selects one gesture.
    // This runs in CityContext before any simulation reads the sampled input.
    public sealed class CityEmotes : MonoBehaviour
    {
        public const float HoldDelay = .22f;
        public static readonly string[] Names = { "Feliz", "Triste", "Dançar", "Acenar", "Rir", "Aplaudir" };
        CityContext ctx;
        CityEmoteWheel view;
        bool pending, waitForRelease, pointerArmed, swallowPointer;
        float pressedAt, healthAtStart;
        Vector2 pointerAtOpen;
        public bool WheelOpen { get; private set; }
        public CityEmote Selected { get; private set; } = CityEmote.None;
        public CityEmote Current { get; private set; } = CityEmote.None;
        public float StartedAt { get; private set; }
        public float Until { get; private set; }
        public int LastReactionCount { get; private set; }
        public bool Performing => Current != CityEmote.None && ctx && ctx.Clock < Until;
        public bool CanOpen => ctx && !ctx.Paused && !ctx.UI.MapOpen && !ctx.Motor.Busy && !ctx.Motor.Riding && !ctx.Motor.Swimming &&
            !ctx.Motor.SeatHeight.HasValue && ctx.Motor.Grounded && !ctx.Remote.Active && !ctx.Remote.Scanning;

        public void Initialize(CityContext context)
        {
            ctx = context; view = gameObject.AddComponent<CityEmoteWheel>(); view.Initialize(ctx, this);
        }
        public void SampleInput()
        {
            var input = ctx.Input;
            bool wasOpen = WheelOpen;
            if (input.Pause || ctx.Paused || !Application.isFocused && !Application.isBatchMode)
            { CancelInput(); input.Talk = false; return; }
            if (Performing && (!CanOpen || input.Move.sqrMagnitude > .01f || input.Jump || input.CrouchPressed || input.Interact || input.Talk || input.TalkPressed ||
                input.HackPressed || input.Drone || input.Fire || input.MeleePressed || input.AimHeld || input.AimToggle || input.Fall || input.Weapon > 0 ||
                ctx.Combat.Health < healthAtStart)) CancelPerformance();
            if (!Performing && Current != CityEmote.None) CancelPerformance();
            if (waitForRelease)
            {
                if (!input.TalkHeld) waitForRelease = false;
                input.Talk = false; return;
            }
            if (!CanOpen && WheelOpen) { CancelInput(); return; }
            if (input.TalkPressed) { pending = true; pressedAt = Time.unscaledTime; }
            if (CanOpen && pending && input.TalkHeld && Time.unscaledTime - pressedAt >= HoldDelay)
            {
                pending = false; WheelOpen = true; Selected = CityEmote.None;
                ctx.RefreshCursor();
                pointerAtOpen = Pointer; pointerArmed = false;
                ctx.Motor.ClearMotion(); view.Show(true);
            }
            if (WheelOpen)
            {
                Vector2 pointer = Pointer;
                if ((pointer - pointerAtOpen).sqrMagnitude > 25) pointerArmed = true;
                Selected = pointerArmed ? view.Pick(pointer) : CityEmote.None;
                view.Highlight(Selected);
                if (input.TalkReleased || !input.TalkHeld)
                {
                    var choice = Selected; CloseWheel(); pending = false;
                    if (choice != CityEmote.None) Perform(choice);
                }
            }
            else if (pending && input.TalkReleased)
            { pending = false; input.Talk = true; }
            // Mouse buttons used while selecting must never become shots/camera drags.
            if (wasOpen || WheelOpen)
            {
                SuppressSelectionInput(input);
                swallowPointer = UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1);
            }
            else if (swallowPointer)
            {
                input.Fire = input.FireHeld = input.AimHeld = false; input.Look = Vector2.zero;
                swallowPointer = UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1);
            }
        }
        Vector2 Pointer => ctx.Input.TouchEmotePointer ?? (Vector2)UnityEngine.Input.mousePosition;
        static void SuppressSelectionInput(CityInput input)
        {
            input.Move = input.Look = Vector2.zero; input.Zoom = 0;
            input.Talk = input.Interact = input.Run = input.Jump = input.Fire = input.FireHeld = input.AimHeld = input.AimToggle = false;
            input.HackPressed = input.HackHeld = input.HackReleased = input.HackerToggle = input.HackNext = input.Drone = false;
            input.Fall = input.Reload = input.Holster = false; input.Weapon = 0;
            input.MeleePressed = false;
            input.CrouchPressed = false;
        }
        public bool Perform(CityEmote emote)
        {
            if (!CanOpen || (int)emote < 0 || (int)emote >= Names.Length) return false;
            ctx.Combat.Holster(); ctx.Actors.CancelInteraction(); ctx.Motor.ClearMotion();
            Current = emote; StartedAt = ctx.Clock; Until = ctx.Clock + (emote == CityEmote.Dance ? 5.4f : 3.4f);
            healthAtStart = ctx.Combat.Health;
            view.Bubble(ctx.Player, emote, Until, false);
            LastReactionCount = ctx.Residents.ReactToEmote(emote);
            return true;
        }
        public void ShowReaction(Transform person, CityEmote emote, float until) => view.Bubble(person, emote, until, true);
        public void CancelPerformance()
        {
            Current = CityEmote.None; Until = 0;
            if (view && ctx) view.HideBubble(ctx.Player);
        }
        public void CancelInput()
        {
            pending = false; waitForRelease = ctx && ctx.Input.TalkHeld;
            CloseWheel();
        }
        void CloseWheel() { WheelOpen = false; Selected = CityEmote.None; if (view) view.Show(false); if (ctx) ctx.RefreshCursor(); }
        void OnDisable() { CancelInput(); CancelPerformance(); }
    }
}
