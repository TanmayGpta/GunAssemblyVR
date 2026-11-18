using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // Hands instead of controller models while the controllers are held. Each controller gets a copy of the same smooth
    // hand mesh the tracked hands use (tracking drivers removed), posed from the controller's input:
    //   grip button  -> middle, ring and little fingers wrap
    //   trigger      -> index finger curls (a little when the finger rests on the trigger, fully when pulled)
    //   thumb on the stick / A B X Y -> thumb lowers
    // The hand is placed on the controller's grip pose (OpenXR: Y = where the knuckles point, Z = little finger to
    // thumb, X = palm normal). While a controller holds the pistol, the pistol sits in that hand's fist exactly like with
    // tracked hands: grip in the fist, muzzle along the knuckles, slide towards the thumb.
    public class VRControllerHands : MonoBehaviour
    {
        public VRHands hands;
        public GameObject leftHandPrefab;
        public GameObject rightHandPrefab;
        public Material handMaterial;

        [Header("Placement (controller grip space)")]
        [Tooltip("How far (m) the palm sits behind the centre of the controller handle.")]
        public float palmDepth = 0.035f;
        [Tooltip("Extra offset (m) of the hand in controller grip space.")]
        public Vector3 positionOffset = new Vector3(0f, -0.01f, 0f);
        [Tooltip("Extra rotation (deg) of the hand in controller grip space.")]
        public Vector3 rotationOffset = Vector3.zero;
        [Tooltip("Extra rotation (deg) of the held pistol in controller grip space.")]
        public Vector3 pistolRotationOffset = Vector3.zero;
        [Tooltip("Turn the palm to face the other way (rotates the hand 180 degrees about the knuckle direction).")]
        public bool flipPalm;
        [Tooltip("Point the fingers the other way along the controller (rotates the hand 180 degrees about the palm normal).")]
        public bool flipFingers;
        [Tooltip("Tilts the hand and the held pistol down (deg) from the controller's forward direction.")]
        public float pitchDown = 15f;
        // Placement is re-applied every frame, so all of the above can be tuned live in Play mode.

        [Header("Finger bend (knuckle, middle, last joint, deg)")]
        public Vector3 relaxedCurl = new Vector3(12f, 15f, 8f);
        public Vector3 gripCurl = new Vector3(75f, 85f, 45f);
        public Vector3 indexTouchCurl = new Vector3(25f, 30f, 15f);
        public Vector3 indexPullCurl = new Vector3(60f, 75f, 40f);
        public Vector3 thumbRestCurl = new Vector3(10f, 10f, 5f);
        public Vector3 thumbDownCurl = new Vector3(25f, 30f, 20f);
        [Tooltip("How fast fingers follow the buttons (1/s).")]
        public float fingerSpeed = 20f;

        public enum PoseSwapTrigger { OnDirectHover, OnGrab }
        public enum PoseKind { None, GunGrip, PartGrip }

        [Header("Object-attached hand poses (controllers only)")]
        [Tooltip("OnDirectHover: hide the controller hand and show the object's separate pose hand as soon as the Direct Interactor touches it. OnGrab: swap only once gripped.")]
        public PoseSwapTrigger poseSwapTrigger = PoseSwapTrigger.OnDirectHover;
        [Tooltip("Trigger radius (m) of each controller's Direct Interactor sphere for hovering near parts (0 leaves the prefab radius unchanged).")]
        public float directHoverRadius = 0.09f;
        [Tooltip("Offset of the controller Direct Interactor trigger sphere in controller space, shifted forward toward the palm/fingers.")]
        public Vector3 directHoverOffset = new Vector3(0f, -0.01f, 0.025f);
        [Tooltip("Maximum direct-hover distance (m) from the palm trigger center before the Gun Grip pose swaps on (0 = full sphere radius).")]
        public float gunHoverMaxDistance = 0.055f;
        [Tooltip("Extra distance margin (m) a neighboring part must beat the current hovered part by before stealing hover.")]
        public float hoverSwitchMargin = 0.012f;

        [Header("Parts grip pose (separate hand pose for gun parts)")]
        [Tooltip("Finger curl angles (deg) where Index & Middle start before fitting to the part mesh.")]
        public Vector3 partOpenCurl = new Vector3(18f, 22f, 12f);
        public Vector3 partMinCurl = new Vector3(34f, 42f, 22f);
        public Vector3 partMaxCurl = new Vector3(72f, 84f, 48f);
        [Tooltip("Reference curl (deg) used to measure the thumb-index pinch anchor point.")]
        public Vector3 partPinchRefCurl = new Vector3(48f, 58f, 32f);
        public Vector3 partRingCurl = new Vector3(78f, 88f, 52f);
        public Vector3 partLittleCurl = new Vector3(82f, 90f, 55f);
        public Vector3 partThumbBaseRotation = new Vector3(10f, -34f, 12f);
        public Vector3 partThumbKnuckleRotation = new Vector3(18f, 12f, 0f);
        public Vector3 partThumbTipRotation = new Vector3(22f, 6f, 0f);
        [Range(0.6f, 1.2f)] public float partThumbLength = 0.9f;
        public Vector3 partGripOffset = Vector3.zero;

        [Header("Pistol wrap (fingers bend until they touch the grip)")]
        [Tooltip("Most the middle/ring/little fingers may bend while wrapping the grip (knuckle, middle, last joint, deg).")]
        public Vector3 wrapMaxCurl = new Vector3(90f, 100f, 70f);
        [Tooltip("Finger thickness (m) used for the touch test.")]
        public float fingerRadius = 0.009f;
        [Tooltip("Index finger skin thickness (m) from its bone line, used against the trigger's real surface: the pull " +
                 "bend stops when the finger skin meets the trigger. Raise if it still sinks in slightly, lower if it stops short.")]
        public float pullContactRadius = 0.005f;
        [Tooltip("Bend step (deg) of the touch search. Smaller = tighter fit, more work.")]
        public float wrapStep = 3f;
        [Range(0f, 1f), Tooltip("Ring and little finger wrap at least this fraction of the finger above them, so none sticks out.")]
        public float wrapFollow = 0.9f;
        [Tooltip("Shifts the held pistol in its own space (m): X right, Y up towards the hammer, Z towards the muzzle. " +
                 "Raise Y if the index finger sits above the trigger.")]
        public Vector3 pistolHoldOffset = new Vector3(0f, 0.025f, 0.035f);
        [Tooltip("Turns the held pistol about its own vertical axis (deg) so the backstrap sits in the web between thumb " +
                 "and index and the thumb can lie along the far panel. Mirrored automatically for the left hand.")]
        public float gripYaw = 15f;
        [Tooltip("How far below the top of the grip the thumb wraps around the backstrap (m).")]
        public float thumbDropFromTop = 0.012f;
        [Range(0.6f, 1.2f), Tooltip("Length of the thumb's two outer segments (1 = model length).")]
        public float thumbLength = 0.85f;
        [Tooltip("Extra bend (deg) the index adds over its trigger-resting pose when the trigger is fully pulled.")]
        public Vector3 indexPullExtra = new Vector3(12f, 15f, 8f);
        [Tooltip("How far down the thumb tip angles while running forward along the far panel (m).")]
        public float thumbTipDrop = 0.008f;
        [Tooltip("Most the palm-to-knuckle bone may turn (deg) to bring the knuckle round behind the backstrap. " +
                 "Lower it if the thumb pad crumples.")]
        public float thumbBaseTurnLimit = 70f;
        [Tooltip("How far forward along the far panel (from the back corner) the thumb base points (m). Higher = thumb " +
                 "lies more along the grip instead of across its back.")]
        public float thumbBaseAimForward = 0.03f;
        [Tooltip("How far down the thumb base points (m). Higher = lower knuckle.")]
        public float thumbBaseAimDrop = 0.012f;
        [Tooltip("Manual extra turn (deg) of the palm-to-knuckle bone while holding the pistol, in the bone's own axes " +
                 "(mirrored for the left hand). Change X/Y/Z live in Play mode to rest the thumb base on the grip.")]
        public Vector3 thumbBaseRotation = Vector3.zero;
        [Tooltip("Manual extra turn (deg) at the thumb knuckle (the joint after the base) while holding the pistol.")]
        public Vector3 thumbKnuckleRotation = Vector3.zero;
        [Tooltip("Manual extra turn (deg) at the last thumb joint (before the tip) while holding the pistol.")]
        public Vector3 thumbTipRotation = Vector3.zero;
        [Tooltip("On: the thumb wraps the grip automatically and the three rotations above fine-tune it. " +
                 "Off: the thumb is posed only by the three rotations above (full manual control).")]
        public bool thumbAutoWrap = true;
        [Tooltip("Draw the thumb's wrap path in the Scene view (yellow) while holding the pistol.")]
        public bool showThumbPath = true;

        static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };

        class Finger
        {
            public Transform[] bones;       // proximal, intermediate, distal (thumb: metacarpal, proximal, distal)
            public Quaternion[] bind;
            public Transform tip;           // fingertip joint, if the hand has one
            public Vector3[] bindPos;       // bind local positions of the bones
            public Vector3 tipBindPos;
            public Vector3 held;            // smoothed curl while holding (solved wrap)
        }

        // A pistol piece as an oriented box: its mesh bounds in its own space.
        // Fitted tight around the mesh: b is in the piece's space turned by rot, so a raked grip panel gets a slanted
        // box instead of a big axis-aligned one that sticks out cm past the real surface.
        struct Box
        {
            public Transform t;
            public Quaternion rot;
            public Bounds b;
        }

        static readonly Dictionary<Mesh, Box> FitCache = new Dictionary<Mesh, Box>();
        readonly Dictionary<Component, Bounds> visualBoundsCache = new Dictionary<Component, Bounds>();

        class HandRig
        {
            public Transform root, palm;
            public Quaternion palmRot;
            public Vector3 palmPos;
            public Finger index, thumb;
            public readonly List<Finger> wrap = new List<Finger>();
            public readonly Quaternion[] thumbRot = new Quaternion[3];
            public Vector3[] debugPath;
        }

        class Side
        {
            public bool left;
            public ActionBasedController controller;
            public XRDirectInteractor direct;
            public XRBaseInteractor[] grabInteractors;
            public Transform pistolAttach;
            public HandRig controllerRig, gunPoseRig, partPoseRig;
            public XRBaseController[] controllers;
            public InputAction triggerTouch, thumbTouch;
            public float grip, trigger, touch, thumbDown;
            public VRAssemblyGrab pistol;         // currently held LowerFrame (attach point + recoil)
            public VRAssemblyGrab cachedGunGrab;  // LowerFrame currently loaded into gripBoxes/triggerSolids
            public IXRSelectInteractable activeTarget;
            public PoseKind activeKind;
            public bool targetWasSelected;
            public Vector3 handPosInGun;
            public Quaternion handRotInGun = Quaternion.identity;
            public Vector3 handLocalPos;
            public Quaternion handLocalRot = Quaternion.identity;
            public Component cachedPartComponent;
            public Bounds cachedPartBounds;
            public bool hasCachedPartBounds;
            public Vector3 heldAnchorLocal;
            public Vector3 partPinchLocal;
            public readonly List<Box> gripBoxes = new List<Box>();       // Grip_* pieces: fingers and thumb wrap these
            public readonly List<Box> indexBoxes = new List<Box>();      // grip + Trigger*: the index rests on these
            public readonly List<Box> partBoxes = new List<Box>();       // visual mesh bounds for part pinch pose
            public readonly List<Solid> triggerSolids = new List<Solid>(); // Trigger* surfaces: the pull bend stops at these
            public readonly List<Vector3> gripPoints = new List<Vector3>(); // grip surface vertices in grip-attach space
            public readonly Dictionary<IXRInteractor, Transform> originalAttach = new Dictionary<IXRInteractor, Transform>();
        }

        readonly List<Side> sides = new List<Side>();

        void OnEnable() => Application.onBeforeRender += OnBeforeRender;

        void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            foreach (Side s in sides)
            {
                SetRigActive(s.gunPoseRig, false);
                SetRigActive(s.partPoseRig, false);
                s.activeTarget = null;
                s.activeKind = PoseKind.None;
                s.targetWasSelected = false;
            }
        }

        void Start()
        {
            if (hands == null || hands.modality == null) return;
            Build(true, hands.modality.leftController, leftHandPrefab);
            Build(false, hands.modality.rightController, rightHandPrefab);
        }

        void OnDestroy()
        {
            visualBoundsCache.Clear();
            foreach (Side s in sides)
            {
                s.triggerTouch?.Dispose();
                s.thumbTouch?.Dispose();
            }
        }

        void Build(bool left, GameObject controllerObject, GameObject prefab)
        {
            if (controllerObject == null || prefab == null) return;
            var s = new Side { left = left, controller = controllerObject.GetComponentInChildren<ActionBasedController>(true) };
            if (s.controller == null) return;
            s.controller.hideControllerModel = true;
            // The poke interactor's pointer stick (sphere + cylinder) would poke out of the hand; poking still works.
            foreach (XRPokeInteractor poke in controllerObject.GetComponentsInChildren<XRPokeInteractor>(true))
                foreach (Renderer r in poke.GetComponentsInChildren<Renderer>(true))
                    r.enabled = false;

            string sideTag = left ? "Left" : "Right";
            s.controllerRig = BuildHandRig(left, s.controller.transform, prefab, $"VR_Controller_Hand_{sideTag}", true);
            if (s.controllerRig == null) return;
            s.gunPoseRig = BuildHandRig(left, transform, prefab, $"VR_GunGrip_Hand_{sideTag}", false);
            s.partPoseRig = BuildHandRig(left, transform, prefab, $"VR_PartGrip_Hand_{sideTag}", false);

            s.controllers = controllerObject.GetComponentsInChildren<XRBaseController>(true);

            s.pistolAttach = new GameObject("VR_Controller_Pistol_Attach").transform;
            s.pistolAttach.SetParent(s.controller.transform, false);
            Place(s);

            var grabList = new List<XRBaseInteractor>();
            foreach (XRBaseInteractor i in s.controller.GetComponentsInChildren<XRBaseInteractor>(true))
            {
                if (i is XRPokeInteractor) continue;
                grabList.Add(i);
                if (i is XRDirectInteractor direct && s.direct == null)
                {
                    s.direct = direct;
                    if (directHoverRadius > 0f)
                    {
                        foreach (SphereCollider sphere in direct.GetComponents<SphereCollider>())
                        {
                            sphere.radius = directHoverRadius;
                            sphere.center = directHoverOffset;
                        }
                    }
                }
                i.selectEntered.AddListener(args => OnSelect(s, args, true));
                i.selectExited.AddListener(args => OnSelect(s, args, false));
            }
            s.grabInteractors = grabList.ToArray();

            string device = left ? "{LeftHand}" : "{RightHand}";
            s.triggerTouch = new InputAction(type: InputActionType.Button, binding: $"<XRController>{device}/triggerTouched");
            s.thumbTouch = new InputAction(type: InputActionType.Button);
            foreach (string control in new[] { "thumbstickTouched", "primaryTouched", "secondaryTouched", "thumbrestTouched" })
                s.thumbTouch.AddBinding($"<XRController>{device}/{control}");
            s.triggerTouch.Enable();
            s.thumbTouch.Enable();
            sides.Add(s);
        }

        HandRig BuildHandRig(bool left, Transform parent, GameObject prefab, string objectName, bool active)
        {
            // Instantiate under an inactive holder so the tracking drivers never run, then strip every script.
            var holder = new GameObject("Hand_Build");
            holder.SetActive(false);
            GameObject hand = Instantiate(prefab, holder.transform);
            foreach (Transform t in hand.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name.EndsWith("Affordances")) DestroyImmediate(t.gameObject);
            // Reverse order: dependants (e.g. the poke displacer, which requires the skeleton driver) go first.
            MonoBehaviour[] scripts = hand.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = scripts.Length - 1; i >= 0; i--) DestroyImmediate(scripts[i]);
            if (handMaterial != null)
                foreach (SkinnedMeshRenderer smr in hand.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.sharedMaterial = handMaterial;
            hand.name = objectName;

            string p = left ? "L_" : "R_";
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in hand.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
            if (!bones.TryGetValue(p + "Palm", out Transform palm))
            {
                Debug.LogWarning($"[VR Armory] Controller hand: {p}Palm not found in {prefab.name}.", this);
                Destroy(holder);
                return null;
            }

            var rig = new HandRig
            {
                root = hand.transform,
                palm = palm,
                index = MakeFinger(bones, p + "IndexTip", p + "IndexProximal", p + "IndexIntermediate", p + "IndexDistal"),
                thumb = MakeFinger(bones, p + "ThumbTip", p + "ThumbMetacarpal", p + "ThumbProximal", p + "ThumbDistal"),
            };
            foreach (string f in new[] { "Middle", "Ring", "Little" })
            {
                Finger finger = MakeFinger(bones, p + f + "Tip", p + f + "Proximal", p + f + "Intermediate", p + f + "Distal");
                if (finger != null) rig.wrap.Add(finger);
            }

            // Palm pose in the hand's own space (bind pose).
            rig.root.localPosition = Vector3.zero;
            rig.root.localRotation = Quaternion.identity;
            rig.palmRot = Quaternion.Inverse(rig.root.rotation) * palm.rotation;
            rig.palmPos = rig.root.InverseTransformPoint(palm.position);
            rig.root.SetParent(parent, false);
            rig.root.gameObject.SetActive(active);
            Destroy(holder);
            StoreThumb(rig);
            return rig;
        }

        static void SetRigActive(HandRig rig, bool active)
        {
            if (rig != null && rig.root != null && rig.root.gameObject.activeSelf != active)
                rig.root.gameObject.SetActive(active);
        }

        Finger MakeFinger(Dictionary<string, Transform> bones, string tip, params string[] names)
        {
            var f = new Finger { bones = new Transform[names.Length], bind = new Quaternion[names.Length], held = gripCurl,
                                 bindPos = new Vector3[names.Length] };
            if (bones.TryGetValue(tip, out f.tip)) f.tipBindPos = f.tip.localPosition;
            for (int i = 0; i < names.Length; i++)
            {
                if (!bones.TryGetValue(names[i], out Transform t)) return null;
                f.bones[i] = t;
                f.bind[i] = t.localRotation;
                f.bindPos[i] = t.localPosition;
            }
            return f;
        }

        // Pistol held by this controller -> it goes into the hand's fist; anything else keeps the rig's attach point.
        void OnSelect(Side s, BaseInteractionEventArgs args, bool entered)
        {
            if (!(args.interactorObject is XRBaseInteractor interactor)) return;
            if (!(args.interactableObject is VRAssemblyGrab grab) || !grab.wrapTrackedHand) return;
            if (entered)
            {
                if (!s.originalAttach.ContainsKey(interactor)) s.originalAttach[interactor] = interactor.attachTransform;
                interactor.attachTransform = s.pistolAttach;
                s.pistol = grab;
                if (s.cachedGunGrab != grab) CollectGunBoxes(s, grab);
            }
            else if (s.originalAttach.TryGetValue(interactor, out Transform original))
            {
                interactor.attachTransform = original;
                s.originalAttach.Remove(interactor);
                if (s.originalAttach.Count == 0) s.pistol = null;
            }
        }

        static void CollectGunBoxes(Side s, VRAssemblyGrab grab)
        {
            s.cachedGunGrab = grab;
            s.gripBoxes.Clear();
            s.indexBoxes.Clear();
            s.triggerSolids.Clear();
            s.gripPoints.Clear();
            if (grab == null) return;
            Transform g = grab.attachTransform;
            foreach (MeshFilter mf in grab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null) continue;
                string n = mf.name;
                bool isGrip = n.StartsWith("Grip_");
                if (!isGrip && !n.StartsWith("Trigger")) continue;
                Box box = FitBox(mesh);
                box.t = mf.transform;
                s.indexBoxes.Add(box);
                if (!isGrip)
                {
                    if (mesh.isReadable) s.triggerSolids.Add(MakeSolid(mf.transform, mesh));
                    continue;
                }
                s.gripBoxes.Add(box);
                // Real grip surface points in grip space, for the thumb path (sampled down to a few thousand).
                if (g != null && mesh.isReadable)
                {
                    Vector3[] v = mesh.vertices;
                    int stride = Mathf.Max(1, v.Length / 1500);
                    for (int i = 0; i < v.Length; i += stride)
                        s.gripPoints.Add(g.InverseTransformPoint(mf.transform.TransformPoint(v[i])));
                }
            }
            if (s.gripBoxes.Count == 0)
                Debug.LogWarning($"[VR Armory] Controller hand: no Grip_* pieces under {grab.name}; fingers use the fixed grip curl.", grab);
        }

        // Smallest box around the mesh, searching a turn of up to 45 degrees about each of its own axes (the grip pieces
        // are slabs raked in one plane). Needs Read/Write on the mesh; otherwise the plain mesh bounds.
        static Box FitBox(Mesh mesh)
        {
            if (FitCache.TryGetValue(mesh, out Box cached)) return cached;
            var box = new Box { rot = Quaternion.identity, b = mesh.bounds };
            if (mesh.isReadable)
            {
                Vector3[] v = mesh.vertices;
                float best = Volume(box.b);
                Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
                foreach (Vector3 axis in axes)
                {
                    for (float a = -45f; a <= 45f; a += 1.5f)
                    {
                        Quaternion q = Quaternion.AngleAxis(a, axis);
                        Quaternion qi = Quaternion.Inverse(q);
                        Vector3 first = qi * v[0];
                        var b = new Bounds(first, Vector3.zero);
                        for (int i = 1; i < v.Length; i++) b.Encapsulate(qi * v[i]);
                        float vol = Volume(b);
                        if (vol < best * 0.995f)
                        {
                            best = vol;
                            box.rot = q;
                            box.b = b;
                        }
                    }
                }
            }
            FitCache[mesh] = box;
            return box;
        }

        static float Volume(Bounds b) => Mathf.Max(b.size.x, 1e-4f) * Mathf.Max(b.size.y, 1e-4f) * Mathf.Max(b.size.z, 1e-4f);

        // Hand and pistol pose on the controller. Hand joint space: +Z towards the fingers, +Y out of the back of the
        // hand. Controller space: +Z forward (fingers / muzzle, tilted by pitchDown), palm normal along X (see dorsal).
        void Place(Side s)
        {
            // Measured on device: fingers (and the pistol's muzzle) run along the controller's forward axis, tilted down.
            Vector3 knuckles = Quaternion.AngleAxis(pitchDown, Vector3.right) * (flipFingers ? Vector3.back : Vector3.forward);
            // Measured on device: the back of the hand is grip -X on the left hand and +X on the right (the opposite of
            // the axis sign in the OpenXR grip description), otherwise each hand reads as the mirror-image hand.
            Vector3 dorsal = (s.left ? Vector3.left : Vector3.right) * (flipPalm ? -1f : 1f);
            Quaternion targetRot = Quaternion.Euler(rotationOffset) * Quaternion.LookRotation(knuckles, dorsal);
            Vector3 targetPos = dorsal * palmDepth + positionOffset;
            s.handLocalRot = targetRot * Quaternion.Inverse(s.controllerRig.palmRot);
            s.handLocalPos = targetPos - s.handLocalRot * s.controllerRig.palmPos;
            s.controllerRig.root.localRotation = s.handLocalRot;
            s.controllerRig.root.localPosition = s.handLocalPos;

            // Pistol in the fist: muzzle along the knuckles, slide towards the thumb.
            Vector3 thumbSide = Vector3.Cross(knuckles, dorsal) * (s.left ? -1f : 1f);
            Quaternion pistolRot = Quaternion.Euler(pistolRotationOffset) * Quaternion.LookRotation(knuckles, thumbSide);
            // Yaw so the back of the grip swings towards the palm side (into the web of the hand).
            float palmSide = Mathf.Sign((Quaternion.Inverse(pistolRot) * dorsal).x);
            s.pistolAttach.localRotation = pistolRot * Quaternion.Euler(0f, -palmSide * gripYaw, 0f);
            s.pistolAttach.localPosition = positionOffset + s.pistolAttach.localRotation * pistolHoldOffset;

            s.handPosInGun = Quaternion.Inverse(s.pistolAttach.localRotation) * (s.handLocalPos - s.pistolAttach.localPosition);
            s.handRotInGun = Quaternion.Inverse(s.pistolAttach.localRotation) * s.handLocalRot;
            ApplyRecoil(s);
        }

        // Live-fire recoil: hand and pistol attach turn and shift together as one rigid piece (the pistol follows its
        // attach point), pivoting at the wrist a little behind the grip, so nothing moves on its own.
        static void ApplyRecoil(Side s)
        {
            VRAssemblyGrab gun = s.pistol;
            if (gun == null || (gun.kickAngle == 0f && gun.kickOffsetLocal == Vector3.zero)) return;
            Transform c = s.controller.transform;
            Vector3 axis = c.InverseTransformDirection(gun.transform.TransformDirection(gun.kickAxisLocal)).normalized;
            Vector3 offset = c.InverseTransformVector(gun.transform.TransformVector(gun.kickOffsetLocal));
            Quaternion turn = Quaternion.AngleAxis(gun.kickAngle, axis);
            Vector3 back = offset.sqrMagnitude > 1e-10f ? offset.normalized : Vector3.zero;
            Vector3 wrist = s.pistolAttach.localPosition + back * 0.07f;
            foreach (Transform t in new[] { s.controllerRig.root, s.pistolAttach })
            {
                t.localPosition = wrist + turn * (t.localPosition - wrist) + offset;
                t.localRotation = turn * t.localRotation;
            }
        }

        // The rig's controller models (spawned by the controllers on their first update) stay hidden.
        static void HideControllerModels(Side s)
        {
            foreach (XRBaseController c in s.controllers)
            {
                if (c == null) continue;
                if (!c.hideControllerModel) c.hideControllerModel = true;
                if (c.model != null && c.model.gameObject.activeSelf) c.model.gameObject.SetActive(false);
            }
        }

        void OnBeforeRender()
        {
            foreach (Side s in sides)
            {
                if (s.controller == null || !s.controller.isActiveAndEnabled || s.activeTarget == null) continue;
                if (s.activeKind == PoseKind.GunGrip) UpdateGunTransform(s);
                else if (s.activeKind == PoseKind.PartGrip) UpdatePartTransform(s, s.targetWasSelected);
            }
        }

        void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-fingerSpeed * Time.deltaTime);
            foreach (Side s in sides)
            {
                if (s.controllerRig == null || s.controller == null || !s.controller.isActiveAndEnabled)
                {
                    SetRigActive(s.gunPoseRig, false);
                    SetRigActive(s.partPoseRig, false);
                    s.activeTarget = null;
                    s.activeKind = PoseKind.None;
                    s.targetWasSelected = false;
                    continue;
                }

                HideControllerModels(s);
                Place(s);
                s.grip = Mathf.Lerp(s.grip, Read(s.controller.selectActionValue), k);
                s.trigger = Mathf.Lerp(s.trigger, Read(s.controller.activateActionValue), k);
                s.touch = Mathf.Lerp(s.touch, s.triggerTouch.IsPressed() ? 1f : 0f, k);
                s.thumbDown = Mathf.Lerp(s.thumbDown, s.thumbTouch.IsPressed() || s.grip > 0.5f ? 1f : 0f, k);

                ResolveActiveTarget(s, out PoseKind wantKind, out IXRSelectInteractable wantTarget, out bool wantSelected);
                bool targetChanged = wantKind != s.activeKind || !ReferenceEquals(wantTarget, s.activeTarget);
                if (targetChanged)
                {
                    s.activeKind = wantKind;
                    s.activeTarget = wantTarget;
                    s.targetWasSelected = false;
                }

                SetRigActive(s.controllerRig, wantKind == PoseKind.None);
                SetRigActive(s.gunPoseRig, wantKind == PoseKind.GunGrip);
                SetRigActive(s.partPoseRig, wantKind == PoseKind.PartGrip);

                if (wantKind == PoseKind.None)
                {
                    PoseControllerIdle(s, k);
                }
                else if (wantKind == PoseKind.GunGrip)
                {
                    UpdateGunGrip(s, wantTarget as VRAssemblyGrab, wantSelected, targetChanged, k);
                }
                else
                {
                    UpdatePartGrip(s, wantTarget, wantSelected, targetChanged, k);
                }
                s.targetWasSelected = wantSelected;
            }
        }

        void ResolveActiveTarget(Side s, out PoseKind kind, out IXRSelectInteractable target, out bool selected)
        {
            kind = PoseKind.None;
            target = null;
            selected = false;

            if (s.grabInteractors != null)
            {
                foreach (XRBaseInteractor i in s.grabInteractors)
                {
                    if (i == null || !i.hasSelection) continue;
                    foreach (IXRSelectInteractable sel in i.interactablesSelected)
                    {
                        PoseKind k = ClassifyTarget(sel);
                        if (k == PoseKind.None) continue;
                        kind = k;
                        target = sel;
                        selected = true;
                        return;
                    }
                }
            }

            if (poseSwapTrigger != PoseSwapTrigger.OnDirectHover || s.direct == null || !s.direct.isActiveAndEnabled) return;

            float bestScore = float.MaxValue;
            Vector3 hoverOrigin = HoverOriginWorld(s);
            foreach (IXRHoverInteractable hovered in s.direct.interactablesHovered)
            {
                if (!(hovered is IXRSelectInteractable sel)) continue;
                var comp = sel as Component;
                if (comp == null || !comp.gameObject.activeInHierarchy) continue;
                PoseKind k = ClassifyTarget(sel);
                if (k == PoseKind.None || sel.isSelected || !sel.IsSelectableBy(s.direct)) continue;

                float d = ClosestDistanceToDirect(comp, sel, s.direct, hoverOrigin);
                if (k == PoseKind.GunGrip && gunHoverMaxDistance > 0f && d > gunHoverMaxDistance) continue;

                // Small margin so hovering near a part does not jitter onto an adjacent neighbor.
                float score = !s.targetWasSelected && ReferenceEquals(sel, s.activeTarget) ? d - hoverSwitchMargin : d;
                if (score < bestScore)
                {
                    bestScore = score;
                    target = sel;
                    kind = k;
                }
            }
        }

        Vector3 HoverOriginWorld(Side s) => s.direct != null ? s.direct.transform.TransformPoint(directHoverOffset) : s.controller.transform.position;

        static PoseKind ClassifyTarget(IXRSelectInteractable sel)
        {
            // Keep slide racking (VRPokeOnlyInteractable) with the live controller hand so it pulls back with the hand.
            if (sel is VRAssemblyGrab grab) return grab.wrapTrackedHand ? PoseKind.GunGrip : PoseKind.PartGrip;
            return PoseKind.None;
        }

        float ClosestDistanceToDirect(Component comp, IXRSelectInteractable sel, XRDirectInteractor direct, Vector3 originWorld)
        {
            if (TryGetVisualBounds(comp, null, out Bounds vb))
            {
                Vector3 localPt = comp.transform.InverseTransformPoint(originWorld);
                return Vector3.Distance(comp.transform.TransformPoint(vb.ClosestPoint(localPt)), originWorld);
            }
            Collider col = comp.GetComponent<Collider>();
            if (col != null && col.enabled) return Vector3.Distance(col.ClosestPoint(originWorld), originWorld);
            Transform a = sel.GetAttachTransform(direct);
            return a != null ? Vector3.Distance(a.position, originWorld) : float.MaxValue;
        }

        void PoseControllerIdle(Side s, float k)
        {
            HandRig rig = s.controllerRig;
            ApplyThumbLength(rig, thumbLength);
            foreach (Finger f in rig.wrap)
            {
                f.held = Vector3.Lerp(f.held, gripCurl, k);
                Pose(f, Vector3.Lerp(relaxedCurl, f.held, s.grip));
            }
            if (rig.index != null)
            {
                Vector3 index = Vector3.Lerp(relaxedCurl, indexTouchCurl, Mathf.Max(s.touch, s.trigger > 0.02f ? 1f : 0f));
                Pose(rig.index, Vector3.Lerp(index, indexPullCurl, s.trigger));
                rig.index.held = index;
            }
            if (rig.thumb != null)
            {
                Vector3 thumb = Vector3.Lerp(thumbRestCurl, thumbDownCurl, s.thumbDown);
                Pose(rig.thumb, thumb);
                rig.thumb.held = thumb;
                StoreThumb(rig);
            }
        }

        void UpdateGunGrip(Side s, VRAssemblyGrab gunGrab, bool selected, bool targetChanged, float k)
        {
            if (gunGrab == null) return;
            if (s.cachedGunGrab != gunGrab) CollectGunBoxes(s, gunGrab);

            UpdateGunTransform(s);
            HandRig rig = s.gunPoseRig;
            ApplyThumbLength(rig, thumbLength);

            bool wrapping = s.gripBoxes.Count > 0;
            float gripWeight = selected ? Mathf.Max(0.85f, s.grip) : 1f;
            float triggerInput = selected ? s.trigger : 0f;

            Vector3 above = Vector3.zero;
            foreach (Finger f in rig.wrap)
            {
                Vector3 target = gripCurl;
                if (wrapping)
                {
                    target = Vector3.Max(Solve(f, relaxedCurl, wrapMaxCurl, s.gripBoxes), above * wrapFollow);
                    above = target;
                }
                f.held = targetChanged ? target : Vector3.Lerp(f.held, target, k);
                Pose(f, Vector3.Lerp(relaxedCurl, f.held, gripWeight));
            }

            if (rig.index != null)
            {
                if (wrapping)
                {
                    Vector3 rest = Solve(rig.index, relaxedCurl, indexPullCurl, s.indexBoxes);
                    rig.index.held = targetChanged ? rest : Vector3.Lerp(rig.index.held, rest, k);
                    Pose(rig.index, Vector3.Max(Vector3.zero, rig.index.held + indexPullExtra * PullRoom(rig.index, rig.index.held, triggerInput, s.triggerSolids)));
                }
                else
                {
                    Vector3 index = Vector3.Lerp(relaxedCurl, indexTouchCurl, Mathf.Max(s.touch, triggerInput > 0.02f ? 1f : 0f));
                    Pose(rig.index, Vector3.Lerp(index, indexPullCurl, triggerInput));
                    rig.index.held = index;
                }
            }

            if (rig.thumb != null)
            {
                if (wrapping) PoseThumbWrap(s, rig, gunGrab, targetChanged ? 1f : k);
                else
                {
                    Vector3 thumb = Vector3.Lerp(thumbRestCurl, thumbDownCurl, s.thumbDown);
                    Pose(rig.thumb, thumb);
                    rig.thumb.held = thumb;
                    StoreThumb(rig);
                }
            }
        }

        void UpdateGunTransform(Side s)
        {
            if (!(s.activeTarget is VRAssemblyGrab gunGrab) || s.gunPoseRig == null) return;
            Transform attach = gunGrab.attachTransform != null ? gunGrab.attachTransform : gunGrab.transform;
            s.gunPoseRig.root.position = attach.position + attach.rotation * s.handPosInGun;
            s.gunPoseRig.root.rotation = attach.rotation * s.handRotInGun;
        }

        void UpdatePartGrip(Side s, IXRSelectInteractable target, bool selected, bool targetChanged, float k)
        {
            var comp = target as Component;
            if (comp == null) return;
            if (s.cachedPartComponent != comp) CollectPartBoxes(s, comp);

            HandRig rig = s.partPoseRig;
            ApplyThumbLength(rig, partThumbLength);

            // Place the separate part-grip hand using a fixed reference pinch point before fitting Index/Middle.
            ApplyPartStaticFingers(s, rig, setReferenceIndex: true);
            s.partPinchLocal = MeasurePinchLocal(rig);
            UpdatePartTransform(s, selected);

            for (int i = 0; i < rig.wrap.Count; i++)
            {
                Finger f = rig.wrap[i];
                if (i == 0)
                {
                    Vector3 fitted = s.partBoxes.Count > 0 ? Solve(f, partOpenCurl, partMaxCurl, s.partBoxes) : partMinCurl;
                    fitted = Vector3.Max(fitted, partMinCurl);
                    f.held = targetChanged ? fitted : Vector3.Lerp(f.held, fitted, k);
                    Pose(f, f.held);
                }
                else
                {
                    Vector3 curl = i == 1 ? partRingCurl : partLittleCurl;
                    f.held = targetChanged ? curl : Vector3.Lerp(f.held, curl, k);
                    Pose(f, f.held);
                }
            }

            if (rig.index != null)
            {
                Vector3 fitted = s.partBoxes.Count > 0 ? Solve(rig.index, partOpenCurl, partMaxCurl, s.partBoxes) : partPinchRefCurl;
                fitted = Vector3.Max(fitted, partMinCurl);
                rig.index.held = targetChanged ? fitted : Vector3.Lerp(rig.index.held, fitted, k);
                Pose(rig.index, rig.index.held);
            }
        }

        void UpdatePartTransform(Side s, bool selected)
        {
            var comp = s.activeTarget as Component;
            HandRig rig = s.partPoseRig;
            if (comp == null || rig == null || rig.root == null) return;

            Transform targetT = comp.transform;
            Vector3 anchorWorld;
            if (selected)
            {
                if (!s.targetWasSelected)
                {
                    Vector3 selectRef = GetSelectedAttachWorld(s, s.activeTarget);
                    s.heldAnchorLocal = s.hasCachedPartBounds
                        ? s.cachedPartBounds.ClosestPoint(targetT.InverseTransformPoint(selectRef))
                        : Vector3.zero;
                }
                anchorWorld = targetT.TransformPoint(s.heldAnchorLocal);
            }
            else
            {
                Vector3 hoverRef = HoverOriginWorld(s);
                Vector3 localAnchor = s.hasCachedPartBounds
                    ? s.cachedPartBounds.ClosestPoint(targetT.InverseTransformPoint(hoverRef))
                    : Vector3.zero;
                anchorWorld = targetT.TransformPoint(localAnchor);
            }

            Quaternion worldHandRot = s.controller.transform.rotation * s.handLocalRot;
            rig.root.rotation = worldHandRot;
            rig.root.position = anchorWorld - worldHandRot * s.partPinchLocal + worldHandRot * partGripOffset;
        }

        static Vector3 GetSelectedAttachWorld(Side s, IXRSelectInteractable target)
        {
            if (s.direct != null && s.direct.IsSelecting(target)) return s.direct.transform.position;
            if (s.grabInteractors != null)
            {
                foreach (XRBaseInteractor i in s.grabInteractors)
                    if (i != null && i.IsSelecting(target))
                        return i.GetAttachTransform(target).position;
            }
            return (target as Component).transform.position;
        }

        void ApplyPartStaticFingers(Side s, HandRig rig, bool setReferenceIndex)
        {
            if (setReferenceIndex && rig.index != null) Pose(rig.index, partPinchRefCurl);
            if (rig.thumb != null)
            {
                Pose(rig.thumb, thumbRestCurl);
                if (rig.thumb.bones.Length >= 3)
                {
                    ManualTurn(rig, s.left, 0, partThumbBaseRotation);
                    ManualTurn(rig, s.left, 1, partThumbKnuckleRotation);
                    ManualTurn(rig, s.left, 2, partThumbTipRotation);
                }
            }
        }

        static Vector3 MeasurePinchLocal(HandRig rig)
        {
            if (rig.index == null || rig.thumb == null) return Vector3.zero;
            Vector3 midpointWorld = Vector3.Lerp(TipPosition(rig.index), TipPosition(rig.thumb), 0.45f);
            return rig.root.InverseTransformPoint(midpointWorld);
        }

        static void ApplyThumbLength(HandRig rig, float scale)
        {
            if (rig.thumb == null || rig.thumb.bones.Length < 3) return;
            rig.thumb.bones[2].localPosition = rig.thumb.bindPos[2] * scale;
            if (rig.thumb.tip != null) rig.thumb.tip.localPosition = rig.thumb.tipBindPos * scale;
        }

        void CollectPartBoxes(Side s, Component comp)
        {
            s.cachedPartComponent = comp;
            s.partBoxes.Clear();
            s.hasCachedPartBounds = TryGetVisualBounds(comp, s.partBoxes, out s.cachedPartBounds);
        }

        bool TryGetVisualBounds(Component comp, List<Box> outBoxes, out Bounds localBounds)
        {
            localBounds = default;
            if (comp == null) return false;
            if (outBoxes == null && visualBoundsCache.TryGetValue(comp, out localBounds)) return true;

            Transform rootT = comp.transform;
            AssemblyPart ownPart = comp.GetComponent<AssemblyPart>();
            bool hasBounds = false;
            foreach (MeshFilter mf in rootT.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (ownPart != null && mf.GetComponentInParent<AssemblyPart>(true) != ownPart) continue;
                if (mf.GetComponentInParent<SnapSocket>(true) != null || mf.GetComponentInParent<Canvas>(true) != null) continue;
                Renderer r = mf.GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;

                if (outBoxes != null)
                {
                    Box box = FitBox(mf.sharedMesh);
                    box.t = mf.transform;
                    outBoxes.Add(box);
                }

                Matrix4x4 m = RelativeMatrix(rootT, mf.transform);
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!hasBounds) { localBounds = new Bounds(p, Vector3.zero); hasBounds = true; }
                    else localBounds.Encapsulate(p);
                }
            }

            if (!hasBounds)
            {
                BoxCollider bc = comp.GetComponent<BoxCollider>();
                if (bc != null)
                {
                    localBounds = new Bounds(bc.center, bc.size);
                    hasBounds = true;
                    if (outBoxes != null) outBoxes.Add(new Box { t = bc.transform, rot = Quaternion.identity, b = localBounds });
                }
            }
            if (hasBounds) visualBoundsCache[comp] = localBounds;
            return hasBounds;
        }

        static Matrix4x4 RelativeMatrix(Transform root, Transform target)
        {
            if (target == null || target == root) return Matrix4x4.identity;
            Transform parent = target.parent;
            Matrix4x4 local = Matrix4x4.TRS(target.localPosition, target.localRotation, target.localScale);
            if (parent == null || parent == root) return local;
            return RelativeMatrix(root, parent) * local;
        }

        static float Read(InputActionProperty property)
        {
            InputAction a = property.action;
            return a != null && a.enabled ? Mathf.Clamp01(a.ReadValue<float>()) : 0f;
        }

        // Touch search: starting open, bend the knuckle in small steps until the finger (from that joint out to the tip)
        // touches a box, back off one step, then do the same for the middle and last joints. The bones are left posed
        // at the result; the caller re-poses with the smoothed value.
        Vector3 Solve(Finger f, Vector3 from, Vector3 max, List<Box> boxes)
        {
            if (f == null) return from;
            Vector3 c = from;
            Pose(f, c);
            float step = Mathf.Max(0.5f, wrapStep);
            for (int j = 0; j < f.bones.Length && j < 3; j++)
            {
                float a = from[j];
                // Segments already touching in the open pose (e.g. the finger base resting on the grip) are ignored,
                // otherwise they would stop the finger from bending at all.
                int first = SkipTouching(f, j, boxes);
                while (a < max[j] && first < f.bones.Length)
                {
                    float next = Mathf.Min(a + step, max[j]);
                    SetJoint(f, j, next);
                    if (ChainHits(f, first, boxes))
                    {
                        SetJoint(f, j, a);
                        break;
                    }
                    a = next;
                }
                c[j] = a;
            }
            return c;
        }

        // Thumb wraps the opposite way round the grip to the fingers, like a real pistol grip: the palm-to-knuckle bone
        // crosses behind the backstrap so the knuckle ends at the far back corner, then the two outer segments lie
        // forward along the far panel. A path is laid round the grip at thumb height, one finger-thickness off the
        // surface (grip space VR_Grip_Attach: Z muzzle, Y hammer):
        //   palm-side back corner -> far back corner (behind the backstrap) -> forward along the far panel
        // and each joint in turn is placed on it: every bone is turned so its end lands on the path one bone-length on
        // from where the previous one ended. So the thumb follows the grip and never cuts through it or hangs in the air.
        void PoseThumbWrap(Side s, HandRig rig, VRAssemblyGrab gunGrab, float k)
        {
            Finger t = rig.thumb;
            Pose(t, thumbRestCurl);
            if (t.bones.Length >= 3 && thumbAutoWrap && TryThumbPath(s, rig, gunGrab, out Vector3[] path))
            {
                rig.debugPath = path;
                Transform g = gunGrab != null ? gunGrab.attachTransform : null;
                float seg0 = Vector3.Distance(path[0], path[1]);
                Vector3 baseTarget = PathPoint(path, seg0 + thumbBaseAimForward) - (g != null ? g.up : Vector3.up) * thumbBaseAimDrop;
                Aim(t.bones[0], t.bones[1].position, baseTarget, 1f, thumbBaseTurnLimit);
                ManualTurn(rig, s.left, 0, thumbBaseRotation);
                float along = Vector3.Distance(path[0], path[1]);
                for (int i = 1; i < 3; i++)
                {
                    Transform b = t.bones[i];
                    Vector3 end = i + 1 < 3 ? t.bones[i + 1].position : TipPosition(t);
                    Vector3 target = OnPath(path, b.position, (end - b.position).magnitude, ref along);
                    Aim(b, end, target, 1f, 110f);
                    ManualTurn(rig, s.left, i, i == 1 ? thumbKnuckleRotation : thumbTipRotation);
                }
            }
            else if (t.bones.Length >= 3)
            {
                // Auto wrap off: rest pose plus the manual turns only, so every joint can be set by hand.
                ManualTurn(rig, s.left, 0, thumbBaseRotation);
                ManualTurn(rig, s.left, 1, thumbKnuckleRotation);
                ManualTurn(rig, s.left, 2, thumbTipRotation);
            }
            for (int i = 0; i < t.bones.Length && i < rig.thumbRot.Length; i++)
            {
                rig.thumbRot[i] = k >= 1f ? t.bones[i].localRotation : Quaternion.Slerp(rig.thumbRot[i], t.bones[i].localRotation, k);
                t.bones[i].localRotation = rig.thumbRot[i];
            }
        }

        // Manual extra turn of one thumb bone in its own axes, mirrored for the left hand.
        static void ManualTurn(HandRig rig, bool left, int bone, Vector3 euler)
        {
            if (euler == Vector3.zero || rig.thumb == null || bone >= rig.thumb.bones.Length) return;
            if (left) { euler.y = -euler.y; euler.z = -euler.z; }
            rig.thumb.bones[bone].localRotation *= Quaternion.Euler(euler);
        }

        void OnDrawGizmos()
        {
            if (!showThumbPath) return;
            Gizmos.color = Color.yellow;
            foreach (Side s in sides)
            {
                Vector3[] path = s.gunPoseRig?.debugPath;
                if (s.cachedGunGrab == null || path == null) continue;
                for (int i = 1; i < path.Length; i++) Gizmos.DrawLine(path[i - 1], path[i]);
                foreach (Vector3 p in path) Gizmos.DrawWireSphere(p, 0.003f);
            }
        }

        static void StoreThumb(HandRig rig)
        {
            if (rig.thumb == null) return;
            for (int i = 0; i < rig.thumb.bones.Length && i < rig.thumbRot.Length; i++)
                rig.thumbRot[i] = rig.thumb.bones[i].localRotation;
        }

        // Turns bone so the point 'from' (its child joint) heads towards 'to': weight of the full turn, capped in degrees.
        static void Aim(Transform bone, Vector3 from, Vector3 to, float weight, float maxTurn)
        {
            Vector3 have = from - bone.position, want = to - bone.position;
            if (have.sqrMagnitude < 1e-10f || want.sqrMagnitude < 1e-10f) return;
            float angle = Vector3.Angle(have, want);
            if (angle < 0.01f) return;
            float turn = Mathf.Min(angle * weight, maxTurn);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(have, want), turn / angle) * bone.rotation;
        }

        // Walks the polyline from distance 'along' (metres along it) in 2 mm steps and returns the first point that is
        // 'length' away from 'centre' (where the bone reaches the path). If the bone cannot reach the path, the nearest
        // path point ahead. 'along' is advanced so the next bone continues further along.
        static Vector3 OnPath(Vector3[] path, Vector3 centre, float length, ref float along)
        {
            const float step = 0.002f;
            float total = 0f;
            for (int i = 1; i < path.Length; i++) total += Vector3.Distance(path[i - 1], path[i]);
            float bestD = float.MaxValue, bestS = along;
            for (float s = along; s <= total; s += step)
            {
                Vector3 p = PathPoint(path, s);
                float d = Vector3.Distance(p, centre);
                if (d >= length && s > along)
                {
                    along = s;
                    return p;
                }
                if (Mathf.Abs(d - length) < bestD) { bestD = Mathf.Abs(d - length); bestS = s; }
            }
            along = bestS;
            return PathPoint(path, bestS);
        }

        static Vector3 PathPoint(Vector3[] path, float s)
        {
            for (int i = 1; i < path.Length; i++)
            {
                float len = Vector3.Distance(path[i - 1], path[i]);
                if (s <= len || i == path.Length - 1) return Vector3.Lerp(path[i - 1], path[i], len > 1e-6f ? Mathf.Clamp01(s / len) : 1f);
                s -= len;
            }
            return path[path.Length - 1];
        }

        bool TryThumbPath(Side s, HandRig rig, VRAssemblyGrab gunGrab, out Vector3[] path)
        {
            path = null;
            Transform g = gunGrab != null ? gunGrab.attachTransform : null;
            if (g == null || rig == null || rig.palm == null || s.gripBoxes.Count == 0) return false;

            // Grip outline in grip space. With the real surface points: only the slice at the thumb's height, so the
            // raked grip's lower back does not push the path cm out into the air. Without them: fitted box corners.
            var pts = new List<Vector3>();
            if (s.gripPoints.Count > 0) pts.AddRange(s.gripPoints);
            else
                foreach (Box box in s.gripBoxes)
                {
                    if (box.t == null) continue;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 corner = new Vector3(
                            (c & 1) == 0 ? box.b.min.x : box.b.max.x,
                            (c & 2) == 0 ? box.b.min.y : box.b.max.y,
                            (c & 4) == 0 ? box.b.min.z : box.b.max.z);
                        pts.Add(g.InverseTransformPoint(box.t.TransformPoint(box.rot * corner)));
                    }
                }
            if (pts.Count == 0) return false;

            float top = float.MinValue;
            foreach (Vector3 p in pts) top = Mathf.Max(top, p.y);
            float y = top - thumbDropFromTop;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (Vector3 p in pts)
                if (Mathf.Abs(p.y - y) < 0.008f) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            if (min.x > max.x)   // no points in the slice: whole outline
                foreach (Vector3 p in pts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }

            float pad = fingerRadius * 1.1f;
            float centreX = (min.x + max.x) * 0.5f;
            // Palm side from where the back of the hand faces (palm +Y), not from where the palm sits: with the hand
            // set back on the grip the palm is almost straight behind it and its position cannot tell the sides apart.
            bool palmOnPlusX = g.InverseTransformDirection(rig.palm.up).x > 0f;
            float farX = palmOnPlusX ? min.x - pad : max.x + pad;
            float backZ = min.z - pad;

            float nearX = palmOnPlusX ? max.x + pad : min.x - pad;
            path = new[]
            {
                g.TransformPoint(new Vector3(nearX, y, backZ)),                         // palm-side back corner
                g.TransformPoint(new Vector3(farX, y, backZ)),                          // far back corner
                g.TransformPoint(new Vector3(farX, y - thumbTipDrop, max.z + pad)),     // forward along the far panel
            };
            return true;
        }

        static void SetJoint(Finger f, int j, float angle) => f.bones[j].localRotation = f.bind[j] * Quaternion.Euler(angle, 0f, 0f);

        // Samples the finger from segment j out to the tip.
        bool ChainHits(Finger f, int j, List<Box> boxes)
        {
            for (int i = j; i < f.bones.Length; i++)
                if (SegmentHits(f, i, boxes)) return true;
            return false;
        }

        bool SegmentHits(Finger f, int i, List<Box> boxes)
        {
            int n = f.bones.Length;
            Vector3 a = f.bones[i].position;
            Vector3 b = i + 1 < n ? f.bones[i + 1].position : TipPosition(f);
            for (int sample = 1; sample <= 4; sample++)
                if (Touches(Vector3.Lerp(a, b, sample * 0.25f), boxes)) return true;
            return false;
        }

        // ---------- Trigger as a solid surface (real triangles, not a fitted box) ----------

        public sealed class Solid
        {
            public Transform t;
            public Vector3[] local;   // mesh vertices
            public int[] tris;
            public Vector3[] world;   // vertices in world space, refreshed each PullRoom (the trigger can move)
            public Bounds bounds;     // world bounds of 'world', grown by the finger thickness
        }

        static readonly Dictionary<Mesh, (Vector3[] v, int[] t)> SolidCache = new Dictionary<Mesh, (Vector3[], int[])>();

        static Solid MakeSolid(Transform t, Mesh mesh)
        {
            if (!SolidCache.TryGetValue(mesh, out var data)) SolidCache[mesh] = data = (mesh.vertices, mesh.triangles);
            return new Solid { t = t, local = data.v, tris = data.t, world = new Vector3[data.v.Length] };
        }

        const int PullSweepSteps = 16;
        bool[] pullFree = new bool[0];

        // How much of the trigger pull (0..pull) the index can bend by before its surface reaches the trigger's surface.
        // Swept in small steps from the rest pose so the thin trigger blade cannot be skipped, then refined.
        // If the trigger springs forward into the resting finger (release), the result goes negative: the finger is
        // pushed open until clear. Only segments that touch the trigger even with the finger straight are left out.
        float PullRoom(Finger f, Vector3 rest, float pull, List<Solid> solids)
        {
            if (f == null || solids.Count == 0) return pull;
            pull = Mathf.Max(0f, pull);
            float r = Mathf.Max(0.0005f, pullContactRadius);
            foreach (Solid s in solids)
            {
                if (s.t == null) continue;
                Matrix4x4 m = s.t.localToWorldMatrix;
                for (int i = 0; i < s.local.Length; i++) s.world[i] = m.MultiplyPoint3x4(s.local[i]);
                var b = new Bounds(s.world.Length > 0 ? s.world[0] : s.t.position, Vector3.zero);
                for (int i = 1; i < s.world.Length; i++) b.Encapsulate(s.world[i]);
                b.Expand(2f * r);
                s.bounds = b;
            }
            int n = f.bones.Length;
            if (pullFree.Length != n) pullFree = new bool[n];
            Pose(f, Vector3.zero);
            for (int i = 0; i < n; i++) pullFree[i] = !SegmentOnSolid(f, i, solids, r);

            if (PullHits(f, rest, solids, r))
            {
                // Trigger is over the resting finger: open it (down to straight) until clear.
                float inside = 0f;
                for (int k = 1; k <= PullSweepSteps; k++)
                {
                    float t = -(float)k / PullSweepSteps;
                    if (PullHits(f, rest + indexPullExtra * t, solids, r)) { inside = t; continue; }
                    float lo = t, hi = inside;   // lo clear, hi touching
                    for (int i = 0; i < 6; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (PullHits(f, rest + indexPullExtra * mid, solids, r)) hi = mid; else lo = mid;
                    }
                    return lo;
                }
                return -1f;
            }
            if (pull <= 0f) return 0f;

            float prev = 0f;
            for (int k = 1; k <= PullSweepSteps; k++)
            {
                float t = pull * k / PullSweepSteps;
                if (!PullHits(f, rest + indexPullExtra * t, solids, r)) { prev = t; continue; }
                float lo = prev, hi = t;
                for (int i = 0; i < 6; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (PullHits(f, rest + indexPullExtra * mid, solids, r)) hi = mid; else lo = mid;
                }
                return lo;
            }
            return pull;
        }

        bool PullHits(Finger f, Vector3 curl, List<Solid> solids, float r)
        {
            Pose(f, curl);
            for (int i = 0; i < pullFree.Length; i++)
                if (pullFree[i] && SegmentOnSolid(f, i, solids, r)) return true;
            return false;
        }

        static bool SegmentOnSolid(Finger f, int i, List<Solid> solids, float r)
        {
            int n = f.bones.Length;
            Vector3 a = f.bones[i].position;
            Vector3 b = i + 1 < n ? f.bones[i + 1].position : TipPosition(f);
            for (int sample = 0; sample <= 6; sample++)
                if (OnSolid(Vector3.Lerp(a, b, sample / 6f), solids, r)) return true;
            return false;
        }

        static bool OnSolid(Vector3 p, List<Solid> solids, float r)
        {
            float r2 = r * r;
            foreach (Solid s in solids)
            {
                if (s.t == null || !s.bounds.Contains(p)) continue;
                Vector3[] w = s.world;
                int[] t = s.tris;
                for (int i = 0; i + 2 < t.Length; i += 3)
                    if ((ClosestOnTriangle(p, w[t[i]], w[t[i + 1]], w[t[i + 2]]) - p).sqrMagnitude < r2) return true;
            }
            return false;
        }

        // Closest point to p on triangle abc (Ericson, Real-Time Collision Detection 5.1.5).
        static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            float v = vb * denom, w2 = vc * denom;
            return a + ab * v + ac * w2;
        }

        // First segment from j outwards that is not already touching in the current pose.
        int SkipTouching(Finger f, int j, List<Box> boxes)
        {
            while (j < f.bones.Length && SegmentHits(f, j, boxes)) j++;
            return j;
        }

        static Vector3 TipPosition(Finger f)
        {
            int n = f.bones.Length;
            if (f.tip != null) return f.tip.position;
            Vector3 last = f.bones[n - 1].position, prev = f.bones[n - 2].position;
            return last + (last - prev) * 0.8f;
        }

        bool Touches(Vector3 p, List<Box> boxes)
        {
            float r2 = fingerRadius * fingerRadius;
            foreach (Box box in boxes)
            {
                if (box.t == null) continue;
                Vector3 local = Quaternion.Inverse(box.rot) * box.t.InverseTransformPoint(p);
                Vector3 closest = Vector3.Max(box.b.min, Vector3.Min(box.b.max, local));
                if ((box.t.TransformPoint(box.rot * closest) - p).sqrMagnitude < r2) return true;
            }
            return false;
        }

        // Bends each joint about its own side axis (+X curls towards the palm in hand joint space).
        static void Pose(Finger f, Vector3 curl)
        {
            if (f == null) return;
            for (int i = 0; i < f.bones.Length; i++)
                f.bones[i].localRotation = f.bind[i] * Quaternion.Euler(curl[i], 0f, 0f);
        }
    }
}
