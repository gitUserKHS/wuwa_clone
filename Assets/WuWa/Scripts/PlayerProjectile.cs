using UnityEngine;

namespace WuWa
{
    /// Homing shadow arrow fired by the Shaman echo active.
    public class PlayerProjectile : MonoBehaviour
    {
        public float speed = 22f;
        public float damage = 150f;
        public float life = 3f;
        public float turnRate = 260f;    // deg/s homing
        public Element element = Element.Havoc;
        public Color color = new Color(0.85f, 0.4f, 1f);

        Transform _target;
        float _age;
        static readonly Collider[] _buf = new Collider[8];
        MemberConfig _source;

        public static void Fire(Vector3 from, Vector3 dir, Transform target, float damage, Element element, Color color, MemberConfig source)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "EchoArrow";
            go.transform.position = from;
            go.transform.rotation = Quaternion.LookRotation(dir);
            go.transform.localScale = Vector3.one * 0.3f;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = VFXLibrary.SoftDotAdditive;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color * 2.2f);
            mr.SetPropertyBlock(mpb);

            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.22f;
            tr.startWidth = 0.22f;
            tr.endWidth = 0.02f;
            tr.sharedMaterial = VFXLibrary.StreakAdditive;
            tr.startColor = new Color(color.r, color.g, color.b, 0.9f);
            tr.endColor = new Color(color.r, color.g, color.b, 0f);

            var p = go.AddComponent<PlayerProjectile>();
            p.damage = damage;
            p.element = element;
            p.color = color;
            p._target = target;
            p._source = source;
        }

        void Update()
        {
            _age += Time.deltaTime;
            if (_age > life) { Pop(false); return; }

            if (_target != null)
            {
                Vector3 want = (_target.position + Vector3.up * 1.1f - transform.position).normalized;
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(want), turnRate * Time.deltaTime);
            }
            transform.position += transform.forward * speed * Time.deltaTime;

            int hitN = Physics.OverlapSphereNonAlloc(transform.position, 0.55f, _buf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitN; i++)
            {
                var h = _buf[i].GetComponentInParent<Health>();
                if (h == null || !h.IsAlive) continue;
                bool crit = _source != null && Random.value < _source.EffCrit;
                float dmg = damage * Random.Range(0.92f, 1.08f) * (crit ? (_source != null ? _source.EffCritMul : 2f) : 1f);
                h.TakeDamage(new DamageInfo
                {
                    amount = dmg, crit = crit, element = element,
                    sourcePos = transform.position - transform.forward, knockback = 2f, staggerPower = 8f, source = gameObject
                });
                VFXLibrary.SpawnHitSpark(h.transform.position + Vector3.up, color, 0.9f);
                AudioMan.I.Play(Sfx.Hit(), h.transform.position, 0.7f);
                if (_source != null) { _source.GainEnergy(4f); _source.GainConcerto(3f); }
                Pop(true);
                return;
            }

            if (Physics.CheckSphere(transform.position, 0.2f,
                    ~(Layers.PlayerMask | Layers.EnemyMask | (1 << Layers.Pickup)), QueryTriggerInteraction.Ignore))
                Pop(false);
        }

        void Pop(bool hit)
        {
            if (!hit) VFXLibrary.SpawnHitSpark(transform.position, color, 0.5f);
            Destroy(gameObject);
        }
    }
}
