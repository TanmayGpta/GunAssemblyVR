# 🪖 VR Tactical Armory: Weapon Assembly & Qualification Simulator

[![Unity](https://img.shields.io/badge/Unity-2022.3.62f3_LTS-000000.svg?logo=unity&logoColor=white)](https://unity.com/)
[![Platform](https://img.shields.io/badge/Platform-Meta_Quest_2%20%7C%203%20%7C%20Desktop-blue.svg)](https://www.meta.com/quest/)
[![Language](https://img.shields.io/badge/Language-C%23_9.0-239120.svg?logo=csharp&logoColor=white)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![Standard](https://img.shields.io/badge/Military_Standard-FM_3--23.35-red.svg)](https://armypubs.army.mil/)
[![Toolkit](https://img.shields.io/badge/Framework-XR_Interaction_Toolkit_(XRI)-blueviolet.svg)](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@latest/)

An institutional-grade, military-standard Virtual Reality armorer training simulator built in **Unity 2022.3 LTS**. Designed to instruct military personnel and armory apprentices on field-stripping, reassembling, inspecting, and qualifying with the standard-issue **Beretta 92 FS (M9)** 9×19mm NATO service pistol following U.S. Army Field Manual **FM 3-23.35**.

---

## 🎯 Project Overview

In military weapon handling, muscle memory is critical. Traditional VR assembly simulators often reduce weapon handling to a flat 2D "bench puzzle," where users drop parts onto a table like building blocks. 

This simulator implements an **input-agnostic two-handed physics architecture**:
* **In-Hand Manipulation:** Work can be done on the bench or by picking up the host receiver (`01_Lower_Frame` or `02_Upper_Slide`) into your off-hand in front of your chest to inspect the magwell, slide rails, or barrel cavity in 3D space.
* **Dual-Target Input:** Fully operable without a VR headset via a desktop two-handed simulation rig, while remaining 100% plug-and-play compatible with Unity's official **XR Interaction Toolkit (XRI)** for Oculus / Meta Quest.

---

## ✨ Key Features

* **5 Modular Sub-Assemblies:** Factory model cleaned and rebuilt into 5 canonical field modules (`LowerFrame`, `UpperSlide`, `Barrel`, `RecoilSpring`, `15Rd_Magazine`) at 1:1 true military scale (~21.7 cm length).
* **Holographic Ghost Shader (`Hologram.shader`):** Custom Fresnel rim-lit additive outline shader with `ZTest Always` to view internal seating targets through solid steel slide and frame geometry.
* **Dynamic Trajectory Guide Rails (`TryGuide`):** Automatically guides recessed parts (like the magazine sliding deep into the grip or the barrel into the slide) along the true mechanical insertion vector.
* **Full FM 3-23.35 Function Check (`FunctionCheck.cs`):** 7-step internal lockwork inspection verifying safety disconnection, Double-Action hammer trip, slide cocking under trigger hold, disconnector reset, and Single-Action break.
* **Procedural Sound Engine:** Real-time synthesized metallic acoustic clicks for hammer falls, safety clicks, and disconnector resets without third-party audio dependencies.
* **3-Lane Live-Fire Range Annex:** Downrange military shooting range featuring human silhouette targets with segregated `Head` and `Torso` hit detection zones (`RangeTarget.cs`).
* **Tactical World HUD:** In-world canvas displaying real-time checklists, tolerance deviations, and directive updates directly above the workbench.

---

## 🗺️ Facility Layout

```
+-------------------------------------------------------------+
|                LIVE-FIRE SHOOTING RANGE                     |
|                                                             |
|   [ Lane 01 Target ]    [ Lane 02 Target ]    [ Lane 03 ]   |
|         (7m)                  (15m)              (25m)      |
|                                                             |
|----------------- FIRING LINE BARRIER -----------------------|
|                                                             |
|                     [ Tactical HUD ]                        |
|   +-----------------------------------------------------+   |
|   |                  ARMORER WORKBENCH                  |   |
|   |  [02 Slide]   [03 Barrel]   [04 Spring]   [05 Mag]  |   |
|   |  [01 Frame]   -------------------------   --------  |   |
|   +-----------------------------------------------------+   |
|                            ^                                |
|                            | (Player Spawn)                 |
+-------------------------------------------------------------+
```

---

## 🎮 Desktop Controls (No-Headset Testing)

| Key / Input | Action | Description |
| :--- | :--- | :--- |
| **Left Click (Game Window)** | **Capture Cursor** | Locks mouse to first-person view (`Esc` to unlock) |
| **`W` `A` `S` `D`** | **Movement** | Walk around facility (`Shift` to sprint) |
| **Mouse Look** | **Aim / Look** | Center reticle aims ray at work zones |
| **Hold LMB** | **Pick Up Part** | Grabs aimed module; follows look direction |
| **Release LMB** | **Seat / Drop** | Sockets claim aligned parts; otherwise returns to mat |
| **Right Click (RMB)** | **Cancel Grab** | Instantly drops held part back to its bench slot |
| **`Q` / `E`** | **Rotate Part** | Rotates held part by ±15° increments |
| **`R`** | **Reset Rotation** | Resets held part rotation to original factory bench orientation |
| **`F`** | **Off-Hand Hold** | Toggles holding aimed Frame or Slide up to chest |
| **Hold MMB + Mouse** | **Inspect in Hand** | Rotates off-hand gun in 3D space (`C` to re-center) |
| **`V`** | **Safety Toggle** | Engages/disengages safety lever during Function Check |
| **Hold `T`** | **Trigger Squeeze**| Squeezes trigger (DA / SA) during Function Check |
| **Hold `G`, then release**| **Slide Rack** | Cycles slide rearward to cock hammer and test reset |

---

## 🛠️ Tech Stack & Architecture

```
Assets/
├── Editor/
│   ├── AssemblySetup.cs         # Menu item wiring 5 modules, sockets, and HUD
│   ├── ArmorySetupHelper.cs     # Mesh cleanup, pivot zeroing, bounding box setup
│   └── ArmoryDressingBuilder.cs # Spawns architecture, lockers, lighting, and range
├── Materials/                   # Military PBR materials + Assembly_Ghost.mat
├── Scenes/
│   └── SampleScene.unity        # Unified single-scene architecture
├── Scripts/
│   ├── Assembly/
│   │   ├── AssemblyPart.cs      # Input-agnostic grabbable module & off-hand holder
│   │   ├── SnapSocket.cs        # Ghost outline, guide rails, tolerance validation
│   │   ├── GunAssemblyManager.cs# State machine governing Step 01 to 05 flow
│   │   ├── AssemblyHUD.cs       # Live data binding to Tactical HUD UI
│   │   ├── DesktopAssemblyInteractor.cs # Two-handed keyboard/mouse simulator
│   │   └── FunctionCheck.cs     # FM 3-23.35 7-step lockwork & procedural audio
│   ├── Player/
│   │   └── ArmoryPlayerController.cs # First-person movement & look control
│   └── Range/
│       └── RangeTarget.cs       # Downrange silhouette target hit registration
└── Shaders/
    └── Hologram.shader          # Additive rim-lit ghost shader (Single-Pass Instanced)
```

---

## 🚀 Getting Started for Collaborators

### Prerequisites
* **Unity Hub** installed.
* **Unity 2022.3.62f3 LTS** (or any 2022.3.x LTS version).
* **Git** installed on your workstation.

### Installation
1. **Clone the repository:**
   ```bash
   git clone https://github.com/YOUR_ORGANIZATION/Gun_Assembly.git
   cd Gun_Assembly
   ```
2. **Open in Unity:**
   * Open **Unity Hub** → click **Add** → select the cloned `Gun_Assembly` directory.
   * Ensure Unity Editor version is set to `2022.3.62f3` (or compatible LTS).
   * Allow Unity 3–5 minutes to import packages and generate local library caches.
3. **Launch the Scene:**
   * In the Project window, navigate to `Assets/Scenes/SampleScene.unity`.
   * Double-click to open.
   * Press **Play (▶)**, click inside the Game view to lock cursor, and walk up to the workbench!

---

## 🤝 Git Collaboration Guidelines

To avoid corrupted Unity project caches and merge conflicts when working together:

1. **Never Commit `Library/` or `Temp/`:**  
   The included [`.gitignore`](file:///.gitignore) handles this automatically. Only commit `Assets/`, `Packages/`, and `ProjectSettings/`.
2. **Always Commit `.meta` Files:**  
   Every asset (script, model, material) has a corresponding `.meta` file containing its internal Unity GUID. Never ignore or delete `.meta` files.
3. **Branching Strategy:**  
   Always work on feature branches (`git checkout -b feature/shooting-mechanic`) and merge via Pull Requests. Avoid two people editing `SampleScene.unity` at the exact same time.

---

## 🔮 Future Scope Roadmap

- [x] Phase 01: Armory architecture, lighting, and shooting range layout.
- [x] Phase 02: 5-step weapon assembly with holographic ghost outlines and guide rails.
- [x] Phase 03: Two-handed in-air inspection and manipulation.
- [x] Phase 04: FM 3-23.35 7-step weapon function check with procedural audio.
- [ ] Phase 05: Live-fire shooting mechanics downrange (slide cycling, casing ejection, 9mm recoil, muzzle flash).
- [ ] Phase 06: Target qualification scoring (timed drills at 7m, 15m, 25m with HUD report).
- [ ] Phase 07: Meta Quest VR deployment using official XR Interaction Toolkit (XRI).

---

## 📜 License & Military Reference
* Standard Reference: **U.S. Army Field Manual FM 3-23.35 (Combat Training with Pistols & Revolvers)**.
* Developed for academic & instructional demonstration.
