using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    public enum BountyType { KillRegion, Rift, RankS, Elite, Chest }

    public class Bounty
    {
        public int id;
        public BountyType type;
        public int region = -1;
        public int goal = 1, progress;
        public bool done, grand;

        public string Title
        {
            get
            {
                string g = grand ? "대현상 · " : "현상 · ";
                switch (type)
                {
                    case BountyType.KillRegion: return g + WorldRegions.RegionName(region) + "의 그림자 토벌";
                    case BountyType.Rift: return g + "침식 균열 정화";
                    case BountyType.RankS: return g + "완벽한 악장 (전투 평가 S)";
                    case BountyType.Elite: return g + "정예 그림자 토벌";
                    default: return g + "보물 상자 수색";
                }
            }
        }

        public string Objective
        {
            get
            {
                string p = "  (" + Mathf.Min(progress, goal) + "/" + goal + ")";
                switch (type)
                {
                    case BountyType.KillRegion: return WorldRegions.RegionName(region) + "에서 그림자 " + goal + "체 처치" + p;
                    case BountyType.Rift: return "침식 균열 " + goal + "회 정화" + p;
                    case BountyType.RankS: return "전투 평가 S " + goal + "회" + p;
                    case BountyType.Elite: return "주술사 · 거암 " + goal + "체 처치" + p;
                    default: return "보물 상자 " + goal + "개 개봉" + p;
                }
            }
        }

        public int Shards { get { return grand ? 500 : type == BountyType.RankS ? 250 : type == BountyType.Rift ? 220 : type == BountyType.Elite ? 200 : 150; } }

        public string RewardText
        {
            get
            {
                return grand ? "조각소리 500 · 검은 잔재 2 · 왕관 파편 1 · 짙은 잔재 4"
                             : "조각소리 " + Shards + " · 짙은 잔재 2 · " + ItemDB.Get(ItemDB.CrystalFor(DropTables.ElementOfRegion(region < 0 ? 0 : region))).name + " 2 · 공명석 1 · 공명석 조각 2";
            }
        }

        public bool HasTarget { get { return type == BountyType.KillRegion; } }
        public Vector3 Target { get { return BountyBoard.RegionCenter(region); } }
    }

    /// 현상 게시판: three bounties a day (in-game day, 44 min) plus a grand bounty every
    /// third day. Always active — progress comes from gameplay events, rewards on completion.
    public static class BountyBoard
    {
        public static readonly List<Bounty> Active = new List<Bounty>();
        public static int Day = -1;
        public static int GrandDay = -1;
        public static event Action Changed;
        static int _nextId = 1;

        static readonly Vector2[] Centers = { new Vector2(0, -40), new Vector2(-60, 210), new Vector2(340, 330), new Vector2(390, -100), new Vector2(-360, -80), new Vector2(-190, 500), new Vector2(90, -360), new Vector2(-215, -165), new Vector2(0, 0) };
        public static Vector3 RegionCenter(int region)
        {
            var c = Centers[Mathf.Clamp(region, 0, Centers.Length - 1)];
            return new Vector3(c.x, WorldRegions.HeightAt(c.x, c.y), c.y);
        }

        public static void Tick()
        {
            if (DayNightCycle.DayIndex != Day) Generate(DayNightCycle.DayIndex);
        }

        static void Generate(int day)
        {
            Day = day;
            // grand bounty survives its 3-day window; everything else is replaced
            Active.RemoveAll(b => !b.grand || b.done || day - GrandDay >= 3);
            var rng = new System.Random(day * 7919 + 13);
            var types = new List<BountyType> { BountyType.KillRegion, BountyType.Rift, BountyType.RankS, BountyType.Elite, BountyType.Chest };
            var discovered = new List<int>();
            for (int r = 0; r < 8; r++) if (MapDiscovery.RegionDiscovered(r)) discovered.Add(r);
            if (discovered.Count == 0) discovered.Add(WorldRegions.Plains);
            for (int i = 0; i < 3; i++)
            {
                var t = types[rng.Next(types.Count)];
                types.Remove(t);
                var b = new Bounty { id = _nextId++, type = t };
                switch (t)
                {
                    case BountyType.KillRegion: b.region = discovered[rng.Next(discovered.Count)]; b.goal = 8 + rng.Next(7); break;
                    case BountyType.Rift: b.goal = 1; break;
                    case BountyType.RankS: b.goal = 2; break;
                    case BountyType.Elite: b.goal = 3; break;
                    default: b.goal = 2; break;
                }
                if (b.region < 0) b.region = discovered[rng.Next(discovered.Count)];
                Active.Add(b);
            }
            if (day % 3 == 0 && day - GrandDay >= 3)
            {
                GrandDay = day;
                var g = new Bounty { id = _nextId++, grand = true, type = rng.Next(2) == 0 ? BountyType.Elite : BountyType.KillRegion, region = discovered[rng.Next(discovered.Count)] };
                g.goal = g.type == BountyType.Elite ? 6 : 20;
                Active.Add(g);
            }
            if (Changed != null) Changed();
            if (QuestSystem.I != null) QuestSystem.I.RefreshTracker();
        }

        static void Progress(Func<Bounty, bool> match, int n = 1)
        {
            bool any = false;
            foreach (var b in Active)
            {
                if (b.done || !match(b)) continue;
                b.progress += n;
                any = true;
                if (b.progress >= b.goal) Complete(b);
            }
            if (any)
            {
                if (Changed != null) Changed();
                if (QuestSystem.I != null) QuestSystem.I.RefreshTracker();
            }
        }

        public static void NotifyKill(EnemyKind kind, int region, bool boss)
        {
            if (boss) return;
            bool elite = kind == EnemyKind.Ranged || kind == EnemyKind.Tank;
            Progress(b => b.type == BountyType.KillRegion && b.region == region);
            if (elite) Progress(b => b.type == BountyType.Elite);
        }

        public static void NotifyRift() { Progress(b => b.type == BountyType.Rift); }
        public static void NotifyRank(string rank) { if (rank == "S") Progress(b => b.type == BountyType.RankS); }
        public static void NotifyChest() { Progress(b => b.type == BountyType.Chest); }

        static void Complete(Bounty b)
        {
            b.done = true;
            if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(b.Shards);
            if (b.grand)
            {
                Inventory.Add(ItemDB.Residue2, 2); Inventory.Add(ItemDB.Crown, 1); Inventory.Add(ItemDB.Residue1, 4);
            }
            else
            {
                Inventory.Add(ItemDB.Residue1, 2);
                Inventory.Add(ItemDB.CrystalFor(DropTables.ElementOfRegion(b.region < 0 ? 0 : b.region)), 2);
                Inventory.Add(ItemDB.Stone1, 1); Inventory.Add(ItemDB.Stone0, 2);
            }
            AudioMan.I.Play2D(Sfx.PerfectDodge(), 0.7f, 1.1f);
            HUDController.Toast((b.grand ? "대현상 완료! " : "현상 완료 — ") + b.Title.Replace("현상 · ", "").Replace("대", ""));
            if (QuestSystem.I != null && QuestSystem.I.TrackedBounty == b.id) QuestSystem.I.TrackedBounty = -1;
            Tutorial.Trigger("bounty_done");
        }

        public static Bounty Get(int id) { foreach (var b in Active) if (b.id == id) return b; return null; }

        // ---------------------------------------------------------------- save
        public static void Export(out int day, out int grandDay, out int[] types, out int[] regions, out int[] goals, out int[] progress, out int[] done, out int[] grand)
        {
            day = Day; grandDay = GrandDay;
            int n = Active.Count;
            types = new int[n]; regions = new int[n]; goals = new int[n]; progress = new int[n]; done = new int[n]; grand = new int[n];
            for (int i = 0; i < n; i++)
            {
                var b = Active[i];
                types[i] = (int)b.type; regions[i] = b.region; goals[i] = b.goal; progress[i] = b.progress; done[i] = b.done ? 1 : 0; grand[i] = b.grand ? 1 : 0;
            }
        }

        public static void Import(int day, int grandDay, int[] types, int[] regions, int[] goals, int[] progress, int[] done, int[] grand)
        {
            Active.Clear();
            Day = day; GrandDay = grandDay;
            if (types != null)
                for (int i = 0; i < types.Length; i++)
                    Active.Add(new Bounty
                    {
                        id = _nextId++, type = (BountyType)types[i],
                        region = regions != null && i < regions.Length ? regions[i] : -1,
                        goal = goals != null && i < goals.Length ? goals[i] : 1,
                        progress = progress != null && i < progress.Length ? progress[i] : 0,
                        done = done != null && i < done.Length && done[i] != 0,
                        grand = grand != null && i < grand.Length && grand[i] != 0,
                    });
            if (Changed != null) Changed();
        }
    }
}
