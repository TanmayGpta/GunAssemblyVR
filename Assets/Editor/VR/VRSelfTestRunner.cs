#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using VRArmory.Tests;

namespace VRArmory.Editor
{
    // Starts the automated Play Mode self-test (see Assets/Scripts/Tests/VRArmorySelfTest.cs).
    // Results are printed to the Console as "SELFTEST|mode|test|PASS/FAIL|detail" lines; nothing is saved.
    // Command line (Unity closed):
    //   Unity.exe -batchmode -projectPath <project> -executeMethod VRArmory.Editor.VRSelfTestRunner.RunVR -logFile vr_test.log
    //   (exit code 0 = all passed). Use RunDesktop for the desktop-mode test.
    public static class VRSelfTestRunner
    {
        [MenuItem("Tools/VR Armory/VR/Run Self-Test (Desktop Mode)", priority = 20)]
        public static void RunDesktop() => Run("desktop", false);

        [MenuItem("Tools/VR Armory/VR/Run Self-Test (VR Rig, Simulated Controllers)", priority = 21)]
        public static void RunVR() => Run("vr", true);

        static void Run(string mode, bool forceVR)
        {
            if (EditorApplication.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SessionState.SetString(VRArmorySelfTest.ModeKey, mode);
            SessionState.SetBool(VRPlatformSwitch.ForceVRSessionKey, forceVR);
            EditorSceneManager.OpenScene(VRProjectSetup.ScenePath);
            EditorApplication.EnterPlaymode();
        }
    }
}
#endif
