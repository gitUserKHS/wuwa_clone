using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Procedural 64px map icons (signed-distance rasterized at Awake, no assets).
    public static class MapIcons
    {
        static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        const int N = 64;

        public static Sprite Get(string key)
        {
            Sprite s;
            if (_cache.TryGetValue(key, out s) && s != null) return s;
            s = Make(key);
            _cache[key] = s;
            return s;
        }

        static Sprite Make(string key)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = new Vector2(x - 31.5f, y - 31.5f);
                    float a = Alpha(key, p);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        // ---------------------------------------------------------------- shapes (negative = inside)
        static float Disc(Vector2 p, float r) { return p.magnitude - r; }
        static float Ring(Vector2 p, float r, float w) { return Mathf.Abs(p.magnitude - r) - w; }
        static float Box(Vector2 p, Vector2 c, Vector2 half, float round = 0f)
        {
            var q = new Vector2(Mathf.Abs(p.x - c.x) - half.x + round, Mathf.Abs(p.y - c.y) - half.y + round);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - round;
        }
        static float Diamond(Vector2 p, float r) { return (Mathf.Abs(p.x) + Mathf.Abs(p.y) - r) * 0.7071f; }
        static float Poly(Vector2 p, Vector2[] v)
        {
            float d = float.MaxValue;
            float s = 1f;
            int n = v.Length;
            for (int i = 0, j = n - 1; i < n; j = i, i++)
            {
                Vector2 e = v[j] - v[i];
                Vector2 w = p - v[i];
                float t = Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                Vector2 b = w - e * t;
                d = Mathf.Min(d, b.sqrMagnitude);
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }
        static float U(float a, float b) { return Mathf.Min(a, b); }
        static float Cut(float a, float b) { return Mathf.Max(a, -b); }
        static float Aa(float d) { return Mathf.Clamp01(0.5f - d); }
        static Vector2 V(float x, float y) { return new Vector2(x, y); }

        static float Alpha(string key, Vector2 p)
        {
            switch (key)
            {
                case "disc": return Aa(Disc(p, 30f));
                case "dot": return Aa(Disc(p, 12f));
                case "ring": return Aa(Ring(p, 26f, 3f));
                case "diamond": return Aa(Diamond(p, 24f));
                case "tower":
                    return Aa(U(Poly(p, new[] { V(0, 29), V(13, -6), V(5, -6), V(5, -24), V(-5, -24), V(-5, -6), V(-13, -6) }), Ring(p, 28f, 1.6f)));
                case "waystone":
                    return Aa(U(Diamond(p, 22f), Cut(Diamond(p, 29f), Diamond(p, 26f))));
                case "chest":
                    return Aa(Cut(Box(p, V(0, -2), V(24f, 18f), 5f), Box(p, V(0, 5), V(23f, 1.6f))));
                case "house":
                    return Aa(Cut(U(Poly(p, new[] { V(0, 28), V(27, 4), V(-27, 4) }), Box(p, V(0, -10), V(18f, 14f))), Box(p, V(0, -16), V(5f, 8f))));
                case "bag":
                    return Aa(U(Box(p, V(0, -6), V(20f, 16f), 5f), Cut(Ring(p - V(0, 10), 10f, 3f), Box(p, V(0, 0), V(30f, 10f)))));
                case "key":
                    return Aa(U(U(Ring(p - V(0, 12), 10f, 4f), Box(p, V(0, -8), V(3.5f, 20f))), Box(p, V(5, -20), V(6f, 3f))));
                case "boss":
                    return Aa(U(U(Ring(p, 21f, 5f), Poly(p, new[] { V(-20, 8), V(-31, 30), V(-8, 22) })), Poly(p, new[] { V(20, 8), V(31, 30), V(8, 22) })));
                case "arena":
                    return Aa(U(Ring(p, 24f, 4f), Disc(p, 8f)));
                case "rift":
                    {
                        var v = new Vector2[16];
                        for (int i = 0; i < 16; i++) { float r = (i % 2 == 0) ? 29f : 12f; float a = i / 16f * Mathf.PI * 2f + Mathf.PI * 0.5f; v[i] = V(Mathf.Cos(a) * r, Mathf.Sin(a) * r); }
                        return Aa(Poly(p, v));
                    }
                case "grapple":
                    return Aa(U(Ring(p, 18f, 4f), Box(p, V(0, 22), V(3f, 8f))));
                case "camp":
                    return Aa(Cut(Cut(U(Disc(p - V(0, 5), 17f), Box(p, V(0, -12), V(13f, 8f), 3f)), Disc(p - V(-7, 6), 4.5f)), Disc(p - V(7, 6), 4.5f)));
                case "quest":
                    return Aa(U(Cut(Diamond(p, 27f), Diamond(p, 19f)), Disc(p, 6f)));
                case "player":
                    return Aa(Poly(p, new[] { V(0, 29), V(23, -22), V(0, -10), V(-23, -22) }));
                case "pin":
                    return Aa(U(Box(p, V(-11, 0), V(2.5f, 27f)), Poly(p, new[] { V(-9, 27), V(22, 16), V(-9, 5) })));
                case "cursor":
                    return Aa(Cut(U(Ring(p, 20f, 1.8f), U(Box(p, V(0, 0), V(1.4f, 30f)), Box(p, V(0, 0), V(30f, 1.4f)))), Disc(p, 9f)));
                case "cone":
                    {
                        float r = p.magnitude;
                        if (r > 31f || r < 0.5f) return 0f;
                        float ang = Mathf.Abs(Mathf.Atan2(p.x, p.y)) * Mathf.Rad2Deg;
                        float edge = Mathf.Clamp01((35f - ang) / 4f);
                        return edge * (1f - r / 31f) * 0.9f;
                    }
                case "tick":
                    return Aa(Box(p, V(0, 22), V(1.6f, 6f)));
                // ---- item icons (bag / shop)
                case "shard":
                    return Aa(Poly(p, new[] { V(-4, 28), V(20, 6), V(8, -26), V(-18, -14), V(-24, 10) }));
                case "crystal":
                    return Aa(U(Diamond(p, 26f), Cut(Diamond(p - V(0, 4), 14f), Diamond(p - V(0, 4), 9f))));
                case "crown":
                    return Aa(Poly(p, new[] { V(-28, -18), V(28, -18), V(28, -4), V(22, 4), V(16, 26), V(6, 2), V(0, 28), V(-6, 2), V(-16, 26), V(-22, 4), V(-28, -4) }));
                case "stone":
                    {
                        var v = new Vector2[6];
                        for (int i = 0; i < 6; i++) { float a = i / 6f * Mathf.PI * 2f + Mathf.PI / 6f; v[i] = V(Mathf.Cos(a) * 26f, Mathf.Sin(a) * 26f); }
                        return Aa(Cut(Poly(p, v), Box(p, V(0, 0), V(12f, 2f))));
                    }
                case "tuner":
                    return Aa(U(U(Box(p, V(-9, 8), V(3f, 20f)), Box(p, V(9, 8), V(3f, 20f))), U(Box(p, V(0, -8), V(12f, 3f)), Box(p, V(0, -18), V(3f, 12f)))));
                case "flask":
                    return Aa(U(Box(p, V(0, 20), V(7f, 9f), 2f), Cut(Disc(p - V(0, -6), 21f), Box(p, V(0, -2), V(9f, 2f)))));
                case "food":
                    return Aa(U(Cut(Disc(p - V(0, -2), 26f), Box(p, V(0, 16), V(30f, 16f))), Box(p, V(0, -2), V(28f, 3f))));
                case "potion":
                    return Aa(U(Box(p, V(0, 22), V(6f, 8f)), U(Disc(p - V(0, -4), 18f), Box(p, V(0, 8), V(8f, 10f)))));
                case "sword":
                    return Aa(U(U(Poly(p, new[] { V(0, 30), V(6, 20), V(6, -6), V(-6, -6), V(-6, 20) }), Box(p, V(0, -9), V(16f, 3f))), Box(p, V(0, -20), V(3.5f, 10f))));
                default:
                    return Aa(Disc(p, 20f));
            }
        }
    }
}
