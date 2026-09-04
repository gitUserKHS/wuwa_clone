using System;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Confirm / Choice / HoldConfirm dialogs on the Modal layer. ESC/B = cancel.
    public class ConfirmScreen : UIScreen
    {
        public class Args
        {
            public string title, body;
            public string[] options;          // buttons, left to right
            public int dangerIndex = -1;      // which option is destructive (tinted, hold-to-confirm)
            public float holdSeconds;         // >0: dangerIndex button must be held
            public Action<int> onPick;        // index or -1 for cancel
            public int cancelIndex = -1;      // which option counts as cancel (default: last)
        }

        public override string Id { get { return "Confirm"; } }
        public override UILayer Layer { get { return UILayer.Modal; } }
        Args _args;
        Image _dim;
        Image _panel;
        Text _title, _body;
        readonly System.Collections.Generic.List<Button> _buttons = new System.Collections.Generic.List<Button>();
        Selectable _default;
        bool _done;

        protected override void Build()
        {
            _dim = UIKit.Img("dim", Root, new Color(0f, 0f, 0f, 0.62f), null, true);
            UIKit.Stretch(_dim.rectTransform);
            _panel = UIKit.Panel("panel", Root, new Color(0.07f, 0.085f, 0.11f, 0.98f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, 300f));
            var band = UIKit.Img("band", _panel.transform, UIKit.Theme.Accent);
            var brt = band.rectTransform; brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f); brt.anchoredPosition = Vector2.zero; brt.sizeDelta = new Vector2(0f, 2f);
            _title = UIKit.Txt("title", _panel.transform, new Vector2(0f, 1f), new Vector2(32f, -26f), new Vector2(620f, 34f), "", 24, new Color(1f, 0.93f, 0.75f), TextAnchor.MiddleLeft, true);
            _body = UIKit.Txt("body", _panel.transform, new Vector2(0f, 1f), new Vector2(32f, -74f), new Vector2(616f, 120f), "", 17, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
        }

        public override void OnOpen(object args)
        {
            _args = args as Args;
            _done = false;
            foreach (var b in _buttons) if (b != null) { b.gameObject.SetActive(false); Destroy(b.gameObject); }   // inactive now so focus collection skips them
            _buttons.Clear();
            if (_args == null) { _args = new Args { title = "", body = "", options = new[] { "확인" } }; }
            _title.text = _args.title;
            _body.text = _args.body;
            int n = _args.options.Length;
            float w = Mathf.Min(220f, (620f - (n - 1) * 16f) / n);
            float totalW = n * w + (n - 1) * 16f;
            _default = null;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                bool danger = i == _args.dangerIndex;
                float x = -totalW * 0.5f + i * (w + 16f);
                var b = UIKit.Btn("opt" + i, _panel.transform, new Vector2(0.5f, 0f), new Vector2(x + w * 0.5f, 34f), new Vector2(w, 48f),
                    danger && _args.holdSeconds > 0f ? _args.options[i] + " (홀드)" : _args.options[i],
                    danger ? UIKit.Theme.Danger : (i == CancelIndex ? UIKit.Theme.Button : UIKit.Theme.Confirm), null);
                var rt = b.GetComponent<RectTransform>(); rt.pivot = new Vector2(0.5f, 0f);
                if (danger && _args.holdSeconds > 0f)
                {
                    var hold = b.gameObject.AddComponent<OptionsPanel.HoldButton>();
                    hold.holdSeconds = _args.holdSeconds;
                    hold.onHeld = () => Pick(idx);
                    hold.label = b.GetComponentInChildren<Text>();
                    hold.fill = UIKit.Img("hold", b.transform, new Color(1f, 0.4f, 0.3f, 0.45f));
                    var frt = hold.fill.rectTransform; frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(0f, 1f); frt.pivot = new Vector2(0f, 0.5f); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
                    hold.fill.transform.SetAsFirstSibling();
                    hold.width = w;
                }
                else b.onClick.AddListener(() => Pick(idx));
                _buttons.Add(b);
                // destructive dialogs focus 취소 first; ordinary ones (warp, save) focus the primary option
                bool risky = _args.dangerIndex >= 0 || _args.holdSeconds > 0f;
                if (i == (risky ? CancelIndex : 0)) _default = b;
            }
            if (_default == null && _buttons.Count > 0) _default = _buttons[0];
            FocusNavigator.MarkDirty();
        }

        int CancelIndex { get { return _args == null ? -1 : (_args.cancelIndex >= 0 ? _args.cancelIndex : _args.options.Length - 1); } }
        public override Selectable DefaultFocus { get { return _default; } }

        void Pick(int idx)
        {
            if (_done) return;
            _done = true;
            var cb = _args != null ? _args.onPick : null;
            ScreenRouter.Pop();
            if (cb != null) cb(idx);
        }

        public override bool OnBack()
        {
            Pick(-1);
            return true;
        }
    }

    public static class Modal
    {
        public static void Confirm(string title, string body, string ok, string cancel, bool danger, Action onOk, Action onCancel = null)
        {
            ScreenRouter.Push("Confirm", new ConfirmScreen.Args
            {
                title = title, body = body, options = new[] { ok, cancel }, dangerIndex = danger ? 0 : -1, cancelIndex = 1,
                onPick = i => { if (i == 0) { if (onOk != null) onOk(); } else if (onCancel != null) onCancel(); }
            });
        }

        public static void HoldConfirm(string title, string body, string ok, float seconds, Action onOk)
        {
            ScreenRouter.Push("Confirm", new ConfirmScreen.Args
            {
                title = title, body = body, options = new[] { ok, "취소" }, dangerIndex = 0, holdSeconds = seconds, cancelIndex = 1,
                onPick = i => { if (i == 0 && onOk != null) onOk(); }
            });
        }

        public static void Choice(string title, string body, string[] options, Action<int> pick, int cancelIndex = -1)
        {
            ScreenRouter.Push("Confirm", new ConfirmScreen.Args { title = title, body = body, options = options, cancelIndex = cancelIndex, onPick = pick });
        }
    }
}
