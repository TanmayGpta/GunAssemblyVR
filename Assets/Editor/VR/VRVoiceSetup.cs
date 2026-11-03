#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VRArmory.Editor
{
    // Voice clips for VRVoiceGuide.
    //  - Every audio file put in Assets/Audio/Voice/ gets the same Quest-friendly import settings (mono, Vorbis,
    //    compressed in memory), so a replaced or new recording needs no manual setup.
    //  - LoadClips() finds the clip of every VoiceLine by its file name, VO_<line>.wav (used by Setup VR Scene).
    //  - "Export Voice Script" writes every line and its words to Tools/VoiceGeneration/voice_script.tsv, the list a
    //    voice actor records from (or that generate_voice.py reads), so the recordings always match VRTrainingText.
    public class VRVoiceSetup : AssetPostprocessor
    {
        public const string VoiceFolder = "Assets/Audio/Voice";
        public const string ScriptPath = "Tools/VoiceGeneration/voice_script.tsv";

        public static string ClipPath(VoiceLine line) => $"{VoiceFolder}/VO_{line}.wav";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(VoiceFolder + "/")) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true;
            importer.loadInBackground = false;
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            s.preloadAudioData = true;                         // ready the moment it is needed: no delay on the first word
            s.loadType = AudioClipLoadType.CompressedInMemory; // a few hundred KB in memory instead of several MB
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;                                  // clear speech, small files
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = s;
        }

        // One entry per VoiceLine; the clip is null (and reported) where no recording exists yet.
        public static VoiceClip[] LoadClips(out List<VoiceLine> missing)
        {
            var clips = new List<VoiceClip>();
            missing = new List<VoiceLine>();
            foreach (VoiceLine line in Enum.GetValues(typeof(VoiceLine)))
            {
                if (line == VoiceLine.None) continue;
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath(line));
                if (clip == null) missing.Add(line);
                clips.Add(new VoiceClip { line = line, clip = clip });
            }
            return clips.ToArray();
        }

        [MenuItem("Tools/VR Armory/VR/Export Voice Script", priority = 30)]
        public static void ExportScript()
        {
            var sb = new StringBuilder("file\tline\ttext\n");
            foreach (VoiceLine line in Enum.GetValues(typeof(VoiceLine)))
            {
                if (line == VoiceLine.None) continue;
                sb.Append($"VO_{line}.wav\t{line}\t{VRTrainingText.Spoken(line)}\n");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath));
            File.WriteAllText(ScriptPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[VR Armory] Voice script written to {Path.GetFullPath(ScriptPath)}");
        }
    }
}
#endif
