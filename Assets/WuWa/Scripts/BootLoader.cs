using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WuWa
{
    /// Boot scene: shows the title + a progress bar instantly, then streams the
    /// world scene in asynchronously — no frozen black window on launch.
    public class BootLoader : MonoBehaviour
    {
        public Image progressFill;
        public Text progressText;
        public string targetScene = "WuWaField";

        AsyncOperation _op;
        float _shown;

        void Start()
        {
            Application.backgroundLoadingPriority = ThreadPriority.High;
            _op = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
            _op.allowSceneActivation = true;
        }

        void Update()
        {
            if (_op == null) return;
            float target = Mathf.Clamp01(_op.progress / 0.9f);
            _shown = Mathf.MoveTowards(_shown, target, Time.unscaledDeltaTime * 0.9f);
            if (progressFill != null) progressFill.fillAmount = _shown;
            if (progressText != null)
                progressText.text = _shown < 0.999f
                    ? "세계의 소리를 조율하는 중…  " + Mathf.RoundToInt(_shown * 100f) + "%"
                    : "곧 시작됩니다";
        }
    }
}
