using UnityEngine;

namespace WuWa
{
    public enum EchoStatType { AtkPct, AtkFlat, CritRate, CritDmg, SkillDmg, ConcertoGain, MoveSpd, DmgReduce }

    [System.Serializable]
    public struct EchoStat
    {
        public EchoStatType type;
        public float value;
        public EchoStat(EchoStatType t, float v) { type = t; value = v; }

        public string Text
        {
            get
            {
                if (type == EchoStatType.AtkFlat) return EchoStats.NameOf(type) + " +" + Mathf.RoundToInt(value);
                return EchoStats.NameOf(type) + " +" + value.ToString("0.#") + "%";
            }
        }
    }

    /// One physical echo item: a catalogue entry plus its own rolled main stat
    /// and substats (WuWa-style — two copies of the same echo differ).
    public class EchoInstance
    {
        public int uid;
        public int defId;
        public int level;                      // enhancement +0..+5, boosts the main stat
        public EchoStat main;
        public EchoStat[] subs = new EchoStat[0];
        public int revealed = -1;                // opened substats (-1 = legacy save: all)
        public bool locked;                      // no sale / batch dispose / retune while locked

        public const int MaxLevel = 5;
        public int Revealed { get { return revealed < 0 ? subs.Length : Mathf.Clamp(revealed, 0, subs.Length); } }

        public EchoDef Def { get { return EchoDB.Get(defId); } }

        public float Sum(EchoStatType t)
        {
            float v = main.type == t ? main.value : 0f;
            int n = Revealed;
            for (int i = 0; i < n; i++) if (subs[i].type == t) v += subs[i].value;
            return v;
        }
    }

    /// Stat roll tables. Main stat pool depends on cost class, substat count on
    /// rarity (1★=2, 3★=3, 5★=4), all rolled once when the echo drops.
    public static class EchoStats
    {
        public static string NameOf(EchoStatType t)
        {
            switch (t)
            {
                case EchoStatType.AtkPct: return "공격력";
                case EchoStatType.AtkFlat: return "공격력(+)";
                case EchoStatType.CritRate: return "크리 확률";
                case EchoStatType.CritDmg: return "크리 피해";
                case EchoStatType.SkillDmg: return "스킬 피해";
                case EchoStatType.ConcertoGain: return "협주 획득";
                case EchoStatType.MoveSpd: return "이동 속도";
                default: return "피해 감소";
            }
        }

        struct Range
        {
            public EchoStatType t; public float lo, hi;
            public Range(EchoStatType t, float lo, float hi) { this.t = t; this.lo = lo; this.hi = hi; }
        }

        static readonly Range[] MainC4 =
        {
            new Range(EchoStatType.CritRate, 18f, 24f),
            new Range(EchoStatType.CritDmg, 36f, 48f),
            new Range(EchoStatType.AtkPct, 27f, 33f),
        };
        static readonly Range[] MainC3 =
        {
            new Range(EchoStatType.AtkPct, 18f, 24f),
            new Range(EchoStatType.SkillDmg, 20f, 28f),
        };
        static readonly Range[] MainC1 =
        {
            new Range(EchoStatType.AtkPct, 10f, 14f),
            new Range(EchoStatType.AtkFlat, 30f, 50f),
        };
        static readonly Range[] SubPool =
        {
            new Range(EchoStatType.AtkPct, 4f, 8f),
            new Range(EchoStatType.AtkFlat, 12f, 28f),
            new Range(EchoStatType.CritRate, 4f, 8f),
            new Range(EchoStatType.CritDmg, 8f, 16f),
            new Range(EchoStatType.SkillDmg, 5f, 10f),
            new Range(EchoStatType.ConcertoGain, 6f, 12f),
            new Range(EchoStatType.MoveSpd, 3f, 6f),
            new Range(EchoStatType.DmgReduce, 3f, 6f),
        };

        static float RollValue(Range r, System.Random rng)
        {
            float v = Mathf.Lerp(r.lo, r.hi, (float)rng.NextDouble());
            return r.t == EchoStatType.AtkFlat ? Mathf.Round(v) : Mathf.Round(v * 10f) / 10f;
        }

        public static EchoInstance Roll(int defId, int uid, System.Random rng)
        {
            var def = EchoDB.Get(defId);
            if (def == null) return null;
            var inst = new EchoInstance { uid = uid, defId = defId };

            var pool = def.cost >= 4 ? MainC4 : def.cost >= 3 ? MainC3 : MainC1;
            var mr = pool[rng.Next(pool.Length)];
            inst.main = new EchoStat(mr.t, RollValue(mr, rng));

            int subCount = def.star >= 5 ? 4 : def.star >= 3 ? 3 : 2;
            var subs = new EchoStat[subCount];
            var used = new System.Collections.Generic.List<EchoStatType> { inst.main.type };
            for (int i = 0; i < subCount; i++)
            {
                Range pick;
                int guard = 0;
                do { pick = SubPool[rng.Next(SubPool.Length)]; guard++; }
                while (used.Contains(pick.t) && guard < 40);
                used.Add(pick.t);
                subs[i] = new EchoStat(pick.t, RollValue(pick, rng));
            }
            inst.subs = subs;
            inst.revealed = 1;                       // one substat known at drop; tuning opens the rest
            return inst;
        }

        /// Main-stat types an echo of this cost class can carry (merge target choice).
        public static EchoStatType[] MainPool(int cost)
        {
            var pool = cost >= 4 ? MainC4 : cost >= 3 ? MainC3 : MainC1;
            var arr = new EchoStatType[pool.Length];
            for (int i = 0; i < pool.Length; i++) arr[i] = pool[i].t;
            return arr;
        }

        public static void RollMain(EchoInstance inst, EchoStatType type, System.Random rng)
        {
            var def = inst.Def;
            var pool = def.cost >= 4 ? MainC4 : def.cost >= 3 ? MainC3 : MainC1;
            foreach (var r in pool) if (r.t == type) { inst.main = new EchoStat(type, RollValue(r, rng)); return; }
            inst.main = new EchoStat(type, RollValue(pool[0], rng));
        }

        /// Rerolls one substat (type + value), avoiding duplicates.
        public static void RollSub(EchoInstance inst, int idx, System.Random rng)
        {
            if (inst == null || idx < 0 || idx >= inst.subs.Length) return;
            var used = new System.Collections.Generic.List<EchoStatType> { inst.main.type };
            for (int i = 0; i < inst.subs.Length; i++) if (i != idx) used.Add(inst.subs[i].type);
            Range pick; int guard = 0;
            do { pick = SubPool[rng.Next(SubPool.Length)]; guard++; } while (used.Contains(pick.t) && guard < 40);
            inst.subs[idx] = new EchoStat(pick.t, RollValue(pick, rng));
        }

        public const int TuneCost = 50, RetuneCost = 80;
        public static int MergeCost(int star) { return star >= 5 ? 400 : star >= 3 ? 200 : 100; }

        // ------------------------------------------------------------ upgrades
        public static int EnhanceCost(EchoInstance inst) { return 30 * (inst.level + 1); }
        public const int RerollCost = 60;

        /// +12% main stat per level, capped at +5.
        public static bool Enhance(EchoInstance inst)
        {
            if (inst == null || inst.level >= EchoInstance.MaxLevel) return false;
            inst.level++;
            float v = inst.main.value * 1.12f;
            inst.main = new EchoStat(inst.main.type,
                inst.main.type == EchoStatType.AtkFlat ? Mathf.Round(v) : Mathf.Round(v * 10f) / 10f);
            return true;
        }

        /// Reroll every substat (same count, main stat untouched).
        public static void RerollSubs(EchoInstance inst, System.Random rng)
        {
            if (inst == null) return;
            var used = new System.Collections.Generic.List<EchoStatType> { inst.main.type };
            for (int i = 0; i < inst.subs.Length; i++)
            {
                Range pick;
                int guard = 0;
                do { pick = SubPool[rng.Next(SubPool.Length)]; guard++; }
                while (used.Contains(pick.t) && guard < 40);
                used.Add(pick.t);
                inst.subs[i] = new EchoStat(pick.t, RollValue(pick, rng));
            }
        }
    }
}
