using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Ambient wildlife: a small flock of birds wheeling high overhead, procedural
    /// meshes with a vertex-flap shader that follow the player as they travel.
    ///
    /// Low-flying butterflies used to drift over the meadows here too, but at 20 cm across
    /// and a few metres from the camera they read as flickering specks rather than wildlife,
    /// so they were removed. The Critter shader keeps its butterfly branch (_Shape 0).
    public class Critters : MonoBehaviour
    {
        public int birdCount = 7;
        public Shader critterShader;                // serialized so the build ships the shader

        class Critter
        {
            public Transform t;
            public float seed;
        }

        readonly List<Critter> _birds = new List<Critter>();
        Transform _player;
        Material _birdMat;
        Mesh _birdMesh;
        MaterialPropertyBlock _mpb;
        float _flockAngle;
        Vector3 _flockCenter;
        static readonly int PhaseId = Shader.PropertyToID("_Phase");

        void Start()
        {
            var sh = critterShader != null ? critterShader : Shader.Find("WuWa/Critter");
            if (sh == null) { Debug.LogWarning("[WuWa] Critter shader missing"); enabled = false; return; }
            _mpb = new MaterialPropertyBlock();
            _birdMat = new Material(sh);
            _birdMat.SetFloat("_Shape", 1f);
            _birdMat.SetFloat("_Flap", 7f);
            _birdMat.SetFloat("_FlapAmp", 0.45f);
            _birdMat.SetColor("_Color", new Color(0.12f, 0.12f, 0.16f));
            _birdMesh = WingMesh(1f, 0.16f);

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
            var c = new Critter { t = go.transform, seed = Random.value * 100f };
            go.transform.localScale = Vector3.one * scale;
            _mpb.Clear();
            _mpb.SetFloat(PhaseId, c.seed);
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
            bool show = DayNightCycle.Night01 < 0.6f && !GameDirector.MenuOpen;
            Vector3 pp = _player.position;
            float time = Time.time;

            // birds: a lazy circle high over the player, a V behind the leader
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
    }
}
