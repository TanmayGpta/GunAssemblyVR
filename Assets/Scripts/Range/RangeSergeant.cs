using System;
using UnityEngine;
using UnityEngine.UI;

namespace VRArmory
{
    // Sgt. Forrest Gump at the side of the firing line. He idles and turns to face the trainee once they come near; close
    // to him, a floating window asks "Ready for live-fire training?":
    //   Yes     -> TrainingRequested (the fire training takes over; the window stays hidden while it runs)
    //   Not now -> the window hides until the trainee walks away and comes back.
    // The window turns (about the vertical axis) to face the trainee. Desktop (cursor captured, no pointer for world
    // UI): Enter = Yes, Backspace = Not now.
    public class RangeSergeant : MonoBehaviour
    {
        public Animator soldier;
        public VRVoiceGuide voice;
        [Tooltip("The assembly trainer's voice: silenced while the trainee is at the range, so the two never overlap.")]
        public VRVoiceGuide trainerVoice;
        [Tooltip("Past this Z (towards the range) the trainer is silent.")]
        public float rangeEntryZ = 6.8f;

        [Header("Feet on the floor")]
        [Tooltip("Floor height under him. His animations hold the body a little higher than the T-pose he was placed in, so every frame he is lowered until his toes touch it.")]
        public float floorY;
        public bool groundFeet = true;
        [Tooltip("Height of the toe bone above the sole.")]
        public float toeClearance = 0.025f;
        public CanvasGroup prompt;
        public Text questionText;
        public Button yesButton;
        public Button noButton;
        public Text desktopHint;

        [Tooltip("The window appears within this distance (m, on the floor) of the sergeant.")]
        public float promptRadius = 1.6f;
        [Tooltip("...and only in front of him (degrees either side of the way he stands): not from the booth behind him.")]
        public float promptAngle = 80f;
        [Tooltip("He turns to face the trainee within this distance.")]
        public float lookRadius = 5f;
        public float turnSpeed = 160f; // degrees per second
        public float fadeTime = 0.25f;
        [Tooltip("Seconds he 'talks' when the window opens without a voice clip.")]
        public float talkTime = 2.5f;

        // False: the pistol is not assembled and checked yet; he sends the trainee back to the bench (no Yes button).
        public Func<bool> CanStart;

        public event Action TrainingRequested;
        public event Action Declined;

        // While true (training running) the window never opens.
        public bool Busy { get; set; }

        private enum State { Away, Asking, Dismissed }
        private State state = State.Away;
        private float baseYaw;
        private float talkTimer;
        private bool grounded;
        private static readonly int TalkingId = Animator.StringToHash("Talking");
        private static readonly int SaluteId = Animator.StringToHash("Salute");

        void Start()
        {
            if (soldier != null) baseYaw = soldier.transform.eulerAngles.y;
            yesButton.onClick.AddListener(Accept);
            noButton.onClick.AddListener(Decline);
            Canvas canvas = prompt.GetComponent<Canvas>();
            if (canvas != null) canvas.worldCamera = Camera.main;
            if (desktopHint != null) desktopHint.gameObject.SetActive(!VRPlatformSwitch.IsVR);
            SetPromptVisible(false, true);
        }

        void Update()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            Vector3 me = soldier != null ? soldier.transform.position : transform.position;
            float dist = Vector2.Distance(new Vector2(eye.x, eye.z), new Vector2(me.x, me.z));

            // Face the trainee when they are around, otherwise his post.
            if (soldier != null)
            {
                Vector3 to = eye - me;
                float targetYaw = dist < lookRadius && !Busy ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : baseYaw;
                float yaw = Mathf.MoveTowardsAngle(soldier.transform.eulerAngles.y, targetYaw, turnSpeed * Time.deltaTime);
                soldier.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                if (talkTimer > 0f) talkTimer -= Time.deltaTime;
                bool talking = talkTimer > 0f || (voice != null && voice.Current != VoiceLine.None);
                if (soldier.runtimeAnimatorController != null) soldier.SetBool(TalkingId, talking);
            }

            Vector3 flat = new Vector3(eye.x - me.x, 0f, eye.z - me.z);
            bool inFront = Vector3.Angle(Quaternion.Euler(0f, baseYaw, 0f) * Vector3.forward, flat) < promptAngle;
            bool near = dist < promptRadius && inFront;
            UpdateTrainerVoice(eye.z > rangeEntryZ || Busy || state == State.Asking);
            switch (state)
            {
                case State.Away:
                    if (near && !Busy) Ask();
                    break;
                case State.Asking:
                    if (!near || Busy) Leave(State.Away);
                    else if (!VRPlatformSwitch.IsVR && Input.GetKeyDown(KeyCode.Return) && yesButton.gameObject.activeSelf) Accept();
                    else if (!VRPlatformSwitch.IsVR && Input.GetKeyDown(KeyCode.Backspace)) Decline();
                    break;
                case State.Dismissed:
                    if (dist > promptRadius * 1.3f) state = State.Away;
                    break;
            }

            if (prompt.alpha > 0f) FaceTrainee(eye);
            float target = state == State.Asking ? 1f : 0f;
            if (prompt.alpha != target)
            {
                prompt.alpha = Mathf.MoveTowards(prompt.alpha, target, Time.deltaTime / Mathf.Max(0.01f, fadeTime));
                if (prompt.alpha <= 0f) prompt.gameObject.SetActive(false);
            }
        }

        private void Ask()
        {
            state = State.Asking;
            bool ready = CanStart == null || CanStart();
            if (questionText != null) questionText.text = ready ? VRTrainingText.RangeOffer : VRTrainingText.RangeNotReady;
            yesButton.gameObject.SetActive(ready);
            if (desktopHint != null) desktopHint.text = ready ? VRTrainingText.RangeDesktopHint : VRTrainingText.RangeDesktopHintNotReady;
            Say(ready ? VoiceLine.Rng_Offer : VoiceLine.Rng_NotReady);
            SetPromptVisible(true, false);
        }

        // He speaks (and his talking animation follows the voice). The assembly trainer goes quiet.
        public void Say(params VoiceLine[] lines)
        {
            if (trainerVoice != null) trainerVoice.Stop();
            if (voice != null && lines.Length > 0 && voice.ClipFor(lines[0]) != null) voice.Play(lines);
            else talkTimer = talkTime;
        }

        private void Leave(State next)
        {
            state = next;
            prompt.interactable = prompt.blocksRaycasts = false;
        }

        private void Accept()
        {
            if (state != State.Asking) return;
            Leave(State.Dismissed);
            if (soldier != null && soldier.runtimeAnimatorController != null) soldier.SetTrigger(SaluteId);
            TrainingRequested?.Invoke();
            if (TrainingRequested == null) Debug.Log("[VR Armory] Fire training requested (the training round comes in the next step).");
        }

        private void Decline()
        {
            if (state != State.Asking) return;
            Leave(State.Dismissed);
            if (yesButton.gameObject.activeSelf) Say(VoiceLine.Rng_Declined);
            Declined?.Invoke();
        }

        private void SetPromptVisible(bool on, bool instant)
        {
            if (on) prompt.gameObject.SetActive(true);
            prompt.interactable = prompt.blocksRaycasts = on;
            if (instant)
            {
                prompt.alpha = on ? 1f : 0f;
                if (!on) prompt.gameObject.SetActive(false);
            }
        }

        private void UpdateTrainerVoice(bool silent)
        {
            if (trainerVoice == null || trainerVoice.muted == silent) return;
            trainerVoice.muted = silent;
            if (silent) trainerVoice.Stop();
        }

        // After the animation has posed him: lower / raise the whole body so the lowest toe sits on the floor.
        void LateUpdate()
        {
            if (!groundFeet || soldier == null || !soldier.isHuman) return;
            float low = float.MaxValue;
            Foot(HumanBodyBones.LeftToes, HumanBodyBones.LeftFoot, ref low);
            Foot(HumanBodyBones.RightToes, HumanBodyBones.RightFoot, ref low);
            if (low == float.MaxValue) return;
            float error = floorY - low;
            Vector3 p = soldier.transform.position;
            p.y += grounded ? error * Mathf.Clamp01(Time.deltaTime * 8f) : error;
            soldier.transform.position = p;
            grounded = true;
        }

        private void Foot(HumanBodyBones toes, HumanBodyBones foot, ref float low)
        {
            Transform t = soldier.GetBoneTransform(toes);
            if (t != null)
            {
                low = Mathf.Min(low, t.position.y - toeClearance);
                return;
            }
            t = soldier.GetBoneTransform(foot);
            if (t != null) low = Mathf.Min(low, t.position.y - 0.07f); // ankle height above the sole
        }

        // Turn the window about the vertical axis so it faces the trainee (canvas forward points away from the viewer).
        private void FaceTrainee(Vector3 eye)
        {
            Transform t = prompt.transform;
            Vector3 away = t.position - eye;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
