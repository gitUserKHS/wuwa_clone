using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WuWa.EditorTools
{
    /// High-resolution stylised world map bake (paper + ink): biome paint,
    /// hillshade, contours, water + coastline, roads, canopy stipple, region
    /// borders, snow hatching, paper grain. One HeightAt per pixel.
    public static class WuWaMapBake
    {
        const string Path = "Assets/WuWa/Art/World/WorldMap.png";

        [MenuItem("WuWa/Map/Bake 2048")] static void Bake2048() { Bake(2048); }
        [MenuItem("WuWa/Map/Bake 4096")] static void Bake4096() { Bake(4096); }

        struct Stipple { public float x, z, r; public int kind; public float sx, sz; }   // kind 0 fruit 1 pine 2 fab 3 dead 4 rock 5 building

        public static Texture2D Bake(int res)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float half = WorldRegions.WorldHalf;
            float size = half * 2f;
            float mpp = size / res;

            // ---- scene stipples (main thread)
            var stip = new List<Stipple>();
            foreach (var rootName in new[] { "WorldEnv", "VillageRoot", "RuinsCity", "ForestEnv", "Environment" })
            {
                var root = GameObject.Find(rootName);
                if (root == null) continue;
                foreach (Transform ch in root.transform)
                {
                    string n = ch.name.ToLower();
                    int kind = -1;
                    if (n.Contains("logs")) continue;
                    if (n.Contains("fruit_tree")) kind = n.Contains("dead") ? 3 : 0;
                    else if (n.Contains("pine_tree")) kind = n.Contains("dead") ? 3 : 1;
                    else if (n.Contains("fabulous_tree")) kind = 2;
                    else if (n.Contains("rock") || n.Contains("menhir")) kind = 4;
                    else if (n.Contains("house") || n.Contains("ruin") || n.Contains("bridge")) kind = 5;
                    if (kind < 0) continue;
                    var rends = ch.GetComponentsInChildren<Renderer>();
                    if (rends.Length == 0) continue;
                    var b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    stip.Add(new Stipple { x = b.center.x, z = b.center.z, r = Mathf.Max(b.extents.x, b.extents.z) * (kind == 5 ? 1f : 0.85f), kind = kind, sx = b.extents.x, sz = b.extents.z });
                }
            }

            // ---- height field
            var H = new float[res * res];
            for (int y = 0; y < res; y++)
            {
                float wz = (y + 0.5f) * mpp - half;
                for (int x = 0; x < res; x++) H[y * res + x] = WorldRegions.HeightAt((x + 0.5f) * mpp - half, wz);
            }

            var px = new Color[res * res];
            var water = new byte[res * res];
            var key = new Vector3(0.45f, 0.75f, 0.35f).normalized;
            var fill = new Vector3(-0.35f, 0.6f, -0.45f).normalized;
            var ink = new Color(0.16f, 0.13f, 0.10f);
            var shallow = new Color(0.40f, 0.66f, 0.70f);
            var deep = new Color(0.13f, 0.32f, 0.45f);
            var road = new Color(0.62f, 0.52f, 0.36f);
            var roadEdge = new Color(0.45f, 0.36f, 0.25f);
            var white = new Color(0.93f, 0.95f, 0.98f);

            // ---- pass 1: paint, shade, contours, water, roads, snow
            for (int y = 0; y < res; y++)
            {
                float wz = (y + 0.5f) * mpp - half;
                for (int x = 0; x < res; x++)
                {
                    float wx = (x + 0.5f) * mpp - half;
                    int i = y * res + x;
                    float h = H[i];
                    float hl = H[y * res + Mathf.Max(x - 1, 0)], hr = H[y * res + Mathf.Min(x + 1, res - 1)];
                    float hd = H[Mathf.Max(y - 1, 0) * res + x], hu = H[Mathf.Min(y + 1, res - 1) * res + x];
                    var nrm = new Vector3(hl - hr, 2f * mpp, hd - hu).normalized;

                    Color c = WorldRegions.PaintAt(wx, wz, h, nrm.y).gamma;
                    float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
                    c = Color.Lerp(c, new Color(lum, lum, lum), 0.12f) * 1.06f;

                    float shade = 0.70f + 0.45f * Mathf.Clamp01(Vector3.Dot(nrm, key)) + 0.12f * Mathf.Clamp01(Vector3.Dot(nrm, fill));
                    c *= shade;
                    float slope = 1f - nrm.y;
                    if (slope > 0.5f && ((x + y) & 3) == 0) c *= 0.78f;

                    // contours (edge crossing with right/up neighbours)
                    int b4 = Mathf.FloorToInt(h / 4f), b20 = Mathf.FloorToInt(h / 20f);
                    if (Mathf.FloorToInt(hr / 20f) != b20 || Mathf.FloorToInt(hu / 20f) != b20) c = Color.Lerp(c, ink, 0.42f);
                    else if (Mathf.FloorToInt(hr / 4f) != b4 || Mathf.FloorToInt(hu / 4f) != b4) c = Color.Lerp(c, ink, 0.18f);

                    bool isWater = WorldRegions.LakeM(wx, wz) > 0.12f && h < WorldRegions.WaterY;
                    if (isWater)
                    {
                        float depth = WorldRegions.WaterY - h;
                        c = Color.Lerp(shallow, deep, WorldRegions.S(0f, 3.5f, depth));
                        if (((x + y * 3) % 23) == 0) c = Color.Lerp(c, white, 0.12f);          // ripple stipple
                        water[i] = 1;
                    }
                    else
                    {
                        float pd = WorldRegions.PathDist(wx, wz);
                        if (pd < 2.4f)
                        {
                            c = Color.Lerp(c, road, 0.9f);
                            if (pd < 0.35f && ((x + y) / 7) % 2 == 0) c = Color.Lerp(c, white, 0.35f);
                        }
                        else if (pd < 3.4f) c = Color.Lerp(c, roadEdge, 0.6f * (1f - (pd - 2.4f)));
                        if (h > 48f)
                        {
                            float s = WorldRegions.S(48f, 62f, h);
                            c = Color.Lerp(c, white, ((x * 3 + y) % 5 == 0) ? s * 0.95f : s * 0.6f);
                        }
                    }
                    c.a = 1f;
                    px[i] = c;
                }
            }

            // ---- pass 2: coastline (foam on water, ink on land)
            var foam = new Color(0.95f, 0.98f, 1f);
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    int i = y * res + x;
                    bool w = water[i] != 0;
                    bool nearOther = false;
                    int reach = w ? 2 : 3;
                    for (int k = 1; k <= reach && !nearOther; k++)
                    {
                        if (x - k >= 0 && (water[i - k] != 0) != w) nearOther = true;
                        else if (x + k < res && (water[i + k] != 0) != w) nearOther = true;
                        else if (y - k >= 0 && (water[i - k * res] != 0) != w) nearOther = true;
                        else if (y + k < res && (water[i + k * res] != 0) != w) nearOther = true;
                    }
                    if (!nearOther) continue;
                    px[i] = w ? Color.Lerp(px[i], foam, 0.8f) : Color.Lerp(px[i], ink, 0.65f);
                }

            // ---- pass 3: region borders (dotted ink), sampled at 1024
            {
                const int g = 1024;
                var reg = new byte[g * g];
                for (int y = 0; y < g; y++)
                    for (int x = 0; x < g; x++)
                        reg[y * g + x] = (byte)WorldRegions.RegionAt((x + 0.5f) / g * size - half, (y + 0.5f) / g * size - half);
                int cell = Mathf.Max(1, res / g);
                for (int y = 0; y < g - 1; y++)
                    for (int x = 0; x < g - 1; x++)
                    {
                        byte r0 = reg[y * g + x];
                        if (reg[y * g + x + 1] == r0 && reg[(y + 1) * g + x] == r0) continue;
                        if (((x + y) % 6) >= 3) continue;
                        for (int dy = 0; dy < cell; dy++)
                            for (int dx = 0; dx < cell; dx++)
                            {
                                int xx = x * cell + dx, yy = y * cell + dy;
                                if (xx >= res || yy >= res) continue;
                                px[yy * res + xx] = Color.Lerp(px[yy * res + xx], ink, 0.7f);
                            }
                    }
            }

            // ---- pass 4: canopy / rocks / buildings
            var fruitC = new Color(0.30f, 0.52f, 0.24f);
            var pineC = new Color(0.16f, 0.36f, 0.22f);
            var fabC = new Color(0.58f, 0.36f, 0.62f);
            var deadC = new Color(0.46f, 0.40f, 0.31f);
            var rockC = new Color(0.50f, 0.50f, 0.50f);
            var bldC = new Color(0.55f, 0.48f, 0.42f);
            foreach (var s in stip)
            {
                int cx = Mathf.RoundToInt((s.x + half) / mpp), cy = Mathf.RoundToInt((s.z + half) / mpp);
                if (s.kind == 5)
                {
                    int ex = Mathf.CeilToInt(s.sx / mpp), ez = Mathf.CeilToInt(s.sz / mpp);
                    for (int yy = cy - ez; yy <= cy + ez; yy++)
                        for (int xx = cx - ex; xx <= cx + ex; xx++)
                        {
                            if (xx < 0 || yy < 0 || xx >= res || yy >= res) continue;
                            bool edge = xx == cx - ex || xx == cx + ex || yy == cy - ez || yy == cy + ez;
                            px[yy * res + xx] = edge ? ink : bldC;
                        }
                    continue;
                }
                float rpx = Mathf.Max(1.5f, s.r / mpp);
                int ri = Mathf.CeilToInt(rpx);
                Color baseC = s.kind == 0 ? fruitC : s.kind == 1 ? pineC : s.kind == 2 ? fabC : s.kind == 3 ? deadC : rockC;
                for (int yy = cy - ri; yy <= cy + ri; yy++)
                    for (int xx = cx - ri; xx <= cx + ri; xx++)
                    {
                        if (xx < 0 || yy < 0 || xx >= res || yy >= res) continue;
                        float dx = xx - cx, dy = yy - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / rpx;
                        if (d > 1f) continue;
                        Color c = baseC;
                        float lit = (dx - dy) / rpx;                      // NW light, SE shadow
                        c *= 1f + Mathf.Clamp(-lit, -0.16f, 0.12f);
                        if (d > 0.82f) c = Color.Lerp(c, ink, 0.45f);
                        px[yy * res + xx] = Color.Lerp(px[yy * res + xx], c, s.kind == 4 ? 0.85f : 0.95f);
                    }
            }

            // ---- pass 5: paper grain, rim parchment, vignette
            var parchment = new Color(0.84f, 0.78f, 0.64f);
            uint seed = 0x9E3779B9u;
            for (int y = 0; y < res; y++)
            {
                float wz = (y + 0.5f) * mpp - half;
                for (int x = 0; x < res; x++)
                {
                    float wx = (x + 0.5f) * mpp - half;
                    int i = y * res + x;
                    seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                    float grain = 1f + ((seed & 0xFF) / 255f - 0.5f) * 0.06f;
                    Color c = px[i] * grain;
                    float rim = WorldRegions.RimM(wx, wz);
                    c = Color.Lerp(c, parchment, WorldRegions.S(0.62f, 1f, rim) * 0.85f);
                    float vr = Mathf.Sqrt(wx * wx + wz * wz) / half;
                    c *= 1f - WorldRegions.S(0.82f, 1.05f, vr) * 0.25f;
                    c.a = 1f;
                    px[i] = c;
                }
            }

            var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
            tex.SetPixels(px);
            tex.Apply(false);
            System.IO.File.WriteAllBytes(Path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(Path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 4;
            imp.maxTextureSize = res;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            var ps = imp.GetPlatformTextureSettings("Standalone");
            ps.overridden = true; ps.maxTextureSize = res; ps.format = TextureImporterFormat.BC7; ps.compressionQuality = 100;
            imp.SetPlatformTextureSettings(ps);
            imp.SaveAndReimport();
            var result = AssetDatabase.LoadAssetAtPath<Texture2D>(Path);

            var map = Object.FindAnyObjectByType<MapSystem>();
            if (map != null)
            {
                map.worldMap = result;
                EditorUtility.SetDirty(map);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(map.gameObject.scene);
            }
            Debug.Log("[WuWa] map bake " + res + "² in " + (sw.ElapsedMilliseconds / 1000f).ToString("0.0") + "s, stipples " + stip.Count);
            return result;
        }
    }
}
