using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Management;

namespace VRArmory
{
    // Decides at startup which player the scene uses.
    //   Headset running (OpenXR started)  -> VR rig on, desktop player off.
    //   No headset (Editor, Windows build) -> desktop player exactly as before, all VR objects stay off.
    // Everything VR lives under 'vrContent', which is saved inactive, so desktop mode never touches it.
    [DefaultExecutionOrder(-500)]
    public class VRPlatformSwitch : MonoBehaviour
    {
        public GameObject desktopPlayer;
        public GameObject vrContent;
        public XRInteractionManager interactionManager;
        [Tooltip("Grab components on the gun parts. Saved disabled so desktop mode never creates an XR Interaction Manager.")]
        public VRAssemblyGrab[] partGrabs;

        public static bool IsVR { get; private set; }

        // Editor only: the automated self-test sets this to use the VR rig without a headset.
        // A session flag (not a scene field) so it survives the scene reload of the Restart button and is never saved.
        public const string ForceVRSessionKey = "VRArmory.ForceVR";

        void Awake()
        {
            IsVR = IsXRRunning() || ForcedByEditorTest();

            if (!IsVR)
            {
                if (vrContent != null) vrContent.SetActive(false);
                return;
            }

            if (desktopPlayer != null) desktopPlayer.SetActive(false);
            if (vrContent != null) vrContent.SetActive(true);
            foreach (VRAssemblyGrab grab in partGrabs)
            {
                if (grab == null) continue;
                grab.interactionManager = interactionManager;
                grab.enabled = true;
            }
        }

        static bool ForcedByEditorTest()
        {
#if UNITY_EDITOR
            return UnityEditor.SessionState.GetBool(ForceVRSessionKey, false);
#else
            return false;
#endif
        }

        static bool IsXRRunning()
        {
            XRGeneralSettings settings = XRGeneralSettings.Instance;
            return settings != null && settings.Manager != null && settings.Manager.activeLoader != null;
        }
    }
}
