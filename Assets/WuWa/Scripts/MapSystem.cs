using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Map data hub: the baked world texture, dynamic POIs (rifts), the tracked
    /// target, warping, and the minimap widget. The full map is MapScreen (router).
    public class MapSystem : MonoBehaviour
    {
        public static MapSystem I { get; private set; }
        public static bool MinimapEnabled = true;
        public static float MinimapRadius = 120f;     // metres shown around the player (settings)
        public static int MinimapMode = 1;            // 0 north-up, 1 rotate with camera (settings)

        public Texture2D worldMap;                    // assigned by the editor bake (Assets/WuWa/Art/World/WorldMap.png)
        public float worldHalf = 860f;

        Canvas _canvas;
        MinimapWidget _mini;
        bool _warping;

        // ---------------------------------------------------------------- dynamic POIs (handles, not indices)
        public class DynamicPoi { public int id; public Vector3 pos; public string label; public MapCategory cat; }
        public static readonly List<DynamicPoi> Dynamic = new List<DynamicPoi>();
        static int _nextPoi = 1;

        public static int AddDynamicPoi(Vector3 pos, string label, MapCategory cat)
        {
            var p = new DynamicPoi { id = _nextPoi++, pos = pos, label = label, cat = cat };
            Dynamic.Add(p);
            return p.id;
        }

        public static void RemoveDynamicPoi(int id) { Dynamic.RemoveAll(p => p.id == id); }
        public static void ClearDynamic() { Dynamic.Clear(); }

        // ---------------------------------------------------------------- tracked target (map "추적")
        public static bool HasTracked { get; private set; }
        public static Vector3 TrackedPos { get; private set; }
        public static string TrackedName { get; private set; }

        public static void SetTracked(Vector3 pos, string name)
        {
            HasTracked = true; TrackedPos = pos; TrackedName = name;
            HUDController.Toast("추적 — " + name);
        }

        public static void ClearTracked() { HasTracked = false; TrackedName = null; }

        /// Tracked target if set, otherwise the quest target.
        public static bool Objective(out Vector3 pos, out string name)
        {
            if (HasTracked) { pos = TrackedPos; name = TrackedName; return true; }
            string obj;
            if (QuestSystem.I != null && QuestSystem.I.TrackedTarget(out pos, out name, out obj)) return true;
            pos = Vector3.zero; name = null; return false;
        }

        // ---------------------------------------------------------------- helpers
        public static float WorldSize { get { return WorldRegions.WorldHalf * 2f; } }
        public static Vector2 World01(Vector3 world)
        {
            return new Vector2((world.x + WorldRegions.WorldHalf) / WorldSize, (world.z + WorldRegions.WorldHalf) / WorldSize);
        }

        public static bool WarpBlocked(out string reason)
        {
            var pc = PlayerController.Instance;
            if (Cutscene.Active) { reason = "컷신 중에는 워프할 수 없습니다"; return true; }
            if (ArenaTrial.Running) { reason = "시련 중에는 워프할 수 없습니다"; return true; }
            if (pc != null && pc.InCombat) { reason = "전투 중에는 워프할 수 없습니다"; return true; }
            reason = null; return false;
        }

        void Awake()
        {
            I = this;
            _canvas = UIKit.MakeCanvas("MapCanvas", transform, 60, false);
            _mini = _canvas.gameObject.AddComponent<MinimapWidget>();
            _mini.Build(_canvas.transform, worldMap);
        }

        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            MapDiscovery.Tick();
            bool showMini = MinimapEnabled && !ScreenRouter.IsOpen && !GameDirector.MenuOpen && !Cutscene.Active && worldMap != null && !HUDController.HudHidden;
            _mini.SetVisible(showMini);
        }

        public void Warp(Vector3 dest, string name)
        {
            if (_warping) return;
            string why;
            if (WarpBlocked(out why)) { HUDController.Toast(why); return; }
            StartCoroutine(WarpRoutine(dest, name));
        }

        System.Collections.IEnumerator WarpRoutine(Vector3 dest, string name)
        {
            _warping = true;
            ScreenRouter.CloseAll();
            HUDController.FadeScreen(1f, 0.22f);
            AudioMan.I.Play2D(Sfx.Swap(), 0.6f, 0.8f);
            yield return new WaitForSecondsRealtime(0.3f);

            var pc = PlayerController.Instance;
            if (pc != null)
            {
                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                Vector3 p = dest + new Vector3(2.2f, 0f, 2.2f);
                p.y = WorldRegions.HeightAt(p.x, p.z) + 1.6f;
                pc.transform.position = p;
                if (cc != null) cc.enabled = true;
            }
            VFXLibrary.Flash(dest + Vector3.up * 1.2f, new Color(0.55f, 0.95f, 1f), 2.5f, 0.35f);
            MapDiscovery.RevealCircle(dest, 140f);
            yield return new WaitForSecondsRealtime(0.15f);
            HUDController.FadeScreen(0f, 0.45f);
            HUDController.Toast("워프 — " + name);
            _warping = false;
        }
    }
}
