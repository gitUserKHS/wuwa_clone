using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WuWa
{
    /// Minimal cinematic director: letterbox bars, subtitle lines and a camera
    /// override. Used for the chapter intro, tower activation and boss entrance.
    public class Cutscene : MonoBehaviour
    {
        public static Cutscene I { get; private set; }
        public static bool Active { get; private set; }

        Canvas _canvas;
        RectTransform _barTop, _barBottom;
        Text _subtitle, _titleCard, _skipHint;
        Font _font;
        float _hold;
        const float SkipHold = 1.2f;

        void Awake()
        {
            I = this;
            _font = GetFont();
            Build();
        }

        void OnDestroy() { if (I == this) I = null; Active = false; }

        static Font GetFont()
        {
            string[] names = { "Malgun Gothic", "malgun", "Segoe UI", "Arial" };
            foreach (var n in names)
            {
                try { var f = Font.CreateDynamicFontFromOSFont(n, 22); if (f != null) return f; } catch { }
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void Build()
        {
            var go = new GameObject("CutsceneCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 80;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _barTop = Bar(new Vector2(0.5f, 1f));
            _barBottom = Bar(new Vector2(0.5f, 0f));

            var sub = new GameObject("subtitle");
            sub.transform.SetParent(_canvas.transform, false);
            var srt = sub.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 0f); srt.anchorMax = new Vector2(0.5f, 0f);
            srt.pivot = new Vector2(0.5f, 0f);
            srt.anchoredPosition = new Vector2(0f, 150f);
            srt.sizeDelta = new Vector2(1400f, 60f);
            _subtitle = sub.AddComponent<Text>();
            _subtitle.font = _font; _subtitle.fontSize = 26; _subtitle.alignment = TextAnchor.MiddleCenter;
            _subtitle.color = new Color(1f, 0.97f, 0.9f, 0f);
            var ol = sub.AddComponent<Outline>();
            ol.effectColor = new Color(0f, 0f, 0f, 0.85f);
            ol.effectDistance = new Vector2(1.6f, -1.6f);

            var tc = new GameObject("titleCard");
            tc.transform.SetParent(_canvas.transform, false);
            var trt = tc.AddComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(0f, 120f);
            trt.sizeDelta = new Vector2(1600f, 120f);
            _titleCard = tc.AddComponent<Text>();
            _titleCard.font = _font; _titleCard.fontSize = 72; _titleCard.fontStyle = FontStyle.Bold;
            _titleCard.alignment = TextAnchor.MiddleCenter;
            _titleCard.color = new Color(1f, 0.9f, 0.55f, 0f);
            var ol2 = tc.AddComponent<Outline>();
            ol2.effectColor = new Color(0f, 0f, 0f, 0.9f);
            ol2.effectDistance = new Vector2(2.2f, -2.2f);

            var sh = new GameObject("skipHint");
            sh.transform.SetParent(_canvas.transform, false);
            var shrt = sh.AddComponent<RectTransform>();
            shrt.anchorMin = shrt.anchorMax = new Vector2(1f, 0f); shrt.pivot = new Vector2(1f, 0f);
            shrt.anchoredPosition = new Vector2(-40f, 28f); shrt.sizeDelta = new Vector2(420f, 30f);
            _skipHint = sh.AddComponent<Text>();
            _skipHint.font = _font; _skipHint.fontSize = 18; _skipHint.alignment = TextAnchor.MiddleRight;
            _skipHint.color = new Color(1f, 1f, 1f, 0.5f);
            _skipHint.raycastTarget = false;
            var ol3 = sh.AddComponent<Outline>();
            ol3.effectColor = new Color(0f, 0f, 0f, 0.8f);

            _canvas.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- hold-to-skip
        void Update()
        {
            if (!Active) { _hold = 0f; return; }
            _hold = InputService.PauseHeld ? _hold + Time.unscaledDeltaTime : 0f;
            if (_skipHint != null)
            {
                string k = Glyph.Key("System/Pause", "Esc");
                _skipHint.text = _hold > 0f ? k + " 홀드 — 스킵 " + Mathf.RoundToInt(Mathf.Clamp01(_hold / SkipHold) * 100f) + "%" : k + " 홀드 — 스킵";
                _skipHint.color = new Color(1f, 0.92f, 0.7f, _hold > 0f ? 0.95f : 0.5f);
            }
            if (_hold >= SkipHold) Skip();
        }

        /// Stops the running routine, hands the camera back and closes the bars.
        public void Skip()
        {
            if (!Active) return;
            _hold = 0f;
            StopAllCoroutines();
            var cam = Camera.main;
            var tpc = cam != null ? cam.GetComponent<ThirdPersonCamera>() : null;
            if (tpc != null) tpc.enabled = true;
            _titleCard.color = new Color(_titleCard.color.r, _titleCard.color.g, _titleCard.color.b, 0f);
            StartCoroutine(End());
        }

        RectTransform Bar(Vector2 anchor)
        {
            var go = new GameObject("bar");
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, anchor.y); rt.anchorMax = new Vector2(1f, anchor.y);
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(0f, 0f);
            var img = go.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
            return rt;
        }

        // ---------------------------------------------------------------- API
        public void PlayIntro()
        {
            StartCoroutine(IntroRoutine());
        }

        public void PlayTowerActivation(Vector3 towerPos, string towerName)
        {
            StartCoroutine(TowerRoutine(towerPos, towerName));
        }

        public void PlayBossIntro(Transform boss)
        {
            StartCoroutine(BossRoutine(boss));
        }

        /// Chapter break: letterbox, big title card, one subtitle line.
        public void PlayChapterCard(string title, string sub)
        {
            StartCoroutine(ChapterRoutine(title, sub));
        }

        IEnumerator ChapterRoutine(string title, string sub)
        {
            yield return Begin();
            _titleCard.text = title;
            float t = 0f;
            while (t < 2.6f)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Min(1f, t / 0.5f);
                if (2.6f - t < 0.5f) a = Mathf.Min(a, (2.6f - t) / 0.5f);
                _titleCard.color = new Color(1f, 0.9f, 0.6f, a);
                yield return null;
            }
            yield return Line(sub, 2.6f);
            yield return End();
        }

        // ---------------------------------------------------------------- routines
        IEnumerator Begin()
        {
            Active = true;
            _canvas.gameObject.SetActive(true);
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.unscaledDeltaTime;
                float h = Mathf.Lerp(0f, 110f, t / 0.35f);
                _barTop.sizeDelta = new Vector2(0f, h);
                _barBottom.sizeDelta = new Vector2(0f, h);
                yield return null;
            }
        }

        IEnumerator End()
        {
            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.unscaledDeltaTime;
                float h = Mathf.Lerp(110f, 0f, t / 0.3f);
                _barTop.sizeDelta = new Vector2(0f, h);
                _barBottom.sizeDelta = new Vector2(0f, h);
                yield return null;
            }
            _subtitle.color = new Color(_subtitle.color.r, _subtitle.color.g, _subtitle.color.b, 0f);
            _titleCard.color = new Color(_titleCard.color.r, _titleCard.color.g, _titleCard.color.b, 0f);
            _canvas.gameObject.SetActive(false);
            Active = false;
        }

        IEnumerator Line(string text, float dur)
        {
            _subtitle.text = text;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Min(1f, t / 0.4f);
                if (dur - t < 0.4f) a = Mathf.Min(a, (dur - t) / 0.4f);
                _subtitle.color = new Color(1f, 0.97f, 0.9f, a);
                yield return null;
            }
        }

        IEnumerator IntroRoutine()
        {
            yield return Begin();
            var cam = Camera.main;
            var tpc = cam != null ? cam.GetComponent<ThirdPersonCamera>() : null;
            var player = Object.FindAnyObjectByType<PlayerController>();
            if (tpc != null) tpc.enabled = false;
            if (cam != null && player != null)
            {
                Vector3 basePos = player.transform.position;
                Vector3 from = basePos + new Vector3(0f, 26f, -34f);
                Vector3 to = basePos + new Vector3(0f, 2.2f, -5.2f);
                float t = 0f;
                StartCoroutine(Line("노래가 사라진 지 칠십 년 —", 2.6f));
                while (t < 5.2f)
                {
                    t += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, t / 5.2f);
                    cam.transform.position = Vector3.Lerp(from, to, k);
                    cam.transform.LookAt(basePos + Vector3.up * 1.2f);
                    if (t > 2.7f && t < 2.75f) StartCoroutine(Line("소리의 기억을 새긴 조율사가, 녹야에서 눈을 떴다.", 2.4f));
                    yield return null;
                }
            }
            if (tpc != null) tpc.enabled = true;
            yield return End();
        }

        IEnumerator TowerRoutine(Vector3 towerPos, string towerName)
        {
            yield return Begin();
            var cam = Camera.main;
            var tpc = cam != null ? cam.GetComponent<ThirdPersonCamera>() : null;
            if (tpc != null) tpc.enabled = false;
            if (cam != null)
            {
                Vector3 from = towerPos + new Vector3(6f, 3f, -10f);
                Vector3 to = towerPos + new Vector3(-4f, 9f, -14f);
                float t = 0f;
                StartCoroutine(Line(towerName + "에 소리가 돌아온다…", 2.6f));
                while (t < 3f)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, t / 3f);
                    cam.transform.position = Vector3.Lerp(from, to, k);
                    cam.transform.LookAt(towerPos + Vector3.up * 7f);
                    yield return null;
                }
            }
            if (tpc != null) tpc.enabled = true;
            yield return End();
        }

        IEnumerator BossRoutine(Transform boss)
        {
            yield return Begin();
            var cam = Camera.main;
            var tpc = cam != null ? cam.GetComponent<ThirdPersonCamera>() : null;
            if (tpc != null) tpc.enabled = false;
            if (cam != null && boss != null)
            {
                Vector3 c = boss.position;
                float t = 0f;
                _titleCard.text = "무관의 그림자";
                AudioMan.I.Play2D(Sfx.Ult(), 0.6f, 0.6f);
                while (t < 3.4f)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, t / 3.4f);
                    Vector3 from = c + new Vector3(0f, 12f, -16f);
                    Vector3 to = c + new Vector3(2.5f, 2.2f, -7f);
                    cam.transform.position = Vector3.Lerp(from, to, k);
                    cam.transform.LookAt(c + Vector3.up * (2.2f - k * 0.4f));
                    float a = t < 0.6f ? t / 0.6f : (t > 2.8f ? Mathf.Max(0f, (3.4f - t) / 0.6f) : 1f);
                    _titleCard.color = new Color(1f, 0.9f, 0.55f, a);
                    yield return null;
                }
                _titleCard.color = new Color(1f, 0.9f, 0.55f, 0f);
            }
            if (tpc != null) tpc.enabled = true;
            yield return End();
        }
    }
}
