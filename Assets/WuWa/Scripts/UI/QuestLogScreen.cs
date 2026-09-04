using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Hub tab "퀘스트": 메인 (chapter steps) · 사이드 (현상 게시판) · 이벤트 (rifts, arena, exploration).
    public class QuestLogScreen : UIScreen
    {
        public override string Id { get { return "Quest"; } }
        public override string Title { get { return "퀘스트"; } }
        public override bool IsHubTab { get { return true; } }

        static readonly string[] Tabs = { "메인", "사이드", "이벤트" };
        ScreenRouter.HubHeader _header;
        readonly List<Button> _tabBtns = new List<Button>();
        readonly List<GameObject> _rows = new List<GameObject>();
        RectTransform _list;
        Text _title, _sub, _body, _reward, _empty;
        Button _btnTrack, _btnMap;
        int _tab, _selMain = -1, _selBounty = -1;
        bool _dirty;

        protected override void Build()
        {
            _header = ScreenRouter.BuildHubHeader(Root, "퀘스트", Id);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                var b = UIKit.Btn("qtab" + i, Root, new Vector2(0f, 1f), new Vector2(60f, -140f - i * 58f), new Vector2(150f, 50f), Tabs[i], UIKit.Theme.Button, () => { _tab = idx; Refresh(); }, 17);
                b.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
                _tabBtns.Add(b);
            }
            var view = UIKit.Img("view", Root, new Color(1f, 1f, 1f, 0.03f), null, true);
            var vrt = view.rectTransform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(0f, 1f); vrt.pivot = new Vector2(0f, 1f);
            vrt.anchoredPosition = new Vector2(240f, -140f); vrt.sizeDelta = new Vector2(900f, 900f);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40f;
            var content = new GameObject("content");
            content.transform.SetParent(view.transform, false);
            _list = content.AddComponent<RectTransform>();
            _list.anchorMin = new Vector2(0f, 1f); _list.anchorMax = new Vector2(1f, 1f); _list.pivot = new Vector2(0.5f, 1f);
            _list.anchoredPosition = Vector2.zero; _list.sizeDelta = new Vector2(0f, 900f);
            scroll.content = _list; scroll.viewport = vrt;
            _empty = UIKit.Txt("empty", view.transform, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(860f, 60f), "", 16, UIKit.Theme.TextLo, TextAnchor.UpperLeft);
            _empty.rectTransform.pivot = new Vector2(0f, 1f);

            var panel = UIKit.Panel("detail", Root, new Color(1f, 1f, 1f, 0.05f), new Vector2(0f, 1f), new Vector2(1170f, -140f), new Vector2(690f, 900f));
            var pr = panel.transform;
            _title = UIKit.Txt("title", pr, new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(640f, 34f), "", 22, UIKit.Theme.TextHi, TextAnchor.MiddleLeft, true);
            _title.rectTransform.pivot = new Vector2(0f, 1f);
            _sub = UIKit.Txt("sub", pr, new Vector2(0f, 1f), new Vector2(24f, -60f), new Vector2(640f, 30f), "", 16, UIKit.Theme.Accent, TextAnchor.MiddleLeft);
            _sub.rectTransform.pivot = new Vector2(0f, 1f);
            _body = UIKit.Txt("body", pr, new Vector2(0f, 1f), new Vector2(24f, -100f), new Vector2(640f, 520f), "", 15, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
            _body.rectTransform.pivot = new Vector2(0f, 1f);
            _reward = UIKit.Txt("reward", pr, new Vector2(0f, 1f), new Vector2(24f, -700f), new Vector2(640f, 60f), "", 14, UIKit.Theme.Info, TextAnchor.UpperLeft);
            _reward.rectTransform.pivot = new Vector2(0f, 1f);
            _btnTrack = UIKit.Btn("track", pr, new Vector2(0f, 1f), new Vector2(24f, -790f), new Vector2(310f, 46f), "추적", UIKit.Theme.Confirm, Track, 16);
            _btnTrack.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            _btnMap = UIKit.Btn("map", pr, new Vector2(0f, 1f), new Vector2(354f, -790f), new Vector2(310f, 46f), "지도에서 보기", UIKit.Theme.Button, ShowOnMap, 16);
            _btnMap.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
        }

        public override void OnOpen(object args)
        {
            ScreenRouter.RefreshHubHeader(_header);
            BountyBoard.Changed += MarkDirty;
            if (_selMain < 0 && QuestSystem.I != null) _selMain = QuestSystem.I.CurrentIndex;
            Refresh();
        }

        public override void OnClose() { BountyBoard.Changed -= MarkDirty; }
        public override void OnTick() { if (_dirty) { _dirty = false; Refresh(); } }
        void MarkDirty() { _dirty = true; }
        public override Selectable DefaultFocus { get { return _tabBtns[Mathf.Clamp(_tab, 0, 2)]; } }

        Button Row(int i, string left, string right, Color bg, System.Action click, bool dim = false)
        {
            var b = UIKit.Btn("row" + i, _list, new Vector2(0f, 1f), new Vector2(12f, -10f - i * 58f), new Vector2(876f, 50f), "", bg, click, 14);
            b.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            var l = UIKit.Txt("l", b.transform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(640f, 40f), left, 16, dim ? UIKit.Theme.TextLo : UIKit.Theme.TextHi, TextAnchor.MiddleLeft);
            l.rectTransform.pivot = new Vector2(0f, 0.5f);
            var r = UIKit.Txt("r", b.transform, new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(220f, 40f), right, 14, UIKit.Theme.TextLo, TextAnchor.MiddleRight);
            r.rectTransform.pivot = new Vector2(1f, 0.5f);
            _rows.Add(b.gameObject);
            return b;
        }

        void Refresh()
        {
            for (int i = 0; i < _tabBtns.Count; i++) _tabBtns[i].GetComponent<Image>().color = i == _tab ? UIKit.Theme.Selected : UIKit.Theme.Button;
            foreach (var r in _rows) Destroy(r);
            _rows.Clear();
            var qs = QuestSystem.I;
            int n = 0;
            _empty.text = "";
            if (_tab == 0 && qs != null)
            {
                int cur = qs.CurrentIndex;
                for (int i = 0; i < qs.StepCount; i++)
                {
                    int state = qs.StepState(i);
                    if (state == 0 && i > cur + 1) continue;          // only the next locked step shows
                    var s = qs.Step(i);
                    int idx = i;
                    string icon = state == 2 ? "✓  " : state == 1 ? "▶  " : "🔒  ";
                    string right = state == 1 ? "진행 중" : state == 2 ? "완료" : "잠김";
                    bool sel = idx == _selMain;
                    Row(n++, icon + s.title + "  —  " + s.objective, right, sel ? UIKit.Theme.Selected : UIKit.Theme.Cell, () => { _selMain = idx; Refresh(); }, state == 0);
                }
            }
            else if (_tab == 1)
            {
                var list = BountyBoard.Active;
                if (list.Count == 0) _empty.text = "오늘의 현상이 아직 없습니다 — 마을 게시판은 게임 내 하루마다 갱신됩니다.";
                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i];
                    int idx = i;
                    bool tracked = qs != null && qs.TrackedBounty == b.id;
                    string right = b.done ? "완료" : (Mathf.Min(b.progress, b.goal) + " / " + b.goal) + (tracked ? "  ●" : "");
                    Row(n++, (b.done ? "✓  " : b.grand ? "★  " : "•  ") + b.Title, right, idx == _selBounty ? UIKit.Theme.Selected : (b.grand ? new Color(0.22f, 0.18f, 0.30f, 1f) : UIKit.Theme.Cell), () => { _selBounty = idx; Refresh(); }, b.done);
                }
            }
            else
            {
                string rift = MapSystem.Dynamic.Count > 0 ? "침식 균열 활성 중 — 지도의 보라색 마커" : "침식 균열 대기 (80~140초 주기 · 밤 ×0.6)";
                Row(n++, "✦  " + rift, "정화 " + ContentStats.RiftsClosed + "회", UIKit.Theme.Cell, () => { });
                Row(n++, "◉  시련의 제단 — 완주 " + ContentStats.ArenaClears + "회 · 최고 " + ContentStats.ArenaBestWave + "웨이브", ArenaTrial.Running ? "진행 중" : "대기", UIKit.Theme.Cell, () => { });
                Row(n++, "☀  " + (DayNightCycle.I != null ? DayNightCycle.I.TimeString : "") + " · " + (DayNightCycle.DayIndex + 1) + "일째 · 상점/현상/군락 일일 갱신", "", UIKit.Theme.Cell, () => { });
                for (int r = 0; r < 8; r++)
                {
                    if (!MapDiscovery.RegionDiscovered(r)) continue;
                    var st = RegionCompletion.Of(r);
                    Row(n++, "▣  " + WorldRegions.RegionName(r) + "  정화율 " + st.Percent + "%", st.done + " / " + st.total, UIKit.Theme.Cell, () => { });
                }
            }
            _list.sizeDelta = new Vector2(0f, Mathf.Max(900f, n * 58f + 20f));
            RefreshDetail();
            FocusNavigator.MarkDirty();
        }

        void RefreshDetail()
        {
            var qs = QuestSystem.I;
            _btnTrack.gameObject.SetActive(false); _btnMap.gameObject.SetActive(false);
            if (_tab == 0 && qs != null)
            {
                int i = Mathf.Clamp(_selMain, 0, qs.StepCount - 1);
                var s = qs.Step(i);
                int state = qs.StepState(i);
                _title.text = s.title;
                _sub.text = (state == 2 ? "완료" : state == 1 ? "진행 중" : "잠김") + "   ·   " + s.objective + (s.goal > 1 && state == 1 ? "  (" + s.progress + "/" + s.goal + ")" : "");
                _body.text = state == 0 ? "이전 목표를 완료하면 열립니다." : s.description;
                _reward.text = "보상  " + s.reward;
                bool tracked = qs.TrackedBounty < 0;
                _btnTrack.gameObject.SetActive(state == 1);
                _btnTrack.GetComponentInChildren<Text>().text = tracked ? "● 추적 중 (메인)" : "메인 퀘스트 추적";
                _btnTrack.interactable = !tracked;
                _btnMap.gameObject.SetActive(s.hasTarget && state == 1);
            }
            else if (_tab == 1)
            {
                var list = BountyBoard.Active;
                if (list.Count == 0 || _selBounty < 0 || _selBounty >= list.Count) { _title.text = "현상 게시판"; _sub.text = "메아리 마을"; _body.text = "매일 3건 + 3일마다 대현상 1건. 목표를 달성하면 자동으로 보상이 지급됩니다.\n\n유형: 지역 처치 · 균열 정화 · 전투 평가 S · 정예 처치 · 상자 개봉"; _reward.text = ""; return; }
                var b = list[_selBounty];
                _title.text = b.Title;
                _sub.text = b.done ? "완료" : b.Objective;
                _body.text = (b.grand ? "3일마다 걸리는 대현상입니다.\n\n" : "") + (b.type == BountyType.KillRegion ? WorldRegions.RegionName(b.region) + " 지역에서 그림자를 처치하세요. 지도에서 지역을 확인할 수 있습니다." : b.type == BountyType.Rift ? "들판에 열리는 보랏빛 균열을 정화하세요. 밤에 더 자주 열립니다." : b.type == BountyType.RankS ? "전투를 피격·패리·변주 기준으로 평가합니다. S를 받으려면 피격을 줄이고 패리·변주를 섞으세요." : b.type == BountyType.Elite ? "주술사·거암의 그림자는 황무지·호수·고원·도시에 있습니다." : "미개봉 보물 상자를 여세요. 지도의 상자 마커는 탐색한 셀에서 보입니다.");
                _reward.text = "보상  " + b.RewardText;
                bool tracked = qs != null && qs.TrackedBounty == b.id;
                _btnTrack.gameObject.SetActive(!b.done);
                _btnTrack.GetComponentInChildren<Text>().text = tracked ? "추적 해제" : "추적";
                _btnTrack.interactable = true;
                _btnMap.gameObject.SetActive(b.HasTarget);
            }
            else
            {
                _title.text = "이벤트"; _sub.text = "반복 컨텐츠 현황";
                _body.text = "침식 균열은 정화할 때마다 조각소리·에코·소재를, 지역별 첫 정화는 정화율을 올립니다.\n시련의 제단은 반복할수록 파도가 거세집니다.\n지역 정화율 50%·100%에 보상이 있습니다.";
                _reward.text = "";
            }
        }

        void Track()
        {
            var qs = QuestSystem.I;
            if (qs == null) return;
            if (_tab == 0) qs.TrackedBounty = -1;
            else if (_tab == 1 && _selBounty >= 0 && _selBounty < BountyBoard.Active.Count)
            {
                var b = BountyBoard.Active[_selBounty];
                qs.TrackedBounty = qs.TrackedBounty == b.id ? -1 : b.id;
            }
            qs.RefreshTracker();
            Refresh();
        }

        void ShowOnMap()
        {
            var qs = QuestSystem.I;
            if (qs == null) return;
            Vector3 pos; string name, obj;
            if (_tab == 0) { var s = qs.Step(Mathf.Clamp(_selMain, 0, qs.StepCount - 1)); if (!s.hasTarget) return; pos = s.target; }
            else if (_tab == 1 && _selBounty >= 0 && _selBounty < BountyBoard.Active.Count) pos = BountyBoard.Active[_selBounty].Target;
            else return;
            MapScreen.PendingFocus = pos;
            ScreenRouter.Push("Map");
        }
    }
}
