using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Journey summary: shown when the demo's main quest completes (and by the
    /// tests). Exploration tallies on the left, combat tallies on the right.
    public class ResultsScreen : UIScreen
    {
        public override string Id { get { return "Results"; } }
        public override string Title { get { return "여정의 기록"; } }

        Text _title, _sub, _left, _right, _footer;
        Button _continue, _toTitle;

        protected override void Build()
        {
            var bg = UIKit.Img("bg", Root, new Color(0.03f, 0.04f, 0.055f, 0.94f), null, true);
            UIKit.Stretch(bg.rectTransform);
            var band = UIKit.Img("band", Root, new Color(1f, 0.82f, 0.35f, 0.6f));
            var brt = band.rectTransform; brt.anchorMin = new Vector2(0.5f, 1f); brt.anchorMax = new Vector2(0.5f, 1f); brt.pivot = new Vector2(0.5f, 1f); brt.anchoredPosition = new Vector2(0f, -196f); brt.sizeDelta = new Vector2(1180f, 2f);
            _title = UIKit.Txt("title", Root, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1200f, 70f), "잔향 — 여정의 기록", 46, UIKit.Theme.Accent, TextAnchor.MiddleCenter, true, true);
            _sub = UIKit.Txt("sub", Root, new Vector2(0.5f, 1f), new Vector2(0f, -166f), new Vector2(1200f, 30f), "", 18, UIKit.Theme.TextLo, TextAnchor.MiddleCenter);

            var lp = UIKit.Panel("leftPanel", Root, UIKit.Theme.Panel, new Vector2(0.5f, 1f), new Vector2(-300f, -230f), new Vector2(560f, 560f));
            UIKit.Txt("lh", lp.transform, new Vector2(0f, 1f), new Vector2(26f, -20f), new Vector2(400f, 30f), "─ 탐험 ─", 20, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
            _left = UIKit.Txt("left", lp.transform, new Vector2(0f, 1f), new Vector2(26f, -62f), new Vector2(510f, 480f), "", 17, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
            var rp = UIKit.Panel("rightPanel", Root, UIKit.Theme.Panel, new Vector2(0.5f, 1f), new Vector2(300f, -230f), new Vector2(560f, 560f));
            UIKit.Txt("rh", rp.transform, new Vector2(0f, 1f), new Vector2(26f, -20f), new Vector2(400f, 30f), "─ 전투 ─", 20, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
            _right = UIKit.Txt("right", rp.transform, new Vector2(0f, 1f), new Vector2(26f, -62f), new Vector2(510f, 480f), "", 17, UIKit.Theme.TextHi, TextAnchor.UpperLeft);

            _footer = UIKit.Txt("footer", Root, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1200f, 30f), "", 17, UIKit.Theme.Info, TextAnchor.MiddleCenter);
            _continue = UIKit.Btn("continue", Root, new Vector2(0.5f, 0f), new Vector2(-130f, 70f), new Vector2(240f, 50f), "계속 탐험", UIKit.Theme.Confirm, () => ScreenRouter.CloseAll(), 18);
            _toTitle = UIKit.Btn("toTitle", Root, new Vector2(0.5f, 0f), new Vector2(130f, 70f), new Vector2(240f, 50f), "타이틀로", UIKit.Theme.Button, () =>
                Modal.Choice("타이틀로", "타이틀 화면으로 돌아갑니다.", new[] { "저장 후 이동", "저장 안 함", "취소" },
                    k => { if (k == 0 && GameDirector.I != null) GameDirector.I.ReturnToTitle(true); else if (k == 1 && GameDirector.I != null) GameDirector.I.ReturnToTitle(false); }, 2), 18);
        }

        public override Selectable DefaultFocus { get { return _continue; } }

        public override void OnOpen(object args) { Refresh(); }

        static string Row(string label, string value) { return label.PadRight(14) + value + "\n"; }

        void Refresh()
        {
            bool done = GameFlags.Has("demo_done");
            _sub.text = done ? "세계의 노래가 겹겹이 돌아왔다. 여운은 계속된다…" : "지금까지의 여정";

            // ---- exploration
            var team = PlayerController.Instance != null ? PlayerController.Instance.GetComponent<TeamManager>() : null;
            string party = "";
            if (team != null && team.members != null)
                for (int i = 0; i < team.members.Length; i++)
                {
                    var m = team.members[i];
                    if (m == null) continue;
                    var cp = ProgressSystem.I != null ? ProgressSystem.I.Of(i) : null;
                    party += (party.Length > 0 ? " · " : "") + m.charName + " Lv " + (cp != null ? cp.level : 1);
                }
            int stones = 0, stonesOn = 0;
            foreach (var w in Waystone.All) if (w != null) { stones++; if (w.Discovered) stonesOn++; }
            int chests = 0, chestsOpen = 0;
            foreach (var c in TreasureChest.All) if (c != null) { chests++; if (c.Opened || GameFlags.Has("chest_" + c.chestId)) chestsOpen++; }
            int regionsDone = 0, regionsCounted = 0, pctSum = 0;
            for (int r = 0; r <= WorldRegions.Rim; r++)
            {
                var s = RegionCompletion.Of(r);
                if (s.total <= 0) continue;
                regionsCounted++; pctSum += s.Percent;
                if (s.done >= s.total) regionsDone++;
            }
            int echoDefs = 0, echoKnown = 0;
            foreach (var d in EchoDB.All) { echoDefs++; if (EchoSystem.I != null && EchoSystem.I.Discovered(d.id)) echoKnown++; }
            int enemiesSeen = 0;
            foreach (var e in Codex.Enemies) if (Codex.EnemySeen(e)) enemiesSeen++;
            int regionsFound = 0;
            for (int r = 0; r <= WorldRegions.Rim; r++) if (MapDiscovery.RegionDiscovered(r)) regionsFound++;

            string L = "";
            L += Row("플레이 시간", SaveSystem.Clock(SaveSystem.PlaySeconds));
            L += Row("파티", party);
            L += Row("조각소리", UIKit.Num(ProgressSystem.I != null ? ProgressSystem.I.Shards : 0) + "   ·   증표 " + Inventory.TrialTokens);
            L += Row("공명탑", ResonanceTower.ActiveCount + " / 4");
            L += Row("공명 표석", stonesOn + " / " + stones);
            L += Row("보물 상자", chestsOpen + " / " + chests + "  (개봉 " + ContentStats.ChestsOpened + "회)");
            L += Row("지역 발견", regionsFound + " / " + (WorldRegions.Rim + 1));
            L += Row("지역 정화", "완료 " + regionsDone + " / " + regionsCounted + "  ·  평균 " + (regionsCounted > 0 ? pctSum / regionsCounted : 0) + "%");
            L += Row("에코 도감", echoKnown + " / " + echoDefs);
            L += Row("적 도감", enemiesSeen + " / " + Codex.Enemies.Length);
            L += Row("튜토리얼", Tutorial.SeenCount + " / " + Tutorial.Cards.Length);
            L += Row("일자", "게임 내 " + (DayNightCycle.DayIndex + 1) + "일째");
            _left.text = L;

            // ---- combat
            int kindKills = 0;
            foreach (var k in Codex.KillsByKind) kindKills += k;
            string R = "";
            R += Row("처치", ContentStats.Kills + "  (정예 " + Codex.EliteKills + " · 보스 " + Codex.BossKills + ")");
            R += Row("패리", ContentStats.Parries.ToString());
            R += Row("완벽 회피", ContentStats.PerfectDodges.ToString());
            R += Row("전투 평가 S", ContentStats.RankS + "회");
            R += Row("시련 완주", ContentStats.ArenaClears + "회  (최고 " + ContentStats.ArenaBestWave + "웨이브)");
            R += Row("균열 정화", ContentStats.RiftsClosed + "회");
            R += Row("현상", BountyBoard.Active.Count + "건 진행 중");
            R += Row("메인 퀘스트", (QuestSystem.I != null ? Mathf.Min(QuestSystem.I.StepIndex, QuestSystem.I.StepCount) + " / " + QuestSystem.I.StepCount : "-") + (done ? "  ·  완결" : ""));
            _right.text = R;

            _footer.text = done ? "데모를 끝까지 플레이해 주셔서 고맙습니다. 세계는 계속 열려 있습니다." : "여정은 계속됩니다.";
        }
    }
}
