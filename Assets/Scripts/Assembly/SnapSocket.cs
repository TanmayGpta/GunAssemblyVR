using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRArmory
{
    // Seat point for one AssemblyPart. The socket transform IS the seated pose of the part's root
    // (all Beretta modules share the factory origin, so for the barrel this is identity under 02_Upper_Slide).
    // In Guided mode it renders a holographic ghost of the accepted part plus the insertion path,
    // tinted by live alignment. Equivalent to an XRSocketInteractor hover/select filter for the Quest build.
    public class SnapSocket : MonoBehaviour
    {
        public enum Alignment { Hidden, Faint, Valid, Warning }

        [Header("Fit")]
        public AssemblyPart acceptedPart;
        public float snapDistance = 0.06f;
        public float snapAngle = 20f;
        public float hintRange = 0.35f;
        public float snapDuration = 0.25f;

        [Header("Ghost Preview")]
        public Material ghostTemplate;
        [Tooltip("Socket-local direction the part enters from; the path line is drawn from here to the seat.")]
        public Vector3 insertionDirection = new Vector3(0f, 0f, -1f);
        public float insertionPathLength = 0.12f;

        public Alignment CurrentAlignment { get; private set; }
        public string WarningReason { get; private set; }
        public float LastDistance { get; private set; }
        public float LastAngle { get; private set; }
        public bool IsUnlocked { get; private set; }
        public bool IsFilled { get; private set; }

        public event Action<SnapSocket> Seated;

        public static readonly List<SnapSocket> All = new List<SnapSocket>();

        private static readonly Color FaintColor = new Color(0.0f, 1.0f, 0.75f, 0.12f);
        private static readonly Color ValidColor = new Color(0.1f, 1.0f, 0.3f, 0.6f);
        private static readonly Color AmberColor = new Color(1.0f, 0.6f, 0.0f, 0.6f);
        private static readonly Color RedColor = new Color(1.0f, 0.1f, 0.05f, 0.6f);

        private GameObject ghostRoot;
        private Material ghostMat;
        private LineRenderer pathLine;
        private float warningFlashTimer;
        private float confirmTimer;
        private const float ConfirmDuration = 0.8f;

        private bool HintsEnabled => GunAssemblyManager.Instance == null || GunAssemblyManager.Instance.HintsEnabled;

        void Awake()
        {
            BuildGhost();
        }

        void OnEnable()
        {
            All.Add(this);
            AssemblyPart.Released += OnPartReleased;
        }

        void OnDisable()
        {
            All.Remove(this);
            AssemblyPart.Released -= OnPartReleased;
        }

        public void SetUnlocked(bool unlocked)
        {
            IsUnlocked = unlocked;
        }

        // Guide rail for pointer interactors: when the aim point is near this socket's insertion path, the held
        // part slides along that path. Seats buried inside the host (magazine well) or entered from the side
        // can't be reached by resting the part on a surface, so without this they never come within snapDistance.
        public bool TryGuide(AssemblyPart part, Vector3 aimPoint, float captureRadius, out Vector3 guided)
        {
            guided = aimPoint;
            if (IsFilled || !IsUnlocked || part != acceptedPart) return false;
            Vector3 dir = transform.TransformDirection(insertionDirection).normalized;
            float t = Mathf.Clamp(Vector3.Dot(aimPoint - transform.position, dir), 0f, insertionPathLength);
            Vector3 onPath = transform.position + dir * t;
            if (Vector3.Distance(aimPoint, onPath) > captureRadius) return false;
            guided = onPath;
            return true;
        }

        void Update()
        {
            if (IsFilled)
            {
                // Fit confirmation: bright green seam pulse that fades out.
                if (confirmTimer > 0f)
                {
                    confirmTimer -= Time.deltaTime;
                    float k = Mathf.Clamp01(confirmTimer / ConfirmDuration);
                    ShowGhost(new Color(0.3f, 1.0f, 0.4f, k * (0.6f + 0.4f * Mathf.Sin(Time.time * 30f))), false);
                    if (confirmTimer <= 0f) HideGhost();
                }
                return;
            }

            AssemblyPart held = AssemblyPart.HeldParts.Count > 0 ? AssemblyPart.HeldParts[0] : null;
            Evaluate(held);

            if (!HintsEnabled)
            {
                HideGhost();
                return;
            }

            if (warningFlashTimer > 0f)
            {
                warningFlashTimer -= Time.deltaTime;
                bool on = Mathf.Repeat(warningFlashTimer, 0.2f) > 0.1f;
                ShowGhost(on ? RedColor : new Color(RedColor.r, RedColor.g, RedColor.b, 0.1f), false);
                return;
            }

            switch (CurrentAlignment)
            {
                case Alignment.Hidden:
                    HideGhost();
                    break;
                case Alignment.Faint:
                    // Brighten gradually as the part approaches.
                    float near = 1f - Mathf.Clamp01((LastDistance - snapDistance) / hintRange);
                    ShowGhost(new Color(FaintColor.r, FaintColor.g, FaintColor.b, FaintColor.a + near * 0.15f), true);
                    break;
                case Alignment.Valid:
                    float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 10f);
                    ShowGhost(new Color(ValidColor.r, ValidColor.g, ValidColor.b, ValidColor.a * pulse + 0.15f), true);
                    break;
                case Alignment.Warning:
                    ShowGhost(IsUnlocked && held == acceptedPart ? AmberColor : RedColor, true);
                    break;
            }
        }

        private void Evaluate(AssemblyPart held)
        {
            WarningReason = null;
            // Nothing held, or the held part is this socket's own host (socket rides along with it).
            if (held == null || transform.IsChildOf(held.transform))
            {
                CurrentAlignment = Alignment.Hidden;
                return;
            }

            LastDistance = Vector3.Distance(held.transform.position, transform.position);
            LastAngle = Quaternion.Angle(held.transform.rotation, transform.rotation);

            if (held != acceptedPart)
            {
                // Only the active socket calls out a wrong part, and only when it's brought right up to it.
                CurrentAlignment = IsUnlocked && LastDistance <= snapDistance * 2f ? Alignment.Warning : Alignment.Hidden;
                if (CurrentAlignment == Alignment.Warning) WarningReason = "WRONG PART FOR THIS SOCKET";
                return;
            }

            if (!IsUnlocked)
            {
                CurrentAlignment = LastDistance <= hintRange ? Alignment.Warning : Alignment.Hidden;
                if (CurrentAlignment == Alignment.Warning) WarningReason = "OUT OF SEQUENCE - COMPLETE THE CURRENT STEP FIRST";
                return;
            }

            if (LastDistance <= snapDistance)
            {
                CurrentAlignment = LastAngle <= snapAngle ? Alignment.Valid : Alignment.Warning;
                if (CurrentAlignment == Alignment.Warning) WarningReason = "MISALIGNED - ROTATE TO MATCH THE GHOST";
            }
            else
            {
                CurrentAlignment = Alignment.Faint;
            }
        }

        private void OnPartReleased(AssemblyPart part)
        {
            if (IsFilled || transform.IsChildOf(part.transform)) return;

            float dist = Vector3.Distance(part.transform.position, transform.position);
            float angle = Quaternion.Angle(part.transform.rotation, transform.rotation);

            if (part == acceptedPart && IsUnlocked && dist <= snapDistance && angle <= snapAngle)
            {
                IsFilled = true;
                HideGhost();
                part.SeatInto(transform, snapDuration, OnSnapComplete);
                return;
            }

            // Released near the socket but wrong part / wrong angle / out of sequence.
            if (part == acceptedPart ? dist <= hintRange : IsUnlocked && dist <= snapDistance * 2f)
            {
                warningFlashTimer = 0.6f;
            }
        }

        private void OnSnapComplete()
        {
            confirmTimer = ConfirmDuration;
            Seated?.Invoke(this);
        }

        private void BuildGhost()
        {
            if (acceptedPart == null) return;

            ghostMat = ghostTemplate != null ? new Material(ghostTemplate) : new Material(Shader.Find("VRArmory/Hologram"));
            ghostRoot = new GameObject("Ghost_" + acceptedPart.name);
            ghostRoot.transform.SetParent(transform, false);

            // Clone every mesh of the accepted part at its pose relative to the part root.
            Transform partRoot = acceptedPart.transform;
            Vector3 rootScale = partRoot.lossyScale;
            foreach (MeshFilter mf in acceptedPart.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                GameObject g = new GameObject("Ghost_" + mf.name);
                g.transform.SetParent(ghostRoot.transform, false);
                g.transform.localPosition = partRoot.InverseTransformPoint(mf.transform.position);
                g.transform.localRotation = Quaternion.Inverse(partRoot.rotation) * mf.transform.rotation;
                Vector3 s = mf.transform.lossyScale;
                g.transform.localScale = new Vector3(s.x / rootScale.x, s.y / rootScale.y, s.z / rootScale.z);
                g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                MeshRenderer mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ghostMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            pathLine = ghostRoot.AddComponent<LineRenderer>();
            pathLine.sharedMaterial = ghostMat;
            pathLine.useWorldSpace = true;
            pathLine.positionCount = 2;
            pathLine.widthMultiplier = 0.004f;
            pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ghostRoot.SetActive(false);
        }

        private void ShowGhost(Color color, bool showPath)
        {
            if (ghostRoot == null) return;
            ghostRoot.SetActive(true);
            ghostMat.SetColor("_Color", color);
            pathLine.enabled = showPath;
            if (showPath)
            {
                Vector3 dir = transform.TransformDirection(insertionDirection).normalized;
                pathLine.SetPosition(0, transform.position + dir * insertionPathLength);
                pathLine.SetPosition(1, transform.position);
            }
        }

        private void HideGhost()
        {
            if (ghostRoot != null) ghostRoot.SetActive(false);
        }
    }
}
