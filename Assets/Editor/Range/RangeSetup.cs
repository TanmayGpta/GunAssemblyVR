#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static VRArmory.Editor.VRSceneSetup;

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

        // Sgt. Forrest Gump: just outside the left wall of the leftmost booth (its open end, at the archway), facing the
        // armory the trainee walks in from. His offer window floats at his side, toward the middle of the archway.
        // (Stepped forward from the wall end so his salute clears the wall.)
        static readonly Vector3 SergeantPos = new Vector3(-2.05f, 0f, 7.25f);
        static readonly Vector3 SergeantLooksAt = new Vector3(-0.4f, 0f, 5.1f);
        static readonly Vector3 PromptPos = new Vector3(-1.4f, 1.5f, 6.85f);

        // Fire training booth: the middle one (all three dummies in view). Trainee stands at BoothStand facing +Z; the
        // pistol and magazine lie on the counter (top 0.85 m, front edge on the firing line Z 9.5); the panel floats at
        // the trainee's left, by the booth wall.
        static readonly Vector3 BoothStand = new Vector3(0f, 0f, 8.55f);
        static readonly Vector3 BoothEye = new Vector3(0f, 1.55f, 8.55f);
        static readonly Vector3 PistolSpot = new Vector3(-0.12f, 0.85f, 9.15f);
        static readonly Vector3 MagazineSpot = new Vector3(0.18f, 0.85f, 9.15f);
        static readonly Vector3 BoothPanelPos = new Vector3(-0.42f, 1.42f, 9.0f);  // clear of the booth wall (X -0.75)
        static readonly Vector3 ResultsPos = new Vector3(0f, 1.45f, 9.35f);       // over the counter, straight ahead
        const float BoothW = 600f, BoothH = 440f;
        const float PromptW = 760f, PromptH = 300f; // canvas units = mm
        const string SergeantRootName = "Range_Sergeant";

        [MenuItem("Tools/VR Armory/Range/2. Setup Range Sergeant", priority = 21)]
        public static void SetupSergeant()
        {
            if (Object.FindObjectOfType<RangeTarget>(true) == null)
            {
                EditorUtility.DisplayDialog("VR Armory", "No range in the open scene. Open SampleScene and try again.", "OK");
                return;
            }
            VRVoiceGuide trainerVoice = null;
            foreach (VRVoiceGuide v in Object.FindObjectsOfType<VRVoiceGuide>(true))
                if (v.GetComponentInParent<RangeSergeant>() == null) trainerVoice = v;
            GameObject old = GameObject.Find(SergeantRootName);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(SergeantRootName);

            // Stand him on whatever floor is there (armory / range floors).
            Physics.SyncTransforms();
            Vector3 feet = SergeantPos;
            if (Physics.Raycast(SergeantPos + Vector3.up * 2f, Vector3.down, out RaycastHit floor, 4f, ~0, QueryTriggerInteraction.Ignore)) feet.y = floor.point.y;
            Animator soldier = IntroSceneSetup.BuildSoldier(root.transform, feet, SergeantLooksAt, IntroSceneSetup.SoldierController(), 0f);
            if (soldier != null) soldier.name = "Sgt_Forrest_Gump";

            GameObject canvas = IntroSceneSetup.WorldCanvas(root.transform, "Range_Offer_Window", new Vector2(PromptW, PromptH), true);
            Vector3 face = PromptPos - new Vector3(SergeantLooksAt.x, PromptPos.y, SergeantLooksAt.z);
            canvas.transform.SetPositionAndRotation(PromptPos, Quaternion.LookRotation(face));
            Transform p = canvas.transform;
            float cw = PromptW - 80f, right = PromptW * 0.5f - 40f;
            Label(p, "Speaker", VRTrainingText.SergeantName, 22, Accent, new Vector2(0f, 112f), new Vector2(cw, 34f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(p, "Section", VRTrainingText.RangeSection, 18, TextSecondary, new Vector2(0f, 112f), new Vector2(cw, 34f), TextAnchor.MiddleRight, FontStyle.Bold);
            Text question = Label(p, "Question", VRTrainingText.RangeOffer, 26, TextPrimary, new Vector2(0f, 18f), new Vector2(cw, 130f), TextAnchor.UpperLeft);
            Button yes = MakeButton(p, "Yes_Button", VRTrainingText.RangeYes, new Vector2(right - 120f, -100f), new Vector2(240f, 58f), true);
            Button no = MakeButton(p, "No_Button", VRTrainingText.RangeNo, new Vector2(right - 240f - 16f - 90f, -100f), new Vector2(180f, 58f), false);
            Text hint = Label(p, "Desktop_Hint", VRTrainingText.RangeDesktopHint, 15, TextSecondary, new Vector2(-PromptW * 0.5f + 40f + 130f, -100f), new Vector2(260f, 40f), TextAnchor.MiddleLeft);

            RangeSergeant sergeant = root.AddComponent<RangeSergeant>();
            sergeant.soldier = soldier;
            sergeant.questionText = question;
            sergeant.trainerVoice = trainerVoice;
            sergeant.floorY = feet.y;
            sergeant.voice = BuildVoice(root.transform);
            sergeant.prompt = canvas.AddComponent<CanvasGroup>();
            sergeant.yesButton = yes;
            sergeant.noButton = no;
            sergeant.desktopHint = hint;

            BuildTraining(root.transform, sergeant);

            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            Debug.Log("[VR Armory] Range sergeant and fire training set up" + (soldier == null ? " WITHOUT the soldier model (see .gitignore)." : "."));
        }

        static VRVoiceGuide BuildVoice(Transform parent)
        {
            var go = new GameObject("Sergeant_Voice");
            go.transform.SetParent(parent, false);
            go.AddComponent<AudioSource>().playOnAwake = false;
            VRVoiceGuide voice = go.AddComponent<VRVoiceGuide>();
            voice.clips = VRVoiceSetup.LoadClips(out List<VoiceLine> missing);
            foreach (VoiceLine l in missing)
                if (l.ToString().StartsWith("Rng_")) Debug.LogWarning($"[VR Armory] Missing voice clip VO_{l}.wav (Tools/VoiceGeneration).");
            return voice;
        }

        // The training round: booth points, the panel at the trainee's left, and FireTraining wired to everything.
        static void BuildTraining(Transform root, RangeSergeant sergeant)
        {
            PistolFiring firing = Object.FindObjectOfType<PistolFiring>(true);
            if (firing == null)
            {
                Debug.LogWarning("[VR Armory] No live firing in the scene: run Range > 1. Setup Live Firing, then this again.");
                return;
            }
            Transform booth = new GameObject("Training_Booth").transform;
            booth.SetParent(root, false);
            Transform stand = Point(booth, "Stand_Point", BoothStand, Quaternion.identity);
            Transform pistolSpot = Point(booth, "Pistol_Spot", PistolSpot, Quaternion.identity);
            Transform magSpot = Point(booth, "Magazine_Spot", MagazineSpot, Quaternion.identity);

            GameObject canvas = IntroSceneSetup.WorldCanvas(booth, "Booth_Panel", new Vector2(BoothW, BoothH), true);
            canvas.transform.SetPositionAndRotation(BoothPanelPos, Quaternion.LookRotation(BoothPanelPos - BoothEye));
            Transform p = canvas.transform;
            float cw = BoothW - 70f, right = BoothW * 0.5f - 35f;

            FireTraining t = root.gameObject.AddComponent<FireTraining>();
            Label(p, "Title", VRTrainingText.RangeSection, 20, Accent, new Vector2(0f, 190f), new Vector2(cw, 30f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(p, "Speaker", VRTrainingText.SergeantName, 16, TextSecondary, new Vector2(0f, 190f), new Vector2(cw, 30f), TextAnchor.MiddleRight, FontStyle.Bold);
            t.stepText = Label(p, "Step", "", 23, TextPrimary, new Vector2(0f, 128f), new Vector2(cw, 80f), TextAnchor.UpperLeft);
            float statW = (cw - 30f) / 3f;
            t.timeText = Stat(p, "Time", new Vector2(-statW - 15f, 30f), statW);
            t.roundsText = Stat(p, "Rounds", new Vector2(0f, 30f), statW);
            t.scoreText = Stat(p, "Score", new Vector2(statW + 15f, 30f), statW);
            t.resultText = Label(p, "Result", "", 20, TextPrimary, new Vector2(0f, -78f), new Vector2(cw, 110f), TextAnchor.MiddleCenter);
            t.endButton = MakeButton(p, "End_Button", VRTrainingText.RangeEnd, new Vector2(right - 110f, -180f), new Vector2(220f, 54f), false);
            t.retryButton = MakeButton(p, "Retry_Button", VRTrainingText.RangeRetry, new Vector2(right - 200f - 16f - 100f, -180f), new Vector2(200f, 54f), true);
            t.finishButton = MakeButton(p, "Finish_Button", VRTrainingText.RangeFinish, new Vector2(right - 100f, -180f), new Vector2(200f, 54f), false);
            t.desktopHint = Label(p, "Desktop_Hint", VRTrainingText.RangeBoothDesktopHint, 13, TextSecondary, new Vector2(-BoothW * 0.5f + 35f + 90f, -180f), new Vector2(180f, 54f), TextAnchor.MiddleLeft);
            t.panel = canvas.AddComponent<CanvasGroup>();

            t.sergeant = sergeant;
            t.voice = sergeant.voice;
            t.firing = firing;
            t.functionCheck = firing.functionCheck;
            t.frameGrab = firing.frameGrab;
            t.frame = firing.functionCheck != null ? firing.functionCheck.frame : null;
            t.ammo = firing.ammo;
            t.scoring = Object.FindObjectOfType<RangeScoring>(true);
            t.standPoint = stand;
            t.pistolSpot = pistolSpot;
            t.magazineSpot = magSpot;
            t.resultsSpot = Point(booth, "Results_Spot", ResultsPos, Quaternion.LookRotation(ResultsPos - BoothEye));
        }

        static Text Stat(Transform parent, string name, Vector2 pos, float width)
        {
            Rounded(parent, name + "_Card", pos, new Vector2(width, 96f), new Color(0.11f, 0.135f, 0.17f, 1f), 0.3f);
            return Label(parent, name, "", 17, TextSecondary, pos, new Vector2(width, 96f), TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        static Transform Point(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(pos, rot);
            return t;
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
