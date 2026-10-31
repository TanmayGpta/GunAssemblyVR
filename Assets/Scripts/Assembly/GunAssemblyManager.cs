using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRArmory
{
    public enum AssemblyMode { GuidedTraining, TimeTrial }

    [Serializable]
    public class AssemblyStep
    {
        public string title;
        public string instruction;
        public GunPart part;
        [Tooltip("Leave empty for steps whose socket is not built yet.")]
        public SnapSocket socket;
        [Tooltip("Non-socket step driven by an interaction script (step 05).")]
        public FunctionCheck functionCheck;
        [NonSerialized] public bool completed;
    }

    // Phase 02 assembly state machine: walks the step list, unlocks one socket at a time,
    // and drives Tactical_HUD_Canvas. GuidedTraining shows ghosts + live tolerance hints;
    // TimeTrial (future scope) hides all hints and runs a qualification countdown.
    public class GunAssemblyManager : MonoBehaviour
    {
        public static GunAssemblyManager Instance { get; private set; }

        public AssemblyMode mode = AssemblyMode.GuidedTraining;
        public float timeTrialSeconds = 120f;
        public AssemblyHUD hud;
        public List<AssemblyStep> steps = new List<AssemblyStep>();

        [Tooltip("Start step 01 as soon as the scene starts (desktop). The VR intro screen turns this off and calls BeginTraining().")]
        public bool beginOnStart = true;
        [Tooltip("How long 'STEP COMPLETE' / rejection messages stay on the HUD before live hints resume.")]
        public float messageHoldSeconds = 1.5f;
        [Tooltip("Text appended to the instruction when the socket's host is on the bench. {0} = host name.")]
        public string hostHint = "[F] HOLD {0} IN YOUR OFF HAND (OR WORK ON THE BENCH)";

        public event Action<int> StepStarted;
        public event Action AllStepsCompleted;
        public event Action<string> PartRejected;

        public int CurrentStepIndex { get; private set; }
        public bool HintsEnabled => mode == AssemblyMode.GuidedTraining;
        public AssemblyStep CurrentStep => CurrentStepIndex < steps.Count ? steps[CurrentStepIndex] : null;
        public bool HasStarted { get; private set; }
        public bool IsComplete { get; private set; }
        public int MistakeCount { get; private set; }
        public float ElapsedSeconds { get; private set; }

        private float timeRemaining;
        private bool trialFailed;
        private float messageHoldTimer;
        private int pendingStepIndex = -1;
        private int lastRejectFrame = -1;

        // Last values shown by the live directive, so the text is only rebuilt when something visible changes.
        private SnapSocket.Alignment shownAlignment;
        private int shownMillimetres = -1, shownDegrees = -1;
        private bool shownHostInHand;
        private string shownReason;
        private SnapSocket shownSocket;

        private static readonly Color Amber = new Color(1.0f, 0.72f, 0.0f);
        private static readonly Color Green = new Color(0.0f, 1.0f, 0.55f);
        private static readonly Color Red = new Color(1.0f, 0.25f, 0.2f);

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            foreach (AssemblyStep s in steps)
            {
                if (s.socket == null) continue;
                s.socket.SetUnlocked(false);
                s.socket.Seated += OnSocketSeated;
                s.socket.Rejected += OnSocketRejected;
            }
            foreach (AssemblyStep s in steps)
            {
                if (s.functionCheck != null) s.functionCheck.Completed += OnActionStepCompleted;
            }

            timeRemaining = timeTrialSeconds;
            if (hud != null)
            {
                hud.SetHeader("[ PHASE 02 : FIELD ASSEMBLY ]", "BERETTA 92 FS 9MM NATO  |  FM 3-23.35 REASSEMBLY SEQUENCE");
                hud.SetFooter(mode == AssemblyMode.GuidedTraining ? "MODE: GUIDED TRAINING" : "MODE: TIME TRIAL");
            }
            if (beginOnStart) BeginTraining();
            else if (hud != null)
            {
                hud.SetChecklist(steps, -1);
                hud.SetDirective("PRESS START TRAINING ON THE PANEL TO YOUR LEFT", Amber);
            }
        }

        // Unlocks step 01. Safe to call more than once.
        public void BeginTraining()
        {
            if (HasStarted) return;
            HasStarted = true;
            BeginStep(0);
        }

        void Update()
        {
            if (!HasStarted) return;
            if (!IsComplete) ElapsedSeconds += Time.deltaTime;

            if (mode == AssemblyMode.TimeTrial && !trialFailed && CurrentStep != null)
            {
                timeRemaining -= Time.deltaTime;
                if (hud != null) hud.SetFooter($"TIME TRIAL  {Mathf.FloorToInt(Mathf.Max(0f, timeRemaining) / 60f):00}:{Mathf.Max(0f, timeRemaining) % 60f:00.0}");
                if (timeRemaining <= 0f) FailTrial();
            }

            // A completion or rejection message stays readable for a moment before live hints take over again.
            if (messageHoldTimer > 0f)
            {
                messageHoldTimer -= Time.deltaTime;
                if (messageHoldTimer > 0f) return;
                shownSocket = null; // force the live directive to redraw
                if (pendingStepIndex >= 0)
                {
                    int next = pendingStepIndex;
                    pendingStepIndex = -1;
                    BeginStep(next);
                }
            }

            if (HintsEnabled) UpdateLiveDirective();
        }

        private void BeginStep(int index)
        {
            CurrentStepIndex = index;
            if (hud != null) hud.SetChecklist(steps, CurrentStepIndex);

            AssemblyStep step = CurrentStep;
            if (step == null)
            {
                if (hud != null) hud.SetDirective("ALL STEPS COMPLETE - WEAPON ASSEMBLED AND FUNCTION CHECKED", Green);
                if (!IsComplete)
                {
                    IsComplete = true;
                    AllStepsCompleted?.Invoke();
                }
                return;
            }

            StepStarted?.Invoke(index);
            if (step.socket != null) step.socket.SetUnlocked(true);
            else if (step.functionCheck != null) step.functionCheck.Begin(hud);
            else if (hud != null) hud.SetDirective($"STEP {index + 1:00} SOCKET NOT YET INSTALLED (NEXT BUILD)", Amber);
        }

        private void OnSocketSeated(SnapSocket socket)
        {
            AssemblyStep step = CurrentStep;
            if (step == null || step.socket != socket || step.completed) return;

            step.completed = true;
            socket.SetUnlocked(false);
            if (hud != null) hud.SetDirective($"STEP {CurrentStepIndex + 1:00} COMPLETE : {step.title}", Green);
            messageHoldTimer = messageHoldSeconds;
            AdvanceAfterMessage(CurrentStepIndex + 1);
        }

        private void OnActionStepCompleted()
        {
            AssemblyStep step = CurrentStep;
            if (step == null || step.functionCheck == null || step.completed) return;
            step.completed = true;
            // The function check has already shown its "PASSED" message; hold it before the final summary.
            messageHoldTimer = messageHoldSeconds;
            AdvanceAfterMessage(CurrentStepIndex + 1);
        }

        // The next socket step unlocks at once (so a quick trainee is never told "out of sequence"); only the
        // HUD message is held. Steps that write their own HUD text (function check, final summary) wait for it.
        private void AdvanceAfterMessage(int next)
        {
            if (next < steps.Count && steps[next].socket != null) BeginStep(next);
            else
            {
                if (hud != null) hud.SetChecklist(steps, CurrentStepIndex);
                pendingStepIndex = next;
            }
        }

        private void OnSocketRejected(SnapSocket socket, AssemblyPart part, string reason)
        {
            if (!HasStarted || IsComplete) return;
            // One release can be rejected by two sockets (e.g. the active one and the part's own locked one).
            if (Time.frameCount == lastRejectFrame) return;
            lastRejectFrame = Time.frameCount;

            MistakeCount++;
            PartRejected?.Invoke(reason);
            if (hud != null && pendingStepIndex < 0)
            {
                hud.SetDirective($"NOT SEATED : {reason}", Red);
                messageHoldTimer = messageHoldSeconds;
            }
        }

        private void UpdateLiveDirective()
        {
            AssemblyStep step = CurrentStep;
            if (hud == null || step == null || step.socket == null) return;

            SnapSocket s = step.socket;
            AssemblyPart host = s.Host;
            bool hostInHand = host != null && (host.IsInOffHand || host.IsHeld);
            int mm = Mathf.RoundToInt(s.LastDistance * 1000f);
            int deg = Mathf.RoundToInt(s.LastAngle);

            // Skip the string work when nothing visible has changed since last frame.
            bool numbersShown = s.CurrentAlignment == SnapSocket.Alignment.Faint || s.CurrentAlignment == SnapSocket.Alignment.Warning;
            if (s == shownSocket && s.CurrentAlignment == shownAlignment && hostInHand == shownHostInHand
                && s.WarningReason == shownReason
                && (!numbersShown || (mm == shownMillimetres && deg == shownDegrees))) return;
            shownSocket = s;
            shownAlignment = s.CurrentAlignment;
            shownHostInHand = hostInHand;
            shownReason = s.WarningReason;
            shownMillimetres = mm;
            shownDegrees = deg;

            switch (s.CurrentAlignment)
            {
                case SnapSocket.Alignment.Hidden:
                    hud.SetDirective(hostInHand || host == null ? step.instruction
                        : step.instruction + "\n" + string.Format(hostHint, host.name), Amber);
                    break;
                case SnapSocket.Alignment.Faint:
                    hud.SetDirective($"BRING TO GHOST  {mm / 10f:0.0} cm / {deg} deg   (TOL {s.snapDistance * 100f:0} cm / {s.snapAngle:0} deg)", Amber);
                    break;
                case SnapSocket.Alignment.Valid:
                    hud.SetDirective("ALIGNED - RELEASE TO SEAT", Green);
                    break;
                case SnapSocket.Alignment.Warning:
                    hud.SetDirective(s.WarningReason + $"   ({deg} deg)", Red);
                    break;
            }
        }

        private void FailTrial()
        {
            trialFailed = true;
            foreach (AssemblyStep s in steps)
            {
                if (s.socket != null) s.socket.SetUnlocked(false);
            }
            if (hud != null) hud.SetDirective("TIME EXPIRED - QUALIFICATION FAILED", Red);
        }
    }
}
