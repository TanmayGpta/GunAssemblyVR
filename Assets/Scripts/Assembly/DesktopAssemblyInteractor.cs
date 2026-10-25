using UnityEngine;

namespace VRArmory
{
    // No-headset test interactor that simulates two hands. Centre-screen reticle is the main hand: hold LMB to
    // grab an AssemblyPart, release LMB to let sockets claim it, RMB to drop it back to the table. Q/E rotate the
    // held part, R resets its rotation. F puts the aimed part (frame / slide) in the off hand, held steady in front
    // of the chest; hold MMB + move the mouse to turn it, C re-centres it, F again returns it to the bench.
    // While a base is in the off hand the main-hand part turns with it, so parts arrive aligned to the base.
    public class DesktopAssemblyInteractor : MonoBehaviour
    {
        public Camera playerCamera;
        public float reach = 2.5f;
        public float surfaceOffset = 0.025f;
        public float freeHoldDistance = 0.6f;
        public float rotateStep = 15f;
        public float followSharpness = 18f;
        [Tooltip("Aim this close to the active socket's insertion path and the held part rides the path.")]
        public float guideRadius = 0.05f;

        [Header("Off Hand")]
        public float offHandDistance = 0.38f;
        public float offHandDrop = 0.05f;
        public float offHandRotateSpeed = 4f;

        private bool onRail;
        private AssemblyPart hovered;
        private AssemblyPart held;
        private Quaternion heldRotation;
        private Vector3 offHandOffset;
        private Quaternion offHandRotation;
        private ArmoryPlayerController controller;
        private CharacterController body;
        private Vector3 offHandSmoothOffset;
        private Quaternion offHandSmoothRotation;
        private AssemblyPart ignoredOffHand, ignoredHeld;

        void Awake()
        {
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            controller = GetComponent<ArmoryPlayerController>();
            body = GetComponent<CharacterController>();
        }

        void OnDisable()
        {
            SetHovered(null);
            if (held != null) held.DropToTable();
            held = null;
            if (AssemblyPart.OffHand != null) AssemblyPart.OffHand.EndOffHandHold();
            SetLookLocked(false);
        }

        void Update()
        {
            if (playerCamera == null) return;

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                SetHovered(null);
                SetLookLocked(false);
                if (held != null) { held.DropToTable(); held = null; }
                return;
            }

            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            bool hasAim = AimRaycast(ray, reach, transform, out RaycastHit aim);
            AssemblyPart aimed = hasAim ? aim.collider.GetComponentInParent<AssemblyPart>() : null;
            if (aimed != null && !aimed.IsGrabbable) aimed = null;

            UpdateOffHand(aimed);
            SyncBodyCollisions();

            if (held == null)
            {
                SetHovered(aimed);
                if (hovered != null && Input.GetMouseButtonDown(0) && hovered.BeginGrab())
                {
                    held = hovered;
                    hovered = null;
                    heldRotation = held.HomeWorldRotation;
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Q)) heldRotation = Quaternion.AngleAxis(-rotateStep, Vector3.up) * heldRotation;
            if (Input.GetKeyDown(KeyCode.E)) heldRotation = Quaternion.AngleAxis(rotateStep, Vector3.up) * heldRotation;
            if (Input.GetKeyDown(KeyCode.R)) heldRotation = held.HomeWorldRotation;

            Vector3 target = hasAim ? aim.point + aim.normal * surfaceOffset : ray.GetPoint(freeHoldDistance);
            Vector3 aimPoint = hasAim ? aim.point : target;
            onRail = false;
            foreach (SnapSocket s in SnapSocket.All)
            {
                if (s.TryGuide(held, aimPoint, guideRadius, out Vector3 guided))
                {
                    target = guided;
                    onRail = true;
                    break;
                }
            }
            held.MoveHeld(target, BaseTurn() * heldRotation, followSharpness);

            if (Input.GetMouseButtonUp(0))
            {
                AssemblyPart p = held;
                held = null;
                p.EndGrab();
            }
            else if (Input.GetMouseButtonDown(1))
            {
                held.DropToTable();
                held = null;
            }
        }

        // How far the off-hand base has been turned from its bench pose. Applied to the main-hand part so a
        // part picked up from the bench stays aligned with the base however the base is held.
        private Quaternion BaseTurn()
        {
            AssemblyPart b = AssemblyPart.OffHand;
            return b != null ? b.transform.rotation * Quaternion.Inverse(b.HomeWorldRotation) : Quaternion.identity;
        }

        private void UpdateOffHand(AssemblyPart aimed)
        {
            AssemblyPart b = AssemblyPart.OffHand;

            if (Input.GetKeyDown(KeyCode.F))
            {
                if (b != null)
                {
                    b.EndOffHandHold();
                    b = null;
                }
                else if (aimed != null && aimed.BeginOffHandHold())
                {
                    b = aimed;
                    offHandRotation = b.transform.rotation;
                    Recentre();
                    offHandSmoothOffset = b.transform.position - transform.position;
                    offHandSmoothRotation = offHandRotation;
                }
            }

            bool rotating = b != null && Input.GetMouseButton(2);
            SetLookLocked(rotating);
            if (b == null) return;
            if (Input.GetKeyDown(KeyCode.C)) Recentre();
            if (rotating)
            {
                float mx = Input.GetAxis("Mouse X") * offHandRotateSpeed;
                float my = Input.GetAxis("Mouse Y") * offHandRotateSpeed;
                offHandRotation = Quaternion.AngleAxis(-mx, Vector3.up)
                                * Quaternion.AngleAxis(my, playerCamera.transform.right) * offHandRotation;
            }

        }

        // After the player controller has moved this frame. Anchored to the body's position only, like a hand held
        // still: looking around moves the reticle over the base instead of dragging the base along with the view.
        // Smoothing is on the body-relative pose, so walking never makes the base lag behind into the body.
        void LateUpdate()
        {
            AssemblyPart b = AssemblyPart.OffHand;
            if (b == null) return;
            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            offHandSmoothOffset = Vector3.Lerp(offHandSmoothOffset, offHandOffset, t);
            offHandSmoothRotation = Quaternion.Slerp(offHandSmoothRotation, offHandRotation, t);
            b.MoveOffHand(transform.position + offHandSmoothOffset, offHandSmoothRotation, float.PositiveInfinity);
        }

        // Parts in either hand must not shove the CharacterController (kinematic colliders right in front of it
        // made WASD push the player around).
        private void SyncBodyCollisions()
        {
            if (body == null) return;
            SetIgnore(ref ignoredOffHand, AssemblyPart.OffHand);
            SetIgnore(ref ignoredHeld, held);
        }

        private void SetIgnore(ref AssemblyPart current, AssemblyPart wanted)
        {
            if (current == wanted) return;
            if (current != null) foreach (Collider c in current.Colliders) if (c != null) Physics.IgnoreCollision(body, c, false);
            current = wanted;
            if (current != null) foreach (Collider c in current.Colliders) if (c != null) Physics.IgnoreCollision(body, c, true);
        }

        private void Recentre()
        {
            Transform cam = playerCamera.transform;
            offHandOffset = cam.position + cam.forward * offHandDistance - Vector3.up * offHandDrop - transform.position;
        }

        private void SetLookLocked(bool locked)
        {
            if (controller != null) controller.lookLocked = locked;
        }

        // Nearest hit that is not part of the player rig (the camera sits inside the CharacterController capsule,
        // which a plain Physics.Raycast reports as the first hit).
        public static bool AimRaycast(Ray ray, float maxDistance, Transform ignoreRoot, out RaycastHit result)
        {
            result = default;
            float best = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(ray, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    result = h;
                }
            }
            return best < float.MaxValue;
        }

        private void SetHovered(AssemblyPart part)
        {
            if (hovered == part) return;
            if (hovered != null) hovered.SetHighlight(false);
            hovered = part;
            if (hovered != null) hovered.SetHighlight(true);
        }

        void OnGUI()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            Color prev = GUI.color;
            GUI.color = held != null ? new Color(0.1f, 1f, 0.4f) : hovered != null ? new Color(0.3f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0.8f);
            GUI.DrawTexture(new Rect(cx - 8f, cy - 1f, 16f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy - 8f, 2f, 16f), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            string hint = held != null
                ? $"HOLDING {held.name}{(onRail ? "  [ON INSERTION PATH]" : "")}   |   RELEASE LMB: seat / return   RMB: drop   Q/E: rotate {rotateStep:0} deg   R: reset rotation"
                : hovered != null ? $"{hovered.name}   |   HOLD LMB: pick up   F: hold in off hand" : "AIM AT A PART";
            GUI.Label(new Rect(cx - 400f, Screen.height - 40f, 800f, 24f), hint, style);

            AssemblyPart b = AssemblyPart.OffHand;
            string offHint = b != null
                ? $"OFF HAND: {b.name}   |   HOLD MMB + MOUSE: turn   C: re-centre   F: return to bench"
                : "OFF HAND: EMPTY   |   AIM AT FRAME / SLIDE + F: hold it in front of you";
            GUI.Label(new Rect(cx - 400f, Screen.height - 64f, 800f, 24f), offHint, style);
            GUI.color = prev;
        }
    }
}
