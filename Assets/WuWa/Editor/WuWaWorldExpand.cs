using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WuWa.EditorTools
{
    /// M-scale open world: tiled vertex-painted terrain over WorldRegions.HeightAt,
    /// seven biomes, lake + water, village, ruins, dense scatter, towers, orbs,
    /// region triggers and spawners. Idempotent — reruns rebuild everything it owns.
    public static class WuWaWorldExpand
    {
        const int Tiles = 8;               // 8x8 tiles
        const float TileSize = 215f;       // world spans ±860
        const int TileRes = 76;

        static readonly Vector3[] KeepClear =
        {
            new Vector3(0, 0, 0), new Vector3(0, 0, 70), new Vector3(20, 0, -18),
            new Vector3(-60, 0, 122), new Vector3(-60, 0, 215),
            new Vector3(-190, 0, 505), new Vector3(-355, 0, -85),
        };

        static Vector2 V(float x, float z) { return new Vector2(x, z); }

        [MenuItem("WuWa/Repaint Tiles + Water")]
        public static void RepaintTilesOnly()
        {
            WuWaWorldBuild.ClearSceneDirtiness();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var wg = GameObject.Find("WorldGround");
            if (wg != null) Object.DestroyImmediate(wg);
            var wl = GameObject.Find("WaterLake");
            if (wl != null) Object.DestroyImmediate(wl);
            BuildGroundTiles();
            BuildWater();
            // renormalize the lake bridge if the scatter placed one
            var env = GameObject.Find("WorldEnv");
            if (env != null)
            {
                var br = env.transform.Find("LakeBridge");
                if (br == null)
                    foreach (Transform ch in env.transform)
                        if (ch.name.ToLowerInvariant().Contains("bridge")) { br = ch; break; }
                if (br != null)
                {
                    br.position = new Vector3(318f, WorldRegions.WaterY + 0.1f, -94f);
                    var rends = br.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        foreach (var r in rends) b.Encapsulate(r.bounds);
                        float len = Mathf.Max(b.size.x, b.size.z);
                        if (len > 0.01f) br.localScale *= Mathf.Clamp(16f / len, 0.02f, 40f);
                    }
                }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[WuWa] repaint complete");
        }

        [MenuItem("WuWa/Rescatter Only")]
        public static void RescatterOnly()
        {
            WuWaWorldBuild.ClearSceneDirtiness();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var env = GameObject.Find("WorldEnv");
            if (env != null) Object.DestroyImmediate(env);
            BuildScatter();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log("[WuWa] rescatter complete");
        }

        [MenuItem("WuWa/Build Open World")]
        public static void BuildWorldAll()
        {
            WuWaWorldBuild.ClearSceneDirtiness();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.name != "WuWaField")
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/WuWa/Scenes/WuWaField.unity");
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            foreach (var n in new[] { "Ground", "ForestGround", "Environment", "ForestEnv",
                                      "WorldGround", "WorldEnv", "WaterLake", "AmbientFX",
                                      "WRegionTriggers", "WSpawners", "WOrbs", "VillageRoot", "RuinsCity" })
            {
                var go = GameObject.Find(n);
                if (go != null) Object.DestroyImmediate(go);
            }

            System.IO.Directory.CreateDirectory("Assets/WuWa/Art/World");

            BuildGroundTiles();
            BuildWater();
            BuildScatter();
            BuildRuinsCity();
            BuildVillage();
            BuildTowersAndTriggers();
            BuildSpawnersAndOrbs();

            var amb = new GameObject("AmbientFX");
            amb.AddComponent<AmbientFX>();

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 240f;
            RenderSettings.fogEndDistance = 780f;
            RenderSettings.fogColor = new Color(0.72f, 0.80f, 0.90f);
            var cam = Camera.main;
            if (cam != null) cam.farClipPlane = 1100f;

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[WuWa] world expand complete");
        }

        // ================================================================ ground
        static Material GroundMat()
        {
            const string path = "Assets/WuWa/Art/World/GroundToon.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("WuWa/ToonGround");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = shader;
            return mat;
        }

        static void BuildGroundTiles()
        {
            var root = new GameObject("WorldGround").transform;
            var mat = GroundMat();
            float half = Tiles * TileSize * 0.5f;

            for (int tz = 0; tz < Tiles; tz++)
                for (int tx = 0; tx < Tiles; tx++)
                {
                    float ox = -half + tx * TileSize;
                    float oz = -half + tz * TileSize;

                    var verts = new List<Vector3>();
                    var cols = new List<Color>();
                    var norms = new List<Vector3>();
                    var tris = new List<int>();

                    for (int z = 0; z <= TileRes; z++)
                        for (int x = 0; x <= TileRes; x++)
                        {
                            float wx = ox + x / (float)TileRes * TileSize;
                            float wz = oz + z / (float)TileRes * TileSize;
                            float h = WorldRegions.HeightAt(wx, wz);
                            verts.Add(new Vector3(wx, h, wz));
                            norms.Add(WorldRegions.NormalAt(wx, wz));
                            cols.Add(PaintAt(wx, wz, h));
                        }
                    for (int z = 0; z < TileRes; z++)
                        for (int x = 0; x < TileRes; x++)
                        {
                            int a = z * (TileRes + 1) + x;
                            int b = a + TileRes + 1;
                            tris.Add(a); tris.Add(b); tris.Add(a + 1);
                            tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                        }

                    string path = "Assets/WuWa/Art/World/wtile_" + tx + "_" + tz + ".asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    bool fresh = mesh == null;
                    if (fresh) mesh = new Mesh();
                    mesh.Clear();
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.SetVertices(verts);
                    mesh.SetNormals(norms);
                    mesh.SetColors(cols);
                    mesh.SetTriangles(tris, 0);
                    mesh.RecalculateBounds();
                    if (fresh) AssetDatabase.CreateAsset(mesh, path);
                    else EditorUtility.SetDirty(mesh);

                    // physics uses a quarter-resolution copy of the same heightfield —
                    // far cheaper to cook and to hold in memory than the render mesh
                    int colRes = TileRes / 2;
                    var cverts = new List<Vector3>();
                    var ctris = new List<int>();
                    for (int z = 0; z <= colRes; z++)
                        for (int x = 0; x <= colRes; x++)
                            cverts.Add(verts[(z * 2) * (TileRes + 1) + (x * 2)]);
                    for (int z = 0; z < colRes; z++)
                        for (int x = 0; x < colRes; x++)
                        {
                            int a = z * (colRes + 1) + x;
                            int b = a + colRes + 1;
                            ctris.Add(a); ctris.Add(b); ctris.Add(a + 1);
                            ctris.Add(a + 1); ctris.Add(b); ctris.Add(b + 1);
                        }
                    string cpath = "Assets/WuWa/Art/World/wtileC_" + tx + "_" + tz + ".asset";
                    var cmesh = AssetDatabase.LoadAssetAtPath<Mesh>(cpath);
                    bool cfresh = cmesh == null;
                    if (cfresh) cmesh = new Mesh();
                    cmesh.Clear();
                    cmesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    cmesh.SetVertices(cverts);
                    cmesh.SetTriangles(ctris, 0);
                    cmesh.RecalculateBounds();
                    if (cfresh) AssetDatabase.CreateAsset(cmesh, cpath);
                    else EditorUtility.SetDirty(cmesh);

                    var go = new GameObject("wtile_" + tx + "_" + tz);
                    go.transform.SetParent(root);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    go.AddComponent<MeshCollider>().sharedMesh = cmesh;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                }
        }

        static float PathDist(float x, float z) { return WorldRegions.PathDist(x, z); }

        static Color PaintAt(float x, float z, float h) { return WorldRegions.PaintAt(x, z, h); }

        static float S(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // ================================================================ water
        static void BuildWater()
        {
            const string mpath = "Assets/WuWa/Art/World/WaterMat.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mpath);
            var shader = Shader.Find("WuWa/AnimeWater");
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, mpath); }
            else mat.shader = shader;
            mat.SetFloat("_WaveScale", 0.45f);
            mat.SetColor("_FoamColor", new Color(0.85f, 1f, 1f, 0.55f));

            int res = 46;
            float size = 480f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                    verts.Add(new Vector3(390f + (x / (float)res - 0.5f) * size, WorldRegions.WaterY,
                                          -100f + (z / (float)res - 0.5f) * size));
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int a = z * (res + 1) + x;
                    int b = a + res + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            const string wpath = "Assets/WuWa/Art/World/WaterMesh.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(wpath);
            bool fresh = mesh == null;
            if (fresh) mesh = new Mesh();
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (fresh) AssetDatabase.CreateAsset(mesh, wpath);

            var go = new GameObject("WaterLake");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ================================================================ scatter
        class Pools
        {
            public List<GameObject> trees = new List<GameObject>();
            public List<GameObject> pines = new List<GameObject>();
            public List<GameObject> deadTrees = new List<GameObject>();
            public List<GameObject> deadPines = new List<GameObject>();
            public List<GameObject> rocks = new List<GameObject>();
            public List<GameObject> menhir = new List<GameObject>();
            public List<GameObject> grass = new List<GameObject>();
            public List<GameObject> flowers = new List<GameObject>();
            public List<GameObject> shrubs = new List<GameObject>();
            public List<GameObject> deadShrubs = new List<GameObject>();
            public List<GameObject> shrooms = new List<GameObject>();
            public List<GameObject> logs = new List<GameObject>();
            public GameObject bridge;
            public List<GameObject> fabTrees = new List<GameObject>();
            public List<GameObject> fabShrooms = new List<GameObject>();
        }

        static Pools LoadPools()
        {
            var p = new Pools();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Polytope Studio" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string n = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || go.GetComponentInChildren<MeshRenderer>() == null) continue;
                if (n.Contains("stump") || n.Contains("_cut")) continue;
                if (n.Contains("fruit_tree") && n.Contains("dead")) p.deadTrees.Add(go);
                else if (n.Contains("pine") && n.Contains("dead")) p.deadPines.Add(go);
                else if (n.Contains("fruit_tree")) p.trees.Add(go);
                else if (n.Contains("pine")) p.pines.Add(go);
                else if (n.Contains("menhir")) p.menhir.Add(go);
                else if (n.Contains("rock")) p.rocks.Add(go);
                else if (n.Contains("high_grass")) p.grass.Add(go);
                else if (n.Contains("grass")) p.grass.Add(go);
                else if (n.Contains("poppy")) p.flowers.Add(go);
                else if (n.Contains("shrub") && n.Contains("dead")) p.deadShrubs.Add(go);
                else if (n.Contains("shrub")) p.shrubs.Add(go);
                else if (n.Contains("mushroom")) p.shrooms.Add(go);
                else if (n.Contains("logs")) p.logs.Add(go);
                else if (n.Contains("bridge")) p.bridge = go;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ithappy" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string n = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || go.GetComponentInChildren<MeshRenderer>() == null) continue;
                if (n.Contains("fabulous_tree") || n == "big_fabulous_tree_001") p.fabTrees.Add(go);
                else if (n.Contains("fabulous_mushroom")) p.fabShrooms.Add(go);
            }
            return p;
        }

        static System.Random _rng;
        static Transform _envRoot;

        static bool SpotOk(float x, float z, float pathClear, float maxSlope, bool allowShore)
        {
            if (PathDist(x, z) < pathClear) return false;
            if (WorldRegions.NormalAt(x, z).y < 1f - maxSlope) return false;
            float h = WorldRegions.HeightAt(x, z);
            if (!allowShore && h < WorldRegions.WaterY + 0.35f) return false;
            if (allowShore && h < WorldRegions.WaterY - 0.15f) return false;
            foreach (var k in KeepClear)
                if (Vector2.Distance(new Vector2(x, z), new Vector2(k.x, k.z)) < 14f) return false;
            if (WorldRegions.VillageM(x, z) > 0.35f) return false;    // village decorates itself
            return true;
        }

        static void Scatter(List<GameObject> pool, int count, Vector2 center, float radius,
            System.Func<float, float, float> maskFn, float maskMin,
            float sMin, float sMax, float pathClear = 4.5f, float maxSlope = 0.24f, bool allowShore = false)
        {
            if (pool == null || pool.Count == 0) return;
            int placed = 0, guard = 0;
            while (placed < count && guard < count * 14)
            {
                guard++;
                float ang = (float)_rng.NextDouble() * Mathf.PI * 2f;
                float rad = Mathf.Sqrt((float)_rng.NextDouble()) * radius;
                float x = center.x + Mathf.Cos(ang) * rad;
                float z = center.y + Mathf.Sin(ang) * rad;
                if (Mathf.Abs(x) > 840f || Mathf.Abs(z) > 840f) continue;
                if (maskFn != null && maskFn(x, z) < maskMin) continue;
                if (!SpotOk(x, z, pathClear, maxSlope, allowShore)) continue;

                var prefab = pool[_rng.Next(pool.Count)];
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _envRoot);
                inst.transform.position = new Vector3(x, WorldRegions.HeightAt(x, z) - 0.06f, z);
                inst.transform.rotation = Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f);
                inst.transform.localScale *= Mathf.Lerp(sMin, sMax, (float)_rng.NextDouble());
                GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic);
                placed++;
            }
        }

        static void BuildScatter()
        {
            _rng = new System.Random(20260831);
            _envRoot = new GameObject("WorldEnv").transform;
            var p = LoadPools();

            // 녹야 평원 — fruit trees, shrubs, rocks, grass, poppies
            System.Func<float, float, float> plainsM = (x, z) =>
                (1f - S(200f, 260f, Mathf.Sqrt(x * x + z * z)))
                * (1f - WorldRegions.ForestM(x, z)) * (1f - WorldRegions.RuinsM(x, z));
            Scatter(p.trees, 260, V(0, 0), 250f, plainsM, 0.5f, 0.95f, 1.55f);
            Scatter(p.shrubs, 120, V(0, 0), 250f, plainsM, 0.5f, 0.9f, 1.6f);
            Scatter(p.rocks, 45, V(0, 0), 250f, plainsM, 0.5f, 0.8f, 1.8f);
            Scatter(p.grass, 650, V(0, 0), 250f, plainsM, 0.5f, 1.0f, 1.8f, 2.6f);
            Scatter(p.flowers, 170, V(0, 0), 250f, plainsM, 0.5f, 1.0f, 1.7f, 2.6f);

            // 속삭임 숲 — dense mixed woods + giants + mushrooms
            Scatter(p.trees, 240, V(-60, 210), 125f, WorldRegions.ForestM, 0.55f, 1.0f, 1.7f);
            Scatter(p.pines, 140, V(-60, 210), 125f, WorldRegions.ForestM, 0.55f, 1.0f, 1.8f);
            Scatter(p.trees, 12, V(-60, 210), 115f, WorldRegions.ForestM, 0.6f, 2.4f, 3.2f);
            Scatter(p.shrooms, 70, V(-60, 210), 125f, WorldRegions.ForestM, 0.55f, 1.0f, 1.9f, 2.6f);
            Scatter(p.grass, 110, V(-60, 210), 125f, WorldRegions.ForestM, 0.55f, 1.0f, 1.8f, 2.6f);

            // 노을빛 언덕 — the pink lands
            Scatter(p.fabTrees, 185, V(340, 330), 220f, WorldRegions.BloomM, 0.5f, 0.9f, 1.6f);
            Scatter(p.fabShrooms, 65, V(340, 330), 220f, WorldRegions.BloomM, 0.5f, 0.9f, 1.7f, 2.8f);
            Scatter(p.flowers, 150, V(340, 330), 220f, WorldRegions.BloomM, 0.5f, 1.1f, 1.9f, 2.6f);
            Scatter(p.grass, 120, V(340, 330), 220f, WorldRegions.BloomM, 0.5f, 1.0f, 1.7f, 2.6f);

            // 거울 호수 — shore rocks, reeds, far-shore pines, a wooden bridge
            Scatter(p.rocks, 60, V(390, -100), 215f, WorldRegions.LakeM, 0.35f, 0.9f, 2.0f, 4.5f, 0.3f, true);
            Scatter(p.grass, 150, V(390, -100), 215f, WorldRegions.LakeM, 0.35f, 1.1f, 2.0f, 2.6f, 0.3f, true);
            Scatter(p.pines, 45, V(470, -180), 130f, WorldRegions.LakeM, 0.3f, 1.0f, 1.7f);
            if (p.bridge != null)
            {
                var br = (GameObject)PrefabUtility.InstantiatePrefab(p.bridge, _envRoot);
                br.name = "LakeBridge";
                float bx = 318f, bz = -94f;
                br.transform.position = new Vector3(bx, WorldRegions.WaterY + 0.1f, bz);
                br.transform.rotation = Quaternion.Euler(0f, 96f, 0f);
                var rends = br.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    float len = Mathf.Max(b.size.x, b.size.z);
                    if (len > 0.01f) br.transform.localScale *= Mathf.Clamp(16f / len, 0.02f, 40f);
                }
                GameObjectUtility.SetStaticEditorFlags(br, StaticEditorFlags.BatchingStatic);
            }

            // 잿빛 황무지 — dead woods, ore, logs, lone menhirs
            Scatter(p.deadTrees, 105, V(-360, -80), 230f, WorldRegions.WasteM, 0.5f, 1.0f, 1.8f);
            Scatter(p.deadPines, 85, V(-360, -80), 230f, WorldRegions.WasteM, 0.5f, 1.0f, 1.9f);
            Scatter(p.deadShrubs, 60, V(-360, -80), 230f, WorldRegions.WasteM, 0.5f, 0.9f, 1.7f);
            Scatter(p.rocks, 55, V(-360, -80), 230f, WorldRegions.WasteM, 0.5f, 1.0f, 2.3f);
            Scatter(p.logs, 25, V(-360, -80), 230f, WorldRegions.WasteM, 0.5f, 1.0f, 1.6f);
            Scatter(p.menhir, 8, V(-360, -80), 200f, WorldRegions.WasteM, 0.55f, 1.2f, 2.0f);

            // 서리 고원 — pine highlands + a stone ring at the summit clearing
            Scatter(p.pines, 230, V(-190, 500), 245f, WorldRegions.FrostM, 0.5f, 1.0f, 1.9f);
            Scatter(p.rocks, 65, V(-190, 500), 245f, WorldRegions.FrostM, 0.5f, 1.0f, 2.2f);
            Scatter(p.shrubs, 40, V(-190, 500), 245f, WorldRegions.FrostM, 0.5f, 0.9f, 1.4f);
            for (int i = 0; i < 9; i++)
            {
                if (p.menhir.Count == 0) break;
                float a = i / 9f * Mathf.PI * 2f;
                float mx = -190f + Mathf.Cos(a) * 14f, mz = 520f + Mathf.Sin(a) * 14f;
                var mh = (GameObject)PrefabUtility.InstantiatePrefab(p.menhir[i % p.menhir.Count], _envRoot);
                mh.transform.position = new Vector3(mx, WorldRegions.HeightAt(mx, mz) - 0.1f, mz);
                mh.transform.rotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg + 90f, 0f);
                mh.transform.localScale *= 1.6f;
                GameObjectUtility.SetStaticEditorFlags(mh, StaticEditorFlags.BatchingStatic);
            }

            // 노래잃은 도시 주변 — moss rocks and withered brush
            Scatter(p.menhir, 20, V(90, -360), 205f, WorldRegions.RuinsM, 0.45f, 1.0f, 1.8f);
            Scatter(p.rocks, 35, V(90, -360), 205f, WorldRegions.RuinsM, 0.45f, 0.9f, 1.9f);
            Scatter(p.deadShrubs, 35, V(90, -360), 205f, WorldRegions.RuinsM, 0.45f, 0.9f, 1.5f);

            // 세계의 등뼈 — sparse hardy pines and boulders on the rim slopes
            System.Func<float, float, float> rimLow = (x, z) =>
                WorldRegions.RimM(x, z) > 0.05f && WorldRegions.RimM(x, z) < 0.7f ? 1f : 0f;
            Scatter(p.pines, 120, V(0, 0), 780f, rimLow, 0.5f, 1.1f, 2.0f, 3f, 0.4f);
            Scatter(p.rocks, 90, V(0, 0), 780f, rimLow, 0.5f, 1.2f, 2.6f, 3f, 0.5f);
        }

        // ================================================================ ruins city
        static Material RuinMat()
        {
            const string path = "Assets/WuWa/Art/World/RuinStone.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", new Color(0.62f, 0.63f, 0.60f));
            mat.SetFloat("_Smoothness", 0.05f);
            return mat;
        }

        static void BuildRuinsCity()
        {
            var root = new GameObject("RuinsCity").transform;
            var mat = RuinMat();
            var rng = new System.Random(777);

            System.Action<Vector3, Vector3, float> block = (pos, size, yaw) =>
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "ruin";
                b.transform.SetParent(root);
                float h = WorldRegions.HeightAt(pos.x, pos.z);
                b.transform.position = new Vector3(pos.x, h + size.y * 0.5f - 0.25f, pos.z);
                b.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                b.transform.localScale = size;
                b.GetComponent<MeshRenderer>().sharedMaterial = mat;
                GameObjectUtility.SetStaticEditorFlags(b, StaticEditorFlags.BatchingStatic);
            };

            // broken colonnade avenue leading to a shattered plaza
            for (int i = 0; i < 7; i++)
            {
                float z = -300f - i * 16f;
                float hL = 5.5f + (float)rng.NextDouble() * 4f;
                float hR = 5.5f + (float)rng.NextDouble() * 4f;
                if (i != 3) block(new Vector3(78f, 0f, z), new Vector3(1.7f, hL, 1.7f), (float)rng.NextDouble() * 8f);
                if (i != 5) block(new Vector3(102f, 0f, z), new Vector3(1.7f, hR, 1.7f), (float)rng.NextDouble() * 8f);
            }
            // fallen slabs + shattered walls
            for (int i = 0; i < 14; i++)
            {
                float x = 30f + (float)rng.NextDouble() * 120f;
                float z = -430f + (float)rng.NextDouble() * 120f;
                block(new Vector3(x, 0f, z),
                    new Vector3(2.5f + (float)rng.NextDouble() * 4f, 0.8f + (float)rng.NextDouble() * 1.4f, 4f + (float)rng.NextDouble() * 5f),
                    (float)rng.NextDouble() * 360f);
            }
            for (int i = 0; i < 6; i++)
            {
                float x = 40f + (float)rng.NextDouble() * 100f;
                float z = -420f + (float)rng.NextDouble() * 100f;
                block(new Vector3(x, 0f, z),
                    new Vector3(9f + (float)rng.NextDouble() * 6f, 6f + (float)rng.NextDouble() * 5f, 1.1f),
                    (float)rng.NextDouble() * 360f);
            }
            // two tall wall-run faces flanking the plaza
            block(new Vector3(60f, 0f, -372f), new Vector3(16f, 11f, 1.2f), 12f);
            block(new Vector3(120f, 0f, -368f), new Vector3(16f, 11f, 1.2f), -9f);

            // grapple hooks across the city
            GrapplePoint.Build(new Vector3(90f, WorldRegions.HeightAt(90f, -330f) + 9f, -330f)).transform.SetParent(root);
            GrapplePoint.Build(new Vector3(64f, WorldRegions.HeightAt(64f, -376f) + 12f, -376f)).transform.SetParent(root);
            GrapplePoint.Build(new Vector3(118f, WorldRegions.HeightAt(118f, -392f) + 10f, -392f)).transform.SetParent(root);
        }

        // ================================================================ village
        static GameObject PlaceProp(string nameContains, Vector2 pos, float yaw, float targetHeight, Transform parent)
        {
            GameObject prefab = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ithappy", "Assets/Polytope Studio" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string n = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (n == nameContains.ToLowerInvariant() || (prefab == null && n.Contains(nameContains.ToLowerInvariant())))
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (n == nameContains.ToLowerInvariant()) break;
                }
            }
            if (prefab == null) return null;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            inst.transform.position = new Vector3(pos.x, 0f, pos.y);
            inst.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // normalize wildly different pack scales by bounds height
            var rends = inst.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0 && targetHeight > 0f)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                if (b.size.y > 0.01f)
                    inst.transform.localScale *= Mathf.Clamp(targetHeight / b.size.y, 0.02f, 60f);
            }
            inst.transform.position = new Vector3(pos.x, WorldRegions.HeightAt(pos.x, pos.y) - 0.03f, pos.y);
            GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic);
            return inst;
        }

        static void BuildVillage()
        {
            var root = new GameObject("VillageRoot").transform;
            Vector2 c = new Vector2(-215f, -165f);

            PlaceProp("house_001", c + new Vector2(-16f, 14f), 155f, 6.5f, root);
            PlaceProp("house_002", c + new Vector2(14f, 18f), 195f, 6.0f, root);
            PlaceProp("house_003", c + new Vector2(22f, -8f), 262f, 6.8f, root);
            PlaceProp("stall_001", c + new Vector2(-8f, -12f), 40f, 3.2f, root);
            PlaceProp("stall_table_001", c + new Vector2(-2f, -16f), 220f, 1.1f, root);
            PlaceProp("cart_001", c + new Vector2(8f, -20f), 285f, 2.0f, root);
            PlaceProp("pointer_001", c + new Vector2(-24f, -20f), 130f, 2.4f, root);
            PlaceProp("crane_001", c + new Vector2(30f, 10f), 320f, 5.0f, root);

            var rng = new System.Random(313);
            string[] clutter = { "barrel_001", "box_001", "box_002", "box_003", "jug_001", "jug_003", "bag_001", "bucket_001", "log_002" };
            for (int i = 0; i < 22; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 6f + (float)rng.NextDouble() * 22f;
                PlaceProp(clutter[rng.Next(clutter.Length)],
                    c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r),
                    (float)rng.NextDouble() * 360f, 0.7f + (float)rng.NextDouble() * 0.6f, root);
            }

            // fence ring with a gap toward the road (east side)
            for (int i = 0; i < 26; i++)
            {
                float a = i / 26f * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 20f)) < 26f) continue;   // gate gap
                Vector2 fp = c + new Vector2(Mathf.Cos(a) * 38f, Mathf.Sin(a) * 38f);
                var f = PlaceProp("modular_fence_wood_01", fp, a * Mathf.Rad2Deg + 90f, 1.5f, root);
                if (f == null) break;
            }

            // warm village lamp
            var lamp = new GameObject("villageLamp");
            lamp.transform.SetParent(root);
            lamp.transform.position = new Vector3(c.x, WorldRegions.HeightAt(c.x, c.y) + 3.4f, c.y);
            var l = lamp.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.82f, 0.55f);
            l.range = 26f;
            l.intensity = 2.4f;
        }

        // ================================================================ gameplay wiring
        static void BuildTowersAndTriggers()
        {
            if (GameObject.Find("Tower_2") == null)
            {
                var t = ResonanceTower.Build(new Vector3(-190f, WorldRegions.HeightAt(-190f, 505f), 505f), 2, "서리 고원 공명탑");
                t.gameObject.name = "Tower_2";
            }
            if (GameObject.Find("Tower_3") == null)
            {
                var t = ResonanceTower.Build(new Vector3(-355f, WorldRegions.HeightAt(-355f, -85f), -85f), 3, "잿빛 공명탑");
                t.gameObject.name = "Tower_3";
            }

            var root = new GameObject("WRegionTriggers").transform;
            System.Action<int, string, float, float, float> trig = (id, name, x, z, r) =>
            {
                var go = new GameObject("RT_" + name);
                go.transform.SetParent(root);
                go.transform.position = new Vector3(x, WorldRegions.HeightAt(x, z), z);
                var rt = go.AddComponent<RegionTrigger>();
                rt.regionId = id;
                rt.regionName = name;
                rt.checkRadius = r;
            };
            trig(2, "노을빛 언덕", 305f, 295f, 30f);
            trig(3, "거울 호수", 330f, -90f, 32f);
            trig(4, "잿빛 황무지", -325f, -95f, 32f);
            trig(5, "서리 고원", -180f, 448f, 30f);
            trig(6, "노래잃은 도시", 86f, -320f, 28f);
            trig(7, "메아리 마을", -206f, -158f, 24f);
        }

        static void BuildSpawnersAndOrbs()
        {
            var root = new GameObject("WSpawners").transform;
            System.Action<string, float, float, float> sp = (prefab, x, z, mul) =>
            {
                var pgo = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WuWa/Prefabs/" + prefab + ".prefab");
                if (pgo == null) return;
                var s = new GameObject("WSpawn_" + prefab).AddComponent<EnemySpawner>();
                s.transform.SetParent(root);
                s.transform.position = new Vector3(x, WorldRegions.HeightAt(x, z) + 0.2f, z);
                s.enemyPrefab = pgo;
                s.respawnDelay = 38f;
                s.statMul = mul;
            };
            // 잿빛 황무지 2.2×
            sp("EnemyMob", -330f, -140f, 2.2f); sp("EnemyMob", -400f, -60f, 2.2f);
            sp("EnemyFast", -370f, -140f, 2.2f); sp("EnemyTank", -420f, -110f, 2.2f);
            sp("EnemyRanged", -350f, -20f, 2.2f);
            // 서리 고원 2.5×
            sp("EnemyTank", -150f, 490f, 2.5f); sp("EnemyTank", -240f, 530f, 2.5f);
            sp("EnemyRanged", -200f, 560f, 2.5f); sp("EnemyFast", -130f, 540f, 2.5f);
            sp("EnemyMob", -250f, 470f, 2.5f);
            // 거울 호수 1.9×
            sp("EnemyMob", 330f, -170f, 1.9f); sp("EnemyFast", 430f, -180f, 1.9f);
            sp("EnemyRanged", 460f, -50f, 1.9f);
            // 노래잃은 도시 2.0×
            sp("EnemyFast", 60f, -340f, 2.0f); sp("EnemyTank", 110f, -370f, 2.0f);
            sp("EnemyMob", 80f, -410f, 2.0f); sp("EnemyRanged", 130f, -330f, 2.0f);
            sp("EnemyFast", 45f, -390f, 2.0f);
            // 노을빛 언덕 1.8×
            sp("EnemyMob", 300f, 350f, 1.8f); sp("EnemyFast", 380f, 300f, 1.8f);
            sp("EnemyMob", 350f, 400f, 1.8f);

            // exploration reward orbs (echo caches)
            var oroot = new GameObject("WOrbs").transform;
            System.Action<float, float, int> orb = (x, z, echoId) =>
            {
                var def = EchoDB.Get(echoId);
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "EchoCache";
                go.transform.SetParent(oroot);
                go.layer = Layers.Pickup;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.position = new Vector3(x, WorldRegions.HeightAt(x, z) + 1.1f, z);
                go.transform.localScale = Vector3.one * 0.55f;
                var mr = go.GetComponent<MeshRenderer>();
                var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                m.SetTexture("_BaseMap", VFXLibrary.MakeSoftDot());
                Color col = def != null ? Color.Lerp(def.Tint, Color.white, 0.3f) : new Color(0.8f, 0.55f, 1f);
                m.SetColor("_BaseColor", col * 2.2f);
                mr.sharedMaterial = m;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = col;
                l.intensity = 2.8f;
                l.range = 6f;
                var o = go.AddComponent<EchoOrb>();
                o.echoId = echoId;
            };
            orb(360f, -70f, 2); orb(452f, -140f, 3);          // lake shore
            orb(-190f, 522f, 3); orb(-155f, 470f, 2);          // frost ring
            orb(-395f, -95f, 1); orb(-330f, -45f, 3);          // waste
            orb(90f, -362f, 0); orb(64f, -400f, 4);            // ruins plaza
            orb(330f, 320f, 1); orb(390f, 355f, 2);            // bloom clearing
        }

        // ================================================================ M5: waystones, chests, balance
        [MenuItem("WuWa/M5 Apply (warp+chests)")]
        public static void M5Apply()
        {
            WuWaWorldBuild.ClearSceneDirtiness();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.name != "WuWaField")
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/WuWa/Scenes/WuWaField.unity");
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            foreach (var n in new[] { "WaystonesRoot", "ChestsRoot" })
            {
                var old = GameObject.Find(n);
                if (old != null) Object.DestroyImmediate(old);
            }

            var wsRoot = new GameObject("WaystonesRoot").transform;
            System.Action<float, float, int, string> stone = (x, z, id, name) =>
            {
                var w = Waystone.Build(new Vector3(x, WorldRegions.HeightAt(x, z), z), id, name);
                w.transform.SetParent(wsRoot);
            };
            stone(-212f, -158f, 0, "메아리 마을 표석");
            stone(330f, -84f, 1, "거울 호수 표석");
            stone(330f, 318f, 2, "노을빛 언덕 표석");
            stone(84f, -322f, 3, "노래잃은 도시 표석");

            var chRoot = new GameObject("ChestsRoot").transform;
            int cid = 0;
            System.Action<float, float, int> chest = (x, z, tier) =>
            {
                var c = TreasureChest.Build(new Vector3(x, WorldRegions.HeightAt(x, z), z), cid++, tier);
                c.transform.SetParent(chRoot);
            };
            // 녹야 평원
            chest(48f, 62f, 0); chest(-95f, 45f, 0); chest(70f, -95f, 0); chest(-42f, -120f, 0);
            // 속삭임 숲
            chest(-120f, 255f, 0); chest(-15f, 265f, 0); chest(-62f, 298f, 1);
            // 노을빛 언덕
            chest(280f, 352f, 0); chest(400f, 290f, 0); chest(352f, 408f, 1); chest(428f, 350f, 1);
            // 거울 호수
            chest(300f, -162f, 0); chest(470f, -32f, 0); chest(452f, -180f, 1); chest(336f, -96f, 2);
            // 잿빛 황무지
            chest(-300f, -162f, 0); chest(-428f, -42f, 0); chest(-388f, -142f, 1); chest(-330f, 18f, 1);
            // 서리 고원
            chest(-122f, 468f, 1); chest(-258f, 542f, 1); chest(-190f, 524f, 2);
            // 노래잃은 도시
            chest(46f, -332f, 1); chest(92f, -392f, 2);
            // 마을 · 세계의 등뼈
            chest(-232f, -150f, 0); chest(-556f, 138f, 2);

            // balance pass: the boss hits the level curve a little harder
            var bossGo = GameObject.Find("BossSpawner");
            if (bossGo != null)
            {
                var bs = bossGo.GetComponent<EnemySpawner>();
                if (bs != null && bs.statMul < 1.14f) bs.statMul = 1.15f;
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log("[WuWa] M5 apply complete: stones=4 chests=" + cid);
        }

        // ================================================================ M4: map bake, layers, systems, boot scene
        [MenuItem("WuWa/M4 Apply (map+perf+systems)")]
        public static void M4Apply()
        {
            WuWaWorldBuild.ClearSceneDirtiness();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.name != "WuWaField")
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/WuWa/Scenes/WuWaField.unity");
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            // 1) low-res colliders (rebuilds tiles; paint already linear)
            var wg = GameObject.Find("WorldGround");
            if (wg != null) Object.DestroyImmediate(wg);
            BuildGroundTiles();

            // 2) world map texture
            var mapTex = BakeWorldMap();

            // 3) distance-cull layers on decoration
            AssignDecoLayers();

            // 4) scene systems
            var sys = GameObject.Find("Systems");
            if (sys == null) sys = new GameObject("Systems");
            if (sys.GetComponent<SaveSystem>() == null) sys.AddComponent<SaveSystem>();
            if (sys.GetComponent<CombatScore>() == null) sys.AddComponent<CombatScore>();
            if (sys.GetComponent<PerfTuner>() == null) sys.AddComponent<PerfTuner>();
            var map = Object.FindAnyObjectByType<MapSystem>();
            if (map == null)
            {
                var mgo = new GameObject("MapSystem");
                map = mgo.AddComponent<MapSystem>();
            }
            map.worldMap = mapTex;
            EditorUtility.SetDirty(map);

            // 5) shadow distance for the open world
            var urp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline
                as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp != null) urp.shadowDistance = 90f;

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            // 6) boot scene + build settings (opens other scenes, so field is saved first)
            BuildBootScene();

            AssetDatabase.SaveAssets();
            Debug.Log("[WuWa] M4 apply complete");
        }

        static Texture2D BakeWorldMap()
        {
            const int res = 768;
            var tex = new Texture2D(res, res, TextureFormat.RGB24, true);
            var px = new Color[res * res];
            var sun = new Vector3(0.45f, 0.75f, 0.35f).normalized;
            for (int y = 0; y < res; y++)
            {
                float wz = (y / (float)(res - 1) - 0.5f) * WorldRegions.WorldHalf * 2f;
                for (int x = 0; x < res; x++)
                {
                    float wx = (x / (float)(res - 1) - 0.5f) * WorldRegions.WorldHalf * 2f;
                    float h = WorldRegions.HeightAt(wx, wz);
                    Color c = PaintAt(wx, wz, h).gamma;          // back to display-space
                    if (h < WorldRegions.WaterY - 0.05f)
                        c = Color.Lerp(c, new Color(0.15f, 0.34f, 0.45f), 0.8f);
                    float light = Mathf.Clamp01(Vector3.Dot(WorldRegions.NormalAt(wx, wz), sun));
                    c *= 0.72f + light * 0.42f;
                    px[y * res + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            const string path = "Assets/WuWa/Art/World/WorldMap.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void AssignDecoLayers()
        {
            int big = 0, small = 0;
            foreach (var rootName in new[] { "WorldEnv", "ForestEnv", "RuinsCity", "VillageRoot" })
            {
                var root = GameObject.Find(rootName);
                if (root == null) continue;
                foreach (Transform child in root.transform)
                {
                    if (child.GetComponent<GrapplePoint>() != null) continue;    // gameplay marker, never cull
                    var rends = child.GetComponentsInChildren<Renderer>();
                    if (rends.Length == 0) continue;
                    var b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    int layer = b.size.y < 1.3f ? PerfTuner.SmallDecoLayer : PerfTuner.BigDecoLayer;
                    foreach (var t in child.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                    if (layer == PerfTuner.BigDecoLayer) big++; else small++;
                }
            }
            Debug.Log("[WuWa] deco layers: big=" + big + " small=" + small);
        }

        static void BuildBootScene()
        {
            var boot = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var camGo = new GameObject("BootCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.045f, 0.06f, 0.075f);
            camGo.AddComponent<AudioListener>();

            var canvasGo = new GameObject("BootCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            System.Func<string, string, int, Color, Vector2, Vector2, UnityEngine.UI.Text> mkTxt =
                (n, txt, size, col, pos, dim) =>
                {
                    var go = new GameObject(n);
                    go.transform.SetParent(canvasGo.transform, false);
                    var rt = go.AddComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = pos;
                    rt.sizeDelta = dim;
                    var t = go.AddComponent<UnityEngine.UI.Text>();
                    t.font = font;
                    t.text = txt;
                    t.fontSize = size;
                    t.color = col;
                    t.alignment = TextAnchor.MiddleCenter;
                    return t;
                };

            var title = mkTxt("title", "잔  향", 84, new Color(1f, 0.92f, 0.7f), new Vector2(0f, 90f), new Vector2(800f, 120f));
            title.fontStyle = FontStyle.Bold;
            mkTxt("sub", "— 노래가 사라진 세계 —", 22, new Color(1f, 1f, 1f, 0.55f), new Vector2(0f, 20f), new Vector2(700f, 40f));
            var progText = mkTxt("prog", "", 18, new Color(1f, 1f, 1f, 0.75f), new Vector2(0f, -110f), new Vector2(700f, 30f));

            var trackGo = new GameObject("track");
            trackGo.transform.SetParent(canvasGo.transform, false);
            var trt = trackGo.AddComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(0f, -60f);
            trt.sizeDelta = new Vector2(560f, 10f);
            var trackImg = trackGo.AddComponent<UnityEngine.UI.Image>();
            trackImg.color = new Color(1f, 1f, 1f, 0.12f);

            var fillGo = new GameObject("fill");
            fillGo.transform.SetParent(trackGo.transform, false);
            var frt = fillGo.AddComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<UnityEngine.UI.Image>();
            fillImg.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            fillImg.color = new Color(1f, 0.82f, 0.4f);
            fillImg.type = UnityEngine.UI.Image.Type.Filled;
            fillImg.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fillImg.fillAmount = 0f;

            var loaderGo = new GameObject("BootLoader");
            var loader = loaderGo.AddComponent<BootLoader>();
            loader.progressFill = fillImg;
            loader.progressText = progText;
            loader.targetScene = "WuWaField";

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(boot, "Assets/WuWa/Scenes/WuWaBoot.unity");

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/WuWa/Scenes/WuWaBoot.unity", true),
                new EditorBuildSettingsScene("Assets/WuWa/Scenes/WuWaField.unity", true),
            };

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/WuWa/Scenes/WuWaField.unity");
        }
    }
}
