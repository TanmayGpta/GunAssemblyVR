using UnityEngine;
using UnityEngine.XR.Hands;
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

        [Header("Tracked hands")]
        [Tooltip("Wrap a tracked hand's middle/ring/little fingers around this part while it is held (the pistol grip).")]
        public bool wrapTrackedHand;
        [Tooltip("Optional: snaps the ray's end to one point (the grip) while it is anywhere over this part. Enabled only while this grab is (VR only).")]
        public XRInteractableSnapVolume snapVolume;

        [Header("Hold angle")]
        [Tooltip("Small parts keep the angle they lie at. On: a right-hand grab mirrors that angle (left/right), so the part " +
                 "leans the right hand's way (the magazine, pushed up into the grip with either hand).")]
        public bool mirrorForRightHand;

        public AssemblyPart Part { get; private set; }

        // Recoil kick of the held pistol (set by PistolFiring each frame): turn kickAngle degrees about kickAxisLocal and
        // shift by kickOffsetLocal, both in this part's local space. VRControllerHands applies it to the controller hand
        // AND the pistol's attach point together, so hand and pistol move as one (fingers stay on the grip).
        [System.NonSerialized] public float kickAngle;
        [System.NonSerialized] public Vector3 kickAxisLocal = Vector3.right;
        [System.NonSerialized] public Vector3 kickOffsetLocal;

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

        protected override void OnEnable()
        {
            base.OnEnable();
            if (snapVolume != null) snapVolume.enabled = true;
        }

        protected override void OnDisable()
        {
            if (snapVolume != null) snapVolume.enabled = false;
            base.OnDisable();
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
            // Tracked hands: the pistol is grabbed only through the hand's direct interactor (VRHandFistGrab picks it and
            // holds it in the palm grip pose), so the hand ray only highlights it. A fingertip poke never picks a part up.
            if (VRHands.Instance != null && VRHands.Instance.IsTrackedHand(interactor, out _)
                && (interactor is XRPokeInteractor || (interactor is XRRayInteractor && wrapTrackedHand))) return false;
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

        // Before XRI fixes the hold (dynamic attach keeps the part's current rotation): mirror it for the right hand.
        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
            if (mirrorForRightHand && interactorsSelecting.Count == 0 && VRHands.Instance != null
                && !VRHands.Instance.IsLeft(args.interactorObject))
                transform.rotation = MirroredRotation(transform.rotation);
            base.OnSelectEntering(args);
        }

        // The same pose reflected across the trainee's vertical centre plane (normal = camera right). The reflected frame
        // is left-handed, so the part's thinnest axis (its own mirror-symmetry normal, e.g. a magazine's width) is
        // flipped back: the shape looks exactly mirrored, and the result is a proper rotation.
        private Quaternion MirroredRotation(Quaternion r)
        {
            Camera cam = Camera.main;
            Vector3 n = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up) : Vector3.right;
            if (n.sqrMagnitude < 1e-6f) return r;
            n.Normalize();
            Vector3[] axes = { r * Vector3.right, r * Vector3.up, r * Vector3.forward };
            for (int i = 0; i < 3; i++) axes[i] -= 2f * Vector3.Dot(axes[i], n) * n;
            axes[ThinnestLocalAxis()] = -axes[ThinnestLocalAxis()];
            return Quaternion.LookRotation(axes[2], axes[1]);
        }

        private int thinAxis = -1;

        private int ThinnestLocalAxis()
        {
            if (thinAxis >= 0) return thinAxis;
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
            Vector3 size = max - min;
            thinAxis = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            return thinAxis;
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
            SetGripPose(args.interactorObject, true);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args); // XRI has already restored the original parent here
            lastHand = args.interactorObject;
            SetGripPose(args.interactorObject, false);
            // Skipped while the scene is unloading (Restart / quit): the part is being destroyed anyway and
            // could not start its return-to-bench animation.
            if (interactorsSelecting.Count == 0 && Part.IsHeld && gameObject.activeInHierarchy) Part.EndGrab();
        }

        private void SetGripPose(IXRInteractor interactor, bool on)
        {
            if (wrapTrackedHand && VRHands.Instance != null && VRHands.Instance.IsTrackedHand(interactor, out Handedness side))
                VRHandGripPose.SetGrip(side, on);
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
