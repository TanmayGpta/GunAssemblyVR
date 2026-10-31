#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace VRArmory.Editor
{
    // Military armory / barracks set dressing for the workbench room (inner bounds X -5.35..5.35, Z -2.35..7.35).
    // Keeps the bench (origin) -> archway (Z 7.5, X -2.5..2.5) walkway clear. Rebuilds Set_Dressing each run.
    public static class ArmoryDressingBuilder
    {
        private const float WallLeftX = -5.35f, WallRightX = 5.35f, WallBackZ = -2.35f, WallFrontZ = 7.35f;

        private static Material olive, coyote, gunmetal, steel, wood, woodDark, lockerDoor, black, red, yellow, plate, panel, fixture, hotLamp, cork;
        private static Font font;

        [MenuItem("Tools/VR Armory/Build Armory Set Dressing")]
        public static void BuildSetDressing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                Debug.LogWarning("[VR Armory] Exit Play mode before building set dressing.");
                return;
            }
            if (!ArmoryEditorGuard.Confirm("Build Armory Set Dressing", "Destroys Armory_Facility/Set_Dressing and builds a new one.")) return;

            GameObject facility = GameObject.Find("Armory_Facility");
            if (facility == null)
            {
                Debug.LogWarning("[VR Armory] Armory_Facility not found. Run 'Setup Workbench and Gun Parts' first.");
                return;
            }

            Transform old = facility.transform.Find("Set_Dressing");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            LoadMaterials();

            Transform root = ArmorySetupHelper.NewGroup(facility.transform, "Set_Dressing");
            root.position = Vector3.zero;
            root.rotation = Quaternion.identity;

            BuildWeaponRacks(ArmorySetupHelper.NewGroup(root, "Weapon_Racks"));
            BuildLockers(ArmorySetupHelper.NewGroup(root, "Gear_Lockers"));
            BuildAmmoStorage(ArmorySetupHelper.NewGroup(root, "Ammo_Storage"));
            BuildBackWall(ArmorySetupHelper.NewGroup(root, "Back_Wall_Displays"));
            BuildFrontWall(ArmorySetupHelper.NewGroup(root, "Range_Entry"));
            BuildFloorAndCeiling(ArmorySetupHelper.NewGroup(root, "Floor_Markings_And_Fixtures"));

            foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>())
            {
                GameObjectUtility.SetStaticEditorFlags(mr.gameObject, StaticEditorFlags.BatchingStatic);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(facility.scene);
            Debug.Log("[VR Armory] Armory set dressing built: weapon racks, gear lockers, ammo storage, briefing displays, range entry.");
        }

        private static void LoadMaterials()
        {
            olive = ArmorySetupHelper.GetOrCreateMat("Mil_Olive_Drab", new Color(0.27f, 0.29f, 0.19f), 0.15f, 0.1f);
            coyote = ArmorySetupHelper.GetOrCreateMat("Mil_Coyote_Tan", new Color(0.55f, 0.45f, 0.30f), 0.15f, 0f);
            gunmetal = ArmorySetupHelper.GetOrCreateMat("Mil_Gunmetal", new Color(0.07f, 0.07f, 0.08f), 0.45f, 0.6f);
            steel = ArmorySetupHelper.GetOrCreateMat("Mil_Steel_Dark", new Color(0.25f, 0.27f, 0.29f), 0.5f, 0.75f);
            wood = ArmorySetupHelper.GetOrCreateMat("Mil_Crate_Wood", new Color(0.45f, 0.33f, 0.20f), 0.1f, 0f);
            woodDark = ArmorySetupHelper.GetOrCreateMat("Mil_Crate_Wood_Dark", new Color(0.30f, 0.21f, 0.12f), 0.1f, 0f);
            lockerDoor = ArmorySetupHelper.GetOrCreateMat("Mil_Locker_Door", new Color(0.31f, 0.33f, 0.22f), 0.25f, 0.3f);
            black = ArmorySetupHelper.GetOrCreateMat("Mil_Black", new Color(0.03f, 0.03f, 0.03f), 0.1f, 0f);
            red = ArmorySetupHelper.GetOrCreateMat("Mil_Safety_Red", new Color(0.70f, 0.05f, 0.05f), 0.4f, 0.1f);
            yellow = ArmorySetupHelper.GetOrCreateMat("Range_Safety_Yellow", new Color(1.0f, 0.78f, 0.0f), 0.2f, 0f);
            plate = ArmorySetupHelper.GetOrCreateMat("Mil_Name_Plate", new Color(0.85f, 0.85f, 0.80f), 0.3f, 0f);
            panel = ArmorySetupHelper.GetOrCreateMat("Mil_Sign_Panel", new Color(0.06f, 0.08f, 0.07f), 0.2f, 0f);
            cork = ArmorySetupHelper.GetOrCreateMat("Mil_Pegboard", new Color(0.36f, 0.30f, 0.22f), 0.05f, 0f);
            fixture = ArmorySetupHelper.GetOrCreateMat("Range_Flood_Fixture", new Color(0.9f, 0.9f, 0.85f), 0.5f, 0f, new Color(1.0f, 0.97f, 0.88f) * 2.0f);
            hotLamp = ArmorySetupHelper.GetOrCreateMat("Mil_Range_Hot_Lamp", new Color(0.8f, 0.05f, 0.05f), 0.6f, 0f, new Color(1.0f, 0.05f, 0.02f) * 3.0f);

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ---------------- LEFT WALL: M4 carbine racks ----------------
        private static void BuildWeaponRacks(Transform group)
        {
            float[] rackZ = { 1.0f, 4.2f };
            for (int r = 0; r < rackZ.Length; r++)
            {
                float z = rackZ[r];
                Transform rack = ArmorySetupHelper.NewGroup(group, "Rifle_Rack_0" + (r + 1));

                Prim(PrimitiveType.Cube, rack, "Back_Panel", new Vector3(WallLeftX + 0.03f, 1.0f, z), new Vector3(0.05f, 1.6f, 2.3f), olive, true);
                Prim(PrimitiveType.Cube, rack, "Butt_Tray", new Vector3(WallLeftX + 0.18f, 0.24f, z), new Vector3(0.3f, 0.08f, 2.3f), steel, true);
                Prim(PrimitiveType.Cube, rack, "Retaining_Bar", new Vector3(WallLeftX + 0.2f, 1.05f, z), new Vector3(0.26f, 0.04f, 2.3f), steel);
                Prim(PrimitiveType.Cube, rack, "Upright_A", new Vector3(WallLeftX + 0.18f, 0.65f, z - 1.15f), new Vector3(0.3f, 1.3f, 0.04f), steel, true);
                Prim(PrimitiveType.Cube, rack, "Upright_B", new Vector3(WallLeftX + 0.18f, 0.65f, z + 1.15f), new Vector3(0.3f, 1.3f, 0.04f), steel, true);
                Prim(PrimitiveType.Cylinder, rack, "Security_Cable", new Vector3(WallLeftX + 0.33f, 0.85f, z), new Vector3(0.012f, 1.15f, 0.012f), gunmetal, false, new Vector3(90f, 0f, 0f));

                Material furniture = r == 0 ? gunmetal : coyote;
                for (int i = 0; i < 6; i++)
                {
                    Transform rifle = ArmorySetupHelper.NewGroup(rack, "M4A1_0" + (i + 1));
                    rifle.position = new Vector3(WallLeftX + 0.2f, 0.28f, z - 0.825f + i * 0.33f);
                    BuildCarbine(rifle, furniture);
                }
            }
        }

        // Stylized M4A1 standing butt-down. Local +Y = muzzle, +X = magazine/belly side (faces into the room).
        private static void BuildCarbine(Transform rifle, Material furniture)
        {
            Prim(PrimitiveType.Cube, rifle, "Stock", new Vector3(0f, 0.13f, 0f), new Vector3(0.09f, 0.26f, 0.045f), furniture);
            Prim(PrimitiveType.Cylinder, rifle, "Buffer_Tube", new Vector3(-0.015f, 0.31f, 0f), new Vector3(0.03f, 0.06f, 0.03f), gunmetal);
            Prim(PrimitiveType.Cube, rifle, "Receiver", new Vector3(0f, 0.48f, 0f), new Vector3(0.085f, 0.26f, 0.05f), gunmetal);
            Prim(PrimitiveType.Cube, rifle, "Magazine", new Vector3(0.11f, 0.5f, 0f), new Vector3(0.17f, 0.075f, 0.03f), gunmetal, false, new Vector3(0f, 0f, -12f));
            Prim(PrimitiveType.Cube, rifle, "Pistol_Grip", new Vector3(0.075f, 0.39f, 0f), new Vector3(0.1f, 0.035f, 0.03f), furniture, false, new Vector3(0f, 0f, 20f));
            Prim(PrimitiveType.Cube, rifle, "Optic", new Vector3(-0.07f, 0.5f, 0f), new Vector3(0.05f, 0.11f, 0.04f), black);
            Prim(PrimitiveType.Cube, rifle, "Handguard", new Vector3(0f, 0.7f, 0f), new Vector3(0.06f, 0.2f, 0.055f), furniture);
            Prim(PrimitiveType.Cylinder, rifle, "Barrel", new Vector3(0f, 0.87f, 0f), new Vector3(0.018f, 0.08f, 0.018f), gunmetal);
            Prim(PrimitiveType.Cube, rifle, "Front_Sight", new Vector3(-0.03f, 0.82f, 0f), new Vector3(0.05f, 0.015f, 0.012f), gunmetal);
        }

        // ---------------- RIGHT WALL: steel gear lockers ----------------
        private static void BuildLockers(Transform group)
        {
            const float depth = 0.55f, width = 0.6f, height = 1.9f;
            float faceX = WallRightX - depth;
            for (int i = 0; i < 6; i++)
            {
                float z = 0.5f + i * width;
                Transform locker = ArmorySetupHelper.NewGroup(group, "Locker_0" + (i + 1));

                Prim(PrimitiveType.Cube, locker, "Body", new Vector3(WallRightX - depth * 0.5f, height * 0.5f, z), new Vector3(depth, height, width - 0.02f), olive, true);
                Prim(PrimitiveType.Cube, locker, "Door", new Vector3(faceX - 0.003f, height * 0.5f + 0.03f, z), new Vector3(0.006f, height - 0.14f, width - 0.07f), lockerDoor);
                Prim(PrimitiveType.Cube, locker, "Kick_Plate", new Vector3(faceX - 0.003f, 0.04f, z), new Vector3(0.006f, 0.08f, width - 0.02f), black);
                Prim(PrimitiveType.Cube, locker, "Handle", new Vector3(faceX - 0.02f, 1.0f, z - 0.2f), new Vector3(0.03f, 0.12f, 0.025f), steel);
                Prim(PrimitiveType.Cube, locker, "Name_Plate", new Vector3(faceX - 0.008f, 1.78f, z), new Vector3(0.004f, 0.05f, 0.2f), plate);

                // Helmet on alternate lockers; rucksack on the others.
                Transform gear = ArmorySetupHelper.NewGroup(locker, i % 2 == 0 ? "Combat_Helmet" : "Rucksack");
                gear.position = new Vector3(WallRightX - 0.28f, height, z);
                if (i % 2 == 0) BuildHelmet(gear, i == 2 ? olive : coyote);
                else BuildRucksack(gear, i == 3 ? coyote : olive);
            }
        }

        // Ballistic helmet resting on the locker top (local origin = locker top surface). -X faces the room.
        private static void BuildHelmet(Transform t, Material shell)
        {
            // Sphere centred on the locker top so only the upper half (the dome) shows.
            Prim(PrimitiveType.Sphere, t, "Shell_Dome", new Vector3(0f, 0.005f, 0f), new Vector3(0.24f, 0.30f, 0.27f), shell);
            Prim(PrimitiveType.Cylinder, t, "Brim", new Vector3(0f, 0.008f, 0f), new Vector3(0.255f, 0.006f, 0.285f), shell);
            Prim(PrimitiveType.Cube, t, "NVG_Shroud", new Vector3(-0.115f, 0.09f, 0f), new Vector3(0.02f, 0.05f, 0.06f), black, false, new Vector3(0f, 0f, -35f));
            Prim(PrimitiveType.Cube, t, "Rail_L", new Vector3(0f, 0.05f, -0.128f), new Vector3(0.14f, 0.025f, 0.012f), black, false, new Vector3(-20f, 0f, 0f));
            Prim(PrimitiveType.Cube, t, "Rail_R", new Vector3(0f, 0.05f, 0.128f), new Vector3(0.14f, 0.025f, 0.012f), black, false, new Vector3(20f, 0f, 0f));
            Prim(PrimitiveType.Cube, t, "Velcro_Patch", new Vector3(0.02f, 0.145f, 0f), new Vector3(0.09f, 0.006f, 0.07f), olive);
        }

        // Upright assault rucksack (local origin = locker top surface). -X faces the room.
        private static void BuildRucksack(Transform t, Material fabric)
        {
            Prim(PrimitiveType.Cube, t, "Main_Body", new Vector3(0.02f, 0.21f, 0f), new Vector3(0.24f, 0.42f, 0.36f), fabric);
            Prim(PrimitiveType.Cube, t, "Top_Flap", new Vector3(0.0f, 0.43f, 0f), new Vector3(0.27f, 0.05f, 0.37f), fabric);
            Prim(PrimitiveType.Cube, t, "Front_Pouch", new Vector3(-0.125f, 0.16f, 0f), new Vector3(0.06f, 0.2f, 0.26f), fabric);
            Prim(PrimitiveType.Cube, t, "Side_Pouch_L", new Vector3(0.02f, 0.15f, -0.205f), new Vector3(0.16f, 0.22f, 0.05f), fabric);
            Prim(PrimitiveType.Cube, t, "Side_Pouch_R", new Vector3(0.02f, 0.15f, 0.205f), new Vector3(0.16f, 0.22f, 0.05f), fabric);
            Prim(PrimitiveType.Cube, t, "Strap_L", new Vector3(-0.158f, 0.25f, -0.08f), new Vector3(0.008f, 0.36f, 0.03f), black);
            Prim(PrimitiveType.Cube, t, "Strap_R", new Vector3(-0.158f, 0.25f, 0.08f), new Vector3(0.008f, 0.36f, 0.03f), black);
            Prim(PrimitiveType.Cube, t, "Grab_Handle", new Vector3(0.0f, 0.465f, 0f), new Vector3(0.03f, 0.02f, 0.12f), black);
        }

        // ---------------- AMMO CRATES & CANS ----------------
        private static void BuildAmmoStorage(Transform group)
        {
            // Right-front corner: stacked olive ammo crates + wooden crate.
            int[] stackHeights = { 3, 2 };
            float[] stackZ = { 4.75f, 5.6f };
            for (int s = 0; s < stackHeights.Length; s++)
            {
                for (int h = 0; h < stackHeights[s]; h++)
                {
                    AmmoCrate(group, "Ammo_Crate_" + (char)('A' + s) + (h + 1), new Vector3(4.95f, 0.16f + h * 0.32f, stackZ[s]), h % 2 == 1 ? 6f : 0f);
                }
            }
            for (int c = 0; c < 3; c++)
            {
                AmmoCan(group, "M2A1_Ammo_Can_0" + (c + 1), new Vector3(4.83f + c * 0.13f, 0.64f, 5.6f), 0f);
            }
            WoodCrate(group, "Wood_Crate_01", new Vector3(4.85f, 0.3f, 6.6f), new Vector3(0.8f, 0.6f, 0.8f), 0f);

            // Left-front corner: wood crates with a can on top.
            WoodCrate(group, "Wood_Crate_02", new Vector3(-4.8f, 0.3f, 6.65f), new Vector3(0.8f, 0.6f, 0.8f), 0f);
            WoodCrate(group, "Wood_Crate_03", new Vector3(-4.85f, 0.8f, 6.6f), new Vector3(0.6f, 0.4f, 0.6f), 14f);
            AmmoCan(group, "M2A1_Ammo_Can_04", new Vector3(-4.0f, 0.095f, 6.8f), 30f);

            // Back-right: footlockers with cans.
            for (int f = 0; f < 2; f++)
            {
                Transform fl = ArmorySetupHelper.NewGroup(group, "Footlocker_0" + (f + 1));
                float x = 3.2f + f * 1.1f;
                Prim(PrimitiveType.Cube, fl, "Body", new Vector3(x, 0.2f, WallBackZ + 0.35f), new Vector3(0.9f, 0.4f, 0.5f), olive, true);
                Prim(PrimitiveType.Cube, fl, "Lid_Band", new Vector3(x, 0.36f, WallBackZ + 0.35f), new Vector3(0.91f, 0.03f, 0.51f), steel);
                Prim(PrimitiveType.Cube, fl, "Latch", new Vector3(x, 0.32f, WallBackZ + 0.605f), new Vector3(0.08f, 0.06f, 0.01f), steel);
            }
            AmmoCan(group, "M2A1_Ammo_Can_05", new Vector3(3.0f, 0.495f, WallBackZ + 0.35f), 90f);
        }

        private static void AmmoCrate(Transform parent, string name, Vector3 pos, float yaw)
        {
            Transform t = ArmorySetupHelper.NewGroup(parent, name);
            t.position = pos;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, t, "Body", Vector3.zero, new Vector3(0.45f, 0.32f, 0.72f), olive, true);
            Prim(PrimitiveType.Cube, t, "Stencil_Band", new Vector3(0f, 0.06f, 0f), new Vector3(0.455f, 0.035f, 0.725f), yellow);
            Prim(PrimitiveType.Cube, t, "Lid_Lip", new Vector3(0f, 0.15f, 0f), new Vector3(0.46f, 0.02f, 0.73f), woodDark);
            Prim(PrimitiveType.Cube, t, "Handle_L", new Vector3(0f, 0.0f, -0.37f), new Vector3(0.18f, 0.04f, 0.03f), black);
            Prim(PrimitiveType.Cube, t, "Handle_R", new Vector3(0f, 0.0f, 0.37f), new Vector3(0.18f, 0.04f, 0.03f), black);
        }

        private static void AmmoCan(Transform parent, string name, Vector3 pos, float yaw)
        {
            Transform t = ArmorySetupHelper.NewGroup(parent, name);
            t.position = pos;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, t, "Body", Vector3.zero, new Vector3(0.1f, 0.19f, 0.28f), olive, true);
            Prim(PrimitiveType.Cube, t, "Lid", new Vector3(0f, 0.1f, 0f), new Vector3(0.105f, 0.015f, 0.285f), olive);
            Prim(PrimitiveType.Cube, t, "Handle", new Vector3(0f, 0.115f, 0f), new Vector3(0.02f, 0.012f, 0.12f), black);
            Prim(PrimitiveType.Cube, t, "Stencil", new Vector3(0.051f, 0.02f, 0f), new Vector3(0.002f, 0.03f, 0.16f), yellow);
        }

        private static void WoodCrate(Transform parent, string name, Vector3 pos, Vector3 size, float yaw)
        {
            Transform t = ArmorySetupHelper.NewGroup(parent, name);
            t.position = pos;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, t, "Body", Vector3.zero, size, wood, true);
            // Darker edge battens on the four vertical corners.
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0 ? -0.5f : 0.5f) * size.x, sz = (i < 2 ? -0.5f : 0.5f) * size.z;
                Prim(PrimitiveType.Cube, t, "Batten_0" + (i + 1), new Vector3(sx, 0f, sz), new Vector3(0.06f, size.y + 0.01f, 0.06f), woodDark);
            }
            Prim(PrimitiveType.Cube, t, "Band", Vector3.zero, new Vector3(size.x + 0.01f, 0.06f, size.z + 0.01f), woodDark);
        }

        // ---------------- BACK WALL: unit banner + maintenance pegboard ----------------
        private static void BuildBackWall(Transform group)
        {
            float z = WallBackZ + 0.02f;
            Prim(PrimitiveType.Cube, group, "Unit_Banner_Panel", new Vector3(0f, 2.75f, z), new Vector3(3.6f, 1.0f, 0.03f), panel);
            Prim(PrimitiveType.Cube, group, "Unit_Banner_Trim", new Vector3(0f, 2.24f, z + 0.005f), new Vector3(3.6f, 0.03f, 0.03f), yellow);
            Sign(group, "Unit_Banner_Title", "SPECIAL OPERATIONS ARMORY", new Vector3(0f, 2.92f, z + 0.025f), 180f, new Vector2(3400f, 260f), 190, new Color(1.0f, 0.72f, 0f));
            Sign(group, "Unit_Banner_Sub", "AUTHORIZED PERSONNEL ONLY  //  ALL WEAPONS CLEARED AT ALL TIMES", new Vector3(0f, 2.5f, z + 0.025f), 180f, new Vector2(3400f, 120f), 80, new Color(0.88f, 0.92f, 0.96f));

            // Weapon maintenance pegboard (left of the trainee's back).
            float px = -3.4f;
            Prim(PrimitiveType.Cube, group, "Pegboard", new Vector3(px, 1.6f, z), new Vector3(1.8f, 1.0f, 0.02f), cork);
            Sign(group, "Pegboard_Label", "WEAPON MAINTENANCE", new Vector3(px, 2.22f, z + 0.015f), 180f, new Vector2(1800f, 120f), 80, new Color(1.0f, 0.72f, 0f));
            for (int i = 0; i < 6; i++)
            {
                float x = px - 0.7f + i * 0.28f;
                Prim(PrimitiveType.Cube, group, "Tool_Handle_0" + (i + 1), new Vector3(x, 1.38f, z + 0.025f), new Vector3(0.035f, 0.14f, 0.03f), i % 2 == 0 ? red : black);
                Prim(PrimitiveType.Cube, group, "Tool_Shaft_0" + (i + 1), new Vector3(x, 1.6f, z + 0.025f), new Vector3(0.012f, 0.3f - (i % 3) * 0.05f, 0.012f), steel);
            }
            Prim(PrimitiveType.Cube, group, "Cleaning_Kit_Pouch", new Vector3(px + 0.35f, 1.9f, z + 0.04f), new Vector3(0.35f, 0.2f, 0.06f), olive);
            Prim(PrimitiveType.Cube, group, "Cleaning_Rod_Set", new Vector3(px - 0.4f, 1.9f, z + 0.025f), new Vector3(0.6f, 0.012f, 0.012f), steel);
        }

        // ---------------- FRONT WALL: range entry ----------------
        private static void BuildFrontWall(Transform group)
        {
            float z = WallFrontZ - 0.02f;

            // Above the arch: LIVE FIRE RANGE sign with RANGE HOT lamps.
            Prim(PrimitiveType.Cube, group, "Range_Sign_Panel", new Vector3(0f, 3.35f, z), new Vector3(2.6f, 0.5f, 0.03f), panel);
            Sign(group, "Range_Sign_Text", "LIVE FIRE RANGE", new Vector3(0f, 3.35f, z - 0.02f), 0f, new Vector2(2400f, 300f), 180, new Color(1.0f, 0.72f, 0f));
            Prim(PrimitiveType.Sphere, group, "Range_Hot_Lamp_L", new Vector3(-1.6f, 3.35f, z - 0.03f), Vector3.one * 0.16f, hotLamp);
            Prim(PrimitiveType.Sphere, group, "Range_Hot_Lamp_R", new Vector3(1.6f, 3.35f, z - 0.03f), Vector3.one * 0.16f, hotLamp);

            // Left of arch: area-of-operations map + safety rules.
            float mx = -3.9f;
            Prim(PrimitiveType.Cube, group, "AO_Map_Frame", new Vector3(mx, 2.1f, z), new Vector3(1.9f, 1.2f, 0.04f), black);
            Prim(PrimitiveType.Quad, group, "AO_Map", new Vector3(mx, 2.1f, z - 0.025f), new Vector3(1.8f, 1.1f, 1f), GetOrCreateTexMat("Mil_AO_Map", 512, 320, MapPixel, Vector2.one));
            Sign(group, "AO_Map_Label", "AREA OF OPERATIONS  //  GRID 47R", new Vector3(mx, 2.85f, z - 0.01f), 0f, new Vector2(1900f, 120f), 72, new Color(1.0f, 0.72f, 0f));
            Prim(PrimitiveType.Cube, group, "Safety_Rules_Panel", new Vector3(mx, 1.0f, z), new Vector3(1.9f, 0.62f, 0.03f), panel);
            Sign(group, "Safety_Rules_Text",
                "RANGE SAFETY RULES\n1. TREAT EVERY WEAPON AS LOADED\n2. NEVER POINT AT ANYTHING YOU DO NOT INTEND TO DESTROY\n3. FINGER OFF TRIGGER UNTIL ON TARGET\n4. KNOW YOUR TARGET AND WHAT IS BEYOND",
                new Vector3(mx, 1.0f, z - 0.02f), 0f, new Vector2(1800f, 560f), 58, new Color(0.88f, 0.92f, 0.96f));

            // Right of arch: clearing barrel + fire extinguisher.
            Transform barrel = ArmorySetupHelper.NewGroup(group, "Clearing_Barrel");
            Vector3 bp = new Vector3(3.5f, 0f, 6.75f);
            Prim(PrimitiveType.Cylinder, barrel, "Drum", bp + new Vector3(0f, 0.45f, 0f), new Vector3(0.55f, 0.45f, 0.55f), red, true);
            Prim(PrimitiveType.Cylinder, barrel, "Sand_Fill", bp + new Vector3(0f, 0.905f, 0f), new Vector3(0.5f, 0.005f, 0.5f), coyote);
            Prim(PrimitiveType.Cube, barrel, "Yellow_Band", bp + new Vector3(0f, 0.7f, 0f), new Vector3(0.56f, 0.05f, 0.56f), yellow);

            Transform ext = ArmorySetupHelper.NewGroup(group, "Fire_Extinguisher");
            Prim(PrimitiveType.Cube, ext, "Wall_Bracket", new Vector3(4.6f, 0.9f, z - 0.01f), new Vector3(0.18f, 0.06f, 0.02f), steel);
            Prim(PrimitiveType.Cylinder, ext, "Cylinder", new Vector3(4.6f, 0.75f, z - 0.1f), new Vector3(0.15f, 0.25f, 0.15f), red);
            Prim(PrimitiveType.Cube, ext, "Valve_Head", new Vector3(4.6f, 1.03f, z - 0.1f), new Vector3(0.05f, 0.06f, 0.08f), black);
            Sign(ext, "Fire_Label", "FIRE", new Vector3(4.6f, 1.25f, z - 0.01f), 0f, new Vector2(400f, 120f), 90, new Color(0.9f, 0.1f, 0.1f));
        }

        // ---------------- FLOOR MARKINGS + CEILING FIXTURES ----------------
        private static void BuildFloorAndCeiling(Transform group)
        {
            // Yellow walkway edge lines from the back wall to the arch, plus hazard stripes at the threshold.
            float lineLen = WallFrontZ - WallBackZ, lineZ = (WallFrontZ + WallBackZ) * 0.5f;
            Prim(PrimitiveType.Cube, group, "Walkway_Line_L", new Vector3(-2.5f, 0.002f, lineZ), new Vector3(0.08f, 0.004f, lineLen), yellow);
            Prim(PrimitiveType.Cube, group, "Walkway_Line_R", new Vector3(2.5f, 0.002f, lineZ), new Vector3(0.08f, 0.004f, lineLen), yellow);

            // Workstation box around the bench (X -0.8..0.8, Z -0.45..0.45) including the trainee stand spot at Z -0.95.
            const float boxMinX = -1.3f, boxMaxX = 1.3f, boxMinZ = -1.55f, boxMaxZ = 0.95f;
            float boxW = boxMaxX - boxMinX, boxD = boxMaxZ - boxMinZ, boxCZ = (boxMinZ + boxMaxZ) * 0.5f;
            Prim(PrimitiveType.Cube, group, "Bench_Box_Front", new Vector3(0f, 0.002f, boxMaxZ), new Vector3(boxW + 0.08f, 0.004f, 0.08f), yellow);
            Prim(PrimitiveType.Cube, group, "Bench_Box_Back", new Vector3(0f, 0.002f, boxMinZ), new Vector3(boxW + 0.08f, 0.004f, 0.08f), yellow);
            Prim(PrimitiveType.Cube, group, "Bench_Box_Left", new Vector3(boxMinX, 0.002f, boxCZ), new Vector3(0.08f, 0.004f, boxD), yellow);
            Prim(PrimitiveType.Cube, group, "Bench_Box_Right", new Vector3(boxMaxX, 0.002f, boxCZ), new Vector3(0.08f, 0.004f, boxD), yellow);
            Prim(PrimitiveType.Quad, group, "Hazard_Threshold", new Vector3(0f, 0.003f, 7.2f), new Vector3(5.0f, 0.6f, 1f),
                GetOrCreateTexMat("Mil_Hazard_Stripe", 128, 128, HazardPixel, new Vector2(8.3f, 1f)), false, new Vector3(90f, 0f, 0f));

            // Two rows of strip fixtures over the side walls (centre is covered by the bench lamp + ambient).
            float[] rowX = { -3.0f, 3.0f };
            float[] fixZ = { -0.5f, 2.5f, 5.5f };
            foreach (float x in rowX)
            {
                foreach (float z in fixZ)
                {
                    GameObject f = Prim(PrimitiveType.Cube, group, "Ceiling_Strip_" + (x < 0 ? "L" : "R") + "_" + z.ToString("0.0"), new Vector3(x, 3.97f, z), new Vector3(0.15f, 0.05f, 1.2f), fixture);
                    f.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                Transform fill = ArmorySetupHelper.NewGroup(group, "Wall_Fill_Light_" + (x < 0 ? "L" : "R"));
                fill.position = new Vector3(x * 1.15f, 3.4f, 2.5f);
                Light l = fill.gameObject.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(0.88f, 0.93f, 1.0f);
                l.intensity = 0.9f;
                l.range = 7f;
                l.shadows = LightShadows.None;
            }
        }

        // ---------------- helpers ----------------
        private static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat, bool collider = false, Vector3? euler = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // World-space UI text sign (legacy Text, depth-tested unlike TextMesh). sizeMm is in millimetres.
        private static void Sign(Transform parent, string name, string text, Vector3 worldPos, float yaw, Vector2 sizeMm, int fontSize, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * 0.001f;

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            go.GetComponent<RectTransform>().sizeDelta = sizeMm;

            GameObject txt = new GameObject("Text");
            txt.transform.SetParent(go.transform, false);
            txt.AddComponent<RectTransform>().sizeDelta = sizeMm;
            Text t = txt.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static Material GetOrCreateTexMat(string name, int w, int h, System.Func<int, int, int, int, Color> pixel, Vector2 tiling)
        {
            string matPath = "Assets/Materials/" + name + ".mat";
            string texPath = "Assets/Textures/" + name + ".png";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m != null) return m;

            if (!AssetDatabase.IsValidFolder("Assets/Textures")) AssetDatabase.CreateFolder("Assets", "Textures");
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, pixel(x, y, w, h));
            System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);

            m = new Material(Shader.Find("Standard"));
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            m.mainTextureScale = tiling;
            m.SetFloat("_Glossiness", 0.1f);
            AssetDatabase.CreateAsset(m, matPath);
            return m;
        }

        // Topographic-style map: terrain tint, contour lines, 64px grid, a few red objective markers.
        private static Color MapPixel(int x, int y, int w, int h)
        {
            float v = Mathf.Sin(x * 0.021f) + Mathf.Cos(y * 0.027f) + 0.6f * Mathf.Sin((x + y) * 0.013f) + 0.4f * Mathf.Cos((x - 2 * y) * 0.009f);
            Color c = Color.Lerp(new Color(0.62f, 0.66f, 0.50f), new Color(0.78f, 0.72f, 0.55f), (v + 3f) / 6f);
            float frac = Mathf.Repeat(v * 2.5f, 1f);
            if (frac < 0.06f) c = new Color(0.45f, 0.32f, 0.20f);
            if (x % 64 == 0 || y % 64 == 0) c = new Color(0.15f, 0.25f, 0.35f);
            Vector2[] objectives = { new Vector2(140, 210), new Vector2(330, 120), new Vector2(420, 250) };
            foreach (Vector2 o in objectives)
            {
                float d = Vector2.Distance(new Vector2(x, y), o);
                if (Mathf.Abs(d - 12f) < 2f) c = new Color(0.8f, 0.05f, 0.05f);
            }
            return c;
        }

        private static Color HazardPixel(int x, int y, int w, int h)
        {
            return ((x + y) / 32) % 2 == 0 ? new Color(1.0f, 0.78f, 0.0f) : new Color(0.05f, 0.05f, 0.05f);
        }
    }
}
#endif
