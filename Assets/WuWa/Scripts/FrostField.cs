using UnityEngine;

namespace WuWa
{
    /// Yuki's E — lingering frost prison that slows enemies inside.
    public class FrostField : MonoBehaviour
    {
        public float radius = 4.5f;
        public float duration = 5f;
        public float slowMul = 0.6f;     // 40% slow

        float _age;
        float _tick;
        Transform _ring;
        Material _ringMat;

        public static void Spawn(Vector3 pos, float radius, float duration)
        {
            var go = new GameObject("FrostField");
            go.transform.position = pos;
            var f = go.AddComponent<FrostField>();
            f.radius = radius;
            f.duration = duration;
        }

        void Start()
        {
            Color ice = new Color(0.55f, 0.88f, 1f);
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(transform, false);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localPosition = Vector3.up * 0.12f;
            ring.transform.localScale = Vector3.one * radius * 2.05f;
            var mr = ring.GetComponent<MeshRenderer>();
            _ringMat = VFXLibrary.MakeAdditive(VFXLibrary.MakeRing());
            _ringMat.SetColor("_BaseColor", new Color(ice.r, ice.g, ice.b, 0.55f));
            mr.material = _ringMat;
            _ring = ring.transform;

            var mist = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(mist.GetComponent<Collider>());
            mist.transform.SetParent(transform, false);
            mist.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            mist.transform.localPosition = Vector3.up * 0.08f;
            mist.transform.localScale = Vector3.one * radius * 1.7f;
            var mm = mist.GetComponent<MeshRenderer>();
            var mistMat = VFXLibrary.MakeAdditive(VFXLibrary.MakeSoftDot());
            mistMat.SetColor("_BaseColor", new Color(ice.r, ice.g, ice.b, 0.16f));
            mm.material = mistMat;
        }

        void Update()
        {
            _age += Time.deltaTime;
            if (_age >= duration) { Destroy(gameObject); return; }

            if (_ring != null)
            {
                _ring.Rotate(0f, 0f, 24f * Time.deltaTime);
                float fade = Mathf.Clamp01((duration - _age) / 0.8f);
                var c = _ringMat.GetColor("_BaseColor"); c.a = 0.55f * fade; _ringMat.SetColor("_BaseColor", c);
            }

            _tick -= Time.deltaTime;
            if (_tick <= 0f)
            {
                _tick = 0.3f;
                var hits = Physics.OverlapSphere(transform.position + Vector3.up * 0.6f, radius, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
                var counted = new System.Collections.Generic.HashSet<Health>();
                foreach (var col in hits)
                {
                    var h = col.GetComponentInParent<Health>();
                    if (h == null || !h.IsAlive || counted.Contains(h)) continue;
                    counted.Add(h);
                    EnemyStatus.Of(h).ApplySlow(slowMul, 0.5f);
                }
            }
        }
    }
}
