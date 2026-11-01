#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VRArmory.Editor
{
    // Builds the standalone Quest APK to Builds/Android/GunAssemblyVR_v1_1.apk (the Builds folder is git-ignored).
    //   Tools > VR Armory > VR > 3. Build Quest APK
    // Command line: Unity.exe -batchmode -buildTarget Android -projectPath <project> -executeMethod VRArmory.Editor.VRBuild.BuildQuestApk -quit
    public static class VRBuild
    {
        public const string ApkPath = "Builds/Android/GunAssemblyVR_v1_1.apk";

        [MenuItem("Tools/VR Armory/VR/3. Build Quest APK", priority = 3)]
        public static void BuildQuestApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            EditorUserBuildSettings.buildAppBundle = false; // an .apk for sideloading, not a store .aab
            var options = new BuildPlayerOptions
            {
                scenes = new[] { VRProjectSetup.ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            Debug.Log($"[VR Armory] Build {s.result}: {Path.GetFullPath(ApkPath)}  {s.totalSize / (1024f * 1024f):0.0} MB, "
                      + $"{s.totalErrors} error(s), {s.totalWarnings} warning(s), {s.totalTime.TotalMinutes:0.0} min");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
#endif
