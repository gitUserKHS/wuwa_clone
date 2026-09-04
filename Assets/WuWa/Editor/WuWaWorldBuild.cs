using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace WuWa.EditorTools
{
    /// Assembles the playable open-field scene out of the imported nature packs.
    public static class WuWaWorldBuild
    {
        const string ScenePath = "Assets/WuWa/Scenes/WuWaField.unity";
        const string ArtDir = "Assets/WuWa/Art";
        static readonly Vector2 ArenaCenter = new Vector2(0f, 70f);

        // ---------------------------------------------------------------- layers
        public static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            SetLayer(layers, 8, "Player");
            SetLayer(layers, 9, "Enemy");
            SetLayer(layers, 10, "Pickup");
            tagManager.ApplyModifiedProperties();
            Debug.Log("[WuWa] layers ensured");
        }

        static void SetLayer(SerializedProperty layers, int index, string name)
        {
            var el = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(el.stringValue)) el.stringValue = name;
            else if (el.stringValue != name) Debug.LogWarning("[WuWa] layer " + index + " already " + el.stringValue);
        }

        // ---------------------------------------------------------------- ground
        static float HeightAt(float x, float z)
        {
            float h = Mathf.PerlinNoise(x * 0.015f + 31.7f, z * 0.015f + 11.3f) * 6.5f;
            h += Mathf.PerlinNoise(x * 0.05f + 3.1f, z * 0.05f + 7.7f) * 1.4f;
            // flatten spawn + boss arena
            h *= FlattenMask(new Vector2(x, z), Vector2.zero, 18f, 30f);
            h *= FlattenMask(new Vector2(x, z), ArenaCenter, 24f, 36f);
            return h;
        }

        static float FlattenMask(Vector2 p, Vector2 c, float inner, float outer)
        {
            float d = Vector2.Distance(p, c);
            if (d <= inner) return 0.06f;
            if (d >= outer) return 1f;
            float t = (d - inner) / (outer - inner);
            return Mathf.Lerp(0.06f, 1f, t * t * (3f - 2f * t));
        }

        public static GameObject BuildGround()
        {
            int res = 110;
            float size = 240f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                {
                    float fx = (x / (float)res - 0.5f) * size;
                    float fz = (z / (float)res - 0.5f) * size;
                    verts.Add(new Vector3(fx, HeightAt(fx, fz), fz));
                    uvs.Add(new Vector2(x / (float)res * 40f, z / (float)res * 40f));
                }
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int a = z * (res + 1) + x;
                    int b = a + res + 1;
                    tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            WuWaImportTools.EnsureFolder(ArtDir);
            AssetDatabase.CreateAsset(mesh, ArtDir + "/GroundMesh.asset");

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.35f, 0.57f, 0.30f));
            mat.SetFloat("_Smoothness", 0.04f);
            AssetDatabase.CreateAsset(mat, ArtDir + "/GroundMat.mat");

            var go = new GameObject("Ground");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
            return go;
        }

        // ---------------------------------------------------------------- scatter
        class PropPool
        {
            public List<GameObject> trees = new List<GameObject>();
            public List<GameObject> rocks = new List<GameObject>();
            public List<GameObject> plants = new List<GameObject>();
            public List<GameObject> props = new List<GameObject>();
        }

        static PropPool CollectProps()
        {
            var pool = new PropPool();
            var roots = System.IO.Directory.GetDirectories("Assets")
                .Where(d =>
                {
                    string n = System.IO.Path.GetFileName(d).ToLowerInvariant();
                    return n.Contains("polytope") || n.Contains("fantasy") || n.Contains("ithappy") || n.Contains("lowpoly") || n.Contains("low poly");
                })
                .Select(d => d.Replace('\\', '/'))
                .ToArray();
            Debug.Log("[WuWa] prop roots: " + string.Join(", ", roots));
            if (roots.Length == 0) return pool;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", roots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string n = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                if (go.GetComponentInChildren<MeshRenderer>() == null) continue;
                if (n.Contains("scene") || n.Contains("demo") || n.Contains("showcase")) continue;

                if (Has(n, "tree", "pine", "fir", "birch", "oak", "spruce", "willow")) pool.trees.Add(go);
                else if (Has(n, "rock", "stone", "boulder", "cliff")) pool.rocks.Add(go);
                else if (Has(n, "bush", "grass", "flower", "fern", "plant", "mushroom", "shrub", "reed", "lily")) pool.plants.Add(go);
                else if (Has(n, "tower", "ruin", "pillar", "column", "arch", "crystal", "statue", "well", "windmill", "house", "hut", "camp", "tent", "bridge", "gate")) pool.props.Add(go);
            }
            Debug.Log(string.Format("[WuWa] props — trees:{0} rocks:{1} plants:{2} props:{3}",
                pool.trees.Count, pool.rocks.Count, pool.plants.Count, pool.props.Count));
            return pool;
        }

        static bool Has(string s, params string[] keys) { return keys.Any(s.Contains); }

        static void Scatter(Transform parent, List<GameObject> pool, System.Random rng, int count,
            float rMin, float rMax, float sMin, float sMax, bool avoidCorridor)
        {
            if (pool.Count == 0) return;
            int placed = 0, guard = 0;
            while (placed < count && guard++ < count * 12)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = Mathf.Lerp(rMin, rMax, Mathf.Sqrt((float)rng.NextDouble()));
                float x = Mathf.Cos(ang) * rad;
                float z = Mathf.Sin(ang) * rad;
                if (Vector2.Distance(new Vector2(x, z), Vector2.zero) < rMin) continue;
                if (Vector2.Distance(new Vector2(x, z), ArenaCenter) < 27f) continue;
                if (avoidCorridor && Mathf.Abs(x) < 9f && z > -4f && z < ArenaCenter.y) continue;

                var prefab = pool[rng.Next(pool.Count)];
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                float y = HeightAt(x, z);
                inst.transform.position = new Vector3(x, y - 0.06f, z);
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float s = Mathf.Lerp(sMin, sMax, (float)rng.NextDouble());
                inst.transform.localScale *= s;
                GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic);
                placed++;
            }
        }

        public static void ScatterEnvironment()
        {
            var pool = CollectProps();
            var rng = new System.Random(2077);
            var envRoot = new GameObject("Environment").transform;

            Scatter(envRoot, pool.trees, rng, 120, 22f, 105f, 0.9f, 1.6f, true);
            Scatter(envRoot, pool.rocks, rng, 55, 20f, 105f, 0.8f, 1.7f, false);
            Scatter(envRoot, pool.plants, rng, 240, 16f, 100f, 0.9f, 1.5f, false);
            Scatter(envRoot, pool.props, rng, 12, 45f, 100f, 0.9f, 1.3f, true);

            // stone ring landmark around the boss arena
            var ringSrc = pool.rocks.Count > 0 ? pool.rocks : pool.props;
            if (ringSrc.Count > 0)
            {
                for (int i = 0; i < 12; i++)
                {
                    float a = i / 12f * Mathf.PI * 2f;
                    float x = ArenaCenter.x + Mathf.Cos(a) * 25f;
                    float z = ArenaCenter.y + Mathf.Sin(a) * 25f;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(ringSrc[i % ringSrc.Count], envRoot);
                    inst.transform.position = new Vector3(x, HeightAt(x, z) - 0.1f, z);
                    inst.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                    inst.transform.localScale *= 1.6f;
                    GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic);
                }
            }
        }

        // ---------------------------------------------------------------- sky & post
        static void SetupSkyAndLight()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.87f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42f, -142f, 0f);

            var skyShader = Shader.Find("WuWa/AnimeSky");
            if (skyShader != null)
            {
                var sky = new Material(skyShader);
                sky.SetVector("_SunDir", -sun.transform.forward);
                AssetDatabase.CreateAsset(sky, ArtDir + "/AnimeSkyMat.mat");
                RenderSettings.skybox = sky;
            }
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.64f, 0.76f, 0.98f);
            RenderSettings.ambientEquatorColor = new Color(0.80f, 0.83f, 0.86f);
            RenderSettings.ambientGroundColor = new Color(0.38f, 0.42f, 0.46f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 185f;
            RenderSettings.fogColor = new Color(0.72f, 0.84f, 0.99f);
        }

        static void SetupPost()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ArtDir + "/WuWaPost.asset");

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.65f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.6f);

            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.24f);
            vig.smoothness.Override(0.45f);

            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.12f);
            ca.saturation.Override(14f);
            ca.contrast.Override(8f);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);

            EditorUtility.SetDirty(profile);

            var volGo = new GameObject("PostVolume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        // ---------------------------------------------------------------- assembly
        /// Silently drop unsaved modifications on open scenes so scripted scene
        /// switches never raise the "Scene(s) Have Been Modified" dialog.
        public static void ClearSceneDirtiness()
        {
            var m = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (sc.isDirty && m != null) m.Invoke(null, new object[] { sc });
            }
        }

        public static void BuildScene()
        {
            EnsureLayers();
            WuWaImportTools.EnsureFolder("Assets/WuWa/Scenes");
            WuWaImportTools.EnsureFolder(ArtDir);
            ClearSceneDirtiness();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildGround();
            SetupSkyAndLight();
            SetupPost();
            ScatterEnvironment();

            // ---- player party
            var player = new GameObject("Player");
            player.layer = LayerMask.NameToLayer("Player");
            float py = HeightAt(0f, 0f);
            player.transform.position = new Vector3(0f, py + 0.25f, 0f);

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.34f;
            cc.center = new Vector3(0f, 0.92f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.45f;

            var team = player.AddComponent<TeamManager>();
            player.AddComponent<LockOnSystem>();
            player.AddComponent<PlayerController>();
            player.AddComponent<PlayerCombat>();

            var memberList = new List<MemberConfig>();
            for (int i = 0; i < 3; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WuWa/Prefabs/Member" + i + ".prefab");
                if (prefab == null) { Debug.LogError("[WuWa] missing member prefab " + i); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                memberList.Add(inst.GetComponent<MemberConfig>());
            }
            team.members = memberList.ToArray();

            // ---- camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 420f;
            camGo.AddComponent<AudioListener>();
            var tpc = camGo.AddComponent<ThirdPersonCamera>();
            tpc.target = player.transform;
            camGo.transform.position = player.transform.position + new Vector3(0f, 2.4f, -4.5f);
            var extra = cam.GetUniversalAdditionalCameraData();
            extra.renderPostProcessing = true;
            extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // ---- systems
            new GameObject("GameDirector").AddComponent<GameDirector>();
            new GameObject("HUD").AddComponent<HUDController>();

            // ---- enemies
            var mob = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WuWa/Prefabs/EnemyMob.prefab");
            var boss = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WuWa/Prefabs/EnemyBoss.prefab");
            var spawnRoot = new GameObject("Spawners").transform;
            Vector2[] posts =
            {
                new Vector2(11f, 14f), new Vector2(-10f, 17f), new Vector2(19f, 27f),
                new Vector2(-21f, 33f), new Vector2(24f, 47f), new Vector2(-16f, 52f),
                new Vector2(6f, 40f)
            };
            foreach (var p in posts)
            {
                var sp = new GameObject("Spawner").AddComponent<EnemySpawner>();
                sp.transform.SetParent(spawnRoot);
                sp.transform.position = new Vector3(p.x, HeightAt(p.x, p.y) + 0.2f, p.y);
                sp.enemyPrefab = mob;
                sp.respawnDelay = 30f;
            }
            var bossSp = new GameObject("BossSpawner").AddComponent<EnemySpawner>();
            bossSp.transform.SetParent(spawnRoot);
            bossSp.transform.position = new Vector3(ArenaCenter.x, HeightAt(ArenaCenter.x, ArenaCenter.y) + 0.2f, ArenaCenter.y);
            bossSp.enemyPrefab = boss;
            bossSp.bossPost = true;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[WuWa] scene saved: " + ScenePath);
        }

        /// One-shot pipeline after packages are imported and rigs are fixed.
        public static void BuildGameAll()
        {
            ClearSceneDirtiness();
            WuWaFxGen.GenerateFxTextures();
            WuWaFxGen.CopyUiIcons();
            WuWaAnimBuild.BuildAll();
            WuWaCharBuild.BuildMemberPrefabs();
            WuWaCharBuild.BuildEnemyPrefabs();
            WuWaFxGen.GeneratePortraits();
            BuildScene();
            Debug.Log("[WuWa] BuildGameAll complete");
        }
    }
}
