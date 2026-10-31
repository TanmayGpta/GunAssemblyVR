#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VRArmory.Editor
{
    // The Tools/VR Armory builders generated the current scene and some of them destroy and rebuild objects.
    // Every builder asks first, so a stray menu click can't silently undo hand-tuned or VR work.
    // Canonical order on a fresh scene (see VR_VERSION1.md): Setup Workbench and Gun Parts -> Build Armory Set Dressing
    // -> Build Live-Fire Range -> Setup Phase 02 Assembly -> Re-Stage Gun Parts -> VR / Setup VR Scene.
    public static class ArmoryEditorGuard
    {
        public static bool Confirm(string menuName, string consequences)
        {
            if (Application.isBatchMode) return true; // scripted runs pass their intent on the command line
            return EditorUtility.DisplayDialog(
                "VR Armory: " + menuName,
                consequences + "\n\nThe current scene is already built. Save or commit first so you can undo this.\n\nRun it anyway?",
                "Run", "Cancel");
        }

        // A module that already carries an AssemblyPart was laid out by "Re-Stage Gun Parts";
        // the older workbench builder must not move or rescale it again.
        public static bool IsStaged(Transform module)
        {
            return module != null && module.GetComponent<AssemblyPart>() != null;
        }
    }
}
#endif
