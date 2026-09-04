using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// WuWa-accurate echo system: the inventory holds INSTANCES (each drop has
    /// its own rolled main/sub stats), every member owns a loadout of 5 slots
    /// under a cost cap of 12, slot 0 is the main echo (its skill replaces Q).
    /// A physical echo can only be equipped in one place; equipping it elsewhere
    /// moves it. Def passives + sonata bonuses still apply on top of the rolls.
    public class EchoSystem : MonoBehaviour
    {
        public const int MemberCount = 3;
        public const int SlotCount = 5;
        public const int CostCap = 12;

        readonly List<EchoInstance> _inv = new List<EchoInstance>();
        readonly HashSet<int> _discovered = new HashSet<int>();
        readonly int[][] _equipped = new int[MemberCount][];     // uid, -1 = empty
        int _nextUid = 1;
        System.Random _rng = new System.Random();
        TeamManager _team;

        public event Action OnChanged;

        public static EchoSystem I { get; private set; }

        void Awake()
        {
            I = this;
            for (int m = 0; m < MemberCount; m++)
            {
                _equipped[m] = new int[SlotCount];
                for (int s = 0; s < SlotCount; s++) _equipped[m][s] = -1;
            }
        }

        void OnDestroy() { if (I == this) I = null; }

        // ------------------------------------------------------------ inventory
        public IReadOnlyList<EchoInstance> Instances { get { return _inv; } }

        public EchoInstance Get(int uid)
        {
            if (uid < 0) return null;
            for (int i = 0; i < _inv.Count; i++) if (_inv[i].uid == uid) return _inv[i];
            return null;
        }

        public int CountOf(int defId)
        {
            int c = 0;
            for (int i = 0; i < _inv.Count; i++) if (_inv[i].defId == defId) c++;
            return c;
        }

        public bool Discovered(int defId) { return _discovered.Contains(defId); }

        public int EquippedCount(int defId)
        {
            int n = 0;
            for (int m = 0; m < MemberCount; m++)
                for (int s = 0; s < SlotCount; s++)
                {
                    var inst = Get(_equipped[m][s]);
                    if (inst != null && inst.defId == defId) n++;
                }
            return n;
        }

        /// Where this instance sits, or (-1,-1).
        public bool EquipLocation(int uid, out int member, out int slot)
        {
            for (int m = 0; m < MemberCount; m++)
                for (int s = 0; s < SlotCount; s++)
                    if (_equipped[m][s] == uid) { member = m; slot = s; return true; }
            member = -1; slot = -1;
            return false;
        }

        /// Drop entry point: rolls a fresh instance of this echo type.
        public EchoInstance Add(int defId)
        {
            var inst = EchoStats.Roll(defId, _nextUid, _rng);
            if (inst == null) return null;
            _nextUid++;
            _inv.Add(inst);
            bool first = _discovered.Add(defId);
            NotificationFeed.Item("에코 · " + inst.Def.name + " ★" + inst.Def.star, 1, UIKit.Theme.Rarity(inst.Def.star), inst.main.Text);
            if (first) HUDController.Toast("도감 등록 — " + inst.Def.name);
            Notify();
            return inst;
        }

        // ------------------------------------------------------------ loadout
        /// Shop: drop an unequipped instance from the inventory.
        public bool Remove(int uid)
        {
            int m, sl;
            if (EquipLocation(uid, out m, out sl)) return false;
            int idx = _inv.FindIndex(e => e.uid == uid);
            if (idx < 0) return false;
            _inv.RemoveAt(idx);
            if (OnChanged != null) OnChanged();
            return true;
        }

        public int Equipped(int member, int slot)
        {
            if (member < 0 || member >= MemberCount || slot < 0 || slot >= SlotCount) return -1;
            return _equipped[member][slot];
        }

        public EchoInstance InstanceAt(int member, int slot) { return Get(Equipped(member, slot)); }

        public EchoDef DefAt(int member, int slot)
        {
            var inst = InstanceAt(member, slot);
            return inst != null ? inst.Def : null;
        }

        public int UsedCost(int member)
        {
            int c = 0;
            for (int s = 0; s < SlotCount; s++)
            {
                var d = DefAt(member, s);
                if (d != null) c += d.cost;
            }
            return c;
        }

        public bool Equip(int member, int slot, int uid)
        {
            var inst = Get(uid);
            if (inst == null || member < 0 || member >= MemberCount || slot < 0 || slot >= SlotCount) return false;
            if (_equipped[member][slot] == uid) return true;

            var curDef = DefAt(member, slot);
            int costWithout = UsedCost(member) - (curDef != null ? curDef.cost : 0);
            if (costWithout + inst.Def.cost > CostCap)
            {
                HUDController.Toast("코스트 초과 (" + (costWithout + inst.Def.cost) + "/" + CostCap + ")");
                Tutorial.Trigger("echo_cost");
                return false;
            }

            // a physical echo lives in one place — equipping it elsewhere moves it
            int om, os;
            if (EquipLocation(uid, out om, out os)) _equipped[om][os] = -1;

            _equipped[member][slot] = uid;
            AudioMan.I.Play2D(Sfx.Absorb(), 0.5f, 1.2f);
            Notify();
            return true;
        }

        public void Unequip(int member, int slot)
        {
            if (member < 0 || member >= MemberCount || slot < 0 || slot >= SlotCount) return;
            _equipped[member][slot] = -1;
            Notify();
        }

        /// Swap two slots of the same member (cost total unchanged, always valid).
        public void Swap(int member, int a, int b)
        {
            if (member < 0 || member >= MemberCount) return;
            if (a < 0 || a >= SlotCount || b < 0 || b >= SlotCount || a == b) return;
            int t = _equipped[member][a];
            _equipped[member][a] = _equipped[member][b];
            _equipped[member][b] = t;
            Notify();
        }

        /// Main echo (slot 0) of a member — its skill replaces Q.
        public EchoDef MainEchoOf(int member) { return DefAt(member, 0); }

        // ------------------------------------------------------------ upgrades
        /// Spend shards to raise the main stat (+12%/level, max +5).
        /// Lock toggle: locked echoes cannot be sold, batch-disposed, merged away or retuned.
        public bool ToggleLock(int uid)
        {
            var inst = Get(uid);
            if (inst == null) return false;
            inst.locked = !inst.locked;
            UIKit.Sfx(inst.locked ? 1.2f : 1.8f, 0.2f);
            Notify();
            return inst.locked;
        }

        public bool Enhance(int uid)
        {
            var inst = Get(uid);
            if (inst == null) return false;
            if (inst.level >= EchoInstance.MaxLevel) { HUDController.Toast("이미 최대 강화 (+5)"); return false; }
            int cost = EchoStats.EnhanceCost(inst);
            if (ProgressSystem.I == null || !ProgressSystem.I.SpendShards(cost))
            {
                HUDController.Toast("조각소리 부족 (" + cost + " 필요)");
                return false;
            }
            EchoStats.Enhance(inst);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.6f, 1.4f);
            HUDController.Toast("에코 강화 +" + inst.level + " — " + inst.main.Text);
            Notify();
            return true;
        }

        /// Tuning opens the next hidden substat; each enhancement level unlocks one more slot.
        public bool CanTune(int uid, out string why)
        {
            var inst = Get(uid);
            if (inst == null) { why = "-"; return false; }
            if (inst.Revealed >= inst.subs.Length) { why = "전부 개방됨"; return false; }
            if (inst.level < inst.Revealed) { why = "강화 +" + inst.Revealed + " 필요"; return false; }
            if (ProgressSystem.I == null || ProgressSystem.I.Shards < EchoStats.TuneCost) { why = "조각소리 " + EchoStats.TuneCost + " 필요"; return false; }
            if (Inventory.Count(ItemDB.Tuner) < 1) { why = "조율기 1 필요"; return false; }
            why = null; return true;
        }

        public bool Tune(int uid)
        {
            string why;
            if (!CanTune(uid, out why)) { HUDController.Toast("조율 불가 — " + why); return false; }
            var inst = Get(uid);
            ProgressSystem.I.SpendShards(EchoStats.TuneCost);
            Inventory.Remove(ItemDB.Tuner, 1);
            inst.revealed = inst.Revealed + 1;
            AudioMan.I.Play2D(Sfx.Swap(), 0.5f, 1.3f);
            HUDController.Toast("조율 — 부옵 개방: " + inst.subs[inst.Revealed - 1].Text);
            Notify();
            return true;
        }

        /// Rerolls one opened substat (80 + 조율기 1).
        public bool RetuneSub(int uid, int idx)
        {
            var inst = Get(uid);
            if (inst == null || idx < 0 || idx >= inst.Revealed) return false;
            if (inst.locked) { HUDController.Toast("잠긴 에코는 재조율할 수 없습니다"); return false; }
            if (ProgressSystem.I == null || ProgressSystem.I.Shards < EchoStats.RetuneCost) { HUDController.Toast("조각소리 " + EchoStats.RetuneCost + " 필요"); return false; }
            if (Inventory.Count(ItemDB.Tuner) < 1) { HUDController.Toast("조율기 1 필요"); return false; }
            ProgressSystem.I.SpendShards(EchoStats.RetuneCost);
            Inventory.Remove(ItemDB.Tuner, 1);
            string before = inst.subs[idx].Text;
            EchoStats.RollSub(inst, idx, _rng);
            AudioMan.I.Play2D(Sfx.Swap(), 0.5f, 1.2f);
            HUDController.Toast("재조율 — " + before + " → " + inst.subs[idx].Text);
            Notify();
            return true;
        }

        /// Five unequipped echoes of one star → one new echo of the chosen kind and main stat.
        public bool Merge(int star, int defId, EchoStatType mainType)
        {
            var def = EchoDB.Get(defId);
            if (def == null || def.star != star) return false;
            var pool = new List<EchoInstance>();
            foreach (var e in _inv) { int m, s; if (e.locked || EquipLocation(e.uid, out m, out s)) continue; var d = e.Def; if (d != null && d.star == star) pool.Add(e); }
            if (pool.Count < 5) { HUDController.Toast("미장착 ★" + star + " 에코가 5개 필요합니다 (" + pool.Count + "/5)"); return false; }
            int cost = EchoStats.MergeCost(star);
            if (ProgressSystem.I == null || !ProgressSystem.I.SpendShards(cost)) { HUDController.Toast("조각소리 부족 (" + cost + " 필요)"); return false; }
            pool.Sort((a, b) => a.level.CompareTo(b.level));
            for (int i = 0; i < 5; i++) _inv.Remove(pool[i]);
            var inst = EchoStats.Roll(defId, _nextUid, _rng);
            _nextUid++;
            EchoStats.RollMain(inst, mainType, _rng);
            _inv.Add(inst);
            _discovered.Add(defId);
            AudioMan.I.Play2D(Sfx.Ult(), 0.6f, 1.2f);
            HUDController.Toast("합성 — " + def.name + " ★" + def.star + "  [" + inst.main.Text + "]");
            Notify();
            return true;
        }

        // ------------------------------------------------------------ effect queries (per member)
        float PassiveSum(int member, EchoPassive kind)
        {
            float v = 0f;
            for (int s = 0; s < SlotCount; s++)
            {
                var d = DefAt(member, s);
                if (d != null && d.passive == kind) v += d.passiveValue;
            }
            return v;
        }

        /// Rolled main+sub stat total of one type across a member's loadout.
        public float StatSum(int member, EchoStatType t)
        {
            float v = 0f;
            for (int s = 0; s < SlotCount; s++)
            {
                var inst = InstanceAt(member, s);
                if (inst != null) v += inst.Sum(t);
            }
            return v;
        }

        public int FamilyCount(int member, EchoFamily f)
        {
            int n = 0;
            for (int s = 0; s < SlotCount; s++)
            {
                var d = DefAt(member, s);
                if (d != null && d.family == f) n++;
            }
            return n;
        }

        public bool ShadowSonata(int member) { return FamilyCount(member, EchoFamily.Shadow) >= 2; }
        public bool GuardSonata(int member) { return FamilyCount(member, EchoFamily.Guard) >= 2; }

        /// Outgoing damage from def passives + sonata. Rolled ATK stats are NOT
        /// here — they flow through MemberConfig.EffAtk via PushStats.
        public float DamageMulFor(int member)
        {
            float mul = 1f + PassiveSum(member, EchoPassive.AtkPct) / 100f + PassiveSum(member, EchoPassive.AllElemPct) / 100f;
            if (ShadowSonata(member)) mul *= 1.10f;
            return mul;
        }

        public float SkillDamageMulFor(int member)
        {
            return 1f + PassiveSum(member, EchoPassive.SkillDmgPct) / 100f + StatSum(member, EchoStatType.SkillDmg) / 100f;
        }

        public float MoveSpeedMulFor(int member)
        {
            return 1f + PassiveSum(member, EchoPassive.MoveSpeedPct) / 100f + StatSum(member, EchoStatType.MoveSpd) / 100f;
        }

        public float ConcertoMulFor(int member)
        {
            return 1f + StatSum(member, EchoStatType.ConcertoGain) / 100f;
        }

        public float DamageTakenMulFor(int member)
        {
            float mul = 1f - PassiveSum(member, EchoPassive.DamageReductionPct) / 100f - StatSum(member, EchoStatType.DmgReduce) / 100f;
            if (GuardSonata(member)) mul *= 0.92f;
            return Mathf.Clamp(mul, 0.5f, 1f);
        }

        /// Writes the rolled ATK/crit stats into each member so EffAtk / crit
        /// formulas (and the stat sheet) pick them up.
        void PushStats()
        {
            if (_team == null) _team = UnityEngine.Object.FindAnyObjectByType<TeamManager>();
            if (_team == null) return;
            for (int m = 0; m < MemberCount && m < _team.members.Length; m++)
            {
                var mem = _team.members[m];
                if (mem == null) continue;
                mem.echoAtkFlat = StatSum(m, EchoStatType.AtkFlat);
                mem.echoAtkPct = StatSum(m, EchoStatType.AtkPct) / 100f;
                mem.echoCritChance = StatSum(m, EchoStatType.CritRate) / 100f;
                mem.echoCritMul = StatSum(m, EchoStatType.CritDmg) / 100f;
            }
        }

        void Notify()
        {
            PushStats();
            var h = OnChanged;
            if (h != null) h();
            HUDController.NotifyResources();
        }

        // ------------------------------------------------------------ save/load
        [Serializable]
        public class EchoSaveEntry
        {
            public int uid, defId, mainType, level;
            public int revealed = -1;
            public bool locked;
            public float mainVal;
            public int[] subTypes = new int[0];
            public float[] subVals = new float[0];
        }

        public void ExportState(out EchoSaveEntry[] items, out int[] equippedFlat, out int[] discovered, out int nextUid)
        {
            items = new EchoSaveEntry[_inv.Count];
            for (int i = 0; i < _inv.Count; i++)
            {
                var inst = _inv[i];
                var e = new EchoSaveEntry
                {
                    uid = inst.uid,
                    defId = inst.defId,
                    mainType = (int)inst.main.type,
                    level = inst.level,
                    revealed = inst.revealed,
                    locked = inst.locked,
                    mainVal = inst.main.value,
                    subTypes = new int[inst.subs.Length],
                    subVals = new float[inst.subs.Length],
                };
                for (int s = 0; s < inst.subs.Length; s++)
                {
                    e.subTypes[s] = (int)inst.subs[s].type;
                    e.subVals[s] = inst.subs[s].value;
                }
                items[i] = e;
            }
            equippedFlat = new int[MemberCount * SlotCount];
            for (int m = 0; m < MemberCount; m++)
                for (int s = 0; s < SlotCount; s++)
                    equippedFlat[m * SlotCount + s] = _equipped[m][s];
            discovered = new int[_discovered.Count];
            _discovered.CopyTo(discovered);
            nextUid = _nextUid;
        }

        public void ImportState(EchoSaveEntry[] items, int[] equippedFlat, int[] discovered, int nextUid)
        {
            _inv.Clear();
            _discovered.Clear();
            if (items != null)
                foreach (var e in items)
                {
                    if (e == null || EchoDB.Get(e.defId) == null) continue;
                    var inst = new EchoInstance
                    {
                        uid = e.uid,
                        defId = e.defId,
                        level = e.level,
                        revealed = e.revealed,
                        locked = e.locked,
                        main = new EchoStat((EchoStatType)e.mainType, e.mainVal),
                        subs = new EchoStat[e.subTypes.Length],
                    };
                    for (int s = 0; s < e.subTypes.Length && s < e.subVals.Length; s++)
                        inst.subs[s] = new EchoStat((EchoStatType)e.subTypes[s], e.subVals[s]);
                    _inv.Add(inst);
                }
            for (int m = 0; m < MemberCount; m++)
                for (int s = 0; s < SlotCount; s++)
                {
                    int uid = equippedFlat != null && m * SlotCount + s < equippedFlat.Length ? equippedFlat[m * SlotCount + s] : -1;
                    _equipped[m][s] = Get(uid) != null ? uid : -1;
                }
            if (discovered != null) foreach (var d in discovered) _discovered.Add(d);
            _nextUid = Mathf.Max(1, nextUid);
            Notify();
        }
    }
}
