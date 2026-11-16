using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRArmory
{
    // One-off: makes the existing recoil spring sockets accept the spring at any turn about its length (either end
    // first) without re-staging. Only SnapSocket.symmetryAxis is set; nothing is moved or rebuilt.
    public static class SpringSymmetryApply
    {
        // Recoil springs snap only this close to the ghost (other parts: SnapSocket default 0.10).
        public const float SpringSnapDistance = 0.07f;

        [MenuItem("Tools/VR Armory/Apply Spring Symmetry")]
        static void Apply()
        {
            int done = 0;
            foreach (SnapSocket socket in Resources.FindObjectsOfTypeAll<SnapSocket>())
            {
                if (!socket.gameObject.scene.IsValid() || socket.name != "Socket_RecoilSpring" || socket.acceptedPart == null) continue;
                Transform part = socket.acceptedPart.transform;
                Vector3 axis = LongAxis(part);
                Undo.RecordObject(socket, "Apply Spring Symmetry");
                socket.symmetryAxis = axis;
                socket.snapDistance = SpringSnapDistance;
                EditorUtility.SetDirty(socket);
                EditorSceneManager.MarkSceneDirty(socket.gameObject.scene);
                Debug.Log($"[VR Armory] {socket.transform.parent?.name}/{socket.name}: symmetryAxis = {axis}, snapDistance = {SpringSnapDistance}");
                done++;
            }
            Debug.Log($"[VR Armory] Apply Spring Symmetry: {done} socket(s) updated. Save the scene.");
        }

        // The part's local axis (right/up/forward) along which its meshes are longest, measured in world units from
        // mesh corners (Renderer.bounds can be stale in the editor).
        static Vector3 LongAxis(Transform part)
        {
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            float[] min = { float.MaxValue, float.MaxValue, float.MaxValue }, max = { float.MinValue, float.MinValue, float.MinValue };
            foreach (MeshFilter mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                AssemblyPart owner = mf.GetComponentInParent<AssemblyPart>(true);
                if (owner != null && owner.transform != part) continue;   // another part seated under this one
                Bounds b = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 w = mf.transform.TransformPoint(corner);
                    for (int i = 0; i < 3; i++)
                    {
                        float d = Vector3.Dot(w, part.TransformDirection(axes[i]));
                        min[i] = Mathf.Min(min[i], d);
                        max[i] = Mathf.Max(max[i], d);
                    }
                }
            }
            int best = 0;
            for (int i = 1; i < 3; i++) if (max[i] - min[i] > max[best] - min[best]) best = i;
            return axes[best];
        }
    }
}
