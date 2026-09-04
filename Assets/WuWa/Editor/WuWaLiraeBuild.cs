using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WuWa.EditorTools
{
    /// Imports the generated Lirae model (Meshy -> Blender rig, Humanoid) and swaps it into a member
    /// prefab *in place*: the prefab root with its Animator/MemberConfig keeps its fileIDs so the scene's
    /// TeamManager references stay valid; only the visual hierarchy under the root is replaced.
    public static class WuWaLiraeBuild
    {
        // character folder/file stem (Assets/WuWa/Characters/<Char>/<Char>.fbx, <Char>_BaseColor(.png|_Blink.png), <Char>.mat)
        static string Char = "Lirae";
        static string Dir { get { return "Assets/WuWa/Characters/" + Char; } }
        static string ModelPath { get { return Dir + "/" + Char + ".fbx"; } }
        static string TexPath { get { return Dir + "/" + Char + "_BaseColor.png"; } }
        static string MatPath { get { return Dir + "/" + Char + ".mat"; } }
        static string BlinkTexPath { get { return Dir + "/" + Char + "_BaseColor_Blink.png"; } }
        static string LidTexPath { get { return Dir + "/" + Char + "_BaseColor_Lid.png"; } }

        public static string ImportModel(string charStem) { Char = charStem; return ImportModel(); }
        public static string SwapIntoMember(int memberIndex, string charName, string charStem) { Char = charStem; return SwapIntoMember(memberIndex, charName); }

        public static string ImportModel()
        {
            foreach (var tp in new[] { TexPath, BlinkTexPath, LidTexPath })
            {
            var ti = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (ti != null)
            {
                ti.sRGBTexture = true;
                ti.maxTextureSize = 4096;
                ti.npotScale = TextureImporterNPOTScale.None;   // the atlas carries a 256 px hand strip (2048x2304)
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.mipmapEnabled = true;
                ti.streamingMipmaps = false;
                ti.SaveAndReimport();
            }
            }
            var imp = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (imp == null) return "no model importer at " + ModelPath;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.meshCompression = ModelImporterMeshCompression.Off;
            imp.importNormals = ModelImporterNormals.Import;
            imp.importTangents = ModelImporterTangents.CalculateMikk;
            imp.isReadable = false;
            imp.globalScale = 1f;
            imp.useFileScale = true;
            imp.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var anim = model != null ? model.GetComponent<Animator>() : null;
            var av = anim != null ? anim.avatar : null;
            var sb = new System.Text.StringBuilder();
            sb.Append("model=" + (model != null) + " avatar=" + (av != null));
            if (av != null)
            {
                sb.Append(" human=" + av.isHuman + " valid=" + av.isValid);
                var hd = av.humanDescription;
                sb.Append(" mappedBones=" + hd.human.Length);
                var mapped = new HashSet<string>();
                foreach (var hb in hd.human) mapped.Add(hb.humanName);
                foreach (var need in new[] { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand",
                    "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" })
                    if (!mapped.Contains(need)) sb.Append(" MISSING:" + need);
            }
            var smr = model != null ? model.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (smr != null && smr.sharedMesh != null)
                sb.Append(" mesh=" + smr.sharedMesh.vertexCount + "v/" + (smr.sharedMesh.triangles.Length / 3) + "t bounds=" + smr.sharedMesh.bounds.size + " bones=" + smr.bones.Length);
            if (model != null)
            {
                sb.Append(" children=");
                foreach (Transform c in model.transform) sb.Append(c.name + ",");
            }
            return sb.ToString();
        }

        static Material BuildMaterial()
        {
            var sh = Shader.Find("WuWa/CharToon");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            var m = new Material(sh);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            var lidTex = AssetDatabase.LoadAssetAtPath<Texture2D>(LidTexPath);
            if (m.HasProperty("_BlinkMap")) m.SetTexture("_BlinkMap", lidTex != null ? lidTex : tex);
            if (m.HasProperty("_Blink")) m.SetFloat("_Blink", 0f);
            if (m.HasProperty("_ShadeTint")) m.SetColor("_ShadeTint", new Color(0.66f, 0.62f, 0.76f));
            if (m.HasProperty("_MidTint")) m.SetColor("_MidTint", new Color(0.9f, 0.88f, 0.94f));
            if (m.HasProperty("_StepMid")) m.SetFloat("_StepMid", 0.3f);
            if (m.HasProperty("_StepLit")) m.SetFloat("_StepLit", 0.55f);
            if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 0.28f);
            if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0.0022f);
            if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", new Color(0.09f, 0.06f, 0.09f));
            m.name = Char;
            var old = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (old != null)
            {
                old.shader = m.shader;
                old.CopyPropertiesFromMaterial(m);
                EditorUtility.SetDirty(old);
                Object.DestroyImmediate(m);
                return old;
            }
            AssetDatabase.CreateAsset(m, MatPath);
            return m;
        }

        /// Replace the visual hierarchy of Prefabs/Member{index} with the Lirae model, keeping the root.
        public static string SwapIntoMember(int memberIndex, string charName)
        {
            string prefabPath = "Assets/WuWa/Prefabs/Member" + memberIndex + ".prefab";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) return "model missing";
            var modelAnim = model.GetComponent<Animator>();
            if (modelAnim == null || modelAnim.avatar == null || !modelAnim.avatar.isHuman) return "model avatar not humanoid";
            var mat = BuildMaterial();
            var sb = new System.Text.StringBuilder();
            using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                var root = scope.prefabContentsRoot;
                var kids = new List<GameObject>();
                foreach (Transform c in root.transform) kids.Add(c.gameObject);
                sb.Append("removed children=" + kids.Count);
                foreach (var k in kids) Object.DestroyImmediate(k);
                int removedComps = 0;
                foreach (var c in root.GetComponents<Component>())
                {
                    if (c == null || c is Transform || c is Animator) continue;
                    string tn = c.GetType().FullName;
                    if (tn.StartsWith("WuWa")) continue;
                    Object.DestroyImmediate(c); removedComps++;
                }
                sb.Append(" removedRootComps=" + removedComps);

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.scene);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var moved = new List<Transform>();
                foreach (Transform c in inst.transform) moved.Add(c);
                foreach (var c in moved) c.SetParent(root.transform, false);
                Object.DestroyImmediate(inst);
                sb.Append(" moved=" + moved.Count);

                var anim = root.GetComponent<Animator>();
                if (anim == null) anim = root.AddComponent<Animator>();
                anim.avatar = modelAnim.avatar;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (anim.runtimeAnimatorController == null)
                    anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/WuWa/Anim/Member" + memberIndex + ".controller");

                int rends = 0;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    var smr = r as SkinnedMeshRenderer;
                    if (smr != null) { smr.updateWhenOffscreen = true; smr.quality = SkinQuality.Bone4; }
                    rends++;
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;
                // texture-swap blink (eyes live in the albedo)
                var blink = root.GetComponent<EyeBlink>();
                if (blink == null) blink = root.AddComponent<EyeBlink>();
                blink.openTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
                var lid = AssetDatabase.LoadAssetAtPath<Texture2D>(LidTexPath);
                blink.closedTexture = lid != null ? lid : AssetDatabase.LoadAssetAtPath<Texture2D>(BlinkTexPath);
                sb.Append(" blink=" + (blink.closedTexture != null));
                var mc = root.GetComponent<MemberConfig>();
                if (mc != null && !string.IsNullOrEmpty(charName)) mc.charName = charName;
                sb.Append(" renderers=" + rends + " avatar=" + anim.avatar.name + " isHuman=" + anim.isHuman + " layer=" + root.layer + " name=" + (mc != null ? mc.charName : "?"));
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }
    }
}
