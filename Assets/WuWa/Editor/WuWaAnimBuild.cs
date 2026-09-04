using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WuWa.EditorTools
{
    /// Discovers humanoid clips from the imported animation packs, duplicates the
    /// picks as loop-corrected .anim assets and builds member/enemy AnimatorControllers.
    public static class WuWaAnimBuild
    {
        const string AnimDir = "Assets/WuWa/Anim";
        const string ClipDir = "Assets/WuWa/Anim/Clips";

        class ClipEntry
        {
            public AnimationClip clip;
            public string lname;
            public string path;
        }

        static List<ClipEntry> _library;

        static List<ClipEntry> Library
        {
            get
            {
                if (_library != null) return _library;
                _library = new List<ClipEntry>();
                foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.StartsWith("Assets/WuWa")) continue;
                    foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        var c = obj as AnimationClip;
                        if (c == null || !c.humanMotion) continue;
                        if (c.name.StartsWith("__preview")) continue;
                        _library.Add(new ClipEntry { clip = c, lname = c.name.ToLowerInvariant(), path = path });
                    }
                }
                Debug.Log("[WuWa] clip library: " + _library.Count + " humanoid clips");
                return _library;
            }
        }

        public static void LogLibrary()
        {
            foreach (var e in Library) Debug.Log("[WuWa] clip: " + e.clip.name + "  (" + e.path + ")");
        }

        static AnimationClip Find(string[] keywords, string[] avoid = null, int skip = 0)
        {
            var matches = Library.Where(e =>
                    keywords.Any(k => e.lname.Contains(k)) &&
                    (avoid == null || !avoid.Any(a => e.lname.Contains(a))))
                .OrderBy(e => e.lname.Length)
                .ToList();
            if (matches.Count == 0) return null;
            return matches[Mathf.Min(skip, matches.Count - 1)].clip;
        }

        static List<AnimationClip> FindAll(string[] keywords, string[] avoid = null)
        {
            return Library.Where(e =>
                    keywords.Any(k => e.lname.Contains(k)) &&
                    (avoid == null || !avoid.Any(a => e.lname.Contains(a))))
                .Select(e => e.clip)
                .Distinct()
                .ToList();
        }

        static AnimationClip Dupe(AnimationClip src, string newName, bool loop)
        {
            if (src == null) return null;
            WuWaImportTools.EnsureFolder(ClipDir);
            string path = ClipDir + "/" + newName + ".anim";
            var copy = UnityEngine.Object.Instantiate(src);
            copy.name = newName;

            // GUID-stable: overwrite an existing clip asset in place
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            AnimationClip target;
            if (existing != null)
            {
                EditorUtility.CopySerialized(copy, existing);
                existing.name = newName;
                UnityEngine.Object.DestroyImmediate(copy);
                target = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(copy, path);
                target = copy;
            }

            var settings = AnimationUtility.GetAnimationClipSettings(target);
            settings.loopTime = loop;
            settings.loopBlend = loop;
            AnimationUtility.SetAnimationClipSettings(target, settings);
            AnimationUtility.SetAnimationEvents(target, new AnimationEvent[0]);
            EditorUtility.SetDirty(target);
            return target;
        }

        static readonly string[] AttackAvoid = { "idle", "walk", "run", "jump", "death", "die", "hit", "block", "defend", "roll" };

        public static void BuildAll()
        {
            _library = null;
            WuWaImportTools.EnsureFolder(AnimDir);
            WuWaImportTools.EnsureFolder(ClipDir);

            // ---------------- shared locomotion picks
            var idle = Find(new[] { "idle" }, new[] { "sword", "2h", "bow", "crouch", "sit", "block", "combatidle" })
                       ?? Find(new[] { "idle" });
            var walk = Find(new[] { "walk" }, new[] { "back", "left", "right", "crouch", "strafe", "injured" });
            var run = Find(new[] { "run", "jog" }, new[] { "back", "left", "right", "strafe", "attack" });
            var sprint = Find(new[] { "sprint" }, new[] { "back", "strafe" }) ?? run;
            var jump = Find(new[] { "jump" }, new[] { "attack", "running", "land", "end", "down" })
                       ?? Find(new[] { "jump" });
            var fall = Find(new[] { "fall", "falling", "air" }, new[] { "attack", "get", "down", "back" }) ?? jump;
            var glide = Find(new[] { "glide", "fly", "float", "hover" }) ?? fall;
            var dodge = Find(new[] { "roll", "dodge", "evade", "dash" }, new[] { "attack", "back", "left", "right" })
                        ?? Find(new[] { "roll", "dodge", "evade" });
            var hit = Find(new[] { "hit", "damage", "impact" }, new[] { "attack", "crit", "big" });
            var die = Find(new[] { "death", "die", "defeat", "dead" }, new[] { "attack" });
            var stagger = Find(new[] { "stun", "dizzy", "stagger", "knock" }, new[] { "attack" }) ?? hit;
            var intro = Find(new[] { "victory", "taunt", "salute", "cheer", "power" }, new[] { "attack" }) ?? idle;

            var attackPool = FindAll(new[] { "attack", "slash", "punch", "kick", "combo", "swing", "stab", "chop", "strike" }, AttackAvoid);
            attackPool = attackPool.Where(c => c.length > 0.25f && c.length < 4f).ToList();
            var heavyPool = FindAll(new[] { "heavy", "strong", "power", "big", "special", "spin", "360", "crit" }, AttackAvoid);
            var castPool = FindAll(new[] { "cast", "spell", "skill", "magic", "buff" }, AttackAvoid);
            Debug.Log(string.Format("[WuWa] pools — attack:{0} heavy:{1} cast:{2}", attackPool.Count, heavyPool.Count, castPool.Count));
            if (attackPool.Count == 0)
            {
                Debug.LogError("[WuWa] no attack clips found — aborting controller build");
                return;
            }

            // shared duped locomotion
            var dIdle = Dupe(idle, "Idle", true);
            var dWalk = Dupe(walk ?? idle, "Walk", true);
            var dRun = Dupe(run ?? walk ?? idle, "Run", true);
            var dJump = Dupe(jump, "Jump", false);
            var dFall = Dupe(fall, "Fall", true);
            var dGlide = Dupe(glide, "Glide", true);
            var dDodge = Dupe(dodge, "Dodge", false);
            var dHit = Dupe(hit ?? idle, "Hit", false);
            var dDie = Dupe(die ?? hit ?? idle, "Die", false);
            var dStagger = Dupe(stagger ?? idle, "Stagger", true);
            var dIntro = Dupe(intro, "Intro", false);

            for (int m = 0; m < 3; m++)
            {
                var combo = new AnimationClip[4];
                for (int k = 0; k < 4; k++)
                {
                    var src = attackPool[(m * 4 + k) % attackPool.Count];
                    combo[k] = Dupe(src, "M" + m + "_A" + (k + 1), false);
                }
                var heavySrc = heavyPool.Count > 0 ? heavyPool[m % heavyPool.Count] : attackPool[(m * 4 + 3) % attackPool.Count];
                var skillSrc = castPool.Count > 0 ? castPool[m % castPool.Count] : attackPool[(m * 4 + 2) % attackPool.Count];
                var ultSrc = heavyPool.Count > 1 ? heavyPool[(m + 1) % heavyPool.Count] : attackPool[(m * 4 + 1) % attackPool.Count];
                var dHeavy = Dupe(heavySrc, "M" + m + "_Heavy", false);
                var dSkill = Dupe(skillSrc, "M" + m + "_Skill", false);
                var dUlt = Dupe(ultSrc, "M" + m + "_Ult", false);

                BuildController("Member" + m,
                    dIdle, dWalk, dRun, dJump, dFall, dGlide, dDodge, dHit, dDie, dStagger, dIntro,
                    combo, dHeavy, dSkill, dUlt);
            }

            // enemy: reuse pool with offset picks
            var eCombo = new AnimationClip[2];
            eCombo[0] = Dupe(attackPool[1 % attackPool.Count], "E_A1", false);
            eCombo[1] = Dupe(attackPool[2 % attackPool.Count], "E_A2", false);
            BuildEnemyController(dIdle, dRun, eCombo[0], eCombo[1], dHit, dDie, dStagger);

            // members carry a drawn sword: relaxed/guard idles, Speed x Combat loco, right-hand fist
            Debug.Log("[WuWa] sword pose: " + ApplySwordPose());
            // water: procedural tread / breaststroke clips and the Swim blend state
            Debug.Log("[WuWa] swim pose: " + ApplySwimPose());

            AssetDatabase.SaveAssets();
            Debug.Log("[WuWa] animator controllers built");
        }

        static void BuildController(string name,
            AnimationClip idle, AnimationClip walk, AnimationClip run,
            AnimationClip jump, AnimationClip fall, AnimationClip glide, AnimationClip dodge,
            AnimationClip hit, AnimationClip die, AnimationClip stagger, AnimationClip intro,
            AnimationClip[] combo, AnimationClip heavy, AnimationClip skill, AnimationClip ult)
        {
            string path = AnimDir + "/" + name + ".controller";
            var old = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = ctrl.layers[0].stateMachine;

            BlendTree tree;
            var loco = ctrl.CreateBlendTreeInController("Loco", out tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 0.4f);
            tree.AddChild(run, 0.72f);
            tree.AddChild(run, 1f);

            AddState(sm, "Jump", jump);
            AddState(sm, "Fall", fall);
            AddState(sm, "Glide", glide);
            AddState(sm, "Dodge", dodge, 1.25f);
            AddState(sm, "Hit", hit);
            AddState(sm, "Die", die);
            AddState(sm, "Stagger", stagger);
            AddState(sm, "Intro", intro, 1.3f);
            for (int i = 0; i < combo.Length; i++) AddState(sm, "A" + (i + 1), combo[i]);
            AddState(sm, "Heavy", heavy);
            AddState(sm, "Skill", skill);
            AddState(sm, "Ult", ult);

            sm.defaultState = loco;
            EditorUtility.SetDirty(ctrl);
        }

        static void BuildEnemyController(AnimationClip idle, AnimationClip move,
            AnimationClip a1, AnimationClip a2, AnimationClip hit, AnimationClip die, AnimationClip stagger)
        {
            string path = AnimDir + "/Enemy.controller";
            var old = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            var idleState = AddState(sm, "Idle", idle);
            AddState(sm, "Move", move);
            AddState(sm, "A1", a1);
            AddState(sm, "A2", a2);
            AddState(sm, "Hit", hit);
            AddState(sm, "Die", die);
            AddState(sm, "Stagger", stagger);
            sm.defaultState = idleState;
            EditorUtility.SetDirty(ctrl);
        }

        static AnimatorState AddState(AnimatorStateMachine sm, string name, AnimationClip clip, float speed = 1f)
        {
            var st = sm.AddState(name);
            st.motion = clip;
            st.speed = speed;
            return st;
        }

        /// Adds the WuWa expansion states (Plunge / DashAtk / IntroSkill / WallRun)
        /// to the already-built member controllers. Idempotent.
        public static void AddWuWaStates()
        {
            _library = null;
            var attackPool = FindAll(new[] { "attack", "slash", "punch", "kick", "combo", "swing", "stab", "chop", "strike" }, AttackAvoid)
                .Where(c => c.length > 0.25f && c.length < 4f).ToList();
            var plungePool = FindAll(new[] { "slam", "smash", "overhead", "jumpattack", "jump attack", "down" }, AttackAvoid);
            var dashPool = FindAll(new[] { "charge", "dash", "thrust", "lunge", "spin", "running" }, new[] { "idle", "walk", "death", "hit" });
            var castPool = FindAll(new[] { "cast", "spell", "power", "victory", "taunt", "buff", "cheer" }, new[] { "death" });
            var runClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipDir + "/Run.anim");
            Debug.Log(string.Format("[WuWa] wuwa-state pools — plunge:{0} dash:{1} cast:{2}", plungePool.Count, dashPool.Count, castPool.Count));

            for (int m = 0; m < 3; m++)
            {
                string path = AnimDir + "/Member" + m + ".controller";
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ctrl == null) { Debug.LogWarning("[WuWa] missing controller " + path); continue; }
                var sm = ctrl.layers[0].stateMachine;

                var plungeSrc = plungePool.Count > 0 ? plungePool[m % plungePool.Count] : attackPool[(m * 4 + 3) % attackPool.Count];
                var dashSrc = dashPool.Count > 0 ? dashPool[m % dashPool.Count] : attackPool[(m * 4 + 1) % attackPool.Count];
                var introSrc = castPool.Count > 0 ? castPool[m % castPool.Count] : attackPool[(m * 4 + 2) % attackPool.Count];

                EnsureState(sm, "Plunge", Dupe(plungeSrc, "M" + m + "_Plunge", false), 1.1f);
                EnsureState(sm, "DashAtk", Dupe(dashSrc, "M" + m + "_DashAtk", false), 1.3f);
                EnsureState(sm, "IntroSkill", Dupe(introSrc, "M" + m + "_IntroSkill", false), 1.15f);
                EnsureState(sm, "WallRun", runClip, 1.1f);
                EditorUtility.SetDirty(ctrl);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[WuWa] WuWa states added to member controllers");
        }

        static void EnsureState(AnimatorStateMachine sm, string name, AnimationClip clip, float speed)
        {
            foreach (var cs in sm.states)
            {
                if (cs.state.name == name)
                {
                    if (clip != null) cs.state.motion = clip;
                    cs.state.speed = speed;
                    return;
                }
            }
            var st = sm.AddState(name);
            st.motion = clip;
            st.speed = speed;
        }

        // ---------------------------------------------------------------- sword pose
        // The party carries a drawn sword at all times, but every clip in the packs was authored
        // unarmed: the shortest "idle" match is the Brute warrior stance and the attacks splay the
        // fingers. Three fixes, all applied to the existing controllers: a relaxed idle out of
        // combat, a one-handed weapon guard in combat (Loco becomes a Speed x Combat blend that
        // PlayerController drives), and a right-fingers override layer holding a fist so the hand
        // closes on the grip in every state. WeaponVisual places the sword in that fist.
        const string MaskPath = AnimDir + "/RightFingers.mask";
        static readonly string[] MemberIdleNames = { "HumanF@Idle01", "HumanM@Idle01" };
        static readonly string[] MemberCombatIdleNames = { "HumanF@CombatIdle1H01", "HumanM@CombatIdle1H01" };

        /// Right-hand muscle values for a hammer grip. Finger "Stretched" muscles run -1..1 over the
        /// avatar's limit and keep extrapolating past it; on Unity-chan -1.3/-1.7/-0.8 measured
        /// out at ~85/100/40 deg of bend, a closed fist around the 3 cm grip.
        static readonly KeyValuePair<string, float>[] FistMuscles =
        {
            new KeyValuePair<string, float>("RightHand.Thumb.1 Stretched", -1.4f),
            new KeyValuePair<string, float>("RightHand.Thumb.Spread", -1.0f),
            new KeyValuePair<string, float>("RightHand.Thumb.2 Stretched", -1.1f),
            new KeyValuePair<string, float>("RightHand.Thumb.3 Stretched", -0.8f),
            new KeyValuePair<string, float>("RightHand.Index.1 Stretched", -1.3f),
            new KeyValuePair<string, float>("RightHand.Index.Spread", 0f),
            new KeyValuePair<string, float>("RightHand.Index.2 Stretched", -1.7f),
            new KeyValuePair<string, float>("RightHand.Index.3 Stretched", -0.8f),
            new KeyValuePair<string, float>("RightHand.Middle.1 Stretched", -1.3f),
            new KeyValuePair<string, float>("RightHand.Middle.Spread", 0f),
            new KeyValuePair<string, float>("RightHand.Middle.2 Stretched", -1.7f),
            new KeyValuePair<string, float>("RightHand.Middle.3 Stretched", -0.8f),
            new KeyValuePair<string, float>("RightHand.Ring.1 Stretched", -1.3f),
            new KeyValuePair<string, float>("RightHand.Ring.Spread", 0f),
            new KeyValuePair<string, float>("RightHand.Ring.2 Stretched", -1.7f),
            new KeyValuePair<string, float>("RightHand.Ring.3 Stretched", -0.8f),
            new KeyValuePair<string, float>("RightHand.Little.1 Stretched", -1.3f),
            new KeyValuePair<string, float>("RightHand.Little.Spread", 0f),
            new KeyValuePair<string, float>("RightHand.Little.2 Stretched", -1.7f),
            new KeyValuePair<string, float>("RightHand.Little.3 Stretched", -0.8f),
        };

        [MenuItem("WuWa/Anim/Apply sword pose")]
        public static void ApplySwordPoseMenu() { Debug.Log(ApplySwordPose()); }

        /// Idempotent; BuildAll calls it, and it can be re-run alone after tuning FistMuscles.
        public static string ApplySwordPose()
        {
            _library = null;
            WuWaImportTools.EnsureFolder(AnimDir);
            WuWaImportTools.EnsureFolder(ClipDir);
            var idleSrc = FindExact(MemberIdleNames)
                          ?? Find(new[] { "idle" }, new[] { "sword", "2h", "bow", "crouch", "sit", "block", "combatidle" });
            if (idleSrc == null) return "no idle clip in the library";
            var combatSrc = FindExact(MemberCombatIdleNames) ?? idleSrc;
            var idle = Dupe(idleSrc, "MemberIdle", true);
            var combatIdle = Dupe(combatSrc, "MemberIdleCombat", true);
            var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipDir + "/Walk.anim");
            var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipDir + "/Run.anim");
            if (walk == null || run == null) return "Walk/Run clips missing - run BuildAll first";
            var fist = BuildFistClip();
            var mask = BuildRightFingerMask();

            var sb = new System.Text.StringBuilder();
            sb.Append("idle=" + idleSrc.name + " combatIdle=" + combatSrc.name + "\n");
            for (int m = 0; m < 3; m++)
            {
                string path = AnimDir + "/Member" + m + ".controller";
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ctrl == null) { sb.Append("missing " + path + "\n"); continue; }
                EnsureFloatParam(ctrl, "Combat");
                string loco = RebuildLoco(ctrl, idle, combatIdle, walk, run);
                EnsureGripLayer(ctrl, fist, mask);
                EditorUtility.SetDirty(ctrl);
                sb.Append("Member" + m + ": " + loco + " layers=" + ctrl.layers.Length + "\n");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static AnimationClip FindExact(string[] names)
        {
            foreach (var n in names)
            {
                string ln = n.ToLowerInvariant();
                foreach (var e in Library) if (e.lname == ln) return e.clip;
            }
            return null;
        }

        static void EnsureFloatParam(AnimatorController ctrl, string name)
        {
            foreach (var p in ctrl.parameters) if (p.name == name) return;
            ctrl.AddParameter(name, AnimatorControllerParameterType.Float);
        }

        /// Loco as a 2D freeform blend: Speed along X (0 idle / 0.4 walk / 0.72 run / 1 sprint) and
        /// the Combat stance along Y, so every "Loco" crossfade in the gameplay code keeps working.
        /// The existing tree sub-asset is rewritten in place rather than replaced.
        static string RebuildLoco(AnimatorController ctrl, AnimationClip idle, AnimationClip combatIdle,
                                  AnimationClip walk, AnimationClip run)
        {
            var sm = ctrl.layers[0].stateMachine;
            AnimatorState loco = null;
            foreach (var cs in sm.states) if (cs.state.name == "Loco") loco = cs.state;
            if (loco == null) return "no Loco state";
            var tree = loco.motion as BlendTree;
            if (tree == null)
            {
                tree = new BlendTree { name = "Loco", hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(tree, ctrl);
                loco.motion = tree;
            }
            tree.blendType = BlendTreeType.FreeformCartesian2D;
            tree.blendParameter = "Speed";
            tree.blendParameterY = "Combat";
            tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                Child(idle, 0f, 0f), Child(combatIdle, 0f, 1f),
                Child(walk, 0.4f, 0f), Child(walk, 0.4f, 1f),
                Child(run, 0.72f, 0f), Child(run, 0.72f, 1f),
                Child(run, 1f, 0f), Child(run, 1f, 1f),
            };
            EditorUtility.SetDirty(tree);
            return "loco 2D x" + tree.children.Length;
        }

        static ChildMotion Child(Motion m, float speed, float combat)
        {
            return new ChildMotion { motion = m, position = new Vector2(speed, combat), threshold = speed, timeScale = 1f };
        }

        static void EnsureGripLayer(AnimatorController ctrl, AnimationClip fist, AvatarMask mask)
        {
            var layers = ctrl.layers;                 // a copy: write it back at the end
            int idx = -1;
            for (int i = 0; i < layers.Length; i++) if (layers[i].name == "Grip") idx = i;
            if (idx < 0)
            {
                ctrl.AddLayer("Grip");
                layers = ctrl.layers;
                idx = layers.Length - 1;
            }
            var layer = layers[idx];
            layer.avatarMask = mask;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.defaultWeight = 1f;
            var sm = layer.stateMachine;
            AnimatorState st = null;
            foreach (var cs in sm.states) if (cs.state.name == "Fist") st = cs.state;
            if (st == null) st = sm.AddState("Fist");
            st.motion = fist;
            sm.defaultState = st;
            ctrl.layers = layers;
        }

        /// A one-second looping humanoid clip holding the right-hand muscles of FistMuscles.
        static AnimationClip BuildFistClip()
        {
            string path = ClipDir + "/Fist.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = "Fist", frameRate = 30f };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            foreach (var kv in FistMuscles)
            {
                var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key);
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f, kv.Value));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        static AvatarMask BuildRightFingerMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null)
            {
                mask = new AvatarMask { name = "RightFingers" };
                AssetDatabase.CreateAsset(mask, MaskPath);
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, i == (int)AvatarMaskBodyPart.RightFingers);
            EditorUtility.SetDirty(mask);
            return mask;
        }

        // ---------------------------------------------------------------- swim pose
        // No pack ships a swim, so the water clips are authored here as humanoid muscle curves:
        // treading water (upright, sculling arms, cycling legs) and a breaststroke (prone, root
        // pitched 62 deg, arms glide-sweep-recover, frog kick). The Swim state blends them on
        // Speed: 0 tread, 0.5 stroke, 1 the same stroke at 1.6x for the dash. Root position and
        // rotation are baked into the pose so they survive applyRootMotion = false. Muscle sign
        // convention: -1 is the first word of the name, +1 the second (Arm Down-Up: +1 = up).

        static AnimationCurve K(params float[] tv)
        {
            var keys = new Keyframe[tv.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(tv[i * 2], tv[i * 2 + 1]);
            var c = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        static void Both(Dictionary<string, AnimationCurve> d, string suffix, AnimationCurve c)
        {
            d["Left " + suffix] = c;
            d["Right " + suffix] = c;
        }

        static AnimationClip WriteMuscleClip(string name, float length, Dictionary<string, AnimationCurve> curves, Vector3 rootT, float pitchDeg)
        {
            string path = ClipDir + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = name, frameRate = 30f };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            foreach (var kv in curves)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), kv.Value);
            var q = Quaternion.AngleAxis(pitchDeg, Vector3.right);
            var root = new Dictionary<string, float>
            {
                { "RootT.x", rootT.x }, { "RootT.y", rootT.y }, { "RootT.z", rootT.z },
                { "RootQ.x", q.x }, { "RootQ.y", q.y }, { "RootQ.z", q.z }, { "RootQ.w", q.w },
            };
            foreach (var kv in root)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), AnimationCurve.Constant(0f, length, kv.Value));
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = true;
            s.loopBlend = true;
            s.loopBlendOrientation = true;
            s.loopBlendPositionY = true;
            s.loopBlendPositionXZ = true;
            s.keepOriginalOrientation = true;
            s.keepOriginalPositionY = true;
            s.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// Treading water: upright with a slight lean back, arms sculling just under the surface,
        /// legs cycling. Root 0.54 (hips ~0.48 m above the swim root = shoulders at the surface).
        static AnimationClip BuildTreadClip()
        {
            const float L = 2.0f;
            var d = new Dictionary<string, AnimationCurve>();
            d["Neck Nod Down-Up"] = K(0f, 0.3f, L, 0.3f);
            d["Head Nod Down-Up"] = K(0f, 0.45f, L, 0.45f);
            d["Spine Front-Back"] = K(0f, 0.05f, L, 0.05f);
            Both(d, "Arm Down-Up", K(0f, -0.05f, 0.5f, -0.2f, 1f, -0.05f, 1.5f, -0.2f, L, -0.05f));
            d["Left Arm Front-Back"] = K(0f, -0.35f, 0.5f, -0.05f, 1f, -0.35f, 1.5f, -0.05f, L, -0.35f);
            d["Right Arm Front-Back"] = K(0f, -0.05f, 0.5f, -0.35f, 1f, -0.05f, 1.5f, -0.35f, L, -0.05f);
            Both(d, "Forearm Stretch", K(0f, -0.35f, L, -0.35f));
            Both(d, "Hand Down-Up", K(0f, 0f, L, 0f));
            Both(d, "Shoulder Down-Up", K(0f, 0.15f, L, 0.15f));
            d["Left Upper Leg Front-Back"] = K(0f, -0.55f, 0.5f, -0.15f, 1f, -0.55f, 1.5f, -0.15f, L, -0.55f);
            d["Right Upper Leg Front-Back"] = K(0f, -0.15f, 0.5f, -0.55f, 1f, -0.15f, 1.5f, -0.55f, L, -0.15f);
            Both(d, "Upper Leg In-Out", K(0f, 0.25f, L, 0.25f));
            d["Left Lower Leg Stretch"] = K(0f, -0.8f, 0.5f, -0.3f, 1f, -0.8f, 1.5f, -0.3f, L, -0.8f);
            d["Right Lower Leg Stretch"] = K(0f, -0.3f, 0.5f, -0.8f, 1f, -0.3f, 1.5f, -0.8f, L, -0.3f);
            Both(d, "Foot Up-Down", K(0f, 0.3f, L, 0.3f));
            return WriteMuscleClip("SwimTread", L, d, new Vector3(0f, 0.54f, 0f), -8f);
        }

        /// Breaststroke: glide (0) -> outsweep (0.3) -> pull to the chest (0.55) -> recover (0.85)
        /// -> extend (1.1) -> glide (1.4); legs tuck during the recovery and kick out on the extend.
        /// Root 0.82 up (hips 0.22 m under the surface), pitched 62 deg so the head clears the water.
        static AnimationClip BuildSwimClip()
        {
            const float L = 1.4f;
            var d = new Dictionary<string, AnimationCurve>();
            d["Neck Nod Down-Up"] = K(0f, 0.55f, L, 0.55f);
            d["Head Nod Down-Up"] = K(0f, 0.8f, L, 0.8f);
            d["Spine Front-Back"] = K(0f, 0.25f, L, 0.25f);
            d["Chest Front-Back"] = K(0f, 0.15f, L, 0.15f);
            Both(d, "Arm Down-Up", K(0f, 1.0f, 0.3f, 0.55f, 0.55f, -0.25f, 0.85f, -0.15f, 1.1f, 0.6f, L, 1.0f));
            Both(d, "Arm Front-Back", K(0f, -0.15f, 0.3f, -0.2f, 0.55f, -0.75f, 0.85f, -0.85f, 1.1f, -0.45f, L, -0.15f));
            Both(d, "Forearm Stretch", K(0f, 0.1f, 0.3f, -0.1f, 0.55f, -0.9f, 0.85f, -1.0f, 1.1f, -0.4f, L, 0.1f));
            Both(d, "Hand Down-Up", K(0f, 0f, L, 0f));
            Both(d, "Shoulder Down-Up", K(0f, 0.4f, 0.55f, 0f, L, 0.4f));
            Both(d, "Upper Leg Front-Back", K(0f, 0.05f, 0.4f, 0.05f, 0.7f, -0.55f, 0.95f, -0.45f, 1.2f, 0f, L, 0.05f));
            Both(d, "Upper Leg In-Out", K(0f, 0.05f, 0.4f, 0.05f, 0.7f, 0.35f, 0.95f, 0.7f, 1.2f, 0.1f, L, 0.05f));
            Both(d, "Lower Leg Stretch", K(0f, 0.05f, 0.4f, 0.05f, 0.7f, -0.9f, 0.95f, -0.7f, 1.2f, 0f, L, 0.05f));
            Both(d, "Foot Up-Down", K(0f, 0.6f, 0.4f, 0.6f, 0.7f, -0.5f, 0.95f, -0.4f, 1.2f, 0.6f, L, 0.6f));
            return WriteMuscleClip("Swim", L, d, new Vector3(0f, 0.82f, 0f), 62f);
        }

        static void EnsureSwimState(AnimatorController ctrl, AnimationClip tread, AnimationClip swim)
        {
            var sm = ctrl.layers[0].stateMachine;
            AnimatorState st = null;
            foreach (var cs in sm.states) if (cs.state.name == "Swim") st = cs.state;
            if (st == null) st = sm.AddState("Swim");
            var tree = st.motion as BlendTree;
            if (tree == null)
            {
                tree = new BlendTree { name = "Swim", hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(tree, ctrl);
                st.motion = tree;
            }
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                new ChildMotion { motion = tread, threshold = 0f, timeScale = 1f },
                new ChildMotion { motion = swim, threshold = 0.5f, timeScale = 1f },
                new ChildMotion { motion = swim, threshold = 1f, timeScale = 1.6f },
            };
            EditorUtility.SetDirty(tree);
        }

        [MenuItem("WuWa/Anim/Apply swim pose")]
        public static void ApplySwimPoseMenu() { Debug.Log(ApplySwimPose()); }

        /// Idempotent; BuildAll calls it, and it can be re-run alone after tuning the clips.
        public static string ApplySwimPose()
        {
            WuWaImportTools.EnsureFolder(AnimDir);
            WuWaImportTools.EnsureFolder(ClipDir);
            var tread = BuildTreadClip();
            var swim = BuildSwimClip();
            var sb = new System.Text.StringBuilder();
            for (int m = 0; m < 3; m++)
            {
                string path = AnimDir + "/Member" + m + ".controller";
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ctrl == null) { sb.Append("missing " + path + "\n"); continue; }
                EnsureSwimState(ctrl, tread, swim);
                EditorUtility.SetDirty(ctrl);
                sb.Append("Member" + m + ": Swim state ok\n");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        /// Read a controller's state clip length (used when baking AttackDefs).
        public static float StateClipLength(string controllerPath, string state, float fallback)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (ctrl == null) return fallback;
            foreach (var cs in ctrl.layers[0].stateMachine.states)
            {
                if (cs.state.name != state) continue;
                var clip = cs.state.motion as AnimationClip;
                if (clip != null) return clip.length;
            }
            return fallback;
        }
    }
}
