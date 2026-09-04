using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    public enum UILayer { Screen, Popup, Modal }

    /// Base class for every routed screen. Built lazily on first open.
    public abstract class UIScreen : MonoBehaviour
    {
        public abstract string Id { get; }
        public virtual UILayer Layer { get { return UILayer.Screen; } }
        public virtual bool PausesTime { get { return true; } }
        public virtual bool IsHubTab { get { return false; } }
        public virtual bool UsesMapContext { get { return false; } }
        public virtual string Title { get { return Id; } }
        public RectTransform Root { get; protected set; }
        public CanvasGroup Group { get; protected set; }
        public virtual Transform FocusRoot { get { return Root; } }
        public virtual Selectable DefaultFocus { get { return null; } }
        bool _built;

        public void EnsureBuilt(Transform layerParent)
        {
            if (_built) return;
            _built = true;
            var go = new GameObject(Id + "Screen");
            go.transform.SetParent(layerParent, false);
            Root = go.AddComponent<RectTransform>();
            UIKit.Stretch(Root);
            Group = go.AddComponent<CanvasGroup>();
            Build();
            go.SetActive(false);
        }

        protected virtual void Build() { }
        public virtual void SetShown(bool on) { if (Root != null && Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on); }
        public virtual void OnOpen(object args) { }
        public virtual void OnClose() { }
        /// Return true to consume ESC/B (internal state cancelled) instead of popping.
        public virtual bool OnBack() { return false; }
        public virtual void OnTick() { }
        public virtual void OnTab(int dir) { }
    }

    /// The single owner of the screen stack, time scale, input context, cursor
    /// and HUD visibility. Hotkeys are handled here (design doc 7.1–7.4).
    public static class ScreenRouter
    {
        static readonly List<UIScreen> _stack = new List<UIScreen>();
        static readonly Dictionary<string, UIScreen> _screens = new Dictionary<string, UIScreen>();
        public static Transform ScreenLayer, PopupLayer, ModalLayer, SystemLayer;
        public static UIScreen Top { get; private set; }
        public static bool IsOpen { get { return _stack.Count > 0; } }
        public static int Depth { get { return _stack.Count; } }
        public static event Action Changed;
        public static readonly string[] HubOrder = { "Character", "Bag", "Quest", "Codex", "Map", "Settings" };
        static bool _suppressSfx;

        public static void Register(UIScreen s) { _screens[s.Id] = s; }
        public static UIScreen Get(string id) { UIScreen s; return _screens.TryGetValue(id, out s) ? s : null; }
        public static bool Contains(string id) { var s = Get(id); return s != null && _stack.Contains(s); }

        static Transform LayerFor(UIScreen s)
        {
            return s.Layer == UILayer.Modal ? ModalLayer : s.Layer == UILayer.Popup ? PopupLayer : ScreenLayer;
        }

        public static void Push(string id, object args = null)
        {
            var s = Get(id);
            if (s == null) { Debug.LogWarning("[WuWa] no screen " + id); return; }
            if (_stack.Count >= 4 && s.Layer == UILayer.Screen) { Replace(id, args); return; }
            if (_stack.Contains(s)) { _stack.Remove(s); s.OnClose(); s.SetShown(false); }
            s.EnsureBuilt(LayerFor(s));
            _stack.Add(s);
            s.SetShown(true);
            // a screen bug must never leave the router half-open (stack entry without Top)
            try { s.OnOpen(args); }
            catch (Exception ex) { Debug.LogError("[WuWa] screen " + id + " OnOpen failed: " + ex); }
            Apply(true);
        }

        public static void Replace(string id, object args = null)
        {
            if (_stack.Count > 0)
            {
                var old = _stack[_stack.Count - 1];
                _stack.RemoveAt(_stack.Count - 1);
                old.OnClose();
                old.SetShown(false);
            }
            Push(id, args);
        }

        public static void Pop()
        {
            if (_stack.Count == 0) return;
            var s = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            s.OnClose();
            s.SetShown(false);
            Apply(false);
        }

        public static void CloseAll()
        {
            while (_stack.Count > 0)
            {
                var s = _stack[_stack.Count - 1];
                _stack.RemoveAt(_stack.Count - 1);
                s.OnClose();
                s.SetShown(false);
            }
            Apply(false);
        }

        public static void Toggle(string id)
        {
            if (Top != null && Top.Id == id) { CloseAll(); return; }
            var s = Get(id);
            if (s == null) return;
            if (Top != null && Top.IsHubTab && s.IsHubTab) Replace(id);
            else Push(id);
        }

        public static void Back()
        {
            if (Top == null) return;
            if (!Top.OnBack()) Pop();
        }

        public static void HubCycle(int dir)
        {
            if (Top == null || !Top.IsHubTab || !SaveSystem.SessionStarted) return;
            int i = Array.IndexOf(HubOrder, Top.Id);
            if (i < 0) return;
            Replace(HubOrder[(i + dir + HubOrder.Length) % HubOrder.Length]);
        }

        static void Apply(bool opening)
        {
            Top = _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
            // only the topmost Screen-layer entry and whatever sits above it are visible
            int baseIdx = 0;
            for (int i = _stack.Count - 1; i >= 0; i--) if (_stack[i].Layer == UILayer.Screen) { baseIdx = i; break; }
            for (int i = 0; i < _stack.Count; i++) _stack[i].SetShown(i >= baseIdx);

            bool open = _stack.Count > 0;
            bool pause = false;
            for (int i = baseIdx; i < _stack.Count; i++) if (_stack[i].PausesTime) pause = true;
            Time.timeScale = open && pause ? 0f : 1f;

            InputService.Pop(InputContext.Map);
            InputService.Pop(InputContext.Menu);
            if (open) InputService.Push(Top.UsesMapContext ? InputContext.Map : InputContext.Menu);
            GameDirector.MenuOpen = open;
            CursorService.Apply(open ? CursorService.Mode.Menu : CursorService.Mode.Gameplay);
            HUDController.SetHudVisible(!open);
            FocusNavigator.Bind(Top);
            DragItem.CancelActive();
            if (opening && !_suppressSfx) UIKit.Sfx(1.6f, 0.3f);
            if (Changed != null) Changed();
        }

        /// Hotkeys; called every frame by UIRoot after InputService has sampled input.
        public static void Tick()
        {
            if (Cutscene.Active || DialogueSystem.Active || InputService.Current == InputContext.Rebind) return;
            if (!IsOpen)
            {
                if (!SaveSystem.SessionStarted) return;              // no session yet: nothing to open
                if (InputService.PausePressed) Push("Pause");
                else if (InputService.CharacterPressed) Push("Character");
                else if (InputService.BagPressed) Push("Bag");
                else if (InputService.SettingsPressed) Push("Settings");
                else if (InputService.MapPressed) Push("Map");
                else if (InputService.QuestPressed) Push("Quest");
                else if (InputService.CodexPressed) Push("Codex");
                return;
            }
            var top = Top;
            bool back = InputService.MenuClosePressed || InputService.UICancelPressed || InputService.PausePressed
                     || (top.UsesMapContext && InputService.MapClosePressed);
            if (top.Layer == UILayer.Modal)
            {
                if (back) Back(); else top.OnTick();
                return;
            }
            if (back) Back();
            else if (!SaveSystem.SessionStarted) top.OnTick();           // title / slot list: no hub hotkeys
            else if (InputService.MenuCharacterPressed) Toggle("Character");
            else if (InputService.MenuBagPressed) Toggle("Bag");
            else if (InputService.MenuSettingsPressed) Toggle("Settings");
            else if (InputService.MenuMapPressed) Toggle("Map");
            else if (InputService.MenuQuestPressed) Toggle("Quest");
            else if (InputService.MenuCodexPressed) Toggle("Codex");
            else if (InputService.UITabPrevPressed) { if (top.IsHubTab && !top.UsesMapContext) HubCycle(-1); else top.OnTab(-1); }
            else if (InputService.UITabNextPressed) { if (top.IsHubTab && !top.UsesMapContext) HubCycle(1); else top.OnTab(1); }
            else top.OnTick();
        }

        // ---------------------------------------------------------------- hub header shared by new screens
        public class HubHeader
        {
            public Text title, currency, hint;
            public readonly List<Button> tabs = new List<Button>();
        }

        static readonly string[] HubLabels = { "캐릭터", "가방", "퀘스트", "도감", "지도", "설정" };

        public static HubHeader BuildHubHeader(Transform root, string title, string activeId)
        {
            var h = new HubHeader();
            UIKit.Stretch(UIKit.Img("bg", root, UIKit.Theme.Bg).rectTransform);
            var sheen = UIKit.Img("sheen", root, new Color(0.16f, 0.2f, 0.24f, 0.35f));
            var srt = sheen.rectTransform; srt.anchorMin = new Vector2(0f, 0.62f); srt.anchorMax = Vector2.one; srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            var band = UIKit.Img("band", root, new Color(1f, 0.82f, 0.35f, 0.5f));
            var brt = band.rectTransform; brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f); brt.anchoredPosition = new Vector2(0f, -86f); brt.sizeDelta = new Vector2(0f, 2f);
            h.title = UIKit.Txt("hdrTitle", root, new Vector2(0f, 1f), new Vector2(60f, -30f), new Vector2(600f, 44f), title, 30, new Color(1f, 0.93f, 0.75f, 1f), TextAnchor.MiddleLeft, true);
            for (int i = 0; i < HubOrder.Length; i++)
            {
                string id = HubOrder[i];
                bool active = id == activeId;
                var b = UIKit.Btn("tab_" + id, root, new Vector2(0.5f, 1f), new Vector2(-375f + i * 150f, -28f), new Vector2(140f, 40f), HubLabels[i],
                    active ? UIKit.Theme.Selected : UIKit.Theme.Button, () => { if (!SaveSystem.SessionStarted) return; if (Top == null || Top.Id != id) Toggle(id); });
                h.tabs.Add(b);
            }
            h.currency = UIKit.Txt("currency", root, new Vector2(1f, 1f), new Vector2(-60f, -30f), new Vector2(360f, 30f), "", 18, UIKit.Theme.Info, TextAnchor.MiddleRight, true);
            h.hint = UIKit.Txt("hint", root, new Vector2(1f, 1f), new Vector2(-60f, -62f), new Vector2(700f, 22f), "", 13, UIKit.Theme.TextLo, TextAnchor.MiddleRight);
            RefreshHubHeader(h);
            return h;
        }

        public static void RefreshHubHeader(HubHeader h)
        {
            if (h == null) return;
            foreach (var b in h.tabs) if (b != null) b.interactable = SaveSystem.SessionStarted;
            int shards = ProgressSystem.I != null ? ProgressSystem.I.Shards : 0;
            if (h.currency != null) h.currency.text = "조각소리  " + UIKit.Num(shards) + "   ·   증표 " + Inventory.TrialTokens + "   ·   조율기 " + Inventory.Count(ItemDB.Tuner);
            if (h.hint != null)
                h.hint.text = Glyph.Key("UI/TabPrev", "Q") + " / " + Glyph.Key("UI/TabNext", "E") + " 탭 전환  ·  " + Glyph.Key("UI/Cancel", "Esc") + " 뒤로";
        }
    }
}
