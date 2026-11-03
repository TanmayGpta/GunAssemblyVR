using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // Grabbing with tracked hands, independent of hand angle.
    //
    // The rig's own hand ray only exists while Meta reports a valid "aim" pose (palm roughly facing forward), and it
    // tilts upward while the fingers close (pinch drift), so ray-only grabbing worked at one angle and missed at the
    // rest. Instead, when the hand closes (fist: middle + ring curl, or pinch: thumb tip to index tip, both from the
    // finger joints), this picks the target itself:
    //   1. a part the hand is touching (XRI direct hover)
    //   2. else the part the hand ray is on, or was on in the last half second
    //   3. (fist only) else the grabbable part closest to where the knuckles point (cone from the palm, any angle)
    // and selects it with the hand's direct interactor. The grab is held until the hand opens.
    //
    // The direct interactor's attach point is replaced by a palm-based "pistol grip" pose: centred in the closed fist,
    // forward along the hand/forearm, up towards the thumb. The pistol's own attach point is its grip (muzzle forward,
    // slide up), so it always sits in the hand like a real pistol, and an outstretched arm aims it straight.
    //
    // The select/UI-press input is injected into the hand's controller state after the controllers read their input
    // (-29990) and before the interaction manager uses it (-105).
    [DefaultExecutionOrder(-1000)]
    public class VRHandFistGrab : MonoBehaviour
    {
        public VRHands hands;

        [Header("Gesture")]
        [Tooltip("Middle + ring finger curl (0 open .. 1 fist) that starts a grab.")]
        [Range(0f, 1f)] public float fistOn = 0.7f;
        [Tooltip("Curl below which the hand counts as open again (lets go).")]
        [Range(0f, 1f)] public float fistOff = 0.4f;
        [Tooltip("Thumb tip to index tip distance (m) that counts as a pinch.")]
        public float pinchOn = 0.02f;
        [Tooltip("Distance (m) at which the pinch is released.")]
        public float pinchOff = 0.035f;

        [Header("Targeting")]
        [Tooltip("Seconds a part stays the grab target after the hand ray slides off it.")]
        public float stickyTime = 0.5f;
        [Tooltip("Half-angle (deg) of the cone from the palm used when nothing is touched or under the ray.")]
        public float coneAngle = 15f;
        [Tooltip("Reach (m) of the palm cone.")]
        public float coneReach = 2f;

        [Header("Palm grip pose")]
        [Tooltip("How far (m) the grip centre sits in front of the palm, inside the closed fist.")]
        public float gripDepth = 0.03f;
        [Tooltip("How far (m) the grip centre sits towards the little finger from the palm centre.")]
        public float gripDrop = 0.01f;

        static readonly FieldInfo s_SelectState = typeof(XRBaseController).GetField("m_SelectInteractionState", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo s_UIPressState = typeof(XRBaseController).GetField("m_UIPressInteractionState", BindingFlags.NonPublic | BindingFlags.Instance);

        class Hand
        {
            public Handedness side;
            public XRBaseController controller;
            public XRDirectInteractor direct;
            public XRRayInteractor ray;
            public Transform palmAttach, originalAttach;
            public bool fist, pinch, wasActive, wasUIPress, palmValid;
            public IXRSelectInteractable lastHovered;
            public float lastHoveredTime = -10f;
        }

        Hand left, right;
        static bool s_LeftFist, s_RightFist;

        // True while that tracked hand is closed in a grabbing fist (not a pinch).
        public static bool IsFist(Handedness side) => side == Handedness.Left ? s_LeftFist : s_RightFist;

        static void PublishFist(Hand h)
        {
            if (h.side == Handedness.Left) s_LeftFist = h.fist;
            else s_RightFist = h.fist;
        }
        Transform trackingSpace;
        VRAssemblyGrab[] grabs;

        void Start()
        {
            XROrigin origin = hands.modality != null ? hands.modality.GetComponentInParent<XROrigin>() : null;
            if (origin == null) origin = FindObjectOfType<XROrigin>();
            trackingSpace = origin != null ? origin.CameraFloorOffsetObject.transform : null;
            grabs = FindObjectsOfType<VRAssemblyGrab>(true);
            left = Make(Handedness.Left, hands.modality != null ? hands.modality.leftHand : null);
            right = Make(Handedness.Right, hands.modality != null ? hands.modality.rightHand : null);
            if (s_SelectState == null) Debug.LogWarning("[VR Armory] Hand grab disabled: XRI select state field not found.", this);
        }

        void OnEnable() => Application.onBeforeRender += OnBeforeRender;
        void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

        Hand Make(Handedness side, GameObject handObject)
        {
            var h = new Hand { side = side };
            if (handObject == null) return h;
            h.controller = handObject.GetComponentInChildren<XRBaseController>(true);
            h.direct = handObject.GetComponentInChildren<XRDirectInteractor>(true);
            h.ray = handObject.GetComponentInChildren<XRRayInteractor>(true);
            if (h.direct != null)
            {
                h.palmAttach = new GameObject($"VR_Palm_Grip_Attach_{side}").transform;
                h.palmAttach.SetParent(transform, false);
                h.originalAttach = h.direct.attachTransform; // small parts keep the rig's own attach
            }
            return h;
        }

        void Update()
        {
            if (s_SelectState == null) return;
            Tick(left);
            Tick(right);
        }

        // Keeps the held part glued to the latest hand pose for rendering.
        void OnBeforeRender()
        {
            if (left != null) UpdatePalmAttach(left);
            if (right != null) UpdatePalmAttach(right);
        }

        void Tick(Hand h)
        {
            if (h.controller == null || h.direct == null || !h.controller.isActiveAndEnabled)
            {
                h.fist = h.pinch = h.wasActive = h.wasUIPress = false;
                PublishFist(h);
                return;
            }
            UpdatePalmAttach(h);

            float pinchDistance = PinchDistance(h.side);
            h.pinch = h.pinch ? pinchDistance < pinchOff : pinchDistance < pinchOn;
            float curl = VRHandGripPose.RawGraspCurl(h.side);
            h.fist = curl >= 0f && (h.fist ? curl > fistOff : curl > fistOn);
            PublishFist(h);

            bool rayActive = h.ray != null && h.ray.isActiveAndEnabled;
            if (rayActive && h.ray.interactablesHovered.Count > 0 && h.ray.interactablesHovered[0] is IXRSelectInteractable hovered)
            {
                h.lastHovered = hovered;
                h.lastHoveredTime = Time.time;
            }
            bool onUI = rayActive && h.ray.TryGetCurrentUIRaycastResult(out _);

            // Select: the rig's own pinch (if the runtime has it), our pinch, or a fist. A fist does not press UI.
            var state = (InteractionState)s_SelectState.GetValue(h.controller);
            bool want = state.active || h.pinch || h.fist;
            bool started = want && !h.wasActive;
            state.activatedThisFrame = started;
            state.deactivatedThisFrame = !want && h.wasActive;
            state.active = want;
            if (want) state.value = 1f;
            s_SelectState.SetValue(h.controller, state);
            h.wasActive = want;

            // The palm grip pose is only for the pistol; every other part uses the rig's own attach point.
            SetAttach(h, HoldingPistol(h));

            // At the moment the hand closes, pick the target ourselves so hand angle and ray drift do not matter.
            if (started && !onUI && !h.direct.hasSelection && !(rayActive && h.ray.hasSelection)
                && h.direct.interactionManager != null)
            {
                // The palm cone is for the fist only: a pinch pointed at the floor is a teleport, not a grab.
                if (PickTarget(h, rayActive, useCone: h.fist, out IXRSelectInteractable target, out IXRSelectInteractor by))
                {
                    if (ReferenceEquals(by, h.direct)) SetAttach(h, true); // pistol: palm grip pose from the first frame
                    h.direct.interactionManager.SelectEnter(by, target);
                }
            }

            // UI press (training panel buttons) from the pinch only.
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

        static bool IsPistol(IXRInteractable i) => i is VRAssemblyGrab g && g.wrapTrackedHand;

        bool HoldingPistol(Hand h) => h.direct.hasSelection && IsPistol(h.direct.interactablesSelected[0]);

        void SetAttach(Hand h, bool palm)
        {
            Transform wanted = palm && h.palmValid ? h.palmAttach : h.originalAttach;
            if (h.direct.attachTransform != wanted) h.direct.attachTransform = wanted;
        }

        // Pistol: picked here and held in the palm grip pose (direct interactor), from touch, the ray (also a moment
        // after it slid off) or, for a fist, the palm cone. Small parts: left to XRI's normal direct / ray grab, except
        // the sticky case where the ray slid off them just before the pinch registered (then the ray grabs them, and its
        // force grab pulls them in, as before).
        bool PickTarget(Hand h, bool rayActive, bool useCone, out IXRSelectInteractable target, out IXRSelectInteractor by)
        {
            target = null;
            by = null;
            bool recent = rayActive && h.lastHovered != null && Time.time - h.lastHoveredTime < stickyTime;

            // 1. Touching: small parts first (XRI grabs them itself); the pistol only if it is the only thing touched.
            IXRSelectInteractable touchedPistol = null;
            foreach (IXRHoverInteractable t in h.direct.interactablesHovered)
            {
                if (!(t is IXRSelectInteractable s) || !s.IsSelectableBy(h.direct)) continue;
                if (!IsPistol(s)) return false;
                touchedPistol = s;
            }
            if (touchedPistol != null && !touchedPistol.isSelected) return Pick(h.direct, touchedPistol, out target, out by);

            // 2. On (or a moment ago on) the hand ray.
            if (recent)
            {
                if (IsPistol(h.lastHovered))
                {
                    if (!h.lastHovered.isSelected && h.palmValid && h.lastHovered.IsSelectableBy(h.direct))
                        return Pick(h.direct, h.lastHovered, out target, out by);
                }
                else if (h.ray.interactablesHovered.Count == 0 && h.lastHovered.IsSelectableBy(h.ray))
                    return Pick(h.ray, h.lastHovered, out target, out by); // sticky small-part grab
                else
                    return false; // ray is on a small part right now: XRI grabs it
            }

            // 3. Fist: the pistol, if the knuckles point at it.
            if (!useCone || !h.palmValid) return false;
            foreach (VRAssemblyGrab g in grabs)
            {
                if (g == null || !g.wrapTrackedHand || !g.isActiveAndEnabled || g.isSelected || !g.IsSelectableBy((IXRSelectInteractor)h.direct)) continue;
                Vector3 to = g.transform.position - h.palmAttach.position;
                float dist = to.magnitude;
                if (dist > coneReach || dist < 1e-4f) continue;
                if (Vector3.Dot(to / dist, h.palmAttach.forward) >= Mathf.Cos(coneAngle * Mathf.Deg2Rad))
                    return Pick(h.direct, g, out target, out by);
            }
            return false;
        }

        static bool Pick(IXRSelectInteractor interactor, IXRSelectInteractable interactable, out IXRSelectInteractable target, out IXRSelectInteractor by)
        {
            target = interactable;
            by = interactor;
            return true;
        }

        // Pistol-grip pose from the palm joint (XR Hands: +Z towards the fingers, +Y out of the back of the hand):
        // forward = along the hand, up = towards the thumb, centred inside the closed fist.
        void UpdatePalmAttach(Hand h)
        {
            h.palmValid = false;
            if (h.palmAttach == null || trackingSpace == null) return;
            XRHandSubsystem subsystem = hands.Subsystem;
            if (subsystem == null) return;
            XRHand hand = h.side == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm)) return;

            Vector3 forward = palm.rotation * Vector3.forward;
            Vector3 back = palm.rotation * Vector3.up;
            Vector3 thumbSide = h.side == Handedness.Right ? Vector3.Cross(forward, back) : Vector3.Cross(back, forward);
            Vector3 position = palm.position - back * gripDepth - thumbSide * gripDrop;

            h.palmAttach.SetPositionAndRotation(
                trackingSpace.TransformPoint(position),
                trackingSpace.rotation * Quaternion.LookRotation(forward, thumbSide));
            h.palmValid = true;
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
