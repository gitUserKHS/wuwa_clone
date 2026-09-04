using System;
using UnityEngine;

namespace WuWa
{
    /// Per-character growth (level 1–50, 돌파 I–III, skills Lv1–5) + the shard
    /// wallet. Kill EXP goes 100% to the on-field member and 80% to the bench.
    /// Party level is derived (floor of the average) for scaling and gating.
    public class ProgressSystem : MonoBehaviour
    {
        public const int MaxLevel = Growth.MaxLevel;
        public const int MemberCount = 3;

        public CharacterProgress[] Chars = { new CharacterProgress(), new CharacterProgress(), new CharacterProgress() };
        public int Shards { get; private set; }

        TeamManager _team;
        readonly float[] _baseHp = new float[MemberCount];
        readonly float[] _baseSkillCd = new float[MemberCount];
        readonly float[] _baseUltMax = new float[MemberCount];
        readonly float[] _baseOutroDur = new float[MemberCount];
        readonly float[] _baseOutroMul = new float[MemberCount];
        bool _basesCached;

        public event Action OnChanged;
        public static ProgressSystem I { get; private set; }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            CacheBases();
            ApplyStats();
        }

        void CacheBases()
        {
            if (_basesCached) return;
            _team = UnityEngine.Object.FindAnyObjectByType<TeamManager>();
            if (_team == null) return;
            for (int i = 0; i < _team.members.Length && i < MemberCount; i++)
            {
                var m = _team.members[i];
                if (m == null) continue;
                _baseHp[i] = m.maxHp; _baseSkillCd[i] = m.skillCooldown; _baseUltMax[i] = m.ultEnergyMax;
                _baseOutroDur[i] = m.outroBuffDur; _baseOutroMul[i] = m.outroBuffMul;
            }
            _basesCached = true;
        }

        public CharacterProgress Of(int member) { return Chars[Mathf.Clamp(member, 0, MemberCount - 1)]; }
        public int ActiveIndex { get { return _team != null ? _team.ActiveIndex : 0; } }

        /// Derived party level: floor of the average character level (scaling, shop gates).
        public int Level
        {
            get
            {
                int sum = 0; for (int i = 0; i < MemberCount; i++) sum += Chars[i].level;
                return Mathf.Max(1, sum / MemberCount);
            }
        }
        /// HUD: active character's EXP toward the next level.
        public float Exp { get { return Of(ActiveIndex).exp; } }
        public float ExpNeed { get { return Growth.ExpNeed(Of(ActiveIndex).level); } }

        // ---------------------------------------------------------------- kills / exp
        public void AddKill(EnemyKind kind, float regionMul = 1f)
        {
            float exp; int shards;
            switch (kind)
            {
                case EnemyKind.Boss: exp = 600f; shards = 120; break;
                case EnemyKind.Ranged:
                case EnemyKind.Tank: exp = 90f; shards = 18; break;
                default: exp = 30f; shards = 6; break;
            }
            exp *= 1f + 0.35f * (regionMul - 1f);
            shards = Mathf.RoundToInt(shards * regionMul);
            Shards += shards;
            if (shards > 0) NotificationFeed.Currency("조각소리", shards);
            int active = ActiveIndex;
            for (int i = 0; i < MemberCount; i++) AddExp(i, i == active ? exp : exp * 0.8f, i == active);
            Notify();
        }

        /// Returns the number of levels gained. EXP past the cap is retained (spent after ascension).
        public int AddExp(int member, float xp, bool announce = true)
        {
            var c = Of(member);
            int cap = Growth.LevelCap(c.ascension);
            c.exp += Mathf.Max(0f, xp);
            int gained = 0;
            while (c.level < cap && c.exp >= Growth.ExpNeed(c.level))
            {
                c.exp -= Growth.ExpNeed(c.level);
                c.level++;
                gained++;
            }
            if (c.level >= cap) c.exp = Mathf.Min(c.exp, Growth.ExpNeed(c.level) - 1f);
            if (gained > 0) OnLevelUp(member, announce);
            return gained;
        }

        void OnLevelUp(int member, bool announce)
        {
            ApplyStats();
            if (_team != null && member < _team.members.Length && _team.members[member] != null)
                _team.members[member].hp = _team.members[member].maxHp;      // level-up full heal
            if (announce)
            {
                var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
                if (player != null)
                {
                    VFXLibrary.SpawnNova(player.transform.position, new Color(1f, 0.95f, 0.6f), 3.5f);
                    VFXLibrary.Flash(player.transform.position + Vector3.up * 1.2f, new Color(1f, 0.95f, 0.6f), 2.6f, 0.3f);
                }
                AudioMan.I.Play2D(Sfx.PerfectDodge(), 0.8f, 0.85f);
                string nm = _team != null && member < _team.members.Length && _team.members[member] != null ? _team.members[member].charName : "캐릭터";
                var c = Of(member);
                HUDController.Toast("레벨 업!  " + nm + " Lv " + c.level + (c.level >= Growth.LevelCap(c.ascension) && c.ascension < Growth.MaxAscension ? "  (돌파 필요)" : ""));
                Tutorial.Trigger("levelup");
            }
            if (_team != null) _team.NotifyHpChanged();
        }

        /// Feeds EXP stones. Returns false when capped or none owned.
        public bool UseStone(int member, int stoneId, int n = 1)
        {
            var def = ItemDB.Get(stoneId);
            if (def == null || def.expValue <= 0) return false;
            var c = Of(member);
            if (c.level >= Growth.LevelCap(c.ascension)) { HUDController.Toast("레벨 상한 — 돌파가 필요합니다"); return false; }
            n = Mathf.Min(n, Inventory.Count(stoneId));
            if (n <= 0) { HUDController.Toast(def.name + "이(가) 없습니다"); return false; }
            Inventory.Remove(stoneId, n);
            int gained = AddExp(member, def.expValue * n, true);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.6f, 1.3f);
            if (gained == 0) HUDController.Toast(def.name + " ×" + n + " 투입 — EXP +" + def.expValue * n);
            Notify();
            return true;
        }

        // ---------------------------------------------------------------- ascension / skills
        public bool CanAscend(int member, out string why)
        {
            var c = Of(member);
            if (c.ascension >= Growth.MaxAscension) { why = "최대 돌파"; return false; }
            if (c.level < Growth.AscendGate(c.ascension)) { why = "Lv " + Growth.AscendGate(c.ascension) + " 필요"; return false; }
            return Growth.CanPay(Growth.AscendCost(c.ascension, ElementOf(member)), out why);
        }

        public bool Ascend(int member)
        {
            string why;
            if (!CanAscend(member, out why)) { HUDController.Toast("돌파 불가 — " + why); return false; }
            var c = Of(member);
            if (!Growth.Pay(Growth.AscendCost(c.ascension, ElementOf(member)))) return false;
            c.ascension++;
            ApplyStats();
            AudioMan.I.Play2D(Sfx.Ult(), 0.7f, 0.9f);
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player != null) VFXLibrary.SpawnNova(player.transform.position, new Color(1f, 0.85f, 0.4f), 5f, true);
            HUDController.Toast("돌파 " + Growth.AscensionNames[c.ascension] + " — " + Growth.AscendNode(c.ascension) + " · 레벨 상한 " + Growth.LevelCap(c.ascension));
            // retained EXP may now level up
            AddExp(member, 0f, true);
            Notify();
            return true;
        }

        public bool CanUpgradeSkill(int member, int idx, out string why)
        {
            var c = Of(member);
            int lv = c.skillLv[idx];
            if (lv >= Growth.MaxSkill) { why = "최대 레벨"; return false; }
            if (lv >= Growth.SkillCap(c.ascension)) { why = "돌파 " + Growth.AscensionNames[c.ascension + 1] + " 필요"; return false; }
            return Growth.CanPay(Growth.SkillCost(lv, ElementOf(member)), out why);
        }

        public bool UpgradeSkill(int member, int idx)
        {
            string why;
            if (!CanUpgradeSkill(member, idx, out why)) { HUDController.Toast("강화 불가 — " + why); return false; }
            var c = Of(member);
            if (!Growth.Pay(Growth.SkillCost(c.skillLv[idx], ElementOf(member)))) return false;
            c.skillLv[idx]++;
            ApplyStats();
            AudioMan.I.Play2D(Sfx.Absorb(), 0.7f, 1.4f);
            HUDController.Toast(Growth.SkillNames[idx] + " Lv " + c.skillLv[idx] + " — 배율 ×" + Growth.SkillMul(idx, c.skillLv[idx]).ToString("0.00"));
            Notify();
            return true;
        }

        public float SkillMul(int member, AttackCat cat)
        {
            int idx = Growth.SkillIndexOf(cat);
            return idx < 0 ? 1f : Growth.SkillMul(idx, Of(member).skillLv[idx]);
        }

        int ElementOf(int member)
        {
            if (_team == null || member >= _team.members.Length || _team.members[member] == null) return 0;
            switch (_team.members[member].element)
            {
                case Element.Glacio: return 1;
                case Element.Fusion: return 2;
                default: return 0;
            }
        }

        // ---------------------------------------------------------------- stats
        public void ApplyStats()
        {
            CacheBases();
            if (_team == null) return;
            for (int i = 0; i < _team.members.Length && i < MemberCount; i++)
            {
                var m = _team.members[i];
                if (m == null) continue;
                var c = Chars[i];
                m.statMul = Growth.StatMul(c.level, c.ascension);
                if (_baseHp[i] > 0f)
                {
                    float frac = m.maxHp > 0f ? m.hp / m.maxHp : 1f;
                    m.maxHp = Mathf.Round(_baseHp[i] * m.statMul);
                    m.hp = Mathf.Min(m.maxHp, m.maxHp * frac);
                }
                m.ascCritChance = c.ascension >= 1 ? 0.04f : 0f;
                m.ascAtkPct = c.ascension >= 2 ? 0.06f : 0f;
                m.ascCritMul = c.ascension >= 3 ? 0.12f : 0f;
                m.skillCooldown = Mathf.Max(1f, _baseSkillCd[i] - (c.skillLv[1] >= 5 ? 1f : 0f));
                m.ultEnergyMax = c.skillLv[2] >= 5 ? 90f : _baseUltMax[i];
                m.outroBuffDur = _baseOutroDur[i] + (c.skillLv[3] >= 3 ? 1f : 0f) + (c.skillLv[3] >= 5 ? 1f : 0f);
                m.outroBuffMul = _baseOutroMul[i] + (c.skillLv[3] >= 5 ? (_baseOutroMul[i] < 1f ? -0.04f : 0.04f) : 0f);
            }
        }

        // ---------------------------------------------------------------- wallet
        public bool SpendShards(int amount)
        {
            if (Shards < amount) return false;
            Shards -= amount;
            Notify();
            return true;
        }

        public void GrantShards(int amount)
        {
            Shards += amount;
            if (amount > 0) NotificationFeed.Currency("조각소리", amount);
            Notify();
        }

        void Notify() { var h = OnChanged; if (h != null) h(); }

        // ---------------------------------------------------------------- save
        public CharacterProgress[] Export()
        {
            var arr = new CharacterProgress[MemberCount];
            for (int i = 0; i < MemberCount; i++)
                arr[i] = new CharacterProgress { level = Chars[i].level, exp = Chars[i].exp, ascension = Chars[i].ascension, skillLv = (int[])Chars[i].skillLv.Clone() };
            return arr;
        }

        /// v1/v2 saves carry a party level only: every character gets it, with the
        /// ascensions that level implies so nobody is stuck under a cap.
        public void ImportState(int legacyLevel, float legacyExp, int shards, CharacterProgress[] chars)
        {
            Shards = Mathf.Max(0, shards);
            bool hasChars = chars != null && chars.Length >= MemberCount && chars[0] != null;
            for (int i = 0; i < MemberCount; i++)
            {
                if (hasChars)
                {
                    var s = chars[i] ?? new CharacterProgress();
                    Chars[i] = new CharacterProgress
                    {
                        level = Mathf.Clamp(s.level, 1, MaxLevel), exp = Mathf.Max(0f, s.exp),
                        ascension = Mathf.Clamp(s.ascension, 0, Growth.MaxAscension),
                        skillLv = s.skillLv != null && s.skillLv.Length == 4 ? (int[])s.skillLv.Clone() : new[] { 1, 1, 1, 1 },
                    };
                    for (int k = 0; k < 4; k++) Chars[i].skillLv[k] = Mathf.Clamp(Chars[i].skillLv[k], 1, Growth.MaxSkill);
                }
                else
                {
                    int lv = Mathf.Clamp(legacyLevel, 1, MaxLevel);
                    int asc = lv >= 40 ? 3 : lv >= 30 ? 2 : lv >= 20 ? 1 : 0;
                    Chars[i] = new CharacterProgress { level = lv, exp = Mathf.Max(0f, legacyExp), ascension = asc };
                }
            }
            ApplyStats();
            if (_team != null) _team.NotifyHpChanged();
            Notify();
        }
    }
}
