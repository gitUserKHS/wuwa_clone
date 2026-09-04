using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WuWa
{
    /// Shared code-built UI kit: one font, generated sprites, theme tokens and
    /// widget factories. Every new screen builds through this (design doc 7.12).
    public static class UIKit
    {
        static Font _font;
        static Sprite _white, _dot, _ring, _rounded;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                string[] names = { "Malgun Gothic", "malgun", "Segoe UI", "Arial" };
                foreach (var n in names)
                {
                    try { var f = UnityEngine.Font.CreateDynamicFontFromOSFont(n, 22); if (f != null) { _font = f; return f; } } catch { }
                }
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Sprite White { get { if (_white == null) _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f); return _white; } }
        public static Sprite Dot { get { if (_dot == null) { var t = VFXLibrary.MakeSoftDot(); _dot = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f); } return _dot; } }
        public static Sprite Ring { get { if (_ring == null) { var t = VFXLibrary.MakeRing(); _ring = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f); } return _ring; } }
        /// 9-sliced rounded rectangle (radius 8 px at 32 px).
        public static Sprite Rounded
        {
            get
            {
                if (_rounded != null) return _rounded;
                const int n = 32; const float r = 8f;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - n * 0.5f) - (n * 0.5f - r));
                        float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - n * 0.5f) - (n * 0.5f - r));
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(r - d + 0.5f);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                tex.SetPixels32(px); tex.Apply();
                tex.wrapMode = TextureWrapMode.Clamp;
                _rounded = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
                return _rounded;
            }
        }

        public static class Theme
        {
            public static readonly Color Bg = new Color(0.055f, 0.07f, 0.085f, 1f);
            public static readonly Color Panel = new Color(1f, 1f, 1f, 0.04f);
            public static readonly Color Cell = new Color(0.12f, 0.15f, 0.17f, 1f);
            public static readonly Color Button = new Color(0.16f, 0.19f, 0.22f, 1f);
            public static readonly Color Selected = new Color(0.30f, 0.26f, 0.12f, 1f);
            public static readonly Color Accent = new Color(1f, 0.85f, 0.4f, 1f);
            public static readonly Color TextHi = new Color(1f, 1f, 1f, 0.92f);
            public static readonly Color TextLo = new Color(1f, 1f, 1f, 0.55f);
            public static readonly Color Positive = new Color(0.6f, 1f, 0.7f, 1f);
            public static readonly Color Danger = new Color(0.32f, 0.16f, 0.14f, 1f);
            public static readonly Color Info = new Color(0.7f, 0.88f, 1f, 1f);
            public static readonly Color Confirm = new Color(0.20f, 0.30f, 0.22f, 1f);
            public static Color Rarity(int star)
            {
                if (star >= 5) return new Color(1f, 0.78f, 0.3f);
                if (star >= 4) return new Color(0.8f, 0.55f, 1f);
                if (star >= 3) return new Color(0.5f, 0.75f, 1f);
                if (star >= 2) return new Color(0.5f, 0.9f, 0.6f);
                return new Color(0.8f, 0.8f, 0.8f);
            }
        }

        // ---------------------------------------------------------------- factories
        public static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        public static void Stretch(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
        }

        public static Image Img(string name, Transform parent, Color c, Sprite sprite = null, bool raycast = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : White;
            img.type = sprite == Rounded ? Image.Type.Sliced : Image.Type.Simple;
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        public static Image Panel(string name, Transform parent, Color c, Vector2 anchor, Vector2 pos, Vector2 size, bool rounded = true)
        {
            var img = Img(name, parent, c, rounded ? Rounded : White);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return img;
        }

        public static Text Txt(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, string text, int fs, Color c, TextAnchor align, bool bold = false, bool outline = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = Font; t.text = text; t.fontSize = fs; t.color = c; t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (outline)
            {
                var ol = go.AddComponent<Outline>();
                ol.effectColor = new Color(0f, 0f, 0f, 0.8f);
                ol.effectDistance = new Vector2(1.2f, -1.2f);
            }
            return t;
        }

        public static Button Btn(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, string label, Color bg, Action onClick, int fs = 16)
        {
            var img = Img(name, parent, bg, Rounded, true);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.2f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = new Color(1.25f, 1.25f, 1.15f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            b.colors = colors;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;   // FocusNavigator drives focus
            if (onClick != null) b.onClick.AddListener(() => { onClick(); });
            if (!string.IsNullOrEmpty(label))
                Txt("label", img.transform, new Vector2(0.5f, 0.5f), Vector2.zero, size, label, fs, Color.white, TextAnchor.MiddleCenter);
            return b;
        }

        /// Small key/button badge ("F", "Ⓑ", "R1") — a pill with a label.
        public static RectTransform Badge(Transform parent, string text, Vector2 anchor, Vector2 pos, float height = 30f)
        {
            float w = Mathf.Max(height, 14f + text.Length * 11f);
            var img = Img("badge", parent, new Color(0.9f, 0.9f, 0.95f, 0.14f), Rounded);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(w, height);
            var t = Txt("t", img.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, height), text, Mathf.RoundToInt(height * 0.52f), Theme.TextHi, TextAnchor.MiddleCenter, true);
            t.name = "badgeText";
            return rt;
        }

        public static void SetBadgeText(RectTransform badge, string text)
        {
            var t = badge.Find("badgeText");
            if (t == null) return;
            var txt = t.GetComponent<Text>();
            txt.text = text;
            float w = Mathf.Max(badge.sizeDelta.y, 14f + text.Length * 11f);
            badge.sizeDelta = new Vector2(w, badge.sizeDelta.y);
            t.GetComponent<RectTransform>().sizeDelta = badge.sizeDelta;
        }

        public static Canvas MakeCanvas(string name, Transform parent, int order, bool raycast)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (raycast) go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            var mod = go.AddComponent<InputSystemUIInputModule>();
            InputService.BindUiModule(mod);
        }

        public static void Sfx(float pitch = 1.6f, float vol = 0.3f)
        {
            AudioMan.I.Play2D(WuWa.Sfx.Swap(), vol, pitch);
        }

        public static string Num(int n) { return n.ToString("N0"); }
    }
}
