#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRArmory.Editor
{
    // Second weapon (Intratec TEC-9). The model and its textures are local-only files (see .gitignore), produced outside
    // Unity as six parts: 01_Lower_Frame, 02_Upper_Receiver, 03_Bolt, 04_Recoil_Spring, 05_Takedown_Pin, 06_32Rd_Magazine.
    // This sets the texture import types, builds the five materials, assigns them to the model and drops a preview copy
    // in the scene. Safe to re-run.
    public static class Tec9ModelSetup
    {
        public const string ModelPath = "Assets/Models/tec9.fbx";
        const string TextureDir = "Assets/Models/tec9_textures";
        const string MaterialDir = "Assets/Models/tec9_materials";
        const string PreviewName = "TEC9_Preview";

        static readonly string[] Sets = { "Lower", "Receiver", "Barrel", "Mag", "Mechanism" };

        [MenuItem("Tools/VR Armory/TEC-9/1. Setup Model (materials + preview)")]
        public static void FromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ModelPath))
            {
                Debug.LogError("TEC-9 setup: " + ModelPath + " is missing (it is a local-only file).");
                return;
            }

            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets/Models", "tec9_materials");

            var model = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            model.bakeAxisConversion = true;
            model.importAnimation = false;
            model.isReadable = true; // the controller hand fits its fingers to the Grip_Body / Trigger vertices
            model.importCameras = false;
            model.importLights = false;

            foreach (var set in Sets)
            {
                var material = BuildMaterial(set);
                model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "TEC9_" + set), material);
            }

            AssetDatabase.SaveAssets();
            model.SaveAndReimport();
            Debug.Log("TEC-9 setup: materials assigned. Next: Tools/VR Armory/TEC-9/2. Stage Parts on Bench.");
        }

        static Material BuildMaterial(string set)
        {
            string path = MaterialDir + "/TEC9_" + set + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_MainTex", LoadTexture(set, "BaseColor", false, false));
            material.SetColor("_Color", Color.white);

            material.SetTexture("_BumpMap", LoadTexture(set, "Normal", true, false));
            material.EnableKeyword("_NORMALMAP");

            // Metallic in RGB, smoothness in alpha.
            material.SetTexture("_MetallicGlossMap", LoadTexture(set, "MetallicSmoothness", false, true));
            material.EnableKeyword("_METALLICGLOSSMAP");
            material.SetFloat("_GlossMapScale", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);

            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D LoadTexture(string set, string kind, bool normal, bool linear)
        {
            string path = TextureDir + "/TEC9_" + set + "_" + kind + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("TEC-9 setup: texture missing: " + path);
                return null;
            }

            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.sRGBTexture == linear)
            {
                importer.textureType = type;
                importer.sRGBTexture = !linear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- Parts on the bench ----------

        const string GroupName = "Staged_TEC9_Parts";
        const string GhostMaterialPath = "Assets/Materials/Assembly_Ghost.mat";
        const float RealLength = 0.317f;          // m, TEC-9 overall length
        // Hold point, measured on the model: so far behind the muzzle tip and above the magazine's base that the trigger
        // lands where it does in the Beretta's hold. Tune here if the hand sits wrong, then re-run this menu.
        const float HoldBehindMuzzle = 0.267f;
        const float HoldAboveBottom = 0.177f;
        // World scale of the Beretta's VR_Grip_Attach: VRControllerHands' thumb values are in that attach's units.
        const float BerettaGripScale = 2.1903f;
        const float MatMargin = 0.08f;
        const float MinColliderSize = 0.03f;      // m, so the small pin is easy to grab

        static readonly (string name, GunPart type)[] PartList =
        {
            ("01_Lower_Frame", GunPart.LowerFrame),
            ("02_Upper_Receiver", GunPart.UpperReceiver),
            ("03_Bolt", GunPart.Bolt),
            ("04_Recoil_Spring", GunPart.RecoilSpring),
            ("05_Takedown_Pin", GunPart.TakedownPin),
            ("06_32Rd_Magazine", GunPart.Magazine),
        };

        [MenuItem("Tools/VR Armory/TEC-9/2. Stage Parts on Bench")]
        public static void StageFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!Stage()) return;
            VRSceneSetup.SetupFromMenu(); // grabs, the TEC-9's weapon card, kit switch
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // Rebuilds Staged_TEC9_Parts: the six parts lying on the mat, a socket for each step on the part it seats into,
        // the hand's hold point on the frame, and the weapon kit with its steps.
        static bool Stage()
        {
            GameObject station = GameObject.Find("ArmorerStation");
            Transform mat = station != null ? station.transform.Find("Workbench/Armorer_Rubber_Mat") : null;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Material ghost = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
            if (mat == null || prefab == null)
            {
                Debug.LogError("TEC-9 staging needs ArmorerStation/Workbench/Armorer_Rubber_Mat and " + ModelPath + ".");
                return false;
            }

            Transform old = station.transform.Find(GroupName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform group = new GameObject(GroupName).transform;
            group.SetParent(station.transform, false);
            group.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Assembled gun at real size, at the world origin.
            GameObject model = (GameObject)Object.Instantiate(prefab);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Bounds all = WorldBounds(model.transform);
            model.transform.localScale *= RealLength / Mathf.Max(all.size.x, all.size.y, all.size.z);

            var parts = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var (name, _) in PartList)
            {
                Transform t = model.transform.Find(name);
                if (t == null)
                {
                    Debug.LogError("TEC-9 staging: part " + name + " not found in " + ModelPath + ".");
                    Object.DestroyImmediate(model);
                    Object.DestroyImmediate(group.gameObject);
                    return false;
                }
                parts[name] = t;
            }
            Transform lower = parts["01_Lower_Frame"], upper = parts["02_Upper_Receiver"], mag = parts["06_32Rd_Magazine"];

            // Gun axes: muzzle = the upper's long axis, pointing away from the frame; up = magazine -> upper.
            all = WorldBounds(model.transform);
            Bounds ub = WorldBounds(upper), lb = WorldBounds(lower), mb = WorldBounds(mag);
            Vector3 muzzle = LongestAxis(ub.size);
            if (Vector3.Dot(ub.center - lb.center, muzzle) < 0f) muzzle = -muzzle;
            Vector3 up = Cardinal(Vector3.ProjectOnPlane(ub.center - mb.center, muzzle));
            Vector3 lateral = Vector3.Cross(muzzle, up).normalized;

            // Hand hold point on the frame, facing the muzzle, scaled like the Beretta's.
            float tip = Vector3.Dot(all.center, muzzle) + Vector3.Dot(all.extents, Abs(muzzle));
            float bottom = Vector3.Dot(all.center, up) - Vector3.Dot(all.extents, Abs(up));
            Vector3 hold = muzzle * (tip - HoldBehindMuzzle) + up * (bottom + HoldAboveBottom) + lateral * Vector3.Dot(lb.center, lateral);
            Transform attach = new GameObject("VR_Grip_Attach").transform;
            attach.SetParent(lower, false);
            attach.SetPositionAndRotation(hold, Quaternion.LookRotation(muzzle, up));
            attach.localScale = Vector3.one * (BerettaGripScale / lower.lossyScale.x);

            // Sockets: the seated pose is where the part sits in the assembled model.
            (string socket, string host, string part, Vector3 from)[] sockets =
            {
                ("Socket_Bolt", "02_Upper_Receiver", "03_Bolt", -muzzle),
                ("Socket_RecoilSpring", "02_Upper_Receiver", "04_Recoil_Spring", -muzzle),
                ("Socket_Upper", "01_Lower_Frame", "02_Upper_Receiver", up),
                ("Socket_Pin", "01_Lower_Frame", "05_Takedown_Pin", lateral),
                ("Socket_Magazine", "01_Lower_Frame", "06_32Rd_Magazine", -up),
            };
            var assemblyParts = new System.Collections.Generic.Dictionary<string, AssemblyPart>();
            foreach (var (name, type) in PartList)
            {
                AssemblyPart ap = parts[name].gameObject.AddComponent<AssemblyPart>();
                ap.partType = type;
                assemblyParts[name] = ap;
            }
            var made = new System.Collections.Generic.Dictionary<string, SnapSocket>();
            foreach (var s in sockets)
            {
                Transform host = parts[s.host], part = parts[s.part];
                Transform socketT = new GameObject(s.socket).transform;
                socketT.SetParent(host, false);
                Matrix4x4 m = host.worldToLocalMatrix * part.localToWorldMatrix;
                socketT.localPosition = m.GetColumn(3);
                socketT.localRotation = m.rotation;
                socketT.localScale = m.lossyScale;
                SnapSocket socket = socketT.gameObject.AddComponent<SnapSocket>();
                socket.acceptedPart = assemblyParts[s.part];
                socket.ghostTemplate = ghost;
                socket.insertionDirection = socketT.InverseTransformDirection(s.from);
                made[s.socket] = socket;
            }

            // Weapon kit (axes in frame space for the card picture, taken while the gun is still assembled).
            WeaponKit kit = group.gameObject.AddComponent<WeaponKit>();
            kit.id = VRTrainingText.Tec9Id;
            kit.displayName = VRTrainingText.Tec9Name;
            kit.hudSubtitle = VRTrainingText.Tec9HudSubtitle;
            kit.frame = assemblyParts["01_Lower_Frame"];
            kit.muzzleLocal = lower.InverseTransformDirection(muzzle).normalized;
            kit.upLocal = lower.InverseTransformDirection(up).normalized;
            kit.steps = new System.Collections.Generic.List<AssemblyStep>
            {
                new AssemblyStep { title = "BOLT INTO RECEIVER", part = GunPart.Bolt, socket = made["Socket_Bolt"],
                    instruction = "SLIDE THE BOLT INTO THE BACK OF THE RECEIVER TUBE" },
                new AssemblyStep { title = "RECOIL SPRING UNIT", part = GunPart.RecoilSpring, socket = made["Socket_RecoilSpring"],
                    instruction = "INSERT THE RECOIL SPRING UNIT BEHIND THE BOLT" },
                new AssemblyStep { title = "UPPER ON LOWER", part = GunPart.UpperReceiver, socket = made["Socket_Upper"],
                    instruction = "SET THE UPPER RECEIVER ONTO THE FRAME" },
                new AssemblyStep { title = "TAKEDOWN PIN", part = GunPart.TakedownPin, socket = made["Socket_Pin"],
                    instruction = "PUSH THE TAKEDOWN PIN THROUGH THE FRONT LUG" },
                new AssemblyStep { title = "MAGAZINE INSERTED", part = GunPart.Magazine, socket = made["Socket_Magazine"],
                    instruction = "INSERT THE 32-ROUND MAGAZINE INTO THE MAGAZINE WELL" },
            };

            // Onto the bench: lying on its side, muzzle downrange (+Z), one row across the mat in part order.
            Quaternion lay = Quaternion.Inverse(Quaternion.LookRotation(muzzle, lateral));
            Bounds matBounds = mat.GetComponent<Renderer>().bounds;
            var order = new System.Collections.Generic.List<Transform>();
            foreach (var (name, _) in PartList)
            {
                Transform t = parts[name];
                t.SetParent(group, true);
                t.rotation = lay * t.rotation;
                order.Add(t);
            }
            Object.DestroyImmediate(model);

            float total = 0f;
            foreach (Transform t in order) total += WorldBounds(t).size.x;
            float usable = matBounds.size.x - 2f * MatMargin;
            float gap = Mathf.Max(0.03f, (usable - total) / (order.Count - 1));
            float cursor = matBounds.center.x - (total + gap * (order.Count - 1)) * 0.5f;
            foreach (Transform t in order)
            {
                Bounds b = WorldBounds(t);
                t.position += new Vector3(cursor + b.size.x * 0.5f - b.center.x, matBounds.max.y + 0.001f - b.min.y, matBounds.center.z - b.center.z);
                cursor += b.size.x + gap;

                AssemblySetup.FitCollider(t);
                BoxCollider box = t.GetComponent<BoxCollider>();
                if (box != null)
                {
                    float min = MinColliderSize / Mathf.Max(1e-6f, t.lossyScale.x);
                    box.size = Vector3.Max(box.size, Vector3.one * min);
                }
                Rigidbody rb = t.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
            }

            Debug.Log($"TEC-9 staging: {order.Count} parts on the bench, {made.Count} sockets, mat {matBounds.size.x:0.00} m wide, parts {total:0.00} m, gap {gap * 100f:0.0} cm.");
            return true;
        }

        // From mesh corners: Renderer.bounds can be stale right after objects are created or moved in edit mode.
        static Bounds WorldBounds(Transform root)
        {
            bool any = false;
            Bounds b = default;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = mf.transform.TransformPoint(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        static Vector3 LongestAxis(Vector3 s) => s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;

        static Vector3 Cardinal(Vector3 v)
        {
            Vector3 a = Abs(v);
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            return a.y >= a.z ? new Vector3(0f, Mathf.Sign(v.y), 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // A loose copy to look at. Not part of the lesson; re-running replaces it.
        static void PlacePreview()
        {
            var old = GameObject.Find(PreviewName);
            if (old != null) Object.DestroyImmediate(old);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var preview = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            preview.name = PreviewName;

            var view = SceneView.lastActiveSceneView;
            if (view != null) preview.transform.position = view.pivot;

            Selection.activeGameObject = preview;
            if (view != null) view.FrameSelected();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }
}
#endif
