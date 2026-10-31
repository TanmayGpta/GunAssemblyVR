# GunAssemblyVR – Version 1 (Meta Quest 3S)

Version 1 turns the desktop prototype into a standalone Meta Quest 3S app **without replacing it**.
The same scene, assembly sequence, sockets, HUD and function check run in both modes:

| Mode | When | Player |
|---|---|---|
| Desktop | Play in the Editor, or a Windows build (no headset running) | `Armorer_Player` (WASD / mouse), unchanged |
| VR | OpenXR is running (the Quest APK, or Quest Link with a loader enabled) | XR Origin from XRI Starter Assets |

`VR_Version1/VRPlatformSwitch` decides at startup. Everything VR lives under `VR_Version1/VR_Content`,
which is **saved inactive**, so desktop mode never touches it.

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
    ├── VR_Training_Menu         world-space panel left of the bench (intro / progress / completion)
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
| `VRTrainingMenu` | Intro screen, timer / mistakes / tips, completion screen, Restart (scene reload) and Exit |
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
3. **Build Quest APK** – `Builds/Android/GunAssemblyVR_v1.apk`.
- **Run Self-Test (Desktop Mode)** / **(VR Rig, Simulated Controllers)** – automated Play Mode tests; results in the Console as `SELFTEST|...` lines.

Order of the older builders on a fresh scene (each now asks first):
Setup Workbench and Gun Parts → Build Armory Set Dressing → Build Live-Fire Range → Setup Phase 02 Assembly → Re-Stage Gun Parts → VR / Setup VR Scene.
**On the current scene none of them needs to be run.**

## VR controls

| Control | Action |
|---|---|
| Grip (middle finger) | Pick up / hold a part. Either hand |
| Release grip at the green ghost | Seat the part |
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
- **Scene merges:** `SampleScene.unity` is one file; agree who edits it, or put new modules in prefabs or additive scenes.
- Run both self-tests before committing; they must end with `summary|PASS`.

## Known limitations

- Tested automatically in the Editor with simulated controllers. Real-headset feel (grab comfort, readability, frame rate) still needs testing on the Quest 3S.
- Hand tracking is not included (controllers only).
- Ray grabs pull the part to the hand (XRI "force grab"); a two-handed grab is done with near grabs.
- The shooting range is still scenery only (as in the prototype).
