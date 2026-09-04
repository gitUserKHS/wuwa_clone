using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WuWa
{
    public enum HapticCategory { Combat, Move, Fx, UI }

    /// Envelope mixer for gamepad rumble: requests decay on unscaled time, the
    /// per-frame max of each motor is sent once. Design doc ch.9.5.
    public class HapticsService : MonoBehaviour
    {
        public static float Intensity = 0.5f;
        public static bool Combat = true, Move = true, Fx = true, UI = false;
        public static bool LightBar = true;

        struct Req { public float low, high, end, dur; public HapticCategory cat; }
        static readonly List<Req> _reqs = new List<Req>();
        static bool _idle = true;

        public static void Play(float low, float high, float duration, HapticCategory cat)
        {
            if (Intensity <= 0.001f || !Enabled(cat)) return;
            _reqs.Add(new Req { low = low, high = high, dur = Mathf.Max(0.01f, duration), end = Time.unscaledTime + duration, cat = cat });
        }

        static bool Enabled(HapticCategory c)
        {
            switch (c)
            {
                case HapticCategory.Combat: return Combat;
                case HapticCategory.Move: return Move;
                case HapticCategory.Fx: return Fx;
                default: return UI;
            }
        }

        // convenience presets (design table)
        public static void Hit(float weight) { Play(Mathf.Lerp(0.15f, 0.35f, weight), Mathf.Lerp(0.35f, 0.6f, weight), Mathf.Lerp(0.06f, 0.12f, weight), HapticCategory.Combat); }
        public static void HeavyHit() { Play(0.55f, 0.7f, 0.14f, HapticCategory.Combat); }
        public static void Parry() { Play(0.7f, 1f, 0.12f, HapticCategory.Combat); }
        public static void PerfectDodge() { Play(0.2f, 0.6f, 0.08f, HapticCategory.Combat); }
        public static void Hurt(float frac) { Play(0.5f * Mathf.Clamp01(frac * 4f), 0.2f, 0.2f, HapticCategory.Combat); }
        public static void Dash() { Play(0.1f, 0.25f, 0.05f, HapticCategory.Move); }
        public static void Land() { Play(0f, 0.15f, 0.04f, HapticCategory.Move); }
        public static void GrappleFire() { Play(0.3f, 0.5f, 0.15f, HapticCategory.Move); }
        public static void Intro() { Play(0.4f, 0.4f, 0.12f, HapticCategory.Combat); }
        public static void Event() { Play(0.5f, 0.5f, 0.4f, HapticCategory.Fx); }

        void Update()
        {
            var pad = Gamepad.current;
            if (pad == null) { _reqs.Clear(); return; }
            if (GameDirector.MenuOpen || !Application.isFocused)
            {
                if (!_idle) { pad.SetMotorSpeeds(0f, 0f); _idle = true; }
                _reqs.Clear();
                return;
            }
            float now = Time.unscaledTime;
            float low = 0f, high = 0f;
            for (int i = _reqs.Count - 1; i >= 0; i--)
            {
                var r = _reqs[i];
                if (now >= r.end) { _reqs.RemoveAt(i); continue; }
                float k = (r.end - now) / r.dur;           // linear decay
                low = Mathf.Max(low, r.low * k);
                high = Mathf.Max(high, r.high * k);
            }
            low *= Intensity; high *= Intensity;
            if (low > 0.001f || high > 0.001f) { pad.SetMotorSpeeds(low, high); _idle = false; }
            else if (!_idle) { pad.SetMotorSpeeds(0f, 0f); _idle = true; }
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) { InputSystem.PauseHaptics(); _idle = true; }
            else InputSystem.ResumeHaptics();
        }

        void OnDestroy() { InputSystem.ResetHaptics(); }
    }
}
