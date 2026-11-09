#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VRArmory.Editor
{
    // Import settings for the Mixamo soldier in Assets/Characters/ (model + Idle / Talking / Salute animations):
    //  - every file is Humanoid (its own avatar), so the separately downloaded animations drive the model;
    //  - Idle and Talking loop, Salute plays once;
    //  - root motion is baked into the pose, so the soldier never drifts or turns away while he talks.
    // Applied on every (re)import; IntroSceneSetup reimports files that were imported before this existed.
    public class SoldierImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/Characters";
        public const string ModelPath = Folder + "/Ch15_nonPBR.fbx";
        public const string IdlePath = Folder + "/Idle.fbx";
        public const string TalkingPath = Folder + "/Talking.fbx";
        public const string SalutePath = Folder + "/Salute.fbx";
        public const string TextureFolder = Folder + "/Textures"; // unpacked from the model (git-ignored like the model)
        const int MaxTextureSize = 2048; // Quest: plenty for a character 2-3 m away

        static bool IsSoldierFile(string path) => path.StartsWith(Folder + "/") && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!IsSoldierFile(assetPath)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = assetPath != ModelPath;
        }

        // The textures unpacked from the model: Quest-sized, normal maps marked as such.
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureFolder + "/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = MaxTextureSize;
            if (assetPath.Contains("_Normal")) importer.textureType = TextureImporterType.NormalMap;
        }

        // Mixamo embeds the textures inside the FBX; Unity does not unpack them on its own, so the soldier imports white.
        // Unpacks them once into TextureFolder and reimports the model so its materials find them.
        public static void ExtractTextures()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) return;
            if (AssetDatabase.IsValidFolder(TextureFolder) && AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }).Length > 0) return;
            if (!AssetDatabase.IsValidFolder(TextureFolder)) AssetDatabase.CreateFolder(Folder, "Textures");
            if (!importer.ExtractTextures(TextureFolder))
            {
                Debug.LogWarning($"[VR Armory] No embedded textures found in {ModelPath}: download it again from Mixamo as \"FBX for Unity\".");
                return;
            }
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        }

        void OnPreprocessAnimation()
        {
            if (!IsSoldierFile(assetPath) || assetPath == ModelPath) return;
            var importer = (ModelImporter)assetImporter;
            bool loop = assetPath != SalutePath;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        public static bool NeedsReimport(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            return importer != null && importer.animationType != ModelImporterAnimationType.Human;
        }

        // The first animation clip in an FBX (Mixamo names it "mixamo.com").
        public static AnimationClip LoadClip(string path)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
            }
            return null;
        }
    }
}
#endif
