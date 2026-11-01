using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // In-world guidance for VR (lives under VR_Content, so desktop never runs it). It only reads the assembly state
    // (GunAssemblyManager, SnapSocket, AssemblyPart, FunctionCheck) and shows, one thing at a time:
    //   step part not in hand  -> the part pulses and a marker floats above it
    //   step part in hand      -> a line to the glowing outline, and a direction or rotation hint near the target
    //   function check         -> a marker on the pistol, then a tooltip on the controller holding it, and an arrow on
    //                             the slide when it has to be racked
    // The training panel (VRTrainingMenu) turns the same state into words, so the panel and the world never disagree.
    // It also adds VRPlacementAssist to every grabbable part and makes the socket ghosts easier to see.
    public class VRGuidance : MonoBehaviour
    {
        public enum Phase { Idle, PickUp, Place, PickUpPistol, FunctionCheck, Done }

        public enum Correction
        {
            None, BringToTarget, MoveLeft, MoveRight, MoveUp, MoveDown, MoveAway, MoveCloser,
            RotateClockwise, RotateCounterClockwise, TurnLeft, TurnRight, TiltUp, TiltDown, Aligned,
        }

        public GunAssemblyManager manager;
        public FunctionCheck functionCheck;
        public VRFunctionCheckControls functionCheckControls;
        public Camera viewCamera;

        [Header("Look")]
        [Tooltip("Unlit vertex-colour material (Sprites/Default) for lines and arrows.")]
        public Material lineMaterial;
        public Sprite roundedSprite;
        public Color guideColor = new Color(0.3f, 0.95f, 0.88f);
        public Color successColor = new Color(0.35f, 0.92f, 0.5f);

        [Header("Placement assist (feel only; validation stays in SnapSocket)")]
        public float assistRadius = 0.15f;
        public float rotationAssistAngle = 45f;
        public float seatDwell = 0.25f;

        public Phase CurrentPhase { get; private set; }
        public AssemblyPart HighlightedPart { get; private set; }
        public AssemblyPart WrongPartHeld { get; private set; }
        public AssemblyPart HostHeld { get; private set; }
        public SnapSocket TargetSocket { get; private set; }
        public Correction CurrentCorrection { get; private set; }
        public string TooltipText { get; private set; }
        public bool PistolInLeftHand { get; private set; }
        public string SafetyButton => PistolInLeftHand ? "X" : "A";
        public bool GuideLineVisible => guideLine != null && guideLine.enabled;
        public bool RotationArcVisible => arcLine != null && arcLine.enabled;
        public bool RackHintVisible => rackLine != null && rackLine.enabled;
        public Transform TooltipTransform => tooltip != null ? tooltip.root.transform : null;

        private AssemblyPart highlighted;
        private readonly Dictionary<AssemblyPart, Renderer[]> partRenderers = new Dictionary<AssemblyPart, Renderer[]>();
        private readonly Dictionary<AssemblyPart, Vector3> partLocalCentre = new Dictionary<AssemblyPart, Vector3>();

        private GameObject marker;
        private Label markerLabel, targetLabel, tooltip, rackLabel;
        private LineRenderer guideLine, arcLine, rackLine;
        private Transform guideHead, arcHead, rackHead;
        private readonly List<Burst> bursts = new List<Burst>();
        private Font font;

        private const float ArcSweep = 140f;
        private const int ArcSegments = 24;

        // A small billboard text tag in the world.
        private class Label
        {
            public GameObject root;
            public Image background;
            public Text text;
        }

        private class Burst
        {
            public LineRenderer line;
            public Vector3 centre;
            public float age = 1f, delay, maxRadius;
        }

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (viewCamera == null) viewCamera = Camera.main;

            foreach (VRAssemblyGrab grab in FindObjectsOfType<VRAssemblyGrab>(true))
            {
                VRPlacementAssist assist = grab.GetComponent<VRPlacementAssist>();
                if (assist == null) assist = grab.gameObject.AddComponent<VRPlacementAssist>();
                assist.assistRadius = assistRadius;
                assist.rotationAssistAngle = rotationAssistAngle;
                assist.seatDwell = seatDwell;
            }

            foreach (AssemblyStep step in manager.steps)
            {
                if (step.socket == null) continue;
                step.socket.amberWhenMisaligned = false; // the panel explains the correction instead
                step.socket.faintAlphaBoost = 0.12f;
                step.socket.showInsertionPath = false;  // one line only: ours, from the held part to the outline
                step.socket.Seated += OnSeated;
            }
            if (functionCheck != null) functionCheck.Completed += OnFunctionCheckPassed;

            // Measured now, while every part rests on the bench on its own.
            foreach (AssemblyPart p in FindObjectsOfType<AssemblyPart>()) LocalCentre(p);

            BuildVisuals();
        }

        void OnDestroy()
        {
            if (manager != null)
            {
                foreach (AssemblyStep step in manager.steps)
                {
                    if (step.socket != null) step.socket.Seated -= OnSeated;
                }
            }
            if (functionCheck != null) functionCheck.Completed -= OnFunctionCheckPassed;
        }

        // After XRI and the sockets have moved and evaluated the parts this frame.
        void LateUpdate()
        {
            WrongPartHeld = null;
            HostHeld = null;
            TargetSocket = null;
            CurrentCorrection = Correction.None;
            TooltipText = null;
            AssemblyPart wantHighlight = null;
            bool showMarker = false, showGuide = false, showArc = false, showTarget = false, showRack = false;
            Vector3 markerPos = Vector3.zero;
            string markerText = null;

            AssemblyStep step = manager.CurrentStep;
            if (!manager.HasStarted) CurrentPhase = Phase.Idle;
            else if (manager.IsComplete || step == null) CurrentPhase = Phase.Done;
            else if (step.socket != null)
            {
                SnapSocket socket = step.socket;
                AssemblyPart part = socket.acceptedPart;
                foreach (AssemblyPart p in AssemblyPart.HeldParts)
                {
                    if (p == part) continue;
                    if (p == socket.Host) HostHeld = p;
                    else WrongPartHeld = p;
                }

                if (socket.IsFilled)
                {
                    CurrentPhase = Phase.Place; // seat animation running: nothing to point at
                }
                else if (!part.IsHeld)
                {
                    CurrentPhase = Phase.PickUp;
                    wantHighlight = part;
                    showMarker = !part.IsSeated;
                    markerPos = TopOf(part);
                    markerText = VRTrainingText.PartName(part.partType).ToUpperInvariant();
                }
                else
                {
                    CurrentPhase = Phase.Place;
                    TargetSocket = socket;
                    Transform view = viewCamera.transform;
                    CurrentCorrection = socket.CurrentAlignment == SnapSocket.Alignment.Valid
                        ? Correction.Aligned
                        : ComputeCorrection(part.transform.position, part.transform.rotation, socket.transform.position, socket.transform.rotation,
                                            view, socket.snapDistance, socket.snapAngle, assistRadius);

                    Vector3 from = CentreOf(part);
                    Vector3 to = socket.transform.TransformPoint(LocalCentre(part));
                    showGuide = CurrentCorrection >= Correction.BringToTarget && CurrentCorrection <= Correction.MoveCloser;
                    if (showGuide) DrawGuide(from, to);
                    showArc = CurrentCorrection >= Correction.RotateClockwise && CurrentCorrection <= Correction.TiltDown;
                    if (showArc) DrawArc(from, part, view);
                    showTarget = CurrentCorrection == Correction.BringToTarget;
                    if (showTarget) PlaceLabel(targetLabel, to + Vector3.up * 0.09f, VRTrainingText.TargetLabel);
                }
            }
            else if (step.functionCheck != null && functionCheck.IsRunning && !functionCheck.Passed)
            {
                VRAssemblyGrab frameGrab = functionCheckControls.frameGrab;
                AssemblyPart frame = functionCheck.frame;
                if (!frameGrab.isSelected)
                {
                    CurrentPhase = Phase.PickUpPistol;
                    wantHighlight = frame;
                    showMarker = frame.IsGrabbable;
                    markerPos = TopOf(frame);
                    markerText = VRTrainingText.PistolLabel;
                }
                else
                {
                    CurrentPhase = Phase.FunctionCheck;
                    Transform hand = frameGrab.interactorsSelecting[0].transform;
                    ActionBasedController left = functionCheckControls.leftController;
                    ActionBasedController right = functionCheckControls.rightController;
                    PistolInLeftHand = left != null && hand.IsChildOf(left.transform);
                    Transform controller = PistolInLeftHand ? left.transform : right != null ? right.transform : hand;
                    TooltipText = StageTooltip(functionCheck.Stage);
                    // Beside the hand, on its outer side, so it never covers the slide (where the rack hint goes).
                    Transform view = viewCamera.transform;
                    Vector3 outward = PistolInLeftHand ? -view.right : view.right;
                    Vector3 toEye = (view.position - controller.position).normalized;
                    PlaceLabel(tooltip, controller.position + outward * 0.11f - view.up * 0.02f + toEye * 0.03f, TooltipText);

                    showRack = functionCheck.Stage == 4;
                    if (showRack) DrawRackHint(frame);
                }
            }

            SetHighlighted(wantHighlight);
            if (highlighted != null)
            {
                VRAssemblyGrab grab = highlighted.GetComponent<VRAssemblyGrab>();
                bool hovered = grab != null && grab.isHovered;
                highlighted.SetHighlight(hovered ? 0.6f : 0.2f + 0.3f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4f)));
            }
            HighlightedPart = highlighted;

            marker.SetActive(showMarker);
            markerLabel.root.SetActive(showMarker);
            if (showMarker)
            {
                Vector3 p = markerPos + Vector3.up * (0.06f + 0.012f * Mathf.Sin(Time.time * 3f));
                marker.transform.position = p;
                PlaceLabel(markerLabel, p + Vector3.up * 0.055f, markerText);
            }
            targetLabel.root.SetActive(showTarget);
            guideLine.enabled = showGuide;
            guideHead.gameObject.SetActive(showGuide);
            arcLine.enabled = showArc;
            arcHead.gameObject.SetActive(showArc);
            tooltip.root.SetActive(TooltipText != null);
            rackLine.enabled = showRack;
            rackHead.gameObject.SetActive(showRack);
            if (!showRack) rackLabel.root.SetActive(false);

            UpdateBursts();
        }

        // ---------- Direction from the real pose error ----------

        // What the trainee should do next to bring 'part' onto 'seat', in words relative to their own view.
        // Rotation is fixed first (the position is helped by VRPlacementAssist), then position.
        public static Correction ComputeCorrection(Vector3 partPos, Quaternion partRot, Vector3 seatPos, Quaternion seatRot,
                                                   Transform view, float snapDistance, float snapAngle, float farDistance)
        {
            Vector3 delta = seatPos - partPos;
            if (delta.magnitude > farDistance) return Correction.BringToTarget;

            if (Quaternion.Angle(partRot, seatRot) > snapAngle)
            {
                // The world rotation that would take the part onto the seat; its axis seen from the trainee's eyes.
                (seatRot * Quaternion.Inverse(partRot)).ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) axis = -axis;
                float f = Vector3.Dot(axis, view.forward), u = Vector3.Dot(axis, view.up), r = Vector3.Dot(axis, view.right);
                // Unity is left-handed: a positive turn about an axis pointing away from the viewer looks counter-clockwise.
                if (Mathf.Abs(f) >= Mathf.Abs(u) && Mathf.Abs(f) >= Mathf.Abs(r)) return f > 0f ? Correction.RotateCounterClockwise : Correction.RotateClockwise;
                if (Mathf.Abs(u) >= Mathf.Abs(r)) return u > 0f ? Correction.TurnRight : Correction.TurnLeft; // about up: the front swings right
                return r > 0f ? Correction.TiltDown : Correction.TiltUp;                                        // about right: the front dips
            }

            if (delta.magnitude > snapDistance)
            {
                float x = Vector3.Dot(delta, view.right), y = Vector3.Dot(delta, view.up), z = Vector3.Dot(delta, view.forward);
                if (Mathf.Abs(x) >= Mathf.Abs(y) && Mathf.Abs(x) >= Mathf.Abs(z)) return x > 0f ? Correction.MoveRight : Correction.MoveLeft;
                if (Mathf.Abs(y) >= Mathf.Abs(z)) return y > 0f ? Correction.MoveUp : Correction.MoveDown;
                return z > 0f ? Correction.MoveAway : Correction.MoveCloser;
            }
            return Correction.Aligned;
        }

        public static string CorrectionText(Correction c)
        {
            switch (c)
            {
                case Correction.BringToTarget: return VRTrainingText.BringToTarget;
                case Correction.MoveLeft: return VRTrainingText.MoveLeft;
                case Correction.MoveRight: return VRTrainingText.MoveRight;
                case Correction.MoveUp: return VRTrainingText.MoveUp;
                case Correction.MoveDown: return VRTrainingText.MoveDown;
                case Correction.MoveAway: return VRTrainingText.MoveAway;
                case Correction.MoveCloser: return VRTrainingText.MoveCloser;
                case Correction.RotateClockwise: return VRTrainingText.RotateClockwise;
                case Correction.RotateCounterClockwise: return VRTrainingText.RotateCounterClockwise;
                case Correction.TurnLeft: return VRTrainingText.TurnLeft;
                case Correction.TurnRight: return VRTrainingText.TurnRight;
                case Correction.TiltUp: return VRTrainingText.TiltUp;
                case Correction.TiltDown: return VRTrainingText.TiltDown;
                case Correction.Aligned: return VRTrainingText.Aligned;
                default: return null;
            }
        }

        private string StageTooltip(int stage)
        {
            switch (stage)
            {
                case 0: return VRTrainingText.PressButton(SafetyButton);
                case 1: return functionCheck.SafeTriggerPulled ? VRTrainingText.LetGo : VRTrainingText.PullTrigger;
                case 2: return functionCheck.TriggerPull >= 0.2f ? VRTrainingText.LetGo : VRTrainingText.PressButton(SafetyButton);
                case 4: return VRTrainingText.HoldTrigger;
                case 5: return VRTrainingText.LetGoSlowly;
                default: return VRTrainingText.PullTrigger;
            }
        }

        // ---------- Success moments ----------

        private void OnSeated(SnapSocket socket)
        {
            Vector3 c = CentreOf(socket.acceptedPart);
            bool last = true;
            foreach (AssemblyStep s in manager.steps)
            {
                if (s.socket != null && !s.socket.IsFilled) last = false;
            }
            StartBurst(c, 0.14f, 0f);
            if (last)
            {
                // Assembly complete: a bigger double ring around the whole pistol.
                Vector3 gun = CentreOf(functionCheck.frame);
                StartBurst(gun, 0.26f, 0.12f);
                StartBurst(gun, 0.34f, 0.28f);
            }
        }

        private void OnFunctionCheckPassed()
        {
            Vector3 gun = CentreOf(functionCheck.frame);
            StartBurst(gun, 0.26f, 0f);
            StartBurst(gun, 0.34f, 0.16f);
        }

        private void StartBurst(Vector3 centre, float radius, float delay)
        {
            Burst b = bursts.Find(x => x.age >= 1f);
            if (b == null)
            {
                b = new Burst { line = MakeLine("Guide_Burst", 0.006f, 0.006f) };
                b.line.loop = true;
                b.line.positionCount = 32;
                bursts.Add(b);
            }
            b.centre = centre;
            b.maxRadius = radius;
            b.delay = delay;
            b.age = 0f;
        }

        private void UpdateBursts()
        {
            Vector3 toEye = viewCamera.transform.position;
            foreach (Burst b in bursts)
            {
                if (b.delay > 0f) { b.delay -= Time.deltaTime; b.line.enabled = false; continue; }
                b.line.enabled = b.age < 1f;
                if (b.age >= 1f) continue;
                b.age += Time.deltaTime / 0.7f;
                float k = Mathf.Clamp01(b.age);
                float radius = Mathf.Lerp(0.02f, b.maxRadius, 1f - (1f - k) * (1f - k));
                Color c = new Color(successColor.r, successColor.g, successColor.b, 1f - k);
                b.line.startColor = b.line.endColor = c;
                Quaternion face = Quaternion.LookRotation(b.centre - toEye);
                for (int i = 0; i < 32; i++)
                {
                    float a = i * Mathf.PI * 2f / 32f;
                    b.line.SetPosition(i, b.centre + face * new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
                }
            }
        }

        // ---------- Drawing ----------

        private void DrawGuide(Vector3 from, Vector3 to)
        {
            float pulse = 0.55f + 0.35f * Mathf.Sin(Time.time * 5f);
            guideLine.startColor = new Color(guideColor.r, guideColor.g, guideColor.b, 0.25f);
            guideLine.endColor = new Color(guideColor.r, guideColor.g, guideColor.b, pulse);
            Vector3 dir = to - from;
            float len = dir.magnitude;
            Vector3 end = len > 0.03f ? to - dir / len * 0.02f : to;
            guideLine.SetPosition(0, from);
            guideLine.SetPosition(1, end);
            if (len > 0.001f) guideHead.SetPositionAndRotation(end - dir / len * 0.02f, Quaternion.LookRotation(dir));
            SetColor(guideHead, guideLine.endColor);
        }

        // Curved arrow around the part showing which way to turn it (the same axis the correction was chosen from).
        private void DrawArc(Vector3 centre, AssemblyPart part, Transform view)
        {
            Vector3 axis;
            switch (CurrentCorrection)
            {
                case Correction.RotateCounterClockwise: axis = view.forward; break;
                case Correction.RotateClockwise: axis = -view.forward; break;
                case Correction.TurnRight: axis = view.up; break;
                case Correction.TurnLeft: axis = -view.up; break;
                case Correction.TiltDown: axis = view.right; break;
                default: axis = -view.right; break;
            }
            Vector3 start = Vector3.ProjectOnPlane(view.up, axis);
            if (start.sqrMagnitude < 0.01f) start = Vector3.ProjectOnPlane(-view.forward, axis);
            start.Normalize();
            float radius = Mathf.Clamp(BoundsOf(part).extents.magnitude * 0.75f, 0.06f, 0.14f);
            float alpha = 0.6f + 0.3f * Mathf.Sin(Time.time * 5f);
            Color c = new Color(guideColor.r, guideColor.g, guideColor.b, alpha);
            arcLine.startColor = new Color(c.r, c.g, c.b, 0.15f);
            arcLine.endColor = c;
            for (int i = 0; i <= ArcSegments; i++)
            {
                float a = -ArcSweep * 0.5f + ArcSweep * i / ArcSegments;
                arcLine.SetPosition(i, centre + Quaternion.AngleAxis(a, axis) * start * radius);
            }
            Vector3 tip = centre + Quaternion.AngleAxis(ArcSweep * 0.5f, axis) * start * radius;
            Vector3 tangent = Vector3.Cross(axis, tip - centre).normalized; // direction of a positive turn
            arcHead.SetPositionAndRotation(tip, Quaternion.LookRotation(tangent, axis));
            SetColor(arcHead, c);
        }

        private void DrawRackHint(AssemblyPart frame)
        {
            Transform grip = frame.transform.Find("VR_Slide_Rack_Grip");
            if (grip == null) { rackLine.enabled = false; return; }
            Vector3 rear = functionCheck.SlideRearDirection.normalized;
            float travel = Mathf.Repeat(Time.time * 0.8f, 1f);
            Vector3 a = grip.position + rear * (0.01f + 0.02f * travel);
            Vector3 b = a + rear * 0.06f;
            Color c = new Color(guideColor.r, guideColor.g, guideColor.b, 1f - 0.6f * travel);
            rackLine.startColor = new Color(c.r, c.g, c.b, 0.2f);
            rackLine.endColor = c;
            rackLine.SetPosition(0, a);
            rackLine.SetPosition(1, b);
            rackHead.SetPositionAndRotation(b, Quaternion.LookRotation(rear));
            SetColor(rackHead, c);

            XRBaseInteractable rackGrip = grip.GetComponent<XRBaseInteractable>();
            bool grabbed = rackGrip != null && rackGrip.isSelected;
            rackLabel.root.SetActive(!grabbed);
            if (!grabbed) PlaceLabel(rackLabel, grip.position + viewCamera.transform.up * 0.08f, VRTrainingText.RackHere);
        }

        private void SetHighlighted(AssemblyPart part)
        {
            if (highlighted == part) return;
            if (highlighted != null) highlighted.SetHighlight(false);
            highlighted = part;
        }

        private void PlaceLabel(Label label, Vector3 position, string text)
        {
            label.root.SetActive(true);
            label.root.transform.position = position;
            label.root.transform.rotation = Quaternion.LookRotation(position - viewCamera.transform.position, Vector3.up);
            if (label.text.text == text) return;
            label.text.text = text;
            label.background.rectTransform.sizeDelta = new Vector2(label.text.preferredWidth + 44f, 58f);
        }

        // ---------- Part geometry (gun meshes only: socket ghosts are children of their host and are skipped) ----------

        private Renderer[] RenderersOf(AssemblyPart part)
        {
            // Rebuilt when parts are seated into this one (they become its children), so the pistol marker covers the whole gun.
            int childCount = part.transform.childCount;
            if (partRenderers.TryGetValue(part, out Renderer[] rs) && cachedChildCounts[part] == childCount) return rs;
            var list = new List<Renderer>();
            foreach (Renderer r in part.GetComponentsInChildren<Renderer>())
            {
                if (r.GetComponentInParent<SnapSocket>() == null) list.Add(r);
            }
            rs = list.ToArray();
            partRenderers[part] = rs;
            cachedChildCounts[part] = childCount;
            return rs;
        }
        private readonly Dictionary<AssemblyPart, int> cachedChildCounts = new Dictionary<AssemblyPart, int>();

        private Bounds BoundsOf(AssemblyPart part) => BoundsOf(RenderersOf(part));

        private static Bounds BoundsOf(Renderer[] rs)
        {
            if (rs.Length == 0) return default;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private Vector3 CentreOf(AssemblyPart part) => BoundsOf(part).center;

        private Vector3 TopOf(AssemblyPart part)
        {
            Bounds b = BoundsOf(part);
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }

        // Centre of the part's own meshes in its local space (measured on the bench), so the outline's centre is
        // socket.TransformPoint(LocalCentre): the socket is the seated pose of the part root.
        private Vector3 LocalCentre(AssemblyPart part)
        {
            if (!partLocalCentre.TryGetValue(part, out Vector3 c))
            {
                c = part.transform.InverseTransformPoint(CentreOf(part));
                partLocalCentre[part] = c;
            }
            return c;
        }

        // ---------- Visual objects (created once, reused) ----------

        private void BuildVisuals()
        {
            Mesh arrow = ArrowMesh();
            marker = MakeArrow("Guide_Marker", arrow, 0.035f, 0.045f);
            marker.transform.rotation = Quaternion.LookRotation(Vector3.down);
            markerLabel = MakeLabel("Guide_Marker_Label", guideColor, new Color(0.04f, 0.06f, 0.08f, 0.9f));
            targetLabel = MakeLabel("Guide_Target_Label", guideColor, new Color(0.04f, 0.06f, 0.08f, 0.9f));
            tooltip = MakeLabel("Guide_Controller_Tooltip", new Color(0.04f, 0.06f, 0.08f), guideColor);
            rackLabel = MakeLabel("Guide_Rack_Label", guideColor, new Color(0.04f, 0.06f, 0.08f, 0.9f));

            guideLine = MakeLine("Guide_Line", 0.003f, 0.007f);
            guideHead = MakeArrow("Guide_Line_Head", arrow, 0.022f, 0.03f).transform;
            arcLine = MakeLine("Guide_Rotation_Arc", 0.003f, 0.007f);
            arcLine.positionCount = ArcSegments + 1;
            arcHead = MakeArrow("Guide_Rotation_Head", arrow, 0.024f, 0.032f).transform;
            rackLine = MakeLine("Guide_Rack_Arrow", 0.004f, 0.008f);
            rackHead = MakeArrow("Guide_Rack_Head", arrow, 0.024f, 0.032f).transform;

            foreach (Label l in new[] { markerLabel, targetLabel, tooltip, rackLabel }) l.root.SetActive(false);
            marker.SetActive(false);
        }

        private LineRenderer MakeLine(string name, float startWidth, float endWidth)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = lineMaterial;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = startWidth;
            lr.endWidth = endWidth;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        private GameObject MakeArrow(string name, Mesh mesh, float width, float length)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(width, width, length);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(lineMaterial) { color = guideColor }; // own copy, tinted per arrow
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        private static void SetColor(Transform arrow, Color c)
        {
            arrow.GetComponent<MeshRenderer>().sharedMaterial.color = c;
        }

        private Label MakeLabel(string name, Color textColor, Color backColor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.0007f; // 1 unit = 0.7 mm: 30-unit text reads well at arm's length
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            ((RectTransform)go.transform).sizeDelta = new Vector2(600f, 80f);

            var label = new Label { root = go };
            GameObject bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(go.transform, false);
            label.background = bg.AddComponent<Image>();
            label.background.sprite = roundedSprite;
            label.background.type = Image.Type.Sliced;
            label.background.pixelsPerUnitMultiplier = 0.35f; // fully rounded ends
            label.background.color = backColor;
            label.background.raycastTarget = false;

            GameObject txt = new GameObject("Text", typeof(RectTransform));
            txt.transform.SetParent(go.transform, false);
            ((RectTransform)txt.transform).sizeDelta = new Vector2(600f, 80f);
            label.text = txt.AddComponent<Text>();
            label.text.font = font;
            label.text.fontSize = 30;
            label.text.fontStyle = FontStyle.Bold;
            label.text.alignment = TextAnchor.MiddleCenter;
            label.text.color = textColor;
            label.text.raycastTarget = false;
            label.text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return label;
        }

        // Four-sided pyramid pointing along +Z (base 1 x 1 at z = 0, tip at z = 1).
        private static Mesh ArrowMesh()
        {
            Vector3 tip = new Vector3(0f, 0f, 1f);
            Vector3 a = new Vector3(-0.5f, -0.5f, 0f), b = new Vector3(0.5f, -0.5f, 0f), c = new Vector3(0.5f, 0.5f, 0f), d = new Vector3(-0.5f, 0.5f, 0f);
            var mesh = new Mesh { name = "Guide_Arrow" };
            mesh.vertices = new[] { tip, a, b, tip, b, c, tip, c, d, tip, d, a, a, b, c, d };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 13, 12, 15, 14 };
            var colors = new Color[16];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
