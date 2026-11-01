namespace VRArmory
{
    // Every sentence the VR trainee reads, in one place. Short, plain words: the trainee may never have used
    // a headset or seen a pistol taken apart. Only the VR panel and the VR guidance read from here.
    public static class VRTrainingText
    {
        // ---------- Modules ----------
        public const string Brand = "GUNASSEMBLY VR";
        public const string ModulesTitle = "Choose your training";
        public const string ModulesFooter = "Point at a card and pull the trigger";

        public const string AssemblyModuleNumber = "01";
        public const string AssemblyModuleTitle = "Gun Assembly";
        public const string AssemblyModuleBody = "Put a pistol together step by step, then test that it works.";
        public const string StatusAvailable = "AVAILABLE";
        public const string StatusInProgress = "IN PROGRESS  ·  CONTINUE";
        public const string StatusCompleted = "COMPLETED  ·  TRAIN AGAIN";

        public const string ShootingModuleNumber = "02";
        public const string ShootingModuleTitle = "Shooting Training";
        public const string ShootingModuleBody = "Aim and fire at targets on the live range.";
        public const string StatusComingSoon = "COMING SOON";

        public const string ComingSoonLabel = "MODULE 02";
        public const string ComingSoonBody = "Aim and fire at targets on the range.\nThis module is being built and arrives in a future update.";
        public const string Back = "Back";

        // ---------- Briefing ----------
        public const string IntroLabel = "MODULE 01  ·  GUN ASSEMBLY";
        public const string IntroTitle = "How it works";
        public const string GripChip = "GRIP";
        public const string GripUse = "Squeeze to pick up and hold a part";
        public const string TriggerChip = "TRIGGER";
        public const string TriggerUse = "Pull to press buttons";
        public const string StickChip = "THUMBSTICK";
        public const string StickUse = "Push forward to move, left or right to turn";
        public const string IntroHint = "Glowing parts and outlines always show you what to do next.";
        public const string StartTraining = "Start training";

        // ---------- Training ----------
        public const string Restart = "Restart";
        public static string StepLabel(int index, int count, string name) => $"STEP {index + 1} OF {count}  ·  {name.ToUpperInvariant()}";

        public static string PartName(GunPart part)
        {
            switch (part)
            {
                case GunPart.Barrel: return "barrel";
                case GunPart.RecoilSpring: return "recoil spring";
                case GunPart.UpperSlide: return "slide";
                case GunPart.Magazine: return "magazine";
                default: return "frame";
            }
        }

        public const string FunctionCheckName = "Function check";

        public static string PickUp(GunPart part) => $"Pick up the glowing {PartName(part)}";
        public const string PickUpFirstDetail = "Reach out and squeeze the GRIP button on your controller.";
        public const string PickUpDetail = "Squeeze GRIP to pick it up.";
        public static string HostHeldDetail(GunPart host) => $"Good - keep holding the {PartName(host)}. Use your other hand.";

        public static string Place(GunPart part)
        {
            switch (part)
            {
                case GunPart.Barrel: return "Put the barrel into the slide";
                case GunPart.RecoilSpring: return "Slide the spring in under the barrel";
                case GunPart.UpperSlide: return "Put the slide onto the frame";
                case GunPart.Magazine: return "Push the magazine into the grip";
                default: return "Put it in place";
            }
        }
        public const string PlaceDetail = "Move it to the glowing outline. It clicks in when it lines up.";

        public static string WrongPart(GunPart held) => $"That's the {PartName(held)} - it comes later";
        public static string WrongPartDetail(GunPart wanted) => $"Put it down and pick up the glowing {PartName(wanted)}.";

        // Live correction while the right part is carried (VRGuidance.Correction).
        public const string BringToTarget = "Follow the line to the glowing outline";
        public const string MoveLeft = "Move it to the left";
        public const string MoveRight = "Move it to the right";
        public const string MoveUp = "Raise it a little";
        public const string MoveDown = "Lower it a little";
        public const string MoveAway = "Push it away from you";
        public const string MoveCloser = "Bring it toward you";
        public const string RotateClockwise = "Turn it clockwise";
        public const string RotateCounterClockwise = "Turn it counter-clockwise";
        public const string TurnLeft = "Turn it to the left";
        public const string TurnRight = "Turn it to the right";
        public const string TiltUp = "Tilt the front up";
        public const string TiltDown = "Tilt the front down";
        public const string Aligned = "Perfect - hold it there";
        public const string TargetLabel = "PUT IT HERE";

        public static string Seated(GunPart part) => $"{Capital(PartName(part))} in place!";

        // Placement rejected (the part goes back to the bench).
        public const string RejectOutOfSequence = "Not yet - that part comes later";
        public const string RejectWrongPart = "That part doesn't go there";
        public const string RejectTooFar = "Too far from the outline - try again";
        public const string RejectMisaligned = "The angle was off - try again";
        public const string RejectDetail = "The part went back to the bench.";

        // ---------- Assembly complete -> function check ----------
        public const string AssemblyComplete = "Assembly complete!";
        public const string AssemblyCompleteDetail = "Next: test that the pistol works.";

        public const string PickUpPistol = "Pick up the pistol";
        public const string PickUpPistolDetail = "Hold it by the grip with one hand.";
        public const string PistolLabel = "PICK ME UP";

        // FunctionCheck stages 0..6. {0} = the safety button of the hand holding the pistol (A or X).
        static readonly string[] StageTitles =
        {
            "Turn the safety on",
            "Pull the trigger all the way",
            "Turn the safety off",
            "Pull the trigger all the way",
            "Keep holding the trigger",
            "Slowly let go of the trigger",
            "Pull the trigger once more",
        };
        static readonly string[] StageDetails =
        {
            "Press {0} on the hand holding the pistol.",
            "The safety is on, so the hammer must not move.",
            "Let go of the trigger, then press {0}.",
            "This time the hammer drops.",
            "With your other hand, grab the back of the slide, pull it back, then let go.",
            "Stop when you feel the click.",
            "The hammer drops again. That is the last check.",
        };
        public static string StageTitle(int stage) => StageTitles[stage];
        public static string StageDetail(int stage, string safetyButton) => string.Format(StageDetails[stage], safetyButton);
        public const string ReleaseTrigger = "Now let go of the trigger";
        public const string ReleaseTriggerFirst = "Let go of the trigger first";
        public const string HoldTriggerWhileRacking = "Keep the trigger pulled while you rack";

        // In-world tooltips at the controller / slide.
        public static string PressButton(string button) => $"Press {button}";
        public const string PullTrigger = "Pull trigger";
        public const string HoldTrigger = "Hold trigger";
        public const string LetGoSlowly = "Let go slowly";
        public const string LetGo = "Let go";
        public const string RackHere = "Grab here, pull back";

        // FunctionCheck.Notified messages, translated. Unknown messages are not shown.
        public static string Notification(string message)
        {
            if (message.StartsWith("TRIGGER DISCONNECTED")) return "Safe - the hammer did not fall";
            if (message.StartsWith("DECOCKED")) return "Hammer lowered safely";
            if (message.StartsWith("RESET")) return "Click! The trigger has reset";
            if (message.Contains("HAMMER FELL")) return "Click! The hammer fell";
            if (message.StartsWith("TRIGGER RELEASED TOO EARLY")) return "You let go too early - squeeze, hold, and rack again";
            if (message.StartsWith("HOLD THE TRIGGER BACK")) return "Keep the trigger pulled while you rack the slide";
            return null;
        }

        public const string CheckPassed = "Function check passed!";
        public const string CheckPassedDetail = "The pistol works correctly.";

        // ---------- Complete ----------
        public const string CompleteTitle = "Training complete";
        public const string CompleteBody = "You assembled the pistol and proved that it works.";
        public static string Result(int seconds, int mistakes) =>
            $"<color=#8FA3B8>TIME</color>  {seconds / 60:00}:{seconds % 60:00}          <color=#8FA3B8>MISTAKES</color>  {mistakes}";
        public const string TrainAgain = "Train again";
        public const string BackToModules = "Back to modules";

        static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
