#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRArmory.Editor
{
    // Fire range setup for the training scene (SampleScene), additive like the VR setup: run it again any time.
    //   1. Setup Live Firing: the assembled pistol fires once its function check has passed (PistolFiring + ShotEffects).
    // Plus a Play-mode shortcut for testing: Debug > Skip Assembly seats every part and passes the function check.
    public static class RangeSetup
    {
        public const string WeaponAudioFolder = "Assets/Audio/Weapons";
        const string RootName = "Range_Live_Fire";
        // Near / Mid / Far dummy distances from the firing line, metres (ArmorySetupHelper builds them at the same).
        public static readonly float[] TargetDistances = { 6f, 10f, 15f };

        [MenuItem("Tools/VR Armory/Range/1. Setup Live Firing", priority = 20)]
        public static void SetupLiveFiring()
        {
            FunctionCheck functionCheck = Object.FindObjectOfType<FunctionCheck>(true);
            if (functionCheck == null)
            {
                EditorUtility.DisplayDialog("VR Armory", "No FunctionCheck in the open scene. Open SampleScene (with the assembly set up) and try again.", "OK");
                return;
            }

            VRAssemblyGrab frameGrab = null;
            foreach (VRAssemblyGrab g in Object.FindObjectsOfType<VRAssemblyGrab>(true))
            {
                AssemblyPart part = g.GetComponent<AssemblyPart>();
                if (part != null && part.partType == GunPart.LowerFrame) frameGrab = g;
            }
            if (frameGrab == null) Debug.LogWarning("[VR Armory] No VR grab on the pistol frame (run Setup VR Scene first): live fire will work without recoil kick / vibration.");

            GameObject root = GameObject.Find(RootName);
            if (root == null) root = new GameObject(RootName);

            ShotEffects effects = GetOrAdd<ShotEffects>(root);
            effects.baseMaterial = VRSceneSetup.GetOrCreateGuideMaterial(); // Sprites/Default, saved so it is in the build
            effects.holeSize = 0.022f;

            PistolFiring firing = GetOrAdd<PistolFiring>(root);
            firing.functionCheck = functionCheck;
            firing.frameGrab = frameGrab;
            firing.effects = effects;
            firing.ammo = SetupMagazine(root, functionCheck, frameGrab);
            firing.kickAngle = 3f;    // hand + pistol turn together about the wrist
            firing.kickBack = 0.015f;
            firing.gunshot = LoadWeaponClip("Gunshot");
            if (firing.gunshot == null) Debug.LogWarning($"[VR Armory] No Gunshot audio file in {WeaponAudioFolder}: shots will be silent.");

            RangeScoring scoring = GetOrAdd<RangeScoring>(root);
            scoring.firing = firing;
            EditorUtility.SetDirty(scoring);

            PlaceTargets();

            EditorUtility.SetDirty(effects);
            EditorUtility.SetDirty(firing);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            Debug.Log("[VR Armory] Live firing set up: the pistol fires once its function check has passed (Play mode: Tools > VR Armory > Debug > Skip Assembly).");
        }

        // Chamber + magazine: the pistol's magazine part and the socket in its grip.
        static PistolMagazine SetupMagazine(GameObject root, FunctionCheck functionCheck, VRAssemblyGrab frameGrab)
        {
            PistolMagazine ammo = GetOrAdd<PistolMagazine>(root);
            ammo.functionCheck = functionCheck;
            ammo.frameGrab = frameGrab;
            ammo.magazine = null;
            ammo.magazineWell = null;
            foreach (AssemblyPart part in Object.FindObjectsOfType<AssemblyPart>(true))
            {
                if (part.partType == GunPart.Magazine) ammo.magazine = part;
            }
            foreach (SnapSocket socket in Object.FindObjectsOfType<SnapSocket>(true))
            {
                if (ammo.magazine != null && socket.acceptedPart == ammo.magazine) ammo.magazineWell = socket;
            }
            if (ammo.magazine == null || ammo.magazineWell == null) Debug.LogWarning("[VR Armory] Magazine part or its socket not found: the magazine cannot be released / reloaded.");
            EditorUtility.SetDirty(ammo);
            return ammo;
        }

        // Moves the dummies (nearest first) to TargetDistances and re-aims each one's ceiling spot light at it.
        static void PlaceTargets()
        {
            RangeTarget[] targets = Object.FindObjectsOfType<RangeTarget>(true);
            System.Array.Sort(targets, (a, b) => a.distanceMeters.CompareTo(b.distanceMeters));
            for (int i = 0; i < targets.Length && i < TargetDistances.Length; i++)
            {
                RangeTarget t = targets[i];
                Undo.RecordObject(t, "Place range targets");
                Undo.RecordObject(t.transform, "Place range targets");
                t.SetDistance(TargetDistances[i]);
                EditorUtility.SetDirty(t);

                // "Target_02_Mid" -> spot light "Target_Spot_02"
                string[] parts = t.targetId != null ? t.targetId.Split('_') : new string[0];
                GameObject spot = parts.Length >= 2 ? GameObject.Find("Target_Spot_" + parts[1]) : null;
                if (spot == null) continue;
                Undo.RecordObject(spot.transform, "Place range targets");
                Vector3 p = t.transform.position;
                spot.transform.position = new Vector3(spot.transform.position.x, spot.transform.position.y, p.z - 3f);
                spot.transform.LookAt(new Vector3(p.x, 1.4f * t.transform.lossyScale.y, p.z));
            }
        }

        // A clip in Assets/Audio/Weapons whose file name is 'name' (any audio format), imported for instant playback.
        internal static AudioClip LoadWeaponClip(string name)
        {
            if (!AssetDatabase.IsValidFolder(WeaponAudioFolder)) return null;
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:AudioClip", new[] { WeaponAudioFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), name, System.StringComparison.OrdinalIgnoreCase)) continue;
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer != null)
                {
                    AudioImporterSampleSettings s = importer.defaultSampleSettings;
                    bool changed = s.loadType != AudioClipLoadType.DecompressOnLoad || !s.preloadAudioData;
                    if (changed)
                    {
                        s.loadType = AudioClipLoadType.DecompressOnLoad; // short and played often: no decode delay
                        s.preloadAudioData = true;
                        importer.defaultSampleSettings = s;
                        importer.SaveAndReimport();
                    }
                }
                return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            return null;
        }

        [MenuItem("Tools/VR Armory/Debug/Skip Assembly (Play Mode)", priority = 100)]
        static void SkipAssembly()
        {
            if (GunAssemblyManager.Instance != null) GunAssemblyManager.Instance.DebugCompleteAll();
        }

        [MenuItem("Tools/VR Armory/Debug/Skip Assembly (Play Mode)", true)]
        static bool SkipAssemblyValid() => Application.isPlaying && GunAssemblyManager.Instance != null;

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
#endif
