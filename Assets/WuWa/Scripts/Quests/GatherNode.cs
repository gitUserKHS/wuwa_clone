using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// 채집 군락: daily gather spots converted from the old echo caches. The first
    /// gather still yields the cache's echo; every in-game day it gives region
    /// crystals, a residue and sometimes 노래풀 구이.
    public class GatherNode : MonoBehaviour, IInteractable
    {
        public int nodeId;
        public int region;
        public int echoId = -1;
        public int lastDay = -999;
        public bool everGathered;

        public static readonly List<GatherNode> All = new List<GatherNode>();
        static bool _booted;
        public static void ResetBoot() { _booted = false; All.Clear(); }
        Light _light;
        readonly List<Renderer> _rends = new List<Renderer>();
        float _pulse;

        void OnEnable() { All.Add(this); InteractionManager.Register(this); }
        void OnDisable() { All.Remove(this); InteractionManager.Unregister(this); }

        public bool Available { get { return lastDay != DayNightCycle.DayIndex; } }

        // IInteractable
        public Vector3 InteractPosition { get { return transform.position; } }
        public float InteractRange { get { return 3.4f; } }
        public int InteractPriority { get { return 2; } }
        public string InteractLabel { get { return "군락 채집" + (everGathered ? "" : " (첫 채집: 에코)"); } }
        public bool CanInteract { get { return Available; } }

        public void Interact()
        {
            if (!Available) return;
            lastDay = DayNightCycle.DayIndex;
            bool first = !everGathered;
            everGathered = true;
            GameFlags.Set("node_" + nodeId);
            Inventory.Add(ItemDB.CrystalFor(DropTables.ElementOfRegion(region)), 2);
            Inventory.Add(ItemDB.Residue0, 1);
            if (Random.value < 0.3f) Inventory.Add(ItemDB.FoodAtk, 1);
            if (first && echoId >= 0 && EchoSystem.I != null) EchoSystem.I.Add(echoId);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.7f, 1.0f);
            VFXLibrary.SpawnNova(transform.position + Vector3.up * 0.3f, new Color(0.6f, 1f, 0.7f), 2.4f);
            HUDController.Toast("군락 채집 — 내일 다시 자랍니다");
            HUDController.SetInteractPrompt(null);
            Tutorial.Trigger("gather");
            ApplyVisual();
        }

        void Update()
        {
            _pulse += Time.deltaTime * 2f;
            if (_light != null) _light.intensity = Available ? 1.6f + Mathf.Sin(_pulse) * 0.5f : 0.25f;
        }

        void ApplyVisual()
        {
            bool on = Available;
            foreach (var r in _rends)
                if (r != null) r.material.SetColor("_BaseColor", (on ? new Color(0.55f, 1f, 0.6f) : new Color(0.3f, 0.45f, 0.32f)) * (on ? 1.8f : 0.8f));
            if (_light != null) _light.enabled = true;
        }

        /// Converts every scene "EchoCache" orb into a gather node (called once at boot).
        public static void Bootstrap()
        {
            if (_booted) return;
            _booted = true;
            var root = GameObject.Find("WOrbs");
            if (root == null) return;
            var caches = new List<EchoOrb>(root.GetComponentsInChildren<EchoOrb>());
            int id = 0;
            foreach (var orb in caches)
            {
                Vector3 pos = orb.transform.position;
                pos.y = WorldRegions.HeightAt(pos.x, pos.z);
                var go = new GameObject("GatherNode_" + id);
                go.transform.SetParent(root.transform, false);
                go.transform.position = pos;
                var node = go.AddComponent<GatherNode>();
                node.nodeId = id++;
                node.region = WorldRegions.RegionAt(pos.x, pos.z);
                node.echoId = orb.echoId;
                node.BuildVisual();
                Destroy(orb.gameObject);
            }
        }

        void BuildVisual()
        {
            var mat = VFXLibrary.MakeAdditive(VFXLibrary.MakeSoftDot());
            for (int i = 0; i < 5; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "bud";
                s.transform.SetParent(transform, false);
                Destroy(s.GetComponent<Collider>());
                float a = i / 5f * Mathf.PI * 2f;
                s.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.55f, 0.35f + (i % 2) * 0.25f, Mathf.Sin(a) * 0.55f);
                s.transform.localScale = Vector3.one * (0.22f + (i % 2) * 0.1f);
                var mr = s.GetComponent<MeshRenderer>();
                mr.material = new Material(mat);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _rends.Add(mr);
            }
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "stem";
            stem.transform.SetParent(transform, false);
            Destroy(stem.GetComponent<Collider>());
            stem.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            stem.transform.localScale = new Vector3(0.12f, 0.25f, 0.12f);
            stem.GetComponent<MeshRenderer>().material.color = new Color(0.25f, 0.4f, 0.2f);
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point; _light.color = new Color(0.6f, 1f, 0.7f); _light.range = 5f; _light.intensity = 1.6f;
            ApplyVisual();
        }

        // ---------------------------------------------------------------- save
        public static void Export(out int[] days)
        {
            int n = 0; foreach (var g in All) if (g != null) n = Mathf.Max(n, g.nodeId + 1);
            days = new int[n];
            for (int i = 0; i < n; i++) days[i] = -999;
            foreach (var g in All) if (g != null) days[g.nodeId] = g.lastDay;
        }

        public static void Import(int[] days)
        {
            foreach (var g in All)
            {
                if (g == null) continue;
                g.lastDay = days != null && g.nodeId < days.Length ? days[g.nodeId] : -999;
                g.everGathered = GameFlags.Has("node_" + g.nodeId);
                g.ApplyVisual();
            }
        }
    }
}
