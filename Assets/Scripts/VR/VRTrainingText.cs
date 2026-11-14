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
        public const string StatusLiveRange = "LIVE RANGE";
        public const string StatusLocked = "ASSEMBLE FIRST";
        public const string ShootingLockedBody = "Assemble your pistol and complete the function check first (module 01).\nThen choose this module again, or talk to the sergeant at the range.";

        public const string ComingSoonLabel = "MODULE 02";
        public const string ComingSoonBody = "Aim and fire at targets on the range.\nThis module is being built and arrives in a future update.";
        public const string Back = "Back";

        // ---------- Weapon choice ----------
        public const string WeaponSelectTitle = "Choose a weapon to assemble";
        public const string ChangeWeapon = "Change weapon";
        public static string WeaponCardPill(int parts, int bestSeconds) => bestSeconds > 0
            ? $"{parts} PARTS  ·  BEST {bestSeconds / 60:00}:{bestSeconds % 60:00}"
            : $"{parts} PARTS  ·  NOT BUILT YET";

        // ---------- Briefing ----------
        public const string IntroLabel = "MODULE 01  ·  GUN ASSEMBLY";
        public const string IntroTitle = "How it works";
        public const string GripChip = "GRIP";
        public const string GripUse = "Squeeze to pick up and hold a part";
        public const string TriggerChip = "TRIGGER";
        public const string TriggerUse = "Pull to press buttons";
        public const string StickChip = "THUMBSTICK";
        public const string StickUse = "Left stick to walk, right stick to turn";
        public const string IntroHint = "Glowing parts and outlines always show you what to do next.\nMissed an instruction? Press B or Y to hear it again.";
        public const string StartTraining = "Start training";

        // ---------- Training ----------
        public const string Restart = "Restart";
        public const string Repeat = "Hear again";
        public static string StepLabel(int index, int count, string name) => $"STEP {index + 1} OF {count}  ·  {name.ToUpperInvariant()}";

        public static string PartName(GunPart part)
        {
            switch (part)
            {
                case GunPart.Barrel: return "barrel";
                case GunPart.RecoilSpring: return "recoil spring";
                case GunPart.UpperSlide: return "slide";
                case GunPart.Magazine: return "magazine";
                case GunPart.Bolt: return "bolt";
                case GunPart.TakedownPin: return "takedown pin";
                case GunPart.UpperReceiver: return "upper receiver";
                default: return "frame";
            }
        }

        public const string FunctionCheckName = "Function check";

        public static string PickUp(GunPart part) => $"Pick up the glowing {PartName(part)}";
        public const string PickUpFirstDetail = "Reach out and squeeze the GRIP button on your controller.";
        public const string PickUpDetail = "Squeeze GRIP to pick it up.";
        public static string HostHeldDetail(GunPart host) => HostHeldDetail(PartName(host));
        public static string HostHeldDetail(string host) => $"Good - keep holding the {host} in this hand.";

        // Parts are joined in the hands: the receiving part in one hand, the new part in the other.
        // A frame with parts already in it is "the pistol".
        public static string HostName(GunPart host, bool assembled) => host == GunPart.LowerFrame && assembled ? "pistol" : PartName(host);
        public static string PickUpHost(string host) => $"Pick up the {host} with one hand";
        public static string PickUpHostDetail(GunPart part, bool first) => first
            ? $"Squeeze GRIP to hold it. Then take the {PartName(part)} in your other hand."
            : $"Then take the {PartName(part)} in your other hand and join them.";
        public static string PickUpHostOtherHand(string host) => $"Now pick up the {host} with your other hand";
        public static string PickUpHostOtherHandDetail(GunPart part) => $"Keep holding the {PartName(part)}. Join the two parts in your hands, not on the table.";
        public static string PickUpOtherHand(GunPart part) => $"Now pick up the {PartName(part)} with your other hand";

        public static string Place(GunPart part)
        {
            switch (part)
            {
                case GunPart.Barrel: return "Put the barrel into the slide";
                case GunPart.RecoilSpring: return "Slide the spring in under the barrel";
                case GunPart.UpperSlide: return "Put the slide onto the frame";
                case GunPart.Magazine: return "Push the magazine into the grip";
                case GunPart.Bolt: return "Slide the bolt into the back of the receiver";
                case GunPart.TakedownPin: return "Push the pin through the front of the frame";
                case GunPart.UpperReceiver: return "Put the upper receiver onto the frame";
                default: return "Put it in place";
            }
        }

        // Same, for the weapon on the bench ('weaponId' = WeaponKit.id) where its wording differs.
        public static string Place(GunPart part, string weaponId)
        {
            if (weaponId == Tec9Id)
            {
                switch (part)
                {
                    case GunPart.RecoilSpring: return "Slide the spring unit in behind the bolt";
                    case GunPart.Magazine: return "Push the magazine up into the magazine well";
                }
            }
            return Place(part);
        }

        // ---------- TEC-9 ----------
        public const string Tec9Id = "tec9";
        public const string Tec9Name = "TEC-9";
        public const string Tec9HudSubtitle = "INTRATEC TEC-9 9MM  |  FIELD REASSEMBLY SEQUENCE";
        public const string PlaceDetail = "Bring your hands together and line it up with the glowing outline. It clicks in.";

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
        public const string TargetLabel = "JOIN HERE";

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

        // TEC-9: no safety, single action, a bolt with a charging knob on the side. Its check is stages 3..6.
        public static string StageTitle(int stage, string weaponId) =>
            weaponId == Tec9Id && stage == 3 ? "Pull the trigger all the way" : StageTitle(stage);
        public static string StageDetail(int stage, string safetyButton, string weaponId)
        {
            if (weaponId == Tec9Id)
            {
                switch (stage)
                {
                    case 3: return "The gun is cocked, so the hammer drops.";
                    case 4: return "With your other hand, grab the charging knob on the side, pull it back, then let go.";
                }
            }
            return StageDetail(stage, safetyButton);
        }
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

        // ---------- Intro scene (soldier briefing) ----------
        public const string SergeantName = "SGT. FORREST GUMP";

        // Fire range: the sergeant's offer.
        public const string RangeSection = "LIVE-FIRE TRAINING";
        public const string RangeOffer = "Ready for live-fire training, soldier? One magazine, fifteen rounds. Hit the targets as fast and as accurately as you can.";
        public const string RangeYes = "YES, START";
        public const string RangeNo = "NOT NOW";
        public const string RangeDesktopHint = "[ENTER] YES     [BACKSPACE] NOT NOW";
        public const string RangeDesktopHintNotReady = "[BACKSPACE] CLOSE";
        public const string RangeNotReady = "Assemble your pistol and complete the function check first. Then come and see me.";
        public const string RangeOk = "OK";
        // The booth panel during the fire training.
        public const string RangeStepMagazine = "Pick up the magazine from the counter.";
        public const string RangeStepLoad = "Pick up the pistol, push the magazine into the grip, then rack the slide.";
        public const string RangeStepRack = "Rack the slide to load the first round.";
        public const string RangeStepInsert = "Push the magazine up into the grip.";
        public const string RangeStepReady = "Safety off, then fire when ready. The clock starts with your first shot.";
        public const string RangeStepFiring = "Fire all 15 rounds. Farther targets and the centre rings score more.";
        public static string RangeStepFiringFor(int rounds) => $"Fire all {rounds} rounds. Farther targets and the centre rings score more.";
        // TEC-9: magazine well in front of the trigger guard, charging knob, no safety, no bolt hold-open.
        public const string RangeStepLoadTec9 = "Pick up the pistol, push the magazine into the well in front of the trigger guard, then pull the charging knob back.";
        public const string RangeStepRackTec9 = "Pull the charging knob all the way back and let go, to load the first round.";
        public const string RangeStepInsertTec9 = "Push the magazine up into the well in front of the trigger guard.";
        public const string RangeStepReadyTec9 = "No safety on this pistol. Fire when ready. The clock starts with your first shot.";
        public const string RangeStepUnlimitedTec9 = "Fire at will. Magazine empty? Drop it (B / Y, desktop M), load the fresh one from the counter, then pull the charging knob back - the bolt does not lock open.";
        public const string RangeStepDone = "CEASE FIRE - magazine empty.";
        public const string RangeEnd = "END TRAINING";
        public const string RangeRetry = "TRY AGAIN";
        public const string RangeFinish = "FINISH";
        public const string RangeBoothDesktopHint = "[ENTER] TRY AGAIN     [BACKSPACE] FINISH / END";
        // Range menu (weapon -> mode -> targets).
        public const string RangeMenuWeaponTitle = "Choose your weapon";
        public const string RangeMenuModeTitle = "Choose a mode";
        public const string RangeMenuTargetsTitle = "Choose your targets";
        public const string RangeMenuBack = "BACK";
        public const string RangeModeUnlimited = "Unlimited";
        public const string RangeModeUnlimitedBody = "No timer, endless ammo. Drop an empty magazine, load a fresh one.";
        public const string RangeModeUnlimitedPill = "FREE PRACTICE";
        public const string RangeModeTimed = "Timed";
        public static string RangeTimedCardBody(int magazine) => $"One magazine, {magazine} rounds, against the clock.";
        public const string RangeModeTimedPill = "SCORED";
        public const string RangeTargetsStationary = "Stationary";
        public const string RangeTargetsStationaryBody = "All three targets stand still, each in clear view.";
        public const string RangeTargetsMoving = "Moving";
        public const string RangeTargetsMovingBody = "The targets sweep side to side across their lanes.";
        public const string RangeTargetsPill = "TIMED";
        public const string RangeWeaponPistolBody = "9 mm semi-automatic service pistol.";
        public static string RangeWeaponMagazinePill(int magazine) => $"MAGAZINE  {magazine} ROUNDS";
        public const string RangeMenuDesktopHint = "[1] / [2] CHOOSE     [BACKSPACE] BACK";
        public const string RangeStepUnlimited = "Fire at will. Magazine empty? Drop it (B / Y, desktop M) and load the fresh one from the counter.";
        public const string RangeUnlimitedDesktopHint = "[BACKSPACE] END";
        public const string SergeantRole = "INSTRUCTOR";
        public const string SectionBriefing = "BRIEFING";
        public const string SectionControls = "CONTROLS";
        public const string SectionReady = "READY";
        public const string IntroWaiting = "...";
        public const string Next = "Next";
        public const string Skip = "Skip";
        public const string Replay = "Replay";
        public const string StartGame = "Start";
        public const string ControlsLabel = "CONTROLS";
        public const string ControlsTitle = "Your controllers";
        public const string ControlsFooter = "Try them now - each one lights up green when you press it.";
        public const string GripRowUse = "Pick up and hold parts";
        public const string TriggerRowUse = "Press buttons and fire";
        public const string SticksChip = "STICKS";
        public const string SticksRowUse = "Left walks, right turns";
        public const string PrimaryChip = "A / X";
        public const string PrimaryRowUse = "Safety on / off";
        public const string SecondaryChip = "B / Y";
        public const string SecondaryRowUse = "Hear it again";

        // ---------- Spoken (VRVoiceGuide) ----------
        // What the voice says. The recordings (Assets/Audio/Voice/VO_<line>.wav) are made from exactly these words;
        // after changing one, record or generate that clip again (see VR_GUIDANCE.md). One action per line, plain words,
        // commas where the speaker should take a breath.
        public static string Spoken(VoiceLine line)
        {
            switch (line)
            {
                case VoiceLine.Intro_ChooseModule: return "Welcome. To begin, point your controller at Gun Assembly, and pull the trigger.";
                case VoiceLine.Intro_Briefing: return "In this training, you will put a pistol together, and then check that it works. When you are ready, point at Start training, and pull the trigger.";
                case VoiceLine.Tec9_Briefing: return "In this training, you will put the TEC-9 together, from its bolt to its magazine, and then check that it works. When you are ready, point at Start training, and pull the trigger.";
                case VoiceLine.Common_WellDone: return "Well done.";
                case VoiceLine.Common_Good: return "Good.";

                case VoiceLine.Asm_Pick_Barrel: return "First, pick up the glowing slide with one hand. Squeeze the grip button on the side of your controller. Then, with your other hand, pick up the barrel.";
                case VoiceLine.Asm_Place_Barrel: return "Now bring your hands together, and put the barrel into the slide. Line it up with the glowing outline, and it will click into place.";
                case VoiceLine.Asm_Pick_Spring: return "Next, hold the slide in one hand, and pick up the recoil spring with the other.";
                case VoiceLine.Asm_Place_Spring: return "Bring them together, and slide the spring in, just under the barrel.";
                case VoiceLine.Asm_Pick_Slide: return "Next, pick up the frame with one hand, and the slide with the other.";
                case VoiceLine.Asm_Place_Slide: return "Now bring them together, and push the slide onto the frame, until it clicks.";
                case VoiceLine.Asm_Pick_Magazine: return "Next, hold the pistol by its grip in one hand, and pick up the magazine with the other.";
                case VoiceLine.Asm_Place_Magazine: return "Push the magazine up, into the grip, until it clicks.";
                case VoiceLine.Asm_TryAgain: return "Not quite. The part went back to the bench. Pick up the glowing part, and try again.";
                case VoiceLine.Asm_Complete: return "The pistol is assembled.";

                case VoiceLine.FC_Intro: return "Now, let's check that it works. Pick up the pistol by its grip.";
                case VoiceLine.FC_PickUpAgain: return "Pick the pistol back up by its grip, and we will carry on.";
                case VoiceLine.FC_SafetyOn_A: return "First, turn the safety on. Press the A button, on the hand holding the pistol.";
                case VoiceLine.FC_SafetyOn_X: return "First, turn the safety on. Press the X button, on the hand holding the pistol.";
                case VoiceLine.FC_TriggerDemo_Safe: return "The safety is on. Watch the glowing trigger. It moves all the way back, and then forward again.";
                case VoiceLine.FC_YourTurn_Trigger: return "Now you try. Pull the trigger all the way back.";
                case VoiceLine.FC_SafeOk: return "Good. The hammer did not move.";
                case VoiceLine.FC_LetGo: return "Now let go of the trigger.";
                case VoiceLine.FC_SafetyOff_A: return "Now turn the safety off. Press A again.";
                case VoiceLine.FC_SafetyOff_X: return "Now turn the safety off. Press X again.";
                case VoiceLine.FC_TriggerDemo_Fire: return "Watch the trigger once more. This time, the hammer will drop.";
                case VoiceLine.FC_HammerDropped: return "Click. The hammer dropped.";
                case VoiceLine.FC_KeepHolding: return "Keep holding the trigger back.";
                case VoiceLine.FC_SlideDemo: return "Now watch the glowing slide. It moves all the way back, then springs forward.";
                case VoiceLine.FC_YourTurn_Rack: return "With your other hand, grab the back of the slide. Pull it back, then let go.";
                case VoiceLine.FC_HoldWhileRacking: return "Keep the trigger pulled while you pull the slide. Let's try that again.";
                case VoiceLine.FC_ReleasedTooEarly: return "You let go of the trigger too early. Pull it, hold it, and pull the slide again.";
                case VoiceLine.FC_ReleaseDemo: return "Watch the trigger. It moves forward slowly, and stops at a click.";
                case VoiceLine.FC_YourTurn_Release: return "Now you try. Let the trigger out slowly, and stop when you hear the click.";
                case VoiceLine.FC_ResetClick: return "That click means the trigger has reset.";
                case VoiceLine.FC_TriggerDemo_Last: return "Watch the trigger one last time.";
                case VoiceLine.FC_YourTurn_Last: return "Now pull the trigger all the way, one last time.";
                case VoiceLine.FC_Passed: return "Well done. The pistol works correctly.";
                case VoiceLine.Training_Complete: return "Your training is complete. You can train again, or go back to the modules.";

                case VoiceLine.Sgt_Welcome: return "Welcome, recruit. I am Sergeant Forrest Gump, and I will be your instructor today.";
                case VoiceLine.Sgt_WhyItMatters: return "In the field, your weapon is your lifeline. A soldier who knows their weapon inside and out can clear a jam, fix a fault, and keep it working, even under pressure. That can save your life, and the lives of your team.";
                case VoiceLine.Sgt_Simulator: return "In this simulator, you will put a pistol together, part by part, and then check that it works. Practise as many times as you need, safely, until it becomes second nature.";
                case VoiceLine.Sgt_ControlsIntro: return "Before we begin, let's go over your controls. Look at the panel on your right.";
                case VoiceLine.Sgt_Grip: return "This is the grip button, on the side of each controller. Squeeze it to pick up, and hold, a part.";
                case VoiceLine.Sgt_Trigger: return "This is the trigger, under your index finger. Pull it to press buttons. On the pistol, it pulls the trigger.";
                case VoiceLine.Sgt_Thumbsticks: return "These are the thumbsticks. Push the left stick to walk, and the right stick to turn.";
                case VoiceLine.Sgt_PrimaryButton: return "This is the A button on the right controller, and X on the left. During the function check, it turns the safety on and off.";
                case VoiceLine.Sgt_SecondaryButton: return "And this is B, or Y. Press it at any time, to hear an instruction again.";
                case VoiceLine.Sgt_Ready: return "That's everything. When you are ready, point at Start, and pull the trigger. Good luck, recruit.";
                case VoiceLine.Rng_Offer: return "Soldier! Ready for some live-fire training?";
                case VoiceLine.Rng_NotReady: return "Assemble your pistol, and check that it works, first. Then come and see me.";
                case VoiceLine.Rng_Handover: return "Step up to the counter. Your pistol and magazine are in front of you. Pick up the magazine.";
                case VoiceLine.Rng_Magazine: return "This is the Beretta 92 FS, nine millimetre. Its magazine holds fifteen rounds, and it fires one round each time you pull the trigger. A trained shooter puts about forty aimed rounds a minute on target.";
                case VoiceLine.Rng_Load: return "Pick up the pistol. Push the magazine up into the grip, then rack the slide, to load the first round.";
                case VoiceLine.Rng_FireWhenReady: return "Safety off. The clock starts with your first shot. Farther targets, and the centre rings, score more. Fire when ready.";
                case VoiceLine.Rng_CeaseFire: return "Cease fire! Magazine empty. Let's see how you did.";
                case VoiceLine.Rng_Declined: return "Come back when you're ready, soldier.";
                case VoiceLine.Rng_Reload_B: return "Magazine empty! Press B to drop it. Then pick up a fresh, fully loaded magazine from the counter.";
                case VoiceLine.Rng_Reload_Y: return "Magazine empty! Press Y to drop it. Then pick up a fresh, fully loaded magazine from the counter.";
                case VoiceLine.Rng_Tec9_Magazine: return "This is the Intratec TEC-9, nine millimetre. Its magazine holds thirty-two rounds. Each pull of the trigger fires two rounds, and it kicks harder than the Beretta, so the second round climbs. Hold it firmly, and bring it back on target after every pull.";
                case VoiceLine.Rng_Tec9_Load: return "Pick up the pistol. Push the magazine up into the well, in front of the trigger guard. Then pull the charging knob all the way back, and let it go, to load the first round.";
                case VoiceLine.Rng_Tec9_FireWhenReady: return "This pistol has no safety, so keep your finger off the trigger until you are on target. The clock starts with your first shot. Farther targets, and the centre rings, score more. Fire when ready.";
                case VoiceLine.Rng_Tec9_Reload_B: return "Magazine empty! The bolt does not lock open on this pistol. Press B to drop the magazine, push in a fresh one from the counter, then pull the charging knob back to load.";
                case VoiceLine.Rng_Tec9_Reload_Y: return "Magazine empty! The bolt does not lock open on this pistol. Press Y to drop the magazine, push in a fresh one from the counter, then pull the charging knob back to load.";
                case VoiceLine.Intro_ChooseWeapon: return "Choose the weapon you want to put together. Point at its card, and pull the trigger.";
                case VoiceLine.Tec9_Pick_Bolt: return "First, pick up the glowing upper receiver with one hand. Then, with your other hand, pick up the bolt.";
                case VoiceLine.Tec9_Place_Bolt: return "Now bring your hands together, and slide the bolt into the back of the receiver, until it clicks.";
                case VoiceLine.Tec9_Pick_Spring: return "Next, hold the receiver in one hand, and pick up the recoil spring unit with the other.";
                case VoiceLine.Tec9_Place_Spring: return "Push the spring unit into the back of the receiver, behind the bolt.";
                case VoiceLine.Tec9_Pick_Upper: return "Next, pick up the frame with one hand, and the upper receiver with the other.";
                case VoiceLine.Tec9_Place_Upper: return "Now set the upper receiver down onto the frame, until it clicks.";
                case VoiceLine.Tec9_Pick_Pin: return "Next, hold the pistol in one hand, and pick up the small takedown pin with the other.";
                case VoiceLine.Tec9_Place_Pin: return "Push the pin through the front of the frame. It locks the two halves together.";
                case VoiceLine.Tec9_Pick_Magazine: return "Next, hold the pistol by its grip in one hand, and pick up the magazine with the other.";
                case VoiceLine.Tec9_Place_Magazine: return "Push the magazine up, into the magazine well, in front of the trigger guard, until it clicks.";
                case VoiceLine.Tec9_FC_TriggerDemo_First: return "This pistol has no safety, and it is already cocked. Watch the trigger. It moves all the way back, and the hammer drops.";
                case VoiceLine.Tec9_FC_BoltDemo: return "Now watch the glowing bolt. It moves all the way back, then springs forward.";
                case VoiceLine.Tec9_FC_YourTurn_Rack: return "With your other hand, grab the charging knob on the side. Pull it back, then let go.";
                case VoiceLine.Tec9_FC_HoldWhileRacking: return "Keep the trigger pulled while you pull the charging knob. Let's try that again.";
                case VoiceLine.Tec9_FC_ReleasedTooEarly: return "You let go of the trigger too early. Pull it, hold it, and pull the charging knob again.";
                default: return "";
            }
        }

        static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
