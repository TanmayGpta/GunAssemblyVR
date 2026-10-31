#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRArmory.Editor
{
    // Wires Phase 02 assembly into the scene: AssemblyPart on all 5 modules, Socket_Barrel on the slide,
    // Gun_Assembly_Manager + step list, AssemblyHUD on the HUD canvas, desktop interactor on the player.
    public static class AssemblySetup
    {
        private const string GhostMatPath = "Assets/Materials/Assembly_Ghost.mat";

        [MenuItem("Tools/VR Armory/Setup Phase 02 Assembly")]
        public static void SetupAssembly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                Debug.LogWarning("[VR Armory] Exit Play mode before setting up assembly.");
                return;
            }
            if (!ArmoryEditorGuard.Confirm("Setup Phase 02 Assembly", "Re-creates the assembly step list on Gun_Assembly_Manager (any edits to the steps are replaced).")) return;

            GameObject station = GameObject.Find("ArmorerStation");
            Transform parts = station != null ? station.transform.Find("Staged_Gun_Parts") : null;
            if (parts == null)
            {
                Debug.LogWarning("[VR Armory] ArmorerStation/Staged_Gun_Parts not found.");
                return;
            }

            var modules = new Dictionary<string, GunPart>
            {
                { "01_Lower_Frame", GunPart.LowerFrame },
                { "02_Upper_Slide", GunPart.UpperSlide },
                { "03_Barrel", GunPart.Barrel },
                { "04_Recoil_Spring", GunPart.RecoilSpring },
                { "05_15Rd_Magazine", GunPart.Magazine },
            };
            var partLookup = new Dictionary<GunPart, AssemblyPart>();
            foreach (var kv in modules)
            {
                Transform m = parts.Find(kv.Key);
                if (m == null) continue;
                AssemblyPart ap = GetOrAdd<AssemblyPart>(m.gameObject);
                ap.partType = kv.Value;
                partLookup[kv.Value] = ap;
            }

            // One socket per socketed step, parented to its host module. Seat poses are computed by Re-Stage Gun Parts.
            var sockets = new Dictionary<string, SnapSocket>();
            foreach (SocketSpec spec in Sockets)
            {
                Transform host = parts.Find(spec.host), part = parts.Find(spec.part);
                if (host == null || part == null) continue;
                Transform socketT = host.Find(spec.socket);
                if (socketT == null)
                {
                    socketT = new GameObject(spec.socket).transform;
                    socketT.SetParent(host, false);
                }
                SnapSocket socket = GetOrAdd<SnapSocket>(socketT.gameObject);
                socket.acceptedPart = part.GetComponent<AssemblyPart>();
                socket.ghostTemplate = GetOrCreateGhostMaterial();
                EditorUtility.SetDirty(socket);
                sockets[spec.socket] = socket;
            }

            // HUD binding.
            Transform hudT = station.transform.Find("Tactical_HUD_Canvas");
            AssemblyHUD hud = hudT != null ? (GetOrAdd<AssemblyHUD>(hudT.gameObject)) : null;

            // Manager + Phase 02 step list.
            Transform mgrT = station.transform.Find("Gun_Assembly_Manager");
            if (mgrT == null)
            {
                mgrT = new GameObject("Gun_Assembly_Manager").transform;
                mgrT.SetParent(station.transform, false);
            }
            GunAssemblyManager mgr = GetOrAdd<GunAssemblyManager>(mgrT.gameObject);
            mgr.hud = hud;
            FunctionCheck fc = GetOrAdd<FunctionCheck>(mgrT.gameObject);
            partLookup.TryGetValue(GunPart.LowerFrame, out fc.frame);
            partLookup.TryGetValue(GunPart.UpperSlide, out fc.slide);
            EditorUtility.SetDirty(fc);
            mgr.steps = new List<AssemblyStep>
            {
                new AssemblyStep { title = "BARREL SEATED IN SLIDE", part = GunPart.Barrel, socket = Find(sockets, "Socket_Barrel"),
                    instruction = "PICK UP 03_BARREL AND SEAT IT IN THE SLIDE (FOLLOW THE GHOST)" },
                new AssemblyStep { title = "RECOIL SPRING + GUIDE ROD", part = GunPart.RecoilSpring, socket = Find(sockets, "Socket_RecoilSpring"),
                    instruction = "INSERT RECOIL SPRING AND GUIDE ROD UNDER THE BARREL" },
                new AssemblyStep { title = "SLIDE MOUNTED ON FRAME", part = GunPart.UpperSlide, socket = Find(sockets, "Socket_Slide"),
                    instruction = "SLIDE THE UPPER ASSEMBLY ONTO THE FRAME RAILS" },
                new AssemblyStep { title = "MAGAZINE INSERTED", part = GunPart.Magazine, socket = Find(sockets, "Socket_Magazine"),
                    instruction = "INSERT THE 15-ROUND MAGAZINE INTO THE GRIP" },
                new AssemblyStep { title = "FUNCTION CHECK", part = GunPart.LowerFrame, functionCheck = fc,
                    instruction = "PERFORM SAFETY + FUNCTION CHECK" },
            };

            // Desktop interactor on the player.
            GameObject player = GameObject.Find("Armorer_Player");
            if (player != null)
            {
                DesktopAssemblyInteractor di = GetOrAdd<DesktopAssemblyInteractor>(player);
                di.playerCamera = player.GetComponentInChildren<Camera>();
            }

            EditorUtility.SetDirty(mgr);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(station.scene);
            Debug.Log($"[VR Armory] Phase 02 assembly wired: 5 AssemblyParts, {sockets.Count} sockets, Gun_Assembly_Manager, AssemblyHUD, DesktopAssemblyInteractor.");
        }

        private enum InsertFrom { Below, Front }

        private struct SocketSpec
        {
            public string socket, host, part;
            public bool inset;       // seated inside the host body (barrel, spring, magazine) vs. mounted on it (slide on frame)
            public InsertFrom from;  // direction the part arrives from, for the ghost path line
        }

        private static readonly SocketSpec[] Sockets =
        {
            new SocketSpec { socket = "Socket_Barrel", host = "02_Upper_Slide", part = "03_Barrel", inset = true, from = InsertFrom.Below },
            new SocketSpec { socket = "Socket_RecoilSpring", host = "02_Upper_Slide", part = "04_Recoil_Spring", inset = true, from = InsertFrom.Below },
            new SocketSpec { socket = "Socket_Slide", host = "01_Lower_Frame", part = "02_Upper_Slide", inset = false, from = InsertFrom.Front },
            new SocketSpec { socket = "Socket_Magazine", host = "01_Lower_Frame", part = "05_15Rd_Magazine", inset = true, from = InsertFrom.Below },
        };

        private static SnapSocket Find(Dictionary<string, SnapSocket> sockets, string name)
        {
            return sockets.TryGetValue(name, out SnapSocket s) ? s : null;
        }

        // =========================================================
        // RE-STAGE: factory-assembled sub-pieces, real-world scale, one evenly spaced row on the mat.
        // =========================================================
        private const float PartWorldScale = 1.0f;   // 1.0 = real Beretta 92 FS size
        private const float MatMargin = 0.12f;
        private static readonly string[] ModuleOrder = { "01_Lower_Frame", "02_Upper_Slide", "03_Barrel", "04_Recoil_Spring", "05_15Rd_Magazine" };

        [MenuItem("Tools/VR Armory/Re-Stage Gun Parts")]
        public static void RestageParts()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                Debug.LogWarning("[VR Armory] Exit Play mode before re-staging parts.");
                return;
            }
            if (!ArmoryEditorGuard.Confirm("Re-Stage Gun Parts", "Moves all five gun modules back to their bench row and recomputes every socket seat pose.")) return;

            GameObject station = GameObject.Find("ArmorerStation");
            Transform parts = station != null ? station.transform.Find("Staged_Gun_Parts") : null;
            Transform mat = station != null ? station.transform.Find("Workbench/Armorer_Rubber_Mat") : null;
            if (parts == null || mat == null)
            {
                Debug.LogWarning("[VR Armory] Staged_Gun_Parts or Armorer_Rubber_Mat not found.");
                return;
            }
            Bounds matBounds = mat.GetComponent<Renderer>().bounds;

            var modules = new List<Transform>();
            foreach (string n in ModuleOrder)
            {
                Transform m = parts.Find(n);
                if (m != null) modules.Add(m);
            }

            // Original assembled pose of every FBX mesh, relative to the FBX root (scene meshes are the FBX's sub-assets).
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null)
            {
                Debug.LogWarning("[VR Armory] " + FbxPath + " not found.");
                return;
            }
            var factory = new Dictionary<Mesh, Matrix4x4>();
            foreach (MeshFilter mf in fbx.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null && !factory.ContainsKey(mf.sharedMesh))
                    factory[mf.sharedMesh] = fbx.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            }

            // 1. beretta92.fbx is an exploded view: collapse each sub-piece back onto its module's main body, rescale.
            var mains = new Dictionary<Transform, Transform>();
            foreach (Transform m in modules)
            {
                m.rotation = Quaternion.identity;
                m.localScale = Vector3.one;
                Transform main = MainPiece(m);
                if (main == null || !factory.ContainsKey(main.GetComponent<MeshFilter>().sharedMesh))
                {
                    Debug.LogWarning($"[VR Armory] {m.name}: main body mesh not found in {FbxPath}; left as-is.");
                    continue;
                }
                mains[m] = main;
                main.localPosition = Vector3.zero;
                main.localRotation = Quaternion.identity;

                Mesh mainMesh = main.GetComponent<MeshFilter>().sharedMesh;
                foreach (Transform c in m)
                {
                    MeshFilter cmf = c.GetComponent<MeshFilter>();
                    if (c == main || cmf == null || !factory.ContainsKey(cmf.sharedMesh)) continue;
                    bool inset = System.Array.IndexOf(InsetPieces, c.name) >= 0;
                    ApplyLocal(c, CollapsedPose(main.localScale, factory[mainMesh], mainMesh, factory[cmf.sharedMesh], cmf.sharedMesh, inset));
                }

                m.localScale = Vector3.one * (PartWorldScale / main.localScale.x);
            }

            // 2. Socket seats: the part's main body, exploded pose collapsed into/onto the host's main body,
            //    expressed for the part's module root (part main sits at its module origin).
            var socketTs = new List<(Transform socket, SocketSpec spec)>();
            foreach (SocketSpec spec in Sockets)
            {
                Transform hostM = parts.Find(spec.host), partM = parts.Find(spec.part);
                Transform socketT = hostM != null ? hostM.Find(spec.socket) : null;
                if (socketT == null || !mains.ContainsKey(hostM) || !mains.ContainsKey(partM)) continue;
                Transform hm = mains[hostM], pm = mains[partM];
                Mesh hMesh = hm.GetComponent<MeshFilter>().sharedMesh, pMesh = pm.GetComponent<MeshFilter>().sharedMesh;
                Matrix4x4 partMainInHost = CollapsedPose(hm.localScale, factory[hMesh], hMesh, factory[pMesh], pMesh, spec.inset);
                ApplyLocal(socketT, partMainInHost * Matrix4x4.TRS(Vector3.zero, Quaternion.identity, pm.localScale).inverse);
                socketT.localScale = Vector3.one;
                socketTs.Add((socketT, spec));
            }
            Transform slideM = parts.Find("02_Upper_Slide"), frameM = parts.Find("01_Lower_Frame");
            AlignMagazineToGrip(frameM, parts.Find("05_15Rd_Magazine"), mains, factory);

            // 3. Same yaw for every module (keeps barrel/slide alignment) so the muzzle points downrange (+Z).
            Quaternion yaw = Quaternion.identity;
            Transform rear = slideM != null ? slideM.Find("Rear_Sight") : null;
            if (rear != null && mains.ContainsKey(slideM))
            {
                // Slide's long mesh axis, signed to point away from the rear sight.
                Transform slideMain = mains[slideM];
                Vector3 size = slideMain.GetComponent<MeshFilter>().sharedMesh.bounds.size;
                Vector3 axis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
                Vector3 muzzle = slideMain.TransformDirection(axis);
                if (Vector3.Dot(muzzle, slideMain.GetComponent<Renderer>().bounds.center - rear.GetComponent<Renderer>().bounds.center) < 0f) muzzle = -muzzle;
                // Pure Y rotation taking the horizontal muzzle direction onto +Z.
                if (muzzle.x * muzzle.x + muzzle.z * muzzle.z > 1e-8f) yaw = Quaternion.Euler(0f, Mathf.Atan2(-muzzle.x, muzzle.z) * Mathf.Rad2Deg, 0f);
            }
            foreach (Transform m in modules) m.rotation = yaw;

            // Ghost path directions. Muzzle is +Z after the yaw; gun "up" = frame -> slide in the exploded model (slide sits on top).
            if (mains.ContainsKey(frameM) && mains.ContainsKey(slideM))
            {
                Transform fm = mains[frameM], sm = mains[slideM];
                Mesh fMesh = fm.GetComponent<MeshFilter>().sharedMesh, sMesh = sm.GetComponent<MeshFilter>().sharedMesh;
                Vector3 upLocal = factory[fMesh].inverse.MultiplyPoint3x4(factory[sMesh].MultiplyPoint3x4(sMesh.bounds.center)) - fMesh.bounds.center;
                Vector3 up = Vector3.ProjectOnPlane(fm.TransformDirection(upLocal), Vector3.forward).normalized;
                foreach (var (socketT, spec) in socketTs)
                {
                    Vector3 from = spec.from == InsertFrom.Front ? Vector3.forward : -up;
                    socketT.GetComponent<SnapSocket>().insertionDirection = socketT.InverseTransformDirection(from);
                    EditorUtility.SetDirty(socketT.GetComponent<SnapSocket>());
                }
            }

            // 4. Lay out left->right in assembly order with equal gaps, centred on the mat, flush on its surface.
            float totalWidth = 0f;
            var widths = new List<float>();
            foreach (Transform m in modules)
            {
                float w = WorldBounds(m).size.x;
                widths.Add(w);
                totalWidth += w;
            }
            float usable = matBounds.size.x - 2f * MatMargin;
            float gap = modules.Count > 1 ? Mathf.Max(0.03f, (usable - totalWidth) / (modules.Count - 1)) : 0f;
            float cursor = matBounds.center.x - (totalWidth + gap * (modules.Count - 1)) * 0.5f;

            for (int i = 0; i < modules.Count; i++)
            {
                Transform m = modules[i];
                Bounds b = WorldBounds(m);
                Vector3 offset = new Vector3(cursor + widths[i] * 0.5f - b.center.x, matBounds.max.y + 0.001f - b.min.y, matBounds.center.z - b.center.z);
                m.position += offset;
                cursor += widths[i] + gap;

                FitCollider(m);
                Rigidbody rb = m.GetComponent<Rigidbody>();
                if (rb == null) rb = m.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(station.scene);
            Debug.Log($"[VR Armory] Re-staged {modules.Count} parts at {PartWorldScale:0.##}x real scale, mat {matBounds.size.x:0.00} x {matBounds.size.z:0.00} m, parts {totalWidth:0.00} m wide, gap {gap * 100f:0.0} cm, yaw {yaw.eulerAngles.y:0}.");
            DiagnosePicking(modules);
        }

        private const string FbxPath = "Assets/Models/beretta92.fbx";

        // Largest mesh child = the module's main body (Frame_Body, Slide_Body, ...).
        private static Transform MainPiece(Transform m)
        {
            Transform main = null;
            float best = -1f;
            foreach (Transform c in m)
            {
                MeshFilter mf = c.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Vector3 s = Vector3.Scale(mf.sharedMesh.bounds.size, c.localScale);
                float v = s.x * s.y * s.z;
                if (v > best) { best = v; main = c; }
            }
            return main;
        }

        // Pieces that sit inside their body when assembled (spring around the guide rod); all others mount on its surface.
        private static readonly string[] InsetPieces = { "Recoil_Spring" };

        // Module-space pose of a piece after undoing the explosion. Works in the main mesh's local space (all FBX nodes share
        // rotation/scale): keeps the piece's orientation and on-axis offsets, and pulls it along every axis it was pushed out on
        // until it touches the main body's box (inset = fully inside it). Main body sits at module origin with scale mainScale.
        private static Matrix4x4 CollapsedPose(Vector3 mainScale, Matrix4x4 mainFactory, Mesh mainMesh, Matrix4x4 pieceFactory, Mesh pieceMesh, bool inset)
        {
            Matrix4x4 rel = mainFactory.inverse * pieceFactory;
            Vector3 c = rel.MultiplyPoint3x4(pieceMesh.bounds.center);
            Vector3 pe = pieceMesh.bounds.extents, e = Vector3.zero;
            for (int i = 0; i < 3; i++)
                e[i] = Mathf.Abs(rel[i, 0]) * pe.x + Mathf.Abs(rel[i, 1]) * pe.y + Mathf.Abs(rel[i, 2]) * pe.z;

            Bounds mb = mainMesh.bounds;
            Vector3 target = c;
            for (int i = 0; i < 3; i++)
            {
                float lo = inset ? mb.min[i] + e[i] : mb.min[i] - e[i];
                float hi = inset ? mb.max[i] - e[i] : mb.max[i] + e[i];
                target[i] = lo > hi ? mb.center[i] : Mathf.Clamp(c[i], lo, hi);
            }
            return Matrix4x4.TRS(Vector3.zero, Quaternion.identity, mainScale) * Matrix4x4.Translate(target - c) * rel;
        }

        // The exploded model puts the magazine in front of the gun, so the generic collapse lands it at the muzzle end.
        // The grip panels were only exploded sideways and still mark the grip: centre the magazine on them along the
        // gun's long and width axes, with its floorplate just below the grip bottom. Works in frame-module space.
        private static void AlignMagazineToGrip(Transform frameM, Transform magM, Dictionary<Transform, Transform> mains, Dictionary<Mesh, Matrix4x4> factory)
        {
            Transform socketT = frameM != null ? frameM.Find("Socket_Magazine") : null;
            Transform gripL = frameM != null ? frameM.Find("Grip_Panel_Left") : null, gripR = frameM != null ? frameM.Find("Grip_Panel_Right") : null;
            if (socketT == null || magM == null || gripL == null || gripR == null || !mains.ContainsKey(frameM) || !mains.ContainsKey(SlideOf(frameM))) return;

            // Gun axes in frame-module space: long = frame's longest mesh axis, up = frame -> slide in the exploded model.
            Transform fm = mains[frameM], sm = mains[SlideOf(frameM)];
            Mesh fMesh = fm.GetComponent<MeshFilter>().sharedMesh, sMesh = sm.GetComponent<MeshFilter>().sharedMesh;
            Vector3 fs = fMesh.bounds.size;
            Vector3 longAxis = fs.x >= fs.y && fs.x >= fs.z ? Vector3.right : fs.y >= fs.z ? Vector3.up : Vector3.forward;
            Vector3 up = factory[fMesh].inverse.MultiplyPoint3x4(factory[sMesh].MultiplyPoint3x4(sMesh.bounds.center)) - fMesh.bounds.center;
            up = Vector3.ProjectOnPlane(up, longAxis).normalized;
            Vector3 width = Vector3.Cross(longAxis, up).normalized;
            Vector3[] axes = { longAxis, width, up };

            // Grip extents from both panels; magazine extents (tube + floorplate) as seated by the socket.
            var grip = new List<Vector3>();
            foreach (Transform g in new[] { gripL, gripR }) grip.AddRange(MeshCorners(g, frameM));
            var mag = new List<Vector3>();
            Matrix4x4 seat = Matrix4x4.TRS(socketT.localPosition, socketT.localRotation, Vector3.one);
            foreach (MeshFilter mf in magM.GetComponentsInChildren<MeshFilter>())
            {
                foreach (Vector3 p in MeshCorners(mf.transform, magM)) mag.Add(seat.MultiplyPoint3x4(p));
            }

            Vector3 delta = Vector3.zero;
            for (int a = 0; a < 3; a++)
            {
                Range(grip, axes[a], out float gMin, out float gMax);
                Range(mag, axes[a], out float mMin, out float mMax);
                float shift = a < 2 ? (gMin + gMax - mMin - mMax) * 0.5f      // centre on the grip
                                    : (gMin - 0.004f / frameM.lossyScale.x) - mMin; // floorplate just below grip bottom
                delta += axes[a] * shift;
            }
            socketT.localPosition += delta;
        }

        private static Transform SlideOf(Transform frameM)
        {
            return frameM.parent.Find("02_Upper_Slide");
        }

        // 8 mesh-bounds corners of t's mesh, in space's local coordinates.
        private static IEnumerable<Vector3> MeshCorners(Transform t, Transform space)
        {
            Bounds b = t.GetComponent<MeshFilter>().sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                yield return space.InverseTransformPoint(t.TransformPoint(c));
            }
        }

        private static void Range(List<Vector3> pts, Vector3 axis, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (Vector3 p in pts)
            {
                float d = Vector3.Dot(p, axis);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
        }

        private static void ApplyLocal(Transform t, Matrix4x4 m)
        {
            t.localPosition = m.GetColumn(3);
            t.localRotation = m.rotation;
            t.localScale = m.lossyScale;
        }

        private static Bounds WorldBounds(Transform root)
        {
            Renderer[] rs = root.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // BoxCollider on the module root from all 8 corners of every child mesh (rotation-safe).
        private static void FitCollider(Transform m)
        {
            bool first = true;
            Bounds local = new Bounds();
            foreach (MeshFilter mf in m.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<Renderer>() == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (first) { local = new Bounds(p, Vector3.zero); first = false; }
                    else local.Encapsulate(p);
                }
            }
            BoxCollider col = GetOrAdd<BoxCollider>(m.gameObject);
            col.enabled = true;
            col.isTrigger = false;
            col.center = local.center;
            col.size = local.size;
        }

        // Edit-mode check that the player camera's ray actually reaches each part's collider.
        private static void DiagnosePicking(List<Transform> modules)
        {
            Camera cam = GameObject.Find("Armorer_Player")?.GetComponentInChildren<Camera>();
            if (cam == null) return;
            Physics.SyncTransforms();
            foreach (Transform m in modules)
            {
                Vector3 target = m.GetComponent<BoxCollider>().bounds.center;
                Vector3 dir = target - cam.transform.position;
                string hitName = DesktopAssemblyInteractor.AimRaycast(new Ray(cam.transform.position, dir), 5f, cam.transform.root, out RaycastHit hit) ? hit.collider.name : "NOTHING";
                Debug.Log($"[VR Armory] Pick ray -> {m.name}: hits '{hitName}' at {dir.magnitude:0.00} m (layer {LayerMask.LayerToName(m.gameObject.layer)})");
            }
        }

        private static Material GetOrCreateGhostMaterial()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(GhostMatPath);
            if (m != null) return m;
            m = new Material(Shader.Find("VRArmory/Hologram"));
            AssetDatabase.CreateAsset(m, GhostMatPath);
            return m;
        }

        // Explicit null check: GetComponent returns a fake-null object in the editor, which defeats '??'.
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
#endif
