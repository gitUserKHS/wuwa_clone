using UnityEngine;

namespace WuWa
{
    /// Render scaling for the open world: small decoration stops rendering
    /// beyond ~170m, large decoration beyond ~540m (fog hides both edges).
    /// Layers are assigned by the editor world builder.
    public class PerfTuner : MonoBehaviour
    {
        public const int BigDecoLayer = 20;
        public const int SmallDecoLayer = 21;

        Camera _applied;
        public static float DistanceMul = 1f;
        static PerfTuner _inst;
        void Awake() { _inst = this; }
        public static void Reapply() { if (_inst != null) _inst._applied = null; }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || cam == _applied) return;
            _applied = cam;
            var dists = new float[32];
            dists[BigDecoLayer] = 540f * DistanceMul;
            dists[SmallDecoLayer] = 170f * DistanceMul;
            cam.layerCullDistances = dists;
        }
    }
}
