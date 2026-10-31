#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace VRArmory.Editor
{
    public static class ArmorySetupHelper
    {
        [MenuItem("Tools/VR Armory/Setup Workbench and Gun Parts")]
        public static void CleanAndAssembleModules()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                return;
            }
            if (!ArmoryEditorGuard.Confirm("Setup Workbench and Gun Parts",
                "Rebuilds the room, player and Tactical HUD (HUD children are destroyed and recreated). Gun modules already staged by Re-Stage Gun Parts are left untouched, and assembly sockets are never purged.")) return;

            AssetDatabase.Refresh();

            GameObject armoryStation = GameObject.Find("ArmorerStation");
            if (armoryStation == null)
            {
                Debug.LogWarning("[VR Armory] ArmorerStation not found in active scene.");
                return;
            }

            Transform benchGroup = armoryStation.transform.Find("Workbench");
            if (benchGroup == null)
            {
                GameObject bg = new GameObject("Workbench");
                bg.transform.SetParent(armoryStation.transform, false);
                benchGroup = bg.transform;
            }

            string[] benchParts = { "Workbench_Top", "Table_Leg_1", "Table_Leg_2", "Table_Leg_3", "Table_Leg_4", "Armorer_Rubber_Mat" };
            foreach (string bp in benchParts)
            {
                Transform t = armoryStation.transform.Find(bp);
                if (t != null) t.SetParent(benchGroup, true);
            }

            Transform matTransform = benchGroup.Find("Armorer_Rubber_Mat") ?? armoryStation.transform.Find("Armorer_Rubber_Mat");
            float matSurfaceY = 0.858f;
            if (matTransform != null)
            {
                Renderer mr = matTransform.GetComponent<Renderer>();
                if (mr != null) matSurfaceY = mr.bounds.max.y;
            }

            Transform partsContainer = armoryStation.transform.Find("Staged_Gun_Parts");
            if (partsContainer == null) return;

            // =========================================================
            // 1. SOLIDIFY, PURGE JUNK & RENAME MODULE 01: LOWER FRAME
            // =========================================================
            Transform tFrame = partsContainer.Find("01_Lower_Frame");
            if (tFrame != null && !ArmoryEditorGuard.IsStaged(tFrame))
            {
                Transform mainFrame = FindPart(tFrame, "Frame_Body", "frame_");
                Transform leftGrip = FindPart(tFrame, "Grip_Panel_Left", "left_grip_");
                Transform rightGrip = FindPart(tFrame, "Grip_Panel_Right", "right_grip_");
                Transform trigger = FindPart(tFrame, "Trigger_Shoe", "trigger_rotate_");
                Transform hammer = FindPart(tFrame, "Hammer", "hammer_");
                Transform latch = FindPart(tFrame, "Takedown_Latch", "disassembling_latch_");
                Transform slideStop = FindPart(tFrame, "Slide_Catch", "slide_stop_");

                HashSet<Transform> kept = new HashSet<Transform>();
                if (mainFrame != null) { mainFrame.name = "Frame_Body"; kept.Add(mainFrame); }
                if (leftGrip != null) { leftGrip.name = "Grip_Panel_Left"; kept.Add(leftGrip); }
                if (rightGrip != null) { rightGrip.name = "Grip_Panel_Right"; kept.Add(rightGrip); }
                if (trigger != null) { trigger.name = "Trigger_Shoe"; kept.Add(trigger); }
                if (hammer != null) { hammer.name = "Hammer"; kept.Add(hammer); }
                if (latch != null) { latch.name = "Takedown_Latch"; kept.Add(latch); }
                if (slideStop != null) { slideStop.name = "Slide_Catch"; kept.Add(slideStop); }

                PurgeJunk(tFrame, kept);

                if (mainFrame != null)
                {
                    mainFrame.gameObject.SetActive(true);
                    mainFrame.localPosition = Vector3.zero;
                    mainFrame.localRotation = Quaternion.identity;

                    if (leftGrip != null)
                    {
                        leftGrip.gameObject.SetActive(true);
                        leftGrip.localPosition = new Vector3(-0.014f, -0.010f, -0.040f);
                        leftGrip.localRotation = Quaternion.identity;
                    }

                    if (rightGrip != null)
                    {
                        rightGrip.gameObject.SetActive(true);
                        rightGrip.localPosition = new Vector3(0.014f, -0.010f, -0.040f);
                        rightGrip.localRotation = Quaternion.identity;
                    }

                    if (trigger != null)
                    {
                        trigger.gameObject.SetActive(true);
                        trigger.localPosition = new Vector3(0f, 0.035f, 0.010f);
                        trigger.localRotation = Quaternion.identity;
                    }

                    if (hammer != null)
                    {
                        hammer.gameObject.SetActive(true);
                        hammer.localPosition = new Vector3(0f, 0.020f, -0.060f);
                        hammer.localRotation = Quaternion.identity;
                    }

                    if (latch != null)
                    {
                        latch.gameObject.SetActive(true);
                        latch.localPosition = new Vector3(-0.016f, 0.030f, 0.030f);
                        latch.localRotation = Quaternion.identity;
                    }

                    if (slideStop != null)
                    {
                        slideStop.gameObject.SetActive(true);
                        slideStop.localPosition = new Vector3(-0.016f, 0.030f, 0.010f);
                        slideStop.localRotation = Quaternion.identity;
                    }
                }

                PlaceFlush(tFrame.gameObject, new Vector3(-0.25f, 0f, -0.06f), 0f, matSurfaceY);
            }

            // =========================================================
            // 2. SOLIDIFY, PURGE JUNK & RENAME MODULE 02: UPPER SLIDE
            // =========================================================
            Transform tSlide = partsContainer.Find("02_Upper_Slide");
            if (tSlide != null && !ArmoryEditorGuard.IsStaged(tSlide))
            {
                Transform mainSlide = FindPart(tSlide, "Slide_Body", "slide_");
                Transform rearSight = FindPart(tSlide, "Rear_Sight", "rear_sight_");
                Transform safetyRight = FindPart(tSlide, "Ambi_Safety_Lever", "safety_right_lever_");
                Transform safetyCyl = FindPart(tSlide, "Safety_Axle", "safety_cylinder_");

                HashSet<Transform> kept = new HashSet<Transform>();
                if (mainSlide != null) { mainSlide.name = "Slide_Body"; kept.Add(mainSlide); }
                if (rearSight != null) { rearSight.name = "Rear_Sight"; kept.Add(rearSight); }
                if (safetyRight != null) { safetyRight.name = "Ambi_Safety_Lever"; kept.Add(safetyRight); }
                if (safetyCyl != null) { safetyCyl.name = "Safety_Axle"; kept.Add(safetyCyl); }

                PurgeJunk(tSlide, kept);

                if (mainSlide != null)
                {
                    mainSlide.gameObject.SetActive(true);
                    mainSlide.localPosition = Vector3.zero;
                    mainSlide.localRotation = Quaternion.identity;

                    if (rearSight != null)
                    {
                        rearSight.gameObject.SetActive(true);
                        rearSight.localPosition = new Vector3(0f, 0.002f, -0.082f);
                        rearSight.localRotation = Quaternion.identity;
                    }

                    if (safetyRight != null)
                    {
                        safetyRight.gameObject.SetActive(true);
                        safetyRight.localPosition = new Vector3(0.015f, 0.005f, -0.080f);
                        safetyRight.localRotation = Quaternion.identity;
                    }

                    if (safetyCyl != null)
                    {
                        safetyCyl.gameObject.SetActive(true);
                        safetyCyl.localPosition = new Vector3(0f, 0.005f, -0.080f);
                        safetyCyl.localRotation = Quaternion.identity;
                    }
                }

                PlaceFlush(tSlide.gameObject, new Vector3(-0.22f, 0f, 0.14f), 0f, matSurfaceY);
            }

            // =========================================================
            // 3. SOLIDIFY, PURGE JUNK & RENAME MODULE 03: BARREL
            // =========================================================
            Transform tBarrel = partsContainer.Find("03_Barrel");
            if (tBarrel != null && !ArmoryEditorGuard.IsStaged(tBarrel))
            {
                Transform mainBarrel = FindPart(tBarrel, "Barrel_Assembly", "barrel_");
                Transform lockingBlock = FindPart(tBarrel, "Locking_Block", "locking_block_");

                HashSet<Transform> kept = new HashSet<Transform>();
                if (mainBarrel != null) { mainBarrel.name = "Barrel_Assembly"; kept.Add(mainBarrel); }
                if (lockingBlock != null) { lockingBlock.name = "Locking_Block"; kept.Add(lockingBlock); }

                PurgeJunk(tBarrel, kept);

                if (mainBarrel != null)
                {
                    mainBarrel.gameObject.SetActive(true);
                    mainBarrel.localPosition = Vector3.zero;
                    mainBarrel.localRotation = Quaternion.identity;

                    if (lockingBlock != null)
                    {
                        lockingBlock.gameObject.SetActive(true);
                        lockingBlock.localPosition = new Vector3(0f, -0.010f, -0.020f);
                        lockingBlock.localRotation = Quaternion.identity;
                    }
                }

                PlaceFlush(tBarrel.gameObject, new Vector3(0.18f, 0f, 0.14f), 0f, matSurfaceY);
            }

            // =========================================================
            // 4. SOLIDIFY, PURGE JUNK & RENAME MODULE 04: RECOIL SPRING
            // =========================================================
            Transform tSpring = partsContainer.Find("04_Recoil_Spring");
            if (tSpring != null && !ArmoryEditorGuard.IsStaged(tSpring))
            {
                Transform guide = FindPart(tSpring, "Guide_Rod", "recoil_spring_guide_");
                Transform spring = FindPart(tSpring, "Recoil_Spring", "recoil_spring_");

                HashSet<Transform> kept = new HashSet<Transform>();
                if (guide != null) { guide.name = "Guide_Rod"; kept.Add(guide); }
                if (spring != null) { spring.name = "Recoil_Spring"; kept.Add(spring); }

                PurgeJunk(tSpring, kept);

                if (guide != null)
                {
                    guide.gameObject.SetActive(true);
                    guide.localPosition = Vector3.zero;
                    guide.localRotation = Quaternion.identity;
                }

                if (spring != null)
                {
                    spring.gameObject.SetActive(true);
                    spring.localPosition = Vector3.zero;
                    spring.localRotation = Quaternion.identity;
                }

                PlaceFlush(tSpring.gameObject, new Vector3(0.18f, 0f, 0.02f), 0f, matSurfaceY);
            }

            // =========================================================
            // 5. SOLIDIFY, PURGE JUNK & RENAME MODULE 05: MAGAZINE
            // =========================================================
            Transform tMag = partsContainer.Find("05_15Rd_Magazine");
            if (tMag != null && !ArmoryEditorGuard.IsStaged(tMag))
            {
                Transform mainMag = FindPart(tMag, "Magazine_Tube", "magazine_");
                Transform baseplate = FindPart(tMag, "Magazine_Floorplate", "magazine_baseplate_");

                HashSet<Transform> kept = new HashSet<Transform>();
                if (mainMag != null) { mainMag.name = "Magazine_Tube"; kept.Add(mainMag); }
                if (baseplate != null) { baseplate.name = "Magazine_Floorplate"; kept.Add(baseplate); }

                PurgeJunk(tMag, kept);

                if (mainMag != null)
                {
                    mainMag.gameObject.SetActive(true);
                    mainMag.localPosition = Vector3.zero;
                    mainMag.localRotation = Quaternion.identity;

                    if (baseplate != null)
                    {
                        baseplate.gameObject.SetActive(true);
                        baseplate.localPosition = new Vector3(0f, 0f, -0.065f);
                        baseplate.localRotation = Quaternion.identity;
                    }
                }

                PlaceFlush(tMag.gameObject, new Vector3(0.15f, 0f, -0.12f), 90f, matSurfaceY);
            }

            // =========================================================
            // 6. ENVIRONMENT (FLOOR, WALLS, CEILING, LIGHTING)
            // =========================================================
            SetupArmoryEnvironment();

            // =========================================================
            // 7. MOVABLE FIRST-PERSON PLAYER CONTROLLER
            // =========================================================
            SetupPlayerController();

            // =========================================================
            // 8. PHASE 01 TACTICAL HUD CANVAS
            // =========================================================
            CreateTacticalHUD(armoryStation);

            // Ensure ceiling lamp lighting above bench
            Transform ceilingLight = armoryStation.transform.Find("Workbench_Ceiling_Light");
            if (ceilingLight != null)
            {
                Light l = ceilingLight.GetComponent<Light>();
                if (l != null)
                {
                    l.intensity = 2.8f;
                    l.range = 7f;
                    l.shadows = LightShadows.Soft;
                }
            }

            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(armoryStation);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            }
            Debug.Log("[VR Armory] All junk parts purged, custom hierarchy cleanly renamed, room built, and player active!");
        }

        private static Transform FindPart(Transform parent, string exactName, string prefix)
        {
            Transform t = parent.Find(exactName);
            if (t != null) return t;

            foreach (Transform child in parent)
            {
                string n = child.name.ToLower();
                if (n.StartsWith(prefix.ToLower())) return child;
            }
            return null;
        }

        private static void PurgeJunk(Transform parent, HashSet<Transform> kept)
        {
            List<GameObject> toDestroy = new List<GameObject>();
            foreach (Transform c in parent)
            {
                // Assembly sockets live under the modules too; deleting them broke every assembly step.
                if (!kept.Contains(c) && c.GetComponent<SnapSocket>() == null)
                {
                    toDestroy.Add(c.gameObject);
                }
            }

            foreach (GameObject go in toDestroy)
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void SetupArmoryEnvironment()
        {
            GameObject facility = GameObject.Find("Armory_Facility");
            if (facility == null)
            {
                facility = new GameObject("Armory_Facility");
            }

            // Ensure Materials
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            Material floorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Floor_Concrete.mat");
            if (floorMat == null)
            {
                floorMat = new Material(Shader.Find("Standard"));
                floorMat.color = new Color(0.12f, 0.14f, 0.16f);
                floorMat.SetFloat("_Glossiness", 0.35f);
                floorMat.SetFloat("_Metallic", 0.15f);
                AssetDatabase.CreateAsset(floorMat, "Assets/Materials/Floor_Concrete.mat");
            }

            Material wallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wall_Industrial.mat");
            if (wallMat == null)
            {
                wallMat = new Material(Shader.Find("Standard"));
                wallMat.color = new Color(0.24f, 0.27f, 0.30f);
                wallMat.SetFloat("_Glossiness", 0.15f);
                AssetDatabase.CreateAsset(wallMat, "Assets/Materials/Wall_Industrial.mat");
            }

            Material ceilingMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Ceiling_Dark.mat");
            if (ceilingMat == null)
            {
                ceilingMat = new Material(Shader.Find("Standard"));
                ceilingMat.color = new Color(0.08f, 0.09f, 0.11f);
                ceilingMat.SetFloat("_Glossiness", 0.1f);
                AssetDatabase.CreateAsset(ceilingMat, "Assets/Materials/Ceiling_Dark.mat");
            }

            Transform archGroup = facility.transform.Find("Architecture");
            if (archGroup == null)
            {
                GameObject ag = new GameObject("Architecture");
                ag.transform.SetParent(facility.transform, false);
                archGroup = ag.transform;
            }

            Transform lightGroup = facility.transform.Find("Facility_Lighting");
            if (lightGroup == null)
            {
                GameObject lg = new GameObject("Facility_Lighting");
                lg.transform.SetParent(facility.transform, false);
                lightGroup = lg.transform;
            }

            // Reparent any existing loose parts
            string[] archParts = { "Room_Floor", "Wall_Back", "Wall_Left", "Wall_Right", "Wall_Front_Left", "Wall_Front_Right", "Wall_Front_Arch_Header", "Room_Ceiling" };
            foreach (string ap in archParts)
            {
                Transform t = facility.transform.Find(ap);
                if (t != null) t.SetParent(archGroup, true);
            }
            Transform looseLight = facility.transform.Find("Armory_Ambient_Light");
            if (looseLight != null) looseLight.SetParent(lightGroup, true);

            // Room shell spans X -5.65..5.65, Z -2.65..7.65 (outer wall faces) so it butts
            // cleanly against the Shooting_Range corridor at Z = 7.65 without overlap/z-fighting.

            // 1. FLOOR (Top surface flush at Y = 0.0m)
            CreateOrUpdateCube(archGroup.gameObject, "Room_Floor", new Vector3(0f, -0.1f, 2.5f), new Vector3(11.3f, 0.2f, 10.3f), floorMat);

            // 2. BACK WALL (Behind player & workbench)
            CreateOrUpdateCube(archGroup.gameObject, "Wall_Back", new Vector3(0f, 2.0f, -2.5f), new Vector3(11.3f, 4.0f, 0.3f), wallMat);

            // 3. LEFT WALL
            CreateOrUpdateCube(archGroup.gameObject, "Wall_Left", new Vector3(-5.5f, 2.0f, 2.5f), new Vector3(0.3f, 4.0f, 10.3f), wallMat);

            // 4. RIGHT WALL
            CreateOrUpdateCube(archGroup.gameObject, "Wall_Right", new Vector3(5.5f, 2.0f, 2.5f), new Vector3(0.3f, 4.0f, 10.3f), wallMat);

            // 5. FRONT WALLS WITH 5.0m x 2.8m ARCHWAY OPENING (X -2.5..2.5) TO SHOOTING CORRIDOR
            CreateOrUpdateCube(archGroup.gameObject, "Wall_Front_Left", new Vector3(-4.075f, 2.0f, 7.5f), new Vector3(3.15f, 4.0f, 0.3f), wallMat);
            CreateOrUpdateCube(archGroup.gameObject, "Wall_Front_Right", new Vector3(4.075f, 2.0f, 7.5f), new Vector3(3.15f, 4.0f, 0.3f), wallMat);
            CreateOrUpdateCube(archGroup.gameObject, "Wall_Front_Arch_Header", new Vector3(0f, 3.4f, 7.5f), new Vector3(5.0f, 1.2f, 0.3f), wallMat);

            // 6. CEILING
            CreateOrUpdateCube(archGroup.gameObject, "Room_Ceiling", new Vector3(0f, 4.1f, 2.5f), new Vector3(11.3f, 0.2f, 10.3f), ceilingMat);

            // 7. AMBIENT ROOM LIGHT
            Transform roomLightTr = lightGroup.Find("Armory_Ambient_Light");
            GameObject roomLightObj;
            if (roomLightTr == null)
            {
                roomLightObj = new GameObject("Armory_Ambient_Light");
                roomLightObj.transform.SetParent(lightGroup, false);
            }
            else
            {
                roomLightObj = roomLightTr.gameObject;
            }
            roomLightObj.transform.position = new Vector3(0f, 3.5f, 1.0f);
            Light rLight = roomLightObj.GetComponent<Light>();
            if (rLight == null) rLight = roomLightObj.AddComponent<Light>();
            rLight.type = LightType.Point;
            rLight.color = new Color(0.88f, 0.93f, 1.0f); // crisp fluorescent tactical white
            rLight.intensity = 1.4f;
            rLight.range = 18f;
            rLight.shadows = LightShadows.Soft;
        }

        private static GameObject CreateOrUpdateCube(GameObject parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            Transform t = parent.transform.Find(name);
            GameObject go;
            if (t == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(parent.transform);
            }
            else
            {
                go = t.gameObject;
            }

            go.transform.position = pos;
            go.transform.localScale = scale;
            go.transform.rotation = Quaternion.identity;

            Renderer r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;

            return go;
        }

        // =========================================================
        // LIVE-FIRE QUALIFICATION RANGE (PHASES 07-09)
        // Lane runs +Z through the front archway. 3 adjacent 1.5m booths fill the 5m arch (X -2.25..2.25).
        // Firing line = counter front edge at Z 9.5; each booth has one dummy at its own distance.
        // =========================================================
        private const float BoothWidth = 1.5f;
        private static readonly float[] BoothCenterX = { -1.5f, 0f, 1.5f };
        private static readonly float[] TargetDistance = { 10f, 15f, 25f };
        private static readonly string[] TargetLabel = { "Near", "Mid", "Far" };
        private const float FiringLineZ = 9.5f;
        private const float BackstopZ = 35.0f;
        private const float CounterTopY = 0.85f;
        private const float DummyScale = 1.2f;

        [MenuItem("Tools/VR Armory/Build Live-Fire Range")]
        public static void BuildLiveFireRange()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                return;
            }
            if (!ArmoryEditorGuard.Confirm("Build Live-Fire Range", "Destroys the Shooting_Range object and builds a new one.")) return;

            // Re-apply corrected room shell so the armory butts cleanly against the corridor.
            SetupArmoryEnvironment();

            GameObject existing = GameObject.Find("Shooting_Range");
            if (existing != null) Object.DestroyImmediate(existing);

            Material floorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Floor_Concrete.mat");
            Material wallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wall_Industrial.mat");
            Material ceilingMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Ceiling_Dark.mat");
            Material partitionMat = GetOrCreateMat("Range_Booth_Partition", new Color(0.32f, 0.35f, 0.38f), 0.1f, 0f);
            Material counterMat = GetOrCreateMat("Range_Counter_Steel", new Color(0.42f, 0.44f, 0.47f), 0.55f, 0.8f);
            Material backstopMat = GetOrCreateMat("Range_Backstop_Rubber", new Color(0.05f, 0.05f, 0.06f), 0.05f, 0f);
            Material stripeMat = GetOrCreateMat("Range_Safety_Yellow", new Color(1.0f, 0.78f, 0.0f), 0.2f, 0f);
            Material dummyMat = GetOrCreateMat("Range_Dummy_Body", new Color(0.55f, 0.47f, 0.33f), 0.1f, 0f);
            Material ringsMat = GetOrCreateRingsMat();
            Material standMat = GetOrCreateMat("Range_Target_Stand", new Color(0.18f, 0.20f, 0.22f), 0.3f, 0.6f);
            Material fixtureMat = GetOrCreateMat("Range_Flood_Fixture", new Color(0.9f, 0.9f, 0.85f), 0.5f, 0f, new Color(1.0f, 0.97f, 0.88f) * 2.0f);

            Transform range = new GameObject("Shooting_Range").transform;
            Transform arch = NewGroup(range, "Range_Architecture");
            Transform booths = NewGroup(range, "Booths");
            Transform targets = NewGroup(range, "Targets");
            Transform lighting = NewGroup(range, "Range_Lighting");

            // ---- Corridor shell: Z 7.65 -> 35.15, same width/height as armory ----
            const float corridorStartZ = 7.65f;
            float corridorEndZ = BackstopZ + 0.15f;
            float corridorZ = (corridorStartZ + corridorEndZ) * 0.5f, corridorLen = corridorEndZ - corridorStartZ;
            Cube(arch, "Range_Floor", new Vector3(0f, -0.1f, corridorZ), new Vector3(11.3f, 0.2f, corridorLen), floorMat);
            Cube(arch, "Range_Ceiling", new Vector3(0f, 4.1f, corridorZ), new Vector3(11.3f, 0.2f, corridorLen), ceilingMat);
            Cube(arch, "Range_Wall_Left", new Vector3(-5.5f, 2.0f, corridorZ), new Vector3(0.3f, 4.0f, corridorLen), wallMat);
            Cube(arch, "Range_Wall_Right", new Vector3(5.5f, 2.0f, corridorZ), new Vector3(0.3f, 4.0f, corridorLen), wallMat);
            Cube(arch, "Range_Backstop", new Vector3(0f, 2.0f, BackstopZ), new Vector3(10.7f, 4.0f, 0.3f), backstopMat);

            // Waist-high firing-line barriers seal the lane either side of the booth row (no walking downrange).
            Cube(arch, "Firing_Line_Barrier_Left", new Vector3(-3.8125f, 0.5f, FiringLineZ), new Vector3(3.075f, 1.0f, 0.08f), stripeMat);
            Cube(arch, "Firing_Line_Barrier_Right", new Vector3(3.8125f, 0.5f, FiringLineZ), new Vector3(3.075f, 1.0f, 0.08f), stripeMat);

            // ---- Shared partitions (2.2m tall, extend 0.5m past firing line) between/around the 3 booths ----
            const float partitionZ = 9.0f, partitionLen = 2.0f;
            float rowLeftX = BoothCenterX[0] - BoothWidth * 0.5f;
            for (int i = 0; i <= BoothCenterX.Length; i++)
            {
                Cube(booths, "Booth_Partition_" + (char)('A' + i), new Vector3(rowLeftX + i * BoothWidth, 1.1f, partitionZ), new Vector3(0.05f, 2.2f, partitionLen), partitionMat);
            }

            const float counterDepth = 0.6f, counterThick = 0.05f;
            float counterZ = FiringLineZ - counterDepth * 0.5f;

            for (int i = 0; i < BoothCenterX.Length; i++)
            {
                float x = BoothCenterX[i];
                string num = "0" + (i + 1);
                Transform booth = NewGroup(booths, "Booth_" + num);

                // Counter: top surface exactly Y 0.85, front edge on the firing line.
                Cube(booth, "Shooting_Counter_Top", new Vector3(x, CounterTopY - counterThick * 0.5f, counterZ), new Vector3(1.4f, counterThick, counterDepth), counterMat);
                Cube(booth, "Counter_Front_Panel", new Vector3(x, (CounterTopY - counterThick) * 0.5f, FiringLineZ - 0.03f), new Vector3(1.4f, CounterTopY - counterThick, 0.04f), partitionMat);
                Cube(booth, "Shooter_Box_Marker", new Vector3(x, 0.002f, 8.4f), new Vector3(0.9f, 0.004f, 0.04f), stripeMat, false);

                // Anchors for Phase 07-09 scripting (weapon hand-off point and trainee stand point).
                NewGroup(booth, "Pistol_Rest_Point").position = new Vector3(x, CounterTopY + 0.001f, counterZ - 0.1f);
                NewGroup(booth, "Shooter_Position").position = new Vector3(x, 0f, 8.4f);

                // ---- One dummy per booth, at that booth's distance, facing the booth (-Z) ----
                string id = "Target_" + num + "_" + TargetLabel[i];
                Transform t = NewGroup(targets, id);
                t.position = new Vector3(x, 0f, FiringLineZ);

                Cube(t, "Stand_Base", new Vector3(0f, 0.025f, 0f), new Vector3(0.6f, 0.05f, 0.3f), standMat);
                Cube(t, "Stand_Post", new Vector3(0f, 0.53f, 0f), new Vector3(0.05f, 0.96f, 0.05f), standMat, false);
                Cube(t, "Dummy_Neck", new Vector3(0f, 1.69f, 0f), new Vector3(0.10f, 0.08f, 0.10f), dummyMat, false);
                Cube(t, "HitZone_Torso", new Vector3(0f, 1.33f, 0f), new Vector3(0.45f, 0.65f, 0.22f), dummyMat);

                // Concentric scoring rings on the torso front (Quad faces -Z, toward the booth).
                GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                face.name = "Scoring_Rings";
                face.transform.SetParent(t, false);
                face.transform.localPosition = new Vector3(0f, 1.33f, -0.112f);
                face.transform.localScale = new Vector3(0.45f, 0.65f, 1f);
                face.GetComponent<Renderer>().sharedMaterial = ringsMat;
                Object.DestroyImmediate(face.GetComponent<Collider>());
                GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "HitZone_Head";
                head.transform.SetParent(t, false);
                head.transform.localPosition = new Vector3(0f, 1.84f, 0f);
                head.transform.localScale = new Vector3(0.21f, 0.24f, 0.21f);
                head.GetComponent<Renderer>().sharedMaterial = dummyMat;

                t.localScale = Vector3.one * DummyScale;

                RangeTarget rt = t.gameObject.AddComponent<RangeTarget>();
                rt.targetId = id;
                rt.firingLineZ = FiringLineZ;
                rt.SetDistance(TargetDistance[i]);

                // Shadowed spot aimed at each dummy from the ceiling centreline (no fixture mesh,
                // so the visible ceiling stays a single evenly spaced row).
                Transform spot = NewGroup(lighting, "Target_Spot_" + num);
                spot.position = new Vector3(0f, 3.85f, t.position.z - 3.0f);
                spot.LookAt(new Vector3(x, 1.4f * DummyScale, t.position.z));
                Light sl = spot.gameObject.AddComponent<Light>();
                sl.type = LightType.Spot;
                sl.color = new Color(1.0f, 0.97f, 0.9f);
                sl.spotAngle = 35f;
                sl.intensity = 3.0f;
                sl.range = 8f;
                sl.shadows = LightShadows.Soft;
            }

            // ---- Single centreline row of overhead floodlights, evenly spaced (no shadows: VR perf) ----
            const float floodStartZ = 10.5f, floodSpacing = 4.0f;
            int floodCount = Mathf.FloorToInt((BackstopZ - 0.5f - floodStartZ) / floodSpacing) + 1;
            for (int i = 0; i < floodCount; i++)
            {
                AddFlood(lighting, "Range_Floodlight_" + (i + 1).ToString("00"), new Vector3(0f, 3.85f, floodStartZ + i * floodSpacing), 90f, 120f, 2.0f, 8f, LightShadows.None, fixtureMat);
            }

            EditorSceneManagerMarkDirty();
            Debug.Log($"[VR Armory] Live-fire range built: 3 booths, firing line Z={FiringLineZ}, dummies at {TargetDistance[0]}m / {TargetDistance[1]}m / {TargetDistance[2]}m.");
        }

        private static void AddFlood(Transform parent, string name, Vector3 pos, float pitch, float spotAngle, float intensity, float range, LightShadows shadows, Material fixtureMat)
        {
            Transform lt = NewGroup(parent, name);
            lt.position = pos;
            lt.rotation = Quaternion.Euler(pitch, 0f, 0f);
            Light l = lt.gameObject.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = new Color(1.0f, 0.97f, 0.9f);
            l.spotAngle = spotAngle;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows;

            GameObject fixture = Cube(parent, name + "_Fixture", new Vector3(pos.x, 3.97f, pos.z), new Vector3(0.6f, 0.06f, 0.25f), fixtureMat, false);
            fixture.transform.SetParent(lt, true);
            fixture.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        internal static Transform NewGroup(Transform parent, string name)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // Local-space cube under parent; colliders kept by default (walls, counter, hit zones).
        private static GameObject Cube(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        internal static Material GetOrCreateMat(string name, Color color, float gloss, float metallic, Color? emission = null)
        {
            string path = "Assets/Materials/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            m = new Material(Shader.Find("Standard"));
            m.color = color;
            m.SetFloat("_Glossiness", gloss);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // Procedural concentric-ring scoring face, aspect-matched to the 0.45 x 0.65 torso so rings stay circular.
        private static Material GetOrCreateRingsMat()
        {
            const string matPath = "Assets/Materials/Range_Dummy_Rings.mat";
            const string texPath = "Assets/Textures/Target_Rings.png";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m != null) return m;

            if (!AssetDatabase.IsValidFolder("Assets/Textures")) AssetDatabase.CreateFolder("Assets", "Textures");

            const int w = 450, h = 650;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            Color body = new Color(0.55f, 0.47f, 0.33f);
            Color ink = new Color(0.06f, 0.06f, 0.06f);
            Color bull = new Color(0.7f, 0.08f, 0.06f);
            Vector2 c = new Vector2(w * 0.5f, h * 0.55f); // centre mass, slightly above torso middle
            const float ringStep = 36f, lineWidth = 4f, bullRadius = 18f;
            const int ringCount = 6;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    Color col = body;
                    if (d <= bullRadius) col = bull;
                    else if (d <= ringStep * ringCount + lineWidth && Mathf.Abs(d - Mathf.Round(d / ringStep) * ringStep) < lineWidth * 0.5f) col = ink;
                    tex.SetPixel(x, y, col);
                }
            }
            System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);

            m = new Material(Shader.Find("Standard"));
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            m.SetFloat("_Glossiness", 0.05f);
            AssetDatabase.CreateAsset(m, matPath);
            return m;
        }

        private static void EditorSceneManagerMarkDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }

        private static void SetupPlayerController()
        {
            GameObject player = GameObject.Find("Armorer_Player");
            if (player == null)
            {
                player = new GameObject("Armorer_Player");
            }

            // Position trainee standing naturally 0.95m in front of workbench center
            player.transform.position = new Vector3(0f, 0.0f, -0.95f);
            player.transform.rotation = Quaternion.identity;

            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc == null) cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 45f;

            // Camera setup at standard human eye height 1.65m
            Camera cam = Camera.main;
            if (cam == null) cam = Object.FindObjectOfType<Camera>();
            if (cam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                cam = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
            }

            cam.transform.SetParent(player.transform);
            cam.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            cam.transform.localRotation = Quaternion.Euler(20f, 0f, 0f); // looking naturally down at table
            cam.nearClipPlane = 0.03f; // crisp close-up inspection of gun parts
            cam.fieldOfView = 65f;

            System.Type pcType = System.Type.GetType("VRArmory.ArmoryPlayerController, Assembly-CSharp");
            if (pcType == null)
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    pcType = asm.GetType("VRArmory.ArmoryPlayerController");
                    if (pcType != null) break;
                }
            }

            if (pcType != null)
            {
                Component pc = player.GetComponent(pcType);
                if (pc == null) pc = player.AddComponent(pcType);
                var camField = pcType.GetField("playerCamera");
                if (camField != null) camField.SetValue(pc, cam);
            }
        }

        private static void PlaceFlush(GameObject module, Vector3 targetXZ, float angleY, float matTopY, float scale = 1.45f)
        {
            module.transform.localScale = Vector3.one * scale;
            module.transform.rotation = Quaternion.Euler(0f, angleY, 0f);
            module.transform.position = new Vector3(targetXZ.x, matTopY, targetXZ.z);

            Bounds b = GetCombinedBounds(module);
            float lift = (matTopY + 0.001f) - b.min.y;
            module.transform.position += new Vector3(0f, lift, 0f);

            // Compute local bounding box for the BoxCollider
            BoxCollider col = module.GetComponent<BoxCollider>();
            if (col == null) col = module.AddComponent<BoxCollider>();

            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (Renderer r in module.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.gameObject.activeInHierarchy) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Vector3 lMin = module.transform.InverseTransformPoint(r.transform.TransformPoint(mf.sharedMesh.bounds.min));
                    Vector3 lMax = module.transform.InverseTransformPoint(r.transform.TransformPoint(mf.sharedMesh.bounds.max));
                    if (first)
                    {
                        localBounds = new Bounds((lMin + lMax) * 0.5f, Vector3.zero);
                        localBounds.Encapsulate(lMin);
                        localBounds.Encapsulate(lMax);
                        first = false;
                    }
                    else
                    {
                        localBounds.Encapsulate(lMin);
                        localBounds.Encapsulate(lMax);
                    }
                }
            }

            if (!first)
            {
                col.center = localBounds.center;
                col.size = localBounds.size;
            }

            Rigidbody rb = module.GetComponent<Rigidbody>();
            if (rb == null) rb = module.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }

        private static Bounds GetCombinedBounds(GameObject obj)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.zero);

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i].gameObject.activeInHierarchy)
                    b.Encapsulate(renderers[i].bounds);
            }
            return b;
        }

        private static void CreateTacticalHUD(GameObject parentStation)
        {
            Transform existingHUD = parentStation.transform.Find("Tactical_HUD_Canvas");
            GameObject hudObj;
            if (existingHUD != null)
            {
                hudObj = existingHUD.gameObject;
            }
            else
            {
                hudObj = new GameObject("Tactical_HUD_Canvas");
                hudObj.transform.SetParent(parentStation.transform);
            }

            // World space position: floating above workbench, directly ahead of trainee eye line
            hudObj.transform.position = new Vector3(0f, 1.40f, 0.35f);
            hudObj.transform.rotation = Quaternion.Euler(14f, 0f, 0f);
            hudObj.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);

            Canvas canvas = hudObj.GetComponent<Canvas>();
            if (canvas == null) canvas = hudObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            CanvasScaler scaler = hudObj.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = hudObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            GraphicRaycaster raycaster = hudObj.GetComponent<GraphicRaycaster>();
            if (raycaster == null) hudObj.AddComponent<GraphicRaycaster>();

            RectTransform canvasRect = hudObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(960f, 540f);

            // Clean existing children
            for (int i = hudObj.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(hudObj.transform.GetChild(i).gameObject);
            }

            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // 1. Dark HUD Panel Background
            GameObject bgObj = new GameObject("HUD_Background");
            bgObj.transform.SetParent(hudObj.transform, false);
            RectTransform bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.sizeDelta = new Vector2(960f, 540f);
            Image bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.04f, 0.06f, 0.08f, 0.94f);

            // 2. Tactical Amber Header
            GameObject headerObj = new GameObject("Header_Title");
            headerObj.transform.SetParent(hudObj.transform, false);
            RectTransform headerRect = headerObj.AddComponent<RectTransform>();
            headerRect.anchoredPosition = new Vector2(0f, 210f);
            headerRect.sizeDelta = new Vector2(900f, 60f);
            Text headerText = headerObj.AddComponent<Text>();
            headerText.font = defaultFont;
            headerText.fontSize = 28;
            headerText.fontStyle = FontStyle.Bold;
            headerText.color = new Color(1.0f, 0.72f, 0.0f, 1.0f); // Tactical Amber #FFB700
            headerText.alignment = TextAnchor.MiddleCenter;
            headerText.text = "[ PHASE 01 : WEAPON RECEPTION & INSPECTION ]";

            // 3. Subtitle
            GameObject subObj = new GameObject("Subtitle");
            subObj.transform.SetParent(hudObj.transform, false);
            RectTransform subRect = subObj.AddComponent<RectTransform>();
            subRect.anchoredPosition = new Vector2(0f, 160f);
            subRect.sizeDelta = new Vector2(900f, 40f);
            Text subText = subObj.AddComponent<Text>();
            subText.font = defaultFont;
            subText.fontSize = 20;
            subText.color = new Color(0.0f, 1.0f, 0.55f, 1.0f); // Phosphor Green #00FF88
            subText.alignment = TextAnchor.MiddleCenter;
            subText.text = "BERETTA 92 FS 9MM NATO  |  5 SUB-ASSEMBLIES STAGED";

            // 4. Checklist Text
            GameObject listObj = new GameObject("Checklist");
            listObj.transform.SetParent(hudObj.transform, false);
            RectTransform listRect = listObj.AddComponent<RectTransform>();
            listRect.anchoredPosition = new Vector2(0f, 20f);
            listRect.sizeDelta = new Vector2(820f, 220f);
            Text listText = listObj.AddComponent<Text>();
            listText.font = defaultFont;
            listText.fontSize = 20;
            listText.lineSpacing = 1.35f;
            listText.color = new Color(0.88f, 0.92f, 0.96f, 1.0f);
            listText.alignment = TextAnchor.UpperLeft;
            listText.text =
                "[ + ]  01_LOWER_FRAME      : Lower Receiver, Grips, Trigger Shoe, Hammer  [STAGED]\n" +
                "[ + ]  02_UPPER_SLIDE      : Machined Steel Slide, Sights, Safety Axle   [STAGED]\n" +
                "[ + ]  03_BARREL           : 4.9\" Cold-Hammer-Forged Barrel & Lug Block   [STAGED]\n" +
                "[ + ]  04_RECOIL_SPRING    : Captive Recoil Spring & Steel Guide Rod     [STAGED]\n" +
                "[ + ]  05_15RD_MAGAZINE    : 15-Round Double-Stack Steel Magazine Box    [STAGED]";

            // 5. Armorer Directive Status
            GameObject statusObj = new GameObject("Directive_Status");
            statusObj.transform.SetParent(hudObj.transform, false);
            RectTransform statusRect = statusObj.AddComponent<RectTransform>();
            statusRect.anchoredPosition = new Vector2(0f, -140f);
            statusRect.sizeDelta = new Vector2(880f, 50f);
            Text statusText = statusObj.AddComponent<Text>();
            statusText.font = defaultFont;
            statusText.fontSize = 18;
            statusText.fontStyle = FontStyle.Bold;
            statusText.color = new Color(1.0f, 0.72f, 0.0f, 1.0f);
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.text = "DIRECTIVE: ALL MODULES RESTING FLUSH ON RUBBER MAT. READY FOR ASSEMBLY.";

            // 6. Action Button Visual
            GameObject btnObj = new GameObject("Start_Button");
            btnObj.transform.SetParent(hudObj.transform, false);
            RectTransform btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.anchoredPosition = new Vector2(0f, -210f);
            btnRect.sizeDelta = new Vector2(380f, 55f);
            Image btnImg = btnObj.AddComponent<Image>();
            btnImg.color = new Color(0.0f, 0.5f, 0.28f, 0.95f);

            GameObject btnTextObj = new GameObject("Button_Text");
            btnTextObj.transform.SetParent(btnObj.transform, false);
            RectTransform btnTextRect = btnTextObj.AddComponent<RectTransform>();
            btnTextRect.sizeDelta = new Vector2(380f, 55f);
            Text btnText = btnTextObj.AddComponent<Text>();
            btnText.font = defaultFont;
            btnText.fontSize = 20;
            btnText.fontStyle = FontStyle.Bold;
            btnText.color = Color.white;
            btnText.alignment = TextAnchor.MiddleCenter;
            btnText.text = "BEGIN STEP 01: BARREL INSERTION";
        }
    }
}
#endif
