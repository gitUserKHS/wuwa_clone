using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WuWa.EditorTools
{
    /// Builds the three playable member prefabs (Unity-chan variants with toon
    /// materials) and the shadow enemy prefabs.
    public static class WuWaCharBuild
    {
        const string PrefabDir = "Assets/WuWa/Prefabs";
        const string MatDir = "Assets/WuWa/Art/Materials";

        // ---------------------------------------------------------------- model
        public static GameObject FindChanModel()
        {
            // prefer the plain model FBX; fall back to any unitychan prefab
            var guids = AssetDatabase.FindAssets("unitychan t:Model");
            string best = null;
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                string n = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (n == "unitychan") { best = p; break; }
                if (best == null && n.Contains("unitychan")) best = p;
            }
            if (best == null)
            {
                foreach (var g in AssetDatabase.FindAssets("unitychan t:Prefab"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant() == "unitychan") { best = p; break; }
                }
            }
            if (best == null) { Debug.LogError("[WuWa] unitychan model not found"); return null; }
            Debug.Log("[WuWa] unitychan source: " + best);

            var imp = AssetImporter.GetAtPath(best) as ModelImporter;
            if (imp != null && imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(best);
        }

        public static Shader FindToonShader()
        {
            string[] preferred =
            {
                "Toon", "Unity Toon Shader/Toon", "UnityToonShader/Toon", "Toon (Built-in)"
            };
            foreach (var n in preferred)
            {
                var s = Shader.Find(n);
                if (s != null) { Debug.Log("[WuWa] toon shader: " + s.name); return s; }
            }
            var all = ShaderUtil.GetAllShaderInfo()
                .Where(i => i.name.ToLowerInvariant().Contains("toon") &&
                            !i.name.Contains("Hidden") && !i.name.Contains("Tess"))
                .OrderBy(i => i.name.Length)
                .ToList();
            foreach (var info in all)
            {
                var s = Shader.Find(info.name);
                if (s != null) { Debug.Log("[WuWa] toon shader (searched): " + s.name); return s; }
            }
            Debug.LogWarning("[WuWa] no toon shader found; falling back to Simple Lit");
            return Shader.Find("Universal Render Pipeline/Simple Lit");
        }

        // ---------------------------------------------------------------- materials
        static bool NameHas(string s, params string[] keys)
        {
            s = s.ToLowerInvariant();
            return keys.Any(s.Contains);
        }

        static void SetTex(Material m, Texture t, params string[] props)
        {
            foreach (var p in props) if (m.HasProperty(p)) m.SetTexture(p, t);
        }

        static void SetCol(Material m, Color c, params string[] props)
        {
            foreach (var p in props) if (m.HasProperty(p)) m.SetColor(p, c);
        }

        static Material MakeUnlit(Texture main, Color tint, bool transparent)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh);
            SetTex(m, main, "_BaseMap", "_MainTex");
            SetCol(m, tint, "_BaseColor", "_Color");
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            }
            return m;
        }

        static Material MakeToon(Shader toon, Texture main, Color tint)
        {
            var m = new Material(toon);
            SetTex(m, main, "_MainTex", "_BaseMap");
            SetCol(m, tint, "_Color", "_BaseColor");
            SetTex(m, main, "_1st_ShadeMap", "_2nd_ShadeMap");
            SetCol(m, new Color(tint.r * 0.72f, tint.g * 0.70f, tint.b * 0.82f), "_1st_ShadeColor");
            SetCol(m, new Color(tint.r * 0.55f, tint.g * 0.52f, tint.b * 0.66f), "_2nd_ShadeColor");
            if (m.HasProperty("_BaseColor_Step")) m.SetFloat("_BaseColor_Step", 0.62f);
            if (m.HasProperty("_BaseShade_Feather")) m.SetFloat("_BaseShade_Feather", 0.06f);
            if (m.HasProperty("_Outline_Width")) m.SetFloat("_Outline_Width", 1.0f);
            SetCol(m, new Color(0.18f, 0.12f, 0.16f), "_Outline_Color");
            if (m.HasProperty("_Is_LightColor_Base")) m.SetFloat("_Is_LightColor_Base", 1f);
            return m;
        }

        /// GUID-stable persist: overwrite the existing asset in place so prefab
        /// references never break, even across repeated builds.
        static Material Persist(Material m, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old != null)
            {
                old.shader = m.shader;
                old.CopyPropertiesFromMaterial(m);
                old.renderQueue = m.renderQueue;
                EditorUtility.SetDirty(old);
                Object.DestroyImmediate(m);
                return old;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// Replace every renderer material with toon/unlit variants, tinted per member.
        static void ApplyCharacterMaterials(GameObject inst, Shader toon, Color themeTint, string matSubdir, bool shadow)
        {
            WuWaImportTools.EnsureFolder(MatDir + "/" + matSubdir);
            var cache = new Dictionary<Material, Material>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    Material rep;
                    if (!cache.TryGetValue(src, out rep))
                    {
                        Texture main = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
                        if (main == null && src.HasProperty("_BaseMap")) main = src.GetTexture("_BaseMap");
                        string n = (src.name + "_" + (main != null ? main.name : "")).ToLowerInvariant();

                        if (shadow)
                        {
                            var simple = Shader.Find("Universal Render Pipeline/Simple Lit");
                            rep = new Material(simple);
                            SetTex(rep, main, "_BaseMap", "_MainTex");
                            SetCol(rep, themeTint, "_BaseColor", "_Color");
                            if (rep.HasProperty("_Smoothness")) rep.SetFloat("_Smoothness", 0.05f);
                            rep.EnableKeyword("_EMISSION");
                            if (rep.HasProperty("_EmissionColor")) rep.SetColor("_EmissionColor", Color.black);
                        }
                        else if (NameHas(n, "cheek", "eyeline", "eyelash", "mat_class", "tel", "alpha"))
                        {
                            rep = MakeUnlit(main, Color.white, true);
                        }
                        else if (NameHas(n, "face", "eye", "eyebase", "skin", "body_01"))
                        {
                            rep = MakeUnlit(main, Color.white, false);
                        }
                        else
                        {
                            Color tint = Color.Lerp(Color.white, themeTint, 0.30f);
                            rep = MakeToon(toon, main, tint);
                        }
                        rep.name = matSubdir + "_" + src.name;
                        rep = Persist(rep, MatDir + "/" + matSubdir + "/" + rep.name + ".mat");
                        cache[src] = rep;
                    }
                    mats[i] = rep;
                }
                r.sharedMaterials = mats;
            }
        }

        /// Remove imported pack scripts (old input system usage) except spring bones.
        static void StripScripts(GameObject inst)
        {
            var comps = inst.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var c in comps)
            {
                if (c == null) continue;   // missing script
                string tn = c.GetType().FullName;
                if (tn.StartsWith("WuWa")) continue;
                if (tn.Contains("Spring")) continue;
                Object.DestroyImmediate(c, true);
            }
        }

        // ---------------------------------------------------------------- members
        class MemberDef
        {
            public string name;
            public Element element;
            public Color color;
            public float hp, atk, skillCd;
            public float skillMul, ultMul;
            public float[] comboMul;
            public OutroType outro;
            public float outroVal;
        }

        // Kit differentiation per GDD ch.5 — Haru marks, Yuki controls, Aka bursts.
        static readonly MemberDef[] Defs =
        {
            new MemberDef { name = "리라에", element = Element.Spectro, color = new Color(1f, 0.84f, 0.42f),
                hp = 9200f, atk = 118f, skillCd = 7f, skillMul = 3.8f, ultMul = 9.5f,
                comboMul = new[] { 0.95f, 1.05f, 1.25f, 1.6f }, outro = OutroType.DamageUp, outroVal = 1.18f },
            new MemberDef { name = "세레네", element = Element.Glacio, color = new Color(0.45f, 0.82f, 1f),
                hp = 8400f, atk = 106f, skillCd = 8f, skillMul = 3.4f, ultMul = 8.5f,
                comboMul = new[] { 0.9f, 1.0f, 1.2f, 1.5f }, outro = OutroType.SkillHaste, outroVal = 0.8f },
            new MemberDef { name = "에이리스", element = Element.Fusion, color = new Color(1f, 0.46f, 0.36f),
                hp = 8800f, atk = 128f, skillCd = 7f, skillMul = 4.2f, ultMul = 11f,
                comboMul = new[] { 1.0f, 1.1f, 1.3f, 1.7f }, outro = OutroType.HeavyUp, outroVal = 1.25f },
        };

        public static void BuildMemberPrefabs()
        {
            var model = FindChanModel();
            if (model == null) return;
            var toon = FindToonShader();
            WuWaImportTools.EnsureFolder(PrefabDir);

            for (int i = 0; i < 3; i++)
            {
                var def = Defs[i];
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Member" + i;
                StripScripts(inst);
                ApplyCharacterMaterials(inst, toon, def.color, "member" + i, false);

                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                string ctrlPath = "Assets/WuWa/Anim/Member" + i + ".controller";
                anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ctrlPath);
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var mc = inst.AddComponent<MemberConfig>();
                mc.charName = def.name;
                mc.element = def.element;
                mc.themeColor = def.color;
                mc.portraitResource = "UI/portrait_" + i;
                mc.maxHp = def.hp;
                mc.baseAtk = def.atk;
                mc.skillCooldown = def.skillCd;
                mc.ultEnergyMax = 100f;
                mc.outroType = def.outro;
                mc.outroBuffMul = def.outroVal;

                mc.combo = new AttackDef[4];
                float[] dmg = def.comboMul;
                float[] stag = { 8f, 9f, 10f, 15f };
                for (int k = 0; k < 4; k++)
                {
                    mc.combo[k] = new AttackDef
                    {
                        state = "A" + (k + 1),
                        dmgMul = dmg[k],
                        hitTime = 0.36f,
                        clipLen = WuWaAnimBuild.StateClipLength(ctrlPath, "A" + (k + 1), 0.8f),
                        speed = 1.35f,
                        range = 2.1f,
                        radius = 1.75f,
                        knockback = k == 3 ? 5f : 2.2f,
                        stagger = stag[k],
                        lunge = 2.6f,
                        vfx = 0
                    };
                }
                mc.heavy = new AttackDef
                {
                    state = "Heavy", dmgMul = 2.4f, hitTime = 0.42f,
                    clipLen = WuWaAnimBuild.StateClipLength(ctrlPath, "Heavy", 1.1f),
                    speed = 1.15f, range = 2.4f, radius = 2.1f, knockback = 6.5f, stagger = 24f, lunge = 3.4f, vfx = 1
                };
                mc.skill = new AttackDef
                {
                    state = "Skill", dmgMul = def.skillMul, hitTime = 0.4f,
                    clipLen = WuWaAnimBuild.StateClipLength(ctrlPath, "Skill", 1.2f),
                    speed = 1.1f, range = 0f, radius = 4.6f, knockback = 4.5f, stagger = 32f, lunge = 0.5f, vfx = 2
                };
                mc.ult = new AttackDef
                {
                    state = "Ult", dmgMul = def.ultMul, hitTime = 0.45f,
                    clipLen = WuWaAnimBuild.StateClipLength(ctrlPath, "Ult", 1.5f),
                    speed = 1.0f, range = 0f, radius = 7.2f, knockback = 10f, stagger = 70f, lunge = 0f, vfx = 3
                };
                mc.plunge.state = "Plunge";
                mc.plunge.clipLen = WuWaAnimBuild.StateClipLength(ctrlPath, "Plunge", 0.9f);
                mc.dashAtk.state = "DashAtk";
                mc.dashAtk.clipLen = WuWaAnimBuild.StateClipLength(ctrlPath, "DashAtk", 0.8f);
                mc.dashAtk.speed = 1.3f;
                mc.introSkill.state = "IntroSkill";
                mc.introSkill.clipLen = WuWaAnimBuild.StateClipLength(ctrlPath, "IntroSkill", 1.1f);

                string prefabPath = PrefabDir + "/Member" + i + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                Object.DestroyImmediate(inst);
                Debug.Log("[WuWa] built " + prefabPath);
            }
            // generated Meshy characters (Blender rig) replace the unitychan visuals; re-apply so a rebuild never reverts them
            string[] stems = { "Lirae", "Selene", "Eiris" };
            string[] names = { "\uB9AC\uB77C\uC5D0", "\uC138\uB808\uB124", "\uC5D0\uC774\uB9AC\uC2A4" };
            for (int i = 0; i < 3; i++)
                if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WuWa/Characters/" + stems[i] + "/" + stems[i] + ".fbx") != null)
                    Debug.Log("[WuWa] " + stems[i] + " swap: " + WuWaLiraeBuild.SwapIntoMember(i, names[i], stems[i]));
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- enemies
        public static void BuildEnemyPrefabs()
        {
            var model = FindChanModel();
            if (model == null) return;
            BuildEnemy(model, "EnemyMob", EnemyKind.Melee, new Color(0.16f, 0.13f, 0.24f),
                hp: 2100f, dmg: 340f, speed: 3.8f, scale: 1.06f, name: "그림자 방랑자",
                stagger: 90f, atkCd: 2.4f, parry: 0.32f);
            BuildEnemy(model, "EnemyFast", EnemyKind.Fast, new Color(0.08f, 0.26f, 0.28f),
                hp: 1400f, dmg: 290f, speed: 5.4f, scale: 0.97f, name: "질풍의 그림자",
                stagger: 70f, atkCd: 1.7f, parry: 0.3f);
            BuildEnemy(model, "EnemyRanged", EnemyKind.Ranged, new Color(0.26f, 0.13f, 0.36f),
                hp: 1600f, dmg: 300f, speed: 3.4f, scale: 1.0f, name: "주술사의 그림자",
                stagger: 75f, atkCd: 2.8f, parry: 0f);
            BuildEnemy(model, "EnemyTank", EnemyKind.Tank, new Color(0.30f, 0.10f, 0.12f),
                hp: 5200f, dmg: 520f, speed: 2.6f, scale: 1.42f, name: "거암의 그림자",
                stagger: 180f, atkCd: 3.2f, parry: 0.5f);
            BuildEnemy(model, "EnemyBoss", EnemyKind.Boss, new Color(0.2f, 0.1f, 0.12f),
                hp: 17000f, dmg: 520f, speed: 4.4f, scale: 2.1f, name: "무관의 그림자",
                stagger: 220f, atkCd: 2.8f, parry: 0.35f);
            AssetDatabase.SaveAssets();
        }

        static void BuildEnemy(GameObject model, string prefabName, EnemyKind kind, Color tint,
            float hp, float dmg, float speed, float scale, string name, float stagger, float atkCd, float parry)
        {
            bool boss = kind == EnemyKind.Boss;
            var root = new GameObject(prefabName);
            root.layer = LayerMask.NameToLayer("Enemy");

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "rig";
            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale = Vector3.one * scale;
            StripScripts(inst);
            ApplyCharacterMaterials(inst, null, tint, prefabName.ToLowerInvariant(), true);
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;

            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/WuWa/Anim/Enemy.controller");
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.7f * scale;
            cc.radius = 0.38f * scale;
            cc.center = new Vector3(0f, 0.88f * scale, 0f);
            cc.slopeLimit = 50f;

            var health = root.AddComponent<Health>();
            health.maxHp = hp;
            health.maxStagger = stagger;
            health.isBoss = boss;
            health.displayName = name;

            var ai = root.AddComponent<EnemyAI>();
            ai.kind = kind;
            ai.moveSpeed = speed;
            ai.attackDamage = dmg;
            ai.isBoss = boss;
            ai.parryChance = parry;
            ai.attackCooldown = atkCd;
            ai.heavyPoise = kind == EnemyKind.Tank || boss;
            ai.attackRange = boss ? 3.4f : (kind == EnemyKind.Tank ? 3.0f : 2.4f);
            ai.attackRadius = boss ? 2.6f : (kind == EnemyKind.Tank ? 2.5f : 1.9f);
            ai.telegraphTime = boss ? 0.72f : (kind == EnemyKind.Tank ? 0.85f : (kind == EnemyKind.Fast ? 0.42f : 0.55f));
            ai.chaseRange = boss ? 30f : (kind == EnemyKind.Ranged ? 26f : 22f);
            ai.projectileDamage = 300f;
            ai.preferredRange = 11f;

            string path = PrefabDir + "/" + prefabName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("[WuWa] built " + path);
        }
    }
}
