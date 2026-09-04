using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Item stacks, the quick slot, flask charges and the secondary wallet.
    /// Static so every system can grant items; saved by SaveSystem.
    public static class Inventory
    {
        static readonly Dictionary<int, int> _stacks = new Dictionary<int, int>();
        public static event Action Changed;
        public static int QuickSlot = ItemDB.FoodAtk;
        public const int FlaskMax = 3;
        public static int FlaskCharges = FlaskMax;
        public static int TrialTokens;
        public static int TunerPity;

        public static int Count(int id) { int c; return _stacks.TryGetValue(id, out c) ? c : 0; }
        public static bool Has(int id, int n) { return Count(id) >= n; }

        /// Adds a stack (clamped to the item's cap) and posts a pickup card. Returns the amount actually added.
        public static int Add(int id, int n, bool notify = true)
        {
            var def = ItemDB.Get(id);
            if (def == null || n <= 0) return 0;
            int cur = Count(id);
            int add = Mathf.Min(n, def.stackCap - cur);
            if (add <= 0) { if (notify) HUDController.Toast(def.name + " — 더 이상 가질 수 없습니다 (" + def.stackCap + ")"); return 0; }
            _stacks[id] = cur + add;
            GameFlags.Set("item_" + id);
            if (notify) NotificationFeed.Item(def.name, add, def.Tint);
            Notify();
            return add;
        }

        public static bool Remove(int id, int n)
        {
            if (n <= 0) return true;
            int cur = Count(id);
            if (cur < n) return false;
            if (cur - n == 0) _stacks.Remove(id); else _stacks[id] = cur - n;
            Notify();
            return true;
        }

        public static void AddTokens(int n)
        {
            if (n == 0) return;
            TrialTokens = Mathf.Clamp(TrialTokens + n, 0, 999);
            if (n > 0) NotificationFeed.Item("시련 증표", n, UIKit.Theme.Rarity(4), null, true);
            Notify();
        }

        public static bool SpendTokens(int n)
        {
            if (TrialTokens < n) return false;
            TrialTokens -= n;
            Notify();
            return true;
        }

        /// Owned stacks sorted by rarity (desc) then id.
        public static List<KeyValuePair<int, int>> Stacks(ItemCategory? cat = null)
        {
            var list = new List<KeyValuePair<int, int>>();
            foreach (var kv in _stacks)
            {
                var d = ItemDB.Get(kv.Key);
                if (d == null || kv.Value <= 0) continue;
                if (cat.HasValue && d.cat != cat.Value) continue;
                list.Add(kv);
            }
            list.Sort((a, b) =>
            {
                var da = ItemDB.Get(a.Key); var db = ItemDB.Get(b.Key);
                int s = db.star.CompareTo(da.star);
                return s != 0 ? s : a.Key.CompareTo(b.Key);
            });
            return list;
        }

        // ---------------------------------------------------------------- use
        public static bool Use(int id)
        {
            var def = ItemDB.Get(id);
            if (def == null || def.cat != ItemCategory.Consumable) return false;
            if (def.effect == ItemEffect.Heal) { HUDController.Toast("공명의 물약은 " + Glyph.Key("Player/Flask", "X") + " 키로 시전합니다"); return false; }
            if (Count(id) <= 0) { HUDController.Toast(def.name + "이(가) 없습니다"); return false; }
            switch (def.effect)
            {
                case ItemEffect.AtkBuff: BuffSystem.Apply(BuffKind.Atk, def.effectValue, def.effectDur, "공격력 +" + Mathf.RoundToInt(def.effectValue * 100f) + "%"); break;
                case ItemEffect.DefBuff: BuffSystem.Apply(BuffKind.Def, def.effectValue, def.effectDur, "받는 피해 −" + Mathf.RoundToInt(def.effectValue * 100f) + "%"); break;
                case ItemEffect.StaminaBuff: BuffSystem.Apply(BuffKind.Stamina, def.effectValue, def.effectDur, "스태미나 소모 −" + Mathf.RoundToInt(def.effectValue * 100f) + "%"); break;
                default: return false;
            }
            Remove(id, 1);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.6f, 1.2f);
            HUDController.Toast(def.name + " 사용 — " + BuffSystem.LastApplied);
            return true;
        }

        public static bool UseQuick()
        {
            if (QuickSlot <= 0) { HUDController.Toast("퀵슬롯이 비어 있습니다 — 가방 > 소모품에서 지정"); return false; }
            return Use(QuickSlot);
        }

        public static void SetQuick(int id)
        {
            QuickSlot = id;
            var d = ItemDB.Get(id);
            HUDController.Toast("퀵슬롯 " + Glyph.Key("Player/QuickItem", "Z") + " — " + (d != null ? d.name : "비움"));
            Notify();
        }

        public static void RefillFlask(string reason)
        {
            if (FlaskCharges >= FlaskMax) return;
            FlaskCharges = FlaskMax;
            HUDController.Toast("공명의 물약 충전 " + FlaskCharges + "/" + FlaskMax + " — " + reason);
            Notify();
        }

        public static bool ConsumeFlask()
        {
            if (FlaskCharges <= 0) return false;
            FlaskCharges--;
            Notify();
            return true;
        }

        // ---------------------------------------------------------------- save
        public static void Export(out int[] ids, out int[] counts)
        {
            ids = new int[_stacks.Count]; counts = new int[_stacks.Count];
            int i = 0;
            foreach (var kv in _stacks) { ids[i] = kv.Key; counts[i] = kv.Value; i++; }
        }

        public static void Import(int[] ids, int[] counts, int quick, int flask, int tokens, int pity)
        {
            _stacks.Clear();
            if (ids != null && counts != null)
                for (int i = 0; i < ids.Length && i < counts.Length; i++)
                    if (ItemDB.Get(ids[i]) != null && counts[i] > 0) _stacks[ids[i]] = counts[i];
            QuickSlot = quick > 0 ? quick : ItemDB.FoodAtk;
            FlaskCharges = flask < 0 ? FlaskMax : Mathf.Clamp(flask, 0, FlaskMax);
            TrialTokens = Mathf.Max(0, tokens);
            TunerPity = Mathf.Max(0, pity);
            Notify();
        }

        public static void Reset()
        {
            _stacks.Clear();
            QuickSlot = ItemDB.FoodAtk; FlaskCharges = FlaskMax; TrialTokens = 0; TunerPity = 0;
            Notify();
        }

        static void Notify() { if (Changed != null) Changed(); }
    }
}
