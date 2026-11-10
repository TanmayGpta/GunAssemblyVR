using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRArmory
{
    // The one VR training panel, in front of the bench. Screens:
    //   Modules  -> (Gun Assembly) Briefing -> Training (steps 1-5) -> Complete -> Modules ...
    //            -> (Shooting) Coming Soon -> Back -> Modules
    // In VR the desktop HUD canvas is hidden, so this is the only place with instructions. The words come from
    // VRTrainingText, the state from GunAssemblyManager / FunctionCheck / VRGuidance. Assembly logic is untouched:
    // the panel only calls GunAssemblyManager.BeginTraining() and reloads the scene to restart.
    public class VRTrainingMenu : MonoBehaviour
    {
        public GunAssemblyManager manager;
        public FunctionCheck functionCheck;
        public VRGuidance guidance;

        [Header("Screens")]
        public GameObject modulesPanel;
        public GameObject comingSoonPanel;
        public GameObject introPanel;
        public GameObject progressPanel;
        public GameObject completePanel;

        [Header("Modules")]
        public Button assemblyModuleButton;
        public Button shootingModuleButton;
        public Text assemblyModuleStatus;
        public Button comingSoonBackButton;

        [Header("Briefing")]
        public Button startButton;
        public Button introBackButton;

        [Header("Training")]
        public Text stepText;
        public Text timerText;
        public Text headlineText;
        public Text detailText;
        public Image feedbackPill;
        public Image feedbackDot;
        public Text feedbackText;
        public Image[] progressSegments;
        [Tooltip("Width the progress segments share, canvas units.")]
        public float progressWidth = 704f;
        public Button restartButton;
        [Tooltip("Training screen: back to the weapon cards.")]
        public Button changeWeaponButton;

        [Header("Weapon choice (between the modules and the briefing)")]
        public GameObject weaponPanel;
        [Tooltip("One per card, in card order.")]
        public WeaponKit[] weaponKits = new WeaponKit[0];
        public Button[] weaponCards = new Button[0];
        [Tooltip("One per card: the centre of the picture area (a child of the card).")]
        public Transform[] weaponModelSpots = new Transform[0];
        public Text[] weaponPills = new Text[0];
        public Button weaponBackButton;
        [Tooltip("The weapon's picture is fitted inside this box on the card, metres.")]
        public Vector2 weaponModelBox = new Vector2(0.27f, 0.105f);
        [Tooltip("Plays the current spoken instruction (and its demonstration) again. Wired by VRTrainingCoach.")]
        public Button repeatButton;

        [Header("Complete")]
        public Text resultText;
        public Button trainAgainButton;
        public Button modulesButton;

        [Header("Colours")]
        public Color accent = new Color(0.3f, 0.92f, 0.86f);
        public Color success = new Color(0.36f, 0.9f, 0.52f);
        public Color warning = new Color(1.0f, 0.76f, 0.32f);
        public Color error = new Color(1.0f, 0.45f, 0.4f);
        public Color segmentIdle = new Color(1f, 1f, 1f, 0.12f);

        public enum Feedback { None, Info, Guide, Success, Error }

        public Feedback ShownFeedback { get; private set; }

        // Set before the scene reload of "Train again" / "Restart", so the new scene goes straight to step 1.
        private static bool startTrainingAfterReload;
        // Set before the scene reload that swaps the weapon on the bench, so the new scene opens on the briefing.
        private static bool showBriefingAfterReload;
        private readonly System.Collections.Generic.List<GameObject> weaponModels = new System.Collections.Generic.List<GameObject>();

        private GameObject shownPanel;
        private float screenFade;
        private float pop;
        private Vector3 baseScale;
        private int shownSeconds = -1;
        private bool startPending;

        // A message that stays for a moment (step done, part rejected, function check event), over the live hints.
        private string messageTitle, messageDetail, messageText;
        private Feedback messageKind;
        private float messageTimer;

        private AudioSource audioSource;
        private AudioClip clickClip, stepClip, bigClip;

        void Awake()
        {
            baseScale = transform.localScale;
            // The trainee starts the session from this panel instead of the assembly starting by itself.
            manager.beginOnStart = false;
            // One panel only: the desktop HUD keeps running (its text still updates) but is not drawn in VR.
            if (manager.hud != null)
            {
                Canvas hudCanvas = manager.hud.GetComponent<Canvas>();
                if (hudCanvas != null) hudCanvas.enabled = false;
            }

            assemblyModuleButton.onClick.AddListener(OnAssemblyModule);
            shootingModuleButton.onClick.AddListener(OnShootingModule);
            comingSoonBackButton.onClick.AddListener(() => Show(modulesPanel));
            startButton.onClick.AddListener(OnStartPressed);
            introBackButton.onClick.AddListener(() => Show(weaponPanel != null ? weaponPanel : modulesPanel));
            for (int i = 0; i < weaponCards.Length; i++)
            {
                int index = i;
                if (weaponCards[i] != null) weaponCards[i].onClick.AddListener(() => PickWeapon(index));
            }
            // Back from the cards: to the training in progress if there is one, otherwise to the modules.
            if (weaponBackButton != null)
                weaponBackButton.onClick.AddListener(() => Show(manager.HasStarted && !manager.IsComplete ? progressPanel : modulesPanel));
            if (changeWeaponButton != null) changeWeaponButton.onClick.AddListener(() => Show(weaponPanel));
            restartButton.onClick.AddListener(Restart);
            trainAgainButton.onClick.AddListener(Restart);
            modulesButton.onClick.AddListener(() => Show(modulesPanel));
            foreach (Button b in GetComponentsInChildren<Button>(true)) b.onClick.AddListener(() => Play(clickClip, 0.35f));

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            clickClip = ProceduralAudio.MakeClick("UI_Click", 0.015f, 3800f, 0.5f);
            stepClip = ProceduralAudio.MakeChime("Step_Done", 0.09f, 0.35f, 660f, 990f);
            bigClip = ProceduralAudio.MakeChime("Module_Done", 0.11f, 0.4f, 523f, 659f, 784f, 1047f);

            LayoutProgressSegments();
            startPending = startTrainingAfterReload;
            startTrainingAfterReload = false;
            bool briefing = showBriefingAfterReload;
            showBriefingAfterReload = false;
            Show(startPending ? progressPanel : briefing ? introPanel : modulesPanel);
        }

        // One segment per step of the chosen weapon, sharing the width; spare segments are hidden.
        private void LayoutProgressSegments()
        {
            int count = Mathf.Min(manager.steps.Count, progressSegments.Length);
            const float gap = 8f;
            float w = count > 0 ? (progressWidth - gap * (count - 1)) / count : 0f;
            for (int i = 0; i < progressSegments.Length; i++)
            {
                progressSegments[i].gameObject.SetActive(i < count);
                if (i >= count) continue;
                RectTransform rt = progressSegments[i].rectTransform;
                rt.anchoredPosition = new Vector2(-progressWidth * 0.5f + w * 0.5f + i * (w + gap), rt.anchoredPosition.y);
                rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
            }
        }

        void OnEnable()
        {
            manager.StepStarted += OnStepStarted;
            manager.AllStepsCompleted += OnCompleted;
            foreach (AssemblyStep s in manager.steps)
            {
                if (s.socket == null) continue;
                s.socket.Seated += OnSeated;
                s.socket.Rejected += OnRejected;
            }
            if (functionCheck != null)
            {
                functionCheck.Notified += OnCheckEvent;
                functionCheck.Completed += OnCheckPassed;
            }
        }

        void OnDisable()
        {
            manager.StepStarted -= OnStepStarted;
            manager.AllStepsCompleted -= OnCompleted;
            foreach (AssemblyStep s in manager.steps)
            {
                if (s.socket == null) continue;
                s.socket.Seated -= OnSeated;
                s.socket.Rejected -= OnRejected;
            }
            if (functionCheck != null)
            {
                functionCheck.Notified -= OnCheckEvent;
                functionCheck.Completed -= OnCheckPassed;
            }
        }

        void Update()
        {
            // After a restart: begin once GunAssemblyManager.Start has locked every socket.
            if (startPending)
            {
                startPending = false;
                manager.BeginTraining();
            }

            AnimatePanel();
            if (shownPanel == progressPanel) UpdateTraining();
            else if (shownPanel == modulesPanel) UpdateModules();
        }

        // ---------- Navigation ----------

        private void OnAssemblyModule()
        {
            if (manager.HasStarted && !manager.IsComplete) Show(progressPanel); // continue where the trainee left off
            else if (weaponPanel != null) Show(weaponPanel);                    // choose (or choose again) what to build
            else if (manager.IsComplete) Restart();
            else Show(introPanel);
        }

        // A weapon card. The weapon already on the bench and untouched: on to the briefing. Otherwise the scene is
        // loaded again with the chosen weapon's parts on the bench: the same weapon starts at step 1 at once (train
        // again), a different one opens on the briefing.
        private void PickWeapon(int index)
        {
            if (index >= weaponKits.Length || weaponKits[index] == null) return;
            WeaponKit kit = weaponKits[index];
            WeaponKitSwitch kits = WeaponKitSwitch.Instance;
            bool onBench = kits == null || kits.Active == kit;
            if (onBench && !manager.HasStarted)
            {
                Show(introPanel);
                return;
            }
            if (onBench && !manager.IsComplete)
            {
                Show(progressPanel);
                return;
            }
            WeaponKitSwitch.Choose(kit);
            if (onBench) startTrainingAfterReload = true;
            else showBriefingAfterReload = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // Card footer (parts, best time) and the assembled weapon's picture, while the cards are on screen.
        private void RefreshWeaponCards()
        {
            for (int i = 0; i < weaponKits.Length; i++)
            {
                WeaponKit kit = weaponKits[i];
                if (kit == null) continue;
                if (i < weaponPills.Length && weaponPills[i] != null)
                    Set(weaponPills[i], VRTrainingText.WeaponCardPill(kit.PartCount, kit.BestSeconds));
                if (weaponModels.Count > i || i >= weaponModelSpots.Length) continue;
                weaponModels.Add(kit.BuildAssembledCopy(weaponModelSpots[i], weaponModelBox));
            }
        }

        private void ClearWeaponModels()
        {
            foreach (GameObject go in weaponModels)
                if (go != null) Destroy(go);
            weaponModels.Clear();
        }

        private void OnStartPressed()
        {
            Show(progressPanel);
            manager.BeginTraining();
        }

        // After every Awake (FireTraining registers itself there).
        void Start()
        {
            RelabelShootingModule();
        }

        // Module 02: straight into the fire training booth when the pistol is assembled and checked; otherwise its panel
        // says to do that first.
        private void OnShootingModule()
        {
            FireTraining training = FireTraining.Instance;
            if (training != null && training.CanBegin) training.Begin();
            else if (training == null || !training.Running) Show(comingSoonPanel);
        }

        // The card and panel were built when this module was "coming soon": give them their live texts.
        private void RelabelShootingModule()
        {
            if (FireTraining.Instance == null) return;
            foreach (Text t in shootingModuleButton.GetComponentsInChildren<Text>(true))
                if (t.text == VRTrainingText.StatusComingSoon) t.text = VRTrainingText.StatusLiveRange;
            foreach (Text t in comingSoonPanel.GetComponentsInChildren<Text>(true))
            {
                if (t.text == VRTrainingText.StatusComingSoon) t.text = VRTrainingText.StatusLocked;
                else if (t.text == VRTrainingText.ComingSoonBody) t.text = VRTrainingText.ShootingLockedBody;
            }
        }

        private void Show(GameObject panel)
        {
            foreach (GameObject p in new[] { modulesPanel, comingSoonPanel, weaponPanel, introPanel, progressPanel, completePanel })
                if (p != null) p.SetActive(p == panel);
            shownPanel = panel;
            screenFade = 0f;
            if (panel == progressPanel) RefreshTraining(true);
            if (panel == weaponPanel && weaponPanel != null) RefreshWeaponCards();
            else ClearWeaponModels();
        }

        // Reloading the scene is the cleanest reset: every part, socket and the function check start fresh.
        // (The static lists in AssemblyPart / SnapSocket are emptied by their OnDisable.)
        public void Restart()
        {
            startTrainingAfterReload = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ---------- Assembly events ----------

        private void OnStepStarted(int index)
        {
            if (shownPanel == progressPanel) RefreshTraining(true);
        }

        private void OnSeated(SnapSocket socket)
        {
            bool assemblyDone = true;
            foreach (AssemblyStep s in manager.steps)
            {
                if (s.socket != null && !s.socket.IsFilled) assemblyDone = false;
            }
            if (assemblyDone)
            {
                ShowMessage(VRTrainingText.AssemblyComplete, VRTrainingText.AssemblyCompleteDetail, VRTrainingText.AssemblyComplete, Feedback.Success, 3.2f);
                Play(bigClip, 0.6f);
                pop = 1f;
            }
            else
            {
                ShowMessage(null, null, VRTrainingText.Seated(socket.acceptedPart.partType), Feedback.Success, 1.6f);
                Play(stepClip, 0.5f);
                pop = 0.6f;
            }
        }

        private int lastRejectFrame = -1;

        private void OnRejected(SnapSocket socket, AssemblyPart part, string reason)
        {
            // One release can be rejected by two sockets; GunAssemblyManager counts it once, so does the panel.
            if (!manager.HasStarted || manager.IsComplete || Time.frameCount == lastRejectFrame) return;
            lastRejectFrame = Time.frameCount;
            string text = reason.StartsWith("OUT OF SEQUENCE") ? VRTrainingText.RejectOutOfSequence
                        : reason.StartsWith("WRONG PART") ? VRTrainingText.RejectWrongPart
                        : reason.StartsWith("NOT CLOSE") ? VRTrainingText.RejectTooFar
                        : VRTrainingText.RejectMisaligned;
            ShowMessage(null, VRTrainingText.RejectDetail, text, Feedback.Error, 2.4f);
        }

        private void OnCheckEvent(string message)
        {
            string text = VRTrainingText.Notification(message);
            if (text == null || functionCheck.Passed) return;
            bool problem = message.StartsWith("TRIGGER RELEASED TOO EARLY") || message.StartsWith("HOLD THE TRIGGER BACK");
            ShowMessage(null, null, text, problem ? Feedback.Error : Feedback.Success, problem ? 2.6f : 1.4f);
            if (!problem) Play(stepClip, 0.3f);
        }

        private void OnCheckPassed()
        {
            ShowMessage(VRTrainingText.CheckPassed, VRTrainingText.CheckPassedDetail, VRTrainingText.CheckPassed, Feedback.Success, 10f);
            Play(stepClip, 0.5f);
            pop = 0.6f;
        }

        private void OnCompleted()
        {
            int seconds = Mathf.FloorToInt(manager.ElapsedSeconds);
            resultText.text = VRTrainingText.Result(seconds, manager.MistakeCount);
            if (WeaponKitSwitch.Instance != null && WeaponKitSwitch.Instance.Active != null)
                WeaponKitSwitch.Instance.Active.ReportTime(seconds);
            Show(completePanel);
            Play(bigClip, 0.6f);
            pop = 1f;
        }

        private void ShowMessage(string title, string detail, string text, Feedback kind, float seconds)
        {
            messageTitle = title;
            messageDetail = detail;
            messageText = text;
            messageKind = kind;
            messageTimer = seconds;
            if (shownPanel == progressPanel) RefreshTraining(false);
        }

        // ---------- Training screen ----------

        private void UpdateTraining()
        {
            if (messageTimer > 0f) messageTimer -= Time.deltaTime;
            RefreshTraining(false);

            int seconds = Mathf.FloorToInt(manager.ElapsedSeconds);
            if (seconds != shownSeconds)
            {
                shownSeconds = seconds;
                timerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
            }
        }

        // Builds headline, detail and feedback from the current state. Text components skip unchanged strings.
        private void RefreshTraining(bool stepChanged)
        {
            int count = manager.steps.Count;
            int index = Mathf.Min(manager.CurrentStepIndex, count - 1);
            AssemblyStep step = manager.steps[index];
            bool isCheck = step.functionCheck != null;
            string stepName = isCheck ? VRTrainingText.FunctionCheckName : VRTrainingText.PartName(step.part);
            Set(stepText, VRTrainingText.StepLabel(index, count, stepName));

            for (int i = 0; i < progressSegments.Length && i < count; i++)
            {
                Color c = manager.steps[i].completed ? success : i == index && manager.HasStarted ? accent : segmentIdle;
                if (i == index && manager.HasStarted && !manager.steps[i].completed) c.a = 0.65f + 0.35f * Mathf.Sin(Time.time * 4f);
                progressSegments[i].color = c;
            }

            string title, detail, feedback = null;
            Feedback kind = Feedback.None;
            GuidanceText(step, index, out title, out detail, ref feedback, ref kind);

            if (messageTimer > 0f)
            {
                if (messageTitle != null) title = messageTitle;
                if (messageDetail != null) detail = messageDetail;
                feedback = messageText;
                kind = messageKind;
            }
            Set(headlineText, title);
            Set(detailText, detail);
            SetFeedback(feedback, kind);
        }

        private void GuidanceText(AssemblyStep step, int index, out string title, out string detail, ref string feedback, ref Feedback kind)
        {
            if (!manager.HasStarted)
            {
                title = VRTrainingText.PickUp(step.part);
                detail = VRTrainingText.PickUpFirstDetail;
                return;
            }

            if (step.socket != null)
            {
                GunPart part = step.socket.acceptedPart.partType;
                if (guidance.CurrentPhase == VRGuidance.Phase.Place)
                {
                    WeaponKitSwitch kits = WeaponKitSwitch.Instance;
                    title = VRTrainingText.Place(part, kits != null && kits.Active != null ? kits.Active.id : null);
                    detail = VRTrainingText.PlaceDetail;
                    feedback = VRGuidance.CorrectionText(guidance.CurrentCorrection);
                    kind = guidance.CurrentCorrection == VRGuidance.Correction.Aligned ? Feedback.Success
                         : guidance.CurrentCorrection == VRGuidance.Correction.BringToTarget ? Feedback.Info : Feedback.Guide;
                    return;
                }
                title = VRTrainingText.PickUp(part);
                detail = guidance.HostHeld != null ? VRTrainingText.HostHeldDetail(guidance.HostHeld.partType)
                       : index == 0 ? VRTrainingText.PickUpFirstDetail : VRTrainingText.PickUpDetail;
                if (guidance.WrongPartHeld != null)
                {
                    detail = VRTrainingText.WrongPartDetail(part);
                    feedback = VRTrainingText.WrongPart(guidance.WrongPartHeld.partType);
                    kind = Feedback.Error;
                }
                return;
            }

            // Function check.
            if (functionCheck.Passed)
            {
                title = VRTrainingText.CheckPassed;
                detail = VRTrainingText.CheckPassedDetail;
                return;
            }
            if (guidance.CurrentPhase != VRGuidance.Phase.FunctionCheck)
            {
                title = VRTrainingText.PickUpPistol;
                detail = VRTrainingText.PickUpPistolDetail;
                return;
            }
            int stage = Mathf.Clamp(functionCheck.Stage, 0, FunctionCheck.StageCount - 1);
            title = VRTrainingText.StageTitle(stage);
            detail = VRTrainingText.StageDetail(stage, guidance.SafetyButton);
            if (stage == 1 && functionCheck.SafeTriggerPulled) title = VRTrainingText.ReleaseTrigger;
            if (stage == 2 && functionCheck.TriggerPull >= 0.2f) title = VRTrainingText.ReleaseTriggerFirst;
            if (stage == 4 && functionCheck.TriggerPull < 0.8f)
            {
                feedback = VRTrainingText.HoldTriggerWhileRacking;
                kind = Feedback.Guide;
            }
            feedback = feedback ?? $"CHECK {stage + 1} OF {FunctionCheck.StageCount}";
            if (kind == Feedback.None) kind = Feedback.Info;
        }

        private void SetFeedback(string text, Feedback kind)
        {
            ShownFeedback = text == null ? Feedback.None : kind;
            bool show = text != null;
            if (feedbackPill.gameObject.activeSelf != show) feedbackPill.gameObject.SetActive(show);
            if (!show) return;
            Color c = kind == Feedback.Success ? success : kind == Feedback.Error ? error : kind == Feedback.Guide ? warning : accent;
            feedbackPill.color = new Color(c.r, c.g, c.b, 0.16f);
            feedbackDot.color = c;
            feedbackText.color = c;
            Set(feedbackText, text);
        }

        private static void Set(Text t, string s)
        {
            if (t.text != s) t.text = s;
        }

        // ---------- Modules screen ----------

        private void UpdateModules()
        {
            Set(assemblyModuleStatus, manager.IsComplete ? VRTrainingText.StatusCompleted
                                    : manager.HasStarted ? VRTrainingText.StatusInProgress : VRTrainingText.StatusAvailable);
        }

        // ---------- Motion ----------

        // Screens fade in; success moments give the panel a small "pop".
        private void AnimatePanel()
        {
            if (screenFade < 1f)
            {
                screenFade = Mathf.Min(1f, screenFade + Time.deltaTime / 0.2f);
                CanvasGroup g = shownPanel != null ? shownPanel.GetComponent<CanvasGroup>() : null;
                if (g != null) g.alpha = screenFade;
            }
            if (pop > 0f)
            {
                pop = Mathf.Max(0f, pop - Time.deltaTime / 0.35f);
                transform.localScale = baseScale * (1f + 0.035f * Mathf.Sin(pop * Mathf.PI));
            }
        }

        private void Play(AudioClip clip, float volume)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, volume);
        }
    }
}
