using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Hub tab "캐릭터": member rail + 3D preview on the left, 속성 · 무기 · 스킬 · 에코
    /// tabs on the right, each with its growth panel (design doc 7.10).
    public class CharacterScreen : UIScreen
    {
        public override string Id { get { return "Character"; } }
        public override string Title { get { return "캐릭터"; } }
        public override bool IsHubTab { get { return true; } }

        static readonly string[] TabNames = { "속성", "무기", "스킬", "에코" };
        static readonly int[] Stones = { ItemDB.Stone0, ItemDB.Stone1, ItemDB.Stone2 };

        ScreenRouter.HubHeader _header;
        TeamManager _team;
        int _member, _tab, _selSlot;
        bool _dirty;

        // rail + preview
        readonly Image[] _railIcons = new Image[3];
        readonly Image[] _railRings = new Image[3];
        readonly Text[] _railNames = new Text[3];
        readonly Text[] _railLv = new Text[3];
        readonly GameObject[] _railCells = new GameObject[3];
        RenderTexture _rt;
        RawImage _modelView;
        GameObject _previewRig, _previewCamHolder;
        Camera _previewCam;
        Text _charName, _charElement;

        // tabs
        readonly Button[] _tabBtns = new Button[4];
        readonly GameObject[] _panels = new GameObject[4];

        // 속성
        Text _statText, _levelText, _expText, _ascText;
        Image _expFill;
        readonly Button[] _stoneBtns = new Button[3];
        Button _ascendBtn;

        // 무기
        Image _wIcon;
        Text _wName, _wDetail;
        readonly Button[] _wStoneBtns = new Button[3];
        Button _wAscendBtn;
        RectTransform _wList;
        readonly List<GameObject> _wCells = new List<GameObject>();

        // 스킬
        class SkillCard { public Text title, body; public Button btn; }
        readonly SkillCard[] _skills = new SkillCard[4];

        // 에코
        readonly Image[] _slotFrames = new Image[5];
        readonly Image[] _slotIcons = new Image[5];
        readonly Text[] _slotNames = new Text[5];
        Text _costText, _sonataText, _echoDetail;
        RectTransform _ownedGrid;
        ScrollRect _ownedScroll;
        readonly List<GameObject> _ownedCells = new List<GameObject>();
        Button _enhanceBtn, _tuneBtn, _retuneBtn, _mergeBtn;

        // ================================================================ build
        protected override void Build()
        {
            _header = ScreenRouter.BuildHubHeader(Root, "캐릭터", Id);

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var cell = UIKit.Img("rail" + i, Root, new Color(0.10f, 0.13f, 0.15f, 1f), null, true);
                var rt = cell.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(52f, -120f - i * 130f); rt.sizeDelta = new Vector2(108f, 116f);
                var btn = cell.gameObject.AddComponent<Button>();
                btn.targetGraphic = cell;
                var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
                btn.onClick.AddListener(() => SelectMember(idx));
                _railCells[i] = cell.gameObject;
                var ring = UIKit.Img("ring", cell.transform, Color.white, UIKit.Ring);
                ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = new Vector2(0.5f, 0.58f);
                ring.rectTransform.sizeDelta = new Vector2(86f, 86f);
                _railRings[i] = ring;
                var icon = UIKit.Img("icon", cell.transform, Color.white, UIKit.Dot);
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.58f);
                icon.rectTransform.sizeDelta = new Vector2(72f, 72f);
                icon.preserveAspect = true;
                _railIcons[i] = icon;
                _railNames[i] = UIKit.Txt("nm", cell.transform, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(104f, 20f), "", 13, Color.white, TextAnchor.MiddleCenter);
                _railNames[i].rectTransform.pivot = new Vector2(0.5f, 0.5f);
                _railLv[i] = UIKit.Txt("lv", cell.transform, new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(104f, 16f), "", 11, UIKit.Theme.Accent, TextAnchor.MiddleCenter);
                _railLv[i].rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }

            _rt = new RenderTexture(640, 960, 16);
            var mv = new GameObject("modelView");
            mv.transform.SetParent(Root, false);
            _modelView = mv.AddComponent<RawImage>();
            _modelView.texture = _rt;
            var mrt = _modelView.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0f, 0.5f); mrt.pivot = new Vector2(0f, 0.5f);
            mrt.anchoredPosition = new Vector2(180f, -40f); mrt.sizeDelta = new Vector2(560f, 840f);
            _modelView.raycastTarget = false;
            _charName = UIKit.Txt("cname", Root, new Vector2(0f, 0f), new Vector2(210f, 118f), new Vector2(560f, 56f), "", 44, Color.white, TextAnchor.LowerLeft, true);
            _charElement = UIKit.Txt("celem", Root, new Vector2(0f, 0f), new Vector2(214f, 84f), new Vector2(560f, 30f), "", 20, new Color(1f, 1f, 1f, 0.75f), TextAnchor.LowerLeft);

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _tabBtns[i] = UIKit.Btn("tab" + i, Root, new Vector2(0f, 1f), new Vector2(820f + i * 156f, -112f), new Vector2(148f, 46f), TabNames[i], UIKit.Theme.Button, () => { _tab = idx; Refresh(); }, 17);
                _tabBtns[i].GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
                var p = new GameObject("panel" + i);
                p.transform.SetParent(Root, false);
                var prt = p.AddComponent<RectTransform>();
                prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f); prt.pivot = new Vector2(0f, 1f);
                prt.anchoredPosition = new Vector2(820f, -170f); prt.sizeDelta = new Vector2(1030f, 880f);
                var bg = UIKit.Img("bg", p.transform, new Color(1f, 1f, 1f, 0.04f), UIKit.Rounded);
                UIKit.Stretch(bg.rectTransform);
                _panels[i] = p;
            }
            BuildStatPanel(_panels[0].transform);
            BuildWeaponPanel(_panels[1].transform);
            BuildSkillPanel(_panels[2].transform);
            BuildEchoPanel(_panels[3].transform);
        }

        Text T(Transform p, string n, Vector2 pos, Vector2 size, string s, int fs, Color c, TextAnchor a = TextAnchor.UpperLeft, bool bold = false)
        {
            var t = UIKit.Txt(n, p, new Vector2(0f, 1f), pos, size, s, fs, c, a, bold);
            t.rectTransform.pivot = new Vector2(0f, 1f);
            return t;
        }

        Button B(Transform p, string n, Vector2 pos, Vector2 size, string label, Color bg, System.Action act, int fs = 15)
        {
            var b = UIKit.Btn(n, p, new Vector2(0f, 1f), pos, size, label, bg, act, fs);
            b.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            return b;
        }

        void BuildStatPanel(Transform p)
        {
            _statText = T(p, "stat", new Vector2(28f, -22f), new Vector2(980f, 330f), "", 17, UIKit.Theme.TextHi);
            var sep = UIKit.Img("sep", p, new Color(1f, 1f, 1f, 0.12f));
            var srt = sep.rectTransform; srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(0.5f, 1f); srt.anchoredPosition = new Vector2(0f, -352f); srt.sizeDelta = new Vector2(-40f, 1f);
            _levelText = T(p, "lv", new Vector2(28f, -366f), new Vector2(980f, 30f), "", 21, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
            var bar = UIKit.Img("expBg", p, new Color(0f, 0f, 0f, 0.4f), UIKit.Rounded);
            var brt = bar.rectTransform; brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 1f); brt.anchoredPosition = new Vector2(28f, -404f); brt.sizeDelta = new Vector2(974f, 14f);
            _expFill = UIKit.Img("fill", bar.transform, new Color(0.65f, 0.9f, 1f, 0.9f), UIKit.Rounded);
            var frt = _expFill.rectTransform; frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(0f, 1f); frt.pivot = new Vector2(0f, 0.5f); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero; frt.sizeDelta = new Vector2(0f, 0f);
            _expText = T(p, "exp", new Vector2(28f, -424f), new Vector2(980f, 22f), "", 13, UIKit.Theme.TextLo);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                _stoneBtns[i] = B(p, "stone" + i, new Vector2(28f + i * 328f, -454f), new Vector2(318f, 44f), "-", UIKit.Theme.Confirm, () => { if (ProgressSystem.I != null) ProgressSystem.I.UseStone(_member, Stones[idx], 1); }, 14);
            }
            _ascText = T(p, "asc", new Vector2(28f, -516f), new Vector2(980f, 130f), "", 15, UIKit.Theme.TextHi);
            _ascendBtn = B(p, "ascend", new Vector2(28f, -660f), new Vector2(340f, 48f), "-", new Color(0.30f, 0.26f, 0.12f, 1f), () =>
            {
                if (ProgressSystem.I == null) return;
                string why;
                if (!ProgressSystem.I.CanAscend(_member, out why)) { HUDController.Toast("돌파 불가 — " + why); return; }
                var c = ProgressSystem.I.Of(_member);
                Modal.Confirm("돌파 " + Growth.AscensionNames[c.ascension + 1], Growth.CostText(Growth.AscendCost(c.ascension, ElementIdx())) + "\n\n" + Growth.AscendNode(c.ascension + 1) + " · 레벨 상한 " + Growth.LevelCap(c.ascension + 1) + " · 스킬 상한 Lv " + Growth.SkillCap(c.ascension + 1),
                    "돌파", "취소", false, () => ProgressSystem.I.Ascend(_member));
            }, 16);
        }

        void BuildWeaponPanel(Transform p)
        {
            var card = UIKit.Panel("card", p, new Color(0.17f, 0.15f, 0.10f, 1f), new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(320f, 300f));
            T(card.transform, "tag", new Vector2(14f, -10f), new Vector2(290f, 22f), "장착 중", 14, UIKit.Theme.Accent, TextAnchor.UpperLeft, true);
            _wIcon = UIKit.Img("icon", card.transform, Color.white, MapIcons.Get("sword"));
            _wIcon.rectTransform.anchorMin = _wIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.62f);
            _wIcon.rectTransform.sizeDelta = new Vector2(110f, 110f);
            _wName = UIKit.Txt("name", card.transform, new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(300f, 44f), "", 18, Color.white, TextAnchor.MiddleCenter, true);
            _wName.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _wDetail = T(p, "detail", new Vector2(28f, -340f), new Vector2(320f, 200f), "", 14, UIKit.Theme.TextHi);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                _wStoneBtns[i] = B(p, "wstone" + i, new Vector2(28f, -546f + -i * 46f), new Vector2(320f, 40f), "-", UIKit.Theme.Confirm, () =>
                {
                    var w = WeaponSystem.I != null ? WeaponSystem.I.InstanceOf(_member) : null;
                    if (w != null) WeaponSystem.I.UseStone(w.uid, Stones[idx], 1);
                }, 13);
            }
            _wAscendBtn = B(p, "wascend", new Vector2(28f, -690f), new Vector2(320f, 46f), "-", new Color(0.30f, 0.26f, 0.12f, 1f), () =>
            {
                var w = WeaponSystem.I != null ? WeaponSystem.I.InstanceOf(_member) : null;
                if (w == null) return;
                string why;
                if (!WeaponSystem.I.CanAscend(w.uid, out why)) { HUDController.Toast("무기 돌파 불가 — " + why); return; }
                Modal.Confirm("무기 돌파 " + (w.ascension + 1), Growth.CostText(Growth.WeaponAscendCost(w.ascension)) + "\n\n레벨 상한 " + Growth.WeaponLevelCap(w.Def.tier, w.ascension + 1) + " · 패시브 성장",
                    "돌파", "취소", false, () => WeaponSystem.I.Ascend(w.uid));
            }, 14);
            T(p, "listTag", new Vector2(388f, -28f), new Vector2(600f, 24f), "보유 무기 — 장착 또는 재료로 투입", 15, UIKit.Theme.TextLo);
            _wList = UIKit.Rect("list", p, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(388f, -64f), new Vector2(620f, 800f));
        }

        void BuildSkillPanel(Transform p)
        {
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var card = UIKit.Panel("skill" + i, p, new Color(1f, 1f, 1f, 0.04f), new Vector2(0f, 1f), new Vector2(28f, -24f - i * 208f), new Vector2(974f, 196f));
                var sc = new SkillCard();
                sc.title = T(card.transform, "title", new Vector2(20f, -14f), new Vector2(700f, 30f), "", 19, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
                sc.body = T(card.transform, "body", new Vector2(20f, -50f), new Vector2(720f, 140f), "", 14, UIKit.Theme.TextHi);
                sc.btn = B(card.transform, "up", new Vector2(760f, -70f), new Vector2(196f, 50f), "-", UIKit.Theme.Confirm, () => { if (ProgressSystem.I != null) ProgressSystem.I.UpgradeSkill(_member, idx); }, 15);
                _skills[i] = sc;
            }
        }

        void BuildEchoPanel(Transform p)
        {
            _costText = T(p, "cost", new Vector2(28f, -18f), new Vector2(300f, 30f), "", 20, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
            _sonataText = T(p, "sonata", new Vector2(320f, -18f), new Vector2(680f, 30f), "", 14, UIKit.Theme.Info, TextAnchor.MiddleLeft);
            for (int i = 0; i < 5; i++)
            {
                int slot = i;
                var frame = UIKit.Img("slot" + i, p, UIKit.Theme.Cell, UIKit.Rounded, true);
                var rt = frame.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(28f + i * 198f, -58f); rt.sizeDelta = new Vector2(186f, 170f);
                var btn = frame.gameObject.AddComponent<Button>();
                btn.targetGraphic = frame;
                var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
                btn.onClick.AddListener(() => OnSlotClicked(slot));
                _slotFrames[i] = frame;
                T(frame.transform, "tag", new Vector2(10f, -8f), new Vector2(166f, 20f), i == 0 ? "메인 · Q 스킬" : "슬롯 " + (i + 1), 12, i == 0 ? UIKit.Theme.Accent : UIKit.Theme.TextLo, TextAnchor.UpperLeft, i == 0);
                var icon = UIKit.Img("icon", frame.transform, new Color(1f, 1f, 1f, 0.12f), UIKit.Dot);
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.56f);
                icon.rectTransform.sizeDelta = new Vector2(74f, 74f);
                _slotIcons[i] = icon;
                _slotNames[i] = UIKit.Txt("nm", frame.transform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(176f, 36f), "", 12, Color.white, TextAnchor.MiddleCenter);
                _slotNames[i].rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }
            T(p, "hint", new Vector2(28f, -236f), new Vector2(980f, 22f), "슬롯을 선택하고 아래 에코를 클릭해 장착 · 선택된 슬롯을 다시 클릭하면 해제", 13, UIKit.Theme.TextLo);
            // The owned list runs past any fixed rect after a handful of echoes drop, and a plain
            // RectTransform neither clips nor scrolls, so everything past ~13 was drawn off-panel.
            // Same masked scroll view the bag and codex use; RefreshEcho sizes the content by row.
            var ownedView = UIKit.Img("ownedView", p, new Color(1f, 1f, 1f, 0.03f), null, true);
            var ovr = ownedView.rectTransform;
            ovr.anchorMin = ovr.anchorMax = new Vector2(0f, 1f); ovr.pivot = new Vector2(0f, 1f);
            ovr.anchoredPosition = new Vector2(28f, -266f); ovr.sizeDelta = new Vector2(658f, 600f);
            ownedView.gameObject.AddComponent<RectMask2D>();
            _ownedScroll = ownedView.gameObject.AddComponent<ScrollRect>();
            _ownedScroll.horizontal = false;
            _ownedScroll.movementType = ScrollRect.MovementType.Clamped;
            _ownedScroll.scrollSensitivity = 40f;
            _ownedGrid = UIKit.Rect("owned", ownedView.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 600f));
            _ownedScroll.viewport = ovr;
            _ownedScroll.content = _ownedGrid;
            var det = UIKit.Panel("det", p, new Color(1f, 1f, 1f, 0.05f), new Vector2(0f, 1f), new Vector2(694f, -262f), new Vector2(316f, 600f));
            _echoDetail = T(det.transform, "det", new Vector2(14f, -12f), new Vector2(290f, 360f), "", 13, UIKit.Theme.TextHi);
            _enhanceBtn = B(det.transform, "enh", new Vector2(14f, -392f), new Vector2(288f, 40f), "-", new Color(0.30f, 0.26f, 0.12f, 1f), () => { var inst = SelInst(); if (inst != null) EchoSystem.I.Enhance(inst.uid); }, 13);
            _tuneBtn = B(det.transform, "tune", new Vector2(14f, -438f), new Vector2(288f, 40f), "-", new Color(0.14f, 0.24f, 0.30f, 1f), () => { var inst = SelInst(); if (inst != null) EchoSystem.I.Tune(inst.uid); }, 13);
            _retuneBtn = B(det.transform, "retune", new Vector2(14f, -484f), new Vector2(288f, 40f), "-", new Color(0.14f, 0.24f, 0.30f, 1f), RetunePrompt, 13);
            _mergeBtn = B(det.transform, "merge", new Vector2(14f, -530f), new Vector2(288f, 40f), "합성 (같은 ★ 5개 → 1개)", new Color(0.22f, 0.18f, 0.30f, 1f), MergePrompt, 13);
        }

        // ================================================================ open / close / tick
        public override void OnOpen(object args)
        {
            ScreenRouter.RefreshHubHeader(_header);
            _team = Object.FindAnyObjectByType<TeamManager>();
            if (_team != null && _member >= _team.members.Length) _member = 0;
            if (ProgressSystem.I != null) ProgressSystem.I.OnChanged += MarkDirty;
            if (EchoSystem.I != null) EchoSystem.I.OnChanged += MarkDirty;
            if (WeaponSystem.I != null) WeaponSystem.I.OnChanged += MarkDirty;
            Inventory.Changed += MarkDirty;
            BuildPreview();
            Refresh();
        }

        public override void OnClose()
        {
            if (ProgressSystem.I != null) ProgressSystem.I.OnChanged -= MarkDirty;
            if (EchoSystem.I != null) EchoSystem.I.OnChanged -= MarkDirty;
            if (WeaponSystem.I != null) WeaponSystem.I.OnChanged -= MarkDirty;
            Inventory.Changed -= MarkDirty;
            DestroyPreview();
        }

        public override void OnTick()
        {
            int m = InputService.MenuMemberPressed;
            if (m >= 0) SelectMember(m);
            else if (InputService.MenuMemberPrevPressed) SelectMember((_member + 2) % 3);
            else if (InputService.MenuMemberNextPressed) SelectMember((_member + 1) % 3);
            if (_dirty) { _dirty = false; Refresh(); }
        }

        void MarkDirty() { _dirty = true; }
        public override Selectable DefaultFocus { get { return _tabBtns[Mathf.Clamp(_tab, 0, 3)]; } }

        void SelectMember(int idx)
        {
            if (_team == null || idx >= _team.members.Length || _team.members[idx] == null) return;
            _member = idx; _selSlot = 0;
            BuildPreview();
            Refresh();
        }

        int ElementIdx()
        {
            var m = Member;
            if (m == null) return 0;
            return m.element == Element.Glacio ? 1 : m.element == Element.Fusion ? 2 : 0;
        }

        MemberConfig Member { get { return _team != null && _member < _team.members.Length ? _team.members[_member] : null; } }
        EchoInstance SelInst() { return EchoSystem.I != null ? EchoSystem.I.InstanceAt(_member, _selSlot) : null; }

        // ================================================================ preview rig (ported)
        void BuildPreview()
        {
            DestroyPreview();
            var mem = Member;
            if (mem == null) return;
            _previewRig = Instantiate(mem.gameObject);
            _previewRig.name = "~charPreview";
            _previewRig.SetActive(true);
            _previewRig.transform.position = new Vector3(600f, 240f, 600f);
            _previewRig.transform.rotation = Quaternion.Euler(0f, 12f, 0f);
            var anim = _previewRig.GetComponent<Animator>();
            if (anim != null)
            {
                anim.updateMode = AnimatorUpdateMode.UnscaledTime;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (anim.runtimeAnimatorController != null) anim.Play("Loco", 0, 0f);
            }
            var camGo = new GameObject("~previewCam");
            _previewCam = camGo.AddComponent<Camera>();
            _previewCam.clearFlags = CameraClearFlags.SolidColor;
            _previewCam.backgroundColor = new Color(0.075f, 0.095f, 0.115f, 1f);
            _previewCam.fieldOfView = 30f; _previewCam.nearClipPlane = 0.05f; _previewCam.farClipPlane = 25f;
            _previewCam.targetTexture = _rt;
            camGo.transform.position = _previewRig.transform.position + new Vector3(0f, 1.15f, 2.9f);
            camGo.transform.LookAt(_previewRig.transform.position + Vector3.up * 1.02f);
            var key = new GameObject("~pKey").AddComponent<Light>();
            key.type = LightType.Spot; key.transform.SetParent(camGo.transform, false);
            key.transform.localPosition = new Vector3(1.2f, 0.8f, 0f);
            key.transform.LookAt(_previewRig.transform.position + Vector3.up);
            key.range = 9f; key.spotAngle = 70f; key.intensity = 3.2f; key.color = new Color(1f, 0.97f, 0.9f);
            var rim = new GameObject("~pRim").AddComponent<Light>();
            rim.type = LightType.Spot; rim.transform.SetParent(camGo.transform, false);
            rim.transform.localPosition = new Vector3(-1.6f, 1.4f, -4.6f);
            rim.transform.LookAt(_previewRig.transform.position + Vector3.up);
            rim.range = 10f; rim.spotAngle = 80f; rim.intensity = 2.2f; rim.color = new Color(0.6f, 0.75f, 1f);
            _previewCamHolder = camGo;
        }

        void DestroyPreview()
        {
            if (_previewRig != null) Destroy(_previewRig);
            if (_previewCamHolder != null) Destroy(_previewCamHolder);
            _previewRig = null; _previewCamHolder = null; _previewCam = null;
        }

        // ================================================================ refresh
        void Refresh()
        {
            if (_team == null) return;
            ScreenRouter.RefreshHubHeader(_header);
            var ps = ProgressSystem.I;
            for (int i = 0; i < 3; i++)
            {
                bool has = i < _team.members.Length && _team.members[i] != null;
                _railCells[i].SetActive(has);
                if (!has) continue;
                var m = _team.members[i];
                var portrait = Resources.Load<Texture2D>(m.portraitResource);
                if (portrait != null) { _railIcons[i].sprite = Sprite.Create(portrait, new Rect(0, 0, portrait.width, portrait.height), new Vector2(0.5f, 0.5f), 100f); _railIcons[i].color = Color.white; }
                else _railIcons[i].color = m.themeColor;
                _railRings[i].color = i == _member ? UIKit.Theme.Accent : new Color(m.themeColor.r, m.themeColor.g, m.themeColor.b, 0.55f);
                _railNames[i].text = m.charName;
                var c = ps != null ? ps.Of(i) : null;
                _railLv[i].text = c != null ? "Lv " + c.level + (c.ascension > 0 ? " · " + Growth.AscensionNames[c.ascension] : "") : "";
            }
            var mem = Member;
            if (mem == null) return;
            _charName.text = mem.charName;
            _charName.color = Color.Lerp(mem.themeColor, Color.white, 0.3f);
            _charElement.text = ElementInfo.KoreanName(mem.element) + " 속성 · 조율사";
            for (int i = 0; i < 4; i++)
            {
                _tabBtns[i].GetComponent<Image>().color = i == _tab ? UIKit.Theme.Selected : UIKit.Theme.Button;
                _panels[i].SetActive(i == _tab);
            }
            switch (_tab)
            {
                case 0: RefreshStats(mem); break;
                case 1: RefreshWeapon(mem); break;
                case 2: RefreshSkills(mem); break;
                default: RefreshEcho(mem); break;
            }
            FocusNavigator.MarkDirty();
        }

        void RefreshStats(MemberConfig m)
        {
            var es = EchoSystem.I; var ps = ProgressSystem.I;
            var c = ps != null ? ps.Of(_member) : new CharacterProgress();
            string atkLine = "기본 " + m.baseAtk;
            if (m.bonusAtk > 0.5f) atkLine += " + 무기 " + Mathf.RoundToInt(m.bonusAtk);
            if (m.echoAtkFlat > 0.5f) atkLine += " + 에코 " + Mathf.RoundToInt(m.echoAtkFlat);
            if (m.echoAtkPct + m.ascAtkPct > 0.001f) atkLine += " · +" + Mathf.RoundToInt((m.echoAtkPct + m.ascAtkPct) * 100f) + "%";
            atkLine += " · 성장 ×" + m.statMul.ToString("F2");
            _statText.text =
                "HP                " + Mathf.CeilToInt(m.hp) + " / " + Mathf.CeilToInt(m.maxHp) + "\n" +
                "공격력            " + Mathf.RoundToInt(m.EffAtk) + "   (" + atkLine + ")\n" +
                "크리티컬 확률     " + Mathf.RoundToInt(m.EffCrit * 100f) + "%   ·   크리티컬 피해 " + Mathf.RoundToInt(m.EffCritMul * 100f) + "%\n" +
                "공명 스킬 쿨다운  " + m.skillCooldown + "s   ·   해방 에너지 " + m.ultEnergyMax + "\n\n" +
                "─ 에코 보너스 ─   피해 ×" + (es != null ? es.DamageMulFor(_member) : 1f).ToString("F2") + " · 스킬 ×" + (es != null ? es.SkillDamageMulFor(_member) : 1f).ToString("F2") +
                " · 협주 ×" + (es != null ? es.ConcertoMulFor(_member) : 1f).ToString("F2") + " · 이동 ×" + (es != null ? es.MoveSpeedMulFor(_member) : 1f).ToString("F2") + " · 받는 피해 ×" + (es != null ? es.DamageTakenMulFor(_member) : 1f).ToString("F2") + "\n" +
                "─ 스킬 배율 ─     일반 ×" + Growth.SkillMul(0, c.skillLv[0]).ToString("0.00") + " · 스킬 ×" + Growth.SkillMul(1, c.skillLv[1]).ToString("0.00") + " · 해방 ×" + Growth.SkillMul(2, c.skillLv[2]).ToString("0.00") + " · 변주 ×" + Growth.SkillMul(3, c.skillLv[3]).ToString("0.00") + "\n" +
                "─ 돌파 노드 ─     " + (c.ascension >= 1 ? Growth.AscendNode(1) : "(I 미개방)") + " · " + (c.ascension >= 2 ? Growth.AscendNode(2) : "(II 미개방)") + " · " + (c.ascension >= 3 ? Growth.AscendNode(3) : "(III 미개방)");

            int cap = Growth.LevelCap(c.ascension);
            float need = Growth.ExpNeed(c.level);
            _levelText.text = "Lv " + c.level + " / " + cap + "   돌파 " + (c.ascension > 0 ? Growth.AscensionNames[c.ascension] : "-") + "   " + ElementInfo.KoreanName(m.element);
            bool capped = c.level >= cap;
            _expFill.rectTransform.sizeDelta = new Vector2(974f * (capped ? 1f : Mathf.Clamp01(c.exp / need)), 0f);
            _expText.text = capped ? "레벨 상한 — 돌파하면 계속 성장합니다 (보관 EXP " + Mathf.RoundToInt(c.exp) + ")" : "EXP " + Mathf.RoundToInt(c.exp) + " / " + Mathf.RoundToInt(need) + "   ·   전투 EXP: 필드 100% · 대기 80%";
            for (int i = 0; i < 3; i++)
            {
                var d = ItemDB.Get(Stones[i]);
                int have = Inventory.Count(Stones[i]);
                _stoneBtns[i].GetComponentInChildren<Text>().text = d.name + " ×" + have + "  (+" + d.expValue + " EXP)";
                _stoneBtns[i].interactable = have > 0 && !capped;
            }
            if (c.ascension >= Growth.MaxAscension)
            {
                _ascText.text = "─ 돌파 III 완료 ─  모든 노드 개방 · 레벨 상한 50";
                _ascendBtn.gameObject.SetActive(false);
            }
            else
            {
                var cost = Growth.AscendCost(c.ascension, ElementIdx());
                string why; bool ok = ps != null && ps.CanAscend(_member, out why);
                _ascText.text = "─ 돌파 " + Growth.AscensionNames[c.ascension + 1] + " ─  Lv " + Growth.AscendGate(c.ascension) + " 도달 시 · 상한 Lv " + Growth.LevelCap(c.ascension + 1) + " · 스킬 Lv " + Growth.SkillCap(c.ascension + 1) + " · 노드: " + Growth.AscendNode(c.ascension + 1) + "\n필요  " + Growth.CostText(cost) + (ok ? "" : "\n미충족: " + (ps != null ? (ps.CanAscend(_member, out why) ? "" : why) : ""));
                _ascendBtn.gameObject.SetActive(true);
                _ascendBtn.interactable = ok;
                _ascendBtn.GetComponentInChildren<Text>().text = "돌파 " + Growth.AscensionNames[c.ascension + 1];
            }
        }

        void RefreshWeapon(MemberConfig mem)
        {
            var ws = WeaponSystem.I;
            var cur = ws != null ? ws.InstanceOf(_member) : null;
            _wIcon.color = cur != null ? cur.Def.Tint : new Color(1f, 1f, 1f, 0.12f);
            _wName.text = cur != null ? cur.Def.name + "  T" + cur.Def.tier : "비어있음";
            if (cur != null)
            {
                bool capped = cur.level >= cur.LevelCap;
                _wDetail.text = "Lv " + cur.level + " / " + cur.LevelCap + "   돌파 " + cur.ascension + " / " + Growth.WeaponMaxAscension(cur.Def.tier) + "\n공격력 +" + Mathf.RoundToInt(cur.Atk) + "   " + cur.PassiveText + "\n" +
                    (capped ? "레벨 상한 — 돌파 필요 (보관 EXP " + Mathf.RoundToInt(cur.exp) + ")" : "EXP " + Mathf.RoundToInt(cur.exp) + " / " + Mathf.RoundToInt(Growth.WExpNeed(cur.level))) +
                    "\n\n『" + cur.Def.lore + "』";
                for (int i = 0; i < 3; i++)
                {
                    var d = ItemDB.Get(Stones[i]); int have = Inventory.Count(Stones[i]);
                    _wStoneBtns[i].GetComponentInChildren<Text>().text = d.name + " ×" + have + " (+" + d.expValue + ")";
                    _wStoneBtns[i].interactable = have > 0 && !capped;
                }
                bool maxAsc = cur.ascension >= Growth.WeaponMaxAscension(cur.Def.tier);
                string why; bool ok = ws.CanAscend(cur.uid, out why);
                _wAscendBtn.gameObject.SetActive(!maxAsc);
                _wAscendBtn.interactable = ok;
                _wAscendBtn.GetComponentInChildren<Text>().text = maxAsc ? "" : "돌파 " + (cur.ascension + 1) + " — " + (ok ? "가능" : why);
            }
            else { _wDetail.text = ""; foreach (var b in _wStoneBtns) b.interactable = false; _wAscendBtn.gameObject.SetActive(false); }

            foreach (var c in _wCells) Destroy(c);
            _wCells.Clear();
            if (ws == null) return;
            int row = 0;
            foreach (var w in ws.Items)
            {
                var inst = w; int em;
                bool worn = ws.IsEquipped(inst.uid, out em);
                var cell = UIKit.Img("w" + inst.uid, _wList, worn ? new Color(0.10f, 0.14f, 0.12f, 1f) : UIKit.Theme.Cell, UIKit.Rounded);
                var rt = cell.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, -row * 92f); rt.sizeDelta = new Vector2(620f, 84f);
                var icon = UIKit.Img("ic", cell.transform, inst.Def.Tint, MapIcons.Get("sword"));
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f); icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                icon.rectTransform.anchoredPosition = new Vector2(12f, 0f); icon.rectTransform.sizeDelta = new Vector2(56f, 56f);
                T(cell.transform, "n", new Vector2(80f, -10f), new Vector2(330f, 24f), inst.Def.name + "  T" + inst.Def.tier + "   Lv " + inst.level + (inst.ascension > 0 ? " · 돌파 " + inst.ascension : ""), 15, Color.white, TextAnchor.UpperLeft, true);
                T(cell.transform, "d", new Vector2(80f, -38f), new Vector2(330f, 40f), "공격 +" + Mathf.RoundToInt(inst.Atk) + " · " + inst.PassiveText + (worn ? "\n장착: " + (_team.members[em] != null ? _team.members[em].charName : "?") : ""), 12, new Color(1f, 0.9f, 0.6f, 0.9f));
                if (!worn || em != _member)
                    B(cell.transform, "eq", new Vector2(420f, -12f), new Vector2(92f, 30f), "장착", UIKit.Theme.Confirm, () => { ws.Equip(_member, inst.uid); }, 13);
                if (!worn && cur != null)
                    B(cell.transform, "feed", new Vector2(420f, -46f), new Vector2(190f, 30f), "재료로 투입 (+" + Growth.WeaponFeedExp(inst.Def.tier) + ")", new Color(0.3f, 0.22f, 0.16f, 1f), () =>
                        Modal.Confirm("무기 투입", inst.Def.name + " Lv " + inst.level + "을(를) 소모해 " + cur.Def.name + "에 EXP " + Growth.WeaponFeedExp(inst.Def.tier) + "+를 투입합니다.", "투입", "취소", true, () => ws.Feed(cur.uid, inst.uid)), 12);
                _wCells.Add(cell.gameObject);
                row++;
            }
        }

        void RefreshSkills(MemberConfig m)
        {
            var ps = ProgressSystem.I;
            var c = ps != null ? ps.Of(_member) : new CharacterProgress();
            string[] desc =
            {
                "4단 콤보 " + ComboText(m) + " · 강공 " + m.heavy.dmgMul + "× · 낙하 " + m.plunge.dmgMul + "× · 대시 " + m.dashAtk.dmgMul + "×",
                "광역 " + m.skill.dmgMul + "× · 쿨다운 " + m.skillCooldown + "s",
                "대광역 " + m.ult.dmgMul + "× · 에너지 " + m.ultEnergyMax,
                "변주 " + m.introSkill.dmgMul + "× 광역 · 여운: " + OutroDesc(m),
            };
            for (int i = 0; i < 4; i++)
            {
                int lv = c.skillLv[i];
                int cap = Growth.SkillCap(c.ascension);
                var sc = _skills[i];
                sc.title.text = Growth.SkillNames[i] + "   Lv " + lv + " / " + Growth.MaxSkill + (lv >= cap && lv < Growth.MaxSkill ? "   (상한 Lv " + cap + " — 돌파 " + Growth.AscensionNames[c.ascension + 1] + " 필요)" : "");
                string perk = Growth.SkillPerk(i, lv);
                string next = lv < Growth.MaxSkill ? "다음 Lv " + (lv + 1) + ": ×" + Growth.SkillMul(i, lv + 1).ToString("0.00") + (Growth.SkillPerk(i, lv + 1) != perk ? " · " + Growth.SkillPerk(i, lv + 1) : "") + "\n필요  " + Growth.CostText(Growth.SkillCost(lv, ElementIdx())) : "최대 레벨";
                sc.body.text = desc[i] + "\n배율 ×" + Growth.SkillMul(i, lv).ToString("0.00") + (perk != "" ? " · " + perk : "") + "\n" + next;
                string why; bool ok = ps != null && ps.CanUpgradeSkill(_member, i, out why);
                sc.btn.interactable = ok;
                sc.btn.GetComponentInChildren<Text>().text = lv >= Growth.MaxSkill ? "MAX" : ok ? "강화 → Lv " + (lv + 1) : (ps != null && !ps.CanUpgradeSkill(_member, i, out why) ? why : "-");
            }
        }

        static string ComboText(MemberConfig m)
        {
            string s = "";
            for (int i = 0; i < m.combo.Length; i++) s += m.combo[i].dmgMul.ToString("F1") + "×" + (i < m.combo.Length - 1 ? "→" : "");
            return s;
        }

        static string OutroDesc(MemberConfig m)
        {
            switch (m.outroType)
            {
                case OutroType.DamageUp: return "후속 피해 +" + Mathf.RoundToInt((m.outroBuffMul - 1f) * 100f) + "% · " + m.outroBuffDur + "s";
                case OutroType.SkillHaste: return "후속 스킬 쿨다운 −" + Mathf.RoundToInt((1f - m.outroBuffMul) * 100f) + "% · " + m.outroBuffDur + "s";
                case OutroType.HeavyUp: return "후속 강공격 +" + Mathf.RoundToInt((m.outroBuffMul - 1f) * 100f) + "% · " + m.outroBuffDur + "s";
                default: return "-";
            }
        }

        void RefreshEcho(MemberConfig m)
        {
            var es = EchoSystem.I;
            _costText.text = "코스트 " + (es != null ? es.UsedCost(_member) : 0) + " / " + EchoSystem.CostCap;
            string sonata = "";
            if (es != null && es.ShadowSonata(_member)) sonata += "그림자 소나타(2): 속성딜 +10%   ";
            if (es != null && es.GuardSonata(_member)) sonata += "수호 소나타(2): 피해감소 +8%";
            if (sonata == "") sonata = "소나타 미발동 (그림자 " + (es != null ? es.FamilyCount(_member, EchoFamily.Shadow) : 0) + "/2 · 수호 " + (es != null ? es.FamilyCount(_member, EchoFamily.Guard) : 0) + "/2)";
            _sonataText.text = sonata;
            for (int i = 0; i < 5; i++)
            {
                var inst = es != null ? es.InstanceAt(_member, i) : null;
                var d = inst != null ? inst.Def : null;
                _slotFrames[i].color = i == _selSlot ? UIKit.Theme.Selected : (i == 0 ? new Color(0.17f, 0.15f, 0.10f, 1f) : UIKit.Theme.Cell);
                _slotIcons[i].color = d != null ? d.Tint : new Color(1f, 1f, 1f, 0.10f);
                _slotNames[i].text = d != null ? d.name + (inst.level > 0 ? " +" + inst.level : "") + " ★" + d.star + "\n" + inst.main.Text : "비어있음";
                _slotNames[i].color = d != null ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            }
            foreach (var c in _ownedCells) Destroy(c);
            _ownedCells.Clear();
            if (es != null)
            {
                int col = 0, row = 0;
                foreach (var it in es.Instances)
                {
                    var inst = it; var d = inst.Def; int em, esl;
                    bool worn = es.EquipLocation(inst.uid, out em, out esl);
                    var cell = UIKit.Btn("own" + inst.uid, _ownedGrid, new Vector2(0f, 1f), new Vector2(col * 326f, -row * 92f), new Vector2(316f, 84f), "", worn ? new Color(0.10f, 0.14f, 0.12f, 1f) : UIKit.Theme.Cell,
                        () => { if (EchoSystem.I != null) { EchoSystem.I.Equip(_member, _selSlot, inst.uid); Refresh(); } }, 12);
                    cell.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
                    var icon = UIKit.Img("ic", cell.transform, d.Tint, UIKit.Dot);
                    icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f); icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                    icon.rectTransform.anchoredPosition = new Vector2(10f, 0f); icon.rectTransform.sizeDelta = new Vector2(50f, 50f);
                    T(cell.transform, "n", new Vector2(70f, -8f), new Vector2(240f, 20f), d.name + (inst.level > 0 ? " +" + inst.level : "") + " ★" + d.star + " · c" + d.cost, 12, Color.white);
                    T(cell.transform, "m", new Vector2(70f, -30f), new Vector2(240f, 20f), inst.main.Text, 12, new Color(1f, 0.9f, 0.6f, 0.95f), TextAnchor.UpperLeft, true);
                    T(cell.transform, "s", new Vector2(70f, -52f), new Vector2(240f, 22f), "부옵 " + inst.Revealed + "/" + inst.subs.Length + " 개방" + (worn ? "  · " + (_team.members[em] != null ? _team.members[em].charName : "장착") + (em == _member ? " 슬롯" + (esl + 1) : "") : ""), 11, new Color(0.75f, 0.85f, 0.95f, 0.85f));
                    _ownedCells.Add(cell.gameObject);
                    col++; if (col >= 2) { col = 0; row++; }
                }
                if (es.Instances.Count == 0) _ownedCells.Add(T(_ownedGrid, "none", new Vector2(4f, -8f), new Vector2(600f, 40f), "보유 에코 없음 — 그림자를 정화하면 에코가 남습니다", 14, UIKit.Theme.TextLo).gameObject);
                int rows = row + (col > 0 ? 1 : 0);
                _ownedGrid.sizeDelta = new Vector2(0f, Mathf.Max(600f, rows * 92f + 8f));
            }
            RefreshEchoDetail(es);
        }

        void RefreshEchoDetail(EchoSystem es)
        {
            var inst = SelInst();
            bool has = inst != null;
            _enhanceBtn.gameObject.SetActive(has); _tuneBtn.gameObject.SetActive(has); _retuneBtn.gameObject.SetActive(has);
            if (!has)
            {
                _echoDetail.text = "슬롯 " + (_selSlot + 1) + " — 비어있음\n\n왼쪽 목록의 에코를 클릭해 장착하세요.\n같은 종류라도 개체마다 메인·부가 스탯이 다릅니다.\n\n합성: 같은 ★ 5개(미장착)를 원하는 종류·메인 스탯으로 다시 빚습니다.";
                return;
            }
            var d = inst.Def;
            string subs = "";
            for (int i = 0; i < inst.subs.Length; i++) subs += i < inst.Revealed ? "  ◇ " + inst.subs[i].Text + "\n" : "  ◇ ??? (미개방)\n";
            _echoDetail.text = d.name + (inst.level > 0 ? " +" + inst.level : "") + "  " + new string('★', d.star) + "\n코스트 " + d.cost + " · " + (d.family == EchoFamily.Shadow ? "그림자 계열" : "수호 계열") +
                "\n\n◆ 메인 스탯\n  " + inst.main.Text + "\n\n◇ 부가 스탯 (" + inst.Revealed + "/" + inst.subs.Length + ")\n" + subs + "\n◈ 패시브  " + PassiveText(d) + (_selSlot == 0 ? "\n◉ Q  " + d.activeName + " — " + d.activeDesc : "");
            bool maxed = inst.level >= EchoInstance.MaxLevel;
            _enhanceBtn.GetComponentInChildren<Text>().text = maxed ? "최대 강화 (+5)" : "강화 +" + (inst.level + 1) + "  (조각소리 " + EchoStats.EnhanceCost(inst) + ")";
            _enhanceBtn.interactable = !maxed;
            string why; bool canTune = es.CanTune(inst.uid, out why);
            _tuneBtn.GetComponentInChildren<Text>().text = inst.Revealed >= inst.subs.Length ? "부옵 전부 개방됨" : canTune ? "조율 — 부옵 개방  (50 + 조율기 1)" : "조율 — " + why;
            _tuneBtn.interactable = canTune;
            _retuneBtn.GetComponentInChildren<Text>().text = "재조율 — 부옵 1개 리롤  (80 + 조율기 1)";
            _retuneBtn.interactable = inst.Revealed > 0;
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

        void OnSlotClicked(int slot)
        {
            if (_selSlot == slot)
            {
                if (EchoSystem.I != null && EchoSystem.I.Equipped(_member, slot) >= 0) EchoSystem.I.Unequip(_member, slot);
            }
            else _selSlot = slot;
            Refresh();
        }

        void RetunePrompt()
        {
            var inst = SelInst();
            if (inst == null || EchoSystem.I == null) return;
            var opts = new List<string>();
            for (int i = 0; i < inst.Revealed; i++) opts.Add(inst.subs[i].Text);
            opts.Add("취소");
            int uid = inst.uid;
            Modal.Choice("재조율 — 부옵 선택", "선택한 부옵 하나만 다시 굴립니다. (조각소리 80 + 조율기 1)", opts.ToArray(), pick => { if (pick >= 0 && pick < opts.Count - 1) EchoSystem.I.RetuneSub(uid, pick); }, opts.Count - 1);
        }

        void MergePrompt()
        {
            var es = EchoSystem.I;
            if (es == null) return;
            int[] stars = { 1, 3, 5 };
            var counts = new int[3];
            foreach (var e in es.Instances) { int m, s; if (es.EquipLocation(e.uid, out m, out s)) continue; int si = System.Array.IndexOf(stars, e.Def.star); if (si >= 0) counts[si]++; }
            var opts = new List<string>();
            for (int i = 0; i < 3; i++) opts.Add("★" + stars[i] + "  (미장착 " + counts[i] + "/5)");
            opts.Add("취소");
            Modal.Choice("합성 — 등급 선택", "같은 등급 미장착 에코 5개를 소모해 원하는 종류와 메인 스탯의 새 에코 1개를 만듭니다.", opts.ToArray(), pick =>
            {
                if (pick < 0 || pick >= 3) return;
                if (counts[pick] < 5) { HUDController.Toast("미장착 ★" + stars[pick] + " 에코가 5개 필요합니다 (" + counts[pick] + "/5)"); return; }
                MergePickDef(stars[pick]);
            }, 3);
        }

        void MergePickDef(int star)
        {
            var defs = new List<EchoDef>();
            foreach (var d in EchoDB.All) if (d.star == star) defs.Add(d);
            var opts = new List<string>();
            foreach (var d in defs) opts.Add(d.name);
            opts.Add("취소");
            Modal.Choice("합성 — 종류 선택 (★" + star + ")", "만들 에코의 종류를 고르세요.", opts.ToArray(), pick =>
            {
                if (pick < 0 || pick >= defs.Count) return;
                MergePickMain(star, defs[pick]);
            }, defs.Count);
        }

        void MergePickMain(int star, EchoDef def)
        {
            var types = EchoStats.MainPool(def.cost);
            var opts = new List<string>();
            foreach (var t in types) opts.Add(EchoStats.NameOf(t));
            opts.Add("취소");
            int cost = EchoStats.MergeCost(star);
            Modal.Choice("합성 — 메인 스탯 선택", def.name + " · 조각소리 " + cost + " · 미장착 ★" + star + " 5개 소모 (낮은 강화 순)", opts.ToArray(), pick =>
            {
                if (pick < 0 || pick >= types.Length) return;
                if (EchoSystem.I != null) EchoSystem.I.Merge(star, def.id, types[pick]);
            }, types.Length);
        }
    }
}
