using System;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    public enum QuestEvent { Kill, Tower, Boss, Reach, Talk, Rift, Arena }

    [Serializable]
    public class QuestStep
    {
        public string title;
        public string objective;
        public string description = "", reward = "";
        public QuestEvent type;
        public int goal = 1;
        public int param = -1;           // tower id / region id, -1 = any
        public Vector3 target;           // tracker distance marker
        public bool hasTarget;
        [NonSerialized] public int progress;
    }

    /// Linear chapter-1 quest line with a HUD tracker (GDD ch.2).
    public class QuestSystem : MonoBehaviour
    {
        readonly List<QuestStep> _steps = new List<QuestStep>();
        int _current;
        bool _done;

        public static QuestSystem I { get; private set; }
        public QuestStep Current { get { return !_done && _current < _steps.Count ? _steps[_current] : null; } }
        public int StepIndex { get { return _done ? _steps.Count : _current; } }

        /// Save-game restore: jump the chain to a step (progress inside a step resets).
        public void ImportStep(int step)
        {
            _current = Mathf.Clamp(step, 0, _steps.Count);
            _done = _current >= _steps.Count;
            RefreshTracker();
        }

        public const int Chapter1Steps = 5;
        public const int Chapter2Steps = 11;

        void Awake()
        {
            I = this;
            _steps.Add(new QuestStep { title = "1장 · 깨어난 박자", objective = "그림자 정화", type = QuestEvent.Kill, goal = 3 });
            _steps.Add(new QuestStep { title = "1장 · 첫 공명", objective = "녹야 공명탑 해방", type = QuestEvent.Tower, param = 0, hasTarget = true });
            _steps.Add(new QuestStep { title = "1장 · 속삭임을 따라", objective = "속삭임 숲 진입", type = QuestEvent.Reach, param = 1, hasTarget = true });
            _steps.Add(new QuestStep { title = "1장 · 숲의 탑", objective = "숲 공명탑 해방", type = QuestEvent.Tower, param = 1, hasTarget = true });
            _steps.Add(new QuestStep { title = "1장 · 무관의 그림자", objective = "아레나의 지배자 처치", type = QuestEvent.Boss, hasTarget = true });
            // ---- chapter 2: follow the echo west, then up the frost
            _steps.Add(new QuestStep { title = "2장 · 메아리를 따라", objective = "메아리 마을 방문", type = QuestEvent.Reach, param = 7, hasTarget = true, target = new Vector3(-206f, 0f, -158f) });
            _steps.Add(new QuestStep { title = "2장 · 잿빛 정화", objective = "황무지의 그림자 정화", type = QuestEvent.Kill, goal = 5, param = WorldRegions.Waste, hasTarget = true, target = new Vector3(-360f, 0f, -80f) });
            _steps.Add(new QuestStep { title = "2장 · 재의 노래", objective = "잿빛 공명탑 해방", type = QuestEvent.Tower, param = 3, hasTarget = true });
            _steps.Add(new QuestStep { title = "2장 · 서리를 넘어", objective = "서리 고원 등정", type = QuestEvent.Reach, param = 5, hasTarget = true, target = new Vector3(-180f, 0f, 448f) });
            _steps.Add(new QuestStep { title = "2장 · 고원의 탑", objective = "서리 공명탑 해방", type = QuestEvent.Tower, param = 2, hasTarget = true });
            _steps.Add(new QuestStep { title = "2장 · 서리의 원혼", objective = "고원의 그림자 토벌", type = QuestEvent.Kill, goal = 4, param = WorldRegions.Frost, hasTarget = true, target = new Vector3(-190f, 0f, 500f) });
            // ---- chapter 3: rifts, the trial altar and the merchant's rumor
            _steps.Add(new QuestStep { title = "3장 · 마을의 소문", objective = "메아리 마을의 상인과 대화", type = QuestEvent.Talk, param = 0, hasTarget = true, target = new Vector3(-223f, 0f, -174f) });
            _steps.Add(new QuestStep { title = "3장 · 침식을 막아라", objective = "침식 균열 정화 (들판에 보랏빛 빛기둥)", type = QuestEvent.Rift, goal = 1 });
            _steps.Add(new QuestStep { title = "3장 · 시련의 제단", objective = "동쪽 평원의 시련의 제단 도달", type = QuestEvent.Reach, param = 9, hasTarget = true, target = new Vector3(165f, 0f, -150f) });
            _steps.Add(new QuestStep { title = "3장 · 다섯 번의 파도", objective = "시련 5웨이브 완주", type = QuestEvent.Arena, hasTarget = true, target = new Vector3(165f, 0f, -150f) });
            _steps.Add(new QuestStep { title = "3장 · 잔향의 끝", objective = "침식 균열 2회 추가 정화", type = QuestEvent.Rift, goal = 2 });
            Describe();
        }

        void Describe()
        {
            string[] desc =
            {
                "노래가 사라진 지 칠십 년. 눈을 뜬 조율사 앞에 그림자들이 다가온다. 몸을 풀며 셋을 정화하자.",
                "녹야 평원 한가운데 서 있는 공명탑에 소리를 되돌리면 지역이 밝혀지고 리스폰 지점이 된다.",
                "숲 쪽에서 파발꾼의 숨소리가 들린다. 속삭임 숲으로 들어가 보자.",
                "숲의 공명탑을 해방하면 첫 장의 노래가 완성된다.",
                "평원 북쪽의 아레나에서 왕관 없는 그림자가 기다린다. 이중 충격파의 두 번째 파동 직전이 패리 타이밍.",
                "서쪽의 메아리 마을에 사람이 남아 있다. 상인과 표석이 있다.",
                "황무지의 그림자를 정화해 잿빛 공명탑으로 가는 길을 연다.",
                "잿빛 공명탑을 해방하면 서리 고원의 길이 열린다.",
                "북서쪽 고원은 춥고 거암의 그림자가 지킨다. 스태미나를 아끼며 오르자.",
                "서리 공명탑을 해방하면 두 번째 장이 끝난다.",
                "고원의 그림자를 토벌해 마을의 근심을 덜자.",
                "상인이 균열의 소문을 전한다. 그와 대화하자.",
                "들판에 보랏빛 빛기둥이 선다. 안의 그림자를 모두 정화하면 균열이 닫힌다.",
                "동쪽 평원의 시련의 제단에 도달하자. 지기가 규칙을 설명한다.",
                "다섯 파도를 버티면 제단이 보답한다. 파도 사이 3.5초의 숨 고르기를 활용하자.",
                "균열을 두 번 더 정화하면 잔향의 세 번째 장이 완성된다.",
            };
            string[] reward =
            {
                "조각소리 · 전투 감각", "탑 해방 · 물약 충전 · 워프 지점", "지역 발견", "1장 완료: 조각소리 300 · 조율기 1 · 공명석 조각 3 · 흐린 잔재 4 · 구이 2", "★5 에코 · 왕관 파편 · 보스 소재",
                "마을 · 상점 · 표석", "지역 소재 · 경험치", "탑 해방 · 워프", "지역 발견", "2장 완료: 조각소리 400 · 조율기 2 · 공명석 2 · 회절 결정 3 · 조림 2", "경험치 · 잔재",
                "상점 · 균열 소문", "균열 보상 · 지역 정화율", "제단 · 시련 해금", "시련 완주 보상 · 증표 3 · 왕관 파편", "3장 완료: 조각소리 500 · 조율기 3 · 공명 결정 1 · 왕관 파편 2 · 증표 5",
            };
            for (int i = 0; i < _steps.Count; i++)
            {
                if (i < desc.Length) _steps[i].description = desc[i];
                if (i < reward.Length) _steps[i].reward = reward[i];
            }
        }

        // ---------------------------------------------------------------- log / tracking (S6)
        public int StepCount { get { return _steps.Count; } }
        public int CurrentIndex { get { return _current; } }
        public bool Done { get { return _done; } }
        public QuestStep Step(int i) { return _steps[Mathf.Clamp(i, 0, _steps.Count - 1)]; }
        /// 2 = done, 1 = active, 0 = locked
        public int StepState(int i) { return i < _current ? 2 : (i == _current && !_done ? 1 : 0); }
        /// Bounty id being tracked instead of the main quest (-1 = main).
        public int TrackedBounty = -1;

        public bool TrackedTarget(out Vector3 pos, out string name, out string objective)
        {
            if (TrackedBounty >= 0)
            {
                var b = BountyBoard.Get(TrackedBounty);
                if (b != null && !b.done) { name = b.Title; objective = b.Objective; pos = b.Target; return b.HasTarget; }
                TrackedBounty = -1;
            }
            var s = Current;
            if (s != null) { name = s.title; objective = s.objective + (s.goal > 1 ? "  (" + s.progress + "/" + s.goal + ")" : ""); pos = s.target; return s.hasTarget; }
            pos = Vector3.zero; name = null; objective = null; return false;
        }

        public void RefreshTracker()
        {
            if (TrackedBounty >= 0)
            {
                var b = BountyBoard.Get(TrackedBounty);
                if (b != null && !b.done) { HUDController.SetQuest(b.Title, b.Objective, b.Target, b.HasTarget); return; }
                TrackedBounty = -1;
            }
            var s = Current;
            if (s == null) { HUDController.SetQuest("3장 완료", "세계에 노래가 돌아왔다 — 균열과 시련은 계속된다", Vector3.zero, false); return; }
            string prog = s.goal > 1 ? "  (" + s.progress + "/" + s.goal + ")" : "";
            HUDController.SetQuest(s.title, s.objective + prog, s.target, s.hasTarget);
        }

        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            // tracker targets are looked up from the scene by name
            SetTarget(1, "Tower_0");
            SetTarget(2, "ForestGate");
            SetTarget(3, "Tower_1");
            SetTarget(4, "BossSpawner");
            SetTarget(7, "Tower_3");
            SetTarget(9, "Tower_2");
            SetTarget(11, "NPC_Merchant");
            SetTarget(13, "ArenaAltar");
            SetTarget(14, "ArenaAltar");
            RefreshTracker();
        }

        void SetTarget(int step, string goName)
        {
            var go = GameObject.Find(goName);
            if (go != null && step < _steps.Count) _steps[step].target = go.transform.position;
        }

        public void Notify(QuestEvent ev, int param = -1)
        {
            var s = Current;
            if (s == null || s.type != ev) return;
            if (s.param >= 0 && param >= 0 && s.param != param) return;
            s.progress++;
            if (s.progress >= s.goal)
            {
                AudioMan.I.Play2D(Sfx.Absorb(), 0.7f, 0.8f);
                HUDController.Toast("목표 달성 — " + s.objective);
                _current++;
                if (SaveSystem.I != null) SaveSystem.I.AutoSave("퀘스트 진행");
                if (_current == Chapter1Steps)
                {
                    // chapter break: the first song is free — a fainter echo answers from the west
                    if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(300);
                    DropTables.QuestChapter(1);
                    if (MusicDirector.I != null) MusicDirector.I.PlayVictory();
                    if (Cutscene.I != null)
                        Cutscene.I.PlayChapterCard("2장 · 메아리를 따라", "첫 노래가 돌아온 밤 — 서쪽에서 희미한 메아리가 응답했다.");
                }
                if (_current == Chapter2Steps)
                {
                    if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(400);
                    DropTables.QuestChapter(2);
                    if (MusicDirector.I != null) MusicDirector.I.PlayVictory();
                    if (Cutscene.I != null)
                        Cutscene.I.PlayChapterCard("3장 · 잔향의 시련", "노래가 돌아온 땅에 침식이 스며든다 — 마을의 상인이 소문을 전한다.");
                }
                if (_current >= _steps.Count)
                {
                    _done = true;
                    HUDController.Victory();
                    if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(500);
                    DropTables.QuestChapter(3);
                    if (Cutscene.I != null)
                        Cutscene.I.PlayChapterCard("잔향 — 데모 완결", "세계의 노래가 겹겹이 돌아왔다. 여운은 계속된다…");
                    GameFlags.Set("demo_done");
                    StartCoroutine(ShowResults());
                }
            }
            RefreshTracker();
        }

        /// Demo completion: once the closing chapter card is gone, show the journey summary.
        System.Collections.IEnumerator ShowResults()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            while (Cutscene.Active || DialogueSystem.Active) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);
            if (!ScreenRouter.IsOpen) ScreenRouter.Push("Results");
        }

    }
}
