using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Title over the live world (the camera orbits the idle party). It is the
    /// bottom of the stack while no session runs and cannot be closed with ESC —
    /// only 새로 시작 / 이어하기 leave it.
    public class TitleScreen : UIScreen
    {
        public override string Id { get { return "Title"; } }
        public override string Title { get { return "타이틀"; } }
        public override bool PausesTime { get { return false; } }

        Button _continue, _new, _load, _settings, _quit;
        Text _latest, _version, _hint;
        RawImage _thumb; Texture2D _thumbTex;

        protected override void Build()
        {
            var shade = UIKit.Img("shade", Root, new Color(0.02f, 0.03f, 0.05f, 0.22f), null, true);
            UIKit.Stretch(shade.rectTransform);
            var left = UIKit.Img("left", Root, new Color(0.02f, 0.03f, 0.05f, 0.7f));
            var lrt = left.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = new Vector2(0.4f, 1f); lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var edge = UIKit.Img("edge", Root, new Color(1f, 0.82f, 0.35f, 0.35f));
            var ert = edge.rectTransform; ert.anchorMin = new Vector2(0.4f, 0f); ert.anchorMax = new Vector2(0.4f, 1f); ert.pivot = new Vector2(0f, 0.5f); ert.anchoredPosition = Vector2.zero; ert.sizeDelta = new Vector2(2f, 0f);

            UIKit.Txt("logo", Root, new Vector2(0f, 1f), new Vector2(140f, -190f), new Vector2(700f, 130f), "잔향", 104, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true, true);
            UIKit.Txt("sub", Root, new Vector2(0f, 1f), new Vector2(146f, -300f), new Vector2(700f, 34f), "명조풍 오픈월드 액션 데모  ·  Wuthering Waves clone", 20, UIKit.Theme.TextLo, TextAnchor.MiddleLeft);
            var band = UIKit.Img("band", Root, new Color(1f, 0.82f, 0.35f, 0.5f));
            var brt = band.rectTransform; brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 1f); brt.anchoredPosition = new Vector2(140f, -340f); brt.sizeDelta = new Vector2(420f, 2f);

            _continue = Entry(0, "이어하기", Continue);
            _new = Entry(1, "새로 시작", NewGame);
            _load = Entry(2, "불러오기", () => ScreenRouter.Push("Slots", "load"));
            _settings = Entry(3, "설정", () => ScreenRouter.Push("Settings"));
            _quit = Entry(4, "게임 종료", () => Modal.Confirm("게임 종료", "게임을 종료할까요?", "종료", "취소", true, () => Application.Quit()));

            var tgo = new GameObject("thumb"); tgo.transform.SetParent(Root, false);
            var trt = tgo.AddComponent<RectTransform>(); trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 1f); trt.anchoredPosition = new Vector2(140f, -716f); trt.sizeDelta = new Vector2(192f, 108f);
            _thumb = tgo.AddComponent<RawImage>(); _thumb.raycastTarget = false; _thumb.gameObject.SetActive(false);
            _latest = UIKit.Txt("latest", Root, new Vector2(0f, 1f), new Vector2(348f, -716f), new Vector2(400f, 110f), "", 14, UIKit.Theme.TextLo, TextAnchor.UpperLeft);
            _hint = UIKit.Txt("hint", Root, new Vector2(0f, 0f), new Vector2(140f, 34f), new Vector2(700f, 24f), "", 13, UIKit.Theme.TextLo, TextAnchor.MiddleLeft);
            _version = UIKit.Txt("version", Root, new Vector2(1f, 0f), new Vector2(-40f, 34f), new Vector2(600f, 24f), "", 13, UIKit.Theme.TextLo, TextAnchor.MiddleRight);
            BuildLicense();
        }

        /// The party models are Unity-chan, which ships under the Unity-Chan License 2.0. That
        /// licence requires the UCL logo to be displayed, so it lives on the title screen rather
        /// than buried in a text file nobody ships.
        void BuildLicense()
        {
            var logo = Resources.Load<Texture2D>("UI/ucl_logo");
            float x = -40f, w = 0f;
            if (logo != null)
            {
                var go = new GameObject("uclLogo");
                go.transform.SetParent(Root, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(1f, 0f);
                rt.anchoredPosition = new Vector2(x, 68f);
                float k = 58f / logo.height;
                rt.sizeDelta = new Vector2(logo.width * k, 58f);
                var img = go.AddComponent<RawImage>();
                img.texture = logo;
                img.color = new Color(1f, 1f, 1f, 0.72f);
                img.raycastTarget = false;
                w = rt.sizeDelta.x + 12f;
            }
            UIKit.Txt("uclText", Root, new Vector2(1f, 0f), new Vector2(x - w, 78f), new Vector2(420f, 40f),
                "Character model: ⓒ Unity Technologies Japan/UCL\nLicensed under the Unity-Chan License 2.0",
                12, UIKit.Theme.TextLo, TextAnchor.LowerRight);
        }

        Button Entry(int i, string label, System.Action onClick)
        {
            var b = UIKit.Btn("entry" + i, Root, new Vector2(0f, 1f), new Vector2(140f, -380f - i * 64f), new Vector2(360f, 54f), label, UIKit.Theme.Button, onClick, 20);
            var lt = b.GetComponentInChildren<Text>();
            lt.alignment = TextAnchor.MiddleLeft;
            lt.rectTransform.anchoredPosition = new Vector2(26f, 0f);
            return b;
        }

        public override Selectable DefaultFocus { get { return _continue.gameObject.activeSelf ? _continue : _new; } }
        public override bool OnBack() { return true; }        // the title never pops

        public override void OnOpen(object args) { Refresh(); }

        void Refresh()
        {
            var heads = SaveSystem.ReadHeaders();
            int latest = SaveSystem.LatestSlot(heads);
            _continue.gameObject.SetActive(latest >= 0);
            if (_thumbTex != null) { Destroy(_thumbTex); _thumbTex = null; }
            _thumbTex = latest >= 0 ? SaveSystem.LoadThumb(latest) : null;
            _thumb.texture = _thumbTex; _thumb.gameObject.SetActive(_thumbTex != null);
            if (_thumbTex == null) _latest.rectTransform.anchoredPosition = new Vector2(140f, -716f); else _latest.rectTransform.anchoredPosition = new Vector2(348f, -716f);
            if (latest >= 0)
            {
                var h = heads[latest];
                _latest.text = "최근 저장  ·  " + SaveSystem.SlotName(latest) + "\n" + SaveSystem.Describe(h, false);
            }
            else _latest.text = "저장된 여정이 없습니다. 새로 시작하세요.";
            _hint.text = Glyph.Key("UI/Submit", "Enter") + " 선택   ·   방향키/스틱 이동   ·   " + Glyph.Key("UI/Cancel", "Esc") + " 하위 화면 닫기";
            _version.text = "v" + Application.version + "   ·   Unity " + Application.unityVersion + "   ·   " + (InputService.GamepadActive ? "패드" : "키보드/마우스");
        }

        void Continue()
        {
            int s = SaveSystem.LatestSlot(SaveSystem.ReadHeaders());
            if (s < 0) { HUDController.Toast("저장된 여정이 없습니다"); return; }
            if (GameDirector.I != null) GameDirector.I.BeginContinue(s);
        }

        void NewGame()
        {
            if (SaveSystem.SlotExists(0))
                Modal.Confirm("새로 시작", "자동 저장 슬롯의 진행이 새 여정으로 덮어써집니다.\n수동 슬롯 1~3은 그대로 남습니다.", "새로 시작", "취소", true,
                    () => { if (GameDirector.I != null) GameDirector.I.BeginNewGame(); });
            else if (GameDirector.I != null) GameDirector.I.BeginNewGame();
        }

        public override void OnTick()
        {
            if (Time.frameCount % 30 == 0 && _version != null)
                _version.text = "v" + Application.version + "   ·   Unity " + Application.unityVersion + "   ·   " + (InputService.GamepadActive ? "패드" : "키보드/마우스");
        }
    }
}
