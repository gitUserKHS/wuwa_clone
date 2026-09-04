using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WuWa
{
    /// Virtual devices for CLI play tests: events go through the real action
    /// asset, so bindings, contexts and glyphs are exercised end to end.
    public static class InputSim
    {
        static Gamepad _pad;
        static Keyboard _kb;
        static Mouse _mouse;
        static GamepadState _padState;
        static float _wheelAccum;

        public static Gamepad Pad
        {
            get
            {
                if (_pad == null || !_pad.added) { _pad = InputSystem.AddDevice<Gamepad>("WuWaSimPad"); _padState = new GamepadState(); }
                return _pad;
            }
        }
        public static Keyboard Kb { get { if (_kb == null || !_kb.added) _kb = InputSystem.AddDevice<Keyboard>("WuWaSimKb"); return _kb; } }
        public static Mouse MouseDev { get { if (_mouse == null || !_mouse.added) _mouse = InputSystem.AddDevice<Mouse>("WuWaSimMouse"); return _mouse; } }

        public static void PadButton(GamepadButton b, bool down)
        {
            var pad = Pad;
            uint bit = 1u << (int)b;
            if (down) _padState.buttons |= bit; else _padState.buttons &= ~bit;
            InputSystem.QueueStateEvent(pad, _padState);
        }

        public static void PadSticks(Vector2 left, Vector2 right)
        {
            var pad = Pad;
            _padState.leftStick = left;
            _padState.rightStick = right;
            InputSystem.QueueStateEvent(pad, _padState);
        }

        public static void PadTriggers(float lt, float rt)
        {
            var pad = Pad;
            _padState.leftTrigger = lt;
            _padState.rightTrigger = rt;
            InputSystem.QueueStateEvent(pad, _padState);
        }

        public static void Key(Key key, bool down)
        {
            var kb = Kb;
            var st = new KeyboardState();
            if (down) st.Set(key, true);
            InputSystem.QueueStateEvent(kb, st);
        }

        public static void MouseScroll(float notches)
        {
            var m = MouseDev;
            var st = new MouseState { scroll = new Vector2(0f, notches * 120f) };
            InputSystem.QueueStateEvent(m, st);
        }

        public static void RemoveAll()
        {
            if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
            if (_kb != null && _kb.added) InputSystem.RemoveDevice(_kb);
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
            _pad = null; _kb = null; _mouse = null;
        }
    }
}
