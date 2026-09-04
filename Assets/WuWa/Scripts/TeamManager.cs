using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WuWa
{
    /// Three-member party with WuWa-style tag swapping and intro bursts.
    public class TeamManager : MonoBehaviour
    {
        public MemberConfig[] members = new MemberConfig[0];
        public float swapCooldown = 1.4f;

        int _active;
        float _nextSwapTime;
        PlayerController _ctrl;
        PlayerCombat _combat;

        public event Action OnTeamChanged;   // active member or hp changed

        public MemberConfig Active
        {
            get { return members != null && members.Length > 0 ? members[Mathf.Clamp(_active, 0, members.Length - 1)] : null; }
        }
        public int ActiveIndex { get { return _active; } }
        public bool AnyAlive
        {
            get
            {
                if (members == null) return false;
                for (int i = 0; i < members.Length; i++)
                    if (members[i] != null && members[i].hp > 0f) return true;
                return false;
            }
        }

        void Awake()
        {
            _ctrl = GetComponent<PlayerController>();
            _combat = GetComponent<PlayerCombat>();
        }

        void Start()
        {
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == null) continue;
                if (members[i].hp < 0f) members[i].hp = members[i].maxHp;
                members[i].gameObject.SetActive(i == _active);
            }
            BindActive(false);
            NotifyHpChanged();
        }

        void Update()
        {
            if (Time.timeScale <= 0.001f || GameDirector.CursorFree) return;
            int s = InputService.SwapPressed;
            if (s >= 0 && s < members.Length) TrySwap(s);
        }

        public void TickAll(float dt)
        {
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m == null) continue;
                m.TickResources(dt);
                if (i != _active && m.hp > 0f)
                    m.GainEnergy(2.2f * dt);   // off-field concerto trickle
            }
        }

        /// Save restore: switch the on-field member without the swap animation or cost.
        public void RestoreActive(int index)
        {
            if (members == null || index < 0 || index >= members.Length || members[index] == null || index == _active) return;
            _active = index;
            for (int i = 0; i < members.Length; i++) if (members[i] != null) members[i].gameObject.SetActive(i == _active);
            BindActive(false);
            NotifyHpChanged();
        }

        public void TrySwap(int index)
        {
            if (index < 0 || index >= members.Length || index == _active) return;
            if (Time.time < _nextSwapTime) return;
            var target = members[index];
            if (target == null || target.hp <= 0f) { HUDController.Toast("전투 불능 상태입니다"); return; }

            if (_combat != null) _combat.CancelAttack();
            _nextSwapTime = Time.time + swapCooldown;

            var old = Active;
            if (old != null) old.gameObject.SetActive(false);
            _active = index;
            var now = Active;
            now.gameObject.SetActive(true);

            BindActive(true);
            VFXLibrary.SpawnSwapFlash(transform.position + Vector3.up * 0.9f, now.themeColor);
            AudioMan.I.Play2D(Sfx.Swap(), 0.7f);
            if (_combat != null) _combat.IntroBurst(now, old);

            var h = OnTeamChanged;
            if (h != null) h();
        }

        public bool SwapToNextAlive()
        {
            for (int i = 0; i < members.Length; i++)
            {
                int idx = (_active + 1 + i) % members.Length;
                if (idx == _active) continue;
                if (members[idx] != null && members[idx].hp > 0f)
                {
                    float saved = _nextSwapTime;
                    _nextSwapTime = 0f;
                    TrySwap(idx);
                    _nextSwapTime = Mathf.Max(saved, Time.time + 0.5f);
                    return true;
                }
            }
            return false;
        }

        void BindActive(bool intro)
        {
            var m = Active;
            if (m == null || _ctrl == null) return;
            _ctrl.BindAnimator(m.Anim);
            if (intro) WuWaUtil.Fade(m.Anim, "Intro", 0.03f);
        }

        public void NotifyHpChanged()
        {
            var h = OnTeamChanged;
            if (h != null) h();
        }

        public void ReviveAll()
        {
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m == null) continue;
                m.hp = m.maxHp;
                m.energy = 0f;
                m.skillCdLeft = 0f;
            }
            NotifyHpChanged();
        }

        /// Flask: the active member heals more than the bench.
        public void HealSplit(float activeFrac, float otherFrac)
        {
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m == null || m.hp <= 0f) continue;
                m.hp = Mathf.Min(m.maxHp, m.hp + m.maxHp * (i == _active ? activeFrac : otherFrac));
            }
            NotifyHpChanged();
        }

        public void HealAll(float frac, float energyGain)
        {
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m == null || m.hp <= 0f) continue;
                m.hp = Mathf.Min(m.maxHp, m.hp + m.maxHp * frac);
                m.GainEnergy(energyGain);
            }
            NotifyHpChanged();
        }
    }
}
