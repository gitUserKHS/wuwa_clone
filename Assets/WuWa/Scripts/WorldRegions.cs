using UnityEngine;

namespace WuWa
{
    /// Single source of truth for the open-world terrain: height field, region
    /// masks and names. The legacy plains/forest formulas are preserved EXACTLY
    /// inside their original bounds so every placed object (towers, spawners,
    /// ruins, grapple points) stays grounded; new biomes only shape the land
    /// outside that zone. Roads, biome paint and grass density live here too so
    /// the editor tile painter, the world map bake and the runtime grass all
    /// agree pixel for pixel.
    public static class WorldRegions
    {
        public const int Plains = 0, Forest = 1, Bloom = 2, Lake = 3, Waste = 4, Frost = 5, Ruins = 6, Village = 7, Rim = 8;

        public const float WorldHalf = 860f;
        public const float WaterY = 0.55f;

        // biome centers
        static readonly Vector2 BloomC = new Vector2(340f, 330f);
        static readonly Vector2 LakeC = new Vector2(390f, -100f);
        static readonly Vector2 WasteC = new Vector2(-360f, -80f);
        static readonly Vector2 FrostC = new Vector2(-190f, 500f);
        static readonly Vector2 RuinsC = new Vector2(90f, -360f);
        static readonly Vector2 VillageC = new Vector2(-215f, -165f);

        public static float S(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Radial(float x, float z, Vector2 c, float inner, float outer)
        {
            return 1f - S(inner, outer, Vector2.Distance(new Vector2(x, z), c));
        }

        // ---------------------------------------------------------------- masks
        /// 1 inside the original M1–M3 play space (plains circle + forest box).
        public static float LegacyZone(float x, float z)
        {
            float m1 = 1f - S(170f, 250f, Mathf.Sqrt(x * x + z * z));
            float bx = Mathf.Max(Mathf.Abs(x + 60f) - 120f, 0f);
            float bz = Mathf.Max(Mathf.Abs(z - 210f) - 120f, 0f);
            float m2 = 1f - S(0f, 90f, Mathf.Sqrt(bx * bx + bz * bz));
            return Mathf.Max(m1, m2);
        }

        public static float BloomM(float x, float z) { return Radial(x, z, BloomC, 150f, 235f); }
        public static float LakeM(float x, float z) { return Radial(x, z, LakeC, 150f, 230f); }
        public static float WasteM(float x, float z) { return Radial(x, z, WasteC, 165f, 245f); }
        public static float FrostM(float x, float z) { return Radial(x, z, FrostC, 175f, 265f); }
        public static float RuinsM(float x, float z) { return Radial(x, z, RuinsC, 145f, 220f); }
        public static float VillageM(float x, float z) { return Radial(x, z, VillageC, 55f, 95f); }
        public static float RimM(float x, float z) { return S(620f, 800f, Mathf.Sqrt(x * x + z * z)); }

        public static float ForestM(float x, float z)
        {
            float bx = Mathf.Max(Mathf.Abs(x + 60f) - 110f, 0f);
            float bz = Mathf.Max(Mathf.Abs(z - 210f) - 110f, 0f);
            return 1f - S(0f, 40f, Mathf.Sqrt(bx * bx + bz * bz));
        }

        // ---------------------------------------------------------------- height
        /// Legacy M1–M3 formula — DO NOT TOUCH (existing objects sit on it).
        public static float BaseH(float x, float z)
        {
            float h = Mathf.PerlinNoise(x * 0.015f + 31.7f, z * 0.015f + 11.3f) * 6.5f;
            h += Mathf.PerlinNoise(x * 0.05f + 3.1f, z * 0.05f + 7.7f) * 1.4f;
            h *= FlatSpot(x, z, 0f, 0f, 18f, 30f);
            h *= FlatSpot(x, z, 0f, 70f, 24f, 36f);
            return h;
        }

        static float FlatSpot(float x, float z, float cx, float cz, float inner, float outer)
        {
            float d = Vector2.Distance(new Vector2(x, z), new Vector2(cx, cz));
            if (d <= inner) return 0.06f;
            if (d >= outer) return 1f;
            float t = (d - inner) / (outer - inner);
            return Mathf.Lerp(0.06f, 1f, t * t * (3f - 2f * t));
        }

        public static float HeightAt(float x, float z)
        {
            float h = BaseH(x, z);
            float open = 1f - LegacyZone(x, z);

            // rolling hills across the new lands
            float hills = Mathf.PerlinNoise(x * 0.008f + 77.7f, z * 0.008f + 13.3f) * 13f
                        + Mathf.PerlinNoise(x * 0.021f + 5.5f, z * 0.021f + 9.9f) * 3.5f;
            h += hills * open;

            // frost plateau rises to the far north-west
            h += FrostM(x, z) * open * (14f + Mathf.PerlinNoise(x * 0.03f + 1.2f, z * 0.03f + 8.8f) * 3f);

            // mirror-lake basin sinks below the waterline
            float lk = LakeM(x, z);
            float scoop = S(0.15f, 0.9f, lk) * open;
            // a -3.8 m shelf near the shore, dropping to about -10 m in the middle: room to dive
            float bed = -3.8f - 6.5f * S(0.45f, 1f, lk) + Mathf.PerlinNoise(x * 0.05f + 2.2f, z * 0.05f + 6.6f) * 0.9f;
            h = Mathf.Lerp(h, bed, scoop);

            // songless ruins sit on gentle terraces
            h = Mathf.Lerp(h, h * 0.5f + 1.4f, RuinsM(x, z) * open * 0.75f);

            // the village square is flattened
            h = Mathf.Lerp(h, 2.1f, VillageM(x, z) * 0.94f);

            // mountain rim encloses the world
            float rim = RimM(x, z);
            h += rim * (26f + Mathf.PerlinNoise(x * 0.012f + 3.3f, z * 0.012f + 7.1f) * 38f
                        + Mathf.PerlinNoise(x * 0.045f + 9.1f, z * 0.045f + 4.4f) * 8f);
            return h;
        }

        /// Analytic-ish normal from finite differences (smooth across mesh tiles).
        public static Vector3 NormalAt(float x, float z)
        {
            const float e = 1.6f;
            float hl = HeightAt(x - e, z), hr = HeightAt(x + e, z);
            float hd = HeightAt(x, z - e), hu = HeightAt(x, z + e);
            return new Vector3(hl - hr, 2f * e, hd - hu).normalized;
        }

        // ---------------------------------------------------------------- region query
        public static int RegionAt(float x, float z)
        {
            if (RimM(x, z) > 0.55f) return Rim;
            if (VillageM(x, z) > 0.5f) return Village;
            if (FrostM(x, z) > 0.5f) return Frost;
            if (LakeM(x, z) > 0.5f) return Lake;
            if (WasteM(x, z) > 0.5f) return Waste;
            if (RuinsM(x, z) > 0.5f) return Ruins;
            if (BloomM(x, z) > 0.5f) return Bloom;
            if (ForestM(x, z) > 0.5f) return Forest;
            return Plains;
        }

        public static string RegionName(int id)
        {
            switch (id)
            {
                case Forest: return "속삭임 숲";
                case Bloom: return "노을빛 언덕";
                case Lake: return "거울 호수";
                case Waste: return "잿빛 황무지";
                case Frost: return "서리 고원";
                case Ruins: return "노래잃은 도시";
                case Village: return "메아리 마을";
                case Rim: return "세계의 등뼈";
                default: return "녹야 평원";
            }
        }

        // ---------------------------------------------------------------- roads
        static readonly Vector2[][] Paths =
        {
            new[] { V(0, 0), V(20, -18), V(0, 70), V(-60, 122), V(-60, 215) },
            new[] { V(0, 0), V(-100, -90), V(-215, -165) },
            new[] { V(-215, -165), V(-300, -110), V(-360, -80) },
            new[] { V(0, 0), V(140, -40), V(270, -75), V(360, -95) },
            new[] { V(0, 70), V(140, 160), V(260, 260), V(330, 320) },
            new[] { V(-60, 215), V(-110, 330), V(-160, 430), V(-185, 485) },
            new[] { V(20, -18), V(50, -160), V(80, -300), V(90, -350) },
        };

        static Vector2 V(float x, float z) { return new Vector2(x, z); }
        public static Vector2[][] RoadPaths { get { return Paths; } }

        /// Distance to the nearest dirt road centerline.
        public static float PathDist(float x, float z)
        {
            var p = new Vector2(x, z);
            float best = 9999f;
            foreach (var path in Paths)
                for (int i = 0; i < path.Length - 1; i++)
                {
                    Vector2 a = path[i], b = path[i + 1];
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
                }
            return best;
        }

        // ---------------------------------------------------------------- paint
        /// Biome ground color (linear space) at a point — used by the tile painter,
        /// the world map bake and the runtime grass tint.
        public static Color PaintAt(float x, float z, float h)
        {
            return PaintAt(x, z, h, NormalAt(x, z).y);
        }

        public static Color PaintAt(float x, float z, float h, float normalY)
        {
            float n = Mathf.PerlinNoise(x * 0.045f + 51f, z * 0.045f + 27f);      // patchiness
            float n2 = Mathf.PerlinNoise(x * 0.11f + 9f, z * 0.11f + 71f);

            var c = new Color(0.42f, 0.58f, 0.31f);                                // plains green
            c = Color.Lerp(c, new Color(0.35f, 0.52f, 0.28f), n * 0.5f);

            c = Color.Lerp(c, new Color(0.24f, 0.42f, 0.26f), ForestM(x, z));

            float bl = BloomM(x, z);
            var bloom = Color.Lerp(new Color(0.55f, 0.53f, 0.33f), new Color(0.72f, 0.47f, 0.42f), n * 0.8f);
            c = Color.Lerp(c, bloom, bl);

            float wa = WasteM(x, z);
            var waste = Color.Lerp(new Color(0.42f, 0.35f, 0.28f), new Color(0.30f, 0.29f, 0.28f), n);
            c = Color.Lerp(c, waste, wa);

            float ru = RuinsM(x, z);
            c = Color.Lerp(c, new Color(0.47f, 0.49f, 0.42f), ru * 0.9f);

            float fr = FrostM(x, z);
            var frost = Color.Lerp(new Color(0.88f, 0.92f, 0.97f), new Color(0.72f, 0.78f, 0.88f), n2 * 0.5f);
            c = Color.Lerp(c, frost, fr);

            float lk = LakeM(x, z);
            if (lk > 0.12f)
            {
                if (h < WaterY - 0.35f)
                    c = Color.Lerp(c, new Color(0.30f, 0.38f, 0.35f), lk);                 // lakebed
                else if (h < 2.6f)
                    c = Color.Lerp(c, new Color(0.80f, 0.72f, 0.50f), lk * (1f - S(1.6f, 2.6f, h)));  // beach
            }

            c = Color.Lerp(c, new Color(0.45f, 0.60f, 0.33f), VillageM(x, z));

            // steep slopes and the rim read as rock, high rim gets snow caps
            float slope = 1f - normalY;
            c = Color.Lerp(c, new Color(0.50f, 0.47f, 0.44f), S(0.30f, 0.55f, slope) * 0.85f);
            float rim = RimM(x, z);
            c = Color.Lerp(c, new Color(0.44f, 0.42f, 0.41f), rim * 0.8f);
            if (h > 48f) c = Color.Lerp(c, new Color(0.92f, 0.94f, 0.98f), S(48f, 62f, h));

            // dirt roads on top
            float pd = PathDist(x, z);
            c = Color.Lerp(c, new Color(0.60f, 0.50f, 0.34f), (1f - S(2.4f, 6.0f, pd)) * 0.85f);

            // subtle global variation; convert to linear so the painted values
            // are what actually shows on screen (project is linear color space)
            float v = 0.93f + n2 * 0.14f;
            var outC = new Color(Mathf.Clamp01(c.r * v), Mathf.Clamp01(c.g * v), Mathf.Clamp01(c.b * v), 1f).linear;
            outC.a = 1f;
            return outC;
        }

        // ---------------------------------------------------------------- grass
        /// 0..1 blade density for the runtime grass field.
        public static float GrassDensity(float x, float z, float h, float normalY)
        {
            if (h < WaterY + 0.45f) return 0f;
            float d = 1f;
            d = Mathf.Lerp(d, 0.55f, ForestM(x, z));
            d = Mathf.Lerp(d, 0.80f, BloomM(x, z));
            d = Mathf.Lerp(d, 0.10f, WasteM(x, z));
            d = Mathf.Lerp(d, 0.22f, FrostM(x, z));
            d = Mathf.Lerp(d, 0.25f, RuinsM(x, z) * 0.9f);
            d = Mathf.Lerp(d, 0.35f, VillageM(x, z));
            float lk = LakeM(x, z);
            if (lk > 0.12f && h < 2.6f) d *= 0.45f;                  // beach sand
            d *= 1f - RimM(x, z);
            d *= S(2.2f, 5.5f, PathDist(x, z));                      // clear the roads
            d *= 1f - S(0.22f, 0.40f, 1f - normalY);                 // no grass on rock faces
            return Mathf.Clamp01(d);
        }
    }
}
