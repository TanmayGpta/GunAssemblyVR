#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.XR.Interaction.Toolkit.Transformers;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace VRArmory.Editor
{
    // Adds the Version 1 VR layer to SampleScene. Strictly additive and safe to run again:
    //  - creates (or updates) one new root object, VR_Version1, holding everything VR;
    //  - adds XRGeneralGrabTransformer + VRAssemblyGrab to the five gun parts (saved disabled, desktop never uses them);
    //  - never moves, renames or deletes an existing object.
    //   Tools > VR Armory > VR > 2. Setup VR Scene (additive)
    public static class VRSceneSetup
    {
        const string RootName = "VR_Version1";
        const string ContentName = "VR_Content";
        // Hands rig: controllers when they are held, tracked hands when they are put down (XR Input Modality Manager).
        const string SetupPrefabPath = VRProjectSetup.HandsSetupPrefabPath;
        const string OldSetupName = "XR Interaction Setup"; // controller-only rig from Version 1
        const float GrabEaseIn = 0.15f; // s for a grabbed part to travel into the hand
        const string GuideMaterialPath = "Assets/Materials/VR/VR_Guide_Line.mat";
        const string GhostMaterialPath = "Assets/Materials/VR/VR_Ghost_Demo.mat";
        const float WalkSpeed = 1.6f;  // m/s, normal walking pace
        const float TurnSpeed = 75f;   // deg/s

        // Where the desktop player stands: in front of the bench, facing downrange (+Z).
        static readonly Vector3 BenchStandPoint = new Vector3(0f, 0f, -0.95f);

        // Training panel colours (one palette: teal = "this is what to use", green = done, amber = adjust, red = wrong).
        static readonly Color PanelColor = new Color(0.05f, 0.065f, 0.085f, 0.97f);
        static readonly Color CardColor = new Color(0.115f, 0.14f, 0.175f, 1f);
        static readonly Color Accent = new Color(0.3f, 0.92f, 0.86f);
        static readonly Color Success = new Color(0.36f, 0.9f, 0.52f);
        static readonly Color Warning = new Color(1.0f, 0.76f, 0.32f);
        static readonly Color TextPrimary = new Color(0.95f, 0.97f, 1f);
        static readonly Color TextSecondary = new Color(0.62f, 0.68f, 0.76f);
        static readonly Color PrimaryButton = new Color(0.14f, 0.6f, 0.56f);
        static readonly Color SecondaryButton = new Color(0.17f, 0.2f, 0.25f);

        static Sprite RoundedSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        static Sprite CircleSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        // Panel size in canvas units (1 unit = 1 mm). Text boxes are left-aligned inside a 48 mm margin.
        const float PanelW = 800f, PanelH = 460f, ContentW = 704f;

        [MenuItem("Tools/VR Armory/VR/2. Setup VR Scene (additive)", priority = 2)]
        public static void SetupFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Build()) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // Command line: -executeMethod VRArmory.Editor.VRSceneSetup.SetupBatch
        public static void SetupBatch()
        {
            var scene = EditorSceneManager.OpenScene(VRProjectSetup.ScenePath);
            if (Build()) EditorSceneManager.SaveScene(scene);
        }

        static bool Build()
        {
            GunAssemblyManager manager = Object.FindObjectOfType<GunAssemblyManager>();
            GameObject desktopPlayer = GameObject.Find("Armorer_Player");
            GameObject setupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SetupPrefabPath);
            if (manager == null || desktopPlayer == null || setupPrefab == null)
            {
                Debug.LogError("[VR Armory] VR setup needs Gun_Assembly_Manager, Armorer_Player and the XRI Hands Interaction Demo sample (Tools > VR Armory > VR > Import Hand Tracking Samples).");
                return false;
            }
            FunctionCheck functionCheck = manager.GetComponent<FunctionCheck>();

            GameObject root = FindOrCreate(null, RootName);
            Transform content = FindOrCreate(root.transform, ContentName).transform;
            content.gameObject.SetActive(true); // while building

            // --- XR rig: the Hands Interaction Demo "XR Interaction Hands Setup" (interaction manager, EventSystem,
            // XR Origin with controllers + tracked hands). It replaces the controller-only rig of earlier versions.
            Transform oldSetup = content.Find(OldSetupName);
            if (oldSetup != null) Object.DestroyImmediate(oldSetup.gameObject);
            Transform setupT = content.Find(setupPrefab.name);
            if (setupT == null)
            {
                GameObject setup = (GameObject)PrefabUtility.InstantiatePrefab(setupPrefab, content);
                setupT = setup.transform;
            }
            XRInteractionManager interactionManager = setupT.GetComponentInChildren<XRInteractionManager>(true);
            XROrigin origin = setupT.GetComponentInChildren<XROrigin>(true);
            origin.transform.SetPositionAndRotation(BenchStandPoint, Quaternion.identity);
            PrefabUtility.RecordPrefabInstancePropertyModifications(origin.transform);
            var originSo = new SerializedObject(origin);
            originSo.FindProperty("m_RequestedTrackingOriginMode").intValue = (int)XROrigin.TrackingOriginMode.Floor; // real standing height
            originSo.ApplyModifiedProperties();

            // Locomotion: left stick walks, right stick turns smoothly. No teleport or snap turn on either hand.
            // (Set through SerializedObject: the C# properties also enable input actions, which must not happen in edit mode.)
            foreach (ActionBasedControllerManager cm in setupT.GetComponentsInChildren<ActionBasedControllerManager>(true))
            {
                bool left = cm.name.Contains("Left");
                var so = new SerializedObject(cm);
                so.FindProperty("m_SmoothMotionEnabled").boolValue = left;
                so.FindProperty("m_SmoothTurnEnabled").boolValue = !left;
                so.FindProperty("m_TeleportModeActivate").objectReferenceValue = null;
                so.FindProperty("m_TeleportModeCancel").objectReferenceValue = null;
                so.FindProperty("m_KeepLocomotionWhileHolding").boolValue = true; // thumbstick never moves a held part
                so.ApplyModifiedProperties();
            }
            foreach (DynamicMoveProvider move in setupT.GetComponentsInChildren<DynamicMoveProvider>(true))
            {
                var so = new SerializedObject(move);
                so.FindProperty("m_MoveSpeed").floatValue = WalkSpeed;
                so.ApplyModifiedProperties();
            }
            foreach (ActionBasedContinuousTurnProvider turn in setupT.GetComponentsInChildren<ActionBasedContinuousTurnProvider>(true))
            {
                var so = new SerializedObject(turn);
                so.FindProperty("m_TurnSpeed").floatValue = TurnSpeed;
                so.ApplyModifiedProperties();
            }
            // No comfort vignette: the Starter Assets tunneling vignette narrows the view while moving/turning.
            foreach (TunnelingVignetteController vignette in setupT.GetComponentsInChildren<TunnelingVignetteController>(true))
            {
                vignette.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(vignette.gameObject);
            }

            // Controller and hand rays: a far grab pulls the part into the hand (force grab) and the thumbstick no longer
            // pushes/rotates what is held (no anchor control), so the wrist does all repositioning.
            // The pointer line stays dim until it is over a panel, button or interactable.
            foreach (XRRayInteractor ray in setupT.GetComponentsInChildren<XRRayInteractor>(true))
            {
                if (ray.name.Contains("Teleport")) continue;
                var raySo = new SerializedObject(ray);
                raySo.FindProperty("m_UseForceGrab").boolValue = true;
                raySo.FindProperty("m_AllowAnchorControl").boolValue = false;
                raySo.ApplyModifiedProperties();
                if (ray.GetComponent<XRInteractorLineVisual>() != null)
                    GetOrAdd<VRRayHoverDimmer>(ray.gameObject);
            }

            // Tracked hands: who is a hand vs a controller, finger curl, and the grip wrap while holding the pistol.
            XRInputModalityManager modality = setupT.GetComponentInChildren<XRInputModalityManager>(true);
            GameObject handsObject = FindOrCreate(content, "VR_Hands");
            VRHands hands = GetOrAdd<VRHands>(handsObject);
            hands.modality = modality;
            GetOrAdd<VRHandGripPose>(handsObject);
            if (modality == null) Debug.LogWarning("[VR Armory] No XR Input Modality Manager on the rig: controller/hand switching will not work.");

            // One hand mesh per hand: the Left/Right Hand Interaction Visuals drive themselves (XRHandSkeletonDriver),
            // so the rig's extra XR Hands "Hand Visualizer" only adds a second copy of each hand.
            foreach (Transform t in origin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Hand Visualizer") continue;
                t.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
            }
            EditorUtility.SetDirty(hands);

            // Hand-mode movement: free-hand pinch teleport (controllers stay joystick-only, their teleport is off above).
            VRHandTeleport handTeleport = GetOrAdd<VRHandTeleport>(handsObject);
            handTeleport.hands = hands;
            handTeleport.lineMaterial = GetOrCreateGuideMaterial();
            handTeleport.teleportation = setupT.GetComponentInChildren<TeleportationProvider>(true);
            if (handTeleport.teleportation == null)
            {
                LocomotionSystem locomotion = setupT.GetComponentInChildren<LocomotionSystem>(true);
                if (locomotion != null)
                {
                    handTeleport.teleportation = GetOrAdd<TeleportationProvider>(locomotion.gameObject);
                    handTeleport.teleportation.system = locomotion;
                }
                else Debug.LogWarning("[VR Armory] No Locomotion System on the rig: hand teleport will not work.");
            }
            else handTeleport.teleportation.enabled = true;
            EditorUtility.SetDirty(handTeleport);

            GetOrAdd<VRLocomotionCollision>(FindOrCreate(content, "VR_Locomotion_Collision")).body = origin.GetComponent<CharacterController>();
            ActionBasedController leftController = FindController(origin, modality != null ? modality.leftController : null, "Left Controller");
            ActionBasedController rightController = FindController(origin, modality != null ? modality.rightController : null, "Right Controller");
            Camera xrCamera = origin.Camera;

            // --- Gun parts: XR grab, saved disabled ---
            var grabs = new List<VRAssemblyGrab>();
            VRAssemblyGrab frameGrab = null;
            foreach (AssemblyPart part in Object.FindObjectsOfType<AssemblyPart>(true))
            {
                VRAssemblyGrab grab = ConfigurePart(part, interactionManager);
                grabs.Add(grab);
                if (part.partType == GunPart.LowerFrame) frameGrab = grab;
            }

            // Teleport objects from earlier versions.
            foreach (string oldName in new[] { "Teleport_Floor", "Bench_Teleport_Anchor" })
            {
                Transform old = content.Find(oldName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            // --- Shared (desktop + VR): moving range targets ---
            BuildMovingTargets();

            // --- VR-only behaviour ---
            VRFunctionCheckControls fcControls = GetOrAdd<VRFunctionCheckControls>(FindOrCreate(content, "VR_Function_Check_Controls"));
            fcControls.functionCheck = functionCheck;
            fcControls.frameGrab = frameGrab;
            fcControls.interactionManager = interactionManager;
            fcControls.leftController = leftController;
            fcControls.rightController = rightController;
            fcControls.hands = hands;

            // In-world hints (highlight, target line, direction arrows, controller tooltips) + placement assist.
            VRGuidance guidance = GetOrAdd<VRGuidance>(FindOrCreate(content, "VR_Guidance"));
            guidance.manager = manager;
            guidance.functionCheck = functionCheck;
            guidance.functionCheckControls = fcControls;
            guidance.viewCamera = xrCamera;
            guidance.lineMaterial = GetOrCreateGuideMaterial();
            guidance.roundedSprite = RoundedSprite;

            VRTrainingMenu menu = BuildMenu(content, manager, functionCheck, guidance, xrCamera);

            // v1.2: ghost demonstrations of the slide / trigger movements, and the spoken instructor.
            VRGhostDemo ghostDemo = GetOrAdd<VRGhostDemo>(FindOrCreate(content, "VR_Ghost_Demo"));
            ghostDemo.functionCheck = functionCheck;
            ghostDemo.ghostMaterial = GetOrCreateGhostMaterial();
            ghostDemo.lineMaterial = guidance.lineMaterial;

            GameObject voiceObject = FindOrCreate(content, "VR_Voice_Guide");
            VRVoiceGuide voice = GetOrAdd<VRVoiceGuide>(voiceObject); // adds its AudioSource
            voiceObject.GetComponent<AudioSource>().playOnAwake = false;
            voice.clips = VRVoiceSetup.LoadClips(out List<VoiceLine> missingVoice);
            if (missingVoice.Count > 0)
                Debug.LogWarning($"[VR Armory] {missingVoice.Count} voice line(s) have no recording yet (they play silently): {string.Join(", ", missingVoice)}");

            VRTrainingCoach coach = GetOrAdd<VRTrainingCoach>(FindOrCreate(content, "VR_Training_Coach"));
            coach.manager = manager;
            coach.functionCheck = functionCheck;
            coach.functionCheckControls = fcControls;
            coach.guidance = guidance;
            coach.menu = menu;
            coach.voice = voice;
            coach.ghost = ghostDemo;

            QuestPerformanceSettings perf = GetOrAdd<QuestPerformanceSettings>(FindOrCreate(content, "Quest_Performance"));
            perf.keepShadows = FindLights("Directional Light", "Workbench_Ceiling_Light");

            // --- Shared (desktop + VR): assembly sounds ---
            AssemblyFeedbackAudio audio = GetOrAdd<AssemblyFeedbackAudio>(FindOrCreate(root.transform, "Assembly_Feedback_Audio"));
            audio.manager = manager;

            // --- The switch that picks desktop or VR at startup ---
            VRPlatformSwitch platformSwitch = GetOrAdd<VRPlatformSwitch>(root);
            platformSwitch.desktopPlayer = desktopPlayer;
            platformSwitch.vrContent = content.gameObject;
            platformSwitch.interactionManager = interactionManager;
            platformSwitch.partGrabs = grabs.ToArray();

            foreach (Object o in new Object[] { menu, guidance, fcControls, ghostDemo, voice, coach, perf, audio, platformSwitch }) EditorUtility.SetDirty(o);
            content.gameObject.SetActive(false); // desktop mode never sees the VR objects
            Debug.Log($"[VR Armory] VR scene setup done: {grabs.Count} grabbable parts, XR rig at {BenchStandPoint}, training panel, guidance, smooth locomotion, moving targets, function-check controls, "
                      + $"ghost demonstrations, voice guide ({voice.clips.Length - missingVoice.Count}/{voice.clips.Length} lines recorded).");
            return true;
        }

        static VRAssemblyGrab ConfigurePart(AssemblyPart part, XRInteractionManager interactionManager)
        {
            // Scaling off: the thumbstick must never resize a gun part.
            XRGeneralGrabTransformer transformer = GetOrAdd<XRGeneralGrabTransformer>(part.gameObject);
            transformer.allowOneHandedScaling = false;
            transformer.allowTwoHandedScaling = false;

            VRAssemblyGrab grab = GetOrAdd<VRAssemblyGrab>(part.gameObject);
            grab.interactionManager = interactionManager;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;          // released parts are handled by the sockets / return to the bench
            grab.attachEaseInTime = GrabEaseIn;  // a grabbed part travels smoothly into the hand
            if (part.partType == GunPart.LowerFrame)
            {
                // The pistol always lands in the palm by its grip, muzzle forward; a tracked hand wraps around it.
                grab.useDynamicAttach = false;
                grab.attachTransform = GetOrCreateGripAttach(part.transform);
                grab.wrapTrackedHand = true;
            }
            else
            {
                grab.useDynamicAttach = true;    // small parts: held where they were grabbed, keeping their rotation
                grab.matchAttachRotation = true;
                grab.wrapTrackedHand = false;
            }
            grab.trackScale = false;
            grab.retainTransformParent = true;
            grab.addDefaultGrabTransformers = false;
            grab.startingSingleGrabTransformers = new List<XRBaseGrabTransformer> { transformer };
            grab.startingMultipleGrabTransformers = new List<XRBaseGrabTransformer> { transformer };
            // Frame and slide are the large sub-assemblies: a second hand may join to turn them.
            bool twoHanded = part.partType == GunPart.LowerFrame || part.partType == GunPart.UpperSlide;
            grab.selectMode = twoHanded ? InteractableSelectMode.Multiple : InteractableSelectMode.Single;
            grab.enabled = false; // VRPlatformSwitch enables it when a headset is running
            EditorUtility.SetDirty(transformer);
            EditorUtility.SetDirty(grab);
            return grab;
        }

        // Each dummy sweeps across the range at its own pace; farther dummies travel wider.
        static void BuildMovingTargets()
        {
            RangeTarget[] targets = Object.FindObjectsOfType<RangeTarget>(true);
            System.Array.Sort(targets, (a, b) => a.distanceMeters.CompareTo(b.distanceMeters));
            for (int i = 0; i < targets.Length; i++)
            {
                RangeTargetMover mover = GetOrAdd<RangeTargetMover>(targets[i].gameObject);
                mover.amplitude = 1f + i * 0.75f;
                mover.period = 6f + i * 1.5f;
                mover.phase = i * 0.37f;
                EditorUtility.SetDirty(mover);
            }
        }

        static Material GetOrCreateGuideMaterial()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(GuideMaterialPath);
            if (m != null) return m;
            if (!AssetDatabase.IsValidFolder("Assets/Materials/VR")) AssetDatabase.CreateFolder("Assets/Materials", "VR");
            m = new Material(Shader.Find("Sprites/Default")) { color = Color.white }; // unlit, vertex colour + alpha
            AssetDatabase.CreateAsset(m, GuideMaterialPath);
            return m;
        }

        // A saved material, so the GhostDemo shader is included in the Quest build (Shader.Find alone would not be).
        static Material GetOrCreateGhostMaterial()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
            if (m != null) return m;
            if (!AssetDatabase.IsValidFolder("Assets/Materials/VR")) AssetDatabase.CreateFolder("Assets/Materials", "VR");
            m = new Material(Shader.Find("VRArmory/GhostDemo"));
            AssetDatabase.CreateAsset(m, GhostMaterialPath);
            return m;
        }

        // ---------- Training panel (the one world-space panel, in front of the bench) ----------

        static VRTrainingMenu BuildMenu(Transform content, GunAssemblyManager manager, FunctionCheck functionCheck, VRGuidance guidance, Camera eventCamera)
        {
            GameObject old = content.Find("VR_Training_Menu")?.gameObject;
            if (old != null) Object.DestroyImmediate(old); // our own object: rebuilt so layout changes apply

            GameObject go = new GameObject("VR_Training_Menu", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(content, false);
            // Straight ahead behind the bench, above the parts (they lie at 0.86 m), about 1.3 m from the trainee's eyes,
            // tilted slightly up toward them. Replaces both the left-hand menu and (in VR) the desktop HUD.
            go.transform.SetPositionAndRotation(new Vector3(0f, 1.48f, 0.42f), Quaternion.Euler(6f, 0f, 0f));
            go.transform.localScale = Vector3.one * 0.001f; // 1 canvas unit = 1 mm
            ((RectTransform)go.transform).sizeDelta = new Vector2(PanelW, PanelH);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f; // sharp text at VR distances
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            Rounded(go.transform, "Background", Vector2.zero, new Vector2(PanelW, PanelH), PanelColor, 0.4f).raycastTarget = true;
            Rounded(go.transform, "Accent_Rule", new Vector2(0f, PanelH * 0.5f - 3f), new Vector2(PanelW - 48f, 3f), Accent, 2f);

            VRTrainingMenu menu = go.AddComponent<VRTrainingMenu>();
            menu.manager = manager;
            menu.functionCheck = functionCheck;
            menu.guidance = guidance;

            BuildModules(go.transform, menu);
            BuildComingSoon(go.transform, menu);
            BuildIntro(go.transform, menu);
            BuildTraining(go.transform, menu, manager.steps.Count);
            BuildComplete(go.transform, menu);

            foreach (GameObject p in new[] { menu.comingSoonPanel, menu.introPanel, menu.progressPanel, menu.completePanel }) p.SetActive(false);
            return menu;
        }

        static void BuildModules(Transform root, VRTrainingMenu menu)
        {
            Transform p = (menu.modulesPanel = Panel(root, "Modules_Panel")).transform;
            Left(p, "Brand", VRTrainingText.Brand, 18, Accent, 192f, FontStyle.Bold);
            Left(p, "Title", VRTrainingText.ModulesTitle, 40, TextPrimary, 150f, FontStyle.Bold, 56f);

            menu.assemblyModuleButton = Card(p, "Module_01_Card", new Vector2(-178f, -22f), true, VRTrainingText.AssemblyModuleNumber,
                VRTrainingText.AssemblyModuleTitle, VRTrainingText.AssemblyModuleBody, VRTrainingText.StatusAvailable, out menu.assemblyModuleStatus);
            menu.shootingModuleButton = Card(p, "Module_02_Card", new Vector2(178f, -22f), false, VRTrainingText.ShootingModuleNumber,
                VRTrainingText.ShootingModuleTitle, VRTrainingText.ShootingModuleBody, VRTrainingText.StatusComingSoon, out _);

            Label(p, "Footer", VRTrainingText.ModulesFooter, 18, TextSecondary, new Vector2(0f, -192f), new Vector2(ContentW, 30f), TextAnchor.MiddleCenter);
        }

        static void BuildComingSoon(Transform root, VRTrainingMenu menu)
        {
            Transform p = (menu.comingSoonPanel = Panel(root, "Coming_Soon_Panel")).transform;
            Left(p, "Label", VRTrainingText.ComingSoonLabel, 18, Accent, 192f, FontStyle.Bold);
            Left(p, "Title", VRTrainingText.ShootingModuleTitle, 44, TextPrimary, 146f, FontStyle.Bold, 60f);
            Pill(p, "Status", VRTrainingText.StatusComingSoon, Warning, new Vector2(-352f, 92f), out _);
            Left(p, "Body", VRTrainingText.ComingSoonBody, 26, TextSecondary, 10f, FontStyle.Normal, 100f);
            menu.comingSoonBackButton = MakeButton(p, "Back_Button", VRTrainingText.Back, new Vector2(-352f + 110f, -170f), new Vector2(220f, 64f), false);
        }

        static void BuildIntro(Transform root, VRTrainingMenu menu)
        {
            Transform p = (menu.introPanel = Panel(root, "Intro_Panel")).transform;
            Left(p, "Label", VRTrainingText.IntroLabel, 18, Accent, 192f, FontStyle.Bold);
            Left(p, "Title", VRTrainingText.IntroTitle, 40, TextPrimary, 150f, FontStyle.Bold, 56f);

            (string chip, string use)[] rows =
            {
                (VRTrainingText.GripChip, VRTrainingText.GripUse),
                (VRTrainingText.TriggerChip, VRTrainingText.TriggerUse),
                (VRTrainingText.StickChip, VRTrainingText.StickUse),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                float y = 72f - i * 66f;
                Image chip = Rounded(p, "Chip_" + rows[i].chip, new Vector2(-352f + 86f, y), new Vector2(172f, 48f), SecondaryButton, 0.6f);
                Label(chip.transform, "Text", rows[i].chip, 18, Accent, Vector2.zero, new Vector2(172f, 48f), TextAnchor.MiddleCenter, FontStyle.Bold);
                Label(p, "Use_" + rows[i].chip, rows[i].use, 25, TextPrimary, new Vector2(-352f + 196f + 254f, y), new Vector2(508f, 48f), TextAnchor.MiddleLeft);
            }
            Left(p, "Hint", VRTrainingText.IntroHint, 21, Accent, -116f, FontStyle.Normal, 60f); // two lines

            menu.introBackButton = MakeButton(p, "Back_Button", VRTrainingText.Back, new Vector2(-352f + 90f, -182f), new Vector2(180f, 64f), false);
            menu.startButton = MakeButton(p, "Start_Button", VRTrainingText.StartTraining, new Vector2(352f - 140f, -182f), new Vector2(280f, 64f), true);
        }

        static void BuildTraining(Transform root, VRTrainingMenu menu, int stepCount)
        {
            Transform p = (menu.progressPanel = Panel(root, "Progress_Panel")).transform;
            menu.stepText = Left(p, "Step", "", 18, Accent, 194f, FontStyle.Bold);
            menu.timerText = Label(p, "Timer", "00:00", 18, TextSecondary, new Vector2(0f, 194f), new Vector2(ContentW, 30f), TextAnchor.MiddleRight);

            // One segment per step: green = done, teal = now, faint = later.
            const float gap = 8f;
            float w = (ContentW - gap * (stepCount - 1)) / stepCount;
            menu.progressSegments = new Image[stepCount];
            for (int i = 0; i < stepCount; i++)
            {
                float x = -ContentW * 0.5f + w * 0.5f + i * (w + gap);
                menu.progressSegments[i] = Rounded(p, $"Progress_{i + 1}", new Vector2(x, 166f), new Vector2(w, 8f), new Color(1f, 1f, 1f, 0.12f), 3f);
            }

            menu.headlineText = Left(p, "Headline", "", 44, TextPrimary, 96f, FontStyle.Bold, 112f);
            menu.detailText = Left(p, "Detail", "", 26, TextSecondary, 14f, FontStyle.Normal, 70f);

            menu.feedbackPill = Rounded(p, "Feedback", new Vector2(0f, -76f), new Vector2(ContentW, 68f), new Color(1f, 1f, 1f, 0.12f), 0.45f);
            menu.feedbackDot = MakeGraphic<Image>(menu.feedbackPill.transform, "Dot", new Vector2(-ContentW * 0.5f + 34f, 0f), new Vector2(18f, 18f));
            menu.feedbackDot.sprite = CircleSprite;
            menu.feedbackText = Label(menu.feedbackPill.transform, "Text", "", 27, Accent, new Vector2(30f, 0f), new Vector2(ContentW - 100f, 60f), TextAnchor.MiddleLeft, FontStyle.Bold);

            menu.restartButton = MakeButton(p, "Restart_Button", VRTrainingText.Restart, new Vector2(352f - 80f, -186f), new Vector2(160f, 52f), false);
            // Left of Restart: hear the current instruction (and see its demonstration) again.
            menu.repeatButton = MakeButton(p, "Repeat_Button", VRTrainingText.Repeat, new Vector2(352f - 160f - 12f - 100f, -186f), new Vector2(200f, 52f), false);
        }

        static void BuildComplete(Transform root, VRTrainingMenu menu)
        {
            Transform p = (menu.completePanel = Panel(root, "Complete_Panel")).transform;

            // Green badge with a check mark made of two bars.
            Image badge = MakeGraphic<Image>(p, "Badge", new Vector2(0f, 142f), new Vector2(92f, 92f));
            badge.sprite = CircleSprite;
            badge.color = Success;
            Image a = MakeGraphic<Image>(badge.transform, "Check_Short", new Vector2(-15f, -5f), new Vector2(27f, 10f));
            a.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            Image b = MakeGraphic<Image>(badge.transform, "Check_Long", new Vector2(6f, 2f), new Vector2(47f, 10f));
            b.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            a.color = b.color = PanelColor;

            Label(p, "Heading", VRTrainingText.CompleteTitle, 44, TextPrimary, new Vector2(0f, 62f), new Vector2(ContentW, 60f), TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(p, "Body", VRTrainingText.CompleteBody, 25, TextSecondary, new Vector2(0f, 14f), new Vector2(ContentW, 40f), TextAnchor.MiddleCenter);
            Image stats = Rounded(p, "Stats", new Vector2(0f, -66f), new Vector2(520f, 72f), CardColor, 0.5f);
            menu.resultText = Label(stats.transform, "Result", "", 28, TextPrimary, Vector2.zero, new Vector2(500f, 64f), TextAnchor.MiddleCenter, FontStyle.Bold);
            menu.resultText.supportRichText = true;

            menu.trainAgainButton = MakeButton(p, "Train_Again_Button", VRTrainingText.TrainAgain, new Vector2(-150f, -178f), new Vector2(260f, 64f), false);
            menu.modulesButton = MakeButton(p, "Modules_Button", VRTrainingText.BackToModules, new Vector2(150f, -178f), new Vector2(280f, 64f), true);
        }

        // A module card: the whole card is the button.
        static Button Card(Transform parent, string name, Vector2 pos, bool available, string number, string title, string body, string status, out Text statusText)
        {
            Vector2 size = new Vector2(340f, 240f);
            Image img = Rounded(parent, name, pos, size, Color.white, 0.4f);
            img.raycastTarget = true; // the controller ray must land on the card
            Button btn = img.gameObject.AddComponent<Button>();
            ColorBlock c = btn.colors;
            c.normalColor = available ? CardColor : CardColor * 0.85f + new Color(0f, 0f, 0f, 0.15f);
            c.highlightedColor = available ? new Color(0.16f, 0.21f, 0.25f) : new Color(0.15f, 0.17f, 0.2f);
            c.selectedColor = c.normalColor;
            c.pressedColor = new Color(0.09f, 0.12f, 0.15f);
            c.fadeDuration = 0.08f;
            btn.colors = c;
            btn.targetGraphic = img;

            Color numberColor = available ? Accent : TextSecondary * 0.8f;
            Color titleColor = available ? TextPrimary : TextSecondary;
            Rounded(img.transform, "Accent", new Vector2(0f, size.y * 0.5f - 2f), new Vector2(size.x - 40f, 3f), available ? Accent : TextSecondary * 0.5f, 2f);
            Label(img.transform, "Number", number, 54, numberColor, new Vector2(0f, 68f), new Vector2(292f, 70f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(img.transform, "Title", title, 28, titleColor, new Vector2(0f, 12f), new Vector2(292f, 40f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(img.transform, "Body", body, 19, TextSecondary, new Vector2(0f, -36f), new Vector2(292f, 54f), TextAnchor.UpperLeft);
            Pill(img.transform, "Status", status, available ? Accent : Warning, new Vector2(-146f, -88f), out statusText, available ? 264f : 0f);
            return btn;
        }

        // Coloured status pill, left edge at 'leftCentre.x'.
        static Image Pill(Transform parent, string name, string text, Color color, Vector2 leftCentre, out Text label, float width = 0f)
        {
            float w = width > 0f ? width : 22f + text.Length * 11f;
            Image pill = Rounded(parent, name, new Vector2(leftCentre.x + w * 0.5f, leftCentre.y), new Vector2(w, 34f), new Color(color.r, color.g, color.b, 0.16f), 0.7f);
            label = Label(pill.transform, "Text", text, 16, color, Vector2.zero, new Vector2(w, 34f), TextAnchor.MiddleCenter, FontStyle.Bold);
            return pill;
        }

        static GameObject Panel(Transform parent, string name)
        {
            GameObject p = new GameObject(name, typeof(RectTransform));
            p.layer = parent.gameObject.layer;
            p.transform.SetParent(parent, false);
            ((RectTransform)p.transform).sizeDelta = new Vector2(PanelW, PanelH);
            p.AddComponent<CanvasGroup>(); // fade-in
            return p;
        }

        static T MakeGraphic<T>(Transform parent, string name, Vector2 pos, Vector2 size) where T : Graphic
        {
            T g = Make<T>(parent, name, pos, size);
            g.raycastTarget = false;
            return g;
        }

        // 9-sliced rounded rectangle; a smaller pixelsPerUnitMultiplier gives rounder corners.
        static Image Rounded(Transform parent, string name, Vector2 pos, Vector2 size, Color color, float cornerScale)
        {
            Image img = Make<Image>(parent, name, pos, size);
            img.sprite = RoundedSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = cornerScale;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static T Make<T>(Transform parent, string name, Vector2 pos, Vector2 size) where T : Component
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return go.AddComponent<T>();
        }

        // Left-aligned line across the content width.
        static Text Left(Transform parent, string name, string text, int size, Color color, float y, FontStyle style, float height = 34f)
        {
            return Label(parent, name, text, size, color, new Vector2(0f, y), new Vector2(ContentW, height), TextAnchor.MiddleLeft, style);
        }

        static Text Label(Transform parent, string name, string text, int size, Color color, Vector2 pos, Vector2 box, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            Text t = Make<Text>(parent, name, pos, box);
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.lineSpacing = 1.1f;
            t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, bool primary)
        {
            Image img = Rounded(parent, name, pos, size, Color.white, 0.5f); // tinted by the button colours below
            img.raycastTarget = true;
            Button b = img.gameObject.AddComponent<Button>();
            Color baseColor = primary ? PrimaryButton : SecondaryButton;
            ColorBlock c = b.colors;
            c.normalColor = baseColor;
            c.highlightedColor = baseColor + new Color(0.08f, 0.1f, 0.1f, 0f);
            c.selectedColor = baseColor;
            c.pressedColor = baseColor * 0.7f + new Color(0f, 0f, 0f, 0.3f);
            c.fadeDuration = 0.08f;
            b.colors = c;
            b.targetGraphic = img;
            Label(img.transform, "Label", label, 24, primary ? Color.white : TextPrimary, Vector2.zero, size, TextAnchor.MiddleCenter, FontStyle.Bold);
            return b;
        }

        // ---------- helpers ----------

        static ActionBasedController FindController(XROrigin origin, GameObject modalityObject, string name)
        {
            if (modalityObject != null)
            {
                ActionBasedController fromModality = modalityObject.GetComponentInChildren<ActionBasedController>(true);
                if (fromModality != null) return fromModality;
            }
            foreach (ActionBasedController c in origin.GetComponentsInChildren<ActionBasedController>(true))
            {
                if (c.name == name) return c;
            }
            Debug.LogWarning($"[VR Armory] {name} not found in the XR Origin.");
            return null;
        }

        // Attach point at the pistol grip, in frame space: centred on the grip panels and moved up towards the web of
        // the hand, facing the muzzle (backstrap -> frontstrap), up towards the hammer. Axes are snapped to the nearest
        // frame axis, since the frame is modelled axis-aligned.
        static Transform GetOrCreateGripAttach(Transform frame)
        {
            const string attachName = "VR_Grip_Attach";
            Transform attach = frame.Find(attachName);
            if (attach == null)
            {
                attach = new GameObject(attachName).transform;
                attach.SetParent(frame, false);
            }

            Bounds grip = default;
            bool any = false, hasFront = false, hasBack = false, hasHammer = false;
            Vector3 front = Vector3.zero, back = Vector3.zero, hammer = Vector3.zero;
            foreach (Renderer r in frame.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                Vector3 c = frame.InverseTransformPoint(r.bounds.center);
                if (n.StartsWith("Grip_"))
                {
                    if (!any)
                    {
                        grip = new Bounds(c, Vector3.zero);
                        any = true;
                    }
                    grip.Encapsulate(frame.InverseTransformPoint(r.bounds.min));
                    grip.Encapsulate(frame.InverseTransformPoint(r.bounds.max));
                }
                if (n == "Grip_Frontstrap_Fill") { front = c; hasFront = true; }
                else if (n == "Grip_Backstrap_Fill") { back = c; hasBack = true; }
                else if (n == "Hammer") { hammer = c; hasHammer = true; }
            }
            if (!any || !hasFront || !hasBack || !hasHammer)
            {
                Debug.LogWarning("[VR Armory] Grip pieces not found on the frame; the pistol will be held at its pivot.");
                attach.localPosition = Vector3.zero;
                attach.localRotation = Quaternion.identity;
                return attach;
            }

            Vector3 up = Cardinal(hammer - grip.center);
            Vector3 forward = front - back;
            forward -= Vector3.Dot(forward, up) * up;
            forward = Cardinal(forward);
            float height = Vector3.Dot(grip.size, new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z)));
            attach.localPosition = grip.center + up * (height * 0.2f);
            attach.localRotation = Quaternion.LookRotation(forward, up);
            EditorUtility.SetDirty(attach);
            return attach;
        }

        static Vector3 Cardinal(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        static Light[] FindLights(params string[] names)
        {
            var lights = new List<Light>();
            foreach (Light l in Object.FindObjectsOfType<Light>(true))
            {
                if (System.Array.IndexOf(names, l.name) >= 0) lights.Add(l);
            }
            return lights.ToArray();
        }

        static GameObject FindOrCreate(Transform parent, string name)
        {
            if (parent == null)
            {
                foreach (GameObject g in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (g.name == name) return g;
                }
                GameObject created = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(created, "VR Setup");
                return created;
            }
            Transform t = parent.Find(name);
            if (t != null) return t.gameObject;
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        // Explicit null check: GetComponent returns a fake-null object in the editor, which defeats '??'.
        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
#endif
