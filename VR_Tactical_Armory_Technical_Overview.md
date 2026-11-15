# VR Tactical Armory: Weapon Assembly & Live-Fire Qualification
## Comprehensive Technical Architecture & Systems Reference

---

## 1. Executive Summary & Tech Stack

**VR Tactical Armory** is an immersive virtual reality qualification and weapon assembly simulation developed for the **Meta Quest 3S / Quest 2 / Quest 3** standalone headsets and PC VR. The simulation models the full mechanical field-strip disassembly, guided reassembly, seven-stage function check, and live-fire range qualification of the **Beretta 92 FS / M9** service pistol.

### Platform & Engine Specifications
* **Engine:** Unity 2022.3.62f3 LTS (C# .NET Standard 2.1)
* **XR Architecture:** OpenXR Plugin 1.10+ (`XR_EXT_hand_tracking`, `XR_FB_hand_tracking_aim`)
* **XR Framework:** Unity XR Interaction Toolkit (XRI) 2.6.4
* **Hand Tracking API:** Unity XR Hands 1.4.1 (`com.unity.xr.hands`)
* **Input System:** Unity New Input System 1.7+ (`UnityEngine.InputSystem`)
* **Render Pipeline:** Built-in Render Pipeline (BIRP) with Single-Pass Instanced Stereo
* **Target Hardware:** Meta Quest 3S (Standalone Android ARM64 / Meta Horizon Link)

---

## 2. Core Architectural Systems & Unity APIs

### System 1: Adaptive UI/Interactable Pointer Dimmer
* **Primary Script:** `Assets/Scripts/VR/VRRayHoverDimmer.cs`
* **Unity Libraries:** 
  * `UnityEngine.XR.Interaction.Toolkit` (`XRRayInteractor`, `XRInteractorLineVisual`)
  * `UnityEngine` (`Gradient`, `GradientColorKey`, `GradientAlphaKey`, `Mathf.MoveTowards`)

#### How It Works:
Standard VR laser lines can be visually intrusive and clutter the player's field of view. The `VRRayHoverDimmer` dynamically modulates ray opacity based on contextual gaze and aiming:
1. **Context Polling (`LateUpdate`):**
   * Calls `XRRayInteractor.TryGetCurrentUIRaycastResult(out _)` to detect intersections with World Space Canvases (`TrackedDeviceGraphicRaycaster`).
   * Evaluates `m_Ray.interactablesHovered.Count > 0` and `m_Ray.hasSelection` to detect hover over physical gun components.
2. **Smooth Alpha Interpolation:**
   * If aiming at valid UI or gun parts, the target opacity transitions to `1.0` (100% white).
   * In open space, the target drops to `0.08` (8% opacity, faint guide).
   * Alpha smoothly animates using `Mathf.MoveTowards()` over a `0.12s` window.
3. **Gradient Construction:**
   * Dynamically constructs a 2-key `Gradient` where the origin retains base alpha and the tip tapers to 25% of origin alpha, assigning directly to `validColorGradient` and `invalidColorGradient`.

---

### System 2: Dual Input Modality (Seamless Hand <-> Controller Switching)
* **Primary Scripts:** `Assets/Scripts/VR/VRHands.cs`, `Assets/Editor/VR/VRSceneSetup.cs`
* **Unity Libraries:** 
  * `UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager`
  * `UnityEngine.XR.Hands` (`XRHandSubsystem`, `SubsystemManager`)

#### How It Works:
1. **Modality Polling:**
   * `XRInputModalityManager` monitors subsystem events `trackingAcquired` and `trackingLost`.
   * When Touch controllers are active, it enables `m_LeftController` / `m_RightController` and disables `m_LeftHand` / `m_RightHand`.
   * When controllers are set on the table, Quest exterior cameras acquire optical tracking, activating `m_LeftHand` and `m_RightHand`.
2. **Central Interactor Querying (`VRHands.cs`):**
   * Acts as a singleton provider answering `IsTrackedHand(IXRInteractor)` and `IsLeft(IXRInteractor)` to allow gameplay scripts to dynamically adapt interaction logic without hardcoded tags.

---

### System 3: Optical Bare-Hand Physics & Natural Grasping
* **Primary Scripts:** `Assets/Scripts/VR/VRHandFistGrab.cs`, `Assets/Scripts/VR/VRHandTeleport.cs`
* **Unity Libraries:**
  * `UnityEngine.XR.Hands` (`XRHandJointID`, `XRFingerShapeTypes`, `XRHandSkeletonDriver`)
  * `UnityEngine.XR.Interaction.Toolkit` (`XRDirectInteractor`, `XRRayInteractor`)

#### How It Works:
1. **Joint Skeleton Driving:**
   * `XRHandSubsystem` tracks 26 joints per hand at 90Hz. `XRHandSkeletonDriver` updates the bone hierarchy of `LeftHand.fbx` / `RightHand.fbx`.
2. **Dual Gesture Parsing:**
   * **Fist Grasp:** Calculates middle and ring finger curl via `CalculateFingerShape(XRHandFingerID.Middle, XRFingerShapeTypes.FullCurl)`. A curl threshold above `0.7` initiates a grab, while dropping below `0.4` releases it.
   * **Pinch Grab:** Evaluates Euclidean distance `Vector3.Distance(thumbTip, indexTip) < 0.02m`.
3. **Angle-Independent Palm Snapping:**
   * Optical aim rays drift when fingers curl. To solve this, `VRHandFistGrab` casts an invisible conical volume (`coneAngle = 15°`, `coneReach = 2.0m`) forward along the palm vector to grab parts even when the wrist is tilted downward.

---

### System 4: Weapon Holding Poses & Real Index Finger Trigger Curl
* **Primary Scripts:** `Assets/Scripts/VR/VRHandGripPose.cs`, `Assets/Scripts/VR/VRFunctionCheckControls.cs`
* **Unity Libraries:**
  * `UnityEngine.XR.Hands.Processing.IXRHandProcessor`
  * `UnityEngine.Mathf` (`InverseLerp`)

#### How It Works:
1. **Hybrid Grip Wrap Pipeline (`VRHandGripPose.cs`):**
   * Implements `IXRHandProcessor`, running in the low-level XR Hands joint processing loop before render.
   * When the lower frame is grabbed, the processor overrides the middle, ring, and pinky bones to locked angular curl values (`70°`, `85°`, `45°`), wrapping them naturally around the grip panels without metal clipping.
2. **Continuous Analog Trigger Pull (`VRFunctionCheckControls.cs`):**
   * While the lower fingers hold the pistol, the thumb and index finger remain 100% live-tracked.
   * `hands.TryGetFingerCurl(side, XRHandFingerID.Index, out float curl)` reads real-time index curl.
   * `Mathf.InverseLerp(fingerCurlRest = 0.35f, fingerCurlFull = 0.80f, curl)` maps knuckle bend directly into trigger depression (`FunctionCheck.SetTrigger(pull)`).
3. **Two-Handed Slide Rack:**
   * An `XRSimpleInteractable` trigger is anchored to the slide serrations. When the off-hand grasps and moves backward, `Vector3.Dot(handPos - startPos, frame.forward)` calculates millimeter travel, cocking the hammer.

---

### System 5: Controller-Mounted Hand Visuals
* **Primary Script:** `Assets/Scripts/VR/VRControllerHands.cs`
* **Unity Libraries:**
  * `UnityEngine.InputSystem` (`InputAction`, `<XRController>{Hand}/triggerTouched`, `thumbstickTouched`)
  * `UnityEngine.XR.Interaction.Toolkit` (`ActionBasedController.hideControllerModel`)

#### How It Works:
1. **Hardware Masking:** Sets `hideControllerModel = true` to conceal standard plastic Touch controller models.
2. **Rig Parenting & Offset Snapping:** Instantiates stripped hand visualizers and anchors the wrist to the controller root with calibrated pitch and dorsal orientation vectors.
3. **Capacitive Touch-Driven Posing:**
   * Squeezing the physical grip button smoothly blends middle/ring/pinky bones into a fist.
   * Capacitive sensors on the thumbstick and buttons raise/lower the virtual thumb.
   * Pulling the analog index trigger bends the virtual index finger into the trigger guard.

---

### System 6: Guided Assembly, Snap-Fitting & Holographic Demonstrations
* **Primary Scripts:** `Assets/Scripts/VR/VRGuidance.cs`, `Assets/Scripts/VR/VRAssemblyGrab.cs`, `Assets/Scripts/VR/VRGhostDemo.cs`
* **Unity Libraries:**
  * `UnityEngine.XR.Interaction.Toolkit` (`XRSocketInteractor`, `XRGrabInteractable`)
  * `UnityEngine.LineRenderer`, `MaterialPropertyBlock`

#### How It Works:
1. **Spatial Hint Beacons:** Line renderers and pulsating target markers guide the trainee from the bench staging area to the active assembly socket.
2. **Snap-Fit Verification:** `AssemblyPart.cs` validates position/rotation tolerances. When a part enters its target socket within tolerance, haptic feedback pulses and audio confirmation fires.
3. **Ghost Holograms (`VRGhostDemo.cs`):** Renders semi-transparent ghost meshes demonstrating exact assembly trajectories (e.g., sliding the barrel into the slide, locking the slide pin).

---

### System 7: Live-Fire Qualification Range, Ballistics & Scoring
* **Primary Scripts:** `Assets/Scripts/Range/FireTraining.cs`, `Assets/Scripts/Range/RangeSergeant.cs`, `Assets/Scripts/Range/PistolMagazine.cs`, `Assets/Scripts/Range/ShotEffects.cs`
* **Unity Libraries:**
  * `UnityEngine.Physics` (`Raycast`, `RaycastHit`)
  * `UnityEngine.UI` (`Canvas`, `Button`, `Text`)
  * `UnityEngine.AudioSource`, `ParticleSystem`

#### How It Works:
1. **Interactive Range Sergeant NPC:** Proximity detection triggers the sergeant to turn toward the player within 2.5m, initiating qualification dialogue with custom voice clips.
2. **Magazine Insertion & Slide Battery:**
   * Detects magazine entry into the lower frame grip cavity.
   * Racking the slide chambers the first round. Empty magazine locks the slide back via slide-catch simulation.
3. **Raycast Ballistics & Decal Management:**
   * Fires instant raycasts from the barrel muzzle forward.
   * Spawns bullet tracer lines, dust puff particles, and bullet hole decals attached to hit colliders (supports moving targets).
   * Recycles decals via an object pool capped at 60 instances to protect Quest mobile performance.
4. **Qualification Scoring:**
   * Calculates hit distances on bullseye targets at 10m, 15m, and 25m.
   * Multiplies ring score (1–10) by distance multiplier.
   * Tracks split times, running score, and outputs an end-of-qualification summary card (Score, Accuracy %, Time).

---

## 3. Team Member Work Breakdown

| Member | Domain | Key Responsibilities & Deliverables |
| :--- | :--- | :--- |
| **Shashwat** | **3D Weapon Modeling & Technical Art** | Model decomposition into 14 functional Beretta 92 FS components; pivot point calibration; collision bounds setup; slide rail and mechanical axis alignment. |
| **Dhanush** | **Instructional Guidance & Ghost Animations** | Holographic ghost animation demonstrations (`VRGhostDemo`); guidance hint markers; directional target lines; voice clip integration for training phases. |
| **Tanmay** | **VR Interaction Architecture & Hands Engineering** | OpenXR rig configuration; optical bare-hand tracking integration; natural fist/pinch grab algorithms; hybrid grip poses (`VRHandGripPose` & `VRControllerHands`). |
| **Ayush** | **Live-Fire Range, Ballistics & Scoring** | Range qualification mechanics (`FireTraining`); Range Sergeant NPC dialogue logic; ballistics raycasting; decal pooling; distance-weighted bullseye scoring. |
| **Dishita** | **VR UI/UX, Spatial HUD & Adaptive Optics** | Dynamic floating canvas scoreboards; qualification result cards; adaptive laser pointer dimmer (`VRRayHoverDimmer`); UI raycasting integration. |

---
*Generated: November 2026 | VR Tactical Armory Development Team*
