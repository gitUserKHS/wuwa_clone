using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WuWa
{
    /// Hover/focus tooltip service on the Popup layer.
    public static class Tooltip
    {
        static Transform _layer;
        static Image _panel;
        static Text _text;
        static Func<string> _src;
        static RectTransform _target;
        static float _showAt = -1f;
        static bool _fromFocus;

        public static void Init(Transform popupLayer)
        {
            _layer = popupLayer;
            _target = null; _src = null;
            _panel = UIKit.Img("tooltip", _layer, new Color(0.05f, 0.06f, 0.09f, 0.96f), UIKit.Rounded);
            var rt = _panel.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(360f, 60f);
            _text = UIKit.Txt("t", _panel.transform, new Vector2(0f, 1f), new Vector2(12f, -10f), new Vector2(336f, 40f), "", 14, UIKit.Theme.TextHi, TextAnchor.UpperLeft);
            _panel.gameObject.SetActive(false);
        }

        public static void Bind(GameObject target, Func<string> text)
        {
            if (target == null) return;
            var trig = target.GetComponent<TooltipTrigger>();
            if (trig == null) trig = target.AddComponent<TooltipTrigger>();
            trig.source = text;
            // a pooled cell that is currently hovered/focused gets the new text, not the stale one
            if (_target == trig.transform) _src = text;
        }

        internal static void Request(RectTransform target, Func<string> src, bool fromFocus)
        {
            _target = target; _src = src; _fromFocus = fromFocus;
            _showAt = Time.unscaledTime + 0.35f;
        }

        internal static void Release(RectTransform target)
        {
            if (_target != target) return;
            _target = null; _src = null; _showAt = -1f;
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        public static void Hide() { Release(_target); }

        public static void Tick()
        {
            if (_panel == null) return;
            if (_target == null || _src == null || !_target.gameObject.activeInHierarchy) { if (_panel.gameObject.activeSelf) _panel.gameObject.SetActive(false); return; }
            if (Time.unscaledTime < _showAt) return;
            string s = _src();
            if (string.IsNullOrEmpty(s)) { if (_panel.gameObject.activeSelf) _panel.gameObject.SetActive(false); return; }
            if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);
            _text.text = s;
            float h = Mathf.Max(48f, _text.preferredHeight + 20f);
            _panel.rectTransform.sizeDelta = new Vector2(360f, h);
            // place above the target (screen space == overlay canvas space at this scaler)
            Vector3 world = _target.TransformPoint(new Vector3(_target.rect.xMin, _target.rect.yMax, 0f));
            var prt = _panel.rectTransform;
            float scale = prt.lossyScale.x > 0.0001f ? 1f / prt.lossyScale.x : 1f;
            Vector2 local = (Vector2)world * scale;
            local += new Vector2(0f, 8f);
            local.x = Mathf.Clamp(local.x, 8f, 1920f / scale - 368f);
            local.y = Mathf.Clamp(local.y, 8f, 1080f / scale - h - 8f);
            prt.anchoredPosition = local;
        }
    }

    public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public Func<string> source;
        public void OnPointerEnter(PointerEventData e) { Tooltip.Request(transform as RectTransform, source, false); }
        public void OnPointerExit(PointerEventData e) { Tooltip.Release(transform as RectTransform); }
        public void OnSelect(BaseEventData e) { Tooltip.Request(transform as RectTransform, source, true); }
        public void OnDeselect(BaseEventData e) { Tooltip.Release(transform as RectTransform); }
        void OnDisable() { Tooltip.Release(transform as RectTransform); }
    }
}
