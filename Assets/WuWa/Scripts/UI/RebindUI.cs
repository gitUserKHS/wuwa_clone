using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WuWa
{
    /// Key/pad rebinding rows for the 조작 tab: one row per action with a KB/M
    /// badge, a pad badge and a reset. Listening runs in the Rebind context.
    public static class RebindUI
    {
        static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "Player/Jump", "점프 / 활공(홀드)" }, { "Player/Dodge", "회피 / 질주(홀드)" }, { "Player/Sprint", "질주 (전용) · 물에서 잠수/하강" },
            { "Player/Attack", "일반 공격 / 강공격(홀드)" }, { "Player/HeavyAttack", "강공격 (전용 키)" }, { "Player/Skill", "공명 스킬" },
            { "Player/Liberation", "공명 해방" }, { "Player/EchoSkill", "에코 스킬" }, { "Player/Swap1", "교대 1" }, { "Player/Swap2", "교대 2" }, { "Player/Swap3", "교대 3" },
            { "Player/QuickItem", "퀵 아이템" }, { "Player/Flask", "공명의 물약" }, { "Player/Interact", "상호작용" }, { "Player/Grapple", "갈고리" },
            { "Player/LockOn", "락온 (홀드 = 해제)" }, { "Player/Map", "지도" }, { "Player/Character", "캐릭터" }, { "Player/Bag", "가방" },
            { "Player/Quest", "퀘스트" }, { "Player/Codex", "도감" }, { "Player/Settings", "설정" },
            { "System/Help", "도움말" }, { "System/Save", "빠른 저장" }, { "System/Respawn", "리스폰" }, { "System/HudToggle", "HUD 숨김" },
        };
        static readonly string[] Order =
        {
            "Player/Jump", "Player/Dodge", "Player/Sprint", "Player/Attack", "Player/HeavyAttack", "Player/Skill", "Player/Liberation", "Player/EchoSkill",
            "Player/Swap1", "Player/Swap2", "Player/Swap3", "Player/QuickItem", "Player/Flask", "Player/Interact", "Player/Grapple", "Player/LockOn",
            "Player/Map", "Player/Character", "Player/Bag", "Player/Quest", "Player/Codex", "Player/Settings",
            "System/Help", "System/Save", "System/Respawn", "System/HudToggle",
        };

        class Row { public string action; public RectTransform kb, pad; }
        static readonly List<Row> _rows = new List<Row>();
        static InputActionRebindingExtensions.RebindingOperation _op;
        static string _pendingAction, _pendingGroup;
        static int _pendingIndex = -1;
        static Image _overlay;
        static Text _overlayText;
        static float _listenStarted;

        /// OptionsPanel extra builder hook — returns the number of rows added.
        public static int BuildRows(string tab, Transform content, int startRow)
        {
            if (tab != SettingsCatalog.TabControls) return 0;
            _rows.Clear();
            int row = startRow;
            float rowH = OptionsPanel.RowHeight;
            var head = UIKit.Txt("rebindHead", content, new Vector2(0f, 1f), new Vector2(24f, -10f - row * rowH - 14f), new Vector2(800f, 30f),
                "─ 키 설정 (클릭 후 원하는 키/버튼을 누르세요 · Backspace = 해제) ─", 16, UIKit.Theme.Accent, TextAnchor.MiddleLeft, true);
            head.rectTransform.pivot = new Vector2(0f, 0.5f);
            UIKit.Txt("colKb", content, new Vector2(1f, 1f), new Vector2(-400f, -10f - row * rowH - 14f), new Vector2(160f, 30f), "키보드 · 마우스", 13, UIKit.Theme.TextLo, TextAnchor.MiddleCenter);
            UIKit.Txt("colPad", content, new Vector2(1f, 1f), new Vector2(-200f, -10f - row * rowH - 14f), new Vector2(160f, 30f), "게임패드", 13, UIKit.Theme.TextLo, TextAnchor.MiddleCenter);
            row++;
            foreach (var key in Order)
            {
                var action = InputService.Action(key);
                if (action == null) continue;
                float y = -10f - row * rowH;
                var rowGo = new GameObject("rb_" + key.Replace('/', '_'));
                rowGo.transform.SetParent(content, false);
                var rrt = rowGo.AddComponent<RectTransform>();
                rrt.anchorMin = new Vector2(0f, 1f); rrt.anchorMax = new Vector2(1f, 1f); rrt.pivot = new Vector2(0.5f, 1f);
                rrt.anchoredPosition = new Vector2(0f, y); rrt.sizeDelta = new Vector2(0f, rowH);
                var bg = rowGo.AddComponent<Image>(); bg.sprite = UIKit.White; bg.color = new Color(1f, 1f, 1f, row % 2 == 0 ? 0.025f : 0.045f); bg.raycastTarget = false;
                var lb = UIKit.Txt("lb", rowGo.transform, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(420f, 30f), Labels.ContainsKey(key) ? Labels[key] : key, 16, UIKit.Theme.TextHi, TextAnchor.MiddleLeft);
                lb.rectTransform.pivot = new Vector2(0f, 0.5f);

                var r = new Row { action = key };
                r.kb = MakeSlot(rowGo.transform, key, WuWaInputSpec.SchemeKbm, new Vector2(-400f, 0f));
                r.pad = MakeSlot(rowGo.transform, key, WuWaInputSpec.SchemePad, new Vector2(-200f, 0f));
                var reset = UIKit.Btn("reset", rowGo.transform, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(60f, 36f), "↺", UIKit.Theme.Button, () => ResetAction(key), 18);
                var rrt2 = reset.GetComponent<RectTransform>(); rrt2.pivot = new Vector2(1f, 0.5f);
                Tooltip.Bind(reset.gameObject, () => "이 액션의 키를 기본값으로 되돌립니다");
                _rows.Add(r);
                row++;
            }
            RefreshBadges();
            return row - startRow;
        }

        static RectTransform MakeSlot(Transform parent, string action, string group, Vector2 pos)
        {
            var b = UIKit.Btn("slot_" + group, parent, new Vector2(1f, 0.5f), pos, new Vector2(160f, 36f), "—", UIKit.Theme.Cell, () => StartListen(action, group), 15);
            var rt = b.GetComponent<RectTransform>(); rt.pivot = new Vector2(1f, 0.5f);
            Tooltip.Bind(b.gameObject, () => "클릭한 뒤 새 키를 누르세요");
            return rt;
        }

        static int BindingIndex(InputAction action, string group)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var b = action.bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                if (!string.IsNullOrEmpty(b.groups) && b.groups.Contains(group)) return i;
            }
            return -1;
        }

        static string SlotText(InputAction action, string group)
        {
            int i = BindingIndex(action, group);
            if (i < 0) return "—";
            var b = action.bindings[i];
            string path = b.hasOverrides ? b.overridePath : b.path;
            if (string.IsNullOrEmpty(path)) return "(없음)";
            return Glyph.Badge(path);
        }

        public static void RefreshBadges()
        {
            foreach (var r in _rows)
            {
                var action = InputService.Action(r.action);
                if (action == null) continue;
                SetText(r.kb, SlotText(action, WuWaInputSpec.SchemeKbm));
                SetText(r.pad, SlotText(action, WuWaInputSpec.SchemePad));
            }
        }

        static void SetText(RectTransform slot, string text)
        {
            var t = slot.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        // ---------------------------------------------------------------- listening
        static void StartListen(string actionKey, string group)
        {
            var action = InputService.Action(actionKey);
            if (action == null) return;
            int idx = BindingIndex(action, group);
            if (idx < 0)
            {
                // no binding in this group yet (e.g. HeavyAttack): add an empty one to rebind into
                action.AddBinding("", null, null, group);
                idx = action.bindings.Count - 1;
            }
            _pendingAction = actionKey; _pendingGroup = group; _pendingIndex = idx;
            InputService.EnterRebind(true);
            ShowOverlay(true, Labels.ContainsKey(actionKey) ? Labels[actionKey] : actionKey, group);
            _listenStarted = Time.unscaledTime;
        }

        static void ShowOverlay(bool on, string label = "", string group = "")
        {
            if (_overlay == null)
            {
                _overlay = UIKit.Img("rebindOverlay", ScreenRouter.ModalLayer, new Color(0f, 0f, 0f, 0.7f), null, true);
                UIKit.Stretch(_overlay.rectTransform);
                var panel = UIKit.Panel("p", _overlay.transform, new Color(0.07f, 0.085f, 0.11f, 0.98f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 200f));
                _overlayText = UIKit.Txt("t", panel.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, 160f), "", 20, UIKit.Theme.TextHi, TextAnchor.MiddleCenter);
            }
            _overlay.gameObject.SetActive(on);
            if (on)
                _overlayText.text = "『" + label + "』 — " + (group == WuWaInputSpec.SchemePad ? "패드 버튼을 누르세요" : "키 또는 마우스 버튼을 누르세요") +
                                    "\n\nEsc 취소  ·  Backspace 해제  ·  6초 후 자동 취소";
        }

        /// Called from UIRoot each frame: starts the operation once the Rebind context is live.
        public static void Tick()
        {
            if (_pendingIndex >= 0 && _op == null && InputService.Current == InputContext.Rebind)
            {
                var action = InputService.Action(_pendingAction);
                if (action == null) { Finish(false); return; }
                try
                {
                    _op = action.PerformInteractiveRebinding(_pendingIndex)
                        .WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta").WithControlsExcluding("<Pointer>/position")
                        .WithControlsExcluding("<Keyboard>/anyKey").WithControlsExcluding("<Keyboard>/escape").WithControlsExcluding("<Keyboard>/backspace")
                        .WithCancelingThrough("<Keyboard>/escape")
                        .OnMatchWaitForAnother(0.1f)
                        .WithTimeout(6f)
                        .OnCancel(op => Finish(false))
                        .OnComplete(op => Finish(true));
                    if (_pendingGroup == WuWaInputSpec.SchemePad) _op.WithControlsHavingToMatchPath("<Gamepad>");
                    else { _op.WithControlsHavingToMatchPath("<Keyboard>"); _op.WithControlsHavingToMatchPath("<Mouse>"); }
                    _op.Start();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WuWa] rebind failed to start: " + ex.Message);
                    Finish(false);
                }
            }
            if (_op != null && Keyboard.current != null && Keyboard.current.backspaceKey.wasPressedThisFrame)
            {
                // Backspace = unbind this slot. Cancel() runs Finish(false) synchronously, so grab the target first.
                var action = InputService.Action(_pendingAction);
                int idx = _pendingIndex;
                string key = _pendingAction;
                _op.Cancel();
                if (action != null && idx >= 0 && idx < action.bindings.Count) action.ApplyBindingOverride(idx, "");
                InputService.SaveOverrides();
                RefreshBadges();
                HUDController.RefreshHelp();
                HUDController.Toast("키 해제됨 — " + (Labels.ContainsKey(key) ? Labels[key] : key));
            }
        }

        static void Finish(bool completed)
        {
            var action = InputService.Action(_pendingAction);
            if (_op != null) { _op.Dispose(); _op = null; }
            InputService.EnterRebind(false);
            ShowOverlay(false);
            int idx = _pendingIndex; string group = _pendingGroup; string key = _pendingAction;
            _pendingIndex = -1;
            if (!completed || action == null) { RefreshBadges(); return; }

            // conflict: same effective path in the same map + group
            string newPath = action.bindings[idx].effectivePath;
            InputAction other = null; int otherIdx = -1;
            foreach (var map in InputService.Asset.actionMaps)
            {
                if (map.name != action.actionMap.name && !(map.name == "System" || action.actionMap.name == "System")) continue;
                foreach (var a in map.actions)
                {
                    if (a == action) continue;
                    for (int i = 0; i < a.bindings.Count; i++)
                    {
                        var b = a.bindings[i];
                        if (b.isComposite || b.isPartOfComposite || string.IsNullOrEmpty(b.groups) || !b.groups.Contains(group)) continue;
                        if (b.effectivePath == newPath) { other = a; otherIdx = i; break; }
                    }
                    if (other != null) break;
                }
                if (other != null) break;
            }
            if (other != null)
            {
                string otherLabel = Labels.ContainsKey(other.actionMap.name + "/" + other.name) ? Labels[other.actionMap.name + "/" + other.name] : other.name;
                var o = other; int oi = otherIdx;
                Modal.Choice("이미 할당된 키", "『" + Glyph.Badge(newPath) + "』는 이미 『" + otherLabel + "』에 할당되어 있습니다.",
                    new[] { "교체 (상대 슬롯 비움)", "취소" }, pick =>
                    {
                        if (pick == 0) { o.ApplyBindingOverride(oi, ""); InputService.SaveOverrides(); }
                        else action.RemoveBindingOverride(idx);
                        RefreshBadges();
                        if (HUDController.I != null) HUDController.RefreshHelp();
                    }, 1);
                return;
            }
            InputService.SaveOverrides();
            RefreshBadges();
            HUDController.RefreshHelp();
            HUDController.Toast("키 설정 저장됨 — " + (Labels.ContainsKey(key) ? Labels[key] : key) + " = " + Glyph.Badge(newPath));
        }

        public static bool CancelListening()
        {
            if (_op == null && _pendingIndex < 0) return false;
            if (_op != null) _op.Cancel(); else Finish(false);
            return true;
        }

        static void ResetAction(string actionKey)
        {
            var action = InputService.Action(actionKey);
            if (action == null) return;
            for (int i = 0; i < action.bindings.Count; i++) action.RemoveBindingOverride(i);
            InputService.SaveOverrides();
            RefreshBadges();
            HUDController.RefreshHelp();
        }
    }
}
