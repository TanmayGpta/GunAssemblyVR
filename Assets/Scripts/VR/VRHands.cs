using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace VRArmory
{
    // One place that answers "is this interactor a tracked hand or a controller, and which side?" and reads finger
    // curl from XR Hands. The XR Input Modality Manager on the rig swaps the controller and hand objects automatically
    // (controllers picked up -> controllers, put down -> tracked hands), so every question is answered from that.
    public class VRHands : MonoBehaviour
    {
        public XRInputModalityManager modality;

        public static VRHands Instance { get; private set; }

        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();
        XRHandSubsystem m_Subsystem;

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        public XRHandSubsystem Subsystem
        {
            get
            {
                if (m_Subsystem != null && m_Subsystem.running) return m_Subsystem;
                m_Subsystem = null;
                SubsystemManager.GetSubsystems(s_Subsystems);
                foreach (XRHandSubsystem s in s_Subsystems)
                {
                    if (s.running)
                    {
                        m_Subsystem = s;
                        break;
                    }
                }
                return m_Subsystem;
            }
        }

        // True when the interactor lives under the rig's tracked-hand object (not a controller).
        public bool IsTrackedHand(IXRInteractor interactor, out Handedness side)
        {
            side = Handedness.Invalid;
            if (interactor == null || modality == null) return false;
            Transform t = interactor.transform;
            if (modality.leftHand != null && t.IsChildOf(modality.leftHand.transform)) side = Handedness.Left;
            else if (modality.rightHand != null && t.IsChildOf(modality.rightHand.transform)) side = Handedness.Right;
            return side != Handedness.Invalid;
        }

        // True for the left controller or the left tracked hand.
        public bool IsLeft(IXRInteractor interactor)
        {
            if (interactor == null || modality == null) return false;
            Transform t = interactor.transform;
            return (modality.leftHand != null && t.IsChildOf(modality.leftHand.transform))
                   || (modality.leftController != null && t.IsChildOf(modality.leftController.transform));
        }

        Transform m_TrackingSpace;

        // World position of a hand joint (joint poses are in the XR Origin's tracking space).
        public bool TryGetJointPosition(Handedness side, XRHandJointID joint, out Vector3 world)
        {
            world = default;
            XRHandSubsystem subsystem = Subsystem;
            if (subsystem == null) return false;
            if (m_TrackingSpace == null)
            {
                XROrigin origin = modality != null ? modality.GetComponentInParent<XROrigin>() : null;
                if (origin == null) origin = FindObjectOfType<XROrigin>();
                if (origin == null) return false;
                m_TrackingSpace = origin.CameraFloorOffsetObject.transform;
            }
            XRHand hand = side == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked || !hand.GetJoint(joint).TryGetPose(out Pose pose)) return false;
            world = m_TrackingSpace.TransformPoint(pose.position);
            return true;
        }

        // The pointing ray of the left or right tracked hand (null if the rig has none).
        public XRRayInteractor HandRay(bool leftSide)
        {
            GameObject hand = modality == null ? null : leftSide ? modality.leftHand : modality.rightHand;
            return hand != null ? hand.GetComponentInChildren<XRRayInteractor>(true) : null;
        }

        // 0 = finger straight, 1 = fully curled, as estimated by the platform's finger-shape configuration.
        public bool TryGetFingerCurl(Handedness side, XRHandFingerID finger, out float curl)
        {
            curl = 0f;
            XRHandSubsystem subsystem = Subsystem;
            if (subsystem == null) return false;
            XRHand hand = side == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked) return false;
            return hand.CalculateFingerShape(finger, XRFingerShapeTypes.FullCurl).TryGetFullCurl(out curl);
        }
    }
}
