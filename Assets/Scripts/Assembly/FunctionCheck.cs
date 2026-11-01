using System;
using UnityEngine;

namespace VRArmory
{
    // Step 05: FM 3-23.35 function check on the assembled pistol. Simulates the Beretta 92 FS lockwork
    // (safety/decocker, DA/SA hammer, trigger disconnect + reset, slide cocking the hammer) and animates the
    // Ambi_Safety_Lever, Hammer, Trigger_Shoe and slide. Desktop input: V safety, hold T trigger, hold G rack slide.
    // In VR, VRFunctionCheckControls turns this desktop input off and drives the same Set* methods instead.
    public class FunctionCheck : MonoBehaviour
    {
        public AssemblyPart frame;
        public AssemblyPart slide;
        public float hammerCockAngle = 55f;
        public float triggerPullAngle = 18f;
        public float safetyAngle = 60f;
        public float slideTravel = 0.045f;
        public float triggerPullTime = 0.35f;
        public float slideRackTime = 0.18f;

        [Header("Input")]
        [Tooltip("Read the V / T / G keys. Turned off by the VR controls, which call the Set* methods directly.")]
        public bool desktopInput = true;
        // Control names shown in the HUD directives.
        public string safetyControl = "[V]";
        public string triggerControl = "[HOLD T]";
        public string rackControl = "[HOLD G, THEN RELEASE]";

        public event Action Completed;
        public event Action HammerFell;
        // Short event messages ("RESET", "DOUBLE ACTION - HAMMER FELL", ...) as they are shown on the HUD.
        public event Action<string> Notified;
        public bool IsRunning { get; private set; }
        public bool Passed { get; private set; }
        // Current check, 0..6 (StageCount once passed), so other UIs can explain it.
        public int Stage => (int)check;
        public const int StageCount = 7;
        public bool SafeTriggerPulled => safeTriggerPulled;
        public bool SafetyOn => safetyOn;
        public float TriggerPull => triggerPull;
        public float SlideAmount => slideAmount;
        public Vector3 SlideRearDirection => frame != null ? frame.transform.TransformDirection(-muzzleLocal) : Vector3.back;

        private enum Check { SafeOn, SafeTrigger, SafeOff, DoubleAction, RackHeld, Reset, SingleAction, Done }

        // {0} = safety control, {1} = trigger control, {2} = rack control.
        private static readonly string[] Directives =
        {
            "FUNCTION CHECK 1/7 : PLACE SAFETY ON  {0}",
            "FUNCTION CHECK 2/7 : SAFE - SQUEEZE TRIGGER FULLY  {1}  (HAMMER MUST NOT FALL)",
            "FUNCTION CHECK 3/7 : RELEASE TRIGGER, PLACE SAFETY OFF / FIRE  {0}",
            "FUNCTION CHECK 4/7 : DOUBLE ACTION - SQUEEZE TRIGGER FULLY  {1}",
            "FUNCTION CHECK 5/7 : KEEP TRIGGER HELD - RACK THE SLIDE  {2}",
            "FUNCTION CHECK 6/7 : SLOWLY RELEASE TRIGGER - FEEL FOR THE RESET",
            "FUNCTION CHECK 7/7 : SINGLE ACTION - SQUEEZE TRIGGER  {1}",
            "FUNCTION CHECK PASSED - WEAPON FUNCTIONAL",
        };

        private static readonly Color Amber = new Color(1.0f, 0.72f, 0.0f);
        private static readonly Color Green = new Color(0.0f, 1.0f, 0.55f);

        // Lockwork state.
        private bool safetyOn;
        private bool hammerCocked;
        private bool needsReset;
        private float triggerPull, slideAmount;
        private float hammerAngle, safetyLeverAngle;
        private bool safeTriggerPulled, rackedWhileHeld;

        private Check check;
        private AssemblyHUD hud;
        private float lastEventTimer;

        private Piece hammer, trigger, lever;
        private Transform slideT;
        private Vector3 slideRestLocal, slideBackLocal;
        private Vector3 muzzleLocal = Vector3.forward;
        private bool rigBuilt;
        private AudioSource audioSource;
        private AudioClip fallClip, resetClip, leverClip;

        // A moving piece rotated about a pivot/axis stored in its parent's space, so it follows the gun.
        private class Piece
        {
            public Transform t;
            public Vector3 restPos;
            public Quaternion restRot;
            public Vector3 pivot, axis;

            public void Apply(float angle)
            {
                if (t == null) return;
                t.localPosition = restPos;
                t.localRotation = restRot;
                if (angle != 0f) t.RotateAround(t.parent.TransformPoint(pivot), t.parent.TransformDirection(axis), angle);
            }
        }

        public void Begin(AssemblyHUD targetHud)
        {
            if (IsRunning) return; // already running: rebuilding the rig now would record animated poses as "rest"
            hud = targetHud;
            if (frame == null || slide == null)
            {
                Debug.LogWarning("[VR Armory] FunctionCheck needs frame and slide references.");
                return;
            }
            if (!rigBuilt) BuildRig();
            IsRunning = true;
            check = Check.SafeOn;
            Say(null);
        }

        void Update()
        {
            if (!IsRunning) return;

            // Desktop keys only while the cursor is captured and the main hand is free. The lockwork animation
            // below runs regardless, so VR (or any other caller of the Set* methods) is animated too.
            if (desktopInput && Cursor.lockState == CursorLockMode.Locked && AssemblyPart.HeldParts.Count == 0)
            {
                if (Input.GetKeyDown(KeyCode.V)) SetSafety(!safetyOn);
                SetTrigger(Mathf.MoveTowards(triggerPull, Input.GetKey(KeyCode.T) ? 1f : 0f, Time.deltaTime / (Input.GetKey(KeyCode.T) ? triggerPullTime : 0.15f)));
                SetSlide(Mathf.MoveTowards(slideAmount, Input.GetKey(KeyCode.G) ? 1f : 0f, Time.deltaTime / slideRackTime));
            }

            Animate();
            if (lastEventTimer > 0f && (lastEventTimer -= Time.deltaTime) <= 0f) Say(null);
        }

        // ---------- Lockwork ----------

        public void SetSafety(bool on)
        {
            if (on == safetyOn) return;
            safetyOn = on;
            Play(leverClip, 0.6f);
            if (on && hammerCocked)
            {
                // 92 FS decocker: engaging the safety drops a cocked hammer onto the blocked firing pin.
                hammerCocked = false;
                Play(fallClip, 0.8f);
                Notify("DECOCKED - HAMMER DROPPED SAFELY");
            }
            if (check == Check.SafeOn && on) Advance();
            else if (check == Check.SafeOff && !on && triggerPull < 0.2f) Advance();
        }

        public void SetTrigger(float pull)
        {
            float prev = triggerPull;
            triggerPull = pull;

            if (needsReset && pull < 0.3f && prev >= 0.3f)
            {
                needsReset = false;
                Play(resetClip, 0.5f);
                Notify("RESET");
                if (check == Check.Reset && hammerCocked) Advance();
            }

            if (safetyOn)
            {
                // Safety disconnects the trigger bar: full travel, hammer never moves.
                if (check == Check.SafeTrigger && pull >= 1f && !safeTriggerPulled)
                {
                    safeTriggerPulled = true;
                    Notify("TRIGGER DISCONNECTED - HAMMER DID NOT FALL - OK");
                }
                if (check == Check.SafeTrigger && safeTriggerPulled && pull < 0.2f) Advance();
                return;
            }

            if (needsReset) return;
            if (hammerCocked && pull >= 0.4f) DropHammer(false);
            else if (!hammerCocked && pull >= 1f && slideAmount < 0.05f) DropHammer(true);
        }

        public void SetSlide(float amount)
        {
            float prev = slideAmount;
            slideAmount = amount;
            if (amount >= 0.7f && prev < 0.7f)
            {
                hammerCocked = !safetyOn; // on SAFE the hammer follows the slide back down
                if (check == Check.RackHeld && triggerPull > 0.8f) rackedWhileHeld = true;
            }
            if (amount <= 0f && prev > 0f && check == Check.RackHeld)
            {
                // needsReset = the trigger is still held from the double-action pull. If it was let go before the
                // slide went forward, the reset already happened and check 6 could never be reached.
                if (rackedWhileHeld && hammerCocked && needsReset) Advance();
                else
                {
                    Notify(rackedWhileHeld ? "TRIGGER RELEASED TOO EARLY - SQUEEZE, HOLD, RACK AGAIN" : "HOLD THE TRIGGER BACK WHILE RACKING");
                    rackedWhileHeld = false;
                }
            }
        }

        private void DropHammer(bool doubleAction)
        {
            hammerCocked = false;
            needsReset = true;
            Play(fallClip, 1f);
            HammerFell?.Invoke();
            Notify(doubleAction ? "DOUBLE ACTION - HAMMER FELL" : "SINGLE ACTION - HAMMER FELL");
            if (check == Check.DoubleAction && doubleAction) Advance();
            else if (check == Check.SingleAction && !doubleAction) Advance();
        }

        private void Advance()
        {
            check++;
            Say(null);
            if (check == Check.Done)
            {
                // From here on the manager owns the HUD (final summary); the lockwork stays operable but silent.
                Passed = true;
                lastEventTimer = 0f;
                Completed?.Invoke();
            }
        }

        // ---------- Visuals ----------

        private void Animate()
        {
            float target;
            if (slideAmount > 0f) target = Mathf.Max(hammerCocked ? hammerCockAngle : 0f, Mathf.Clamp01(slideAmount / 0.7f) * hammerCockAngle);
            else if (hammerCocked) target = hammerCockAngle;
            else if (!safetyOn && !needsReset) target = triggerPull * hammerCockAngle * 0.95f; // DA pull draws the hammer back
            else target = 0f;
            // Hammer falls fast, cocks at trigger/slide speed.
            hammerAngle = target < hammerAngle ? Mathf.MoveTowards(hammerAngle, target, 1500f * Time.deltaTime) : target;
            safetyLeverAngle = Mathf.MoveTowards(safetyLeverAngle, safetyOn ? safetyAngle : 0f, 600f * Time.deltaTime);

            hammer?.Apply(hammerAngle);
            trigger?.Apply(triggerPull * triggerPullAngle);
            lever?.Apply(safetyLeverAngle);
            if (slideT != null) slideT.localPosition = Vector3.Lerp(slideRestLocal, slideBackLocal, slideAmount);
        }

        private void BuildRig()
        {
            Transform f = frame.transform;
            slideT = slide.transform;
            slideRestLocal = slideT.localPosition;

            // Gun axes in frame space: muzzle = slide's long axis (away from the rear sight), up = frame -> slide.
            Bounds sb = LocalBounds(f, slideT), fb = LocalBounds(f, f);
            Vector3 muzzle = LongestAxis(sb.size);
            Transform rearSight = FindPiece(slideT, "Rear_Sight");
            if (rearSight != null && Vector3.Dot(f.InverseTransformPoint(rearSight.position) - sb.center, muzzle) > 0f) muzzle = -muzzle;
            Vector3 upRaw = sb.center - fb.center;
            upRaw -= Vector3.Dot(upRaw, muzzle) * muzzle;
            Vector3 up = Cardinal(upRaw);
            Vector3 lateral = Vector3.Cross(muzzle, up).normalized;
            muzzleLocal = muzzle;

            slideBackLocal = slideRestLocal - muzzle * (slideTravel / f.lossyScale.x);

            hammer = MakePiece(f, FindPiece(f, "Hammer"), muzzle, up, lateral, -1f, true);
            trigger = MakePiece(f, FindPiece(f, "Trigger_Shoe"), muzzle, up, lateral, 1f, false);

            // The slide carries both Ambi_Safety_Lever and Safety_Axle; a plain "Safety" search picked the axle.
            Transform leverT = FindPiece(slideT, "Ambi_Safety_Lever");
            if (leverT == null) leverT = FindPiece(slideT, "Safety");
            lever = MakePiece(f, leverT, muzzle, up, lateral, 0f, false);
            Transform axle = FindPiece(slideT, "Safety_Axle");
            if (lever != null && axle != null)
            {
                // The lever turns about its axle, not about its own centre.
                lever.pivot = lever.t.parent.InverseTransformPoint(f.TransformPoint(LocalBounds(f, axle).center));
            }

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            fallClip = ProceduralAudio.MakeClick("Hammer_Fall", 0.035f, 2200f, 1f);
            resetClip = ProceduralAudio.MakeClick("Trigger_Reset", 0.012f, 4200f, 0.5f);
            leverClip = ProceduralAudio.MakeClick("Safety_Lever", 0.02f, 3000f, 0.6f);
            rigBuilt = true;
        }

        // pivotEnd: -1 = rotate about the end opposite 'up' (hammer base), +1 = the 'up' end (trigger top), 0 = centre.
        // The angle sign is chosen so the far end moves rearward (hammer cocking, trigger pull).
        private static Piece MakePiece(Transform frameT, Transform t, Vector3 muzzle, Vector3 up, Vector3 lateral, float pivotEnd, bool farEndUp)
        {
            if (t == null || t.parent == null) return null;
            Transform p = t.parent;
            Bounds b = LocalBounds(frameT, t);
            Vector3 pivotF = b.center + up * (pivotEnd * Vector3.Scale(b.extents, Abs(up)).magnitude * 0.85f);
            Vector3 farF = b.center + up * ((pivotEnd == 0f ? (farEndUp ? 1f : -1f) : -pivotEnd) * Vector3.Scale(b.extents, Abs(up)).magnitude);
            Vector3 axisF = lateral;
            Vector3 moved = Quaternion.AngleAxis(10f, axisF) * (farF - pivotF);
            if (Vector3.Dot(moved - (farF - pivotF), -muzzle) < 0f) axisF = -axisF;

            return new Piece
            {
                t = t,
                restPos = t.localPosition,
                restRot = t.localRotation,
                pivot = p.InverseTransformPoint(frameT.TransformPoint(pivotF)),
                axis = p.InverseTransformDirection(frameT.TransformDirection(axisF)).normalized,
            };
        }

        private static Transform FindPiece(Transform root, string nameContains)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != root && t.name.Contains(nameContains) && t.GetComponent<MeshFilter>() != null) return t;
            }
            return null;
        }

        // Bounds of every mesh under 'target', expressed in 'space' local coordinates.
        private static Bounds LocalBounds(Transform space, Transform target)
        {
            bool any = false;
            Bounds result = default;
            foreach (MeshFilter mf in target.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 l = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { result = new Bounds(l, Vector3.zero); any = true; }
                    else result.Encapsulate(l);
                }
            }
            return result;
        }

        private static Vector3 LongestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return Vector3.right;
            return size.y >= size.z ? Vector3.up : Vector3.forward;
        }

        private static Vector3 Cardinal(Vector3 v)
        {
            Vector3 a = Abs(v);
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            return a.y >= a.z ? new Vector3(0f, Mathf.Sign(v.y), 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // ---------- Feedback ----------

        private void Notify(string text)
        {
            lastEventTimer = 1.6f;
            Say(text);
            Notified?.Invoke(text);
        }

        private void Say(string evt)
        {
            if (hud == null || Passed) return;
            string d = string.Format(Directives[(int)check], safetyControl, triggerControl, rackControl);
            hud.SetDirective(evt != null ? $"{evt}   >>   {d}" : d, check == Check.Done ? Green : Amber);
        }

        private void Play(AudioClip clip, float volume)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, volume);
        }
    }
}
