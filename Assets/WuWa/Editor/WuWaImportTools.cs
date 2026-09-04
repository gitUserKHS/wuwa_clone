using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WuWa.EditorTools
{
    /// Asset Store package import + post-import fixes, driven via Unity CLI.
    public static class WuWaImportTools
    {
        public static string CacheRoot
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Unity", "Asset Store-5.x");
            }
        }

        public static void ImportFromCache(string relativePath)
        {
            string full = Path.Combine(CacheRoot, relativePath);
            if (!File.Exists(full))
            {
                Debug.LogError("[WuWa] package not found: " + full);
                return;
            }
            Debug.Log("[WuWa] importing " + Path.GetFileName(full));
            AssetDatabase.ImportPackage(full, false);
        }

        /// Force humanoid rig on every model that carries animation under the folder.
        public static int ForceHumanoid(string folder, bool includeNonAnimated = false)
        {
            if (!AssetDatabase.IsValidFolder(folder)) { Debug.LogWarning("[WuWa] no folder " + folder); return 0; }
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;
                bool hasAnim = imp.importAnimation && imp.clipAnimations != null;
                if (imp.animationType != ModelImporterAnimationType.Human)
                {
                    imp.animationType = ModelImporterAnimationType.Human;
                    imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    imp.SaveAndReimport();
                    changed++;
                }
            }
            Debug.Log("[WuWa] humanoid forced on " + changed + " models in " + folder);
            return changed;
        }

        /// Convert built-in/legacy materials to URP. Official converter first, manual sweep after.
        public static void ConvertMaterialsToURP()
        {
            try
            {
                UnityEditor.Rendering.Universal.Converters.RunInBatchMode(
                    UnityEditor.Rendering.Universal.ConverterContainerId.BuiltInToURP);
                Debug.Log("[WuWa] URP batch converter finished");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WuWa] URP batch converter unavailable: " + e.Message);
            }
            ManualMaterialSweep();
        }

        static readonly string[] LegacyPrefixes =
        {
            "Standard", "Legacy Shaders/", "Mobile/", "Particles/", "Nature/", "Unlit/Texture", "Toon/"
        };

        public static void ManualMaterialSweep()
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            var urpParticle = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (urpLit == null) { Debug.LogError("[WuWa] URP Lit shader missing"); return; }
            int converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/WuWa")) continue;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                string sn = mat.shader.name;
                bool broken = sn == "Hidden/InternalErrorShader";
                bool legacy = LegacyPrefixes.Any(p => sn.StartsWith(p));
                if (!broken && !legacy) continue;

                Texture main = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Color col = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                bool particle = sn.Contains("Particle");
                mat.shader = particle && urpParticle != null ? urpParticle : urpLit;
                if (main != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", main);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
                EditorUtility.SetDirty(mat);
                converted++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[WuWa] manual material sweep converted " + converted + " materials");
        }

        /// Remove scripts from imported packs that break under the new Input System.
        public static void DeleteAssets(string[] paths)
        {
            foreach (var p in paths)
            {
                if (AssetDatabase.LoadMainAssetAtPath(p) != null || AssetDatabase.IsValidFolder(p))
                {
                    AssetDatabase.DeleteAsset(p);
                    Debug.Log("[WuWa] deleted " + p);
                }
            }
            AssetDatabase.Refresh();
        }

        public static void ListTopFolders()
        {
            foreach (var d in Directory.GetDirectories("Assets"))
                Debug.Log("[WuWa] Assets folder: " + d);
        }

        /// Log every script asset under a folder (to spot legacy scripts fast).
        public static void ListScripts(string folder)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { folder }))
                Debug.Log("[WuWa] script: " + AssetDatabase.GUIDToAssetPath(guid));
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
