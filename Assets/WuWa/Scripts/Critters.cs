using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Ambient wildlife: butterflies drifting low over the meadows by day and a
    /// small flock of birds wheeling overhead. Both are procedural meshes with a
    /// vertex-flap shader, respawned around the player as they travel.
    public class Critters : MonoBehaviour
    {
        public int butterflyCount = 16;
        public int birdCount = 7;
        public Shader critterShader;                // serialized so the build ships the shader

        class Critter
        {
            public Transform t;
            public Renderer r;
            public Vector3 home;
            public Vector3 vel;
            public float seed, alt, speed, scale;
            public Color color;
        }

        readonly List<Critter> _flies = new List<Critter>();
        readonly List<Critter> _birds = new List<Critter>();
        Transform _player;
        Material _flyMat, _birdMat;
        Mesh _flyMesh, _birdMesh;
        MaterialPropertyBlock _mpb;
        float _flockAngle;
        Vector3 _flockCenter;
        static readonly int PhaseId = Shader.PropertyToID("_Phase");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static readonly Color[] FlyColors =
        {
            new Color(1f, 0.85f, 0.25f), new Color(0.35f, 0.65f, 1f), new Color(1f, 0.55f, 0.75f),
            new Color(0.95f, 0.95f, 0.9f), new Color(1f, 0.55f, 0.2f), new Color(0.55f, 0.9f, 0.5f),
            new Color(0.75f, 0.5f, 1f),
        };

        void Start()
        {
            var sh = critterShader != null ? critterShader : Shader.Find("WuWa/Critter");
            if (sh == null) { Debug.LogWarning("[WuWa] Critter shader missing"); enabled = false; return; }
            _mpb = new MaterialPropertyBlock();
            _flyMat = new Material(sh);
            _flyMat.SetFloat("_Shape", 0f);
            _flyMat.SetFloat("_Flap", 22f);
            _flyMat.SetFloat("_FlapAmp", 0.7f);
            _birdMat = new Material(sh);
            _birdMat.SetFloat("_Shape", 1f);
            _birdMat.SetFloat("_Flap", 7f);
            _birdMat.SetFloat("_FlapAmp", 0.45f);
            _birdMat.SetColor("_Color", new Color(0.12f, 0.12f, 0.16f));
            _flyMesh = WingMesh(1f, 0.55f);
            _birdMesh = WingMesh(1f, 0.16f);

            // small (20–28 cm span) so they read as butterflies, not drifting blobs
            for (int i = 0; i < butterflyCount; i++) _flies.Add(Make("butterfly", _flyMesh, _flyMat, 0.10f + Random.value * 0.04f));
            for (int i = 0; i < birdCount; i++) _birds.Add(Make("bird", _birdMesh, _birdMat, 1.1f + Random.value * 0.4f));
        }

        Critter Make(string n, Mesh mesh, Material mat, float scale)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var c = new Critter
            {
                t = go.transform, r = r, seed = Random.value * 100f, alt = 0.9f + Random.value * 1.3f,
                speed = 1.2f + Random.value * 1.4f, scale = scale, color = FlyColors[Random.Range(0, FlyColors.Length)],
            };
            go.transform.localScale = Vector3.one * scale;
            _mpb.Clear();
            _mpb.SetFloat(PhaseId, c.seed);
            if (mesh == _flyMesh) _mpb.SetColor(ColorId, c.color);
            r.SetPropertyBlock(_mpb);
            go.SetActive(false);
            return c;
        }

        static Mesh WingMesh(float span, float depth)
        {
            // two wing quads in the XZ plane; the shader flaps them by |x|
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                int b = verts.Count;
                verts.Add(new Vector3(0f, 0f, -depth)); uvs.Add(new Vector2(0f, 0f));
                verts.Add(new Vector3(sx * span, 0f, -depth)); uvs.Add(new Vector2(1f, 0f));
                verts.Add(new Vector3(sx * span, 0f, depth)); uvs.Add(new Vector2(1f, 1f));
                verts.Add(new Vector3(0f, 0f, depth)); uvs.Add(new Vector2(0f, 1f));
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var m = new Mesh { name = "Wings" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            var bb = m.bounds; bb.Expand(1.5f); m.bounds = bb;
            return m;
        }

        void LateUpdate()
        {
            if (_player == null)
            {
                var p = Object.FindAnyObjectByType<PlayerController>();
                if (p == null) return;
                _player = p.transform;
                _flockCenter = _player.position + Vector3.up * 45f;
            }
            float dt = Time.deltaTime;
            float night = DayNightCycle.Night01;
            bool show = night < 0.6f && !GameDirector.MenuOpen;
            Vector3 pp = _player.position;
            float time = Time.time;

            // ---- butterflies
            for (int i = 0; i < _flies.Count; i++)
            {
                var c = _flies[i];
                bool ok = show && RegionOk(c.home);
                if (WuWaUtil.Flat(c.home - pp).magnitude > 42f || !ok)
                {
                    if (!Reseed(c, pp)) { c.t.gameObject.SetActive(false); continue; }
                }
                if (!c.t.gameObject.activeSelf) c.t.gameObject.SetActive(true);
                Vector3 target = c.home + new Vector3(
                    (Mathf.PerlinNoise(c.seed, time * 0.12f) - 0.5f) * 9f, 0f,
                    (Mathf.PerlinNoise(c.seed + 7f, time * 0.12f) - 0.5f) * 9f);
                target.y = WorldRegions.HeightAt(target.x, target.z) + c.alt + Mathf.Sin(time * 2.1f + c.seed) * 0.3f;
                Vector3 pos = c.t.position;
                if (pos.sqrMagnitude < 0.01f) pos = target;
                Vector3 want = (target - pos);
                float d = want.magnitude;
                Vector3 desired = d > 0.05f ? want / d * c.speed : Vector3.zero;
                Vector3 away = pos - pp;
                float ad = away.magnitude;
                if (ad < 2.4f) desired += away.normalized * 4f + Vector3.up * 2.5f;
                c.vel = Vector3.Lerp(c.vel, desired, 1f - Mathf.Exp(-2.5f * dt));
                pos += c.vel * dt;
                c.t.position = pos;
                Vector3 flat = WuWaUtil.Flat(c.vel);
                if (flat.sqrMagnitude > 0.01f)
                {
                    float bank = Mathf.Clamp(Vector3.Dot(c.vel, c.t.right) * -12f, -35f, 35f);
                    c.t.rotation = Quaternion.LookRotation(flat) * Quaternion.Euler(0f, 0f, bank);
                }
            }

            // ---- birds: a lazy circle high over the player, a V behind the leader
            bool birdsOk = show && WorldRegions.RegionAt(pp.x, pp.z) != WorldRegions.Rim;
            _flockCenter = Vector3.Lerp(_flockCenter, pp, 1f - Mathf.Exp(-0.25f * dt));
            _flockAngle += dt * 0.11f;
            for (int i = 0; i < _birds.Count; i++)
            {
                var c = _birds[i];
                if (!birdsOk) { c.t.gameObject.SetActive(false); continue; }
                if (!c.t.gameObject.activeSelf) c.t.gameObject.SetActive(true);
                float lag = i * 0.055f;
                float a = _flockAngle - lag;
                float radius = 38f + Mathf.Sin(c.seed) * 6f;
                int wing = (i + 1) / 2, sideSign = (i % 2 == 0) ? 1 : -1;
                Vector3 orbit = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 side = Vector3.Cross(Vector3.up, tangent);
                Vector3 pos = _flockCenter + orbit + side * (sideSign * wing * 2.2f) - tangent * (wing * 1.6f);
                float ground = WorldRegions.HeightAt(pos.x, pos.z);
                pos.y = Mathf.Max(ground + 30f, pp.y + 36f) + Mathf.Sin(time * 0.7f + c.seed) * 2.5f + wing * 0.8f;
                c.t.position = Vector3.Lerp(c.t.position.sqrMagnitude < 0.01f ? pos : c.t.position, pos, 1f - Mathf.Exp(-3f * dt));
                c.t.rotation = Quaternion.LookRotation(tangent) * Quaternion.Euler(0f, 0f, -18f);
            }
        }

        static bool RegionOk(Vector3 p)
        {
            int r = WorldRegions.RegionAt(p.x, p.z);
            return r == WorldRegions.Plains || r == WorldRegions.Bloom || r == WorldRegions.Forest
                || r == WorldRegions.Village || r == WorldRegions.Lake;
        }

        bool Reseed(Critter c, Vector3 pp)
        {
            for (int k = 0; k < 6; k++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                float rad = 8f + Random.value * 26f;
                var h = new Vector3(pp.x + Mathf.Cos(ang) * rad, 0f, pp.z + Mathf.Sin(ang) * rad);
                float gy = WorldRegions.HeightAt(h.x, h.z);
                if (gy < WorldRegions.WaterY + 0.3f || !RegionOk(h)) continue;
                h.y = gy;
                c.home = h;
                c.t.position = h + Vector3.up * c.alt;
                c.vel = Vector3.zero;
                return true;
            }
            return false;
        }
    }
}
