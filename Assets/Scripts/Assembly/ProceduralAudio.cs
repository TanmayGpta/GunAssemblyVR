using UnityEngine;

namespace VRArmory
{
    // Small synthesized sounds, so the project needs no audio files.
    public static class ProceduralAudio
    {
        // Short decaying metallic click (moved here from FunctionCheck so assembly feedback can share it).
        public static AudioClip MakeClick(string name, float length, float freq, float gain)
        {
            const int rate = 44100;
            int n = Mathf.CeilToInt(length * rate);
            float[] data = new float[n];
            System.Random rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Exp(-t / (length * 0.25f));
                float noise = (float)rng.NextDouble() * 2f - 1f;
                data[i] = gain * env * (0.6f * noise + 0.4f * Mathf.Sin(2f * Mathf.PI * freq * t));
            }
            AudioClip clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Low two-tone buzz for a rejected action.
        public static AudioClip MakeBuzz(string name, float length, float freq, float gain)
        {
            const int rate = 44100;
            int n = Mathf.CeilToInt(length * rate);
            float[] data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((length - t) / 0.04f);
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t));
                data[i] = gain * env * (0.5f * square + 0.5f * Mathf.Sin(2f * Mathf.PI * freq * 1.5f * t));
            }
            AudioClip clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
