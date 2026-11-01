using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace VRArmory
{
    // Placement help for one gun part in VR. VRGuidance adds it at runtime, so it never exists in desktop mode.
    //
    // ASSISTANCE (feel only): it runs after XRGeneralGrabTransformer in the grab pipeline. While the part is near its
    // own unlocked socket, the pose that follows the hand is pulled part of the way toward the seat:
    //   distance shown = assistRadius * x^2 * (2 - x),  x = hand distance / assistRadius
    // which is smooth and always grows with the hand's distance, so the part never jumps or moves against the hand.
    // Rotation is pulled the same way, but only when the angle is already below rotationAssistAngle.
    //
    // VALIDATION (unchanged): it never seats a part itself. When SnapSocket reports Valid (its own 6 cm / 20 deg check)
    // for seatDwell seconds, the hands let go and the normal release path (AssemblyPart.EndGrab -> SnapSocket)
    // validates and seats the part. Wrong parts and out-of-sequence parts are never pulled: their socket is not unlocked.
    [RequireComponent(typeof(AssemblyPart))]
    public class VRPlacementAssist : XRBaseGrabTransformer
    {
        [Tooltip("The pull starts at this distance from the seat, in metres.")]
        public float assistRadius = 0.15f;
        [Tooltip("Rotation is only helped when it is already within this angle of the seat.")]
        public float rotationAssistAngle = 45f;
        [Tooltip("How long the part must stay aligned before it clicks in (no need to let go of the grip).")]
        public float seatDwell = 0.25f;
        public bool autoSeat = true;

        public SnapSocket Socket { get; private set; }
        // How strongly the position was pulled in the last processed frame (0 = not at all, 1 = fully onto the seat).
        public float PositionPull { get; private set; }
        public bool IsAssisting => PositionPull > 0f;

        protected override RegistrationMode registrationMode => RegistrationMode.SingleAndMultiple; // one or two hands

        private AssemblyPart part;
        private XRGrabInteractable grab;
        private float alignedTime;

        void Awake()
        {
            part = GetComponent<AssemblyPart>();
            grab = GetComponent<XRGrabInteractable>();
        }

        // Only while this part's socket is the active step.
        private bool HasActiveSocket()
        {
            if (Socket == null)
            {
                foreach (SnapSocket s in SnapSocket.All)
                {
                    if (s.acceptedPart == part) Socket = s;
                }
            }
            return Socket != null && Socket.IsUnlocked && !Socket.IsFilled;
        }

        public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
        {
            PositionPull = 0f;
            if (!HasActiveSocket()) return;

            Vector3 seatPos = Socket.transform.position;
            Quaternion seatRot = Socket.transform.rotation;
            float d = Vector3.Distance(targetPose.position, seatPos);
            if (d >= assistRadius) return;

            float x = d / assistRadius;
            float pull = (1f - x) * (1f - x);
            targetPose.position = Vector3.Lerp(targetPose.position, seatPos, pull);
            PositionPull = pull;

            float angle = Quaternion.Angle(targetPose.rotation, seatRot);
            if (angle < rotationAssistAngle)
            {
                float near = Mathf.Clamp01((assistRadius - d) / (assistRadius - Socket.snapDistance));
                float y = angle / rotationAssistAngle;
                targetPose.rotation = Quaternion.Slerp(targetPose.rotation, seatRot, (1f - y) * (1f - y) * near);
            }
        }

        void Update()
        {
            if (!autoSeat || grab == null || !grab.isSelected || !HasActiveSocket())
            {
                alignedTime = 0f;
                return;
            }
            alignedTime = Socket.CurrentAlignment == SnapSocket.Alignment.Valid ? alignedTime + Time.deltaTime : 0f;
            if (alignedTime < seatDwell) return;

            alignedTime = 0f;
            // Same as the trainee opening their hand: VRAssemblyGrab calls EndGrab when the last hand has let go.
            for (int i = grab.interactorsSelecting.Count - 1; i >= 0; i--)
                grab.interactionManager.SelectExit(grab.interactorsSelecting[i], grab);
        }
    }
}
