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

        public int CurrentStepIndex { get; private set; }
        public bool HintsEnabled => mode == AssemblyMode.GuidedTraining;
        public AssemblyStep CurrentStep => CurrentStepIndex < steps.Count ? steps[CurrentStepIndex] : null;

        private float timeRemaining;
        private bool trialFailed;

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
            BeginStep(0);
        }

        void Update()
        {
            if (mode == AssemblyMode.TimeTrial && !trialFailed && CurrentStep != null)
            {
                timeRemaining -= Time.deltaTime;
                if (hud != null) hud.SetFooter($"TIME TRIAL  {Mathf.FloorToInt(Mathf.Max(0f, timeRemaining) / 60f):00}:{Mathf.Max(0f, timeRemaining) % 60f:00.0}");
                if (timeRemaining <= 0f) FailTrial();
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
                return;
            }

            if (step.socket != null) step.socket.SetUnlocked(true);
            else if (step.functionCheck != null) step.functionCheck.Begin(hud);
            else if (hud != null) hud.SetDirective($"STEP {index + 1:00} SOCKET NOT YET INSTALLED (NEXT BUILD)", Amber);
        }

        private void OnSocketSeated(SnapSocket socket)
        {
            AssemblyStep step = CurrentStep;
            if (step == null || step.socket != socket) return;

            step.completed = true;
            socket.SetUnlocked(false);
            if (hud != null) hud.SetDirective($"STEP {CurrentStepIndex + 1:00} COMPLETE : {step.title}", Green);
            BeginStep(CurrentStepIndex + 1);
        }

        private void OnActionStepCompleted()
        {
            AssemblyStep step = CurrentStep;
            if (step == null || step.functionCheck == null) return;
            step.completed = true;
            BeginStep(CurrentStepIndex + 1);
        }

        private void UpdateLiveDirective()
        {
            AssemblyStep step = CurrentStep;
            if (hud == null || step == null || step.socket == null) return;

            SnapSocket s = step.socket;
            switch (s.CurrentAlignment)
            {
                case SnapSocket.Alignment.Hidden:
                    AssemblyPart host = s.GetComponentInParent<AssemblyPart>();
                    bool hostInHand = host != null && host.IsInOffHand;
                    hud.SetDirective(hostInHand || host == null ? step.instruction
                        : $"{step.instruction}\n[F] HOLD {host.name} IN YOUR OFF HAND (OR WORK ON THE BENCH)", Amber);
                    break;
                case SnapSocket.Alignment.Faint:
                    hud.SetDirective($"BRING TO GHOST  {s.LastDistance * 100f:0.0} cm / {s.LastAngle:0} deg   (TOL {s.snapDistance * 100f:0} cm / {s.snapAngle:0} deg)", Amber);
                    break;
                case SnapSocket.Alignment.Valid:
                    hud.SetDirective("ALIGNED - RELEASE TO SEAT", Green);
                    break;
                case SnapSocket.Alignment.Warning:
                    hud.SetDirective(s.WarningReason + $"   ({s.LastAngle:0} deg)", Red);
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
