using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    public enum EnemyKind { Melee, Fast, Ranged, Tank, Boss }

    /// Caps how many enemies may wind up an attack at once so group fights
    /// stay readable (GDD ch.4). Bosses bypass the cap.
    public static class AggroDirector
    {
        public const int MaxAttackers = 2;
        static readonly List<EnemyAI> _tokens = new List<EnemyAI>();

        public static bool Request(EnemyAI ai)
        {
            _tokens.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy || (t.Hp != null && !t.Hp.IsAlive));
            if (ai.isBoss) return true;
            if (_tokens.Contains(ai)) return true;
            if (_tokens.Count >= MaxAttackers) return false;
            _tokens.Add(ai);
            return true;
        }

        public static void Release(EnemyAI ai) { _tokens.Remove(ai); }
        public static void Reset() { _tokens.Clear(); }
    }

    /// Shadow AI. Red telegraph = dodge it, GOLD telegraph = parry window (hit them
    /// while gold to interrupt, WuWa-style). Variants: fast dual-strike, ranged
    /// caster, heavy tank with poise, arena boss with AoE slam.
    [RequireComponent(typeof(Health))]
    public class EnemyAI : MonoBehaviour
    {
        public static readonly List<EnemyAI> All = new List<EnemyAI>();

        [Header("Kind")]
        public EnemyKind kind = EnemyKind.Melee;
        public bool isBoss;

        [Header("Move")]
        public float moveSpeed = 3.6f;
        public float chaseRange = 22f;
        public float attackRange = 2.4f;
        public float turnSharpness = 8f;

        [Header("Attack")]
        public float attackDamage = 380f;
        public float attackKnockback = 7f;
        public float telegraphTime = 0.55f;
        public float attackCooldown = 2.4f;
        public float attackRadius = 1.9f;
        [Range(0f, 1f)] public float parryChance = 0.35f;
        public bool heavyPoise;                 // no flinch/knockback reaction

        [Header("Ranged")]
        public float preferredRange = 11f;
        public float projectileDamage = 300f;

        [Header("Anim state names")]
        public string stIdle = "Idle", stMove = "Move", stA1 = "A1", stA2 = "A2", stHit = "Hit", stDie = "Die", stStagger = "Stagger";

        Health _hp;
        Animator _anim;
        Transform _player;
        PlayerController _playerCtrl;
        CharacterController _cc;
        Vector3 _home;
        float _nextAttackTime;
        float _vy;
        bool _acting;
        bool _dead;
        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        float _wobbleSeed;
        Color _teleColor = Color.black;      // active telegraph tint, restored after hit flashes
        Color _baseGlow = Color.black;       // faint standing glow: super-armor indicator
        internal EnemyStatus Status;         // wired by EnemyStatus when first applied
        Transform _rig;
        Vector3 _rigBasePos;
        Coroutine _flashCo;
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public Health Hp { get { return _hp; } }
        public bool ParryOpen { get; private set; }
        static bool _pityGiven;              // first kill always drops an echo
        public static void ResetStatics() { _pityGiven = false; AggroDirector.Reset(); }
        bool _phase2;
        static readonly Collider[] _strikeBuf = new Collider[8];
        public bool IsAggro
        {
            get
            {
                if (_dead || _player == null) return false;
                return WuWaUtil.Flat(_player.position - transform.position).magnitude < chaseRange + 4f;
            }
        }

        void Awake()
        {
            _hp = GetComponent<Health>();
            _anim = GetComponentInChildren<Animator>();
            _cc = GetComponent<CharacterController>();
            _home = transform.position;
            _renderers = GetComponentsInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
            _wobbleSeed = Random.value * 7f;
            if (_anim != null) { _rig = _anim.transform; _rigBasePos = _rig.localPosition; }

            _hp.OnDamaged += OnDamaged;
            _hp.OnStaggered += OnStaggered;
            _hp.OnDied += OnDied;
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Start()
        {
            var p = PlayerController.Instance != null ? PlayerController.Instance : Object.FindAnyObjectByType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCtrl = p; }
            _baseGlow = heavyPoise ? new Color(0.32f, 0.07f, 0.05f) : Color.black;   // super-armor tint
            SetTelegraph(_baseGlow);
            WuWaUtil.Fade(_anim, stIdle, 0.1f);
        }

        void Update()
        {
            if (_dead || _player == null) return;
            if (Cutscene.Active) return;          // freeze during cinematics
            float dt = Time.deltaTime;

            if (isBoss && !_phase2 && _hp != null && _hp.hp <= _hp.maxHp * 0.5f) EnterPhase2();

            if (_cc != null && _cc.enabled)
                _vy = _cc.isGrounded ? -2f : Mathf.Max(_vy - 24f * dt, -30f);

            if (_acting)
            {
                if (_cc != null && _cc.enabled) _cc.Move(Vector3.up * _vy * dt);
                return;
            }

            Vector3 to = _player.position - transform.position;
            float dist = WuWaUtil.Flat(to).magnitude;
            bool playerAlive = _playerCtrl == null || _playerCtrl.IsAlive;

            Vector3 move = Vector3.zero;
            if (dist < chaseRange && playerAlive)
            {
                FaceTowards(to, dt);
                if (kind == EnemyKind.Ranged)
                    move = RangedBrain(to, dist);
                else
                    move = MeleeBrain(to, dist);
            }
            else
            {
                Vector3 toHome = WuWaUtil.Flat(_home - transform.position);
                if (toHome.magnitude > 2f)
                {
                    move = toHome.normalized * moveSpeed * 0.6f;
                    FaceTowards(toHome, dt);
                    WuWaUtil.Fade(_anim, stMove, 0.2f);
                }
                else WuWaUtil.Fade(_anim, stIdle, 0.25f);
            }

            // enemies do not follow into deep water: diving is the player's escape
            if (move.sqrMagnitude > 0.01f)
            {
                Vector3 ahead = transform.position + move.normalized * 0.9f;
                if (WorldRegions.HeightAt(ahead.x, ahead.z) < WorldRegions.WaterY - 0.6f)
                {
                    move = Vector3.zero;
                    WuWaUtil.Fade(_anim, stIdle, 0.25f);
                }
            }

            if (Status != null) move *= Status.MoveMul;   // frost slow
            if (_cc != null && _cc.enabled) _cc.Move((move + Vector3.up * _vy) * dt);
        }

        Vector3 MeleeBrain(Vector3 to, float dist)
        {
            if (dist > attackRange * 0.85f)
            {
                WuWaUtil.Fade(_anim, stMove, 0.15f);
                return WuWaUtil.Flat(to).normalized * moveSpeed;
            }
            if (Time.time >= _nextAttackTime && AggroDirector.Request(this))
            {
                StartCoroutine(AttackRoutine());
                return Vector3.zero;
            }
            Vector3 side = Vector3.Cross(Vector3.up, WuWaUtil.Flat(to).normalized);
            WuWaUtil.Fade(_anim, stIdle, 0.2f);
            return side * (Mathf.PingPong(Time.time * 0.7f + _wobbleSeed, 2f) - 1f) * moveSpeed * 0.35f;
        }

        Vector3 RangedBrain(Vector3 to, float dist)
        {
            Vector3 flat = WuWaUtil.Flat(to).normalized;
            if (dist < preferredRange - 3f)
            {
                WuWaUtil.Fade(_anim, stMove, 0.2f);
                return -flat * moveSpeed;                       // back off
            }
            if (dist > preferredRange + 4f)
            {
                WuWaUtil.Fade(_anim, stMove, 0.2f);
                return flat * moveSpeed;
            }
            if (Time.time >= _nextAttackTime && AggroDirector.Request(this))
            {
                StartCoroutine(ShootRoutine());
                return Vector3.zero;
            }
            Vector3 side = Vector3.Cross(Vector3.up, flat);
            WuWaUtil.Fade(_anim, stIdle, 0.25f);
            return side * Mathf.Sin(Time.time * 0.8f + _wobbleSeed) * moveSpeed * 0.4f;
        }

        void FaceTowards(Vector3 dir, float dt)
        {
            dir = WuWaUtil.Flat(dir);
            if (dir.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir.normalized), 1f - Mathf.Exp(-turnSharpness * dt));
        }

        public void DbgForceAttack(bool gold)
        {
            if (_dead) return;
            StopAllCoroutines();
            _acting = false;
            ParryOpen = false;
            _teleColor = Color.black;
            ResetRigVisual();
            StartCoroutine(AttackRoutine(gold ? 1 : 0));
        }

        Vector3 HeadPos { get { return transform.position + Vector3.up * (_cc != null ? _cc.height * 0.95f : 1.7f); } }

        IEnumerator AttackRoutine(int forceGold = -1)
        {
            _acting = true;
            _nextAttackTime = Time.time + attackCooldown * Random.Range(0.85f, 1.25f);

            bool slam = isBoss && Random.value < (_phase2 ? 0.62f : 0.4f);
            bool gold = forceGold == 1 || (forceGold == -1 && Random.value < parryChance);

            // telegraph: red = dodge, gold = parry window (WuWa converging circles)
            Color teleCol = gold ? Palette.Parry : Palette.Dodge;
            SetTelegraph(teleCol * 2.2f);
            float teleDur = telegraphTime * (gold ? 1.35f : 1f) * Palette.TelegraphMul;
            if (gold)
            {
                ParryOpen = true;
                VFXLibrary.SpawnParryTelegraph(transform, HeadPos - transform.position, teleDur);
            }
            float t = 0f;
            while (t < teleDur)
            {
                t += Time.deltaTime;
                if (_player != null) FaceTowards(_player.position - transform.position, Time.deltaTime);
                yield return null;
            }
            SetTelegraph(_baseGlow);
            ParryOpen = false;

            WuWaUtil.Fade(_anim, Random.value < 0.5f ? stA1 : stA2, 0.05f);
            AudioMan.I.Play(Sfx.Whoosh(), transform.position, 0.6f, slam ? 0.6f : 0.85f);

            float lungeT = 0f;
            Vector3 lungeDir = transform.forward;
            float lungeSpd = kind == EnemyKind.Fast ? 9f : (slam || kind == EnemyKind.Tank ? 2.5f : 6.5f);
            while (lungeT < 0.22f)
            {
                lungeT += Time.deltaTime;
                if (_cc != null && _cc.enabled) _cc.Move(lungeDir * lungeSpd * Time.deltaTime);
                yield return null;
            }

            StrikeOnce(slam);

            // fast shadows follow up with a second slash
            if (kind == EnemyKind.Fast && !_dead)
            {
                yield return new WaitForSeconds(0.3f);
                WuWaUtil.Fade(_anim, stA2, 0.04f);
                AudioMan.I.Play(Sfx.Whoosh(), transform.position, 0.55f, 1.15f);
                float l2 = 0f;
                while (l2 < 0.16f)
                {
                    l2 += Time.deltaTime;
                    if (_cc != null && _cc.enabled) _cc.Move(transform.forward * 7f * Time.deltaTime);
                    yield return null;
                }
                StrikeOnce(false);
            }

            yield return new WaitForSeconds(kind == EnemyKind.Fast ? 0.3f : 0.5f);
            _acting = false;
            AggroDirector.Release(this);
        }

        /// Glacio freeze: full stop, iced tint, animator paused.
        public void ApplyFreeze(float duration)
        {
            if (_dead) return;
            StopAllCoroutines();
            ParryOpen = false;
            AggroDirector.Release(this);
            ResetRigVisual();
            StartCoroutine(FreezeRoutine(duration));
        }

        IEnumerator FreezeRoutine(float duration)
        {
            _acting = true;
            if (_anim != null) _anim.speed = 0f;
            SetTelegraph(new Color(0.45f, 0.8f, 1f) * 1.3f);
            yield return new WaitForSeconds(duration);
            if (_anim != null) _anim.speed = 1f;
            SetTelegraph(_baseGlow);
            _acting = false;
        }

        void StrikeOnce(bool slam)
        {
            float radius = slam ? attackRadius * 2.6f : attackRadius;
            Vector3 center = slam ? transform.position : transform.position + Vector3.up * 1.0f + transform.forward * (attackRange * 0.7f);
            if (slam)
            {
                VFXLibrary.SpawnNova(transform.position, new Color(1f, 0.25f, 0.2f), radius);
                CameraShaker.Add(0.4f);
                AudioMan.I.Play(Sfx.Ult(), transform.position, 0.5f, 1.4f);
            }
            int n = Physics.OverlapSphereNonAlloc(center, radius, _strikeBuf, Layers.PlayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var d = _strikeBuf[i].GetComponentInParent<PlayerController>();
                if (d != null)
                {
                    d.TakeDamage(new DamageInfo
                    {
                        amount = attackDamage * (slam ? 1.6f : 1f) * Random.Range(0.9f, 1.1f),
                        crit = false,
                        element = Element.Havoc,
                        sourcePos = transform.position,
                        knockback = attackKnockback * (slam ? 1.6f : 1f),
                        staggerPower = 0f,
                        source = gameObject
                    });
                    break;
                }
            }
        }

        IEnumerator ShootRoutine()
        {
            _acting = true;
            _nextAttackTime = Time.time + attackCooldown * Random.Range(0.9f, 1.3f);
            SetTelegraph(new Color(0.75f, 0.3f, 1f) * 2.2f);
            float t = 0f;
            while (t < telegraphTime)
            {
                t += Time.deltaTime;
                if (_player != null) FaceTowards(_player.position - transform.position, Time.deltaTime);
                yield return null;
            }
            SetTelegraph(_baseGlow);
            WuWaUtil.Fade(_anim, stA1, 0.05f);
            if (_player != null)
            {
                Vector3 muzzle = transform.position + Vector3.up * 1.3f + transform.forward * 0.6f;
                Vector3 target = _player.position + Vector3.up * 1.0f;
                EnemyProjectile.Fire(muzzle, target, projectileDamage);
                if (isBoss || Random.value < 0.35f)
                {
                    yield return new WaitForSeconds(0.28f);
                    if (_player != null)
                        EnemyProjectile.Fire(muzzle, _player.position + Vector3.up * 1.0f, projectileDamage);
                }
            }
            yield return new WaitForSeconds(0.4f);
            _acting = false;
            AggroDirector.Release(this);
        }

        /// Player struck us during a gold window: interrupt + hard stagger (WuWa parry).
        public void GetParried(Vector3 fromPos)
        {
            if (_dead || !ParryOpen) return;
            Debug.Log("[WuWa] PARRIED: " + _hp.displayName);
            ParryOpen = false;
            StopAllCoroutines();
            AggroDirector.Release(this);
            _teleColor = _baseGlow;
            ResetRigVisual();
            _nextAttackTime = Time.time + attackCooldown + 1.5f;
            VFXLibrary.SpawnParryFlash(transform.position + Vector3.up * 1.2f);
            AudioMan.I.Play2D(Sfx.HitCrit(), 0.9f, 0.75f);
            DamageNumbers.SpawnText(transform.position + Vector3.up * 2.1f, "PARRY!", new Color(1f, 0.85f, 0.3f));
            HUDController.Toast("패리 성공!");
            _hp.stagger = Mathf.Min(_hp.stagger, _hp.maxStagger * 0.15f);
            StartCoroutine(StaggerRoutine());
        }

        void OnDamaged(DamageInfo info)
        {
            if (_dead) return;
            DamageNumbers.Spawn(transform.position + Vector3.up * (isBoss ? 2.6f : 1.7f), info.amount, info.crit, ElementInfo.Of(info.element));
            HUDController.PingEnemy(_hp);

            // impact feedback: white flash + rig shake on every hit (even poise enemies)
            if (_flashCo != null) StopCoroutine(_flashCo);
            _flashCo = StartCoroutine(HitFlashRoutine(info.crit));

            if (!_acting && !isBoss && !heavyPoise)
            {
                Vector3 dir = WuWaUtil.Flat(transform.position - info.sourcePos).normalized;
                StartCoroutine(KnockRoutine(dir * info.knockback));
                WuWaUtil.Fade(_anim, stHit, 0.05f);
            }
        }

        IEnumerator HitFlashRoutine(bool crit)
        {
            SetEmission(Color.white * (crit ? 2.4f : 1.7f));
            float t = 0f;
            float dur = crit ? 0.16f : 0.12f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                if (_rig != null)
                {
                    float k = 1f - t / dur;
                    _rig.localPosition = _rigBasePos + (Vector3)(Random.insideUnitCircle * 0.055f * k);
                }
                yield return null;
            }
            ResetRigVisual();
        }

        void ResetRigVisual()
        {
            if (_rig != null) _rig.localPosition = _rigBasePos;
            SetEmission(_teleColor);
        }

        IEnumerator KnockRoutine(Vector3 vel)
        {
            float t = 0f;
            while (t < 0.18f)
            {
                t += Time.deltaTime;
                if (_cc != null && _cc.enabled) _cc.Move(vel * Time.deltaTime * (1f - t / 0.18f));
                yield return null;
            }
        }

        void OnStaggered()
        {
            if (_dead) return;
            StopAllCoroutines();
            ParryOpen = false;
            AggroDirector.Release(this);
            if (_anim != null) _anim.speed = 1f;
            ResetRigVisual();
            StartCoroutine(StaggerRoutine());
        }

        IEnumerator StaggerRoutine()
        {
            _acting = true;
            WuWaUtil.Fade(_anim, stStagger, 0.08f);
            HUDController.Toast(_hp.displayName + " 그로기!");
            VFXLibrary.SpawnPerfectDodge(transform.position + Vector3.up * 1.2f, new Color(1f, 0.9f, 0.4f));
            SetTelegraph(new Color(1f, 0.8f, 0.2f) * 1.4f);
            yield return new WaitForSeconds(isBoss ? 3.2f : 2.0f);
            SetTelegraph(_baseGlow);
            _acting = false;
        }

        void OnDied()
        {
            if (_dead) return;
            _dead = true;
            ParryOpen = false;
            StopAllCoroutines();
            StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            _acting = true;
            AggroDirector.Release(this);
            if (_anim != null) _anim.speed = 1f;
            _teleColor = Color.black;
            ResetRigVisual();
            WuWaUtil.Fade(_anim, stDie, 0.1f);
            AudioMan.I.Play(Sfx.EnemyDie(), transform.position, 0.85f, isBoss ? 0.7f : 1f);
            VFXLibrary.SpawnHitSpark(transform.position + Vector3.up, new Color(0.8f, 0.4f, 1f), 1.4f);
            if (_cc != null) _cc.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

            if (isBoss) GameDirector.I.BossDefeated();

            // finisher beat: last aggroed shadow nearby dies -> brief slow-mo punch
            if (!isBoss)
            {
                bool lastOne = true;
                for (int i = 0; i < All.Count; i++)
                {
                    var o = All[i];
                    if (o == null || o == this || o.Hp == null || !o.Hp.IsAlive || !o.gameObject.activeInHierarchy) continue;
                    if (o.IsAggro && WuWaUtil.Flat(o.transform.position - transform.position).magnitude < 26f)
                    {
                        lastOne = false;
                        break;
                    }
                }
                if (lastOne && IsAggro)
                {
                    Hitstop.I.SlowMo(0.28f, 0.5f, 0.3f);
                    ThirdPersonCamera.PunchFov(-6f, 0.5f);
                    VFXLibrary.SpawnNova(transform.position + Vector3.up * 0.8f, new Color(1f, 0.92f, 0.6f), 4.5f);
                }
            }

            // growth + quest hooks
            float regionMul = DropTables.RegionMul(WorldRegions.RegionAt(transform.position.x, transform.position.z));
            if (ProgressSystem.I != null) ProgressSystem.I.AddKill(kind, regionMul);
            DropTables.RollKill(kind, transform.position, isBoss);
            CombatScore.NotifyKill();
            {
                int killRegion = WorldRegions.RegionAt(transform.position.x, transform.position.z);
                Codex.NotifyKill(kind, isBoss, false); ContentStats.Kills++;
                BountyBoard.NotifyKill(kind, killRegion, isBoss);
            }
            if (QuestSystem.I != null)
            {
                // kills carry their region so chapter-2 steps can filter by area
                QuestSystem.I.Notify(QuestEvent.Kill, WorldRegions.RegionAt(transform.position.x, transform.position.z));
                if (isBoss) QuestSystem.I.Notify(QuestEvent.Boss);
            }

            // echo drop: elites/boss always, small shadows 20% (first kill pity)
            int echoId = -1;
            if (Random.value < EchoDB.DropChance(kind) || !_pityGiven)
            {
                echoId = EchoDB.IdForKind(kind);
                _pityGiven = true;
            }
            EchoOrb.SpawnAt(transform.position + Vector3.up * 0.6f, isBoss ? 3 : 1, echoId);

            // weapon drop: elites 15% tuning sword, boss guarantees the relic blade
            if (WeaponSystem.I != null)
            {
                if (isBoss) WeaponSystem.I.Add(2);
                else if ((kind == EnemyKind.Ranged || kind == EnemyKind.Tank) && Random.value < 0.15f)
                    WeaponSystem.I.Add(1);
            }

            yield return new WaitForSeconds(1.0f);

            // dissolve: the shadow twists thin and streams upward as dark motes
            Vector3 s0 = transform.localScale;
            float t = 0f;
            const float dur = 0.9f;
            float nextMote = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float thin = Mathf.Max(0.02f, 1f - k * k);
                transform.localScale = new Vector3(s0.x * thin, s0.y * (1f + k * 0.7f), s0.z * thin);
                transform.Rotate(0f, 260f * Time.deltaTime, 0f, Space.World);
                transform.position += Vector3.up * 0.9f * Time.deltaTime;
                if (t > nextMote)
                {
                    nextMote = t + 0.12f;
                    VFXLibrary.Flash(transform.position + Vector3.up * (0.5f + k * 1.2f),
                        new Color(0.45f, 0.3f, 0.7f), 0.9f, 0.25f);
                }
                yield return null;
            }
            VFXLibrary.Flash(transform.position + Vector3.up * 1.4f, new Color(0.7f, 0.5f, 1f), 1.6f, 0.3f);
            Destroy(gameObject);
        }

        /// Rift/arena elites: crimson standing glow + poise.
        public void MarkElite()
        {
            heavyPoise = true;
            _baseGlow = new Color(0.55f, 0.08f, 0.16f);
            SetTelegraph(_baseGlow);
        }

        /// Boss at half health: faster, angrier, and it calls two shadows to its side.
        void EnterPhase2()
        {
            _phase2 = true;
            attackCooldown *= 0.72f;
            moveSpeed *= 1.22f;
            telegraphTime *= 0.88f;
            _baseGlow = new Color(0.6f, 0.1f, 0.22f);
            SetTelegraph(_baseGlow);
            HUDController.Toast(_hp.displayName + " — 2페이즈! 그림자가 포효한다");
            CameraShaker.Add(0.7f);
            Hitstop.I.SlowMo(0.3f, 0.5f, 0.3f);
            VFXLibrary.SpawnNova(transform.position + Vector3.up * 0.5f, new Color(1f, 0.2f, 0.35f), 7f, true);
            AudioMan.I.Play(Sfx.Ult(), transform.position, 0.9f, 0.55f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = transform.position + Quaternion.Euler(0f, i == 0 ? -70f : 70f, 0f) * transform.forward * 4f;
                EnemyRegistry.Spawn(EnemyKind.Fast, p, 0.8f);
            }
        }

        void SetTelegraph(Color c)
        {
            _teleColor = c;
            SetEmission(c);
        }

        void SetEmission(Color c)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                _renderers[i].GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColorId, c);
                _renderers[i].SetPropertyBlock(_mpb);
            }
        }
    }
}
