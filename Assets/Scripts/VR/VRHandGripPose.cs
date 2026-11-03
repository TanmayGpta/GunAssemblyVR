using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;
using UnityEngine.XR.Hands.Processing;

namespace VRArmory
{
    // Hybrid grip for tracked hands. While a hand holds the pistol, its middle, ring and little fingers are bent into a
    // fixed wrap around the grip, so they do not sink through the frame. The index finger and thumb stay live-tracked,
    // which is what lets the real index finger pull the trigger. Runs as an XR Hands processor, so the hand mesh and
    // everything else reading joint data sees the same pose. Bone lengths come from the tracked hand, so it fits any
    // hand size.
    public class VRHandGripPose : MonoBehaviour, IXRHandProcessor
    {
        [Tooltip("Bend at the knuckle, middle joint and last joint, in degrees.")]
        public Vector3 middleCurl = new Vector3(70f, 85f, 45f);
        public Vector3 ringCurl = new Vector3(75f, 85f, 45f);
        public Vector3 littleCurl = new Vector3(80f, 85f, 45f);

        public int callbackOrder => 100; // after smoothing processors

        static bool s_LeftGrip, s_RightGrip;
        static float s_LeftRawCurl = -1f, s_RightRawCurl = -1f;

        // Average middle + ring finger curl as tracked, measured before the grip wrap bends them (so a hand holding the
        // pistol can still open to let go). -1 when the hand is not tracked.
        public static float RawGraspCurl(Handedness side) => side == Handedness.Left ? s_LeftRawCurl : s_RightRawCurl;

        // Called by whoever owns the grab (VRAssemblyGrab on the frame).
        public static void SetGrip(Handedness side, bool on)
        {
            if (side == Handedness.Left) s_LeftGrip = on;
            else if (side == Handedness.Right) s_RightGrip = on;
        }

        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();
        XRHandSubsystem m_Subsystem;

        void OnDisable()
        {
            m_Subsystem?.UnregisterProcessor(this);
            m_Subsystem = null;
            s_LeftGrip = s_RightGrip = false;
        }

        void Update()
        {
            if (m_Subsystem != null && m_Subsystem.running) return;
            m_Subsystem?.UnregisterProcessor(this);
            m_Subsystem = null;
            SubsystemManager.GetSubsystems(s_Subsystems);
            foreach (XRHandSubsystem s in s_Subsystems)
            {
                if (!s.running) continue;
                m_Subsystem = s;
                s.RegisterProcessor(this);
                break;
            }
        }

        public void ProcessJoints(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags successFlags, XRHandSubsystem.UpdateType updateType)
        {
            s_LeftRawCurl = GraspCurl(subsystem.leftHand);
            s_RightRawCurl = GraspCurl(subsystem.rightHand);
            if (s_LeftGrip) Wrap(subsystem, subsystem.leftHand);
            if (s_RightGrip) Wrap(subsystem, subsystem.rightHand);
        }

        static float GraspCurl(XRHand hand)
        {
            if (!hand.isTracked) return -1f;
            if (!hand.CalculateFingerShape(XRHandFingerID.Middle, XRFingerShapeTypes.FullCurl).TryGetFullCurl(out float middle)) return -1f;
            if (!hand.CalculateFingerShape(XRHandFingerID.Ring, XRFingerShapeTypes.FullCurl).TryGetFullCurl(out float ring)) return -1f;
            return (middle + ring) * 0.5f;
        }

        void Wrap(XRHandSubsystem subsystem, XRHand hand)
        {
            if (!hand.isTracked) return;
            NativeArray<XRHandJoint> joints = hand.GetRawJointArray();
            Bend(joints, XRHandJointID.MiddleMetacarpal, middleCurl);
            Bend(joints, XRHandJointID.RingMetacarpal, ringCurl);
            Bend(joints, XRHandJointID.LittleMetacarpal, littleCurl);
            subsystem.SetCorrespondingHand(hand);
        }

        // Rebuilds proximal -> intermediate -> distal -> tip from the metacarpal's orientation, bending each joint
        // about its own side axis. In XR Hands joint space +Z runs along the bone and +Y is the back of the hand, so a
        // positive X rotation curls the finger towards the palm on both hands.
        static void Bend(NativeArray<XRHandJoint> joints, XRHandJointID metacarpal, Vector3 curl)
        {
            int m = metacarpal.ToIndex();
            if (!joints[m].TryGetPose(out Pose meta)) return;

            var poses = new Pose[4];
            for (int i = 0; i < 4; i++)
                if (!joints[m + 1 + i].TryGetPose(out poses[i])) return;

            float lenProx = Vector3.Distance(poses[0].position, poses[1].position);
            float lenInter = Vector3.Distance(poses[1].position, poses[2].position);
            float lenDistal = Vector3.Distance(poses[2].position, poses[3].position);

            Quaternion rProx = meta.rotation * Quaternion.Euler(curl.x, 0f, 0f);
            Quaternion rInter = rProx * Quaternion.Euler(curl.y, 0f, 0f);
            Quaternion rDistal = rInter * Quaternion.Euler(curl.z, 0f, 0f);

            Vector3 pProx = poses[0].position; // knuckle stays where it is tracked
            Vector3 pInter = pProx + rProx * Vector3.forward * lenProx;
            Vector3 pDistal = pInter + rInter * Vector3.forward * lenInter;
            Vector3 pTip = pDistal + rDistal * Vector3.forward * lenDistal;

            Set(joints, m + 1, new Pose(pProx, rProx));
            Set(joints, m + 2, new Pose(pInter, rInter));
            Set(joints, m + 3, new Pose(pDistal, rDistal));
            Set(joints, m + 4, new Pose(pTip, rDistal));
        }

        static void Set(NativeArray<XRHandJoint> joints, int index, Pose pose)
        {
            XRHandJoint j = joints[index];
            j.SetPose(pose);
            joints[index] = j;
        }
    }
}
