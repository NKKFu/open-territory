using UnityEditor;
using UnityEngine;

namespace OpenTerritory.Editor
{
    // Preserve recorded transients and loop joins. Clips are shared; spawning
    // an emitter never decodes or regenerates a clip.
    public sealed class CityAudioImporter : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            bool music = assetPath.StartsWith("Assets/Resources/Music/");
            if (!music && !assetPath.StartsWith("Assets/Resources/Audio/")) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = false; importer.loadInBackground = music;
            var settings = importer.defaultSampleSettings;
            settings.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            settings.quality = 1;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = !music;
            importer.defaultSampleSettings = settings;
        }
    }
}
