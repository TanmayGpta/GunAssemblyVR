using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // VR controls for step 05 (function check). FunctionCheck keeps the lockwork and its 7-stage sequence; this script
    // only feeds it controller input instead of the V / T / G keys:
    //   index trigger of the hand holding the pistol  -> SetTrigger (analog, so a slow release finds the reset)
    //   A / X button of that hand                      -> SetSafety (toggle)
    //   other hand grips the rear of the slide, pulls  -> SetSlide (how far the hand moved back, springs forward on release)
    public class VRFunctionCheckControls : MonoBehaviour
    {
        public FunctionCheck functionCheck;
        public VRAssemblyGrab frameGrab;
        public XRInteractionManager interactionManager;
        public ActionBasedController leftController;
        public ActionBasedController rightController;

        [Header("Slide rack grip")]
        [Tooltip("Size of the cube-shaped grab zone at the rear of the slide, in metres.")]
        public float rackGripSize = 0.06f;
        [Tooltip("Seconds for the slide to spring forward after it is let go.")]
        public float slideReturnTime = 0.08f;

        [Header("Feedback")]
        [Range(0f, 1f)] public float hammerHaptic = 0.8f;
        [Range(0f, 1f)] public float safetyHaptic = 0.3f;

        private InputAction leftPrimary, rightPrimary;
        private XRSimpleInteractable rackGrip;
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
            ActionBasedController controller = ControllerOf(pistolHand);

            // Trigger: follows the controller trigger while the pistol is held, springs forward when it is put down.
            float pull = controller != null
                ? Mathf.InverseLerp(0.05f, 0.95f, controller.activateActionValue.action.ReadValue<float>())
                : Mathf.MoveTowards(functionCheck.TriggerPull, 0f, Time.deltaTime / 0.15f);
            functionCheck.SetTrigger(pull);

            // Safety lever on the A / X button of the holding hand.
            if (controller != null && PrimaryPressedThisFrame(pistolHand))
            {
                functionCheck.SetSafety(!functionCheck.SafetyOn);
                VRAssemblyGrab.Pulse(pistolHand, safetyHaptic, 0.04f);
            }

            functionCheck.SetSlide(SlideAmountFromHand());
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

            rackGrip = go.AddComponent<XRSimpleInteractable>();
            rackGrip.interactionManager = interactionManager;
            rackGrip.colliders.Add(box);
            rackGrip.selectEntered.AddListener(OnRackGrabbed);
            go.SetActive(true);
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
