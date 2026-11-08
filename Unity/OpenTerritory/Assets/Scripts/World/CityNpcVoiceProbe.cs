using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OpenTerritory
{
    // Native DSP integration, installed only by the explicit QA argument.
    public sealed class CityNpcVoiceProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; public float value; }
        [Serializable] public class Report { public bool passed = true; public int sampleRate, frames; public float peak; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
        readonly Report report = new Report();
        readonly List<CityResidents.Resident> people = new List<CityResidents.Resident>();
        readonly float[] output = new float[2048];
        CityContext ctx; CityAudio audio; CityAudioCapture capture; string directory;
        float windowPeak; bool finished, socialWatch, firstTurn, secondTurn; int socialOverlap;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-npc-voice-qa") >= 0) new GameObject("Vozes NPC QA opt-in").AddComponent<CityNpcVoiceProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace); }
        void Test(string name, bool passed, float value = 0, string detail = "")
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, value = value, detail = detail }); Debug.Log("NPC_VOICE_QA " + name + " " + passed + " " + value + " " + detail); }
        void Update()
        {
            if (!ctx || finished) return;
            AudioListener.GetOutputData(output, 0);
            foreach (float value in output) windowPeak = Mathf.Max(windowPeak, Mathf.Abs(value));
            if (socialWatch)
            {
                ctx.Rain = 0;
                bool a = audio.NpcSource(people[0]), b = audio.NpcSource(people[1]);
                firstTurn |= a; secondTurn |= b; if (a && b) socialOverlap++;
            }
        }
        void Quiet()
        {
            foreach (var p in people)
            {
                ctx.Residents.Interrupt(p); p.Controlled = p.Down = p.Crossing = false;
                p.Reaction.Kind = CityResidents.ReactionKind.None; p.Reaction.MemoryUntil = 0;
                p.FearUntil = p.TalkUntil = p.DodgeUntil = p.NextVocal = 0;
                p.State = "activity"; p.Activity = "rest"; p.Age = 0; p.Duration = 600;
                p.NextSocial = p.NextPause = ctx.Clock + 600; p.PauseUntil = 0; p.Path.Clear();
            }
        }
        void Place(CityResidents.Resident p, Vector3 at)
        { p.Controller.enabled = false; p.Root.position = at; p.LastPosition = at; p.Controller.enabled = true; }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Application.temporaryCachePath;
            Directory.CreateDirectory(directory); float deadline = Time.realtimeSinceStartup + 120;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { Test("scene-ready", false); Finish(); yield break; }
            audio = ctx.Systems.Audio; report.sampleRate = AudioSettings.outputSampleRate;
            Test("all-18-human-vocal-assets-loaded-with-CC0-provenance", audio && audio.MissingClips == 0 && audio.Manifest.version == 5 &&
                audio.Manifest.clips.Count(e => e.id.StartsWith("npc-") && e.source.StartsWith("npc-") && e.license == "CC0-1.0") == 18);
            Test("only-opt-in-probe-installed", FindObjectsByType<CityNpcVoiceProbe>(FindObjectsSortMode.None).Length == 1 && !FindAnyObjectByType<CityAudioProbe>());
            ctx.UI.SetAudioMix(1, 1, 0, 0); ctx.Population.enabled = ctx.Follow.enabled = false;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.layer = CityContext.GroundLayer;
            floor.transform.position = new Vector3(0, 199, 0); floor.transform.localScale = new Vector3(60, 2, 60);
            ctx.Motor.Warp(new Vector3(0, 200.04f, 0)); ctx.Motor.SetInputLock(true);
            var eye = new Vector3(0, 201.65f, 0); ctx.Camera.transform.SetPositionAndRotation(eye, Quaternion.identity);
            var candidates = ctx.Data.residents.Where(d => !d.stationary && !d.sitting && !d.performance && !ctx.Residents.IsSpawned(d.id)).ToArray();
            var selected = candidates.GroupBy(d => d.id & 1).Select(g => g.First()).Concat(candidates).Distinct().Take(5).ToArray();
            foreach (var data in selected)
            {
                int i = people.Count; Vector3 point = new Vector3(i == 0 ? 0 : 8 + i * 3, 200.04f, 3);
                if (ctx.Residents.Spawn(data.id, point, Quaternion.Euler(0, 180, 0))) people.Add(ctx.Residents.Find(data.id));
            }
            Test("five-real-resident-rigs-and-both-stable-voice-tones", people.Count == 5 && people.Select(p => p.Data.id & 1).Distinct().Count() == 2);
            if (people.Count != 5) { Finish(); yield break; }
            Quiet(); yield return new WaitForSeconds(.3f);
            capture = ctx.Camera.gameObject.AddComponent<CityAudioCapture>(); capture.Begin(report.sampleRate, 60);
            int sourceCount = audio.GetComponentsInChildren<AudioSource>(true).Length;
            var person = people[0]; ctx.Residents.Talk(person); var source = audio.NpcSource(person);
            Test("talk-action-starts-recorded-conversation-with-English-text", source && source.clip.name.StartsWith("npc-talk-") && ctx.Speech.Contains(CityEnglish.Text(person.Data.name)) && person.TalkUntil > ctx.Clock);
            windowPeak = 0; yield return new WaitForSeconds(.3f);
            Test("conversation-reaches-real-listener-DSP", windowPeak > .0005f, windowPeak);
            float pitch = source ? source.pitch : 0;
            Place(person, person.Root.position + Vector3.right * .3f); yield return new WaitForSeconds(.12f);
            Test("voice-follows-moving-speaker-head", source && Vector3.Distance(source.transform.position, person.Rig.Head.position) < .12f);
            ctx.UI.SetSettings(true); int sample = source ? source.timeSamples : 0; float clock = ctx.Clock;
            yield return new WaitForSecondsRealtime(.4f);
            Test("Esc-freezes-voice-position-and-simulation-clock", ctx.Clock == clock && source && Mathf.Abs(source.timeSamples - sample) <= 2048);
            ctx.UI.SetSettings(false); yield return new WaitForSeconds(.12f);
            Test("closing-Esc-resumes-the-same-voice", source && source.isPlaying && source.timeSamples > sample && audio.NpcSource(person) == source);
            ctx.Residents.Control(person, true); yield return new WaitForSeconds(.12f);
            Test("controlling-NPC-interrupts-its-voice", !audio.NpcSource(person)); ctx.Residents.Control(person, false);
            Quiet(); yield return new WaitForSeconds(.12f);
            var clips = new HashSet<string>();
            foreach (var actor in people.Take(2))
                for (int i = 0; i < 4; i++)
                {
                    Place(actor, new Vector3(0, 200.04f, 3)); actor.NextVocal = 0;
                    audio.NpcVocal(actor, "talk"); var voice = audio.NpcSource(actor); if (voice) clips.Add(voice.clip.name);
                    if (actor == person) Test("resident-pitch-stays-stable-take-" + i, voice && Mathf.Abs(voice.pitch - pitch) < .0001f);
                    yield return new WaitForSeconds(.1f); ctx.Residents.Interrupt(actor); yield return new WaitForSeconds(.12f);
                }
            Test("eight-distinct-recorded-conversation-excerpts", clips.Count == 8, clips.Count);
            Place(people[1], new Vector3(12, 200.04f, 3));
            foreach (var offered in new[] { CityEmote.Laugh, CityEmote.Sad, CityEmote.Dance, CityEmote.Wave, CityEmote.Clap })
            {
                Quiet(); yield return new WaitForSeconds(.12f);
                int count = ctx.Residents.ReactToEmote(offered); source = audio.NpcSource(person);
                string family = offered == CityEmote.Laugh ? "npc-laugh-" : offered == CityEmote.Sad ? "npc-sigh-" : offered == CityEmote.Wave ? "npc-ack-" : "npc-cheer-";
                Test("actual-NPC-emote-plays-" + offered, count > 0 && source && source.clip.name.StartsWith(family), count, source ? source.clip.name : "silent");
                windowPeak = 0; yield return new WaitForSeconds(.33f);
                Test("real-DSP-response-" + offered, windowPeak > .0001f, windowPeak);
            }
            Quiet(); yield return new WaitForSeconds(.12f); ctx.Residents.Talk(person);
            ctx.Residents.Startle(person, person.Root.position + Vector3.forward);
            source = audio.NpcSource(person); string gasp = source && source.clip ? source.clip.name : "";
            ctx.Residents.Startle(person, person.Root.position + Vector3.forward);
            Test("surprise-replaces-dialogue-with-a-real-gasp-once", gasp.StartsWith("npc-gasp-") && source && source.clip.name == gasp && person.Reaction.Kind == CityResidents.ReactionKind.Flee);
            yield return new WaitForSeconds(.3f); Quiet(); yield return new WaitForSeconds(.12f);
            person.NextVocal = 0; audio.NpcVocal(person, "talk"); source = audio.NpcSource(person); yield return new WaitForSeconds(.1f);
            float openGain = source ? source.volume : 0;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.layer = CityContext.WorldLayer;
            wall.transform.position = new Vector3(0, 201.5f, 1.5f); wall.transform.localScale = new Vector3(4, 4, .3f);
            yield return new WaitForSeconds(.36f);
            Test("wall-attenuates-and-lowpasses-NPC-voice", source && source.volume < openGain * .4f && source.GetComponent<AudioLowPassFilter>().cutoffFrequency < 5000);
            Destroy(wall); yield return new WaitForSeconds(.32f);
            Test("clearing-wall-restores-voice", source && source.volume > openGain * .85f && source.GetComponent<AudioLowPassFilter>().cutoffFrequency > 14000);
            ctx.UI.SetAudioMix(1, 0, 0, 0); yield return new WaitForSeconds(.12f); windowPeak = 0; yield return new WaitForSeconds(.15f);
            Test("effects-slider-mutes-live-NPC-voice-DSP", source && source.volume == 0 && windowPeak < .0001f, windowPeak);
            ctx.UI.SetAudioMix(1, 1, 0, 0); Quiet(); yield return new WaitForSeconds(.12f);
            audio.NpcVocal(person, "talk"); source = audio.NpcSource(person);
            ctx.Camera.transform.position = eye + Vector3.back * 45; yield return new WaitForSeconds(.15f);
            Test("out-of-range-voice-returns-to-pool", !audio.NpcSource(person)); ctx.Camera.transform.position = eye;
            Quiet(); yield return new WaitForSeconds(.15f);
            Place(person, new Vector3(0, 200.04f, 3)); Place(people[1], new Vector3(1.7f, 200.04f, 3));
            person.NextSocial = ctx.Clock; people[1].NextSocial = ctx.Clock; ctx.Rain = 0; socialWatch = true;
            deadline = ctx.Clock + 13;
            while (person.Partner == null && ctx.Clock < deadline) yield return null;
            Test("routine-starts-a-real-social-conversation", person.Partner == people[1]);
            yield return new WaitForSeconds(5.5f); socialWatch = false;
            Test("both-NPCs-take-audible-turns-without-overlap", firstTurn && secondTurn && socialOverlap == 0, socialOverlap);
            Quiet(); yield return new WaitForSeconds(.15f);
            for (int i = 0; i < people.Count; i++) Place(people[i], new Vector3(i * .7f, 200.04f, 3));
            int accepted = 0; foreach (var actor in people) if (audio.NpcVocal(actor, "talk", 1, false)) accepted++;
            Test("bounded-pool-drops-fifth-background-speaker", audio.NpcVoiceSlots == 4 && accepted == 4 && audio.ActiveNpcVoices == 4, accepted);
            Test("addressed-speaker-replaces-only-background", audio.NpcVocal(people[4], "talk", 1, true) && audio.ActiveNpcVoices == 4 && audio.NpcSource(people[4]));
            audio.Play("reload-carbine", eye + Vector3.forward); var effect = audio.LastSource; var effectClip = effect ? effect.clip : null;
            Quiet(); yield return new WaitForSeconds(.15f);
            Test("interrupting-all-speakers-fully-returns-pool", audio.ActiveNpcVoices == 0);
            Test("NPC-interruption-preserves-unrelated-effects", effect && effect.isPlaying && effect.clip == effectClip);
            accepted = 0; foreach (var actor in people.Take(4)) if (audio.NpcVocal(actor, "talk")) accepted++;
            Test("all-four-voice-slots-reuse-cleanly-without-new-emitters", accepted == 4 && audio.GetComponentsInChildren<AudioSource>(true).Length == sourceCount, accepted);
            ctx.Residents.SetDown(person, true); yield return new WaitForSeconds(.12f);
            Test("downed-NPC-cannot-keep-speaking", !audio.NpcSource(person) && !audio.NpcVocal(person, "laugh"));
            capture.End(Path.Combine(directory, "conversas-e-reacoes-no-jogo.wav")); report.peak = capture.Peak; report.frames = capture.Frames;
            Test("native-DSP-capture-has-speech-with-headroom", report.frames > report.sampleRate * 5 && report.peak > .001f && report.peak < 1, report.peak);
            Test("no-runtime-errors", report.errors.Count == 0, report.errors.Count);
            Finish();
        }
        void Finish()
        {
            finished = true; if (ctx) ctx.UI.SetSettings(false);
            File.WriteAllText(Path.Combine(directory, "npc-voice-qa.json"), JsonUtility.ToJson(report, true)); Application.Quit(report.passed ? 0 : 2);
        }
    }
}
