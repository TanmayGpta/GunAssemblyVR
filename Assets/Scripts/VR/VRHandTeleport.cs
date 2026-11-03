using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // Teleport for tracked hands only (controllers keep joystick walking). Either free hand can do it:
    //   point the hand down at the floor and pinch (thumb + index)  -> an arc and a landing ring appear
    //   release the pinch                                          -> jump there (held parts come along)
    // A hand that is holding something never teleports, so with the pistol in one hand the other hand is the
    // teleport hand. The pinch only starts aiming when the hand ray is not over a panel or part, so UI clicks and
    // grabs are unaffected; while aiming, that hand's ray is switched off.
    public class VRHandTeleport : MonoBehaviour
    {
        public VRHands hands;
        public TeleportationProvider teleportation;
        public Material lineMaterial;

        [Header("Gesture")]
        [Tooltip("Thumb tip to index tip distance (m) that counts as a pinch.")]
        public float pinchOn = 0.02f;
        [Tooltip("Distance (m) at which the pinch is released.")]
        public float pinchOff = 0.035f;
        [Tooltip("The hand must point at least this far downward (aim direction y) to start aiming.")]
        public float minDownward = 0.2f;
        [Tooltip("Seconds the pinch must be held (hand free, pointing down, not over a part or panel) before the arc appears. A quick pinch never teleports.")]
        public float holdTime = 0.45f;

        [Header("Arc")]
        public float launchSpeed = 7f;
        public int maxSegments = 40;
        public float segmentTime = 0.05f;
        [Tooltip("Landing must be within this height (m) of the current floor: no landing on the bench.")]
        public float maxStepHeight = 0.3f;
        public LayerMask surfaceMask = ~0;

        public Color validColor = new Color(0.3f, 0.92f, 0.86f, 0.9f);
        public Color invalidColor = new Color(1f, 0.35f, 0.3f, 0.6f);

        class HandState
        {
            public Handedness side;
            public XRBaseInteractor[] interactors;
            public XRRayInteractor ray;
            public bool pinching, aiming, valid;
            public float heldFor; // how long the current pinch has qualified for teleport
            public Vector3 target;
            public Vector3[] points;
            public int pointCount;
        }

        HandState left, right;
        LineRenderer arc;
        Transform ring;
        MeshRenderer ringRenderer;

        void Start()
        {
            left = MakeState(Handedness.Left, hands.modality != null ? hands.modality.leftHand : null, maxSegments);
            right = MakeState(Handedness.Right, hands.modality != null ? hands.modality.rightHand : null, maxSegments);

            arc = new GameObject("Hand_Teleport_Arc").AddComponent<LineRenderer>();
            arc.transform.SetParent(transform, false);
            arc.useWorldSpace = true;
            arc.widthMultiplier = 0.008f;
            arc.numCapVertices = 4;
            arc.sharedMaterial = lineMaterial;
            arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arc.receiveShadows = false;
            arc.enabled = false;

            GameObject r = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            r.name = "Hand_Teleport_Ring";
            Destroy(r.GetComponent<Collider>());
            r.transform.SetParent(transform, false);
            r.transform.localScale = new Vector3(0.35f, 0.005f, 0.35f);
            var mr = r.GetComponent<MeshRenderer>();
            mr.sharedMaterial = lineMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring = r.transform;
            ringRenderer = mr;
            ring.gameObject.SetActive(false);
        }

        static HandState MakeState(Handedness side, GameObject hand, int segments)
        {
            var s = new HandState { side = side, points = new Vector3[segments + 1] };
            if (hand != null)
            {
                s.interactors = hand.GetComponentsInChildren<XRBaseInteractor>(true);
                s.ray = hand.GetComponentInChildren<XRRayInteractor>(true);
            }
            return s;
        }

        void Update()
        {
            HandState aimingHand = null;
            foreach (HandState h in new[] { left, right })
            {
                Tick(h);
                if (h.aiming && aimingHand == null) aimingHand = h;
            }
            DrawArc(aimingHand);
        }

        void Tick(HandState h)
        {
            bool usable = h.ray != null && (h.ray.gameObject.activeInHierarchy || h.aiming);
            float pinchDistance = usable ? PinchDistance(h.side) : float.MaxValue;
            bool wasPinching = h.pinching;
            h.pinching = h.pinching ? pinchDistance < pinchOff : pinchDistance < pinchOn;

            if (!h.aiming)
            {
                // The whole hold must qualify: any grab, hover or upward point resets it, so a pinch meant for a part
                // or a button never turns into a teleport.
                if (!h.pinching || (wasPinching && h.heldFor < 0f) || !CanStartAiming(h))
                {
                    // A pinch that did not qualify from its first frame stays disqualified until it is released.
                    h.heldFor = h.pinching ? -1f : 0f;
                    return;
                }
                h.heldFor += Time.deltaTime;
                if (h.heldFor < holdTime) return;
                h.aiming = true;
                h.heldFor = 0f;
                h.ray.enabled = false; // no UI clicks or grabs from this hand while aiming
            }

            if (h.pinching && pinchDistance < float.MaxValue)
            {
                ComputeArc(h);
                return;
            }

            // Released (or tracking lost): teleport if the target is good.
            h.aiming = false;
            h.ray.enabled = true;
            if (pinchDistance < float.MaxValue && h.valid && teleportation != null)
            {
                teleportation.QueueTeleportRequest(new TeleportRequest
                {
                    destinationPosition = h.target,
                    destinationRotation = Quaternion.identity,
                    matchOrientation = MatchOrientation.None,
                });
            }
        }

        bool CanStartAiming(HandState h)
        {
            foreach (XRBaseInteractor i in h.interactors)
                if (i != null && i.hasSelection) return false;                        // this hand is holding something
            foreach (XRBaseInteractor i in h.interactors)
                if (i != null && i.hasHover) return false;                            // near a part or button: pinch is meant for it
            if (h.ray.TryGetCurrentUIRaycastResult(out _)) return false;              // pinch is meant for a panel
            return h.ray.rayOriginTransform.forward.y < -minDownward;
        }

        float PinchDistance(Handedness side)
        {
            XRHandSubsystem subsystem = hands.Subsystem;
            if (subsystem == null) return float.MaxValue;
            XRHand hand = side == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked) return float.MaxValue;
            if (!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb)) return float.MaxValue;
            if (!hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index)) return float.MaxValue;
            return Vector3.Distance(thumb.position, index.position); // tracking space is metres, unscaled
        }

        void ComputeArc(HandState h)
        {
            Transform aim = h.ray.rayOriginTransform;
            Vector3 p = aim.position;
            Vector3 v = aim.forward * launchSpeed;
            float floorY = teleportation != null && teleportation.system != null && teleportation.system.xrOrigin != null
                ? teleportation.system.xrOrigin.transform.position.y
                : 0f;

            Vector3[] points = h.points;
            points[0] = p;
            h.valid = false;
            int count = 1;
            for (int i = 1; i <= maxSegments; i++)
            {
                Vector3 next = p + v * segmentTime;
                v += Physics.gravity * segmentTime;
                if (Physics.Linecast(p, next, out RaycastHit hit, surfaceMask, QueryTriggerInteraction.Ignore))
                {
                    points[count++] = hit.point;
                    h.target = hit.point;
                    h.valid = hit.normal.y > 0.8f && Mathf.Abs(hit.point.y - floorY) < maxStepHeight;
                    break;
                }
                points[count++] = next;
                p = next;
            }
            h.pointCount = count;
        }

        void DrawArc(HandState h)
        {
            if (h == null)
            {
                arc.enabled = false;
                ring.gameObject.SetActive(false);
                return;
            }
            Color c = h.valid ? validColor : invalidColor;
            arc.enabled = true;
            arc.positionCount = h.pointCount;
            arc.SetPositions(h.points);
            arc.startColor = c;
            arc.endColor = c;
            ring.gameObject.SetActive(h.valid);
            if (h.valid) ring.position = h.target + Vector3.up * 0.005f;
            ringRenderer.material.color = c;
        }
    }
}
