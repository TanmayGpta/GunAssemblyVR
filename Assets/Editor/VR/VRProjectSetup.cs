#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace VRArmory.Editor
{
    // One-click project configuration for the Meta Quest 3S build (Version 1). Safe to run again: every setting is
    // simply set to the value below. Desktop (Standalone) keeps no XR loader, so the desktop prototype is unchanged.
    //   Tools > VR Armory > VR > 1. Configure Project for Quest 3S
    public static class VRProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/SampleScene.unity";
        public const string IntroScenePath = "Assets/Scenes/Intro.unity";

        // Build order: the Intro (soldier briefing) first, when it has been created, then the training scene.
        public static string[] BuildScenes => System.IO.File.Exists(IntroScenePath) ? new[] { IntroScenePath, ScenePath } : new[] { ScenePath };
        public const string AndroidPackageId = "com.vrarmory.gunassemblyvr";
        const string XRSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        const string LayerSettingsPath = "Assets/XRI/Settings/Resources/InteractionLayerSettings.asset";
        const string LayerSettingsScriptGuid = "191492db6e452eb468b95433ec162164"; // XRI's InteractionLayerSettings script

        [MenuItem("Tools/VR Armory/VR/1. Configure Project for Quest 3S", priority = 1)]
        public static void Configure()
        {
            ConfigureBuildSettings();
            ConfigureAndroidPlayer();
            ConfigureXRManagement();
            ConfigureOpenXR();
            ConfigureQuestQuality();
            ConfigureInteractionLayers();
            ImportHandSamples();
            AssetDatabase.SaveAssets();
            Debug.Log("[VR Armory] Project configured for Meta Quest 3S (OpenXR, Android ARM64, IL2CPP, GLES3, Single Pass Instanced).");
            LogOpenXRValidation();
        }

        // Audit G-09: Build Settings had no scenes, so builds needed the scene passed by hand.
        static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = System.Array.ConvertAll(BuildScenes, path => new EditorBuildSettingsScene(path, true));
        }

        static void ConfigureAndroidPlayer()
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, AndroidPackageId);
            PlayerSettings.gpuSkinning = false; // see VRBuild: GPU skinning on GLES crashed after the scene switch
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29; // Quest OS minimum
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64; // Quest is 64-bit only
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft; // the only orientation Quest supports
            PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, true);
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        }

        static void ConfigureXRManagement()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
            {
                perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XRSettingsPath);
                if (perTarget == null)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                    perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(perTarget, XRSettingsPath);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }

            // Standalone (Windows PC / Meta Horizon Link)
            if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Standalone)) perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone)) perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            XRGeneralSettings standalone = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            standalone.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(standalone.AssignedSettings, typeof(OpenXRLoader).FullName, BuildTargetGroup.Standalone);
            EditorUtility.SetDirty(standalone);

            // Android (Meta Quest 3S Standalone APK)
            if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android)) perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            XRGeneralSettings android = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            android.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(android.AssignedSettings, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android);
            EditorUtility.SetDirty(android);

            EditorUtility.SetDirty(perTarget);
        }

        static void ConfigureOpenXR()
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
            {
                Debug.LogError("[VR Armory] OpenXR settings for Android were not created.");
                return;
            }

            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced; // multiview: both eyes in one pass
            settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;
            EnableFeature<MetaQuestFeature>(settings);
            // "Optimize Buffer Discards" is a Vulkan-only option of the Meta Quest feature; this project renders with GLES3.
            var questFeature = new SerializedObject(settings.GetFeature<MetaQuestFeature>());
            questFeature.FindProperty("optimizeBufferDiscards").boolValue = false;
            // Targeting Quest Pro ("cambria") makes OpenXR add a *required* eye-tracking feature to the manifest,
            // which a Quest 3S (no eye tracking) would refuse. Target Quest 2 / 3 / 3S only.
            SerializedProperty devices = questFeature.FindProperty("targetDevices");
            for (int i = 0; i < devices.arraySize; i++)
            {
                SerializedProperty d = devices.GetArrayElementAtIndex(i);
                string device = d.FindPropertyRelative("manifestName").stringValue;
                d.FindPropertyRelative("enabled").boolValue = device == "quest2" || device == "eureka" || device == "quest3s";
            }
            questFeature.ApplyModifiedProperties();
            EnableFeature<OculusTouchControllerProfile>(settings);          // Quest 2 / Pro style mapping
            EnableFeature<MetaQuestTouchPlusControllerProfile>(settings);   // Quest 3 / 3S Touch Plus controllers
            // Hand tracking: the headset switches to tracked hands when the controllers are put down. With the Meta Quest
            // feature on, XR Hands adds the HAND_TRACKING permission and an optional (required=false) handtracking feature.
            EnableFeature<HandTracking>(settings);          // joint data for the hand mesh and finger trigger
            EnableFeature<MetaHandTrackingAim>(settings);   // system pinch/aim pose for hand rays
            EditorUtility.SetDirty(settings);

            // --- Standalone (Windows PC / Meta Horizon Link) ---
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            OpenXRSettings standaloneSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (standaloneSettings != null)
            {
                standaloneSettings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                standaloneSettings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;
                EnableFeature<OculusTouchControllerProfile>(standaloneSettings);
                EnableFeature<MetaQuestTouchPlusControllerProfile>(standaloneSettings);
                EnableFeature<HandTracking>(standaloneSettings);
                EnableFeature<MetaHandTrackingAim>(standaloneSettings);
                EditorUtility.SetDirty(standaloneSettings);
            }
        }

        // Hand visuals and the controller/hand switching rig come from two official samples:
        //   XR Hands > HandVisualizer and XR Interaction Toolkit > Hands Interaction Demo (needs Shader Graph).
        public const string HandsSetupPrefabPath = "Assets/Samples/XR Interaction Toolkit/2.6.4/Hands Interaction Demo/Prefabs/XR Interaction Hands Setup.prefab";

        [MenuItem("Tools/VR Armory/VR/Import Hand Tracking Samples", priority = 20)]
        public static void ImportHandSamples()
        {
            ImportSample("com.unity.xr.hands", "1.4.1", "HandVisualizer");
            ImportSample("com.unity.xr.interaction.toolkit", "2.6.4", "Hands Interaction Demo");
        }

        static void ImportSample(string package, string version, string sampleName)
        {
            foreach (var sample in UnityEditor.PackageManager.UI.Sample.FindByPackage(package, version))
            {
                if (sample.displayName != sampleName) continue;
                if (!sample.isImported && !sample.Import(UnityEditor.PackageManager.UI.Sample.ImportOptions.HideImportWindow))
                    Debug.LogError($"[VR Armory] Could not import sample '{sampleName}' from {package}.");
                return;
            }
            Debug.LogError($"[VR Armory] Sample '{sampleName}' not found in {package}@{version}. Is the package installed?");
        }

        static void EnableFeature<T>(OpenXRSettings settings) where T : OpenXRFeature
        {
            T feature = settings.GetFeature<T>();
            if (feature == null)
            {
                Debug.LogError($"[VR Armory] OpenXR feature {typeof(T).Name} not found.");
                return;
            }
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        // Android uses the "Medium" quality level and desktop uses "Ultra", so this only changes the headset.
        // 4x MSAA keeps edges clean in VR; fewer per-pixel lights and short hard shadows keep Quest at frame rate.
        static void ConfigureQuestQuality()
        {
            string[] names = QualitySettings.names;
            int medium = System.Array.IndexOf(names, "Medium");
            if (medium < 0) return;

            int previous = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(medium, false);
            QualitySettings.pixelLightCount = 2;
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadows = ShadowQuality.HardOnly;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = 8f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.vSyncCount = 0; // the headset compositor paces frames
            QualitySettings.SetQualityLevel(previous, false);
        }

        // XRI keeps interaction layer names in an asset it normally creates the first time Project Settings is opened.
        // Until it exists the Starter Assets validator throws a NullReferenceException, so it is created here with
        // layer 31 named "Teleport" (the layer the Starter Assets teleport interactor uses).
        static void ConfigureInteractionLayers()
        {
            if (!File.Exists(LayerSettingsPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LayerSettingsPath));
                var yaml = new System.Text.StringBuilder(
@"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: " + LayerSettingsScriptGuid + @", type: 3}
  m_Name: InteractionLayerSettings
  m_EditorClassIdentifier: 
  m_LayerNames:
  - Default
");
                for (int i = 1; i < 32; i++) yaml.Append(i == 31 ? "  - Teleport\n" : "  - \n");
                File.WriteAllText(LayerSettingsPath, yaml.ToString());
                AssetDatabase.ImportAsset(LayerSettingsPath);
            }

            var settings = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(LayerSettingsPath));
            SerializedProperty names = settings.FindProperty("m_LayerNames");
            if (names != null && names.arraySize == 32)
            {
                names.GetArrayElementAtIndex(31).stringValue = "Teleport";
                settings.ApplyModifiedProperties();
            }
        }

        // Unity's own OpenXR checklist for Android (the same list as Project Settings > XR Plug-in Management > Project Validation).
        public static int LogOpenXRValidation()
        {
            var issues = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
            foreach (OpenXRFeature.ValidationRule issue in issues)
            {
                Debug.Log($"[VR Armory] OpenXR validation ({(issue.error ? "ERROR" : "warning")}): {issue.message}");
            }
            Debug.Log($"[VR Armory] OpenXR validation for Android: {issues.Count} open issue(s).");
            return issues.Count;
        }

        // Command line entry point: Unity.exe -batchmode -buildTarget Android -executeMethod VRArmory.Editor.VRProjectSetup.ConfigureBatch -quit
        public static void ConfigureBatch()
        {
            Configure();
        }
    }
}
#endif
