using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // Ammunition for live fire (Beretta 92 FS: 15-round magazine). Works on the assembled pistol once its function check
    // has passed:
    //  - the chamber is loaded by racking the slide with a loaded magazine in (FunctionCheck.SlideRacked); racking with a
    //    round already chambered changes nothing (no round is wasted);
    //  - each shot takes the chambered round and feeds the next from the magazine; when the magazine is empty the slide
    //    locks open (with no magazine in, the slide simply closes on an empty chamber);
    //  - magazine release: B / Y on the hand holding the pistol (desktop: M). An empty magazine drops with physics and
    //    disappears after a few seconds while a fresh one appears on the bench; a magazine with rounds left flies back to
    //    the bench. Push a magazine up into the grip to insert it (its SnapSocket, as in assembly);
    //  - free practice (refillOnInsert): every inserted magazine is full. The fire training turns this off.
    //  - a small readout above the pistol: rounds left and what to do next (RACK THE SLIDE, SLIDE LOCKED, ...).
    public class PistolMagazine : MonoBehaviour
    {
        public FunctionCheck functionCheck;
        public VRAssemblyGrab frameGrab;
        public AssemblyPart magazine;
        public SnapSocket magazineWell;

        public int capacity = 15;
        [Tooltip("Free practice: a magazine is full every time it is inserted.")]
        public bool refillOnInsert = true;
        [Tooltip("Seconds an empty dropped magazine stays on the floor before it disappears.")]
        public float spentLifetime = 4f;
        public bool showReadout = true;
        [Range(0f, 1f)] public float releaseHaptic = 0.35f;

        public static PistolMagazine Instance { get; private set; }

        public int RoundsInMagazine { get; private set; }
        public bool Chambered { get; private set; }
        public bool MagazineInserted { get; private set; }
        public int RoundsLeft => RoundsInMagazine + (Chambered ? 1 : 0);
        public event System.Action Changed;

        private InputAction leftRelease, rightRelease;
        private AudioSource audioSource;
        private AudioClip releaseClip;
        private Transform readout;
        private Text readoutText;
        private string shownText;

        private bool Live => functionCheck != null && functionCheck.Passed && functionCheck.HasRig && FireTraining.LiveFireAllowed;

        void Awake()
        {
            Instance = this;
            leftRelease = new InputAction("Left Magazine Release", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
            rightRelease = new InputAction("Right Magazine Release", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.5f;
            releaseClip = ProceduralAudio.MakeClick("Magazine_Release", 0.03f, 1700f, 0.8f);
        }

        void Start()
        {
            MagazineInserted = magazine != null && magazine.IsSeated;
            RoundsInMagazine = capacity;
            if (magazineWell != null) magazineWell.Seated += OnWellSeated;
            if (functionCheck != null) functionCheck.SlideRacked += OnSlideRacked;
            if (showReadout) BuildReadout();
        }

        void OnEnable()
        {
            leftRelease.Enable();
            rightRelease.Enable();
        }

        void OnDisable()
        {
            leftRelease.Disable();
            rightRelease.Disable();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (magazineWell != null) magazineWell.Seated -= OnWellSeated;
            if (functionCheck != null) functionCheck.SlideRacked -= OnSlideRacked;
            leftRelease.Dispose();
            rightRelease.Dispose();
        }

        // Called by PistolFiring when the hammer falls. False: empty chamber (just the click). lockOpen: the magazine is
        // empty, so the slide stays back after this shot.
        public bool TakeChamberedRound(out bool lockOpen)
        {
            lockOpen = false;
            if (!Chambered) return false;
            Chambered = false;
            if (MagazineInserted && RoundsInMagazine > 0)
            {
                RoundsInMagazine--;
                Chambered = true;
            }
            else lockOpen = MagazineInserted; // the empty magazine's follower lifts the slide stop
            Changed?.Invoke();
            return true;
        }

        // Full magazine in, nothing chambered (the fire training hands the pistol over like this, magazine out).
        public void ResetAmmo(bool magazineFull)
        {
            RoundsInMagazine = magazineFull ? capacity : 0;
            Chambered = false;
            Changed?.Invoke();
        }

        // Fire training: the magazine comes out of the pistol (if in) without a sound and is loaded with 'rounds'; nothing
        // chambered, slide closed. The caller puts the magazine where it should be.
        public void PrepareMagazine(int rounds)
        {
            if (MagazineInserted && magazine != null)
            {
                MagazineInserted = false;
                magazine.Unseat();
                if (magazineWell != null) magazineWell.Empty();
            }
            RoundsInMagazine = Mathf.Clamp(rounds, 0, capacity);
            Chambered = false;
            if (functionCheck != null) functionCheck.ReleaseSlideLock();
            Changed?.Invoke();
        }

        // Is B / Y on this side the magazine release right now (then it is not the voice "repeat" button)?
        public static bool OwnsSecondaryButton(bool left)
        {
            PistolMagazine m = Instance;
            if (m == null || !m.Live || m.frameGrab == null || !m.frameGrab.isSelected) return false;
            IXRSelectInteractor hand = m.frameGrab.interactorsSelecting[0];
            VRHands hands = VRHands.Instance;
            if (hands == null || hands.IsTrackedHand(hand, out _)) return false;
            return hands.IsLeft(hand) == left;
        }

        void Update()
        {
            if (!Live)
            {
                if (readout != null && readout.gameObject.activeSelf) readout.gameObject.SetActive(false);
                return;
            }
            if (MagazineInserted && ReleasePressed()) Release();
            UpdateReadout();
        }

        private bool ReleasePressed()
        {
            if (OwnsSecondaryButton(true) && leftRelease.WasPressedThisFrame()) return true;
            if (OwnsSecondaryButton(false) && rightRelease.WasPressedThisFrame()) return true;
            return Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(KeyCode.M);
        }

        private void Release()
        {
            MagazineInserted = false;
            magazine.Unseat();
            if (magazineWell != null) magazineWell.Empty();
            audioSource.transform.position = magazine.transform.position;
            audioSource.PlayOneShot(releaseClip, 0.8f);
            if (frameGrab != null && frameGrab.isSelected) VRAssemblyGrab.Pulse(frameGrab.interactorsSelecting[0], releaseHaptic, 0.04f);

            if (RoundsInMagazine == 0)
            {
                // Spent: it falls out and is gone in a few seconds; a fresh one is waiting on the bench.
                Vector3 down = functionCheck.frame != null ? -functionCheck.frame.transform.TransformDirection(functionCheck.UpLocal).normalized : Vector3.down;
                SpentMagazine.Spawn(magazine.transform, down * 0.6f, spentLifetime, functionCheck.frame != null ? functionCheck.frame.transform : null);
                magazine.SnapHome();
                // Free practice: a fresh, full one waits on the bench. Timed: that was the only magazine, it is gone.
                if (refillOnInsert) RoundsInMagazine = capacity;
                else magazine.gameObject.SetActive(false);
            }
            else magazine.ReturnHome();
            Changed?.Invoke();
        }

        private void OnWellSeated(SnapSocket socket)
        {
            MagazineInserted = true;
            if (refillOnInsert) RoundsInMagazine = capacity;
            Changed?.Invoke();
        }

        private void OnSlideRacked()
        {
            if (!Live || Chambered || !MagazineInserted || RoundsInMagazine <= 0) return;
            RoundsInMagazine--;
            Chambered = true;
            Changed?.Invoke();
        }

        // ---------- Readout ----------

        private void BuildReadout()
        {
            var go = new GameObject("Ammo_Readout", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(280f, 130f);
            rt.localScale = Vector3.one * 0.00045f; // about 12.6 x 5.9 cm

            var bg = new GameObject("Background", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            Stretch((RectTransform)bg.transform);
            bg.color = new Color(0.04f, 0.05f, 0.07f, 0.7f);
            bg.raycastTarget = false;

            readoutText = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            readoutText.transform.SetParent(go.transform, false);
            Stretch((RectTransform)readoutText.transform);
            readoutText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            readoutText.alignment = TextAnchor.MiddleCenter;
            readoutText.supportRichText = true;
            readoutText.color = Color.white;
            readoutText.raycastTarget = false;
            readoutText.horizontalOverflow = HorizontalWrapMode.Overflow;
            readoutText.verticalOverflow = VerticalWrapMode.Overflow;
            readout = go.transform;
            go.SetActive(false);
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        private void UpdateReadout()
        {
            if (readout == null) return;
            Camera cam = Camera.main;
            Vector3 muzzle = functionCheck.MuzzlePosition;
            bool show = cam != null && Vector3.Distance(cam.transform.position, muzzle) < 1.3f;
            if (readout.gameObject.activeSelf != show) readout.gameObject.SetActive(show);
            if (!show) return;

            // Just above the rear of the slide, turned to the eye.
            Vector3 p = muzzle - functionCheck.MuzzleDirection * 0.11f + Vector3.up * 0.085f;
            readout.position = p;
            readout.rotation = Quaternion.LookRotation(p - cam.transform.position, Vector3.up);

            string status, color = "#F5B83D";
            if (!MagazineInserted) status = Chambered ? "NO MAGAZINE - 1 IN CHAMBER" : "INSERT A MAGAZINE";
            else if (functionCheck.SlideLocked) status = "EMPTY - SLIDE LOCKED";
            else if (!Chambered) status = RoundsInMagazine > 0 ? "RACK THE SLIDE" : "EMPTY";
            else if (functionCheck.SafetyOn) status = "SAFETY ON";
            else
            {
                status = "READY";
                color = "#5CE68A";
            }
            string text = $"<size=58><b>{RoundsLeft}</b></size><size=28> / {capacity}</size>\n<size=24><color={color}>{status}</color></size>";
            if (text != shownText)
            {
                shownText = text;
                readoutText.text = text;
            }
        }
    }
}
