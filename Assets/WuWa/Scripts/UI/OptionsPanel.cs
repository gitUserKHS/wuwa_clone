using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WuWa
{
    /// Settings screen body generated from SettingsStore.Defs: a tab rail on the
    /// left and a scrolling list of rows on the right. Lives inside GameMenus'
    /// options root until the S2 UI kit replaces the shell.
    public static class OptionsPanel
    {
        static Transform _root;
        static Font _font;
        static Sprite _white, _circle;
        static RectTransform _content;
        static ScrollRect _scroll;
        static readonly List<Button> _tabButtons = new List<Button>();
        static readonly List<Action> _rowRefresh = new List<Action>();
        static int _tab;
        static Text _tip;
        static Action _close;

        public const float RowHeight = 54f;
        const float RowH = RowHeight;
        /// (tab, content, firstRow) → rows added. Used by RebindUI for the 조작 tab.
        public static Func<string, Transform, int, int> ExtraBuilder;
        public static Selectable FirstTab { get { return _tabButtons.Count > 0 ? _tabButtons[0] : null; } }
        public static void CycleTab(int dir) { SelectTab((_tab + dir + SettingsCatalog.Tabs.Length) % SettingsCatalog.Tabs.Length); }

        public static void Build(Transform root, Font font, Sprite white, Sprite circle, Action close)
        {
            _root = root; _font = font; _white = white; _circle = circle; _close = close;
            _tabButtons.Clear();

            // tab rail
            for (int i = 0; i < SettingsCatalog.Tabs.Length; i++)
            {
                int idx = i;
                var b = Btn("tab" + i, root, new Vector2(60f, -140f - i * 58f), new Vector2(200f, 50f), SettingsCatalog.Tabs[i],
                    new Color(0.16f, 0.19f, 0.22f, 1f), () => SelectTab(idx));
                _tabButtons.Add(b);
            }
            Btn("reset", root, new Vector2(60f, -140f - SettingsCatalog.Tabs.Length * 58f - 20f), new Vector2(200f, 44f), "이 탭 초기화",
                new Color(0.3f, 0.2f, 0.16f, 1f), () => { SettingsStore.ResetTab(SettingsCatalog.Tabs[_tab]); Refresh(); });
            Btn("close", root, new Vector2(60f, -140f - SettingsCatalog.Tabs.Length * 58f - 74f), new Vector2(200f, 44f), "닫기 (ESC)",
                new Color(0.18f, 0.2f, 0.24f, 1f), () => { if (_close != null) _close(); });

            // scroll view
            var viewGo = new GameObject("view");
            viewGo.transform.SetParent(root, false);
            var vrt = viewGo.AddComponent<RectTransform>();
            vrt.anchorMin = vrt.anchorMax = new Vector2(0f, 1f);
            vrt.pivot = new Vector2(0f, 1f);
            vrt.anchoredPosition = new Vector2(300f, -140f);
            vrt.sizeDelta = new Vector2(1180f, 760f);
            var vimg = viewGo.AddComponent<Image>();
            vimg.sprite = white; vimg.color = new Color(1f, 1f, 1f, 0.04f);
            viewGo.AddComponent<RectMask2D>();
            _scroll = viewGo.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;

            var contentGo = new GameObject("content");
            contentGo.transform.SetParent(viewGo.transform, false);
            _content = contentGo.AddComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(0f, 800f);
            _scroll.content = _content;
            _scroll.viewport = vrt;

            _tip = Txt("tip", root, new Vector2(0f, 1f), new Vector2(300f, -910f), new Vector2(1180f, 40f), "", 14,
                new Color(1f, 1f, 1f, 0.55f), TextAnchor.UpperLeft, false);

            SelectTab(0);
        }

        static void SelectTab(int i)
        {
            _tab = Mathf.Clamp(i, 0, SettingsCatalog.Tabs.Length - 1);
            for (int k = 0; k < _tabButtons.Count; k++)
            {
                var img = _tabButtons[k].GetComponent<Image>();
                img.color = k == _tab ? new Color(0.30f, 0.26f, 0.12f, 1f) : new Color(0.16f, 0.19f, 0.22f, 1f);
            }
            Rebuild();
        }

        static void Rebuild()
        {
            if (_content == null) return;
            foreach (Transform ch in _content) UnityEngine.Object.Destroy(ch.gameObject);
            _rowRefresh.Clear();
            string tab = SettingsCatalog.Tabs[_tab];
            int row = 0;
            foreach (var d in SettingsStore.Defs)
            {
                if (d.tab != tab) continue;
                BuildRow(d, row++);
            }
            if (ExtraBuilder != null) row += ExtraBuilder(tab, _content, row);
            _content.sizeDelta = new Vector2(0f, Mathf.Max(760f, row * RowH + 20f));
            _content.anchoredPosition = Vector2.zero;
            FocusNavigator.MarkDirty();
            if (_tip != null) _tip.text = tab == SettingsCatalog.TabGraphics ? "창 모드·해상도는 즉시 적용됩니다. 품질 프리셋을 고르면 세부 항목이 함께 바뀝니다." :
                tab == SettingsCatalog.TabControls ? "감도·데드존·진동·질주 방식은 즉시 반영됩니다. 아래 키 설정에서 슬롯을 클릭해 키/버튼을 바꾸세요 (↺ = 기본값)." : "";
        }

        public static void Refresh()
        {
            for (int i = 0; i < _rowRefresh.Count; i++) _rowRefresh[i]();
        }

        static void BuildRow(SettingDef d, int row)
        {
            float y = -10f - row * RowH;
            var rowGo = new GameObject("row_" + d.key);
            rowGo.transform.SetParent(_content, false);
            var rrt = rowGo.AddComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0f, 1f); rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.anchoredPosition = new Vector2(0f, y);
            rrt.sizeDelta = new Vector2(0f, RowH);
            var bg = rowGo.AddComponent<Image>();
            bg.sprite = _white; bg.color = new Color(1f, 1f, 1f, row % 2 == 0 ? 0.025f : 0.045f);
            bg.raycastTarget = false;

            var label = Txt("lb", rowGo.transform, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(420f, 30f), d.label, 16,
                new Color(1f, 1f, 1f, 0.9f), TextAnchor.MiddleLeft, false);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            if (!string.IsNullOrEmpty(d.tooltip))
            {
                var tt = Txt("tt", rowGo.transform, new Vector2(0f, 0.5f), new Vector2(24f, -16f), new Vector2(480f, 20f), d.tooltip, 12,
                    new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleLeft, false);
                tt.rectTransform.pivot = new Vector2(0f, 0.5f);
                label.rectTransform.anchoredPosition = new Vector2(24f, 8f);
            }

            switch (d.kind)
            {
                case SettingKind.Slider: BuildSlider(d, rowGo.transform); break;
                case SettingKind.Toggle: BuildToggle(d, rowGo.transform); break;
                case SettingKind.Cycle: BuildCycle(d, rowGo.transform); break;
                default: BuildButton(d, rowGo.transform); break;
            }
        }

        static void BuildSlider(SettingDef d, Transform p)
        {
            var valTxt = Txt("val", p, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(90f, 26f), "", 15,
                new Color(1f, 0.9f, 0.6f, 1f), TextAnchor.MiddleRight, false);
            valTxt.rectTransform.pivot = new Vector2(1f, 0.5f);

            var track = Img("track", p, new Color(0.16f, 0.19f, 0.22f, 1f));
            var trt = track.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(1f, 0.5f);
            trt.pivot = new Vector2(1f, 0.5f);
            trt.anchoredPosition = new Vector2(-124f, 0f);
            trt.sizeDelta = new Vector2(420f, 10f);

            var fill = Img("fill", track.transform, new Color(1f, 0.8f, 0.35f, 0.95f));
            var frt = fill.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 0.5f); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

            var handle = Img("handle", track.transform, Color.white);
            handle.sprite = _circle;
            handle.rectTransform.sizeDelta = new Vector2(24f, 24f);

            var slider = track.gameObject.AddComponent<Slider>();
            slider.targetGraphic = handle;
            slider.fillRect = frt;
            slider.handleRect = handle.rectTransform;
            slider.minValue = d.min; slider.maxValue = d.max;
            slider.wholeNumbers = d.step >= 1f;
            bool percent = d.max <= 2.01f && d.min >= -1.01f && d.step < 1f;
            Action refresh = () =>
            {
                float v = Convert.ToSingle(d.get());
                slider.SetValueWithoutNotify(v);
                valTxt.text = FormatValue(d, v, percent);
            };
            refresh();
            slider.onValueChanged.AddListener(v =>
            {
                if (d.step > 0f) v = Mathf.Round(v / d.step) * d.step;
                SettingsStore.Set(d.key, v);
                valTxt.text = FormatValue(d, v, percent);
            });
            _rowRefresh.Add(refresh);
        }

        static string FormatValue(SettingDef d, float v, bool percent)
        {
            if (d.key == "gfx.brightness") return (v >= 0 ? "+" : "") + v.ToString("F1") + " EV";
            if (d.key == "gfx.fov") return Mathf.RoundToInt(v) + "°";
            if (d.key.StartsWith("ctl.camDistance") || d.key == "ctl.camCombatDistance") return v.ToString("F1") + " m";
            if (d.key.StartsWith("ctl.deadzone") || d.key == "ctl.trigger") return Mathf.RoundToInt(v * 100f) + "%";
            if (percent) return Mathf.RoundToInt(v * 100f) + "%";
            return d.step >= 1f ? Mathf.RoundToInt(v).ToString() : v.ToString("F2");
        }

        static void BuildToggle(SettingDef d, Transform p)
        {
            Text lbl = null;
            var b = Btn("tg", p, Vector2.zero, new Vector2(140f, 40f), "-", new Color(0.16f, 0.19f, 0.22f, 1f), () =>
            {
                bool cur = Convert.ToBoolean(d.get());
                SettingsStore.Set(d.key, !cur);
                Refresh();
            });
            var brt = b.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.anchoredPosition = new Vector2(-24f, 0f);
            lbl = b.GetComponentInChildren<Text>();
            Action refresh = () =>
            {
                bool on = Convert.ToBoolean(d.get());
                lbl.text = on ? "켜짐" : "꺼짐";
                lbl.color = on ? new Color(0.6f, 1f, 0.7f, 1f) : new Color(1f, 1f, 1f, 0.45f);
            };
            refresh();
            _rowRefresh.Add(refresh);
        }

        static void BuildCycle(SettingDef d, Transform p)
        {
            var val = Txt("val", p, new Vector2(1f, 0.5f), new Vector2(-84f, 0f), new Vector2(300f, 30f), "", 16,
                new Color(1f, 0.92f, 0.7f, 1f), TextAnchor.MiddleCenter, false);
            val.rectTransform.pivot = new Vector2(1f, 0.5f);
            Action<int> step = dir =>
            {
                int cur = Convert.ToInt32(d.get());
                int n = d.options.Length;
                SettingsStore.Set(d.key, (cur + dir + n) % n);
                Refresh();
            };
            var left = Btn("lt", p, Vector2.zero, new Vector2(44f, 40f), "◄", new Color(0.16f, 0.19f, 0.22f, 1f), () => step(-1));
            var lrt = left.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(1f, 0.5f); lrt.pivot = new Vector2(1f, 0.5f); lrt.anchoredPosition = new Vector2(-384f, 0f);
            var right = Btn("rt", p, Vector2.zero, new Vector2(44f, 40f), "►", new Color(0.16f, 0.19f, 0.22f, 1f), () => step(1));
            var rrt = right.GetComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 0.5f); rrt.pivot = new Vector2(1f, 0.5f); rrt.anchoredPosition = new Vector2(-24f, 0f);
            Action refresh = () =>
            {
                int cur = Mathf.Clamp(Convert.ToInt32(d.get()), 0, d.options.Length - 1);
                val.text = d.options[cur];
            };
            refresh();
            _rowRefresh.Add(refresh);
        }

        static void BuildButton(SettingDef d, Transform p)
        {
            var b = Btn("btn", p, Vector2.zero, new Vector2(360f, 40f), d.dangerous ? "길게 눌러 실행" : "실행",
                d.dangerous ? new Color(0.32f, 0.16f, 0.14f, 1f) : new Color(0.20f, 0.30f, 0.22f, 1f), () => { if (!d.dangerous && d.onClick != null) d.onClick(); });
            var brt = b.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.anchoredPosition = new Vector2(-24f, 0f);
            if (d.dangerous)
            {
                var hold = b.gameObject.AddComponent<HoldButton>();
                hold.holdSeconds = 2f;
                hold.onHeld = d.onClick;
                hold.label = b.GetComponentInChildren<Text>();
                hold.fill = Img("hold", b.transform, new Color(1f, 0.4f, 0.3f, 0.45f));
                var frt = hold.fill.rectTransform;
                frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(0f, 1f);
                frt.pivot = new Vector2(0f, 0.5f); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
                hold.fill.raycastTarget = false;
                hold.fill.transform.SetAsFirstSibling();
                hold.width = 360f;
            }
        }

        /// Press-and-hold confirmation for destructive buttons.
        public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
        {
            public float holdSeconds = 2f;
            public Action onHeld;
            public Text label;
            public Image fill;
            public float width = 360f;
            float _held = -1f;
            void Update()
            {
                if (_held < 0f) return;
                float t = (Time.unscaledTime - _held) / holdSeconds;
                if (fill != null) fill.rectTransform.sizeDelta = new Vector2(width * Mathf.Clamp01(t), 0f);
                if (t >= 1f)
                {
                    _held = -1f;
                    if (fill != null) fill.rectTransform.sizeDelta = new Vector2(0f, 0f);
                    if (label != null) label.text = "실행됨";
                    if (onHeld != null) onHeld();
                }
            }
            public void OnPointerDown(PointerEventData e) { _held = Time.unscaledTime; }
            public void SimulateDown() { _held = Time.unscaledTime; }
            public void SimulateUp() { Cancel(); }
            public void OnPointerUp(PointerEventData e) { Cancel(); }
            public void OnPointerExit(PointerEventData e) { Cancel(); }
            void Cancel() { _held = -1f; if (fill != null) fill.rectTransform.sizeDelta = new Vector2(0f, 0f); }
        }

        // ---------------------------------------------------------------- mini factory (UIKit replaces this in S2)
        static Image Img(string n, Transform p, Color c)
        {
            var go = new GameObject(n);
            go.transform.SetParent(p, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.sprite = _white; img.color = c;
            return img;
        }

        static Text Txt(string n, Transform p, Vector2 anchor, Vector2 pos, Vector2 size, string text, int fs, Color c, TextAnchor align, bool bold)
        {
            var go = new GameObject(n);
            go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = _font; t.text = text; t.fontSize = fs; t.color = c; t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static Button Btn(string n, Transform p, Vector2 pos, Vector2 size, string label, Color bg, Action onClick)
        {
            var img = Img(n, p, bg);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.2f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            b.colors = colors;
            b.onClick.AddListener(() => { if (onClick != null) onClick(); });
            var t = Txt("label", img.transform, new Vector2(0.5f, 0.5f), Vector2.zero, size, label, 16, Color.white, TextAnchor.MiddleCenter, false);
            t.raycastTarget = false;
            return b;
        }
    }
}
