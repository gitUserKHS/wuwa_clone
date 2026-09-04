using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WuWa
{
    public enum AttackCat { Basic, Heavy, Skill, Ult, Dash, Plunge, Intro, EchoActive }

    /// WuWa-style kit: combo + charged heavy (Forte-empowered), plunge and dash
    /// attacks, resonance skill/liberation, echo actives, parry, dodge counter,
    /// full-Concerto intro/outro swaps, and M2 elemental application:
    /// Spectro mark · Glacio freeze stacks · Fusion burn.
    public class PlayerCombat : MonoBehaviour
    {
        public float comboResetTime = 1.1f;
        public float echoDamageMul = 3.2f;
        public float heavyHoldTime = 0.45f;
        public float counterWindow = 2.2f;
        public float counterDamageMul = 1.5f;

        TeamManager _team;
        PlayerController _ctrl;

        int _comboIndex;
        float _lastAttackEnd;
        Coroutine _attackRoutine;
        bool _busy;
        bool _canCancel;
        bool _queued;
        bool _heavyQueued;
        float _queuedAt;
        public static float BufferTime = 0.5f;      // input buffer window (timing assist widens it)
        int _swingIndex;
        static readonly Collider[] _hitBuf = new Collider[32];
        readonly System.Collections.Generic.HashSet<Health> _counted = new System.Collections.Generic.HashSet<Health>();
        float _echoCdLeft;
        float _echoCdMax = 14f;
        float _lmbDownTime = -10f;
        bool _heavyFiredThisHold;

        // outro buff carried by the incoming member after a full-Concerto swap
        OutroType _outroType;
        float _outroMul = 1f;
        float _outroUntil;

        public bool IsBusy { get { return _busy; } }
        public float EchoCdLeft { get { return _echoCdLeft; } }
        public float EchoCooldown { get { return _echoCdMax; } }
        public bool CounterReady { get { return Time.time - _ctrl.LastPerfectDodge < counterWindow; } }
        public bool OutroBuffActive { get { return Time.time < _outroUntil; } }
        public OutroType ActiveOutro { get { return _outroType; } }

        void Awake()
        {
            _team = GetComponent<TeamManager>();
            _ctrl = GetComponent<PlayerController>();
        }

        void Update()
        {
            if (Time.timeScale <= 0.001f) return;
            if (_team == null || _team.Active == null) return;
            _echoCdLeft = Mathf.Max(0f, _echoCdLeft - Time.deltaTime);
            _team.TickAll(Time.deltaTime);

            if (GameDirector.CursorFree || GameDirector.MenuOpen) return;
            if (_ctrl.IsWallRunning || _ctrl.IsGrappling || _ctrl.IsSwimming) return;

            bool atkDown = InputService.AttackPressed;
            bool atkHeld = InputService.AttackHeld;
            bool skill = InputService.SkillPressed;
            bool ult = InputService.UltPressed;
            bool echo = InputService.EchoPressed;
            if (InputService.HeavyPressed) { _heavyFiredThisHold = true; TryHeavy(); }

            if (atkDown)
            {
                _lmbDownTime = Time.time;
                _heavyFiredThisHold = false;
                TryBasic();
            }
            else if (atkHeld && !_heavyFiredThisHold && Time.time - _lmbDownTime >= heavyHoldTime)
            {
                _heavyFiredThisHold = true;
                TryHeavy();
            }
            if (!atkHeld) _heavyFiredThisHold = false;

            if (skill) TrySkill();
            else if (ult) TryUlt();
            else if (echo) TryEcho();
        }

        // ---------------------------------------------------------------- entries
        public void TryBasic()
        {
            if (_busy)
            {
                _queued = true;                 // input buffer: consumed at the cancel window
                _queuedAt = Time.time;
                return;
            }

            var m = _team.Active;

            if (!_ctrl.IsGrounded && !_ctrl.IsGliding && _ctrl.HeightAboveGround() > 2.2f)
            {
                _attackRoutine = StartCoroutine(PlungeRoutine(m));
                return;
            }
            if (!_ctrl.IsGrounded && !_ctrl.IsGliding) return;

            if (_ctrl.IsDodging || Time.time - _ctrl.DodgeEndTime < 0.28f)
            {
                _comboIndex = 0;
                _swingIndex = 0;
                _attackRoutine = StartCoroutine(AttackRoutine(m.dashAtk, AttackCat.Dash, forteGain: 8f));
                return;
            }

            if (Time.time - _lastAttackEnd > comboResetTime) _comboIndex = 0;
            if (m.combo == null || m.combo.Length == 0) return;
            _swingIndex = Mathf.Min(_comboIndex, m.combo.Length - 1);
            var def = m.combo[_swingIndex];
            _comboIndex = (_comboIndex + 1) % Mathf.Max(1, m.combo.Length);
            _attackRoutine = StartCoroutine(AttackRoutine(def, AttackCat.Basic, forteGain: m.forteGainPerHit));
        }

        public void TryHeavy()
        {
            if (_busy)
            {
                _heavyQueued = true;
                _queuedAt = Time.time;
                return;
            }
            if (!_ctrl.IsGrounded) return;
            var m = _team.Active;
            _comboIndex = 0;
            _swingIndex = 0;
            bool empowered = m.ForteReady;
            if (empowered)
            {
                m.forte = 0f;
                HUDController.Toast("공명 회로 해방!");
                HUDController.NotifyResources();
            }
            _attackRoutine = StartCoroutine(AttackRoutine(m.heavy, AttackCat.Heavy, empowered: empowered));
        }

        public void TrySkill()
        {
            var m = _team.Active;
            if (_busy || m.skillCdLeft > 0.01f) return;
            float cd = m.skillCooldown;
            if (OutroBuffActive && _outroType == OutroType.SkillHaste) cd *= _outroMul;   // e.g. ×0.8
            m.skillCdLeft = cd;
            GainConcertoScaled(m,12f);
            _comboIndex = 0;
            _attackRoutine = StartCoroutine(AttackRoutine(m.skill, AttackCat.Skill));
            HUDController.NotifyResources();
        }

        public void TryUlt()
        {
            var m = _team.Active;
            if (_busy || !m.UltReady) { if (!m.UltReady) HUDController.Toast("공명 에너지가 부족합니다"); return; }
            m.energy = 0f;
            GainConcertoScaled(m,20f);
            _comboIndex = 0;
            _attackRoutine = StartCoroutine(AttackRoutine(m.ult, AttackCat.Ult));
            HUDController.NotifyResources();
        }

        public void TryEcho()
        {
            if (_busy || _echoCdLeft > 0.01f) return;
            var active = EchoSystem.I != null ? EchoSystem.I.MainEchoOf(_team.ActiveIndex) : null;
            _echoCdMax = active == null ? 14f : (active.star >= 5 ? 18f : active.star >= 3 ? 14f : 10f);
            _echoCdLeft = _echoCdMax;
            if (active == null) StartCoroutine(DefaultEchoRoutine());
            else StartCoroutine(EchoActiveRoutine(active));
            HUDController.NotifyResources();
        }

        // ---------------------------------------------------------------- targeting
        Transform FindAttackTarget(float maxDist, float maxAngle)
        {
            if (_ctrl.LockOn != null && _ctrl.LockOn.Target != null) return _ctrl.LockOn.Target;
            Vector3 baseDir = CamInput();
            if (baseDir.sqrMagnitude < 0.01f) baseDir = transform.forward;
            Transform best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < EnemyAI.All.Count; i++)
            {
                var e = EnemyAI.All[i];
                if (e == null || e.Hp == null || !e.Hp.IsAlive || !e.gameObject.activeInHierarchy) continue;
                Vector3 to = WuWaUtil.Flat(e.transform.position - transform.position);
                float d = to.magnitude;
                if (d > maxDist) continue;
                float ang = Vector3.Angle(baseDir, to);
                if (ang > maxAngle && d > 3.5f) continue;
                float score = d + ang * 0.06f;
                if (score < bestScore) { bestScore = score; best = e.transform; }
            }
            return best;
        }

        Vector3 CamInput()
        {
            var cam = CamCache.Main;
            Vector3 fwd = cam != null ? WuWaUtil.Flat(cam.transform.forward).normalized : Vector3.forward;
            Vector3 right = cam != null ? WuWaUtil.Flat(cam.transform.right).normalized : Vector3.right;
            return fwd * _ctrl.MoveInput.y + right * _ctrl.MoveInput.x;
        }

        // ---------------------------------------------------------------- core
        IEnumerator AttackRoutine(AttackDef def, AttackCat cat, float forteGain = 0f, bool empowered = false)
        {
            bool isUlt = cat == AttackCat.Ult;
            _busy = true;
            _canCancel = false;
            _queued = false;
            _heavyQueued = false;
            _ctrl.MovementLock = 1f;

            var m = _team.Active;
            var anim = m.Anim;

            bool tracking = def.vfx <= 1 && !isUlt;
            Transform aimTarget = tracking ? FindAttackTarget(8.5f, 85f) : null;
            if (aimTarget != null)
                _ctrl.FaceInstant(aimTarget.position - transform.position);
            else
            {
                Vector3 wish = CamInput();
                if (wish.sqrMagnitude > 0.01f) _ctrl.FaceInstant(wish);
            }

            if (anim != null) anim.speed = def.speed;
            WuWaUtil.Fade(anim, def.state, 0.05f);
            AudioMan.I.Play(Sfx.Whoosh(), transform.position, 0.5f, isUlt ? 0.7f : 1f + _swingIndex * 0.06f);

            if (isUlt)
            {
                Hitstop.I.SlowMo(0.3f, 0.55f, 0.25f);
                CameraShaker.Add(0.5f);
                ThirdPersonCamera.PunchFov(9f, 0.7f);
                VFXLibrary.SpawnUltFlash(transform.position, m.themeColor);
            }
            if (empowered)
            {
                Hitstop.I.SlowMo(0.4f, 0.3f, 0.2f);
                VFXLibrary.SpawnUltFlash(transform.position, new Color(1f, 0.85f, 0.35f));
            }

            float dur = Mathf.Max(0.2f, def.clipLen / Mathf.Max(0.2f, def.speed));
            float hitAt = dur * Mathf.Clamp01(def.hitTime);
            bool finisher = cat == AttackCat.Basic && m.combo != null && _swingIndex == m.combo.Length - 1;
            float cancelAt = dur * (finisher ? 0.8f : 0.62f);      // the finisher commits — no early chain
            float endAt = dur * 0.92f;

            if (aimTarget == null) _ctrl.AddImpulse(transform.forward * def.lunge);

            float t = 0f;
            bool hitDone = false;
            while (t < endAt)
            {
                t += Time.deltaTime;
                if (aimTarget != null && t < hitAt)
                {
                    float d = WuWaUtil.Flat(aimTarget.position - transform.position).magnitude;
                    if (d > def.range * 0.8f && d < 9.5f)
                        _ctrl.AttackTrack(aimTarget.position, Time.deltaTime);
                }
                if (!hitDone && t >= hitAt)
                {
                    hitDone = true;
                    DoHit(def, cat, empowered ? 1.85f : 1f, empowered);
                    if (finisher) { CameraShaker.Add(0.22f); ThirdPersonCamera.PunchFov(3.5f, 0.35f); }
                    if (forteGain > 0f) { m.GainForte(forteGain); HUDController.NotifyResources(); }
                }
                if (t >= cancelAt)
                {
                    _canCancel = true;
                    if ((_queued || _heavyQueued) && Time.time - _queuedAt > BufferTime) { _queued = false; _heavyQueued = false; }   // stale buffer
                    if (_queued || _heavyQueued) break;
                }
                yield return null;
            }

            if (anim != null) anim.speed = 1f;
            _busy = false;
            _canCancel = false;
            _ctrl.MovementLock = 0f;
            _lastAttackEnd = Time.time;

            if (_heavyQueued) { _heavyQueued = false; TryHeavy(); }
            else if (_queued) { _queued = false; TryBasic(); }
            else WuWaUtil.Fade(anim, _ctrl.IsGrounded ? "Loco" : "Fall", 0.16f);
        }

        IEnumerator PlungeRoutine(MemberConfig m)
        {
            _busy = true;
            _ctrl.MovementLock = 1f;
            _ctrl.BeginPlunge();
            var anim = m.Anim;
            WuWaUtil.Fade(anim, "Plunge", 0.05f);
            AudioMan.I.Play(Sfx.Whoosh(), transform.position, 0.6f, 0.8f);

            float t = 0f;
            while (!_ctrl.IsGrounded && t < 2.6f)
            {
                t += Time.deltaTime;
                _ctrl.PlungeFall(Time.deltaTime);
                yield return null;
            }

            _ctrl.EndPlunge();
            var def = m.plunge;
            VFXLibrary.SpawnPlungeImpact(transform.position, m.themeColor, def.radius);
            CameraShaker.Add(0.55f);
            Hitstop.I.Freeze(0.06f);
            AudioMan.I.Play(Sfx.Ult(), transform.position, 0.55f, 1.6f);
            DoHit(def, AttackCat.Plunge, 1f, false, aroundSelf: true);
            m.GainForte(10f);
            HUDController.NotifyResources();

            yield return new WaitForSeconds(0.28f);
            _busy = false;
            _ctrl.MovementLock = 0f;
            _lastAttackEnd = Time.time;
            WuWaUtil.Fade(anim, "Loco", 0.15f);
        }

        // ---------------------------------------------------------------- damage
        float CritChanceOf(MemberConfig m)
        {
            float bonus = WeaponSystem.I != null ? WeaponSystem.I.CritRateBonusFor(_team.ActiveIndex) : 0f;
            return m.EffCrit + bonus;
        }

        void GainConcertoScaled(MemberConfig m, float amount)
        {
            float mul = WeaponSystem.I != null ? WeaponSystem.I.ConcertoMulFor(_team.ActiveIndex) : 1f;
            if (EchoSystem.I != null) mul *= EchoSystem.I.ConcertoMulFor(_team.ActiveIndex);
            m.GainConcerto(amount * mul);
        }

        float TotalOutgoingMul(MemberConfig m, AttackCat cat)
        {
            float mul = BuffSystem.AtkMul;                                     // 노래풀 구이
            if (ProgressSystem.I != null) mul *= ProgressSystem.I.SkillMul(_team.ActiveIndex, cat);   // 스킬 레벨
            if (OutroBuffActive && _outroType == OutroType.DamageUp) mul *= _outroMul;
            if (OutroBuffActive && _outroType == OutroType.HeavyUp && cat == AttackCat.Heavy) mul *= _outroMul;
            if (EchoSystem.I != null)
            {
                int idx = _team.ActiveIndex;
                mul *= EchoSystem.I.DamageMulFor(idx);
                if (cat == AttackCat.Skill || cat == AttackCat.Ult || cat == AttackCat.EchoActive)
                    mul *= EchoSystem.I.SkillDamageMulFor(idx);
            }
            if (WeaponSystem.I != null && (cat == AttackCat.Skill || cat == AttackCat.Ult || cat == AttackCat.EchoActive))
                mul *= WeaponSystem.I.SkillDmgMulFor(_team.ActiveIndex);
            return mul;
        }

        /// M2 elemental application rules (GDD ch.4/5).
        void ApplyElement(MemberConfig m, Health h, AttackCat cat)
        {
            var st = EnemyStatus.Of(h);
            switch (m.element)
            {
                case Element.Spectro:
                    if (cat == AttackCat.Skill || cat == AttackCat.Ult || cat == AttackCat.Intro)
                        st.ApplySpectroMark();
                    break;
                case Element.Glacio:
                    if (cat == AttackCat.Basic || cat == AttackCat.Dash || cat == AttackCat.Plunge)
                        st.ApplyGlacioStack();
                    else if (cat == AttackCat.Ult)
                        st.TriggerFreeze(EnemyStatus.FreezeDuration);
                    else if (cat == AttackCat.Intro)
                        st.ApplySlow(0.6f, 3f);
                    break;
                case Element.Fusion:
                    if (cat == AttackCat.Basic || cat == AttackCat.Heavy || cat == AttackCat.Dash ||
                        cat == AttackCat.Plunge || cat == AttackCat.Intro)
                        st.ApplyBurn(m.EffAtk * 0.25f);
                    break;
            }
        }

        void DoHit(AttackDef def, AttackCat cat, float extraMul = 1f, bool empowered = false, bool aroundSelf = false)
        {
            bool isUlt = cat == AttackCat.Ult;
            var m = _team.Active;
            bool aoe360 = def.vfx >= 2 || aroundSelf;
            float radius = def.radius * (empowered ? 1.3f : 1f);
            Vector3 center = aoe360
                ? transform.position + Vector3.up * 0.9f
                : transform.position + Vector3.up * 1.0f + transform.forward * def.range;

            Color fxColor = empowered ? new Color(1f, 0.85f, 0.35f) : m.themeColor;
            switch (def.vfx)
            {
                case 0: VFXLibrary.SpawnSlash(center, transform.rotation, fxColor, _swingIndex); break;
                case 1: VFXLibrary.SpawnHeavySlash(center, transform.rotation, fxColor); break;
                case 2: if (!aroundSelf) VFXLibrary.SpawnNova(transform.position, fxColor, radius); break;
                case 3: VFXLibrary.SpawnNova(transform.position, fxColor, radius, true); break;
            }
            if (empowered) VFXLibrary.SpawnNova(transform.position, fxColor, radius);

            // Yuki's E leaves a lingering frost prison
            if (cat == AttackCat.Skill && m.element == Element.Glacio)
                FrostField.Spawn(transform.position, radius, 5f);

            bool counter = CounterReady;
            float counterMul = 1f;
            if (counter)
            {
                counterMul = counterDamageMul;
                _ctrl.ConsumeCounter();
                VFXLibrary.SpawnCounterFlash(transform.position + Vector3.up * 1.2f, fxColor);
                HUDController.Toast("회피 반격!");
            }

            float buffMul = TotalOutgoingMul(m, cat);

            int hitCount = Physics.OverlapSphereNonAlloc(center, radius, _hitBuf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            var hits = _hitBuf;
            bool connected = false;
            bool parried = false;
            var counted = _counted;
            counted.Clear();
            for (int i = 0; i < hitCount; i++)
            {
                var h = hits[i].GetComponentInParent<Health>();
                if (h == null || !h.IsAlive || counted.Contains(h)) continue;
                counted.Add(h);
                connected = true;

                var ai = h.GetComponent<EnemyAI>();
                if (ai != null && ai.ParryOpen)
                {
                    ai.GetParried(transform.position);
                    GainConcertoScaled(m,20f);
                    CombatScore.NotifyParry();
                    parried = true;
                }

                // Aka's E hits burning targets 30% harder
                float perTarget = 1f;
                if (cat == AttackCat.Skill && m.element == Element.Fusion)
                {
                    var st = h.GetComponent<EnemyStatus>();
                    if (st != null && st.Burning) perTarget = 1.3f;
                }

                bool crit = counter || Random.value < CritChanceOf(m);
                float dmg = m.EffAtk * def.dmgMul * extraMul * counterMul * buffMul * perTarget
                            * Random.Range(0.92f, 1.08f) * (crit ? m.EffCritMul : 1f);
                h.TakeDamage(new DamageInfo
                {
                    amount = dmg,
                    crit = crit,
                    element = m.element,
                    sourcePos = transform.position,
                    knockback = def.knockback,
                    staggerPower = def.stagger * (empowered ? 2f : 1f),
                    source = gameObject
                });
                ApplyElement(m, h, cat);
                m.GainEnergy(isUlt ? 0f : 7.5f);
                GainConcertoScaled(m,6f);
                bool heavyBlow = def.dmgMul >= 1.4f || empowered || isUlt;
                VFXLibrary.SpawnHitSpark(h.transform.position + Vector3.up * 1.1f, ElementInfo.Of(m.element), crit ? 1.4f : (heavyBlow ? 1.25f : 1.0f));
                AudioMan.I.Play(heavyBlow ? Sfx.HitHeavy() : (crit ? Sfx.HitCrit() : Sfx.Hit()),
                    h.transform.position, crit ? 1.0f : 0.92f, 1f, 0.06f, 0.4f);
            }

            if (connected)
            {
                float weight = Mathf.Clamp01((def.dmgMul - 0.9f) / 1.6f);
                if (empowered || isUlt) weight = 1f;
                Hitstop.I.Freeze(Mathf.Lerp(0.055f, 0.13f, weight) + (parried ? 0.05f : 0f), 0.03f);
                CameraShaker.Add(Mathf.Lerp(0.22f, 0.5f, weight) + (parried ? 0.2f : 0f) + (isUlt ? 0.2f : 0f));
                if (MusicDirector.I != null) MusicDirector.I.Duck(weight > 0.5f ? 0.35f : 0.18f);   // SFX ducking
                if (weight > 0.5f) ThirdPersonCamera.PunchFov(-3.5f, 0.3f);
                HUDController.NotifyResources();
            }
        }

        void AoeDamage(MemberConfig m, Vector3 center, float radius, float dmgMul, float knockback, float stagger, AttackCat cat)
        {
            var hits = Physics.OverlapSphere(center + Vector3.up * 0.8f, radius, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            var counted = new System.Collections.Generic.HashSet<Health>();
            bool any = false;
            float buffMul = TotalOutgoingMul(m, cat);
            foreach (var c in hits)
            {
                var h = c.GetComponentInParent<Health>();
                if (h == null || !h.IsAlive || counted.Contains(h)) continue;
                counted.Add(h);
                any = true;
                var ai = h.GetComponent<EnemyAI>();
                if (ai != null && ai.ParryOpen) { ai.GetParried(center); GainConcertoScaled(m,20f); CombatScore.NotifyParry(); }
                bool crit = Random.value < CritChanceOf(m);
                float dmg = m.EffAtk * dmgMul * buffMul * Random.Range(0.92f, 1.08f) * (crit ? m.EffCritMul : 1f);
                h.TakeDamage(new DamageInfo
                {
                    amount = dmg, crit = crit, element = m.element,
                    sourcePos = center, knockback = knockback, staggerPower = stagger, source = gameObject
                });
                ApplyElement(m, h, cat);
                m.GainEnergy(6f);
                GainConcertoScaled(m,6f);
                VFXLibrary.SpawnHitSpark(h.transform.position + Vector3.up, ElementInfo.Of(m.element), 1f);
            }
            if (any) { Hitstop.I.Freeze(0.05f); CameraShaker.Add(0.3f); HUDController.NotifyResources(); }
        }

        // ---------------------------------------------------------------- echo actives
        IEnumerator DefaultEchoRoutine()
        {
            var m = _team.Active;
            GainConcertoScaled(m,12f);
            Vector3 pos = transform.position + transform.forward * 3.2f;
            pos.y = WuWaUtil.GroundHeight(pos);
            VFXLibrary.SpawnEchoStrike(pos, m.themeColor);
            AudioMan.I.Play(Sfx.Skill(), pos, 0.8f, 0.8f);
            yield return new WaitForSeconds(0.35f);
            AoeDamage(m, pos, 3.4f, echoDamageMul, 6f, 30f, AttackCat.EchoActive);
        }

        IEnumerator EchoActiveRoutine(EchoDef echo)
        {
            var m = _team.Active;
            GainConcertoScaled(m,12f);
            var target = FindAttackTarget(12f, 120f);
            if (target != null) _ctrl.FaceInstant(target.position - transform.position);

            switch (echo.id)
            {
                case 0:   // 그림자 할퀴기 — 전방 3연격
                {
                    _busy = true; _ctrl.MovementLock = 1f;
                    string[] states = { "A1", "A2", "A3" };
                    for (int i = 0; i < 3; i++)
                    {
                        WuWaUtil.Fade(m.Anim, states[i], 0.04f);
                        if (m.Anim != null) m.Anim.speed = 1.6f;
                        AudioMan.I.Play(Sfx.Whoosh(), transform.position, 0.5f, 1.1f + i * 0.08f);
                        Vector3 c = transform.position + Vector3.up + transform.forward * 2.0f;
                        VFXLibrary.SpawnSlash(c, transform.rotation, new Color(0.75f, 0.6f, 1f), i);
                        AoeDamage(m, c, 1.9f, 1.2f, 2.5f, 8f, AttackCat.EchoActive);
                        yield return new WaitForSeconds(0.18f);
                    }
                    if (m.Anim != null) m.Anim.speed = 1f;
                    _busy = false; _ctrl.MovementLock = 0f;
                    WuWaUtil.Fade(m.Anim, "Loco", 0.15f);
                    break;
                }
                case 1:   // 질풍 가르기 — 관통 돌진
                {
                    _busy = true; _ctrl.MovementLock = 1f;
                    WuWaUtil.Fade(m.Anim, "DashAtk", 0.04f);
                    AudioMan.I.Play(Sfx.Dash(), transform.position, 0.7f, 1.2f);
                    Vector3 dir = transform.forward;
                    float dist = 0f;
                    var hitOnce = new System.Collections.Generic.HashSet<Health>();
                    while (dist < 6f)
                    {
                        float step = 26f * Time.deltaTime;
                        dist += step;
                        _ctrl.AttackTrack(transform.position + dir * 2f, Time.deltaTime * 1.75f);
                        var cols = Physics.OverlapSphere(transform.position + Vector3.up, 1.6f, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
                        foreach (var col in cols)
                        {
                            var h = col.GetComponentInParent<Health>();
                            if (h == null || !h.IsAlive || hitOnce.Contains(h)) continue;
                            hitOnce.Add(h);
                            float dmg = m.EffAtk * 1.8f * TotalOutgoingMul(m, AttackCat.EchoActive) * Random.Range(0.92f, 1.08f);
                            h.TakeDamage(new DamageInfo { amount = dmg, element = m.element, sourcePos = transform.position, knockback = 3f, staggerPower = 14f, source = gameObject });
                            VFXLibrary.SpawnHitSpark(h.transform.position + Vector3.up, new Color(0.55f, 1f, 0.85f), 1.1f);
                            AudioMan.I.Play(Sfx.Hit(), h.transform.position, 0.9f);
                            GainConcertoScaled(m,6f);
                        }
                        yield return null;
                    }
                    if (hitOnce.Count > 0) { Hitstop.I.Freeze(0.06f); CameraShaker.Add(0.3f); }
                    _busy = false; _ctrl.MovementLock = 0f;
                    WuWaUtil.Fade(m.Anim, "Loco", 0.12f);
                    break;
                }
                case 2:   // 그림자 화살 ×3 — 유도
                {
                    WuWaUtil.Fade(m.Anim, "Skill", 0.05f);
                    AudioMan.I.Play(Sfx.Skill(), transform.position, 0.6f, 1.3f);
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 muzzle = transform.position + Vector3.up * 1.4f + transform.right * (i - 1) * 0.4f;
                        Vector3 dir = (transform.forward + transform.up * 0.25f + transform.right * (i - 1) * 0.18f).normalized;
                        PlayerProjectile.Fire(muzzle, dir, target,
                            m.EffAtk * 1.4f * TotalOutgoingMul(m, AttackCat.EchoActive),
                            m.element, new Color(0.85f, 0.4f, 1f), m);
                        yield return new WaitForSeconds(0.09f);
                    }
                    break;
                }
                case 3:   // 대지 강타 — 광역 넉업 + 강 그로기
                {
                    _busy = true; _ctrl.MovementLock = 1f;
                    WuWaUtil.Fade(m.Anim, "Heavy", 0.04f);
                    yield return new WaitForSeconds(0.28f);
                    VFXLibrary.SpawnPlungeImpact(transform.position, new Color(0.9f, 0.55f, 0.3f), 3.5f);
                    CameraShaker.Add(0.5f);
                    AudioMan.I.Play(Sfx.HitHeavy(), transform.position, 1f, 0.8f);
                    AoeDamage(m, transform.position, 3.5f, 2.6f, 9f, 40f, AttackCat.EchoActive);
                    yield return new WaitForSeconds(0.25f);
                    _busy = false; _ctrl.MovementLock = 0f;
                    WuWaUtil.Fade(m.Anim, "Loco", 0.15f);
                    break;
                }
                default:  // 4: 무관의 군림 — 이중 충격파
                {
                    _busy = true; _ctrl.MovementLock = 1f;
                    WuWaUtil.Fade(m.Anim, "IntroSkill", 0.04f);
                    Hitstop.I.SlowMo(0.35f, 0.3f, 0.2f);
                    VFXLibrary.SpawnUltFlash(transform.position, new Color(0.85f, 0.65f, 0.2f));
                    yield return new WaitForSeconds(0.3f);
                    VFXLibrary.SpawnNova(transform.position, new Color(0.9f, 0.7f, 0.25f), 5f, true);
                    AoeDamage(m, transform.position, 5f, 3.0f, 7f, 35f, AttackCat.EchoActive);
                    AudioMan.I.Play(Sfx.Ult(), transform.position, 0.7f, 1.1f);
                    yield return new WaitForSeconds(0.35f);
                    VFXLibrary.SpawnNova(transform.position, new Color(0.9f, 0.7f, 0.25f), 5f, true);
                    AoeDamage(m, transform.position, 5f, 1.5f, 9f, 35f, AttackCat.EchoActive);
                    _busy = false; _ctrl.MovementLock = 0f;
                    WuWaUtil.Fade(m.Anim, "Loco", 0.15f);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- misc
        public void CancelAttack()
        {
            if (!_busy) return;
            if (_attackRoutine != null) StopCoroutine(_attackRoutine);
            _ctrl.EndPlunge();
            var m = _team != null ? _team.Active : null;
            if (m != null && m.Anim != null) m.Anim.speed = 1f;
            _busy = false;
            _canCancel = false;
            _queued = false;
            _heavyQueued = false;
            _ctrl.MovementLock = 0f;
            _lastAttackEnd = Time.time;
        }

        public void IntroBurst(MemberConfig incoming, MemberConfig outgoing)
        {
            bool full = outgoing != null && outgoing.ConcertoReady;
            if (full) CombatScore.NotifyIntro();
            if (full)
            {
                outgoing.concerto = 0f;
                _outroType = outgoing.outroType;
                _outroMul = outgoing.outroBuffMul;
                _outroUntil = Time.time + outgoing.outroBuffDur;
                string buffName = _outroType == OutroType.DamageUp ? "피해 증가"
                    : _outroType == OutroType.SkillHaste ? "스킬 가속" : "강공 강화";
                HUDController.Toast("변주 스킬! " + outgoing.charName + "의 여운 — " + buffName);
                if (_attackRoutine != null) StopCoroutine(_attackRoutine);
                _attackRoutine = StartCoroutine(IntroSkillRoutine(incoming));
                return;
            }

            VFXLibrary.SpawnNova(transform.position, incoming.themeColor, 4.2f);
            AudioMan.I.Play(Sfx.Skill(), transform.position, 0.7f, 1.15f);
            AoeDamage(incoming, transform.position, 4.2f, 1.6f, 4f, 20f, AttackCat.Intro);
        }

        IEnumerator IntroSkillRoutine(MemberConfig m)
        {
            _busy = true;
            _ctrl.MovementLock = 1f;
            var anim = m.Anim;
            var def = m.introSkill;
            Hitstop.I.SlowMo(0.32f, 0.4f, 0.25f);
            ThirdPersonCamera.PunchFov(6f, 0.5f);
            if (anim != null) anim.speed = def.speed;
            WuWaUtil.Fade(anim, def.state, 0.03f);
            VFXLibrary.SpawnUltFlash(transform.position, m.themeColor);
            AudioMan.I.Play2D(Sfx.Ult(), 0.5f, 1.3f);

            float dur = Mathf.Max(0.3f, def.clipLen / Mathf.Max(0.2f, def.speed));
            float hitAt = dur * def.hitTime;
            float t = 0f;
            bool hitDone = false;
            while (t < dur * 0.9f)
            {
                t += Time.deltaTime;
                if (!hitDone && t >= hitAt)
                {
                    hitDone = true;
                    VFXLibrary.SpawnNova(transform.position, m.themeColor, def.radius, true);
                    AoeDamage(m, transform.position, def.radius, def.dmgMul, def.knockback, def.stagger, AttackCat.Intro);
                }
                yield return null;
            }
            if (anim != null) anim.speed = 1f;
            _busy = false;
            _ctrl.MovementLock = 0f;
            _lastAttackEnd = Time.time;
            WuWaUtil.Fade(anim, "Loco", 0.15f);
        }
    }
}
