using UnityEngine;

namespace WuWa
{
    /// Shadow bolt fired by ranged enemies. Fully code-built, dodgeable.
    public class EnemyProjectile : MonoBehaviour
    {
        public float speed = 15f;
        public float damage = 300f;
        public float life = 4.5f;
        public float hitRadius = 0.65f;

        Vector3 _dir;
        float _age;
        static readonly Collider[] _buf = new Collider[4];

        public static void Fire(Vector3 from, Vector3 targetPos, float damage)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "ShadowBolt";
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.42f;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = VFXLibrary.SoftDotAdditive;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(0.85f, 0.4f, 1f) * 2.4f);
            mr.SetPropertyBlock(mpb);

            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.8f, 0.4f, 1f);
            l.range = 5f;
            l.intensity = 2.6f;

            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.28f;
            tr.startWidth = 0.32f;
            tr.endWidth = 0.02f;
            tr.sharedMaterial = VFXLibrary.StreakAdditive;
            tr.startColor = new Color(0.85f, 0.4f, 1f, 0.9f);
            tr.endColor = new Color(0.85f, 0.4f, 1f, 0f);

            var p = go.AddComponent<EnemyProjectile>();
            p.damage = damage;
            p._dir = (targetPos - from).normalized;
            go.transform.rotation = Quaternion.LookRotation(p._dir);
            AudioMan.I.Play(Sfx.Skill(), from, 0.45f, 1.5f);
        }

        void Update()
        {
            _age += Time.deltaTime;
            if (_age > life) { Pop(); return; }

            transform.position += _dir * speed * Time.deltaTime;

            // player hit
            int n = Physics.OverlapSphereNonAlloc(transform.position, hitRadius, _buf, Layers.PlayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var pc = _buf[i].GetComponentInParent<PlayerController>();
                if (pc != null)
                {
                    pc.TakeDamage(new DamageInfo
                    {
                        amount = damage * Random.Range(0.92f, 1.08f),
                        crit = false,
                        element = Element.Havoc,
                        sourcePos = transform.position - _dir * 2f,
                        knockback = 3.5f,
                        staggerPower = 0f,
                        source = gameObject
                    });
                    Pop();
                    return;
                }
            }

            // ground / obstacle hit
            if (Physics.CheckSphere(transform.position, 0.25f,
                    ~(Layers.PlayerMask | Layers.EnemyMask | (1 << Layers.Pickup)), QueryTriggerInteraction.Ignore))
                Pop();
        }

        void Pop()
        {
            VFXLibrary.SpawnHitSpark(transform.position, new Color(0.85f, 0.4f, 1f), 0.8f);
            Destroy(gameObject);
        }
    }
}
