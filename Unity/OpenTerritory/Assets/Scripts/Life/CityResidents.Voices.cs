namespace OpenTerritory
{
    public sealed partial class CityResidents
    {
        void TickConversationVoice(Resident person)
        {
            if (ctx.Clock < person.NextMurmur || person.TalkUntil - ctx.Clock < .55f) return;
            if (ctx.Systems.Audio && ctx.Systems.Audio.NpcVocal(person, "talk"))
                person.NextMurmur = person.NextVocal + .25f;
            else person.NextMurmur = ctx.Clock + .4f;
        }
        void TickSocialVoice(Resident person)
        {
            var partner = person.Partner;
            if (partner == null || person.SocialUntil - ctx.Clock < .6f || ctx.Clock < person.NextMurmur ||
                partner.Down || partner.Controlled || partner.TalkUntil > ctx.Clock) return;
            // Each exchange has one turn per participant, never two simultaneous mouths.
            if (ctx.Systems.Audio && !ctx.Systems.Audio.NpcSource(partner) && ctx.Systems.Audio.NpcVocal(person, "talk", 1, false))
                person.NextMurmur = person.SocialUntil;
            else person.NextMurmur = ctx.Clock + .35f;
        }
    }
}
