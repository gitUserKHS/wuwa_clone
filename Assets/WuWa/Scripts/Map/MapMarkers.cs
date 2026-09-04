using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    public enum MapCategory { Tower, Waystone, Boss, Arena, Rift, Village, Chest, Grapple, Camp, Quest, Pin, Player, Tracked, Gather }

    public class MapMarker
    {
        public MapCategory cat;
        public Vector3 pos;
        public string name;
        public string icon;
        public Color color;
        public float size = 16f;
        public int lod;                 // minimum zoom tier on the full map
        public bool warpable;
        public bool active;             // tower activated / waystone attuned / chest unopened
        public string status;
        public object source;
        public bool onMinimap = true;
    }

    /// Shared marker collection (full map + minimap) with discovery + filter rules.
    public static class MapMarkers
    {
        public static readonly List<MapMarker> List = new List<MapMarker>();
        static float _next;
        static ResonanceTower[] _towers;
        static EnemySpawner[] _spawners;
        static Transform _bossSpawner;
        static Waystone[] _stonesCache;
        public static readonly MapCategory[] Filterable =
            { MapCategory.Tower, MapCategory.Waystone, MapCategory.Boss, MapCategory.Arena, MapCategory.Village, MapCategory.Chest, MapCategory.Grapple, MapCategory.Camp, MapCategory.Pin, MapCategory.Gather };
        public static readonly string[] FilterLabels = { "공명탑", "공명 표석", "보스", "시련 제단", "마을 · NPC", "보물 상자", "갈고리 지점", "적 캠프", "핀", "채집 군락" };
        public static readonly string[] FilterIcons = { "tower", "waystone", "boss", "arena", "house", "chest", "grapple", "camp", "pin", "crystal" };

        public static readonly Color TowerOn = new Color(1f, 0.85f, 0.4f), TowerOff = new Color(0.6f, 0.62f, 0.66f);
        public static readonly Color StoneOn = new Color(0.55f, 0.95f, 1f), StoneOff = new Color(0.5f, 0.55f, 0.6f);
        public static readonly Color BossC = new Color(1f, 0.38f, 0.32f), ArenaC = new Color(0.55f, 0.9f, 1f), RiftC = new Color(0.85f, 0.5f, 1f);
        public static readonly Color VillageC = new Color(1f, 0.65f, 0.3f), ChestGold = new Color(1f, 0.82f, 0.3f), ChestSilver = new Color(0.85f, 0.88f, 0.95f), ChestWood = new Color(0.75f, 0.6f, 0.4f);
        public static readonly Color GrappleC = new Color(0.5f, 0.9f, 0.85f), CampC = new Color(1f, 0.45f, 0.4f), QuestC = new Color(1f, 0.9f, 0.35f), PlayerC = new Color(0.55f, 0.95f, 1f);

        public static bool Enabled(MapCategory c)
        {
            int idx = System.Array.IndexOf(Filterable, c);
            if (idx < 0) return true;                      // Player / Quest / Rift / Tracked are always on
            return (SettingsStore.D.mapFilters & (1 << idx)) != 0;
        }

        public static void SetEnabled(MapCategory c, bool on)
        {
            int idx = System.Array.IndexOf(Filterable, c);
            if (idx < 0) return;
            int m = SettingsStore.D.mapFilters;
            m = on ? (m | (1 << idx)) : (m & ~(1 << idx));
            SettingsStore.D.mapFilters = m;
            SettingsStore.MarkDirty();
        }

        public static void SetAll(bool on)
        {
            SettingsStore.D.mapFilters = on ? -1 : 0;
            SettingsStore.MarkDirty();
        }

        public static bool TowerActiveInRegion(int region)
        {
            if (_towers == null || _towers.Length == 0) _towers = Object.FindObjectsByType<ResonanceTower>(FindObjectsSortMode.None);
            foreach (var t in _towers)
                if (t != null && t.Activated && WorldRegions.RegionAt(t.transform.position.x, t.transform.position.z) == region) return true;
            return false;
        }

        /// Rebuilds the list (at most 10 Hz unless forced).
        public static void Collect(bool force = false)
        {
            if (!force && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.1f;
            List.Clear();

            if (_towers == null || _towers.Length == 0) _towers = Object.FindObjectsByType<ResonanceTower>(FindObjectsSortMode.None);
            foreach (var t in _towers)
                if (t != null)
                    List.Add(new MapMarker { cat = MapCategory.Tower, pos = t.transform.position, name = t.towerName, icon = "tower", color = t.Activated ? TowerOn : TowerOff, size = 24f, lod = 0, warpable = t.Activated, active = t.Activated, status = t.Activated ? "해방됨 · 워프 가능" : "미해방", source = t });

            foreach (var w in Waystone.All)
                if (w != null)
                    List.Add(new MapMarker { cat = MapCategory.Waystone, pos = w.transform.position, name = w.stoneName, icon = "waystone", color = w.Discovered ? StoneOn : StoneOff, size = 20f, lod = 0, warpable = w.Discovered, active = w.Discovered, status = w.Discovered ? "조율됨 · 워프 가능" : "미조율", source = w });

            if (_bossSpawner == null) { var b = GameObject.Find("BossSpawner"); if (b != null) _bossSpawner = b.transform; }
            if (_bossSpawner != null)
                List.Add(new MapMarker { cat = MapCategory.Boss, pos = _bossSpawner.position, name = "무관의 그림자", icon = "boss", color = BossC, size = 22f, lod = 0, status = "보스" });
            if (ArenaTrial.I != null)
                List.Add(new MapMarker { cat = MapCategory.Arena, pos = ArenaTrial.I.transform.position, name = "시련의 제단", icon = "arena", color = ArenaC, size = 20f, lod = 0, status = "완주 " + ContentStats.ArenaClears + "회" });

            foreach (var d in MapSystem.Dynamic)
                List.Add(new MapMarker { cat = d.cat, pos = d.pos, name = d.label, icon = d.cat == MapCategory.Rift ? "rift" : "dot", color = RiftC, size = 22f, lod = 0, status = "활성 중", source = d });

            // village + NPCs (NPCs need a discovered region)
            List.Add(new MapMarker { cat = MapCategory.Village, pos = new Vector3(-215f, 0f, -165f), name = "메아리 마을", icon = "house", color = VillageC, size = 20f, lod = 0, status = "마을" });
            foreach (var n in NPC.All)
            {
                if (n == null) continue;
                int region = WorldRegions.RegionAt(n.transform.position.x, n.transform.position.z);
                if (!MapDiscovery.RegionDiscovered(region)) continue;
                string icon = n.role == NpcRole.Merchant ? "bag" : n.role == NpcRole.Keeper ? "key" : "dot";
                List.Add(new MapMarker { cat = MapCategory.Village, pos = n.transform.position, name = n.npcName, icon = icon, color = VillageC, size = n.role == NpcRole.Villager ? 12f : 16f, lod = 1, status = n.role == NpcRole.Merchant ? "상인" : n.role == NpcRole.Keeper ? "지기" : "주민", source = n });
            }

            // chests: seen (cell revealed) or the region's tower is active
            foreach (var ch in TreasureChest.All)
            {
                if (ch == null || ch.Opened) continue;
                var p = ch.transform.position;
                if (!MapDiscovery.IsRevealed(p) && !TowerActiveInRegion(WorldRegions.RegionAt(p.x, p.z))) continue;
                List.Add(new MapMarker { cat = MapCategory.Chest, pos = p, name = (ch.tier >= 2 ? "황금" : ch.tier == 1 ? "은빛" : "나무") + " 상자", icon = "chest",
                    color = ch.tier >= 2 ? ChestGold : ch.tier == 1 ? ChestSilver : ChestWood, size = ch.tier >= 2 ? 16f : 13f, lod = ch.tier == 0 ? 2 : 1, active = true, status = "미개봉", source = ch });
            }

            foreach (var g in GrapplePoint.All)
            {
                if (g == null) continue;
                var p = g.transform.position;
                if (!MapDiscovery.RegionDiscovered(WorldRegions.RegionAt(p.x, p.z))) continue;
                List.Add(new MapMarker { cat = MapCategory.Grapple, pos = p, name = "갈고리 지점", icon = "grapple", color = GrappleC, size = 13f, lod = 2, onMinimap = false, source = g });
            }

            if (_spawners == null || _spawners.Length == 0) _spawners = Object.FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None);
            foreach (var s in _spawners)
            {
                if (s == null || s.bossPost) continue;
                var p = s.transform.position;
                if (!MapDiscovery.IsRevealed(p)) continue;
                List.Add(new MapMarker { cat = MapCategory.Camp, pos = p, name = "적 캠프", icon = "camp", color = CampC, size = 13f, lod = 2, onMinimap = false, source = s });
            }

            Vector3 qpos; string qname, qobj;
            if (QuestSystem.I != null && QuestSystem.I.TrackedTarget(out qpos, out qname, out qobj))
                List.Add(new MapMarker { cat = MapCategory.Quest, pos = qpos, name = qname, icon = "quest", color = QuestC, size = 26f, lod = 0, status = qobj });

            foreach (var g in GatherNode.All)
            {
                if (g == null) continue;
                var p = g.transform.position;
                if (!MapDiscovery.RegionDiscovered(g.region)) continue;
                List.Add(new MapMarker { cat = MapCategory.Gather, pos = p, name = "채집 군락", icon = "crystal", color = g.Available ? new Color(0.6f, 1f, 0.7f) : new Color(0.45f, 0.55f, 0.48f), size = 14f, lod = 1, active = g.Available, status = g.Available ? "채집 가능" : "내일 다시", source = g });
            }
            if (MapSystem.HasTracked)
                List.Add(new MapMarker { cat = MapCategory.Tracked, pos = MapSystem.TrackedPos, name = MapSystem.TrackedName, icon = "quest", color = new Color(0.6f, 0.9f, 1f), size = 22f, lod = 0, status = "추적 중" });

            foreach (var p in MapPins.All)
                List.Add(new MapMarker { cat = MapCategory.Pin, pos = p.pos, name = "핀 · " + MapPins.ColorNames[p.color], icon = "pin", color = MapPins.Colors[p.color], size = 20f, lod = 0, status = "우클릭/X: 색 변경 · 홀드: 삭제", source = p });
        }

        // ---------------------------------------------------------------- region stats
        public struct RegionStats { public int chests, chestsOpened, npcs, grapples; public bool hasTower, towerOn, hasStone, stoneOn; }

        public static RegionStats Stats(int region)
        {
            var r = new RegionStats();
            foreach (var ch in TreasureChest.All)
                if (ch != null && WorldRegions.RegionAt(ch.transform.position.x, ch.transform.position.z) == region) { r.chests++; if (ch.Opened) r.chestsOpened++; }
            foreach (var n in NPC.All) if (n != null && WorldRegions.RegionAt(n.transform.position.x, n.transform.position.z) == region) r.npcs++;
            foreach (var g in GrapplePoint.All) if (g != null && WorldRegions.RegionAt(g.transform.position.x, g.transform.position.z) == region) r.grapples++;
            if (_towers == null || _towers.Length == 0) _towers = Object.FindObjectsByType<ResonanceTower>(FindObjectsSortMode.None);
            foreach (var t in _towers) if (t != null && WorldRegions.RegionAt(t.transform.position.x, t.transform.position.z) == region) { r.hasTower = true; if (t.Activated) r.towerOn = true; }
            foreach (var w in Waystone.All) if (w != null && WorldRegions.RegionAt(w.transform.position.x, w.transform.position.z) == region) { r.hasStone = true; if (w.Discovered) r.stoneOn = true; }
            return r;
        }

        public static void InvalidateCaches() { _towers = null; _spawners = null; _bossSpawner = null; _stonesCache = null; List.Clear(); }
    }
}
