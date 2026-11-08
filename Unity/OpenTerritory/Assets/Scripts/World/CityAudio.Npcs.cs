using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityAudio
    {
        // Separate from effects: an interrupted speaker can never stop a reused gunshot.
        const int NpcVoiceCapacity = 4;
        sealed class NpcVoice
        {
            public Voice voice;
            public CityResidents.Resident speaker;
            public float until, fade = 1;
            public bool foreground, stopping;
        }
        readonly NpcVoice[] npcVoices = new NpcVoice[NpcVoiceCapacity];
        static readonly string[] talkFamilies = { "npc-talk-warm", "npc-talk-soft" };
        static readonly string[] laughFamilies = { "npc-laugh-warm", "npc-laugh-bright" };
        static readonly string[] gaspFamilies = { "npc-gasp-warm", "npc-gasp-bright" };
        static readonly string[] sighFamilies = { "npc-sigh-warm", "npc-sigh-bright" };
        static readonly string[] cheerFamilies = { "npc-cheer-warm", "npc-cheer-bright" };
        public int NpcVoiceSlots => npcVoices.Length;
        public int ActiveNpcVoices
        {
            get { int count = 0; foreach (var slot in npcVoices) if (slot != null && slot.speaker != null) count++; return count; }
        }
        public AudioSource NpcSource(CityResidents.Resident person)
        { if (person == null) return null; foreach (var slot in npcVoices) if (slot != null && slot.speaker == person) return slot.voice.source; return null; }
        void InitializeNpcVoices()
        {
            for (int i = 0; i < npcVoices.Length; i++)
            {
                var voice = Create("Voz de morador " + (i + 1), false, 20);
                voice.category = "effects"; voice.source.dopplerLevel = 0;
                voice.source.minDistance = 1.8f; voice.source.spread = 12;
                npcVoices[i] = new NpcVoice { voice = voice };
            }
        }
        static Vector3 Mouth(CityResidents.Resident person)
        { return person.Rig != null && person.Rig.Head ? person.Rig.Head.position : person.Root.position + Vector3.up * (1.65f * person.Root.lossyScale.y); }
        static void ClearNpcVoice(NpcVoice slot)
        {
            slot.voice.source.Stop(); slot.voice.source.clip = null; slot.voice.source.volume = 0;
            slot.voice.filter.cutoffFrequency = 18000; slot.voice.room.enabled = false;
            slot.speaker = null; slot.stopping = false; slot.fade = 1; slot.until = 0;
        }
        public void StopNpcVoice(CityResidents.Resident person)
        { foreach (var slot in npcVoices) if (slot != null && slot.speaker == person) slot.stopping = true; }
        public bool NpcVocal(CityResidents.Resident person, string reaction, float strength = 1, bool foreground = true)
        {
            if (!ctx || !ctx.Ready || ctx.Paused || person == null || !person.Root || !person.Root.gameObject.activeInHierarchy ||
                person.Down || person.Controlled || person.Bus != null) return false;
            int tone = person.Data.id & 1;
            string family = reaction == "talk" ? talkFamilies[tone] : reaction == "laugh" ? laughFamilies[tone] :
                reaction == "gasp" ? gaspFamilies[tone] : reaction == "sigh" ? sighFamilies[tone] : reaction == "cheer" ? cheerFamilies[tone] :
                reaction == "ack" ? "npc-ack" : null;
            if (family == null || !variants.TryGetValue(family, out int count)) return false;
            Vector3 at = Mouth(person); float range = foreground ? 20 : 12;
            if ((at - ctx.Camera.transform.position).sqrMagnitude > range * range || BusGain("effects") <= .0001f) return false;
            NpcVoice selected = null, distantBackground = null; float farthest = -1;
            foreach (var slot in npcVoices)
            {
                if (slot == null) continue;
                if (slot.speaker == person) { if (!slot.stopping) return false; selected = slot; break; }
                if (slot.speaker == null || !slot.voice.source.isPlaying) selected = slot;
                else if (!slot.foreground)
                {
                    float distance = (slot.voice.source.transform.position - ctx.Camera.transform.position).sqrMagnitude;
                    if (distance > farthest) { farthest = distance; distantBackground = slot; }
                }
            }
            // Saturation drops background chatter; an addressed speaker may replace background only.
            if (selected == null) selected = foreground ? distantBackground : null;
            if (selected == null) return false;
            if (person.NextVocal > ctx.Clock && (selected.speaker != person || !selected.stopping || selected.voice.kind == family)) return false;
            int serial = serials.TryGetValue(family, out int n) ? n : 0;
            string key = family + "-" + serial % count;
            if (!clips.TryGetValue(key, out var clip)) return false;
            ClearNpcVoice(selected); var voice = selected.voice;
            selected.speaker = person; selected.foreground = foreground;
            voice.kind = family; voice.gain = Mathf.Clamp01(strength) * (foreground ? .62f : .36f);
            voice.source.transform.position = at; voice.source.clip = clip; voice.source.maxDistance = range;
            voice.source.priority = foreground ? 55 : 115;
            // A stable, small pitch offset belongs to the resident, not to each utterance.
            voice.source.pitch = .96f + Mathf.Abs(person.Data.id % 5) * .018f;
            voice.obstruction = Obstruction(at); voice.nextRay = ctx.Clock + .15f;
            voice.filter.cutoffFrequency = voice.obstruction > 0 ? 1400 : 18000;
            voice.room.enabled = room != null && room.Contains(at);
            if (voice.room.enabled) { voice.room.reverbPreset = AudioReverbPreset.Room; voice.room.reverbLevel = -2100; voice.room.reflectionsLevel = -2300; voice.room.decayTime = .4f; }
            voice.source.volume = voice.gain * BusGain("effects") * Mathf.Lerp(1, .28f, voice.obstruction);
            selected.until = ctx.Clock + clip.length / voice.source.pitch;
            person.NextVocal = ctx.Clock + Mathf.Max(.8f, clip.length / voice.source.pitch + .18f);
            voice.source.Play(); serials[family] = serial + 1; eventCounts[family] = EventCount(family) + 1;
            return true;
        }
        void UpdateNpcVoices(float dt)
        {
            foreach (var slot in npcVoices)
            {
                if (slot == null || slot.speaker == null) continue;
                var person = slot.speaker; var voice = slot.voice;
                if (!person.Root || !person.Root.gameObject.activeInHierarchy || person.Down || person.Controlled || person.Bus != null ||
                    ctx.Clock >= slot.until || !voice.source.isPlaying) { ClearNpcVoice(slot); continue; }
                voice.source.transform.position = Mouth(person);
                if ((voice.source.transform.position - ctx.Camera.transform.position).sqrMagnitude > voice.source.maxDistance * voice.source.maxDistance) slot.stopping = true;
                if (slot.stopping) slot.fade = Mathf.MoveTowards(slot.fade, 0, dt * 18);
                if (slot.fade <= 0) { ClearNpcVoice(slot); continue; }
                if (ctx.Clock >= voice.nextRay) { voice.obstruction = Obstruction(voice.source.transform.position); voice.nextRay = ctx.Clock + .15f; }
                voice.source.volume = voice.gain * slot.fade * BusGain("effects") * Mathf.Lerp(1, .28f, voice.obstruction);
                voice.filter.cutoffFrequency = Mathf.Lerp(voice.filter.cutoffFrequency, voice.obstruction > 0 ? 1400 : 18000, 1 - Mathf.Exp(-dt * 12));
            }
        }
        void DisposeNpcVoices()
        { foreach (var slot in npcVoices) if (slot != null && slot.voice.source) Destroy(slot.voice.source.gameObject); }
    }
}
