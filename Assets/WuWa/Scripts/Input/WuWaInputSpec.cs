using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WuWa
{
    /// Single source of truth for the action asset: maps, actions and default
    /// bindings (design doc ch.9). The editor writes it out as a .inputactions
    /// JSON with deterministic GUIDs (so saved binding overrides survive), and
    /// the runtime can rebuild the same asset in memory as a fallback.
    public static class WuWaInputSpec
    {
        public const string AssetResourcePath = "Input/WuWaInput";
        public const string SchemeKbm = "KeyboardMouse";
        public const string SchemePad = "Gamepad";

        public class ActionDef
        {
            public string map, name, type, control;
            public string interactions = "";
            public string[] kbm = new string[0];      // binding paths; "2DVector:up,down,left,right" = composite
            public string[] pad = new string[0];
            public string padInteractions = "";       // interaction applied to pad bindings only
        }

        static ActionDef A(string map, string name, string type, string control, string[] kbm, string[] pad, string interactions = "", string padInteractions = "")
        {
            return new ActionDef { map = map, name = name, type = type, control = control, kbm = kbm, pad = pad, interactions = interactions, padInteractions = padInteractions };
        }
        static string[] K(params string[] p) { return p; }

        public static readonly ActionDef[] Actions =
        {
            // ---------------------------------------------------------------- Player
            A("Player", "Move", "Value", "Vector2",
                K("2DVector:<Keyboard>/w,<Keyboard>/s,<Keyboard>/a,<Keyboard>/d", "2DVector:<Keyboard>/upArrow,<Keyboard>/downArrow,<Keyboard>/leftArrow,<Keyboard>/rightArrow"),
                K("<Gamepad>/leftStick")),
            A("Player", "Look", "Value", "Vector2", K("<Mouse>/delta"), K("<Gamepad>/rightStick")),
            A("Player", "Zoom", "Value", "Axis", K("<Mouse>/scroll/y"), new string[0]),
            A("Player", "Jump", "Button", "Button", K("<Keyboard>/space"), K("<Gamepad>/buttonSouth")),
            A("Player", "Dodge", "Button", "Button", K("<Keyboard>/leftShift", "<Mouse>/rightButton"), K("<Gamepad>/rightShoulder")),
            A("Player", "Sprint", "Button", "Button", K("<Keyboard>/leftCtrl"), K("<Gamepad>/leftStickPress")),
            A("Player", "Attack", "Button", "Button", K("<Mouse>/leftButton"), K("<Gamepad>/buttonWest")),
            A("Player", "HeavyAttack", "Button", "Button", new string[0], new string[0]),
            A("Player", "Skill", "Button", "Button", K("<Keyboard>/e"), K("<Gamepad>/buttonNorth")),
            A("Player", "Liberation", "Button", "Button", K("<Keyboard>/r"), K("<Gamepad>/rightTrigger")),
            A("Player", "EchoSkill", "Button", "Button", K("<Keyboard>/q"), K("<Gamepad>/leftShoulder")),
            A("Player", "Swap1", "Button", "Button", K("<Keyboard>/1"), K("<Gamepad>/dpad/up")),
            A("Player", "Swap2", "Button", "Button", K("<Keyboard>/2"), K("<Gamepad>/dpad/right")),
            A("Player", "Swap3", "Button", "Button", K("<Keyboard>/3"), K("<Gamepad>/dpad/down")),
            A("Player", "QuickItem", "Button", "Button", K("<Keyboard>/z"), K("<Gamepad>/dpad/left")),
            A("Player", "Flask", "Button", "Button", K("<Keyboard>/x"), K("<Gamepad>/dpad/left"), "", "hold(duration=0.4)"),
            A("Player", "Interact", "Button", "Button", K("<Keyboard>/f"), K("<Gamepad>/buttonEast")),
            A("Player", "Grapple", "Button", "Button", K("<Keyboard>/t"), K("<Gamepad>/leftTrigger")),
            A("Player", "LockOn", "Button", "Button", K("<Mouse>/middleButton", "<Keyboard>/tab"), K("<Gamepad>/rightStickPress")),
            A("Player", "LockOnHold", "Button", "Button", K("<Mouse>/middleButton", "<Keyboard>/tab"), K("<Gamepad>/rightStickPress"), "hold(duration=0.4)"),
            A("Player", "Map", "Button", "Button", K("<Keyboard>/m"), K("<Gamepad>/select")),
            A("Player", "Character", "Button", "Button", K("<Keyboard>/c"), new string[0]),
            A("Player", "Bag", "Button", "Button", K("<Keyboard>/b", "<Keyboard>/i"), new string[0]),
            A("Player", "Quest", "Button", "Button", K("<Keyboard>/j"), new string[0]),
            A("Player", "Codex", "Button", "Button", K("<Keyboard>/k"), new string[0]),
            A("Player", "Settings", "Button", "Button", K("<Keyboard>/o"), new string[0]),
            // ---------------------------------------------------------------- System (always on)
            A("System", "Pause", "Button", "Button", K("<Keyboard>/escape"), K("<Gamepad>/start")),
            A("System", "CursorFree", "Button", "Button", K("<Keyboard>/leftAlt"), new string[0]),
            A("System", "Help", "Button", "Button", K("<Keyboard>/f1"), new string[0]),
            A("System", "Save", "Button", "Button", K("<Keyboard>/f9"), new string[0]),
            A("System", "Respawn", "Button", "Button", K("<Keyboard>/f5"), new string[0]),
            A("System", "HudToggle", "Button", "Button", K("<Keyboard>/f11"), new string[0]),
            // ---------------------------------------------------------------- UI
            A("UI", "Navigate", "Value", "Vector2",
                K("2DVector:<Keyboard>/upArrow,<Keyboard>/downArrow,<Keyboard>/leftArrow,<Keyboard>/rightArrow"),
                K("<Gamepad>/dpad", "<Gamepad>/leftStick")),
            A("UI", "Submit", "Button", "Button", K("<Keyboard>/enter", "<Keyboard>/space"), K("<Gamepad>/buttonSouth")),
            A("UI", "Cancel", "Button", "Button", K("<Keyboard>/escape"), K("<Gamepad>/buttonEast")),
            A("UI", "Point", "PassThrough", "Vector2", K("<Mouse>/position"), new string[0]),
            A("UI", "Click", "PassThrough", "Button", K("<Mouse>/leftButton"), new string[0]),
            A("UI", "ScrollWheel", "PassThrough", "Vector2", K("<Mouse>/scroll"), new string[0]),
            A("UI", "TabPrev", "Button", "Button", K("<Keyboard>/q"), K("<Gamepad>/leftShoulder")),
            A("UI", "TabNext", "Button", "Button", K("<Keyboard>/e"), K("<Gamepad>/rightShoulder")),
            A("UI", "Context", "Button", "Button", K("<Keyboard>/x"), K("<Gamepad>/buttonWest")),
            A("UI", "Detail", "Button", "Button", K("<Keyboard>/y"), K("<Gamepad>/buttonNorth")),
            A("UI", "Filter", "Button", "Button", K("<Keyboard>/f"), K("<Gamepad>/rightTrigger")),
            // ---------------------------------------------------------------- Menu (hub screens)
            A("Menu", "Close", "Button", "Button", K("<Keyboard>/escape"), K("<Gamepad>/buttonEast")),
            A("Menu", "Member1", "Button", "Button", K("<Keyboard>/1"), new string[0]),
            A("Menu", "Member2", "Button", "Button", K("<Keyboard>/2"), new string[0]),
            A("Menu", "Member3", "Button", "Button", K("<Keyboard>/3"), new string[0]),
            A("Menu", "MemberPrev", "Button", "Button", new string[0], K("<Gamepad>/leftTrigger")),
            A("Menu", "MemberNext", "Button", "Button", new string[0], K("<Gamepad>/rightTrigger")),
            A("Menu", "Character", "Button", "Button", K("<Keyboard>/c"), new string[0]),
            A("Menu", "Bag", "Button", "Button", K("<Keyboard>/b", "<Keyboard>/i"), new string[0]),
            A("Menu", "Settings", "Button", "Button", K("<Keyboard>/o"), new string[0]),
            A("Menu", "Quest", "Button", "Button", K("<Keyboard>/j"), new string[0]),
            A("Menu", "Codex", "Button", "Button", K("<Keyboard>/k"), new string[0]),
            A("Menu", "Map", "Button", "Button", K("<Keyboard>/m"), K("<Gamepad>/select")),
            // ---------------------------------------------------------------- Map
            A("Map", "Close", "Button", "Button", K("<Keyboard>/m", "<Keyboard>/escape"), K("<Gamepad>/buttonEast", "<Gamepad>/select")),
            A("Map", "MarkerPrev", "Button", "Button", K("<Keyboard>/q"), K("<Gamepad>/leftShoulder")),
            A("Map", "MarkerNext", "Button", "Button", K("<Keyboard>/e"), K("<Gamepad>/rightShoulder")),
            A("Map", "Warp", "Button", "Button", K("<Keyboard>/enter", "<Keyboard>/f"), K("<Gamepad>/buttonSouth")),
            A("Map", "Zoom", "Value", "Axis", K("<Mouse>/scroll/y", "1DAxis:<Keyboard>/minus,<Keyboard>/equals"), K("1DAxis:<Gamepad>/leftTrigger,<Gamepad>/rightTrigger")),
            A("Map", "Pan", "Value", "Vector2",
                K("2DVector:<Keyboard>/w,<Keyboard>/s,<Keyboard>/a,<Keyboard>/d"), K("<Gamepad>/rightStick")),
            A("Map", "Cursor", "Value", "Vector2", new string[0], K("<Gamepad>/leftStick")),
            A("Map", "Pin", "Button", "Button", K("<Mouse>/rightButton", "<Keyboard>/p"), K("<Gamepad>/buttonWest")),
            A("Map", "Filter", "Button", "Button", K("<Keyboard>/tab"), K("<Gamepad>/buttonNorth")),
            A("Map", "Center", "Button", "Button", K("<Keyboard>/space"), K("<Gamepad>/rightStickPress")),
            // ---------------------------------------------------------------- Dialogue
            A("Dialogue", "Advance", "Button", "Button", K("<Keyboard>/f", "<Keyboard>/space", "<Keyboard>/e", "<Keyboard>/enter", "<Mouse>/leftButton"), K("<Gamepad>/buttonSouth", "<Gamepad>/buttonEast")),
            A("Dialogue", "Skip", "Button", "Button", K("<Keyboard>/escape"), K("<Gamepad>/start")),
        };

        // ---------------------------------------------------------------- deterministic ids
        public static string Guid(string key)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes("wuwa-input:" + key));
                return new Guid(bytes).ToString("D");
            }
        }

        static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }

        /// .inputactions JSON (Unity's InputActionAsset importer format).
        public static string ToJson(string assetName)
        {
            var maps = new List<string>();
            foreach (var a in Actions) if (!maps.Contains(a.map)) maps.Add(a.map);
            var sb = new StringBuilder();
            sb.Append("{\n  \"name\": \"").Append(assetName).Append("\",\n  \"maps\": [\n");
            for (int mi = 0; mi < maps.Count; mi++)
            {
                string map = maps[mi];
                sb.Append("    {\n      \"name\": \"").Append(map).Append("\",\n      \"id\": \"").Append(Guid("map/" + map)).Append("\",\n      \"actions\": [\n");
                var acts = new List<ActionDef>();
                foreach (var a in Actions) if (a.map == map) acts.Add(a);
                for (int i = 0; i < acts.Count; i++)
                {
                    var a = acts[i];
                    sb.Append("        {\n          \"name\": \"").Append(a.name).Append("\",\n          \"type\": \"").Append(a.type)
                      .Append("\",\n          \"id\": \"").Append(Guid("action/" + map + "/" + a.name))
                      .Append("\",\n          \"expectedControlType\": \"").Append(a.control)
                      .Append("\",\n          \"processors\": \"\",\n          \"interactions\": \"").Append(Esc(a.interactions))
                      .Append("\",\n          \"initialStateCheck\": ").Append(a.type == "Value" ? "true" : "false").Append("\n        }");
                    sb.Append(i < acts.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("      ],\n      \"bindings\": [\n");
                var binds = new List<string>();
                foreach (var a in acts)
                {
                    AppendBindings(binds, map, a, a.kbm, SchemeKbm, a.interactions);
                    AppendBindings(binds, map, a, a.pad, SchemePad, string.IsNullOrEmpty(a.padInteractions) ? a.interactions : a.padInteractions);
                }
                for (int i = 0; i < binds.Count; i++) sb.Append(binds[i]).Append(i < binds.Count - 1 ? ",\n" : "\n");
                sb.Append("      ]\n    }").Append(mi < maps.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ],\n  \"controlSchemes\": [\n");
            sb.Append("    {\n      \"name\": \"").Append(SchemeKbm).Append("\",\n      \"bindingGroup\": \"").Append(SchemeKbm).Append("\",\n      \"devices\": [\n");
            sb.Append("        { \"devicePath\": \"<Keyboard>\", \"isOptional\": false, \"isOR\": false },\n");
            sb.Append("        { \"devicePath\": \"<Mouse>\", \"isOptional\": false, \"isOR\": false }\n      ]\n    },\n");
            sb.Append("    {\n      \"name\": \"").Append(SchemePad).Append("\",\n      \"bindingGroup\": \"").Append(SchemePad).Append("\",\n      \"devices\": [\n");
            sb.Append("        { \"devicePath\": \"<Gamepad>\", \"isOptional\": false, \"isOR\": false }\n      ]\n    }\n  ]\n}\n");
            return sb.ToString();
        }

        static void AppendBindings(List<string> outList, string map, ActionDef a, string[] paths, string group, string interactions)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                string p = paths[i];
                string key = "bind/" + map + "/" + a.name + "/" + group + "/" + i;
                if (p.StartsWith("2DVector:"))
                {
                    var parts = p.Substring(9).Split(',');
                    outList.Add(Bind(key, "2DVector", "2DVector", "", "", "", a.name, true, false));
                    string[] names = { "up", "down", "left", "right" };
                    for (int k = 0; k < 4 && k < parts.Length; k++)
                        outList.Add(Bind(key + "/" + names[k], names[k], parts[k].Trim(), "", "", group, a.name, false, true));
                }
                else if (p.StartsWith("1DAxis:"))
                {
                    var parts = p.Substring(7).Split(',');
                    outList.Add(Bind(key, "1DAxis", "1DAxis", "", "", "", a.name, true, false));
                    string[] names = { "negative", "positive" };
                    for (int k = 0; k < 2 && k < parts.Length; k++)
                        outList.Add(Bind(key + "/" + names[k], names[k], parts[k].Trim(), "", "", group, a.name, false, true));
                }
                else outList.Add(Bind(key, "", p, interactions, "", group, a.name, false, false));
            }
        }

        static string Bind(string key, string name, string path, string interactions, string processors, string groups, string action, bool composite, bool part)
        {
            return "        {\n          \"name\": \"" + Esc(name) + "\",\n          \"id\": \"" + Guid(key) + "\",\n          \"path\": \"" + Esc(path)
                 + "\",\n          \"interactions\": \"" + Esc(interactions) + "\",\n          \"processors\": \"" + Esc(processors)
                 + "\",\n          \"groups\": \"" + groups + "\",\n          \"action\": \"" + action
                 + "\",\n          \"isComposite\": " + (composite ? "true" : "false") + ",\n          \"isPartOfComposite\": " + (part ? "true" : "false") + "\n        }";
        }

        /// Runtime fallback: build the same asset in memory (ids are not stable → overrides won't persist).
        public static InputActionAsset BuildRuntimeAsset()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "WuWaInput(runtime)";
            var maps = new Dictionary<string, InputActionMap>();
            foreach (var a in Actions)
            {
                InputActionMap map;
                if (!maps.TryGetValue(a.map, out map)) { map = asset.AddActionMap(a.map); maps[a.map] = map; }
                var type = a.type == "Value" ? InputActionType.Value : a.type == "PassThrough" ? InputActionType.PassThrough : InputActionType.Button;
                var action = map.AddAction(a.name, type, null, a.interactions, null, null, a.control);
                AddRuntimeBindings(action, a.kbm, SchemeKbm, a.interactions);
                AddRuntimeBindings(action, a.pad, SchemePad, string.IsNullOrEmpty(a.padInteractions) ? a.interactions : a.padInteractions);
            }
            asset.AddControlScheme(SchemeKbm).WithRequiredDevice("<Keyboard>").WithRequiredDevice("<Mouse>");
            asset.AddControlScheme(SchemePad).WithRequiredDevice("<Gamepad>");
            return asset;
        }

        static void AddRuntimeBindings(InputAction action, string[] paths, string group, string interactions)
        {
            foreach (var p in paths)
            {
                if (p.StartsWith("2DVector:"))
                {
                    var parts = p.Substring(9).Split(',');
                    action.AddCompositeBinding("2DVector")
                        .With("Up", parts[0].Trim(), group).With("Down", parts[1].Trim(), group)
                        .With("Left", parts[2].Trim(), group).With("Right", parts[3].Trim(), group);
                }
                else if (p.StartsWith("1DAxis:"))
                {
                    var parts = p.Substring(7).Split(',');
                    action.AddCompositeBinding("1DAxis").With("Negative", parts[0].Trim(), group).With("Positive", parts[1].Trim(), group);
                }
                else action.AddBinding(p, interactions, null, group);
            }
        }
    }
}
