using System;
using System.IO;
using UnityEngine;

namespace WuWa
{
    /// JSON saves (design doc ch.11): one auto slot + three manual slots. Every
    /// slot has a small header file the title screen reads without parsing the
    /// body. Writes are atomic (tmp → replace, previous copy kept as .bak) and a
    /// broken body falls back to the backup. Versioned migrations run on load.
    /// Nothing is loaded or ticked until the title screen starts a session.
    public class SaveSystem : MonoBehaviour
    {
        public const int CurrentVersion = 6;
        public const int SlotCount = 4;               // 0 = auto, 1..3 = manual

        [Serializable]
        public class SaveData
        {
            public int version = CurrentVersion;
            public string savedAt;
            public int level = 1;
            public float exp;
            public int shards;
            public int[] weaponIds = new int[0];
            public int[] weaponCounts = new int[0];
            public int[] weaponEquipped = new int[0];
            public EchoSystem.EchoSaveEntry[] echoes = new EchoSystem.EchoSaveEntry[0];
            public int[] echoEquipped = new int[0];
            public int[] echoDiscovered = new int[0];
            public int echoNextUid = 1;
            public int[] towersActive = new int[0];
            public int[] waystones = new int[0];
            public int[] chestsOpened = new int[0];
            public int questStep;
            public float[] respawn = new float[0];
            // options (v1 only — migrated into options.json once)
            public float masterVol = 1f, bgmVol = 1f, sfxVol = 1f;
            public float shakeMul = 1f, hitstopMul = 1f;
            public bool dmgNumbers = true, minimap = true;
            // world clock, story flags, repeatable content
            public float timeOfDay = -1f;
            public string[] flags = new string[0];
            public int arenaClears, arenaBestWave, riftsClosed;
            public int arenaTierBest;                  // v6
            public float playSeconds;
            public string fog;
            public int discoveredRegions;
            public float[] pins = new float[0];
            public int[] pinColors = new int[0];
            // v2 (S4): items, quick slot, flask, wallets, day index, shop stock
            public int[] itemIds = new int[0];
            public int[] itemCounts = new int[0];
            public int quickSlot = -1;
            public int flaskCharges = -1;
            public int trialTokens;
            public int tunerPity;
            public int dayIndex;
            public int shopDay = -1;
            public int[] shopBought = new int[0];
            // v3 (S5): per-character growth, weapon instances
            public CharacterProgress[] chars = new CharacterProgress[0];
            public WeaponSystem.WeaponSaveEntry[] weaponInstances = new WeaponSystem.WeaponSaveEntry[0];
            public int weaponNextUid = 1;
            public int[] weaponEquipUid = new int[0];
            // v4 (S6): chest respawn days, gather nodes, rift regions, codex kills, bounties, tracking
            public int[] chestsOpenedDay = new int[0];
            public int[] nodeDays = new int[0];
            public int riftRegionMask;
            public int[] killsByKind = new int[0];
            public int eliteKills, bossKills;
            public int bountyDay = -1, bountyGrandDay = -1;
            public int[] bountyTypes = new int[0], bountyRegions = new int[0], bountyGoals = new int[0], bountyProgress = new int[0], bountyDone = new int[0], bountyGrand = new int[0];
            public int trackedBounty = -1;
            // v5 (S7): resume position, on-field member, results-screen tallies
            public float[] pos = new float[0];
            public float yaw;
            public int activeMember;
            public int kills, parries, perfectDodges, rankS, chestsOpenedCount;
        }

        /// Small per-slot header (separate file) for the title / slot list.
        [Serializable]
        public class SlotHeader
        {
            public int slot;
            public int version;
            public string savedAt = "";
            public string reason = "";
            public string chapter = "", quest = "", region = "";
            public int[] levels = new int[0];
            public float playSeconds;
            public int shards;
            public int towers;
            public bool demoDone;
        }

        public static SaveSystem I { get; private set; }

        public static float AutosaveInterval = 300f;
        public static int NoticeMode = 1;              // 0 icon only, 1 icon+toast, 2 off
        public static float PlaySeconds;
        public static string LastSaveInfo;
        public static bool SessionStarted { get; private set; }
        public static int ActiveSlot = -1;             // slot this session came from / last manual slot; -1 = none yet
        public static bool SkipQuitSave;               // "저장 안 함" quit
        public static event Action SlotsChanged;

        float _autosaveTimer = 300f;
        string _lastReason;

        // ---------------------------------------------------------------- files
        static string Dir { get { return Application.persistentDataPath; } }
        static string FileFor(int slot) { return Path.Combine(Dir, slot == 0 ? "wuwa_slot_auto.json" : "wuwa_slot_" + slot + ".json"); }
        static string HeadFor(int slot) { return Path.Combine(Dir, slot == 0 ? "wuwa_slot_auto.head.json" : "wuwa_slot_" + slot + ".head.json"); }
        static string LegacyPath { get { return Path.Combine(Dir, "wuwa_save.json"); } }
        static string ThumbFor(int slot) { return Path.Combine(Dir, slot == 0 ? "wuwa_slot_auto.jpg" : "wuwa_slot_" + slot + ".jpg"); }
        public static bool HasThumb(int slot) { return File.Exists(ThumbFor(slot)); }
        public static string SlotName(int slot) { return slot == 0 ? "자동 저장" : "슬롯 " + slot; }
        public static bool SlotExists(int slot) { return slot >= 0 && slot < SlotCount && File.Exists(FileFor(slot)); }

        void Awake()
        {
            I = this;
            SessionStarted = false;
            ActiveSlot = -1;
            PlaySeconds = 0f;
            LastSaveInfo = null;
            SkipQuitSave = false;
            MigrateLegacyFile();
        }

        void OnDestroy() { if (I == this) I = null; }

        /// The pre-S7 single file becomes the auto slot (or is parked if one already exists).
        static void MigrateLegacyFile()
        {
            try
            {
                if (!File.Exists(LegacyPath)) return;
                if (!File.Exists(FileFor(0))) { File.Move(LegacyPath, FileFor(0)); Debug.Log("[WuWa] legacy save moved to the auto slot"); }
                else
                {
                    string parked = LegacyPath + ".migrated";
                    if (File.Exists(parked)) File.Delete(parked);
                    File.Move(LegacyPath, parked);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[WuWa] legacy save migration failed: " + ex.Message); }
        }

        void Update()
        {
            if (!SessionStarted) return;
            PlaySeconds += Time.unscaledDeltaTime;
            _autosaveTimer -= Time.unscaledDeltaTime;
            if (_autosaveTimer <= 0f)
            {
                var pc = PlayerController.Instance;
                if (pc != null && pc.InCombat) _autosaveTimer = 15f;      // let the fight end first
                else AutoSave("주기 저장");
            }
            if (InputService.SavePressed) QuickSave();
        }

        void OnApplicationQuit() { if (SessionStarted && !SkipQuitSave) WriteSlot(0, "종료 저장", false); }

        // ---------------------------------------------------------------- session
        public void NewGame()
        {
            SessionStarted = true;
            ActiveSlot = -1;
            PlaySeconds = 0f;
            LastSaveInfo = null;
            _autosaveTimer = AutosaveInterval;
            Debug.Log("[WuWa] new game");
        }

        public bool LoadSlot(int slot)
        {
            string note;
            var d = ReadBody(slot, out note);
            if (d == null) return false;
            int was = Migrate(d);
            try { ApplyLoaded(d); }
            catch (Exception ex) { Debug.LogWarning("[WuWa] load apply failed: " + ex.Message + "\n" + ex.StackTrace); }
            SessionStarted = true;
            ActiveSlot = slot;
            _autosaveTimer = AutosaveInterval;
            LastSaveInfo = d.savedAt + "  ·  " + SlotName(slot) + (note != null ? "  ·  " + note : "");
            HUDController.Toast("이어하기 — " + SlotName(slot) + " · " + d.savedAt + (was < CurrentVersion ? "  (v" + was + " → v" + CurrentVersion + " 이관)" : "") + (note != null ? "  · " + note : ""));
            Debug.Log("[WuWa] loaded slot " + slot + " saved " + d.savedAt + " v" + was);
            return true;
        }

        static SaveData ReadBody(int slot, out string note)
        {
            note = null;
            string path = FileFor(slot);
            if (File.Exists(path))
            {
                try { var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(path)); if (d != null) return d; }
                catch (Exception ex) { Debug.LogWarning("[WuWa] slot " + slot + " unreadable: " + ex.Message); }
            }
            string bak = path + ".bak";
            if (File.Exists(bak))
            {
                try { var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(bak)); if (d != null) { note = "백업에서 복구"; return d; } }
                catch (Exception ex) { Debug.LogWarning("[WuWa] slot " + slot + " backup unreadable: " + ex.Message); }
            }
            return null;
        }

        /// Stepwise version upgrades; returns the version the file had.
        public static int Migrate(SaveData d)
        {
            int from = d.version;
            if (d.version < 2)
            {
                // v1: party-wide level/exp, weapon count tables, no items — importers convert the tables
                d.itemIds = new int[0]; d.itemCounts = new int[0];
                d.quickSlot = -1; d.flaskCharges = -1; d.dayIndex = 0; d.shopDay = -1;
            }
            if (d.version < 3)
            {
                // v2: no per-character growth / weapon instances — ProgressSystem/WeaponSystem derive them
                if (d.chars == null) d.chars = new CharacterProgress[0];
                if (d.weaponInstances == null) d.weaponInstances = new WeaponSystem.WeaponSaveEntry[0];
            }
            if (d.version < 4)
            {
                // v3: no chest days, gather nodes, bounties or tracking
                int n = d.chestsOpened != null ? d.chestsOpened.Length : 0;
                d.chestsOpenedDay = new int[n];
                for (int i = 0; i < n; i++) d.chestsOpenedDay[i] = -999;
                d.nodeDays = new int[0]; d.riftRegionMask = 0;
                d.bountyDay = -1; d.bountyGrandDay = -1; d.trackedBounty = -1;
            }
            if (d.version < 5)
            {
                // v4: no resume position / tallies — continue at the respawn point
                d.pos = new float[0]; d.yaw = 0f; d.activeMember = 0;
                d.kills = 0; d.parries = 0; d.perfectDodges = 0; d.rankS = 0;
                d.chestsOpenedCount = d.chestsOpened != null ? d.chestsOpened.Length : 0;
            }
            if (d.version < 6)
            {
                // v5: no trial tiers / echo locks — a cleared trial counts as Tier I
                d.arenaTierBest = d.arenaClears > 0 ? 1 : 0;
            }
            if (from < CurrentVersion) Debug.Log("[WuWa] save migrated v" + from + " → v" + CurrentVersion);
            d.version = CurrentVersion;
            return from;
        }

        // ---------------------------------------------------------------- writing
        public void AutoSave(string reason)
        {
            if (!SessionStarted) return;
            _autosaveTimer = AutosaveInterval;
            WriteSlot(0, reason, true);
        }

        /// F9: the slot this session lives in (manual slot if one was chosen, else the auto slot).
        public void QuickSave()
        {
            if (!SessionStarted || CombatBlocked()) return;
            WriteSlot(ActiveSlot > 0 ? ActiveSlot : 0, "수동 저장", true);
        }

        public bool SaveToSlot(int slot, string reason)
        {
            if (!SessionStarted || slot <= 0 || slot >= SlotCount || CombatBlocked()) return false;
            ActiveSlot = slot;
            WriteSlot(slot, reason, true);
            return true;
        }

        static bool CombatBlocked()
        {
            var pc = PlayerController.Instance;
            if (pc != null && pc.InCombat) { HUDController.Toast("전투 중에는 저장할 수 없습니다"); return true; }
            return false;
        }

        void WriteSlot(int slot, string reason, bool notify)
        {
            _lastReason = reason;
            try
            {
                var d = Collect();
                AtomicWrite(FileFor(slot), JsonUtility.ToJson(d, true));
                CaptureThumb(slot);
                File.WriteAllText(HeadFor(slot), JsonUtility.ToJson(HeaderFrom(d, slot, reason), true));
                LastSaveInfo = DateTime.Now.ToString("HH:mm:ss") + "  ·  " + SlotName(slot) + (string.IsNullOrEmpty(reason) ? "" : "  ·  " + reason);
                if (notify)
                {
                    HUDController.SaveIndicator();
                    if (NoticeMode != 2) HUDController.Toast("저장됨 — " + SlotName(slot) + " · " + reason);
                }
                Debug.Log("[WuWa] saved slot " + slot + " (" + reason + ") -> " + FileFor(slot));
                if (SlotsChanged != null) SlotsChanged();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WuWa] save failed: " + ex.Message);
                HUDController.Toast("저장 실패 — " + ex.Message);
            }
        }

        static void AtomicWrite(string path, string text)
        {
            string tmp = path + ".tmp", bak = path + ".bak";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, bak);
            else File.Move(tmp, path);
        }

        SaveData Collect()
        {
            var d = new SaveData { savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };

            if (ProgressSystem.I != null)
            {
                d.level = ProgressSystem.I.Level;
                d.exp = ProgressSystem.I.Exp;
                d.chars = ProgressSystem.I.Export();
                d.shards = ProgressSystem.I.Shards;
            }
            if (WeaponSystem.I != null)
                WeaponSystem.I.Export(out d.weaponInstances, out d.weaponNextUid, out d.weaponEquipUid);
            if (EchoSystem.I != null)
                EchoSystem.I.ExportState(out d.echoes, out d.echoEquipped, out d.echoDiscovered, out d.echoNextUid);

            var towers = UnityEngine.Object.FindObjectsByType<ResonanceTower>(FindObjectsSortMode.None);
            int n = 0;
            foreach (var t in towers) if (t.Activated) n++;
            d.towersActive = new int[n];
            int i = 0;
            foreach (var t in towers) if (t.Activated) d.towersActive[i++] = t.towerId;

            var stones = new System.Collections.Generic.List<int>();
            foreach (var w in Waystone.All) if (w != null && w.Discovered) stones.Add(w.stoneId);
            d.waystones = stones.ToArray();

            var chests = new System.Collections.Generic.List<int>();
            var chestDays = new System.Collections.Generic.List<int>();
            foreach (var ch in TreasureChest.All) if (ch != null && ch.Opened) { chests.Add(ch.chestId); chestDays.Add(ch.openedDay); }
            d.chestsOpened = chests.ToArray();
            d.chestsOpenedDay = chestDays.ToArray();

            d.fog = MapDiscovery.Export();
            d.discoveredRegions = MapDiscovery.RegionMask;
            MapPins.Export(out d.pins, out d.pinColors);
            Inventory.Export(out d.itemIds, out d.itemCounts);
            d.quickSlot = Inventory.QuickSlot; d.flaskCharges = Inventory.FlaskCharges;
            d.trialTokens = Inventory.TrialTokens; d.tunerPity = Inventory.TunerPity;
            d.dayIndex = DayNightCycle.DayIndex;
            ShopStock.Export(out d.shopDay, out d.shopBought);
            GatherNode.Export(out d.nodeDays);
            d.riftRegionMask = RegionCompletion.RiftRegionMask;
            Codex.Export(out d.killsByKind, out d.eliteKills, out d.bossKills);
            BountyBoard.Export(out d.bountyDay, out d.bountyGrandDay, out d.bountyTypes, out d.bountyRegions, out d.bountyGoals, out d.bountyProgress, out d.bountyDone, out d.bountyGrand);
            d.trackedBounty = QuestSystem.I != null ? QuestSystem.I.TrackedBounty : -1;

            if (QuestSystem.I != null) d.questStep = QuestSystem.I.StepIndex;
            if (GameDirector.I != null)
            {
                var r = GameDirector.I.respawnPoint;
                d.respawn = new[] { r.x, r.y, r.z };
            }
            var pc = PlayerController.Instance;
            if (pc != null)
            {
                var p = pc.transform.position;
                d.pos = new[] { p.x, p.y, p.z };
                d.yaw = pc.transform.eulerAngles.y;
                var team = pc.GetComponent<TeamManager>();
                d.activeMember = team != null ? team.ActiveIndex : 0;
            }

            d.masterVol = AudioListener.volume;
            d.bgmVol = MusicDirector.BgmMul;
            d.sfxVol = AudioMan.SfxMul;
            d.shakeMul = CameraShaker.Mul;
            d.hitstopMul = Hitstop.Mul;
            d.dmgNumbers = DamageNumbers.Enabled;
            d.minimap = MapSystem.MinimapEnabled;
            if (DayNightCycle.I != null) d.timeOfDay = DayNightCycle.I.timeOfDay;
            d.flags = GameFlags.Export();
            d.arenaClears = ContentStats.ArenaClears;
            d.arenaBestWave = ContentStats.ArenaBestWave;
            d.riftsClosed = ContentStats.RiftsClosed;
            d.arenaTierBest = ContentStats.ArenaTierBest;
            d.kills = ContentStats.Kills; d.parries = ContentStats.Parries; d.perfectDodges = ContentStats.PerfectDodges;
            d.rankS = ContentStats.RankS; d.chestsOpenedCount = ContentStats.ChestsOpened;
            d.playSeconds = PlaySeconds;
            return d;
        }

        // ---------------------------------------------------------------- thumbnails (256×144 JPG next to the slot)
        static void CaptureThumb(int slot)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) return;
                const int W = 256, H = 144;
                var rt = RenderTexture.GetTemporary(W, H, 24);
                var prevTarget = cam.targetTexture;
                var prevActive = RenderTexture.active;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(ThumbFor(slot), tex.EncodeToJPG(82));
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception ex) { Debug.LogWarning("[WuWa] thumbnail failed: " + ex.Message); }
        }

        /// Loads a slot thumbnail (caller owns / destroys the texture); null when missing.
        public static Texture2D LoadThumb(int slot)
        {
            try
            {
                string p = ThumbFor(slot);
                if (!File.Exists(p)) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!t.LoadImage(File.ReadAllBytes(p))) { UnityEngine.Object.Destroy(t); return null; }
                t.filterMode = FilterMode.Bilinear;
                return t;
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- headers
        static SlotHeader HeaderFrom(SaveData d, int slot, string reason)
        {
            var h = new SlotHeader { slot = slot, version = d.version, savedAt = d.savedAt ?? "", reason = reason ?? "" };
            var qs = QuestSystem.I;
            if (qs != null && qs.StepCount > 0)
            {
                if (d.questStep >= qs.StepCount) { h.chapter = "완결"; h.quest = "잔향 — 데모 완결"; }
                else
                {
                    string t = qs.Step(d.questStep).title;
                    int cut = t.IndexOf('·');
                    h.chapter = cut > 0 ? t.Substring(0, cut).Trim() : "";
                    h.quest = t;
                }
            }
            float x = 0f, z = 0f; bool hasPos = false;
            if (d.pos != null && d.pos.Length == 3) { x = d.pos[0]; z = d.pos[2]; hasPos = true; }
            else if (d.respawn != null && d.respawn.Length == 3) { x = d.respawn[0]; z = d.respawn[2]; hasPos = true; }
            h.region = hasPos ? WorldRegions.RegionName(WorldRegions.RegionAt(x, z)) : "";
            if (d.chars != null && d.chars.Length > 0)
            {
                h.levels = new int[d.chars.Length];
                for (int i = 0; i < d.chars.Length; i++) h.levels[i] = d.chars[i] != null ? d.chars[i].level : d.level;
            }
            else h.levels = new[] { d.level, d.level, d.level };
            h.playSeconds = d.playSeconds;
            h.shards = d.shards;
            h.towers = d.towersActive != null ? d.towersActive.Length : 0;
            h.demoDone = d.flags != null && Array.IndexOf(d.flags, "demo_done") >= 0;
            return h;
        }

        /// Headers of every slot (null = empty). A body without a header gets one rebuilt.
        public static SlotHeader[] ReadHeaders()
        {
            var hs = new SlotHeader[SlotCount];
            for (int s = 0; s < SlotCount; s++)
            {
                string body = FileFor(s), head = HeadFor(s);
                if (!File.Exists(body)) continue;
                if (File.Exists(head))
                {
                    try { hs[s] = JsonUtility.FromJson<SlotHeader>(File.ReadAllText(head)); } catch { hs[s] = null; }
                }
                if (hs[s] == null)
                {
                    try
                    {
                        var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(body));
                        if (d != null)
                        {
                            Migrate(d);
                            hs[s] = HeaderFrom(d, s, "이관");
                            File.WriteAllText(head, JsonUtility.ToJson(hs[s], true));
                        }
                    }
                    catch (Exception ex) { Debug.LogWarning("[WuWa] header rebuild failed for slot " + s + ": " + ex.Message); }
                }
            }
            return hs;
        }

        public static int LatestSlot(SlotHeader[] hs)
        {
            int best = -1; string bestAt = null;
            if (hs == null) return -1;
            for (int i = 0; i < hs.Length; i++)
                if (hs[i] != null && (bestAt == null || string.CompareOrdinal(hs[i].savedAt, bestAt) > 0)) { best = i; bestAt = hs[i].savedAt; }
            return best;
        }

        public static string Clock(float s)
        {
            int h = Mathf.FloorToInt(s / 3600f), m = Mathf.FloorToInt(s / 60f) % 60;
            return h > 0 ? h + "시간 " + m + "분" : m + "분";
        }

        public static string Describe(SlotHeader h, bool multiline)
        {
            if (h == null) return "비어 있음";
            string lv = "";
            if (h.levels != null) for (int i = 0; i < h.levels.Length; i++) lv += (i > 0 ? "/" : "") + h.levels[i];
            string sep = multiline ? "\n" : "  ·  ";
            string line1 = (string.IsNullOrEmpty(h.quest) ? "" : h.quest + "  ·  ") + h.region;
            string line2 = "Lv " + lv + "  ·  플레이 " + Clock(h.playSeconds) + "  ·  조각소리 " + UIKit.Num(h.shards) + "  ·  공명탑 " + h.towers + "/4" + (h.demoDone ? "  ·  완결" : "");
            string line3 = h.savedAt + (string.IsNullOrEmpty(h.reason) ? "" : "  ·  " + h.reason);
            return line1 + sep + line2 + sep + line3;
        }

        public static void DeleteSlot(int slot)
        {
            try
            {
                string f = FileFor(slot);
                foreach (var p in new[] { f, f + ".bak", f + ".tmp", HeadFor(slot), ThumbFor(slot) }) if (File.Exists(p)) File.Delete(p);
                if (ActiveSlot == slot) ActiveSlot = -1;
                if (SlotsChanged != null) SlotsChanged();
                Debug.Log("[WuWa] deleted slot " + slot);
            }
            catch (Exception ex) { Debug.LogWarning("[WuWa] delete failed: " + ex.Message); }
        }

        /// Settings > 저장: wipes the slot this session lives in (auto slot when none was chosen).
        public void DeleteSave()
        {
            int s = ActiveSlot > 0 ? ActiveSlot : 0;
            DeleteSlot(s);
            HUDController.Toast("저장 데이터 삭제됨 — " + SlotName(s));
        }

        // ---------------------------------------------------------------- applying a loaded body
        void ApplyLoaded(SaveData d)
        {
            if (ProgressSystem.I != null) ProgressSystem.I.ImportState(d.level, d.exp, d.shards, d.chars);
            if (WeaponSystem.I != null) WeaponSystem.I.Import(d.weaponInstances, d.weaponNextUid, d.weaponEquipUid, d.weaponIds, d.weaponCounts, d.weaponEquipped);
            if (EchoSystem.I != null) EchoSystem.I.ImportState(d.echoes, d.echoEquipped, d.echoDiscovered, d.echoNextUid);

            if (d.towersActive != null)
                foreach (var t in UnityEngine.Object.FindObjectsByType<ResonanceTower>(FindObjectsSortMode.None))
                    foreach (var id in d.towersActive)
                        if (t.towerId == id) t.RestoreActivated();

            if (d.waystones != null)
                foreach (var w in Waystone.All)
                    foreach (var id in d.waystones)
                        if (w != null && w.stoneId == id) w.RestoreDiscovered();

            if (d.chestsOpened != null)
                for (int k = 0; k < d.chestsOpened.Length; k++)
                {
                    int id = d.chestsOpened[k];
                    int day = d.chestsOpenedDay != null && k < d.chestsOpenedDay.Length ? d.chestsOpenedDay[k] : -999;
                    foreach (var ch in TreasureChest.All)
                        if (ch != null && ch.chestId == id) ch.RestoreOpened(day);
                }

            if (QuestSystem.I != null) QuestSystem.I.ImportStep(d.questStep);
            MapDiscovery.Import(d.fog, d.discoveredRegions);
            MapPins.Import(d.pins, d.pinColors);
            Inventory.Import(d.itemIds, d.itemCounts, d.quickSlot, d.flaskCharges, d.trialTokens, d.tunerPity);
            DayNightCycle.DayIndex = d.dayIndex;
            ShopStock.Import(d.shopDay, d.shopBought);

            var pc = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (GameDirector.I != null && d.respawn != null && d.respawn.Length == 3)
                GameDirector.I.respawnPoint = new Vector3(d.respawn[0], d.respawn[1], d.respawn[2]);
            Vector3 at = Vector3.zero; bool hasAt = false;
            if (d.pos != null && d.pos.Length == 3) { at = new Vector3(d.pos[0], d.pos[1], d.pos[2]) + Vector3.up * 0.15f; hasAt = true; }
            else if (GameDirector.I != null && d.respawn != null && d.respawn.Length == 3) { at = GameDirector.I.respawnPoint + Vector3.up * 1.2f; hasAt = true; }
            if (pc != null && hasAt)
            {
                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                pc.transform.position = at;
                if (d.pos != null && d.pos.Length == 3) pc.transform.rotation = Quaternion.Euler(0f, d.yaw, 0f);
                if (cc != null) cc.enabled = true;
                var team = pc.GetComponent<TeamManager>();
                if (team != null && d.activeMember > 0) team.RestoreActive(d.activeMember);
            }

            // options moved to options.json — import the old values once, then the store owns them
            SettingsStore.MigrateFromSave(d.masterVol, d.bgmVol, d.sfxVol, d.shakeMul, d.hitstopMul, d.dmgNumbers, d.minimap);
            if (d.timeOfDay >= 0f && DayNightCycle.I != null) DayNightCycle.I.SetTime(d.timeOfDay);
            GameFlags.Import(d.flags);
            Tutorial.MarkAllSeenIfVeteran(d.playSeconds);
            GatherNode.Import(d.nodeDays);
            RegionCompletion.RiftRegionMask = d.riftRegionMask;
            Codex.Import(d.killsByKind, d.eliteKills, d.bossKills);
            BountyBoard.Import(d.bountyDay, d.bountyGrandDay, d.bountyTypes, d.bountyRegions, d.bountyGoals, d.bountyProgress, d.bountyDone, d.bountyGrand);
            Codex.SeedIfVeteran(d.playSeconds);
            if (QuestSystem.I != null) { QuestSystem.I.TrackedBounty = d.trackedBounty; QuestSystem.I.RefreshTracker(); }
            ContentStats.ArenaClears = d.arenaClears;
            ContentStats.ArenaBestWave = d.arenaBestWave;
            ContentStats.RiftsClosed = d.riftsClosed;
            ContentStats.ArenaTierBest = d.arenaTierBest;
            ContentStats.Kills = d.kills; ContentStats.Parries = d.parries; ContentStats.PerfectDodges = d.perfectDodges;
            ContentStats.RankS = d.rankS; ContentStats.ChestsOpened = d.chestsOpenedCount;
            PlaySeconds = d.playSeconds;
        }
    }
}
