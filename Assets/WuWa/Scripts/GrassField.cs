using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WuWa
{
    /// GPU-instanced procedural grass. Blades are generated per 20 m chunk around
    /// the player straight from WorldRegions (height, biome paint, road/slope
    /// masks), uploaded once to a structured buffer and drawn with
    /// Graphics.RenderMeshPrimitives. Chunk builds are time-sliced (one per
    /// frame after the initial ring) so the field never hitches.
    public class GrassField : MonoBehaviour
    {
        public const float ChunkSize = 20f;
        public float viewRadius = 66f;
        public float fadeStart = 46f;
        public int bladesPerChunk = 1500;
        public Shader bladeShader;                  // serialized so the build ships the shader
        public static bool Enabled = true;
        static GrassField _inst;

        /// Settings hook: density tier 0-3 (0 = off) and distance tier 0-2.
        public static void ApplyQuality(int density, int distance)
        {
            Enabled = density > 0;
            if (_inst == null) return;
            int[] blades = { 600, 600, 1500, 2200 };
            float[] radius = { 40f, 66f, 90f };
            float[] fade = { 28f, 46f, 64f };
            int d = Mathf.Clamp(density, 0, 3), r = Mathf.Clamp(distance, 0, 2);
            bool changed = _inst.bladesPerChunk != blades[d] || !Mathf.Approximately(_inst.viewRadius, radius[r]);
            _inst.bladesPerChunk = blades[d];
            _inst.viewRadius = radius[r];
            _inst.fadeStart = fade[r];
            if (_inst._scratch == null || _inst._scratch.Length < _inst.bladesPerChunk) _inst._scratch = new Blade[_inst.bladesPerChunk];
            if (changed) { _inst.ReleaseAll(); _inst._burst = 9; }
        }

        struct Blade
        {
            public Vector3 pos;
            public Vector3 col;
            public float yaw, h, w, seed;
        }
        const int Stride = 10 * 4;          // 2 x float3 + 4 floats, tightly packed

        class Chunk
        {
            public ComputeBuffer buf;
            public int count;
            public Bounds bounds;
            public MaterialPropertyBlock mpb;
        }

        readonly Dictionary<Vector2Int, Chunk> _chunks = new Dictionary<Vector2Int, Chunk>();
        readonly List<Vector2Int> _wanted = new List<Vector2Int>();
        readonly List<Vector2Int> _remove = new List<Vector2Int>();
        Mesh _blade;
        Material _mat;
        Transform _player;
        Blade[] _scratch;
        int _burst = 9;                     // chunks allowed on the first frames

        static readonly int BladesId = Shader.PropertyToID("_Blades");
        static readonly int PlayerId = Shader.PropertyToID("_PlayerPos");
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        void Start()
        {
            _inst = this;
            var sh = bladeShader != null ? bladeShader : Shader.Find("WuWa/GrassBlade");
            if (sh == null) { Debug.LogWarning("[WuWa] GrassBlade shader missing"); enabled = false; return; }
            _mat = new Material(sh);
            _blade = BuildBladeMesh();
            _scratch = new Blade[bladesPerChunk];
        }

        void OnDestroy()
        {
            foreach (var kv in _chunks) if (kv.Value.buf != null) kv.Value.buf.Release();
            _chunks.Clear();
        }

        static Mesh BuildBladeMesh()
        {
            // tapered strip: 3 segments + tip, x = width factor, y = height factor
            float[] ys = { 0f, 0.34f, 0.68f };
            float[] ws = { 0.5f, 0.42f, 0.26f };
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int r = 0; r < 3; r++)
            {
                verts.Add(new Vector3(-ws[r], ys[r], 0f)); uvs.Add(new Vector2(0f, ys[r]));
                verts.Add(new Vector3(ws[r], ys[r], 0f)); uvs.Add(new Vector2(1f, ys[r]));
            }
            verts.Add(new Vector3(0f, 1f, 0f)); uvs.Add(new Vector2(0.5f, 1f));
            int[] tris = { 0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5, 4, 6, 5 };
            var m = new Mesh { name = "GrassBlade" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return m;
        }

        void LateUpdate()
        {
            if (_mat == null) return;
            if (_player == null)
            {
                var p = Object.FindAnyObjectByType<PlayerController>();
                if (p != null) _player = p.transform;
            }
            var cam = Camera.main;
            Vector3 c = _player != null ? _player.position : (cam != null ? cam.transform.position : Vector3.zero);

            if (!Enabled)
            {
                if (_chunks.Count > 0) ReleaseAll();
                return;
            }

            // ---- which chunks should exist
            int r = Mathf.CeilToInt(viewRadius / ChunkSize);
            var center = new Vector2Int(Mathf.FloorToInt(c.x / ChunkSize), Mathf.FloorToInt(c.z / ChunkSize));
            _wanted.Clear();
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var key = new Vector2Int(center.x + dx, center.y + dz);
                    if (ChunkDist(key, c) <= viewRadius + ChunkSize * 0.6f) _wanted.Add(key);
                }

            // ---- drop the ones that fell out of range
            _remove.Clear();
            foreach (var kv in _chunks)
                if (ChunkDist(kv.Key, c) > viewRadius + ChunkSize * 1.6f) _remove.Add(kv.Key);
            for (int i = 0; i < _remove.Count; i++)
            {
                var ch = _chunks[_remove[i]];
                if (ch.buf != null) ch.buf.Release();
                _chunks.Remove(_remove[i]);
            }

            // ---- build missing chunks, nearest first, time-sliced
            int budget = _burst > 0 ? 3 : 1;
            while (budget > 0)
            {
                float best = float.MaxValue;
                Vector2Int bestKey = default(Vector2Int);
                bool any = false;
                for (int i = 0; i < _wanted.Count; i++)
                {
                    if (_chunks.ContainsKey(_wanted[i])) continue;
                    float d = ChunkDist(_wanted[i], c);
                    if (d < best) { best = d; bestKey = _wanted[i]; any = true; }
                }
                if (!any) break;
                _chunks[bestKey] = Build(bestKey);
                budget--;
                if (_burst > 0) _burst--;
            }

            // ---- draw
            _mat.SetVector(PlayerId, c);
            _mat.SetVector(FadeId, new Vector4(fadeStart, viewRadius, 0f, 0f));
            foreach (var kv in _chunks)
            {
                var ch = kv.Value;
                if (ch.count == 0) continue;
                var rp = new RenderParams(_mat)
                {
                    worldBounds = ch.bounds,
                    matProps = ch.mpb,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    lightProbeUsage = LightProbeUsage.Off,
                    layer = 0,
                };
                Graphics.RenderMeshPrimitives(rp, _blade, 0, ch.count);
            }
        }

        static float ChunkDist(Vector2Int key, Vector3 c)
        {
            float cx = (key.x + 0.5f) * ChunkSize, cz = (key.y + 0.5f) * ChunkSize;
            return Mathf.Sqrt((cx - c.x) * (cx - c.x) + (cz - c.z) * (cz - c.z));
        }

        void ReleaseAll()
        {
            foreach (var kv in _chunks) if (kv.Value.buf != null) kv.Value.buf.Release();
            _chunks.Clear();
        }

        Chunk Build(Vector2Int key)
        {
            var ch = new Chunk { mpb = new MaterialPropertyBlock() };
            float ox = key.x * ChunkSize, oz = key.y * ChunkSize;
            var rng = new System.Random(unchecked(key.x * 73856093 ^ key.y * 19349663 ^ 0x5bd1e995));
            int n = 0;
            float minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < bladesPerChunk; i++)
            {
                float x = ox + (float)rng.NextDouble() * ChunkSize;
                float z = oz + (float)rng.NextDouble() * ChunkSize;
                float h = WorldRegions.HeightAt(x, z);
                if (h < WorldRegions.WaterY + 0.45f) continue;

                // cheap slope from forward differences (shared by density + paint)
                float hx = WorldRegions.HeightAt(x + 1.2f, z);
                float hz = WorldRegions.HeightAt(x, z + 1.2f);
                float ny = 1.2f / Mathf.Sqrt((h - hx) * (h - hx) + (h - hz) * (h - hz) + 1.44f);

                float dens = WorldRegions.GrassDensity(x, z, h, ny);
                if (dens <= 0.001f || rng.NextDouble() > dens) continue;

                Color paint = WorldRegions.PaintAt(x, z, h, ny);
                float lum = paint.r * 0.3f + paint.g * 0.59f + paint.b * 0.11f;
                bool snowy = lum > 0.5f;
                float vary = 0.86f + (float)rng.NextDouble() * 0.28f;
                Vector3 col = snowy
                    ? new Vector3(paint.r * vary, paint.g * vary, paint.b * vary)
                    : new Vector3(paint.r * vary * 0.95f, paint.g * vary * 1.12f, paint.b * vary * 0.8f);

                float hgt = (0.22f + (float)rng.NextDouble() * 0.3f) * Mathf.Lerp(0.7f, 1f, dens);
                if (snowy) hgt *= 0.7f;
                _scratch[n++] = new Blade
                {
                    pos = new Vector3(x, h - 0.02f, z),
                    col = col,
                    yaw = (float)rng.NextDouble() * Mathf.PI * 2f,
                    h = hgt,
                    w = 0.075f + (float)rng.NextDouble() * 0.06f,
                    seed = (float)rng.NextDouble(),
                };
                if (h < minY) minY = h;
                if (h > maxY) maxY = h;
            }

            ch.count = n;
            if (n > 0)
            {
                ch.buf = new ComputeBuffer(n, Stride, ComputeBufferType.Structured);
                ch.buf.SetData(_scratch, 0, 0, n);
                ch.mpb.SetBuffer(BladesId, ch.buf);
                ch.bounds = new Bounds(
                    new Vector3(ox + ChunkSize * 0.5f, (minY + maxY) * 0.5f + 0.5f, oz + ChunkSize * 0.5f),
                    new Vector3(ChunkSize + 3f, maxY - minY + 3f, ChunkSize + 3f));
            }
            return ch;
        }
    }
}
