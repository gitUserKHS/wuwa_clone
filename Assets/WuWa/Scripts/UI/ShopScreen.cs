using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Merchant screen (opened after the merchant's dialogue): 구매 · 판매 · 변환.
    public class ShopScreen : UIScreen
    {
        public override string Id { get { return "Shop"; } }
        public override string Title { get { return "상점"; } }

        static readonly string[] Tabs = { "구매", "판매", "변환" };
        class Row { public GameObject go; public Text name, desc, price; public Button btn; public Image bg; }
        readonly List<Button> _tabBtns = new List<Button>();
        readonly List<Row> _rows = new List<Row>();
        Text _wallet, _info, _hdr;
        RectTransform _content;
        int _tab;
        bool _dirty;

        protected override void Build()
        {
            UIKit.Stretch(UIKit.Img("bg", Root, UIKit.Theme.Bg).rectTransform);
            _hdr = UIKit.Txt("title", Root, new Vector2(0f, 1f), new Vector2(60f, -30f), new Vector2(700f, 44f), "상점 — 메아리 마을", 30, new Color(1f, 0.93f, 0.75f, 1f), TextAnchor.MiddleLeft, true);
            _hdr.rectTransform.pivot = new Vector2(0f, 1f);
            _wallet = UIKit.Txt("wallet", Root, new Vector2(1f, 1f), new Vector2(-60f, -30f), new Vector2(600f, 30f), "", 18, UIKit.Theme.Info, TextAnchor.MiddleRight, true);
            _wallet.rectTransform.pivot = new Vector2(1f, 1f);
            var band = UIKit.Img("band", Root, new Color(1f, 0.82f, 0.35f, 0.5f));
            var brt = band.rectTransform; brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f); brt.anchoredPosition = new Vector2(0f, -86f); brt.sizeDelta = new Vector2(0f, 2f);

            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                var b = UIKit.Btn("stab" + i, Root, new Vector2(0f, 1f), new Vector2(60f, -140f - i * 58f), new Vector2(150f, 50f), Tabs[i], UIKit.Theme.Button, () => { _tab = idx; Refresh(); }, 17);
                b.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
                _tabBtns.Add(b);
            }
            UIKit.Btn("close", Root, new Vector2(0f, 1f), new Vector2(60f, -140f - Tabs.Length * 58f - 20f), new Vector2(150f, 44f), "나가기 (" + Glyph.Key("UI/Cancel", "Esc") + ")", UIKit.Theme.Button, () => ScreenRouter.Back(), 15)
                .GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);

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
            ShopStock.Tick();
            ShopStock.Changed += MarkDirty; Inventory.Changed += MarkDirty;
            if (ProgressSystem.I != null) ProgressSystem.I.OnChanged += MarkDirty;
            Refresh();
        }

        public override void OnClose()
        {
            ShopStock.Changed -= MarkDirty; Inventory.Changed -= MarkDirty;
            if (ProgressSystem.I != null) ProgressSystem.I.OnChanged -= MarkDirty;
        }

        public override void OnTick() { if (_dirty) { _dirty = false; Refresh(); } }
        public override void OnTab(int dir) { _tab = (_tab + dir + Tabs.Length) % Tabs.Length; Refresh(); }
        void MarkDirty() { _dirty = true; }
        public override Selectable DefaultFocus { get { return _rows.Count > 0 && _rows[0].go.activeSelf ? _rows[0].btn : _tabBtns[0]; } }

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
                r.name = UIKit.Txt("name", r.go.transform, new Vector2(0f, 0.5f), new Vector2(20f, 12f), new Vector2(700f, 26f), "", 17, UIKit.Theme.TextHi, TextAnchor.MiddleLeft, true);
                r.name.rectTransform.pivot = new Vector2(0f, 0.5f);
                r.desc = UIKit.Txt("desc", r.go.transform, new Vector2(0f, 0.5f), new Vector2(20f, -12f), new Vector2(760f, 22f), "", 13, UIKit.Theme.TextLo, TextAnchor.MiddleLeft);
                r.desc.rectTransform.pivot = new Vector2(0f, 0.5f);
                r.price = UIKit.Txt("price", r.go.transform, new Vector2(1f, 0.5f), new Vector2(-300f, 0f), new Vector2(240f, 30f), "", 15, UIKit.Theme.Info, TextAnchor.MiddleRight);
                r.price.rectTransform.pivot = new Vector2(1f, 0.5f);
                r.btn = UIKit.Btn("act", r.go.transform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(260f, 46f), "-", UIKit.Theme.Confirm, null, 15);
                r.btn.GetComponent<RectTransform>().pivot = new Vector2(1f, 0.5f);
                _rows.Add(r);
            }
            return _rows[i];
        }

        void Refresh()
        {
            for (int i = 0; i < _tabBtns.Count; i++) _tabBtns[i].GetComponent<Image>().color = i == _tab ? UIKit.Theme.Selected : UIKit.Theme.Button;
            int shards = ProgressSystem.I != null ? ProgressSystem.I.Shards : 0;
            _wallet.text = "조각소리  " + UIKit.Num(shards) + "   ·   증표 " + Inventory.TrialTokens + "   ·   조율기 " + Inventory.Count(ItemDB.Tuner);
            int n = 0;
            if (_tab == 0)
            {
                foreach (var o in ShopStock.Offers)
                {
                    var r = GetRow(n++);
                    var offer = o;
                    string why; bool ok = ShopStock.Available(o, out why);
                    r.name.text = o.name + (o.limit > 0 ? "   <color=#ffffff88>오늘 " + (o.limit - o.bought) + "/" + o.limit + "</color>" : "");
                    r.name.supportRichText = true;
                    r.desc.text = o.desc;
                    r.price.text = o.price + " 조각소리";
                    SetBtn(r, ok ? "구매" : why, ok && shards >= o.price, () => ShopStock.Buy(offer));
                }
                _info.text = "재고 한정 품목은 게임 내 하루(" + (DayNightCycle.I != null ? DayNightCycle.I.dayLengthMinutes.ToString("0") : "44") + "분)마다 채워집니다. 조각소리는 그림자 처치 · 상자 · 균열 · 시련 · 퀘스트로 얻습니다.";
            }
            else if (_tab == 1)
            {
                foreach (var kv in Inventory.Stacks())
                {
                    var d = ItemDB.Get(kv.Key);
                    if (d.sell <= 0) continue;
                    var r = GetRow(n++);
                    int id = d.id, have = kv.Value;
                    r.name.text = d.name + "   <color=#ffffff88>보유 " + have + "</color>";
                    r.name.supportRichText = true;
                    r.desc.text = d.desc;
                    r.price.text = "개당 " + d.sell;
                    SetBtn(r, have > 1 ? "판매 (1 / 전부)" : "판매", true, () =>
                    {
                        if (have <= 1) { ShopStock.SellItem(id, 1, true); return; }
                        Modal.Choice("판매 — " + d.name, "상점 매입가 " + d.sell + " / 개", new[] { "1개", "전부 " + have + "개 (+" + d.sell * have + ")", "취소" }, pick => { if (pick == 0) ShopStock.SellItem(id, 1, true); else if (pick == 1) ShopStock.SellItem(id, have, true); }, 2);
                    });
                }
                int[] stars = { 1, 3, 5 };
                foreach (var st in stars)
                {
                    int cnt = 0;
                    if (EchoSystem.I != null)
                        foreach (var e in EchoSystem.I.Instances)
                        {
                            int m, s; if (EchoSystem.I.EquipLocation(e.uid, out m, out s)) continue;
                            var def = e.Def; if (def == null) continue;
                            bool match = st <= 1 ? def.star <= 1 : st <= 3 ? (def.star > 1 && def.star <= 3) : def.star >= 5;
                            if (match) cnt++;
                        }
                    var r = GetRow(n++);
                    int star = st;
                    r.name.text = "미장착 ★" + st + " 에코 전부   <color=#ffffff88>" + cnt + "개</color>";
                    r.name.supportRichText = true;
                    r.desc.text = "장착 중인 에코는 제외됩니다";
                    r.price.text = "개당 " + ShopStock.EchoPrice(st, true);
                    SetBtn(r, "전부 판매", cnt > 0, () => Modal.Confirm("에코 판매", "미장착 ★" + star + " 에코 " + cnt + "개를 판매합니다.", "판매", "취소", true, () => ShopStock.SellEchoesOfStar(star, true)));
                }
                if (WeaponSystem.I != null)
                    foreach (var wd in WeaponDB.All)
                    {
                        int extra = WeaponSystem.I.CountOf(wd.id) - WeaponSystem.I.EquippedCount(wd.id);
                        if (extra <= 0) continue;
                        var r = GetRow(n++);
                        int wid = wd.id;
                        r.name.text = wd.name + " (여분)   <color=#ffffff88>" + extra + "개</color>";
                        r.name.supportRichText = true;
                        r.desc.text = "T" + wd.tier + " · 공격 +" + wd.atk;
                        r.price.text = "개당 " + ShopStock.WeaponPrice(wd.tier, true);
                        SetBtn(r, "1개 판매", true, () => ShopStock.SellWeapon(wid, true));
                    }
                _info.text = n == 0 ? "판매할 것이 없습니다." : "가방에서도 판매할 수 있지만 상점 매입가의 80%만 받습니다.";
            }
            else
            {
                foreach (var c in ShopStock.Conversions)
                {
                    var r = GetRow(n++);
                    var conv = c;
                    r.name.text = c.name;
                    r.desc.text = ItemDB.Get(c.fromId).name + " 보유 " + Inventory.Count(c.fromId) + " → " + ItemDB.Get(c.toId).name + " 보유 " + Inventory.Count(c.toId);
                    r.price.text = "수수료 " + c.cost;
                    SetBtn(r, "변환", Inventory.Has(c.fromId, c.fromN) && shards >= c.cost, () => ShopStock.Convert(conv));
                }
                _info.text = "소재 변환은 부족한 상위 소재를 채우는 용도입니다.";
            }
            for (int i = 0; i < n; i++) { _rows[i].go.SetActive(true); _rows[i].go.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -i * 68f); _rows[i].bg.color = new Color(1f, 1f, 1f, i % 2 == 0 ? 0.03f : 0.05f); }
            for (int i = n; i < _rows.Count; i++) _rows[i].go.SetActive(false);
            _content.sizeDelta = new Vector2(0f, Mathf.Max(820f, n * 68f + 16f));
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
