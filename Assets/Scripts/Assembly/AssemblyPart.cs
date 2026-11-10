using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRArmory
{
    // Scenes store these as numbers: new values go at the end.
    public enum GunPart { LowerFrame, UpperSlide, Barrel, RecoilSpring, Magazine, Bolt, TakedownPin, UpperReceiver }

    // Grabbable gun sub-assembly. Input-agnostic: the desktop interactor drives BeginGrab / MoveHeld / EndGrab,
    // and for Quest an XRGrabInteractable adapter only needs to call the same methods from selectEntered / selectExited.
    [DisallowMultipleComponent]
    public class AssemblyPart : MonoBehaviour
    {
        public GunPart partType;
        public float returnDuration = 0.3f;
        public Color highlightColor = new Color(0.3f, 1.0f, 0.85f);

        public static readonly List<AssemblyPart> HeldParts = new List<AssemblyPart>();
        // Raised on release (before the part decides to return home) so sockets can claim it.
        public static event Action<AssemblyPart> Released;
        // Base sub-assembly held steady in the off hand (frame or slide) while the main hand inserts parts into it.
        public static AssemblyPart OffHand { get; private set; }

        public bool IsHeld { get; private set; }
        public bool IsInOffHand => OffHand == this;
        public Collider[] Colliders => colliders;
        public bool IsSeated { get; private set; }
        public bool IsGrabbable => !IsSeated && !isAnimating && !IsHeld;
        public Quaternion HomeWorldRotation => homeParent != null ? homeParent.rotation * homeLocalRot : homeLocalRot;

        private Transform homeParent;
        private Vector3 homeLocalPos;
        private Quaternion homeLocalRot;
        private Vector3 homeLocalScale;
        private Renderer[] renderers;
        private Collider[] colliders;
        private int[] colliderLayers;
        private MaterialPropertyBlock block;
        private bool isAnimating;
        private bool claimedOnRelease;
        private bool layersHidden;

        void Awake()
        {
            homeParent = transform.parent;
            homeLocalPos = transform.localPosition;
            homeLocalRot = transform.localRotation;
            homeLocalScale = transform.localScale;
            renderers = GetComponentsInChildren<Renderer>();
            colliders = GetComponentsInChildren<Collider>();
            colliderLayers = new int[colliders.Length];
            block = new MaterialPropertyBlock();
        }

        void OnDisable()
        {
            HeldParts.Remove(this);
            if (OffHand == this) OffHand = null;
        }

        public void SetHighlight(bool on)
        {
            SetHighlight(on ? 0.45f : 0f);
        }

        // Partial tint toward highlightColor (0 = off), e.g. the pulsing "pick me up" glow of the VR guidance.
        public void SetHighlight(float amount)
        {
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                if (amount <= 0f || r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_Color"))
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                block.Clear();
                block.SetColor("_Color", Color.Lerp(r.sharedMaterial.color, highlightColor, amount));
                r.SetPropertyBlock(block);
            }
        }

        // hideFromAimRay: the desktop interactor's centre-screen ray must pass through the part it holds.
        // VR passes false, because the other hand has to be able to touch a held part for a two-handed grab.
        public bool BeginGrab(bool hideFromAimRay = true)
        {
            if (!IsGrabbable) return false;
            if (OffHand == this) OffHand = null; // hand-over from off hand to main hand
            IsHeld = true;
            HeldParts.Add(this);
            SetHighlight(false);
            layersHidden = hideFromAimRay;
            if (hideFromAimRay)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    colliderLayers[i] = colliders[i].gameObject.layer;
                    colliders[i].gameObject.layer = 2; // Ignore Raycast
                }
            }
            return true;
        }

        public void MoveHeld(Vector3 targetPos, Quaternion targetRot, float sharpness)
        {
            if (!IsHeld) return;
            float t = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
        }

        // Off hand keeps its colliders so the main-hand aim ray can land on the base and find its sockets.
        public bool BeginOffHandHold()
        {
            if (!IsGrabbable || OffHand != null) return false;
            OffHand = this;
            SetHighlight(false);
            return true;
        }

        public void MoveOffHand(Vector3 targetPos, Quaternion targetRot, float sharpness)
        {
            if (OffHand != this) return;
            float t = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
        }

        public void EndOffHandHold()
        {
            if (OffHand != this) return;
            OffHand = null;
            ReturnHome();
        }

        // Release: sockets get first claim via Released; otherwise the part returns to its table spot.
        public void EndGrab()
        {
            if (!IsHeld) return;
            ReleaseHold();
            claimedOnRelease = false;
            Released?.Invoke(this);
            if (!claimedOnRelease) ReturnHome();
        }

        // Explicit drop (desktop right-click / cancel): skip sockets, go straight back to the table.
        public void DropToTable()
        {
            if (!IsHeld) return;
            ReleaseHold();
            ReturnHome();
        }

        public void SeatInto(Transform seat, float duration, Action onSeated)
        {
            claimedOnRelease = true;
            StartCoroutine(SeatRoutine(seat, duration, onSeated));
        }

        // Live fire: the magazine comes out of the grip again. Back under its bench parent, grabbable, where it is now.
        public void Unseat()
        {
            if (!IsSeated) return;
            StopAllCoroutines();
            isAnimating = false;
            transform.SetParent(homeParent, true);
            transform.localScale = homeLocalScale;
            IsSeated = false;
            foreach (Collider c in colliders) c.enabled = true;
        }

        // Fire training: the part's "home" (where it goes when let go) becomes a spot on the booth counter, until
        // RestoreHome puts it back to the armory bench.
        private bool homeOverridden;
        private Transform savedHomeParent;
        private Vector3 savedHomePos;
        private Quaternion savedHomeRot;

        public void OverrideHome(Vector3 worldPos, Quaternion worldRot)
        {
            if (!homeOverridden)
            {
                savedHomeParent = homeParent;
                savedHomePos = homeLocalPos;
                savedHomeRot = homeLocalRot;
                homeOverridden = true;
            }
            homeLocalPos = homeParent != null ? homeParent.InverseTransformPoint(worldPos) : worldPos;
            homeLocalRot = homeParent != null ? Quaternion.Inverse(homeParent.rotation) * worldRot : worldRot;
        }

        public void RestoreHome()
        {
            if (!homeOverridden) return;
            homeOverridden = false;
            homeParent = savedHomeParent;
            homeLocalPos = savedHomePos;
            homeLocalRot = savedHomeRot;
        }

        // Straight to its place on the bench, no flight (a fresh magazine appearing there).
        public void SnapHome()
        {
            StopAllCoroutines();
            isAnimating = false;
            transform.SetParent(homeParent, false);
            transform.localPosition = homeLocalPos;
            transform.localRotation = homeLocalRot;
            transform.localScale = homeLocalScale;
        }

        public void ReturnHome()
        {
            Vector3 pos = homeParent != null ? homeParent.TransformPoint(homeLocalPos) : homeLocalPos;
            StartCoroutine(MoveRoutine(pos, HomeWorldRotation, returnDuration, null));
        }

        private void ReleaseHold()
        {
            IsHeld = false;
            HeldParts.Remove(this);
            if (!layersHidden) return;
            layersHidden = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].gameObject.layer = colliderLayers[i];
            }
        }

        private IEnumerator SeatRoutine(Transform seat, float duration, Action onSeated)
        {
            // Animate in the host's space, so the part lands on the seat even while the host
            // is being carried around in the other hand (VR).
            isAnimating = true;
            Transform host = seat.parent;
            Vector3 startPos = host.InverseTransformPoint(transform.position);
            Quaternion startRot = Quaternion.Inverse(host.rotation) * transform.rotation;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                transform.position = host.TransformPoint(Vector3.Lerp(startPos, seat.localPosition, k));
                transform.rotation = host.rotation * Quaternion.Slerp(startRot, seat.localRotation, k);
                yield return null;
            }
            isAnimating = false;

            // Lock as a child of the receiving sub-assembly at the exact seat pose.
            transform.SetParent(seat.parent, true);
            transform.localPosition = seat.localPosition;
            transform.localRotation = seat.localRotation;
            transform.localScale = seat.localScale;
            IsSeated = true;
            foreach (Collider c in colliders) c.enabled = false;
            onSeated?.Invoke();
        }

        private IEnumerator MoveRoutine(Vector3 pos, Quaternion rot, float duration, Action done)
        {
            isAnimating = true;
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                transform.position = Vector3.Lerp(startPos, pos, k);
                transform.rotation = Quaternion.Slerp(startRot, rot, k);
                yield return null;
            }
            transform.position = pos;
            transform.rotation = rot;
            isAnimating = false;
            done?.Invoke();
        }
    }
}
