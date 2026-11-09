using System;
using UnityEngine;
using UnityEngine.UI;

namespace VRArmory
{
    // The Intro scene: Sgt. Forrest Gump's briefing, then Start loads the training scene.
    //   fade in -> briefing lines (subtitles in the dialog box) -> controls lines (the controller panel highlights each
    //   control) -> "Start" + "Replay" -> fade out -> training scene.
    // "Next" moves on to the following line (and its controller highlight); "Skip" jumps straight to the end. The soldier plays his talking animation while a line is spoken, idles between
    // lines and salutes when the briefing ends. Works with a headset (controller ray / hand poke) and on the desktop
    // (mouse), using whichever camera VRPlatformSwitch left active.
    public class IntroDirector : MonoBehaviour
    {
        [Serializable]
        public class Step
        {
            public VoiceLine line;
            public ControllerGuidePanel.Control highlight;
            public string section;
        }

        public VRVoiceGuide voice;
        public Animator soldier;
        public ControllerGuidePanel controls;
        public SceneFader fader;
        public Canvas[] canvases = new Canvas[0];

        [Header("Dialog box")]
        public Text sectionText;
        public Text subtitleText;
        public Button nextButton;
        public Button skipButton;
        public Button replayButton;
        public Button startButton;

        public string nextScene = "SampleScene";
        public float startDelay = 1.5f;
        public Step[] steps = new Step[0];

        static readonly int TalkingId = Animator.StringToHash("Talking");
        static readonly int SaluteId = Animator.StringToHash("Salute");

        private bool running;       // the briefing is playing (or about to)
        private bool heardALine;    // the voice has started at least one line of this run
        private float beginAt;
        private bool starting;
        private int currentStep = -1; // index in steps of the line on screen (-1 before the first)

        void Start()
        {
            Camera cam = Camera.main;
            foreach (Canvas c in canvases)
            {
                if (c != null) c.worldCamera = cam;
            }
            if (!VRPlatformSwitch.IsVR)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (voice != null) voice.LineStarted += OnLineStarted;
            nextButton.onClick.AddListener(Next);
            skipButton.onClick.AddListener(Finish);
            replayButton.onClick.AddListener(Begin);
            startButton.onClick.AddListener(StartGame);
            Begin();
        }

        void OnDestroy()
        {
            if (voice != null) voice.LineStarted -= OnLineStarted;
        }

        void Begin()
        {
            running = true;
            heardALine = false;
            currentStep = -1;
            beginAt = Time.time + startDelay;
            if (voice != null) voice.Stop();
            subtitleText.text = VRTrainingText.IntroWaiting;
            sectionText.text = VRTrainingText.SectionBriefing;
            ShowEndButtons(false);
            if (controls != null) controls.Highlight(ControllerGuidePanel.Control.None);
        }

        void OnLineStarted(VoiceLine line)
        {
            if (!running) return;
            heardALine = true;
            int i = System.Array.FindIndex(steps, s => s.line == line);
            if (i >= 0) Show(i);
        }

        void Show(int i)
        {
            currentStep = i;
            subtitleText.text = VRTrainingText.Spoken(steps[i].line);
            sectionText.text = steps[i].section;
            if (controls != null) controls.Highlight(steps[i].highlight);
        }

        // Cuts the current line and plays the briefing on from the next one; after the last line it ends the briefing.
        void Next()
        {
            if (!running) return;
            int next = currentStep + 1;
            if (next >= steps.Length)
            {
                Finish();
                return;
            }
            beginAt = 0f;
            heardALine = true;
            Show(next); // at once: the voice starts the line after the cut-off line has faded
            var lines = new VoiceLine[steps.Length - next];
            for (int i = 0; i < lines.Length; i++) lines[i] = steps[next + i].line;
            if (voice != null) voice.Play(lines);
        }

        void Update()
        {
            if (running && beginAt > 0f && Time.time >= beginAt)
            {
                beginAt = 0f;
                var lines = new VoiceLine[steps.Length];
                for (int i = 0; i < steps.Length; i++) lines[i] = steps[i].line;
                if (voice != null) voice.Play(lines);
            }
            else if (running && beginAt == 0f && heardALine && (voice == null || !voice.IsBusy))
            {
                Finish();
            }

            if (soldier != null && soldier.runtimeAnimatorController != null)
                soldier.SetBool(TalkingId, voice != null && voice.Current != VoiceLine.None);
        }

        // End of the briefing (played through or skipped): Start and Replay appear, the soldier salutes.
        void Finish()
        {
            if (!running) return;
            running = false;
            beginAt = 0f;
            if (voice != null) voice.Stop();
            subtitleText.text = VRTrainingText.Spoken(VoiceLine.Sgt_Ready);
            sectionText.text = VRTrainingText.SectionReady;
            if (controls != null) controls.Highlight(ControllerGuidePanel.Control.None);
            ShowEndButtons(true);
            if (soldier != null && soldier.runtimeAnimatorController != null) soldier.SetTrigger(SaluteId);
        }

        void ShowEndButtons(bool end)
        {
            skipButton.gameObject.SetActive(!end);
            nextButton.gameObject.SetActive(!end);
            replayButton.gameObject.SetActive(end);
            startButton.gameObject.SetActive(end);
        }

        void StartGame()
        {
            if (starting) return;
            starting = true;
            if (voice != null) voice.Stop();
            startButton.interactable = replayButton.interactable = false;
            if (fader != null) fader.FadeOutAndLoad(nextScene);
            else UnityEngine.SceneManagement.SceneManager.LoadScene(nextScene);
        }
    }
}
