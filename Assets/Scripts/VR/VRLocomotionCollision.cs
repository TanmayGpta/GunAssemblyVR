using UnityEngine;

namespace VRArmory
{
    // The rig's CharacterController must not bump into gun parts: a part held out in front would otherwise block walking.
    public class VRLocomotionCollision : MonoBehaviour
    {
        public CharacterController body;

        void Start()
        {
            if (body == null) return;
            foreach (AssemblyPart part in FindObjectsOfType<AssemblyPart>(true))
            {
                foreach (Collider c in part.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(body, c, true);
            }
        }
    }
}
