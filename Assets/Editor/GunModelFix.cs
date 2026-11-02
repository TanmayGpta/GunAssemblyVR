#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRArmory.Editor
{
    // beretta92.fbx is an exploded view and Re-Stage only collapses it approximately, so the assembled pistol had floating
    // pieces and see-through gaps. This puts every piece where it belongs and fills the gaps with plain static blocks
    // (group "Static_Detail": no collider, no script, not grabbable). Safe to re-run: pieces are placed at absolute targets.
    //
    // All numbers are millimetres in gun axes: -X = muzzle, +Y = the side the takedown lever is on, +Z = up.
    // They share one origin, measured from the model, in which Frame_Body and Slide_Body have the *Min values below.
    public static class GunModelFix
    {
        static readonly Vector3 FrameMin = new Vector3(-104.7f, -23.9f, -81.7f);
        static readonly Vector3 SlideMin = new Vector3(-122.8f, -22.8f, 36.0f);

        // The slide stood on top of the frame rails; lowered, the rails sit inside it and its nose meets the dust cover.
        const float SlideDrop = 19f;

        const string DetailGroup = "Static_Detail";

        [MenuItem("Tools/VR Armory/Fix Gun Model (static filler)")]
        public static void FromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Apply()) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // Command line: -executeMethod VRArmory.Editor.GunModelFix.ApplyBatch
        public static void ApplyBatch()
        {
            var scene = EditorSceneManager.OpenScene(VRProjectSetup.ScenePath);
            if (Apply()) EditorSceneManager.SaveScene(scene);
        }

        public static bool Apply()
        {
            GameObject station = GameObject.Find("ArmorerStation");
            Transform parts = station != null ? station.transform.Find("Staged_Gun_Parts") : null;
            Transform frame = parts != null ? parts.Find("01_Lower_Frame") : null;
            Transform slide = parts != null ? parts.Find("02_Upper_Slide") : null;
            Transform barrel = parts != null ? parts.Find("03_Barrel") : null;
            Transform mag = parts != null ? parts.Find("05_15Rd_Magazine") : null;
            Transform mat = station != null ? station.transform.Find("Workbench/Armorer_Rubber_Mat") : null;
            if (frame == null || slide == null || barrel == null || mag == null || mat == null)
            {
                Debug.LogError("[VR Armory] Fix Gun Model: gun modules or the bench mat not found.");
                return false;
            }
            Transform frameBody = frame.Find("Frame_Body"), slideBody = slide.Find("Slide_Body");
            Transform seatSlide = frame.Find("Socket_Slide"), seatMag = frame.Find("Socket_Magazine");
            if (frameBody == null || slideBody == null || seatSlide == null || seatMag == null)
            {
                Debug.LogError("[VR Armory] Fix Gun Model: Frame_Body, Slide_Body or a socket not found.");
                return false;
            }

            // --- Slide seat: move the socket so the seated Slide_Body lands SlideDrop lower on the frame ---
            Vector3 frameOrigin = Min(frameBody, frame) - Mm(FrameMin);
            Vector3 slideTarget = frameOrigin + Mm(SlideMin + new Vector3(0f, 0f, -SlideDrop));
            Vector3 slideNow = Local(frame, seatSlide.position) + Min(slideBody, slide);
            seatSlide.position += frame.rotation * (slideTarget - slideNow);

            // --- Pieces on the frame ---
            Place(frame, frameBody, FrameMin, "Trigger_Shoe", new Vector3(-3.6f, -13.2f, -17f));   // exploded straight up: back into the guard
            Place(frame, frameBody, FrameMin, "Hammer", new Vector3(78.7f, -13.4f, 6.2f));         // behind the slide, base in the frame
            Place(frame, frameBody, FrameMin, "Takedown_Latch", new Vector3(-28.8f, -14f, 7f));    // shaft through the frame, lever on its side
            Place(frame, frameBody, FrameMin, "Grip_Panel_Left", new Vector3(25.7f, 5.5f, -81.2f));
            Place(frame, frameBody, FrameMin, "Grip_Panel_Right", new Vector3(24.3f, -29.9f, -81.2f));

            // --- Pieces on the slide ---
            Place(slide, slideBody, SlideMin, "Rear_Sight", new Vector3(52.3f, -14.7f, 73.6f));    // on the slide, not above it
            Place(slide, slideBody, SlideMin, "Safety_Axle", new Vector3(46.7f, -17.1f, 57.1f));   // through the slide, knob on its side

            // --- Pieces on the barrel: locking block under the barrel, centred on it ---
            Transform barrelMain = barrel.Find("Barrel_Assembly");
            if (barrelMain != null) PlaceRelative(barrel, barrelMain, "Locking_Block", new Vector3(84.4f, -3.6f, -0.4f));

            // --- Magazine: tube back out of the trigger guard, floorplate under the tube ---
            Vector3 magSeat = Local(frame, seatMag.position);
            PlaceSeated(mag, magSeat, frameOrigin, "Magazine_Tube", new Vector3(20f, -19.5f, -78.5f));
            PlaceSeated(mag, magSeat, frameOrigin, "Magazine_Floorplate", new Vector3(50.5f, -22.3f, -89.5f));

            // --- Filler: only inside the gun, where you could otherwise see straight through it ---
            Transform frameDetail = ResetGroup(frame);
            Block(frameDetail, frame, frameOrigin, "Frame_Core_Front", new Vector3(-30f, -19.5f, 10f), new Vector3(26f, 2.7f, 27.5f), 0f);
            Block(frameDetail, frame, frameOrigin, "Frame_Core_Rear", new Vector3(26f, -19.5f, -20f), new Vector3(84f, 2.7f, 27.5f), 0f);
            Block(frameDetail, frame, frameOrigin, "Grip_Backstrap_Fill", new Vector3(76f, -20f, -78f), new Vector3(88f, 3f, 0f), 14f);
            Block(frameDetail, frame, frameOrigin, "Grip_Frontstrap_Fill", new Vector3(36f, -20f, -78f), new Vector3(46f, 3f, -12f), 14f);

            Vector3 slideOrigin = Min(slideBody, slide) - Mm(SlideMin);
            Transform slideDetail = ResetGroup(slide);
            Block(slideDetail, slide, slideOrigin, "Slide_Breech_Fill", new Vector3(14f, -18.5f, 50f), new Vector3(66f, 1.6f, 65f), 0f);
            Block(slideDetail, slide, slideOrigin, "Slide_Rear_Fill", new Vector3(66f, -18.5f, 50f), new Vector3(80f, 1.6f, 56f), 0f);

            // --- Colliders and bench contact follow the new shapes (positions on the bench otherwise unchanged) ---
            float matTop = mat.GetComponent<Renderer>().bounds.max.y;
            foreach (Transform m in parts)
            {
                if (m.GetComponent<AssemblyPart>() == null) continue;
                AssemblySetup.FitCollider(m);
                m.position += Vector3.up * (matTop + 0.001f - AssemblySetup.WorldBounds(m).min.y);
            }

            Debug.Log("[VR Armory] Gun model fixed: pieces re-seated, slide lowered onto the rails, static filler added.");
            return true;
        }

        static Vector3 Mm(Vector3 v) => v * 0.001f;

        // A world point in the module's axes, in metres, measured from the module root.
        static Vector3 Local(Transform module, Vector3 world) => Quaternion.Inverse(module.rotation) * (world - module.position);

        // Lowest corner of a piece's mesh bounds in the module's axes.
        static Vector3 Min(Transform piece, Transform module)
        {
            Bounds b = piece.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 min = Vector3.positiveInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                min = Vector3.Min(min, Local(module, piece.TransformPoint(c)));
            }
            return min;
        }

        static void Place(Transform module, Transform body, Vector3 bodyMin, string piece, Vector3 targetMin)
        {
            Transform t = module.Find(piece);
            if (t == null) { Debug.LogWarning($"[VR Armory] Fix Gun Model: {piece} not found."); return; }
            Vector3 origin = Min(body, module) - Mm(bodyMin);
            t.position += module.rotation * (origin + Mm(targetMin) - Min(t, module));
        }

        // targetOffset = piece minimum relative to the anchor piece's minimum.
        static void PlaceRelative(Transform module, Transform anchor, string piece, Vector3 targetOffset)
        {
            Transform t = module.Find(piece);
            if (t == null) { Debug.LogWarning($"[VR Armory] Fix Gun Model: {piece} not found."); return; }
            t.position += module.rotation * (Min(anchor, module) + Mm(targetOffset) - Min(t, module));
        }

        // For a part whose target is given on the frame while it is seated in a frame socket (seat poses share the frame's axes).
        static void PlaceSeated(Transform module, Vector3 seatInFrame, Vector3 frameOrigin, string piece, Vector3 targetMin)
        {
            Transform t = module.Find(piece);
            if (t == null) { Debug.LogWarning($"[VR Armory] Fix Gun Model: {piece} not found."); return; }
            t.position += module.rotation * (frameOrigin + Mm(targetMin) - (seatInFrame + Min(t, module)));
        }

        static Transform ResetGroup(Transform module)
        {
            Transform old = module.Find(DetailGroup);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform g = new GameObject(DetailGroup).transform;
            g.SetParent(module, false);
            return g;
        }

        // A box between two corners; lean tilts it about the width axis so grip fillers follow the rake of the grip.
        static void Block(Transform group, Transform module, Vector3 origin, string name, Vector3 min, Vector3 max, float lean)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(group, false);
            go.transform.rotation = module.rotation * Quaternion.Euler(0f, -lean, 0f);
            go.transform.position = module.position + module.rotation * (origin + Mm((min + max) * 0.5f));
            Vector3 world = Mm(max - min), scale = group.lossyScale;
            go.transform.localScale = new Vector3(world.x / scale.x, world.y / scale.y, world.z / scale.z);

            Renderer source = module.GetComponentInChildren<MeshFilter>().GetComponent<Renderer>();
            go.GetComponent<Renderer>().sharedMaterial = source.sharedMaterial;
        }
    }
}
#endif
