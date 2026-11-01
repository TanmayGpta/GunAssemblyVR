# GunAssemblyVR – Version 1 (Meta Quest 3S)

Version 1 turns the desktop prototype into a standalone Meta Quest 3S app **without replacing it**.
The same scene, assembly sequence, sockets, HUD and function check run in both modes:

| Mode | When | Player |
|---|---|---|
| Desktop | Play in the Editor, or a Windows build (no headset running) | `Armorer_Player` (WASD / mouse), unchanged |
| VR | OpenXR is running (the Quest APK, or Quest Link with a loader enabled) | XR Origin from XRI Starter Assets |

`VR_Version1/VRPlatformSwitch` decides at startup. Everything VR lives under `VR_Version1/VR_Content`,
which is **saved inactive**, so desktop mode never touches it.

## Version 1.1 – guided VR experience

Version 1.1 (branch `feature/vr-version1-1-ux`) keeps all of the above and changes only what the VR trainee sees and feels.
Desktop mode is unchanged.

| Area | What changed |
|---|---|
| One panel | `VR_Training_Menu` is now the single panel, straight ahead behind the bench. In VR the desktop `Tactical_HUD_Canvas` is not drawn (its `Canvas` is disabled at runtime; `AssemblyHUD` still runs). The old left-hand panel is gone. |
| Screens | Modules (01 Gun Assembly / 02 Shooting Training – Coming Soon) → Briefing (controls) → Training (step, progress bar, one instruction, one feedback line) → Complete (time, mistakes, Train again / Back to modules). |
| Guidance (`VRGuidance`) | The next part pulses and has a floating marker; once picked up, a line leads to the glowing outline; direction hints ("Move it to the left", "Turn it clockwise", …) come from the real position / rotation error in the trainee's view; a curved arrow shows rotation. Function check: marker on the pistol, tooltip beside the hand holding it ("Press A", "Pull trigger"), arrow on the slide for racking. |
| Placement assist (`VRPlacementAssist`) | XRI grab transformer added at runtime to each part. Within 15 cm of the part's own **unlocked** socket the part is pulled smoothly toward the seat (rotation only when already within 45°). When `SnapSocket` reports Valid (unchanged 6 cm / 20°) for 0.25 s, the hands let go and the normal release path seats the part. Wrong / out-of-sequence parts are never pulled. |
| Feedback | Seat: chime + panel "Barrel in place!" + green ring. Rejection: plain-language reason on the panel. Misaligned correct part: calm teal ghost instead of amber (VR only). |
| Text | Every sentence the trainee reads is in `VRTrainingText`. |

Small additive API changes (desktop behaviour unchanged): `AssemblyPart.SetHighlight(float)`, `SnapSocket.amberWhenMisaligned` / `faintAlphaBoost` / `showInsertionPath`, `FunctionCheck.Stage` / `SafeTriggerPulled` / `Notified`, `ProceduralAudio.MakeChime`.

## Technical configuration

| Item | Value |
|---|---|
| Unity | 2022.3.62f3, Built-in Render Pipeline (kept) |
| XR packages | OpenXR 1.14.3, XR Interaction Toolkit 2.6.4, XR Management 4.4.0, Input System 1.14.0 (all Unity-verified for 2022.3.62f3) |
| Active Input Handling | Both (legacy `Input.*` for the desktop scripts, Input System for XR) |
| OpenXR (Android) | Meta Quest Support (targets include Quest 3S), Oculus Touch + Meta Quest Touch Plus profiles, Single Pass Instanced, 16-bit depth |
| OpenXR (Standalone) | no loader, so desktop mode stays exactly as before |
| Android | package `com.vrarmory.gunassemblyvr`, min API 29, ARM64 only, IL2CPP, OpenGL ES 3, Linear, Landscape Left, ASTC |
| Quality (Android = "Medium") | 2 pixel lights, 4x MSAA, hard shadows, 8 m shadow distance, 1 cascade. Desktop uses "Ultra" and is unchanged |
| Build Settings | `Assets/Scenes/SampleScene.unity` (was empty) |

## Scene hierarchy added by Version 1

```
VR_Version1                      VRPlatformSwitch (desktop or VR)
├── Assembly_Feedback_Audio      AssemblyFeedbackAudio (seat clack / reject buzz, both modes)
└── VR_Content  (inactive)
    ├── XR Interaction Setup     XRI Starter Assets prefab: XR Interaction Manager, EventSystem + XRUIInputModule, XR Origin
    ├── Teleport_Floor           invisible TeleportationArea over the armory floor
    ├── Bench_Teleport_Anchor    green disc: teleport here to stand at the bench
    ├── VR_Training_Menu         the one world-space panel in front of the bench (modules / briefing / training / complete)
    ├── VR_Guidance              in-world hints + adds VRPlacementAssist to the parts at runtime (v1.1)
    ├── VR_Function_Check_Controls
    └── Quest_Performance        turns off shadows on all lights except the bench lamp and sun (VR only)
```

Each of the five gun parts (`ArmorerStation/Staged_Gun_Parts/0x_...`) also got `XRGeneralGrabTransformer` and
`VRAssemblyGrab`, saved **disabled**. No existing object was moved, renamed or deleted.

## Scripts

New (`Assets/Scripts/VR/`):

| Script | Job |
|---|---|
| `VRPlatformSwitch` | Desktop or VR at startup; enables the part grabs in VR |
| `VRAssemblyGrab` | `XRGrabInteractable` for one part. Calls `AssemblyPart.BeginGrab` / `EndGrab`, so all validation stays in `SnapSocket`. Blocks grabbing seated or returning parts, allows a second hand only on the frame and slide, adds highlight and vibration |
| `VRFunctionCheckControls` | Step 05 in VR: index trigger → `SetTrigger`, A/X → `SetSafety`, other hand pulls the slide grip → `SetSlide` |
| `VRTrainingMenu` | The one VR panel: module selection, Coming Soon, briefing, training instructions, completion. Restart / Train again = scene reload; Back to modules does not reload |
| `VRGuidance` (1.1) | Highlight, marker, target line, direction / rotation hints, function-check tooltips, success rings |
| `VRPlacementAssist` (1.1) | Magnetic pull near the active socket + click-in when Valid (validation stays in `SnapSocket`) |
| `VRTrainingText` (1.1) | All trainee-facing sentences |
| `QuestPerformanceSettings` | Shadow budget for Quest |

New (`Assets/Scripts/Assembly/`): `AssemblyFeedbackAudio`, `ProceduralAudio` (synthesized sounds, moved out of `FunctionCheck`).

Changed teammate scripts (small, behaviour-preserving):

| Script | Change |
|---|---|
| `FunctionCheck` | Animation no longer needs the desktop cursor lock (G-04); animates `Ambi_Safety_Lever` about its axle instead of `Safety_Axle` (G-05); releasing the trigger before the slide no longer dead-ends check 6 (G-06); `desktopInput` flag and control names in the HUD text; `Begin` cannot rebuild the rig twice; stops writing to the HUD after it has passed |
| `GunAssemblyManager` | "STEP COMPLETE" stays readable for 1.5 s (G-07); ignores duplicate completion; counts rejections and shows the reason; `beginOnStart` / `BeginTraining()` for the VR intro screen; events `StepStarted`, `AllStepsCompleted`, `PartRejected`; directive text only rebuilt when it changes |
| `SnapSocket` | Evaluates every held part, not only the first (two hands); `Rejected` event with a reason; a later-step part says "OUT OF SEQUENCE" instead of "WRONG PART" (G-08) |
| `AssemblyPart` | Seat animation follows a host held in the other hand; `BeginGrab(hideFromAimRay = true)` (VR passes `false` so the other hand can still touch a held part) |
| `AssemblyHUD` | Skips unchanged text updates; `DirectiveText` getter |
| Editor builders | Every builder asks for confirmation; "Setup Workbench and Gun Parts" no longer deletes sockets or re-stages already staged parts (G-03, G-10) |

## Editor tools (`Tools > VR Armory > VR`)

1. **Configure Project for Quest 3S** – all settings in the table above. Safe to re-run.
2. **Setup VR Scene (additive)** – creates or updates `VR_Version1`. Safe to re-run.
3. **Build Quest APK** – `Builds/Android/GunAssemblyVR_v1_1.apk`.
- **Run Self-Test (Desktop Mode)** / **(VR Rig, Simulated Controllers)** – automated Play Mode tests; results in the Console as `SELFTEST|...` lines.

Order of the older builders on a fresh scene (each now asks first):
Setup Workbench and Gun Parts → Build Armory Set Dressing → Build Live-Fire Range → Setup Phase 02 Assembly → Re-Stage Gun Parts → VR / Setup VR Scene.
**On the current scene none of them needs to be run.**

## VR controls

| Control | Action |
|---|---|
| Grip (middle finger) | Pick up / hold a part. Either hand |
| Bring the part to the glowing outline | It is guided in and clicks into place (or release grip when it turns green) |
| Both grips on frame or slide | Two-handed hold |
| Thumbstick forward, release | Teleport (aim at the floor or the green bench disc) |
| Thumbstick left / right | Snap turn |
| Index trigger at a button | Press UI |
| Function check: index trigger (hand holding the pistol) | Squeeze the trigger (analog) |
| Function check: A / X (same hand) | Safety on / off |
| Function check: other hand grips the rear of the slide, pulls back, lets go | Rack the slide |

## Continuing development (teammate notes)

- **Keep assembly logic input-agnostic.** New interactions should call the existing public API (`AssemblyPart.BeginGrab/EndGrab`, `FunctionCheck.Set*`, `GunAssemblyManager.BeginTraining`) rather than reading controllers inside the assembly scripts.
- **New VR objects go under `VR_Content`**, so desktop mode stays untouched. Shared objects go directly under `VR_Version1`.
- **A new grabbable part:** add `AssemblyPart`, `XRGeneralGrabTransformer` (scaling off) and `VRAssemblyGrab` (disabled), then add it to `VRPlatformSwitch.partGrabs`. Easiest: re-run *Setup VR Scene*, which does this for every `AssemblyPart`.
- **A new step:** add it to `Gun_Assembly_Manager.steps` (socket or action step); the HUD, the VR menu progress and the sounds pick it up automatically.
- **Shooting range (Phase 05+):** a VR pistol can reuse `VRFunctionCheckControls`' trigger reading; `RangeTarget.RegisterHit` is ready to be called.
  The module card already exists (`VRTrainingMenu.shootingModuleButton`, currently opening the Coming Soon screen).
- **New trainee text:** add it to `VRTrainingText`, not inline in a script.
- **Scene merges:** `SampleScene.unity` is one file; agree who edits it, or put new modules in prefabs or additive scenes.
- Run both self-tests before committing; they must end with `summary|PASS`.

## Known limitations

- Tested automatically in the Editor with simulated controllers. Real-headset feel (grab comfort, readability, frame rate) still needs testing on the Quest 3S.
- Hand tracking is not included (controllers only).
- Ray grabs pull the part to the hand (XRI "force grab"); a two-handed grab is done with near grabs.
- The shooting range is still scenery only (as in the prototype); in VR it is announced as "Coming Soon".
- Version 1.1 UX (guidance, assist, module menu) is VR-only; the desktop prototype keeps its original HUD.
