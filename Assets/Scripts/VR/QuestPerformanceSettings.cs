using UnityEngine;

namespace VRArmory
{
    // Runs only in VR (it sits under VR_Content). The armory has 15 realtime lights and 6 of them cast soft shadows;
    // two are point lights, and each point-light shadow re-renders the scene six times. On Quest we keep the shadows
    // that matter at the bench and switch the rest off. The scene file is not changed, so desktop keeps every shadow.
    public class QuestPerformanceSettings : MonoBehaviour
    {
        [Tooltip("Lights that keep their shadows in VR (the bench lamp makes the parts sit on the mat).")]
        public Light[] keepShadows;

        void Awake()
        {
            int turnedOff = 0;
            foreach (Light l in FindObjectsOfType<Light>())
            {
                if (l.shadows == LightShadows.None || System.Array.IndexOf(keepShadows, l) >= 0) continue;
                l.shadows = LightShadows.None;
                turnedOff++;
            }
            Debug.Log($"[VR Armory] Quest performance: shadows off on {turnedOff} light(s), kept on {keepShadows.Length}.");
        }
    }
}
