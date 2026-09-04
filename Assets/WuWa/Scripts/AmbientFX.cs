using UnityEngine;

namespace WuWa
{
    /// Region-flavored ambient particles that follow the camera: pink petals in
    /// the bloom hills, snowfall on the frost plateau, embers over the wastes,
    /// spores in the forest, drifting motes elsewhere. After dark the meadows
    /// fill with slow blinking fireflies.
    public class AmbientFX : MonoBehaviour
    {
        ParticleSystem _ps;
        ParticleSystemRenderer _psr;
        ParticleSystem _fire;
        Transform _cam;
        int _region = -999;
        float _check;
        bool _fireOn;

        void Start()
        {
            var go = new GameObject("~ambientPs");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _psr = go.GetComponent<ParticleSystemRenderer>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetTexture("_BaseMap", VFXLibrary.MakeSoftDot());
            SetupTransparent(mat);
            _psr.material = mat;
            _psr.renderMode = ParticleSystemRenderMode.Billboard;

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(80f, 30f, 80f);

            var main = _ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 260;

            BuildFireflies(mat);
        }

        void BuildFireflies(Material baseMat)
        {
            var go = new GameObject("~fireflies");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -1.5f, 0f);
            _fire = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(baseMat);
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f));
            // soft round glow instead of a hard square — hard dots read as noise over the grass
            var soft = VFXLibrary.MakeSoftDot();
            mat.mainTexture = soft;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", soft);
            r.material = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            _fire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var shape = _fire.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(46f, 3.5f, 46f);

            var main = _fire.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 1f, 0.4f, 0.85f), new Color(1f, 0.92f, 0.5f, 0.85f));
            main.gravityModifier = 0f;

            var em = _fire.emission;
            em.rateOverTime = 8f;

            var vel = _fire.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.25f, 0.35f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

            // blink: alpha pulses several times over the particle's life
            var col = _fire.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.1f, 0.3f),
                    new GradientAlphaKey(1f, 0.45f), new GradientAlphaKey(0.15f, 0.62f), new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var noise = _fire.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.3f;
        }

        static void SetupTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        void LateUpdate()
        {
            if (_cam == null)
            {
                var c = Camera.main;
                if (c == null) return;
                _cam = c.transform;
            }
            transform.position = _cam.position;

            _check -= Time.deltaTime;
            if (_check > 0f) return;
            _check = 0.7f;
            int r = WorldRegions.RegionAt(_cam.position.x, _cam.position.z);
            if (r != _region)
            {
                _region = r;
                Apply(r);
            }

            // fireflies: warm lowlands only, after dusk
            bool meadow = r == WorldRegions.Plains || r == WorldRegions.Forest || r == WorldRegions.Bloom
                       || r == WorldRegions.Village || r == WorldRegions.Lake;
            bool wantFire = meadow && DayNightCycle.Night01 > 0.55f;
            if (wantFire != _fireOn && _fire != null)
            {
                _fireOn = wantFire;
                if (wantFire) _fire.Play();
                else _fire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        void Apply(int region)
        {
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            var main = _ps.main;
            var em = _ps.emission;
            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;

            switch (region)
            {
                case WorldRegions.Bloom:      // drifting pink petals
                    Config(main, em, new Color(1f, 0.62f, 0.72f, 0.85f), new Color(1f, 0.8f, 0.85f, 0.7f),
                        0.10f, 0.22f, 7f, 26f);
                    Vel(vel, -1.4f, -0.9f, 1.6f);
                    break;
                case WorldRegions.Frost:      // snowfall
                    Config(main, em, new Color(0.95f, 0.97f, 1f, 0.9f), Color.white,
                        0.07f, 0.15f, 8f, 40f);
                    Vel(vel, -2.2f, -1.4f, 1.0f);
                    break;
                case WorldRegions.Waste:      // rising embers
                    Config(main, em, new Color(1f, 0.55f, 0.2f, 0.9f), new Color(1f, 0.3f, 0.1f, 0.6f),
                        0.05f, 0.12f, 5f, 14f);
                    Vel(vel, 0.8f, 1.8f, 0.9f);
                    break;
                case WorldRegions.Forest:     // green spores
                    Config(main, em, new Color(0.6f, 1f, 0.6f, 0.55f), new Color(0.8f, 1f, 0.7f, 0.4f),
                        0.05f, 0.11f, 9f, 14f);
                    Vel(vel, -0.25f, 0.25f, 0.5f);
                    break;
                case WorldRegions.Lake:       // cyan glints
                    Config(main, em, new Color(0.55f, 0.95f, 1f, 0.6f), new Color(0.8f, 1f, 1f, 0.45f),
                        0.045f, 0.10f, 6f, 9f);
                    Vel(vel, 0.1f, 0.5f, 0.4f);
                    break;
                case WorldRegions.Ruins:      // grey dust motes
                    Config(main, em, new Color(0.8f, 0.8f, 0.75f, 0.4f), new Color(0.9f, 0.9f, 0.85f, 0.3f),
                        0.05f, 0.10f, 8f, 8f);
                    Vel(vel, -0.2f, 0.2f, 0.6f);
                    break;
                case WorldRegions.Rim:        // thin high-altitude flurry
                    Config(main, em, new Color(1f, 1f, 1f, 0.5f), Color.white, 0.06f, 0.11f, 6f, 12f);
                    Vel(vel, -1.5f, -0.8f, 1.6f);
                    break;
                default:                      // plains / village: dandelion fluff
                    Config(main, em, new Color(1f, 0.98f, 0.8f, 0.55f), new Color(1f, 1f, 0.9f, 0.4f),
                        0.05f, 0.10f, 8f, 7f);
                    Vel(vel, 0.15f, 0.55f, 0.7f);
                    break;
            }
            _ps.Play();
        }

        static void Config(ParticleSystem.MainModule main, ParticleSystem.EmissionModule em,
            Color a, Color b, float sizeMin, float sizeMax, float life, float rate)
        {
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            em.rateOverTime = rate;
        }

        static void Vel(ParticleSystem.VelocityOverLifetimeModule vel, float yMin, float yMax, float drift)
        {
            vel.x = new ParticleSystem.MinMaxCurve(-drift, drift);
            vel.y = new ParticleSystem.MinMaxCurve(yMin, yMax);
            vel.z = new ParticleSystem.MinMaxCurve(-drift, drift);
        }
    }
}
