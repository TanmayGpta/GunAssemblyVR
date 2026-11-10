using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // The live-fire training round, started by "Yes" at the range sergeant:
    //   1. the trainee is moved into the middle booth facing downrange and stays there (walking and teleporting off,
    //      looking and turning still work); the pistol (unless already in hand) and its magazine, unloaded, are laid on
    //      the counter, like on a real range. Letting go of either puts it back on the counter.
    //   2. sergeant: pick up the magazine -> (picked up) the pistol's facts -> load and rack -> (chambered) fire when ready.
    //   3. the clock starts with the first shot; every hit on a dummy scores (RangeScoring). One magazine, 15 rounds.
    //   4. the last round ends it: "Cease fire", the booth panel shows score, time, hits and best shot, with Try again /
    //      Finish. "End training" stops it at any time. Finish / End unlocks the trainee.
    // The booth panel floats at the trainee's left: current instruction, time, rounds and score.
    public class FireTraining : MonoBehaviour
    {
        public RangeSergeant sergeant;
        public VRVoiceGuide voice;
        public FunctionCheck functionCheck;
        public PistolFiring firing;
        public PistolMagazine ammo;
        public RangeScoring scoring;
        [Tooltip("Weapon / mode / targets screen shown when the trainee reaches the booth.")]
        public RangeModeMenu menu;
        public AssemblyPart frame;
        public VRAssemblyGrab frameGrab;

        [Header("Booth")]
        [Tooltip("Where the trainee stands (floor), facing downrange along its forward.")]
        public Transform standPoint;
        [Tooltip("Spots on the counter top for the pistol and the magazine (counter surface height).")]
        public Transform pistolSpot;
        public Transform magazineSpot;

        [Header("Booth panel")]
        public CanvasGroup panel;
        [Tooltip("Where the panel moves for the results: in front of the trainee, over the counter.")]
        public Transform resultsSpot;
        public float panelMoveTime = 0.4f;
        public Text stepText;
        public Text timeText;
        public Text roundsText;
        public Text scoreText;
        public Text resultText;
        public Button endButton;
        public Button retryButton;
        public Button finishButton;
        public Text desktopHint;

        public bool Running => phase != Phase.Off;
        public static FireTraining Instance { get; private set; }
        public bool CanBegin => !Running && functionCheck != null && functionCheck.Passed;

        // The pistol only fires (and loads) during a range session, never in the armory.
        public static bool LiveFireAllowed => Instance != null && Instance.phase >= Phase.Magazine;

        private enum Phase { Off, Choose, Magazine, Load, Ready, Firing, Results }
        private Phase phase = Phase.Off;
        private bool timedMode = true;
        private bool movingTargets;
        private bool reloadHintGiven;
        private readonly List<RangeTargetMover> movers = new List<RangeTargetMover>();
        private readonly List<bool> moversWereOn = new List<bool>();
        private float elapsed;
        private bool timing;
        private int shots;
        private readonly List<Behaviour> lockedMovement = new List<Behaviour>();
        private Vector3 sidePos;
        private Quaternion sideRot;
        private bool panelAtFront;
        private float panelMove = 1f;
        private Vector3 moveFromPos;
        private Quaternion moveFromRot;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            sidePos = panel.transform.position;
            sideRot = panel.transform.rotation;
            if (sergeant != null)
            {
                sergeant.CanStart = () => functionCheck != null && functionCheck.Passed;
                sergeant.TrainingRequested += Begin;
            }
            endButton.onClick.AddListener(Stop);
            retryButton.onClick.AddListener(Retry);
            finishButton.onClick.AddListener(Stop);
            if (menu != null)
            {
                menu.Chosen += OnChosen;
                menu.Cancelled += Stop;
            }
            Canvas canvas = panel.GetComponent<Canvas>();
            if (canvas != null) canvas.worldCamera = Camera.main;
            if (desktopHint != null) desktopHint.gameObject.SetActive(!VRPlatformSwitch.IsVR);
            panel.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (sergeant != null) sergeant.TrainingRequested -= Begin;
            if (firing != null) firing.Fired -= OnFired;
            if (menu != null)
            {
                menu.Chosen -= OnChosen;
                menu.Cancelled -= Stop;
            }
        }

        // ---------- Flow ----------

        public void Begin()
        {
            if (Running || functionCheck == null || !functionCheck.Passed) return;
            sergeant.Busy = true;
            LockMovement(true);
            MovePlayerToBooth();
            phase = Phase.Choose;
            if (menu != null) menu.Open();
            else OnChosen(0, true, false);
        }

        // From the menu: lay out the weapon and start the first round in that mode.
        private void OnChosen(int weapon, bool timed, bool moving)
        {
            if (phase != Phase.Choose) return;
            timedMode = timed;
            movingTargets = moving;
            reloadHintGiven = false;
            frame.gameObject.SetActive(true);
            if (ammo.magazine != null) ammo.magazine.gameObject.SetActive(true);
            LayOutWeapon();
            ammo.refillOnInsert = !timed; // unlimited: every magazine pushed in is full
            SetTargets(true);
            firing.Fired -= OnFired;
            firing.Fired += OnFired;
            if (desktopHint != null) desktopHint.text = timed ? VRTrainingText.RangeBoothDesktopHint : VRTrainingText.RangeUnlimitedDesktopHint;
            panel.gameObject.SetActive(true);
            StartRound(true);
        }

        // on: stationary (lane centres, nothing covering anything) or moving, as chosen. Off: back as they were.
        private void SetTargets(bool on)
        {
            if (on)
            {
                movers.Clear();
                moversWereOn.Clear();
                foreach (RangeTargetMover m in FindObjectsOfType<RangeTargetMover>())
                {
                    movers.Add(m);
                    moversWereOn.Add(m.enabled);
                    bool move = timedMode && movingTargets;
                    m.enabled = move;
                    if (!move) m.ResetToLane();
                }
                return;
            }
            for (int i = 0; i < movers.Count; i++)
                if (movers[i] != null) movers[i].enabled = moversWereOn[i];
            movers.Clear();
            moversWereOn.Clear();
        }

        // first: the full briefing (magazine facts); a retry goes straight to loading.
        private void StartRound(bool first)
        {
            scoring.ResetScore();
            elapsed = 0f;
            timing = false;
            shots = 0;
            ShowResults(false);
            MovePanel(false, true);
            if (firing.effects != null) firing.effects.ClearHoles();
            if (first)
            {
                phase = Phase.Magazine;
                sergeant.Say(VoiceLine.Rng_Handover);
            }
            else
            {
                phase = Phase.Load;
                sergeant.Say(VoiceLine.Rng_Load);
            }
        }

        private void Retry()
        {
            if (phase != Phase.Results) return;
            ammo.PrepareMagazine(ammo.capacity);
            PlaceOnCounter(ammo.magazine, magazineSpot, true);
            StartRound(false);
        }

        // Finish (after the results) or End training (any time): the trainee is free again.
        public void Stop()
        {
            if (!Running) return;
            bool hadRound = phase != Phase.Choose;
            phase = Phase.Off;
            timing = false;
            if (menu != null && menu.IsOpen) menu.Close();
            if (hadRound) SetTargets(false);
            firing.Fired -= OnFired;
            if (voice != null) voice.Stop();
            panel.gameObject.SetActive(false);
            MovePanel(false, false);
            if (firing.effects != null) firing.effects.ClearHoles();
            ammo.refillOnInsert = true;
            // Desktop: dropping the interactor's hold (its OnDisable lets go of the held part and the off hand).
            DesktopAssemblyInteractor desk = FindObjectOfType<DesktopAssemblyInteractor>();
            if (desk != null && desk.enabled && (frame.IsHeld || frame.IsInOffHand || (ammo.magazine != null && ammo.magazine.IsHeld)))
            {
                desk.enabled = false;
                desk.enabled = true;
            }
            PutAway(frame);
            PutAway(ammo.magazine);
            LockMovement(false);
            sergeant.Busy = false;
        }

        // Out of the hand, back to the bench and hidden until the next round (Begin shows it again).
        private static void PutAway(AssemblyPart part)
        {
            if (part == null) return;
            VRAssemblyGrab grab = part.GetComponent<VRAssemblyGrab>();
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
            if (part.IsInOffHand) part.EndOffHandHold();
            part.RestoreHome();
            if (!part.IsSeated) part.SnapHome(); // a magazine still in the grip stays there, hidden with the pistol
            part.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!Running || phase == Phase.Choose) return; // the menu has the controls while it is up

            switch (phase)
            {
                case Phase.Magazine:
                    if (ammo.magazine.IsHeld || ammo.MagazineInserted)
                    {
                        phase = Phase.Load;
                        sergeant.Say(VoiceLine.Rng_Magazine, VoiceLine.Rng_Load);
                    }
                    break;
                case Phase.Load:
                    if (ammo.Chambered)
                    {
                        phase = Phase.Ready;
                        if (timedMode) sergeant.Say(VoiceLine.Rng_FireWhenReady); // that line is about the clock
                    }
                    break;
            }
            if (timing) elapsed += Time.deltaTime;
            AnimatePanel();

            if (!VRPlatformSwitch.IsVR)
            {
                if (phase == Phase.Results && Input.GetKeyDown(KeyCode.Return)) Retry();
                else if (Input.GetKeyDown(KeyCode.Backspace)) Stop();
            }
            UpdatePanel();
        }

        private void OnFired()
        {
            if (phase < Phase.Magazine || phase == Phase.Results) return;
            shots++;
            if (phase != Phase.Firing)
            {
                timing = timedMode;
                phase = Phase.Firing;
                if (voice != null) voice.Stop(); // he stops talking once the shooting starts
            }
            if (timedMode && ammo.RoundsLeft == 0) End();
            else if (!timedMode && ammo.RoundsLeft == 0 && !reloadHintGiven)
            {
                // Unlimited: the first empty magazine of the session gets the how-to-reload line, only once.
                reloadHintGiven = true;
                bool left = frameGrab != null && frameGrab.isSelected && VRHands.Instance != null
                            && VRHands.Instance.IsLeft(frameGrab.interactorsSelecting[0]);
                sergeant.Say(left ? VoiceLine.Rng_Reload_Y : VoiceLine.Rng_Reload_B);
            }
        }

        private void End()
        {
            phase = Phase.Results;
            timing = false;
            sergeant.Say(VoiceLine.Rng_CeaseFire);
            ShowResults(true);
            MovePanel(true, true);
        }

        // ---------- Booth panel ----------

        // Results: the panel glides to the front of the booth; otherwise it sits at the trainee's left.
        private void MovePanel(bool front, bool animate)
        {
            if (resultsSpot == null || front == panelAtFront) return;
            panelAtFront = front;
            moveFromPos = panel.transform.position;
            moveFromRot = panel.transform.rotation;
            panelMove = animate ? 0f : 1f;
            if (!animate) PlacePanel(1f);
        }

        private void AnimatePanel()
        {
            if (panelMove >= 1f) return;
            panelMove = Mathf.Min(1f, panelMove + Time.deltaTime / Mathf.Max(0.01f, panelMoveTime));
            PlacePanel(Mathf.SmoothStep(0f, 1f, panelMove));
        }

        private void PlacePanel(float k)
        {
            Vector3 toPos = panelAtFront ? resultsSpot.position : sidePos;
            Quaternion toRot = panelAtFront ? resultsSpot.rotation : sideRot;
            panel.transform.SetPositionAndRotation(Vector3.Lerp(moveFromPos, toPos, k), Quaternion.Slerp(moveFromRot, toRot, k));
        }

        private void ShowResults(bool on)
        {
            resultText.gameObject.SetActive(on);
            retryButton.gameObject.SetActive(on);
            finishButton.gameObject.SetActive(on);
            endButton.gameObject.SetActive(!on);
            if (!on) return;
            float accuracy = shots > 0 ? 100f * scoring.ScoringHits / shots : 0f;
            resultText.text =
                $"<size=30><b>SCORE  {scoring.TotalScore}</b></size>\n" +
                $"TIME  {elapsed:0.00} s     HITS  {scoring.ScoringHits} / {shots}  ({accuracy:0}%)\n" +
                $"BEST SHOT  {scoring.BestShot}";
        }

        private void UpdatePanel()
        {
            stepText.text = StepText();
            timeText.text = timedMode ? $"TIME\n<size=34><b>{elapsed:0.00}</b></size>" : $"SHOTS\n<size=34><b>{shots}</b></size>";
            roundsText.text = $"ROUNDS\n<size=34><b>{ammo.RoundsLeft}</b></size> / {ammo.capacity}";
            scoreText.text = $"SCORE\n<size=34><b>{scoring.TotalScore}</b></size>";
        }

        private string StepText()
        {
            switch (phase)
            {
                case Phase.Magazine: return VRTrainingText.RangeStepMagazine;
                case Phase.Load:
                    if (!ammo.MagazineInserted) return ammo.magazine.IsHeld ? VRTrainingText.RangeStepInsert : VRTrainingText.RangeStepLoad;
                    return VRTrainingText.RangeStepRack;
                case Phase.Ready: return timedMode ? VRTrainingText.RangeStepReady : VRTrainingText.RangeStepUnlimited;
                case Phase.Firing: return timedMode ? VRTrainingText.RangeStepFiring : VRTrainingText.RangeStepUnlimited;
                case Phase.Results: return VRTrainingText.RangeStepDone;
            }
            return "";
        }

        // ---------- Booth set-up ----------

        // Pistol on the counter (unless the trainee is already holding it), magazine out and loaded with 15 next to it.
        private void LayOutWeapon()
        {
            ammo.refillOnInsert = false;
            if (ammo.magazine != null) ammo.magazine.gameObject.SetActive(true); // hidden if the last round dropped it
            ammo.PrepareMagazine(ammo.capacity);
            bool held = frame.IsHeld || frame.IsInOffHand || (frameGrab != null && frameGrab.isSelected);
            PlaceOnCounter(frame, pistolSpot, !held);
            PlaceOnCounter(ammo.magazine, magazineSpot, true);
        }

        // Makes 'spot' the part's home (lying as it does on the bench, resting on the counter) and puts it there now.
        private static void PlaceOnCounter(AssemblyPart part, Transform spot, bool moveNow)
        {
            if (part == null || spot == null) return;
            Quaternion rot = part.HomeWorldRotation;
            part.OverrideHome(spot.position + Vector3.up * 0.03f, rot);
            if (!moveNow) return;
            part.SnapHome();
            // Sit it on the counter: lift by how far its lowest point is below the counter surface.
            Bounds b = RendererBounds(part.transform);
            Vector3 pos = part.transform.position + Vector3.up * (spot.position.y + 0.002f - b.min.y);
            part.OverrideHome(pos, rot);
            part.SnapHome();
        }

        private static Bounds RendererBounds(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private void MovePlayerToBooth()
        {
            if (standPoint == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(standPoint.forward, Vector3.up).normalized;
            XROrigin origin = VRPlatformSwitch.IsVR ? FindObjectOfType<XROrigin>() : null;
            if (origin != null)
            {
                CharacterController cc = origin.GetComponent<CharacterController>();
                bool ccOn = cc != null && cc.enabled;
                if (cc != null) cc.enabled = false;
                origin.MatchOriginUpCameraForward(Vector3.up, forward);
                Vector3 cam = origin.Camera.transform.position;
                origin.MoveCameraToWorldLocation(new Vector3(standPoint.position.x, cam.y, standPoint.position.z));
                if (cc != null) cc.enabled = ccOn;
                return;
            }
            ArmoryPlayerController player = FindObjectOfType<ArmoryPlayerController>();
            if (player == null) return;
            CharacterController body = player.GetComponent<CharacterController>();
            if (body != null) body.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(standPoint.position.x, player.transform.position.y, standPoint.position.z), Quaternion.LookRotation(forward));
            if (body != null) body.enabled = true;
        }

        // VR: walking and teleporting off (turning stays). Desktop: the player controller stops moving.
        private void LockMovement(bool locked)
        {
            if (locked)
            {
                lockedMovement.Clear();
                foreach (LocomotionProvider p in FindObjectsOfType<LocomotionProvider>())
                {
                    if (p is SnapTurnProviderBase || p is ContinuousTurnProviderBase || !p.enabled) continue;
                    p.enabled = false;
                    lockedMovement.Add(p);
                }
                foreach (VRHandTeleport t in FindObjectsOfType<VRHandTeleport>())
                {
                    if (!t.enabled) continue;
                    t.enabled = false;
                    lockedMovement.Add(t);
                }
            }
            else
            {
                foreach (Behaviour b in lockedMovement)
                    if (b != null) b.enabled = true;
                lockedMovement.Clear();
            }
            ArmoryPlayerController player = FindObjectOfType<ArmoryPlayerController>();
            if (player != null) player.moveLocked = locked;
        }
    }
}
