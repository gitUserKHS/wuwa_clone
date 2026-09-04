using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// Resonance waystone: walk close to attune it, then warp to it from the
    /// map. Towers double as warp points once liberated; waystones cover the
    /// regions without one (village, lake, bloom hills, ruins).
    public class Waystone : MonoBehaviour
    {
        public int stoneId;
        public string stoneName = "공명 표석";

        public bool Discovered { get; private set; }

        public static readonly List<Waystone> All = new List<Waystone>();

        MeshRenderer _gem;
        Light _light;
        Transform _ring;
        Transform _player;
        float _pulse;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Start()
        {
            var p = Object.FindAnyObjectByType<PlayerController>();
            if (p != null) _player = p.transform;
            var gem = transform.Find("gem");
            if (gem != null) _gem = gem.GetComponent<MeshRenderer>();
            if (gem != null) _light = gem.GetComponent<Light>();
            _ring = transform.Find("ring");
            ApplyVisual();
        }

        void Update()
        {
            _pulse += Time.deltaTime * (Discovered ? 2.0f : 0.6f);
            if (_ring != null && Discovered)
                _ring.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
            if (_light != null)
                _light.intensity = Discovered ? 1.8f + Mathf.Sin(_pulse) * 0.5f : 0.15f;

            if (Discovered || _player == null) return;
            if (WuWaUtil.Flat(_player.position - transform.position).magnitude < 9f)
                Discover();
        }

        public void Discover()
        {
            if (Discovered) return;
            Discovered = true;
            ApplyVisual();
            VFXLibrary.SpawnNova(transform.position, new Color(0.55f, 0.95f, 1f), 4.5f);
            AudioMan.I.Play2D(Sfx.Absorb(), 0.6f, 1.3f);
            HUDController.Toast("공명 표석 조율 — " + stoneName + "  (M 지도에서 워프 가능)");
            Inventory.RefillFlask("표석 조율");
        }

        /// Save-game restore: attune silently.
        public void RestoreDiscovered()
        {
            if (Discovered) return;
            Discovered = true;
            ApplyVisual();
        }

        void ApplyVisual()
        {
            if (_gem != null)
                _gem.material.SetColor("_BaseColor",
                    (Discovered ? new Color(0.55f, 0.95f, 1f) : new Color(0.35f, 0.42f, 0.48f)) * (Discovered ? 2.2f : 0.9f));
        }

        /// Code-built waystone (called by the editor scene builder).
        public static Waystone Build(Vector3 basePos, int id, string name)
        {
            var root = new GameObject("Waystone_" + id);
            root.transform.position = basePos;
            var ws = root.AddComponent<Waystone>();
            ws.stoneId = id;
            ws.stoneName = name;

            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "pillar";
            pillar.transform.SetParent(root.transform, false);
            pillar.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            pillar.transform.localScale = new Vector3(0.55f, 0.8f, 0.55f);
            var pm = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            pm.SetColor("_BaseColor", new Color(0.45f, 0.47f, 0.5f));
            pillar.GetComponent<MeshRenderer>().sharedMaterial = pm;

            var gem = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gem.name = "gem";
            Object.DestroyImmediate(gem.GetComponent<Collider>());
            gem.transform.SetParent(root.transform, false);
            gem.transform.localPosition = new Vector3(0f, 1.95f, 0f);
            gem.transform.localScale = Vector3.one * 0.42f;
            var gm = VFXLibrary.MakeAdditive(VFXLibrary.MakeSoftDot());
            gm.SetColor("_BaseColor", new Color(0.35f, 0.42f, 0.48f));
            gem.GetComponent<MeshRenderer>().sharedMaterial = gm;
            var l = gem.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.55f, 0.95f, 1f);
            l.range = 8f;
            l.intensity = 0.15f;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "ring";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = Vector3.one * 3.4f;
            var rm = VFXLibrary.MakeAdditive(VFXLibrary.MakeRing());
            rm.SetColor("_BaseColor", new Color(0.55f, 0.95f, 1f) * 0.8f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = rm;

            return ws;
        }
    }
}
