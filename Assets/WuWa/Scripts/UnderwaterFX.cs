using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WuWa
{
    /// Fontaine-style underwater look and sound, driven purely by the camera height: blue fog
    /// that closes in, a cool colour filter and vignette on the post volume, a low-pass on the
    /// listener and drifting bubbles. Lives on the main camera; runs in LateUpdate so it lands
    /// after DayNightCycle has written the day's fog colour for the frame.
    public class UnderwaterFX : MonoBehaviour
    {
        public static UnderwaterFX I { get; private set; }
        public float Wet { get; private set; }

        static readonly Color WetFog = new Color(0.07f, 0.30f, 0.40f);
        static readonly Color WetFilter = new Color(0.60f, 0.85f, 1.00f);

        Camera _cam;
        AudioLowPassFilter _lpf;
        ParticleSystem _bubbles;
        Volume _vol;
        bool _dayNight;
        bool _dryValid;
        Color _dryFog;
        float _dryStart, _dryEnd, _dryVignette;

        public static void Ensure()
        {
            if (I != null) return;
            var cam = CamCache.Main;
            if (cam == null) return;
            var fx = cam.GetComponent<UnderwaterFX>();
            if (fx == null) fx = cam.gameObject.AddComponent<UnderwaterFX>();
            I = fx;
        }

        void Awake()
        {
            I = this;
            _cam = GetComponent<Camera>();
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            Restore();
        }

        void Start()
        {
            _lpf = GetComponent<AudioLowPassFilter>();
            if (_lpf == null) _lpf = gameObject.AddComponent<AudioLowPassFilter>();
            _lpf.cutoffFrequency = 22000f;
            _lpf.enabled = false;
            _vol = FindAnyObjectByType<Volume>();
            _dayNight = FindAnyObjectByType<DayNightCycle>() != null;
            _bubbles = MakeBubbles();
        }

        ParticleSystem MakeBubbles()
        {
            var go = new GameObject("fx_underwater_bubbles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -0.5f, 3f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = new Color(0.75f, 0.92f, 1f, 0.55f);
            main.maxParticles = 200;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(7f, 4f, 7f);
            var vel = ps.velocityOverLifetime; vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = VFXLibrary.SoftDotAdditive;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        void LateUpdate()
        {
            bool under = _cam != null && _cam.transform.position.y < WorldRegions.WaterY - 0.05f;
            Wet = Mathf.MoveTowards(Wet, under ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            if (Wet <= 0.001f)
            {
                if (_dryValid) Restore();
                return;
            }

            // DayNightCycle rewrites the fog colour every Update, so re-read it each frame; the
            // start/end distances are set once by the world build and only sampled on entry.
            if (!_dryValid || _dayNight) _dryFog = _dryValid && !_dayNight ? _dryFog : RenderSettings.fogColor;
            if (!_dryValid)
            {
                _dryStart = RenderSettings.fogStartDistance;
                _dryEnd = RenderSettings.fogEndDistance;
                _dryVignette = 0.25f;
                Vignette v0;
                if (_vol != null && _vol.sharedProfile != null && _vol.profile.TryGet(out v0)) _dryVignette = v0.intensity.value;
                _dryValid = true;
            }

            RenderSettings.fogColor = Color.Lerp(_dryFog, WetFog, Wet);
            RenderSettings.fogStartDistance = Mathf.Lerp(_dryStart, 1.5f, Wet);
            RenderSettings.fogEndDistance = Mathf.Lerp(_dryEnd, 32f, Wet);

            if (_vol != null && _vol.sharedProfile != null)
            {
                var prof = _vol.profile;
                ColorAdjustments ca;
                if (prof.TryGet(out ca))
                {
                    ca.colorFilter.overrideState = true;
                    ca.colorFilter.value = Color.Lerp(Color.white, WetFilter, Wet);
                    ca.saturation.overrideState = true;
                    ca.saturation.value = -12f * Wet;
                }
                Vignette vig;
                if (prof.TryGet(out vig))
                {
                    vig.intensity.overrideState = true;
                    vig.intensity.value = Mathf.Lerp(_dryVignette, 0.42f, Wet);
                }
            }

            if (_lpf != null)
            {
                _lpf.enabled = true;
                _lpf.cutoffFrequency = Mathf.Lerp(22000f, 650f, Wet);
            }
            if (_bubbles != null)
            {
                var em = _bubbles.emission;
                em.rateOverTime = 12f * Wet;
                if (!_bubbles.isPlaying) _bubbles.Play();
            }
        }

        void Restore()
        {
            if (!_dryValid) return;
            _dryValid = false;
            RenderSettings.fogColor = _dryFog;
            RenderSettings.fogStartDistance = _dryStart;
            RenderSettings.fogEndDistance = _dryEnd;
            if (_vol != null && _vol.sharedProfile != null)
            {
                var prof = _vol.profile;
                ColorAdjustments ca;
                if (prof.TryGet(out ca)) { ca.colorFilter.value = Color.white; ca.saturation.value = 0f; }
                Vignette vig;
                if (prof.TryGet(out vig)) vig.intensity.value = _dryVignette;
            }
            if (_lpf != null) { _lpf.cutoffFrequency = 22000f; _lpf.enabled = false; }
            if (_bubbles != null) _bubbles.Stop();
        }
    }
}
