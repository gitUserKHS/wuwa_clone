using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WuWa.EditorTools
{
    /// v4 quality pass: real post-processing profile, cutout foliage shading,
    /// render settings. Every step is idempotent and GUID-stable.
    public static class WuWaQualityPass
    {
        const string PostPath = "Assets/WuWa/Art/WuWaPost.asset";
        const string ScenePath = "Assets/WuWa/Scenes/WuWaField.unity";

        static void EnsureFieldScene()
        {
            WuWaWorldBuild.ClearSceneDirtiness();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.name != "WuWaField")
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
        }

        static void SaveField()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        // ================================================================ step 1
        [MenuItem("WuWa/Quality/1 Post + Foliage + Render Settings")]
        public static void ApplyStep1()
        {
            EnsureFieldScene();
            RebuildPost();
            int n = ConvertFoliageMaterials();
            ApplyRenderSettings();
            SaveField();
            Debug.Log("[WuWa] quality step1 complete: foliage mats=" + n);
        }

        // ================================================================ step 2
        [MenuItem("WuWa/Quality/2 World FX (grass, day-night, critters)")]
        public static void ApplyStep2()
        {
            EnsureFieldScene();
            var fx = GameObject.Find("WorldFX");
            if (fx == null) fx = new GameObject("WorldFX");
            if (fx.GetComponent<DayNightCycle>() == null) fx.AddComponent<DayNightCycle>();
            var grass = fx.GetComponent<GrassField>();
            if (grass == null) grass = fx.AddComponent<GrassField>();
            var critters = fx.GetComponent<Critters>();
            if (critters == null) critters = fx.AddComponent<Critters>();

            // runtime-only shaders must be referenced (or always-included) or the player build drops them
            grass.bladeShader = Shader.Find("WuWa/GrassBlade");
            critters.critterShader = Shader.Find("WuWa/Critter");
            EditorUtility.SetDirty(grass);
            EditorUtility.SetDirty(critters);
            EnsureAlwaysIncluded(new[] { "WuWa/GrassBlade", "WuWa/Critter", "WuWa/ToonFoliage", "WuWa/ToonGround", "WuWa/AnimeWater", "WuWa/AnimeSky",
                                         "Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Simple Lit" });

            // water: refresh the material so the new shader properties get sane values
            var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/WuWa/Art/World/WaterMat.mat");
            if (water != null)
            {
                water.shader = Shader.Find("WuWa/AnimeWater");
                water.SetFloat("_WaveScale", 0.45f);
                water.SetColor("_FoamColor", new Color(0.85f, 1f, 1f, 0.55f));
                water.SetColor("_HorizonColor", new Color(0.72f, 0.86f, 1f, 1f));
                water.SetFloat("_FoamWidth", 1.4f);
                water.SetFloat("_SpecPower", 160f);
                EditorUtility.SetDirty(water);
            }
            var ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/WuWa/Art/World/GroundToon.mat");
            if (ground != null)
            {
                ground.shader = Shader.Find("WuWa/ToonGround");
                ground.SetFloat("_DetailStrength", 0.5f);
                ground.SetFloat("_StrataStrength", 0.5f);
                EditorUtility.SetDirty(ground);
            }
            SaveField();
            Debug.Log("[WuWa] quality step2 complete");
        }

        // ================================================================ S1: input + settings foundation
        [MenuItem("WuWa/Quality/S1 Input + Settings")]
        public static void ApplyS1()
        {
            WuWaInputBuild.Generate();
            WuWaInputBuild.CreateGraphicsRefs();
            Debug.Log("[WuWa] S1 assets ready");
        }

        [MenuItem("WuWa/Quality/S2 UI Framework (UIRoot)")]
        public static void ApplyS2()
        {
            var go = GameObject.Find("UIRoot");
            if (go == null) go = new GameObject("UIRoot");
            if (go.GetComponent<UIRoot>() == null) go.AddComponent<UIRoot>();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(go.scene);
            Debug.Log("[WuWa] S2 applied: UIRoot in scene " + go.scene.name);
        }

        [MenuItem("WuWa/Quality/S3 Map (input asset + 4096 bake)")]
        public static void ApplyS3()
        {
            WuWaInputBuild.Generate();
            WuWaMapBake.Bake(4096);
            Debug.Log("[WuWa] S3 applied");
        }

        [MenuItem("WuWa/Quality/S4 Items (day length 44)")]
        public static void ApplyS4()
        {
            var dn = Object.FindAnyObjectByType<DayNightCycle>();
            if (dn != null)
            {
                dn.dayLengthMinutes = 44f;
                EditorUtility.SetDirty(dn);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(dn.gameObject.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(dn.gameObject.scene);
            }
            Debug.Log("[WuWa] S4 applied: day length " + (dn != null ? dn.dayLengthMinutes : 0f) + " min");
        }

        [MenuItem("WuWa/Quality/S5 Growth (scene cleanup)")]
        public static void ApplyS5()
        {
            int removed = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log("[WuWa] S5 applied: removed " + removed + " missing script(s)");
        }

        [MenuItem("WuWa/Quality/S6 Quests (input asset)")]
        public static void ApplyS6()
        {
            WuWaInputBuild.Generate();
            Debug.Log("[WuWa] S6 applied: input asset regenerated (Menu/Quest, Menu/Codex)");
        }

        [MenuItem("WuWa/Quality/S7 Release (version, input asset)")]
        public static void ApplyS7()
        {
            PlayerSettings.bundleVersion = "0.6.0";
            WuWaInputBuild.Generate();
            AssetDatabase.SaveAssets();
            Debug.Log("[WuWa] S7 applied: version " + PlayerSettings.bundleVersion);
        }

        // ================================================================ step 3: content
        [MenuItem("WuWa/Quality/3 Content (registry, arena, NPCs, rifts)")]
        public static void ApplyStep3()
        {
            EnsureFieldScene();
            var fx = GameObject.Find("WorldFX");
            if (fx == null) fx = new GameObject("WorldFX");
            var reg = fx.GetComponent<EnemyRegistry>();
            if (reg == null) reg = fx.AddComponent<EnemyRegistry>();
            reg.prefabs = new[] { LoadPrefab("EnemyMob"), LoadPrefab("EnemyFast"), LoadPrefab("EnemyRanged"), LoadPrefab("EnemyTank"), LoadPrefab("EnemyBoss") };
            EditorUtility.SetDirty(reg);
            if (fx.GetComponent<RiftDirector>() == null) fx.AddComponent<RiftDirector>();

            // ---- trial altar on the east plain
            var oldArena = GameObject.Find("ArenaAltar");
            if (oldArena != null) Object.DestroyImmediate(oldArena);
            var oldTrig = GameObject.Find("RT_시련의 제단");
            if (oldTrig != null) Object.DestroyImmediate(oldTrig);
            Vector3 ac = new Vector3(165f, 0f, -150f);
            float hMax = float.MinValue, hMin = float.MaxValue;
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2f;
                for (float r = 0f; r <= 26f; r += 4f)
                {
                    float h = WorldRegions.HeightAt(ac.x + Mathf.Cos(a) * r, ac.z + Mathf.Sin(a) * r);
                    hMax = Mathf.Max(hMax, h); hMin = Mathf.Min(hMin, h);
                }
            }
            ac.y = hMax + 0.35f;
            ClearDecoAround(ac, 32f);
            var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/WuWa/Art/World/AltarStone.mat");
            if (stone == null)
            {
                stone = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
                AssetDatabase.CreateAsset(stone, "Assets/WuWa/Art/World/AltarStone.mat");
            }
            stone.SetColor("_BaseColor", new Color(0.40f, 0.42f, 0.50f));
            stone.SetFloat("_Smoothness", 0.12f);
            EditorUtility.SetDirty(stone);
            var trim = AssetDatabase.LoadAssetAtPath<Material>("Assets/WuWa/Art/World/AltarTrim.mat");
            if (trim == null)
            {
                trim = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
                AssetDatabase.CreateAsset(trim, "Assets/WuWa/Art/World/AltarTrim.mat");
            }
            trim.SetColor("_BaseColor", new Color(0.55f, 0.62f, 0.72f));
            trim.EnableKeyword("_EMISSION");
            trim.SetColor("_EmissionColor", new Color(0.25f, 0.5f, 0.7f) * 0.6f);
            EditorUtility.SetDirty(trim);
            var trial = ArenaTrial.Build(ac, stone, trim);
            // foundation down to the lowest ground + a ramp from the west
            float depth = Mathf.Max(1.5f, ac.y - hMin + 1.5f);
            ArenaTrial.FlatCylinder("foundation", trial.transform, new Vector3(0f, -0.85f - depth * 0.5f, 0f),
                new Vector3(trial.platformRadius * 2f + 4f, depth * 0.5f, trial.platformRadius * 2f + 4f), stone);
            float rampLen = 16f;
            Vector3 rampFoot = ac + new Vector3(-(trial.platformRadius + rampLen), 0f, 0f);
            rampFoot.y = WorldRegions.HeightAt(rampFoot.x, rampFoot.z);
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "ramp";
            ramp.transform.SetParent(trial.transform, false);
            Vector3 top = ac + new Vector3(-trial.platformRadius + 1f, -0.25f, 0f);
            ramp.transform.position = (top + rampFoot) * 0.5f;
            Vector3 dir = top - rampFoot;
            ramp.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
            ramp.transform.localScale = new Vector3(dir.magnitude + 2f, 0.5f, 7f);
            ramp.GetComponent<MeshRenderer>().sharedMaterial = stone;
            foreach (var t in trial.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = false;

            var trig = new GameObject("RT_시련의 제단");
            trig.transform.position = ac;
            var rt = trig.AddComponent<RegionTrigger>();
            rt.regionId = 9;
            rt.regionName = "시련의 제단";
            rt.checkRadius = 30f;

            // ---- NPCs
            BuildNpc("NPC_Keeper", "제단지기 로엔", NpcRole.Keeper, 2, ac + new Vector3(-trial.platformRadius - rampLen - 5f, 0f, 6f), 90f,
                new Color(0.75f, 0.78f, 0.9f), new Color(0.55f, 0.5f, 0.75f), 1);
            Vector2 vc = new Vector2(-215f, -165f);
            BuildNpc("NPC_Merchant", "상인 마르타", NpcRole.Merchant, 0, new Vector3(vc.x - 8f, 0f, vc.y - 9f), 150f,
                new Color(0.9f, 0.7f, 0.45f), new Color(0.95f, 0.75f, 0.45f), 2);
            BuildNpc("NPC_Villager", "마을 아이 피오", NpcRole.Villager, 1, new Vector3(vc.x + 13f, 0f, vc.y + 5f), 220f,
                new Color(1f, 0.75f, 0.85f), new Color(0.6f, 0.9f, 0.75f), 0);

            // ---- lake bridge reads as wood instead of blank white
            var env = GameObject.Find("WorldEnv");
            if (env != null)
            {
                var br = env.transform.Find("LakeBridge");
                if (br != null)
                    foreach (var r in br.GetComponentsInChildren<Renderer>())
                        foreach (var m in r.sharedMaterials)
                            if (m != null && m.HasProperty("_BaseColor"))
                            {
                                m.SetColor("_BaseColor", new Color(0.58f, 0.42f, 0.28f));
                                EditorUtility.SetDirty(m);
                            }
            }

            SaveField();
            Debug.Log("[WuWa] quality step3 complete: arena y=" + ac.y.ToString("F1") + " (ground " + hMin.ToString("F1") + ".." + hMax.ToString("F1") + ")");
        }

        static void EnsureAlwaysIncluded(string[] shaderNames)
        {
            var gfx = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gfx == null || gfx.Length == 0) return;
            var so = new SerializedObject(gfx[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return;
            int added = 0;
            foreach (var n in shaderNames)
            {
                var sh = Shader.Find(n);
                if (sh == null) continue;
                bool present = false;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { present = true; break; }
                if (present) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                added++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[WuWa] always-included shaders +" + added);
        }

        static GameObject LoadPrefab(string n)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WuWa/Prefabs/" + n + ".prefab");
        }

        static void ClearDecoAround(Vector3 c, float radius)
        {
            int n = 0;
            foreach (var rootName in new[] { "WorldEnv", "Environment", "ForestEnv" })
            {
                var root = GameObject.Find(rootName);
                if (root == null) continue;
                var kill = new List<GameObject>();
                foreach (Transform ch in root.transform)
                    if (WuWaUtil.Flat(ch.position - c).magnitude < radius) kill.Add(ch.gameObject);
                foreach (var k in kill) { Object.DestroyImmediate(k); n++; }
            }
            Debug.Log("[WuWa] cleared " + n + " decorations around " + c);
        }

        /// Talkable villager built from a party member's model, recolored.
        static GameObject BuildNpc(string goName, string npcName, NpcRole role, int id, Vector3 pos, float yaw, Color hair, Color cloth, int memberIndex)
        {
            var old = GameObject.Find(goName);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(goName);
            pos.y = WorldRegions.HeightAt(pos.x, pos.z);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var npc = root.AddComponent<NPC>();
            npc.npcName = npcName;
            npc.role = role;
            npc.npcId = id;

            var prefab = LoadPrefab("Member" + memberIndex);
            if (prefab != null)
            {
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                rig.name = "rig";
                rig.transform.localPosition = Vector3.zero;
                rig.transform.localRotation = Quaternion.identity;
                foreach (var c in rig.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null) continue;
                    if (c.GetType().FullName.Contains("Spring")) continue;
                    Object.DestroyImmediate(c);
                }
                foreach (var col in rig.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                RecolorRig(rig, hair, cloth, "npc" + id);
            }

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.name = "marker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(0f, 2.35f, 0f);
            marker.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            marker.transform.localScale = Vector3.one * 0.2f;
            var mm = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            mm.SetColor("_BaseColor", new Color(1f, 0.85f, 0.4f));
            mm.EnableKeyword("_EMISSION");
            mm.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.3f) * 1.4f);
            marker.GetComponent<MeshRenderer>().sharedMaterial = mm;

            var cap = root.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.9f, 0f);
            cap.height = 1.8f;
            cap.radius = 0.35f;
            return root;
        }

        static void RecolorRig(GameObject rig, Color hair, Color cloth, string subdir)
        {
            string dir = "Assets/WuWa/Art/Materials/" + subdir;
            WuWaImportTools.EnsureFolder(dir);
            var cache = new Dictionary<Material, Material>();
            foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    string n = src.name.ToLowerInvariant();
                    bool skip = n.Contains("face") || n.Contains("eye") || n.Contains("skin") || n.Contains("cheek") || n.Contains("tel") || n.Contains("alpha") || n.Contains("class");
                    if (skip) continue;
                    Material rep;
                    if (!cache.TryGetValue(src, out rep))
                    {
                        Color tint = n.Contains("hair") ? hair : cloth;
                        rep = new Material(src);
                        foreach (var prop in new[] { "_BaseColor", "_Color", "_1st_ShadeColor", "_2nd_ShadeColor" })
                            if (rep.HasProperty(prop)) rep.SetColor(prop, rep.GetColor(prop) * tint);
                        rep.name = subdir + "_" + src.name;
                        string path = dir + "/" + rep.name + ".mat";
                        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (existing != null)
                        {
                            existing.shader = rep.shader;
                            existing.CopyPropertiesFromMaterial(rep);
                            Object.DestroyImmediate(rep);
                            rep = existing;
                            EditorUtility.SetDirty(existing);
                        }
                        else AssetDatabase.CreateAsset(rep, path);
                        cache[src] = rep;
                    }
                    mats[i] = rep;
                }
                r.sharedMaterials = mats;
            }
        }

        // ---------------------------------------------------------------- post
        public static VolumeProfile RebuildPost()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, PostPath);
            }
            // drop stale sub-assets (an earlier build left null entries behind)
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(PostPath))
                if (sub is VolumeComponent) Object.DestroyImmediate(sub, true);
            profile.components.Clear();

            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.62f);
            bloom.tint.Override(new Color(1f, 0.96f, 0.9f));
            bloom.highQualityFiltering.Override(true);

            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);

            var ca = Add<ColorAdjustments>(profile);
            ca.postExposure.Override(0.18f);
            ca.contrast.Override(12f);
            ca.saturation.Override(14f);

            var split = Add<SplitToning>(profile);
            split.shadows.Override(new Color(0.56f, 0.62f, 0.80f));
            split.highlights.Override(new Color(1f, 0.95f, 0.85f));
            split.balance.Override(-8f);

            var vig = Add<Vignette>(profile);
            vig.intensity.Override(0.21f);
            vig.smoothness.Override(0.42f);
            vig.color.Override(new Color(0.04f, 0.05f, 0.09f));

            var flare = Add<ScreenSpaceLensFlare>(profile);
            flare.intensity.Override(0.28f);
            flare.bloomMip.Override(2);
            flare.streaksIntensity.Override(0.15f);
            flare.chromaticAbberationIntensity.Override(0.15f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var vol = Object.FindAnyObjectByType<Volume>();
            if (vol == null) vol = new GameObject("PostVolume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
            EditorUtility.SetDirty(vol);
            Debug.Log("[WuWa] post profile rebuilt: " + profile.components.Count + " components");
            return profile;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        // ---------------------------------------------------------------- foliage
        static bool IsFoliageName(string n)
        {
            n = n.ToLowerInvariant();
            return n.Contains("foliage") || n.Contains("leaf") || n.Contains("leaves")
                || n.Contains("grass") || n.Contains("poppy") || n.Contains("flower");
        }

        public static int ConvertFoliageMaterials()
        {
            var shader = Shader.Find("WuWa/ToonFoliage");
            if (shader == null) { Debug.LogError("[WuWa] ToonFoliage shader missing"); return 0; }
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Polytope Studio" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || !IsFoliageName(mat.name)) continue;
                var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
                if (tex == null && mat.HasProperty("_BaseTexture")) tex = mat.GetTexture("_BaseTexture");
                if (tex == null && mat.HasProperty("_MainTex")) tex = mat.GetTexture("_MainTex");
                if (tex == null) continue;

                string n = mat.name.ToLowerInvariant();
                bool ground = n.Contains("grass") || n.Contains("poppy") || n.Contains("flower");
                mat.shader = shader;
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Cutoff", 0.42f);
                mat.SetFloat("_UvHeightMask", ground ? 1f : 0f);
                mat.SetFloat("_WindStrength", ground ? 0.42f : 0.14f);
                mat.SetFloat("_WindSpeed", ground ? 1.6f : 1.2f);
                mat.SetFloat("_BottomDarken", ground ? 0.28f : 0f);
                mat.SetFloat("_Translucency", n.Contains("pine") ? 0.22f : 0.38f);
                mat.SetFloat("_Wrap", n.Contains("pine") ? 0.35f : 0.5f);
                mat.renderQueue = (int)RenderQueue.AlphaTest;
                EditorUtility.SetDirty(mat);
                count++;
                Debug.Log("[WuWa] foliage: " + mat.name + " <- " + tex.name);
            }
            AssetDatabase.SaveAssets();
            return count;
        }

        // ---------------------------------------------------------------- render settings
        public static void ApplyRenderSettings()
        {
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urp.shadowDistance = 120f;
                urp.shadowCascadeCount = 4;
                urp.cascade4Split = new Vector3(0.06f, 0.18f, 0.45f);
                urp.supportsHDR = true;
                urp.msaaSampleCount = 1;
                EditorUtility.SetDirty(urp);
            }
            var cam = Camera.main;
            if (cam != null)
            {
                var extra = cam.GetUniversalAdditionalCameraData();
                extra.renderPostProcessing = true;
                extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                extra.antialiasingQuality = AntialiasingQuality.High;
                extra.dithering = true;
                EditorUtility.SetDirty(cam);
            }
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.shadowStrength = 0.86f;
                sun.shadowNormalBias = 0.6f;
                sun.shadowBias = 0.08f;
                EditorUtility.SetDirty(sun);
            }
        }
    }
}
