using System.Collections.Generic;
using UnityEngine;

namespace VRArmory
{
    // One weapon the trainee can assemble. Sits on the group that holds the weapon's staged parts (its sockets ride on
    // those parts), so switching the group off removes the whole weapon from the bench. Only one kit is on at a time:
    // WeaponKitSwitch turns the chosen one on when the scene loads and hands its steps to the assembly manager.
    public class WeaponKit : MonoBehaviour
    {
        [Tooltip("Stable key (saved best time, the choice kept across a scene reload).")]
        public string id = "beretta92";
        public string displayName = "Beretta 92 FS";
        [Tooltip("Second line of the desktop HUD header.")]
        public string hudSubtitle = "";
        public AssemblyPart frame;
        public FunctionCheck functionCheck;
        public List<AssemblyStep> steps = new List<AssemblyStep>();
        [Tooltip("Bore direction and top of the gun, in the frame's local space (for the picture on the weapon card).")]
        public Vector3 muzzleLocal = Vector3.left;
        public Vector3 upLocal = Vector3.forward;

        public int PartCount => GetComponentsInChildren<AssemblyPart>(true).Length;

        private string BestKey => "VRArmory.BestTime." + id;
        // Fastest finished assembly in seconds, 0 = never finished.
        public int BestSeconds => PlayerPrefs.GetInt(BestKey, 0);

        // Assembled and function-checked at least once (any session): this weapon stays available at the range.
        private string UnlockKey => "VRArmory.RangeUnlocked." + id;
        public bool RangeUnlocked => PlayerPrefs.GetInt(UnlockKey, 0) == 1;

        public void UnlockRange()
        {
            if (RangeUnlocked) return;
            PlayerPrefs.SetInt(UnlockKey, 1);
            PlayerPrefs.Save();
        }

        public void ReportTime(int seconds)
        {
            if (seconds <= 0 || (BestSeconds > 0 && BestSeconds <= seconds)) return;
            PlayerPrefs.SetInt(BestKey, seconds);
            PlayerPrefs.Save();
        }

        // A fixed copy of the weapon as it looks assembled (meshes only), fitted inside 'box' (metres) at 'spot' and seen
        // from the side with the muzzle to the left. Each part is placed where its socket seats it, so this works while
        // the parts still lie apart on the bench, and equally once some or all of them are seated.
        public GameObject BuildAssembledCopy(Transform spot, Vector2 box)
        {
            if (frame == null || spot == null) return null;
            var socketOf = new Dictionary<AssemblyPart, SnapSocket>();
            foreach (SnapSocket socket in GetComponentsInChildren<SnapSocket>(true))
                if (socket.acceptedPart != null) socketOf[socket.acceptedPart] = socket;

            Transform root = new GameObject(displayName + "_Model").transform;
            root.SetParent(spot, false);
            Transform content = new GameObject("Content").transform;
            content.SetParent(root, false);

            foreach (AssemblyPart part in GetComponentsInChildren<AssemblyPart>(true))
            {
                if (!AssembledPose(part, socketOf, 0, out Matrix4x4 pose)) continue;
                Matrix4x4 toPart = part.transform.worldToLocalMatrix;
                foreach (MeshRenderer r in part.GetComponentsInChildren<MeshRenderer>(true))
                {
                    // Own meshes only: not a part already seated inside this one, not a socket's ghost, nothing hidden.
                    if (!r.enabled || r.GetComponentInParent<AssemblyPart>(true) != part) continue;
                    if (r.GetComponentInParent<SnapSocket>(true) != null || r.GetComponentInParent<Canvas>(true) != null) continue;
                    if (!ActiveUnder(r.transform, part.transform)) continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;

                    Matrix4x4 m = pose * toPart * r.transform.localToWorldMatrix;
                    var go = new GameObject(r.name);
                    go.transform.SetParent(content, false);
                    go.transform.localPosition = m.GetColumn(3);
                    go.transform.localRotation = m.rotation;
                    go.transform.localScale = m.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                }
            }

            // Gun top -> up, one side toward the viewer (-Z of the spot); the side that puts the muzzle on the left.
            Vector3 up = upLocal.sqrMagnitude > 0f ? upLocal.normalized : Vector3.up;
            Vector3 side = Vector3.Cross(muzzleLocal, up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            Quaternion toViewer = Quaternion.LookRotation(Vector3.back, Vector3.up);
            Quaternion turn = toViewer * Quaternion.Inverse(Quaternion.LookRotation(side, up));
            if ((turn * muzzleLocal).x > 0f) turn = toViewer * Quaternion.Inverse(Quaternion.LookRotation(-side, up));
            content.localRotation = turn;

            Bounds b = LocalBounds(root, content);
            if (b.size == Vector3.zero) return root.gameObject;
            float spotScale = Mathf.Max(1e-6f, spot.lossyScale.x);
            float fit = Mathf.Min(box.x / Mathf.Max(1e-6f, b.size.x), box.y / Mathf.Max(1e-6f, b.size.y));
            float s = fit / spotScale;
            root.localScale = Vector3.one * s;
            root.localPosition = new Vector3(-b.center.x * s, -b.center.y * s, (-b.max.z * s) - 0.004f / spotScale);
            return root.gameObject;
        }

        // Where 'part' sits in the assembled weapon, in the frame's local space: the frame itself, or the part's socket
        // followed up through the parts that socket rides on.
        private bool AssembledPose(AssemblyPart part, Dictionary<AssemblyPart, SnapSocket> socketOf, int depth, out Matrix4x4 pose)
        {
            pose = Matrix4x4.identity;
            if (part == frame) return true;
            if (depth > 8 || !socketOf.TryGetValue(part, out SnapSocket socket)) return false;
            AssemblyPart host = socket.GetComponentInParent<AssemblyPart>(true);
            if (host == null || !AssembledPose(host, socketOf, depth + 1, out Matrix4x4 hostPose)) return false;
            pose = hostPose * host.transform.worldToLocalMatrix * socket.transform.localToWorldMatrix;
            return true;
        }

        private static bool ActiveUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

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
    }

    // Runs before everything else in the scene: switches on the chosen weapon's kit (the first one unless the trainee
    // picked another before the scene was reloaded) and points the assembly manager and the VR helpers at it. They all
    // read these references in their own Awake / OnEnable / Start, which come later.
    [DefaultExecutionOrder(-10000)]
    public class WeaponKitSwitch : MonoBehaviour
    {
        public static WeaponKitSwitch Instance { get; private set; }

        // Kept across the scene reload that follows a choice.
        private static string chosenId;

        public WeaponKit[] kits = new WeaponKit[0];
        public GunAssemblyManager manager;
        [Header("VR helpers (may be empty)")]
        public VRTrainingMenu menu;
        public VRGuidance guidance;
        public VRTrainingCoach coach;
        public VRGhostDemo ghostDemo;
        public VRFunctionCheckControls functionCheckControls;

        public WeaponKit Active { get; private set; }

        // Takes effect when the scene is loaded again.
        public static void Choose(WeaponKit kit)
        {
            chosenId = kit != null ? kit.id : null;
        }

        void Awake()
        {
            Instance = this;
            foreach (WeaponKit kit in kits)
                if (kit != null && (Active == null || kit.id == chosenId)) Active = kit;
            if (Active == null) return;

            foreach (WeaponKit kit in kits)
                if (kit != null) kit.gameObject.SetActive(kit == Active);

            if (manager != null)
            {
                manager.steps = Active.steps;
                if (!string.IsNullOrEmpty(Active.hudSubtitle)) manager.hudSubtitle = Active.hudSubtitle;
            }
            // A weapon without a function check yet keeps the helpers on the (never started) default one.
            FunctionCheck check = Active.functionCheck;
            if (check == null) return;
            if (menu != null) menu.functionCheck = check;
            if (guidance != null) guidance.functionCheck = check;
            if (coach != null) coach.functionCheck = check;
            if (ghostDemo != null) ghostDemo.functionCheck = check;
            if (functionCheckControls != null)
            {
                functionCheckControls.functionCheck = check;
                if (Active.frame != null) functionCheckControls.frameGrab = Active.frame.GetComponent<VRAssemblyGrab>();
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
