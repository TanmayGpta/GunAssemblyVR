#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.XR.Interaction.Toolkit.UI;
using static VRArmory.Editor.VRSceneSetup;

namespace VRArmory.Editor
{
    // Builds Assets/Scenes/Intro.unity from scratch: a dark briefing stage, Sgt. Forrest Gump (Mixamo soldier) on a
    // lit platform, the dialog box (subtitles, Skip / Replay / Start) in front of him, the controller panel to the
    // right, the same XR rig as the training scene (standing still: no walking or turning here) and a desktop camera.
    // The scene is generated: running this again rebuilds it, so change layout here, not by hand in the scene.
    // Also puts the Intro first in Build Settings, before SampleScene.
    //   Tools > VR Armory > VR > 4. Setup Intro Scene
    public static class IntroSceneSetup
    {
        const string MaterialFolder = "Assets/Materials/Intro";
        const string ControllerAnimatorPath = SoldierImportSettings.Folder + "/Soldier_Intro.controller";
        
        // Layout, in metres. The trainee stands at the origin facing +Z, eyes at about 1.6 m.
        static readonly Vector3 Eye = new Vector3(0f, 1.6f, 0f);
        static readonly Vector3 SoldierPos = new Vector3(-0.55f, 0f, 2.1f);
        const float SoldierHeight = 1.8f;
        static readonly Vector3 DialogPos = new Vector3(-0.55f, 1.06f, 1.4f);
        static readonly Vector3 ControlsPos = new Vector3(0.6f, 1.36f, 1.25f);
        const float DialogW = 1000f, DialogH = 330f;   // canvas units = mm
        const float ControlsW = 900f, ControlsH = 620f;

        // Quest Touch Plus models on the panel, seen from above like a controller diagram: forward = the controller's
        // pointing direction, tilted so its top (sticks, buttons) faces the trainee. Size = longest side, in m.
        const float ControllerSize = 0.2f;
        static readonly Vector3 LeftModelPos = new Vector3(-0.19f, 0.045f, -0.08f);
        static readonly Vector3 RightModelPos = new Vector3(0.19f, 0.045f, -0.08f);
        const float ModelPitch = 55f;  // 0 = lying flat pointing away, 90 = top facing the trainee straight on
        const float ModelYaw = 10f;    // tips turned slightly inward

        static readonly Color Backdrop = new Color(0.03f, 0.035f, 0.045f);

        [MenuItem("Tools/VR Armory/VR/4. Setup Intro Scene", priority = 4)]
        public static void SetupFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        // Command line: -executeMethod VRArmory.Editor.IntroSceneSetup.SetupBatch
        public static void SetupBatch() => Build();

        static void Build()
        {
            GameObject setupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VRProjectSetup.HandsSetupPrefabPath);
            if (setupPrefab == null)
            {
                Debug.LogError("[VR Armory] Intro setup needs the XRI Hands Interaction Demo sample (Tools > VR Armory > VR > Import Hand Tracking Samples).");
                return;
            }
            // Soldier files imported before SoldierImportSettings existed are still Generic: reimport them as Humanoid.
            foreach (string path in new[] { SoldierImportSettings.ModelPath, SoldierImportSettings.IdlePath, SoldierImportSettings.TalkingPath, SoldierImportSettings.SalutePath })
            {
                if (SoldierImportSettings.NeedsReimport(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            SoldierImportSettings.ExtractTextures();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildLighting();
            BuildStage();

            // --- Players: VR rig (saved inactive) and desktop camera; VRPlatformSwitch keeps one of them. ---
            GameObject root = new GameObject("Intro");
            GameObject vrContent = new GameObject("VR_Content");
            vrContent.transform.SetParent(root.transform, false);
            XRInteractionManager interactionManager = BuildRig(setupPrefab, vrContent.transform);
            GameObject desktop = BuildDesktopPlayer(root.transform);

            VRPlatformSwitch platformSwitch = root.AddComponent<VRPlatformSwitch>();
            platformSwitch.desktopPlayer = desktop;
            platformSwitch.vrContent = vrContent;
            platformSwitch.interactionManager = interactionManager;
            platformSwitch.partGrabs = new VRAssemblyGrab[0];

            // --- Soldier, panels, voice, fade, director ---
            Animator soldier = BuildSoldier(root.transform, SoldierPos, Vector3.zero, BuildAnimatorController());
            IntroDirector director = new GameObject("Intro_Director").AddComponent<IntroDirector>();
            director.transform.SetParent(root.transform, false);
            Canvas dialog = BuildDialog(root.transform, director);
            ControllerGuidePanel controls = BuildControls(root.transform, out Canvas controlsCanvas);

            VRVoiceGuide voice = director.gameObject.AddComponent<VRVoiceGuide>(); // adds its AudioSource
            director.GetComponent<AudioSource>().playOnAwake = false;
            voice.clips = VRVoiceSetup.LoadClips(out List<VoiceLine> missing);
            missing.RemoveAll(l => !l.ToString().StartsWith("Sgt_"));
            if (missing.Count > 0)
                Debug.LogWarning($"[VR Armory] {missing.Count} briefing line(s) have no recording yet (they play silently): {string.Join(", ", missing)}. Export Voice Script, then run generate_voice.py.");

            SceneFader fader = director.gameObject.AddComponent<SceneFader>();
            fader.fadeMaterial = GetOrCreateGuideMaterial();

            director.voice = voice;
            director.soldier = soldier;
            director.controls = controls;
            director.fader = fader;
            director.canvases = new[] { dialog, controlsCanvas };
            director.nextScene = System.IO.Path.GetFileNameWithoutExtension(VRProjectSetup.ScenePath);
            director.steps = new[]
            {
                Step(VoiceLine.Sgt_Welcome, ControllerGuidePanel.Control.None, VRTrainingText.SectionBriefing),
                Step(VoiceLine.Sgt_WhyItMatters, ControllerGuidePanel.Control.None, VRTrainingText.SectionBriefing),
                Step(VoiceLine.Sgt_Simulator, ControllerGuidePanel.Control.None, VRTrainingText.SectionBriefing),
                Step(VoiceLine.Sgt_ControlsIntro, ControllerGuidePanel.Control.None, VRTrainingText.SectionControls),
                Step(VoiceLine.Sgt_Grip, ControllerGuidePanel.Control.Grip, VRTrainingText.SectionControls),
                Step(VoiceLine.Sgt_Trigger, ControllerGuidePanel.Control.Trigger, VRTrainingText.SectionControls),
                Step(VoiceLine.Sgt_Thumbsticks, ControllerGuidePanel.Control.Thumbstick, VRTrainingText.SectionControls),
                Step(VoiceLine.Sgt_PrimaryButton, ControllerGuidePanel.Control.Primary, VRTrainingText.SectionControls),
                Step(VoiceLine.Sgt_SecondaryButton, ControllerGuidePanel.Control.Secondary, VRTrainingText.SectionControls),
                Step(VoiceLine.Sgt_Ready, ControllerGuidePanel.Control.None, VRTrainingText.SectionReady),
            };

            vrContent.SetActive(false); // VRPlatformSwitch turns it on when a headset is running
            EditorSceneManager.SaveScene(scene, VRProjectSetup.IntroScenePath);
            EditorBuildSettings.scenes = System.Array.ConvertAll(VRProjectSetup.BuildScenes, p => new EditorBuildSettingsScene(p, true));
            Debug.Log($"[VR Armory] Intro scene built: {VRProjectSetup.IntroScenePath} (build index 0, then {VRProjectSetup.ScenePath}). "
                      + (soldier != null ? "Soldier ready." : "No soldier model: see .gitignore for the file it needs."));
        }

        static IntroDirector.Step Step(VoiceLine line, ControllerGuidePanel.Control c, string section) =>
            new IntroDirector.Step { line = line, highlight = c, section = section };

        // ---------- Stage ----------

        static void BuildLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.37f, 0.4f); // a lit room, not a void
            RenderSettings.fog = false;

            Light key = new GameObject("Key_Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            key.intensity = 0.5f;
            key.color = new Color(1f, 0.96f, 0.9f);
            key.shadows = LightShadows.None;

            Light spot = new GameObject("Soldier_Spot").AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.transform.position = SoldierPos + new Vector3(0.9f, 3.3f, -1.7f);
            spot.transform.LookAt(SoldierPos + Vector3.up * 1.15f);
            spot.range = 7f;
            spot.spotAngle = 42f;
            spot.intensity = 2.6f;
            spot.color = new Color(1f, 0.93f, 0.82f);
            spot.shadows = LightShadows.None;

            Light rim = new GameObject("Soldier_Rim").AddComponent<Light>();
            rim.type = LightType.Point;
            rim.transform.position = SoldierPos + new Vector3(0.1f, 2.1f, 0.9f);
            rim.range = 3.5f;
            rim.intensity = 1.3f;
            rim.color = Accent;
            rim.shadows = LightShadows.None;
        }

        // Briefing room around the trainee (origin, facing +Z): X -RoomHalfW..RoomHalfW, Z RoomBack..RoomFront.
        const float RoomHalfW = 4.5f, RoomBack = -3.5f, RoomFront = 4.5f, RoomH = 4f;

        static void BuildStage()
        {
            Transform stage = new GameObject("Stage").transform;
            Material floor = Mat("Intro_Floor", "Standard", new Color(0.2f, 0.21f, 0.23f), 0.35f);
            Material joint = Mat("Intro_Floor_Joint", "Standard", new Color(0.11f, 0.115f, 0.125f), 0.1f);
            Material wall = Mat("Intro_Wall", "Standard", new Color(0.31f, 0.33f, 0.36f), 0.15f);
            Material lower = Mat("Intro_Wall_Lower", "Standard", new Color(0.13f, 0.14f, 0.16f), 0.3f);
            Material trim = Mat("Intro_Trim", "Standard", new Color(0.08f, 0.085f, 0.095f), 0.5f);
            Material ceiling = Mat("Intro_Ceiling", "Standard", new Color(0.22f, 0.23f, 0.25f), 0.05f);
            Material lamp = Mat("Intro_Lamp", "Unlit/Color", new Color(0.93f, 0.96f, 1f), 0f);
            Material locker = Mat("Intro_Locker", "Standard", new Color(0.23f, 0.28f, 0.26f), 0.55f);
            Material board = Mat("Intro_Board", "Standard", new Color(0.05f, 0.06f, 0.07f), 0.6f);
            Material wood = Mat("Intro_Bench", "Standard", new Color(0.33f, 0.24f, 0.16f), 0.3f);
            Material exitSign = Mat("Intro_Exit_Sign", "Unlit/Color", new Color(0.2f, 0.85f, 0.4f), 0f);
            Material plinth = Mat("Intro_Platform", "Standard", new Color(0.12f, 0.13f, 0.15f), 0.45f);
            Material accent = Mat("Intro_Accent", "Unlit/Color", Accent, 0f);

            float w = RoomHalfW * 2f, d = RoomFront - RoomBack, cz = (RoomFront + RoomBack) * 0.5f;

            // Floor: concrete slab with tile joints every 1.5 m.
            Shape(stage, PrimitiveType.Cube, "Floor", new Vector3(0f, -0.05f, cz), new Vector3(w, 0.1f, d), floor);
            for (float x = -RoomHalfW + 1.5f; x < RoomHalfW - 0.01f; x += 1.5f)
                Shape(stage, PrimitiveType.Cube, "Floor_Joint", new Vector3(x, 0.001f, cz), new Vector3(0.015f, 0.002f, d), joint);
            for (float z = RoomBack + 1.5f; z < RoomFront - 0.01f; z += 1.5f)
                Shape(stage, PrimitiveType.Cube, "Floor_Joint", new Vector3(0f, 0.001f, z), new Vector3(w, 0.002f, 0.015f), joint);

            // Four walls (the front one, behind the soldier, has no pilasters so the title stays clear).
            Wall(stage, "Front_Wall", new Vector3(0f, 0f, RoomFront), Vector3.back, w, false, wall, lower, trim, accent);
            Wall(stage, "Back_Wall", new Vector3(0f, 0f, RoomBack), Vector3.forward, w, true, wall, lower, trim, accent);
            Wall(stage, "Left_Wall", new Vector3(-RoomHalfW, 0f, cz), Vector3.right, d, true, wall, lower, trim, accent);
            Wall(stage, "Right_Wall", new Vector3(RoomHalfW, 0f, cz), Vector3.left, d, true, wall, lower, trim, accent);

            // Ceiling with light panels, and two lamps that actually light the room.
            Shape(stage, PrimitiveType.Cube, "Ceiling", new Vector3(0f, RoomH + 0.05f, cz), new Vector3(w, 0.1f, d), ceiling);
            foreach (float x in new[] { -2.4f, 0f, 2.4f })
                foreach (float z in new[] { -2f, 0.6f, 3.1f })
                {
                    Shape(stage, PrimitiveType.Cube, "Ceiling_Panel_Frame", new Vector3(x, RoomH - 0.006f, z), new Vector3(1.3f, 0.012f, 0.7f), trim);
                    Shape(stage, PrimitiveType.Cube, "Ceiling_Panel", new Vector3(x, RoomH - 0.014f, z), new Vector3(1.2f, 0.01f, 0.6f), lamp);
                }
            foreach (float z in new[] { -1f, 2.6f })
            {
                Light l = new GameObject("Ceiling_Light").AddComponent<Light>();
                l.transform.SetParent(stage, false);
                l.type = LightType.Point;
                l.transform.position = new Vector3(0f, RoomH - 0.4f, z);
                l.range = 7f;
                l.intensity = 0.7f;
                l.color = new Color(0.92f, 0.95f, 1f);
                l.shadows = LightShadows.None;
            }

            // Right wall: a row of gear lockers.
            Transform lockers = new GameObject("Lockers").transform;
            lockers.SetParent(stage, false);
            for (int i = 0; i < 6; i++)
            {
                float z = -1.2f + i * 0.62f;
                float x = RoomHalfW - 0.06f - 0.25f;
                Shape(lockers, PrimitiveType.Cube, "Locker", new Vector3(x, 0.95f, z), new Vector3(0.5f, 1.9f, 0.6f), locker);
                Shape(lockers, PrimitiveType.Cube, "Locker_Seam", new Vector3(x - 0.251f, 0.95f, z + 0.305f), new Vector3(0.004f, 1.9f, 0.012f), trim);
                Shape(lockers, PrimitiveType.Cube, "Locker_Handle", new Vector3(x - 0.265f, 1.05f, z + 0.2f), new Vector3(0.025f, 0.14f, 0.02f), trim);
                for (int v = 0; v < 3; v++)
                    Shape(lockers, PrimitiveType.Cube, "Locker_Vent", new Vector3(x - 0.251f, 1.65f + v * 0.04f, z), new Vector3(0.004f, 0.012f, 0.32f), trim);
            }

            // Left wall: briefing board with an accent frame, and a bench under it.
            float lx = -RoomHalfW + 0.06f;
            Shape(stage, PrimitiveType.Cube, "Board_Frame", new Vector3(lx + 0.02f, 1.75f, 0.5f), new Vector3(0.03f, 1.1f, 2.1f), accent);
            Shape(stage, PrimitiveType.Cube, "Board", new Vector3(lx + 0.04f, 1.75f, 0.5f), new Vector3(0.03f, 1.04f, 2.04f), board);
            Shape(stage, PrimitiveType.Cube, "Bench_Seat", new Vector3(lx + 0.25f, 0.45f, 0.5f), new Vector3(0.4f, 0.05f, 1.8f), wood);
            foreach (float z in new[] { -0.25f, 1.25f })
                Shape(stage, PrimitiveType.Cube, "Bench_Leg", new Vector3(lx + 0.25f, 0.21f, z), new Vector3(0.34f, 0.42f, 0.06f), trim);

            // Back wall (behind the trainee): the door out, with an exit sign.
            float bz = RoomBack + 0.06f;
            Shape(stage, PrimitiveType.Cube, "Door_Frame", new Vector3(3.3f, 1.12f, bz + 0.02f), new Vector3(1.2f, 2.24f, 0.04f), trim);
            Shape(stage, PrimitiveType.Cube, "Door", new Vector3(3.3f, 1.07f, bz + 0.04f), new Vector3(1.0f, 2.1f, 0.04f), locker);
            Shape(stage, PrimitiveType.Cube, "Door_Handle", new Vector3(2.95f, 1.05f, bz + 0.075f), new Vector3(0.14f, 0.025f, 0.03f), trim);
            Shape(stage, PrimitiveType.Cube, "Exit_Sign", new Vector3(3.3f, 2.45f, bz + 0.04f), new Vector3(0.4f, 0.14f, 0.04f), exitSign);

            Shape(stage, PrimitiveType.Cylinder, "Platform", SoldierPos + Vector3.up * 0.015f, new Vector3(1.2f, 0.015f, 1.2f), plinth);
            Shape(stage, PrimitiveType.Cylinder, "Platform_Rim", SoldierPos + Vector3.up * 0.01f, new Vector3(1.27f, 0.01f, 1.27f), accent);

            // Title on the front wall, behind the soldier.
            GameObject title = new GameObject("Wall_Title", typeof(RectTransform));
            title.transform.SetParent(stage, false);
            title.transform.SetPositionAndRotation(new Vector3(0f, 3.0f, RoomFront - 0.07f), Quaternion.identity);
            title.transform.localScale = Vector3.one * 0.01f; // 1 canvas unit = 1 cm
            ((RectTransform)title.transform).sizeDelta = new Vector2(800f, 160f);
            title.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            title.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            Label(title.transform, "Brand", VRTrainingText.Brand, 90, new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, 0.9f), new Vector2(0f, 25f), new Vector2(800f, 110f), TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(title.transform, "Tagline", "WEAPON HANDLING TRAINING", 28, Accent, new Vector2(0f, -50f), new Vector2(800f, 40f), TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        // One wall: 'face' = the middle of its inner face at floor level, 'inward' = toward the room. Darker lower panel
        // with a trim rail and an accent line on top, baseboard, crown, and (optionally) pilasters every 2.25 m.
        static void Wall(Transform parent, string name, Vector3 face, Vector3 inward, float length, bool pilasters,
                         Material wall, Material lower, Material trim, Material accent)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(face, Quaternion.LookRotation(-inward)); // local +Z = out of the room, X = along it
            Shape(t, PrimitiveType.Cube, "Wall", new Vector3(0f, RoomH * 0.5f, 0.1f), new Vector3(length, RoomH, 0.2f), wall);
            Shape(t, PrimitiveType.Cube, "Lower_Panel", new Vector3(0f, 0.55f, -0.015f), new Vector3(length, 1.1f, 0.03f), lower);
            Shape(t, PrimitiveType.Cube, "Rail", new Vector3(0f, 1.12f, -0.035f), new Vector3(length, 0.04f, 0.04f), trim);
            Shape(t, PrimitiveType.Cube, "Accent_Line", new Vector3(0f, 1.15f, -0.05f), new Vector3(length, 0.012f, 0.01f), accent);
            Shape(t, PrimitiveType.Cube, "Baseboard", new Vector3(0f, 0.06f, -0.04f), new Vector3(length, 0.12f, 0.02f), trim);
            Shape(t, PrimitiveType.Cube, "Crown", new Vector3(0f, RoomH - 0.04f, -0.03f), new Vector3(length, 0.08f, 0.06f), trim);
            if (!pilasters) return;
            int n = Mathf.FloorToInt(length / 2.25f);
            for (int i = 1; i < n; i++)
            {
                float x = -length * 0.5f + i * (length / n);
                Shape(t, PrimitiveType.Cube, "Pilaster", new Vector3(x, RoomH * 0.5f, -0.05f), new Vector3(0.22f, RoomH, 0.1f), lower);
            }
        }

        static GameObject Shape(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static Material Mat(string name, string shader, Color color, float smoothness, bool emissive = false)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Intro");
            string path = $"{MaterialFolder}/{name}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find(shader);
            m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (emissive)
            {
                // Emission on (driven per part by ControllerGuidePanel); a tiny non-black value keeps the keyword enabled.
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.001f, 0.001f, 0.001f));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- Players ----------

        static XRInteractionManager BuildRig(GameObject setupPrefab, Transform parent)
        {
            Transform setupT = ((GameObject)PrefabUtility.InstantiatePrefab(setupPrefab, parent)).transform;
            XRInteractionManager interactionManager = setupT.GetComponentInChildren<XRInteractionManager>(true);
            XROrigin origin = setupT.GetComponentInChildren<XROrigin>(true);
            origin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.RecordPrefabInstancePropertyModifications(origin.transform);
            var originSo = new SerializedObject(origin);
            originSo.FindProperty("m_RequestedTrackingOriginMode").intValue = (int)XROrigin.TrackingOriginMode.Floor;
            originSo.ApplyModifiedProperties();

            Camera cam = origin.Camera;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Backdrop;
                PrefabUtility.RecordPrefabInstancePropertyModifications(cam);
            }

            // Standing still for the briefing: no teleport ray, and the move / turn providers are off.
            foreach (ActionBasedControllerManager cm in setupT.GetComponentsInChildren<ActionBasedControllerManager>(true))
            {
                var so = new SerializedObject(cm);
                so.FindProperty("m_SmoothMotionEnabled").boolValue = true;
                so.FindProperty("m_SmoothTurnEnabled").boolValue = true;
                so.FindProperty("m_TeleportModeActivate").objectReferenceValue = null;
                so.FindProperty("m_TeleportModeCancel").objectReferenceValue = null;
                so.ApplyModifiedProperties();
            }
            foreach (LocomotionProvider p in setupT.GetComponentsInChildren<LocomotionProvider>(true))
            {
                p.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(p);
            }
            foreach (TunnelingVignetteController vignette in setupT.GetComponentsInChildren<TunnelingVignetteController>(true))
            {
                vignette.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(vignette.gameObject);
            }

            // Same hand fixes as the training scene: one hand mesh per hand, on the Standard-shader material.
            foreach (Transform t in origin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Hand Visualizer") continue;
                t.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
            }
            Material handMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Samples/XR Hands/1.4.1/HandVisualizer/Materials/HandsDefaultMaterial.mat");
            XRInputModalityManager modality = setupT.GetComponentInChildren<XRInputModalityManager>(true);
            if (handMaterial != null && modality != null)
            {
                foreach (GameObject hand in new[] { modality.leftHand, modality.rightHand })
                {
                    if (hand == null) continue;
                    foreach (SkinnedMeshRenderer smr in hand.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        smr.sharedMaterial = handMaterial;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
                    }
                }
            }
            foreach (XRRayInteractor ray in setupT.GetComponentsInChildren<XRRayInteractor>(true))
            {
                if (ray.GetComponent<XRInteractorLineVisual>() != null) GetOrAdd<VRRayHoverDimmer>(ray.gameObject);
            }
            return interactionManager;
        }

        static GameObject BuildDesktopPlayer(Transform parent)
        {
            GameObject desktop = new GameObject("Desktop_Player");
            desktop.transform.SetParent(parent, false);

            GameObject camGo = new GameObject("Desktop_Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(desktop.transform, false);
            camGo.transform.SetPositionAndRotation(Eye, Quaternion.Euler(6f, 3f, 0f));
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Backdrop;
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();

            GameObject events = new GameObject("Desktop_EventSystem");
            events.transform.SetParent(desktop.transform, false);
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            return desktop;
        }

        // ---------- Soldier ----------

        // The Mixamo soldier standing at pos (feet on the floor at pos.y + 0.03), facing lookAt, SoldierHeight tall, with
        // a contact shadow. Null (with a warning) when the git-ignored model is missing. Also used for the range sergeant.
        // lift: feet this far above pos.y (the Intro platform's top).
        internal static Animator BuildSoldier(Transform parent, Vector3 pos, Vector3 lookAt, RuntimeAnimatorController controller, float lift = 0.03f)
        {
            Vector3 SoldierPos = pos;
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(SoldierImportSettings.ModelPath);
            if (model == null)
            {
                Debug.LogWarning($"[VR Armory] {SoldierImportSettings.ModelPath} not found: the Intro scene is built without the soldier (see .gitignore for the model it needs).");
                return null;
            }
            GameObject soldier = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            soldier.name = "Sgt_Forrest_Gump";
            Vector3 toPlayer = new Vector3(lookAt.x - SoldierPos.x, 0f, lookAt.z - SoldierPos.z);
            soldier.transform.SetPositionAndRotation(SoldierPos + Vector3.up * lift, Quaternion.LookRotation(toPlayer));

            // Mixamo exports vary in scale: make him SoldierHeight tall, feet on the platform.
            // Bounds from the posed mesh's vertices: Renderer.bounds is stale right after instantiating (and a skinned
            // mesh's bounds box is padded), which left him floating.
            Bounds b = PosedMeshBounds(soldier);
            if (b.size.y > 0.01f)
            {
                soldier.transform.localScale *= SoldierHeight / b.size.y;
                b = PosedMeshBounds(soldier);
                soldier.transform.position += Vector3.up * (SoldierPos.y + lift - b.min.y);
            }
            foreach (SkinnedMeshRenderer smr in soldier.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.updateWhenOffscreen = false;
            }

            Animator animator = GetOrAdd<Animator>(soldier);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Soft contact shadow under his feet (no real-time shadows on Quest).
            GameObject shadow = new GameObject("Contact_Shadow", typeof(RectTransform));
            shadow.transform.SetParent(parent, false);
            shadow.transform.SetPositionAndRotation(SoldierPos + Vector3.up * (lift + 0.002f), Quaternion.Euler(90f, 0f, 0f));
            shadow.transform.localScale = Vector3.one * 0.001f;
            ((RectTransform)shadow.transform).sizeDelta = new Vector2(700f, 700f);
            shadow.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            Image disc = MakeGraphic<Image>(shadow.transform, "Disc", Vector2.zero, new Vector2(700f, 700f));
            disc.sprite = CircleSprite;
            disc.color = new Color(0f, 0f, 0f, 0.55f);
            return animator;
        }

        // World bounds of every mesh vertex in its current pose (skinned meshes baked).
        static Bounds PosedMeshBounds(GameObject go)
        {
            Bounds b = new Bounds();
            bool any = false;
            var baked = new Mesh();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Vector3[] verts;
                Matrix4x4 m;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    smr.BakeMesh(baked, true); // scale applied, so place it without the renderer's scale
                    verts = baked.vertices;
                    m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                }
                else if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                {
                    verts = mf.sharedMesh.vertices;
                    m = r.transform.localToWorldMatrix;
                }
                else continue;
                foreach (Vector3 v in verts)
                {
                    Vector3 p = m.MultiplyPoint3x4(v);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            Object.DestroyImmediate(baked);
            return any ? b : RendererBounds(go);
        }

        static Bounds RendererBounds(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // Idle <-> Talking on the "Talking" bool, "Salute" trigger plays the salute once, then back to Idle.
        // The soldier's animator (Idle / Talking / Salute) as saved by the Intro setup; built if it does not exist yet.
        internal static RuntimeAnimatorController SoldierController()
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerAnimatorPath);
            return c != null ? c : BuildAnimatorController();
        }

        static AnimatorController BuildAnimatorController()
        {
            AnimationClip idle = SoldierImportSettings.LoadClip(SoldierImportSettings.IdlePath);
            AnimationClip talking = SoldierImportSettings.LoadClip(SoldierImportSettings.TalkingPath);
            AnimationClip salute = SoldierImportSettings.LoadClip(SoldierImportSettings.SalutePath);

            AssetDatabase.DeleteAsset(ControllerAnimatorPath);
            AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(ControllerAnimatorPath);
            c.AddParameter("Talking", AnimatorControllerParameterType.Bool);
            c.AddParameter("Salute", AnimatorControllerParameterType.Trigger);
            AnimatorStateMachine sm = c.layers[0].stateMachine;

            AnimatorState idleState = sm.AddState("Idle", new Vector3(300f, 0f));
            idleState.motion = idle;
            AnimatorState talkState = sm.AddState("Talking", new Vector3(300f, 120f));
            talkState.motion = talking;
            AnimatorState saluteState = sm.AddState("Salute", new Vector3(600f, 60f));
            saluteState.motion = salute;
            sm.defaultState = idleState;

            Transition(idleState, talkState, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "Talking");
            Transition(talkState, idleState, 0.4f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Talking");
            Transition(idleState, saluteState, 0.25f).AddCondition(AnimatorConditionMode.If, 0f, "Salute");
            Transition(talkState, saluteState, 0.25f).AddCondition(AnimatorConditionMode.If, 0f, "Salute");
            AnimatorStateTransition back = Transition(saluteState, idleState, 0.4f);
            back.hasExitTime = true;
            back.exitTime = 0.92f;

            if (idle == null || talking == null || salute == null)
                Debug.LogWarning("[VR Armory] Soldier animation clip(s) missing in Assets/Characters (Idle.fbx, Talking.fbx, Salute.fbx).");
            AssetDatabase.SaveAssets();
            return c;
        }

        static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            return t;
        }

        // ---------- Panels ----------

        internal static GameObject WorldCanvas(Transform parent, string name, Vector2 size, bool interactive)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.001f; // 1 canvas unit = 1 mm
            ((RectTransform)go.transform).sizeDelta = size;
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            if (interactive)
            {
                go.AddComponent<TrackedDeviceGraphicRaycaster>(); // controller ray / hand poke
                go.AddComponent<GraphicRaycaster>();              // desktop mouse
            }
            Rounded(go.transform, "Background", Vector2.zero, size, PanelColor, 0.4f).raycastTarget = interactive;
            Rounded(go.transform, "Accent_Rule", new Vector2(0f, size.y * 0.5f - 3f), new Vector2(size.x - 48f, 3f), Accent, 2f);
            return go;
        }

        // Faces the trainee's eyes (canvas forward points away from the viewer).
        static Quaternion FacingEye(Vector3 pos) => Quaternion.LookRotation(pos - Eye);

        static Canvas BuildDialog(Transform parent, IntroDirector director)
        {
            GameObject go = WorldCanvas(parent, "Dialog_Panel", new Vector2(DialogW, DialogH), true);
            go.transform.SetPositionAndRotation(DialogPos, FacingEye(DialogPos));
            Transform p = go.transform;
            float cw = DialogW - 80f;

            Label(p, "Speaker", VRTrainingText.SergeantName, 22, Accent, new Vector2(0f, 128f), new Vector2(cw, 34f), TextAnchor.MiddleLeft, FontStyle.Bold);
            director.sectionText = Label(p, "Section", VRTrainingText.SectionBriefing, 18, TextSecondary, new Vector2(0f, 128f), new Vector2(cw, 34f), TextAnchor.MiddleRight, FontStyle.Bold);
            director.subtitleText = Label(p, "Subtitle", VRTrainingText.IntroWaiting, 27, TextPrimary, new Vector2(0f, 18f), new Vector2(cw, 170f), TextAnchor.UpperLeft);

            float right = DialogW * 0.5f - 40f;
            director.skipButton = MakeButton(p, "Skip_Button", VRTrainingText.Skip, new Vector2(right - 90f, -122f), new Vector2(180f, 58f), false);
            director.nextButton = MakeButton(p, "Next_Button", VRTrainingText.Next, new Vector2(right - 180f - 16f - 90f, -122f), new Vector2(180f, 58f), true);
            director.startButton = MakeButton(p, "Start_Button", VRTrainingText.StartGame, new Vector2(right - 120f, -122f), new Vector2(240f, 58f), true);
            director.replayButton = MakeButton(p, "Replay_Button", VRTrainingText.Replay, new Vector2(right - 240f - 16f - 90f, -122f), new Vector2(180f, 58f), false);
            director.startButton.gameObject.SetActive(false);
            director.replayButton.gameObject.SetActive(false);
            return go.GetComponent<Canvas>();
        }

        static ControllerGuidePanel BuildControls(Transform parent, out Canvas canvas)
        {
            Transform root = new GameObject("Controls_Panel").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(ControlsPos, FacingEye(ControlsPos));

            GameObject go = WorldCanvas(root, "Canvas", new Vector2(ControlsW, ControlsH), false);
            canvas = go.GetComponent<Canvas>();
            Transform p = go.transform;
            float cw = ControlsW - 80f;
            Label(p, "Label", VRTrainingText.ControlsLabel, 18, Accent, new Vector2(0f, 268f), new Vector2(cw, 30f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(p, "Title", VRTrainingText.ControlsTitle, 34, TextPrimary, new Vector2(0f, 230f), new Vector2(cw, 46f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(p, "Footer", VRTrainingText.ControlsFooter, 17, TextSecondary, new Vector2(0f, -272f), new Vector2(cw, 30f), TextAnchor.MiddleCenter);

            ControllerGuidePanel panel = root.gameObject.AddComponent<ControllerGuidePanel>();
            panel.highlightColor = Accent;
            panel.pressedColor = Success;
            panel.rowColor = CardColor;
            panel.chipColor = SecondaryButton;
            panel.chipTextColor = Accent;

            (ControllerGuidePanel.Control control, string chip, string use)[] rows =
            {
                (ControllerGuidePanel.Control.Grip, VRTrainingText.GripChip, VRTrainingText.GripRowUse),
                (ControllerGuidePanel.Control.Trigger, VRTrainingText.TriggerChip, VRTrainingText.TriggerRowUse),
                (ControllerGuidePanel.Control.Thumbstick, VRTrainingText.SticksChip, VRTrainingText.SticksRowUse),
                (ControllerGuidePanel.Control.Primary, VRTrainingText.PrimaryChip, VRTrainingText.PrimaryRowUse),
                (ControllerGuidePanel.Control.Secondary, VRTrainingText.SecondaryChip, VRTrainingText.SecondaryRowUse),
            };
            // One card per control along the bottom; the controllers fill the space above them.
            const float cardW = 160f, cardH = 116f, gap = 10f;
            panel.rows = new ControllerGuidePanel.Row[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                float x = (i - (rows.Length - 1) * 0.5f) * (cardW + gap);
                Image bg = Rounded(p, "Card_" + rows[i].chip, new Vector2(x, -172f), new Vector2(cardW, cardH), CardColor, 0.5f);
                Image chip = Rounded(bg.transform, "Chip", new Vector2(0f, 30f), new Vector2(cardW - 20f, 40f), SecondaryButton, 0.6f);
                Text chipText = Label(chip.transform, "Text", rows[i].chip, 18, Accent, Vector2.zero, new Vector2(cardW - 20f, 40f), TextAnchor.MiddleCenter, FontStyle.Bold);
                Label(bg.transform, "Use", rows[i].use, 16, TextPrimary, new Vector2(0f, -22f), new Vector2(cardW - 16f, 56f), TextAnchor.MiddleCenter);
                panel.rows[i] = new ControllerGuidePanel.Row { control = rows[i].control, background = bg, chip = chip, chipText = chipText };
            }

            panel.leftController = ControllerModel(root, true, LeftModelPos, ModelYaw);
            panel.rightController = ControllerModel(root, false, RightModelPos, -ModelYaw);

            return panel;
        }

        static MeshRenderer ControllerModel(Transform parent, bool left, Vector3 localPos, float yaw)
        {
            Mesh mesh = TouchPlusControllerModels.BuildPartsMesh(left, out Matrix4x4 meshToRoot);
            if (mesh == null)
            {
                Debug.LogWarning($"[VR Armory] {TouchPlusControllerModels.ModelPath(left)} not found: no controller model on the panel.");
                return null;
            }
            // Textured plastic with emission on (each control's glow is set per material slot by ControllerGuidePanel).
            Material m = Mat(left ? "Intro_TouchPlus_Left" : "Intro_TouchPlus_Right", "Standard", Color.white, 0.4f, true);
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TouchPlusControllerModels.BaseColorPath(left));
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TouchPlusControllerModels.NormalPath);
            m.SetTexture("_BumpMap", normal);
            if (normal != null) m.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(m);

            // Pivot = place and angle on the panel; the model inside keeps the FBX's own mesh orientation.
            Transform pivot = new GameObject(left ? "TouchPlus_Left" : "TouchPlus_Right").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = localPos;
            pivot.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-ModelPitch, 0f, 0f);

            GameObject model = new GameObject("Model");
            model.transform.SetParent(pivot, false);
            model.transform.localPosition = meshToRoot.GetColumn(3);
            model.transform.localRotation = meshToRoot.rotation;
            model.transform.localScale = meshToRoot.lossyScale;
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = model.AddComponent<MeshRenderer>();
            var mats = new Material[TouchPlusControllerModels.PartCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = m;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            Bounds b = r.bounds;
            float largest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (largest > 0.0001f) model.transform.localScale *= ControllerSize / largest;
            b = r.bounds;
            model.transform.position += pivot.position - b.center;
            return r;
        }
    }
}
#endif
