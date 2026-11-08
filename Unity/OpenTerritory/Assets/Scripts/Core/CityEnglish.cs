using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTerritory
{
    // English is the game's presentation language. Imported names/IDs remain valid
    // as lookup keys; translation is applied only to player-facing text.
    public static class CityEnglish
    {
        [Serializable] public sealed class Entry { public string source, target; }
        [Serializable] public sealed class Catalog { public Entry[] entries; }
        static readonly Dictionary<string, string> exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.Ordinal);
        static Regex phrases;
        static bool ready;
        public static IReadOnlyDictionary<string, string> Entries { get { Initialize(); return exact; } }
        static void Initialize()
        {
            if (ready) return;
            ready = true;
            var asset = Resources.Load<TextAsset>("English");
            if (!asset) { Debug.LogError("English text catalog is missing."); return; }
            var catalog = JsonUtility.FromJson<Catalog>(asset.text);
            var keys = new List<string>();
            foreach (var entry in catalog.entries)
            {
                if (string.IsNullOrEmpty(entry.source) || string.IsNullOrEmpty(entry.target)) continue;
                if (!exact.ContainsKey(entry.source)) keys.Add(entry.source);
                exact[entry.source] = entry.target;
            }
            keys.Sort((a, b) => b.Length.CompareTo(a.Length));
            var patterns = new List<string>();
            foreach (string key in keys)
                patterns.Add((char.IsLetterOrDigit(key[0]) ? @"(?<![\p{L}\p{N}_])" : "") + Regex.Escape(key) +
                    (char.IsLetterOrDigit(key[key.Length - 1]) ? @"(?![\p{L}\p{N}_])" : ""));
            phrases = new Regex(string.Join("|", patterns), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        static string Case(string source, string target)
        {
            int letters = 0;
            foreach (char c in source) if (char.IsLetter(c)) { letters++; if (char.IsLower(c)) return target; }
            return letters > 1 ? target.ToUpperInvariant() : target;
        }
        static string Replace(Match match) => Case(match.Value, exact[match.Value]);
        public static string Text(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            Initialize();
            if (exact.TryGetValue(value, out string translation)) return Case(value, translation);
            if (cache.TryGetValue(value, out translation)) return translation;
            translation = phrases != null ? phrases.Replace(value, Replace) : value;
            if (cache.Count >= 2048) cache.Clear();
            cache[value] = translation; return translation;
        }
        public static void TranslateSceneLabels()
        {
            foreach (var label in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                label.text = Text(label.text);
        }
    }
}
