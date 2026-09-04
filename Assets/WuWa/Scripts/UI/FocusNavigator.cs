using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WuWa
{
    /// Gamepad/keyboard focus for code-built screens: collects Selectables under
    /// the top screen, moves focus by direction, submits, and draws a focus ring.
    public static class FocusNavigator
    {
        public static bool Active { get; private set; }
        static UIScreen _screen;
        static Selectable _current;
        static Image _ring;
        static float _nextRepeat;
        static Vector2 _lastDir;
        static bool _dirty = true;
        static readonly List<Selectable> _sel = new List<Selectable>();
        static readonly List<RaycastResult> _dummy = new List<RaycastResult>();

        public static void Init(Transform popupLayer)
        {
            _memory.Clear(); _sel.Clear(); _current = null; _screen = null;   // scene reload
            _ring = UIKit.Img("focusRing", popupLayer, new Color(1f, 0.85f, 0.4f, 0.95f), UIKit.Rounded);
            _ring.type = Image.Type.Sliced;
            _ring.fillCenter = false;
            _ring.raycastTarget = false;
            _ring.rectTransform.anchorMin = _ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _ring.gameObject.SetActive(false);
        }

        static readonly Dictionary<UIScreen, Selectable> _memory = new Dictionary<UIScreen, Selectable>();

        static int _boundFrame = -1;

        public static void Bind(UIScreen s)
        {
            _screen = s;
            _current = null;
            _dirty = true;
            _lastDir = Vector2.zero;
            _boundFrame = Time.frameCount;
            if (s == null) { Hide(); return; }
            Collect();
            // coming back from a popup/modal restores the element that was focused before
            Selectable remembered;
            if (_memory.TryGetValue(s, out remembered) && remembered != null && remembered.gameObject.activeInHierarchy && remembered.interactable && _sel.Contains(remembered))
            {
                Focus(remembered);
                return;
            }
            var def = s.DefaultFocus;
            Focus(def != null && def.gameObject.activeInHierarchy ? def : First());
        }

        public static void ForgetMemory(UIScreen s) { if (s != null) _memory.Remove(s); }

        public static void MarkDirty() { _dirty = true; Tooltip.Hide(); }
        public static Selectable Current { get { return _current; } }

        static void Hide() { if (_ring != null && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false); }

        static void Collect()
        {
            _sel.Clear();
            _dirty = false;
            if (_screen == null || _screen.FocusRoot == null) return;
            foreach (var s in _screen.FocusRoot.GetComponentsInChildren<Selectable>(false))
                if (s != null && s.interactable && s.gameObject.activeInHierarchy && !(s is Scrollbar)) _sel.Add(s);
        }

        /// Destroyed or deactivated entries (rows rebuilt, dialog buttons replaced) force a re-collect.
        static bool ListStale()
        {
            for (int i = 0; i < _sel.Count; i++)
                if (_sel[i] == null || !_sel[i].gameObject.activeInHierarchy || !_sel[i].interactable) return true;
            return false;
        }

        static Vector2 Center(Selectable s)
        {
            var rt = s.transform as RectTransform;
            return rt != null ? (Vector2)rt.position + Vector2.Scale(rt.rect.center, rt.lossyScale) : (Vector2)s.transform.position;
        }

        static Selectable First()
        {
            Selectable best = null; float bestScore = float.MaxValue;
            for (int i = 0; i < _sel.Count; i++)
            {
                if (_sel[i] == null) continue;
                var c = Center(_sel[i]);
                float score = -c.y * 2f + c.x;          // top-left first
                if (score < bestScore) { bestScore = score; best = _sel[i]; }
            }
            return best;
        }

        public static void Focus(Selectable s)
        {
            _current = s;
            if (s == null) return;
            if (_screen != null) _memory[_screen] = s;
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != s.gameObject) es.SetSelectedGameObject(s.gameObject);
            ScrollIntoView(s);
        }

        static void ScrollIntoView(Selectable s)
        {
            var scroll = s.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || scroll.viewport == null) return;
            var item = s.transform as RectTransform;
            if (item == null) return;
            // item position in content space (content pivot is top)
            Vector3 local = scroll.content.InverseTransformPoint(item.position);
            float itemTop = -local.y - item.rect.height * 0.5f;             // distance from content top
            float itemBottom = itemTop + item.rect.height;
            float viewH = scroll.viewport.rect.height;
            float contentH = scroll.content.rect.height;
            if (contentH <= viewH) return;
            float scrolled = (1f - scroll.verticalNormalizedPosition) * (contentH - viewH);
            float target = scrolled;
            if (itemTop < scrolled + 10f) target = itemTop - 10f;
            else if (itemBottom > scrolled + viewH - 10f) target = itemBottom - viewH + 10f;
            target = Mathf.Clamp(target, 0f, contentH - viewH);
            scroll.verticalNormalizedPosition = 1f - target / (contentH - viewH);
        }

        public static void Tick()
        {
            if (_screen == null || _screen.FocusRoot == null || !_screen.FocusRoot.gameObject.activeInHierarchy) { Hide(); return; }
            if (_current != null && (!_current.gameObject.activeInHierarchy || !_current.interactable)) _current = null;
            if (_dirty || _current == null || ListStale())
            {
                Collect();
                if (_current == null || !_sel.Contains(_current)) Focus(First());
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 9f) Active = false;

            Vector2 nav = InputService.UINavigate;
            if (nav.sqrMagnitude > 0.3f)
            {
                Vector2 dir = Mathf.Abs(nav.x) > Mathf.Abs(nav.y) ? new Vector2(Mathf.Sign(nav.x), 0f) : new Vector2(0f, Mathf.Sign(nav.y));
                bool fresh = dir != _lastDir;
                if (fresh || Time.unscaledTime >= _nextRepeat)
                {
                    Active = true;
                    Move(dir);
                    _nextRepeat = Time.unscaledTime + (fresh ? 0.4f : 0.12f);
                    _lastDir = dir;
                }
            }
            else _lastDir = Vector2.zero;

            // Submit works without prior navigation (pad users press A straight away), but never on
            // the frame the screen was bound — the press that opened it must not also confirm it.
            if (_current != null && _boundFrame != Time.frameCount)
            {
                var hold = _current.GetComponent<OptionsPanel.HoldButton>();
                if (hold != null)
                {
                    if (InputService.UISubmitPressed) { hold.SimulateDown(); Active = true; }
                    if (InputService.UISubmitReleased) hold.SimulateUp();
                }
                else if (InputService.UISubmitPressed)
                {
                    Active = true;
                    ExecuteEvents.Execute(_current.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                    UIKit.Sfx(2.2f, 0.15f);
                    _dirty = true;
                }
            }
            UpdateRing();
        }

        static void Move(Vector2 dir)
        {
            if (_current == null) { Focus(First()); return; }
            if (Mathf.Abs(dir.x) > 0.5f)
            {
                var slider = _current as Slider;
                if (slider != null)
                {
                    float step = slider.wholeNumbers ? 1f : (slider.maxValue - slider.minValue) * 0.05f;
                    slider.value = Mathf.Clamp(slider.value + step * Mathf.Sign(dir.x), slider.minValue, slider.maxValue);
                    return;
                }
                var cyc = _current.GetComponent<FocusCycle>();
                if (cyc != null) { cyc.Step(dir.x > 0f ? 1 : -1); return; }
            }
            Vector2 from = Center(_current);
            Selectable best = null; float bestScore = float.MaxValue;
            for (int i = 0; i < _sel.Count; i++)
            {
                var s = _sel[i];
                if (s == null || s == _current || !s.gameObject.activeInHierarchy) continue;
                Vector2 to = Center(s) - from;
                float along = Vector2.Dot(to, dir);
                if (along < 4f) continue;
                float perp = Mathf.Abs(Vector2.Dot(to, new Vector2(-dir.y, dir.x)));
                float score = along + perp * 2.2f;
                if (score < bestScore) { bestScore = score; best = s; }
            }
            if (best != null) { Focus(best); UIKit.Sfx(2.4f, 0.08f); }
        }

        static void UpdateRing()
        {
            if (_ring == null) return;
            bool show = Active && _current != null && _current.gameObject.activeInHierarchy;
            if (_ring.gameObject.activeSelf != show) _ring.gameObject.SetActive(show);
            if (!show) return;
            var rt = _current.transform as RectTransform;
            if (rt == null) return;
            var ringRt = _ring.rectTransform;
            ringRt.position = rt.TransformPoint(rt.rect.center);
            ringRt.rotation = rt.rotation;
            float scale = ringRt.lossyScale.x > 0.0001f ? rt.lossyScale.x / ringRt.lossyScale.x : 1f;
            ringRt.sizeDelta = rt.rect.size * scale + new Vector2(10f, 10f);
        }
    }

    /// Optional: a focusable that steps a value with left/right instead of moving focus.
    public class FocusCycle : MonoBehaviour
    {
        public System.Action<int> onStep;
        public void Step(int dir) { if (onStep != null) onStep(dir); }
    }
}
