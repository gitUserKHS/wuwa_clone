using System.Collections;
using UnityEngine;

namespace WuWa
{
    /// Respawns a mob prefab at its post a while after it dies (bosses excluded).
    /// Open-world scaling: posts only keep an enemy alive while the player is
    /// nearby — far-away mobs despawn and come back instantly on approach.
    public class EnemySpawner : MonoBehaviour
    {
        public GameObject enemyPrefab;
        public float respawnDelay = 25f;
        public bool bossPost;
        public float statMul = 1f;       // region difficulty scaling (forest > plains)
        public float activateRange = 95f;
        public float sleepRange = 150f;

        GameObject _current;
        bool _waiting;
        bool _pendingSpawn = true;       // never spawned yet, or despawned by distance
        Transform _player;

        void Start()
        {
            var p = PlayerController.Instance != null ? PlayerController.Instance : Object.FindAnyObjectByType<PlayerController>();
            if (p != null) _player = p.transform;
            if (bossPost) SpawnNow();    // bosses always hold their arena
        }

        void Update()
        {
            if (bossPost) return;
            if (_player == null)
            {
                var p = PlayerController.Instance;
                if (p == null) return;                       // no player yet: hold the post
                _player = p.transform;
            }
            float dist = WuWaUtil.Flat(_player.position - transform.position).magnitude;

            if (_current != null)
            {
                if (dist > sleepRange)
                {
                    Destroy(_current);       // silent despawn — no kill credit, no timer
                    _current = null;
                    _pendingSpawn = true;
                }
                return;
            }

            if (dist > activateRange) return;
            if (_pendingSpawn) { SpawnNow(); return; }
            if (!_waiting) StartCoroutine(RespawnRoutine());   // died → timed respawn
        }

        IEnumerator RespawnRoutine()
        {
            _waiting = true;
            yield return new WaitForSeconds(respawnDelay);
            SpawnNow();
            _waiting = false;
        }

        void SpawnNow()
        {
            if (enemyPrefab == null) return;
            _pendingSpawn = false;
            Vector3 pos = transform.position;
            pos.y = WuWaUtil.GroundHeight(pos) + 0.1f;
            _current = Instantiate(enemyPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            _current.SetActive(true);
            if (Mathf.Abs(statMul - 1f) > 0.001f)
            {
                var h = _current.GetComponent<Health>();
                if (h != null) { h.maxHp *= statMul; h.hp = h.maxHp; }
                var ai = _current.GetComponent<EnemyAI>();
                if (ai != null) ai.attackDamage *= statMul;
            }
        }
    }
}
