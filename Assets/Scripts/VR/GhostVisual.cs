using UnityEngine;

namespace VRArmory
{
    // Marks a demonstration-only copy of a gun part (made by VRGhostDemo). It has meshes but no collider or script, so
    // nothing can grab it; scripts that measure or highlight the real parts (VRGuidance) skip anything under it.
    [DisallowMultipleComponent]
    public class GhostVisual : MonoBehaviour
    {
    }
}
