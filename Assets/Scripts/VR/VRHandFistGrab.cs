using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // Real-life grab for tracked hands: close the hand (middle + ring fingers curl) around a part within reach and it
    // is held until the hand opens. Pinch still works as well. Implemented as an extra "select" input on the hand's
    // controller: it runs after the controllers read their input (-29990) and before the interaction manager uses it
    // (-105), so XRI's normal direct grab does the rest (attach points, ease-in, sockets, two hands).
    // Only near grabs: a fist never grabs through the ray, so it cannot pull a far part by accident.
    // It also adds its own pinch (thumb tip to index tip, from the joints) to select and UI press. The rig's own pinch
    // comes from the Meta hand-aim extension, which is missing on some runtimes (e.g. Quest Link), so without this a
    // pinch would grab or click nothing there.
    [DefaultExecutionOrder(-1000)]
    public class VRHandFistGrab : MonoBehaviour
    {
        public VRHands hands;

        [Tooltip("Middle + ring finger curl (0 open .. 1 fist) that starts a grab.")]
        [Range(0f, 1f)] public float fistOn = 0.55f;
        [Tooltip("Curl below which the hand counts as open again (lets go).")]
        [Range(0f, 1f)] public float fistOff = 0.35f;
        [Tooltip("Thumb tip to index tip distance (m) that counts as a pinch.")]
        public float pinchOn = 0.02f;
        [Tooltip("Distance (m) at which the pinch is released.")]
        public float pinchOff = 0.035f;

        // XRBaseController keeps the select state the interactors read in a private field, copied from the input once
        // per frame; this is the only way to add a second select source without replacing the rig's components.
        static readonly FieldInfo s_SelectState = typeof(XRBaseController).GetField("m_SelectInteractionState", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo s_UIPressState = typeof(XRBaseController).GetField("m_UIPressInteractionState", BindingFlags.NonPublic | BindingFlags.Instance);

        class Hand
        {
            public Handedness side;
            public XRBaseController controller;
            public XRDirectInteractor direct;
            public bool fist, pinch, wasActive, wasUIPress;
        }

        Hand left, right;

        void Start()
        {
            left = Make(Handedness.Left, hands.modality != null ? hands.modality.leftHand : null);
            right = Make(Handedness.Right, hands.modality != null ? hands.modality.rightHand : null);
            if (s_SelectState == null) Debug.LogWarning("[VR Armory] Fist grab disabled: XRI select state field not found.", this);
        }

        static Hand Make(Handedness side, GameObject handObject)
        {
            var h = new Hand { side = side };
            if (handObject != null)
            {
                h.controller = handObject.GetComponentInChildren<XRBaseController>(true);
                h.direct = handObject.GetComponentInChildren<XRDirectInteractor>(true);
            }
            return h;
        }

        void Update()
        {
            if (s_SelectState == null) return;
            Tick(left);
            Tick(right);
        }

        void Tick(Hand h)
        {
            if (h.controller == null || h.direct == null || !h.controller.isActiveAndEnabled)
            {
                h.fist = h.pinch = h.wasActive = h.wasUIPress = false;
                return;
            }

            float pinchDistance = PinchDistance(h.side);
            h.pinch = h.pinch ? pinchDistance < pinchOff : pinchDistance < pinchOn;

            float curl = VRHandGripPose.RawGraspCurl(h.side);
            h.fist = curl >= 0f && (h.fist ? curl > fistOff : curl > fistOn);
            bool fistGrab = h.fist && (h.direct.hasSelection || h.direct.hasHover);

            var state = (InteractionState)s_SelectState.GetValue(h.controller);
            bool want = state.active || fistGrab || h.pinch; // state.active is the rig's own pinch, if the runtime has it
            // Edges are recomputed from our own history, since the controller's edges only know about the pinch.
            state.activatedThisFrame = want && !h.wasActive;
            state.deactivatedThisFrame = !want && h.wasActive;
            state.active = want;
            if (fistGrab) state.value = 1f;
            s_SelectState.SetValue(h.controller, state);
            h.wasActive = want;

            // UI press (buttons on the training panel) from the same pinch.
            if (s_UIPressState == null) return;
            var ui = (InteractionState)s_UIPressState.GetValue(h.controller);
            bool press = ui.active || h.pinch;
            ui.activatedThisFrame = press && !h.wasUIPress;
            ui.deactivatedThisFrame = !press && h.wasUIPress;
            ui.active = press;
            if (h.pinch) ui.value = 1f;
            s_UIPressState.SetValue(h.controller, ui);
            h.wasUIPress = press;
        }

        float PinchDistance(Handedness side)
        {
            XRHandSubsystem subsystem = hands.Subsystem;
            if (subsystem == null) return float.MaxValue;
            XRHand hand = side == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked) return float.MaxValue;
            if (!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb)) return float.MaxValue;
            if (!hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index)) return float.MaxValue;
            return Vector3.Distance(thumb.position, index.position);
        }
    }
}
