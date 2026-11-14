using System;
using UnityEngine;
using UnityEngine.UI;

namespace VRArmory
{
    // A weapon the trainee can pick at the range. 'model' is the visual root shown on the counter (its mesh renderers are
    // copied, no scripts); 'magazineModel' + 'magazineSeat' add the magazine as it sits in the grip when it is not
    // already under the model.
    [Serializable]
    public class RangeWeapon
    {
        public string displayName = "Beretta 92 FS";
        [Tooltip("WeaponKit.id of the gun (per-weapon wording and voice lines).")]
        public string id = "beretta92";
        [Tooltip("This weapon's live fire (its PistolMagazine is firing.ammo).")]
        public PistolFiring firing;
        public int magazineSize = 15;
        public Transform model;
        public Transform magazineModel;
        public Transform magazineSeat;
        [Tooltip("Optional: the gun's axes are read from its function check (found at run time) instead of the two below.")]
        public FunctionCheck functionCheck;
        [Tooltip("Bore direction and top of the gun, in the model's local space.")]
        public Vector3 muzzleLocal = Vector3.forward;
        public Vector3 upLocal = Vector3.up;
    }

    // The screen in front of the booth before a range session, card pages like the training menu:
    //   1. weapon (one card per weapon: name, its 3D model as seen lying on a table from above, magazine size),
    //   2. mode (Unlimited / Timed), 3. for Timed, the targets (Stationary / Moving).
    // Desktop: 1 / 2 / 3 pick a card, Backspace goes back (or ends on the first page).
    public class RangeModeMenu : MonoBehaviour
    {
        public RangeWeapon[] weapons = new RangeWeapon[0];

        public CanvasGroup panel;
        public GameObject weaponPage;
        public GameObject modePage;
        public GameObject targetsPage;
        public Button[] weaponCards = new Button[0];
        public Button unlimitedCard;
        public Button timedCard;
        public Button stationaryCard;
        public Button movingCard;
        [Tooltip("Body text of the Timed card (shows the chosen weapon's magazine size).")]
        public Text timedBodyText;
        public Button backButton;
        public Button cancelButton;
        public Text desktopHint;

        [Header("Model on the card")]
        [Tooltip("One per weapon card: the centre of the picture area on the card (a child of the card).")]
        public Transform[] modelSpots = new Transform[0];
        [Tooltip("The model is fitted inside this box on the card, metres.")]
        public Vector2 modelBox = new Vector2(0.27f, 0.11f);

        // weapon index, timed, moving targets
        public event Action<int, bool, bool> Chosen;
        public event Action Cancelled;

        public bool IsOpen { get; private set; }

        private enum Step { Weapon, Mode, Targets }
        private Step step;
        private int weapon;
        private bool[] available;
        private readonly System.Collections.Generic.List<GameObject> modelCopies = new System.Collections.Generic.List<GameObject>();

        void Start()
        {
            for (int i = 0; i < weaponCards.Length; i++)
            {
                int index = i;
                if (weaponCards[i] != null) weaponCards[i].onClick.AddListener(() => PickWeapon(index));
            }
            unlimitedCard.onClick.AddListener(() => Finish(false, false));
            timedCard.onClick.AddListener(() => Show(Step.Targets));
            stationaryCard.onClick.AddListener(() => Finish(true, false));
            movingCard.onClick.AddListener(() => Finish(true, true));
            backButton.onClick.AddListener(Back);
            cancelButton.onClick.AddListener(Cancel);
            Canvas canvas = panel.GetComponent<Canvas>();
            if (canvas != null) canvas.worldCamera = Camera.main;
            if (desktopHint != null) desktopHint.gameObject.SetActive(!VRPlatformSwitch.IsVR);
            if (!IsOpen) panel.gameObject.SetActive(false);
        }

        // Which weapons can go to the range (assembled now or in an earlier session): the others' cards are hidden and
        // the ones left are spread evenly. null = all.
        public void SetAvailable(bool[] available)
        {
            this.available = available;
            int shown = 0;
            for (int i = 0; i < weaponCards.Length; i++) if (Available(i)) shown++;
            int slot = 0;
            for (int i = 0; i < weaponCards.Length; i++)
            {
                if (weaponCards[i] == null) continue;
                bool on = Available(i);
                weaponCards[i].gameObject.SetActive(on);
                if (!on) continue;
                var rt = (RectTransform)weaponCards[i].transform;
                rt.anchoredPosition = new Vector2((slot - (shown - 1) * 0.5f) * 356f, rt.anchoredPosition.y);
                slot++;
            }
        }

        // The n-th shown card (desktop keys 1 / 2 / ...), -1 if none.
        private int ShownWeapon(int n)
        {
            for (int i = 0; i < weapons.Length; i++)
                if (Available(i) && n-- == 0) return i;
            return -1;
        }

        private bool Available(int index) => index >= 0 && index < weapons.Length && (available == null || (index < available.Length && available[index]));

        public void Open()
        {
            IsOpen = true;
            panel.gameObject.SetActive(true);
            Show(Step.Weapon);
        }

        public void Close()
        {
            IsOpen = false;
            panel.gameObject.SetActive(false);
            ClearModels();
        }

        void Update()
        {
            if (!IsOpen || VRPlatformSwitch.IsVR) return;
            int key = Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1) ? 0
                    : Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2) ? 1
                    : Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3) ? 2 : -1;
            if (key >= 0)
            {
                switch (step)
                {
                    case Step.Weapon:
                        int pick = ShownWeapon(key);
                        if (pick >= 0) PickWeapon(pick);
                        break;
                    case Step.Mode: if (key == 0) Finish(false, false); else if (key == 1) Show(Step.Targets); break;
                    case Step.Targets: if (key < 2) Finish(true, key == 1); break;
                }
            }
            else if (Input.GetKeyDown(KeyCode.Backspace))
            {
                if (step == Step.Weapon) Cancel();
                else Back();
            }
        }

        private void PickWeapon(int index)
        {
            if (!IsOpen || step != Step.Weapon || !Available(index)) return;
            weapon = index;
            Show(Step.Mode);
        }

        private void Finish(bool timed, bool moving)
        {
            if (!IsOpen) return;
            Close();
            Chosen?.Invoke(weapon, timed, moving);
        }

        private void Cancel()
        {
            if (!IsOpen) return;
            Close();
            Cancelled?.Invoke();
        }

        private void Back()
        {
            if (step == Step.Targets) Show(Step.Mode);
            else if (step == Step.Mode) Show(Step.Weapon);
        }

        private void Show(Step s)
        {
            step = s;
            weaponPage.SetActive(s == Step.Weapon);
            modePage.SetActive(s == Step.Mode);
            targetsPage.SetActive(s == Step.Targets);
            backButton.gameObject.SetActive(s != Step.Weapon);
            if (timedBodyText != null && weapons.Length > 0)
                timedBodyText.text = VRTrainingText.RangeTimedCardBody(weapons[weapon].magazineSize);
            if (s == Step.Weapon) BuildModels();
            else ClearModels();
        }

        // ---------- Weapon models on the counter ----------

        private void ClearModels()
        {
            foreach (GameObject go in modelCopies)
                if (go != null) Destroy(go);
            modelCopies.Clear();
        }

        // A fixed copy of each weapon's meshes (no colliders or scripts) on its card, under the name: seen as when the gun
        // lies on a table and you look down at it - the side that faces up on the bench faces you, top of the gun up.
        private void BuildModels()
        {
            if (modelCopies.Count > 0) return;
            for (int i = 0; i < weapons.Length && i < modelSpots.Length; i++)
            {
                RangeWeapon w = weapons[i];
                Transform spot = modelSpots[i];
                if (w == null || w.model == null || spot == null || !Available(i)) continue;

                // The weapon's kit builds it as assembled from wherever its parts are (a weapon not built this session
                // has its slide etc. still apart on the bench, so copying the frame alone would leave them out).
                WeaponKit kit = KitOf(w);
                if (kit != null)
                {
                    GameObject copy = kit.BuildAssembledCopy(spot, modelBox);
                    if (copy != null) modelCopies.Add(copy);
                    continue;
                }

                Transform root = new GameObject(w.displayName + "_Model").transform;
                root.SetParent(spot, false);
                root.gameObject.layer = 0;
                modelCopies.Add(root.gameObject);
                var content = new GameObject("Content").transform;
                content.SetParent(root, false);
                content.gameObject.layer = 0;

                Matrix4x4 toModel = w.model.worldToLocalMatrix;
                CopyMeshes(w.model, w.model, toModel, content);
                if (w.magazineModel != null && w.magazineSeat != null && !w.magazineModel.IsChildOf(w.model))
                {
                    // Where the magazine sits when seated in the grip, then its meshes relative to its own root.
                    Matrix4x4 seat = Matrix4x4.TRS(w.magazineSeat.position, w.magazineSeat.rotation, w.magazineSeat.lossyScale);
                    CopyMeshes(w.magazineModel, w.magazineModel, toModel * seat * w.magazineModel.worldToLocalMatrix, content);
                }

                // Turn: gun top -> card up, the face that is up on the bench -> toward the viewer (card -Z).
                bool fc = w.functionCheck != null && w.functionCheck.HasRig;
                Vector3 upL = fc ? w.functionCheck.UpLocal : w.upLocal;
                Vector3 muzzleL = fc ? w.functionCheck.MuzzleLocal : w.muzzleLocal;
                Vector3 up = upL.sqrMagnitude > 0f ? upL.normalized : Vector3.up;
                Vector3 side = Vector3.Cross(muzzleL, up);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                side.Normalize();
                AssemblyPart part = w.model.GetComponent<AssemblyPart>();
                Quaternion bench = part != null ? part.HomeWorldRotation : w.model.rotation;
                if (bench.x == 0f && bench.y == 0f && bench.z == 0f && bench.w == 0f) bench = w.model.rotation; // part never started (its kit is off this session)
                Vector3 face = Vector3.Dot(bench * side, Vector3.up) >= 0f ? side : -side;
                content.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(face, up));

                // Fit inside the picture box and centre it, just in front of the card.
                Bounds b = LocalBounds(root, content);
                if (b.size == Vector3.zero) continue;
                float spotScale = Mathf.Max(1e-6f, spot.lossyScale.x);
                float fit = Mathf.Min(modelBox.x / Mathf.Max(1e-6f, b.size.x), modelBox.y / Mathf.Max(1e-6f, b.size.y)); // world m per root unit
                float s = fit / spotScale;
                root.localScale = Vector3.one * s;
                root.localPosition = new Vector3(-b.center.x * s, -b.center.y * s, (-b.max.z * s) - 0.004f / spotScale);
            }
        }

        private static WeaponKit KitOf(RangeWeapon w)
        {
            if (WeaponKitSwitch.Instance == null || w.functionCheck == null) return null;
            foreach (WeaponKit k in WeaponKitSwitch.Instance.kits)
                if (k != null && k.functionCheck == w.functionCheck) return k;
            return null;
        }

        // Bounds of every mesh under 'content', in 'space' local coordinates.
        private static Bounds LocalBounds(Transform space, Transform content)
        {
            Bounds b = new Bounds();
            bool any = false;
            foreach (MeshFilter mf in content.GetComponentsInChildren<MeshFilter>())
            {
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        // Copies each enabled MeshRenderer under 'root' (even while the weapon itself is hidden), placed by
        // 'worldToSpace' (world -> the copy's model space).
        private static void CopyMeshes(Transform root, Transform activeRoot, Matrix4x4 worldToSpace, Transform parent)
        {
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled || !ActiveUnder(r.transform, activeRoot)) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (r.GetComponentInParent<Canvas>(true) != null) continue;
                Matrix4x4 m = worldToSpace * r.transform.localToWorldMatrix;
                var go = new GameObject(r.name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = m.GetColumn(3);
                go.transform.localRotation = m.rotation;
                go.transform.localScale = m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
            }
        }

        // Active ignoring the root's own state (the weapon may be hidden while its model is shown here).
        private static bool ActiveUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }
    }
}
