using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Code-built combat VFX (URP-compatible additive particles, arcs and rings).
    /// Textures are loaded from Resources/FX when baked, otherwise generated at runtime.
    public static class VFXLibrary
    {
        static Texture2D _softDot, _streak, _ring;
        static Material _addMat, _addStreakMat, _addRingMat;
        static GameObject _runnerGo;
        static VfxRunner _runner;

        // ------------------------------------------------------------- textures
        static Texture2D SoftDot
        {
            get
            {
                if (_softDot == null) _softDot = LoadOrMake("FX/softdot", MakeSoftDot);
                return _softDot;
            }
        }
        static Texture2D Streak
        {
            get
            {
                if (_streak == null) _streak = LoadOrMake("FX/streak", MakeStreak);
                return _streak;
            }
        }
        static Texture2D Ring
        {
            get
            {
                if (_ring == null) _ring = LoadOrMake("FX/ring", MakeRing);
                return _ring;
            }
        }

        static Texture2D LoadOrMake(string path, System.Func<Texture2D> maker)
        {
            var t = Resources.Load<Texture2D>(path);
            return t != null ? t : maker();
        }

        public static Texture2D MakeSoftDot()
        {
            int s = 64;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            t.Apply();
            return t;
        }

        public static Texture2D MakeStreak()
        {
            int w = 128, h = 32;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1), v = y / (float)(h - 1);
                    float edge = Mathf.Sin(v * Mathf.PI);
                    float body = Mathf.Sin(u * Mathf.PI);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(edge * body, 1.5f)));
                }
            t.Apply();
            return t;
        }

        public static Texture2D MakeRing()
        {
            int s = 128;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / 0.16f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------- materials
        public static Material MakeAdditive(Texture2D tex)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh);
            m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);       // additive
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            m.SetOverrideTag("RenderType", "Transparent");
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3050;
            return m;
        }

        static Material AddMat { get { if (_addMat == null) _addMat = MakeAdditive(SoftDot); return _addMat; } }
        static Material StreakMat { get { if (_addStreakMat == null) _addStreakMat = MakeAdditive(Streak); return _addStreakMat; } }
        static Material RingMat { get { if (_addRingMat == null) _addRingMat = MakeAdditive(Ring); return _addRingMat; } }
        public static Material SoftDotAdditive { get { return AddMat; } }
        public static Material StreakAdditive { get { return StreakMat; } }

        class VfxRunner : MonoBehaviour { }

        static VfxRunner Runner
        {
            get
            {
                if (_runner == null)
                {
                    _runnerGo = new GameObject("~VFX");
                    Object.DontDestroyOnLoad(_runnerGo);
                    _runner = _runnerGo.AddComponent<VfxRunner>();
                }
                return _runner;
            }
        }

        // ------------------------------------------------------------- helpers
        static ParticleSystem NewPs(string name, Vector3 pos, Material mat)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // new systems auto-play; stop before configuring
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = mat;
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            return ps;
        }

        static Gradient Grad(Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 0.25f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.4f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // ------------------------------------------------------------- effects
        public static void SpawnHitSpark(Vector3 pos, Color c, float scale = 1f)
        {
            var ps = NewPs("fx_hit", pos, AddMat);
            var main = ps.main;
            main.duration = 0.4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f * scale, 9f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f * scale, 0.3f * scale);
            main.gravityModifier = 0.35f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(c);
            var burst = ps.emission; burst.enabled = true;
            burst.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(16 * scale), (short)(24 * scale)) });
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.15f;
            // flash sprite
            Flash(pos, c, 0.55f * scale, 0.12f);
            ps.Play();
        }

        static readonly Stack<GameObject> _flashPool = new Stack<GameObject>();
        static MaterialPropertyBlock _flashMpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// Pooled camera-facing flash sprite driven by a property block (no material instances).
        public static float FlashMul = 1f;     // big screen-filling flashes scale with this (accessibility)

        public static void Flash(Vector3 pos, Color c, float size, float life)
        {
            if (size > 1.4f) size *= Mathf.Lerp(0.3f, 1f, FlashMul);
            GameObject go = null;
            while (_flashPool.Count > 0 && go == null) go = _flashPool.Pop();   // skip refs killed by a scene reload
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(go.GetComponent<Collider>());
                go.name = "fx_flash";
                go.GetComponent<MeshRenderer>().sharedMaterial = AddMat;
            }
            go.SetActive(true);
            go.transform.position = pos;
            Runner.StartCoroutine(FlashRoutine(go.transform, go.GetComponent<MeshRenderer>(), c * 2f, size, life));
        }

        static IEnumerator FlashRoutine(Transform tr, MeshRenderer mr, Color col, float size, float life)
        {
            if (_flashMpb == null) _flashMpb = new MaterialPropertyBlock();
            float t = 0f;
            var cam = CamCache.Main;
            while (t < life)
            {
                if (tr == null) yield break;
                t += Time.deltaTime;
                float k = 1f - t / life;
                tr.localScale = Vector3.one * size * (0.6f + 0.6f * (1f - k));
                if (cam != null) tr.rotation = Quaternion.LookRotation(tr.position - cam.transform.position);
                col.a = k;
                _flashMpb.SetColor(BaseColorId, col);
                mr.SetPropertyBlock(_flashMpb);
                yield return null;
            }
            if (tr != null) { tr.gameObject.SetActive(false); _flashPool.Push(tr.gameObject); }
        }

        static Mesh _slashArc, _heavyArc;
        static Mesh SlashArc { get { if (_slashArc == null) _slashArc = ArcMesh(150f, 1.35f, 0.55f); return _slashArc; } }
        static Mesh HeavyArc { get { if (_heavyArc == null) _heavyArc = ArcMesh(230f, 1.8f, 0.85f); return _heavyArc; } }
        static MaterialPropertyBlock _slashMpb;

        /// Curved slash arc following an attack swing.
        public static void SpawnSlash(Vector3 pos, Quaternion rot, Color c, int comboIndex)
        {
            float tilt = (comboIndex % 2 == 0) ? 35f : -35f;
            if (comboIndex == 3) tilt = 90f;
            var go = new GameObject("fx_slash");
            go.transform.position = pos;
            go.transform.rotation = rot * Quaternion.Euler(0f, 0f, tilt);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = SlashArc;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = StreakMat;
            Runner.StartCoroutine(SlashRoutine(go, mr, c * 1.8f, 0.22f));
        }

        public static void SpawnHeavySlash(Vector3 pos, Quaternion rot, Color c)
        {
            var go = new GameObject("fx_heavy");
            go.transform.position = pos;
            go.transform.rotation = rot * Quaternion.Euler(0f, 0f, 8f);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = HeavyArc;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = StreakMat;
            Runner.StartCoroutine(SlashRoutine(go, mr, c * 2.2f, 0.3f));
            SpawnHitSpark(pos, c, 0.8f);
        }

        static IEnumerator SlashRoutine(GameObject go, MeshRenderer mr, Color col, float life)
        {
            if (_slashMpb == null) _slashMpb = new MaterialPropertyBlock();
            float t = 0f;
            Vector3 s0 = Vector3.one * 0.65f;
            while (t < life)
            {
                if (go == null) yield break;
                t += Time.deltaTime;
                float k = t / life;
                go.transform.localScale = Vector3.Lerp(s0, Vector3.one * 1.45f, Mathf.Sqrt(k));
                col.a = 1f - k * k;
                _slashMpb.SetColor(BaseColorId, col);
                mr.SetPropertyBlock(_slashMpb);
                yield return null;
            }
            Object.Destroy(go);
        }

        static Mesh ArcMesh(float sweepDeg, float radius, float width)
        {
            int seg = 24;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float start = -sweepDeg * 0.5f * Mathf.Deg2Rad;
            float end = sweepDeg * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(start, end, i / (float)seg);
                Vector3 dir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                verts.Add(dir * (radius - width * 0.5f));
                verts.Add(dir * (radius + width * 0.5f));
                float u = i / (float)seg;
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, 1f));
                if (i < seg)
                {
                    int b = i * 2;
                    tris.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 });
                    tris.AddRange(new[] { b + 2, b + 1, b, b + 3, b + 1, b + 2 });
                }
            }
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// Expanding ground shockwave ring + sparks (skills, ultimates, boss slam).
        public static void SpawnNova(Vector3 pos, Color c, float radius, bool ult = false)
        {
            pos.y += 0.15f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "fx_nova";
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.material = new Material(RingMat);
            mr.material.SetColor("_BaseColor", c * (ult ? 2.6f : 1.9f));
            Runner.StartCoroutine(NovaRoutine(go.transform, mr, radius, ult ? 0.6f : 0.4f));

            var ps = NewPs("fx_nova_ps", pos, AddMat);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, ult ? 0.9f : 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.2f, radius * 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, ult ? 0.5f : 0.3f);
            main.gravityModifier = ult ? -0.05f : 0.15f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(c);
            var em = ps.emission; em.enabled = true;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(ult ? 70 : 34)) });
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.4f;
            ps.Play();

            if (ult) Flash(pos + Vector3.up * 1.2f, c, 4f, 0.35f);
        }

        static IEnumerator NovaRoutine(Transform tr, MeshRenderer mr, float radius, float life)
        {
            float t = 0f;
            while (t < life)
            {
                t += Time.deltaTime;
                float k = t / life;
                float ease = 1f - (1f - k) * (1f - k);
                tr.localScale = Vector3.one * Mathf.Lerp(0.5f, radius * 2.2f, ease);
                var c = mr.material.GetColor("_BaseColor"); c.a = 1f - k; mr.material.SetColor("_BaseColor", c);
                yield return null;
            }
            Object.Destroy(tr.gameObject);
        }

        public static void SpawnUltFlash(Vector3 pos, Color c)
        {
            Flash(pos + Vector3.up * 1.3f, c, 5.5f, 0.4f);
            var ps = NewPs("fx_ult_rise", pos, AddMat);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.4f);
            main.gravityModifier = -0.6f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(c);
            var em = ps.emission; em.enabled = true;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 1.6f;
            ps.Play();
        }

        public static void SpawnSwapFlash(Vector3 pos, Color c)
        {
            Flash(pos, c, 2.6f, 0.25f);
            SpawnHitSpark(pos, c, 1.1f);
        }

        public static void SpawnPerfectDodge(Vector3 pos, Color c)
        {
            Flash(pos, Color.white, 2.2f, 0.18f);
            var ps = NewPs("fx_pdodge", pos, AddMat);
            var main = ps.main;
            main.startLifetime = 0.5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(c);
            var em = ps.emission; em.enabled = true;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.8f;
            ps.Play();
        }

        public static void SpawnJumpPuff(Vector3 pos, Color c)
        {
            var ps = NewPs("fx_jump", pos, AddMat);
            var main = ps.main;
            main.startLifetime = 0.4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(c);
            var em = ps.emission; em.enabled = true;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.45f;
            ps.Play();
        }

        // ---------------------------------------------------------------- water
        static MaterialPropertyBlock _waterMpb;

        /// Flat expanding ring on the water surface (wake, entry, strokes). Property block, no
        /// material instances: a swimmer spawns three or four of these a second.
        public static void SpawnRipple(Vector3 pos, float size, float life, float alpha = 0.5f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "fx_ripple";
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(90f, Random.value * 360f, 0f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = RingMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Runner.StartCoroutine(RippleRoutine(go.transform, mr, size, life, alpha));
        }

        static IEnumerator RippleRoutine(Transform tr, MeshRenderer mr, float size, float life, float alpha)
        {
            if (_waterMpb == null) _waterMpb = new MaterialPropertyBlock();
            float t = 0f;
            while (t < life)
            {
                if (tr == null) yield break;
                t += Time.deltaTime;
                float k = t / life;
                float ease = 1f - (1f - k) * (1f - k);
                tr.localScale = Vector3.one * Mathf.Lerp(size * 0.35f, size, ease);
                _waterMpb.SetColor(BaseColorId, new Color(0.85f, 1f, 1f, alpha * (1f - k)));
                mr.SetPropertyBlock(_waterMpb);
                yield return null;
            }
            if (tr != null) Object.Destroy(tr.gameObject);
        }

        /// Entry / stroke splash: two rings and a cone of droplets that fall back.
        public static void SpawnSplash(Vector3 pos, float scale)
        {
            SpawnRipple(pos, 2.2f * scale, 0.7f, 0.45f);
            SpawnRipple(pos, 1.3f * scale, 0.5f, 0.35f);
            var ps = NewPs("fx_splash", pos, AddMat);
            ps.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f * scale, 5f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.10f);
            main.gravityModifier = 1.1f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(new Color(0.55f, 0.72f, 0.8f));
            var em = ps.emission; em.enabled = true;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(14f * scale, 6f, 36f)) });
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 35f; shape.radius = 0.35f * scale;
            ps.Play();
        }

        /// A puff of rising air bubbles (diving, underwater dash).
        public static void SpawnBubbles(Vector3 pos, int n, float rise = 1.2f)
        {
            var ps = NewPs("fx_bubbles", pos, AddMat);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.gravityModifier = -rise * 0.12f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(new Color(0.75f, 0.92f, 1f));
            var em = ps.emission; em.enabled = true;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(n, 1, 60)) });
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.25f;
            ps.Play();
        }

        public static void SpawnDashGhost(PlayerController player, Color c)
        {
            Runner.StartCoroutine(DashGhostRoutine(player, c));
        }

        static IEnumerator DashGhostRoutine(PlayerController player, Color c)
        {
            var ps = NewPs("fx_dash", player.transform.position + Vector3.up * 0.9f, AddMat);
            ps.transform.SetParent(player.transform, true);
            var main = ps.main;
            main.duration = 0.5f;
            main.startLifetime = 0.35f;
            main.startSpeed = 0.4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(c);
            var em = ps.emission; em.enabled = true; em.rateOverTime = 0f; em.rateOverDistance = 6f;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.35f;
            ps.Play();
            yield return new WaitForSeconds(0.45f);
            if (ps != null)
            {
                ps.transform.SetParent(null, true);
                ps.Stop();
            }
        }

        public static void SpawnEchoStrike(Vector3 groundPos, Color c)
        {
            Runner.StartCoroutine(EchoStrikeRoutine(groundPos, c));
        }

        // ---------------------------------------------------------- WuWa additions

        /// Converging gold double-ring above a parryable enemy (WuWa telegraph).
        public static void SpawnParryTelegraph(Transform enemy, Vector3 localOffset, float duration)
        {
            Runner.StartCoroutine(ParryTelegraphRoutine(enemy, localOffset, duration));
        }

        static IEnumerator ParryTelegraphRoutine(Transform enemy, Vector3 offset, float duration)
        {
            Color gold = new Color(1f, 0.82f, 0.2f);
            var inner = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(inner.GetComponent<Collider>());
            var outer = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(outer.GetComponent<Collider>());
            var mi = inner.GetComponent<MeshRenderer>();
            var mo = outer.GetComponent<MeshRenderer>();
            mi.material = new Material(RingMat); mi.material.SetColor("_BaseColor", gold * 1.9f);
            mo.material = new Material(RingMat); mo.material.SetColor("_BaseColor", gold * 1.4f);

            float t = 0f;
            var cam = Camera.main;
            while (t < duration && enemy != null)
            {
                t += Time.deltaTime;
                Vector3 pos = enemy.position + offset + Vector3.up * 0.55f;
                inner.transform.position = pos;
                outer.transform.position = pos;
                if (cam != null)
                {
                    var rot = Quaternion.LookRotation(pos - cam.transform.position);
                    inner.transform.rotation = rot;
                    outer.transform.rotation = rot;
                }
                float k = Mathf.Clamp01(t / duration);
                inner.transform.localScale = Vector3.one * 0.85f;
                outer.transform.localScale = Vector3.one * Mathf.Lerp(2.6f, 0.85f, k);
                var co = mo.material.GetColor("_BaseColor"); co.a = 0.55f + 0.45f * k; mo.material.SetColor("_BaseColor", co);
                yield return null;
            }
            Object.Destroy(inner);
            Object.Destroy(outer);
        }

        public static void SpawnParryFlash(Vector3 pos)
        {
            Color gold = new Color(1f, 0.85f, 0.3f);
            Flash(pos, gold, 3.2f, 0.28f);
            SpawnHitSpark(pos, gold, 1.5f);
            var ps = NewPs("fx_parry", pos, StreakMat);
            var main = ps.main;
            main.startLifetime = 0.4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(gold);
            var em = ps.emission; em.enabled = true;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.3f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            ps.Play();
        }

        public static void SpawnCounterFlash(Vector3 pos, Color c)
        {
            Flash(pos, Color.Lerp(c, Color.white, 0.5f), 2.4f, 0.2f);
            SpawnHitSpark(pos, Color.Lerp(c, new Color(1f, 0.9f, 0.4f), 0.5f), 1.2f);
        }

        /// Plunge crater: expanding ring + dust + rising shards.
        public static void SpawnPlungeImpact(Vector3 pos, Color c, float radius)
        {
            SpawnNova(pos, c, radius, false);
            SpawnNova(pos, Color.Lerp(c, Color.white, 0.4f), radius * 0.55f, false);
            var ps = NewPs("fx_plunge", pos + Vector3.up * 0.2f, AddMat);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            main.gravityModifier = 0.9f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(c);
            var em = ps.emission; em.enabled = true;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 48) });
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f; shape.radius = 0.5f;
            ps.Play();
        }

        /// Glowing rope from the player to a grapple target.
        public static void GrappleLine(Transform from, Vector3 to, float duration, Color c)
        {
            Runner.StartCoroutine(GrappleLineRoutine(from, to, duration, c));
        }

        static IEnumerator GrappleLineRoutine(Transform from, Vector3 to, float duration, Color c)
        {
            var go = new GameObject("fx_grapple");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(StreakMat);
            lr.startColor = c;
            lr.endColor = new Color(c.r, c.g, c.b, 0.4f);
            lr.startWidth = 0.2f;
            lr.endWidth = 0.06f;
            lr.positionCount = 2;
            float t = 0f;
            while (t < duration && from != null)
            {
                t += Time.deltaTime;
                Vector3 hand = from.position + Vector3.up * 1.25f + from.forward * 0.3f;
                lr.SetPosition(0, hand);
                lr.SetPosition(1, to);
                if (Vector3.Distance(hand, to) < 1.6f) break;
                yield return null;
            }
            Object.Destroy(go);
        }

        static IEnumerator EchoStrikeRoutine(Vector3 pos, Color c)
        {
            // warning ring
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = pos + Vector3.up * 0.12f;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * 6.8f;
            var mr = go.GetComponent<MeshRenderer>();
            mr.material = new Material(RingMat);
            mr.material.SetColor("_BaseColor", c * 1.4f);
            float t = 0f;
            while (t < 0.33f)
            {
                t += Time.deltaTime;
                var col = mr.material.GetColor("_BaseColor");
                col.a = 0.4f + 0.6f * Mathf.PingPong(t * 8f, 1f);
                mr.material.SetColor("_BaseColor", col);
                yield return null;
            }
            Object.Destroy(go);
            // impact beam
            var beam = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(beam.GetComponent<Collider>());
            beam.transform.position = pos + Vector3.up * 6f;
            beam.transform.localScale = new Vector3(1.6f, 14f, 1f);
            var bmr = beam.GetComponent<MeshRenderer>();
            bmr.material = new Material(StreakMat);
            bmr.material.SetColor("_BaseColor", c * 2.4f);
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 look = beam.transform.position - cam.transform.position; look.y = 0f;
                beam.transform.rotation = Quaternion.LookRotation(look) * Quaternion.Euler(0f, 0f, 90f);
            }
            SpawnNova(pos, c, 3.4f);
            float bt = 0f;
            while (bt < 0.28f)
            {
                bt += Time.deltaTime;
                var col = bmr.material.GetColor("_BaseColor");
                col.a = 1f - bt / 0.28f;
                bmr.material.SetColor("_BaseColor", col);
                yield return null;
            }
            Object.Destroy(beam);
        }
    }
}
