using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WuWa.EditorTools
{
    /// Writes the action asset from WuWaInputSpec (deterministic GUIDs) and the
    /// graphics reference asset the settings applier needs at runtime.
    public static class WuWaInputBuild
    {
        public const string AssetPath = "Assets/WuWa/Resources/Input/WuWaInput.inputactions";
        public const string RefsPath = "Assets/WuWa/Resources/WuWaGraphicsRefs.asset";

        [MenuItem("WuWa/Input/Generate Action Asset")]
        public static void Generate()
        {
            WuWaImportTools.EnsureFolder("Assets/WuWa/Resources");
            WuWaImportTools.EnsureFolder("Assets/WuWa/Resources/Input");
            string json = WuWaInputSpec.ToJson("WuWaInput");
            File.WriteAllText(AssetPath, json);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(AssetPath);
            Debug.Log("[WuWa] input asset " + (asset != null ? "ok: maps=" + asset.actionMaps.Count : "FAILED to import") + " (" + json.Length + " chars)");
        }

        [MenuItem("WuWa/Input/Create Graphics Refs")]
        public static void CreateGraphicsRefs()
        {
            WuWaImportTools.EnsureFolder("Assets/WuWa/Resources");
            var refs = AssetDatabase.LoadAssetAtPath<WuWaGraphicsRefs>(RefsPath);
            if (refs == null)
            {
                refs = ScriptableObject.CreateInstance<WuWaGraphicsRefs>();
                AssetDatabase.CreateAsset(refs, RefsPath);
            }
            refs.urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            refs.renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            refs.post = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/WuWa/Art/WuWaPost.asset");
            EditorUtility.SetDirty(refs);
            AssetDatabase.SaveAssets();
            Debug.Log("[WuWa] graphics refs: urp=" + (refs.urp != null) + " renderer=" + (refs.renderer != null) + " post=" + (refs.post != null));
        }
    }
}
