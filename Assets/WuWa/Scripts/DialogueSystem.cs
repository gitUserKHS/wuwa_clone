using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WuWa
{
    /// Lightweight NPC dialogue box: speaker name, typewriter body, advance
    /// with F / Space / E / click. Suppresses game input while open (time keeps
    /// running so the world stays alive behind the conversation).
    public class DialogueSystem : MonoBehaviour
    {
        public static bool Active { get; private set; }
        static DialogueSystem _inst;

        Canvas _canvas;
        Font _font;
        GameObject _root;
        Text _speaker, _body, _hint;
        Image _portraitFrame;
        string[] _lines;
        int _index;
        float _charTimer;
        int _shown;
        Action _onDone;
        float _openedAt;
        float _lineDoneAt;
        public static float CharsPerSecond = 42f;
        public static bool AutoAdvance;
        public static float TextScale = 1f;

        public static void Show(string speaker, string[] lines, Action onDone = null)
        {
            if (lines == null || lines.Length == 0) { if (onDone != null) onDone(); return; }
            if (_inst == null)
            {
                var go = new GameObject("DialogueSystem");
                _inst = go.AddComponent<DialogueSystem>();
                _inst.Build();
            }
            _inst.Begin(speaker, lines, onDone);
        }

        static Font GetFont()
        {
            string[] names = { "Malgun Gothic", "malgun", "Segoe UI", "Arial" };
            foreach (var n in names)
            {
                try { var f = Font.CreateDynamicFontFromOSFont(n, 22); if (f != null) return f; } catch { }
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void Build()
        {
            _font = GetFont();
            var cgo = new GameObject("DialogueCanvas");
            cgo.transform.SetParent(transform, false);
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 90;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("box");
            _root.transform.SetParent(cgo.transform, false);
            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 150f);
            rt.sizeDelta = new Vector2(1180f, 190f);

            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.05f, 0.08f, 0.86f);
            bg.raycastTarget = false;

            var band = MakeImg("band", _root.transform, new Color(1f, 0.82f, 0.4f, 0.9f));
            var brt = band.rectTransform;
            brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f); brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(0f, 2f);

            _speaker = MakeText("speaker", _root.transform, new Vector2(0f, 1f), new Vector2(28f, -14f), new Vector2(600f, 30f), 22,
                new Color(1f, 0.88f, 0.55f), TextAnchor.UpperLeft, true);
            _body = MakeText("body", _root.transform, new Vector2(0f, 1f), new Vector2(28f, -54f), new Vector2(1120f, 110f), 21,
                new Color(1f, 1f, 1f, 0.95f), TextAnchor.UpperLeft, false);
            _hint = MakeText("hint", _root.transform, new Vector2(1f, 0f), new Vector2(-24f, 12f), new Vector2(400f, 24f), 14,
                new Color(1f, 1f, 1f, 0.5f), TextAnchor.LowerRight, false);
            _hint.text = "F · Space · 클릭  →  계속";
            _root.SetActive(false);
        }

        Image MakeImg(string n, Transform p, Color c)
        {
            var go = new GameObject(n);
            go.transform.SetParent(p, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        Text MakeText(string n, Transform p, Vector2 anchor, Vector2 pos, Vector2 size, int fs, Color c, TextAnchor align, bool bold)
        {
            var go = new GameObject(n);
            go.transform.SetParent(p, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = fs; t.color = c; t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        void Begin(string speaker, string[] lines, Action onDone)
        {
            _lines = lines;
            _index = 0;
            _onDone = onDone;
            _speaker.text = speaker;
            _hint.text = Glyph.Key("Dialogue/Advance", "F") + " · Space · 클릭  →  계속";
            _body.fontSize = Mathf.RoundToInt(21f * TextScale);
            _shown = 0;
            _charTimer = 0f;
            _body.text = "";
            _root.SetActive(true);
            Active = true;
            GameDirector.MenuOpen = true;
            _openedAt = Time.unscaledTime;
            AudioMan.I.Play2D(Sfx.Swap(), 0.25f, 1.8f);
        }

        void Update()
        {
            if (!Active) return;
            string line = _lines[_index];
            if (_shown < line.Length)
            {
                _charTimer += Time.unscaledDeltaTime;
                int want = Mathf.Min(line.Length, _shown + Mathf.FloorToInt(_charTimer * CharsPerSecond));
                if (want != _shown) { _shown = want; _charTimer = 0f; _body.text = line.Substring(0, _shown); if (_shown >= line.Length) _lineDoneAt = Time.unscaledTime; }
            }
            if (Time.unscaledTime - _openedAt < 0.25f) return;      // swallow the press that opened us

            bool adv = InputService.DialogueAdvancePressed;
            bool esc = InputService.DialogueSkipPressed;
            if (AutoAdvance && _shown >= line.Length && Time.unscaledTime - _lineDoneAt > 1.6f) adv = true;
            if (!adv && !esc) return;

            if (_shown < line.Length && !esc)
            {
                _shown = line.Length;
                _body.text = line;
                _lineDoneAt = Time.unscaledTime;
                return;
            }
            _index++;
            if (esc || _index >= _lines.Length) Finish();
            else
            {
                _shown = 0; _charTimer = 0f; _body.text = "";
                AudioMan.I.Play2D(Sfx.Swap(), 0.15f, 2.2f);
            }
        }

        void Finish()
        {
            _root.SetActive(false);
            Active = false;
            GameDirector.MenuOpen = false;
            var cb = _onDone;
            _onDone = null;
            if (cb != null) cb();
        }
    }
}
