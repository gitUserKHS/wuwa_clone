using UnityEngine;

namespace WuWa
{
    /// Scene-side table of enemy prefabs so runtime content (arena waves, rift
    /// events, boss adds) can spawn shadows without asset lookups. Filled by the
    /// editor quality pass; indexed by EnemyKind.
    public class EnemyRegistry : MonoBehaviour
    {
        public static EnemyRegistry I { get; private set; }

        public GameObject[] prefabs = new GameObject[5];     // Melee, Fast, Ranged, Tank, Boss

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public static GameObject PrefabFor(EnemyKind kind)
        {
            if (I == null || I.prefabs == null) return null;
            int i = (int)kind;
            return i >= 0 && i < I.prefabs.Length ? I.prefabs[i] : null;
        }

        /// Spawns a shadow at ground level. statMul scales HP + damage; elites are
        /// bigger, tougher and carry a crimson standing glow.
        public static EnemyAI Spawn(EnemyKind kind, Vector3 pos, float statMul, bool elite = false, Transform parent = null, bool snapToGround = true)
        {
            var prefab = PrefabFor(kind);
            if (prefab == null) return null;
            if (snapToGround) pos.y = WorldRegions.HeightAt(pos.x, pos.z) + 0.15f;
            var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), parent);
            go.SetActive(true);
            var h = go.GetComponent<Health>();
            var ai = go.GetComponent<EnemyAI>();
            float mul = Mathf.Max(0.3f, statMul) * (elite ? 1.7f : 1f);
            if (h != null)
            {
                h.maxHp *= mul;
                h.hp = h.maxHp;
                if (elite) { h.maxStagger *= 1.5f; h.stagger = h.maxStagger; h.displayName = "정예 " + h.displayName; }
            }
            if (ai != null)
            {
                ai.attackDamage *= Mathf.Max(0.3f, statMul) * (elite ? 1.35f : 1f);
                if (elite)
                {
                    ai.moveSpeed *= 1.12f;
                    ai.attackCooldown *= 0.85f;
                    ai.MarkElite();
                }
            }
            if (elite) go.transform.localScale *= 1.22f;
            VFXLibrary.SpawnNova(pos + Vector3.up * 0.5f, elite ? new Color(1f, 0.3f, 0.45f) : new Color(0.6f, 0.35f, 0.9f), elite ? 3.5f : 2.2f);
            return ai;
        }
    }
}
