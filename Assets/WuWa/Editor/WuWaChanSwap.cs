using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WuWa;

namespace WuWa.EditorTools
{
    /// Installs the Unity-chan model into the party member prefabs, one recolour per member.
    ///
    /// Unlike WuWaLiraeBuild (one mesh, one material, one albedo), Unity-chan is 23 renderers across
    /// 9 materials, so materials are rebuilt per source-material NAME rather than blanket-assigned.
    /// The FBX importer settings are deliberately left untouched: the package already ships it as
    /// Humanoid with blendshapes on and globalScale 0.01 / useFileScale off, and "fixing" those the
    /// way the Meshy path does would shrink the model 100x and strip the face shapes.
    public static class WuWaChanSwap
    {
        const string ChanFbx = "Assets/unity-chan!/Unity-chan! Model/Art/Models/unitychan.fbx";
        const string Tex = "Assets/unity-chan!/Unity-chan! Model/Art/UnityChanShader/Texture/";
        const string Out = "Assets/WuWa/Characters/UnityChan/";

        /// Outline widths are per material because Unity-chan's vertex colours are all (1,1,1,1):
        /// CharToon multiplies the hull width by vertex-colour R, so there is no authored mask to
        /// thin the outline on the face. Face-region meshes get 0 or the tiny coplanar eye/lash
        /// overlays each grow their own black shell.
        class Slot
        {
            public string tex;          // texture path, {set} substituted with the colour-set key
            public float outline;
            public bool clip;
            public float cutoff = 0.35f;
            public float hairBand;
            public bool transparent;    // cheek blush only: too faint for alpha clipping
            public bool flat;           // near-flat light ramp, see below
        }

        /// `flat` matters more than it sounds. CharToon's ramp was tuned for the Meshy characters,
        /// whose faces are sculpted; Unity-chan's face is a nearly flat plane with the features
        /// painted on, so under that ramp the whole face oval lands in the shadow band at once and
        /// the cream texture times the cool shade tint comes out olive-grey, in a hard-edged patch
        /// against the lit neck. Anime faces are conventionally shaded flat for exactly this reason.
        /// The skin shares the treatment so the neck does not step against the jaw.
        static readonly Dictionary<string, Slot> Slots = new Dictionary<string, Slot>
        {
            { "body",      new Slot { tex = Out + "{set}/body_01.png",       outline = 0.0018f } },
            { "hair",      new Slot { tex = Out + "{set}/hair_01.png",       outline = 0.0018f, hairBand = 0.35f } },
            { "skin1",     new Slot { tex = Tex + "skin_01.tga",             outline = 0.0016f, flat = true } },
            { "face",      new Slot { tex = Tex + "face_00.tga",             outline = 0f, flat = true } },
            { "eyeline",   new Slot { tex = Tex + "eyeline_00.tga",          outline = 0f, clip = true, cutoff = 0.30f, flat = true } },
            { "eyebase",   new Slot { tex = Tex + "eyeline_00.tga",          outline = 0f, flat = true } },
            { "eye_L1",    new Slot { tex = Out + "{set}/eye_iris_L_00.png", outline = 0f, clip = true, cutoff = 0.30f, flat = true } },
            { "eye_R1",    new Slot { tex = Out + "{set}/eye_iris_R_00.png", outline = 0f, clip = true, cutoff = 0.30f, flat = true } },
            { "mat_cheek", new Slot { tex = Tex + "cheek_00.tga",            outline = 0f, transparent = true } },
        };

        class ChanSet
        {
            public string key;
            public string charName;
            public Color rim;
            public Color shade;
        }

        static readonly ChanSet[] Sets =
        {
            new ChanSet { key = "copper",   charName = "리라에",  rim = new Color(1.00f, 0.82f, 0.55f), shade = new Color(0.72f, 0.64f, 0.58f) },
            new ChanSet { key = "moonfire", charName = "세레네",  rim = new Color(0.72f, 0.86f, 1.00f), shade = new Color(0.62f, 0.66f, 0.82f) },
            new ChanSet { key = "winerose", charName = "에이리스", rim = new Color(1.00f, 0.72f, 0.72f), shade = new Color(0.76f, 0.62f, 0.64f) },
        };

        [MenuItem("WuWa/Swap Unity-chan into all members")]
        public static void SwapAllMenu() { Debug.Log(SwapAll()); }

        public static string SwapAll()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Sets.Length; i++) sb.Append("[" + i + "] " + SwapChanIntoMember(i) + "\n");
            // The swap deletes every child of the member root, which takes the spring bones with it.
            // Re-applying here means the two passes cannot drift out of order.
            sb.Append(WuWaChanSpring.AddAll());
            return sb.ToString();
        }

        public static string SwapChanIntoMember(int memberIndex)
        {
            if (memberIndex < 0 || memberIndex >= Sets.Length) return "bad member index";
            var set = Sets[memberIndex];
            string prefabPath = "Assets/WuWa/Prefabs/Member" + memberIndex + ".prefab";

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ChanFbx);
            if (model == null) return "unitychan.fbx missing at " + ChanFbx;
            var modelAnim = model.GetComponent<Animator>();
            if (modelAnim == null || modelAnim.avatar == null || !modelAnim.avatar.isHuman)
                return "unitychan avatar not humanoid";

            var mats = BuildMaterials(set);
            var sb = new System.Text.StringBuilder();

            using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                var root = scope.prefabContentsRoot;

                // keep the root (and its WuWa components) so the scene's TeamManager references survive
                var kids = new List<GameObject>();
                foreach (Transform c in root.transform) kids.Add(c.gameObject);
                foreach (var k in kids) Object.DestroyImmediate(k);
                foreach (var c in root.GetComponents<Component>())
                {
                    if (c == null || c is Transform || c is Animator) continue;
                    if (c.GetType().FullName.StartsWith("WuWa")) continue;
                    Object.DestroyImmediate(c);
                }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.scene);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var moved = new List<Transform>();
                foreach (Transform c in inst.transform) moved.Add(c);
                foreach (var c in moved) c.SetParent(root.transform, false);
                Object.DestroyImmediate(inst);

                var anim = root.GetComponent<Animator>();
                if (anim == null) anim = root.AddComponent<Animator>();
                anim.avatar = modelAnim.avatar;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (anim.runtimeAnimatorController == null)
                    anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        "Assets/WuWa/Anim/Member" + memberIndex + ".controller");

                // re-materialise by the ORIGINAL material name, not by slot index
                int rends = 0, mapped = 0;
                var unmatched = new List<string>();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var cur = r.sharedMaterials;
                    for (int i = 0; i < cur.Length; i++)
                    {
                        string key = cur[i] != null ? cur[i].name : "";
                        Material m;
                        if (mats.TryGetValue(key, out m)) { cur[i] = m; mapped++; }
                        else if (!string.IsNullOrEmpty(key) && !unmatched.Contains(key)) unmatched.Add(key);
                    }
                    r.sharedMaterials = cur;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    var smr = r as SkinnedMeshRenderer;
                    if (smr != null) { smr.updateWhenOffscreen = true; smr.quality = SkinQuality.Bone4; }
                    rends++;
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;

                // real blendshape blink: Unity-chan has EYE_DEF_C on the eye and eyelash meshes, so
                // the old texture-swap / _Blink path is neither needed nor safe here (the mesh has no
                // UV2 eye coordinates, so CharToon's eyelid sweep would smear across the whole body).
                var blink = root.GetComponent<EyeBlink>();
                if (blink == null) blink = root.AddComponent<EyeBlink>();
                blink.openTexture = null;
                blink.closedTexture = null;
                var targets = new List<SkinnedMeshRenderer>();
                int shapeIndex = -1;
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    {
                        if (!smr.sharedMesh.GetBlendShapeName(i).EndsWith("EYE_DEF_C")) continue;
                        targets.Add(smr);
                        shapeIndex = i;
                        break;
                    }
                }
                blink.blendShapeTargets = targets.ToArray();
                blink.blendShapeIndex = shapeIndex;

                var mc = root.GetComponent<MemberConfig>();
                if (mc != null) mc.charName = set.charName;

                sb.Append("set=" + set.key + " name=" + set.charName);
                sb.Append(" renderers=" + rends + " matsMapped=" + mapped);
                sb.Append(" blinkMeshes=" + targets.Count + " shapeIdx=" + shapeIndex);
                sb.Append(" avatar=" + anim.avatar.name + " layer=" + root.layer);
                if (unmatched.Count > 0) sb.Append(" UNMATCHED=" + string.Join("|", unmatched.ToArray()));
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static Dictionary<string, Material> BuildMaterials(ChanSet set)
        {
            string dir = Out + set.key + "/Materials";
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), dir));
            var toon = Shader.Find("WuWa/CharToon");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var result = new Dictionary<string, Material>();

            foreach (var kv in Slots)
            {
                var slot = kv.Value;
                string texPath = slot.tex.Replace("{set}", set.key);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                string matPath = dir + "/" + kv.Key + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                var want = slot.transparent ? lit : toon;
                if (m == null) { m = new Material(want); AssetDatabase.CreateAsset(m, matPath); }
                else if (m.shader != want) m.shader = want;

                if (slot.transparent)
                {
                    m.SetFloat("_Surface", 1f);                       // transparent
                    m.SetFloat("_Blend", 0f);                         // alpha
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    if (tex != null) m.SetTexture("_BaseMap", tex);
                    m.SetColor("_BaseColor", Color.white);
                }
                else
                {
                    if (tex != null) m.SetTexture("_BaseMap", tex);
                    m.SetColor("_BaseColor", Color.white);
                    m.SetColor("_RimColor", set.rim);
                    if (slot.flat)
                    {
                        m.SetColor("_ShadeTint", new Color(0.93f, 0.87f, 0.87f));
                        m.SetColor("_MidTint", new Color(0.98f, 0.95f, 0.94f));
                        m.SetFloat("_StepMid", 0.04f);
                        m.SetFloat("_StepLit", 0.16f);
                        m.SetFloat("_StepSoft", 0.12f);
                        m.SetFloat("_Wrap", 0.65f);
                        m.SetFloat("_AmbientBoost", 0.50f);
                        m.SetFloat("_RimStrength", 0.18f);
                    }
                    else
                    {
                        m.SetColor("_ShadeTint", set.shade);
                        m.SetColor("_MidTint", new Color(0.90f, 0.88f, 0.90f));
                        m.SetFloat("_StepMid", 0.22f);
                        m.SetFloat("_StepLit", 0.48f);
                        m.SetFloat("_StepSoft", 0.05f);
                        m.SetFloat("_Wrap", 0.35f);
                        m.SetFloat("_AmbientBoost", 0.35f);
                        m.SetFloat("_RimStrength", 0.32f);
                    }
                    m.SetFloat("_HairBand", slot.hairBand);
                    m.SetFloat("_HairBandCenter", 1.5f);
                    m.SetFloat("_OutlineWidth", slot.outline);
                    m.SetColor("_OutlineColor", new Color(0.07f, 0.05f, 0.08f, 1f));
                    m.SetFloat("_AlphaClip", slot.clip ? 1f : 0f);
                    m.SetFloat("_Cutoff", slot.cutoff);
                    // no UV2 eye coordinates on this mesh: leave the eyelid sweep fully off
                    m.SetFloat("_Blink", 0f);
                }
                EditorUtility.SetDirty(m);
                result[kv.Key] = m;
            }
            AssetDatabase.SaveAssets();
            return result;
        }
    }
}
