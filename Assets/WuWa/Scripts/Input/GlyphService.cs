using UnityEngine;
using UnityEngine.InputSystem;

namespace WuWa
{
    /// Text badges for the active device ("F", "Ⓑ", "R1"…). Sprite glyphs come
    /// with the UI kit in S2; every prompt already routes through here.
    public static class Glyph
    {
        public enum Style { Auto, Keyboard, Xbox, PlayStation }
        public static Style Current = Style.Auto;

        static bool UsePad
        {
            get
            {
                if (Current == Style.Keyboard) return false;
                if (Current == Style.Xbox || Current == Style.PlayStation) return true;
                return InputService.GamepadActive;
            }
        }
        static bool PS { get { return Current == Style.PlayStation || (Current == Style.Auto && InputService.IsPlayStation); } }

        /// Badge for an action ("Interact", "Map/Close"…). Falls back to a sensible default.
        public static string Key(string action, string fallback = "?")
        {
            if (!action.Contains("/")) action = "Player/" + action;
            var a = InputService.Action(action);
            if (a == null) return fallback;
            string group = UsePad ? WuWaInputSpec.SchemePad : WuWaInputSpec.SchemeKbm;
            for (int i = 0; i < a.bindings.Count; i++)
            {
                var b = a.bindings[i];
                if (b.isComposite) continue;
                if (string.IsNullOrEmpty(b.groups) || !b.groups.Contains(group)) continue;
                string path = b.hasOverrides ? b.overridePath : b.path;
                if (string.IsNullOrEmpty(path)) continue;
                return Badge(path);
            }
            return fallback;
        }

        /// "F  상자 열기" / "Ⓑ  상자 열기" — the interaction prompt format.
        public static string Prompt(string text, string action = "Interact")
        {
            return Key(action, "F") + "  " + text;
        }

        public static string Badge(string controlPath)
        {
            string p = controlPath.ToLowerInvariant();
            int slash = p.IndexOf('/');
            string ctl = slash >= 0 ? p.Substring(slash + 1) : p;
            bool ps = PS;
            if (p.StartsWith("<gamepad>"))
            {
                switch (ctl)
                {
                    case "buttonsouth": return ps ? "✕" : "Ⓐ";
                    case "buttoneast": return ps ? "○" : "Ⓑ";
                    case "buttonwest": return ps ? "□" : "Ⓧ";
                    case "buttonnorth": return ps ? "△" : "Ⓨ";
                    case "leftshoulder": return ps ? "L1" : "LB";
                    case "rightshoulder": return ps ? "R1" : "RB";
                    case "lefttrigger": return ps ? "L2" : "LT";
                    case "righttrigger": return ps ? "R2" : "RT";
                    case "leftstickpress": return "L3";
                    case "rightstickpress": return "R3";
                    case "leftstick": return "L스틱";
                    case "rightstick": return "R스틱";
                    case "dpad/up": return "↑";
                    case "dpad/down": return "↓";
                    case "dpad/left": return "←";
                    case "dpad/right": return "→";
                    case "dpad": return "D-pad";
                    case "start": return ps ? "OPTIONS" : "☰";
                    case "select": return ps ? "SHARE" : "⧉";
                }
                return ctl.ToUpperInvariant();
            }
            if (p.StartsWith("<mouse>"))
            {
                switch (ctl)
                {
                    case "leftbutton": return "LMB";
                    case "rightbutton": return "RMB";
                    case "middlebutton": return "MMB";
                    case "scroll/y": case "scroll": return "휠";
                    case "delta": return "마우스";
                }
                return ctl.ToUpperInvariant();
            }
            if (p.StartsWith("<keyboard>"))
            {
                switch (ctl)
                {
                    case "space": return "Space";
                    case "leftshift": return "Shift";
                    case "leftctrl": return "Ctrl";
                    case "leftalt": return "Alt";
                    case "escape": return "Esc";
                    case "enter": return "Enter";
                    case "tab": return "Tab";
                    case "uparrow": return "↑";
                    case "downarrow": return "↓";
                    case "leftarrow": return "←";
                    case "rightarrow": return "→";
                }
                if (ctl.Length == 1) return ctl.ToUpperInvariant();
                if (ctl.StartsWith("f") && ctl.Length <= 3) return ctl.ToUpperInvariant();
                return ctl.ToUpperInvariant();
            }
            return controlPath;
        }
    }
}
