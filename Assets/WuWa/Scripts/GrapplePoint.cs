using System.Collections.Generic;
using UnityEngine;

namespace WuWa
{
    /// WuWa-style traversal hook: aim roughly at one and press T to zip to it.
    public class GrapplePoint : MonoBehaviour
    {
        public static readonly List<GrapplePoint> All = new List<GrapplePoint>();

        public float range = 27f;

        Transform _visual;
        Light _light;
        float _pulse;

        void OnEnable() { All.Add(this); }

        void Start()
        {
            // Build() wires these at edit time but they are not serialized — re-resolve
            if (_visual == null) _visual = transform.Find("ring");
            if (_light == null) _light = GetComponent<Light>();
        }
        void OnDisable() { All.Remove(this); }

        void Update()
        {
            _pulse += Time.deltaTime * 2.2f;
            if (_visual != null)
            {
                _visual.localScale = Vector3.one * (1f + Mathf.Sin(_pulse) * 0.12f);
                _visual.Rotate(0f, 55f * Time.deltaTime, 0f, Space.World);
            }
            if (_light != null) _light.intensity = 2.2f + Mathf.Sin(_pulse * 1.7f) * 0.7f;
        }

        /// Best candidate near the camera aim direction, or null.
        public static GrapplePoint Best(Vector3 playerPos, Camera cam)
        {
            if (cam == null) return null;
            GrapplePoint best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var g = All[i];
                if (g == null) continue;
                Vector3 to = g.transform.position - playerPos;
                float dist = to.magnitude;
                if (dist > g.range || dist < 3f) continue;
                float ang = Vector3.Angle(cam.transform.forward, g.transform.position - cam.transform.position);
                if (ang > 32f) continue;
                float score = ang * 1.2f + dist * 0.35f;
                if (score < bestScore) { bestScore = score; best = g; }
            }
            return best;
        }

        /// Code-built glowing hook visual (called by the editor scene builder or at runtime).
        public static GrapplePoint Build(Vector3 pos)
        {
            var root = new GameObject("GrapplePoint");
            root.transform.position = pos;
            var gp = root.AddComponent<GrapplePoint>();

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(orb.GetComponent<Collider>());
            orb.name = "orb";
            orb.transform.SetParent(root.transform, false);
            orb.transform.localScale = Vector3.one * 0.55f;
            var mr = orb.GetComponent<MeshRenderer>();
            mr.sharedMaterial = VFXLibrary.MakeAdditive(VFXLibrary.MakeSoftDot());
            mr.sharedMaterial.SetColor("_BaseColor", new Color(0.5f, 1f, 0.85f) * 1.35f);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.name = "ring";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = Vector3.one * 2.1f;
            var rr = ring.GetComponent<MeshRenderer>();
            rr.sharedMaterial = VFXLibrary.MakeAdditive(VFXLibrary.MakeRing());
            rr.sharedMaterial.SetColor("_BaseColor", new Color(0.5f, 1f, 0.85f) * 1.5f);

            var l = root.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.5f, 1f, 0.85f);
            l.range = 7f;
            l.intensity = 2.2f;

            gp._visual = ring.transform;
            gp._light = l;
            return gp;
        }
    }
}
