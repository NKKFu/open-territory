using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    // English presentation and interaction acceptance. Never installed in normal play.
    public sealed class CityEnglishProbe : MonoBehaviour
    {
        [Serializable] public class Check { public string name, detail; public bool passed; public int count; }
        [Serializable] public class Report { public bool passed = true; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); public List<string> captions = new List<string>(); }
        readonly Report report = new Report();
        readonly Regex portuguese = new Regex(@"\b(você|vocês|não|uma|seu|sua|seus|suas|aqui|nosso|nossa|das|dos|com|para|está|estão|escolha|solte|clique|arraste|segurar|mova|ligado|ligada|desligado|desligada|carregando|configurações|imagem|controles|música|rádio|munição|pulsor|armaria|bairro|praça|orla|farol|oficina|colinas|peixaria|bombeiros|delegacia|livraria|padaria|cemitério|fechar|voltar|paradas|acertos|disparos|aplaudir|conversar|pular|correr|acenar|triste|cargas|cartuchos|pesos|caixote)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        CityContext ctx; string directory; bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "--city-english-qa") >= 0) new GameObject("English acceptance opt-in").AddComponent<CityEnglishProbe>(); }
        void Awake() { Application.logMessageReceived += Log; }
        void OnDestroy() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) report.errors.Add(message + "\n" + trace); }
        void Test(string name, bool passed, string detail = "", int count = 0)
        { report.passed &= passed; report.checks.Add(new Check { name = name, passed = passed, detail = detail, count = count }); Debug.Log("ENGLISH_QA " + name + " " + passed + " " + detail); }
        static bool SettingsChild(Transform item)
        { for (var parent = item; parent; parent = parent.parent) if (parent.name == "Painel de configurações") return true; return false; }
        Text Caption(string name) => FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == name && SettingsChild(t.transform));
        Button Button(string name) => FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == name && SettingsChild(t.transform));
        IEnumerator Frame() { yield return null; yield return new WaitForEndOfFrame(); }
        void Audit(string stage)
        {
            var labels = FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bad = labels.Where(t => !string.IsNullOrWhiteSpace(t.text) && portuguese.IsMatch(t.text)).Select(t => t.name + ": " + t.text).ToArray();
            foreach (var t in labels) if (!string.IsNullOrWhiteSpace(t.text) && !report.captions.Contains(t.text)) report.captions.Add(t.text);
            Test(stage + "-has-no-Portuguese-captions", bad.Length == 0, string.Join(" | ", bad), labels.Length);
        }
        void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var overlays = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var cameras = overlays.Select(c => c.worldCamera).ToArray(); var planes = overlays.Select(c => c.planeDistance).ToArray();
            var oldTarget = ctx.Camera.targetTexture; var oldActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24); Texture2D picture = null;
            try
            {
                foreach (var c in overlays) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = ctx.Camera; c.planeDistance = 1; }
                ctx.Camera.targetTexture = target; Canvas.ForceUpdateCanvases(); ctx.Camera.Render(); RenderTexture.active = target;
                picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); picture.Apply(); File.WriteAllBytes(Path.Combine(directory, name + ".png"), picture.EncodeToPNG());
            }
            finally
            {
                ctx.Camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                for (int i = 0; i < overlays.Length; i++) { overlays[i].renderMode = RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera = cameras[i]; overlays[i].planeDistance = planes[i]; }
                Canvas.ForceUpdateCanvases(); RenderTexture.ReleaseTemporary(target); if (picture) Destroy(picture);
            }
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "--city-report-dir");
            directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Application.temporaryCachePath; Directory.CreateDirectory(directory);
            float end = Time.realtimeSinceStartup + 120;
            while ((!CityContext.Instance || !CityContext.Instance.Ready) && Time.realtimeSinceStartup < end) yield return null;
            ctx = CityContext.Instance;
            if (!ctx || !ctx.Ready) { Test("scene-ready", false); Finish(); yield break; }
            Test("English-catalog-is-included", CityEnglish.Entries.Count >= 500, "entries=" + CityEnglish.Entries.Count);
            Test("native-world-is-not-paused-at-entry", !ctx.Paused && !ctx.UI.MapOpen);
            Test("dialogue-preserves-natural-English", CityEnglish.Text("Oi de novo! Lia · Hoje a baía está bonita.") == "Hi again! Lia · The bay looks beautiful today.");
            var lines = ctx.Data.residents.SelectMany(p => new[] { p.line }.Concat(p.lines ?? Array.Empty<string>())).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();
            var badLines = lines.Where(s => portuguese.IsMatch(CityEnglish.Text(s))).ToArray();
            Test("all-authored-resident-dialogues-are-English", badLines.Length == 0, string.Join(" | ", badLines), lines.Length);
            var badActions = ctx.Actions.Where(a => portuguese.IsMatch(a.Text)).Select(a => a.id + ": " + a.Text).ToArray();
            Test("all-registered-action-captions-are-English", badActions.Length == 0, string.Join(" | ", badActions), ctx.Actions.Count);
            Test("English-decimal-format-keeps-economic-values", CityCommerce.Weight(3) == "1.5 kg" && CityCommerce.Cash(180) == "R$ 180");
            Test("dynamic-commerce-and-weather-are-English", Regex.IsMatch(CityEnglish.Text(ctx.Commerce.Quote("buy", "sidearm")), "^(Buy|Restock|Check) ") && !portuguese.IsMatch(CityEnglish.Text(ctx.Systems.Weather.Forecast())));
            ctx.UI.SetSettings(true); yield return Frame();
            Test("settings-headings-and-image-options-are-English", Caption("Título").text == "SUN BAY" && Caption("Subtítulo").text == "Settings" && Button("Imagem").GetComponentInChildren<Text>().text == "Graphics" && Caption("Campo de visão fixo").text.Contains("73.2°"));
            Audit("graphics-settings"); Capture("01-graphics-settings");
            foreach (string tab in new[] { "Som", "Controles" })
            {
                Button(tab).onClick.Invoke(); yield return Frame(); Audit(tab == "Som" ? "audio-settings" : "controls-settings");
                Capture(tab == "Som" ? "02-audio-settings" : "03-controls-settings");
            }
            Test("audio-settings-have-English-buses", Caption("Efeitos · valor").text.StartsWith("Effects") && Caption("Ambiente · valor").text.StartsWith("Ambience") && Caption("Música e rádio · valor").text.StartsWith("Music and radio"));
            ctx.UI.SetSettings(false); yield return Frame(); ctx.UI.Map.SetOpen(true); yield return Frame();
            Audit("city-map");
            Test("map-guide-has-English-landmarks", FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(t => t.text == "Tide Market") && FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(t => t.text.Contains("Gun shop · weapons and ammo")));
            Test("map-keeps-city-running", ctx.UI.MapOpen && !ctx.Paused);
            Capture("04-city-map"); ctx.UI.Map.SetOpen(false); yield return Frame();
            // A real resident and the actual Q interaction, in an opt-in isolated test area.
            ctx.Population.enabled = ctx.Follow.enabled = false;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.layer = CityContext.GroundLayer;
            floor.transform.position = new Vector3(0, 199, 0); floor.transform.localScale = new Vector3(20, 2, 20);
            ctx.Motor.Warp(new Vector3(0, 200.04f, 0));
            var data = ctx.Data.residents.First(d => !d.stationary && !d.sitting && !d.performance && !ctx.Residents.IsSpawned(d.id));
            ctx.Residents.Spawn(data.id, new Vector3(0, 200.04f, 2.5f), Quaternion.Euler(0, 180, 0)); var person = ctx.Residents.Find(data.id);
            person.State = "activity"; person.Activity = "rest"; person.Duration = 600; person.NextSocial = person.NextPause = ctx.Clock + 600;
            ctx.Camera.transform.SetPositionAndRotation(new Vector3(0, 202.1f, -3), Quaternion.Euler(2, 0, 0));
            yield return new WaitForSeconds(.3f); ctx.RefreshAction();
            Test("Q-selects-a-real-English-talk-action", ctx.CurrentAction != null && ctx.CurrentAction.conversation && ctx.CurrentAction.Text == "Talk");
            Test("Q-action-still-executes", ctx.UseCurrentAction(true)); yield return new WaitForSeconds(.15f);
            Test("real-NPC-dialogue-is-English-and-audible", !portuguese.IsMatch(ctx.Speech) && person.TalkUntil > ctx.Clock && ctx.Systems.Audio.NpcSource(person), ctx.Speech);
            Audit("conversation"); Capture("05-English-conversation");
            ctx.Residents.Interrupt(person); person.NextVocal = 0;
            ctx.Residents.ReactToEmote(CityEmote.Laugh); yield return new WaitForSeconds(.1f);
            Test("laugh-response-uses-English-text-and-human-audio", ctx.Speech.Contains("That was a good one!") && ctx.Systems.Audio.NpcSource(person), ctx.Speech);
            Audit("NPC-reaction");
            var options = new List<CityHackOption>(); int deviceTypes = 0; var checkedTypes = new HashSet<string>();
            foreach (var device in ctx.Systems.HackDevices)
            {
                if (!ctx.Systems.HackDevicePresent(device) || !checkedTypes.Add(device.type)) continue;
                options.Clear(); ctx.Systems.GetHackOptions(device, options);
                bool valid = !portuguese.IsMatch(CityEnglish.Text(ctx.Systems.HackDeviceName(device))) && options.All(o => !portuguese.IsMatch(CityEnglish.Text(o.Label)) && !portuguese.IsMatch(CityEnglish.Text(o.Effect)));
                Test("hacking-options-English-" + device.type, valid, string.Join(" | ", options.Select(o => CityEnglish.Text(o.Label)))); deviceTypes++;
            }
            Test("multiple-live-hacking-categories-covered", deviceTypes >= 5, "types=" + deviceTypes);
            Test("runtime-label-factories-use-English-text", FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t.GetComponentInParent<CityCanvasUI>() || t.GetComponentInParent<CityMapUI>()).All(t => t is CityEnglishText));
            Test("native-run-has-no-errors", report.errors.Count == 0, string.Join(" | ", report.errors));
            Finish();
        }
        void Finish()
        {
            finished = true; if (ctx) { ctx.UI.SetSettings(false); ctx.UI.Map.SetOpen(false); }
            File.WriteAllText(Path.Combine(directory, "english-qa.json"), JsonUtility.ToJson(report, true)); Application.Quit(report.passed ? 0 : 2);
        }
    }
}
