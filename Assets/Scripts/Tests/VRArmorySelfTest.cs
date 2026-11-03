#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.UI;
using InputCommonUsages = UnityEngine.InputSystem.CommonUsages;

namespace VRArmory.Tests
{
    // Automated Play Mode self-test for Version 1 (editor only, never part of a build).
    // Started by Tools > VR Armory > VR > Run Self-Test, or from the command line (see VRSelfTestRunner).
    //   Desktop mode: drives the original AssemblyPart / FunctionCheck API, as the desktop interactor does.
    //   VR mode:      drives the real XR rig with simulated Quest controllers through the real XRI input bindings
    //                 (grip = grab, trigger = UI press / gun trigger, A = safety), so grabs, UI, two-handed holds,
    //                 sockets and the function check are tested end to end. It cannot test the headset itself.
    public class VRArmorySelfTest : MonoBehaviour
    {
        public const string ModeKey = "VRArmory.SelfTestMode"; // "desktop" or "vr"
        public const string ResultLine = "SELFTEST";

        int pass, fail, errors;
        string mode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string m = SessionState.GetString(ModeKey, "");
            if (m == "") return;
            var go = new GameObject("VRArmorySelfTest");
            DontDestroyOnLoad(go); // survives the Restart scene reload
            go.AddComponent<VRArmorySelfTest>().mode = m;
        }

        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            Debug.Log($"{ResultLine}|{mode}|{name}|{(ok ? "PASS" : "FAIL")}|{detail}");
        }

        // A failing nested step can stop the test coroutine without an exception reaching it; never hang a batch run.
        // (v1.2: the VR run now waits for real spoken lines and demonstrations, so it takes longer than before.)
        IEnumerator Watchdog()
        {
            yield return new WaitForSecondsRealtime(420f);
            Check("watchdog-timeout", false, "test did not finish within 420 s");
            Debug.Log($"{ResultLine}|{mode}|summary|FAIL|pass={pass} fail={fail}");
            SessionState.EraseString(ModeKey);
            SessionState.EraseBool(VRPlatformSwitch.ForceVRSessionKey);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else EditorApplication.isPlaying = false;
        }

        void Info(string name, string detail) => Debug.Log($"{ResultLine}|{mode}|{name}|INFO|{detail}");

        IEnumerator Start()
        {
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if ((type == LogType.Error || type == LogType.Exception) && !msg.StartsWith(ResultLine))
                {
                    errors++;
                    Debug.Log($"{ResultLine}|{mode}|console-error|INFO|{msg}");
                }
                if (type == LogType.Warning && msg.Contains("has no clip")) missingClipWarnings++;
            };
            yield return null;
            yield return null;
            StartCoroutine(Watchdog());

            IEnumerator run = mode == "vr" ? RunVR() : RunDesktop();
            // Any exception inside the test is reported instead of hanging the batch run.
            while (true)
            {
                object current;
                try
                {
                    if (!run.MoveNext()) break;
                    current = run.Current;
                }
                catch (Exception e)
                {
                    Check("test-exception", false, e.ToString());
                    break;
                }
                yield return current;
            }

            Check("no-console-errors", errors == 0, $"errors/exceptions logged during the run = {errors}");
            Debug.Log($"{ResultLine}|{mode}|summary|{(fail == 0 ? "PASS" : "FAIL")}|pass={pass} fail={fail}");
            SessionState.EraseString(ModeKey);
            SessionState.EraseBool(VRPlatformSwitch.ForceVRSessionKey);
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        // ------------------------------------------------------------------ shared lookups

        GunAssemblyManager mgr;
        FunctionCheck fc;
        AssemblyHUD hud;

        void FindScene()
        {
            mgr = FindObjectOfType<GunAssemblyManager>();
            fc = mgr.GetComponent<FunctionCheck>();
            hud = FindObjectOfType<AssemblyHUD>();
        }

        AssemblyPart P(GunPart g)
        {
            foreach (AssemblyPart p in FindObjectsOfType<AssemblyPart>()) if (p.partType == g) return p;
            return null;
        }

        SnapSocket S(string n)
        {
            foreach (SnapSocket s in SnapSocket.All) if (s.name == n) return s;
            return null;
        }

        static object Priv(object o, string field) =>
            o?.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)?.GetValue(o);

        string Check5() => Priv(fc, "check").ToString();

        IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        // ------------------------------------------------------------------ DESKTOP

        IEnumerator RunDesktop()
        {
            FindScene();
            GameObject vrContent = FindObjectOfType<VRPlatformSwitch>(true)?.vrContent;
            Check("desktop-player-active", GameObject.Find("Armorer_Player") != null);
            Check("vr-content-inactive", vrContent != null && !vrContent.activeInHierarchy && !VRPlatformSwitch.IsVR);
            Check("no-xr-manager-in-desktop", FindObjectsOfType<XRInteractionManager>().Length == 0,
                $"XRInteractionManagers = {FindObjectsOfType<XRInteractionManager>().Length}");
            Check("scene-wiring", mgr != null && fc != null && hud != null && FindObjectsOfType<AssemblyPart>().Length == 5 && SnapSocket.All.Count == 4);
            Check("desktop-starts-immediately", mgr.HasStarted && mgr.CurrentStepIndex == 0 && S("Socket_Barrel").IsUnlocked);
            // Version 1.1 UX is VR-only: the desktop HUD stays the instruction panel and nothing VR leaks in.
            Check("v11-desktop-hud-visible", hud.GetComponent<Canvas>().enabled);
            Check("v11-desktop-no-vr-guidance", FindObjectsOfType<VRPlacementAssist>().Length == 0 && FindObjectsOfType<VRGuidance>().Length == 0
                                                && FindObjectsOfType<VRTrainingMenu>().Length == 0);
            Check("v11-desktop-socket-style-unchanged", S("Socket_Barrel").amberWhenMisaligned && S("Socket_Barrel").faintAlphaBoost == 0f && S("Socket_Barrel").showInsertionPath);
            // v1.2: the voice guide and the ghost demonstrations are VR-only too.
            Check("v12-desktop-no-voice-or-ghosts", FindObjectsOfType<VRTrainingCoach>().Length == 0 && FindObjectsOfType<VRVoiceGuide>().Length == 0
                                                    && FindObjectsOfType<VRGhostDemo>().Length == 0 && FindObjectsOfType<GhostVisual>(true).Length == 0);

            // Rejections (audit tests), now with feedback + mistake count.
            yield return Place(P(GunPart.RecoilSpring), S("Socket_RecoilSpring"), Quaternion.identity);
            Check("out-of-sequence-rejected", !S("Socket_RecoilSpring").IsFilled && mgr.MistakeCount == 1, $"hud='{hud.DirectiveText}' mistakes={mgr.MistakeCount}");
            Check("rejection-reason-on-hud", hud.DirectiveText.Contains("OUT OF SEQUENCE"), hud.DirectiveText);
            yield return Wait(0.5f);

            yield return Place(P(GunPart.Barrel), S("Socket_Barrel"), Quaternion.Euler(0, 45, 0));
            Check("misaligned-rejected", !S("Socket_Barrel").IsFilled && hud.DirectiveText.Contains("MISALIGNED"), hud.DirectiveText);
            yield return Wait(0.5f);

            // Audit G-08: a later-step part on the active socket is "out of sequence", not "wrong part".
            yield return HoldNear(P(GunPart.Magazine), S("Socket_Barrel"));
            Check("g08-later-part-message", S("Socket_Barrel").WarningReason != null && S("Socket_Barrel").WarningReason.Contains("LATER STEP"),
                S("Socket_Barrel").WarningReason ?? "<none>");
            P(GunPart.Magazine).EndGrab();
            yield return Wait(0.5f);

            // Happy path with the G-07 timing check.
            (GunPart part, string socket)[] seq =
            {
                (GunPart.Barrel, "Socket_Barrel"), (GunPart.RecoilSpring, "Socket_RecoilSpring"),
                (GunPart.UpperSlide, "Socket_Slide"), (GunPart.Magazine, "Socket_Magazine"),
            };
            for (int i = 0; i < seq.Length; i++)
            {
                yield return Place(P(seq[i].part), S(seq[i].socket), Quaternion.identity);
                yield return Wait(0.4f); // seat animation (0.25 s)
                Check($"step{i + 1}-seated", S(seq[i].socket).IsFilled && P(seq[i].part).IsSeated && mgr.steps[i].completed,
                    $"step={mgr.CurrentStepIndex}");
                Check($"g07-step{i + 1}-message-visible", hud.DirectiveText.Contains("COMPLETE"), $"0.4 s after seating: '{hud.DirectiveText}'");
                if (i == seq.Length - 1)
                {
                    // A second "seated" for a finished step (while the next step is still pending) must change nothing.
                    typeof(GunAssemblyManager).GetMethod("OnSocketSeated", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(mgr, new object[] { S(seq[i].socket) });
                    Check("duplicate-completion-ignored", mgr.CurrentStepIndex == 3 && (int)Priv(mgr, "pendingStepIndex") == 4,
                        $"step={mgr.CurrentStepIndex} pending={Priv(mgr, "pendingStepIndex")}");
                }
                yield return Wait(1.6f); // message hold (1.5 s) starts after the 0.25 s seat animation
            }
            Check("function-check-started", fc.IsRunning && mgr.CurrentStepIndex == 4, $"step={mgr.CurrentStepIndex}");
            object lever = Priv(fc, "lever"), hammer = Priv(fc, "hammer");
            string leverName = ((Transform)Priv(lever, "t"))?.name;
            Check("g05-lever-is-ambi-lever", leverName == "Ambi_Safety_Lever", "animated safety piece = " + leverName);
            Transform hammerT = (Transform)Priv(hammer, "t");
            Quaternion hammerRest = hammerT.localRotation;

            // Audit G-04: animation must not depend on the desktop cursor lock (cursor is unlocked in batch mode).
            Info("cursor-lock", Cursor.lockState.ToString());
            fc.SetSafety(true); yield return null;
            fc.SetTrigger(1f); yield return null;
            fc.SetTrigger(0f); yield return null;
            fc.SetSafety(false); yield return null;
            fc.SetTrigger(0.6f); yield return null; yield return null;
            Check("g04-hammer-animates-without-cursor-lock", Quaternion.Angle(hammerRest, hammerT.localRotation) > 1f,
                $"hammer moved {Quaternion.Angle(hammerRest, hammerT.localRotation):0.0} deg during a DA pull");
            fc.SetTrigger(1f); yield return null;            // DA -> check 5
            fc.SetSlide(1f); yield return null;              // rack with trigger held
            fc.SetSlide(0f); yield return null;
            fc.SetTrigger(0f); yield return null;            // reset
            fc.SetTrigger(1f); yield return null;            // SA
            Check("function-check-passes", fc.Passed, "check=" + Check5());
            yield return Wait(1.8f);
            Check("all-steps-complete", mgr.IsComplete && hud.DirectiveText.StartsWith("ALL STEPS COMPLETE"), hud.DirectiveText);
            yield return Wait(2f);
            Check("final-message-persists", hud.DirectiveText.StartsWith("ALL STEPS COMPLETE"), hud.DirectiveText);

            yield return DeadEndRecoveryTest();

            // Restart = scene reload; statics must come back clean.
            SceneManager.LoadScene(0);
            yield return null; yield return null; yield return null;
            FindScene();
            Check("restart-clean-state", mgr.HasStarted && mgr.CurrentStepIndex == 0 && !mgr.IsComplete && AssemblyPart.HeldParts.Count == 0
                                         && SnapSocket.All.Count == 4 && !S("Socket_Barrel").IsFilled,
                $"step={mgr.CurrentStepIndex} held={AssemblyPart.HeldParts.Count} sockets={SnapSocket.All.Count}");
            yield return Place(P(GunPart.Barrel), S("Socket_Barrel"), Quaternion.identity);
            yield return Wait(0.4f);
            Check("assembly-works-after-restart", S("Socket_Barrel").IsFilled && mgr.CurrentStepIndex == 1);
        }

        // Audit G-06: trigger released before the slide went forward used to leave check 6 unreachable.
        IEnumerator DeadEndRecoveryTest()
        {
            var fc2 = new GameObject("FC_DeadEndTest").AddComponent<FunctionCheck>();
            fc2.frame = fc.frame;
            fc2.slide = fc.slide;
            fc2.desktopInput = false;
            fc2.Begin(null);
            string C2() => Priv(fc2, "check").ToString();
            fc2.SetSafety(true); fc2.SetTrigger(1f); fc2.SetTrigger(0f); fc2.SetSafety(false);
            fc2.SetTrigger(1f);                    // DA -> RackHeld
            fc2.SetSlide(1f);                      // rack with the trigger held
            fc2.SetTrigger(0f);                    // let go of the trigger too early
            fc2.SetSlide(0f);                      // slide forward
            string afterMistake = C2();
            fc2.SetTrigger(1f); fc2.SetSlide(1f); fc2.SetSlide(0f); // as the HUD now says: squeeze, hold, rack again
            string afterRetry = C2();
            fc2.SetTrigger(0f);
            string afterReset = C2();
            Check("g06-dead-end-recovers", afterMistake == "RackHeld" && afterRetry == "Reset" && afterReset == "SingleAction",
                $"after early release={afterMistake}, after retry={afterRetry}, after reset={afterReset}");
            Destroy(fc2.gameObject);
            yield return null;
        }

        IEnumerator Place(AssemblyPart part, SnapSocket socket, Quaternion extra)
        {
            if (!part.BeginGrab()) { Check("place-" + part.name, false, "BeginGrab refused"); yield break; }
            part.transform.SetPositionAndRotation(socket.transform.position, socket.transform.rotation * extra);
            yield return null; yield return null;
            part.EndGrab();
        }

        IEnumerator HoldNear(AssemblyPart part, SnapSocket socket)
        {
            part.BeginGrab();
            part.transform.SetPositionAndRotation(socket.transform.position, socket.transform.rotation);
            yield return null; yield return null;
        }

        // ------------------------------------------------------------------ VR

        XRSimulatedController left, right;
        XRSimulatedHMD hmd;
        XROrigin origin;
        VRTrainingMenu menu;

        // Simulated hand state; poses are world space and converted to tracking space when sent.
        class Hand
        {
            public XRSimulatedController device;
            public Vector3 pos;
            public Quaternion rot = Quaternion.identity;
            public bool grip, trigger, primary, secondary;
            public float triggerValue = -1f;
        }
        Hand L, R;

        void Send(Hand h)
        {
            if (origin == null) origin = FindObjectOfType<XROrigin>(); // replaced by the Restart scene reload
            if (origin == null) return;
            Transform space = origin.CameraFloorOffsetObject.transform;
            var s = new XRSimulatedControllerState
            {
                devicePosition = space.InverseTransformPoint(h.pos),
                deviceRotation = Quaternion.Inverse(space.rotation) * h.rot,
                isTracked = true,
                trackingState = 3, // position + rotation
                grip = h.grip ? 1f : 0f,
                trigger = h.triggerValue >= 0f ? h.triggerValue : (h.trigger ? 1f : 0f),
            };
            if (h.grip) s = s.WithButton(ControllerButton.GripButton);
            if (h.trigger || h.triggerValue > 0.5f) s = s.WithButton(ControllerButton.TriggerButton);
            if (h.primary) s = s.WithButton(ControllerButton.PrimaryButton);
            if (h.secondary) s = s.WithButton(ControllerButton.SecondaryButton);
            InputSystem.QueueStateEvent(h.device, s);
        }

        IEnumerator Frames(int n = 3)
        {
            for (int i = 0; i < n; i++)
            {
                Send(L); Send(R);
                yield return null;
            }
        }

        IEnumerator Hold(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Send(L); Send(R);
                yield return null;
            }
        }

        void SendHead()
        {
            var s = new XRSimulatedHMDState
            {
                centerEyePosition = new Vector3(0f, 1.6f, 0f),
                centerEyeRotation = Quaternion.identity,
                devicePosition = new Vector3(0f, 1.6f, 0f),
                deviceRotation = Quaternion.identity,
                isTracked = true,
                trackingState = 3,
            };
            InputSystem.QueueStateEvent(hmd, s);
        }

        IEnumerator RunVR()
        {
            FindScene();
            origin = FindObjectOfType<XROrigin>();
            menu = FindObjectOfType<VRTrainingMenu>();
            guide = FindObjectOfType<VRGuidance>();
            Check("vr-mode-active", VRPlatformSwitch.IsVR && origin != null && GameObject.Find("Armorer_Player") == null);
            Check("single-xr-manager", FindObjectsOfType<XRInteractionManager>().Length == 1, $"count={FindObjectsOfType<XRInteractionManager>().Length}");
            Check("single-event-system", FindObjectsOfType<EventSystem>().Length == 1);
            Check("single-audio-listener", FindObjectsOfType<AudioListener>().Length == 1);
            Check("single-enabled-camera", Array.FindAll(FindObjectsOfType<Camera>(), c => c.enabled).Length == 1);
            // v1.1: the first screen is module selection (was: the briefing). The assembly is still locked until START.
            Check("vr-waits-for-start", !mgr.HasStarted && menu.modulesPanel.activeSelf && !menu.introPanel.activeSelf, "module selection shown, assembly locked");
            Check("fc-desktop-keys-off", !fc.desktopInput && fc.triggerControl.Contains("TRIGGER"));
            Check("no-teleport-objects", GameObject.Find("Teleport_Floor") == null && GameObject.Find("Bench_Teleport_Anchor") == null);
            Check("all-part-grabs-enabled", Array.TrueForAll(FindObjectsOfType<VRAssemblyGrab>(true), g => g.enabled));
            int shadowLights = Array.FindAll(FindObjectsOfType<Light>(), l => l.shadows != LightShadows.None).Length;
            Check("quest-shadow-budget", shadowLights <= 2, $"shadow-casting lights in VR = {shadowLights}");

            // ---- v1.1: exactly one instruction interface
            Canvas hudCanvas = hud.GetComponent<Canvas>();
            Canvas menuCanvas = menu.GetComponent<Canvas>();
            // Any other drawn canvas that gives instructions (AssemblyHUD) or can be pointed at (a raycaster). The armory's
            // wall signs (Range_Sign_Text, Pegboard_Label, ...) are scenery and do not count.
            Canvas[] others = Array.FindAll(FindObjectsOfType<Canvas>(), c => c.enabled && c.isRootCanvas && c != menuCanvas && !c.transform.IsChildOf(guide.transform)
                && (c.GetComponent<AssemblyHUD>() != null || Array.Exists(c.GetComponents<BaseRaycaster>(), r => r.enabled)));
            int raycasters = Array.FindAll(FindObjectsOfType<TrackedDeviceGraphicRaycaster>(), r => r.isActiveAndEnabled).Length;
            Check("v11-single-instruction-interface", !hudCanvas.enabled && FindObjectsOfType<VRTrainingMenu>().Length == 1 && others.Length == 0 && raycasters == 1,
                $"hud drawn={hudCanvas.enabled} other canvases=[{string.Join(",", Array.ConvertAll(others, c => c.name))}] ui raycasters={raycasters}");
            Vector3 toPanel = menu.transform.position - origin.transform.position;
            Check("v11-panel-in-front", Mathf.Abs(toPanel.x) < 0.2f && toPanel.z > 1f && toPanel.z < 1.6f && menu.transform.position.y > 1.25f,
                $"panel {toPanel} from the stand point");
            Check("v11-assist-added-to-every-part", FindObjectsOfType<VRPlacementAssist>().Length == 5);

            // ---- v1.2: voice guide and ghost demonstrations are wired, and every line has its recording
            HookCoach();
            StartCoroutine(VoiceOverlapMonitor());
            Check("v12-coach-wired", coach != null && voice != null && ghost != null && coach.voice == voice && coach.ghost == ghost && coach.menu == menu
                                     && ghost.ghostMaterial != null && ghost.ghostMaterial.shader.name == "VRArmory/GhostDemo" && menu.repeatButton != null,
                $"coach={coach != null} voice={voice != null} ghost={ghost != null} material={ghost?.ghostMaterial?.shader.name}");
            VoiceLine[] allLines = Array.FindAll((VoiceLine[])Enum.GetValues(typeof(VoiceLine)), l => l != VoiceLine.None);
            VoiceLine[] unrecorded = Array.FindAll(allLines, l => voice.ClipFor(l) == null);
            Check("v12-every-voice-line-recorded", unrecorded.Length == 0 && voice.clips.Length == allLines.Length,
                $"{allLines.Length - unrecorded.Length}/{allLines.Length} recorded; missing: {string.Join(",", unrecorded)}");
            Check("v12-voice-clips-quest-settings", Array.TrueForAll(allLines, l => voice.ClipFor(l) == null
                                                    || (voice.ClipFor(l).channels == 1 && voice.ClipFor(l).loadType == AudioClipLoadType.CompressedInMemory)),
                "mono, compressed in memory");
            // The recordings were made from voice_script.tsv; it must still say exactly what VRTrainingText.Spoken says.
            string script = System.IO.File.Exists("Tools/VoiceGeneration/voice_script.tsv") ? System.IO.File.ReadAllText("Tools/VoiceGeneration/voice_script.tsv") : "";
            VoiceLine[] outdated = Array.FindAll(allLines, l => !script.Contains($"VO_{l}.wav\t{l}\t{VRTrainingText.Spoken(l)}\n"));
            Check("v12-recordings-match-script", script.Length > 0 && outdated.Length == 0, "changed since recording: " + string.Join(",", outdated));
            AudioSource voiceSource = voice.GetComponent<AudioSource>();
            Check("v12-voice-own-2d-source", voiceSource.spatialBlend == 0f && !voiceSource.loop && voiceSource.priority == 0 && !voiceSource.playOnAwake);
            Check("v12-welcome-spoken", coach.CurrentCue == VRTrainingCoach.Cue.ChooseModule
                                        && (voice.Current == VoiceLine.Intro_ChooseModule || Said(VoiceLine.Intro_ChooseModule) == 1),
                $"cue={coach.CurrentCue} voice={voice.Current}");

            hmd = InputSystem.AddDevice<XRSimulatedHMD>();
            left = InputSystem.AddDevice<XRSimulatedController>("SimLeft");
            right = InputSystem.AddDevice<XRSimulatedController>("SimRight");
            InputSystem.SetDeviceUsage(left, InputCommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(right, InputCommonUsages.RightHand);
            Vector3 stand = origin.transform.position;
            Vector3 leftRest = stand + new Vector3(-0.25f, 1.1f, 0.3f), rightRest = stand + new Vector3(0.25f, 1.1f, 0.3f);
            L = new Hand { device = left, pos = leftRest };
            R = new Hand { device = right, pos = rightRest };
            SendHead();
            yield return Frames(5);
            Camera cam = origin.Camera;
            // Without a headset there is no XR input subsystem, so the XR Origin keeps the prefab's camera offset;
            // on the Quest, Floor tracking sets that offset to 0. The camera must sit at offset + head height.
            float offsetY = origin.CameraFloorOffsetObject.transform.position.y;
            Check("hmd-tracking-drives-camera", Mathf.Abs(cam.transform.position.y - (offsetY + 1.6f)) < 0.05f,
                $"camera y = {cam.transform.position.y:0.00}, floor offset = {offsetY:0.00} (0 on device)");
            ActionBasedController lc = FindController("Left Controller");
            Check("controllers-tracked", lc != null && Vector3.Distance(lc.transform.position, L.pos) < 0.01f,
                $"left controller at {lc?.transform.position}, sent {L.pos}");

            // ---- v1.1: module selection -> coming soon -> back -> gun assembly -> briefing -> start
            yield return PressButton(R, menu.shootingModuleButton);
            Check("v11-shooting-shows-coming-soon", menu.comingSoonPanel.activeSelf && !mgr.HasStarted);
            yield return PressButton(R, menu.comingSoonBackButton);
            Check("v11-coming-soon-back", menu.modulesPanel.activeSelf && !menu.comingSoonPanel.activeSelf);
            yield return PressButton(R, menu.assemblyModuleButton);
            Check("v11-assembly-module-opens-briefing", menu.introPanel.activeSelf && !mgr.HasStarted);
            yield return WaitFor(() => Said(VoiceLine.Intro_Briefing) > 0, 2f);
            Check("v12-briefing-spoken", Said(VoiceLine.Intro_Briefing) == 1 && Said(VoiceLine.Intro_ChooseModule) <= 1,
                "coming back to the modules screen does not repeat the welcome");

            // ---- UI: point the right ray at START and pull the trigger
            yield return PressButton(R, menu.startButton);
            Check("ui-start-button", mgr.HasStarted && menu.progressPanel.activeSelf && S("Socket_Barrel").IsUnlocked,
                $"started={mgr.HasStarted}");
            yield return Frames(2);
            Check("v11-step1-barrel-highlighted", guide.CurrentPhase == VRGuidance.Phase.PickUp && guide.HighlightedPart == P(GunPart.Barrel)
                                                  && menu.headlineText.text == VRTrainingText.PickUp(GunPart.Barrel),
                $"phase={guide.CurrentPhase} highlighted={guide.HighlightedPart?.name} headline='{menu.headlineText.text}'");
            yield return WaitFor(() => Said(VoiceLine.Asm_Pick_Barrel) > 0, 2f);
            Check("v12-step1-instruction-spoken", Said(VoiceLine.Asm_Pick_Barrel) == 1 && coach.CurrentCue == VRTrainingCoach.Cue.PickUpPart,
                $"cue={coach.CurrentCue}");

            // ---- Rejections through real grabs
            yield return GrabAndRelease(L, P(GunPart.RecoilSpring), S("Socket_RecoilSpring"), Quaternion.identity, false);
            Check("vr-out-of-sequence-rejected", !S("Socket_RecoilSpring").IsFilled && mgr.MistakeCount == 1, $"mistakes={mgr.MistakeCount} hud='{hud.DirectiveText}'");
            Check("v11-rejection-explained-on-panel", menu.ShownFeedback == VRTrainingMenu.Feedback.Error && menu.feedbackText.text == VRTrainingText.RejectOutOfSequence,
                menu.feedbackText.text);
            yield return Hold(0.5f);
            Check("vr-part-returned-to-bench", P(GunPart.RecoilSpring).IsGrabbable && !P(GunPart.RecoilSpring).IsHeld);
            Check("v12-rejection-try-again-spoken", Said(VoiceLine.Asm_TryAgain) == 1 && Said(VoiceLine.Asm_Pick_Barrel) == 1,
                "the part went back: advice once, the pick-up instruction is not repeated by itself");

            // ---- v1.2: "Hear again" on the panel and B on the controller repeat the current instruction
            int picks = Said(VoiceLine.Asm_Pick_Barrel);
            yield return PressButton(R, menu.repeatButton);
            yield return WaitFor(() => Said(VoiceLine.Asm_Pick_Barrel) > picks, 2f);
            Check("v12-hear-again-button", Said(VoiceLine.Asm_Pick_Barrel) == picks + 1 && coach.CurrentCue == VRTrainingCoach.Cue.PickUpPart);
            yield return PressSecondary(R);
            yield return WaitFor(() => Said(VoiceLine.Asm_Pick_Barrel) > picks + 1, 2f);
            Check("v12-hear-again-B-button", Said(VoiceLine.Asm_Pick_Barrel) == picks + 2);

            yield return GrabAndRelease(R, P(GunPart.Barrel), S("Socket_Barrel"), Quaternion.Euler(0f, 0f, 40f), false);
            Check("vr-misaligned-rejected", !S("Socket_Barrel").IsFilled && hud.DirectiveText.Contains("MISALIGNED"), hud.DirectiveText);
            yield return Hold(0.5f);

            // ---- v1.1: the assist never pulls in a wrong part or an out-of-sequence part (grip held, no release)
            AssemblyPart mag = P(GunPart.Magazine);
            yield return GrabAt(L, mag);
            yield return CarryTo(L, mag, () => SeatPose(S("Socket_Barrel")), 0.8f);
            Check("v11-assist-rejects-wrong-part", !S("Socket_Barrel").IsFilled && mag.IsHeld && guide.WrongPartHeld == mag
                                                   && menu.ShownFeedback == VRTrainingMenu.Feedback.Error,
                $"filled={S("Socket_Barrel").IsFilled} held={mag.IsHeld} wrong={guide.WrongPartHeld?.name} feedback={menu.ShownFeedback}");
            yield return ReleaseAt(L, leftRest);
            AssemblyPart spring = P(GunPart.RecoilSpring);
            yield return GrabAt(L, spring);
            yield return CarryTo(L, spring, () => SeatPose(S("Socket_RecoilSpring")), 0.8f);
            Check("v11-assist-rejects-out-of-sequence", !S("Socket_RecoilSpring").IsFilled && spring.IsHeld && !spring.GetComponent<VRPlacementAssist>().IsAssisting,
                $"filled={S("Socket_RecoilSpring").IsFilled} held={spring.IsHeld}");
            yield return ReleaseAt(L, leftRest);
            yield return Hold(0.5f);

            // ---- v1.1: target guidance and directions computed from the real pose error (right = +X, forward = +Z here)
            AssemblyPart barrel = P(GunPart.Barrel);
            SnapSocket barrelSocket = S("Socket_Barrel");
            Transform view = cam.transform;
            yield return GrabAt(R, barrel);
            yield return Hold(0.3f);
            Check("v11-target-guidance-on-grab", guide.CurrentPhase == VRGuidance.Phase.Place && guide.TargetSocket == barrelSocket && guide.GuideLineVisible
                                                 && menu.headlineText.text == VRTrainingText.Place(GunPart.Barrel),
                $"phase={guide.CurrentPhase} target={guide.TargetSocket?.name} line={guide.GuideLineVisible} headline='{menu.headlineText.text}'");
            yield return WaitFor(() => Said(VoiceLine.Asm_Place_Barrel) > 0, 2f);
            Check("v12-place-instruction-spoken", Said(VoiceLine.Asm_Place_Barrel) == 1 && coach.CurrentCue == VRTrainingCoach.Cue.PlacePart, $"cue={coach.CurrentCue}");
            yield return CarryTo(R, barrel, () => new Pose(barrelSocket.transform.position + view.right * 0.10f, barrelSocket.transform.rotation), 0.3f);
            Check("v11-direction-move-left", guide.CurrentCorrection == VRGuidance.Correction.MoveLeft && menu.feedbackText.text == VRTrainingText.MoveLeft,
                $"part 10 cm right of the seat -> {guide.CurrentCorrection}, shown {barrelSocket.LastDistance * 100f:0.0} cm away");
            yield return CarryTo(R, barrel, () => new Pose(barrelSocket.transform.position + view.up * 0.10f, barrelSocket.transform.rotation), 0.3f);
            Check("v11-direction-move-down", guide.CurrentCorrection == VRGuidance.Correction.MoveDown, guide.CurrentCorrection.ToString());
            yield return CarryTo(R, barrel, () => new Pose(barrelSocket.transform.position, Quaternion.AngleAxis(30f, view.forward) * barrelSocket.transform.rotation), 0.3f);
            Check("v11-direction-rotate-clockwise", guide.CurrentCorrection == VRGuidance.Correction.RotateClockwise && guide.RotationArcVisible && !barrelSocket.IsFilled,
                $"turned 30 deg counter-clockwise (as seen) -> {guide.CurrentCorrection}, shown {barrelSocket.LastAngle:0.0} deg off");
            yield return CarryTo(R, barrel, () => new Pose(barrelSocket.transform.position, Quaternion.AngleAxis(30f, view.up) * barrelSocket.transform.rotation), 0.3f);
            Check("v11-direction-turn-left", guide.CurrentCorrection == VRGuidance.Correction.TurnLeft && !barrelSocket.IsFilled,
                $"front turned 30 deg right -> {guide.CurrentCorrection}");
            yield return ReleaseAt(R, rightRest);
            yield return Hold(0.5f);
            Check("v11-no-accidental-seat", !barrelSocket.IsFilled && mgr.CurrentStepIndex == 0 && mgr.MistakeCount == 2, $"mistakes={mgr.MistakeCount}");

            // Repeated grab / release must not break the state.
            for (int i = 0; i < 3; i++)
            {
                yield return GrabAt(R, P(GunPart.Barrel));
                R.grip = false;
                yield return Frames(2);
            }
            yield return Hold(0.5f);
            Check("vr-repeat-grab-release-stable", !P(GunPart.Barrel).IsHeld && P(GunPart.Barrel).IsGrabbable && AssemblyPart.HeldParts.Count == 0 && mgr.CurrentStepIndex == 0);

            // ---- Single-hand part: the second hand cannot steal it
            yield return GrabAt(R, P(GunPart.Barrel));
            VRAssemblyGrab barrelGrab = P(GunPart.Barrel).GetComponent<VRAssemblyGrab>();
            L.pos = R.pos; L.rot = R.rot; L.grip = false;
            yield return Frames(3);
            L.grip = true;
            yield return Frames(4);
            Check("single-part-not-stolen", barrelGrab.interactorsSelecting.Count == 1 && IsHeldBy(barrelGrab, right),
                $"holders={barrelGrab.interactorsSelecting.Count}");
            L.grip = false; R.grip = false;
            L.pos = leftRest;
            yield return Hold(0.6f);

            // ---- Two-handed slide: no jump when the second hand joins or either hand leaves
            AssemblyPart slide = P(GunPart.UpperSlide);
            VRAssemblyGrab slideGrab = slide.GetComponent<VRAssemblyGrab>();
            yield return GrabAt(L, slide);
            yield return LiftTo(L, slide, origin.transform.position + new Vector3(-0.1f, 1.15f, 0.45f));
            Vector3 p0 = slide.transform.position; Quaternion r0 = slide.transform.rotation;
            R.pos = slide.Colliders[0].bounds.center + Vector3.forward * 0.02f; R.rot = L.rot; R.grip = false;
            yield return Frames(3);
            R.grip = true;
            yield return Hold(0.4f);
            float jumpJoin = Vector3.Distance(p0, slide.transform.position), turnJoin = Quaternion.Angle(r0, slide.transform.rotation);
            Check("two-handed-grab", slideGrab.interactorsSelecting.Count == 2, $"holders={slideGrab.interactorsSelecting.Count}");
            Check("two-handed-no-jump-on-join", jumpJoin < 0.01f && turnJoin < 3f, $"moved {jumpJoin * 1000f:0.0} mm, turned {turnJoin:0.0} deg");
            p0 = slide.transform.position; r0 = slide.transform.rotation;
            L.grip = false;
            yield return Hold(0.4f);
            float jumpLeave = Vector3.Distance(p0, slide.transform.position), turnLeave = Quaternion.Angle(r0, slide.transform.rotation);
            Check("release-one-hand-keeps-holding", slideGrab.interactorsSelecting.Count == 1 && slide.IsHeld, $"holders={slideGrab.interactorsSelecting.Count}");
            Check("two-handed-no-jump-on-release", jumpLeave < 0.01f && turnLeave < 3f, $"moved {jumpLeave * 1000f:0.0} mm, turned {turnLeave:0.0} deg");
            Check("v11-host-held-guidance", guide.HostHeld == slide && guide.HighlightedPart == P(GunPart.Barrel)
                                            && menu.detailText.text == VRTrainingText.HostHeldDetail(GunPart.UpperSlide), menu.detailText.text);

            // ---- Step 1 while the slide is held in the air: barrel seated by the other hand
            L.grip = false;
            yield return Frames(2);
            yield return GrabAndRelease(L, P(GunPart.Barrel), S("Socket_Barrel"), Quaternion.identity, true);
            yield return Hold(0.4f);
            Check("vr-step1-seated-into-held-slide", S("Socket_Barrel").IsFilled && P(GunPart.Barrel).transform.parent == slide.transform && mgr.CurrentStepIndex == 1,
                $"step={mgr.CurrentStepIndex}");
            Check("vr-step-complete-message", hud.DirectiveText.Contains("COMPLETE"), hud.DirectiveText);
            Check("v11-seat-success-on-panel", menu.ShownFeedback == VRTrainingMenu.Feedback.Success && menu.feedbackText.text == VRTrainingText.Seated(GunPart.Barrel),
                menu.feedbackText.text);
            yield return WaitFor(() => Said(VoiceLine.Asm_Pick_Spring) > 0, 5f);
            Check("v12-praise-then-next-instruction", Said(VoiceLine.Common_WellDone) == 1 && Said(VoiceLine.Asm_Pick_Spring) == 1
                                                      && SaidAt(VoiceLine.Common_WellDone) < SaidAt(VoiceLine.Asm_Pick_Spring),
                $"well done at {SaidAt(VoiceLine.Common_WellDone):0.0} s, next instruction at {SaidAt(VoiceLine.Asm_Pick_Spring):0.0} s");
            Check("v12-no-repeat-on-regrab", Said(VoiceLine.Asm_Place_Barrel) == 1, $"the barrel was grabbed 5 times, its instruction spoken {Said(VoiceLine.Asm_Place_Barrel)} time(s)");
            R.grip = false;   // put the slide back
            yield return Hold(0.6f);
            Check("held-slide-returned-with-barrel", !slide.IsHeld && P(GunPart.Barrel).transform.parent == slide.transform);
            Check("v11-step2-spring-highlighted", guide.HighlightedPart == spring, guide.HighlightedPart?.name);

            // ---- Step 2, v1.1: brought near the seat with the grip still held, it is guided in and clicks in by itself
            SnapSocket springSocket = S("Socket_RecoilSpring");
            yield return GrabAt(R, spring);
            yield return Hold(0.3f);
            float shownMin = float.MaxValue;
            yield return CarryTo(R, spring, () => new Pose(springSocket.transform.position + view.right * 0.04f, Quaternion.AngleAxis(12f, view.up) * springSocket.transform.rotation),
                1.2f, () => { if (!springSocket.IsFilled && spring.IsHeld) shownMin = Mathf.Min(shownMin, springSocket.LastDistance); });
            Check("v11-assist-pulls-toward-seat", shownMin < 0.03f, $"hand 40 mm off the seat, part shown {shownMin * 1000f:0.0} mm off");
            Check("v11-auto-seat-without-release", springSocket.IsFilled && !spring.IsHeld && R.grip, $"filled={springSocket.IsFilled} held={spring.IsHeld}");
            R.grip = false;
            yield return Hold(0.5f);
            Check("vr-step2-seated", S("Socket_RecoilSpring").IsFilled && mgr.CurrentStepIndex == 2);
            Check("v11-step3-slide-highlighted", guide.HighlightedPart == slide, guide.HighlightedPart?.name);

            // ---- Step 3, v1.1: two-handed slide guided onto the frame; both hands are let go when it clicks in
            yield return GrabAt(L, slide);
            R.pos = slide.Colliders[0].bounds.center + Vector3.forward * 0.02f; R.rot = L.rot; R.grip = false;
            yield return Frames(3);
            R.grip = true;
            yield return Hold(0.4f);
            int holders = slideGrab.interactorsSelecting.Count;
            yield return CarryBothTo(slide, () => SeatPose(S("Socket_Slide")), 1.2f);
            Check("v11-two-handed-assisted-seat", holders == 2 && S("Socket_Slide").IsFilled && slideGrab.interactorsSelecting.Count == 0,
                $"holders before={holders}, filled={S("Socket_Slide").IsFilled}, holders after={slideGrab.interactorsSelecting.Count}");
            L.grip = false; R.grip = false;
            L.pos = leftRest; R.pos = rightRest;
            yield return Hold(0.5f);
            Check("vr-step3-seated", S("Socket_Slide").IsFilled && slide.transform.parent == P(GunPart.LowerFrame).transform && mgr.CurrentStepIndex == 3);
            Check("v11-step4-magazine-highlighted", guide.HighlightedPart == mag, guide.HighlightedPart?.name);

            yield return GrabAndRelease(R, P(GunPart.Magazine), S("Socket_Magazine"), Quaternion.identity, true);
            yield return Hold(2.0f);
            Check("vr-step4-seated-function-check-begins", S("Socket_Magazine").IsFilled && mgr.CurrentStepIndex == 4 && fc.IsRunning, $"step={mgr.CurrentStepIndex} running={fc.IsRunning}");
            Check("vr-fc-directive-uses-vr-controls", hud.DirectiveText.Contains("A / X"), hud.DirectiveText);
            Check("v11-assembly-complete-celebration", menu.headlineText.text == VRTrainingText.AssemblyComplete, menu.headlineText.text);
            yield return Hold(2.0f);
            AssemblyPart frame = P(GunPart.LowerFrame);
            Check("v11-fc-pick-up-pistol", guide.CurrentPhase == VRGuidance.Phase.PickUpPistol && guide.HighlightedPart == frame
                                           && menu.headlineText.text == VRTrainingText.PickUpPistol, $"phase={guide.CurrentPhase} headline='{menu.headlineText.text}'");
            yield return WaitFor(() => Said(VoiceLine.FC_Intro) > 0, 5f);
            Check("v12-assembled-then-function-check-intro", Said(VoiceLine.Asm_Complete) == 1 && Said(VoiceLine.FC_Intro) == 1
                                                             && SaidAt(VoiceLine.Asm_Complete) < SaidAt(VoiceLine.FC_Intro) && Said(VoiceLine.Common_WellDone) == 3,
                $"well done x{Said(VoiceLine.Common_WellDone)} (steps 1-3), assembled, then the function check");

            // ---- Function check with controllers
            yield return GrabAt(R, frame);
            yield return LiftTo(R, frame, origin.transform.position + new Vector3(0.05f, 1.15f, 0.45f));
            Check("pistol-held", frame.IsHeld);
            Transform rightCtrl = FindController("Right Controller").transform;
            Check("v11-fc-tooltip-on-pistol-hand", guide.TooltipText == VRTrainingText.PressButton("A") && Vector3.Distance(guide.TooltipTransform.position, rightCtrl.position) < 0.15f
                                                  && menu.headlineText.text == VRTrainingText.StageTitle(0),
                $"tooltip='{guide.TooltipText}' {Vector3.Distance(guide.TooltipTransform.position, rightCtrl.position) * 100f:0} cm from the right controller, headline='{menu.headlineText.text}'");
            yield return WaitFor(() => Said(VoiceLine.FC_SafetyOn_A) > 0, 3f);
            Check("v12-safety-instruction-names-A", Said(VoiceLine.FC_SafetyOn_A) == 1 && Said(VoiceLine.FC_SafetyOn_X) == 0, "pistol in the right hand");
            R.primary = true; yield return Frames(2); R.primary = false; yield return Frames(2);
            Check("fc1-safety-on-with-A", fc.SafetyOn && Check5() == "SafeTrigger", Check5());

            // ---- v1.2: trigger demonstration. It starts with its "watch" line; the ghost cannot be touched and moves nothing real.
            yield return WaitFor(() => ghost.Playing == VRGhostDemo.Demo.TriggerPull, 4f);
            float lag = Mathf.Abs(GhostStartedAt(VRGhostDemo.Demo.TriggerPull) - SaidAt(VoiceLine.FC_TriggerDemo_Safe));
            Check("v12-trigger-demo-starts-with-voice", ghost.Playing == VRGhostDemo.Demo.TriggerPull && Said(VoiceLine.FC_TriggerDemo_Safe) == 1 && lag < 0.05f,
                $"ghost started {lag * 1000f:0} ms from the first word");
            Transform realTrigger = fc.TriggerPiece;
            Pose realTriggerRest = LocalPose(realTrigger);
            yield return WaitFor(() => ghost.Travel > 0.5f, 3f);
            Check("v12-ghost-trigger-inert", GhostIsInert(ghost.TriggerGhost) && ghost.TriggerGhost.parent == realTrigger.parent && fc.TriggerPull == 0f
                                             && LocalPose(realTrigger) == realTriggerRest && ghost.GhostVisible,
                $"ghost travel {ghost.Travel:0.00} while the real trigger stays at {fc.TriggerPull:0.00}");

            // Put the pistol down in the middle of the demonstration, then pick it up again.
            R.grip = false;
            yield return WaitFor(() => Said(VoiceLine.FC_PickUpAgain) > 0, 3f);
            Check("v12-putting-pistol-down-stops-demo", !ghost.IsPlaying && coach.CurrentCue == VRTrainingCoach.Cue.PickUpPistolAgain && Said(VoiceLine.FC_PickUpAgain) == 1,
                $"cue={coach.CurrentCue} ghost={ghost.Playing}");
            yield return Hold(0.6f); // back on the bench
            yield return GrabAt(R, frame);
            yield return LiftTo(R, frame, origin.transform.position + new Vector3(0.05f, 1.15f, 0.45f));
            yield return WaitFor(() => Said(VoiceLine.FC_TriggerDemo_Safe) >= 2 && ghost.IsPlaying, 4f);
            Check("v12-pick-up-again-resumes-demo", frame.IsHeld && Check5() == "SafeTrigger" && Said(VoiceLine.FC_TriggerDemo_Safe) == 2
                                                    && ghost.Playing == VRGhostDemo.Demo.TriggerPull && Said(VoiceLine.FC_SafetyOn_A) == 1,
                $"held={frame.IsHeld} check={Check5()} ghost={ghost.Playing}");

            // The whole demonstration, then "now you try" - not before both the voice and the ghost have finished.
            float triggerTravel = 0f;
            Pose ghostTriggerFull = default;
            yield return WaitFor(() =>
            {
                if (ghost.IsPlaying && ghost.Travel > triggerTravel) { triggerTravel = ghost.Travel; ghostTriggerFull = LocalPose(ghost.TriggerGhost); }
                return Said(VoiceLine.FC_YourTurn_Trigger) > 0;
            }, 20f);
            float afterWatch = SaidAt(VoiceLine.FC_YourTurn_Trigger) - SaidAt(VoiceLine.FC_TriggerDemo_Safe);
            Check("v12-try-prompt-after-demo", Said(VoiceLine.FC_YourTurn_Trigger) == 1 && !ghost.IsPlaying && triggerTravel >= 0.999f
                                               && afterWatch >= voice.ClipFor(VoiceLine.FC_TriggerDemo_Safe).length,
                $"prompt {afterWatch:0.0} s after the watch line ({voice.ClipFor(VoiceLine.FC_TriggerDemo_Safe).length:0.0} s long), ghost reached {triggerTravel:0.000}");
            R.triggerValue = 1f; yield return Hold(0.2f);
            // The ghost's end pose must be exactly where the real trigger goes when it is pulled all the way.
            Vector3 ghostTipWorld = realTrigger.parent.TransformPoint(ghostTriggerFull.position);
            float triggerGap = Vector3.Distance(ghostTipWorld, realTrigger.position);
            float triggerTurn = Quaternion.Angle(realTrigger.parent.rotation * ghostTriggerFull.rotation, realTrigger.rotation);
            Check("v12-ghost-trigger-matches-real-pull", fc.TriggerPull >= 0.999f && triggerGap < 0.0002f && triggerTurn < 0.5f,
                $"real trigger turned {Quaternion.Angle(realTriggerRest.rotation, realTrigger.localRotation):0.0} deg; ghost end pose off by {triggerGap * 1000f:0.00} mm / {triggerTurn:0.00} deg");
            Check("v11-fc-release-prompt", menu.headlineText.text == VRTrainingText.ReleaseTrigger && guide.TooltipText == VRTrainingText.LetGo, menu.headlineText.text);
            yield return WaitFor(() => Said(VoiceLine.FC_LetGo) > 0, 6f);
            Check("v12-confirmation-then-instruction", Said(VoiceLine.FC_SafeOk) == 1 && Said(VoiceLine.FC_LetGo) == 1 && SaidAt(VoiceLine.FC_SafeOk) < SaidAt(VoiceLine.FC_LetGo));
            R.triggerValue = 0f; yield return Hold(0.2f);
            Check("fc2-safe-trigger", Check5() == "SafeOff", Check5());
            R.primary = true; yield return Frames(2); R.primary = false; yield return Frames(2);
            Check("fc3-safety-off", !fc.SafetyOn && Check5() == "DoubleAction", Check5());
            Transform hammerT = (Transform)Priv(Priv(fc, "hammer"), "t");
            Quaternion hammerRest = hammerT.localRotation;
            // v1.2: the trainee starts pulling while the demonstration is still running: it stops, and is not asked again.
            yield return WaitFor(() => ghost.Playing == VRGhostDemo.Demo.TriggerPull && Said(VoiceLine.FC_TriggerDemo_Fire) > 0, 6f);
            bool demoWasRunning = ghost.Playing == VRGhostDemo.Demo.TriggerPull;
            int tryPrompts = Said(VoiceLine.FC_YourTurn_Trigger);
            R.triggerValue = 0.6f; yield return Hold(0.15f);
            Check("v12-trainee-action-stops-demo", demoWasRunning && !ghost.IsPlaying && !coach.DemoPending, $"demo was running={demoWasRunning}, now={ghost.Playing}");
            float hammerTravel = Quaternion.Angle(hammerRest, hammerT.localRotation);
            R.triggerValue = 1f; yield return Hold(0.2f);
            Check("fc4-double-action", Check5() == "RackHeld", $"{Check5()}, hammer drew back {hammerTravel:0.0} deg at 60% pull");
            Check("v11-fc-rack-hint", guide.RackHintVisible && menu.headlineText.text == VRTrainingText.StageTitle(4), menu.headlineText.text);

            // ---- v1.2: slide demonstration, after "Click. The hammer dropped." and "Keep holding the trigger back."
            yield return WaitFor(() => ghost.Playing == VRGhostDemo.Demo.SlideRack, 10f);
            Check("v12-slide-demo-after-confirmation", ghost.Playing == VRGhostDemo.Demo.SlideRack
                                                       && SaidAt(VoiceLine.FC_HammerDropped) < SaidAt(VoiceLine.FC_KeepHolding) && SaidAt(VoiceLine.FC_KeepHolding) < SaidAt(VoiceLine.FC_SlideDemo)
                                                       && Mathf.Abs(GhostStartedAt(VRGhostDemo.Demo.SlideRack) - SaidAt(VoiceLine.FC_SlideDemo)) < 0.05f
                                                       && Said(VoiceLine.FC_YourTurn_Trigger) == tryPrompts,
                $"ghost={ghost.Playing}, try-prompts {Said(VoiceLine.FC_YourTurn_Trigger)} (was {tryPrompts})");
            Transform slideGhost = ghost.SlideGhost;
            string[] ghostPieces = Array.ConvertAll(slideGhost.GetComponentsInChildren<MeshRenderer>(true), r => r.name);
            Check("v12-slide-ghost-is-slide-only", GhostIsInert(slideGhost) && ghostPieces.Length == 6
                                                   && !Array.Exists(ghostPieces, n => n.Contains("Barrel") || n.Contains("Spring") || n.Contains("Locking")),
                string.Join(",", ghostPieces));
            Vector3 realSlideRest = fc.SlidePiece.localPosition;
            Vector3 ghostSlideBack = Vector3.zero;
            yield return WaitFor(() =>
            {
                if (ghost.Travel >= 0.999f) ghostSlideBack = slideGhost.localPosition;
                return ghost.Travel >= 0.999f;
            }, 4f);
            Vector3 slideOffset = slideGhost.position - fc.SlidePiece.position;
            Check("v12-ghost-slide-travel", Mathf.Abs(slideOffset.magnitude - fc.slideTravel) < 0.0005f && Vector3.Dot(slideOffset.normalized, fc.SlideRearDirection.normalized) > 0.999f
                                            && fc.SlideAmount == 0f && fc.SlidePiece.localPosition == realSlideRest,
                $"ghost {slideOffset.magnitude * 1000f:0.0} mm towards the rear (slideTravel {fc.slideTravel * 1000f:0.0} mm); real slide amount {fc.SlideAmount}");

            // B on the pistol hand during the demonstration: shown and explained again, without repeating the praise.
            int slideDemos = GhostStarts(VRGhostDemo.Demo.SlideRack);
            yield return PressSecondary(R);
            yield return WaitFor(() => GhostStarts(VRGhostDemo.Demo.SlideRack) > slideDemos, 6f);
            Check("v12-B-replays-demo", GhostStarts(VRGhostDemo.Demo.SlideRack) == slideDemos + 1 && Said(VoiceLine.FC_SlideDemo) == 2
                                        && Said(VoiceLine.FC_HammerDropped) == 1 && Check5() == "RackHeld" && fc.TriggerPull > 0.8f,
                $"slide demos {GhostStarts(VRGhostDemo.Demo.SlideRack)}, check={Check5()}");
            yield return WaitFor(() => Said(VoiceLine.FC_YourTurn_Rack) > 0, 20f);
            Check("v12-rack-prompt-after-demo", Said(VoiceLine.FC_YourTurn_Rack) == 1 && !ghost.IsPlaying && Check5() == "RackHeld");

            // Rack: the left hand grips the rear of the slide and pulls back while the trigger stays held.
            Transform grip = frame.transform.Find("VR_Slide_Rack_Grip");
            Check("rack-grip-created", grip != null);
            Vector3 rear = fc.SlideRearDirection;
            L.pos = grip.position; L.rot = R.rot; L.grip = false;
            yield return Frames(3);
            L.grip = true;
            yield return Frames(4);
            for (int i = 1; i <= 10; i++)
            {
                L.pos = grip.position + rear * (fc.slideTravel * 1.1f * i / 10f);
                yield return Frames(1);
            }
            Check("slide-follows-hand", fc.SlideAmount > 0.95f, $"slide amount = {fc.SlideAmount:0.00}");
            float slideGap = Vector3.Distance(fc.SlidePiece.parent.TransformPoint(ghostSlideBack), fc.SlidePiece.position);
            Check("v12-ghost-slide-matches-real-rack", fc.SlideAmount >= 0.999f && slideGap < 0.0002f,
                $"real slide fully back; the ghost's end position is {slideGap * 1000f:0.00} mm from it");
            L.grip = false;
            yield return Hold(0.3f);
            Check("fc5-rack-with-trigger-held", Check5() == "Reset" && fc.SlideAmount == 0f, Check5());

            // ---- v1.2: slow-release demonstration, from the full pull the trainee is holding to the reset click
            yield return WaitFor(() => ghost.Playing == VRGhostDemo.Demo.TriggerRelease, 5f);
            float releaseStart = ghost.Travel, releaseEnd = 1f;
            Check("v12-release-demo-from-full-pull", ghost.Playing == VRGhostDemo.Demo.TriggerRelease && releaseStart > 0.999f && Said(VoiceLine.Common_Good) == 1,
                $"ghost starts at {releaseStart:0.000}");
            yield return WaitFor(() =>
            {
                if (ghost.IsPlaying) releaseEnd = Mathf.Min(releaseEnd, ghost.Travel);
                return Said(VoiceLine.FC_YourTurn_Release) > 0;
            }, 15f);
            Check("v12-release-demo-stops-at-reset-point", Mathf.Abs(releaseEnd - FunctionCheck.TriggerResetPoint) < 0.001f && Said(VoiceLine.FC_YourTurn_Release) == 1
                                                           && Check5() == "Reset" && fc.TriggerPull >= 0.999f,
                $"ghost let out to {releaseEnd:0.000} (reset point {FunctionCheck.TriggerResetPoint}); the real trigger is still held at {fc.TriggerPull:0.00}");
            R.triggerValue = 0.5f; yield return Hold(0.15f);
            R.triggerValue = 0f; yield return Hold(0.15f);
            Check("fc6-trigger-reset", Check5() == "SingleAction", Check5());
            R.triggerValue = 1f; yield return Hold(0.2f);
            Check("fc7-single-action-passed", fc.Passed && Check5() == "Done", Check5());
            Check("v11-fc-passed-on-panel", menu.headlineText.text == VRTrainingText.CheckPassed, menu.headlineText.text);
            R.triggerValue = 0f; R.grip = false;
            yield return Hold(2.0f);
            yield return WaitFor(() => Said(VoiceLine.Training_Complete) > 0, 6f);
            Check("v12-passed-and-complete-spoken", Said(VoiceLine.FC_ResetClick) == 1 && Said(VoiceLine.FC_Passed) == 1 && Said(VoiceLine.Training_Complete) == 1
                                                    && SaidAt(VoiceLine.FC_Passed) < SaidAt(VoiceLine.Training_Complete) && !ghost.IsPlaying && !ghost.GhostVisible,
                $"passed at {SaidAt(VoiceLine.FC_Passed):0.0} s, complete at {SaidAt(VoiceLine.Training_Complete):0.0} s");
            Check("v12-voice-never-overlaps", maxVoicesAtOnce == 1, $"most voice clips audible in one frame = {maxVoicesAtOnce} (all AudioSources in the scene checked every frame)");
            Check("v12-no-missing-clip-warnings", missingClipWarnings == 0, $"warnings = {missingClipWarnings}");
            Check("mistakes-count-only-real-attempts", mgr.MistakeCount == 2, $"mistakes={mgr.MistakeCount} (2 deliberate: out of sequence + misaligned)");
            Check("completion-screen", mgr.IsComplete && menu.completePanel.activeSelf && menu.resultText.text.Contains("TIME"), menu.resultText.text.Replace("\n", " / "));
            Check("hud-final-message", hud.DirectiveText.StartsWith("ALL STEPS COMPLETE"), hud.DirectiveText);

            // ---- v1.1: back to the modules without a reload, coming soon, back, then train again
            GunAssemblyManager before = mgr;
            yield return PressButton(R, menu.modulesButton);
            Check("v11-back-to-modules-without-reload", menu.modulesPanel.activeSelf && FindObjectOfType<GunAssemblyManager>() == before && before.IsComplete
                                                        && menu.assemblyModuleStatus.text == VRTrainingText.StatusCompleted, menu.assemblyModuleStatus.text);
            yield return PressButton(R, menu.shootingModuleButton);
            Check("v11-coming-soon-after-training", menu.comingSoonPanel.activeSelf);
            yield return PressButton(R, menu.comingSoonBackButton);
            Check("v11-back-again", menu.modulesPanel.activeSelf);

            // ---- Restart: selecting the finished module trains again from a fresh bench (scene reload)
            yield return PressButton(R, menu.assemblyModuleButton);
            yield return Hold(0.3f);
            FindScene();
            origin = FindObjectOfType<XROrigin>();
            menu = FindObjectOfType<VRTrainingMenu>();
            guide = FindObjectOfType<VRGuidance>();
            // v1.1: "train again" goes straight to step 1 (was: back to the intro screen).
            Check("restart-reloads-clean", menu != null && menu.progressPanel.activeSelf && mgr.HasStarted && mgr.CurrentStepIndex == 0 && !mgr.IsComplete
                                          && AssemblyPart.HeldParts.Count == 0 && SnapSocket.All.Count == 4 && FindObjectsOfType<XRInteractionManager>().Length == 1
                                          && FindObjectsOfType<EventSystem>().Length == 1 && guide.HighlightedPart == P(GunPart.Barrel),
                $"held={AssemblyPart.HeldParts.Count} sockets={SnapSocket.All.Count} started={mgr.HasStarted}");
            yield return GrabAndRelease(L, P(GunPart.Barrel), S("Socket_Barrel"), Quaternion.identity, true);
            yield return Hold(0.5f);
            Check("vr-assembly-after-restart", S("Socket_Barrel").IsFilled && mgr.CurrentStepIndex == 1, $"step={mgr.CurrentStepIndex}");

            // ---- v1.1: Restart button on the training screen
            yield return PressButton(R, menu.restartButton);
            yield return Hold(0.3f);
            FindScene();
            origin = FindObjectOfType<XROrigin>();
            menu = FindObjectOfType<VRTrainingMenu>();
            Check("v11-restart-from-training", menu.progressPanel.activeSelf && mgr.HasStarted && mgr.CurrentStepIndex == 0 && !S("Socket_Barrel").IsFilled,
                $"step={mgr.CurrentStepIndex}");

            // ---- v1.2: a line without a recording is "spoken" silently for about as long as it would take; nothing breaks
            voice = FindObjectOfType<VRVoiceGuide>(); // the reloaded scene's
            VoiceClip entry = Array.Find(voice.clips, c => c.line == VoiceLine.FC_LetGo);
            AudioClip recording = entry.clip;
            entry.clip = null;
            int warningsBefore = missingClipWarnings;
            voice.Stop();
            yield return Hold(0.6f);
            voice.Play(VoiceLine.FC_LetGo);
            yield return Hold(0.5f);
            bool silentButSpeaking = voice.Current == VoiceLine.FC_LetGo && !voice.GetComponent<AudioSource>().isPlaying;
            yield return WaitFor(() => voice.Current != VoiceLine.FC_LetGo, 6f);
            Check("v12-missing-clip-is-graceful", silentButSpeaking && voice.Current != VoiceLine.FC_LetGo && missingClipWarnings == warningsBefore + 1,
                $"silent stand-in ran={silentButSpeaking}, warnings +{missingClipWarnings - warningsBefore}");
            entry.clip = recording;

            InputSystem.RemoveDevice(left);
            InputSystem.RemoveDevice(right);
            InputSystem.RemoveDevice(hmd);
        }

        VRGuidance guide;

        // ------------------------------------------------------------------ v1.2: voice + ghost helpers

        VRTrainingCoach coach;
        VRVoiceGuide voice;
        VRGhostDemo ghost;
        readonly List<VoiceLine> spoken = new List<VoiceLine>();
        readonly List<(VoiceLine line, float time)> spokenAt = new List<(VoiceLine, float)>();
        readonly List<(VRGhostDemo.Demo demo, float time)> ghostStarts = new List<(VRGhostDemo.Demo, float)>();
        int maxVoicesAtOnce;
        int missingClipWarnings;

        // Finds the v1.2 objects (again after a scene reload) and records every spoken line and ghost start.
        void HookCoach()
        {
            coach = FindObjectOfType<VRTrainingCoach>();
            voice = FindObjectOfType<VRVoiceGuide>();
            ghost = FindObjectOfType<VRGhostDemo>();
            if (voice != null) voice.LineStarted += l => { spoken.Add(l); spokenAt.Add((l, Time.time)); };
            if (ghost != null) ghost.Started += d => ghostStarts.Add((d, Time.time));
        }

        int Said(VoiceLine l) => spoken.FindAll(x => x == l).Count;
        float SaidAt(VoiceLine l) { int i = spokenAt.FindLastIndex(x => x.line == l); return i < 0 ? -1f : spokenAt[i].time; }
        int GhostStarts(VRGhostDemo.Demo d) => ghostStarts.FindAll(x => x.demo == d).Count;
        float GhostStartedAt(VRGhostDemo.Demo d) { int i = ghostStarts.FindLastIndex(x => x.demo == d); return i < 0 ? -1f : ghostStarts[i].time; }

        // Independent of VRVoiceGuide: counts every AudioSource in the scene that is playing a voice clip, each frame.
        IEnumerator VoiceOverlapMonitor()
        {
            while (true)
            {
                int n = 0;
                foreach (AudioSource s in FindObjectsOfType<AudioSource>())
                    if (s.isPlaying && s.clip != null && s.clip.name.StartsWith("VO_")) n++;
                maxVoicesAtOnce = Mathf.Max(maxVoicesAtOnce, n);
                yield return null;
            }
        }

        // Keeps both simulated hands where they are (still holding) until 'condition' is true or 'timeout' passes.
        IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            for (float t = 0f; t < timeout && !condition(); t += Time.deltaTime)
            {
                Send(L); Send(R);
                yield return null;
            }
        }

        // Presses B (right) or Y (left) for a moment.
        IEnumerator PressSecondary(Hand h)
        {
            h.secondary = true;
            yield return Frames(2);
            h.secondary = false;
            yield return Frames(2);
        }

        static bool GhostIsInert(Transform root)
        {
            if (root == null || root.GetComponent<GhostVisual>() == null) return false;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.layer != 2 || t.GetComponent<Collider>() != null || t.GetComponent<XRBaseInteractable>() != null
                    || t.GetComponent<AssemblyPart>() != null || t.GetComponent<Rigidbody>() != null) return false;
            return true;
        }

        static Pose LocalPose(Transform t) => new Pose(t.localPosition, t.localRotation);

        static Pose SeatPose(SnapSocket s) => new Pose(s.transform.position, s.transform.rotation);

        // Carries a held part so that it would land on 'target' (the hand keeps its grab offset), for 'seconds'.
        // The offset is measured now, while the part is away from its seat, so the placement assist is not involved.
        IEnumerator CarryTo(Hand h, AssemblyPart part, Func<Pose> target, float seconds, Action eachFrame = null)
        {
            Vector3 handInPart = part.transform.InverseTransformPoint(h.pos);
            Quaternion handRotInPart = Quaternion.Inverse(part.transform.rotation) * h.rot;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Pose p = target();
                h.pos = Matrix4x4.TRS(p.position, p.rotation, part.transform.lossyScale).MultiplyPoint3x4(handInPart);
                h.rot = p.rotation * handRotInPart;
                yield return Frames(1);
                eachFrame?.Invoke();
            }
        }

        // Same with both hands on a two-handed part.
        IEnumerator CarryBothTo(AssemblyPart part, Func<Pose> target, float seconds)
        {
            Vector3 lIn = part.transform.InverseTransformPoint(L.pos), rIn = part.transform.InverseTransformPoint(R.pos);
            Quaternion lRot = Quaternion.Inverse(part.transform.rotation) * L.rot, rRot = Quaternion.Inverse(part.transform.rotation) * R.rot;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Pose p = target();
                Matrix4x4 m = Matrix4x4.TRS(p.position, p.rotation, part.transform.lossyScale);
                L.pos = m.MultiplyPoint3x4(lIn); L.rot = p.rotation * lRot;
                R.pos = m.MultiplyPoint3x4(rIn); R.rot = p.rotation * rRot;
                yield return Frames(1);
            }
        }

        // Moves the hand away from every socket before letting go, so the release is not a seating attempt.
        IEnumerator ReleaseAt(Hand h, Vector3 restPos)
        {
            h.pos = restPos;
            h.rot = Quaternion.identity;
            yield return Frames(4);
            h.grip = false;
            yield return Frames(2);
        }

        ActionBasedController FindController(string n)
        {
            // Hands rig: the modality manager knows which object is the controller (hand objects also carry an ActionBasedController).
            var modality = origin.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager>(true);
            GameObject m = modality == null ? null : n.StartsWith("Left") ? modality.leftController : modality.rightController;
            if (m != null && m.GetComponentInChildren<ActionBasedController>(true) is ActionBasedController fromModality) return fromModality;
            foreach (ActionBasedController c in origin.GetComponentsInChildren<ActionBasedController>()) if (c.name == n) return c;
            return null;
        }

        bool IsHeldBy(XRGrabInteractable g, XRSimulatedController device)
        {
            ActionBasedController c = FindController(device == left ? "Left Controller" : "Right Controller");
            foreach (IXRSelectInteractor i in g.interactorsSelecting)
                if (i.transform.IsChildOf(c.transform)) return true;
            return false;
        }

        // Near grab: hand moved onto the part's collider, then grip pressed.
        IEnumerator GrabAt(Hand h, AssemblyPart part)
        {
            h.grip = false;
            h.pos = part.Colliders[0].bounds.center;
            yield return Frames(3);
            h.grip = true;
            yield return Frames(4);
        }

        // Moves the hand (and the part it holds) so the part ends at 'target', keeping the grab offset.
        IEnumerator LiftTo(Hand h, AssemblyPart part, Vector3 target)
        {
            Vector3 offset = h.pos - part.transform.position;
            h.pos = target + offset;
            yield return Hold(0.4f); // attach ease-in
        }

        // Grabs a part, carries it so that it lands exactly on the socket pose (times 'extra'), then lets go.
        IEnumerator GrabAndRelease(Hand h, AssemblyPart part, SnapSocket socket, Quaternion extra, bool expectAligned)
        {
            yield return GrabAt(h, part);
            if (!part.IsHeld) { Check("grab-" + part.name, false, "grip did not pick the part up"); yield break; }
            yield return Hold(0.3f);
            // Hand pose relative to the part stays constant while held.
            Vector3 handInPart = part.transform.InverseTransformPoint(h.pos);
            Quaternion handRotInPart = Quaternion.Inverse(part.transform.rotation) * h.rot;
            Quaternion targetRot = socket.transform.rotation * extra;
            for (int i = 0; i < 20; i++) // follow the socket in case its host moves
            {
                Matrix4x4 target = Matrix4x4.TRS(socket.transform.position, targetRot, part.transform.lossyScale);
                h.pos = target.MultiplyPoint3x4(handInPart);
                h.rot = targetRot * handRotInPart;
                yield return Frames(1);
            }
            if (expectAligned) Check($"aligned-{part.name}", socket.CurrentAlignment == SnapSocket.Alignment.Valid,
                $"alignment={socket.CurrentAlignment} {socket.LastDistance * 1000f:0.0} mm {socket.LastAngle:0.0} deg");
            // Let go first: turning the hand in the same frame could carry the part onto the seat rotation as it is released.
            h.grip = false;
            yield return Frames(2);
            h.rot = Quaternion.identity;
        }

        // Points the hand's ray at a UI button and clicks with the trigger.
        IEnumerator PressButton(Hand h, Button button)
        {
            ActionBasedController c = FindController(h.device == left ? "Left Controller" : "Right Controller");
            XRRayInteractor ray = c.GetComponentInChildren<XRRayInteractor>();
            Vector3 target = button.transform.position;
            h.grip = false; h.trigger = false; h.triggerValue = -1f;
            h.pos = origin.transform.position + new Vector3(0f, 1.3f, 0.15f);
            // Point the controller at the button once. The Starter Assets ray follows a smoothed ("stabilized") copy
            // of the controller pose, so wait for it to settle instead of correcting while it is still moving.
            h.rot = Quaternion.LookRotation(target - h.pos);
            for (int i = 0; i < 120 && Vector3.Angle(ray.rayOriginTransform.forward, target - ray.rayOriginTransform.position) > 0.5f; i++)
                yield return Frames(1);
            // Like a user: hold still until the ray is settled over the button, then pull the trigger.
            for (int i = 0; i < 60 && !ray.IsOverUIGameObject(); i++) yield return Frames(1);
            yield return Frames(10);
            bool hit = ray.TryGetCurrentUIRaycastResult(out RaycastResult result) && result.gameObject != null && result.gameObject.transform.IsChildOf(button.transform);
            Transform ro = ray.rayOriginTransform;
            ray.TryGetCurrentRaycast(out RaycastHit? hit3d, out int _, out RaycastResult? uiHit, out int _, out bool isUI);
            Info("ray-on-" + button.name, (hit ? result.gameObject.name : "no UI hit")
                + $" | rayActive={ray.isActiveAndEnabled} ui={ray.enableUIInteraction} originPos={ro.position} fwd={ro.forward}"
                + $" angleToTarget={Vector3.Angle(ro.forward, target - ro.position):0.0} 3d={(hit3d.HasValue && hit3d.Value.collider != null ? hit3d.Value.collider.name : "-")}"
                + $" ui2={(uiHit.HasValue && uiHit.Value.gameObject != null ? uiHit.Value.gameObject.name : "-")} es={EventSystem.current?.name}");
            bool clicked = false;
            UnityEngine.Events.UnityAction onClick = () => clicked = true;
            button.onClick.AddListener(onClick);
            h.trigger = true;
            yield return Frames(3);
            Info("ui-press-state", $"uiPress={c.uiPressInteractionState.active} overUI={ray.IsOverUIGameObject()} activate={c.activateInteractionState.active}");
            h.trigger = false;
            yield return Frames(3);
            if (button == null) yield break; // the click reloaded the scene (Restart)
            button.onClick.RemoveListener(onClick);
            Info("ui-clicked-" + button.name, clicked.ToString());
        }
    }
}
#endif
