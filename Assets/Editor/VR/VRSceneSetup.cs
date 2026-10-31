#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
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
        const string SetupPrefabPath = "Assets/Samples/XR Interaction Toolkit/2.6.4/Starter Assets/Prefabs/XR Interaction Setup.prefab";
        const string MarkerMaterialPath = "Assets/Materials/VR/VR_Teleport_Marker.mat";
        const int TeleportLayer = 31; // the Starter Assets teleport interactor uses interaction layer 31

        // Where the desktop player stands: in front of the bench, facing downrange (+Z).
        static readonly Vector3 BenchStandPoint = new Vector3(0f, 0f, -0.95f);

        static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.08f, 0.94f);
        static readonly Color Amber = new Color(1.0f, 0.72f, 0.0f);
        static readonly Color Green = new Color(0.0f, 1.0f, 0.55f);
        static readonly Color BodyColor = new Color(0.88f, 0.92f, 0.96f);
        static readonly Color ButtonColor = new Color(0.0f, 0.5f, 0.28f, 0.95f);

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
                Debug.LogError("[VR Armory] VR setup needs Gun_Assembly_Manager, Armorer_Player and the XRI Starter Assets sample.");
                return false;
            }
            FunctionCheck functionCheck = manager.GetComponent<FunctionCheck>();

            GameObject root = FindOrCreate(null, RootName);
            Transform content = FindOrCreate(root.transform, ContentName).transform;
            content.gameObject.SetActive(true); // while building

            // --- XR rig: Unity's Starter Assets "XR Interaction Setup" (interaction manager, EventSystem, XR Origin) ---
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

            // Comfort: teleport + snap turn only (no smooth stick walking).
            // (Set through SerializedObject: the C# properties also enable input actions, which must not happen in edit mode.)
            foreach (ActionBasedControllerManager cm in setupT.GetComponentsInChildren<ActionBasedControllerManager>(true))
            {
                var so = new SerializedObject(cm);
                so.FindProperty("m_SmoothMotionEnabled").boolValue = false;
                so.FindProperty("m_SmoothTurnEnabled").boolValue = false;
                so.ApplyModifiedProperties();
            }
            ActionBasedController leftController = FindController(origin, "Left Controller");
            ActionBasedController rightController = FindController(origin, "Right Controller");
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

            // --- Locomotion targets ---
            BuildTeleportFloor(content);
            BuildBenchAnchor(content);

            // --- VR-only behaviour ---
            VRTrainingMenu menu = BuildMenu(content, manager, xrCamera);

            VRFunctionCheckControls fcControls = GetOrAdd<VRFunctionCheckControls>(FindOrCreate(content, "VR_Function_Check_Controls"));
            fcControls.functionCheck = functionCheck;
            fcControls.frameGrab = frameGrab;
            fcControls.interactionManager = interactionManager;
            fcControls.leftController = leftController;
            fcControls.rightController = rightController;

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

            foreach (Object o in new Object[] { menu, fcControls, perf, audio, platformSwitch }) EditorUtility.SetDirty(o);
            content.gameObject.SetActive(false); // desktop mode never sees the VR objects
            Debug.Log($"[VR Armory] VR scene setup done: {grabs.Count} grabbable parts, XR rig at {BenchStandPoint}, menu, teleport, function-check controls.");
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
            grab.useDynamicAttach = true;        // held where it was grabbed: no jump into the palm
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

        static void BuildTeleportFloor(Transform content)
        {
            // Invisible collider just above Room_Floor (10.3 x 11.3 m armory room, walls 0.3 m thick).
            GameObject floor = FindOrCreate(content, "Teleport_Floor");
            floor.transform.SetPositionAndRotation(new Vector3(0f, 0.005f, 2.5f), Quaternion.identity);
            BoxCollider box = GetOrAdd<BoxCollider>(floor);
            box.size = new Vector3(10.6f, 0.01f, 9.6f);
            TeleportationArea area = GetOrAdd<TeleportationArea>(floor);
            area.interactionLayers = 1 << TeleportLayer;
            area.interactionManager = null;
            EditorUtility.SetDirty(area);
        }

        static void BuildBenchAnchor(Transform content)
        {
            // Teleport here to stand exactly at the bench, facing it.
            GameObject anchor = FindOrCreate(content, "Bench_Teleport_Anchor");
            anchor.transform.SetPositionAndRotation(BenchStandPoint, Quaternion.identity);

            Transform marker = anchor.transform.Find("Marker");
            if (marker == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
                marker.name = "Marker";
                marker.SetParent(anchor.transform, false);
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                marker.gameObject.AddComponent<BoxCollider>();
            }
            marker.localPosition = new Vector3(0f, 0.012f, 0f);
            marker.localScale = new Vector3(0.6f, 0.01f, 0.6f);
            MeshRenderer mr = marker.GetComponent<MeshRenderer>();
            mr.sharedMaterial = GetOrCreateMarkerMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            TeleportationAnchor ta = GetOrAdd<TeleportationAnchor>(anchor);
            ta.teleportAnchorTransform = anchor.transform;
            ta.matchOrientation = MatchOrientation.TargetUpAndForward;
            ta.interactionLayers = 1 << TeleportLayer;
            EditorUtility.SetDirty(ta);
        }

        static Material GetOrCreateMarkerMaterial()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(MarkerMaterialPath);
            if (m != null) return m;
            if (!AssetDatabase.IsValidFolder("Assets/Materials/VR")) AssetDatabase.CreateFolder("Assets/Materials", "VR");
            m = new Material(Shader.Find("Standard")) { color = new Color(0.0f, 0.45f, 0.25f) };
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(0.0f, 0.35f, 0.2f));
            AssetDatabase.CreateAsset(m, MarkerMaterialPath);
            return m;
        }

        // ---------- Training menu (world-space UI to the trainee's left) ----------

        static VRTrainingMenu BuildMenu(Transform content, GunAssemblyManager manager, Camera eventCamera)
        {
            GameObject old = content.Find("VR_Training_Menu")?.gameObject;
            if (old != null) Object.DestroyImmediate(old); // our own object: rebuilt so layout changes apply

            GameObject go = new GameObject("VR_Training_Menu", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(content, false);
            // 1.2 m from the standing point, angled to face the trainee.
            go.transform.SetPositionAndRotation(new Vector3(-1.0f, 1.3f, -0.35f), Quaternion.Euler(0f, -58f, 0f));
            go.transform.localScale = Vector3.one * 0.001f; // 1 canvas unit = 1 mm
            ((RectTransform)go.transform).sizeDelta = new Vector2(640f, 480f);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f; // sharp text at VR distances
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            Image bg = Make<Image>(go.transform, "Background", Vector2.zero, new Vector2(640f, 480f));
            bg.color = PanelColor;
            Image rule = Make<Image>(go.transform, "Header_Rule", new Vector2(0f, 138f), new Vector2(580f, 2f));
            rule.color = Amber;
            Label(go.transform, "Title", "GUNASSEMBLY VR", 40, Amber, new Vector2(0f, 200f), new Vector2(600f, 56f), TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(go.transform, "Subtitle", "BERETTA 92 FS  |  FIELD REASSEMBLY TRAINER", 20, Green, new Vector2(0f, 160f), new Vector2(600f, 30f), TextAnchor.MiddleCenter);

            VRTrainingMenu menu = go.AddComponent<VRTrainingMenu>();
            menu.manager = manager;

            // Intro
            menu.introPanel = Panel(go.transform, "Intro_Panel");
            Label(menu.introPanel.transform, "Body",
                "Reassemble the pistol in four steps, then perform the\n7-stage function check (FM 3-23.35).\n\n" +
                "GRIP  -  pick up a part with either hand\n" +
                "RELEASE GRIP  -  at the green ghost to seat the part\n" +
                "BOTH GRIPS  -  hold the frame or slide with two hands\n" +
                "THUMBSTICK  -  push forward to teleport, left / right to turn\n" +
                "TRIGGER  -  point at a button and pull to press it",
                19, BodyColor, new Vector2(0f, 10f), new Vector2(580f, 230f), TextAnchor.UpperLeft);
            menu.startButton = MakeButton(menu.introPanel.transform, "Start_Button", "START TRAINING", new Vector2(0f, -180f), new Vector2(320f, 64f));

            // Progress
            menu.progressPanel = Panel(go.transform, "Progress_Panel");
            menu.stepText = Label(menu.progressPanel.transform, "Step", "", 22, BodyColor, new Vector2(0f, 100f), new Vector2(600f, 36f), TextAnchor.MiddleCenter, FontStyle.Bold);
            menu.statsText = Label(menu.progressPanel.transform, "Stats", "", 20, Green, new Vector2(0f, 62f), new Vector2(600f, 30f), TextAnchor.MiddleCenter);
            menu.tipText = Label(menu.progressPanel.transform, "Tip", "", 19, BodyColor, new Vector2(0f, -45f), new Vector2(570f, 150f), TextAnchor.UpperLeft);
            Button restartDuring = MakeButton(menu.progressPanel.transform, "Restart_Button", "RESTART", new Vector2(0f, -180f), new Vector2(240f, 56f));

            // Complete
            menu.completePanel = Panel(go.transform, "Complete_Panel");
            Label(menu.completePanel.transform, "Heading", "TRAINING COMPLETE", 34, Green, new Vector2(0f, 95f), new Vector2(600f, 48f), TextAnchor.MiddleCenter, FontStyle.Bold);
            menu.resultText = Label(menu.completePanel.transform, "Result", "", 22, BodyColor, new Vector2(0f, -5f), new Vector2(580f, 140f), TextAnchor.MiddleCenter);
            Button restartAfter = MakeButton(menu.completePanel.transform, "Restart_Button", "TRAIN AGAIN", new Vector2(-140f, -180f), new Vector2(240f, 56f));
            menu.exitButton = MakeButton(menu.completePanel.transform, "Exit_Button", "EXIT", new Vector2(140f, -180f), new Vector2(240f, 56f));
            menu.restartButtons = new[] { restartDuring, restartAfter };

            menu.progressPanel.SetActive(false);
            menu.completePanel.SetActive(false);
            return menu;
        }

        static GameObject Panel(Transform parent, string name)
        {
            GameObject p = new GameObject(name, typeof(RectTransform));
            p.layer = parent.gameObject.layer;
            p.transform.SetParent(parent, false);
            ((RectTransform)p.transform).sizeDelta = new Vector2(640f, 480f);
            return p;
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

        static Text Label(Transform parent, string name, string text, int size, Color color, Vector2 pos, Vector2 box, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            Text t = Make<Text>(parent, name, pos, box);
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.lineSpacing = 1.15f;
            t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size)
        {
            Image img = Make<Image>(parent, name, pos, size);
            img.color = Color.white; // tinted by the button colours below
            Button b = img.gameObject.AddComponent<Button>();
            ColorBlock c = b.colors;
            c.normalColor = ButtonColor;
            c.highlightedColor = new Color(0.0f, 0.72f, 0.4f);
            c.selectedColor = ButtonColor;
            c.pressedColor = new Color(0.0f, 0.32f, 0.18f);
            b.colors = c;
            b.targetGraphic = img;
            Label(img.transform, "Label", label, 24, Color.white, Vector2.zero, size, TextAnchor.MiddleCenter, FontStyle.Bold);
            return b;
        }

        // ---------- helpers ----------

        static ActionBasedController FindController(XROrigin origin, string name)
        {
            foreach (ActionBasedController c in origin.GetComponentsInChildren<ActionBasedController>(true))
            {
                if (c.name == name) return c;
            }
            Debug.LogWarning($"[VR Armory] {name} not found in the XR Origin.");
            return null;
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
