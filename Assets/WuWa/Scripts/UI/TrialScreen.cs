using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// 시련의 제단 screen (opened from the altar): pick a trial tier, or spend
    /// 시련 증표 at the altar exchange (design doc ch.4 tables).
    public class TrialScreen : UIScreen
    {
        public override string Id { get { return "Trial"; } }
        public override string Title { get { return "시련의 제단"; } }

        class Exchange { public string name, desc; public int cost; public System.Action grant; }
        static readonly List<Exchange> Exchanges = new List<Exchange>
        {
            new Exchange { name = "★5 에코 — 무관의 그림자 (메인 스탯 선택)", desc = "코스트 4 · 치명 확률 / 치명 피해 / 공격력 % 중 선택 · 부가 스탯 무작위", cost = 12, grant = () =>
                Modal.Choice("메인 스탯 선택", "★5 무관의 그림자의 메인 스탯을 고릅니다.", new[] { "치명 확률", "치명 피해", "공격력 %", "취소" }, k =>
                {
                    if (k < 0 || k > 2 || EchoSystem.I == null) return;
                    if (!Inventory.SpendTokens(12)) { HUDController.Toast("증표가 부족합니다 (12 필요)"); return; }
                    var inst = EchoSystem.I.Add(4);
                    if (inst != null) EchoStats.RollMain(inst, k == 0 ? EchoStatType.CritRate : k == 1 ? EchoStatType.CritDmg : EchoStatType.AtkPct, new System.Random());
                    AudioMan.I.Play2D(Sfx.Ult(), 0.6f, 1.2f);
                    HUDController.Toast("교환 — ★5 무관의 그림자  [" + (inst != null ? inst.main.Text : "") + "]  증표 −12");
                }, 3) },
            new Exchange { name = "조율기", desc = "에코 부옵 개방 · 재조율", cost = 2, grant = () => Take(2, () => Inventory.Add(ItemDB.Tuner, 1), "조율기") },
            new Exchange { name = "무관의 왕관 파편", desc = "돌파 III · 무기 돌파 · 스킬 Lv5 소재", cost = 5, grant = () => Take(5, () => Inventory.Add(ItemDB.Crown, 1), "왕관 파편") },
            new Exchange { name = "지역 결정 ×2 (속성 선택)", desc = "회절 / 응결 / 용융 결정 중 선택", cost = 1, grant = () =>
                Modal.Choice("결정 선택", "증표 1 → 결정 2", new[] { "회절 결정", "응결 결정", "용융 결정", "취소" }, k =>
                {
                    if (k < 0 || k > 2) return;
                    Take(1, () => Inventory.Add(k == 0 ? ItemDB.Crystal0 : k == 1 ? ItemDB.Crystal1 : ItemDB.Crystal2, 2), "결정 ×2");
                }, 3) },
            new Exchange { name = "공명 결정", desc = "캐릭터·무기 EXP 2000", cost = 4, grant = () => Take(4, () => Inventory.Add(ItemDB.Stone2, 1), "공명 결정") },
            new Exchange { name = "잔향검 · 명기 (T3 무기)", desc = "공격 +50 · 협주 획득 +25%", cost = 25, grant = () => Take(25, () => { if (WeaponSystem.I != null) WeaponSystem.I.Add(2); }, "잔향검 · 명기") },
        };

        static void Take(int cost, System.Action give, string what)
        {
            if (!Inventory.SpendTokens(cost)) { HUDController.Toast("증표가 부족합니다 (" + cost + " 필요)"); return; }
            give();
            AudioMan.I.Play2D(Sfx.Absorb(), 0.7f, 1.1f);
            HUDController.Toast("교환 — " + what + "  증표 −" + cost);
        }

        class Row { public GameObject go; public Text name, desc, price; public Button btn; public Image bg; }
        readonly List<Row> _rows = new List<Row>();
        Text _wallet, _info;
        RectTransform _content;
        bool _dirty;

        protected override void Build()
        {
            UIKit.Stretch(UIKit.Img("bg", Root, UIKit.Theme.Bg).rectTransform);
            var hdr = UIKit.Txt("title", Root, new Vector2(0f, 1f), new Vector2(60f, -30f), new Vector2(800f, 44f), "시련의 제단 — 시련 · 증표 교환", 30, new Color(1f, 0.93f, 0.75f, 1f), TextAnchor.MiddleLeft, true);
            hdr.rectTransform.pivot = new Vector2(0f, 1f);
            _wallet = UIKit.Txt("wallet", Root, new Vector2(1f, 1f), new Vector2(-60f, -30f), new Vector2(600f, 30f), "", 18, UIKit.Theme.Info, TextAnchor.MiddleRight, true);
            _wallet.rectTransform.pivot = new Vector2(1f, 1f);
            var band = UIKit.Img("band", Root, new Color(1f, 0.82f, 0.35f, 0.5f));
            var brt = band.rectTransform; brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f); brt.anchoredPosition = new Vector2(0f, -86f); brt.sizeDelta = new Vector2(0f, 2f);
            UIKit.Btn("close", Root, new Vector2(0f, 1f), new Vector2(60f, -140f), new Vector2(150f, 44f), "나가기 (" + Glyph.Key("UI/Cancel", "Esc") + ")", UIKit.Theme.Button, () => ScreenRouter.Back(), 15)
                .GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            var note = UIKit.Txt("note", Root, new Vector2(0f, 1f), new Vector2(60f, -200f), new Vector2(160f, 400f), "시련 증표는 시련 완주(Tier I/II/III = 3/5/8)와 지역 정화율 100%로 얻습니다.\n\n제단을 벗어나면 시련이 무효가 됩니다.", 13, UIKit.Theme.TextLo, TextAnchor.UpperLeft);
            note.rectTransform.pivot = new Vector2(0f, 1f);

            var view = UIKit.Img("view", Root, new Color(1f, 1f, 1f, 0.03f), null, true);
            var vrt = view.rectTransform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(0f, 1f); vrt.pivot = new Vector2(0f, 1f);
            vrt.anchoredPosition = new Vector2(240f, -140f); vrt.sizeDelta = new Vector2(1400f, 820f);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40f;
            var contentGo = new GameObject("content");
            contentGo.transform.SetParent(view.transform, false);
            _content = contentGo.AddComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero; _content.sizeDelta = new Vector2(0f, 820f);
            scroll.content = _content; scroll.viewport = vrt;

            _info = UIKit.Txt("info", Root, new Vector2(0f, 1f), new Vector2(240f, -970f), new Vector2(1400f, 60f), "", 14, UIKit.Theme.TextLo, TextAnchor.UpperLeft);
            _info.rectTransform.pivot = new Vector2(0f, 1f);
        }

        public override void OnOpen(object args)
        {
            Inventory.Changed += MarkDirty;
            if (ProgressSystem.I != null) ProgressSystem.I.OnChanged += MarkDirty;
            Refresh();
        }

        public override void OnClose()
        {
            Inventory.Changed -= MarkDirty;
            if (ProgressSystem.I != null) ProgressSystem.I.OnChanged -= MarkDirty;
        }

        void OnDestroy()
        {
            Inventory.Changed -= MarkDirty;
            if (ProgressSystem.I != null) ProgressSystem.I.OnChanged -= MarkDirty;
        }

        public override void OnTick() { if (_dirty) { _dirty = false; Refresh(); } }
        void MarkDirty() { _dirty = true; }
        public override Selectable DefaultFocus { get { foreach (var r in _rows) if (r.go.activeSelf && r.btn.gameObject.activeSelf && r.btn.interactable) return r.btn; return null; } }

        Row GetRow(int i)
        {
            while (_rows.Count <= i)
            {
                var r = new Row();
                r.go = new GameObject("row" + _rows.Count);
                r.go.transform.SetParent(_content, false);
                var rt = r.go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(-16f, 64f);
                r.bg = r.go.AddComponent<Image>(); r.bg.sprite = UIKit.White; r.bg.color = new Color(1f, 1f, 1f, 0.03f); r.bg.raycastTarget = false;
                r.name = UIKit.Txt("name", r.go.transform, new Vector2(0f, 0.5f), new Vector2(20f, 12f), new Vector2(760f, 26f), "", 17, UIKit.Theme.TextHi, TextAnchor.MiddleLeft, true);
                r.name.rectTransform.pivot = new Vector2(0f, 0.5f);
                r.desc = UIKit.Txt("desc", r.go.transform, new Vector2(0f, 0.5f), new Vector2(20f, -12f), new Vector2(820f, 22f), "", 13, UIKit.Theme.TextLo, TextAnchor.MiddleLeft);
                r.desc.rectTransform.pivot = new Vector2(0f, 0.5f);
                r.price = UIKit.Txt("price", r.go.transform, new Vector2(1f, 0.5f), new Vector2(-300f, 0f), new Vector2(260f, 30f), "", 15, UIKit.Theme.Info, TextAnchor.MiddleRight);
                r.price.rectTransform.pivot = new Vector2(1f, 0.5f);
                r.btn = UIKit.Btn("act", r.go.transform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(260f, 46f), "-", UIKit.Theme.Confirm, null, 15);
                r.btn.GetComponent<RectTransform>().pivot = new Vector2(1f, 0.5f);
                _rows.Add(r);
            }
            return _rows[i];
        }

        void Header(int i, string text)
        {
            var r = GetRow(i);
            r.name.text = text; r.desc.text = ""; r.price.text = "";
            r.name.color = UIKit.Theme.Accent;
            r.btn.gameObject.SetActive(false);
        }

        void Refresh()
        {
            int shards = ProgressSystem.I != null ? ProgressSystem.I.Shards : 0;
            int tokens = Inventory.TrialTokens;
            _wallet.text = "시련 증표  " + tokens + "   ·   조각소리  " + UIKit.Num(shards) + "   ·   조율기 " + Inventory.Count(ItemDB.Tuner);
            int n = 0;
            Header(n++, "─ 시련 ─   완주 " + ContentStats.ArenaClears + "회 · 최고 Tier " + ArenaTrial.TierName(ContentStats.ArenaTierBest) + " · 최고 " + ContentStats.ArenaBestWave + "웨이브");
            for (int tier = 1; tier <= 3; tier++)
            {
                var r = GetRow(n++);
                int t = tier;
                r.name.color = UIKit.Theme.TextHi;
                r.name.text = "시련 Tier " + ArenaTrial.TierName(tier) + "   <color=#ffffff88>" + ArenaTrial.TierRequirement(tier) + "</color>";
                r.name.supportRichText = true;
                r.desc.text = ArenaTrial.TierRewards(tier);
                r.price.text = "증표 +" + ArenaTrial.TierTokens(tier);
                string why = "-"; bool ok = ArenaTrial.I != null && ArenaTrial.I.CanStartTier(tier, out why);
                if (ArenaTrial.I == null) why = "제단 없음";
                r.btn.gameObject.SetActive(true);
                SetBtn(r, ok ? "시작" : why, ok, () => { ScreenRouter.CloseAll(); if (ArenaTrial.I != null) ArenaTrial.I.Begin(t); });
            }
            Header(n++, "─ 증표 교환 ─   보유 " + tokens);
            foreach (var ex in Exchanges)
            {
                var r = GetRow(n++);
                var e = ex;
                r.name.color = UIKit.Theme.TextHi;
                r.name.text = e.name; r.name.supportRichText = false;
                r.desc.text = e.desc;
                r.price.text = "증표 " + e.cost;
                r.btn.gameObject.SetActive(true);
                SetBtn(r, tokens >= e.cost ? "교환" : "증표 부족", tokens >= e.cost, () => e.grant());
            }
            for (int i = 0; i < n; i++) { _rows[i].go.SetActive(true); _rows[i].go.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -i * 68f); _rows[i].bg.color = new Color(1f, 1f, 1f, i % 2 == 0 ? 0.03f : 0.05f); }
            for (int i = n; i < _rows.Count; i++) _rows[i].go.SetActive(false);
            _content.sizeDelta = new Vector2(0f, Mathf.Max(820f, n * 68f + 16f));
            _info.text = "Tier II는 파티 Lv 25 + Tier I 완주, Tier III는 파티 Lv 35 + Tier II 완주가 필요합니다. 상위 티어는 적이 강하고(×1.35 / ×1.7) 보상이 큽니다(×1.3 / ×1.6). Tier III 첫 완주 시 명기 확정.";
            FocusNavigator.MarkDirty();
        }

        void SetBtn(Row r, string label, bool enabled, System.Action act)
        {
            r.btn.onClick.RemoveAllListeners();
            r.btn.onClick.AddListener(() => act());
            r.btn.interactable = enabled;
            r.btn.GetComponentInChildren<Text>().text = label;
            r.btn.GetComponent<Image>().color = enabled ? UIKit.Theme.Confirm : new Color(0.16f, 0.17f, 0.18f, 1f);
        }
    }
}
