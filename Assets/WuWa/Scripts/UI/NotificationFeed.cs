using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Pickup feed (right-middle): merges repeats, lives on the System layer so it
    /// stays visible over menus. Toasts remain for combat/system messages.
    public static class NotificationFeed
    {
        class Card
        {
            public string key;
            public int qty;
            public float born, until;
            public RectTransform rt;
            public CanvasGroup group;
            public Text label;
            public Image bar;
        }

        static Transform _layer;
        static readonly List<Card> _cards = new List<Card>();
        static readonly Stack<Card> _pool = new Stack<Card>();
        const int MaxCards = 5;
        const float Life = 3.5f;

        public static void Init(Transform systemLayer) { _layer = systemLayer; _cards.Clear(); _pool.Clear(); }   // scene reload: old cards are gone

        public static void Item(string name, int qty, Color rarity, string sub = null, bool currency = false)
        {
            if (_layer == null) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (c.key == name && now - c.born < 1.5f)
                {
                    c.qty += qty;
                    c.until = now + Life;
                    c.born = now;
                    c.label.text = Label(name, c.qty, sub, currency);
                    return;
                }
            }
            while (_cards.Count >= MaxCards) Recycle(_cards[0]);
            var card = _pool.Count > 0 ? _pool.Pop() : Make();
            card.key = name; card.qty = qty; card.born = now; card.until = now + Life;
            card.label.text = Label(name, qty, sub, currency);
            card.bar.color = rarity;
            card.rt.gameObject.SetActive(true);
            card.group.alpha = 1f;
            _cards.Add(card);
            Layout();
        }

        public static void Currency(string name, int delta)
        {
            if (delta == 0) return;
            Item(name, delta, UIKit.Theme.Info, null, true);
        }

        static string Label(string name, int qty, string sub, bool currency)
        {
            string s = currency ? name + "  " + (qty >= 0 ? "+" : "") + UIKit.Num(qty) : name + (qty > 1 ? "  ×" + qty : "");
            return string.IsNullOrEmpty(sub) ? s : s + "  <color=#ffffff88>" + sub + "</color>";
        }

        static Card Make()
        {
            var c = new Card();
            var img = UIKit.Img("feedCard", _layer, new Color(0.04f, 0.05f, 0.08f, 0.82f), UIKit.Rounded);
            c.rt = img.rectTransform;
            c.rt.anchorMin = c.rt.anchorMax = new Vector2(1f, 0.5f);
            c.rt.pivot = new Vector2(1f, 0.5f);
            c.rt.sizeDelta = new Vector2(360f, 44f);
            c.group = img.gameObject.AddComponent<CanvasGroup>();
            c.bar = UIKit.Img("bar", img.transform, Color.white, UIKit.Rounded);
            var brt = c.bar.rectTransform; brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = new Vector2(6f, 0f); brt.sizeDelta = new Vector2(5f, -12f);
            c.label = UIKit.Txt("label", img.transform, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(330f, 40f), "", 16, UIKit.Theme.TextHi, TextAnchor.MiddleLeft, false, true);
            c.label.supportRichText = true;
            return c;
        }

        static void Recycle(Card c)
        {
            _cards.Remove(c);
            c.rt.gameObject.SetActive(false);
            _pool.Push(c);
        }

        static bool _screenLayout;

        static void Layout()
        {
            // gameplay: right-middle stack; behind a screen: bottom-center so detail panels stay readable
            _screenLayout = ScreenRouter.IsOpen;
            for (int i = 0; i < _cards.Count; i++)
            {
                var rt = _cards[i].rt;
                if (_screenLayout)
                {
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                    rt.pivot = new Vector2(0.5f, 0f);
                    rt.anchoredPosition = new Vector2(0f, 56f + i * 50f);
                }
                else
                {
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                    rt.pivot = new Vector2(1f, 0.5f);
                    rt.anchoredPosition = new Vector2(-30f, 230f - i * 52f);
                }
            }
        }

        public static void Tick()
        {
            if (_cards.Count == 0) return;
            if (_screenLayout != ScreenRouter.IsOpen) Layout();
            float now = Time.unscaledTime;
            bool changed = false;
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var c = _cards[i];
                float left = c.until - now;
                if (left <= 0f) { Recycle(c); changed = true; continue; }
                c.group.alpha = Mathf.Clamp01(left / 0.6f);
            }
            if (changed) Layout();
        }
    }
}
