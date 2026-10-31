using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRArmory
{
    // World-space panel beside the bench: intro screen -> live progress -> completion screen.
    // It only reads GunAssemblyManager (events and public properties) and calls BeginTraining(); the assembly
    // logic itself is untouched. Buttons are pressed by pointing a controller ray and pulling the trigger.
    public class VRTrainingMenu : MonoBehaviour
    {
        public GunAssemblyManager manager;

        [Header("Panels")]
        public GameObject introPanel;
        public GameObject progressPanel;
        public GameObject completePanel;

        [Header("Progress")]
        public Text stepText;
        public Text statsText;
        public Text tipText;
        public Text resultText;

        [Header("Buttons")]
        public Button startButton;
        public Button[] restartButtons;
        public Button exitButton;

        private int shownSeconds = -1, shownMistakes = -1;

        private const string SocketTip =
            "Grip a part to pick it up. Bring it to the glowing ghost: it turns GREEN when the position and angle are right. " +
            "Then let go of the grip to seat it. Red means wrong part, wrong order or wrong angle.";
        private const string FunctionCheckTip =
            "Pick up the pistol by its grip. INDEX TRIGGER squeezes the trigger, A / X moves the safety. " +
            "To rack, grip the rear of the slide with your other hand, pull it back and let go. Follow the HUD.";

        void Awake()
        {
            // The trainee starts the session from this panel instead of the assembly starting by itself.
            manager.beginOnStart = false;
            manager.hostHint = "TIP: HOLD {0} IN YOUR OTHER HAND, OR LEAVE IT ON THE BENCH";

            startButton.onClick.AddListener(OnStartPressed);
            foreach (Button b in restartButtons) b.onClick.AddListener(Restart);
            exitButton.onClick.AddListener(Exit);
            Show(introPanel);
        }

        void OnEnable()
        {
            manager.StepStarted += OnStepStarted;
            manager.AllStepsCompleted += OnCompleted;
        }

        void OnDisable()
        {
            manager.StepStarted -= OnStepStarted;
            manager.AllStepsCompleted -= OnCompleted;
        }

        // Timer and mistake count, redrawn only when the displayed numbers change.
        void Update()
        {
            if (!progressPanel.activeSelf) return;
            int seconds = Mathf.FloorToInt(manager.ElapsedSeconds);
            if (seconds == shownSeconds && manager.MistakeCount == shownMistakes) return;
            shownSeconds = seconds;
            shownMistakes = manager.MistakeCount;
            statsText.text = $"TIME  {seconds / 60:00}:{seconds % 60:00}      MISTAKES  {shownMistakes}";
        }

        private void OnStartPressed()
        {
            Show(progressPanel);
            manager.BeginTraining();
        }

        private void OnStepStarted(int index)
        {
            AssemblyStep step = manager.steps[index];
            stepText.text = $"STEP {index + 1} / {manager.steps.Count} :  {step.title}";
            tipText.text = step.functionCheck != null ? FunctionCheckTip : SocketTip;
        }

        private void OnCompleted()
        {
            int seconds = Mathf.FloorToInt(manager.ElapsedSeconds);
            resultText.text = $"Weapon reassembled and function-checked.\n\nTIME  {seconds / 60:00}:{seconds % 60:00}\nMISTAKES  {manager.MistakeCount}";
            Show(completePanel);
        }

        private void Show(GameObject panel)
        {
            introPanel.SetActive(panel == introPanel);
            progressPanel.SetActive(panel == progressPanel);
            completePanel.SetActive(panel == completePanel);
        }

        // Reloading the scene is the cleanest reset: every part, socket and the function check start fresh.
        // (The static lists in AssemblyPart / SnapSocket are emptied by their OnDisable.)
        public void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void Exit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
