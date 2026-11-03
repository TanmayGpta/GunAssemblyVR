# GunAssemblyVR – Version 1.2: Ghost Demonstrations & Voice Guidance

Version 1.2 (branch `feature/vr-ghost-animation-voice-guidance`) adds a spoken instructor and see-through
"ghost" demonstrations to the VR training. **Desktop mode is unchanged**: everything below lives under
`VR_Version1/VR_Content` and only runs when a headset is in use.

The trainee hears one short instruction at a time. In the function check, a glowing copy of the slide or trigger
first shows the movement – which part, which way, how far – and only then is the trainee asked to try.
The real action is still checked by `FunctionCheck` / `SnapSocket` exactly as before: a demonstration never moves
a real part and never completes a step.

## What the trainee experiences

| Moment | Voice (file `VO_<name>.wav`) | Ghost |
|---|---|---|
| App starts, module screen | `Intro_ChooseModule` (once per run) | – |
| Briefing screen | `Intro_Briefing` | – |
| Step starts | `Asm_Pick_<Part>` ("First, pick up the glowing barrel…") | – |
| Right part picked up (first time in the step) | `Asm_Place_<Part>` | – |
| Part seated | `Common_WellDone`, then the next step's `Asm_Pick_…` | – |
| Placement rejected | `Asm_TryAgain` (at most every 4 s) | – |
| Last part seated | `Asm_Complete`, then `FC_Intro` when the function check starts | – |
| Check 1/7 safety on | `FC_SafetyOn_A` or `FC_SafetyOn_X` (the hand holding the pistol) | – |
| Check 2/7 trigger on SAFE | `FC_TriggerDemo_Safe` → `FC_YourTurn_Trigger` | trigger pulled and released, 2× |
| Trigger pulled on SAFE | `FC_SafeOk`, `FC_LetGo` | – |
| Check 3/7 safety off | `FC_SafetyOff_A` / `_X` | – |
| Check 4/7 double action | `FC_TriggerDemo_Fire` → `FC_YourTurn_Trigger` | trigger, 1× |
| Check 5/7 rack | `FC_HammerDropped`, `FC_KeepHolding`, `FC_SlideDemo` → `FC_YourTurn_Rack` | slide back and forward, 2× |
| Rack mistake | `FC_HoldWhileRacking` or `FC_ReleasedTooEarly` | stops |
| Check 6/7 reset | `Common_Good`, `FC_ReleaseDemo` → `FC_YourTurn_Release` | trigger let out slowly to the reset point |
| Check 7/7 single action | `FC_ResetClick`, `FC_TriggerDemo_Last` → `FC_YourTurn_Last` | trigger, 1× |
| Function check passed | `FC_Passed` | – |
| Completion screen | `Training_Complete` | – |
| Pistol put down during the check | `FC_PickUpAgain`; picking it up repeats the current instruction | stops |

`→` means "after the demonstration and its line have both finished". The full word-for-word script is
[`Tools/VoiceGeneration/voice_script.tsv`](Tools/VoiceGeneration/voice_script.tsv) (exported from `VRTrainingText.Spoken`).

**Hearing an instruction again:** press **B or Y** on either controller, or point at **Hear again** on the panel.
This repeats the current instruction, including its demonstration.
**If the trainee is stuck:** after 16 s without progress (voice quiet, no demonstration, trainee not acting), the
instruction is given again, at most twice per step.

## Scripts

All in `Assets/Scripts/VR/` unless noted.

| Script | Job |
|---|---|
| `VRTrainingCoach` | The instructor. Reads the training state, decides what to say and which demonstration to show, and keeps them in step. Handles Hear again (panel + B/Y), reminders, mistakes, the pistol being put down. Never completes anything. |
| `VRVoiceGuide` | Plays the recorded lines from one 2D `AudioSource`: one at a time, never overlapping, cut-off instructions fade out. Also defines the `VoiceLine` list. |
| `VRGhostDemo` | Builds the slide and trigger ghosts (once each) and plays a demonstration: `Play(Demo, cycles)`, `Stop()`. |
| `GhostVisual` | Empty marker on each ghost. Scripts that measure or highlight real parts (`VRGuidance`) skip anything under it. |
| `VRTrainingText.Spoken` | The words of every line (all trainee-facing text stays in `VRTrainingText`). |
| `Assets/Shaders/GhostDemo.shader` | The ghost look (see below). Material: `Assets/Materials/VR/VR_Ghost_Demo.mat`. |
| `Assets/Editor/VR/VRVoiceSetup.cs` | Import settings for voice clips, finds each line's clip by file name, *Export Voice Script*. |
| `Tools/VoiceGeneration/generate_voice.py` | Offline generator for the clips (see *Voice assets*). Not part of the app. |

Small additive changes to existing scripts (behaviour unchanged, both self-tests confirm it):

| Script | Change |
|---|---|
| `FunctionCheck` | Read-only pose API for demonstrations: `HasRig`, `SlidePiece`, `TriggerPiece`, `SlideLocalPosition(amount)`, `TriggerLocalPose(pull, …)`; `TriggerResetPoint` constant replaces the `0.3f` literal it already used. |
| `VRFunctionCheckControls` | `RackGripHeld` (read-only). |
| `VRGuidance` | Skips `GhostVisual` renderers when measuring the pistol. |
| `VRTrainingMenu` | `repeatButton` field ("Hear again"). |
| `VRTrainingText` | `Spoken(line)`, `Repeat`, the briefing hint now mentions B / Y. |
| `VRSceneSetup`, `VRBuild` | Create the new objects / material; APK name `GunAssemblyVR_v1_2.apk`. |

## How the ghost demonstrations work

**Where the movement comes from.** The ghost never uses numbers of its own. `FunctionCheck` already knows the
real motion: the slide slides `slideTravel` (4.5 cm) along the gun's rear direction, the trigger turns
`triggerPull × triggerPullAngle` (18°) about a pivot it measured from the model. Its new read-only methods return
those poses for any travel without moving anything:

```
ghost slide   localPosition = functionCheck.SlideLocalPosition(travel)        // travel 0 = rest, 1 = fully back
ghost trigger localPose     = functionCheck.TriggerLocalPose(travel, out p, out r)
```

So the ghost stops exactly where the real part will (the VR self-test measures this: 0.00 mm / 0.00° for the
trigger, 45.0 mm travel for the slide). If the gun model, `slideTravel` or `triggerPullAngle` change, the ghosts follow.

**Building a ghost (once).** The first time a demonstration is needed, `VRGhostDemo` copies the part's meshes into
a new object next to the real part (a child of the frame, so it follows the pistol in the hand):
only `MeshFilter` + `MeshRenderer`, shadows off, on the *Ignore Raycast* layer, with a `GhostVisual` marker.
No collider, no `AssemblyPart`, no XR component – it cannot be grabbed, hit by a ray or seated. For the slide only
the slide's own six meshes are copied (`Slide_Body`, `Rear_Sight`, `Ambi_Safety_Lever`, `Safety_Axle` and the two
`Static_Detail` fills); the barrel and spring seated inside it stay out.

**A demonstration** is a list of short moves played with smooth easing:

| Demo | Moves |
|---|---|
| `SlideRack` | glow on the real slide (0.6 s) → back to full travel (1.1 s) → hold (0.7 s) → spring forward (0.45 s) → rest (0.45 s) |
| `TriggerPull` | glow (0.6 s) → pull to full (0.9 s) → hold (0.7 s) → spring back (0.55 s) → rest (0.45 s) |
| `TriggerRelease` | glow on the trainee's pulled trigger (0.6 s) → let out slowly to `TriggerResetPoint` (1.8 s) → hold at the click |

It fades in and out (0.35 s), gives a short brighter "flash" when it reaches the end of a movement, and the trigger
also shows a faint line along the path of its tip. All timings are Inspector fields on `VR_Ghost_Demo`.

**The look (`VRArmory/GhostDemo`).** Teal like the rest of the guidance, additive and rim-lit (edges glow, faces
stay faint), pushed out 0.6 mm along the normals so at rest it reads as a glowing shell around the real part.
A second, fainter pass shows the parts of the ghost hidden behind the hand or controller, so the trigger
demonstration is never lost. Unlit, untextured, no depth writes, single-pass-instanced: cheap on Quest.

**When it plays (`VRTrainingCoach`).**
- It starts in the same frame as the first word of its "watch" line, so voice and picture always begin together.
- "Now you try" is spoken only after **both** the line and the ghost have finished.
- It stops (fades out in 0.18 s) when the trainee starts the action (trigger moved 15 % from where it was, rack grip
  grabbed, or the slide moved), when the check moves on, when the pistol is put down, or on a rack mistake.
- It never restarts by itself every frame: a cue is only entered when the situation changes.

## How the voice guidance works

**`VoiceLine`** (in `VRVoiceGuide.cs`) lists every line. Line `X` is played from
`Assets/Audio/Voice/VO_X.wav`; its words are `VRTrainingText.Spoken(VoiceLine.X)`.

**Two kinds of line.** *Confirmations* (`Common_WellDone`, `Common_Good`, `Asm_Complete`, `FC_SafeOk`,
`FC_HammerDropped`, `FC_ResetClick`, `FC_Passed` – see `VRVoiceGuide.LetsFinish`) are always heard whole and in order.
*Instructions* are replaced when something newer happens: the one playing fades out over 0.15 s, the queued ones are
dropped. So a fast trainee is never told to do what they have already done, but still hears "Well done."

**No overlaps.** One `AudioSource`, `Play()` replaces rather than adds, and a 0.55 s pause separates two lines.
The source is 2D, priority 0, on its own object: the click, chime and gun sounds use their own sources and are not
affected. The VR self-test checks every `AudioSource` in the scene each frame: never more than one voice clip.

**Missing clip.** If a line has no clip (or `muted` is ticked), it is "spoken" silently for about as long as it
would take (≈150 words per minute) and one warning is logged, so the training and demonstrations keep their rhythm.

**`VRTrainingCoach` cues.** The coach turns the state into one *cue* (`PickUpPart`, `SafeTrigger`, `Rack`, …) each
frame, from the panel screen, `GunAssemblyManager.CurrentStep`, `VRGuidance.CurrentPhase` and `FunctionCheck.Stage`.
Only a change of cue (or of step) speaks; per-step instructions that were already given are not repeated when the
trainee drops and picks up the same part.

## Inspector configuration

`Tools > VR Armory > VR > 2. Setup VR Scene (additive)` creates and wires everything; nothing needs to be set by hand.

| Object (under `VR_Version1/VR_Content`) | Component | Notable fields |
|---|---|---|
| `VR_Ghost_Demo` | `VRGhostDemo` | `functionCheck`, `ghostMaterial` = VR_Ghost_Demo, `lineMaterial` = VR_Guide_Line, colour, all timings |
| `VR_Voice_Guide` | `VRVoiceGuide` + `AudioSource` | `clips` (one per line, filled from `Assets/Audio/Voice`), `volume`, `gap`, `fadeOutTime`, `muted` |
| `VR_Training_Coach` | `VRTrainingCoach` | references; `reminderDelay` (16 s), `maxReminders` (2), `promptDelay` (0.4 s), `demoRepeats` (2), `actingThreshold` (0.15) |
| `VR_Training_Menu` | `VRTrainingMenu` | `repeatButton` (Hear again) |

## Voice assets

All 38 lines are recorded and in the project (`Assets/Audio/Voice/VO_*.wav`, 24 kHz mono, about 2.6 words per
second). They were generated **offline** with the open Kokoro-82M text-to-speech model (Apache-2.0, voice
`af_heart`, speed 0.9), one sentence at a time with a 0.5 s pause between sentences, every line normalised to the
same loudness (-20 dB RMS, soft-limited peaks). The app plays them as ordinary audio files: there is no
text-to-speech and no internet use at runtime. Every clip was checked by transcribing it back with an offline
speech recogniser (Whisper `small.en`): all 38 match their script word for word.

Unity imports every file in `Assets/Audio/Voice/` as mono Vorbis, *Compressed In Memory*, preloaded (set
automatically by `VRVoiceSetup`).

### Replace or add a voice line

1. Change or add the words in `VRTrainingText.Spoken` (a new line also needs a name in the `VoiceLine` enum).
   Keep it to one action, plain words, commas where the speaker should breathe.
2. Use it in `VRTrainingCoach.Speak` (new line), or nothing to do (changed words).
3. `Tools > VR Armory > VR > Export Voice Script` – updates `Tools/VoiceGeneration/voice_script.tsv`.
4. Make the clip, either way:
   - **Generate:** `python Tools/VoiceGeneration/generate_voice.py --model-dir <kokoro folder> --only <LineName>`
     (setup instructions are at the top of the script), or
   - **Record a person:** save `Assets/Audio/Voice/VO_<LineName>.wav` (mono, a little silence at both ends).
     If one line is recorded by a person, record them all, so the instructor stays one voice.
5. `Tools > VR Armory > VR > 2. Setup VR Scene (additive)` assigns the clip; save the scene.
6. Run the VR self-test: `v12-every-voice-line-recorded` and `v12-recordings-match-script` fail if a clip is missing
   or the words changed after recording.

### Add another ghost demonstration

Example: show the safety lever turning.
1. `FunctionCheck`: add a read-only pose method for the piece, like `TriggerLocalPose` (the lever is already a
   `Piece`, so `lever.PoseAt(angle, …)` does the maths).
2. `VRGhostDemo`: add a value to `Demo`, build its ghost in `GhostFor` with `BuildGhost(piece, owner)`, give it its
   moves in `BuildMoves`, and set its pose in `ApplyPose`.
3. `VRTrainingCoach.Speak`: for the cue, call `Demonstrate(Demo.X, cycles, watchLine, tryLine, …)`, and say in
   `TraineeActing` what counts as the trainee starting the action.
4. Add the two voice lines (above) and a self-test check.

## Tests

`Tools > VR Armory > VR > Run Self-Test …` (or the command lines in `VRSelfTestRunner.cs`). Results with this branch:

| Run | Result |
|---|---|
| Desktop | 32/32 PASS (31 before + `v12-desktop-no-voice-or-ghosts`) |
| VR, simulated controllers | 118/118 PASS (82 before + 36 `v12-…` checks) |

The VR checks drive the real rig: they verify that each line is spoken at the right moment and in the right order,
that the ghost starts with its line, cannot be grabbed and moves no real part, that its end poses match the real
trigger and slide exactly, that a trainee's action or putting the pistol down stops it, that Hear again / B replay
it, that the voice never overlaps, and that a missing clip is handled. The run now takes about two minutes because
it waits for real speech.

## Known limitations

- Verified in the Editor with simulated controllers only. **Not yet tried on a Quest 3S**: comfort of the voice
  volume, readability of the ghost in the headset and the pacing still need a real trainee.
- The voice is synthetic (a natural-sounding model, not a person). It can be replaced line by line with recordings.
- English only.
- The ghost shows the slide and trigger; the safety lever and the hammer have no demonstration (the voice and the
  controller tooltip explain the A / X button).
