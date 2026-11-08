using UnityEngine;

namespace OpenTerritory
{
    public static class CityEmoteAnimation
    {
        // Visual offsets only: the feet origin and collision capsule never move.
        public static void Apply(CityRig r, CityEmote emote, float elapsed, float remaining, bool seated = false)
        {
            if (r == null || !r.Valid) return;
            float weight = Mathf.SmoothStep(0, 1, elapsed / .24f) * Mathf.SmoothStep(0, 1, remaining / .35f);
            float beat = Mathf.Sin(elapsed * 7), bounce = Mathf.Sin(elapsed * 14);
            Vector3 chest = Vector3.zero, head = Vector3.zero;
            switch (emote)
            {
                case CityEmote.Happy: chest = new Vector3(-5, 0, beat * 4); head = new Vector3(-8, 0, -beat * 4); break;
                case CityEmote.Sad: chest = new Vector3(12, 0, 0); head = new Vector3(22, -5, 6); break;
                case CityEmote.Dance: chest = new Vector3(4 + bounce * 3, beat * 16, -beat * 9); head = new Vector3(0, -beat * 10, beat * 7); break;
                case CityEmote.Wave: head = new Vector3(-3, beat * 5, -5); break;
                case CityEmote.Laugh: chest = new Vector3(9 + bounce * 3, 0, beat * 3); head = new Vector3(-12 + bounce * 4, 0, 0); break;
                case CityEmote.Clap: head = new Vector3(-4 + beat * 3, 0, 0); break;
            }
            r.Chest.localRotation = Quaternion.Slerp(r.Chest.localRotation, Quaternion.Euler(chest), weight);
            r.Head.localRotation = Quaternion.Slerp(r.Head.localRotation, Quaternion.Euler(head), weight);
            if (emote == CityEmote.Dance && !seated)
            {
                r.Hips.localRotation = Quaternion.Slerp(r.Hips.localRotation, Quaternion.Euler(0, -beat * 12, beat * 5), weight);
                // Bend alternately with both feet planted, instead of skating the capsule.
                for (int i = 0; i < 2; i++)
                {
                    float bend = 7 + (i == 0 ? beat : -beat) * 6;
                    r.Legs[i].localRotation *= Quaternion.Euler(-bend * .5f * weight, 0, 0);
                    r.Knees[i].localRotation *= Quaternion.Euler(bend * weight, 0, 0);
                    r.Feet[i].localRotation *= Quaternion.Euler(-bend * .5f * weight, 0, 0);
                }
            }
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1 : 1;
                Vector3 arm = new Vector3(-10, 0, side * 7), elbow = new Vector3(-18, 0, 0);
                switch (emote)
                {
                    case CityEmote.Happy: arm = new Vector3(-45 - beat * 9, 0, side * 34); elbow.x = -62; break;
                    case CityEmote.Sad: arm = new Vector3(-8, 0, side * 4); elbow.x = -8; break;
                    case CityEmote.Dance: arm = new Vector3(-64 + side * beat * 28, side * 12, side * (38 + bounce * 8)); elbow.x = -70 + side * beat * 20; break;
                    case CityEmote.Wave:
                        if (i == 1) { arm = new Vector3(-135, 0, 18 + beat * 13); elbow = new Vector3(-22, beat * 18, 0); }
                        break;
                    case CityEmote.Laugh: arm = new Vector3(-37 + bounce * 3, side * 14, side * 20); elbow.x = -95; break;
                    case CityEmote.Clap:
                        var point = r.Root.TransformPoint(new Vector3(side * (.085f + .15f * (.5f + .5f * Mathf.Cos(elapsed * 12))), seated ? 1.1f : 1.48f, .49f));
                        CityActors.Reach(r, i, point, weight); continue;
                }
                r.Arms[i].localRotation = Quaternion.Slerp(r.Arms[i].localRotation, Quaternion.Euler(arm), weight);
                r.Elbows[i].localRotation = Quaternion.Slerp(r.Elbows[i].localRotation, Quaternion.Euler(elbow), weight);
            }
        }
    }
}
