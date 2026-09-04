using System;
using UnityEngine;

namespace WuWa
{
    /// Enemy health + stagger (vibration) gauge, in the spirit of WuWa's vibration strength bar.
    public class Health : MonoBehaviour, IDamageable
    {
        public float maxHp = 1800f;
        public float maxStagger = 100f;
        public float staggerRegenDelay = 4f;
        public bool isBoss;
        public string displayName = "Shadow Rover";

        [NonSerialized] public float hp;
        [NonSerialized] public float stagger;
        [NonSerialized] public EnemyStatus Status;      // wired by EnemyStatus.Awake

        public event Action<DamageInfo> OnDamaged;
        public event Action OnStaggered;
        public event Action OnDied;

        float _lastHitTime;
        bool _dead;

        public bool IsAlive { get { return !_dead; } }
        public Transform Root { get { return transform; } }

        void Awake()
        {
            hp = maxHp;
            stagger = maxStagger;
        }

        void Update()
        {
            if (_dead) return;
            if (stagger < maxStagger && Time.time - _lastHitTime > staggerRegenDelay)
                stagger = Mathf.Min(maxStagger, stagger + maxStagger * 0.35f * Time.deltaTime);
        }

        public void TakeDamage(DamageInfo info)
        {
            if (_dead) return;
            if (info.source != null && PlayerController.Instance != null && info.source == PlayerController.Instance.gameObject) CombatScore.NotifyHit();
            var status = Status;
            if (status != null) info.amount *= status.IncomingMul;   // spectro purge-mark
            _lastHitTime = Time.time;
            hp -= info.amount;
            stagger -= info.staggerPower;

            var handler = OnDamaged;
            if (handler != null) handler(info);

            if (stagger <= 0f && hp > 0f)
            {
                stagger = maxStagger;
                var sh = OnStaggered;
                if (sh != null) sh();
            }

            if (hp <= 0f)
            {
                _dead = true;
                hp = 0f;
                var dh = OnDied;
                if (dh != null) dh();
            }
        }
    }
}
