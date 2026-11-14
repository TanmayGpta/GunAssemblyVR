using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // The VR instructor. Turns the training state into spoken instructions (VRVoiceGuide) and, in the function check,
    // movement demonstrations (VRGhostDemo), so the voice and the ghost always tell the same story. It only reads state
    // (VRTrainingMenu screens, GunAssemblyManager, VRGuidance, FunctionCheck) and never completes anything itself:
    // the trainee still does every action, checked by SnapSocket / FunctionCheck exactly as before.
    //
    // Every situation is a "cue" (pick up the barrel, pull the trigger with the safety on, rack the slide, ...).
    // When the cue changes, the coach speaks once for it:
    //   plain cue -> the instruction
    //   demo cue  -> the "watch" line and the ghost demonstration together, then "now you try" once both have finished
    // A demonstration stops as soon as the trainee starts the action, the cue changes or the pistol is put down.
    // If nothing happens for a while, the instruction (and demonstration) is given again, at most twice per cue.
    // B / Y on either controller, or "Hear again" on the panel, repeats it at any time.
    public class VRTrainingCoach : MonoBehaviour
    {
        public enum Cue
        {
            None, ChooseModule, Briefing, PickUpPart, PlacePart, PickUpPistol, PickUpPistolAgain,
            SafetyOn, SafeTrigger, SafeLetGo, SafetyOff, DoubleAction, Rack, Reset, SingleAction, Passed, Complete,
            ChooseWeapon,
        }

        public GunAssemblyManager manager;
        public FunctionCheck functionCheck;
        public VRFunctionCheckControls functionCheckControls;
        public VRGuidance guidance;
        public VRTrainingMenu menu;
        public VRVoiceGuide voice;
        public VRGhostDemo ghost;

        [Header("Timing")]
        [Tooltip("Seconds without progress (voice quiet, no demonstration, trainee not acting) before the instruction is repeated.")]
        public float reminderDelay = 16f;
        public int maxReminders = 2;
        [Tooltip("Pause after a demonstration before the trainee is asked to try.")]
        public float promptDelay = 0.4f;
        [Tooltip("How many times a movement is shown when its step begins (a reminder shows it once).")]
        public int demoRepeats = 2;
        [Tooltip("How far (0..1) the trigger has to move before the trainee counts as having started.")]
        public float actingThreshold = 0.15f;

        public Cue CurrentCue { get; private set; }
        public int RemindersGiven { get; private set; }
        // A demonstration is planned or running and the "now you try" line has not been spoken yet.
        public bool DemoPending => demo != VRGhostDemo.Demo.None;

        // The welcome is spoken once per run, not again after Restart reloads the scene.
        private static bool welcomed;

        private int cueStep = -1;
        private int pickAnnounced = -1, placeAnnounced = -1; // steps whose instruction has been given
        private bool pistolHeldBefore;
        private float idleTime;
        private float triggerMin, triggerMax; // trigger travel seen since the cue began
        private float lastTryAgainTime = -100f;

        // The demonstration of the current cue.
        private VRGhostDemo.Demo demo;
        private VoiceLine demoLine, promptLine;
        private int demoCycles;
        private bool demoStarted;
        private float promptAt = -1f;

        private InputAction leftRepeat, rightRepeat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => welcomed = false; // also correct when domain reload is off in the Editor

        void Awake()
        {
            leftRepeat = new InputAction("Left Secondary Button", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
            rightRepeat = new InputAction("Right Secondary Button", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
            if (menu.repeatButton != null) menu.repeatButton.onClick.AddListener(Repeat);
        }

        void OnEnable()
        {
            leftRepeat.Enable();
            rightRepeat.Enable();
            voice.LineStarted += OnLineStarted;
            functionCheck.Notified += OnCheckEvent;
            foreach (AssemblyStep s in manager.steps)
            {
                if (s.socket == null) continue;
                s.socket.Seated += OnSeated;
                s.socket.Rejected += OnRejected;
            }
        }

        void OnDisable()
        {
            leftRepeat.Disable();
            rightRepeat.Disable();
            voice.LineStarted -= OnLineStarted;
            functionCheck.Notified -= OnCheckEvent;
            foreach (AssemblyStep s in manager.steps)
            {
                if (s.socket == null) continue;
                s.socket.Seated -= OnSeated;
                s.socket.Rejected -= OnRejected;
            }
        }

        void OnDestroy()
        {
            leftRepeat.Dispose();
            rightRepeat.Dispose();
        }

        void Update()
        {
            // B / Y of the hand holding the live pistol is its magazine release, not "repeat".
            if ((leftRepeat.WasPressedThisFrame() && !PistolMagazine.OwnsSecondaryButton(true))
                || (rightRepeat.WasPressedThisFrame() && !PistolMagazine.OwnsSecondaryButton(false))) Repeat();

            Cue cue = ReadCue(out int step);
            if (cue != CurrentCue || step != cueStep) Enter(cue, step);

            float pull = functionCheck.TriggerPull;
            triggerMin = Mathf.Min(triggerMin, pull);
            triggerMax = Mathf.Max(triggerMax, pull);

            UpdateDemo();
            UpdateReminder();
        }

        // Plays the current instruction again, with its demonstration (panel button, B / Y).
        public void Repeat()
        {
            if (CurrentCue == Cue.None) return;
            voice.Stop();
            StartCue(fresh: false, demoRepeats);
        }

        // ---------- Where is the trainee? ----------

        private Cue ReadCue(out int step)
        {
            step = manager.CurrentStepIndex;
            if (menu.completePanel.activeSelf) return Cue.Complete;
            if (!manager.HasStarted || !menu.progressPanel.activeSelf)
            {
                if (menu.introPanel.activeSelf) return Cue.Briefing;
                if (menu.weaponPanel != null && menu.weaponPanel.activeSelf) return Cue.ChooseWeapon;
                return menu.modulesPanel.activeSelf ? Cue.ChooseModule : Cue.None;
            }

            AssemblyStep s = manager.CurrentStep;
            if (s == null) return Cue.None;
            if (s.socket != null) return guidance.CurrentPhase == VRGuidance.Phase.Place ? Cue.PlacePart : Cue.PickUpPart;

            if (functionCheck.Passed) return Cue.Passed;
            if (!functionCheck.IsRunning) return Cue.None;
            if (!functionCheckControls.frameGrab.isSelected) return pistolHeldBefore ? Cue.PickUpPistolAgain : Cue.PickUpPistol;
            switch (functionCheck.Stage)
            {
                case 0: return Cue.SafetyOn;
                case 1: return functionCheck.SafeTriggerPulled ? Cue.SafeLetGo : Cue.SafeTrigger;
                case 2: return Cue.SafetyOff;
                case 3: return Cue.DoubleAction;
                case 4: return Cue.Rack;
                case 5: return Cue.Reset;
                default: return Cue.SingleAction;
            }
        }

        private void Enter(Cue cue, int step)
        {
            // Picking the pistol up again continues where the trainee was, without repeating the praise.
            bool resumed = CurrentCue == Cue.PickUpPistolAgain;
            CurrentCue = cue;
            cueStep = step;
            if (cue >= Cue.SafetyOn && cue <= Cue.SingleAction) pistolHeldBefore = true;
            StartCue(fresh: !resumed, demoRepeats);
        }

        private void StartCue(bool fresh, int cycles)
        {
            CancelDemo();
            idleTime = 0f;
            triggerMin = triggerMax = functionCheck.TriggerPull;
            if (fresh) RemindersGiven = 0;
            Speak(fresh, cycles);
        }

        // ---------- What to say (and show) for each cue ----------

        // fresh = the cue has just begun (confirmations are included and one-time lines are not repeated);
        // otherwise the instruction is being given again.
        private void Speak(bool fresh, int cycles)
        {
            int step = cueStep;
            switch (CurrentCue)
            {
                case Cue.ChooseModule:
                    if (fresh && welcomed) return;
                    welcomed = true;
                    voice.Play(VoiceLine.Intro_ChooseModule);
                    break;
                case Cue.Briefing:
                    voice.Play(Tec9OnBench ? VoiceLine.Tec9_Briefing : VoiceLine.Intro_Briefing);
                    break;

                case Cue.PickUpPart:
                    // Back here in the same step (dropped, or rejected): the try-again line has covered it.
                    if (fresh && pickAnnounced == step) return;
                    pickAnnounced = step;
                    voice.Play(PickLine(manager.steps[step].part));
                    break;
                case Cue.PlacePart:
                    if (fresh && placeAnnounced == step) return;
                    placeAnnounced = step;
                    voice.Play(PlaceLine(manager.steps[step].part));
                    break;

                case Cue.PickUpPistol:
                    voice.Play(VoiceLine.FC_Intro);
                    break;
                case Cue.PickUpPistolAgain:
                    voice.Play(VoiceLine.FC_PickUpAgain);
                    break;
                case Cue.SafetyOn:
                    voice.Play(PistolInLeftHand() ? VoiceLine.FC_SafetyOn_X : VoiceLine.FC_SafetyOn_A);
                    break;
                case Cue.SafeTrigger:
                    Demonstrate(VRGhostDemo.Demo.TriggerPull, cycles, VoiceLine.FC_TriggerDemo_Safe, VoiceLine.FC_YourTurn_Trigger);
                    break;
                case Cue.SafeLetGo:
                    voice.Play(fresh ? VoiceLine.FC_SafeOk : VoiceLine.None, VoiceLine.FC_LetGo);
                    break;
                case Cue.SafetyOff:
                    voice.Play(PistolInLeftHand() ? VoiceLine.FC_SafetyOff_X : VoiceLine.FC_SafetyOff_A);
                    break;
                case Cue.DoubleAction:
                    // Beretta: the trigger was shown two steps ago, once is enough. TEC-9: the check starts here.
                    if (Tec9OnBench) Demonstrate(VRGhostDemo.Demo.TriggerPull, cycles, VoiceLine.Tec9_FC_TriggerDemo_First, VoiceLine.FC_YourTurn_Trigger);
                    else Demonstrate(VRGhostDemo.Demo.TriggerPull, 1, VoiceLine.FC_TriggerDemo_Fire, VoiceLine.FC_YourTurn_Trigger);
                    break;
                case Cue.Rack:
                    Demonstrate(VRGhostDemo.Demo.SlideRack, cycles,
                                Tec9OnBench ? VoiceLine.Tec9_FC_BoltDemo : VoiceLine.FC_SlideDemo,
                                Tec9OnBench ? VoiceLine.Tec9_FC_YourTurn_Rack : VoiceLine.FC_YourTurn_Rack,
                                fresh ? VoiceLine.FC_HammerDropped : VoiceLine.None, VoiceLine.FC_KeepHolding);
                    break;
                case Cue.ChooseWeapon:
                    voice.Play(VoiceLine.Intro_ChooseWeapon);
                    break;
                case Cue.Reset:
                    Demonstrate(VRGhostDemo.Demo.TriggerRelease, 1, VoiceLine.FC_ReleaseDemo, VoiceLine.FC_YourTurn_Release,
                                fresh ? VoiceLine.Common_Good : VoiceLine.None);
                    break;
                case Cue.SingleAction:
                    Demonstrate(VRGhostDemo.Demo.TriggerPull, 1, VoiceLine.FC_TriggerDemo_Last, VoiceLine.FC_YourTurn_Last,
                                fresh ? VoiceLine.FC_ResetClick : VoiceLine.None);
                    break;
                case Cue.Passed:
                    voice.Play(VoiceLine.FC_Passed);
                    break;
                case Cue.Complete:
                    voice.Play(VoiceLine.Training_Complete);
                    break;
            }
        }

        // Weapon on the bench: the TEC-9 has its own assembly and function-check lines.
        private static bool Tec9OnBench =>
            WeaponKitSwitch.Instance != null && WeaponKitSwitch.Instance.Active != null && WeaponKitSwitch.Instance.Active.id == VRTrainingText.Tec9Id;

        private static VoiceLine Tec9Line(GunPart part, bool place)
        {
            switch (part)
            {
                case GunPart.Bolt: return place ? VoiceLine.Tec9_Place_Bolt : VoiceLine.Tec9_Pick_Bolt;
                case GunPart.RecoilSpring: return place ? VoiceLine.Tec9_Place_Spring : VoiceLine.Tec9_Pick_Spring;
                case GunPart.UpperReceiver: return place ? VoiceLine.Tec9_Place_Upper : VoiceLine.Tec9_Pick_Upper;
                case GunPart.TakedownPin: return place ? VoiceLine.Tec9_Place_Pin : VoiceLine.Tec9_Pick_Pin;
                case GunPart.Magazine: return place ? VoiceLine.Tec9_Place_Magazine : VoiceLine.Tec9_Pick_Magazine;
                default: return VoiceLine.None;
            }
        }

        private static VoiceLine PickLine(GunPart part)
        {
            if (Tec9OnBench) return Tec9Line(part, false);
            switch (part)
            {
                case GunPart.Barrel: return VoiceLine.Asm_Pick_Barrel;
                case GunPart.RecoilSpring: return VoiceLine.Asm_Pick_Spring;
                case GunPart.UpperSlide: return VoiceLine.Asm_Pick_Slide;
                case GunPart.Magazine: return VoiceLine.Asm_Pick_Magazine;
                default: return VoiceLine.None;
            }
        }

        private static VoiceLine PlaceLine(GunPart part)
        {
            if (Tec9OnBench) return Tec9Line(part, true);
            switch (part)
            {
                case GunPart.Barrel: return VoiceLine.Asm_Place_Barrel;
                case GunPart.RecoilSpring: return VoiceLine.Asm_Place_Spring;
                case GunPart.UpperSlide: return VoiceLine.Asm_Place_Slide;
                case GunPart.Magazine: return VoiceLine.Asm_Place_Magazine;
                default: return VoiceLine.None;
            }
        }

        // ---------- Demonstrations ----------

        // Speaks 'before', then 'watchLine' while the ghost shows the movement, then 'tryLine' once both have finished.
        private void Demonstrate(VRGhostDemo.Demo kind, int cycles, VoiceLine watchLine, VoiceLine tryLine, params VoiceLine[] before)
        {
            demo = kind;
            demoCycles = cycles;
            demoLine = watchLine;
            promptLine = tryLine;
            demoStarted = false;
            promptAt = -1f;

            var lines = new VoiceLine[before.Length + 1];
            before.CopyTo(lines, 0);
            lines[before.Length] = watchLine;
            voice.Play(lines);
        }

        // The ghost starts with the first word of the "watch" line, so picture and voice always begin together.
        private void OnLineStarted(VoiceLine line)
        {
            if (demo == VRGhostDemo.Demo.None || demoStarted || line != demoLine) return;
            demoStarted = true;
            ghost.Play(demo, demoCycles);
        }

        private void UpdateDemo()
        {
            if (demo == VRGhostDemo.Demo.None) return;

            // The trainee is already doing it: stop showing and do not ask again.
            if (TraineeActing())
            {
                CancelDemo();
                voice.ClearQueue();
                return;
            }

            if (demoStarted && promptAt < 0f && !ghost.IsPlaying && !voice.IsBusy) promptAt = Time.time + promptDelay;
            if (promptAt >= 0f && Time.time >= promptAt)
            {
                VoiceLine prompt = promptLine;
                CancelDemo();
                voice.Play(prompt);
            }
        }

        private void CancelDemo()
        {
            demo = VRGhostDemo.Demo.None;
            demoStarted = false;
            promptAt = -1f;
            ghost.Stop();
        }

        // Has the trainee started the action of this cue? (Then a demonstration would only get in the way.)
        private bool TraineeActing()
        {
            switch (CurrentCue)
            {
                case Cue.SafeTrigger:
                case Cue.DoubleAction:
                case Cue.SingleAction:
                    return functionCheck.TriggerPull - triggerMin > actingThreshold;
                case Cue.Reset:
                    return triggerMax - functionCheck.TriggerPull > actingThreshold;
                case Cue.Rack:
                    return functionCheckControls.RackGripHeld || functionCheck.SlideAmount > 0.05f;
                default:
                    return false;
            }
        }

        // ---------- Help when the trainee is stuck ----------

        private void UpdateReminder()
        {
            bool remindable = CurrentCue >= Cue.PickUpPart && CurrentCue <= Cue.SingleAction;
            if (!remindable || voice.IsBusy || ghost.IsPlaying || promptAt >= 0f || TraineeActing())
            {
                idleTime = 0f;
                return;
            }
            idleTime += Time.deltaTime;
            if (idleTime < reminderDelay || RemindersGiven >= maxReminders) return;
            RemindersGiven++;
            StartCue(fresh: false, 1);
        }

        // ---------- Events ----------

        private void OnSeated(SnapSocket socket)
        {
            bool assemblyDone = true;
            foreach (AssemblyStep s in manager.steps)
            {
                if (s.socket != null && !s.socket.IsFilled) assemblyDone = false;
            }
            // A confirmation: the next step's instruction is queued after it, never over it.
            voice.Play(assemblyDone ? VoiceLine.Asm_Complete : VoiceLine.Common_WellDone);
        }

        private void OnRejected(SnapSocket socket, AssemblyPart part, string reason)
        {
            // One release can be rejected by two sockets; and a quick second attempt does not need the same advice.
            if (!manager.HasStarted || manager.IsComplete || Time.time - lastTryAgainTime < 4f) return;
            lastTryAgainTime = Time.time;
            voice.Play(VoiceLine.Asm_TryAgain);
            idleTime = 0f;
        }

        private void OnCheckEvent(string message)
        {
            if (functionCheck.Passed) return;
            bool tec9 = Tec9OnBench;
            VoiceLine line = message.StartsWith("HOLD THE TRIGGER BACK") ? (tec9 ? VoiceLine.Tec9_FC_HoldWhileRacking : VoiceLine.FC_HoldWhileRacking)
                           : message.StartsWith("TRIGGER RELEASED TOO EARLY") ? (tec9 ? VoiceLine.Tec9_FC_ReleasedTooEarly : VoiceLine.FC_ReleasedTooEarly)
                           : VoiceLine.None;
            if (line == VoiceLine.None) return;
            CancelDemo();
            voice.Play(line);
            idleTime = 0f;
        }

        private bool PistolInLeftHand()
        {
            VRAssemblyGrab grab = functionCheckControls.frameGrab;
            ActionBasedController left = functionCheckControls.leftController;
            return grab.isSelected && left != null && grab.interactorsSelecting[0].transform.IsChildOf(left.transform);
        }
    }
}
