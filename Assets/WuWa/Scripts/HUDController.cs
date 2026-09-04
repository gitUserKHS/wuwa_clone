using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WuWa
{
    /// Entire HUD is built from code at runtime: party slots, HP/energy, skill
    /// cooldowns, boss bar, lock-on marker, toasts, damage numbers, fades.
    public class HUDController : MonoBehaviour
    {
        static HUDController _inst;

        TeamManager _team;
        PlayerCombat _combat;
        PlayerController _player;

        Canvas _canvas;
        CanvasGroup _hudGroup;
        static bool _hudVisible = true;
        Font _font;
        Sprite _white, _circle, _ringSprite;

        // party
        readonly Image[] _slotFrames = new Image[3];
        readonly Image[] _slotPortraits = new Image[3];
        readonly Image[] _slotHp = new Image[3];
        readonly Text[] _slotKeys = new Text[3];
        readonly Image[] _slotConcerto = new Image[3];

        // bottom
        Text _nameText;
        Image _hpFill, _hpGhost, _energyFill, _forteFill;
        Text _hpText;
        Text _forteHint;
        Text _counterHint;
        RectTransform _grappleMarker;

        // skills
        Image _echoIcon, _skillIcon, _ultIcon;
        Image _echoCd, _skillCd, _ultCd;
        Text _ultPct;
        Text _skillCdText, _echoCdText;

        // boss / target
        GameObject _bossRoot;
        Text _bossName;
        Image _bossFill, _bossStagger;
        Health _shownEnemy;
        float _shownEnemyUntil;
        Health _lockTarget;
        RectTransform _lockMarker;

        // quest tracker / progress / interact
        Text _questTitle, _questObj, _levelText;
        Image _expFill;
        Vector3 _questTarget;
        bool _questHasTarget;
        Text _interactText;
        Text _helpText;
        Image _objMarker;
        Text _objDist;
        Text _saveIcon;
        Text _combo, _rankLetter, _rankDetail, _rankBonus; RectTransform _rankCard; float _rankUntil, _comboPop; int _comboVal;
        Image _quickIcon, _flaskIcon;
        Text _quickCount, _flaskCount, _quickKey, _flaskKey, _buffLine;
        float _saveUntil;
        float _interactUntil;
        float _progressPoll;

        // misc
        Text _toast;
        float _toastUntil;
        Image _fade;
        Text _victory;
        Text _fps;
        GameObject _helpRoot;
        public static bool ShowFps, HudHidden;
        public static bool ShowQuestTracker = true;
        public static float Scale = 1f;
        public static bool InteractPromptActive { get { return _inst != null && Time.unscaledTime < _inst._interactUntil; } }
        public static void ApplyScale(float s)
        {
            Scale = s;
            if (_inst == null || _inst._canvas == null) return;
            var sc = _inst._canvas.GetComponent<CanvasScaler>();
            if (sc != null) sc.referenceResolution = new Vector2(1920f / s, 1080f / s);
        }
        Text _eventText;
        readonly Dictionary<string, Sprite> _portraitCache = new Dictionary<string, Sprite>();
        bool _helpToggled;
        Text _clock;
        float _clockPoll;
        readonly System.Collections.Generic.Queue<string> _toastQueue = new System.Collections.Generic.Queue<string>();
        float _fpsAccum; int _fpsFrames; float _fpsTimer;

        // stamina
        Image _stamBg, _stamFill;
        float _stamAlpha;
        float _stamFrac = 1f;
        bool _stamExhausted;

        void Awake()
        {
            _inst = this;
            _font = GetFont();
            _white = MakeSprite(Texture2D.whiteTexture);
            _circle = MakeSprite(VFXLibrary.MakeSoftDot());
            _ringSprite = MakeSprite(VFXLibrary.MakeRing());
            BuildCanvas();
            BuildStaminaBar();
        }

        void BuildStaminaBar()
        {
            var bg = new GameObject("stamBg");
            bg.transform.SetParent(_canvas.transform, false);
            var brt = bg.AddComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 132f);
            brt.sizeDelta = new Vector2(300f, 8f);
            _stamBg = bg.AddComponent<Image>();
            _stamBg.sprite = _white;
            _stamBg.color = new Color(0f, 0f, 0f, 0.45f);
            _stamBg.raycastTarget = false;

            var fill = new GameObject("stamFill");
            fill.transform.SetParent(bg.transform, false);
            var frt = fill.AddComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = new Vector2(1f, 1f);
            frt.offsetMax = new Vector2(1f, -1f);
            frt.sizeDelta = new Vector2(298f, -2f);
            _stamFill = fill.AddComponent<Image>();
            _stamFill.sprite = _white;
            _stamFill.color = new Color(1f, 0.9f, 0.45f, 0.95f);
            _stamFill.raycastTarget = false;
        }

        /// Player stamina readout — visible while draining or refilling, fades
        /// out when full; flashes red while exhausted.
        public static void SetStamina(float frac, bool exhausted)
        {
            if (_inst == null) return;
            _inst._stamFrac = Mathf.Clamp01(frac);
            _inst._stamExhausted = exhausted;
        }

        void UpdateStamina()
        {
            if (_stamBg == null) return;
            float target = _stamFrac >= 0.999f ? 0f : 1f;
            _stamAlpha = Mathf.MoveTowards(_stamAlpha, target, Time.unscaledDeltaTime * (target > _stamAlpha ? 8f : 1.6f));
            bool menu = GameDirector.MenuOpen || Cutscene.Active;
            float a = menu ? 0f : _stamAlpha;
            _stamBg.color = new Color(0f, 0f, 0f, 0.45f * a);
            Color c = _stamExhausted
                ? Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(1f, 0.55f, 0.4f), Mathf.PingPong(Time.unscaledTime * 3f, 1f))
                : new Color(1f, 0.9f, 0.45f);
            c.a = 0.95f * a;
            _stamFill.color = c;
            var rt = _stamFill.rectTransform;
            rt.sizeDelta = new Vector2(298f * _stamFrac, -2f);
        }

        void Start()
        {
            _player = Object.FindAnyObjectByType<PlayerController>();
            if (_player != null)
            {
                _team = _player.GetComponent<TeamManager>();
                _combat = _player.GetComponent<PlayerCombat>();
                if (_team != null) _team.OnTeamChanged += RefreshParty;
            }
            RefreshParty();
            DamageNumbers.Init(_canvas.transform as RectTransform, _font);
            InputService.SchemeChanged += RefreshHelp;
        }

        void OnDestroy() { InputService.SchemeChanged -= RefreshHelp; }

        public static HUDController I { get { return _inst; } }

        /// Screens hide the HUD through a CanvasGroup (F11 toggles the canvas itself).
        public static void SetHudVisible(bool on)
        {
            _hudVisible = on;
            if (_inst != null && _inst._hudGroup != null) _inst._hudGroup.alpha = on ? 1f : 0f;
        }

        public static void SetCombo(int hits)
        {
            if (_inst == null || _inst._combo == null) return;
            if (hits > _inst._comboVal) _inst._comboPop = 1f;
            _inst._comboVal = hits;
            _inst._combo.text = hits >= 2 ? hits + " HIT" : "";
        }

        public static void ShowRankCard(string rank, string detail, string bonus)
        {
            if (_inst == null || _inst._rankCard == null) return;
            _inst._rankLetter.text = rank;
            _inst._rankLetter.color = rank == "S" ? new Color(1f, 0.85f, 0.35f) : rank == "A" ? new Color(0.75f, 0.9f, 1f) : rank == "B" ? new Color(0.7f, 1f, 0.75f) : new Color(0.85f, 0.85f, 0.85f);
            string title = rank == "S" ? "완벽한 악장" : rank == "A" ? "훌륭한 연주" : rank == "B" ? "무난한 연주" : rank == "C" ? "거친 박자" : "흐트러진 박자";
            _inst._rankDetail.text = "전투 평가  " + title + "\n" + detail;
            _inst._rankBonus.text = bonus ?? "";
            _inst._rankUntil = Time.unscaledTime + 3f;
            _inst._rankCard.gameObject.SetActive(true);
            _inst._rankCard.localScale = Vector3.one * 1.15f;
        }

        public static bool RankCardVisible { get { return _inst != null && _inst._rankCard != null && _inst._rankCard.gameObject.activeSelf; } }

        void TickRankCombo()
        {
            if (_rankCard != null && _rankCard.gameObject.activeSelf)
            {
                _rankCard.localScale = Vector3.Lerp(_rankCard.localScale, Vector3.one, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
                if (Time.unscaledTime > _rankUntil) _rankCard.gameObject.SetActive(false);
            }
            if (_combo != null)
            {
                _comboPop = Mathf.Max(0f, _comboPop - Time.unscaledDeltaTime * 4f);
                _combo.transform.localScale = Vector3.one * (1f + _comboPop * 0.25f);
            }
        }

        public static void SaveIndicator()
        {
            if (_inst == null || SaveSystem.NoticeMode == 2) return;
            _inst._saveUntil = Time.unscaledTime + 1.4f;
        }

        static string HelpText()
        {
            string move = InputService.GamepadActive ? "좌스틱" : "WASD";
            return "이동 " + move + " · 회피 " + Glyph.Key("Player/Dodge", "Shift") + " (홀드=질주)\n" +
                   "점프 " + Glyph.Key("Player/Jump", "Space") + " ×2 · 공중 홀드=활공 · 벽으로 달리면 벽타기\n" +
                   "수영: 스태미나 소모 · " + Glyph.Key("Player/Sprint", "Ctrl") + " 잠수/하강 · " + Glyph.Key("Player/Jump", "Space") + " 상승 · 홀드 " + Glyph.Key("Player/Dodge", "Shift") + " 대시 (물속 산소 무제한)\n" +
                   "공격 " + Glyph.Key("Player/Attack", "좌클릭") + " (홀드=강공격 · 회로 가득 시 강화)\n" +
                   "공중 공격=낙하 강타 · 회피 직후 공격=대시 공격\n" +
                   Glyph.Key("Player/Skill", "E") + " 스킬 · " + Glyph.Key("Player/Liberation", "R") + " 해방 · " + Glyph.Key("Player/EchoSkill", "Q") + " 에코 · " + Glyph.Key("Player/Grapple", "T") + " 갈고리\n" +
                   "금색 예고 때 공격 = 패리! · 완벽회피 후 공격 = 반격\n" +
                   Glyph.Key("Player/Swap1", "1") + "/" + Glyph.Key("Player/Swap2", "2") + "/" + Glyph.Key("Player/Swap3", "3") + " 교대 (협주 가득 = 변주 스킬) · " + Glyph.Key("Player/LockOn", "Tab") + " 락온\n" +
                   Glyph.Key("Player/Character", "C") + " 캐릭터 · " + Glyph.Key("Player/Bag", "B") + " 가방 · " + Glyph.Key("Player/Map", "M") + " 지도 · " + Glyph.Key("System/Pause", "Esc") + " 일시정지 · " + Glyph.Key("System/Help", "F1") + " 도움말";
        }

        public static void RefreshHelp()
        {
            if (_inst == null || _inst._helpText == null) return;
            _inst._helpText.text = HelpText();
        }

        void TickSaveIcon()
        {
            if (_saveIcon == null) return;
            bool on = Time.unscaledTime < _saveUntil;
            if (_saveIcon.enabled != on) _saveIcon.enabled = on;
            if (on) _saveIcon.text = "저장 중" + new string('.', 1 + (int)(Time.unscaledTime * 4f) % 3);
        }

        void UpdateObjectiveMarker(float d)
        {
            if (_objMarker == null) return;
            bool show = ShowQuestTracker && d > 8f && !Cutscene.Active && _hudVisible;
            if (_objMarker.gameObject.activeSelf != show) _objMarker.gameObject.SetActive(show);
            if (!show) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 objPos; string objName;
            if (!MapSystem.Objective(out objPos, out objName)) objPos = _questTarget;
            Vector3 sp = cam.WorldToScreenPoint(objPos + Vector3.up * 1.6f);
            bool behind = sp.z < 0f;
            if (behind) { sp.x = Screen.width - sp.x; sp.y = 90f; }
            float mx = 80f, myLo = 90f, myHi = Screen.height - 150f;
            bool edge = behind || sp.x < mx || sp.x > Screen.width - mx || sp.y < myLo || sp.y > myHi;
            Vector2 pos = new Vector2(Mathf.Clamp(sp.x, mx, Screen.width - mx), Mathf.Clamp(sp.y, myLo, myHi));
            _objMarker.rectTransform.position = new Vector3(pos.x, pos.y, 0f);
            _objMarker.color = edge ? new Color(1f, 0.8f, 0.35f, 0.7f) : new Color(1f, 0.92f, 0.55f, 0.95f);
            _objDist.text = Mathf.RoundToInt(d) + "m";
        }

        static Font GetFont()
        {
            string[] names = { "Malgun Gothic", "malgun", "Segoe UI", "Arial" };
            foreach (var n in names)
            {
                try
                {
                    var f = Font.CreateDynamicFontFromOSFont(n, 22);
                    if (f != null) return f;
                }
                catch { }
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        static Sprite MakeSprite(Texture2D t)
        {
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        }

        Sprite LoadSprite(string path, Sprite fallback)
        {
            var s = Resources.Load<Sprite>(path);
            if (s != null) return s;
            var t = Resources.Load<Texture2D>(path);
            if (t != null) return MakeSprite(t);
            return fallback;
        }

        // ================================================================ build
        void BuildCanvas()
        {
            var go = new GameObject("HUDCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            _hudGroup = go.AddComponent<CanvasGroup>();
            _hudGroup.alpha = _hudVisible ? 1f : 0f;

            BuildPartySlots();
            BuildBottomBars();
            BuildSkillIcons();
            BuildBossBar();
            BuildMisc();
        }

        RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        Image NewImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Sprite sprite, Color color)
        {
            var rt = NewRect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        Text NewText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, string text, int fontSize, Color color, TextAnchor align)
        {
            var rt = NewRect(name, parent, anchorMin, anchorMax, pivot, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var ol = rt.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0f, 0f, 0f, 0.75f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        void BuildPartySlots()
        {
            for (int i = 0; i < 3; i++)
            {
                float y = 60f - i * 110f;
                var frame = NewImage("slot" + i, _canvas.transform,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-28f, y), new Vector2(92f, 92f), _circle, new Color(0.08f, 0.09f, 0.13f, 0.82f));
                _slotFrames[i] = frame;

                _slotPortraits[i] = NewImage("portrait", frame.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(78f, 78f), _circle, Color.white);
                _slotPortraits[i].preserveAspect = true;

                var hpBg = NewImage("hpbg", frame.transform,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -4f), new Vector2(72f, 7f), _white, new Color(0f, 0f, 0f, 0.7f));
                _slotHp[i] = NewImage("hp", hpBg.transform,
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(72f, 0f), _white, new Color(0.4f, 1f, 0.55f, 0.95f));

                _slotKeys[i] = NewText("key", frame.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                    new Vector2(8f, -6f), new Vector2(30f, 24f), (i + 1).ToString(), 20, Color.white, TextAnchor.MiddleCenter);

                // concerto (협주 에너지) radial ring around the slot
                var ring = NewImage("concerto", frame.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(104f, 104f), _ringSprite, new Color(1f, 0.9f, 0.5f, 0.85f));
                ring.type = Image.Type.Filled;
                ring.fillMethod = Image.FillMethod.Radial360;
                ring.fillOrigin = (int)Image.Origin360.Top;
                ring.fillClockwise = true;
                ring.fillAmount = 0f;
                ring.raycastTarget = false;
                _slotConcerto[i] = ring;
            }
        }

        void BuildBottomBars()
        {
            _nameText = NewText("charName", _canvas.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-260f, 104f), new Vector2(300f, 30f), "—", 24, Color.white, TextAnchor.LowerLeft);

            // Forte Circuit gauge (공명 회로) sits above the HP bar like in WuWa
            var forteBg = NewImage("forteBg", _canvas.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 90f), new Vector2(560f, 9f), _white, new Color(0f, 0f, 0f, 0.55f));
            _forteFill = NewImage("forteFill", forteBg.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(1f, 0f), new Vector2(558f, -2f), _white, new Color(1f, 0.78f, 0.25f, 0.95f));
            _forteHint = NewText("forteHint", forteBg.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(8f, 0f), new Vector2(220f, 22f), "", 15, new Color(1f, 0.85f, 0.4f, 1f), TextAnchor.MiddleLeft);

            var hpBg = NewImage("hpBg", _canvas.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 66f), new Vector2(560f, 20f), _white, new Color(0f, 0f, 0f, 0.62f));
            _hpGhost = NewImage("hpGhost", hpBg.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(2f, 0f), new Vector2(556f, -4f), _white, new Color(1f, 0.5f, 0.3f, 0.8f));
            _hpFill = NewImage("hpFill", hpBg.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(2f, 0f), new Vector2(556f, -4f), _white, new Color(0.55f, 1f, 0.65f, 1f));
            _hpText = NewText("hpText", hpBg.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), new Vector2(500f, 22f), "", 15, new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleCenter);

            var enBg = NewImage("energyBg", _canvas.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 50f), new Vector2(560f, 8f), _white, new Color(0f, 0f, 0f, 0.55f));
            _energyFill = NewImage("energyFill", enBg.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(1f, 0f), new Vector2(558f, -2f), _white, new Color(1f, 0.95f, 0.6f, 1f));
        }

        Image BuildSkillIcon(string label, Vector2 pos, float size, out Image cdOverlay, Sprite iconSprite)
        {
            var frame = NewImage("sk_" + label, _canvas.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                pos, new Vector2(size, size), _circle, new Color(0.08f, 0.09f, 0.13f, 0.85f));

            var icon = NewImage("icon", frame.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(size - 14f, size - 14f), iconSprite, Color.white);
            icon.preserveAspect = true;

            cdOverlay = NewImage("cd", frame.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(size - 6f, size - 6f), _circle, new Color(0f, 0f, 0f, 0.72f));
            cdOverlay.type = Image.Type.Filled;
            cdOverlay.fillMethod = Image.FillMethod.Radial360;
            cdOverlay.fillOrigin = (int)Image.Origin360.Top;
            cdOverlay.fillClockwise = false;
            cdOverlay.fillAmount = 0f;

            NewText("label", frame.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 4f), new Vector2(40f, 20f), label, 15, new Color(1f, 1f, 1f, 0.9f), TextAnchor.MiddleCenter);
            return icon;
        }

        void BuildSkillIcons()
        {
            var echoSprite = LoadSprite("UI/icon_echo", _circle);
            var skillSprite = LoadSprite("UI/icon_skill", _circle);
            var ultSprite = LoadSprite("UI/icon_ult", _circle);

            _echoIcon = BuildSkillIcon("Q", new Vector2(-262f, 40f), 82f, out _echoCd, echoSprite);
            _skillIcon = BuildSkillIcon("E", new Vector2(-168f, 40f), 92f, out _skillCd, skillSprite);
            _ultIcon = BuildSkillIcon("R", new Vector2(-56f, 40f), 116f, out _ultCd, ultSprite);
            _ultPct = NewText("ultPct", _ultIcon.transform.parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 14f), new Vector2(90f, 24f), "", 18, new Color(1f, 0.95f, 0.6f, 1f), TextAnchor.MiddleCenter);

            // readable cooldown: darker sweep + big remaining-seconds number
            _skillCd.color = new Color(0f, 0f, 0f, 0.78f);
            _echoCd.color = new Color(0f, 0f, 0f, 0.78f);
            _ultCd.color = new Color(0f, 0f, 0f, 0.66f);
            _skillCdText = MakeCdText(_skillIcon.transform, 30);
            _echoCdText = MakeCdText(_echoIcon.transform, 26);
            BuildItemSlots();
        }

        void BuildItemSlots()
        {
            _flaskIcon = BuildItemSlot(new Vector2(-436f, 40f), out _flaskCount, out _flaskKey, MapIcons.Get("flask"));
            _quickIcon = BuildItemSlot(new Vector2(-362f, 40f), out _quickCount, out _quickKey, MapIcons.Get("food"));
            _buffLine = NewText("buffs", _canvas.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-40f, 168f), new Vector2(620f, 22f), "", 14, new Color(0.75f, 1f, 0.8f, 0.95f), TextAnchor.LowerRight);
            var o = _buffLine.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0f, 0f, 0f, 0.8f);
            RefreshItemSlots();
        }

        Image BuildItemSlot(Vector2 pos, out Text count, out Text key, Sprite sprite)
        {
            var frame = NewImage("slot", _canvas.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                pos, new Vector2(62f, 62f), _circle, new Color(0.08f, 0.09f, 0.13f, 0.85f));
            var icon = NewImage("icon", frame.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 4f), new Vector2(34f, 34f), sprite, Color.white);
            icon.preserveAspect = true;
            count = NewText("count", frame.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-4f, 2f), new Vector2(50f, 18f), "", 13, new Color(1f, 1f, 1f, 0.95f), TextAnchor.LowerRight);
            count.fontStyle = FontStyle.Bold;
            key = NewText("key", frame.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -8f), new Vector2(60f, 18f), "", 12, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);
            return icon;
        }

        void RefreshItemSlots()
        {
            if (_quickIcon == null) return;
            var q = ItemDB.Get(Inventory.QuickSlot);
            int qc = q != null ? Inventory.Count(q.id) : 0;
            _quickIcon.sprite = MapIcons.Get(q != null ? q.icon : "food");
            _quickIcon.color = q != null && qc > 0 ? q.Tint : new Color(1f, 1f, 1f, 0.3f);
            _quickCount.text = q != null ? qc.ToString() : "";
            _quickKey.text = Glyph.Key("Player/QuickItem", "Z");
            _flaskIcon.color = Inventory.FlaskCharges > 0 ? new Color(0.6f, 0.95f, 1f) : new Color(1f, 1f, 1f, 0.3f);
            _flaskCount.text = Inventory.FlaskCharges + "/" + Inventory.FlaskMax;
            _flaskKey.text = Glyph.Key("Player/Flask", "X");
            _buffLine.text = BuffSystem.HudLine();
        }

        Text MakeCdText(Transform iconParent, int size)
        {
            var t = NewText("cd", iconParent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(110f, 40f), "", size, new Color(1f, 0.97f, 0.85f, 1f), TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.9f);
            o.effectDistance = new Vector2(1.6f, -1.6f);
            t.raycastTarget = false;
            return t;
        }

        void BuildBossBar()
        {
            var root = NewRect("bossBar", _canvas.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f), new Vector2(760f, 60f));
            _bossRoot = root.gameObject;

            _bossName = NewText("name", root,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(700f, 26f), "???", 22, new Color(1f, 0.92f, 0.75f, 1f), TextAnchor.MiddleCenter);

            var bg = NewImage("bg", root,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -30f), new Vector2(700f, 16f), _white, new Color(0f, 0f, 0f, 0.65f));
            _bossFill = NewImage("fill", bg.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(2f, 0f), new Vector2(696f, -4f), _white, new Color(1f, 0.35f, 0.3f, 1f));

            var sb = NewImage("staggerBg", root,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -48f), new Vector2(700f, 6f), _white, new Color(0f, 0f, 0f, 0.5f));
            _bossStagger = NewImage("stagger", sb.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(1f, 0f), new Vector2(698f, -2f), _white, new Color(1f, 0.9f, 0.45f, 1f));

            _bossRoot.SetActive(false);
        }

        void BuildMisc()
        {
            // lock-on marker
            var marker = NewImage("lockMarker", _canvas.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(70f, 70f), _ringSprite, new Color(1f, 0.65f, 0.3f, 0.95f));
            _lockMarker = marker.rectTransform;
            _lockMarker.gameObject.SetActive(false);

            _toast = NewText("toast", _canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 170f), new Vector2(900f, 40f), "", 28, new Color(1f, 0.95f, 0.8f, 1f), TextAnchor.MiddleCenter);

            _counterHint = NewText("counterHint", _canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -76f), new Vector2(400f, 30f), "", 22, new Color(1f, 0.85f, 0.35f, 1f), TextAnchor.MiddleCenter);

            var gm = NewImage("grappleMarker", _canvas.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(52f, 52f), _ringSprite, new Color(0.5f, 1f, 0.85f, 0.95f));
            _grappleMarker = gm.rectTransform;
            NewText("key", gm.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(40f, 24f), "T", 19, Color.white, TextAnchor.MiddleCenter);
            _grappleMarker.gameObject.SetActive(false);

            _victory = NewText("victory", _canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 60f), new Vector2(1200f, 90f), "", 64, new Color(1f, 0.9f, 0.5f, 1f), TextAnchor.MiddleCenter);

            // combo counter (right of centre) + post-combat rank card (design doc 7.9)
            _combo = NewText("combo", _canvas.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-150f, 200f), new Vector2(360f, 60f), "", 40, new Color(1f, 0.85f, 0.4f, 1f), TextAnchor.MiddleRight);
            _combo.fontStyle = FontStyle.Bold;
            var cardBg = NewImage("rankCard", _canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 250f), new Vector2(540f, 120f), _white, new Color(0.03f, 0.04f, 0.06f, 0.82f));
            _rankCard = cardBg.rectTransform;
            _rankLetter = NewText("letter", cardBg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(24f, 0f), new Vector2(110f, 110f), "", 84, new Color(1f, 0.85f, 0.4f, 1f), TextAnchor.MiddleCenter);
            _rankLetter.fontStyle = FontStyle.Bold;
            _rankDetail = NewText("detail", cardBg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(150f, 16f), new Vector2(380f, 48f), "", 16, new Color(1f, 0.97f, 0.9f, 1f), TextAnchor.MiddleLeft);
            _rankBonus = NewText("bonus", cardBg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(150f, -28f), new Vector2(380f, 26f), "", 15, new Color(0.7f, 0.88f, 1f, 1f), TextAnchor.MiddleLeft);
            _rankCard.gameObject.SetActive(false);

            _fps = NewText("fps", _canvas.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(14f, -12f), new Vector2(300f, 24f), "", 15, new Color(1f, 1f, 1f, 0.55f), TextAnchor.UpperLeft);

            // quest tracker (top-right)
            _questTitle = NewText("questTitle", _canvas.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-18f, -14f), new Vector2(430f, 26f), "", 17, new Color(1f, 0.9f, 0.6f, 1f), TextAnchor.UpperRight);
            _questObj = NewText("questObj", _canvas.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-18f, -40f), new Vector2(430f, 24f), "", 15, new Color(1f, 1f, 1f, 0.85f), TextAnchor.UpperRight);

            // party level + shards + exp
            _levelText = NewText("level", _canvas.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-18f, -70f), new Vector2(430f, 22f), "", 14, new Color(1f, 1f, 1f, 0.75f), TextAnchor.UpperRight);
            var expBg = NewImage("expBg", _canvas.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-18f, -94f), new Vector2(200f, 5f), _white, new Color(0f, 0f, 0f, 0.55f));
            _expFill = NewImage("expFill", expBg.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(200f, 0f), _white, new Color(0.65f, 0.9f, 1f, 0.9f));

            // interact prompt (center-low)
            _interactText = NewText("interact", _canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -130f), new Vector2(500f, 34f), "", 24, new Color(1f, 0.92f, 0.6f, 1f), TextAnchor.MiddleCenter);

            BuildHelp();

            // objective marker (diamond + distance) — screen-clamped when off-screen
            _objMarker = NewImage("objMarker", _canvas.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(18f, 18f), _white, new Color(1f, 0.92f, 0.55f, 0.95f));
            _objMarker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _objMarker.raycastTarget = false;
            _objMarker.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
            _objDist = NewText("dist", _objMarker.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -6f), new Vector2(120f, 22f), "", 14, new Color(1f, 0.95f, 0.8f, 0.95f), TextAnchor.UpperCenter);
            _objDist.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            _objDist.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            _objMarker.gameObject.SetActive(false);

            _saveIcon = NewText("saveIcon", _canvas.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-18f, -108f), new Vector2(200f, 22f), "", 14, new Color(0.8f, 0.9f, 1f, 0.85f), TextAnchor.MiddleRight);
            _saveIcon.enabled = false;

            _clock = NewText("clock", _canvas.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -282f), new Vector2(220f, 22f), "", 15, new Color(1f, 1f, 1f, 0.72f), TextAnchor.UpperLeft);
            _eventText = NewText("eventLine", _canvas.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), new Vector2(820f, 30f), "", 20, new Color(1f, 0.85f, 0.45f, 1f), TextAnchor.UpperCenter);

            _fade = NewImage("fade", _canvas.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, _white, new Color(0f, 0f, 0f, 0f));
            _fade.raycastTarget = false;
        }

        void BuildHelp()
        {
            var root = NewRect("help", _canvas.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(14f, 14f), new Vector2(470f, 330f));
            _helpRoot = root.gameObject;
            var bg = NewImage("bg", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, _white, new Color(0f, 0f, 0f, 0.45f));
            bg.raycastTarget = false;
            string help = HelpText();
            _helpText = NewText("txt", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(14f, 0f), new Vector2(-20f, -16f), help, 17, new Color(1f, 1f, 1f, 0.92f), TextAnchor.MiddleLeft);
        }

        // ================================================================ update
        void Update()
        {
            UpdateStamina();
            TickSaveIcon();
            if (_hudGroup != null)
            {
                // screens (router) and cutscenes both hide the HUD; F11 toggles the canvas itself
                float a = _hudVisible && !Cutscene.Active ? 1f : 0f;
                if (_hudGroup.alpha != a) _hudGroup.alpha = a;
            }
            if (InputService.HudTogglePressed) { HudHidden = !HudHidden; _canvas.enabled = !HudHidden; }
            if (InputService.HelpPressed && _helpRoot != null)
            {
                _helpToggled = true;
                _helpRoot.SetActive(!_helpRoot.activeSelf);
            }
            // the control sheet folds away by itself once the player has had a look (F1 brings it back)
            if (_helpRoot != null && _helpRoot.activeSelf && !_helpToggled && Time.unscaledTime > 26f)
                _helpRoot.SetActive(false);
            _clockPoll -= Time.unscaledDeltaTime;
            if (_clockPoll <= 0f && _clock != null)
            {
                _clockPoll = 1f;
                bool showClock = DayNightCycle.I != null && MapSystem.MinimapEnabled && !GameDirector.MenuOpen && !Cutscene.Active;
                _clock.text = showClock ? (DayNightCycle.IsNight ? "☽  " : "☀  ") + DayNightCycle.I.TimeString : "";
            }
            if (_toastQueue.Count > 0 && Time.unscaledTime >= _toastUntil - 1.2f)
            {
                _toast.text = _toastQueue.Dequeue();
                _toastUntil = Time.unscaledTime + 2.2f;
            }

            UpdateBars();
            UpdateSkills();
            UpdateBoss();
            UpdateLockMarker();
            UpdateToastFps();
            UpdateWuWaExtras();
            UpdateQuestAndProgress();
        }

        void UpdateQuestAndProgress()
        {
            if (_questTitle.enabled != ShowQuestTracker) { _questTitle.enabled = ShowQuestTracker; _questObj.enabled = ShowQuestTracker; }
            if (_questHasTarget && _player != null && _questObj.text.Length > 0)
            {
                float d = WuWaUtil.Flat(_questTarget - _player.transform.position).magnitude;
                int cut = _questObj.text.LastIndexOf("  ·  ");
                string baseText = cut > 0 ? _questObj.text.Substring(0, cut) : _questObj.text;
                _questObj.text = baseText + "  ·  " + Mathf.RoundToInt(d) + "m";
                UpdateObjectiveMarker(d);
            }
            else if (_objMarker != null && _objMarker.gameObject.activeSelf) _objMarker.gameObject.SetActive(false);

            if (_interactText.text.Length > 0 && Time.unscaledTime > _interactUntil)
                _interactText.text = "";

            _progressPoll -= Time.unscaledDeltaTime;
            if (_progressPoll <= 0f)
            {
                _progressPoll = 0.3f;
                RefreshItemSlots();
                var ps = ProgressSystem.I;
                if (ps != null && _levelText != null)
                {
                    var cp = ps.Of(_team != null ? _team.ActiveIndex : 0);
                    string who = _team != null && _team.Active != null ? _team.Active.charName : "파티";
                    _levelText.text = who + " Lv " + cp.level + (cp.ascension > 0 ? " · 돌파 " + Growth.AscensionNames[cp.ascension] : "") + "   ·   조각소리 " + ps.Shards;
                    var sz = _expFill.rectTransform.sizeDelta;
                    sz.x = 200f * Mathf.Clamp01(cp.exp / Growth.ExpNeed(cp.level));
                    _expFill.rectTransform.sizeDelta = sz;
                }
            }
        }

        public static void SetQuest(string title, string objective, Vector3 target, bool hasTarget)
        {
            if (_inst == null) return;
            _inst._questTitle.text = title;
            _inst._questObj.text = objective;
            _inst._questTarget = target;
            _inst._questHasTarget = hasTarget;
        }

        public static void SetInteractPrompt(string text)
        {
            if (_inst == null) return;
            if (string.IsNullOrEmpty(text)) { _inst._interactText.text = ""; return; }
            _inst._interactText.text = text;
            _inst._interactUntil = Time.unscaledTime + 0.3f;
        }

        void UpdateWuWaExtras()
        {
            var m = _team != null ? _team.Active : null;
            if (m != null && _forteFill != null)
            {
                float f = Mathf.Clamp01(m.forte / m.forteMax);
                var fsz = _forteFill.rectTransform.sizeDelta; fsz.x = 558f * f; _forteFill.rectTransform.sizeDelta = fsz;
                bool full = m.ForteReady;
                float pulse = full ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 7f) : 0.95f;
                _forteFill.color = full ? new Color(1f, 0.85f, 0.3f, pulse) : new Color(1f, 0.72f, 0.22f, 0.9f);
                _forteHint.text = full ? "회로 MAX!" : "";
            }

            if (_team != null)
            {
                for (int i = 0; i < 3 && i < _team.members.Length; i++)
                {
                    var mem = _team.members[i];
                    if (mem == null || _slotConcerto[i] == null) continue;
                    float c = Mathf.Clamp01(mem.concerto / mem.concertoMax);
                    _slotConcerto[i].fillAmount = c;
                    bool full = mem.ConcertoReady;
                    float pulse = full ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f + i) : 0.75f;
                    _slotConcerto[i].color = full ? new Color(1f, 0.95f, 0.55f, pulse) : new Color(0.7f, 0.85f, 1f, 0.7f);
                }
            }

            if (_counterHint != null)
                _counterHint.text = (_combat != null && _combat.CounterReady) ? "◈ 반격 기회 ◈" : "";

            // grapple prompt marker
            bool showG = false;
            if (_player != null && _player.GrappleCandidate != null && CamCache.Main != null && !GameDirector.CursorFree)
            {
                Vector3 sp = CamCache.Main.WorldToScreenPoint(_player.GrappleCandidate.transform.position);
                if (sp.z > 0f)
                {
                    _grappleMarker.position = new Vector3(sp.x, sp.y, 0f);
                    _grappleMarker.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.1f);
                    showG = true;
                }
            }
            if (_grappleMarker != null && _grappleMarker.gameObject.activeSelf != showG)
                _grappleMarker.gameObject.SetActive(showG);
        }

        void UpdateBars()
        {
            var m = _team != null ? _team.Active : null;
            if (m == null) return;
            float frac = Mathf.Clamp01(m.hp / m.maxHp);
            var sz = _hpFill.rectTransform.sizeDelta; sz.x = 556f * frac; _hpFill.rectTransform.sizeDelta = sz;
            var gsz = _hpGhost.rectTransform.sizeDelta;
            gsz.x = Mathf.MoveTowards(gsz.x, 556f * frac, 220f * Time.deltaTime);
            _hpGhost.rectTransform.sizeDelta = gsz;
            _hpText.text = Mathf.CeilToInt(m.hp) + " / " + Mathf.CeilToInt(m.maxHp);

            float efrac = Mathf.Clamp01(m.energy / m.ultEnergyMax);
            var esz = _energyFill.rectTransform.sizeDelta; esz.x = 558f * efrac; _energyFill.rectTransform.sizeDelta = esz;
            _energyFill.color = m.UltReady ? new Color(1f, 0.8f, 0.25f, 1f) : new Color(1f, 0.95f, 0.6f, 0.85f);
        }

        void UpdateSkills()
        {
            var m = _team != null ? _team.Active : null;
            if (m == null || _combat == null) return;
            _skillCd.fillAmount = m.skillCooldown > 0f ? Mathf.Clamp01(m.skillCdLeft / m.skillCooldown) : 0f;
            _echoCd.fillAmount = Mathf.Clamp01(_combat.EchoCdLeft / _combat.EchoCooldown);
            float e = Mathf.Clamp01(m.energy / m.ultEnergyMax);
            _ultCd.fillAmount = 1f - e;
            _ultPct.text = m.UltReady ? "READY" : Mathf.FloorToInt(e * 100f) + "%";
            _ultIcon.color = m.UltReady ? Color.white : new Color(0.75f, 0.75f, 0.75f, 1f);

            _skillCdText.text = FormatCd(m.skillCdLeft);
            _echoCdText.text = FormatCd(_combat.EchoCdLeft);
            _skillIcon.color = m.skillCdLeft > 0.05f ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;
            _echoIcon.color = _combat.EchoCdLeft > 0.05f ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;
        }

        static string FormatCd(float cd)
        {
            if (cd <= 0.05f) return "";
            return cd < 3f ? cd.ToString("F1") : Mathf.CeilToInt(cd).ToString();
        }

        void UpdateBoss()
        {
            Health show = null;
            if (_lockTarget != null && _lockTarget.IsAlive) show = _lockTarget;
            else if (_shownEnemy != null && _shownEnemy.IsAlive && Time.unscaledTime < _shownEnemyUntil) show = _shownEnemy;

            if (show == null) { if (_bossRoot.activeSelf) _bossRoot.SetActive(false); return; }
            if (!_bossRoot.activeSelf) _bossRoot.SetActive(true);
            _bossName.text = show.displayName;
            float frac = Mathf.Clamp01(show.hp / show.maxHp);
            var sz = _bossFill.rectTransform.sizeDelta; sz.x = 696f * frac; _bossFill.rectTransform.sizeDelta = sz;
            float sfrac = Mathf.Clamp01(show.stagger / show.maxStagger);
            var ssz = _bossStagger.rectTransform.sizeDelta; ssz.x = 698f * sfrac; _bossStagger.rectTransform.sizeDelta = ssz;
        }

        void UpdateLockMarker()
        {
            var lockOn = _player != null ? _player.LockOn : null;
            bool show = lockOn != null && lockOn.Target != null && CamCache.Main != null;
            if (show)
            {
                Vector3 sp = CamCache.Main.WorldToScreenPoint(lockOn.Target.position + Vector3.up * 1.4f);
                show = sp.z > 0f;
                if (show)
                {
                    _lockMarker.position = new Vector3(sp.x, sp.y, 0f);
                    float pulse = 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.08f;
                    _lockMarker.localScale = Vector3.one * pulse;
                }
            }
            if (_lockMarker.gameObject.activeSelf != show) _lockMarker.gameObject.SetActive(show);
        }

        void UpdateToastFps()
        {
            if (_toast.text.Length > 0 && Time.unscaledTime > _toastUntil) _toast.text = "";
            TickRankCombo();
            _fpsAccum += Time.unscaledDeltaTime; _fpsFrames++; _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer > 0.5f)
            {
                _fps.text = ShowFps ? string.Format("명조 클론 데모 · {0:0} FPS", _fpsFrames / _fpsAccum) : "";
                _fpsAccum = 0f; _fpsFrames = 0; _fpsTimer = 0f;
            }
        }

        void RefreshParty()
        {
            if (_team == null) return;
            for (int i = 0; i < 3; i++)
            {
                bool has = i < _team.members.Length && _team.members[i] != null;
                _slotFrames[i].gameObject.SetActive(has);
                if (!has) continue;
                var m = _team.members[i];
                bool active = i == _team.ActiveIndex;
                _slotFrames[i].color = active ? new Color(0.25f, 0.22f, 0.1f, 0.95f) : new Color(0.08f, 0.09f, 0.13f, 0.8f);
                _slotFrames[i].rectTransform.localScale = active ? Vector3.one * 1.12f : Vector3.one;
                var portrait = Resources.Load<Sprite>(m.portraitResource);
                if (portrait == null && !_portraitCache.TryGetValue(m.portraitResource, out portrait))
                {
                    var t = Resources.Load<Texture2D>(m.portraitResource);
                    portrait = t != null ? MakeSprite(t) : null;
                    _portraitCache[m.portraitResource] = portrait;
                }
                if (portrait != null) { _slotPortraits[i].sprite = portrait; _slotPortraits[i].color = Color.white; }
                else _slotPortraits[i].color = m.themeColor;
                float frac = Mathf.Clamp01(m.hp / m.maxHp);
                var sz = _slotHp[i].rectTransform.sizeDelta; sz.x = 72f * frac; _slotHp[i].rectTransform.sizeDelta = sz;
                _slotHp[i].color = frac > 0.35f ? new Color(0.4f, 1f, 0.55f) : new Color(1f, 0.4f, 0.3f);
                _slotKeys[i].text = (i + 1).ToString() + "  " + m.charName;
            }
            var act = _team.Active;
            if (act != null && _nameText != null)
            {
                _nameText.text = act.charName + "  ·  " + ElementInfo.KoreanName(act.element);
                _nameText.color = Color.Lerp(act.themeColor, Color.white, 0.35f);
            }
        }

        // ================================================================ static API
        public static void Toast(string msg)
        {
            if (_inst == null) return;
            if (!_hudVisible || Cutscene.Active) { NotificationFeed.Item(msg, 1, new Color(0.7f, 0.88f, 1f)); return; }   // HUD hidden behind a screen / cutscene
            // a fresh toast still on screen gets queued instead of stomped (max 4 pending)
            if (Time.unscaledTime < _inst._toastUntil - 1.2f && _inst._toast.text.Length > 0)
            {
                if (_inst._toastQueue.Count < 4) _inst._toastQueue.Enqueue(msg);
                return;
            }
            _inst._toast.text = msg;
            _inst._toastUntil = Time.unscaledTime + 2.2f;
        }

        /// Top-center status line for live events (arena waves, rifts).
        public static void SetEventLine(string text)
        {
            if (_inst != null && _inst._eventText != null) _inst._eventText.text = text ?? "";
        }

        public static void NotifyResources()
        {
            if (_inst != null) _inst.RefreshParty();
        }

        public static void SetLockTarget(Health h)
        {
            if (_inst != null) _inst._lockTarget = h;
        }

        public static void PingEnemy(Health h)
        {
            if (_inst == null || h == null) return;
            _inst._shownEnemy = h;
            _inst._shownEnemyUntil = Time.unscaledTime + 4f;
        }

        public static void FadeScreen(float alpha, float time)
        {
            if (_inst != null) _inst.StartCoroutine(_inst.FadeRoutine(alpha, time));
        }

        IEnumerator FadeRoutine(float target, float time)
        {
            float start = _fade.color.a;
            float t = 0f;
            while (t < time)
            {
                t += Time.unscaledDeltaTime;
                var c = _fade.color; c.a = Mathf.Lerp(start, target, t / time); _fade.color = c;
                yield return null;
            }
        }

        public static void Victory()
        {
            if (_inst == null) return;
            _inst._victory.text = "정 벌 완 료";
            Toast("보스 격파! 필드가 해방되었습니다");
            _inst.StartCoroutine(_inst.VictoryRoutine());
        }

        IEnumerator VictoryRoutine()
        {
            yield return new WaitForSecondsRealtime(5f);
            _victory.text = "";
        }
    }

    /// Pooled floating damage numbers.
    public static class DamageNumbers
    {
        public static bool Enabled = true;    // accessibility toggle
        public static bool CritOnly;
        public static float Scale = 1f;
        static RectTransform _root;
        static Font _font;
        static readonly List<Text> _pool = new List<Text>();

        public static void Init(RectTransform canvasRoot, Font font)
        {
            _root = canvasRoot;
            _font = font;
            _pool.Clear();               // stale (destroyed) entries after a scene reload
        }

        public static void SpawnText(Vector3 worldPos, string text, Color color)
        {
            SpawnInternal(worldPos, text, 34, true, color);
        }

        public static void Spawn(Vector3 worldPos, float amount, bool crit, Color color)
        {
            if (CritOnly && !crit) return;
            SpawnInternal(worldPos, Mathf.RoundToInt(amount).ToString(), crit ? 40 : 27, crit,
                crit ? Color.Lerp(color, Color.white, 0.25f) : color);
        }

        static void SpawnInternal(Vector3 worldPos, string text, int size, bool big, Color color)
        {
            if (!Enabled || _root == null || CamCache.Main == null) return;
            Vector3 sp = CamCache.Main.WorldToScreenPoint(worldPos + (Vector3)(Random.insideUnitCircle * 0.5f));
            if (sp.z < 0f) return;

            Text t = null;
            for (int i = 0; i < _pool.Count; i++)
                if (!_pool[i].gameObject.activeSelf) { t = _pool[i]; break; }
            if (t == null)
            {
                var go = new GameObject("dmg");
                go.transform.SetParent(_root, false);
                t = go.AddComponent<Text>();
                t.font = _font;
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                var ol = go.AddComponent<Outline>();
                ol.effectColor = new Color(0f, 0f, 0f, 0.9f);
                ol.effectDistance = new Vector2(1.6f, -1.6f);
                t.raycastTarget = false;
                _pool.Add(t);
            }
            t.gameObject.SetActive(true);
            t.text = text;
            t.fontSize = Mathf.RoundToInt(size * Scale);
            t.fontStyle = big ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.rectTransform.position = sp;
            t.StartCoroutine(Animate(t, big));
        }

        static IEnumerator Animate(Text t, bool crit)
        {
            float life = crit ? 0.95f : 0.7f;
            float e = 0f;
            Vector3 start = t.rectTransform.position;
            Vector3 drift = new Vector3(Random.Range(-30f, 30f), Random.Range(70f, 110f), 0f);
            while (e < life)
            {
                e += Time.unscaledDeltaTime;
                float k = e / life;
                t.rectTransform.position = start + drift * k;
                var c = t.color; c.a = 1f - k * k; t.color = c;
                float s = crit ? Mathf.Lerp(1.35f, 1f, Mathf.Min(1f, k * 3f)) : 1f;
                t.rectTransform.localScale = Vector3.one * s;
                yield return null;
            }
            t.gameObject.SetActive(false);
        }
    }
}
