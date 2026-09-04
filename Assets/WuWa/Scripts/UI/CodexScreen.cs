using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Hub tab "도감": 에코 · 적 · 지역 · 무기 · 아이템 · 튜토리얼.
    public class CodexScreen : UIScreen
    {
        public override string Id { get { return "Codex"; } }
        public override string Title { get { return "도감"; } }
        public override bool IsHubTab { get { return true; } }

        static readonly string[] Tabs = { "에코", "적", "지역", "무기", "아이템", "튜토리얼" };
        struct Entry { public string name, icon; public Color tint; public bool known; public int index; public string detailTitle, detailSub, detailBody; public Vector3 mapPos; public bool hasMap; public string tutorialId; }

        ScreenRouter.HubHeader _header;
        readonly List<Button> _tabBtns = new List<Button>();
        readonly List<GameObject> _cells = new List<GameObject>();
        readonly List<Entry> _entries = new List<Entry>();
        RectTransform _grid;
        Text _title, _sub, _body, _count;
        Button _btnAction;
        int _tab, _sel;

        protected override void Build()
        {
            _header = ScreenRouter.BuildHubHeader(Root, "도감", Id);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                var b = UIKit.Btn("ctab" + i, Root, new Vector2(0f, 1f), new Vector2(60f, -140f - i * 58f), new Vector2(150f, 50f), Tabs[i], UIKit.Theme.Button, () => { _tab = idx; _sel = 0; Refresh(); }, 16);
                b.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
                _tabBtns.Add(b);
            }
            _count = UIKit.Txt("count", Root, new Vector2(0f, 1f), new Vector2(240f, -112f), new Vector2(900f, 24f), "", 14, UIKit.Theme.TextLo, TextAnchor.MiddleLeft);
            _count.rectTransform.pivot = new Vector2(0f, 1f);
            var view = UIKit.Img("view", Root, new Color(1f, 1f, 1f, 0.03f), null, true);
            var vrt = view.rectTransform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(0f, 1f); vrt.pivot = new Vector2(0f, 1f);
            vrt.anchoredPosition = new Vector2(240f, -140f); vrt.sizeDelta = new Vector2(900f, 900f);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40f;
            var content = new GameObject("content");
            content.transform.SetParent(view.transform, false);
            _grid = content.AddComponent<RectTransform>();
            _grid.anchorMin = new Vector2(0f, 1f); _grid.anchorMax = new Vector2(1f, 1f); _grid.pivot = new Vector2(0.5f, 1f);
            _grid.anchoredPosition = Vector2.zero; _grid.sizeDelta = new Vector2(0f, 900f);
            scroll.content = _grid; scroll.viewport = vrt;

            var panel = UIKit.Panel("detail", Root, new Color(1f, 1f, 1f, 0.05f), new Vector2(0f, 1f), new Vector2(1170f, -140f), new Vector2(690f, 900f));
            var pr = panel.transform;
            _title = UIKit.Txt("title", pr, new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(640f, 34f), "", 22, UIKit.Theme.TextHi, TextAnchor.MiddleLeft, true);
            _title.rectTransform.pivot = new Vector2(0f, 1f);
            _sub = UIKit.Txt("sub", pr, new Vector2(0f, 1f), new Vector2(24f, -60f), new Vector2(640f, 30f), "", 15, UIKit.Theme.Accent, TextAnchor.MiddleLeft);
            _sub.rectTransform.pivot = new Vector2(0f, 1f);
            _body = UIKit.Txt("body", pr, new Vector2(0f, 1f), new Vector2(24f, -100f), new Vector2(640f, 640f), "", 15, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
            _body.rectTransform.pivot = new Vector2(0f, 1f);
            _btnAction = UIKit.Btn("act", pr, new Vector2(0f, 1f), new Vector2(24f, -790f), new Vector2(640f, 46f), "-", UIKit.Theme.Button, Action, 16);
            _btnAction.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        }

        public override void OnOpen(object args) { ScreenRouter.RefreshHubHeader(_header); Refresh(); }
        public override Selectable DefaultFocus { get { return _tabBtns[Mathf.Clamp(_tab, 0, Tabs.Length - 1)]; } }

        void Collect()
        {
            _entries.Clear();
            switch (_tab)
            {
                case 0:
                    foreach (var d in EchoDB.All)
                    {
                        bool known = EchoSystem.I != null && EchoSystem.I.Discovered(d.id);
                        int owned = EchoSystem.I != null ? EchoSystem.I.CountOf(d.id) : 0;
                        _entries.Add(new Entry { name = known ? d.name : "??", icon = "echo", tint = known ? d.Tint : new Color(1f, 1f, 1f, 0.25f), known = known, index = d.id,
                            detailTitle = known ? d.name : "미발견 에코", detailSub = known ? new string('★', d.star) + "   코스트 " + d.cost + "   " + (d.family == EchoFamily.Shadow ? "그림자 계열" : "수호 계열") : "★" + d.star + " · 코스트 " + d.cost,
                            detailBody = known ? "◈ 패시브  " + PassiveText(d) + "\n◉ Q 스킬  " + d.activeName + " — " + d.activeDesc + "\n\n보유 " + owned + "개\n\n『" + d.lore + "』" : "그림자를 정화하면 발견됩니다.\n힌트: " + (d.star >= 5 ? "보스 · 시련 · 상점" : d.star >= 3 ? "정예 그림자 (주술사·거암)" : "잡몹 그림자 20%") });
                    }
                    break;
                case 1:
                    foreach (var e in Codex.Enemies)
                    {
                        bool known = Codex.EnemySeen(e);
                        _entries.Add(new Entry { name = known ? e.name : "미확인 그림자", icon = e.boss ? "boss" : e.key == "rift" ? "rift" : e.elite ? "camp" : "dot", tint = known ? (e.boss ? MapMarkers.BossC : e.key == "rift" ? MapMarkers.RiftC : e.elite ? MapMarkers.CampC : new Color(0.75f, 0.6f, 1f)) : new Color(1f, 1f, 1f, 0.25f), known = known,
                            detailTitle = known ? e.name : "미확인 그림자", detailSub = known ? "처치 " + Codex.KillsOf(e) : "조우하면 기록됩니다", detailBody = known ? e.desc + "\n\n출현  " + e.regions + "\n드랍  " + e.drops + "\n예고  " + e.telegraph : "출현  " + e.regions });
                    }
                    break;
                case 2:
                    foreach (var r in Codex.Regions)
                    {
                        bool known = MapDiscovery.RegionDiscovered(r.id);
                        var st = RegionCompletion.Of(r.id);
                        _entries.Add(new Entry { name = known ? r.name : "???", icon = "house", tint = known ? UIKit.Theme.Accent : new Color(1f, 1f, 1f, 0.25f), known = known, index = r.id, hasMap = known, mapPos = BountyBoard.RegionCenter(r.id),
                            detailTitle = known ? r.name : "미탐색 지역", detailSub = known ? "정화율 " + st.Percent + "%  (" + st.done + "/" + st.total + ")" : "발견하면 기록됩니다", detailBody = known ? r.desc + "\n\n특징  " + r.features + "\n\n정화율 50%: 조각소리 200 · 조율기 1 · 공명석 2\n정화율 100%: 시련 증표 2 · 지역 결정 4" : "지도의 안개를 걷어내면 열립니다." });
                    }
                    break;
                case 3:
                    foreach (var w in WeaponDB.All)
                    {
                        bool known = Codex.WeaponSeen(w.id) || (WeaponSystem.I != null && WeaponSystem.I.CountOf(w.id) > 0);
                        _entries.Add(new Entry { name = known ? w.name : "미획득 무기 (T" + w.tier + ")", icon = "sword", tint = known ? w.Tint : new Color(1f, 1f, 1f, 0.25f), known = known,
                            detailTitle = known ? w.name : "미획득 무기", detailSub = "T" + w.tier + "   공격 +" + w.atk + (known ? "   보유 " + (WeaponSystem.I != null ? WeaponSystem.I.CountOf(w.id) : 0) : ""), detailBody = known ? w.PassiveText + "\n\n『" + w.lore + "』" : (w.tier == 2 ? "정예 그림자 · 상점 260" : w.tier == 3 ? "무관의 그림자 · 시련 · 상점 900 (파티 Lv 15)" : "시작 지급") });
                    }
                    break;
                case 4:
                    foreach (var d in ItemDB.All)
                    {
                        bool known = Codex.ItemSeen(d.id) || Inventory.Count(d.id) > 0 || d.id == ItemDB.Flask;
                        _entries.Add(new Entry { name = known ? d.name : "??", icon = d.icon, tint = known ? d.Tint : new Color(1f, 1f, 1f, 0.25f), known = known,
                            detailTitle = known ? d.name : "미획득 아이템", detailSub = new string('★', d.star) + "   " + ItemDB.CategoryName(d.cat), detailBody = known ? d.desc + "\n\n획득처  " + d.source + "\n용도  " + d.usage : "획득처  " + d.source });
                    }
                    break;
                default:
                    foreach (var c in Tutorial.Cards)
                    {
                        bool known = Tutorial.Seen(c.id);
                        _entries.Add(new Entry { name = c.title, icon = c.modal ? "quest" : "tick", tint = known ? UIKit.Theme.Info : new Color(1f, 1f, 1f, 0.3f), known = known, tutorialId = c.id,
                            detailTitle = c.title, detailSub = known ? "표시됨" : "아직 표시되지 않음" + (c.modal ? " · 모달 카드" : " · 힌트"), detailBody = Tutorial.Expand(c.body) });
                    }
                    break;
            }
        }

        static string PassiveText(EchoDef d)
        {
            switch (d.passive)
            {
                case EchoPassive.AtkPct: return "공격력 +" + d.passiveValue + "%";
                case EchoPassive.MoveSpeedPct: return "이동속도 +" + d.passiveValue + "%";
                case EchoPassive.SkillDmgPct: return "스킬 피해 +" + d.passiveValue + "%";
                case EchoPassive.DamageReductionPct: return "받는 피해 -" + d.passiveValue + "%";
                case EchoPassive.AllElemPct: return "모든 속성 피해 +" + d.passiveValue + "%";
                default: return "";
            }
        }

        void Refresh()
        {
            for (int i = 0; i < _tabBtns.Count; i++) _tabBtns[i].GetComponent<Image>().color = i == _tab ? UIKit.Theme.Selected : UIKit.Theme.Button;
            Collect();
            foreach (var c in _cells) Destroy(c);
            _cells.Clear();
            int known = 0; foreach (var e in _entries) if (e.known) known++;
            _count.text = Tabs[_tab] + "  " + known + " / " + _entries.Count;
            const int cols = 4; const float w = 214f, h = 76f;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i]; int idx = i;
                var b = UIKit.Btn("cell" + i, _grid, new Vector2(0f, 1f), new Vector2(12f + (i % cols) * (w + 8f), -10f - (i / cols) * (h + 8f)), new Vector2(w, h), "", idx == _sel ? UIKit.Theme.Selected : UIKit.Theme.Cell, () => { _sel = idx; Refresh(); }, 13);
                b.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
                var icon = UIKit.Img("ic", b.transform, e.tint, e.icon == "echo" ? UIKit.Dot : MapIcons.Get(e.icon));
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f); icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                icon.rectTransform.anchoredPosition = new Vector2(10f, 0f); icon.rectTransform.sizeDelta = new Vector2(44f, 44f);
                var t = UIKit.Txt("n", b.transform, new Vector2(0f, 0.5f), new Vector2(62f, 0f), new Vector2(150f, 60f), e.name, 13, e.known ? UIKit.Theme.TextHi : UIKit.Theme.TextLo, TextAnchor.MiddleLeft);
                t.rectTransform.pivot = new Vector2(0f, 0.5f);
                _cells.Add(b.gameObject);
            }
            _grid.sizeDelta = new Vector2(0f, Mathf.Max(900f, ((_entries.Count + cols - 1) / cols) * (h + 8f) + 20f));
            if (_entries.Count > 0)
            {
                var e = _entries[Mathf.Clamp(_sel, 0, _entries.Count - 1)];
                _title.text = e.detailTitle; _sub.text = e.detailSub; _body.text = e.detailBody;
                bool act = (_tab == 2 && e.hasMap) || (_tab == 5);
                _btnAction.gameObject.SetActive(act);
                _btnAction.GetComponentInChildren<Text>().text = _tab == 2 ? "지도에서 보기" : "다시 보기";
            }
            else { _title.text = ""; _sub.text = ""; _body.text = ""; _btnAction.gameObject.SetActive(false); }
            FocusNavigator.MarkDirty();
        }

        void Action()
        {
            if (_entries.Count == 0) return;
            var e = _entries[Mathf.Clamp(_sel, 0, _entries.Count - 1)];
            if (_tab == 2 && e.hasMap) { MapScreen.PendingFocus = e.mapPos; ScreenRouter.Push("Map"); }
            else if (_tab == 5 && !string.IsNullOrEmpty(e.tutorialId))
            {
                var card = Tutorial.Get(e.tutorialId);
                if (card != null) ScreenRouter.Push("Tutorial", card);
            }
        }
    }
}
