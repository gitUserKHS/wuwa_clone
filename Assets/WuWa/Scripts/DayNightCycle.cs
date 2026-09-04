using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WuWa
{
    /// Full day/night cycle: the sun (and a dim moon) sweep the sky, the anime
    /// skybox, fog and ambient light blend through keyed palettes, a scrolling
    /// noise cookie on the sun casts drifting cloud shadows, and a global
    /// _WuWaNight float lets the custom shaders deepen their shade at night.
    public class DayNightCycle : MonoBehaviour
    {
        public static DayNightCycle I { get; private set; }

        [Tooltip("Real minutes per in-game day")]
        public float dayLengthMinutes = 22f;
        public float timeOfDay = 9.5f;            // hours 0..24
        public static int DayIndex;                // in-game days elapsed (saved; daily shop stock)
        public bool cloudShadows = true;

        /// 0 = full day, 1 = full night (read by weather, critters, rifts…)
        public static float Night01 { get; private set; }
        public static bool IsNight { get { return Night01 > 0.5f; } }

        struct Key
        {
            public float t;
            public Color sun; public float sunI;
            public Color top, mid, bottom, glow, cloud, fog;
            public Color ambSky, ambEq, ambGround;
            public float night, stars;
        }

        static readonly Key[] Keys =
        {
            K(0.0f,  C(0.55f,0.62f,0.9f), 0.22f, C(0.02f,0.03f,0.08f), C(0.07f,0.09f,0.17f), C(0.04f,0.05f,0.09f), C(0.15f,0.18f,0.3f), C(0.25f,0.28f,0.4f), C(0.06f,0.08f,0.13f), C(0.10f,0.12f,0.22f), C(0.09f,0.10f,0.15f), C(0.04f,0.04f,0.06f), 1f, 1f),
            K(4.6f,  C(0.55f,0.62f,0.9f), 0.22f, C(0.02f,0.03f,0.08f), C(0.07f,0.09f,0.17f), C(0.04f,0.05f,0.09f), C(0.15f,0.18f,0.3f), C(0.25f,0.28f,0.4f), C(0.06f,0.08f,0.13f), C(0.10f,0.12f,0.22f), C(0.09f,0.10f,0.15f), C(0.04f,0.04f,0.06f), 1f, 1f),
            K(5.6f,  C(1f,0.62f,0.4f), 0.55f, C(0.16f,0.2f,0.42f), C(0.85f,0.55f,0.45f), C(0.35f,0.3f,0.4f), C(1f,0.6f,0.35f), C(0.9f,0.65f,0.6f), C(0.55f,0.45f,0.5f), C(0.35f,0.35f,0.6f), C(0.6f,0.45f,0.45f), C(0.25f,0.2f,0.2f), 0.55f, 0.35f),
            K(7.0f,  C(1f,0.85f,0.65f), 1.1f, C(0.3f,0.5f,0.9f), C(0.9f,0.85f,0.8f), C(0.55f,0.62f,0.75f), C(1f,0.85f,0.6f), C(1f,0.95f,0.9f), C(0.72f,0.76f,0.85f), C(0.6f,0.7f,0.95f), C(0.8f,0.78f,0.75f), C(0.38f,0.4f,0.42f), 0.05f, 0f),
            K(12.0f, C(1f,0.96f,0.87f), 1.35f, C(0.28f,0.52f,0.95f), C(0.78f,0.9f,1f), C(0.55f,0.68f,0.82f), C(1f,0.95f,0.82f), C(1f,1f,1f), C(0.72f,0.8f,0.9f), C(0.64f,0.76f,0.98f), C(0.8f,0.83f,0.86f), C(0.38f,0.42f,0.46f), 0f, 0f),
            K(16.5f, C(1f,0.9f,0.72f), 1.2f, C(0.3f,0.5f,0.9f), C(0.9f,0.85f,0.75f), C(0.6f,0.62f,0.72f), C(1f,0.85f,0.55f), C(1f,0.96f,0.88f), C(0.76f,0.78f,0.85f), C(0.62f,0.7f,0.92f), C(0.82f,0.78f,0.72f), C(0.4f,0.4f,0.42f), 0f, 0f),
            K(18.4f, C(1f,0.55f,0.3f), 0.7f, C(0.2f,0.22f,0.5f), C(1f,0.5f,0.35f), C(0.4f,0.3f,0.4f), C(1f,0.45f,0.25f), C(1f,0.6f,0.5f), C(0.6f,0.45f,0.5f), C(0.4f,0.35f,0.6f), C(0.7f,0.45f,0.4f), C(0.28f,0.2f,0.22f), 0.45f, 0.2f),
            K(19.6f, C(0.6f,0.55f,0.8f), 0.3f, C(0.04f,0.05f,0.14f), C(0.25f,0.18f,0.3f), C(0.08f,0.08f,0.14f), C(0.4f,0.25f,0.35f), C(0.35f,0.3f,0.45f), C(0.12f,0.12f,0.2f), C(0.15f,0.15f,0.3f), C(0.18f,0.15f,0.22f), C(0.07f,0.06f,0.09f), 0.9f, 0.7f),
            K(21.0f, C(0.55f,0.62f,0.9f), 0.22f, C(0.02f,0.03f,0.08f), C(0.07f,0.09f,0.17f), C(0.04f,0.05f,0.09f), C(0.15f,0.18f,0.3f), C(0.25f,0.28f,0.4f), C(0.06f,0.08f,0.13f), C(0.10f,0.12f,0.22f), C(0.09f,0.10f,0.15f), C(0.04f,0.04f,0.06f), 1f, 1f),
            K(24.0f, C(0.55f,0.62f,0.9f), 0.22f, C(0.02f,0.03f,0.08f), C(0.07f,0.09f,0.17f), C(0.04f,0.05f,0.09f), C(0.15f,0.18f,0.3f), C(0.25f,0.28f,0.4f), C(0.06f,0.08f,0.13f), C(0.10f,0.12f,0.22f), C(0.09f,0.10f,0.15f), C(0.04f,0.04f,0.06f), 1f, 1f),
        };

        static Color C(float r, float g, float b) { return new Color(r, g, b, 1f); }
        static Key K(float t, Color sun, float sunI, Color top, Color mid, Color bottom, Color glow, Color cloud, Color fog,
            Color ambSky, Color ambEq, Color ambGround, float night, float stars)
        {
            return new Key { t = t, sun = sun, sunI = sunI, top = top, mid = mid, bottom = bottom, glow = glow, cloud = cloud, fog = fog,
                             ambSky = ambSky, ambEq = ambEq, ambGround = ambGround, night = night, stars = stars };
        }

        Light _sun;
        UniversalAdditionalLightData _sunData;
        Material _sky;
        Texture2D _cookie;
        Vector2 _cookieScroll;
        Light[] _lamps;
        float[] _lampBase;
        float _lampPoll;

        static readonly int TopId = Shader.PropertyToID("_TopColor");
        static readonly int MidId = Shader.PropertyToID("_MidColor");
        static readonly int BottomId = Shader.PropertyToID("_BottomColor");
        static readonly int GlowId = Shader.PropertyToID("_HorizonGlow");
        static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        static readonly int SunColId = Shader.PropertyToID("_SunColor");
        static readonly int SunSizeId = Shader.PropertyToID("_SunSize");
        static readonly int CloudId = Shader.PropertyToID("_CloudColor");
        static readonly int StarsId = Shader.PropertyToID("_StarIntensity");
        static readonly int MoonDirId = Shader.PropertyToID("_MoonDir");
        static readonly int MoonColId = Shader.PropertyToID("_MoonColor");
        static readonly int NightId = Shader.PropertyToID("_WuWaNight");

        public string TimeString
        {
            get
            {
                int hh = Mathf.FloorToInt(timeOfDay) % 24;
                int mm = Mathf.FloorToInt((timeOfDay - Mathf.Floor(timeOfDay)) * 60f);
                return hh.ToString("00") + ":" + mm.ToString("00");
            }
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; Shader.SetGlobalFloat(NightId, 0f); }

        void Start()
        {
            _sun = RenderSettings.sun;
            if (_sun == null)
            {
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { _sun = l; break; }
            }
            if (_sun != null)
            {
                _sunData = _sun.GetComponent<UniversalAdditionalLightData>();
                if (_sunData == null) _sunData = _sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            }
            if (RenderSettings.skybox != null)
            {
                _sky = new Material(RenderSettings.skybox);      // instance: never dirty the asset
                RenderSettings.skybox = _sky;
            }
            if (cloudShadows && _sun != null) SetupCloudCookie();

            // warm village lamps brighten after dusk
            var lamps = new System.Collections.Generic.List<Light>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Point && l.gameObject.name.ToLowerInvariant().Contains("lamp")) lamps.Add(l);
            _lamps = lamps.ToArray();
            _lampBase = new float[_lamps.Length];
            for (int i = 0; i < _lamps.Length; i++) _lampBase[i] = _lamps[i].intensity;

            Apply();
        }

        void SetupCloudCookie()
        {
            const int res = 256;
            _cookie = new Texture2D(res, res, TextureFormat.RGBA32, true, true);
            _cookie.wrapMode = TextureWrapMode.Repeat;
            _cookie.filterMode = FilterMode.Bilinear;
            var px = new Color32[res * res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    // tileable fbm via 4-corner blend
                    float u = x / (float)res, v = y / (float)res;
                    float n = TileNoise(u, v, 3.0f) * 0.55f + TileNoise(u, v, 7.0f) * 0.3f + TileNoise(u, v, 15.0f) * 0.15f;
                    float cloud = Mathf.SmoothStep(0.42f, 0.66f, n);
                    byte g = (byte)Mathf.RoundToInt(Mathf.Lerp(1f, 0.55f, cloud) * 255f);
                    px[y * res + x] = new Color32(g, g, g, 255);
                }
            _cookie.SetPixels32(px);
            _cookie.Apply(true, false);
            _sun.cookie = _cookie;
            if (_sunData != null)
            {
                _sunData.lightCookieSize = new Vector2(520f, 520f);
                _sunData.lightCookieOffset = Vector2.zero;
            }
        }

        static float TileNoise(float u, float v, float freq)
        {
            // blend 4 offset samples so the texture tiles seamlessly
            float a = Mathf.PerlinNoise(u * freq + 11.3f, v * freq + 5.1f);
            float b = Mathf.PerlinNoise((u - 1f) * freq + 11.3f, v * freq + 5.1f);
            float c = Mathf.PerlinNoise(u * freq + 11.3f, (v - 1f) * freq + 5.1f);
            float d = Mathf.PerlinNoise((u - 1f) * freq + 11.3f, (v - 1f) * freq + 5.1f);
            float ab = Mathf.Lerp(a, b, u);
            float cd = Mathf.Lerp(c, d, u);
            return Mathf.Lerp(ab, cd, v);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt > 0f && dayLengthMinutes > 0.01f)
            {
                float next = timeOfDay + dt * 24f / (dayLengthMinutes * 60f);
                if (next >= 24f) DayIndex++;
                timeOfDay = Mathf.Repeat(next, 24f);
            }
            if (_cookie != null && _sunData != null)
            {
                _cookieScroll += new Vector2(2.4f, 1.1f) * dt;
                _sunData.lightCookieOffset = _cookieScroll;
            }
            Apply();

            _lampPoll -= dt;
            if (_lampPoll <= 0f && _lamps != null)
            {
                _lampPoll = 0.5f;
                for (int i = 0; i < _lamps.Length; i++)
                    if (_lamps[i] != null) _lamps[i].intensity = _lampBase[i] * Mathf.Lerp(0.35f, 1.6f, Night01);
            }
        }

        public void SetTime(float hours)
        {
            timeOfDay = Mathf.Repeat(hours, 24f);
            Apply();
        }

        Key Sample(float t)
        {
            for (int i = 0; i < Keys.Length - 1; i++)
            {
                if (t >= Keys[i].t && t <= Keys[i + 1].t)
                {
                    float f = Mathf.InverseLerp(Keys[i].t, Keys[i + 1].t, t);
                    f = f * f * (3f - 2f * f);
                    return Blend(Keys[i], Keys[i + 1], f);
                }
            }
            return Keys[0];
        }

        static Key Blend(Key a, Key b, float f)
        {
            return new Key
            {
                sun = Color.Lerp(a.sun, b.sun, f), sunI = Mathf.Lerp(a.sunI, b.sunI, f),
                top = Color.Lerp(a.top, b.top, f), mid = Color.Lerp(a.mid, b.mid, f), bottom = Color.Lerp(a.bottom, b.bottom, f),
                glow = Color.Lerp(a.glow, b.glow, f), cloud = Color.Lerp(a.cloud, b.cloud, f), fog = Color.Lerp(a.fog, b.fog, f),
                ambSky = Color.Lerp(a.ambSky, b.ambSky, f), ambEq = Color.Lerp(a.ambEq, b.ambEq, f), ambGround = Color.Lerp(a.ambGround, b.ambGround, f),
                night = Mathf.Lerp(a.night, b.night, f), stars = Mathf.Lerp(a.stars, b.stars, f),
            };
        }

        void Apply()
        {
            var k = Sample(timeOfDay);
            Night01 = k.night;
            Shader.SetGlobalFloat(NightId, k.night);

            // sun path: rises at 6, peaks at 12, sets at 18; the moon takes the opposite arc
            float pitch = (timeOfDay - 6f) / 12f * 180f;
            float yaw = 205f + (timeOfDay - 12f) * 4f;
            bool sunUp = pitch > 2f && pitch < 178f;
            Quaternion sunRot = Quaternion.Euler(pitch, yaw, 0f);
            Quaternion moonRot = Quaternion.Euler(pitch - 180f, yaw + 30f, 0f);
            Vector3 sunDir = -(sunRot * Vector3.forward);
            Vector3 moonDir = -(moonRot * Vector3.forward);

            if (_sun != null)
            {
                _sun.transform.rotation = sunUp ? sunRot : moonRot;
                _sun.color = k.sun;
                _sun.intensity = k.sunI;
                _sun.shadowStrength = Mathf.Lerp(0.86f, 0.6f, k.night);
            }

            if (_sky != null)
            {
                _sky.SetColor(TopId, k.top);
                _sky.SetColor(MidId, k.mid);
                _sky.SetColor(BottomId, k.bottom);
                _sky.SetColor(GlowId, k.glow);
                _sky.SetColor(CloudId, k.cloud);
                _sky.SetVector(SunDirId, sunDir);
                _sky.SetColor(SunColId, k.sun * Mathf.Clamp01(k.sunI));
                _sky.SetFloat(SunSizeId, 0.035f);
                _sky.SetFloat(StarsId, k.stars);
                _sky.SetVector(MoonDirId, moonDir);
                _sky.SetColor(MoonColId, new Color(0.85f, 0.9f, 1f) * (sunUp ? 0f : 0.9f));
            }

            RenderSettings.fogColor = k.fog;
            RenderSettings.ambientSkyColor = k.ambSky;
            RenderSettings.ambientEquatorColor = k.ambEq;
            RenderSettings.ambientGroundColor = k.ambGround;
        }
    }
}
