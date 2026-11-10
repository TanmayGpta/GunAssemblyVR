#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VRArmory.Editor
{
    // Temporary measuring tool: prints how the Beretta's grip, trigger and hold point relate, in the hold point's own
    // axes (x right, y up, z muzzle, metres), plus the hand placement values saved in the scene.
    public static class GripProbe
    {
        [MenuItem("Tools/VR Armory/Debug/Probe Grip")]
        public static void Probe()
        {
            var sb = new StringBuilder("GRIP PROBE\n");
            foreach (AssemblyPart part in Object.FindObjectsOfType<AssemblyPart>(true))
            {
                Transform attach = part.transform.Find("VR_Grip_Attach");
                if (attach == null) continue;
                sb.AppendLine($"frame {part.name} scale {part.transform.lossyScale.x:F4} attachLocalPos {attach.localPosition.ToString("F4")} attachLocalEuler {attach.localEulerAngles.ToString("F1")}");

                Vector3 gMin = Vector3.one * 9f, gMax = Vector3.one * -9f;
                const int slices = 8;
                const float y0 = -0.09f, step = 0.02f;
                var zMin = new float[slices];
                var zMax = new float[slices];
                var xHalf = new float[slices];
                for (int i = 0; i < slices; i++) { zMin[i] = 9f; zMax[i] = -9f; }

                foreach (MeshFilter mf in part.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    bool grip = mf.name.StartsWith("Grip_");
                    bool named = grip || mf.name.StartsWith("Trigger") || mf.name == "Hammer";
                    Vector3 min = Vector3.one * 9f, max = Vector3.one * -9f;
                    foreach (Vector3 v in mf.sharedMesh.vertices)
                    {
                        Vector3 p = attach.InverseTransformPoint(mf.transform.TransformPoint(v)) * attach.lossyScale.x;
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                        if (!grip) continue;
                        int s = Mathf.FloorToInt((p.y - y0) / step);
                        if (s < 0 || s >= slices) continue;
                        zMin[s] = Mathf.Min(zMin[s], p.z);
                        zMax[s] = Mathf.Max(zMax[s], p.z);
                        xHalf[s] = Mathf.Max(xHalf[s], Mathf.Abs(p.x));
                    }
                    if (grip) { gMin = Vector3.Min(gMin, min); gMax = Vector3.Max(gMax, max); }
                    if (named) sb.AppendLine($"  {mf.name,-26} min {min.ToString("F3")} max {max.ToString("F3")} parent {mf.transform.parent.name}");
                }
                sb.AppendLine($"  ALL Grip_ min {gMin.ToString("F3")} max {gMax.ToString("F3")}");
                for (int i = slices - 1; i >= 0; i--)
                    if (zMax[i] > -8f)
                        sb.AppendLine($"  slice y {y0 + i * step:F2}..{y0 + (i + 1) * step:F2}: back z {zMin[i]:F3} front z {zMax[i]:F3} halfwidth {xHalf[i]:F3}");
            }

            foreach (AssemblyPart part in Object.FindObjectsOfType<AssemblyPart>(true))
            {
                MeshFilter mf = part.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                sb.AppendLine($"part {part.name} lossy {part.transform.lossyScale.ToString("F4")} meshSize {mf.sharedMesh.bounds.size.ToString("F4")} world {Vector3.Scale(mf.sharedMesh.bounds.size, part.transform.lossyScale).ToString("F3")}");
            }
            foreach (VRControllerHands h in Object.FindObjectsOfType<VRControllerHands>(true))
                sb.AppendLine($"hands palmDepth {h.palmDepth} positionOffset {h.positionOffset.ToString("F3")} rotationOffset {h.rotationOffset} pistolRotationOffset {h.pistolRotationOffset} pitchDown {h.pitchDown} pistolHoldOffset {h.pistolHoldOffset.ToString("F3")} gripYaw {h.gripYaw} flipPalm {h.flipPalm} flipFingers {h.flipFingers} thumbDropFromTop {h.thumbDropFromTop}");
            Debug.Log(sb.ToString());
        }
    }
}
#endif
