using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Small procedural audio synthesizer so the demo has combat feedback
    /// without shipping third-party audio. Clips are generated once and cached.
    public static class Sfx
    {
        const int SR = 44100;
        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
        static System.Random _rng = new System.Random(1234);

        static AudioClip Get(string key, System.Func<float[]> gen)
        {
            AudioClip c;
            if (Cache.TryGetValue(key, out c) && c != null) return c;
            float[] data = gen();
            c = AudioClip.Create(key, data.Length, 1, SR, false);
            c.SetData(data, 0);
            Cache[key] = c;
            return c;
        }

        static float Rand() { return (float)(_rng.NextDouble() * 2.0 - 1.0); }

        // ---- envelopes / helpers -------------------------------------------------
        static float Env(float t, float dur, float attack, float pow)
        {
            if (t < attack) return t / Mathf.Max(attack, 1e-4f);
            float r = Mathf.Clamp01((t - attack) / Mathf.Max(dur - attack, 1e-4f));
            return Mathf.Pow(1f - r, pow);
        }

        static float[] NoiseBurst(float dur, float attack, float pow, float lowpass, float gain)
        {
            int n = (int)(dur * SR);
            float[] d = new float[n];
            float last = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float w = Rand();
                last = Mathf.Lerp(last, w, lowpass);
                d[i] = last * Env(t, dur, attack, pow) * gain;
            }
            return d;
        }

        static void AddSine(float[] d, float f0, float f1, float gain, float attack, float pow)
        {
            float dur = d.Length / (float)SR;
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float f = Mathf.Lerp(f0, f1, t / dur);
                phase += 2f * Mathf.PI * f / SR;
                d[i] += Mathf.Sin(phase) * gain * Env(t, dur, attack, pow);
            }
        }

        static void SoftClip(float[] d)
        {
            for (int i = 0; i < d.Length; i++) d[i] = Mathf.Clamp(d[i] * 1.4f / (1f + Mathf.Abs(d[i])), -1f, 1f);
        }

        // ---- public clips --------------------------------------------------------
        public static AudioClip Whoosh()
        {
            return Get("whoosh", () =>
            {
                var d = NoiseBurst(0.22f, 0.05f, 2.2f, 0.12f, 0.55f);
                AddSine(d, 320f, 140f, 0.10f, 0.04f, 2f);
                SoftClip(d); return d;
            });
        }

        public static AudioClip Hit()
        {
            return Get("hit", () =>
            {
                var d = NoiseBurst(0.15f, 0.002f, 3.8f, 0.68f, 1.0f);
                AddSine(d, 260f, 70f, 0.65f, 0.002f, 4.5f);
                AddSine(d, 2400f, 400f, 0.16f, 0.002f, 7f);
                SoftClip(d); return d;
            });
        }

        public static AudioClip HitHeavy()
        {
            return Get("hitheavy", () =>
            {
                var d = NoiseBurst(0.32f, 0.002f, 3.0f, 0.6f, 1.0f);
                AddSine(d, 120f, 34f, 0.95f, 0.002f, 3.2f);
                AddSine(d, 3200f, 300f, 0.2f, 0.002f, 8f);
                AddSine(d, 640f, 90f, 0.3f, 0.002f, 5f);
                SoftClip(d); return d;
            });
        }

        public static AudioClip HitCrit()
        {
            return Get("hitcrit", () =>
            {
                var d = NoiseBurst(0.30f, 0.004f, 3.2f, 0.7f, 0.85f);
                AddSine(d, 880f, 110f, 0.4f, 0.004f, 5f);
                AddSine(d, 1760f, 220f, 0.18f, 0.004f, 6f);
                SoftClip(d); return d;
            });
        }

        public static AudioClip Dash()
        {
            return Get("dash", () =>
            {
                var d = NoiseBurst(0.28f, 0.02f, 2.6f, 0.09f, 0.5f);
                AddSine(d, 900f, 180f, 0.12f, 0.02f, 3f);
                SoftClip(d); return d;
            });
        }

        public static AudioClip Jump()
        {
            return Get("jump", () =>
            {
                var d = NoiseBurst(0.14f, 0.02f, 2.5f, 0.2f, 0.25f);
                AddSine(d, 240f, 480f, 0.25f, 0.02f, 2.5f);
                return d;
            });
        }

        public static AudioClip Land()
        {
            return Get("land", () =>
            {
                var d = NoiseBurst(0.12f, 0.004f, 3.5f, 0.5f, 0.5f);
                AddSine(d, 130f, 50f, 0.5f, 0.004f, 4f);
                SoftClip(d); return d;
            });
        }

        public static AudioClip Splash()
        {
            return Get("splash", () =>
            {
                var d = NoiseBurst(0.5f, 0.01f, 2.2f, 0.32f, 0.55f);
                AddSine(d, 260f, 70f, 0.35f, 0.01f, 3f);
                SoftClip(d); return d;
            });
        }

        /// One breaststroke: a soft, low swoosh of water.
        public static AudioClip Stroke()
        {
            return Get("stroke", () =>
            {
                var d = NoiseBurst(0.55f, 0.12f, 1.6f, 0.10f, 0.32f);
                AddSine(d, 180f, 120f, 0.08f, 0.1f, 2f);
                return d;
            });
        }

        public static AudioClip Bubble()
        {
            return Get("bubble", () =>
            {
                var d = NoiseBurst(0.25f, 0.02f, 2f, 0.2f, 0.15f);
                AddSine(d, 520f, 980f, 0.2f, 0.02f, 2.5f);
                return d;
            });
        }

        public static AudioClip Skill()
        {
            return Get("skill", () =>
            {
                var d = NoiseBurst(0.55f, 0.03f, 2.4f, 0.35f, 0.4f);
                AddSine(d, 300f, 1200f, 0.35f, 0.03f, 2.2f);
                AddSine(d, 150f, 600f, 0.25f, 0.03f, 2.2f);
                SoftClip(d); return d;
            });
        }

        public static AudioClip Ult()
        {
            return Get("ult", () =>
            {
                var d = NoiseBurst(1.1f, 0.02f, 2.0f, 0.5f, 0.6f);
                AddSine(d, 60f, 30f, 0.8f, 0.02f, 2.0f);
                AddSine(d, 400f, 2400f, 0.22f, 0.05f, 2.5f);
                AddSine(d, 800f, 3600f, 0.10f, 0.05f, 3f);
                SoftClip(d); return d;
            });
        }

        public static AudioClip Swap()
        {
            return Get("swap", () =>
            {
                var d = NoiseBurst(0.4f, 0.02f, 2.6f, 0.25f, 0.3f);
                AddSine(d, 500f, 1500f, 0.35f, 0.02f, 3f);
                AddSine(d, 750f, 2250f, 0.2f, 0.02f, 3f);
                return d;
            });
        }

        public static AudioClip Hurt()
        {
            return Get("hurt", () =>
            {
                var d = NoiseBurst(0.2f, 0.004f, 3f, 0.4f, 0.5f);
                AddSine(d, 300f, 90f, 0.4f, 0.004f, 3.5f);
                SoftClip(d); return d;
            });
        }

        public static AudioClip PerfectDodge()
        {
            return Get("pdodge", () =>
            {
                var d = new float[(int)(0.7f * SR)];
                AddSine(d, 1200f, 300f, 0.30f, 0.01f, 2.2f);
                AddSine(d, 1800f, 450f, 0.18f, 0.01f, 2.4f);
                AddSine(d, 2400f, 600f, 0.10f, 0.01f, 2.8f);
                return d;
            });
        }

        public static AudioClip EnemyDie()
        {
            return Get("edie", () =>
            {
                var d = NoiseBurst(0.6f, 0.01f, 2.6f, 0.3f, 0.55f);
                AddSine(d, 500f, 60f, 0.45f, 0.01f, 2.6f);
                SoftClip(d); return d;
            });
        }

        public static AudioClip Absorb()
        {
            return Get("absorb", () =>
            {
                var d = new float[(int)(0.5f * SR)];
                AddSine(d, 400f, 1600f, 0.3f, 0.02f, 2f);
                AddSine(d, 600f, 2400f, 0.15f, 0.02f, 2f);
                return d;
            });
        }

        public static AudioClip WindLoop()
        {
            return Get("wind", () =>
            {
                int n = SR * 6;
                var d = new float[n];
                float last = 0f, slow = 0f;
                for (int i = 0; i < n; i++)
                {
                    float w = Rand();
                    last = Mathf.Lerp(last, w, 0.02f);
                    slow = Mathf.Lerp(slow, Rand(), 0.0006f);
                    float amp = 0.16f + 0.10f * slow;
                    // crossfade the loop ends together
                    float fade = 1f;
                    int edge = SR / 2;
                    if (i < edge) fade = i / (float)edge;
                    else if (i > n - edge) fade = (n - i) / (float)edge;
                    d[i] = last * amp * Mathf.Lerp(0.6f, 1f, fade);
                }
                return d;
            });
        }
    }

    /// Tiny pooled audio player.
    public class AudioMan : MonoBehaviour
    {
        public static float SfxMul = 1f;    // options screen
        static AudioMan _inst;
        readonly List<AudioSource> _pool = new List<AudioSource>();

        public static AudioMan I
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("~AudioMan");
                    DontDestroyOnLoad(go);
                    _inst = go.AddComponent<AudioMan>();
                }
                return _inst;
            }
        }

        AudioSource GetSource()
        {
            for (int i = 0; i < _pool.Count; i++)
                if (!_pool[i].isPlaying) return _pool[i];
            var src = new GameObject("sfx").AddComponent<AudioSource>();
            src.transform.SetParent(transform);
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.maxDistance = 60f;
            _pool.Add(src);
            return src;
        }

        public void Play(AudioClip clip, Vector3 pos, float vol = 1f, float pitch = 1f, float jitter = 0.06f, float spatial = 0.85f)
        {
            if (clip == null) return;
            var s = GetSource();
            s.transform.position = pos;
            s.spatialBlend = spatial;
            s.pitch = pitch + Random.Range(-jitter, jitter);
            s.volume = vol * SfxMul;
            s.clip = clip;
            s.Play();
        }

        public void Play2D(AudioClip clip, float vol = 1f, float pitch = 1f)
        {
            Play(clip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, vol, pitch, 0.03f, 0f);
        }
    }
}
