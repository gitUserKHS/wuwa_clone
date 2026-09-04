using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Hub tab "가방": 에코 · 무기 · 재료 · 소모품 grid with a detail panel
    /// (use / quick-slot / sell). Echo and weapon equipping stays in the character
    /// screen. Echoes can be locked (no sale, no batch dispose, no retune) and
    /// disposed in bulk with quick-pick chips (design doc 7.7).
    public class InventoryScreen : UIScreen
    {
        public override string Id { get { return "Bag"; } }
        public override string Title { get { return "가방"; } }
        public override bool IsHubTab { get { return true; } }

        enum Kind { Echo, Weapon, Item }
        struct Entry { public Kind kind; public int id; public string name; public int star; public Color tint; public int count; public string badge; public string icon; public float level; public bool locked, worn; }
        class Cell { public Button btn; public Image bg, frame, icon; public Text count, badge, star; public Entry entry; }

        static readonly string[] Tabs = { "에코", "무기", "재료", "소모품" };
        static readonly string[] TabHints =
        {
            "잔향석 — 그림자가 남긴 소리. 장착·강화는 캐릭터 화면(C)에서. 잠근 에코는 처분·재조율에서 제외됩니다.",
            "무기 — 장착은 캐릭터 화면(C) > 무기 탭에서. 여분은 판매할 수 있습니다.",
            "재료 — 돌파·스킬·무기 강화 소재. 판매가는 상점가의 80%.",
            "소모품 — 사용하거나 퀵슬롯(" + "Z" + ")에 지정합니다. 물약은 X 키로 시전.",
        };

        ScreenRouter.HubHeader _header;
        readonly List<Button> _tabBtns = new List<Button>();
        readonly List<Cell> _cells = new List<Cell>();
        readonly List<Entry> _entries = new List<Entry>();
        RectTransform _content;
        ScrollRect _scroll;
        Text _hint, _countText, _title, _sub, _body, _foot, _empty;
        Button _btnA, _btnB, _btnC, _batchBtn;
        int _tab;
        bool _hasSel; Entry _sel;
        bool _dirty;
        bool _batch;
        readonly HashSet<int> _picked = new HashSet<int>();

        protected override void Build()
        {
            _header = ScreenRouter.BuildHubHeader(Root, "가방", Id);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                var b = UIKit.Btn("btab" + i, Root, new Vector2(0f, 1f), new Vector2(60f, -140f - i * 58f), new Vector2(150f, 50f), Tabs[i], UIKit.Theme.Button, () => SelectTab(idx), 17);
                b.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
                _tabBtns.Add(b);
            }
            _hint = UIKit.Txt("hint", Root, new Vector2(0f, 1f), new Vector2(240f, -112f), new Vector2(800f, 24f), "", 14, UIKit.Theme.TextLo, TextAnchor.MiddleLeft);
            _hint.rectTransform.pivot = new Vector2(0f, 1f);
            _batchBtn = UIKit.Btn("batch", Root, new Vector2(0f, 1f), new Vector2(1050f, -114f), new Vector2(150f, 28f), "일괄 선택", UIKit.Theme.Button, ToggleBatch, 13);
            _batchBtn.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            _countText = UIKit.Txt("count", Root, new Vector2(0f, 1f), new Vector2(1320f, -112f), new Vector2(0f, 24f), "", 14, UIKit.Theme.TextLo, TextAnchor.MiddleRight);
            _countText.rectTransform.pivot = new Vector2(1f, 1f);
            _countText.rectTransform.sizeDelta = new Vector2(110f, 24f);

            // grid (scroll view)
            var view = UIKit.Img("view", Root, new Color(1f, 1f, 1f, 0.03f), null, true);
            var vrt = view.rectTransform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(0f, 1f); vrt.pivot = new Vector2(0f, 1f);
            vrt.anchoredPosition = new Vector2(240f, -140f); vrt.sizeDelta = new Vector2(1080f, 900f);
            view.gameObject.AddComponent<RectMask2D>();
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 40f;
            var contentGo = new GameObject("content");
            contentGo.transform.SetParent(view.transform, false);
            _content = contentGo.AddComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero; _content.sizeDelta = new Vector2(0f, 900f);
            _scroll.content = _content; _scroll.viewport = vrt;
            _empty = UIKit.Txt("empty", view.transform, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(1000f, 60f), "", 16, UIKit.Theme.TextLo, TextAnchor.UpperLeft);
            _empty.rectTransform.pivot = new Vector2(0f, 1f);

            // detail
            var panel = UIKit.Panel("detail", Root, new Color(1f, 1f, 1f, 0.05f), new Vector2(0f, 1f), new Vector2(1350f, -140f), new Vector2(510f, 900f));
            var pr = panel.transform;
            _title = UIKit.Txt("title", pr, new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(460f, 34f), "", 22, UIKit.Theme.TextHi, TextAnchor.MiddleLeft, true);
            _title.rectTransform.pivot = new Vector2(0f, 1f);
            _sub = UIKit.Txt("sub", pr, new Vector2(0f, 1f), new Vector2(24f, -58f), new Vector2(460f, 26f), "", 15, UIKit.Theme.Accent, TextAnchor.MiddleLeft);
            _sub.rectTransform.pivot = new Vector2(0f, 1f);
            _body = UIKit.Txt("body", pr, new Vector2(0f, 1f), new Vector2(24f, -96f), new Vector2(460f, 560f), "", 15, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
            _body.rectTransform.pivot = new Vector2(0f, 1f);
            _foot = UIKit.Txt("foot", pr, new Vector2(0f, 1f), new Vector2(24f, -700f), new Vector2(460f, 60f), "", 14, UIKit.Theme.TextLo, TextAnchor.UpperLeft);
            _foot.rectTransform.pivot = new Vector2(0f, 1f);
            _btnA = UIKit.Btn("actA", pr, new Vector2(0f, 1f), new Vector2(24f, -770f), new Vector2(460f, 44f), "-", UIKit.Theme.Confirm, () => Action(0), 16);
            _btnA.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            _btnB = UIKit.Btn("actB", pr, new Vector2(0f, 1f), new Vector2(24f, -822f), new Vector2(224f, 44f), "-", UIKit.Theme.Button, () => Action(1), 16);
            _btnB.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            _btnC = UIKit.Btn("actC", pr, new Vector2(0f, 1f), new Vector2(260f, -822f), new Vector2(224f, 44f), "-", new Color(0.3f, 0.22f, 0.16f, 1f), () => Action(2), 16);
            _btnC.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        }

        public override void OnOpen(object args)
        {
            ScreenRouter.RefreshHubHeader(_header);
            Inventory.Changed += MarkDirty;
            if (EchoSystem.I != null) EchoSystem.I.OnChanged += MarkDirty;
            if (WeaponSystem.I != null) WeaponSystem.I.OnChanged += MarkDirty;
            Refresh();
        }

        public override void OnClose()
        {
            Inventory.Changed -= MarkDirty;
            if (EchoSystem.I != null) EchoSystem.I.OnChanged -= MarkDirty;
            if (WeaponSystem.I != null) WeaponSystem.I.OnChanged -= MarkDirty;
        }

        void OnDestroy()
        {
            Inventory.Changed -= MarkDirty;
            if (EchoSystem.I != null) EchoSystem.I.OnChanged -= MarkDirty;
            if (WeaponSystem.I != null) WeaponSystem.I.OnChanged -= MarkDirty;
        }

        public override void OnTick()
        {
            if (_dirty) { _dirty = false; Refresh(); }
            // X / right click: lock toggle on the selected echo (context action, design doc 7.3)
            if (InputService.UIContextPressed && _tab == 0 && _hasSel && _sel.kind == Kind.Echo && !_batch && EchoSystem.I != null)
            {
                EchoSystem.I.ToggleLock(_sel.id);
                _dirty = true;
            }
        }
        void MarkDirty() { _dirty = true; }

        public override Selectable DefaultFocus { get { return _cells.Count > 0 && _cells[0].btn.gameObject.activeSelf ? _cells[0].btn : (_tabBtns.Count > 0 ? _tabBtns[0] : null); } }

        void SelectTab(int i)
        {
            _tab = Mathf.Clamp(i, 0, Tabs.Length - 1);
            _hasSel = false;
            if (_tab != 0) { _batch = false; _picked.Clear(); }
            Refresh();
        }

        public bool BatchMode { get { return _batch; } }
        public int PickedCount { get { return _picked.Count; } }

        void ToggleBatch()
        {
            _batch = !_batch;
            _picked.Clear();
            Refresh();
        }

        // ---------------------------------------------------------------- entries
        void Collect()
        {
            _entries.Clear();
            switch (_tab)
            {
                case 0:
                    if (EchoSystem.I != null)
                        foreach (var inst in EchoSystem.I.Instances)
                        {
                            var d = inst.Def; if (d == null) continue;
                            int m, s; bool worn = EchoSystem.I.EquipLocation(inst.uid, out m, out s);
                            _entries.Add(new Entry { kind = Kind.Echo, id = inst.uid, name = d.name, star = d.star, tint = d.Tint, count = 1, level = inst.level, locked = inst.locked, worn = worn,
                                badge = worn ? "E" : inst.locked ? "잠금" : (inst.level > 0 ? "+" + inst.level : ""), icon = "echo" });
                        }
                    break;
                case 1:
                    if (WeaponSystem.I != null)
                        foreach (var d in WeaponDB.All)
                        {
                            int c = WeaponSystem.I.CountOf(d.id); if (c <= 0) continue;
                            int eq = WeaponSystem.I.EquippedCount(d.id);
                            _entries.Add(new Entry { kind = Kind.Weapon, id = d.id, name = d.name, star = d.tier + 2, tint = d.Tint, count = c, badge = eq > 0 ? "E" + (eq > 1 ? "×" + eq : "") : "", icon = "weapon" });
                        }
                    break;
                case 2:
                    foreach (var kv in Inventory.Stacks())
                    {
                        var d = ItemDB.Get(kv.Key);
                        if (d.cat != ItemCategory.Material && d.cat != ItemCategory.Stone) continue;
                        _entries.Add(new Entry { kind = Kind.Item, id = d.id, name = d.name, star = d.star, tint = d.Tint, count = kv.Value, icon = d.icon });
                    }
                    break;
                default:
                    {
                        var flask = ItemDB.Get(ItemDB.Flask);
                        _entries.Add(new Entry { kind = Kind.Item, id = flask.id, name = flask.name, star = flask.star, tint = flask.Tint, count = Inventory.FlaskCharges, icon = flask.icon, badge = "X" });
                        foreach (var kv in Inventory.Stacks(ItemCategory.Consumable))
                        {
                            var d = ItemDB.Get(kv.Key);
                            _entries.Add(new Entry { kind = Kind.Item, id = d.id, name = d.name, star = d.star, tint = d.Tint, count = kv.Value, icon = d.icon, badge = Inventory.QuickSlot == d.id ? "Z" : "" });
                        }
                        break;
                    }
            }
            _entries.Sort((a, b) => { int s = b.star.CompareTo(a.star); return s != 0 ? s : a.id.CompareTo(b.id); });
        }

        bool Disposable(Entry e) { return e.kind == Kind.Echo && !e.worn && !e.locked; }

        void Refresh()
        {
            for (int i = 0; i < _tabBtns.Count; i++) _tabBtns[i].GetComponent<Image>().color = i == _tab ? UIKit.Theme.Selected : UIKit.Theme.Button;
            _hint.text = TabHints[_tab];
            _batchBtn.gameObject.SetActive(_tab == 0);
            _batchBtn.GetComponentInChildren<Text>().text = _batch ? "선택 모드 종료" : "일괄 선택";
            _batchBtn.GetComponent<Image>().color = _batch ? UIKit.Theme.Selected : UIKit.Theme.Button;
            Collect();
            // drop picks that no longer exist / are no longer disposable
            if (_picked.Count > 0)
            {
                var keep = new HashSet<int>();
                foreach (var e in _entries) if (Disposable(e) && _picked.Contains(e.id)) keep.Add(e.id);
                _picked.Clear(); foreach (var k in keep) _picked.Add(k);
            }
            const int cols = 10; const float cellW = 104f, cellH = 116f;
            for (int i = 0; i < _entries.Count; i++)
            {
                var c = GetCell(i);
                var e = _entries[i];
                c.entry = e;
                c.btn.gameObject.SetActive(true);
                var rt = c.btn.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(8f + (i % cols) * cellW, -8f - (i / cols) * cellH);
                c.icon.sprite = IconFor(e);
                c.icon.color = e.tint;
                c.count.text = e.count > 1 || e.kind == Kind.Item ? e.count.ToString() : "";
                c.star.text = new string('★', Mathf.Clamp(e.star, 1, 5));
                Tooltip.Bind(c.btn.gameObject, () => e.name + (e.count > 1 ? "  ×" + e.count : "") + (e.locked ? "  · 잠금" : "") + (e.worn ? "  · 장착 중" : ""));
            }
            for (int i = _entries.Count; i < _cells.Count; i++) _cells[i].btn.gameObject.SetActive(false);
            int rows = Mathf.Max(1, (_entries.Count + cols - 1) / cols);
            _content.sizeDelta = new Vector2(0f, Mathf.Max(900f, rows * cellH + 16f));
            _countText.text = _entries.Count + " 종";
            _empty.text = _entries.Count == 0 ? (_tab == 0 ? "보유 에코 없음 — 그림자를 정화하면 에코가 남습니다" : _tab == 1 ? "보유 무기 없음" : _tab == 2 ? "재료 없음 — 그림자 처치 · 상자 · 균열 · 시련에서 얻습니다" : "소모품 없음 — 마을 상점에서 구입할 수 있습니다") : "";
            if (!_hasSel && _entries.Count > 0) { _sel = _entries[0]; _hasSel = true; }
            else if (_hasSel)
            {
                // keep selection alive after a change
                bool found = false;
                foreach (var e in _entries) if (e.kind == _sel.kind && e.id == _sel.id) { _sel = e; found = true; break; }
                if (!found) { _hasSel = _entries.Count > 0; if (_hasSel) _sel = _entries[0]; }
            }
            c_refresh();
            RefreshDetail();
            FocusNavigator.MarkDirty();
        }

        void c_refresh()
        {
            for (int i = 0; i < _entries.Count && i < _cells.Count; i++)
            {
                var e = _entries[i]; var c = _cells[i];
                bool sel = _hasSel && _sel.kind == e.kind && _sel.id == e.id;
                bool picked = _batch && e.kind == Kind.Echo && _picked.Contains(e.id);
                bool dim = _batch && e.kind == Kind.Echo && !Disposable(e);
                c.bg.color = picked ? new Color(0.16f, 0.32f, 0.22f, 1f) : (sel && !_batch) ? UIKit.Theme.Selected : dim ? new Color(0.09f, 0.1f, 0.11f, 1f) : UIKit.Theme.Cell;
                c.frame.color = picked ? new Color(0.6f, 1f, 0.7f, 1f) : new Color(e.tint.r, e.tint.g, e.tint.b, sel && !_batch ? 1f : 0.55f);
                c.badge.text = picked ? "선택" : e.badge;
                c.badge.color = picked ? new Color(0.6f, 1f, 0.7f, 1f) : e.badge == "잠금" ? new Color(1f, 0.8f, 0.4f, 0.95f) : new Color(0.6f, 1f, 0.7f, 0.95f);
                c.icon.color = dim ? new Color(e.tint.r, e.tint.g, e.tint.b, 0.35f) : e.tint;
            }
        }

        Sprite IconFor(Entry e)
        {
            if (e.icon == "echo") return UIKit.Dot;
            if (e.icon == "weapon") return MapIcons.Get("sword");
            return MapIcons.Get(e.icon);
        }

        void CellClick(int idx)
        {
            if (idx >= _entries.Count) return;
            var e = _entries[idx];
            if (_batch && e.kind == Kind.Echo)
            {
                if (!Disposable(e)) { HUDController.Toast(e.worn ? "장착 중인 에코는 처분할 수 없습니다" : "잠긴 에코는 처분할 수 없습니다"); return; }
                if (!_picked.Remove(e.id)) _picked.Add(e.id);
                UIKit.Sfx(2.2f, 0.12f);
                c_refresh(); RefreshDetail();
                return;
            }
            _sel = e; _hasSel = true; c_refresh(); RefreshDetail();
        }

        Cell GetCell(int i)
        {
            while (_cells.Count <= i)
            {
                var c = new Cell();
                int idx = _cells.Count;
                c.btn = UIKit.Btn("cell" + idx, _content, new Vector2(0f, 1f), Vector2.zero, new Vector2(96f, 108f), "", UIKit.Theme.Cell, () => CellClick(idx), 12);
                c.btn.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
                c.bg = c.btn.GetComponent<Image>();
                c.frame = UIKit.Img("frame", c.btn.transform, Color.white, UIKit.Rounded);
                c.frame.type = Image.Type.Sliced; c.frame.fillCenter = false;
                UIKit.Stretch(c.frame.rectTransform, 1f);
                c.icon = UIKit.Img("icon", c.btn.transform, Color.white, UIKit.Dot);
                var irt = c.icon.rectTransform; irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f); irt.anchoredPosition = new Vector2(0f, 8f); irt.sizeDelta = new Vector2(48f, 48f);
                c.star = UIKit.Txt("star", c.btn.transform, new Vector2(0f, 1f), new Vector2(6f, -4f), new Vector2(80f, 16f), "", 10, new Color(1f, 0.85f, 0.4f, 0.9f), TextAnchor.UpperLeft);
                c.star.rectTransform.pivot = new Vector2(0f, 1f);
                c.count = UIKit.Txt("count", c.btn.transform, new Vector2(1f, 0f), new Vector2(-6f, 4f), new Vector2(80f, 18f), "", 13, UIKit.Theme.TextHi, TextAnchor.LowerRight, true, true);
                c.count.rectTransform.pivot = new Vector2(1f, 0f);
                c.badge = UIKit.Txt("badge", c.btn.transform, new Vector2(1f, 1f), new Vector2(-6f, -4f), new Vector2(60f, 16f), "", 11, new Color(0.6f, 1f, 0.7f, 0.95f), TextAnchor.UpperRight, true);
                c.badge.rectTransform.pivot = new Vector2(1f, 1f);
                _cells.Add(c);
            }
            return _cells[i];
        }

        // ---------------------------------------------------------------- batch helpers
        int PickedValue()
        {
            int v = 0;
            foreach (var e in _entries) if (e.kind == Kind.Echo && _picked.Contains(e.id)) v += ShopStock.EchoPrice(e.star, false);
            return v;
        }

        int CountDisposable(int star, out int pickedOfStar)
        {
            int n = 0; pickedOfStar = 0;
            foreach (var e in _entries)
            {
                if (!Disposable(e)) continue;
                bool match = star <= 1 ? e.star <= 1 : star <= 3 ? (e.star > 1 && e.star <= 3) : e.star >= 5;
                if (!match) continue;
                n++;
                if (_picked.Contains(e.id)) pickedOfStar++;
            }
            return n;
        }

        void PickStar(int star)
        {
            int picked; int n = CountDisposable(star, out picked);
            bool all = n > 0 && picked == n;
            foreach (var e in _entries)
            {
                if (!Disposable(e)) continue;
                bool match = star <= 1 ? e.star <= 1 : star <= 3 ? (e.star > 1 && e.star <= 3) : e.star >= 5;
                if (!match) continue;
                if (all) _picked.Remove(e.id); else _picked.Add(e.id);
            }
            c_refresh(); RefreshDetail();
        }

        // ---------------------------------------------------------------- detail
        void RefreshDetail()
        {
            if (_batch && _tab == 0)
            {
                int p1, p3, p5;
                int n1 = CountDisposable(1, out p1), n3 = CountDisposable(3, out p3), n5 = CountDisposable(5, out p5);
                int value = PickedValue();
                _title.text = "일괄 처분";
                _sub.text = "선택 " + _picked.Count + "개   ·   +" + UIKit.Num(value) + " 조각소리";
                _body.text = "셀을 눌러 선택하거나 아래 칩으로 한 번에 고릅니다.\n장착 중 · 잠금 에코는 제외됩니다.\n\n처분 가능\n  ★1  " + n1 + "개 (선택 " + p1 + ")\n  ★3  " + n3 + "개 (선택 " + p3 + ")\n  ★5  " + n5 + "개 (선택 " + p5 + ")\n\n가방 처분가는 상점 매입가의 80%입니다.";
                _foot.text = "잠금은 선택 모드를 끄고 에코를 고른 뒤 " + Glyph.Key("UI/Context", "X") + " 또는 잠금 버튼으로.";
                _btnA.gameObject.SetActive(true); _btnB.gameObject.SetActive(true); _btnC.gameObject.SetActive(true);
                _btnA.interactable = _picked.Count > 0;
                _btnB.interactable = n1 > 0; _btnC.interactable = n3 > 0;
                _btnA.GetComponentInChildren<Text>().text = _picked.Count > 0 ? "처분 " + _picked.Count + "개  (+" + UIKit.Num(value) + ")" : "처분할 에코를 선택하세요";
                _btnB.GetComponentInChildren<Text>().text = n1 > 0 && p1 == n1 ? "★1 선택 해제" : "미장착 ★1 전부";
                _btnC.GetComponentInChildren<Text>().text = n3 > 0 && p3 == n3 ? "★3 선택 해제" : "미장착 ★3 전부";
                FocusNavigator.MarkDirty();
                return;
            }
            if (!_hasSel)
            {
                _title.text = ""; _sub.text = ""; _body.text = ""; _foot.text = "";
                _btnA.gameObject.SetActive(false); _btnB.gameObject.SetActive(false); _btnC.gameObject.SetActive(false);
                return;
            }
            var e = _sel;
            _btnA.gameObject.SetActive(true); _btnB.gameObject.SetActive(true); _btnC.gameObject.SetActive(true);
            _btnA.interactable = _btnB.interactable = _btnC.interactable = true;
            string a = "", b = "", c = "";
            switch (e.kind)
            {
                case Kind.Echo:
                    {
                        var inst = EchoSystem.I != null ? EchoSystem.I.Get(e.id) : null;
                        if (inst == null) { _hasSel = false; RefreshDetail(); return; }
                        var d = inst.Def;
                        int m, s; bool worn = EchoSystem.I.EquipLocation(inst.uid, out m, out s);
                        string subs = "";
                        for (int i = 0; i < inst.subs.Length; i++) subs += "  ◇ " + inst.subs[i].Text + "\n";
                        _title.text = d.name + (inst.level > 0 ? "  +" + inst.level : "") + (inst.locked ? "  [잠금]" : "");
                        _sub.text = new string('★', d.star) + "   코스트 " + d.cost + "   " + (d.family == EchoFamily.Shadow ? "그림자 계열" : "수호 계열") + (worn ? "   · 장착 중 (슬롯 " + (s + 1) + ")" : "");
                        _body.text = "◆ 메인 스탯\n  " + inst.main.Text + "\n\n◇ 부가 스탯\n" + subs + "\n◉ 메인 슬롯 장착 시 (Q)\n  " + d.activeName + " — " + d.activeDesc + "\n\n『" + d.lore + "』";
                        _foot.text = "판매가 " + ShopStock.EchoPrice(d.star, false) + " 조각소리 (상점 " + ShopStock.EchoPrice(d.star, true) + ")" + (inst.locked ? "\n잠금 중 — 판매·일괄 처분·재조율 제외" : "\n" + Glyph.Key("UI/Context", "X") + " 잠금 토글");
                        a = "캐릭터 화면에서 장착 · 강화"; b = inst.locked ? "잠금됨" : "판매"; c = inst.locked ? "잠금 해제" : "잠금";
                        _btnB.interactable = !worn && !inst.locked;
                        break;
                    }
                case Kind.Weapon:
                    {
                        var d = WeaponDB.Get(e.id);
                        int cnt = WeaponSystem.I != null ? WeaponSystem.I.CountOf(e.id) : 0;
                        int eq = WeaponSystem.I != null ? WeaponSystem.I.EquippedCount(e.id) : 0;
                        _title.text = d.name;
                        _sub.text = "T" + d.tier + "   공격 +" + d.atk + "   보유 " + cnt + " (장착 " + eq + ")";
                        _body.text = "패시브: " + WeaponPassiveText(d) + "\n\n『" + d.lore + "』";
                        _foot.text = "판매가 " + ShopStock.WeaponPrice(d.tier, false) + " 조각소리 (상점 " + ShopStock.WeaponPrice(d.tier, true) + ") · 여분만 판매";
                        a = "캐릭터 화면에서 장착"; b = "여분 1개 판매"; c = "";
                        _btnB.interactable = cnt - eq > 0; _btnC.gameObject.SetActive(false);
                        break;
                    }
                default:
                    {
                        var d = ItemDB.Get(e.id);
                        bool flask = d.id == ItemDB.Flask;
                        _title.text = d.name;
                        _sub.text = new string('★', d.star) + "   " + ItemDB.CategoryName(d.cat) + (d.element >= 0 ? "   " + ItemDB.ElementName(d.element) : "") + (flask ? "   충전 " + Inventory.FlaskCharges + "/" + Inventory.FlaskMax : "   보유 " + Inventory.Count(d.id) + " / " + d.stackCap);
                        _body.text = d.desc + "\n\n획득처\n  " + d.source + "\n\n용도\n  " + d.usage + (d.expValue > 0 ? "\n\nEXP " + d.expValue + " (S5 성장 화면에서 투입)" : "");
                        _foot.text = d.sell > 0 ? "판매가 " + Mathf.RoundToInt(d.sell * 0.8f) + " 조각소리 (상점 " + d.sell + ")" : "판매 불가";
                        if (d.cat == ItemCategory.Consumable)
                        {
                            a = flask ? Glyph.Key("Player/Flask", "X") + " 키로 시전 (충전 " + Inventory.FlaskCharges + "/" + Inventory.FlaskMax + ")" : "사용";
                            b = flask ? "" : (Inventory.QuickSlot == d.id ? "퀵슬롯 해제" : "퀵슬롯 지정 (" + Glyph.Key("Player/QuickItem", "Z") + ")");
                            c = flask ? "" : "판매";
                            _btnA.interactable = !flask;
                            if (flask) { _btnB.gameObject.SetActive(false); _btnC.gameObject.SetActive(false); }
                        }
                        else
                        {
                            a = d.cat == ItemCategory.Stone ? "캐릭터 화면에서 투입 (레벨 · 무기)" : "캐릭터 화면에서 사용 (돌파 · 스킬)";
                            b = "판매 (수량 선택)"; c = "";
                            _btnC.gameObject.SetActive(false);
                            _btnB.interactable = d.sell > 0;
                        }
                        break;
                    }
            }
            _btnA.GetComponentInChildren<Text>().text = a;
            _btnB.GetComponentInChildren<Text>().text = b;
            _btnC.GetComponentInChildren<Text>().text = c;
            FocusNavigator.MarkDirty();
        }

        static string WeaponPassiveText(WeaponDef d)
        {
            switch (d.passive)
            {
                case WeaponPassive.SkillDmgPct: return "스킬 피해 +" + d.passiveValue + "%";
                case WeaponPassive.ConcertoGainPct: return "협주 획득 +" + d.passiveValue + "%";
                default: return "없음";
            }
        }

        void Action(int which)
        {
            if (_batch && _tab == 0)
            {
                if (which == 0)
                {
                    if (_picked.Count == 0) return;
                    int n = _picked.Count, value = PickedValue();
                    var uids = new List<int>(_picked);
                    Modal.Confirm("일괄 처분", "에코 " + n + "개를 처분해 조각소리 " + UIKit.Num(value) + "을(를) 받습니다.\n장착 중 · 잠금 에코는 포함되지 않습니다.", "처분", "취소", true, () =>
                    {
                        ShopStock.SellEchoes(uids, false);
                        _picked.Clear();
                        _dirty = true;
                    });
                }
                else if (which == 1) PickStar(1);
                else PickStar(3);
                return;
            }
            if (!_hasSel) return;
            var e = _sel;
            switch (e.kind)
            {
                case Kind.Echo:
                    if (which == 0) ScreenRouter.Replace("Character");
                    else if (which == 1) Modal.Confirm("에코 판매", e.name + "을(를) " + ShopStock.EchoPrice(e.star, false) + " 조각소리에 판매합니다.", "판매", "취소", true, () => { ShopStock.SellEcho(e.id, false); _dirty = true; });
                    else if (which == 2 && EchoSystem.I != null) { EchoSystem.I.ToggleLock(e.id); _dirty = true; }
                    break;
                case Kind.Weapon:
                    if (which == 0) ScreenRouter.Replace("Character");
                    else if (which == 1) Modal.Confirm("무기 판매", e.name + " 여분 1개를 " + ShopStock.WeaponPrice(e.star - 2, false) + " 조각소리에 판매합니다.", "판매", "취소", true, () => { ShopStock.SellWeapon(e.id, false); _dirty = true; });
                    break;
                default:
                    {
                        var d = ItemDB.Get(e.id);
                        if (d.cat == ItemCategory.Consumable)
                        {
                            if (which == 0) { Inventory.Use(d.id); _dirty = true; }
                            else if (which == 1) { Inventory.SetQuick(Inventory.QuickSlot == d.id ? 0 : d.id); _dirty = true; }
                            else SellPrompt(d);
                        }
                        else if (which == 1) SellPrompt(d);
                        else if (which == 0) ScreenRouter.Replace("Character");
                        break;
                    }
            }
        }

        void SellPrompt(ItemDef d)
        {
            int have = Inventory.Count(d.id);
            if (have <= 0 || d.sell <= 0) return;
            int each = Mathf.RoundToInt(d.sell * 0.8f);
            var opts = new List<string> { "1개 (+" + each + ")" };
            var amounts = new List<int> { 1 };
            if (have >= 10) { opts.Add("10개 (+" + each * 10 + ")"); amounts.Add(10); }
            if (have > 1) { opts.Add("전부 " + have + "개 (+" + each * have + ")"); amounts.Add(have); }
            opts.Add("취소");
            Modal.Choice("판매 — " + d.name, "판매가는 상점가의 80%입니다.", opts.ToArray(), pick =>
            {
                if (pick < 0 || pick >= amounts.Count) return;
                ShopStock.SellItem(d.id, amounts[pick], false);
                _dirty = true;
            }, opts.Count - 1);
        }
    }
}
