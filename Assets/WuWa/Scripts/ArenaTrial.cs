using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WuWa
{
    /// 시련의 제단 — repeatable wave arena. Touch the altar to start five waves
    /// of shadows scaled to the party level; survive them all for shards, a
    /// five-star echo and a shot at the relic blade. Leaving the platform or a
    /// party wipe aborts the run.
    public class ArenaTrial : MonoBehaviour, IInteractable
    {
        public static ArenaTrial I { get; private set; }
        public static bool Running { get; private set; }

        public float platformRadius = 22f;
        public float interactRange = 3.6f;
        public const int WaveCount = 5;
        public int Tier { get; private set; }
        public static string TierName(int t) { return t >= 3 ? "III" : t == 2 ? "II" : t == 1 ? "I" : "-"; }
        public static float TierMul(int t) { return t >= 3 ? 1.7f : t == 2 ? 1.35f : 1f; }
        public static float RewardMul(int t) { return t >= 3 ? 1.6f : t == 2 ? 1.3f : 1f; }
        public static int TierTokens(int t) { return t >= 3 ? 8 : t == 2 ? 5 : 3; }
        public static string TierRequirement(int t) { return t >= 3 ? "파티 Lv 35 · Tier II 완주" : t == 2 ? "파티 Lv 25 · Tier I 완주" : "제한 없음"; }
        public static string TierRewards(int t)
        {
            return t >= 3 ? "5웨이브 보스 2체 · 적 ×1.7 · 보상 ×1.6 · 조율기 +3 · 첫 완주 명기 확정"
                 : t == 2 ? "정예 2체 · 적 ×1.35 · 보상 ×1.3 · 조율기 +2 · 왕관 파편 +1"
                 : "5웨이브 · 완주 400(첫)/220 · 4분 내 +120 · ★5 에코 · 조율기 2/1 · 왕관 파편 1";
        }

        public bool CanStartTier(int t, out string why)
        {
            int lv = ProgressSystem.I != null ? ProgressSystem.I.Level : 1;
            if (Running) { why = "진행 중"; return false; }
            if (t >= 2 && ContentStats.ArenaTierBest < t - 1) { why = "Tier " + TierName(t - 1) + " 완주 필요"; return false; }
            if (t == 2 && lv < 25) { why = "파티 Lv 25 필요"; return false; }
            if (t >= 3 && lv < 35) { why = "파티 Lv 35 필요"; return false; }
            if (EnemyRegistry.I == null) { why = "적 레지스트리 없음"; return false; }
            why = null; return true;
        }

        Transform _player;
        readonly List<EnemyAI> _alive = new List<EnemyAI>();
        int _wave;
        float _startTime;
        float _outsideSince = -1f;
        Transform _crystal;
        Transform _rune;
        Light _crystalLight;
        float _pulse;
        Coroutine _run;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; Running = false; }

        void Start()
        {
            _crystal = transform.Find("altar/crystal");
            if (_crystal != null) _crystalLight = _crystal.GetComponent<Light>();
        }

        void Update()
        {
            if (_player == null)
            {
                var p = PlayerController.Instance;
                if (p == null) return;
                _player = p.transform;
            }
            _pulse += Time.deltaTime * (Running ? 4f : 1.4f);
            if (_crystal != null)
            {
                _crystal.Rotate(0f, (Running ? 90f : 25f) * Time.deltaTime, 0f, Space.World);
                _crystal.localPosition = new Vector3(0f, 1.9f + Mathf.Sin(_pulse) * 0.12f, 0f);
            }
            if (_rune == null) _rune = transform.Find("rune");
            if (_rune != null)
            {
                float s = platformRadius * 1.55f * (1f + Mathf.Sin(_pulse * 0.5f) * 0.03f);
                _rune.localScale = new Vector3(s, s, 1f);
                _rune.Rotate(0f, 0f, (Running ? 18f : 5f) * Time.deltaTime, Space.Self);
            }
            if (_crystalLight != null) _crystalLight.intensity = (Running ? 4f : 2.2f) + Mathf.Sin(_pulse * 1.3f) * 0.6f;

            float d = WuWaUtil.Flat(_player.position - transform.position).magnitude;
            if (Running)
            {
                // leaving the platform for 4 s forfeits the trial
                if (d > platformRadius + 6f)
                {
                    if (_outsideSince < 0f) _outsideSince = Time.time;
                    float left = 4f - (Time.time - _outsideSince);
                    HUDController.SetEventLine("시련의 제단 이탈 — " + Mathf.CeilToInt(Mathf.Max(0f, left)) + "초 안에 돌아가세요");
                    if (left <= 0f) Abort("제단을 벗어나 시련이 무효가 되었습니다");
                }
                else _outsideSince = -1f;
                var pc = PlayerController.Instance;
                if (pc != null && !pc.IsAlive) Abort("파티 전멸 — 시련 실패");
                return;
            }

        }

        void OnEnable() { InteractionManager.Register(this); }
        void OnDisable() { InteractionManager.Unregister(this); }

        // IInteractable (prompt + key are routed by InteractionManager)
        public Vector3 InteractPosition { get { return transform.position; } }
        public float InteractRange { get { return interactRange; } }
        public int InteractPriority { get { return 1; } }
        public string InteractLabel { get { return "시련의 제단  (완주 " + ContentStats.ArenaClears + "회 · 최고 Tier " + TierName(ContentStats.ArenaTierBest) + ")"; } }
        public bool CanInteract { get { return !Running && !DialogueSystem.Active && !RiftDirector.ActiveNearby(transform.position, 60f); } }
        public void Interact() { ScreenRouter.Push("Trial"); }

        public void Begin(int tier = 1)
        {
            if (Running || EnemyRegistry.I == null) return;
            Running = true;
            Tier = Mathf.Clamp(tier, 1, 3);
            _wave = 0;
            _startTime = Time.time;
            _outsideSince = -1f;
            MusicDirector.ForceCombat = true;
            GameFlags.Set("arena_started");
            AudioMan.I.Play2D(Sfx.Ult(), 0.8f, 0.7f);
            VFXLibrary.SpawnNova(transform.position + Vector3.up * 0.3f, new Color(0.6f, 0.9f, 1f), platformRadius, true);
            CameraShaker.Add(0.5f);
            HUDController.Toast("시련 Tier " + TierName(Tier) + " 시작 — 제단을 지키세요");
            _run = StartCoroutine(RunWaves());
        }

        static float LevelMul()
        {
            int lv = ProgressSystem.I != null ? ProgressSystem.I.Level : 1;
            return 1f + lv * 0.045f;
        }

        IEnumerator RunWaves()
        {
            yield return new WaitForSeconds(2.2f);
            for (_wave = 1; _wave <= WaveCount && Running; _wave++)
            {
                SpawnWave(_wave);
                HUDController.Toast("웨이브 " + _wave + " / " + WaveCount);
                AudioMan.I.Play2D(Sfx.Skill(), 0.6f, 0.8f);
                while (Running)
                {
                    _alive.RemoveAll(e => e == null || e.Hp == null || !e.Hp.IsAlive);
                    float elapsed = Time.time - _startTime;
                    HUDController.SetEventLine("시련 · 웨이브 " + _wave + "/" + WaveCount + " · 남은 그림자 " + _alive.Count + " · " + Clock(elapsed));
                    if (_alive.Count == 0) break;
                    yield return null;
                }
                if (!Running) yield break;
                int shards = Mathf.RoundToInt((25 + _wave * 15) * RewardMul(Tier));
                DropTables.ArenaWave(_wave);
                if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(shards);
                if (_wave < WaveCount)
                {
                    HUDController.Toast("웨이브 " + _wave + " 클리어 — 조각소리 +" + shards);
                    ContentStats.ArenaBestWave = Mathf.Max(ContentStats.ArenaBestWave, _wave);
                    yield return new WaitForSeconds(3.5f);
                }
            }
            if (Running) Complete();
        }

        void SpawnWave(int wave)
        {
            float mul = LevelMul() * TierMul(Tier) * (0.8f + wave * 0.12f);
            var kinds = new List<EnemyKind>();
            switch (wave)
            {
                case 1: kinds.AddRange(new[] { EnemyKind.Melee, EnemyKind.Melee, EnemyKind.Melee }); break;
                case 2: kinds.AddRange(new[] { EnemyKind.Melee, EnemyKind.Fast, EnemyKind.Fast, EnemyKind.Ranged }); break;
                case 3: kinds.AddRange(new[] { EnemyKind.Tank, EnemyKind.Ranged, EnemyKind.Ranged, EnemyKind.Melee }); break;
                case 4: kinds.AddRange(new[] { EnemyKind.Fast, EnemyKind.Fast, EnemyKind.Tank, EnemyKind.Ranged, EnemyKind.Melee }); break;
                default: kinds.AddRange(Tier >= 3 ? new[] { EnemyKind.Boss, EnemyKind.Boss, EnemyKind.Fast, EnemyKind.Ranged } : new[] { EnemyKind.Boss, EnemyKind.Fast, EnemyKind.Ranged }); break;
            }
            for (int i = 0; i < kinds.Count; i++)
            {
                float a = (i / (float)kinds.Count) * Mathf.PI * 2f + wave * 0.7f;
                // spawn ON the platform top (its own y), never on the terrain underneath
                Vector3 pos = transform.position + new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)) * (platformRadius - 4f);
                pos.y = transform.position.y + 0.2f;
                bool elite = wave >= 3 && (i == 0 || (Tier >= 2 && i == 1)) && kinds[i] != EnemyKind.Boss;
                float m = kinds[i] == EnemyKind.Boss ? mul * 0.45f : mul;
                var ai = EnemyRegistry.Spawn(kinds[i], pos, m, elite, transform, false);
                if (ai != null)
                {
                    ai.chaseRange = 60f;                       // arena shadows never lose interest
                    _alive.Add(ai);
                }
            }
        }

        void Complete()
        {
            Running = false;
            MusicDirector.ForceCombat = false;
            float elapsed = Time.time - _startTime;
            bool first = ContentStats.ArenaClears == 0;
            ContentStats.ArenaClears++;
            ContentStats.ArenaBestWave = WaveCount;
            int shards = first ? 400 : 220;
            if (elapsed < 240f) shards += 120;
            shards = Mathf.RoundToInt(shards * RewardMul(Tier));
            if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(shards);
            if (EchoSystem.I != null) EchoSystem.I.Add(4);
            DropTables.ArenaClear(first);
            if (Tier >= 2)
            {
                Inventory.AddTokens(TierTokens(Tier) - 3);
                Inventory.Add(ItemDB.Tuner, Tier >= 3 ? 3 : 2);
                if (Tier == 2) Inventory.Add(ItemDB.Crown, 1);
            }
            if (Tier >= 3 && ContentStats.ArenaTierBest < 3 && WeaponSystem.I != null) WeaponSystem.I.Add(2);
            ContentStats.ArenaTierBest = Mathf.Max(ContentStats.ArenaTierBest, Tier);
            if (WeaponSystem.I != null && (first || Random.value < 0.3f)) WeaponSystem.I.Add(first ? 1 : 2);
            HUDController.SetEventLine("");
            HUDController.Victory();
            HUDController.Toast("시련 Tier " + TierName(Tier) + " 완주! " + Clock(elapsed) + " — 조각소리 +" + shards + ", ★5 에코, 증표 +" + TierTokens(Tier));
            if (MusicDirector.I != null) MusicDirector.I.PlayVictory();
            VFXLibrary.SpawnNova(transform.position + Vector3.up * 0.3f, new Color(1f, 0.9f, 0.5f), platformRadius, true);
            if (QuestSystem.I != null) QuestSystem.I.Notify(QuestEvent.Arena);
            if (SaveSystem.I != null) SaveSystem.I.AutoSave("시련 완주");
        }

        void Abort(string why)
        {
            if (!Running) return;
            Running = false;
            MusicDirector.ForceCombat = false;
            if (_run != null) StopCoroutine(_run);
            foreach (var e in _alive) if (e != null) Destroy(e.gameObject);
            _alive.Clear();
            HUDController.SetEventLine("");
            HUDController.Toast(why);
            AudioMan.I.Play2D(Sfx.Hurt(), 0.6f, 0.6f);
        }

        static string Clock(float s)
        {
            int m = Mathf.FloorToInt(s / 60f);
            int sec = Mathf.FloorToInt(s - m * 60f);
            return m + ":" + sec.ToString("00");
        }

        // ---------------------------------------------------------------- build
        /// A cylinder primitive ships with a capsule collider, which turns into a
        /// giant sphere once the cylinder is squashed flat — swap it for a convex
        /// mesh collider so the disc really is a disc.
        public static GameObject FlatCylinder(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            var cap = go.GetComponent<Collider>();
            if (cap != null) Object.DestroyImmediate(cap);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void MakeFlame(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("flame");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = VFXLibrary.MakeAdditive(VFXLibrary.MakeSoftDot());
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f, 0.9f), new Color(1f, 0.35f, 0.1f, 0.7f));
            main.maxParticles = 60;
            var em = ps.emission; em.rateOverTime = 28f;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 12f; shape.radius = 0.15f;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.6f, 0.25f);
            l.range = 9f;
            l.intensity = 2.4f;
        }

        /// Code-built altar platform (editor scene builder).
        public static ArenaTrial Build(Vector3 center, Material stone, Material trim)
        {
            var root = new GameObject("ArenaAltar");
            root.transform.position = center;
            var trial = root.AddComponent<ArenaTrial>();

            FlatCylinder("platform", root.transform, new Vector3(0f, -0.55f, 0f),
                new Vector3(trial.platformRadius * 2f, 0.6f, trial.platformRadius * 2f), stone);
            FlatCylinder("step", root.transform, new Vector3(0f, -0.65f, 0f),
                new Vector3(trial.platformRadius * 2f + 5f, 0.35f, trial.platformRadius * 2f + 5f), stone);

            // glowing rune ring on the floor
            var rune = GameObject.CreatePrimitive(PrimitiveType.Quad);
            rune.name = "rune";
            Object.DestroyImmediate(rune.GetComponent<Collider>());
            rune.transform.SetParent(root.transform, false);
            rune.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            rune.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rune.transform.localScale = new Vector3(trial.platformRadius * 1.55f, trial.platformRadius * 1.55f, 1f);
            var runeMat = VFXLibrary.MakeAdditive(VFXLibrary.MakeRing());
            runeMat.SetColor("_BaseColor", new Color(0.45f, 0.8f, 1f, 0.9f) * 1.3f);
            rune.GetComponent<MeshRenderer>().sharedMaterial = runeMat;

            // four braziers
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                var bowl = FlatCylinder("brazier", root.transform,
                    new Vector3(Mathf.Cos(a) * (trial.platformRadius - 6f), 0.5f, Mathf.Sin(a) * (trial.platformRadius - 6f)),
                    new Vector3(1.1f, 0.5f, 1.1f), trim);
                MakeFlame(bowl.transform, new Vector3(0f, 1.1f, 0f));
            }

            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = "pillar";
                pillar.transform.SetParent(root.transform, false);
                pillar.transform.localPosition = new Vector3(Mathf.Cos(a) * (trial.platformRadius - 1.2f), 2.4f, Mathf.Sin(a) * (trial.platformRadius - 1.2f));
                pillar.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                pillar.transform.localScale = new Vector3(1.1f, 5.2f, 1.1f);
                pillar.GetComponent<MeshRenderer>().sharedMaterial = stone;
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.name = "cap";
                Object.DestroyImmediate(cap.GetComponent<Collider>());
                cap.transform.SetParent(pillar.transform, false);
                cap.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                cap.transform.localScale = new Vector3(1.3f, 0.08f, 1.3f);
                cap.GetComponent<MeshRenderer>().sharedMaterial = trim;
            }

            var altar = new GameObject("altar");
            altar.transform.SetParent(root.transform, false);
            FlatCylinder("plinth", altar.transform, new Vector3(0f, 0.45f, 0f), new Vector3(2.6f, 0.45f, 2.6f), trim);
            var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crystal.name = "crystal";
            Object.DestroyImmediate(crystal.GetComponent<Collider>());
            crystal.transform.SetParent(altar.transform, false);
            crystal.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            crystal.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            crystal.transform.localScale = Vector3.one * 0.9f;
            var cm = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            cm.SetColor("_BaseColor", new Color(0.55f, 0.9f, 1f));
            cm.EnableKeyword("_EMISSION");
            cm.SetColor("_EmissionColor", new Color(0.4f, 0.8f, 1f) * 1.6f);
            crystal.GetComponent<MeshRenderer>().sharedMaterial = cm;
            var l = crystal.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.5f, 0.85f, 1f);
            l.range = 14f;
            l.intensity = 2.2f;
            return trial;
        }
    }
}
