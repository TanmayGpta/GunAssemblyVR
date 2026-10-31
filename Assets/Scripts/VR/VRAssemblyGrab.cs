using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // XR grab for one gun part. XRGrabInteractable does the standard controller grab (near or ray, dynamic attach,
    // two-handed when Select Mode = Multiple); this class only connects it to the existing assembly logic:
    //   first hand grabs  -> AssemblyPart.BeginGrab()
    //   last hand lets go -> AssemblyPart.EndGrab()  (sockets get first claim, otherwise the part returns to the bench)
    // and adds hover highlight and controller vibration. All validation stays in SnapSocket / GunAssemblyManager.
    [RequireComponent(typeof(AssemblyPart))]
    public class VRAssemblyGrab : XRGrabInteractable
    {
        [Header("Feedback")]
        [Range(0f, 1f)] public float grabHaptic = 0.25f;
        [Range(0f, 1f)] public float alignedHaptic = 0.15f;
        [Range(0f, 1f)] public float seatedHaptic = 0.6f;
        [Range(0f, 1f)] public float rejectedHaptic = 0.8f;

        public AssemblyPart Part { get; private set; }

        private SnapSocket targetSocket;   // the socket this part fits into (none for the frame)
        private SnapSocket.Alignment lastAlignment;
        private IXRSelectInteractor lastHand;

        protected override void Awake()
        {
            base.Awake();
            Part = GetComponent<AssemblyPart>();
        }

        void Start()
        {
            foreach (SnapSocket s in SnapSocket.All)
            {
                if (s.acceptedPart == Part) targetSocket = s;
                s.Rejected += OnRejected;
            }
            if (targetSocket != null) targetSocket.Seated += OnSeated;
        }

        protected override void OnDestroy()
        {
            foreach (SnapSocket s in SnapSocket.All) s.Rejected -= OnRejected;
            if (targetSocket != null) targetSocket.Seated -= OnSeated;
            base.OnDestroy();
        }

        // Who may grab this part right now. XRI asks every frame, also for hands already holding it.
        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor) || Part == null) return false;
            if (IsSelected(interactor)) return true;                            // keep holding
            if (isSelected) return selectMode == InteractableSelectMode.Multiple; // second hand only on two-handed parts, never a "steal"
            return Part.IsGrabbable;                                             // not seated, not flying back to the bench
        }

        public override bool IsHoverableBy(IXRHoverInteractor interactor)
        {
            return base.IsHoverableBy(interactor) && Part != null && (Part.IsGrabbable || isSelected);
        }

        protected override void OnHoverEntered(HoverEnterEventArgs args)
        {
            base.OnHoverEntered(args);
            if (!Part.IsHeld) Part.SetHighlight(true);
        }

        protected override void OnHoverExited(HoverExitEventArgs args)
        {
            base.OnHoverExited(args);
            if (interactorsHovering.Count == 0) Part.SetHighlight(false);
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            lastHand = args.interactorObject;
            // Layers stay as they are, so the other hand's direct interactor (Default layer) can still touch the part.
            if (interactorsSelecting.Count == 1 && !Part.BeginGrab(hideFromAimRay: false))
            {
                Debug.LogWarning($"[VR Armory] {name} was selected while not grabbable.", this);
            }
            Pulse(args.interactorObject, grabHaptic, 0.05f);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args); // XRI has already restored the original parent here
            lastHand = args.interactorObject;
            // Skipped while the scene is unloading (Restart / quit): the part is being destroyed anyway and
            // could not start its return-to-bench animation.
            if (interactorsSelecting.Count == 0 && Part.IsHeld && gameObject.activeInHierarchy) Part.EndGrab();
        }

        // A light tick when the held part comes into tolerance, so the trainee knows they can let go.
        void Update()
        {
            if (!isSelected || targetSocket == null) return;
            SnapSocket.Alignment a = targetSocket.CurrentAlignment;
            if (a == SnapSocket.Alignment.Valid && lastAlignment != SnapSocket.Alignment.Valid) PulseAll(this, alignedHaptic, 0.03f);
            lastAlignment = a;
        }

        private void OnSeated(SnapSocket socket)
        {
            Pulse(lastHand, seatedHaptic, 0.1f);
            // Also the hand holding the part it was seated into.
            VRAssemblyGrab host = socket.Host != null ? socket.Host.GetComponent<VRAssemblyGrab>() : null;
            if (host != null) PulseAll(host, seatedHaptic, 0.1f);
        }

        private void OnRejected(SnapSocket socket, AssemblyPart part, string reason)
        {
            if (part == Part) Pulse(lastHand, rejectedHaptic, 0.15f);
        }

        private static void PulseAll(XRGrabInteractable grab, float amplitude, float duration)
        {
            for (int i = 0; i < grab.interactorsSelecting.Count; i++) Pulse(grab.interactorsSelecting[i], amplitude, duration);
        }

        public static void Pulse(IXRInteractor interactor, float amplitude, float duration)
        {
            if (amplitude > 0f && interactor is XRBaseControllerInteractor hand) hand.SendHapticImpulse(amplitude, duration);
        }
    }
}
