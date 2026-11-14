#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static VRArmory.Editor.VRSceneSetup;

namespace VRArmory.Editor
{
    // Fire range setup for the training scene (SampleScene), additive like the VR setup: run it again any time.
    //   1. Setup Live Firing: the assembled pistol fires once its function check has passed (PistolFiring + ShotEffects).
    // Plus a Play-mode shortcut for testing: Debug > Skip Assembly seats every part and passes the function check.
    public static class RangeSetup
    {
        public const string WeaponAudioFolder = "Assets/Audio/Weapons";
        const string RootName = "Range_Live_Fire";
        // Near / Mid / Far dummy distances from the firing line, metres (ArmorySetupHelper builds them at the same).
        public static readonly float[] TargetDistances = { 6f, 10f, 15f };

        [MenuItem("Tools/VR Armory/Range/1. Setup Live Firing", priority = 20)]
        public static void SetupLiveFiring()
        {
            List<WeaponKit> kits = new List<WeaponKit>();
            foreach (WeaponKit k in Object.FindObjectsOfType<WeaponKit>(true))
                if (k.functionCheck != null) kits.Add(k);
                else Debug.LogWarning($"[VR Armory] Weapon kit '{k.id}' has no function check: no live fire for it.");
            if (kits.Count == 0)
            {
                EditorUtility.DisplayDialog("VR Armory", "No weapon kit with a FunctionCheck in the open scene. Open SampleScene (with the assembly set up) and try again.", "OK");
                return;
            }
            kits.Sort((a, b) => (a.id == BerettaId ? 0 : 1).CompareTo(b.id == BerettaId ? 0 : 1)); // Beretta first

            GameObject root = GameObject.Find(RootName);
            if (root == null) root = new GameObject(RootName);

            ShotEffects effects = GetOrAdd<ShotEffects>(root);
            effects.baseMaterial = VRSceneSetup.GetOrCreateGuideMaterial(); // Sprites/Default, saved so it is in the build
            effects.holeSize = 0.022f;

            // The Beretta's live fire sits on the root (as before), every other weapon's on a child of its own.
            var firings = new List<PistolFiring>();
            foreach (WeaponKit kit in kits)
            {
                GameObject host = root;
                if (kit.id != BerettaId)
                {
                    string name = kit.id + "_Live_Fire";
                    Transform child = root.transform.Find(name);
                    host = child != null ? child.gameObject : new GameObject(name);
                    host.transform.SetParent(root.transform, false);
                }
                firings.Add(SetupFiring(host, kit, effects));
            }

            RangeScoring scoring = GetOrAdd<RangeScoring>(root);
            scoring.firings = firings.ToArray();
            EditorUtility.SetDirty(scoring);

            PlaceTargets();

            EditorUtility.SetDirty(effects);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            Debug.Log($"[VR Armory] Live firing set up for {firings.Count} weapon(s): each fires once its function check has passed (Play mode: Tools > VR Armory > Debug > Skip Assembly). Re-run Range > 2. Setup Range Sergeant.");
        }

        const string BerettaId = "beretta92";

        // One weapon's live fire: firing + magazine on 'host', tuned like the real gun.
        static PistolFiring SetupFiring(GameObject host, WeaponKit kit, ShotEffects effects)
        {
            FunctionCheck functionCheck = kit.functionCheck;
            VRAssemblyGrab frameGrab = kit.frame != null ? kit.frame.GetComponent<VRAssemblyGrab>() : null;
            if (frameGrab == null) Debug.LogWarning($"[VR Armory] No VR grab on the {kit.displayName} frame (run Setup VR Scene first): live fire will work without recoil kick / vibration.");

            PistolFiring firing = GetOrAdd<PistolFiring>(host);
            firing.functionCheck = functionCheck;
            firing.frameGrab = frameGrab;
            firing.effects = effects;
            PistolMagazine ammo = SetupMagazine(host, kit, functionCheck, frameGrab);
            firing.ammo = ammo;

            if (kit.id == VRTrainingText.Tec9Id)
            {
                // TEC-9: straight blowback with a heavy bolt and a long, heavy trigger, so the practical rate of fire is
                // lower (about 5 aimed shots a second at most) and it kicks harder; 32-round magazine; the bolt does not
                // lock open on the last round, so every reload ends with pulling the charging knob.
                ammo.capacity = 32;
                ammo.boltHoldOpen = false;
                ammo.rackWord = "BOLT";
                firing.kickAngle = 5f;
                firing.kickBack = 0.024f;
                firing.kickRecoverTime = 0.1f;
                firing.shotHapticTime = 0.12f;
                firing.cycleDuration = 0.12f;
                firing.minShotInterval = 0.2f;
                firing.roundsPerPull = 2; // trainer setting: two rounds per pull (the real gun is semi-automatic)
            }
            else
            {
                // Beretta 92 FS: 15 rounds, slide locks open on empty.
                ammo.capacity = 15;
                ammo.boltHoldOpen = true;
                ammo.rackWord = "SLIDE";
                firing.kickAngle = 3f;    // hand + pistol turn together about the wrist
                firing.kickBack = 0.015f;
                firing.kickRecoverTime = 0.07f;
                firing.shotHapticTime = 0.1f;
                firing.cycleDuration = 0f; // the function check's own
                firing.minShotInterval = 0f;
                firing.roundsPerPull = 1;
            }

            firing.gunshot = LoadWeaponClip("Gunshot_" + kit.id);
            if (firing.gunshot == null) firing.gunshot = LoadWeaponClip("Gunshot");
            if (firing.gunshot == null) Debug.LogWarning($"[VR Armory] No Gunshot audio file in {WeaponAudioFolder}: shots will be silent.");
            EditorUtility.SetDirty(ammo);
            EditorUtility.SetDirty(firing);
            return firing;
        }

        // Chamber + magazine: the weapon's magazine part and the socket that takes it.
        static PistolMagazine SetupMagazine(GameObject host, WeaponKit kit, FunctionCheck functionCheck, VRAssemblyGrab frameGrab)
        {
            PistolMagazine ammo = GetOrAdd<PistolMagazine>(host);
            ammo.functionCheck = functionCheck;
            ammo.frameGrab = frameGrab;
            ammo.magazine = null;
            ammo.magazineWell = null;
            foreach (AssemblyPart part in kit.GetComponentsInChildren<AssemblyPart>(true))
            {
                if (part.partType == GunPart.Magazine) ammo.magazine = part;
            }
            foreach (SnapSocket socket in Object.FindObjectsOfType<SnapSocket>(true))
            {
                if (ammo.magazine != null && socket.acceptedPart == ammo.magazine) ammo.magazineWell = socket;
            }
            if (ammo.magazine == null || ammo.magazineWell == null) Debug.LogWarning($"[VR Armory] {kit.displayName}: magazine part or its socket not found: the magazine cannot be released / reloaded.");
            return ammo;
        }

        // Moves the dummies (nearest first) to TargetDistances and re-aims each one's ceiling spot light at it.
        static void PlaceTargets()
        {
            RangeTarget[] targets = Object.FindObjectsOfType<RangeTarget>(true);
            System.Array.Sort(targets, (a, b) => a.distanceMeters.CompareTo(b.distanceMeters));
            for (int i = 0; i < targets.Length && i < TargetDistances.Length; i++)
            {
                RangeTarget t = targets[i];
                Undo.RecordObject(t, "Place range targets");
                Undo.RecordObject(t.transform, "Place range targets");
                t.SetDistance(TargetDistances[i]);
                EditorUtility.SetDirty(t);

                // "Target_02_Mid" -> spot light "Target_Spot_02"
                string[] parts = t.targetId != null ? t.targetId.Split('_') : new string[0];
                GameObject spot = parts.Length >= 2 ? GameObject.Find("Target_Spot_" + parts[1]) : null;
                if (spot == null) continue;
                Undo.RecordObject(spot.transform, "Place range targets");
                Vector3 p = t.transform.position;
                spot.transform.position = new Vector3(spot.transform.position.x, spot.transform.position.y, p.z - 3f);
                spot.transform.LookAt(new Vector3(p.x, 1.4f * t.transform.lossyScale.y, p.z));
            }
        }

        // A clip in Assets/Audio/Weapons whose file name is 'name' (any audio format), imported for instant playback.
        internal static AudioClip LoadWeaponClip(string name)
        {
            if (!AssetDatabase.IsValidFolder(WeaponAudioFolder)) return null;
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:AudioClip", new[] { WeaponAudioFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), name, System.StringComparison.OrdinalIgnoreCase)) continue;
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer != null)
                {
                    AudioImporterSampleSettings s = importer.defaultSampleSettings;
                    bool changed = s.loadType != AudioClipLoadType.DecompressOnLoad || !s.preloadAudioData;
                    if (changed)
                    {
                        s.loadType = AudioClipLoadType.DecompressOnLoad; // short and played often: no decode delay
                        s.preloadAudioData = true;
                        importer.defaultSampleSettings = s;
                        importer.SaveAndReimport();
                    }
                }
                return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            return null;
        }

        // Sgt. Forrest Gump: just outside the left wall of the leftmost booth (its open end, at the archway), facing the
        // armory the trainee walks in from. His offer window floats at his side, toward the middle of the archway.
        // (Stepped forward from the wall end so his salute clears the wall.)
        static readonly Vector3 SergeantPos = new Vector3(-2.05f, 0f, 7.25f);
        static readonly Vector3 SergeantLooksAt = new Vector3(-0.4f, 0f, 5.1f);
        static readonly Vector3 PromptPos = new Vector3(-1.4f, 1.5f, 6.85f);

        // Fire training booth: the middle one (all three dummies in view). Trainee stands at BoothStand facing +Z; the
        // pistol and magazine lie on the counter (top 0.85 m, front edge on the firing line Z 9.5); the panel floats at
        // the trainee's left, by the booth wall.
        static readonly Vector3 BoothStand = new Vector3(0f, 0f, 8.55f);
        static readonly Vector3 BoothEye = new Vector3(0f, 1.55f, 8.55f);
        static readonly Vector3 PistolSpot = new Vector3(-0.12f, 0.85f, 9.15f);
        static readonly Vector3 MagazineSpot = new Vector3(0.18f, 0.85f, 9.15f);
        // High over the lane, out past the counter toward the dummies (about 1.9 m from the eye, 30 degrees up), only
        // slightly tilted toward the trainee; above the line of sight downrange.
        static readonly Vector3 BoothPanelPos = new Vector3(0f, 2.45f, 10.2f);
        const float BoothPanelTilt = 15f;    // degrees its face leans down toward the trainee
        const float BoothPanelScale = 1.6f;  // whole panel bigger (layout and text unchanged)
        static readonly Vector3 ResultsPos = new Vector3(0f, 1.6f, 9.95f);       // over the lane, straight ahead
        const float BoothW = 600f, BoothH = 440f;
        const float PromptW = 760f, PromptH = 300f; // canvas units = mm
        const string SergeantRootName = "Range_Sergeant";

        [MenuItem("Tools/VR Armory/Range/2. Setup Range Sergeant", priority = 21)]
        public static void SetupSergeant()
        {
            if (Object.FindObjectOfType<RangeTarget>(true) == null)
            {
                EditorUtility.DisplayDialog("VR Armory", "No range in the open scene. Open SampleScene and try again.", "OK");
                return;
            }
            VRVoiceGuide trainerVoice = null;
            foreach (VRVoiceGuide v in Object.FindObjectsOfType<VRVoiceGuide>(true))
                if (v.GetComponentInParent<RangeSergeant>() == null) trainerVoice = v;
            GameObject old = GameObject.Find(SergeantRootName);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(SergeantRootName);

            // Stand him on whatever floor is there (armory / range floors).
            Physics.SyncTransforms();
            Vector3 feet = SergeantPos;
            if (Physics.Raycast(SergeantPos + Vector3.up * 2f, Vector3.down, out RaycastHit floor, 4f, ~0, QueryTriggerInteraction.Ignore)) feet.y = floor.point.y;
            Animator soldier = IntroSceneSetup.BuildSoldier(root.transform, feet, SergeantLooksAt, IntroSceneSetup.SoldierController(), 0f);
            if (soldier != null) soldier.name = "Sgt_Forrest_Gump";

            GameObject canvas = IntroSceneSetup.WorldCanvas(root.transform, "Range_Offer_Window", new Vector2(PromptW, PromptH), true);
            Vector3 face = PromptPos - new Vector3(SergeantLooksAt.x, PromptPos.y, SergeantLooksAt.z);
            canvas.transform.SetPositionAndRotation(PromptPos, Quaternion.LookRotation(face));
            Transform p = canvas.transform;
            float cw = PromptW - 80f, right = PromptW * 0.5f - 40f;
            Label(p, "Speaker", VRTrainingText.SergeantName, 22, Accent, new Vector2(0f, 112f), new Vector2(cw, 34f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(p, "Section", VRTrainingText.RangeSection, 18, TextSecondary, new Vector2(0f, 112f), new Vector2(cw, 34f), TextAnchor.MiddleRight, FontStyle.Bold);
            Text question = Label(p, "Question", VRTrainingText.RangeOffer, 26, TextPrimary, new Vector2(0f, 18f), new Vector2(cw, 130f), TextAnchor.UpperLeft);
            Button yes = MakeButton(p, "Yes_Button", VRTrainingText.RangeYes, new Vector2(right - 120f, -100f), new Vector2(240f, 58f), true);
            Button no = MakeButton(p, "No_Button", VRTrainingText.RangeNo, new Vector2(right - 240f - 16f - 90f, -100f), new Vector2(180f, 58f), false);
            Text hint = Label(p, "Desktop_Hint", VRTrainingText.RangeDesktopHint, 15, TextSecondary, new Vector2(-PromptW * 0.5f + 40f + 130f, -100f), new Vector2(260f, 40f), TextAnchor.MiddleLeft);

            RangeSergeant sergeant = root.AddComponent<RangeSergeant>();
            sergeant.soldier = soldier;
            sergeant.questionText = question;
            sergeant.trainerVoice = trainerVoice;
            sergeant.floorY = feet.y;
            sergeant.voice = BuildVoice(root.transform);
            sergeant.prompt = canvas.AddComponent<CanvasGroup>();
            sergeant.yesButton = yes;
            sergeant.noButton = no;
            sergeant.desktopHint = hint;

            BuildTraining(root.transform, sergeant);

            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            Debug.Log("[VR Armory] Range sergeant and fire training set up" + (soldier == null ? " WITHOUT the soldier model (see .gitignore)." : "."));
        }

        static VRVoiceGuide BuildVoice(Transform parent)
        {
            var go = new GameObject("Sergeant_Voice");
            go.transform.SetParent(parent, false);
            go.AddComponent<AudioSource>().playOnAwake = false;
            VRVoiceGuide voice = go.AddComponent<VRVoiceGuide>();
            voice.clips = VRVoiceSetup.LoadClips(out List<VoiceLine> missing);
            foreach (VoiceLine l in missing)
                if (l.ToString().StartsWith("Rng_")) Debug.LogWarning($"[VR Armory] Missing voice clip VO_{l}.wav (Tools/VoiceGeneration).");
            return voice;
        }

        // The training round: booth points, the panel at the trainee's left, and FireTraining wired to everything.
        static void BuildTraining(Transform root, RangeSergeant sergeant)
        {
            GameObject liveFire = GameObject.Find(RootName);
            RangeScoring scoring = liveFire != null ? liveFire.GetComponent<RangeScoring>() : null;
            PistolFiring firing = scoring != null && scoring.firings.Length > 0 ? scoring.firings[0] : null; // default: Beretta
            if (firing == null)
            {
                Debug.LogWarning("[VR Armory] No live firing in the scene: run Range > 1. Setup Live Firing, then this again.");
                return;
            }
            Transform booth = new GameObject("Training_Booth").transform;
            booth.SetParent(root, false);
            Transform stand = Point(booth, "Stand_Point", BoothStand, Quaternion.identity);
            Transform pistolSpot = Point(booth, "Pistol_Spot", PistolSpot, Quaternion.identity);
            Transform magSpot = Point(booth, "Magazine_Spot", MagazineSpot, Quaternion.identity);

            GameObject canvas = IntroSceneSetup.WorldCanvas(booth, "Booth_Panel", new Vector2(BoothW, BoothH), true);
            canvas.transform.SetPositionAndRotation(BoothPanelPos, Quaternion.Euler(-BoothPanelTilt, 0f, 0f)); // faces back up the lane
            canvas.transform.localScale *= BoothPanelScale;
            Transform p = canvas.transform;
            float cw = BoothW - 70f, right = BoothW * 0.5f - 35f;

            FireTraining t = root.gameObject.AddComponent<FireTraining>();
            Label(p, "Title", VRTrainingText.RangeSection, 20, Accent, new Vector2(0f, 190f), new Vector2(cw, 30f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(p, "Speaker", VRTrainingText.SergeantName, 16, TextSecondary, new Vector2(0f, 190f), new Vector2(cw, 30f), TextAnchor.MiddleRight, FontStyle.Bold);
            t.stepText = Label(p, "Step", "", 23, TextPrimary, new Vector2(0f, 128f), new Vector2(cw, 80f), TextAnchor.UpperLeft);
            float statW = (cw - 30f) / 3f;
            t.timeText = Stat(p, "Time", new Vector2(-statW - 15f, 30f), statW);
            t.roundsText = Stat(p, "Rounds", new Vector2(0f, 30f), statW);
            t.scoreText = Stat(p, "Score", new Vector2(statW + 15f, 30f), statW);
            t.resultText = Label(p, "Result", "", 20, TextPrimary, new Vector2(0f, -78f), new Vector2(cw, 110f), TextAnchor.MiddleCenter);
            t.endButton = MakeButton(p, "End_Button", VRTrainingText.RangeEnd, new Vector2(right - 110f, -180f), new Vector2(220f, 54f), false);
            t.retryButton = MakeButton(p, "Retry_Button", VRTrainingText.RangeRetry, new Vector2(right - 200f - 16f - 100f, -180f), new Vector2(200f, 54f), true);
            t.finishButton = MakeButton(p, "Finish_Button", VRTrainingText.RangeFinish, new Vector2(right - 100f, -180f), new Vector2(200f, 54f), false);
            t.desktopHint = Label(p, "Desktop_Hint", VRTrainingText.RangeBoothDesktopHint, 13, TextSecondary, new Vector2(-BoothW * 0.5f + 35f + 90f, -180f), new Vector2(180f, 54f), TextAnchor.MiddleLeft);
            t.panel = canvas.AddComponent<CanvasGroup>();

            t.sergeant = sergeant;
            t.voice = sergeant.voice;
            t.firing = firing;
            t.functionCheck = firing.functionCheck;
            t.frameGrab = firing.frameGrab;
            t.frame = firing.functionCheck != null ? firing.functionCheck.frame : null;
            t.ammo = firing.ammo;
            t.scoring = scoring;
            t.standPoint = stand;
            t.pistolSpot = pistolSpot;
            t.magazineSpot = magSpot;
            t.resultsSpot = Point(booth, "Results_Spot", ResultsPos, Quaternion.LookRotation(ResultsPos - BoothEye));
            t.menu = BuildMenu(booth, t);
        }

        // Weapon / mode / targets screen, straight ahead over the counter (where the results show later), card pages in
        // the style of the training menu. The weapons' models lie on the counter below their cards.
        static readonly Vector3 MenuPos = new Vector3(0f, 1.5f, 9.4f);
        const float MenuW = 800f, MenuH = 460f, MenuContentW = 704f;

        static RangeModeMenu BuildMenu(Transform booth, FireTraining t)
        {
            GameObject canvas = IntroSceneSetup.WorldCanvas(booth, "Range_Menu", new Vector2(MenuW, MenuH), true);
            Quaternion rot = Quaternion.LookRotation(MenuPos - BoothEye);
            canvas.transform.SetPositionAndRotation(MenuPos, rot);
            Transform p = canvas.transform;
            float left = -MenuContentW * 0.5f;

            RangeModeMenu m = booth.gameObject.AddComponent<RangeModeMenu>();
            m.panel = canvas.AddComponent<CanvasGroup>();

            // One card per weapon with live fire; at run time only the one assembled in the session is shown.
            WeaponKit[] kits = Object.FindObjectsOfType<WeaponKit>(true);
            var weapons = new List<RangeWeapon>();
            foreach (PistolFiring f in t.scoring != null ? t.scoring.firings : new[] { t.firing })
            {
                if (f == null || f.functionCheck == null) continue;
                WeaponKit kit = System.Array.Find(kits, k => k.functionCheck == f.functionCheck);
                PistolMagazine ammo = f.ammo;
                weapons.Add(new RangeWeapon
                {
                    displayName = kit != null ? kit.displayName : "Beretta 92 FS",
                    id = kit != null ? kit.id : BerettaId,
                    firing = f,
                    magazineSize = ammo != null ? ammo.capacity : 15,
                    model = f.functionCheck.frame != null ? f.functionCheck.frame.transform : null,
                    magazineModel = ammo != null && ammo.magazine != null ? ammo.magazine.transform : null,
                    magazineSeat = ammo != null && ammo.magazineWell != null ? ammo.magazineWell.transform : null,
                    functionCheck = f.functionCheck,
                    // Frame-space axes for the card picture when the function check has not run this session (a weapon
                    // unlocked earlier): without them the default axes turned the gun end-on and blew it up.
                    muzzleLocal = kit != null ? kit.muzzleLocal : Vector3.left,
                    upLocal = kit != null ? kit.upLocal : Vector3.forward,
                });
            }
            m.weapons = weapons.ToArray();

            // 1. Weapon: one card per weapon, side by side: name, the gun's model under it, magazine size.
            Transform page = MenuPage(p, "Weapon_Page", VRTrainingText.RangeMenuWeaponTitle);
            int n = m.weapons.Length;
            m.weaponCards = new Button[n];
            m.modelSpots = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                RangeWeapon w = m.weapons[i];
                float x = (i - (n - 1) * 0.5f) * 356f;
                m.weaponCards[i] = WeaponCard(page, $"Weapon_{i + 1:00}_Card", new Vector2(x, -25f), $"{i + 1:00}", w.displayName,
                    VRTrainingText.RangeWeaponMagazinePill(w.magazineSize), out m.modelSpots[i]);
            }
            m.modelBox = new Vector2(0.27f, 0.105f);
            m.weaponPage = page.gameObject;

            // 2. Mode.
            page = MenuPage(p, "Mode_Page", VRTrainingText.RangeMenuModeTitle);
            m.unlimitedCard = Card(page, "Unlimited_Card", new Vector2(-178f, -22f), true, "01", VRTrainingText.RangeModeUnlimited,
                VRTrainingText.RangeModeUnlimitedBody, VRTrainingText.RangeModeUnlimitedPill, out _);
            m.timedCard = Card(page, "Timed_Card", new Vector2(178f, -22f), true, "02", VRTrainingText.RangeModeTimed,
                VRTrainingText.RangeTimedCardBody(m.weapons[0].magazineSize), VRTrainingText.RangeModeTimedPill, out _);
            Transform body = m.timedCard.transform.Find("Body");
            m.timedBodyText = body != null ? body.GetComponent<Text>() : null;
            m.modePage = page.gameObject;

            // 3. Targets (timed).
            page = MenuPage(p, "Targets_Page", VRTrainingText.RangeMenuTargetsTitle);
            m.stationaryCard = Card(page, "Stationary_Card", new Vector2(-178f, -22f), true, "01", VRTrainingText.RangeTargetsStationary,
                VRTrainingText.RangeTargetsStationaryBody, VRTrainingText.RangeTargetsPill, out _);
            m.movingCard = Card(page, "Moving_Card", new Vector2(178f, -22f), true, "02", VRTrainingText.RangeTargetsMoving,
                VRTrainingText.RangeTargetsMovingBody, VRTrainingText.RangeTargetsPill, out _);
            m.targetsPage = page.gameObject;

            m.cancelButton = MakeButton(p, "End_Button", VRTrainingText.RangeEnd, new Vector2(left + 110f, -190f), new Vector2(220f, 52f), false);
            m.backButton = MakeButton(p, "Back_Button", VRTrainingText.RangeMenuBack, new Vector2(left + 220f + 12f + 80f, -190f), new Vector2(160f, 52f), false);
            m.desktopHint = Label(p, "Desktop_Hint", VRTrainingText.RangeMenuDesktopHint, 14, TextSecondary, new Vector2(-left - 150f, -190f), new Vector2(300f, 40f), TextAnchor.MiddleRight);
            return m;
        }

        // Weapon card (the whole card is the button, coloured like the training menu's module cards): name at the top,
        // the picture area for the gun's model in the middle ('modelSpot'), the magazine size at the bottom.
        internal static Button WeaponCard(Transform parent, string name, Vector2 pos, string number, string title, string pill, out Transform modelSpot)
        {
            Vector2 size = new Vector2(340f, 250f);
            Image img = Rounded(parent, name, pos, size, Color.white, 0.4f);
            img.raycastTarget = true;
            Button btn = img.gameObject.AddComponent<Button>();
            ColorBlock c = btn.colors;
            c.normalColor = CardColor;
            c.highlightedColor = new Color(0.16f, 0.21f, 0.25f);
            c.selectedColor = c.normalColor;
            c.pressedColor = new Color(0.09f, 0.12f, 0.15f);
            c.fadeDuration = 0.08f;
            btn.colors = c;
            btn.targetGraphic = img;

            Rounded(img.transform, "Accent", new Vector2(0f, size.y * 0.5f - 2f), new Vector2(size.x - 40f, 3f), Accent, 2f);
            Label(img.transform, "Title", title, 28, TextPrimary, new Vector2(0f, 90f), new Vector2(292f, 40f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(img.transform, "Number", number, 22, Accent, new Vector2(0f, 90f), new Vector2(292f, 40f), TextAnchor.MiddleRight, FontStyle.Bold);
            modelSpot = new GameObject("Model_Spot", typeof(RectTransform)).transform;
            modelSpot.SetParent(img.transform, false);
            ((RectTransform)modelSpot).anchoredPosition = new Vector2(0f, 8f);
            ((RectTransform)modelSpot).sizeDelta = new Vector2(292f, 120f);
            Image pillBg = Rounded(img.transform, "Status", new Vector2(0f, -96f), new Vector2(292f, 34f), new Color(Accent.r, Accent.g, Accent.b, 0.16f), 0.7f);
            Label(pillBg.transform, "Text", pill, 16, Accent, Vector2.zero, new Vector2(292f, 34f), TextAnchor.MiddleCenter, FontStyle.Bold);
            return btn;
        }

        // A full-size page with the section label and a big left-aligned title, like the training menu's pages.
        static Transform MenuPage(Transform canvas, string name, string title)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = canvas.gameObject.layer;
            go.transform.SetParent(canvas, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(MenuW, MenuH);
            Label(go.transform, "Section", VRTrainingText.RangeSection, 18, Accent, new Vector2(0f, 192f), new Vector2(MenuContentW, 34f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Label(go.transform, "Title", title, 40, TextPrimary, new Vector2(0f, 150f), new Vector2(MenuContentW, 56f), TextAnchor.MiddleLeft, FontStyle.Bold);
            return go.transform;
        }

        static Text Stat(Transform parent, string name, Vector2 pos, float width)
        {
            Rounded(parent, name + "_Card", pos, new Vector2(width, 96f), new Color(0.11f, 0.135f, 0.17f, 1f), 0.3f);
            return Label(parent, name, "", 17, TextSecondary, pos, new Vector2(width, 96f), TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        static Transform Point(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(pos, rot);
            return t;
        }

        [MenuItem("Tools/VR Armory/Debug/Skip Assembly (Play Mode)", priority = 100)]
        static void SkipAssembly()
        {
            if (GunAssemblyManager.Instance != null) GunAssemblyManager.Instance.DebugCompleteAll();
        }

        [MenuItem("Tools/VR Armory/Debug/Skip Assembly (Play Mode)", true)]
        static bool SkipAssemblyValid() => Application.isPlaying && GunAssemblyManager.Instance != null;

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
#endif
