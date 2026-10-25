using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRArmory
{
    public enum GunPart { LowerFrame, UpperSlide, Barrel, RecoilSpring, Magazine }

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
        private Renderer[] renderers;
        private Collider[] colliders;
        private int[] colliderLayers;
        private MaterialPropertyBlock block;
        private bool isAnimating;
        private bool claimedOnRelease;

        void Awake()
        {
            homeParent = transform.parent;
            homeLocalPos = transform.localPosition;
            homeLocalRot = transform.localRotation;
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
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                if (!on || r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_Color"))
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                block.Clear();
                block.SetColor("_Color", Color.Lerp(r.sharedMaterial.color, highlightColor, 0.45f));
                r.SetPropertyBlock(block);
            }
        }

        public bool BeginGrab()
        {
            if (!IsGrabbable) return false;
            if (OffHand == this) OffHand = null; // hand-over from off hand to main hand
            IsHeld = true;
            HeldParts.Add(this);
            SetHighlight(false);
            // Held part must not block the interactor's aim ray.
            for (int i = 0; i < colliders.Length; i++)
            {
                colliderLayers[i] = colliders[i].gameObject.layer;
                colliders[i].gameObject.layer = 2; // Ignore Raycast
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

        public void ReturnHome()
        {
            Vector3 pos = homeParent != null ? homeParent.TransformPoint(homeLocalPos) : homeLocalPos;
            StartCoroutine(MoveRoutine(pos, HomeWorldRotation, returnDuration, null));
        }

        private void ReleaseHold()
        {
            IsHeld = false;
            HeldParts.Remove(this);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].gameObject.layer = colliderLayers[i];
            }
        }

        private IEnumerator SeatRoutine(Transform seat, float duration, Action onSeated)
        {
            yield return MoveRoutine(seat.position, seat.rotation, duration, null);

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
