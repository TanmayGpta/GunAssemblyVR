using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // VR controls for step 05 (function check). FunctionCheck keeps the lockwork and its 7-stage sequence; this script
    // only feeds it controller input instead of the V / T / G keys:
    //   index trigger of the hand holding the pistol  -> SetTrigger (analog, so a slow release finds the reset)
    //   A / X button of that hand                      -> SetSafety (toggle)
    //   other hand grips the rear of the slide, pulls  -> SetSlide (how far the hand moved back, springs forward on release)
    // With tracked hands (controllers put down) the same checks use the real hand:
    //   index finger curl of the hand holding the pistol -> SetTrigger
    //   other hand's fingertip pokes the safety lever     -> SetSafety (toggle)
    public class VRFunctionCheckControls : MonoBehaviour
    {
        public FunctionCheck functionCheck;
        public VRAssemblyGrab frameGrab;
        public XRInteractionManager interactionManager;
        public ActionBasedController leftController;
        public ActionBasedController rightController;
        public VRHands hands;

        [Header("Tracked hands")]
        [Tooltip("Index finger curl (0 straight .. 1 fully curled) where the trigger starts to move.")]
        [Range(0f, 1f)] public float fingerCurlRest = 0.35f;
        [Tooltip("Index finger curl where the trigger is fully pulled.")]
        [Range(0f, 1f)] public float fingerCurlFull = 0.8f;
        [Tooltip("Size of the poke zone on the safety lever, in metres.")]
        public float safetyPokeSize = 0.04f;
        [Tooltip("A free-hand pinch whose pinch point is within this distance (m) of the safety lever flips it.")]
        public float safetyPinchDistance = 0.04f;
        [Tooltip("Thumb tip to index tip distance (m) that counts as a pinch (released again above 1.75x this).")]
        public float pinchOn = 0.02f;
        [Tooltip("Translucent material for the ghost marker on the safety lever.")]
        public Material ghostMaterial;
        [Tooltip("Unlit vertex-colour material for the guide line from the free hand to the lever.")]
        public Material lineMaterial;

        [Header("Slide rack grip")]
        [Tooltip("Size of the cube-shaped grab zone at the rear of the slide, in metres.")]
        public float rackGripSize = 0.06f;
        [Tooltip("Seconds for the slide to spring forward after it is let go.")]
        public float slideReturnTime = 0.08f;

        [Header("Feedback")]
        [Range(0f, 1f)] public float hammerHaptic = 0.8f;
        [Range(0f, 1f)] public float safetyHaptic = 0.3f;

        // The other hand is holding the rear of the slide (racking has started).
        public bool RackGripHeld => rackGrip != null && rackGrip.isSelected;

        private InputAction leftPrimary, rightPrimary;
        private VRPokeOnlyInteractable rackGrip;
        private Transform safetyGhost;
        private LineRenderer safetyLine;
        private bool handMode;
        private bool freePinching;
        private Vector3 rackStartPoint;      // where the rack hand grabbed, in frame space
        private bool rackGripBuilt;

        void Awake()
        {
            functionCheck.desktopInput = false;
            functionCheck.safetyControl = "[A / X BUTTON]";
            functionCheck.triggerControl = "[INDEX TRIGGER]";
            functionCheck.rackControl = "[OTHER HAND: GRIP SLIDE, PULL BACK, LET GO]";

            leftPrimary = new InputAction("Left Primary Button", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
            rightPrimary = new InputAction("Right Primary Button", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
        }

        void OnEnable()
        {
            leftPrimary.Enable();
            rightPrimary.Enable();
            functionCheck.HammerFell += OnHammerFell;
        }

        void OnDisable()
        {
            leftPrimary.Disable();
            rightPrimary.Disable();
            functionCheck.HammerFell -= OnHammerFell;
        }

        void OnDestroy()
        {
            leftPrimary.Dispose();
            rightPrimary.Dispose();
        }

        void Update()
        {
            if (!functionCheck.IsRunning) return;
            if (!rackGripBuilt) BuildRackGrip();

            IXRSelectInteractor pistolHand = frameGrab.isSelected ? frameGrab.interactorsSelecting[0] : null;
            Handedness side = Handedness.Invalid;
            bool trackedHand = hands != null && hands.IsTrackedHand(pistolHand, out side);
            ActionBasedController controller = trackedHand ? null : ControllerOf(pistolHand);
            if (pistolHand != null) SetHandMode(trackedHand);

            // Trigger: follows the real index finger (tracked hand) or the controller trigger while the pistol is held,
            // springs forward when it is put down. If the finger is briefly lost, the trigger holds its position.
            float pull;
            if (trackedHand)
                pull = hands.TryGetFingerCurl(side, XRHandFingerID.Index, out float curl)
                    ? Mathf.InverseLerp(fingerCurlRest, fingerCurlFull, curl)
                    : functionCheck.TriggerPull;
            else if (controller != null)
                pull = Mathf.InverseLerp(0.05f, 0.95f, controller.activateActionValue.action.ReadValue<float>());
            else
                pull = Mathf.MoveTowards(functionCheck.TriggerPull, 0f, Time.deltaTime / 0.15f);
            functionCheck.SetTrigger(pull);

            // Safety lever on the A / X button of the holding hand (tracked hands poke the lever instead).
            if (controller != null && PrimaryPressedThisFrame(pistolHand))
            {
                functionCheck.SetSafety(!functionCheck.SafetyOn);
                VRAssemblyGrab.Pulse(pistolHand, safetyHaptic, 0.04f);
            }

            functionCheck.SetSlide(SlideAmountFromHand());
            UpdateSafetyGhost(pistolHand, trackedHand);
            UpdateSafetyTouch(pistolHand, trackedHand);
        }

        // Tracked hands: the safety is worked by the hand NOT holding the pistol (the holding hand cannot reach it), by
        // pinching at the lever (pinch point = between thumb tip and index tip, from the finger joints). One flip per
        // pinch. A fist at the slide racks it instead, so the two never get confused.
        private void UpdateSafetyTouch(IXRSelectInteractor pistolHand, bool trackedHand)
        {
            Transform lever = functionCheck.SafetyPiece;
            if (!trackedHand || lever == null)
            {
                freePinching = false;
                return;
            }
            Handedness free = hands.IsLeft(pistolHand) ? Handedness.Right : Handedness.Left;
            if (!hands.TryGetJointPosition(free, XRHandJointID.ThumbTip, out Vector3 thumb)
                || !hands.TryGetJointPosition(free, XRHandJointID.IndexTip, out Vector3 index))
            {
                freePinching = false;
                return;
            }

            float gap = Vector3.Distance(thumb, index);
            bool pinching = freePinching ? gap < pinchOn * 1.75f : gap < pinchOn;
            bool pinchStarted = pinching && !freePinching;
            freePinching = pinching;
            if (!pinchStarted) return;

            Vector3 pinchPoint = (thumb + index) * 0.5f;
            if (WorldBounds(lever).SqrDistance(pinchPoint) < safetyPinchDistance * safetyPinchDistance)
                functionCheck.SetSafety(!functionCheck.SafetyOn);
        }

        private void UpdateSafetyGhost(IXRSelectInteractor pistolHand, bool trackedHand)
        {
            if (safetyGhost == null) return;
            int stage = functionCheck.Stage;
            bool safetyNext = stage == 0 || (stage == 2 && functionCheck.TriggerPull < 0.2f);
            XRRayInteractor freeRay = trackedHand ? hands.HandRay(!hands.IsLeft(pistolHand)) : null;
            bool show = safetyNext && freeRay != null && freeRay.isActiveAndEnabled;
            safetyGhost.gameObject.SetActive(show);
            safetyLine.enabled = show;
            if (!show) return;

            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
            safetyGhost.localScale = Vector3.one * (safetyPokeSize * 0.8f * pulse / safetyGhost.parent.lossyScale.x);
            safetyLine.SetPosition(0, freeRay.rayOriginTransform.position);
            safetyLine.SetPosition(1, safetyGhost.position);
        }

        // Pistol hand vs free hand: true when the interactor is on the same side as the hand holding the pistol.
        private bool IsPistolHandSide(IXRInteractor interactor)
        {
            if (!frameGrab.isSelected || hands == null) return false;
            return hands.IsLeft(interactor) == hands.IsLeft(frameGrab.interactorsSelecting[0]);
        }

        private float SlideAmountFromHand()
        {
            if (rackGrip == null || !rackGrip.isSelected)
                return Mathf.MoveTowards(functionCheck.SlideAmount, 0f, Time.deltaTime / slideReturnTime);

            Transform hand = rackGrip.interactorsSelecting[0].GetAttachTransform(rackGrip);
            Vector3 start = frameGrab.transform.TransformPoint(rackStartPoint);
            float pulledBack = Vector3.Dot(hand.position - start, functionCheck.SlideRearDirection);
            return Mathf.Clamp01(pulledBack / functionCheck.slideTravel);
        }

        private bool PrimaryPressedThisFrame(IXRSelectInteractor hand)
        {
            ActionBasedController c = ControllerOf(hand);
            if (c == null) return false;
            if (c == leftController) return leftPrimary.WasPressedThisFrame();
            if (c == rightController) return rightPrimary.WasPressedThisFrame();
            return false;
        }

        private static ActionBasedController ControllerOf(IXRSelectInteractor hand)
        {
            return hand is XRBaseControllerInteractor ci ? ci.xrController as ActionBasedController : null;
        }

        // Grab zone on the top-rear of the slide (the cocking serrations). It is a child of the frame, not the slide,
        // so it stays put while the slide moves back under the hand.
        private void BuildRackGrip()
        {
            rackGripBuilt = true;
            // From now on the pistol is held with one hand: the second hand is for the slide, so it must not
            // join the frame grab (VRAssemblyGrab refuses a second hand on single-select parts).
            frameGrab.selectMode = InteractableSelectMode.Single;

            Transform frame = frameGrab.transform;
            Bounds slide = WorldBounds(functionCheck.slide.transform);
            Vector3 rear = functionCheck.SlideRearDirection;
            Vector3 centre = slide.center + rear * (Vector3.Dot(slide.extents, Abs(rear)) * 0.55f);

            GameObject go = new GameObject("VR_Slide_Rack_Grip");
            go.SetActive(false); // configure before Awake runs
            go.transform.SetParent(frame, false);
            go.transform.position = centre;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = Vector3.one * (rackGripSize / go.transform.lossyScale.x); // the frame is scaled uniformly

            rackGrip = go.AddComponent<VRPokeOnlyInteractable>();
            rackGrip.interactionManager = interactionManager;
            rackGrip.colliders.Add(box);
            rackGrip.canUse = CanRack;
            rackGrip.selectEntered.AddListener(OnRackGrabbed);
            go.SetActive(true);

            BuildSafetyPoke();
        }

        // Panel / tooltip wording follows whichever input is in use.
        private void SetHandMode(bool on)
        {
            if (handMode == on) return;
            handMode = on;
            functionCheck.safetyControl = on ? "[OTHER HAND: PINCH SAFETY LEVER]" : "[A / X BUTTON]";
            functionCheck.triggerControl = on ? "[CURL INDEX FINGER]" : "[INDEX TRIGGER]";
        }

        public bool HandMode => handMode;

        // Who may rack the slide: never the hand holding the pistol, never a poke. A tracked hand must close in a fist on
        // the rear of the slide with its direct interactor (a pinch is for the safety, and the hand ray would let finger
        // fidgeting grab it). Controllers: the other controller's grip, as before.
        private bool CanRack(IXRInteractor interactor)
        {
            if (interactor is XRPokeInteractor || IsPistolHandSide(interactor)) return false;
            if (hands != null && hands.IsTrackedHand(interactor, out Handedness side))
                return interactor is XRDirectInteractor && (rackGrip.isSelected || VRHandFistGrab.IsFist(side));
            return true;
        }

        // Anchor for the ghost marker on the safety lever.
        private void BuildSafetyPoke()
        {
            Transform lever = functionCheck.SafetyPiece;
            if (lever == null) return;
            Bounds b = WorldBounds(lever);
            GameObject go = new GameObject("VR_Safety_Poke");
            go.SetActive(false);
            go.transform.SetParent(lever, false);
            go.transform.position = b.center;
            go.SetActive(true);

            // Ghost marker + guide line (shown only when the safety is the next step, see UpdateSafetyGhost).
            GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ghost.name = "VR_Safety_Ghost";
            Destroy(ghost.GetComponent<Collider>());
            ghost.transform.SetParent(go.transform, false);
            MeshRenderer ghostRenderer = ghost.GetComponent<MeshRenderer>();
            if (ghostMaterial != null) ghostRenderer.sharedMaterial = ghostMaterial;
            ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            safetyGhost = ghost.transform;
            ghost.SetActive(false);

            safetyLine = new GameObject("VR_Safety_Guide_Line").AddComponent<LineRenderer>();
            safetyLine.transform.SetParent(transform, false);
            safetyLine.useWorldSpace = true;
            safetyLine.positionCount = 2;
            safetyLine.widthMultiplier = 0.003f;
            safetyLine.sharedMaterial = lineMaterial;
            safetyLine.startColor = new Color(0.3f, 0.92f, 0.86f, 0.05f);
            safetyLine.endColor = new Color(0.3f, 0.92f, 0.86f, 0.8f);
            safetyLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            safetyLine.enabled = false;
        }

        private void OnRackGrabbed(SelectEnterEventArgs args)
        {
            Transform hand = args.interactorObject.GetAttachTransform(rackGrip);
            rackStartPoint = frameGrab.transform.InverseTransformPoint(hand.position);
            VRAssemblyGrab.Pulse(args.interactorObject, 0.3f, 0.05f);
        }

        private void OnHammerFell()
        {
            if (frameGrab.isSelected) VRAssemblyGrab.Pulse(frameGrab.interactorsSelecting[0], hammerHaptic, 0.08f);
        }

        private static Bounds WorldBounds(Transform root)
        {
            Renderer[] rs = root.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
