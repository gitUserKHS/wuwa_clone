using UnityEngine;

namespace WuWa
{
    /// Echo drop absorbed by the player: heals the team and grants energy.
    public class EchoOrb : MonoBehaviour
    {
        public float healFrac = 0.12f;
        public float energyGain = 15f;
        public float attractRange = 6f;
        public float absorbRange = 1.1f;
        public int echoId = -1;          // >= 0: absorbing also grants this echo item

        Transform _player;
        float _bob;
        Light _light;

        public static void SpawnAt(Vector3 pos, int count, int echoId = -1)
        {
            for (int i = 0; i < count; i++)
            {
                bool carrier = echoId >= 0 && i == 0;    // only the first orb carries the echo
                Color col = carrier && EchoDB.Get(echoId) != null
                    ? Color.Lerp(EchoDB.Get(echoId).Tint, Color.white, 0.25f)
                    : new Color(0.8f, 0.55f, 1f);

                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "EchoOrb";
                go.layer = Layers.Pickup;
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.position = pos + new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));
                go.transform.localScale = Vector3.one * (carrier ? 0.5f : 0.35f);

                var mr = go.GetComponent<MeshRenderer>();
                mr.material = VFXLibrary.MakeAdditive(VFXLibrary.MakeSoftDot());
                mr.material.SetColor("_BaseColor", col * (carrier ? 2.3f : 1.8f));

                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = col;
                l.intensity = carrier ? 3.2f : 2.4f;
                l.range = carrier ? 5.5f : 4f;

                var orb = go.AddComponent<EchoOrb>();
                if (carrier) orb.echoId = echoId;
            }
        }

        void Start()
        {
            var p = Object.FindAnyObjectByType<PlayerController>();
            if (p != null) _player = p.transform;
            _light = GetComponent<Light>();
            _bob = Random.value * 10f;
        }

        void Update()
        {
            _bob += Time.deltaTime * 3f;
            transform.position += Vector3.up * Mathf.Sin(_bob) * 0.003f;
            transform.Rotate(0f, 80f * Time.deltaTime, 0f);

            if (_player == null) return;
            Vector3 to = _player.position + Vector3.up * 1f - transform.position;
            float dist = to.magnitude;
            if (dist < attractRange)
                transform.position += to.normalized * Mathf.Lerp(9f, 2f, dist / attractRange) * Time.deltaTime;

            if (dist < absorbRange)
            {
                var team = _player.GetComponent<TeamManager>();
                if (team != null) team.HealAll(healFrac, energyGain);
                AudioMan.I.Play2D(Sfx.Absorb(), 0.6f);
                VFXLibrary.Flash(transform.position, new Color(0.8f, 0.55f, 1f), 1.2f, 0.2f);
                if (echoId >= 0 && EchoSystem.I != null) EchoSystem.I.Add(echoId);
                else HUDController.Toast("에코 흡수 +HP");
                Destroy(gameObject);
            }
        }
    }
}
