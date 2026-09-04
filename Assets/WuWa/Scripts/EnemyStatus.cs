using System.Collections;
using UnityEngine;

namespace WuWa
{
    /// Elemental application on an enemy: Spectro purge-mark, Glacio freeze
    /// stacks, Fusion burn DoT, plus a generic slow channel (frost field).
    /// Added lazily at runtime by whoever applies the first status.
    [RequireComponent(typeof(Health))]
    public class EnemyStatus : MonoBehaviour
    {
        public const float MarkDuration = 6f;
        public const float MarkDamageMul = 1.15f;
        public const int FreezeStacksNeeded = 3;
        public const float FreezeDuration = 1.5f;
        public const float StackDecay = 4f;
        public const float BurnDuration = 3f;
        public const float BurnTick = 0.5f;

        Health _hp;
        EnemyAI _ai;

        float _markUntil;
        int _glacioStacks;
        float _stackDecayAt;
        float _burnUntil;
        float _burnDps;
        float _nextBurnTick;
        float _slowUntil;
        float _slowMul = 1f;
        float _freezeCdUntil;   // can't be chain-frozen forever

        public bool Marked { get { return Time.time < _markUntil; } }
        public bool Burning { get { return Time.time < _burnUntil; } }
        public float IncomingMul { get { return Marked ? MarkDamageMul : 1f; } }
        public float MoveMul { get { return Time.time < _slowUntil ? _slowMul : 1f; } }

        public static EnemyStatus Of(Health h)
        {
            var st = h.GetComponent<EnemyStatus>();
            if (st == null) st = h.gameObject.AddComponent<EnemyStatus>();
            return st;
        }

        void Awake()
        {
            _hp = GetComponent<Health>();
            _ai = GetComponent<EnemyAI>();
            if (_ai != null) _ai.Status = this;
            var hh = GetComponent<Health>();
            if (hh != null) hh.Status = this;
        }

        void Update()
        {
            if (_hp == null || !_hp.IsAlive) return;

            if (_glacioStacks > 0 && Time.time > _stackDecayAt)
            {
                _glacioStacks--;
                _stackDecayAt = Time.time + StackDecay;
            }

            if (Burning && Time.time >= _nextBurnTick)
            {
                _nextBurnTick = Time.time + BurnTick;
                float dmg = _burnDps * BurnTick;
                _hp.TakeDamage(new DamageInfo
                {
                    amount = dmg, crit = false, element = Element.Fusion,
                    sourcePos = transform.position, knockback = 0f, staggerPower = 0f, source = gameObject
                });
            }
        }

        public void ApplySpectroMark()
        {
            bool fresh = !Marked;
            _markUntil = Time.time + MarkDuration;
            if (fresh)
                DamageNumbers.SpawnText(transform.position + Vector3.up * 2f, "표식", new Color(1f, 0.87f, 0.45f));
        }

        public void ApplyGlacioStack()
        {
            if (Time.time < _freezeCdUntil) return;
            _glacioStacks++;
            _stackDecayAt = Time.time + StackDecay;
            if (_glacioStacks >= FreezeStacksNeeded)
            {
                _glacioStacks = 0;
                TriggerFreeze(FreezeDuration);
            }
        }

        public void TriggerFreeze(float duration)
        {
            if (Time.time < _freezeCdUntil) return;
            float resist = 1f;
            if (_ai != null && _ai.isBoss) resist = 0.5f;
            else if (_ai != null && _ai.heavyPoise) resist = 0.7f;
            _freezeCdUntil = Time.time + 6f;
            DamageNumbers.SpawnText(transform.position + Vector3.up * 2f, "동결!", new Color(0.55f, 0.88f, 1f));
            AudioMan.I.Play(Sfx.PerfectDodge(), transform.position, 0.5f, 1.6f);
            if (_ai != null) _ai.ApplyFreeze(duration * resist);
        }

        public void ApplyBurn(float dps)
        {
            bool fresh = !Burning;
            _burnUntil = Time.time + BurnDuration;
            _burnDps = Mathf.Max(_burnDps, dps);
            if (fresh)
            {
                _nextBurnTick = Time.time + BurnTick;
                DamageNumbers.SpawnText(transform.position + Vector3.up * 2f, "화상", new Color(1f, 0.55f, 0.3f));
            }
        }

        public void ApplySlow(float mul, float duration)
        {
            _slowMul = mul;
            _slowUntil = Time.time + duration;
        }
    }
}
