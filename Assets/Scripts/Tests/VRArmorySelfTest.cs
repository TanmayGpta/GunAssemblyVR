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
        IEnumerator Watchdog()
        {
            yield return new WaitForSecondsRealtime(240f);
            Check("watchdog-timeout", false, "test did not finish within 240 s");
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
            public bool grip, trigger, primary;
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
            Check("vr-mode-active", VRPlatformSwitch.IsVR && origin != null && GameObject.Find("Armorer_Player") == null);
            Check("single-xr-manager", FindObjectsOfType<XRInteractionManager>().Length == 1, $"count={FindObjectsOfType<XRInteractionManager>().Length}");
            Check("single-event-system", FindObjectsOfType<EventSystem>().Length == 1);
            Check("single-audio-listener", FindObjectsOfType<AudioListener>().Length == 1);
            Check("single-enabled-camera", Array.FindAll(FindObjectsOfType<Camera>(), c => c.enabled).Length == 1);
            Check("vr-waits-for-start", !mgr.HasStarted && menu.introPanel.activeSelf, "intro panel shown, assembly locked until START");
            Check("fc-desktop-keys-off", !fc.desktopInput && fc.triggerControl.Contains("TRIGGER"));
            Check("all-part-grabs-enabled", Array.TrueForAll(FindObjectsOfType<VRAssemblyGrab>(true), g => g.enabled));
            int shadowLights = Array.FindAll(FindObjectsOfType<Light>(), l => l.shadows != LightShadows.None).Length;
            Check("quest-shadow-budget", shadowLights <= 2, $"shadow-casting lights in VR = {shadowLights}");

            hmd = InputSystem.AddDevice<XRSimulatedHMD>();
            left = InputSystem.AddDevice<XRSimulatedController>("SimLeft");
            right = InputSystem.AddDevice<XRSimulatedController>("SimRight");
            InputSystem.SetDeviceUsage(left, InputCommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(right, InputCommonUsages.RightHand);
            Vector3 stand = origin.transform.position;
            L = new Hand { device = left, pos = stand + new Vector3(-0.25f, 1.1f, 0.3f) };
            R = new Hand { device = right, pos = stand + new Vector3(0.25f, 1.1f, 0.3f) };
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

            // ---- UI: point the right ray at START and pull the trigger
            yield return PressButton(R, menu.startButton);
            Check("ui-start-button", mgr.HasStarted && menu.progressPanel.activeSelf && S("Socket_Barrel").IsUnlocked,
                $"started={mgr.HasStarted}");

            // ---- Rejections through real grabs
            yield return GrabAndRelease(L, P(GunPart.RecoilSpring), S("Socket_RecoilSpring"), Quaternion.identity, false);
            Check("vr-out-of-sequence-rejected", !S("Socket_RecoilSpring").IsFilled && mgr.MistakeCount == 1, $"mistakes={mgr.MistakeCount} hud='{hud.DirectiveText}'");
            yield return Hold(0.5f);
            Check("vr-part-returned-to-bench", P(GunPart.RecoilSpring).IsGrabbable && !P(GunPart.RecoilSpring).IsHeld);

            yield return GrabAndRelease(R, P(GunPart.Barrel), S("Socket_Barrel"), Quaternion.Euler(0f, 0f, 40f), false);
            Check("vr-misaligned-rejected", !S("Socket_Barrel").IsFilled && hud.DirectiveText.Contains("MISALIGNED"), hud.DirectiveText);
            yield return Hold(0.5f);

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
            L.pos = stand + new Vector3(-0.25f, 1.1f, 0.3f);
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
            XRDirectInteractor rDirect = FindController("Right Controller").GetComponentInChildren<XRDirectInteractor>();
            var rTargets = new List<IXRInteractable>();
            rDirect.GetValidTargets(rTargets);
            Info("right-direct-state", $"active={rDirect.isActiveAndEnabled} selectActive={rDirect.isSelectActive} targets=[{string.Join(",", rTargets.ConvertAll(t => t.transform.name))}]"
                + $" selecting=[{string.Join(",", rDirect.interactablesSelected.ConvertAll(t => t.transform.name))}] handPos={rDirect.transform.position} slideBounds={slide.Colliders[0].bounds}");
            float jumpJoin = Vector3.Distance(p0, slide.transform.position), turnJoin = Quaternion.Angle(r0, slide.transform.rotation);
            Check("two-handed-grab", slideGrab.interactorsSelecting.Count == 2, $"holders={slideGrab.interactorsSelecting.Count}");
            Check("two-handed-no-jump-on-join", jumpJoin < 0.01f && turnJoin < 3f, $"moved {jumpJoin * 1000f:0.0} mm, turned {turnJoin:0.0} deg");
            p0 = slide.transform.position; r0 = slide.transform.rotation;
            L.grip = false;
            yield return Hold(0.4f);
            float jumpLeave = Vector3.Distance(p0, slide.transform.position), turnLeave = Quaternion.Angle(r0, slide.transform.rotation);
            Check("release-one-hand-keeps-holding", slideGrab.interactorsSelecting.Count == 1 && slide.IsHeld, $"holders={slideGrab.interactorsSelecting.Count}");
            Check("two-handed-no-jump-on-release", jumpLeave < 0.01f && turnLeave < 3f, $"moved {jumpLeave * 1000f:0.0} mm, turned {turnLeave:0.0} deg");

            // ---- Step 1 while the slide is held in the air: barrel seated by the other hand
            L.grip = false;
            yield return Frames(2);
            yield return GrabAndRelease(L, P(GunPart.Barrel), S("Socket_Barrel"), Quaternion.identity, true);
            yield return Hold(0.4f);
            Check("vr-step1-seated-into-held-slide", S("Socket_Barrel").IsFilled && P(GunPart.Barrel).transform.parent == slide.transform && mgr.CurrentStepIndex == 1,
                $"step={mgr.CurrentStepIndex}");
            Check("vr-step-complete-message", hud.DirectiveText.Contains("COMPLETE"), hud.DirectiveText);
            R.grip = false;   // put the slide back
            yield return Hold(0.6f);
            Check("held-slide-returned-with-barrel", !slide.IsHeld && P(GunPart.Barrel).transform.parent == slide.transform);

            // ---- Steps 2-4
            yield return GrabAndRelease(R, P(GunPart.RecoilSpring), S("Socket_RecoilSpring"), Quaternion.identity, true);
            yield return Hold(0.5f);
            Check("vr-step2-seated", S("Socket_RecoilSpring").IsFilled && mgr.CurrentStepIndex == 2);
            yield return GrabAndRelease(L, slide, S("Socket_Slide"), Quaternion.identity, true);
            yield return Hold(0.5f);
            Check("vr-step3-seated", S("Socket_Slide").IsFilled && slide.transform.parent == P(GunPart.LowerFrame).transform && mgr.CurrentStepIndex == 3);
            yield return GrabAndRelease(R, P(GunPart.Magazine), S("Socket_Magazine"), Quaternion.identity, true);
            yield return Hold(2.0f);
            Check("vr-step4-seated-function-check-begins", S("Socket_Magazine").IsFilled && mgr.CurrentStepIndex == 4 && fc.IsRunning, $"step={mgr.CurrentStepIndex} running={fc.IsRunning}");
            Check("vr-fc-directive-uses-vr-controls", hud.DirectiveText.Contains("A / X"), hud.DirectiveText);

            // ---- Function check with controllers
            AssemblyPart frame = P(GunPart.LowerFrame);
            yield return GrabAt(R, frame);
            yield return LiftTo(R, frame, origin.transform.position + new Vector3(0.05f, 1.15f, 0.45f));
            Check("pistol-held", frame.IsHeld);
            R.primary = true; yield return Frames(2); R.primary = false; yield return Frames(2);
            Check("fc1-safety-on-with-A", fc.SafetyOn && Check5() == "SafeTrigger", Check5());
            R.triggerValue = 1f; yield return Hold(0.2f);
            R.triggerValue = 0f; yield return Hold(0.2f);
            Check("fc2-safe-trigger", Check5() == "SafeOff", Check5());
            R.primary = true; yield return Frames(2); R.primary = false; yield return Frames(2);
            Check("fc3-safety-off", !fc.SafetyOn && Check5() == "DoubleAction", Check5());
            Transform hammerT = (Transform)Priv(Priv(fc, "hammer"), "t");
            Quaternion hammerRest = hammerT.localRotation;
            R.triggerValue = 0.6f; yield return Hold(0.15f);
            float hammerTravel = Quaternion.Angle(hammerRest, hammerT.localRotation);
            R.triggerValue = 1f; yield return Hold(0.2f);
            Check("fc4-double-action", Check5() == "RackHeld", $"{Check5()}, hammer drew back {hammerTravel:0.0} deg at 60% pull");

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
            L.grip = false;
            yield return Hold(0.3f);
            Check("fc5-rack-with-trigger-held", Check5() == "Reset" && fc.SlideAmount == 0f, Check5());
            R.triggerValue = 0.5f; yield return Hold(0.15f);
            R.triggerValue = 0f; yield return Hold(0.15f);
            Check("fc6-trigger-reset", Check5() == "SingleAction", Check5());
            R.triggerValue = 1f; yield return Hold(0.2f);
            Check("fc7-single-action-passed", fc.Passed && Check5() == "Done", Check5());
            R.triggerValue = 0f; R.grip = false;
            yield return Hold(2.0f);
            Check("mistakes-count-only-real-attempts", mgr.MistakeCount == 2, $"mistakes={mgr.MistakeCount} (2 deliberate: out of sequence + misaligned)");
            Check("completion-screen", mgr.IsComplete && menu.completePanel.activeSelf && menu.resultText.text.Contains("TIME"), menu.resultText.text.Replace("\n", " / "));
            Check("hud-final-message", hud.DirectiveText.StartsWith("ALL STEPS COMPLETE"), hud.DirectiveText);

            // ---- Restart from the completion screen
            Button again = menu.restartButtons[1];
            yield return PressButton(R, again);
            yield return Hold(0.3f);
            FindScene();
            origin = FindObjectOfType<XROrigin>();
            menu = FindObjectOfType<VRTrainingMenu>();
            Check("restart-reloads-clean", menu != null && menu.introPanel.activeSelf && !mgr.HasStarted && AssemblyPart.HeldParts.Count == 0
                                          && SnapSocket.All.Count == 4 && FindObjectsOfType<XRInteractionManager>().Length == 1,
                $"held={AssemblyPart.HeldParts.Count} sockets={SnapSocket.All.Count}");
            yield return PressButton(R, menu.startButton);
            yield return GrabAndRelease(L, P(GunPart.Barrel), S("Socket_Barrel"), Quaternion.identity, true);
            yield return Hold(0.5f);
            Check("vr-assembly-after-restart", S("Socket_Barrel").IsFilled && mgr.CurrentStepIndex == 1, $"step={mgr.CurrentStepIndex}");

            InputSystem.RemoveDevice(left);
            InputSystem.RemoveDevice(right);
            InputSystem.RemoveDevice(hmd);
        }

        ActionBasedController FindController(string n)
        {
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
            h.grip = false;
            h.rot = Quaternion.identity;
            yield return Frames(2);
        }

        // Points the hand's ray at a UI button and clicks with the trigger.
        IEnumerator PressButton(Hand h, Button button)
        {
            ActionBasedController c = FindController(h.device == left ? "Left Controller" : "Right Controller");
            XRRayInteractor ray = c.GetComponentInChildren<XRRayInteractor>();
            Vector3 target = button.transform.position;
            h.grip = false; h.trigger = false; h.triggerValue = -1f;
            h.pos = origin.transform.position + new Vector3(0f, 1.3f, 0.15f);
            // Aim the ray origin at the button. The Starter Assets ray follows a smoothed ("stabilized") copy of the
            // controller, so it lags behind: keep correcting until it points at the button.
            for (int i = 0; i < 60 && Vector3.Angle(ray.rayOriginTransform.forward, target - ray.rayOriginTransform.position) > 0.5f; i++)
            {
                Transform rayOrigin = ray.rayOriginTransform;
                Quaternion correction = Quaternion.FromToRotation(rayOrigin.forward, (target - rayOrigin.position).normalized);
                h.rot = correction * h.rot;
                yield return Frames(3);
            }
            bool hit = ray.TryGetCurrentUIRaycastResult(out RaycastResult result) && result.gameObject != null && result.gameObject.transform.IsChildOf(button.transform);
            Transform ro = ray.rayOriginTransform;
            ray.TryGetCurrentRaycast(out RaycastHit? hit3d, out int _, out RaycastResult? uiHit, out int _, out bool isUI);
            Info("ray-on-" + button.name, (hit ? result.gameObject.name : "no UI hit")
                + $" | rayActive={ray.isActiveAndEnabled} ui={ray.enableUIInteraction} originPos={ro.position} fwd={ro.forward}"
                + $" angleToTarget={Vector3.Angle(ro.forward, target - ro.position):0.0} 3d={(hit3d.HasValue ? hit3d.Value.collider.name : "-")}"
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
