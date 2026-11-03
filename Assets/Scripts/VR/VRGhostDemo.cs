using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRArmory
{
    // Function-check demonstrations in VR: a see-through, glowing copy ("ghost") of the slide or the trigger shows the
    // movement the trainee is about to make - which part, which way, how far - and then fades away.
    // It is a demonstration only:
    //  - the ghosts are plain mesh copies (GhostVisual: no collider, no AssemblyPart, no XR component, Ignore Raycast
    //    layer), so they can never be grabbed, hit by a ray or seated, and the real parts are never moved or changed;
    //  - their poses come from FunctionCheck (SlideLocalPosition / TriggerLocalPose): the same rest pose, direction,
    //    pivot and travel as the real animation, so the ghost stops exactly where the real part will;
    //  - they are children of the real part's parent (the frame), so they follow the pistol wherever it is held.
    // Each ghost is built once, the first time it is needed, and then only shown and hidden.
    // VRTrainingCoach decides when a demonstration plays; this class only knows how to play one.
    public class VRGhostDemo : MonoBehaviour
    {
        public enum Demo { None, SlideRack, TriggerPull, TriggerRelease }

        public FunctionCheck functionCheck;
        [Tooltip("Material using the VRArmory/GhostDemo shader (VR_Ghost_Demo).")]
        public Material ghostMaterial;
        [Tooltip("Unlit vertex-colour material (VR_Guide_Line) for the trigger's path.")]
        public Material lineMaterial;
        public Color ghostColor = new Color(0.3f, 0.95f, 0.88f);

        [Header("Timing (seconds)")]
        public float fadeTime = 0.35f;
        [Tooltip("The ghost first glows on the real part, so the trainee sees which part it is, before it moves.")]
        public float showTime = 0.6f;
        public float slideBackTime = 1.1f;
        [Tooltip("The real slide springs forward; the ghost does too, a little slower so the eye can follow it.")]
        public float slideForwardTime = 0.45f;
        public float triggerPullTime = 0.9f;
        public float triggerReturnTime = 0.55f;
        [Tooltip("Slow release from a full pull to the reset point (FunctionCheck.TriggerResetPoint).")]
        public float triggerReleaseTime = 1.8f;
        [Tooltip("Pause at the end of the movement: this is how far it goes.")]
        public float endHoldTime = 0.7f;
        [Tooltip("Pause back at the start before the next repetition.")]
        public float restTime = 0.45f;

        // The demonstration being played (None once it has finished or was stopped; the ghost may still be fading out).
        public Demo Playing { get; private set; }
        public bool IsPlaying => Playing != Demo.None;
        // How far the ghost is moved right now: 0 = rest, 1 = fully back (as FunctionCheck's SlideAmount / TriggerPull).
        public float Travel { get; private set; }
        public bool GhostVisible => shown != null && shown.root.activeSelf;
        public Transform SlideGhost => slideGhost?.root.transform;
        public Transform TriggerGhost => triggerGhost?.root.transform;
        public event Action<Demo> Started;

        private const int IgnoreRaycastLayer = 2;
        private const int PathSegments = 12;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // One leg of a demonstration: the ghost goes from where it is to 'to' (travel) in 'seconds'.
        private struct Move
        {
            public float seconds, to;
            public bool springBack; // fast start, soft stop (a spring), instead of the default ease in and out
        }

        private class Ghost
        {
            public GameObject root;
            public Material material;
        }

        private Ghost slideGhost, triggerGhost, shown;
        private LineRenderer triggerPath;
        private readonly Vector3[] pathLocal = new Vector3[PathSegments + 1]; // in the trigger's parent space
        private readonly Vector3[] pathWorld = new Vector3[PathSegments + 1];

        private readonly Move[] moves = new Move[5];
        private int moveCount, moveIndex, cyclesLeft;
        private float moveTime, moveFrom, startTravel;
        private float visibility, flash;
        private bool fadingOut;
        private float fadeOutSeconds;

        // ---------- Control ----------

        // Starts a demonstration ('cycles' repetitions), replacing any other. Needs the function check to have begun.
        public void Play(Demo demo, int cycles = 1)
        {
            if (demo == Demo.None || functionCheck == null || !functionCheck.HasRig) return;
            Ghost ghost = GhostFor(demo);
            if (ghost == null) return;

            if (shown != null && shown != ghost) Hide(shown);
            if (shown != ghost) visibility = 0f; // the same ghost played again keeps its glow instead of blinking out
            shown = ghost;
            Playing = demo;
            BuildMoves(demo);
            cyclesLeft = demo == Demo.TriggerRelease ? 1 : Mathf.Max(1, cycles); // a release does not end where it began
            moveIndex = 0;
            moveTime = 0f;
            Travel = moveFrom = startTravel;
            flash = 0f;
            fadingOut = false;
            ghost.root.SetActive(true);
            if (triggerPath != null) triggerPath.enabled = ghost == triggerGhost;
            ApplyPose();
            Started?.Invoke(demo);
        }

        // Ends the demonstration now (the trainee has started, or the step changed). The ghost fades out where it is.
        public void Stop()
        {
            if (!IsPlaying) return;
            Playing = Demo.None;
            FadeOut(fadeTime * 0.5f);
        }

        void OnDisable()
        {
            Playing = Demo.None;
            if (shown != null) Hide(shown);
        }

        void OnDestroy()
        {
            // The two runtime material copies (the ghost objects themselves go with the scene).
            if (slideGhost != null) Destroy(slideGhost.material);
            if (triggerGhost != null) Destroy(triggerGhost.material);
        }

        // ---------- Playback ----------

        void Update()
        {
            if (shown == null || !shown.root.activeSelf) return;

            if (IsPlaying) Advance(Time.deltaTime);

            visibility = Mathf.MoveTowards(visibility, fadingOut ? 0f : 1f, Time.deltaTime / (fadingOut ? fadeOutSeconds : fadeTime));
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 2.5f);
            if (fadingOut && visibility <= 0f)
            {
                Hide(shown);
                return;
            }
            ApplyPose();
        }

        // The first leg of every demonstration holds still (showTime), which is when the ghost fades in.
        private void Advance(float dt)
        {
            moveTime += dt;
            Move m = moves[moveIndex];
            float k = m.seconds > 0f ? Mathf.Clamp01(moveTime / m.seconds) : 1f;
            float eased = m.springBack ? 1f - (1f - k) * (1f - k) : Mathf.SmoothStep(0f, 1f, k);
            Travel = Mathf.Lerp(moveFrom, m.to, eased);
            if (k < 1f) return;

            // Leg finished. A short glow when the ghost arrives at the end of a movement ("this far").
            if (m.to != moveFrom && m.to != startTravel) flash = 1f;
            moveFrom = m.to;
            moveTime = 0f;
            if (++moveIndex < moveCount) return;

            if (--cyclesLeft > 0)
            {
                moveIndex = 0;
                return;
            }
            Playing = Demo.None; // done: fade out
            FadeOut(fadeTime);
        }

        private void FadeOut(float seconds)
        {
            fadingOut = true;
            fadeOutSeconds = Mathf.Max(0.01f, seconds);
        }

        private void BuildMoves(Demo demo)
        {
            float reset = FunctionCheck.TriggerResetPoint;
            switch (demo)
            {
                case Demo.SlideRack:
                    startTravel = 0f;
                    SetMoves(new Move { seconds = showTime, to = 0f },
                             new Move { seconds = slideBackTime, to = 1f },
                             new Move { seconds = endHoldTime, to = 1f },
                             new Move { seconds = slideForwardTime, to = 0f, springBack = true },
                             new Move { seconds = restTime, to = 0f });
                    break;
                case Demo.TriggerPull:
                    startTravel = 0f;
                    SetMoves(new Move { seconds = showTime, to = 0f },
                             new Move { seconds = triggerPullTime, to = 1f },
                             new Move { seconds = endHoldTime, to = 1f },
                             new Move { seconds = triggerReturnTime, to = 0f, springBack = true },
                             new Move { seconds = restTime, to = 0f });
                    break;
                default: // TriggerRelease: starts where the trainee's trigger is (fully pulled), stops at the reset click
                    startTravel = 1f;
                    SetMoves(new Move { seconds = showTime, to = 1f },
                             new Move { seconds = triggerReleaseTime, to = reset },
                             new Move { seconds = endHoldTime * 1.5f, to = reset });
                    break;
            }
        }

        private void SetMoves(params Move[] list)
        {
            moveCount = list.Length;
            for (int i = 0; i < list.Length; i++) moves[i] = list[i];
        }

        private void ApplyPose()
        {
            if (shown == slideGhost)
            {
                shown.root.transform.localPosition = functionCheck.SlideLocalPosition(Travel);
            }
            else if (shown == triggerGhost)
            {
                functionCheck.TriggerLocalPose(Travel, out Vector3 p, out Quaternion r);
                shown.root.transform.localPosition = p;
                shown.root.transform.localRotation = r;
                UpdatePath();
            }

            Color c = ghostColor * (1f + 0.6f * flash);
            c.a = visibility;
            shown.material.SetColor(ColorId, c);
        }

        // Faint line along the path the tip of the trigger takes, brighter towards the end: direction and distance.
        private void UpdatePath()
        {
            if (triggerPath == null) return;
            Transform parent = functionCheck.TriggerPiece.parent;
            for (int i = 0; i <= PathSegments; i++) pathWorld[i] = parent.TransformPoint(pathLocal[i]);
            triggerPath.SetPositions(pathWorld);
            float a = visibility * 0.8f;
            triggerPath.startColor = new Color(ghostColor.r, ghostColor.g, ghostColor.b, a * 0.15f);
            triggerPath.endColor = new Color(ghostColor.r, ghostColor.g, ghostColor.b, a);
        }

        private void Hide(Ghost ghost)
        {
            ghost.root.SetActive(false);
            if (ghost == triggerGhost && triggerPath != null) triggerPath.enabled = false;
            if (ghost == shown)
            {
                shown = null;
                fadingOut = false;
            }
        }

        // ---------- Building the ghosts (once each) ----------

        private Ghost GhostFor(Demo demo)
        {
            if (demo == Demo.SlideRack)
            {
                if (slideGhost == null && functionCheck.SlidePiece != null)
                    slideGhost = BuildGhost(functionCheck.SlidePiece, functionCheck.slide);
                return slideGhost;
            }
            if (triggerGhost == null && functionCheck.TriggerPiece != null)
            {
                triggerGhost = BuildGhost(functionCheck.TriggerPiece, functionCheck.frame);
                BuildTriggerPath();
            }
            return triggerGhost;
        }

        // Copies the meshes of 'piece' that belong to 'owner' (so the barrel and spring seated in the slide stay out),
        // under a new root beside the piece. The root's pose is set every frame from FunctionCheck.
        private Ghost BuildGhost(Transform piece, AssemblyPart owner)
        {
            var root = new GameObject("Ghost_Demo_" + piece.name);
            root.layer = IgnoreRaycastLayer;
            root.AddComponent<GhostVisual>();
            root.transform.SetParent(piece.parent, false);
            root.transform.localRotation = piece.localRotation;
            root.transform.localScale = piece.localScale;

            var ghost = new Ghost { root = root, material = new Material(ghostMaterial) { name = root.name } };
            foreach (MeshRenderer source in piece.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter mf = source.GetComponent<MeshFilter>();
                if (!source.enabled || mf == null || mf.sharedMesh == null) continue;
                if (source.GetComponentInParent<AssemblyPart>() != owner || source.GetComponentInParent<SnapSocket>() != null) continue;

                var copy = new GameObject(source.name);
                copy.layer = IgnoreRaycastLayer;
                copy.transform.SetParent(root.transform, false);
                // Same pose relative to the piece as the original, wherever the piece is right now.
                copy.transform.localPosition = piece.InverseTransformPoint(source.transform.position);
                copy.transform.localRotation = Quaternion.Inverse(piece.rotation) * source.transform.rotation;
                Vector3 s = source.transform.lossyScale, ps = piece.lossyScale;
                copy.transform.localScale = new Vector3(s.x / ps.x, s.y / ps.y, s.z / ps.z);

                copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                MeshRenderer mr = copy.AddComponent<MeshRenderer>();
                var materials = new Material[mf.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = ghost.material;
                mr.sharedMaterials = materials;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            root.SetActive(false);
            return ghost;
        }

        // The path is measured at the corner of the trigger that travels furthest (its tip).
        private void BuildTriggerPath()
        {
            Transform t = functionCheck.TriggerPiece;
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || lineMaterial == null) return;

            functionCheck.TriggerLocalPose(0f, out Vector3 p0, out Quaternion r0);
            functionCheck.TriggerLocalPose(1f, out Vector3 p1, out Quaternion r1);
            Bounds b = mf.sharedMesh.bounds;
            Vector3 tip = b.center;
            float best = -1f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float moved = (InParent(p1, r1, t.localScale, corner) - InParent(p0, r0, t.localScale, corner)).sqrMagnitude;
                if (moved > best) { best = moved; tip = corner; }
            }
            for (int i = 0; i <= PathSegments; i++)
            {
                functionCheck.TriggerLocalPose((float)i / PathSegments, out Vector3 p, out Quaternion r);
                pathLocal[i] = InParent(p, r, t.localScale, tip);
            }

            var go = new GameObject("Ghost_Demo_Trigger_Path");
            go.layer = IgnoreRaycastLayer;
            go.AddComponent<GhostVisual>();
            go.transform.SetParent(transform, false);
            triggerPath = go.AddComponent<LineRenderer>();
            triggerPath.sharedMaterial = lineMaterial;
            triggerPath.useWorldSpace = true;
            triggerPath.positionCount = PathSegments + 1;
            triggerPath.startWidth = 0.0015f;
            triggerPath.endWidth = 0.003f;
            triggerPath.numCapVertices = 3;
            triggerPath.shadowCastingMode = ShadowCastingMode.Off;
            triggerPath.receiveShadows = false;
            triggerPath.enabled = false;
        }

        private static Vector3 InParent(Vector3 pos, Quaternion rot, Vector3 scale, Vector3 local) => pos + rot * Vector3.Scale(scale, local);
    }
}
