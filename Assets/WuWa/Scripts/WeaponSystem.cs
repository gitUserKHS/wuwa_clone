using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// One physical weapon: level 1–40, 돌파 0–3, EXP.
    public class WeaponInstance
    {
        public int uid, defId, level = 1, ascension;
        public float exp;
        public WeaponDef Def { get { return WeaponDB.Get(defId); } }
        public float Atk { get { var d = Def; return d != null ? Growth.WeaponAtk(d.atk, level) : 0f; } }
        public int LevelCap { get { var d = Def; return d != null ? Growth.WeaponLevelCap(d.tier, ascension) : 1; } }
        public float PassiveValue { get { return Growth.WeaponPassiveValue(Def, ascension); } }
        public string PassiveText
        {
            get
            {
                var d = Def; if (d == null) return "";
                switch (d.passive)
                {
                    case WeaponPassive.SkillDmgPct: return "스킬 피해 +" + PassiveValue + "%";
                    case WeaponPassive.ConcertoGainPct: return "협주 에너지 획득 +" + PassiveValue + "%";
                    case WeaponPassive.CritRatePct: return "크리티컬 확률 +" + PassiveValue + "%";
                    default: return "패시브 없음";
                }
            }
        }
    }

    /// Weapon instances + per-member equips. Every member starts with a practice
    /// sword; better tiers drop from elites, the boss, chests and the shop.
    public class WeaponSystem : MonoBehaviour
    {
        public const int MemberCount = 3;

        readonly List<WeaponInstance> _items = new List<WeaponInstance>();
        readonly int[] _equipUid = { -1, -1, -1 };
        int _nextUid = 1;

        public event Action OnChanged;
        public static WeaponSystem I { get; private set; }

        [Serializable]
        public class WeaponSaveEntry { public int uid, defId, level, ascension; public float exp; }

        void Awake()
        {
            I = this;
            for (int m = 0; m < MemberCount; m++) _equipUid[m] = Create(0).uid;   // starter swords for the whole party
        }

        void OnDestroy() { if (I == this) I = null; }

        WeaponInstance Create(int defId)
        {
            var inst = new WeaponInstance { uid = _nextUid++, defId = defId };
            _items.Add(inst);
            GameFlags.Set("weapon_" + defId);
            return inst;
        }

        // ---------------------------------------------------------------- queries
        public IReadOnlyList<WeaponInstance> Items { get { return _items; } }
        public WeaponInstance Get(int uid) { for (int i = 0; i < _items.Count; i++) if (_items[i].uid == uid) return _items[i]; return null; }
        public int CountOf(int defId) { int n = 0; for (int i = 0; i < _items.Count; i++) if (_items[i].defId == defId) n++; return n; }
        public int EquippedCount(int defId) { int n = 0; for (int m = 0; m < MemberCount; m++) { var w = Get(_equipUid[m]); if (w != null && w.defId == defId) n++; } return n; }
        public bool IsEquipped(int uid, out int member) { for (int m = 0; m < MemberCount; m++) if (_equipUid[m] == uid) { member = m; return true; } member = -1; return false; }
        public int EquippedUidOf(int member) { return member >= 0 && member < MemberCount ? _equipUid[member] : -1; }
        public WeaponInstance InstanceOf(int member) { return Get(EquippedUidOf(member)); }
        public int EquippedOf(int member) { var w = InstanceOf(member); return w != null ? w.defId : -1; }
        public WeaponDef WeaponOf(int member) { var w = InstanceOf(member); return w != null ? w.Def : null; }

        /// Cheapest spare copy of a def (unequipped, lowest level) — sold or fed.
        public WeaponInstance Spare(int defId)
        {
            WeaponInstance best = null;
            for (int i = 0; i < _items.Count; i++)
            {
                var w = _items[i]; int m;
                if (w.defId != defId || IsEquipped(w.uid, out m)) continue;
                if (best == null || w.level < best.level) best = w;
            }
            return best;
        }

        // ---------------------------------------------------------------- mutations
        public WeaponInstance Add(int defId)
        {
            var def = WeaponDB.Get(defId);
            if (def == null) return null;
            var inst = Create(defId);
            NotificationFeed.Item("무기 · " + def.name, 1, UIKit.Theme.Rarity(def.tier + 2), "T" + def.tier);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.6f, 0.9f);
            Notify();
            return inst;
        }

        public bool Equip(int member, int uid)
        {
            var w = Get(uid);
            if (w == null || member < 0 || member >= MemberCount) return false;
            int other;
            if (IsEquipped(uid, out other) && other != member) _equipUid[other] = _equipUid[member];   // swap with that member
            _equipUid[member] = uid;
            AudioMan.I.Play2D(Sfx.Swap(), 0.4f, 1.1f);
            Notify();
            return true;
        }

        /// Removes one spare copy of a def (never an equipped one). Used by selling.
        public bool Remove(int defId)
        {
            var w = Spare(defId);
            if (w == null) return false;
            _items.Remove(w);
            Notify();
            return true;
        }

        public bool RemoveUid(int uid)
        {
            int m;
            var w = Get(uid);
            if (w == null || IsEquipped(uid, out m)) return false;
            _items.Remove(w);
            Notify();
            return true;
        }

        /// Returns levels gained. EXP past the cap is retained until the next ascension.
        public int AddExp(int uid, float xp)
        {
            var w = Get(uid);
            if (w == null) return 0;
            int cap = w.LevelCap;
            w.exp += Mathf.Max(0f, xp);
            int gained = 0;
            while (w.level < cap && w.exp >= Growth.WExpNeed(w.level)) { w.exp -= Growth.WExpNeed(w.level); w.level++; gained++; }
            if (w.level >= cap) w.exp = Mathf.Min(w.exp, Growth.WExpNeed(w.level) - 1f);
            Notify();
            return gained;
        }

        public bool UseStone(int uid, int stoneId, int n = 1)
        {
            var w = Get(uid); var def = ItemDB.Get(stoneId);
            if (w == null || def == null || def.expValue <= 0) return false;
            if (w.level >= w.LevelCap) { HUDController.Toast("무기 레벨 상한 — 돌파가 필요합니다"); return false; }
            n = Mathf.Min(n, Inventory.Count(stoneId));
            if (n <= 0) { HUDController.Toast(def.name + "이(가) 없습니다"); return false; }
            Inventory.Remove(stoneId, n);
            int gained = AddExp(uid, def.expValue * n);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.6f, 1.3f);
            HUDController.Toast(w.Def.name + " EXP +" + def.expValue * n + (gained > 0 ? " — Lv " + w.level : ""));
            return true;
        }

        /// Consumes a spare weapon as material (T1 500 / T2 1,500 / T3 4,000 EXP + its own EXP).
        public bool Feed(int uid, int materialUid)
        {
            var w = Get(uid); var mat = Get(materialUid); int m;
            if (w == null || mat == null || uid == materialUid || IsEquipped(materialUid, out m)) return false;
            if (w.level >= w.LevelCap) { HUDController.Toast("무기 레벨 상한 — 돌파가 필요합니다"); return false; }
            float xp = Growth.WeaponFeedExp(mat.Def.tier) + mat.exp;
            for (int l = 1; l < mat.level; l++) xp += Growth.WExpNeed(l) * 0.8f;
            _items.Remove(mat);
            int gained = AddExp(uid, xp);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.6f, 1.1f);
            HUDController.Toast(mat.Def.name + " 투입 — " + w.Def.name + " EXP +" + Mathf.RoundToInt(xp) + (gained > 0 ? " · Lv " + w.level : ""));
            return true;
        }

        public bool CanAscend(int uid, out string why)
        {
            var w = Get(uid);
            if (w == null) { why = "-"; return false; }
            if (w.ascension >= Growth.WeaponMaxAscension(w.Def.tier)) { why = "최대 돌파"; return false; }
            if (w.level < Growth.WeaponAscendGate(w.ascension)) { why = "Lv " + Growth.WeaponAscendGate(w.ascension) + " 필요"; return false; }
            return Growth.CanPay(Growth.WeaponAscendCost(w.ascension), out why);
        }

        public bool Ascend(int uid)
        {
            string why;
            if (!CanAscend(uid, out why)) { HUDController.Toast("무기 돌파 불가 — " + why); return false; }
            var w = Get(uid);
            if (!Growth.Pay(Growth.WeaponAscendCost(w.ascension))) return false;
            w.ascension++;
            AudioMan.I.Play2D(Sfx.Ult(), 0.6f, 1.1f);
            HUDController.Toast(w.Def.name + " 돌파 " + w.ascension + " — 상한 Lv " + w.LevelCap + " · " + w.PassiveText);
            AddExp(uid, 0f);
            Notify();
            return true;
        }

        // ---------------------------------------------------------------- effect queries
        public float AtkFor(int member) { var w = InstanceOf(member); return w != null ? w.Atk : 0f; }

        public float SkillDmgMulFor(int member)
        {
            var w = InstanceOf(member);
            return w != null && w.Def.passive == WeaponPassive.SkillDmgPct ? 1f + w.PassiveValue / 100f : 1f;
        }

        public float ConcertoMulFor(int member)
        {
            var w = InstanceOf(member);
            return w != null && w.Def.passive == WeaponPassive.ConcertoGainPct ? 1f + w.PassiveValue / 100f : 1f;
        }

        public float CritRateBonusFor(int member)
        {
            var w = InstanceOf(member);
            return w != null && w.Def.passive == WeaponPassive.CritRatePct ? w.PassiveValue / 100f : 0f;
        }

        void Notify()
        {
            var h = OnChanged;
            if (h != null) h();
            HUDController.NotifyResources();
        }

        // ---------------------------------------------------------------- save/load
        public void Export(out WeaponSaveEntry[] items, out int nextUid, out int[] equipUid)
        {
            items = new WeaponSaveEntry[_items.Count];
            for (int i = 0; i < _items.Count; i++)
                items[i] = new WeaponSaveEntry { uid = _items[i].uid, defId = _items[i].defId, level = _items[i].level, ascension = _items[i].ascension, exp = _items[i].exp };
            nextUid = _nextUid;
            equipUid = (int[])_equipUid.Clone();
        }

        /// v3 instances, or a v1/v2 count table (each copy becomes a Lv1 instance).
        public void Import(WeaponSaveEntry[] items, int nextUid, int[] equipUid, int[] legacyIds, int[] legacyCounts, int[] legacyEquipped)
        {
            _items.Clear();
            for (int m = 0; m < MemberCount; m++) _equipUid[m] = -1;
            if (items != null && items.Length > 0)
            {
                foreach (var e in items)
                    if (e != null && WeaponDB.Get(e.defId) != null)
                        _items.Add(new WeaponInstance { uid = e.uid, defId = e.defId, level = Mathf.Clamp(e.level, 1, Growth.WeaponMaxLevel), ascension = Mathf.Clamp(e.ascension, 0, 3), exp = Mathf.Max(0f, e.exp) });
                _nextUid = Mathf.Max(1, nextUid);
                if (equipUid != null)
                    for (int m = 0; m < MemberCount && m < equipUid.Length; m++) _equipUid[m] = Get(equipUid[m]) != null ? equipUid[m] : -1;
            }
            else
            {
                _nextUid = 1;
                if (legacyIds != null && legacyCounts != null)
                    for (int i = 0; i < legacyIds.Length && i < legacyCounts.Length; i++)
                        if (WeaponDB.Get(legacyIds[i]) != null)
                            for (int k = 0; k < legacyCounts[i]; k++) Create(legacyIds[i]);
                if (legacyEquipped != null)
                    for (int m = 0; m < MemberCount && m < legacyEquipped.Length; m++)
                    {
                        var w = Spare(legacyEquipped[m]);
                        if (w != null) _equipUid[m] = w.uid;
                    }
            }
            // everyone must hold something
            for (int m = 0; m < MemberCount; m++)
            {
                if (Get(_equipUid[m]) != null) continue;
                var spare = Spare(0);
                _equipUid[m] = (spare ?? Create(0)).uid;
            }
            Notify();
        }
    }
}
