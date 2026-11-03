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

        static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };

        class Finger
        {
            public Transform[] bones;       // proximal, intermediate, distal (thumb: metacarpal, proximal, distal)
            public Quaternion[] bind;
        }

        class Side
        {
            public bool left;
            public ActionBasedController controller;
            public Transform root, pistolAttach;
            public Quaternion palmRot;
            public Vector3 palmPos;
            public XRBaseController[] controllers;
            public Finger index, thumb;
            public readonly List<Finger> wrap = new List<Finger>();
            public InputAction triggerTouch, thumbTouch;
            public float grip, trigger, touch, thumbDown;
            public readonly Dictionary<IXRInteractor, Transform> originalAttach = new Dictionary<IXRInteractor, Transform>();
        }

        readonly List<Side> sides = new List<Side>();

        void Start()
        {
            if (hands == null || hands.modality == null) return;
            Build(true, hands.modality.leftController, leftHandPrefab);
            Build(false, hands.modality.rightController, rightHandPrefab);
        }

        void OnDestroy()
        {
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
            hand.name = left ? "VR_Controller_Hand_Left" : "VR_Controller_Hand_Right";

            string p = left ? "L_" : "R_";
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in hand.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
            if (!bones.TryGetValue(p + "Palm", out Transform palm))
            {
                Debug.LogWarning($"[VR Armory] Controller hand: {p}Palm not found in {prefab.name}.", this);
                Destroy(holder);
                return;
            }
            s.index = MakeFinger(bones, p + "IndexProximal", p + "IndexIntermediate", p + "IndexDistal");
            s.thumb = MakeFinger(bones, p + "ThumbMetacarpal", p + "ThumbProximal", p + "ThumbDistal");
            foreach (string f in new[] { "Middle", "Ring", "Little" })
            {
                Finger finger = MakeFinger(bones, p + f + "Proximal", p + f + "Intermediate", p + f + "Distal");
                if (finger != null) s.wrap.Add(finger);
            }

            // Palm pose in the hand's own space (bind pose).
            Transform root = hand.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            Quaternion palmRot = Quaternion.Inverse(root.rotation) * palm.rotation;
            Vector3 palmPos = root.InverseTransformPoint(palm.position);

            root.SetParent(s.controller.transform, false);
            Destroy(holder);
            s.root = root;
            s.palmRot = palmRot;
            s.palmPos = palmPos;
            s.controllers = controllerObject.GetComponentsInChildren<XRBaseController>(true);

            s.pistolAttach = new GameObject("VR_Controller_Pistol_Attach").transform;
            s.pistolAttach.SetParent(s.controller.transform, false);
            Place(s);

            foreach (XRBaseInteractor i in s.controller.GetComponentsInChildren<XRBaseInteractor>(true))
            {
                if (i is XRPokeInteractor) continue;
                i.selectEntered.AddListener(args => OnSelect(s, args, true));
                i.selectExited.AddListener(args => OnSelect(s, args, false));
            }

            string device = left ? "{LeftHand}" : "{RightHand}";
            s.triggerTouch = new InputAction(type: InputActionType.Button, binding: $"<XRController>{device}/triggerTouched");
            s.thumbTouch = new InputAction(type: InputActionType.Button);
            foreach (string control in new[] { "thumbstickTouched", "primaryTouched", "secondaryTouched", "thumbrestTouched" })
                s.thumbTouch.AddBinding($"<XRController>{device}/{control}");
            s.triggerTouch.Enable();
            s.thumbTouch.Enable();
            sides.Add(s);
        }

        static Finger MakeFinger(Dictionary<string, Transform> bones, params string[] names)
        {
            var f = new Finger { bones = new Transform[names.Length], bind = new Quaternion[names.Length] };
            for (int i = 0; i < names.Length; i++)
            {
                if (!bones.TryGetValue(names[i], out Transform t)) return null;
                f.bones[i] = t;
                f.bind[i] = t.localRotation;
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
            }
            else if (s.originalAttach.TryGetValue(interactor, out Transform original))
            {
                interactor.attachTransform = original;
                s.originalAttach.Remove(interactor);
            }
        }

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
            s.root.localRotation = targetRot * Quaternion.Inverse(s.palmRot);
            s.root.localPosition = targetPos - s.root.localRotation * s.palmPos;

            // Pistol in the fist: muzzle along the knuckles, slide towards the thumb.
            Vector3 thumbSide = Vector3.Cross(knuckles, dorsal) * (s.left ? -1f : 1f);
            s.pistolAttach.localPosition = positionOffset;
            s.pistolAttach.localRotation = Quaternion.Euler(pistolRotationOffset) * Quaternion.LookRotation(knuckles, thumbSide);
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

        void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-fingerSpeed * Time.deltaTime);
            foreach (Side s in sides)
            {
                if (s.root == null || !s.controller.isActiveAndEnabled) continue;
                HideControllerModels(s);
                Place(s);
                s.grip = Mathf.Lerp(s.grip, Read(s.controller.selectActionValue), k);
                s.trigger = Mathf.Lerp(s.trigger, Read(s.controller.activateActionValue), k);
                s.touch = Mathf.Lerp(s.touch, s.triggerTouch.IsPressed() ? 1f : 0f, k);
                s.thumbDown = Mathf.Lerp(s.thumbDown, s.thumbTouch.IsPressed() || s.grip > 0.5f ? 1f : 0f, k);

                foreach (Finger f in s.wrap) Pose(f, Vector3.Lerp(relaxedCurl, gripCurl, s.grip));
                Vector3 index = Vector3.Lerp(relaxedCurl, indexTouchCurl, Mathf.Max(s.touch, s.trigger > 0.02f ? 1f : 0f));
                Pose(s.index, Vector3.Lerp(index, indexPullCurl, s.trigger));
                Pose(s.thumb, Vector3.Lerp(thumbRestCurl, thumbDownCurl, s.thumbDown));
            }
        }

        static float Read(InputActionProperty property)
        {
            InputAction a = property.action;
            return a != null && a.enabled ? Mathf.Clamp01(a.ReadValue<float>()) : 0f;
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
