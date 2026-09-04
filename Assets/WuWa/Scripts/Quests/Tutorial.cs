using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    public class TutorialCard { public string id, title, body; public bool modal; }

    /// Tutorial cards (design doc 7.7): modal cards pause once on first trigger,
    /// hints slide in bottom-right for 6 s. Flags "tut_<id>" are saved; the codex
    /// replays them. Triggers are polled here or fired from gameplay code.
    public static class Tutorial
    {
        public static readonly TutorialCard[] Cards =
        {
            new TutorialCard { id = "move", title = "이동과 질주", body = "{Move} 이동 · {Jump} 점프 (×2 = 이단 점프) · {Dodge} 홀드 = 질주. 공중에서 {Jump} 홀드 = 활공." },
            new TutorialCard { id = "combat", modal = true, title = "그림자와의 전투", body = "{Attack} 4단 콤보 · 홀드 = 강공격 · {Dodge} 회피. 적의 금색 예고 순간에 {Attack}을 맞추면 패리!" },
            new TutorialCard { id = "skill_ult", title = "공명 스킬과 해방", body = "{Skill} 공명 스킬 (쿨다운) · {Liberation} 공명 해방 (에너지 100)." },
            new TutorialCard { id = "forte", modal = true, title = "회로 가득", body = "회로 게이지가 가득 찼습니다. 지금 {Attack} 홀드 = 강화 강공격 (1.85×)." },
            new TutorialCard { id = "concerto", modal = true, title = "협주 가득 — 변주", body = "협주가 가득 찼을 때 {Swap1}·{Swap2}·{Swap3}로 교대하면 들어오는 캐릭터가 변주 스킬을 쓰고, 나가는 캐릭터의 여운 버프가 걸립니다." },
            new TutorialCard { id = "parry", modal = true, title = "패리", body = "적이 금색으로 빛나는 순간 {Attack}! 패리에 성공하면 협주와 그로기가 크게 오릅니다." },
            new TutorialCard { id = "counter", title = "완벽 회피 · 반격", body = "완벽 회피 직후 2.2초 동안 {Attack} = 반격 (크리 ×1.5)." },
            new TutorialCard { id = "lockon", title = "락온", body = "{LockOn} 락온 · 다시 누르면 다음 대상 · 홀드 = 해제. 락온 중 카메라가 대상을 따라갑니다." },
            new TutorialCard { id = "echo_pickup", modal = true, title = "에코", body = "그림자가 남긴 에코를 얻었습니다. 개체마다 스탯이 다릅니다. {Character} 캐릭터 화면 > 에코에서 장착 — 슬롯 1의 에코 스킬은 {EchoSkill}." },
            new TutorialCard { id = "echo_cost", title = "코스트 상한", body = "에코 코스트 합계는 12를 넘을 수 없습니다. ★5 = 4, ★3 = 3, ★1 = 1." },
            new TutorialCard { id = "weapon", title = "무기", body = "새 무기를 얻었습니다. {Character} 캐릭터 화면 > 무기에서 장착·강화하세요." },
            new TutorialCard { id = "tower", modal = true, title = "공명탑", body = "{Interact} 해방하면 지역이 밝혀지고 워프 지점·리스폰 지점이 됩니다. 물약도 충전됩니다." },
            new TutorialCard { id = "waystone", title = "공명 표석", body = "조율된 표석은 {Map} 지도에서 워프할 수 있습니다." },
            new TutorialCard { id = "grapple", title = "갈고리", body = "{Grapple}로 갈고리 지점에 매달려 이동합니다. 공중에서 {Jump}로 놓습니다." },
            new TutorialCard { id = "glide", title = "활공", body = "공중에서 {Jump} 홀드 = 활공. 스태미나가 다하면 떨어집니다." },
            new TutorialCard { id = "wallrun", title = "벽타기", body = "벽을 향해 달리면 벽을 탑니다. {Jump}로 벽에서 튕겨 나갑니다." },
            new TutorialCard { id = "swim", title = "수영", body = "물속에서는 스태미나가 계속 줄어듭니다. 다 닳기 전에 뭍으로." },
            new TutorialCard { id = "stamina", title = "스태미나 고갈", body = "스태미나가 바닥나면 질주·활공·벽타기가 끊깁니다. 25% 이상 회복될 때까지 기다리세요." },
            new TutorialCard { id = "levelup", title = "레벨 업", body = "캐릭터 레벨이 올랐습니다. Lv 20 · 30 · 40에서 돌파가 필요합니다 — {Character} 캐릭터 화면." },
            new TutorialCard { id = "chest", title = "보물 상자", body = "나무 상자는 이틀 뒤 다시 채워집니다. 은빛·황금은 한 번뿐." },
            new TutorialCard { id = "shop", title = "상점", body = "일일 한정 품목은 게임 내 하루(44분)마다 채워집니다. 판매 탭에서 여분을 팔 수 있습니다." },
            new TutorialCard { id = "rift", modal = true, title = "침식 균열", body = "보랏빛 빛기둥 안의 그림자를 모두 정화하면 균열이 닫히고 조각소리·에코·조율기를 얻습니다. 제한 시간이 있습니다." },
            new TutorialCard { id = "arena", title = "시련의 제단", body = "다섯 파도를 버티면 완주. 제단 밖으로 나가면 실패합니다." },
            new TutorialCard { id = "night", title = "밤", body = "밤에는 반딧불이가 뜨고 균열이 더 자주 열립니다." },
            new TutorialCard { id = "hub", title = "허브 화면", body = "{UI/TabPrev} / {UI/TabNext}로 캐릭터·가방·퀘스트·도감·지도·설정을 오갑니다. {UI/Cancel} 뒤로." },
            new TutorialCard { id = "growth", title = "성장", body = "공명석 투입 → 레벨, 돌파 → 상한 해제, 스킬 강화 → 배율. 무기와 에코도 이 화면에서." },
            new TutorialCard { id = "gather", title = "채집 군락", body = "군락은 게임 내 하루마다 다시 자랍니다. 지역 속성 결정과 잔재를 줍니다." },
            new TutorialCard { id = "bounty_done", title = "현상 완료", body = "현상은 매일 3건이 새로 걸립니다. {Quest} 퀘스트 > 사이드 탭에서 확인·추적하세요." },
        };

        static readonly Queue<TutorialCard> _modalQueue = new Queue<TutorialCard>();
        static float _next;
        static float _combatSince = -1f;
        static Image _hintBg; static Text _hintTitle, _hintBody; static float _hintUntil;
        static Transform _layer;

        public static bool Enabled { get { return SettingsStore.D.tutorials; } }
        public static TutorialCard Get(string id) { foreach (var c in Cards) if (c.id == id) return c; return null; }
        public static bool Seen(string id) { return GameFlags.Has("tut_" + id); }
        public static int SeenCount { get { int n = 0; foreach (var c in Cards) if (Seen(c.id)) n++; return n; } }

        public static void Init(Transform systemLayer)
        {
            _layer = systemLayer;
            _modalQueue.Clear(); _hintUntil = 0f; _combatSince = -1f; _next = 0f;
            _hintBg = UIKit.Img("tutHint", _layer, new Color(0.05f, 0.06f, 0.09f, 0.94f), UIKit.Rounded);
            var rt = _hintBg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-40f, 230f); rt.sizeDelta = new Vector2(460f, 96f);
            var band = UIKit.Img("band", _hintBg.transform, UIKit.Theme.Accent);
            var brt = band.rectTransform; brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 0.5f); brt.anchoredPosition = new Vector2(6f, 0f); brt.sizeDelta = new Vector2(4f, -14f);
            _hintTitle = UIKit.Txt("t", _hintBg.transform, new Vector2(0f, 1f), new Vector2(20f, -10f), new Vector2(430f, 22f), "", 15, UIKit.Theme.Accent, TextAnchor.UpperLeft, true);
            _hintTitle.rectTransform.pivot = new Vector2(0f, 1f);
            _hintBody = UIKit.Txt("b", _hintBg.transform, new Vector2(0f, 1f), new Vector2(20f, -34f), new Vector2(430f, 60f), "", 13, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
            _hintBody.rectTransform.pivot = new Vector2(0f, 1f);
            _hintBg.gameObject.SetActive(false);
        }

        /// Fires a card once (respects the tutorials setting and saved flags).
        public static void Trigger(string id)
        {
            var card = Get(id);
            if (card == null || Seen(id)) return;
            GameFlags.Set("tut_" + id);
            if (!Enabled) return;
            Show(card);
        }

        public static void Show(TutorialCard card)
        {
            if (card.modal) _modalQueue.Enqueue(card);
            else ShowHint(card);
        }

        /// Returning players from older saves skip the whole set instead of a card storm.
        public static void MarkAllSeenIfVeteran(float playSeconds)
        {
            if (SeenCount > 0 || playSeconds < 300f) return;
            foreach (var c in Cards) GameFlags.Set("tut_" + c.id);
        }

        public static string Expand(string body)
        {
            int guard = 0;
            while (guard++ < 40)
            {
                int a = body.IndexOf('{'); if (a < 0) break;
                int b = body.IndexOf('}', a); if (b < 0) break;
                string key = body.Substring(a + 1, b - a - 1);
                string action = key.Contains("/") ? key : "Player/" + key;
                string fallback = key == "Move" ? "WASD" : key;
                string glyph = key == "Move" ? (InputService.GamepadActive ? "좌스틱" : "WASD") : Glyph.Key(action, fallback);
                body = body.Substring(0, a) + glyph + body.Substring(b + 1);
            }
            return body;
        }

        static void ShowHint(TutorialCard card)
        {
            if (_hintBg == null) return;
            _hintTitle.text = "튜토리얼 · " + card.title;
            _hintBody.text = Expand(card.body);
            float h = Mathf.Max(96f, _hintBody.preferredHeight + 48f);
            _hintBg.rectTransform.sizeDelta = new Vector2(460f, h);
            _hintBg.gameObject.SetActive(true);
            _hintUntil = Time.unscaledTime + 6f;
            AudioMan.I.Play2D(Sfx.Swap(), 0.3f, 1.8f);
        }

        // ---------------------------------------------------------------- per frame
        public static void Tick()
        {
            if (_hintBg != null && _hintBg.gameObject.activeSelf && Time.unscaledTime > _hintUntil) _hintBg.gameObject.SetActive(false);
            bool gameplay = !ScreenRouter.IsOpen && !Cutscene.Active && !DialogueSystem.Active && InputService.GameplayActive;
            if (_modalQueue.Count > 0 && gameplay)
            {
                var card = _modalQueue.Dequeue();
                ScreenRouter.Push("Tutorial", card);
                return;
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            if (!Enabled) return;
            var pc = PlayerController.Instance;
            if (pc == null) return;
            var top = ScreenRouter.Top;
            if (top != null)
            {
                if (top.Id == "Shop") Trigger("shop");
                else if (top.Id == "Character") Trigger("growth");
                else if (top.IsHubTab) Trigger("hub");
                return;
            }
            if (Cutscene.Active) return;
            Trigger("move");
            Vector3 pp = pc.transform.position;
            bool aggroNear = false, parryNear = false;
            foreach (var e in EnemyAI.All)
            {
                if (e == null || e.Hp == null || !e.Hp.IsAlive || !e.IsAggro) continue;
                float d = WuWaUtil.Flat(e.transform.position - pp).magnitude;
                if (d < 16f) { aggroNear = true; Codex.NotifySeen(e.kind, e.isBoss, false); }
                if (d < 12f && e.ParryOpen) parryNear = true;
            }
            if (aggroNear) { Trigger("combat"); if (_combatSince < 0f) _combatSince = Time.time; } else _combatSince = -1f;
            if (parryNear) Trigger("parry");
            var team = pc.GetComponent<TeamManager>();
            var m = team != null ? team.Active : null;
            if (m != null && aggroNear)
            {
                if (m.skillCdLeft <= 0.01f && Seen("combat")) Trigger("skill_ult");
                if (m.ForteReady) Trigger("forte");
                if (m.ConcertoReady) Trigger("concerto");
                var lockOn = pc.GetComponent<LockOnSystem>();
                if (_combatSince >= 0f && Time.time - _combatSince > 3f && lockOn != null && lockOn.Target == null) Trigger("lockon");
            }
            if (Time.time - pc.LastPerfectDodge < 0.5f) Trigger("counter");
            if (pc.IsGliding) Trigger("glide");
            if (pc.IsWallRunning) Trigger("wallrun");
            if (pc.IsSwimming) Trigger("swim");
            if (pc.IsGrappling) Trigger("grapple");
            if (pc.StaminaExhausted) Trigger("stamina");
            if (EchoSystem.I != null && EchoSystem.I.Instances.Count > 0) Trigger("echo_pickup");
            if (WeaponSystem.I != null && WeaponSystem.I.Items.Count > 3) Trigger("weapon");
            foreach (var w in Waystone.All) if (w != null && w.Discovered) { Trigger("waystone"); break; }
            if (!Seen("tower"))
                foreach (var t in Object.FindObjectsByType<ResonanceTower>(FindObjectsSortMode.None))
                    if (t != null && !t.Activated && WuWaUtil.Flat(t.transform.position - pp).magnitude < 12f) { Trigger("tower"); break; }
            foreach (var d in MapSystem.Dynamic) if (d.cat == MapCategory.Rift) { Trigger("rift"); GameFlags.Set("seen_rift"); break; }
            if (ArenaTrial.Running) Trigger("arena");
            if (DayNightCycle.IsNight) Trigger("night");
        }
    }

    /// Modal tutorial card (Popup layer, pauses the game).
    public class TutorialScreen : UIScreen
    {
        public override string Id { get { return "Tutorial"; } }
        public override UILayer Layer { get { return UILayer.Popup; } }
        Text _title, _body;
        Button _ok;

        protected override void Build()
        {
            var dim = UIKit.Img("dim", Root, new Color(0f, 0f, 0f, 0.55f), null, true);
            UIKit.Stretch(dim.rectTransform);
            var panel = UIKit.Panel("panel", Root, new Color(0.07f, 0.085f, 0.11f, 0.98f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 340f));
            var band = UIKit.Img("band", panel.transform, UIKit.Theme.Accent);
            var brt = band.rectTransform; brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f); brt.anchoredPosition = Vector2.zero; brt.sizeDelta = new Vector2(0f, 2f);
            UIKit.Txt("tag", panel.transform, new Vector2(0f, 1f), new Vector2(32f, -18f), new Vector2(300f, 22f), "튜토리얼", 14, UIKit.Theme.TextLo, TextAnchor.MiddleLeft).rectTransform.pivot = new Vector2(0f, 1f);
            _title = UIKit.Txt("title", panel.transform, new Vector2(0f, 1f), new Vector2(32f, -42f), new Vector2(700f, 36f), "", 26, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
            _title.rectTransform.pivot = new Vector2(0f, 1f);
            _body = UIKit.Txt("body", panel.transform, new Vector2(0f, 1f), new Vector2(32f, -96f), new Vector2(696f, 160f), "", 18, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
            _body.rectTransform.pivot = new Vector2(0f, 1f);
            _ok = UIKit.Btn("ok", panel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(260f, 48f), "확인", UIKit.Theme.Confirm, () => ScreenRouter.Pop(), 17);
            _ok.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0f);
        }

        public override void OnOpen(object args)
        {
            var card = args as TutorialCard;
            _title.text = card != null ? card.title : "";
            _body.text = card != null ? Tutorial.Expand(card.body) : "";
            _ok.GetComponentInChildren<Text>().text = "확인  " + Glyph.Key("UI/Submit", "Enter");
        }

        public override Selectable DefaultFocus { get { return _ok; } }
    }
}
