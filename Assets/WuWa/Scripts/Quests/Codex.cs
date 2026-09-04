using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// 도감 data: enemy and region entries, kill counters, discovery flags.
    public static class Codex
    {
        public class EnemyEntry { public string key, name, desc, regions, drops, telegraph; public int kindIndex = -1; public bool elite, boss; }
        public class RegionEntry { public int id; public string name, desc, features; }

        public static readonly EnemyEntry[] Enemies =
        {
            new EnemyEntry { key = "melee", kindIndex = (int)EnemyKind.Melee, name = "그림자 방랑자", desc = "떠돌이 악사의 발소리가 굳은 그림자. 무리를 지어 다니며 짧은 할퀴기로 덤빈다.", regions = "녹야 평원 · 속삭임 숲 · 전 지역", drops = "조각소리 6×배율 · 흐린 잔재 40% · 에코 20%", telegraph = "금색 예고 후 할퀴기 — 패리 가능" },
            new EnemyEntry { key = "fast", kindIndex = (int)EnemyKind.Fast, name = "질풍의 그림자", desc = "숲을 달리던 파발꾼의 숨소리. 빠르게 파고들었다 빠진다.", regions = "속삭임 숲 · 노을빛 언덕", drops = "조각소리 6×배율 · 흐린 잔재 40% · 에코 20%", telegraph = "돌진 직전 금색 예고 — 회피 직후 반격이 유리" },
            new EnemyEntry { key = "ranged", kindIndex = (int)EnemyKind.Ranged, name = "주술사의 그림자", desc = "기우제의 주문 소리. 멀리서 그림자 화살을 쏜다.", regions = "잿빛 황무지 · 거울 호수", drops = "조각소리 18×배율 · 흐린 2 · 짙은 1(+30%) · 결정 20% · 에코 확정", telegraph = "화살 3발 — 근접해서 끊거나 패리" },
            new EnemyEntry { key = "tank", kindIndex = (int)EnemyKind.Tank, name = "거암의 그림자", desc = "채석장 정 소리의 잔재. 느리지만 한 방이 무겁고 그로기가 길다.", regions = "서리 고원 · 노래잃은 도시", drops = "조각소리 18×배율 · 흐린 2 · 짙은 1(+30%) · 결정 20% · 에코 확정", telegraph = "내려찍기 금색 예고 — 패리 시 큰 그로기" },
            new EnemyEntry { key = "elite", elite = true, name = "침식 정예", desc = "균열에서 쏟아지는 붉은 빛의 그림자. 체력과 공격이 1.7배.", regions = "침식 균열 · 시련 제단", drops = "짙은 잔재 2 · 검은 잔재 30% · 조율기(피티)", telegraph = "일반 개체와 같은 예고, 더 빠른 연계" },
            new EnemyEntry { key = "boss", boss = true, name = "무관의 그림자", desc = "첫 번째 노래의 첫 소절. 왕관 없이 군림하던 서곡의 주인.", regions = "녹야 평원 보스 아레나 (매일 재래)", drops = "조각소리 138/60 · 검은 3 · 회절 결정 4 · 조율기 2 · 왕관 파편 3/1 · ★5 에코", telegraph = "이중 충격파 — 두 번째 파동 직전 패리" },
            new EnemyEntry { key = "rift", name = "침식 균열", desc = "밤에 더 자주 열리는 보랏빛 빛기둥. 안의 그림자를 모두 정화하면 닫힌다.", regions = "평원 · 숲 · 언덕 · 호수 · 황무지 · 고원 · 도시", drops = "조각소리 60+6·Lv · 짙은 2 · 지역 결정 2 · 조율기 35% · 에코", telegraph = "80~140초 주기 · 지역 첫 정화 시 정화율 반영" },
        };

        public static readonly RegionEntry[] Regions =
        {
            new RegionEntry { id = WorldRegions.Plains, name = "녹야 평원", desc = "조율사가 눈을 뜬 초원. 첫 공명탑과 보스 아레나가 있다.", features = "공명탑 · 보스 아레나 · 시련 제단(동쪽) · 나무 상자" },
            new RegionEntry { id = WorldRegions.Forest, name = "속삭임 숲", desc = "파발꾼의 숨소리가 남은 숲. 나무 사이로 질풍의 그림자가 달린다.", features = "숲 공명탑 · 갈고리 지점 · 은빛 상자" },
            new RegionEntry { id = WorldRegions.Bloom, name = "노을빛 언덕", desc = "보랏빛 나무가 서 있는 언덕. 회절 결정이 맺힌다.", features = "표석 · 채집 군락 · 균열" },
            new RegionEntry { id = WorldRegions.Lake, name = "거울 호수", desc = "노래가 멈춘 뒤에도 하늘을 비추는 호수. 응결 결정의 산지.", features = "표석 · 군락 2곳 · 수영 · 다리 건너 상자" },
            new RegionEntry { id = WorldRegions.Waste, name = "잿빛 황무지", desc = "재만 남은 벌판. 주술사의 그림자가 화살을 쏜다. 용융 결정의 산지.", features = "잿빛 공명탑 · 군락 2곳 · 균열" },
            new RegionEntry { id = WorldRegions.Frost, name = "서리 고원", desc = "서쪽 끝의 눈 덮인 고원. 거암의 그림자가 지킨다.", features = "서리 공명탑 · 군락 2곳 · 황금 상자" },
            new RegionEntry { id = WorldRegions.Ruins, name = "노래잃은 도시", desc = "남쪽의 무너진 도시. 광장 아래 잔향이 고여 있다.", features = "표석 · 군락 2곳 · 갈고리 · 잔해" },
            new RegionEntry { id = WorldRegions.Village, name = "메아리 마을", desc = "상인과 주민이 남은 마지막 마을. 상점과 현상 게시판.", features = "상점 · 표석 · NPC · 현상 게시판(퀘스트 J)" },
            new RegionEntry { id = WorldRegions.Rim, name = "세계의 가장자리", desc = "세계를 감싼 산맥. 눈과 바위뿐이다.", features = "설원 · 능선" },
        };

        public static readonly int[] KillsByKind = new int[5];
        public static int EliteKills, BossKills;

        public static void NotifyKill(EnemyKind kind, bool boss, bool elite)
        {
            if (boss) BossKills++;
            else if (elite) EliteKills++;
            int k = (int)kind;
            if (k >= 0 && k < KillsByKind.Length) KillsByKind[k]++;
            GameFlags.Set("seen_" + KeyFor(kind, boss, elite));
        }

        public static void NotifySeen(EnemyKind kind, bool boss, bool elite) { GameFlags.Set("seen_" + KeyFor(kind, boss, elite)); }

        static string KeyFor(EnemyKind kind, bool boss, bool elite)
        {
            if (boss) return "boss";
            if (elite) return "elite";
            switch (kind)
            {
                case EnemyKind.Fast: return "fast";
                case EnemyKind.Ranged: return "ranged";
                case EnemyKind.Tank: return "tank";
                default: return "melee";
            }
        }

        /// Saves from before the codex existed carry no encounter flags: infer them once from progress.
        public static void SeedIfVeteran(float playSeconds)
        {
            if (playSeconds < 300f || GameFlags.Has("codex_seeded")) return;
            GameFlags.Set("codex_seeded");
            int q = QuestSystem.I != null ? QuestSystem.I.CurrentIndex : 0;
            GameFlags.Set("seen_melee");
            if (q > 2 || MapDiscovery.RegionDiscovered(WorldRegions.Forest) || MapDiscovery.RegionDiscovered(WorldRegions.Bloom)) GameFlags.Set("seen_fast");
            if (q > 6 || MapDiscovery.RegionDiscovered(WorldRegions.Waste) || MapDiscovery.RegionDiscovered(WorldRegions.Lake)) GameFlags.Set("seen_ranged");
            if (q > 8 || MapDiscovery.RegionDiscovered(WorldRegions.Frost) || MapDiscovery.RegionDiscovered(WorldRegions.Ruins)) GameFlags.Set("seen_tank");
            if (q > 4) GameFlags.Set("seen_boss");
            if (ContentStats.RiftsClosed > 0 || ContentStats.ArenaClears > 0) GameFlags.Set("seen_elite");
        }

        public static bool EnemySeen(EnemyEntry e)
        {
            if (e.key == "rift") return ContentStats.RiftsClosed > 0 || MapSystem.Dynamic.Count > 0 || GameFlags.Has("seen_rift");
            return GameFlags.Has("seen_" + e.key);
        }

        public static int KillsOf(EnemyEntry e)
        {
            if (e.boss) return BossKills;
            if (e.elite) return EliteKills;
            if (e.key == "rift") return ContentStats.RiftsClosed;
            return e.kindIndex >= 0 && e.kindIndex < KillsByKind.Length ? KillsByKind[e.kindIndex] : 0;
        }

        public static bool WeaponSeen(int defId) { return GameFlags.Has("weapon_" + defId); }
        public static bool ItemSeen(int itemId) { return GameFlags.Has("item_" + itemId); }

        public static void Export(out int[] kills, out int elite, out int boss) { kills = (int[])KillsByKind.Clone(); elite = EliteKills; boss = BossKills; }
        public static void Import(int[] kills, int elite, int boss)
        {
            for (int i = 0; i < KillsByKind.Length; i++) KillsByKind[i] = kills != null && i < kills.Length ? kills[i] : 0;
            EliteKills = elite; BossKills = boss;
        }
        public static void Reset() { for (int i = 0; i < KillsByKind.Length; i++) KillsByKind[i] = 0; EliteKills = 0; BossKills = 0; }
    }
}
