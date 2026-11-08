using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityResidents
    {
        public bool CanReactToEmote(Resident p)
        {
            if (!AvailableForTalk(p) || p.Crossing || p.Doorway != null || p.DodgeUntil > ctx.Clock || IsReactingToHack(p)) return false;
            Vector3 from = ctx.Player.position + Vector3.up * 1.6f, to = p.Root.position + Vector3.up * 1.6f;
            return (to - from).sqrMagnitude <= 36 && ctx.Visible(from, to);
        }
        public int ReactToEmote(CityEmote emote)
        {
            Resident nearest = null; float best = float.MaxValue;
            foreach (var p in People)
            {
                if (!CanReactToEmote(p)) continue;
                var screen = ctx.Camera.WorldToViewportPoint(p.Root.position + Vector3.up * 1.6f);
                if (screen.z <= 0 || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1) continue;
                float score = (screen.x - .5f) * (screen.x - .5f) * 16 + (p.Root.position - ctx.Player.position).sqrMagnitude * .01f;
                if (score >= best) continue; best = score; nearest = p;
            }
            if (nearest == null) return 0;
            int count = 0;
            // The addressed resident responds first. Up to two friends nearby join in.
            React(nearest, emote, true); count++;
            foreach (var p in People)
            {
                if (count >= 3) break;
                if (p == nearest || !CanReactToEmote(p) || (p.Root.position - nearest.Root.position).sqrMagnitude > 9) continue;
                React(p, emote, false); count++;
            }
            return count;
        }
        void React(Resident p, CityEmote offered, bool speak)
        {
            Interrupt(p); p.TalkUntil = 0; p.MoveSpeed = 0; p.Velocity = Vector3.zero;
            CityEmote response = offered == CityEmote.Sad ? CityEmote.Wave : offered == CityEmote.Dance ?
                ((p.Data.id & 1) == 0 && !p.Data.sitting && !p.Data.performance ? CityEmote.Dance : CityEmote.Clap) : offered;
            if ((p.Data.sitting || p.Data.performance) && response == CityEmote.Dance) response = CityEmote.Clap;
            p.Emote = response; p.EmoteStarted = ctx.Clock; p.EmoteUntil = ctx.Clock + (response == CityEmote.Dance ? 5.2f : 3.6f);
            p.NextSocial = p.EmoteUntil + 8;
            ctx.Emotes.ShowReaction(p.Root, response, p.EmoteUntil);
            if (!speak) return;
            string vocal = offered == CityEmote.Laugh ? "laugh" : offered == CityEmote.Sad ? "sigh" :
                offered == CityEmote.Dance || offered == CityEmote.Clap ? "cheer" : "ack";
            ctx.Systems.Audio?.NpcVocal(p, vocal, .9f);
            string line;
            switch (offered)
            {
                case CityEmote.Sad: line = "Ei, estou por aqui se quiser conversar."; break;
                case CityEmote.Dance: line = response == CityEmote.Dance ? "Essa eu acompanho!" : "Boa! Manda ver!"; break;
                case CityEmote.Wave: line = "Oi! Tudo bem por aí?"; break;
                case CityEmote.Laugh: line = "Hahaha! Essa foi boa!"; break;
                case CityEmote.Clap: line = "Aí sim! Valeu!"; break;
                default: line = "Que bom te ver assim!"; break;
            }
            ctx.Say(p.Data.name + " · " + line, p.Root.position + Vector3.up * 2.55f, 3.6f);
        }
        bool TickEmote(Resident p, float dt)
        {
            if (p.Emote == CityEmote.None) return false;
            if (p.EmoteUntil <= ctx.Clock || p.FearUntil > ctx.Clock || p.Crossing || p.DodgeUntil > ctx.Clock ||
                Vector3.Distance(p.Root.position, ctx.Player.position) > 8)
            { ctx.Systems.Audio?.StopNpcVoice(p); p.Emote = CityEmote.None; p.EmoteUntil = 0; return false; }
            p.MoveSpeed = 0; Face(p, ctx.Player.position, dt); return true;
        }
    }
}
