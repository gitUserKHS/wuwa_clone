using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// ESC hub: entries on the left, session summary on the right (design doc 7.5).
    public class PauseMenu : UIScreen
    {
        public override string Id { get { return "Pause"; } }
        public override string Title { get { return "일시정지"; } }

        Text _quest, _party, _shards, _play, _save, _clock;
        Button _first;
        readonly System.Collections.Generic.List<Button> _entries = new System.Collections.Generic.List<Button>();
        readonly System.Collections.Generic.List<KeyValuePair<Text, string>> _hints = new System.Collections.Generic.List<KeyValuePair<Text, string>>();

        protected override void Build()
        {
            var dim = UIKit.Img("dim", Root, new Color(0.02f, 0.03f, 0.05f, 0.78f), null, true);
            UIKit.Stretch(dim.rectTransform);
            UIKit.Txt("title", Root, new Vector2(0f, 1f), new Vector2(120f, -80f), new Vector2(600f, 60f), "일시정지", 44, new Color(1f, 0.93f, 0.75f), TextAnchor.MiddleLeft, true);
            _clock = UIKit.Txt("clock", Root, new Vector2(1f, 1f), new Vector2(-120f, -90f), new Vector2(700f, 30f), "", 18, UIKit.Theme.TextLo, TextAnchor.MiddleRight);

            string[] labels = { "계속하기", "캐릭터", "가방", "퀘스트", "도감", "지도", "설정", "저장하기", "타이틀로", "게임 종료" };
            string[] keys = { "UI/Cancel", "Player/Character", "Player/Bag", "Player/Quest", "Player/Codex", "Player/Map", "Player/Settings", "System/Save", "", "" };
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var b = UIKit.Btn("entry" + i, Root, new Vector2(0f, 1f), new Vector2(120f, -170f - i * 62f), new Vector2(360f, 52f), labels[i],
                    UIKit.Theme.Button, () => Pick(idx), 19);
                var lt = b.GetComponentInChildren<Text>();
                lt.alignment = TextAnchor.MiddleLeft;
                lt.rectTransform.anchoredPosition = new Vector2(24f, 0f);
                if (!string.IsNullOrEmpty(keys[i]))
                {
                    var hint = UIKit.Txt("key", b.transform, new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(120f, 30f), "", 14, UIKit.Theme.TextLo, TextAnchor.MiddleRight);
                    _hints.Add(new KeyValuePair<Text, string>(hint, keys[i]));
                }
                _entries.Add(b);
                if (i == 0) _first = b;
            }
            UIKit.Txt("note", Root, new Vector2(0f, 1f), new Vector2(120f, -170f - labels.Length * 62f - 6f), new Vector2(420f, 40f),
                "허브: 캐릭터 · 가방 · 퀘스트 · 도감 · 지도 · 설정", 13, UIKit.Theme.TextLo, TextAnchor.UpperLeft);

            var panel = UIKit.Panel("summary", Root, new Color(1f, 1f, 1f, 0.05f), new Vector2(1f, 1f), new Vector2(-120f, -170f), new Vector2(640f, 420f));
            UIKit.Txt("h", panel.transform, new Vector2(0f, 1f), new Vector2(28f, -22f), new Vector2(400f, 30f), "─ 파티 ─", 19, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
            _party = UIKit.Txt("party", panel.transform, new Vector2(0f, 1f), new Vector2(28f, -62f), new Vector2(590f, 60f), "", 18, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
            _shards = UIKit.Txt("shards", panel.transform, new Vector2(0f, 1f), new Vector2(28f, -130f), new Vector2(590f, 30f), "", 17, UIKit.Theme.Info, TextAnchor.UpperLeft);
            UIKit.Txt("h2", panel.transform, new Vector2(0f, 1f), new Vector2(28f, -180f), new Vector2(400f, 30f), "─ 진행 ─", 19, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
            _quest = UIKit.Txt("quest", panel.transform, new Vector2(0f, 1f), new Vector2(28f, -220f), new Vector2(590f, 70f), "", 17, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
            _play = UIKit.Txt("play", panel.transform, new Vector2(0f, 1f), new Vector2(28f, -300f), new Vector2(590f, 30f), "", 16, UIKit.Theme.TextLo, TextAnchor.UpperLeft);
            _save = UIKit.Txt("save", panel.transform, new Vector2(0f, 1f), new Vector2(28f, -334f), new Vector2(590f, 60f), "", 16, UIKit.Theme.TextLo, TextAnchor.UpperLeft);
        }

        public override Selectable DefaultFocus { get { return _first; } }

        public override void OnOpen(object args)
        {
            Refresh();
        }

        void Refresh()
        {
            var q = QuestSystem.I != null ? QuestSystem.I.Current : null;
            _quest.text = q != null ? q.title + "\n" + q.objective : "모든 장을 완료했습니다";
            int lv = ProgressSystem.I != null ? ProgressSystem.I.Level : 1;
            var team = PlayerController.Instance != null ? PlayerController.Instance.GetComponent<TeamManager>() : null;
            string party = "";
            if (team != null && team.members != null)
                for (int i = 0; i < team.members.Length; i++)
                {
                    var m = team.members[i];
                    if (m == null) continue;
                    var cp = ProgressSystem.I != null ? ProgressSystem.I.Of(i) : null;
                    party += (party.Length > 0 ? "   ·   " : "") + m.charName + "  Lv " + (cp != null ? cp.level : lv) + (cp != null && cp.ascension > 0 ? " " + Growth.AscensionNames[cp.ascension] : "");
                }
            _party.text = party;
            _shards.text = "조각소리  " + UIKit.Num(ProgressSystem.I != null ? ProgressSystem.I.Shards : 0) + "   ·   공명탑 " + ResonanceTower.ActiveCount + "/4   ·   시련 완주 " + ContentStats.ArenaClears + "   ·   균열 정화 " + ContentStats.RiftsClosed;
            _play.text = "플레이 시간  " + Clock(SaveSystem.PlaySeconds);
            _save.text = "마지막 저장  " + (string.IsNullOrEmpty(SaveSystem.LastSaveInfo) ? "없음" : SaveSystem.LastSaveInfo);
            string region = PlayerController.Instance != null ? WorldRegions.RegionName(WorldRegions.RegionAt(PlayerController.Instance.transform.position.x, PlayerController.Instance.transform.position.z)) : "";
            _clock.text = (DayNightCycle.I != null ? (DayNightCycle.IsNight ? "☾ " : "☀ ") + DayNightCycle.I.TimeString + "   " : "") + region;
            foreach (var h in _hints) h.Key.text = Glyph.Key(h.Value, "");
        }

        static string Clock(float s) { return SaveSystem.Clock(s); }

        void Pick(int idx)
        {
            switch (idx)
            {
                case 0: ScreenRouter.CloseAll(); break;
                case 1: ScreenRouter.Replace("Character"); break;
                case 2: ScreenRouter.Replace("Bag"); break;
                case 3: ScreenRouter.Replace("Quest"); break;
                case 4: ScreenRouter.Replace("Codex"); break;
                case 5: ScreenRouter.Replace("Map"); break;
                case 6: ScreenRouter.Replace("Settings"); break;
                case 7: ScreenRouter.Push("Slots", "save"); break;
                case 8:
                    Modal.Choice("타이틀로", "타이틀 화면으로 돌아갑니다.\n저장하지 않은 진행은 마지막 저장으로 돌아갑니다.", new[] { "저장 후 이동", "저장 안 함", "취소" },
                        k => { if (k == 0 && GameDirector.I != null) GameDirector.I.ReturnToTitle(true); else if (k == 1 && GameDirector.I != null) GameDirector.I.ReturnToTitle(false); }, 2);
                    break;
                case 9:
                    Modal.Choice("게임 종료", "저장하지 않은 진행은 마지막 저장으로 돌아갑니다.", new[] { "저장 후 종료", "저장 안 함", "취소" },
                        k => { if (k == 0) { if (SaveSystem.I != null) SaveSystem.I.AutoSave("종료 저장"); Application.Quit(); } else if (k == 1) { SaveSystem.SkipQuitSave = true; Application.Quit(); } }, 2);
                    break;
            }
        }
    }
}
