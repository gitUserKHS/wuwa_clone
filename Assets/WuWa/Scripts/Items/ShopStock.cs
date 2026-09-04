using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Merchant stock with per-day limits (in-game days), selling (80% in the bag,
    /// full price here) and material conversions.
    public static class ShopStock
    {
        public class Offer
        {
            public string key, name, desc;
            public int price;
            public int limit;            // 0 = unlimited per day
            public int bought;
            public int itemId;           // >0: grants an item stack
            public int qty = 1;
            public Action grant;         // non-item offers (weapons, echoes)
            public int minLevel;
        }

        public class Conversion { public string name; public int fromId, fromN, toId, toN, cost; }

        public static readonly List<Offer> Offers = new List<Offer>
        {
            new Offer { key = "w1", name = "조율검 (T2 무기)", desc = "공격 +28 · 스킬 피해 +10%", price = 260, grant = () => { if (WeaponSystem.I != null) WeaponSystem.I.Add(1); } },
            new Offer { key = "w2", name = "잔향검 · 명기 (T3 무기)", desc = "공격 +50 · 협주 획득 +25%", price = 900, minLevel = 15, grant = () => { if (WeaponSystem.I != null) WeaponSystem.I.Add(2); } },
            new Offer { key = "e5", name = "★5 에코 — 무관의 그림자", desc = "코스트 4 · 메인/부가 스탯 무작위", price = 240, grant = () => { if (EchoSystem.I != null) EchoSystem.I.Add(4); } },
            new Offer { key = "e3", name = "★3 에코 — 주술사 / 거암", desc = "코스트 3 · 둘 중 무작위", price = 110, grant = () => { if (EchoSystem.I != null) EchoSystem.I.Add(UnityEngine.Random.Range(2, 4)); } },
            new Offer { key = "e1", name = "★1 에코 — 방랑자 / 질풍", desc = "코스트 1 · 둘 중 무작위", price = 45, grant = () => { if (EchoSystem.I != null) EchoSystem.I.Add(UnityEngine.Random.Range(0, 2)); } },
            new Offer { key = "tuner", name = "조율기", desc = "에코 부옵 개방 · 재조율", price = 180, limit = 2, itemId = ItemDB.Tuner },
            new Offer { key = "stone", name = "공명석", desc = "캐릭터·무기 EXP 600", price = 120, limit = 5, itemId = ItemDB.Stone1 },
            new Offer { key = "foodA", name = "노래풀 구이", desc = "5분 공격력 +12%", price = 40, itemId = ItemDB.FoodAtk },
            new Offer { key = "foodD", name = "강철껍질 조림", desc = "5분 받는 피해 −15%", price = 40, itemId = ItemDB.FoodDef },
            new Offer { key = "stam", name = "질주의 물약", desc = "90초 스태미나 소모 −50% · 회복 +50%", price = 60, limit = 3, itemId = ItemDB.StaminaPotion },
            new Offer { key = "c0", name = "회절 결정", desc = "리라에 돌파·스킬 소재", price = 60, limit = 3, itemId = ItemDB.Crystal0 },
            new Offer { key = "c1", name = "응결 결정", desc = "세레네 돌파·스킬 소재", price = 60, limit = 3, itemId = ItemDB.Crystal1 },
            new Offer { key = "c2", name = "용융 결정", desc = "에이리스 돌파·스킬 소재", price = 60, limit = 3, itemId = ItemDB.Crystal2 },
        };

        public static readonly List<Conversion> Conversions = new List<Conversion>
        {
            new Conversion { name = "흐린 잔재 10 → 짙은 잔재 1", fromId = ItemDB.Residue0, fromN = 10, toId = ItemDB.Residue1, toN = 1, cost = 50 },
            new Conversion { name = "짙은 잔재 5 → 검은 잔재 1", fromId = ItemDB.Residue1, fromN = 5, toId = ItemDB.Residue2, toN = 1, cost = 150 },
            new Conversion { name = "회절 결정 3 → 응결 결정 2", fromId = ItemDB.Crystal0, fromN = 3, toId = ItemDB.Crystal1, toN = 2, cost = 100 },
            new Conversion { name = "응결 결정 3 → 용융 결정 2", fromId = ItemDB.Crystal1, fromN = 3, toId = ItemDB.Crystal2, toN = 2, cost = 100 },
            new Conversion { name = "용융 결정 3 → 회절 결정 2", fromId = ItemDB.Crystal2, fromN = 3, toId = ItemDB.Crystal0, toN = 2, cost = 100 },
        };

        public static int Day = -1;
        public static event Action Changed;

        public static void Tick()
        {
            if (DayNightCycle.DayIndex == Day) return;
            Day = DayNightCycle.DayIndex;
            foreach (var o in Offers) o.bought = 0;
            if (Changed != null) Changed();
        }

        public static bool Available(Offer o, out string why)
        {
            int lv = ProgressSystem.I != null ? ProgressSystem.I.Level : 1;
            if (o.minLevel > 0 && lv < o.minLevel) { why = "파티 Lv " + o.minLevel + " 필요"; return false; }
            if (o.limit > 0 && o.bought >= o.limit) { why = "오늘 품절"; return false; }
            why = null; return true;
        }

        public static bool Buy(Offer o)
        {
            string why;
            if (!Available(o, out why)) { HUDController.Toast(why); return false; }
            if (ProgressSystem.I == null || !ProgressSystem.I.SpendShards(o.price))
            {
                HUDController.Toast("조각소리가 부족합니다 (" + o.price + " 필요)");
                AudioMan.I.Play2D(Sfx.Hurt(), 0.4f, 1.4f);
                return false;
            }
            if (o.itemId > 0) Inventory.Add(o.itemId, o.qty);
            else if (o.grant != null) o.grant();
            o.bought++;
            AudioMan.I.Play2D(Sfx.Absorb(), 0.7f, 1.1f);
            HUDController.Toast(o.name + " 구매 — 조각소리 −" + o.price);
            if (Changed != null) Changed();
            return true;
        }

        /// Item sale: full shop value here, 80% in the bag.
        public static bool SellItem(int id, int n, bool inShop)
        {
            var d = ItemDB.Get(id);
            if (d == null || d.sell <= 0 || n <= 0) return false;
            n = Mathf.Min(n, Inventory.Count(id));
            if (n <= 0) return false;
            int each = inShop ? d.sell : Mathf.Max(1, Mathf.RoundToInt(d.sell * 0.8f));
            if (!Inventory.Remove(id, n)) return false;
            if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(each * n);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.6f, 0.9f);
            HUDController.Toast(d.name + " ×" + n + " 판매 — 조각소리 +" + (each * n));
            if (Changed != null) Changed();
            return true;
        }

        public static int EchoPrice(int star, bool inShop)
        {
            int p = star >= 5 ? 80 : star >= 3 ? 45 : 15;
            return inShop ? p : Mathf.RoundToInt(p * 0.8f);
        }

        public static int WeaponPrice(int tier, bool inShop)
        {
            int p = tier >= 3 ? 900 : tier == 2 ? 260 : 50;
            return inShop ? p : Mathf.RoundToInt(p * 0.8f);
        }

        public static bool SellEcho(int uid, bool inShop)
        {
            var es = EchoSystem.I;
            var inst = es != null ? es.Get(uid) : null;
            if (inst == null) return false;
            int m, s;
            if (es.EquipLocation(uid, out m, out s)) { HUDController.Toast("장착 중인 에코는 판매할 수 없습니다"); return false; }
            if (inst.locked) { HUDController.Toast("잠긴 에코는 판매할 수 없습니다"); return false; }
            int price = EchoPrice(inst.Def.star, inShop);
            if (!es.Remove(uid)) return false;
            if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(price);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.6f, 0.9f);
            HUDController.Toast(inst.Def.name + " 판매 — 조각소리 +" + price);
            if (Changed != null) Changed();
            return true;
        }

        public static int SellEchoesOfStar(int star, bool inShop)
        {
            var es = EchoSystem.I;
            if (es == null) return 0;
            var sell = new List<int>();
            foreach (var e in es.Instances)
            {
                int m, s;
                if (e.locked || es.EquipLocation(e.uid, out m, out s)) continue;
                var def = e.Def;
                if (def == null) continue;
                bool match = star <= 1 ? def.star <= 1 : star <= 3 ? (def.star > 1 && def.star <= 3) : def.star >= 5;
                if (match) sell.Add(e.uid);
            }
            int n = 0, total = 0;
            foreach (var uid in sell) if (es.Remove(uid)) { n++; total += EchoPrice(star, inShop); }
            if (n > 0)
            {
                if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(total);
                AudioMan.I.Play2D(Sfx.Absorb(), 0.7f, 0.9f);
                HUDController.Toast("에코 " + n + "개 판매 — 조각소리 +" + total);
                if (Changed != null) Changed();
            }
            else HUDController.Toast("판매할 에코가 없습니다");
            return n;
        }

        /// Batch dispose (bag): sells the given unlocked, unequipped echoes with one toast.
        public static int SellEchoes(List<int> uids, bool inShop)
        {
            var es = EchoSystem.I;
            if (es == null || uids == null) return 0;
            int n = 0, total = 0;
            foreach (var uid in uids)
            {
                var inst = es.Get(uid);
                if (inst == null || inst.locked || inst.Def == null) continue;
                int m, s; if (es.EquipLocation(uid, out m, out s)) continue;
                int price = EchoPrice(inst.Def.star, inShop);
                if (es.Remove(uid)) { n++; total += price; }
            }
            if (n > 0)
            {
                if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(total);
                AudioMan.I.Play2D(Sfx.Absorb(), 0.7f, 0.9f);
                HUDController.Toast("에코 " + n + "개 처분 — 조각소리 +" + total);
                if (Changed != null) Changed();
            }
            return n;
        }

        public static bool SellWeapon(int id, bool inShop)
        {
            var ws = WeaponSystem.I;
            var def = WeaponDB.Get(id);
            if (ws == null || def == null) return false;
            if (ws.CountOf(id) - ws.EquippedCount(id) <= 0) { HUDController.Toast("여분 무기가 없습니다 (장착 중인 무기는 판매 불가)"); return false; }
            if (!ws.Remove(id)) return false;
            int price = WeaponPrice(def.tier, inShop);
            if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(price);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.6f, 0.9f);
            HUDController.Toast(def.name + " 판매 — 조각소리 +" + price);
            if (Changed != null) Changed();
            return true;
        }

        public static bool Convert(Conversion c)
        {
            if (!Inventory.Has(c.fromId, c.fromN)) { HUDController.Toast(ItemDB.Get(c.fromId).name + " " + c.fromN + "개 필요"); return false; }
            if (ProgressSystem.I == null || ProgressSystem.I.Shards < c.cost) { HUDController.Toast("조각소리가 부족합니다 (" + c.cost + " 필요)"); return false; }
            ProgressSystem.I.SpendShards(c.cost);
            Inventory.Remove(c.fromId, c.fromN);
            Inventory.Add(c.toId, c.toN);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.7f, 1.1f);
            if (Changed != null) Changed();
            return true;
        }

        public static void Export(out int day, out int[] bought)
        {
            day = Day;
            bought = new int[Offers.Count];
            for (int i = 0; i < Offers.Count; i++) bought[i] = Offers[i].bought;
        }

        public static void Import(int day, int[] bought)
        {
            Day = day;
            for (int i = 0; i < Offers.Count; i++) Offers[i].bought = bought != null && i < bought.Length ? bought[i] : 0;
            if (Changed != null) Changed();
        }
    }
}
