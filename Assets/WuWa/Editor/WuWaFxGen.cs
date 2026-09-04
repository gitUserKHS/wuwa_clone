using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WuWa.EditorTools
{
    /// Bakes FX textures and copies UI icons from the imported icon packs into Resources.
    public static class WuWaFxGen
    {
        const string FxDir = "Assets/WuWa/Resources/FX";
        const string UiDir = "Assets/WuWa/Resources/UI";

        public static void GenerateFxTextures()
        {
            WuWaImportTools.EnsureFolder(FxDir);
            Save(VFXLibrary.MakeSoftDot(), FxDir + "/softdot.png");
            Save(VFXLibrary.MakeStreak(), FxDir + "/streak.png");
            Save(VFXLibrary.MakeRing(), FxDir + "/ring.png");
            AssetDatabase.Refresh();
            foreach (var p in new[] { FxDir + "/softdot.png", FxDir + "/streak.png", FxDir + "/ring.png" })
                MakeSpriteImport(p);
            Debug.Log("[WuWa] FX textures baked");
        }

        static void Save(Texture2D t, string path)
        {
            File.WriteAllBytes(path, t.EncodeToPNG());
        }

        static void MakeSpriteImport(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }

        /// Pick themed icons out of whatever icon packs were imported.
        public static void CopyUiIcons()
        {
            WuWaImportTools.EnsureFolder(UiDir);
            CopyBest("icon_skill", new[] { "sword", "blade", "katana", "saber", "axe" });
            CopyBest("icon_ult", new[] { "star", "crystal", "diamond", "orb", "gem", "fire" });
            CopyBest("icon_echo", new[] { "skull", "claw", "paw", "monster", "rune", "scroll", "potion" });
            AssetDatabase.Refresh();
            Debug.Log("[WuWa] UI icons copied");
        }

        static void CopyBest(string destName, string[] keywords)
        {
            string dest = UiDir + "/" + destName + ".png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(dest) != null) return;

            var candidates = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith("Assets/WuWa") && !p.StartsWith("Assets/unity-chan"))
                .Where(p => (p.ToLowerInvariant().Contains("icon")))
                .ToList();

            string pick = null;
            foreach (var kw in keywords)
            {
                pick = candidates.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains(kw));
                if (pick != null) break;
            }
            if (pick == null) pick = candidates.FirstOrDefault();
            if (pick == null) { Debug.LogWarning("[WuWa] no icon found for " + destName); return; }

            if (!AssetDatabase.CopyAsset(pick, dest))
            {
                Debug.LogWarning("[WuWa] failed to copy " + pick);
                return;
            }
            AssetDatabase.ImportAsset(dest);
            MakeSpriteImport(dest);
            Debug.Log("[WuWa] " + destName + " <= " + pick);
        }

        /// Renders each member prefab's face to a portrait sprite.
        public static void GeneratePortraits()
        {
            WuWaImportTools.EnsureFolder(UiDir);
            for (int i = 0; i < 3; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WuWa/Prefabs/Member" + i + ".prefab");
                if (prefab == null) { Debug.LogWarning("[WuWa] no member prefab " + i); continue; }
                RenderPortrait(prefab, i);
            }
            AssetDatabase.Refresh();
            for (int i = 0; i < 3; i++) MakeSpriteImport(UiDir + "/portrait_" + i + ".png");
            Debug.Log("[WuWa] portraits generated");
        }

        static void RenderPortrait(GameObject prefab, int index)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.position = new Vector3(400f, 300f, 400f);
            inst.transform.rotation = Quaternion.identity;

            Transform head = null;
            var anim = inst.GetComponent<Animator>();
            if (anim != null && anim.isHuman)
            {
                try
                {
                    if (anim.runtimeAnimatorController != null)
                    {
                        anim.Play("Loco", 0, 0f);
                        anim.Update(0.05f);
                    }
                }
                catch { }
                head = anim.GetBoneTransform(HumanBodyBones.Head);
            }
            Vector3 headPos;
            if (head != null) headPos = head.position + Vector3.up * 0.06f;
            else
            {
                var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
                var b = smr != null ? smr.bounds : new Bounds(inst.transform.position + Vector3.up * 1.4f, Vector3.one);
                headPos = new Vector3(b.center.x, b.max.y - b.size.y * 0.09f, b.center.z);
            }

            var mc = inst.GetComponent<MemberConfig>();
            Color bg = mc != null ? Color.Lerp(mc.themeColor, new Color(0.12f, 0.13f, 0.2f), 0.55f) : new Color(0.15f, 0.15f, 0.2f);

            var lightGo = new GameObject("~portraitLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(28f, 160f, 0f);

            var camGo = new GameObject("~portraitCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = bg;
            cam.transform.position = headPos + inst.transform.forward * 0.72f + Vector3.up * 0.02f;
            cam.transform.LookAt(headPos);

            var rt = new RenderTexture(256, 256, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();

            File.WriteAllBytes(UiDir + "/portrait_" + index + ".png", tex.EncodeToPNG());

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(inst);
        }
    }
}
