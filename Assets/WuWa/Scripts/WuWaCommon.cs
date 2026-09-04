using System.Collections;
using UnityEngine;

namespace WuWa
{
    public enum Element { Spectro, Glacio, Fusion, Havoc, Aero, Electro }

    public static class ElementInfo
    {
        public static Color Of(Element e)
        {
            switch (e)
            {
                case Element.Spectro: return new Color(1.00f, 0.87f, 0.45f);
                case Element.Glacio:  return new Color(0.45f, 0.83f, 1.00f);
                case Element.Fusion:  return new Color(1.00f, 0.45f, 0.35f);
                case Element.Havoc:   return new Color(0.85f, 0.35f, 1.00f);
                case Element.Aero:    return new Color(0.45f, 1.00f, 0.75f);
                case Element.Electro: return new Color(0.75f, 0.55f, 1.00f);
                default: return Color.white;
            }
        }

        public static string KoreanName(Element e)
        {
            switch (e)
            {
                case Element.Spectro: return "회절";
                case Element.Glacio:  return "응결";
                case Element.Fusion:  return "용융";
                case Element.Havoc:   return "인멸";
                case Element.Aero:    return "기류";
                case Element.Electro: return "전도";
                default: return "?";
            }
        }
    }

    public struct DamageInfo
    {
        public float amount;
        public bool crit;
        public Element element;
        public Vector3 sourcePos;
        public float knockback;
        public float staggerPower;
        public GameObject source;
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        Transform Root { get; }
        void TakeDamage(DamageInfo info);
    }

    public static class Layers
    {
        static int _player = -2, _enemy = -2, _pickup = -2;

        public static int Player { get { if (_player == -2) _player = Find("Player", 8); return _player; } }
        public static int Enemy { get { if (_enemy == -2) _enemy = Find("Enemy", 9); return _enemy; } }
        public static int Pickup { get { if (_pickup == -2) _pickup = Find("Pickup", 10); return _pickup; } }

        public static int EnemyMask { get { return 1 << Enemy; } }
        public static int PlayerMask { get { return 1 << Player; } }

        static int Find(string name, int fallback)
        {
            int l = LayerMask.NameToLayer(name);
            return l >= 0 ? l : fallback;
        }
    }

    /// Global time manipulation: hit-stop freezes and slow motion (perfect dodge, ultimates).
    public class Hitstop : MonoBehaviour
    {
        static Hitstop _inst;
        Coroutine _running;
        float _baseFixedDelta;

        public static Hitstop I
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("~Hitstop");
                    DontDestroyOnLoad(go);
                    _inst = go.AddComponent<Hitstop>();
                }
                return _inst;
            }
        }

        void Awake() { _baseFixedDelta = Time.fixedDeltaTime; }

        public static float Mul = 1f;    // accessibility: hitstop strength
        public static float SlowMoMul = 1f;   // slow-motion flourish strength (0 = off)

        public void Freeze(float duration, float scale = 0.05f)
        {
            if (Mul <= 0.01f) return;
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Run(scale, duration * Mul, 0f));
        }

        public void SlowMo(float scale, float duration, float easeOut = 0.25f)
        {
            if (SlowMoMul <= 0.01f) return;
            scale = Mathf.Lerp(1f, scale, SlowMoMul);
            duration *= SlowMoMul;
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Run(scale, duration, easeOut));
        }

        IEnumerator Run(float scale, float duration, float easeOut)
        {
            Time.timeScale = GameDirector.MenuOpen ? 0f : scale;
            Time.fixedDeltaTime = _baseFixedDelta * scale;
            float t = 0f;
            while (t < duration) { t += Time.unscaledDeltaTime; yield return null; }
            if (easeOut > 0f)
            {
                float e = 0f;
                while (e < easeOut)
                {
                    e += Time.unscaledDeltaTime;
                    float s = Mathf.Lerp(scale, 1f, e / easeOut);
                    Time.timeScale = GameDirector.MenuOpen ? 0f : s;
                    Time.fixedDeltaTime = _baseFixedDelta * s;
                    yield return null;
                }
            }
            Time.timeScale = GameDirector.MenuOpen ? 0f : 1f;   // never un-pause an open menu
            Time.fixedDeltaTime = _baseFixedDelta;
            _running = null;
        }
    }

    /// Telegraph colours + timing (colour-blind palettes and timing assist live here).
    public static class Palette
    {
        public static Color Dodge = new Color(1f, 0.15f, 0.1f);
        public static Color Parry = new Color(1f, 0.8f, 0.15f);
        public static float TelegraphMul = 1f;

        public static void SetColorblind(int mode)
        {
            switch (mode)
            {
                case 1: Dodge = new Color(0.2f, 0.45f, 1f); Parry = new Color(1f, 0.85f, 0.2f); break;      // red-green: blue vs yellow
                case 2: Dodge = new Color(1f, 0.25f, 0.65f); Parry = new Color(0.3f, 1f, 0.5f); break;      // blue-yellow: magenta vs green
                case 3: Dodge = new Color(1f, 0.1f, 0.1f); Parry = new Color(1f, 1f, 1f); break;            // high contrast: red vs white
                default: Dodge = new Color(1f, 0.15f, 0.1f); Parry = new Color(1f, 0.8f, 0.15f); break;
            }
        }
    }

    /// Cached main camera (Camera.main is a tag lookup on every call).
    public static class CamCache
    {
        static Camera _c;
        public static Camera Main
        {
            get
            {
                if (_c == null || !_c.isActiveAndEnabled) _c = Camera.main;
                return _c;
            }
        }
    }

    /// Trauma-based camera shake, consumed by ThirdPersonCamera.
    public static class CameraShaker
    {
        public static float Trauma;
        public static float Mul = 1f;    // accessibility: shake strength
        public static void Add(float amount) { Trauma = Mathf.Clamp01(Trauma + amount * Mul); }
    }

    public static class WuWaUtil
    {
        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public static float GroundHeight(Vector3 pos, float castFrom = 60f, float castDist = 200f)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * castFrom, Vector3.down, out hit, castDist,
                    ~(Layers.PlayerMask | Layers.EnemyMask | (1 << Layers.Pickup)), QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return 0f;
        }

        public static bool HasState(Animator a, string state)
        {
            return a != null && a.isActiveAndEnabled && a.HasState(0, Animator.StringToHash(state));
        }

        public static void Fade(Animator a, string state, float blend = 0.1f)
        {
            if (a == null || !a.isActiveAndEnabled) return;
            int h = Animator.StringToHash(state);
            if (a.HasState(0, h)) a.CrossFadeInFixedTime(h, blend, 0, 0f);
        }
    }
}
