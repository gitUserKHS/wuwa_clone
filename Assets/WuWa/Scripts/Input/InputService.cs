using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace WuWa
{
    public enum InputContext { Gameplay, Menu, Map, Dialogue, Cutscene, Rebind }
    public enum InputScheme { KeyboardMouse, Gamepad }

    /// Owns the action asset, the context stack (which maps are live), active
    /// device detection and the per-frame snapshot every gameplay script reads.
    /// Runs before everything else (execution order -500). Design doc ch.9.
    [DefaultExecutionOrder(-500)]
    public class InputService : MonoBehaviour
    {
        public static InputService I { get; private set; }
        public static InputActionAsset Asset { get; private set; }
        public static bool Ready { get { return Asset != null; } }

        // ---------------------------------------------------------------- scheme
        public static InputScheme Scheme { get; private set; }
        public static bool GamepadActive { get { return Scheme == InputScheme.Gamepad; } }
        public static bool IsPlayStation { get; private set; }
        public static event Action SchemeChanged;
        static float _lastPadInput = -99f;

        // ---------------------------------------------------------------- settings-driven processing
        public static float DeadzoneL = 0.15f, DeadzoneR = 0.20f;
        public static float StickCurve = 1.4f;           // exponent on remapped magnitude
        public static bool PadAccel = true;

        // ---------------------------------------------------------------- per-frame snapshot (Player)
        public static Vector2 Move;                      // deadzone/curve processed, clamped to 1
        public static Vector2 LookMouse;                 // raw pixel delta this frame
        public static Vector2 LookStick;                 // processed stick (-1..1)
        public static float Zoom;                        // mouse notches (+ = closer) or pad zoom
        public static bool ZoomModifierHeld;             // R3 held → right stick Y zooms instead of pitching
        public static bool JumpPressed, JumpHeld, DodgePressed, DodgeHeld, SprintPressed, SprintHeld;
        public static bool AttackPressed, AttackHeld, HeavyPressed, SkillPressed, UltPressed, EchoPressed;
        public static bool QuickItemPressed, FlaskPressed, InteractPressed, GrapplePressed;
        public static bool LockOnPressed, LockOnHoldPerformed, LockOnHeld;
        public static bool MapPressed, CharacterPressed, BagPressed, QuestPressed, CodexPressed, SettingsPressed;
        public static int SwapPressed = -1;
        // System
        public static bool PausePressed, CursorFreeHeld, HelpPressed, SavePressed, RespawnPressed, HudTogglePressed;
        // UI / Menu / Map / Dialogue
        public static Vector2 UINavigate;
        public static bool UISubmitPressed, UICancelPressed, UITabPrevPressed, UITabNextPressed, UIContextPressed, UIDetailPressed, UIFilterPressed;
        public static bool UISubmitHeld, UISubmitReleased;
        public static bool PauseHeld;                      // cutscene hold-to-skip
        public static bool MenuClosePressed, MenuMemberPrevPressed, MenuMemberNextPressed, MenuCharacterPressed, MenuBagPressed, MenuSettingsPressed, MenuMapPressed;
        public static bool MenuQuestPressed, MenuCodexPressed;
        public static int MenuMemberPressed = -1;
        public static bool MapClosePressed, MapMarkerPrevPressed, MapMarkerNextPressed, MapWarpPressed, MapPinPressed, MapFilterPressed, MapCenterPressed;
        public static bool MapPinHeld;
        public static float MapZoom;
        public static Vector2 MapPan, MapCursor;
        public static bool DialogueAdvancePressed, DialogueSkipPressed;

        // ---------------------------------------------------------------- debug hooks (CLI tests)
        public static Vector2 DbgMove;
        public static bool DbgSprint, DbgJumpHeld;
        static readonly HashSet<string> _dbgPresses = new HashSet<string>();
        static readonly HashSet<string> _dbgHeld = new HashSet<string>();
        /// Simulates a one-frame press of a Player/System action by name (e.g. "Jump").
        public static void DbgPress(string action) { lock (_dbgPresses) _dbgPresses.Add(action); }
        public static void DbgHold(string action, bool held) { lock (_dbgHeld) { if (held) _dbgHeld.Add(action); else _dbgHeld.Remove(action); } }

        // ---------------------------------------------------------------- context stack
        static readonly List<InputContext> _stack = new List<InputContext>();
        static InputContext _applied = (InputContext)(-1);
        public static InputContext Current { get; private set; }
        public static bool GameplayActive { get { return Current == InputContext.Gameplay; } }
        public static void Push(InputContext c) { _stack.Add(c); }
        public static void Pop(InputContext c)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (_stack[i] == c) { _stack.RemoveAt(i); return; }
        }
        public static void ClearStack() { _stack.Clear(); }

        // cached actions
        readonly Dictionary<string, InputAction> _actions = new Dictionary<string, InputAction>();
        InputActionMap _mPlayer, _mSystem, _mUI, _mMenu, _mMap, _mDialogue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("~InputService");
            DontDestroyOnLoad(go);
            go.AddComponent<InputService>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            Asset = Resources.Load<InputActionAsset>(WuWaInputSpec.AssetResourcePath);
            if (Asset == null)
            {
                Debug.LogWarning("[WuWa] input asset missing in Resources — building runtime fallback (binding overrides will not persist)");
                Asset = WuWaInputSpec.BuildRuntimeAsset();
            }
            else Asset = Instantiate(Asset);           // never mutate the imported asset
            foreach (var map in Asset.actionMaps)
                foreach (var a in map.actions) _actions[map.name + "/" + a.name] = a;
            _mPlayer = Asset.FindActionMap("Player");
            _mSystem = Asset.FindActionMap("System");
            _mUI = Asset.FindActionMap("UI");
            _mMenu = Asset.FindActionMap("Menu");
            _mMap = Asset.FindActionMap("Map");
            _mDialogue = Asset.FindActionMap("Dialogue");

            SettingsStore.Load();
            if (!string.IsNullOrEmpty(SettingsStore.D.inputOverrides))
            {
                try { Asset.LoadBindingOverridesFromJson(SettingsStore.D.inputOverrides); }
                catch (Exception ex) { Debug.LogWarning("[WuWa] input overrides rejected: " + ex.Message); }
            }
            SettingsAppliers.ApplyControls();

            Scheme = Gamepad.current != null && Keyboard.current == null ? InputScheme.Gamepad : InputScheme.KeyboardMouse;
            InputSystem.onActionChange += OnActionChange;
            InputSystem.onDeviceChange += OnDeviceChange;
            ApplyContext(InputContext.Gameplay, true);
            gameObject.AddComponent<HapticsService>();
        }

        void OnDestroy()
        {
            InputSystem.onActionChange -= OnActionChange;
            InputSystem.onDeviceChange -= OnDeviceChange;
            if (I == this) I = null;
        }

        public static InputAction Action(string mapSlashName)
        {
            InputAction a;
            return I != null && I._actions.TryGetValue(mapSlashName, out a) ? a : null;
        }

        // ---------------------------------------------------------------- devices
        void OnActionChange(object obj, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed) return;
            var action = obj as InputAction;
            if (action == null) return;
            var control = action.activeControl;
            if (control == null) return;
            var dev = control.device;
            if (dev is Gamepad)
            {
                _lastPadInput = Time.unscaledTime;
                IsPlayStation = dev is UnityEngine.InputSystem.DualShock.DualShockGamepad;
                SetScheme(InputScheme.Gamepad);
            }
            else if (dev is Mouse)
            {
                // tiny mouse jitter right after pad input must not flip the glyphs
                if (Time.unscaledTime - _lastPadInput < 0.5f && action.name == "Look")
                {
                    var v = action.ReadValue<Vector2>();
                    if (v.sqrMagnitude < 4f) return;
                }
                SetScheme(InputScheme.KeyboardMouse);
            }
            else if (dev is Keyboard) SetScheme(InputScheme.KeyboardMouse);
        }

        void OnDeviceChange(InputDevice dev, InputDeviceChange change)
        {
            if (!(dev is Gamepad)) return;
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
            {
                if (Scheme == InputScheme.Gamepad) SetScheme(InputScheme.KeyboardMouse);
                HUDController.Toast("컨트롤러 연결이 끊겼습니다");
            }
            else if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
                HUDController.Toast("컨트롤러 연결됨 — " + dev.displayName);
        }

        static void SetScheme(InputScheme s)
        {
            if (Scheme == s) return;
            Scheme = s;
            CursorService.Refresh();
            if (SchemeChanged != null) SchemeChanged();
        }

        // ---------------------------------------------------------------- contexts
        void ApplyContext(InputContext c, bool force)
        {
            if (!force && c == _applied) return;
            _applied = c;
            Current = c;
            bool player = c == InputContext.Gameplay;
            bool system = c != InputContext.Rebind;
            bool ui = c == InputContext.Menu || c == InputContext.Map;
            bool menu = c == InputContext.Menu;
            bool map = c == InputContext.Map;
            bool dlg = c == InputContext.Dialogue;
            Toggle(_mPlayer, player); Toggle(_mSystem, system); Toggle(_mUI, ui);
            Toggle(_mMenu, menu); Toggle(_mMap, map); Toggle(_mDialogue, dlg);
            GameDirector.MenuOpen = c == InputContext.Menu || c == InputContext.Map;
        }

        static void Toggle(InputActionMap m, bool on)
        {
            if (m == null) return;
            if (on && !m.enabled) m.Enable();
            else if (!on && m.enabled) m.Disable();
        }

        public static void EnterRebind(bool on)
        {
            if (on) Push(InputContext.Rebind); else Pop(InputContext.Rebind);
        }

        // ---------------------------------------------------------------- per frame
        void Update()
        {
            // effective context: overlays win over the stack
            InputContext c;
            if (_stack.Contains(InputContext.Rebind)) c = InputContext.Rebind;
            else if (Cutscene.Active) c = InputContext.Cutscene;
            else if (DialogueSystem.Active) c = InputContext.Dialogue;
            else c = _stack.Count > 0 ? _stack[_stack.Count - 1] : InputContext.Gameplay;
            ApplyContext(c, false);

            ReadPlayer();
            ReadSystem();
            ReadUI();
        }

        bool P(string key) { InputAction a; return _actions.TryGetValue(key, out a) && a.enabled && a.WasPressedThisFrame(); }
        bool H(string key) { InputAction a; return _actions.TryGetValue(key, out a) && a.enabled && a.IsPressed(); }
        bool Perf(string key) { InputAction a; return _actions.TryGetValue(key, out a) && a.enabled && a.WasPerformedThisFrame(); }
        Vector2 V2(string key) { InputAction a; return _actions.TryGetValue(key, out a) && a.enabled ? a.ReadValue<Vector2>() : Vector2.zero; }
        float V1(string key) { InputAction a; return _actions.TryGetValue(key, out a) && a.enabled ? a.ReadValue<float>() : 0f; }

        bool Dbg(string action)
        {
            lock (_dbgPresses) return _dbgPresses.Remove(action);
        }
        bool DbgH(string action) { lock (_dbgHeld) return _dbgHeld.Contains(action); }

        static Vector2 ProcessStick(Vector2 v, float dz, float curve)
        {
            float mag = v.magnitude;
            if (mag <= dz) return Vector2.zero;
            float t = Mathf.Clamp01((mag - dz) / (1f - dz));
            t = Mathf.Pow(t, curve);
            return v / mag * t;
        }

        void ReadPlayer()
        {
            bool live = Current == InputContext.Gameplay;
            Vector2 mv = live ? V2("Player/Move") : Vector2.zero;
            // gamepad sticks get the radial deadzone + response curve; keyboard composites pass through
            var moveAction = Action("Player/Move");
            bool stick = moveAction != null && moveAction.activeControl != null && moveAction.activeControl.device is Gamepad;
            if (stick) mv = ProcessStick(mv, DeadzoneL, StickCurve);
            if (DbgMove.sqrMagnitude > 0.01f) mv = DbgMove;
            Move = Vector2.ClampMagnitude(mv, 1f);

            LookMouse = Vector2.zero; LookStick = Vector2.zero; Zoom = 0f;
            if (live)
            {
                var look = Action("Player/Look");
                if (look != null)
                {
                    Vector2 lv = look.ReadValue<Vector2>();
                    if (look.activeControl != null && look.activeControl.device is Gamepad) LookStick = ProcessStick(lv, DeadzoneR, 1.6f);
                    else LookMouse = lv;
                }
                float scroll = V1("Player/Zoom");
                Zoom = Mathf.Abs(scroll) > 0.01f ? Mathf.Sign(scroll) * Mathf.Clamp(Mathf.Abs(scroll) / 120f, 0.25f, 3f) : 0f;
                ZoomModifierHeld = H("Player/LockOnHold") && Gamepad.current != null && Scheme == InputScheme.Gamepad;
                if (ZoomModifierHeld) { Zoom += LookStick.y * 3f * Time.unscaledDeltaTime; LookStick = Vector2.zero; }
            }

            JumpPressed = live && (P("Player/Jump") || Dbg("Jump"));
            JumpHeld = live && (H("Player/Jump") || DbgJumpHeld || DbgH("Jump"));
            DodgePressed = live && (P("Player/Dodge") || Dbg("Dodge"));
            DodgeHeld = live && (H("Player/Dodge") || DbgH("Dodge"));
            SprintPressed = live && (P("Player/Sprint") || Dbg("Sprint"));
            SprintHeld = live && (H("Player/Sprint") || DbgSprint || DbgH("Sprint"));
            AttackPressed = live && (P("Player/Attack") || Dbg("Attack"));
            AttackHeld = live && (H("Player/Attack") || DbgH("Attack"));
            HeavyPressed = live && (P("Player/HeavyAttack") || Dbg("HeavyAttack"));
            SkillPressed = live && (P("Player/Skill") || Dbg("Skill"));
            UltPressed = live && (P("Player/Liberation") || Dbg("Liberation"));
            EchoPressed = live && (P("Player/EchoSkill") || Dbg("EchoSkill"));
            QuickItemPressed = live && (P("Player/QuickItem") || Dbg("QuickItem"));
            FlaskPressed = live && (Perf("Player/Flask") || Dbg("Flask"));
            InteractPressed = live && (P("Player/Interact") || Dbg("Interact"));
            GrapplePressed = live && (P("Player/Grapple") || Dbg("Grapple"));
            LockOnPressed = live && (P("Player/LockOn") || Dbg("LockOn"));
            LockOnHoldPerformed = live && (Perf("Player/LockOnHold") || Dbg("LockOnHold"));
            LockOnHeld = live && H("Player/LockOnHold");
            SwapPressed = !live ? -1 : (P("Player/Swap1") || Dbg("Swap1")) ? 0 : (P("Player/Swap2") || Dbg("Swap2")) ? 1 : (P("Player/Swap3") || Dbg("Swap3")) ? 2 : -1;
            MapPressed = live && (P("Player/Map") || Dbg("Map"));
            CharacterPressed = live && (P("Player/Character") || Dbg("Character"));
            BagPressed = live && (P("Player/Bag") || Dbg("Bag"));
            QuestPressed = live && (P("Player/Quest") || Dbg("Quest"));
            CodexPressed = live && (P("Player/Codex") || Dbg("Codex"));
            SettingsPressed = live && (P("Player/Settings") || Dbg("Settings"));
        }

        void ReadSystem()
        {
            bool live = Current != InputContext.Rebind;
            PausePressed = live && (P("System/Pause") || Dbg("Pause"));
            PauseHeld = live && H("System/Pause");
            CursorFreeHeld = live && (H("System/CursorFree") || DbgH("CursorFree"));
            HelpPressed = live && (P("System/Help") || Dbg("Help"));
            SavePressed = live && (P("System/Save") || Dbg("Save"));
            RespawnPressed = live && (P("System/Respawn") || Dbg("Respawn"));
            HudTogglePressed = live && (P("System/HudToggle") || Dbg("HudToggle"));
        }

        void ReadUI()
        {
            UINavigate = V2("UI/Navigate");
            UISubmitPressed = P("UI/Submit") || Dbg("Submit");
            bool subNow = H("UI/Submit");
            UISubmitReleased = UISubmitHeld && !subNow;
            UISubmitHeld = subNow;
            UICancelPressed = P("UI/Cancel") || Dbg("Cancel");
            UITabPrevPressed = P("UI/TabPrev") || Dbg("TabPrev");
            UITabNextPressed = P("UI/TabNext") || Dbg("TabNext");
            UIContextPressed = P("UI/Context"); UIDetailPressed = P("UI/Detail"); UIFilterPressed = P("UI/Filter");

            MenuClosePressed = P("Menu/Close") || Dbg("MenuClose");
            MenuMemberPressed = P("Menu/Member1") ? 0 : P("Menu/Member2") ? 1 : P("Menu/Member3") ? 2 : -1;
            MenuMemberPrevPressed = P("Menu/MemberPrev"); MenuMemberNextPressed = P("Menu/MemberNext");
            MenuCharacterPressed = P("Menu/Character") || Dbg("MenuCharacter");
            MenuQuestPressed = P("Menu/Quest"); MenuCodexPressed = P("Menu/Codex");
            MenuBagPressed = P("Menu/Bag") || Dbg("MenuBag");
            MenuSettingsPressed = P("Menu/Settings") || Dbg("MenuSettings");
            MenuMapPressed = P("Menu/Map") || Dbg("MenuMap");

            MapClosePressed = P("Map/Close") || Dbg("MapClose");
            MapMarkerPrevPressed = P("Map/MarkerPrev"); MapMarkerNextPressed = P("Map/MarkerNext");
            MapWarpPressed = P("Map/Warp") || Dbg("MapWarp");
            MapPinPressed = P("Map/Pin"); MapFilterPressed = P("Map/Filter"); MapCenterPressed = P("Map/Center");
            MapPinHeld = H("Map/Pin");
            MapZoom = V1("Map/Zoom");
            MapPan = V2("Map/Pan"); MapCursor = V2("Map/Cursor");

            DialogueAdvancePressed = P("Dialogue/Advance") || Dbg("DialogueAdvance");
            DialogueSkipPressed = P("Dialogue/Skip") || Dbg("DialogueSkip");
        }

        // ---------------------------------------------------------------- overrides persistence
        public static void SaveOverrides()
        {
            if (Asset == null) return;
            SettingsStore.D.inputOverrides = Asset.SaveBindingOverridesAsJson();
            SettingsStore.Save();
        }

        public static void ResetOverrides()
        {
            if (Asset == null) return;
            Asset.RemoveAllBindingOverrides();
            SettingsStore.D.inputOverrides = "";
            SettingsStore.Save();
            SettingsAppliers.ApplyControls();
        }

        /// Binds the EventSystem's UI module to our UI map (call once the EventSystem exists).
        public static void BindUiModule(InputSystemUIInputModule module)
        {
            if (module == null || Asset == null) return;
            var ui = Asset.FindActionMap("UI");
            if (ui == null) return;
            module.actionsAsset = Asset;
            // Navigate/Submit/Cancel are driven by FocusNavigator + ScreenRouter; the module must not
            // fire them too (AddComponent assigns Unity's default UI actions before we get here).
            module.move = null;
            module.submit = null;
            module.cancel = null;
            module.point = InputActionReference.Create(ui.FindAction("Point"));
            module.leftClick = InputActionReference.Create(ui.FindAction("Click"));
            module.scrollWheel = InputActionReference.Create(ui.FindAction("ScrollWheel"));
        }
    }
}
