using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    public enum BuffKind { Atk, Def, Stamina }

    /// Timed consumable buffs (game time, so pausing freezes them).
    public static class BuffSystem
    {
        public class Buff { public BuffKind kind; public float value; public float until; public string name; }
        static readonly List<Buff> _active = new List<Buff>();
        public static IReadOnlyList<Buff> Active { get { return _active; } }
        public static string LastApplied { get; private set; }

        public static float AtkMul { get { return 1f + Sum(BuffKind.Atk); } }
        public static float DamageTakenMul { get { return Mathf.Max(0.2f, 1f - Sum(BuffKind.Def)); } }
        public static float StaminaDrainMul { get { return Mathf.Max(0.2f, 1f - Sum(BuffKind.Stamina)); } }
        public static float StaminaRegenMul { get { return 1f + Sum(BuffKind.Stamina); } }

        static float Sum(BuffKind k)
        {
            float s = 0f;
            for (int i = 0; i < _active.Count; i++) if (_active[i].kind == k) s += _active[i].value;
            return s;
        }

        /// Same kind refreshes (no stacking).
        public static void Apply(BuffKind kind, float value, float dur, string name)
        {
            for (int i = 0; i < _active.Count; i++)
                if (_active[i].kind == kind) { _active[i].value = value; _active[i].until = Time.time + dur; _active[i].name = name; LastApplied = name + " (" + Clock(dur) + ")"; return; }
            _active.Add(new Buff { kind = kind, value = value, until = Time.time + dur, name = name });
            LastApplied = name + " (" + Clock(dur) + ")";
        }

        public static void Tick()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                if (Time.time >= _active[i].until) { HUDController.Toast(_active[i].name + " 효과 종료"); _active.RemoveAt(i); }
        }

        public static void Clear() { _active.Clear(); }

        public static string HudLine()
        {
            if (_active.Count == 0) return "";
            string s = "";
            for (int i = 0; i < _active.Count; i++)
                s += (s.Length > 0 ? "   " : "") + _active[i].name + " " + Clock(_active[i].until - Time.time);
            return s;
        }

        static string Clock(float sec)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(sec));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
