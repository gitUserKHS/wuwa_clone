using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Per-character growth state (design doc ch.4).
    [Serializable]
    public class CharacterProgress
    {
        public int level = 1;
        public float exp;
        public int ascension;                 // 0..3 (돌파 I/II/III)
        public int[] skillLv = { 1, 1, 1, 1 }; // 일반·강공 / 공명 스킬 / 공명 해방 / 변주·여운
    }

    /// Growth tables + formulas shared by characters, skills and weapons.
    public static class Growth
    {
        public const int MaxLevel = 50, MaxAscension = 3, MaxSkill = 5;
        public const int WeaponMaxLevel = 40;
        public const int AnyCrystal = -110;                       // cost sentinel: any element crystal

        public static readonly string[] SkillNames = { "일반 · 강공격", "공명 스킬 (E)", "공명 해방 (R)", "변주 · 여운" };
        public static readonly string[] AscensionNames = { "", "I", "II", "III" };

        // ---------------------------------------------------------------- characters
        public static float ExpNeed(int level) { return 60f + 20f * level + 0.5f * level * level; }
        public static int LevelCap(int asc) { return asc >= MaxAscension ? MaxLevel : 20 + asc * 10; }
        /// Level required to perform ascension asc → asc+1 (20 / 30 / 40).
        public static int AscendGate(int asc) { return 20 + asc * 10; }
        public static float StatMul(int level, int asc) { return 1f + 0.045f * (level - 1) + 0.10f * asc; }
        public static int SkillCap(int asc) { return Mathf.Min(MaxSkill, 2 + asc); }

        public struct Cost
        {
            public int shards;
            public int[] itemIds;
            public int[] counts;
            public static Cost Make(int shards, params int[] pairs)
            {
                var c = new Cost { shards = shards, itemIds = new int[pairs.Length / 2], counts = new int[pairs.Length / 2] };
                for (int i = 0; i < pairs.Length / 2; i++) { c.itemIds[i] = pairs[i * 2]; c.counts[i] = pairs[i * 2 + 1]; }
                return c;
            }
        }

        public static Cost AscendCost(int currentAsc, int element)
        {
            int crystal = ItemDB.CrystalFor(element);
            switch (currentAsc)
            {
                case 0: return Cost.Make(300, ItemDB.Residue0, 6, crystal, 3);
                case 1: return Cost.Make(700, ItemDB.Residue1, 6, ItemDB.Residue2, 1, crystal, 6);
                default: return Cost.Make(1500, ItemDB.Residue1, 10, crystal, 10, ItemDB.Crown, 2);
            }
        }

        public static string AscendNode(int asc)
        {
            switch (asc)
            {
                case 1: return "크리 확률 +4%";
                case 2: return "공격력 +6%";
                case 3: return "크리 피해 +12%";
                default: return "";
            }
        }

        public static Cost SkillCost(int fromLv, int element)
        {
            int crystal = ItemDB.CrystalFor(element);
            switch (fromLv)
            {
                case 1: return Cost.Make(100, ItemDB.Residue0, 3);
                case 2: return Cost.Make(200, ItemDB.Residue1, 3, crystal, 2);
                case 3: return Cost.Make(400, ItemDB.Residue1, 5, crystal, 4);
                default: return Cost.Make(800, ItemDB.Residue2, 2, crystal, 6, ItemDB.Crown, 1);
            }
        }

        static readonly float[] BasicMul = { 1f, 1.06f, 1.12f, 1.18f, 1.25f };
        static readonly float[] SkillMulT = { 1f, 1.07f, 1.14f, 1.21f, 1.30f };
        static readonly float[] IntroMul = { 1f, 1.07f, 1.14f, 1.21f, 1.28f };

        public static float SkillMul(int skillIdx, int lv)
        {
            int i = Mathf.Clamp(lv, 1, MaxSkill) - 1;
            switch (skillIdx)
            {
                case 0: return BasicMul[i];
                case 1:
                case 2: return SkillMulT[i];
                default: return IntroMul[i];
            }
        }

        public static string SkillPerk(int skillIdx, int lv)
        {
            switch (skillIdx)
            {
                case 0: return lv >= 5 ? "회로 강화 배율 2.0×" : "";
                case 1: return lv >= 5 ? "쿨다운 −1s" : "";
                case 2: return lv >= 5 ? "필요 에너지 90" : "";
                default: return lv >= 5 ? "여운 +2s · 배율 +0.04" : lv >= 3 ? "여운 +1s" : "";
            }
        }

        public static int SkillIndexOf(AttackCat cat)
        {
            switch (cat)
            {
                case AttackCat.Basic:
                case AttackCat.Heavy:
                case AttackCat.Dash:
                case AttackCat.Plunge: return 0;
                case AttackCat.Skill: return 1;
                case AttackCat.Ult: return 2;
                case AttackCat.Intro: return 3;
                default: return -1;
            }
        }

        // ---------------------------------------------------------------- weapons
        public static float WeaponAtk(float baseAtk, int level) { return baseAtk * (1f + 0.04f * (level - 1)); }
        public static float WExpNeed(int level) { return 100f + 25f * level; }
        public static int WeaponMaxAscension(int tier) { return tier <= 1 ? 1 : 3; }
        public static int WeaponLevelCap(int tier, int asc) { return Mathf.Min(tier <= 1 ? 20 : WeaponMaxLevel, 10 + asc * 10); }
        public static int WeaponAscendGate(int asc) { return 10 + asc * 10; }
        public static int WeaponFeedExp(int tier) { return tier <= 1 ? 500 : tier == 2 ? 1500 : 4000; }

        public static Cost WeaponAscendCost(int currentAsc)
        {
            switch (currentAsc)
            {
                case 0: return Cost.Make(150, ItemDB.Residue0, 4);
                case 1: return Cost.Make(300, ItemDB.Residue1, 4, AnyCrystal, 2);
                default: return Cost.Make(600, ItemDB.Residue1, 8, ItemDB.Crown, 1);
            }
        }

        public static float WeaponPassiveValue(WeaponDef def, int asc)
        {
            if (def == null) return 0f;
            switch (def.passive)
            {
                case WeaponPassive.SkillDmgPct: return def.passiveValue + 2f * asc;
                case WeaponPassive.ConcertoGainPct: return def.passiveValue + 4f * asc;
                case WeaponPassive.CritRatePct: return def.passiveValue + 1f * asc;
                default: return 0f;
            }
        }

        // ---------------------------------------------------------------- paying
        static int ResolveItem(int id)
        {
            if (id != AnyCrystal) return id;
            int best = ItemDB.Crystal0, bestN = -1;
            for (int e = 0; e < 3; e++) { int n = Inventory.Count(ItemDB.CrystalFor(e)); if (n > bestN) { bestN = n; best = ItemDB.CrystalFor(e); } }
            return best;
        }

        public static bool CanPay(Cost c, out string missing)
        {
            var list = new List<string>();
            int shards = ProgressSystem.I != null ? ProgressSystem.I.Shards : 0;
            if (shards < c.shards) list.Add("조각소리 " + (c.shards - shards) + " 부족");
            if (c.itemIds != null)
                for (int i = 0; i < c.itemIds.Length; i++)
                {
                    int id = ResolveItem(c.itemIds[i]);
                    int have = Inventory.Count(id);
                    if (have < c.counts[i]) list.Add(ItemDB.Get(id).name + " " + (c.counts[i] - have) + " 부족");
                }
            missing = string.Join(" · ", list.ToArray());
            return list.Count == 0;
        }

        public static bool Pay(Cost c)
        {
            string why;
            if (!CanPay(c, out why)) return false;
            if (ProgressSystem.I != null && !ProgressSystem.I.SpendShards(c.shards)) return false;
            if (c.itemIds != null)
                for (int i = 0; i < c.itemIds.Length; i++) Inventory.Remove(ResolveItem(c.itemIds[i]), c.counts[i]);
            return true;
        }

        /// "조각소리 300 · 흐린 잔재 6/6 ✓ · 회절 결정 1/3 ✗"
        public static string CostText(Cost c)
        {
            int shards = ProgressSystem.I != null ? ProgressSystem.I.Shards : 0;
            string s = "조각소리 " + c.shards + (shards >= c.shards ? " ✓" : " ✗");
            if (c.itemIds != null)
                for (int i = 0; i < c.itemIds.Length; i++)
                {
                    int id = ResolveItem(c.itemIds[i]);
                    int have = Inventory.Count(id);
                    s += " · " + ItemDB.Get(id).name + " " + have + "/" + c.counts[i] + (have >= c.counts[i] ? " ✓" : " ✗");
                }
            return s;
        }
    }
}
