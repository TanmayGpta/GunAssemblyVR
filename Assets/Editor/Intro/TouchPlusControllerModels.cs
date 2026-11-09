#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRArmory.Editor
{
    // Meta Quest Touch Plus controller models (Quest 3 / 3S) from Meta's "Oculus Controller Art" package, for the
    // Intro controller panel.
    // Each Meta model is ONE skinned mesh whose buttons are bones, so a single button cannot be lit on its own.
    // BuildPartsMesh splits it by bone weights into a static mesh with one sub-mesh per control:
    //   0 = body, 1 = grip, 2 = trigger, 3 = thumbstick, 4 = A / X, 5 = B / Y   (= ControllerGuidePanel.Control)
    // so ControllerGuidePanel can colour each control through its own material slot.
    public class TouchPlusControllerModels : AssetPostprocessor
    {
        public const string Folder = "Assets/Models/MetaQuestTouchPlus";
        public const string GeneratedFolder = Folder + "/Generated";
        public static string ModelPath(bool left) => $"{Folder}/MetaQuestTouchPlus_{(left ? "Left" : "Right")}.fbx";
        public static string BaseColorPath(bool left) => $"{Folder}/Textures/MetaQuestTouchPlus_{(left ? "Left" : "right")}_BaseColor_AO.png";
        public const string NormalPath = Folder + "/Textures/MetaQuestTouchPlus_Normals.png";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder + "/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true; // the split reads the bone weights
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // the panel uses its own materials
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder + "/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 1024; // a 20 cm model on a panel
            if (assetPath.Contains("_Normals")) importer.textureType = TextureImporterType.NormalMap;
        }

        static int PartOf(string bone)
        {
            string n = bone.ToLowerInvariant();
            if (n.Contains("trigger_grip")) return 1;
            if (n.Contains("trigger_front")) return 2;
            if (n.Contains("thumbstick")) return 3;
            if (n.Contains("button_a") || n.Contains("button_x")) return 4;
            if (n.Contains("button_b") || n.Contains("button_y")) return 5;
            return 0; // controller body, menu / Meta button
        }

        public const int PartCount = 6;

        // The split mesh (saved under Generated/) and where it sits relative to the model root.
        public static Mesh BuildPartsMesh(bool left, out Matrix4x4 meshToRoot)
        {
            meshToRoot = Matrix4x4.identity;
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(left));
            if (asset == null) return null;
            SkinnedMeshRenderer smr = null;
            foreach (SkinnedMeshRenderer s in asset.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (s.name.Contains("oculus_controller")) smr = s; // not the battery indicator quad
            }
            if (smr == null || smr.sharedMesh == null) return null;
            Mesh src = smr.sharedMesh;
            meshToRoot = asset.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;

            // Each vertex belongs to the control of its strongest bone (body when no bone clearly owns it).
            Transform[] bones = smr.bones;
            BoneWeight[] weights = src.boneWeights;
            var vertexPart = new int[src.vertexCount];
            for (int v = 0; v < vertexPart.Length && v < weights.Length; v++)
            {
                BoneWeight w = weights[v];
                vertexPart[v] = w.weight0 >= 0.5f && w.boneIndex0 < bones.Length && bones[w.boneIndex0] != null ? PartOf(bones[w.boneIndex0].name) : 0;
            }

            // A triangle belongs to a control when at least two of its corners do.
            var tris = new List<int>[PartCount];
            for (int i = 0; i < PartCount; i++) tris[i] = new List<int>();
            for (int sub = 0; sub < src.subMeshCount; sub++)
            {
                int[] t = src.GetTriangles(sub);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = vertexPart[t[i]], b = vertexPart[t[i + 1]], c = vertexPart[t[i + 2]];
                    int part = a != 0 && (a == b || a == c) ? a : (b != 0 && b == c ? b : 0);
                    tris[part].Add(t[i]);
                    tris[part].Add(t[i + 1]);
                    tris[part].Add(t[i + 2]);
                }
            }

            var mesh = new Mesh { name = $"TouchPlus_{(left ? "Left" : "Right")}_Parts" };
            mesh.indexFormat = src.indexFormat;
            mesh.vertices = src.vertices;
            mesh.normals = src.normals;
            mesh.tangents = src.tangents;
            mesh.uv = src.uv;
            mesh.subMeshCount = PartCount;
            for (int i = 0; i < PartCount; i++) mesh.SetTriangles(tris[i], i, true);
            mesh.RecalculateBounds();

            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder(Folder, "Generated");
            string path = $"{GeneratedFolder}/{mesh.name}.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
                EditorUtility.SetDirty(mesh);
            }
            else AssetDatabase.CreateAsset(mesh, path);

            var counts = new string[PartCount];
            for (int i = 0; i < PartCount; i++) counts[i] = (tris[i].Count / 3).ToString();
            Debug.Log($"[VR Armory] {mesh.name}: triangles body/grip/trigger/stick/A-X/B-Y = {string.Join("/", counts)}");
            return mesh;
        }
    }
}
#endif
