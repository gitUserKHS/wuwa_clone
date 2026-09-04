using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityChan;      // SpringBone lives here
using WuWa;

namespace WuWa.EditorTools
{
    /// Drives the field scene in play mode from the CLI and writes framed captures of each party
    /// member plus a frame-time sample. Runs as a state machine off EditorApplication.update because
    /// entering play mode reloads the domain — the queued work lives in SessionState, not in a field.
    [InitializeOnLoad]
    public static class WuWaPlaytest
    {
        const string KeyDir = "WuWaPlaytest.dir";
        const string KeyStep = "WuWaPlaytest.step";
        const string KeyFrame = "WuWaPlaytest.frame";
        const string KeyActive = "WuWaPlaytest.active";
        const string KeyMode = "WuWaPlaytest.mode";

        static float _fpsAccum;
        static int _fpsFrames;
        static Quaternion _hairSample;
        static bool _hairFound;
        static readonly List<string> Report = new List<string>();

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static WuWaPlaytest()
        {
            EditorApplication.update += Update;
        }

        /// Called from the CLI. Opens the field scene and enters play mode; the rest runs on update.
        public static string Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            SessionState.SetString(KeyDir, outDir);
            SessionState.SetInt(KeyStep, 0);
            SessionState.SetInt(KeyFrame, 0);
            SessionState.SetInt(KeyActive, 0);
            SessionState.SetString(KeyMode, "");
            EditorSceneManager.OpenScene("Assets/WuWa/Scenes/WuWaField.unity");
            EditorApplication.isPlaying = true;
            return "playtest queued -> " + outDir;
        }

        /// Enemy scenario: continue the save, park one enemy in front of the player with its AI
        /// off and step it through every animator state with framed shots and a pose report.
        public static string RunEnemy(string outDir)
        {
            string r = Run(outDir);
            SessionState.SetString(KeyMode, "enemy");
            return r + " (enemy)";
        }

        /// Water scenario: continue the save, drop the party into the middle of the lake and walk
        /// through tread / stroke / dash / dive / underwater dash / resurface / exhaustion with
        /// framed shots, screen captures (post-processing, fog) and a state report.
        public static string RunSwim(string outDir)
        {
            string r = Run(outDir);
            SessionState.SetString(KeyMode, "swim");
            return r + " (swim)";
        }

        static void Update()
        {
            string dir = SessionState.GetString(KeyDir, "");
            if (string.IsNullOrEmpty(dir) || !EditorApplication.isPlaying) return;

            int frame = SessionState.GetInt(KeyFrame, 0) + 1;
            SessionState.SetInt(KeyFrame, frame);
            int step = SessionState.GetInt(KeyStep, 0);
            int active = SessionState.GetInt(KeyActive, 0);

            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;

            // Whatever UI is up at boot (the title screen) can only be captured through
            // ScreenCapture: a Screen Space - Overlay canvas is not visible to any camera, so the
            // RenderTexture path used for the body shots would come back without it.
            if (frame == 90) ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_boot.png"));

            // The party portraits live in the in-session HUD, which the title screen does not show,
            // so the run has to actually enter a session to verify them. Continue an existing save
            // rather than starting fresh, so the player's progress is preserved.
            if (frame == 150 && !SaveSystem.SessionStarted)
            {
                var gd = GameDirector.I;
                if (gd != null)
                {
                    int saveSlot = SaveSystem.LatestSlot(SaveSystem.ReadHeaders());
                    if (saveSlot >= 0) { gd.BeginContinue(saveSlot); Report.Add("session: continued slot " + saveSlot); }
                    else { gd.BeginNewGame(); Report.Add("session: no save, started new"); }
                }
                else Report.Add("session: no GameDirector");
            }
            if (frame == 330) ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_hud.png"));
            if (frame == 360 && SaveSystem.I != null && SaveSystem.SessionStarted)
            {
                // rewrites the slot thumbnail, which still shows the retired characters
                SaveSystem.I.QuickSave();
                Report.Add("session: quicksave issued");
            }
            if (frame == 430) ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_saved.png"));

            // settle: let the scene stream in and the animators reach a steady state
            if (frame < 500) return;
            string mode = SessionState.GetString(KeyMode, "");
            if (mode == "swim") { SwimUpdate(dir, frame - 500); return; }
            if (mode == "enemy") { EnemyUpdate(dir, frame - 500); return; }

            var team = Object.FindFirstObjectByType<TeamManager>();
            if (team == null)
            {
                Finish(dir, "no TeamManager in scene");
                return;
            }

            // 700 frames per slot: TeamManager.swapCooldown is 1.4 s, and at ~140 fps a shorter
            // phase silently leaves the previous member active. The second half of each slot pins
            // the combat stance (PlayerController.CombatPoseOverride) for the guard-pose shots and
            // then plays the first combo swing for an attack-frame grip check.
            const int SlotFrames = 800;
            int phase = (frame - 500) % SlotFrames;
            int slot = (frame - 500) / SlotFrames;
            if (slot > 2)
            {
                Finish(dir, "ok");
                return;
            }

            if (phase == 0)
            {
                // RestoreActive, not TrySwap: TrySwap is the gameplay path (swap cooldown, downed
                // check, attack cancel, intro burst) and silently refuses, which would capture the
                // wrong character. This only needs the member on screen.
                if (team.ActiveIndex != slot) team.RestoreActive(slot);
                PlayerController.CombatPoseOverride = -1;
                PlayerController.SpeedPoseOverride = -1f;
                _fpsAccum = 0f;
                _fpsFrames = 0;
                SessionState.SetInt(KeyActive, slot);
            }
            else if (phase == 300)
            {
                // Spring bones only move in play mode, and a still frame cannot show it. Sample a
                // hair bone now and again at the capture frame: a non-zero angle proves the
                // SpringManager is actually driving the chain.
                var t0 = team.Active != null ? FindDeep(team.Active.transform, "J_L_HairTail_02") : null;
                _hairSample = t0 != null ? t0.localRotation : Quaternion.identity;
                _hairFound = t0 != null;
            }
            else if (phase == 380)
            {
                var m = team.Active;
                if (m == null)
                {
                    Report.Add(slot + ": no active member");
                    return;
                }
                if (team.ActiveIndex != slot)
                {
                    Report.Add(slot + ": swap refused, active is still " + team.ActiveIndex);
                    return;
                }
                var t = m.transform;
                string stem = Path.Combine(dir, "m" + slot);
                Shot(t, stem + "_body.png", 3.0f, 1.05f, 0.95f, 900, 1400);
                Shot(t, stem + "_feet.png", 1.15f, 0.32f, 0.16f, 1100, 800);
                Shot(t, stem + "_quarter.png", 2.6f, 1.10f, 0.95f, 1000, 1300, 40f);
                // HUD portrait: tight on the head over a flat theme-coloured ground, so the
                // world behind does not end up baked into the party list icons
                // Heights are relative to the character ROOT, which stands at y≈0.20 — the eyes
                // measured out at ~1.33 world, i.e. ~1.16 here. height == lookAt keeps the camera
                // level so the frame centres exactly on the focus point (a tilt shifts it).
                // At fov 32 and 0.80 m the frame is ±0.23 m, so the face fills it with hair above.
                Shot(t, stem + "_portrait.png", 0.80f, 1.16f, 1.16f, 512, 512, 12f, m.themeColor);
                float fps = _fpsFrames > 0 ? _fpsFrames / Mathf.Max(0.0001f, _fpsAccum) : 0f;
                var smr = t.GetComponentInChildren<SkinnedMeshRenderer>();
                int verts = smr != null && smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0;
                var hb = FindDeep(t, "J_L_HairTail_02");
                string hair = !_hairFound || hb == null ? "hair=MISSING"
                    : string.Format("hairSwing={0:F2}deg", Quaternion.Angle(_hairSample, hb.localRotation));
                int springs = t.GetComponentsInChildren<SpringBone>(true).Length;
                Report.Add(string.Format("{0}: name={1} fps={2:F1} verts={3} hp={4:F0} springBones={5} {6}",
                    slot, m.charName, fps, verts, m.hp, springs, hair));
                HandShots(m, stem + "_relax");
                Report.Add(slot + ": relax " + GripReport(m));
                // hold the one-handed guard stance for the second set of shots
                PlayerController.CombatPoseOverride = 1;
            }
            else if (phase == 540)
            {
                var m = team.Active;
                if (m == null || team.ActiveIndex != slot) return;
                var t = m.transform;
                string stem = Path.Combine(dir, "m" + slot);
                Shot(t, stem + "_combat_body.png", 3.0f, 1.05f, 0.95f, 900, 1400);
                Shot(t, stem + "_combat_quarter.png", 2.6f, 1.10f, 0.95f, 1000, 1300, 40f);
                HandShots(m, stem + "_combat");
                Report.Add(slot + ": combat " + GripReport(m));
            }
            else if (phase == 545)
            {
                var m = team.Active;
                if (m != null && team.ActiveIndex == slot) WuWaUtil.Fade(m.Anim, "A1", 0.1f);
            }
            else if (phase == 600)
            {
                // ~0.4 s into the 1.2 s swing: around the hit frame
                var m = team.Active;
                if (m == null || team.ActiveIndex != slot) return;
                var t = m.transform;
                string stem = Path.Combine(dir, "m" + slot);
                Shot(t, stem + "_attack_quarter.png", 2.6f, 1.10f, 0.95f, 1000, 1300, 40f);
                HandShots(m, stem + "_attack");
                var info = m.Anim.GetCurrentAnimatorStateInfo(0);
                Report.Add(slot + ": attack " + GripReport(m) + " state=" + (info.IsName("A1") ? "A1" : "other")
                           + " t=" + info.normalizedTime.ToString("F2"));
                PlayerController.CombatPoseOverride = 0;
                // run cycle on the spot: does the blade clear the legs and the ground while the arm swings?
                PlayerController.SpeedPoseOverride = 0.72f;
                WuWaUtil.Fade(m.Anim, "Loco", 0.1f);
            }
            else if (phase == 700 || phase == 760)
            {
                var m = team.Active;
                if (m == null || team.ActiveIndex != slot) return;
                var t = m.transform;
                string stem = Path.Combine(dir, "m" + slot + "_run" + (phase == 700 ? "A" : "B"));
                Shot(t, stem + "_side.png", 2.8f, 0.9f, 0.8f, 1000, 1300, 90f);
                Shot(t, stem + "_quarter.png", 2.6f, 1.10f, 0.95f, 1000, 1300, 40f);
                Report.Add(slot + ": run" + (phase == 700 ? "A" : "B") + " " + GripReport(m) + " tipY=" + BladeTipHeight(m).ToString("F2"));
                if (phase == 760) PlayerController.SpeedPoseOverride = -1f;
            }
        }

        static GameObject _enemy;
        static readonly string[] EnemyStates = { "Idle", "Move", "A1", "A2", "Hit", "Stagger", "Die" };

        static void EnemyUpdate(string dir, int f)
        {
            var pc = PlayerController.Instance;
            if (pc == null) { Finish(dir, "no PlayerController"); return; }
            if (f == 0)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WuWa/Prefabs/EnemyMob.prefab");
                if (prefab == null) { Finish(dir, "no EnemyMob prefab"); return; }
                Vector3 at = pc.transform.position + pc.transform.forward * 4f;
                at.y = WorldRegions.HeightAt(at.x, at.z);
                _enemy = Object.Instantiate(prefab, at, Quaternion.LookRotation(-pc.transform.forward, Vector3.up));
                var ai = _enemy.GetComponent<EnemyAI>();
                if (ai != null) ai.enabled = false;                 // hold the pose; no chasing
                var cc = _enemy.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;                 // and no gravity drift
                // the front shot sits where the enemy is looking, which is where the player stands,
                // so park the player well off to the side first
                var pcc = pc.GetComponent<CharacterController>();
                if (pcc != null) pcc.enabled = false;
                Vector3 away = pc.transform.position - pc.transform.right * 18f;
                away.y = WorldRegions.HeightAt(away.x, away.z) + 0.4f;
                pc.transform.position = away;
                if (pcc != null) pcc.enabled = true;
                Report.Add("enemy: " + prefab.name + " at " + at.ToString("F1") + " ground=" + WorldRegions.HeightAt(at.x, at.z).ToString("F2"));
                return;
            }
            if (_enemy == null) return;
            var anim = _enemy.GetComponentInChildren<Animator>();
            if (anim == null) { Finish(dir, "enemy has no Animator"); return; }

            int step = (f - 60) / 200, phase = (f - 60) % 200;
            if (f < 60 || step >= EnemyStates.Length)
            {
                if (step >= EnemyStates.Length) { Object.Destroy(_enemy); _enemy = null; Finish(dir, "ok"); }
                return;
            }
            string st = EnemyStates[step];
            if (phase == 0) anim.Play(st, 0, 0f);
            else if (phase == 120)
            {
                string stem = Path.Combine(dir, "enemy_" + step + "_" + st);
                Shot(_enemy.transform, stem + "_front.png", 4.2f, 1.6f, 0.9f, 900, 1200);
                Shot(_enemy.transform, stem + "_side.png", 4.2f, 1.1f, 0.9f, 1100, 900, 90f);
                Report.Add(EnemyPose(_enemy, st));
            }
        }

        /// Yaw of the hip line against the enemy's own forward, how far the hips sit from the
        /// capsule centre, and how far the lowest foot floats above the enemy's feet plane.
        static string EnemyPose(GameObject e, string state)
        {
            var anim = e.GetComponentInChildren<Animator>();
            var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            var lu = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var ru = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (lf == null || ru == null || hips == null) return state + ": bones missing";
            Vector3 hipRight = ru.position - lu.position; hipRight.y = 0f;
            float yaw = hipRight.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(e.transform.right, hipRight.normalized, Vector3.up) : 0f;
            Vector3 off = e.transform.InverseTransformPoint(hips.position);
            float foot = Mathf.Min(lf.position.y, rf.position.y) - e.transform.position.y;
            var info = anim.GetCurrentAnimatorStateInfo(0);
            return string.Format("{0}: yaw={1:F0}deg hipsOff=({2:F2},{3:F2}) hipsY={4:F2} footClearance={5:F2} playing={6} t={7:F2}",
                state, yaw, off.x, off.z, off.y, foot, info.IsName(state) ? state : "OTHER", info.normalizedTime);
        }

        static void SwimUpdate(string dir, int f)
        {
            var pc = PlayerController.Instance;
            if (pc == null) { Finish(dir, "no PlayerController"); return; }
            var t = pc.transform;
            if (f == 0)
            {
                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                t.position = new Vector3(390f, WorldRegions.WaterY + 0.3f, -100f);
                t.rotation = Quaternion.identity;
                if (cc != null) cc.enabled = true;
                Report.Add("swim: dropped at the lake centre, bed=" + WorldRegions.HeightAt(390f, -100f).ToString("F2") + " waterY=" + WorldRegions.WaterY);
            }
            else if (f == 120) Snap(dir, pc, "tread");
            else if (f == 125) InputService.DbgMove = new Vector2(0f, 1f);
            else if (f == 260) Snap(dir, pc, "swim");
            else if (f == 265) PlayerController.DebugSwimDash = true;
            else if (f == 380) Snap(dir, pc, "dash");
            else if (f == 385) { PlayerController.DebugSwimDash = false; InputService.DbgMove = Vector2.zero; PlayerController.DebugDive = 1; }
            else if (f == 500) Snap(dir, pc, "dive");
            else if (f == 505) { PlayerController.DebugDive = -1; InputService.DbgMove = new Vector2(0f, 1f); PlayerController.DebugSwimDash = true; }
            else if (f == 620) Snap(dir, pc, "divedash");
            else if (f == 625) { InputService.DbgMove = Vector2.zero; PlayerController.DebugSwimDash = false; PlayerController.DebugDive = 2; }
            else if (f == 1500) { Snap(dir, pc, "surface"); PlayerController.DebugDive = -1; }       // expect dive=False
            else if (f == 1505) ForceStamina(pc, 0f);                                              // no breath on the surface
            else if (f == 1650) Snap(dir, pc, "exhausted");                                        // expect dive=True (sank, not drowned)
            else if (f == 1655)
            {
                // the way out: a shelf 6 m inside the east shore, camera looking at the bank
                ForceStamina(pc, 240f);
                float xs = 390f;
                while (xs < 700f && WorldRegions.HeightAt(xs, -100f) < WorldRegions.WaterY - 1.05f) xs += 2f;
                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                t.position = new Vector3(xs - 6f, WorldRegions.WaterY - 0.9f, -100f);
                if (cc != null) cc.enabled = true;
                var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
                var yaw = cam != null ? typeof(ThirdPersonCamera).GetField("_yaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null;
                if (yaw != null) yaw.SetValue(cam, 90f);
                PlayerController.DebugDive = 2;
                Report.Add("shore: shelf edge x=" + xs + " bed=" + WorldRegions.HeightAt(xs - 6f, -100f).ToString("F2"));
            }
            else if (f == 2000) { PlayerController.DebugDive = -1; InputService.DbgMove = new Vector2(0f, 1f); }
            else if (f == 2100) Snap(dir, pc, "toshore");
            else if (f == 3000) Snap(dir, pc, "ashore");                                           // expect swim=False
            else if (f == 3005) Finish(dir, "ok");
        }

        static void Snap(string dir, PlayerController pc, string tag)
        {
            var t = pc.transform;
            string stem = Path.Combine(dir, "swim_" + tag);
            Shot(t, stem + "_quarter.png", 2.8f, 1.2f, 0.6f, 1000, 1000, 40f);
            Shot(t, stem + "_side.png", 3.0f, 0.7f, 0.5f, 1200, 900, 90f);
            ScreenCapture.CaptureScreenshot(stem + "_screen.png");
            Report.Add(tag + ": " + SwimReport(pc));
        }

        static string SwimReport(PlayerController pc)
        {
            var fx = UnderwaterFX.I;
            var cam = Camera.main;
            var lpf = cam != null ? cam.GetComponent<AudioLowPassFilter>() : null;
            var info = pc.Anim != null ? pc.Anim.GetCurrentAnimatorStateInfo(0) : default(AnimatorStateInfo);
            string state = pc.Anim == null ? "-" : info.IsName("Swim") ? "Swim" : info.IsName("Loco") ? "Loco" : info.IsName("Fall") ? "Fall" : "other";
            return string.Format("swim={0} dive={1} grounded={12} state={13} depth={2:F2} planar={3:F1} vy={4:F1} blend={5:F2} stamina={6:F0} pitch={7:F0} wet={8:F2} fog={9} camY={10:F2} lpf={11} pos=({14:F0},{15:F0})",
                pc.IsSwimming, pc.IsDiving, WorldRegions.WaterY - pc.transform.position.y, pc.PlanarSpeed, pc.VerticalSpeed,
                pc.SwimBlend, pc.Stamina, pc.ModelPitch, fx != null ? fx.Wet : -1f, RenderSettings.fogColor.ToString("F2"),
                cam != null ? cam.transform.position.y : 0f, lpf != null && lpf.enabled ? lpf.cutoffFrequency.ToString("F0") : "off",
                pc.IsGrounded, state, pc.transform.position.x, pc.transform.position.z);
        }

        static void ForceStamina(PlayerController pc, float v)
        {
            var st = typeof(PlayerController).GetProperty("Stamina");
            var ex = typeof(PlayerController).GetProperty("StaminaExhausted");
            st.GetSetMethod(true).Invoke(pc, new object[] { v });
            ex.GetSetMethod(true).Invoke(pc, new object[] { v <= 0.01f });
        }

        /// Height of the blade tip above the character root, metres (negative = below the feet).
        static float BladeTipHeight(MemberConfig m)
        {
            var anim = m.Anim;
            var hand = anim != null ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand == null) return 99f;
            foreach (Transform c in hand)
                if (c.name.StartsWith("weapon_"))
                    return (c.position + c.up * 0.875f).y - m.transform.position.y;
            return 99f;
        }

        /// Close-ups of the sword hand: from outside the arm, wide against the body, and from the
        /// back of the hand (the sword's +Z is the palm normal, which mostly faces the body) to see
        /// the fingers and thumb closing on the grip.
        static void HandShots(MemberConfig m, string stem)
        {
            var anim = m.Anim;
            var hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand == null) return;
            Transform sword = null;
            foreach (Transform c in hand) if (c.name.StartsWith("weapon_")) sword = c;
            var t = m.transform;
            Vector3 h = hand.position;
            ShotAt(h + t.forward * 0.42f + t.right * 0.32f + Vector3.up * 0.22f, h, stem + "_hand_out.png", 900, 900);
            ShotAt(t.position + t.forward * 1.6f + t.right * 0.9f + Vector3.up * 1.25f, h, stem + "_hand_wide.png", 1000, 1000);
            if (sword != null)
            {
                ShotAt(h - sword.forward * 0.36f + Vector3.up * 0.03f, h, stem + "_hand_back.png", 900, 900);
                ShotAt(h - sword.forward * 0.22f + sword.up * 0.16f + sword.right * 0.12f, h, stem + "_hand_oblique.png", 900, 900);
            }
        }

        static string GripReport(MemberConfig m)
        {
            var anim = m.Anim;
            if (anim == null) return "no animator";
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            Transform sword = null;
            if (hand != null) foreach (Transform c in hand) if (c.name.StartsWith("weapon_")) sword = c;
            var sb = new System.Text.StringBuilder();
            sb.Append(sword != null
                ? "sword lp=" + sword.localPosition.ToString("F3") + " le=" + sword.localEulerAngles.ToString("F0")
                : "sword=MISSING");
            sb.Append(" combat=" + anim.GetFloat("Combat").ToString("F2"));
            sb.Append(" layers=" + anim.layerCount);
            for (int i = 1; i < anim.layerCount; i++)
                sb.Append(" " + anim.GetLayerName(i) + "=" + anim.GetLayerWeight(i).ToString("F1"));
            sb.Append(" curlIndex=" + Curl(anim, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal));
            sb.Append(" curlMiddle=" + Curl(anim, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal));
            if (sword != null)
            {
                Vector3 b = m.transform.InverseTransformDirection(sword.up);
                sb.Append(" blade(fwd,up,right)=(" + b.z.ToString("F2") + "," + b.y.ToString("F2") + "," + b.x.ToString("F2") + ")");
            }
            return sb.ToString();
        }

        /// Knuckle and mid-joint bend of one finger, degrees (open hand ~15/0, fist ~90/90).
        static string Curl(Animator anim, HumanBodyBones p, HumanBodyBones i, HumanBodyBones d)
        {
            var th = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var tp = anim.GetBoneTransform(p);
            var ti = anim.GetBoneTransform(i);
            var td = anim.GetBoneTransform(d);
            if (th == null || tp == null || ti == null || td == null) return "n/a";
            Vector3 a = tp.position - th.position, b = ti.position - tp.position, c = td.position - ti.position;
            return Vector3.Angle(a, b).ToString("F0") + "/" + Vector3.Angle(b, c).ToString("F0");
        }

        static void ShotAt(Vector3 camPos, Vector3 focus, string path, int w, int h, float fov = 32f)
        {
            var go = new GameObject("~playtestCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 200f;
            go.transform.position = camPos;
            go.transform.rotation = Quaternion.LookRotation(focus - camPos, Vector3.up);
            Render(cam, path, w, h);
            Object.DestroyImmediate(go);
        }

        static void Render(Camera cam, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        /// Renders one framed shot with a throwaway camera, so nothing depends on the Game view size
        /// or on the end-of-frame timing that ScreenCapture uses.
        static void Shot(Transform target, string path, float dist, float height, float lookAt,
                         int w, int h, float yaw = 0f, Color? background = null)
        {
            var go = new GameObject("~playtestCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            Vector3 fwd = Quaternion.AngleAxis(yaw, Vector3.up) * target.forward;
            Vector3 focus = target.position + Vector3.up * lookAt;
            go.transform.position = target.position + fwd * dist + Vector3.up * height;
            go.transform.rotation = Quaternion.LookRotation(focus - go.transform.position, Vector3.up);

            // A solid clear colour would still leave the world visible, so the flat portrait ground
            // is a quad parked just behind the head and facing the camera.
            GameObject backdrop = null;
            if (background.HasValue)
            {
                backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
                backdrop.name = "~playtestBackdrop";
                Object.DestroyImmediate(backdrop.GetComponent<Collider>());
                backdrop.transform.position = focus - go.transform.forward * -0.9f;
                backdrop.transform.rotation = Quaternion.LookRotation(go.transform.forward, Vector3.up);
                backdrop.transform.localScale = new Vector3(6f, 6f, 1f);
                var b = background.Value;
                var bm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                bm.SetColor("_BaseColor", new Color(b.r * 0.30f, b.g * 0.30f, b.b * 0.30f, 1f));
                backdrop.GetComponent<MeshRenderer>().sharedMaterial = bm;
                backdrop.GetComponent<MeshRenderer>().shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Render(cam, path, w, h);
            Object.DestroyImmediate(go);
            if (backdrop != null) Object.DestroyImmediate(backdrop);
        }

        static void Finish(string dir, string status)
        {
            PlayerController.CombatPoseOverride = -1;
            PlayerController.SpeedPoseOverride = -1f;
            PlayerController.DebugSwimDash = false;
            PlayerController.DebugDive = -1;
            InputService.DbgMove = Vector2.zero;
            if (_enemy != null) { Object.Destroy(_enemy); _enemy = null; }
            SessionState.SetString(KeyMode, "");
            SessionState.SetString(KeyDir, "");
            File.WriteAllText(Path.Combine(dir, "report.txt"),
                "status: " + status + "\n" + string.Join("\n", Report.ToArray()) + "\n");
            Report.Clear();
            EditorApplication.isPlaying = false;
            Debug.Log("PLAYTEST DONE " + status);
        }
    }
}
