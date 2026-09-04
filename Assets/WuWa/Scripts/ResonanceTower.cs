using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WuWa
{
    /// Resonance tower: approach and press F to awaken it — light returns,
    /// nearby spawns weaken, respawn point moves here (GDD ch.2/3).
    public class ResonanceTower : MonoBehaviour, IInteractable
    {
        public int towerId;
        public string towerName = "공명탑";
        public float interactRange = 4.5f;
        public float weakenRadius = 60f;

        public bool Activated { get; private set; }
        public static int ActiveCount;               // melody layers unlock with this

        Transform _player;
        Light _crownLight;
        MeshRenderer _crownRenderer;
        float _pulse;

        void Start()
        {
            var p = Object.FindAnyObjectByType<PlayerController>();
            if (p != null) _player = p.transform;
            var crown = transform.Find("crown");
            if (crown != null)
            {
                _crownRenderer = crown.GetComponent<MeshRenderer>();
                _crownLight = crown.GetComponent<Light>();
            }
        }

        void Update()
        {
            if (_crownRenderer != null)
            {
                _pulse += Time.deltaTime * (Activated ? 2.2f : 0.8f);
                float glow = Activated ? 1.6f + Mathf.Sin(_pulse) * 0.5f : 0.12f + Mathf.Sin(_pulse) * 0.05f;
                Color c = Activated ? new Color(1f, 0.85f, 0.4f) : new Color(0.4f, 0.5f, 0.6f);
                _crownRenderer.material.SetColor("_EmissionColor", c * glow);
                if (_crownLight != null) _crownLight.intensity = Activated ? 3.2f + Mathf.Sin(_pulse) : 0.4f;
            }

        }

        void OnEnable() { InteractionManager.Register(this); }
        void OnDisable() { InteractionManager.Unregister(this); }

        // IInteractable (prompt + key are routed by InteractionManager)
        public Vector3 InteractPosition { get { return transform.position; } }
        public float InteractRange { get { return interactRange; } }
        public int InteractPriority { get { return 4; } }
        public string InteractLabel { get { return "공명탑 해방"; } }
        public bool CanInteract { get { return !Activated; } }
        public void Interact() { Activate(); }

        public void Activate()
        {
            if (Activated) return;
            Activated = true;
            ActiveCount++;
            HUDController.SetInteractPrompt(null);
            Inventory.RefillFlask("공명탑 해방");
            StartCoroutine(ActivateRoutine());
            if (SaveSystem.I != null) SaveSystem.I.AutoSave("공명탑 해방");
        }

        /// Save-game restore: light the tower without ceremony.
        public void RestoreActivated()
        {
            if (Activated) return;
            Activated = true;
            ActiveCount++;
            foreach (var sp in Object.FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
            {
                if (sp.bossPost) continue;
                if (WuWaUtil.Flat(sp.transform.position - transform.position).magnitude < weakenRadius)
                    sp.respawnDelay *= 2.5f;
            }
        }

        IEnumerator ActivateRoutine()
        {
            // light pillar + burst
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(pillar.GetComponent<Collider>());
            pillar.transform.position = transform.position + Vector3.up * 20f;
            pillar.transform.localScale = new Vector3(3.2f, 44f, 1f);
            var mr = pillar.GetComponent<MeshRenderer>();
            mr.material = VFXLibrary.MakeAdditive(VFXLibrary.MakeStreak());
            mr.material.SetColor("_BaseColor", new Color(1f, 0.9f, 0.5f) * 1.8f);
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 look = pillar.transform.position - cam.transform.position; look.y = 0f;
                pillar.transform.rotation = Quaternion.LookRotation(look) * Quaternion.Euler(0f, 0f, 90f);
            }
            VFXLibrary.SpawnNova(transform.position, new Color(1f, 0.9f, 0.5f), 8f, true);
            AudioMan.I.Play2D(Sfx.Ult(), 0.55f, 1.2f);
            if (MusicDirector.I != null) MusicDirector.I.PlayVictory();
            CameraShaker.Add(0.4f);

            // weaken nearby spawners + move respawn here
            foreach (var sp in Object.FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
            {
                if (sp.bossPost) continue;
                if (WuWaUtil.Flat(sp.transform.position - transform.position).magnitude < weakenRadius)
                    sp.respawnDelay *= 2.5f;
            }
            if (GameDirector.I != null) GameDirector.I.respawnPoint = transform.position + Vector3.forward * 3f;

            HUDController.Toast(towerName + " 해방 — 이 지역의 그림자가 약해집니다");
            if (Cutscene.I != null) Cutscene.I.PlayTowerActivation(transform.position, towerName);
            if (QuestSystem.I != null) QuestSystem.I.Notify(QuestEvent.Tower, towerId);

            float t = 0f;
            while (t < 5f)
            {
                t += Time.deltaTime;
                var c = mr.material.GetColor("_BaseColor");
                c.a = Mathf.Lerp(1f, 0.25f, t / 5f);
                mr.material.SetColor("_BaseColor", c);
                yield return null;
            }
        }

        /// Code-built tower structure (editor scene builder calls this shape).
        public static ResonanceTower Build(Vector3 basePos, int id, string name)
        {
            var root = new GameObject("Tower_" + id);
            root.transform.position = basePos;
            var tower = root.AddComponent<ResonanceTower>();
            tower.towerId = id;
            tower.towerName = name;

            var stoneMat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            stoneMat.SetColor("_BaseColor", new Color(0.45f, 0.47f, 0.52f));

            System.Func<Vector3, Vector3, Material, GameObject> seg = (pos, scale, mat) =>
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                p.transform.SetParent(root.transform, false);
                p.transform.localPosition = pos;
                p.transform.localScale = scale;
                p.GetComponent<MeshRenderer>().sharedMaterial = mat;
                return p;
            };
            seg(new Vector3(0f, 1f, 0f), new Vector3(3.4f, 1f, 3.4f), stoneMat);
            seg(new Vector3(0f, 4.5f, 0f), new Vector3(2.2f, 2.8f, 2.2f), stoneMat);
            seg(new Vector3(0f, 8.6f, 0f), new Vector3(1.5f, 1.6f, 1.5f), stoneMat);

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "crown";
            Object.Destroy(crown.GetComponent<Collider>());
            crown.transform.SetParent(root.transform, false);
            crown.transform.localPosition = new Vector3(0f, 11.2f, 0f);
            crown.transform.localScale = Vector3.one * 1.6f;
            var cm = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            cm.SetColor("_BaseColor", new Color(0.25f, 0.28f, 0.32f));
            cm.EnableKeyword("_EMISSION");
            cm.SetColor("_EmissionColor", Color.black);
            crown.GetComponent<MeshRenderer>().material = cm;
            var l = crown.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.85f, 0.4f);
            l.range = 18f;
            l.intensity = 0.4f;
            return tower;
        }
    }
}
