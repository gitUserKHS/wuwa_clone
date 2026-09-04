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
            EditorSceneManager.OpenScene("Assets/WuWa/Scenes/WuWaField.unity");
            EditorApplication.isPlaying = true;
            return "playtest queued -> " + outDir;
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
            SessionState.SetString(KeyDir, "");
            File.WriteAllText(Path.Combine(dir, "report.txt"),
                "status: " + status + "\n" + string.Join("\n", Report.ToArray()) + "\n");
            Report.Clear();
            EditorApplication.isPlaying = false;
            Debug.Log("PLAYTEST DONE " + status);
        }
    }
}
