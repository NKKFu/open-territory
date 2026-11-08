using UnityEngine;

namespace OpenTerritory
{
    // The settings theme has its own 2D voice so the city's audio stays paused.
    public sealed class CityMenuMusic : MonoBehaviour
    {
        public const string Title = "Bossa Antigua";
        public const string Artist = "Kevin MacLeod";
        public const string SourceUrl = "https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1700069";
        public const string LicenseUrl = "https://creativecommons.org/licenses/by/4.0/";
        public AudioSource Source { get; private set; }

        CityUI owner;
        bool open, started;

        public void Initialize(CityUI menu)
        {
            owner = menu;
            var voice = new GameObject("Música do menu · " + Title);
            voice.transform.SetParent(transform, false);
            Source = voice.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            Source.loop = true;
            Source.spatialBlend = 0;
            Source.dopplerLevel = 0;
            Source.ignoreListenerPause = true;
            Source.priority = 48;
            Source.volume = 0;
            Source.clip = Resources.Load<AudioClip>("Music/bossa-antigua");
            if (!Source.clip) Debug.LogError("Menu music missing: Music/bossa-antigua");
        }

        public void SetOpen(bool value)
        {
            if (open == value || !Source || !Source.clip) return;
            open = value;
            Source.volume = 0;
            if (!open) { Source.Pause(); return; }
            if (started) Source.UnPause();
            else
            {
                Source.clip.LoadAudioData();
                Source.Play();
                started = true;
            }
        }

        void Update()
        {
            if (!owner || !Source) return;
            SetOpen(owner.Open);
            if (!open) return;
            float target = owner.MusicVolume * .55f;
            Source.volume = target == 0 ? 0 : Mathf.MoveTowards(Source.volume, target, Time.unscaledDeltaTime * 1.5f);
        }

        void OnDisable()
        {
            if (Source) { Source.Stop(); Source.volume = 0; }
            open = started = false;
        }
    }
}
