using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Fog of war: a 64×64 cell grid (≈27 m cells) over the 1.72 km world plus a
    /// discovered-region mask. Revealed by walking (140 m), by entering a region,
    /// by activating a tower (whole region) and by attuning a waystone.
    public static class MapDiscovery
    {
        public const int N = 64;
        public static readonly float CellSize = WorldRegions.WorldHalf * 2f / N;
        static readonly byte[] _bits = new byte[N * N / 8];
        static readonly float[] _alpha = new float[N * N];      // 1 = hidden, fades to 0
        static readonly byte[] _texBytes = new byte[N * N];
        static Texture2D _tex;
        static bool _dirtyBits = true, _initAlpha;
        static float _tick;
        static ResonanceTower[] _towers;
        static readonly HashSet<int> _towerSeen = new HashSet<int>();
        static readonly HashSet<int> _stoneSeen = new HashSet<int>();

        public static bool RevealAll;
        public static int RegionMask { get; private set; }
        public static event Action Changed;
        public static int RevealedCells { get; private set; }

        public static Texture2D Texture
        {
            get
            {
                if (_tex == null)
                {
                    _tex = new Texture2D(N, N, TextureFormat.Alpha8, false);
                    _tex.wrapMode = TextureWrapMode.Clamp;
                    _tex.filterMode = FilterMode.Bilinear;
                    for (int i = 0; i < N * N; i++) _alpha[i] = 1f;
                    _initAlpha = true;
                    Upload(true);
                }
                return _tex;
            }
        }

        // ---------------------------------------------------------------- queries
        public static bool Cell(int cx, int cz)
        {
            if (cx < 0 || cz < 0 || cx >= N || cz >= N) return false;
            int i = cz * N + cx;
            return (_bits[i >> 3] & (1 << (i & 7))) != 0;
        }

        public static bool IsRevealed(Vector3 world)
        {
            if (RevealAll) return true;
            int cx, cz; ToCell(world, out cx, out cz);
            return Cell(cx, cz);
        }

        public static bool RegionDiscovered(int id) { return RevealAll || (RegionMask & (1 << id)) != 0; }

        public static void ToCell(Vector3 world, out int cx, out int cz)
        {
            cx = Mathf.FloorToInt((world.x + WorldRegions.WorldHalf) / CellSize);
            cz = Mathf.FloorToInt((world.z + WorldRegions.WorldHalf) / CellSize);
        }

        static Vector2 CellCenter(int cx, int cz)
        {
            return new Vector2((cx + 0.5f) * CellSize - WorldRegions.WorldHalf, (cz + 0.5f) * CellSize - WorldRegions.WorldHalf);
        }

        // ---------------------------------------------------------------- reveal
        static void SetCell(int cx, int cz)
        {
            if (cx < 0 || cz < 0 || cx >= N || cz >= N) return;
            int i = cz * N + cx;
            if ((_bits[i >> 3] & (1 << (i & 7))) != 0) return;
            _bits[i >> 3] |= (byte)(1 << (i & 7));
            RevealedCells++;
            _dirtyBits = true;
        }

        public static void RevealCircle(Vector3 world, float radius)
        {
            int c0x, c0z; ToCell(world, out c0x, out c0z);
            int r = Mathf.CeilToInt(radius / CellSize) + 1;
            var w2 = new Vector2(world.x, world.z);
            for (int cz = c0z - r; cz <= c0z + r; cz++)
                for (int cx = c0x - r; cx <= c0x + r; cx++)
                    if (Vector2.Distance(CellCenter(cx, cz), w2) <= radius) SetCell(cx, cz);
        }

        public static void RevealRegion(int id)
        {
            for (int cz = 0; cz < N; cz++)
                for (int cx = 0; cx < N; cx++)
                {
                    var c = CellCenter(cx, cz);
                    if (WorldRegions.RegionAt(c.x, c.y) == id) SetCell(cx, cz);
                }
            DiscoverRegion(id);
        }

        public static void DiscoverRegion(int id)
        {
            if (id < 0 || id > 30) return;
            int bit = 1 << id;
            if ((RegionMask & bit) != 0) return;
            RegionMask |= bit;
            if (Changed != null) Changed();
        }

        public static void Reset()
        {
            Array.Clear(_bits, 0, _bits.Length);
            RegionMask = 0; RevealedCells = 0;
            _towerSeen.Clear(); _stoneSeen.Clear();
            _dirtyBits = true;
            if (Changed != null) Changed();
        }

        // ---------------------------------------------------------------- save
        public static string Export() { return Convert.ToBase64String(_bits); }

        public static void Import(string b64, int regionMask)
        {
            Array.Clear(_bits, 0, _bits.Length);
            RevealedCells = 0;
            if (!string.IsNullOrEmpty(b64))
            {
                try
                {
                    var b = Convert.FromBase64String(b64);
                    Array.Copy(b, _bits, Mathf.Min(b.Length, _bits.Length));
                }
                catch { }
            }
            for (int i = 0; i < N * N; i++) if ((_bits[i >> 3] & (1 << (i & 7))) != 0) RevealedCells++;
            RegionMask = regionMask;
            _dirtyBits = true;
            // snap the overlay (no fade on load)
            var tex = Texture;
            for (int i = 0; i < N * N; i++) _alpha[i] = ((_bits[i >> 3] & (1 << (i & 7))) != 0 || RevealAll) ? 0f : 1f;
            Upload(true);
            if (Changed != null) Changed();
        }

        // ---------------------------------------------------------------- per frame (10 Hz)
        public static void Tick()
        {
            _tick -= Time.unscaledDeltaTime;
            if (_tick > 0f) return;
            float dt = 0.1f - _tick;
            _tick = 0.1f;

            var pc = PlayerController.Instance;
            if (pc != null)
            {
                var pos = pc.transform.position;
                RevealCircle(pos, 140f);
                DiscoverRegion(WorldRegions.RegionAt(pos.x, pos.z));
            }
            if (_towers == null || _towers.Length == 0) _towers = UnityEngine.Object.FindObjectsByType<ResonanceTower>(FindObjectsSortMode.None);
            if (_towers != null)
                foreach (var t in _towers)
                    if (t != null && t.Activated && !_towerSeen.Contains(t.towerId))
                    {
                        _towerSeen.Add(t.towerId);
                        RevealRegion(WorldRegions.RegionAt(t.transform.position.x, t.transform.position.z));
                        RevealCircle(t.transform.position, 260f);
                    }
            foreach (var w in Waystone.All)
                if (w != null && w.Discovered && !_stoneSeen.Contains(w.stoneId))
                {
                    _stoneSeen.Add(w.stoneId);
                    RevealCircle(w.transform.position, 220f);
                    DiscoverRegion(WorldRegions.RegionAt(w.transform.position.x, w.transform.position.z));
                }

            // fade the overlay toward its target
            var tex = Texture;
            bool changed = _dirtyBits;
            float k = Mathf.Clamp01(dt / 0.35f);
            for (int i = 0; i < N * N; i++)
            {
                float target = (RevealAll || (_bits[i >> 3] & (1 << (i & 7))) != 0) ? 0f : 1f;
                float a = _alpha[i];
                if (Mathf.Abs(a - target) > 0.002f) { _alpha[i] = Mathf.MoveTowards(a, target, k); changed = true; }
            }
            if (changed) Upload(false);
            _dirtyBits = false;
        }

        static void Upload(bool force)
        {
            if (_tex == null) return;
            for (int i = 0; i < N * N; i++) _texBytes[i] = (byte)(Mathf.Clamp01(_alpha[i]) * 255f);
            _tex.SetPixelData(_texBytes, 0);
            _tex.Apply(false);
        }
    }
}
