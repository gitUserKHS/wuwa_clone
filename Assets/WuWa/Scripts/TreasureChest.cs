using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WuWa
{
    /// Exploration chests (GDD ch.3): wood / silver / gold tiers scattered
    /// across the world. F to open — shards, echoes, weapons. Opened state
    /// persists through the save file.
    public class TreasureChest : MonoBehaviour, IInteractable
    {
        public int chestId;
        public int tier;                       // 0 wood, 1 silver, 2 gold

        public bool Opened { get; private set; }
        public int openedDay = -999;
        public bool Respawned { get; private set; }

        public static readonly List<TreasureChest> All = new List<TreasureChest>();

        Transform _lid;
        Light _glow;
        Transform _player;

        void OnEnable() { All.Add(this); InteractionManager.Register(this); }
        void OnDisable() { All.Remove(this); InteractionManager.Unregister(this); }

        void Start()
        {
            var p = Object.FindAnyObjectByType<PlayerController>();
            if (p != null) _player = p.transform;
            _lid = transform.Find("lidPivot");
            _glow = GetComponentInChildren<Light>();
        }

        // IInteractable (prompt + key are routed by InteractionManager)
        public Vector3 InteractPosition { get { return transform.position; } }
        public float InteractRange { get { return 3.6f; } }
        public int InteractPriority { get { return 2; } }
        public string InteractLabel { get { return TierName() + " 상자 열기"; } }
        public bool CanInteract { get { return !Opened; } }
        public void Interact() { Open(); }

        string TierName() { return tier >= 2 ? "황금" : tier == 1 ? "은빛" : "나무"; }

        public void Open()
        {
            if (Opened) return;
            Opened = true;
            openedDay = DayNightCycle.DayIndex;
            GameFlags.Set("chest_" + chestId);
            BountyBoard.NotifyChest(); ContentStats.ChestsOpened++;
            Tutorial.Trigger("chest");
            HUDController.SetInteractPrompt(null);
            StartCoroutine(OpenRoutine());
        }

        public void RestoreOpened(int day) { RestoreOpened(); openedDay = day; }

        void Update()
        {
            if (Opened && tier == 0 && openedDay > -999 && DayNightCycle.DayIndex - openedDay >= 2) Respawn();
        }

        /// Wood chests refill after two in-game days with a smaller haul.
        public void Respawn()
        {
            Opened = false; Respawned = true; openedDay = -999;
            if (_lid == null) _lid = transform.Find("lidPivot");
            if (_lid != null) _lid.localRotation = Quaternion.identity;
            if (_glow == null) _glow = GetComponentInChildren<Light>();
            if (_glow != null) _glow.intensity = 1.2f;
        }

        /// Save-game restore: lid open, no rewards, no ceremony.
        public void RestoreOpened()
        {
            if (Opened) return;
            Opened = true;
            if (_lid == null) _lid = transform.Find("lidPivot");
            if (_lid != null) _lid.localRotation = Quaternion.Euler(-70f, 0f, 0f);
            if (_glow == null) _glow = GetComponentInChildren<Light>();
            if (_glow != null) _glow.intensity = 0f;
        }

        IEnumerator OpenRoutine()
        {
            AudioMan.I.Play(Sfx.Absorb(), transform.position, 0.7f, 0.8f);
            float t = 0f;
            while (t < 0.45f)
            {
                t += Time.deltaTime;
                if (_lid != null)
                    _lid.localRotation = Quaternion.Euler(Mathf.Lerp(0f, -70f, t / 0.45f), 0f, 0f);
                yield return null;
            }

            Color burst = tier >= 2 ? new Color(1f, 0.85f, 0.35f) : tier == 1 ? new Color(0.8f, 0.88f, 1f) : new Color(1f, 0.8f, 0.55f);
            VFXLibrary.SpawnNova(transform.position, burst, tier >= 2 ? 5f : 3f);
            VFXLibrary.Flash(transform.position + Vector3.up * 0.8f, burst, 2f, 0.3f);
            if (_glow != null) _glow.intensity = 0f;

            GrantRewards();
            if (tier >= 2 && MusicDirector.I != null) MusicDirector.I.PlayVictory();
        }

        void GrantRewards()
        {
            if (Respawned) { Inventory.Add(ItemDB.Residue0, 2); if (Random.value < 0.3f) Inventory.Add(ItemDB.Stone0, 1); }
            else DropTables.ChestLoot(tier, transform.position);
            switch (tier)
            {
                case 0:
                {
                    int shards = Respawned ? Random.Range(8, 16) : Random.Range(12, 25);
                    if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(shards);
                    EchoOrb.SpawnAt(transform.position + Vector3.up * 0.7f, 1);
                    HUDController.Toast("나무 상자 — 조각소리 +" + shards);
                    break;
                }
                case 1:
                {
                    if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(25);
                    int defId = Random.Range(0, 4);
                    if (EchoSystem.I != null) EchoSystem.I.Add(defId);
                    HUDController.Toast("은빛 상자 — 조각소리 +25, 에코 발견");
                    break;
                }
                default:
                {
                    if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(60);
                    if (Random.value < 0.5f && WeaponSystem.I != null)
                    {
                        WeaponSystem.I.Add(Random.value < 0.35f ? 2 : 1);
                        HUDController.Toast("황금 상자 — 조각소리 +60, 무기 발견!");
                    }
                    else
                    {
                        if (EchoSystem.I != null) EchoSystem.I.Add(4);
                        HUDController.Toast("황금 상자 — 조각소리 +60, ★5 에코!");
                    }
                    break;
                }
            }
        }

        /// Code-built chest (called by the editor scene builder).
        public static TreasureChest Build(Vector3 basePos, int id, int tier)
        {
            var root = new GameObject("Chest_" + id);
            root.transform.position = basePos;
            root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var chest = root.AddComponent<TreasureChest>();
            chest.chestId = id;
            chest.tier = tier;

            Color body = tier >= 2 ? new Color(0.55f, 0.4f, 0.14f) : tier == 1 ? new Color(0.42f, 0.46f, 0.52f) : new Color(0.4f, 0.28f, 0.16f);
            Color trim = tier >= 2 ? new Color(1f, 0.8f, 0.3f) : tier == 1 ? new Color(0.78f, 0.84f, 0.95f) : new Color(0.55f, 0.42f, 0.26f);

            var bm = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            bm.SetColor("_BaseColor", body);
            var tm = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            tm.SetColor("_BaseColor", trim);
            if (tier >= 2)
            {
                tm.EnableKeyword("_EMISSION");
                tm.SetColor("_EmissionColor", trim * 0.7f);
            }

            var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseGo.name = "base";
            baseGo.transform.SetParent(root.transform, false);
            baseGo.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            baseGo.transform.localScale = new Vector3(1.0f, 0.55f, 0.68f);
            baseGo.GetComponent<MeshRenderer>().sharedMaterial = bm;

            var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
            band.name = "band";
            Object.DestroyImmediate(band.GetComponent<Collider>());
            band.transform.SetParent(root.transform, false);
            band.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            band.transform.localScale = new Vector3(1.03f, 0.14f, 0.71f);
            band.GetComponent<MeshRenderer>().sharedMaterial = tm;

            var lidPivot = new GameObject("lidPivot");
            lidPivot.transform.SetParent(root.transform, false);
            lidPivot.transform.localPosition = new Vector3(0f, 0.58f, -0.34f);   // back hinge

            var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lid.name = "lid";
            Object.DestroyImmediate(lid.GetComponent<Collider>());
            lid.transform.SetParent(lidPivot.transform, false);
            lid.transform.localPosition = new Vector3(0f, 0.1f, 0.34f);
            lid.transform.localScale = new Vector3(1.02f, 0.22f, 0.7f);
            lid.GetComponent<MeshRenderer>().sharedMaterial = tm;

            var latch = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            latch.name = "latch";
            Object.DestroyImmediate(latch.GetComponent<Collider>());
            latch.transform.SetParent(root.transform, false);
            latch.transform.localPosition = new Vector3(0f, 0.5f, 0.36f);
            latch.transform.localScale = Vector3.one * 0.16f;
            latch.GetComponent<MeshRenderer>().sharedMaterial = tm;

            if (tier >= 1)
            {
                var l = latch.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = trim;
                l.range = tier >= 2 ? 6f : 3.5f;
                l.intensity = tier >= 2 ? 1.8f : 0.9f;
            }
            return chest;
        }
    }
}
