using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VRArmory
{
    // Keeps joystick locomotion working while a part is held:
    //  - the player's CharacterController never collides with gun parts. Physics.IgnoreCollision is silently reset when
    //    a collider is toggled or re-parented, and force grab pulls parts right up against the body, so the ignore is
    //    re-applied every frame for every part collider (a handful of cheap calls).
    //  - the stick actions stay enabled: the left-stick move and right-stick turn actions are switched back on if
    //    anything (e.g. the Starter Assets controller manager) turned them off while something is held.
    public class VRLocomotionCollision : MonoBehaviour
    {
        public CharacterController body;
        [Tooltip("Locomotion actions that must stay enabled (left move, right turn).")]
        public InputActionReference[] keepEnabled;

        readonly List<Collider> partColliders = new List<Collider>();
        AssemblyPart[] parts;
        float nextRefresh;

        void Start()
        {
            parts = FindObjectsOfType<AssemblyPart>(true);
            Refresh();
        }

        // Picks up colliders added at runtime (e.g. the slide rack grip and safety zone on the frame).
        void Refresh()
        {
            partColliders.Clear();
            foreach (AssemblyPart part in parts)
                if (part != null) partColliders.AddRange(part.GetComponentsInChildren<Collider>(true));
            nextRefresh = Time.time + 1f;
        }

        void LateUpdate()
        {
            if (Time.time >= nextRefresh) Refresh();

            if (body != null)
            {
                foreach (Collider c in partColliders)
                    if (c != null && c.enabled && !c.isTrigger) Physics.IgnoreCollision(body, c, true);
            }

            if (keepEnabled == null) return;
            foreach (InputActionReference r in keepEnabled)
            {
                InputAction a = r != null ? r.action : null;
                if (a != null && !a.enabled) a.Enable();
            }
        }
    }
}
