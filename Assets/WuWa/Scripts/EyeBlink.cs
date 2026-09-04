using UnityEngine;

namespace WuWa
{
    /// Eyelid animation for characters whose eyes are painted in the albedo (Meshy-style models).
    /// Drives the CharToon shader's _Blink value (0 open .. 1 closed): the shader sweeps an eyelid over the
    /// eye discs (baked in UV2) with a lash line on the moving edge, so the blink closes and opens smoothly.
    /// Falls back to a texture swap when the material has no _Blink property.
    public class EyeBlink : MonoBehaviour
    {
        [Tooltip("Albedo with the eyes painted open (used only by the texture-swap fallback)")]
        public Texture2D openTexture;
        [Tooltip("Albedo with skin painted over the eye discs (lid colour); the shader blends to it above the lid edge")]
        public Texture2D closedTexture;
        public float minInterval = 2.2f;
        public float maxInterval = 5.5f;
        [Tooltip("seconds: lid closing / fully closed hold / re-opening")]
        public float closeTime = 0.07f;
        public float holdTime = 0.045f;
        public float openTime = 0.13f;
        [Range(0f, 1f)] public float doubleBlinkChance = 0.2f;
        [Range(0f, 0.5f)] public float restLid = 0.0f;   // slight permanent droop (0 = fully open)

        [Header("Blend shape mode (models with a real eye-close shape, e.g. Unity-chan)")]
        [Tooltip("Skinned meshes carrying the eye-close shape; usually the eye mesh and the eyelash mesh")]
        public SkinnedMeshRenderer[] blendShapeTargets;
        [Tooltip("Index of the eye-close shape on those meshes; -1 disables blend shape mode")]
        public int blendShapeIndex = -1;

        static readonly int BlinkId = Shader.PropertyToID("_Blink");
        static readonly int BlinkMapId = Shader.PropertyToID("_BlinkMap");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        Renderer[] _renderers;
        MaterialPropertyBlock _block;
        bool _shaderBlink;
        float _next;
        float _phaseStart;
        int _phase;            // 0 idle, 1 closing, 2 hold, 3 opening
        bool _pendingDouble;
        float _value;

        void OnEnable()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
            _shaderBlink = false;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var m = _renderers[i] != null ? _renderers[i].sharedMaterial : null;
                if (m != null && m.HasProperty(BlinkId)) { _shaderBlink = true; break; }
            }
            _phase = 0;
            Apply(restLid);
            Schedule();
        }

        void OnDisable() { Apply(0f); }

        void Update()
        {
            if (_phase < 0) return;                 // held by ForceLid
            float now = Time.unscaledTime;
            if (_phase == 0)
            {
                if (now >= _next) { _phase = 1; _phaseStart = now; }
                return;
            }
            float t = now - _phaseStart;
            if (_phase == 1)
            {
                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, closeTime));
                Apply(Mathf.Lerp(restLid, 1f, EaseIn(k)));
                if (k >= 1f) { _phase = 2; _phaseStart = now; }
            }
            else if (_phase == 2)
            {
                Apply(1f);
                if (t >= holdTime) { _phase = 3; _phaseStart = now; }
            }
            else
            {
                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, openTime));
                Apply(Mathf.Lerp(1f, restLid, EaseOut(k)));
                if (k >= 1f)
                {
                    _phase = 0;
                    if (_pendingDouble) { _pendingDouble = false; _next = now + 0.12f; }
                    else Schedule();
                }
            }
        }

        static float EaseIn(float k) { return k * k; }
        static float EaseOut(float k) { return 1f - (1f - k) * (1f - k); }

        void Schedule()
        {
            _next = Time.unscaledTime + Random.Range(minInterval, maxInterval);
            _pendingDouble = Random.value < doubleBlinkChance;
        }

        bool BlendShapeMode
        {
            get { return blendShapeIndex >= 0 && blendShapeTargets != null && blendShapeTargets.Length > 0; }
        }

        void Apply(float v)
        {
            _value = v;
            // A model with a real eye-close shape drives that and nothing else: writing _Blink here
            // would smear the shader's eyelid sweep across every material that lacks UV2 eye coords.
            if (BlendShapeMode)
            {
                for (int i = 0; i < blendShapeTargets.Length; i++)
                {
                    var smr = blendShapeTargets[i];
                    if (smr == null || smr.sharedMesh == null) continue;
                    if (blendShapeIndex >= smr.sharedMesh.blendShapeCount) continue;
                    smr.SetBlendShapeWeight(blendShapeIndex, Mathf.Clamp01(v) * 100f);
                }
                return;
            }
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                if (_shaderBlink)
                {
                    _block.SetFloat(BlinkId, v);
                    if (closedTexture != null) _block.SetTexture(BlinkMapId, closedTexture);
                }
                else if (openTexture != null && closedTexture != null)
                {
                    _block.SetTexture(BaseMapId, v > 0.5f ? closedTexture : openTexture);
                }
                r.SetPropertyBlock(_block);
            }
        }

        /// Editor/test hook: hold the eyes at a lid value (0 open .. 1 closed) or release.
        public void ForceLid(float value, bool hold)
        {
            _phase = hold ? -1 : 0;
            Apply(value);
            if (!hold) Schedule();
        }
        public void ForceClosed(bool closed) { ForceLid(closed ? 1f : restLid, closed); }
        public float Current { get { return _value; } }
    }
}
