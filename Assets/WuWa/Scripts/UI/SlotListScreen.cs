using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Slot list popup. "load" (title): 자동 + 슬롯 1~3 with load / delete.
    /// "save" (pause): 슬롯 1~3, empty slots save at once, filled ones confirm.
    public class SlotListScreen : UIScreen
    {
        public override string Id { get { return "Slots"; } }
        public override UILayer Layer { get { return UILayer.Popup; } }
        public override string Title { get { return "저장 슬롯"; } }

        string _mode = "load";
        Text _title, _hint;
        readonly Button[] _rows = new Button[SaveSystem.SlotCount];
        readonly Text[] _names = new Text[SaveSystem.SlotCount];
        readonly Text[] _infos = new Text[SaveSystem.SlotCount];
        readonly RawImage[] _thumbs = new RawImage[SaveSystem.SlotCount];
        readonly Texture2D[] _thumbTex = new Texture2D[SaveSystem.SlotCount];
        Button _close;

        protected override void Build()
        {
            var dim = UIKit.Img("dim", Root, new Color(0f, 0f, 0f, 0.55f), null, true);
            UIKit.Stretch(dim.rectTransform);
            var panel = UIKit.Panel("panel", Root, new Color(0.07f, 0.085f, 0.11f, 0.98f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 640f));
            var band = UIKit.Img("band", panel.transform, UIKit.Theme.Accent);
            var brt = band.rectTransform; brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f); brt.anchoredPosition = Vector2.zero; brt.sizeDelta = new Vector2(0f, 2f);
            _title = UIKit.Txt("title", panel.transform, new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(500f, 36f), "", 26, new Color(1f, 0.93f, 0.75f), TextAnchor.MiddleLeft, true);
            _hint = UIKit.Txt("hint", panel.transform, new Vector2(1f, 1f), new Vector2(-32f, -30f), new Vector2(520f, 24f), "", 13, UIKit.Theme.TextLo, TextAnchor.MiddleRight);
            for (int i = 0; i < SaveSystem.SlotCount; i++)
            {
                int idx = i;
                var b = UIKit.Btn("slot" + i, panel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -86f - i * 112f), new Vector2(836f, 100f), "-", UIKit.Theme.Cell, () => Pick(idx), 16);
                var lt = b.GetComponentInChildren<Text>();
                if (lt != null) lt.text = "";
                var tgo = new GameObject("thumb"); tgo.transform.SetParent(b.transform, false);
                var trt = tgo.AddComponent<RectTransform>(); trt.anchorMin = trt.anchorMax = new Vector2(0f, 0.5f); trt.pivot = new Vector2(0f, 0.5f); trt.anchoredPosition = new Vector2(10f, 0f); trt.sizeDelta = new Vector2(144f, 81f);
                _thumbs[i] = tgo.AddComponent<RawImage>(); _thumbs[i].raycastTarget = false; _thumbs[i].color = new Color(1f, 1f, 1f, 0.12f);
                _names[i] = UIKit.Txt("name", b.transform, new Vector2(0f, 1f), new Vector2(170f, -12f), new Vector2(600f, 30f), "", 19, UIKit.Theme.TextHi, TextAnchor.MiddleLeft, true);
                _infos[i] = UIKit.Txt("info", b.transform, new Vector2(0f, 1f), new Vector2(170f, -44f), new Vector2(650f, 54f), "", 14, UIKit.Theme.TextLo, TextAnchor.UpperLeft);
                _rows[i] = b;
            }
            _close = UIKit.Btn("close", panel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(220f, 44f), "닫기", UIKit.Theme.Button, () => ScreenRouter.Pop());
        }

        public override Selectable DefaultFocus
        {
            get
            {
                for (int i = 0; i < _rows.Length; i++) if (_rows[i] != null && _rows[i].gameObject.activeSelf) return _rows[i];
                return _close;
            }
        }

        public override void OnOpen(object args)
        {
            _mode = args as string ?? "load";
            Refresh();
        }

        void Refresh()
        {
            var hs = SaveSystem.ReadHeaders();
            int latest = SaveSystem.LatestSlot(hs);
            bool save = _mode == "save";
            _title.text = save ? "저장하기 — 슬롯 선택" : "불러오기";
            _hint.text = save ? "빈 슬롯은 바로 저장 · 채워진 슬롯은 덮어쓰기 확인" : Glyph.Key("UI/Submit", "Enter") + " 불러오기 / 삭제   ·   " + Glyph.Key("UI/Cancel", "Esc") + " 닫기";
            int vis = 0;
            for (int i = 0; i < SaveSystem.SlotCount; i++)
            {
                bool show = !save || i > 0;
                _rows[i].gameObject.SetActive(show);
                if (!show) continue;
                _rows[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -86f - vis * 112f);
                vis++;
                var h = hs[i];
                _names[i].text = SaveSystem.SlotName(i) + (i == latest ? "   ·   최근" : "") + (i == SaveSystem.ActiveSlot && SaveSystem.SessionStarted ? "   ·   현재 세션" : "");
                _infos[i].text = h != null ? SaveSystem.Describe(h, true) : "비어 있음";
                _rows[i].GetComponent<Image>().color = h != null ? UIKit.Theme.Cell : new Color(0.09f, 0.11f, 0.13f, 1f);
                if (_thumbTex[i] != null) { Destroy(_thumbTex[i]); _thumbTex[i] = null; }
                _thumbTex[i] = h != null ? SaveSystem.LoadThumb(i) : null;
                _thumbs[i].texture = _thumbTex[i];
                _thumbs[i].color = _thumbTex[i] != null ? Color.white : new Color(1f, 1f, 1f, 0.06f);
            }
            FocusNavigator.MarkDirty();
        }

        void Pick(int slot)
        {
            bool exists = SaveSystem.SlotExists(slot);
            if (_mode == "save")
            {
                if (exists)
                    Modal.Confirm("덮어쓰기", SaveSystem.SlotName(slot) + "의 저장을 현재 진행으로 덮어쓸까요?", "저장", "취소", true, () => SaveInto(slot));
                else SaveInto(slot);
                return;
            }
            if (!exists) { HUDController.Toast("비어 있는 슬롯입니다"); return; }
            Modal.Choice(SaveSystem.SlotName(slot), SaveSystem.Describe(SaveSystem.ReadHeaders()[slot], true), new[] { "불러오기", "삭제", "취소" }, k =>
            {
                if (k == 0) { if (GameDirector.I != null) GameDirector.I.BeginContinue(slot); }
                else if (k == 1)
                    Modal.HoldConfirm("슬롯 삭제", SaveSystem.SlotName(slot) + "의 저장 데이터를 지웁니다. 되돌릴 수 없습니다.", "삭제", 2f, () => { SaveSystem.DeleteSlot(slot); Refresh(); });
            }, 2);
        }

        void SaveInto(int slot)
        {
            if (SaveSystem.I != null && SaveSystem.I.SaveToSlot(slot, "수동 저장"))
            {
                Refresh();
                ScreenRouter.Pop();
            }
        }
    }
}
