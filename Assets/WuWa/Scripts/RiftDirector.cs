using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// 침식 균열 — roaming world events. Every couple of minutes (more often at
    /// night) a violet rift tears open some distance from the player and pours
    /// out a pack of shadows led by an elite. Clear them before the rift seals
    /// itself for shards, echoes and a chance at a weapon.
    public class RiftDirector : MonoBehaviour
    {
        public static RiftDirector I { get; private set; }

        public float minInterval = 80f;
        public float maxInterval = 140f;
        public float duration = 150f;
        public float minDistance = 55f;
        public float maxDistance = 110f;

        float _next;
        Transform _player;

        // active rift
        GameObject _rift;
        Vector3 _riftPos;
        float _riftEnds;
        readonly List<EnemyAI> _pack = new List<EnemyAI>();
        bool _active;
        Transform _beam;
        Light _light;
        float _pulse;
        int _poiIndex = -1;

        public static bool Active { get { return I != null && I._active; } }
        public static Vector3 Position { get { return I != null ? I._riftPos : Vector3.zero; } }

        public static bool ActiveNearby(Vector3 p, float radius)
        {
            return Active && WuWaUtil.Flat(I._riftPos - p).magnitude < radius;
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start() { _next = Time.time + 55f; }

        void Update()
        {
            if (_player == null)
            {
                var p = PlayerController.Instance;
                if (p == null) return;
                _player = p.transform;
            }

            if (_active) { TickRift(); return; }

            if (Time.time < _next) return;
            if (Cutscene.Active || GameDirector.MenuOpen || ArenaTrial.Running || EnemyRegistry.I == null) { _next = Time.time + 8f; return; }
            int lv = ProgressSystem.I != null ? ProgressSystem.I.Level : 1;
            if (lv < 2 && ContentStats.RiftsClosed == 0 && !GameFlags.Has("talked_0")) { _next = Time.time + 20f; return; }   // let the story introduce rifts
            if (TryOpen()) return;
            _next = Time.time + 12f;
        }

        bool TryOpen()
        {
            Vector3 pp = _player.position;
            for (int k = 0; k < 14; k++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                float dist = Mathf.Lerp(minDistance, maxDistance, Random.value);
                float x = pp.x + Mathf.Cos(ang) * dist, z = pp.z + Mathf.Sin(ang) * dist;
                if (Mathf.Abs(x) > 700f || Mathf.Abs(z) > 700f) continue;
                int r = WorldRegions.RegionAt(x, z);
                if (r == WorldRegions.Rim || r == WorldRegions.Village) continue;
                float h = WorldRegions.HeightAt(x, z);
                if (h < WorldRegions.WaterY + 1.2f) continue;
                if (WorldRegions.NormalAt(x, z).y < 0.82f) continue;
                if (WorldRegions.VillageM(x, z) > 0.25f) continue;
                if (ArenaTrial.I != null && WuWaUtil.Flat(ArenaTrial.I.transform.position - new Vector3(x, 0f, z)).magnitude < 50f) continue;
                Open(new Vector3(x, h, z), r);
                return true;
            }
            return false;
        }

        void Open(Vector3 pos, int region)
        {
            _active = true;
            _riftPos = pos;
            _riftEnds = Time.time + duration;
            _pack.Clear();
            BuildVisual(pos);

            int lv = ProgressSystem.I != null ? ProgressSystem.I.Level : 1;
            float regionMul = (region == WorldRegions.Waste || region == WorldRegions.Frost || region == WorldRegions.Ruins) ? 1.6f
                            : (region == WorldRegions.Lake || region == WorldRegions.Bloom) ? 1.3f : 1f;
            float mul = regionMul * (1f + lv * 0.04f);
            int count = Mathf.Clamp(3 + lv / 6, 3, 6);
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f + Random.value;
                Vector3 p = pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(3.5f, 6.5f);
                float roll = Random.value;
                EnemyKind kind = roll < 0.4f ? EnemyKind.Melee : roll < 0.7f ? EnemyKind.Fast : roll < 0.9f ? EnemyKind.Ranged : EnemyKind.Tank;
                var ai = EnemyRegistry.Spawn(kind, p, mul, i == 0);
                if (ai != null)
                {
                    ai.chaseRange = 34f;
                    _pack.Add(ai);
                }
            }

            _poiIndex = MapSystem.AddDynamicPoi(pos, "침식 균열", MapCategory.Rift);

            string dir = Compass(pos - _player.position);
            HUDController.Toast("침식 균열이 열렸습니다 — " + Mathf.RoundToInt(WuWaUtil.Flat(pos - _player.position).magnitude) + "m " + dir);
            AudioMan.I.Play2D(Sfx.Ult(), 0.7f, 0.5f);
            CameraShaker.Add(0.25f);
        }

        void TickRift()
        {
            _pulse += Time.deltaTime * 2.5f;
            if (_beam != null)
            {
                float s = 1f + Mathf.Sin(_pulse) * 0.12f;
                _beam.localScale = new Vector3(s, 1f, s);
                _beam.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
            }
            if (_light != null) _light.intensity = 3.5f + Mathf.Sin(_pulse * 1.7f) * 1.2f;

            _pack.RemoveAll(e => e == null || e.Hp == null || !e.Hp.IsAlive);
            float left = _riftEnds - Time.time;
            float dist = WuWaUtil.Flat(_riftPos - _player.position).magnitude;
            if (!ArenaTrial.Running)
                HUDController.SetEventLine("침식 균열 · 남은 그림자 " + _pack.Count + " · " + Mathf.RoundToInt(dist) + "m · " + Clock(left));

            if (_pack.Count == 0) { Close(true); return; }
            if (left <= 0f) { Close(false); return; }
        }

        void Close(bool cleared)
        {
            _active = false;
            if (!ArenaTrial.Running) HUDController.SetEventLine("");
            if (_poiIndex >= 0) MapSystem.RemoveDynamicPoi(_poiIndex);
            _poiIndex = -1;
            if (cleared)
            {
                int lv = ProgressSystem.I != null ? ProgressSystem.I.Level : 1;
                int shards = 60 + lv * 6;
                if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(shards);
                int echoId = Random.value < 0.25f ? 4 : Random.Range(0, 4);
                EchoOrb.SpawnAt(_riftPos + Vector3.up * 0.8f, 2, echoId);
                if (WeaponSystem.I != null && Random.value < 0.2f) WeaponSystem.I.Add(1);
                ContentStats.RiftsClosed++;
                DropTables.RiftLoot(_riftPos);
                BountyBoard.NotifyRift();
                RegionCompletion.NotifyRift(WorldRegions.RegionAt(_riftPos.x, _riftPos.z));
                HUDController.Toast("균열 정화! 조각소리 +" + shards + " · 에코가 흘러나왔다");
                VFXLibrary.SpawnNova(_riftPos + Vector3.up * 0.4f, new Color(0.85f, 0.6f, 1f), 8f, true);
                Hitstop.I.SlowMo(0.35f, 0.4f, 0.3f);
                if (QuestSystem.I != null) QuestSystem.I.Notify(QuestEvent.Rift);
                if (SaveSystem.I != null) SaveSystem.I.AutoSave("균열 정화");
            }
            else
            {
                foreach (var e in _pack) if (e != null) Destroy(e.gameObject);
                _pack.Clear();
                HUDController.Toast("균열이 스스로 닫혔습니다 — 그림자가 흩어진다");
            }
            if (_rift != null) Destroy(_rift);
            _rift = null;
            float interval = Random.Range(minInterval, maxInterval) * (DayNightCycle.IsNight ? 0.6f : 1f);
            _next = Time.time + interval;
        }

        void BuildVisual(Vector3 pos)
        {
            _rift = new GameObject("Rift");
            _rift.transform.position = pos;

            // pillar of light: two crossed additive quads (outer violet halo + white core)
            var bm = VFXLibrary.MakeAdditive(VFXLibrary.MakeSoftDot());
            bm.SetColor("_BaseColor", new Color(0.7f, 0.35f, 1f, 0.9f) * 2.6f);
            var core = VFXLibrary.MakeAdditive(VFXLibrary.MakeSoftDot());
            core.SetColor("_BaseColor", new Color(0.95f, 0.85f, 1f, 1f) * 2.2f);
            var beamRoot = new GameObject("beam");
            beamRoot.transform.SetParent(_rift.transform, false);
            beamRoot.transform.localPosition = new Vector3(0f, 21f, 0f);
            for (int i = 0; i < 4; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(q.GetComponent<Collider>());
                q.name = i < 2 ? "halo" : "core";
                q.transform.SetParent(beamRoot.transform, false);
                q.transform.localRotation = Quaternion.Euler(0f, (i % 2) * 90f, 0f);
                q.transform.localScale = i < 2 ? new Vector3(4.5f, 46f, 1f) : new Vector3(1.4f, 44f, 1f);
                q.GetComponent<MeshRenderer>().sharedMaterial = i < 2 ? bm : core;
            }
            _beam = beamRoot.transform;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "ring";
            Object.Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(_rift.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            ring.transform.localScale = new Vector3(7f, 0.05f, 7f);
            var rm = VFXLibrary.MakeAdditive(VFXLibrary.MakeRing());
            rm.SetColor("_BaseColor", new Color(0.7f, 0.35f, 1f, 0.8f) * 1.4f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = rm;

            var lgo = new GameObject("light");
            lgo.transform.SetParent(_rift.transform, false);
            lgo.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(0.7f, 0.4f, 1f);
            _light.range = 18f;
            _light.intensity = 3.5f;

            // rising motes
            var pgo = new GameObject("motes");
            pgo.transform.SetParent(_rift.transform, false);
            var ps = pgo.AddComponent<ParticleSystem>();
            var psr = pgo.GetComponent<ParticleSystemRenderer>();
            psr.material = bm;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.45f, 1f, 0.9f), new Color(0.4f, 0.2f, 0.8f, 0.6f));
            main.maxParticles = 200;
            var em = ps.emission; em.rateOverTime = 40f;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 3f;
            ps.Play();
        }

        static string Compass(Vector3 d)
        {
            float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            if (a < 0f) a += 360f;
            string[] names = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };
            return names[Mathf.RoundToInt(a / 45f) % 8] + "쪽";
        }

        static string Clock(float s)
        {
            s = Mathf.Max(0f, s);
            int m = Mathf.FloorToInt(s / 60f);
            int sec = Mathf.FloorToInt(s - m * 60f);
            return m + ":" + sec.ToString("00");
        }
    }
}
