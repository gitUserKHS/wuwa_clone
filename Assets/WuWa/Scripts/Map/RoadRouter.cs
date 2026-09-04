using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Road-following route hint (design doc 10.7): the dirt-road polylines form a
    /// graph, the player and the target snap to their nearest road points, and
    /// Dijkstra picks the shortest road walk. When the off-road legs exceed 45% of
    /// the total (or the road walk is far longer than the straight line) the
    /// straight dashed line wins. Recomputed after 25 m of movement.
    public static class RoadRouter
    {
        class Edge { public int to; public float len; }
        struct Snap { public int a, b; public Vector2 p; public float dist; }

        static readonly List<Vector2> _nodes = new List<Vector2>();
        static readonly List<List<Edge>> _adj = new List<List<Edge>>();
        static readonly List<Vector2> _cache = new List<Vector2>();
        static bool _built, _cacheValid;
        static Vector2 _lastFrom, _lastTo;
        static bool _lastSetting;

        public static bool LastUsedRoads { get; private set; }
        public static float LastLength { get; private set; }
        public static int NodeCount { get { if (!_built) Build(); return _nodes.Count; } }

        static int NodeAt(Vector2 p)
        {
            for (int i = 0; i < _nodes.Count; i++) if ((_nodes[i] - p).sqrMagnitude < 4f) return i;   // shared vertices join paths
            _nodes.Add(p);
            _adj.Add(new List<Edge>());
            return _nodes.Count - 1;
        }

        static void Link(int a, int b)
        {
            if (a == b) return;
            float l = Vector2.Distance(_nodes[a], _nodes[b]);
            _adj[a].Add(new Edge { to = b, len = l });
            _adj[b].Add(new Edge { to = a, len = l });
        }

        static void Build()
        {
            _built = true;
            _nodes.Clear(); _adj.Clear();
            foreach (var path in WorldRegions.RoadPaths)
                for (int i = 0; i < path.Length - 1; i++) Link(NodeAt(path[i]), NodeAt(path[i + 1]));
        }

        static Snap Nearest(Vector2 q)
        {
            var best = new Snap { a = -1, b = -1, dist = float.MaxValue };
            for (int a = 0; a < _nodes.Count; a++)
                foreach (var e in _adj[a])
                {
                    if (e.to < a) continue;
                    Vector2 A = _nodes[a], B = _nodes[e.to], ab = B - A;
                    float t = ab.sqrMagnitude > 0.001f ? Mathf.Clamp01(Vector2.Dot(q - A, ab) / ab.sqrMagnitude) : 0f;
                    Vector2 p = A + ab * t;
                    float d = Vector2.Distance(q, p);
                    if (d < best.dist) best = new Snap { a = a, b = e.to, p = p, dist = d };
                }
            return best;
        }

        public static void Invalidate() { _cacheValid = false; }

        /// World-space xz polyline from `from` to `to`; two points = straight line.
        public static List<Vector2> Route(Vector3 from3, Vector3 to3)
        {
            if (!_built) Build();
            var from = new Vector2(from3.x, from3.z);
            var to = new Vector2(to3.x, to3.z);
            bool on = SettingsStore.D.roadRoute;
            if (_cacheValid && on == _lastSetting && (from - _lastFrom).magnitude < 25f && (to - _lastTo).magnitude < 5f) return _cache;
            _lastFrom = from; _lastTo = to; _lastSetting = on; _cacheValid = true;
            _cache.Clear();
            LastUsedRoads = false;
            float straight = Vector2.Distance(from, to);
            LastLength = straight;
            if (!on || _nodes.Count < 2 || straight < 30f) { _cache.Add(from); _cache.Add(to); return _cache; }

            var s = Nearest(from);
            var t = Nearest(to);
            if (s.a < 0 || t.a < 0) { _cache.Add(from); _cache.Add(to); return _cache; }

            // Dijkstra from the two endpoints of the start edge (seeded with the partial lengths)
            int n = _nodes.Count;
            var dist = new float[n]; var prev = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[s.a] = Vector2.Distance(s.p, _nodes[s.a]);
            dist[s.b] = Vector2.Distance(s.p, _nodes[s.b]);
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1; float best = float.MaxValue;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0) break;
                done[u] = true;
                foreach (var e in _adj[u])
                {
                    float nd = dist[u] + e.len;
                    if (nd < dist[e.to]) { dist[e.to] = nd; prev[e.to] = u; }
                }
            }
            float viaA = dist[t.a] < float.MaxValue ? dist[t.a] + Vector2.Distance(_nodes[t.a], t.p) : float.MaxValue;
            float viaB = dist[t.b] < float.MaxValue ? dist[t.b] + Vector2.Distance(_nodes[t.b], t.p) : float.MaxValue;
            int end = viaA <= viaB ? t.a : t.b;
            float road = Mathf.Min(viaA, viaB);
            bool sameEdge = (s.a == t.a && s.b == t.b) || (s.a == t.b && s.b == t.a);
            if (sameEdge) { float direct = Vector2.Distance(s.p, t.p); if (direct <= road) { road = direct; end = -1; } }
            if (road >= float.MaxValue) { _cache.Add(from); _cache.Add(to); return _cache; }

            float legs = s.dist + t.dist;
            float total = legs + road;
            if (legs > 0.45f * total || total > straight * 1.9f) { _cache.Add(from); _cache.Add(to); return _cache; }

            _cache.Add(from);
            _cache.Add(s.p);
            if (end >= 0)
            {
                var chain = new List<int>();
                for (int v = end; v >= 0; v = prev[v]) chain.Add(v);
                chain.Reverse();
                foreach (var v in chain) _cache.Add(_nodes[v]);
            }
            _cache.Add(t.p);
            _cache.Add(to);
            LastUsedRoads = true;
            LastLength = total;
            return _cache;
        }
    }
}
