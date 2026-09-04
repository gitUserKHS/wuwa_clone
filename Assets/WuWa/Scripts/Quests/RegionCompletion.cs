using UnityEngine;

namespace WuWa
{
    /// 지역 정화율: towers, waystones, chests (first opening), gather nodes and the
    /// first rift closed in the region. 50% / 100% pay out once per region.
    public static class RegionCompletion
    {
        public static int RiftRegionMask;
        static float _next;
        static ResonanceTower[] _towers;
        public static void InvalidateCaches() { _towers = null; _next = 0f; }

        public struct Stat { public int total, done; public int Percent { get { return total <= 0 ? 0 : Mathf.RoundToInt(done * 100f / total); } } }

        static bool RiftEligible(int region) { return region != WorldRegions.Village && region != WorldRegions.Rim; }

        public static Stat Of(int region)
        {
            var s = new Stat();
            if (_towers == null || _towers.Length == 0) _towers = Object.FindObjectsByType<ResonanceTower>(FindObjectsSortMode.None);
            foreach (var t in _towers) if (t != null && WorldRegions.RegionAt(t.transform.position.x, t.transform.position.z) == region) { s.total++; if (t.Activated) s.done++; }
            foreach (var w in Waystone.All) if (w != null && WorldRegions.RegionAt(w.transform.position.x, w.transform.position.z) == region) { s.total++; if (w.Discovered) s.done++; }
            foreach (var c in TreasureChest.All) if (c != null && WorldRegions.RegionAt(c.transform.position.x, c.transform.position.z) == region) { s.total++; if (c.Opened || GameFlags.Has("chest_" + c.chestId)) s.done++; }
            foreach (var n in GatherNode.All) if (n != null && n.region == region) { s.total++; if (n.everGathered) s.done++; }
            if (RiftEligible(region)) { s.total++; if ((RiftRegionMask & (1 << region)) != 0) s.done++; }
            return s;
        }

        public static void NotifyRift(int region)
        {
            if (region < 0 || region > 30) return;
            RiftRegionMask |= 1 << region;
        }

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            if (Cutscene.Active) return;
            for (int r = 0; r < 8; r++)
            {
                if (!MapDiscovery.RegionDiscovered(r)) continue;
                var st = Of(r);
                if (st.total == 0) continue;
                if (st.Percent >= 50 && !GameFlags.Has("comp50_" + r))
                {
                    GameFlags.Set("comp50_" + r);
                    if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(200);
                    Inventory.Add(ItemDB.Tuner, 1); Inventory.Add(ItemDB.Stone1, 2);
                    HUDController.Toast(WorldRegions.RegionName(r) + " 정화율 50% — 조각소리 200 · 조율기 1 · 공명석 2");
                    AudioMan.I.Play2D(Sfx.PerfectDodge(), 0.7f, 1.0f);
                }
                if (st.Percent >= 100 && !GameFlags.Has("comp100_" + r))
                {
                    GameFlags.Set("comp100_" + r);
                    Inventory.AddTokens(2);
                    Inventory.Add(ItemDB.CrystalFor(DropTables.ElementOfRegion(r)), 4);
                    HUDController.Toast(WorldRegions.RegionName(r) + " 정화율 100%! — 시련 증표 2 · 결정 4");
                    AudioMan.I.Play2D(Sfx.Ult(), 0.7f, 1.0f);
                }
            }
        }

        public static string Summary(int region)
        {
            var st = Of(region);
            return "정화율 " + st.Percent + "%  (" + st.done + "/" + st.total + ")";
        }
    }
}
