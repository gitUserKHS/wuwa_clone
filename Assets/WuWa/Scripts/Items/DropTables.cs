using UnityEngine;

namespace WuWa
{
    /// Material sources (design doc ch.3/6): kills, chests, rifts, the arena, quest chapters.
    public static class DropTables
    {
        public static float RegionMul(int region)
        {
            switch (region)
            {
                case WorldRegions.Forest: return 1.7f;
                case WorldRegions.Bloom: return 1.8f;
                case WorldRegions.Lake: return 1.9f;
                case WorldRegions.Ruins: return 2.0f;
                case WorldRegions.Waste: return 2.2f;
                case WorldRegions.Frost: return 2.5f;
                case WorldRegions.Rim: return 1.5f;
                default: return 1f;
            }
        }

        /// Element whose crystals a region yields (리라에 회절 / 세레네 응결 / 에이리스 용융).
        public static int ElementOfRegion(int region)
        {
            switch (region)
            {
                case WorldRegions.Lake:
                case WorldRegions.Frost: return 1;
                case WorldRegions.Waste:
                case WorldRegions.Ruins: return 2;
                default: return 0;
            }
        }

        static int CrystalAt(Vector3 pos) { return ItemDB.CrystalFor(ElementOfRegion(WorldRegions.RegionAt(pos.x, pos.z))); }

        public static void RollKill(EnemyKind kind, Vector3 pos, bool boss)
        {
            int region = WorldRegions.RegionAt(pos.x, pos.z);
            float m = RegionMul(region);
            if (boss)
            {
                bool first = !GameFlags.Has("boss_loot");
                GameFlags.Set("boss_loot");
                Inventory.Add(ItemDB.Residue2, 3);
                Inventory.Add(ItemDB.Crystal0, first ? 4 : 2);
                Inventory.Add(ItemDB.Tuner, first ? 2 : 1);
                Inventory.Add(ItemDB.Crown, first ? 3 : 1);
                return;
            }
            bool elite = kind == EnemyKind.Ranged || kind == EnemyKind.Tank;
            if (elite)
            {
                Inventory.Add(ItemDB.Residue0, 2);
                Inventory.Add(ItemDB.Residue1, Random.value < 0.3f ? 2 : 1);
                if (Random.value < 0.2f) Inventory.Add(CrystalAt(pos), 1);
            }
            else if (Random.value < (m >= 1.8f ? 0.6f : 0.4f)) Inventory.Add(ItemDB.Residue0, 1);
        }

        public static void ChestLoot(int tier, Vector3 pos)
        {
            int crystal = CrystalAt(pos);
            switch (tier)
            {
                case 0:
                    Inventory.Add(ItemDB.Residue0, 2);
                    Inventory.Add(ItemDB.Stone0, Random.value < 0.5f ? 2 : 1);
                    break;
                case 1:
                    Inventory.Add(ItemDB.Residue1, 1);
                    Inventory.Add(crystal, 2);
                    Inventory.Add(ItemDB.Stone1, 1);
                    if (Random.value < 0.5f) Inventory.Add(ItemDB.Tuner, 1);
                    if (Random.value < 0.35f) Inventory.Add(ItemDB.StaminaPotion, 1);
                    break;
                default:
                    Inventory.Add(crystal, 3);
                    Inventory.Add(ItemDB.Stone2, 1);
                    Inventory.Add(ItemDB.Tuner, 2);
                    break;
            }
        }

        public static void RiftLoot(Vector3 pos)
        {
            Inventory.Add(ItemDB.Residue1, 2);
            Inventory.Add(CrystalAt(pos), 2);
            Inventory.TunerPity++;
            if (Random.value < 0.35f || Inventory.TunerPity >= 3) { Inventory.Add(ItemDB.Tuner, 1); Inventory.TunerPity = 0; }
            if (Random.value < 0.3f) Inventory.Add(ItemDB.Residue2, 1);
        }

        public static void ArenaWave(int wave)
        {
            Inventory.Add(ItemDB.Residue1, wave <= 2 ? 1 : wave <= 4 ? 2 : 3);
        }

        public static void ArenaClear(bool first)
        {
            Inventory.Add(ItemDB.Stone2, 1);
            Inventory.Add(ItemDB.Residue2, 2);
            Inventory.Add(ItemDB.Tuner, first ? 2 : 1);
            Inventory.Add(ItemDB.Crown, 1);
            Inventory.AddTokens(3);
        }

        public static void QuestChapter(int chapter)
        {
            switch (chapter)
            {
                case 1: Inventory.Add(ItemDB.Tuner, 1); Inventory.Add(ItemDB.Stone0, 3); Inventory.Add(ItemDB.Residue0, 4); Inventory.Add(ItemDB.FoodAtk, 2); break;
                case 2: Inventory.Add(ItemDB.Tuner, 2); Inventory.Add(ItemDB.Stone1, 2); Inventory.Add(ItemDB.Crystal0, 3); Inventory.Add(ItemDB.FoodDef, 2); break;
                default: Inventory.Add(ItemDB.Tuner, 3); Inventory.Add(ItemDB.Stone2, 1); Inventory.Add(ItemDB.Crown, 2); Inventory.AddTokens(5); break;
            }
        }
    }
}
