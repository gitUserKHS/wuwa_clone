using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WuWa
{
    /// Player options — separate from the save slot (options.json). Design doc ch.8.
    [Serializable]
    public class OptionsData
    {
        public int version = 1;
        // audio
        public float masterVol = 1f, bgmVol = 1f, sfxVol = 1f, uiVol = 0.8f, hitVol = 1f, duckMul = 1f;
        public bool muteInBackground = true;
        // gameplay
        public float shakeMul = 1f, hitstopMul = 1f, slowMoMul = 1f, flashMul = 1f;
        public int dmgNumbers = 2;               // 0 off, 1 crit only, 2 all
        public float dmgScale = 1f;
        public bool minimap = true;
        public int minimapMode = 1;              // 0 north-up, 1 rotate with camera (S3)
        public int minimapRadius = 1;            // 70 / 120 / 200
        public bool roadRoute = true;            // map route hint follows the dirt roads
        public int mapFilters = -1;              // marker category bitmask (map filter panel)
        public bool mapRevealAll = false;        // demo convenience: no fog of war
        public bool questTracker = true, objectiveMarker = true, tutorials = true, showFps = false, combatGrade = true, autoPickup = true;
        public int dialogueSpeed = 1;            // 0 slow 1 normal 2 fast 3 instant
        public bool dialogueAuto = false;
        public int autosaveNotice = 1;           // 0 icon only, 1 icon+toast, 2 off
        public float hudScale = 1f;
        // graphics
        public int displayMode = 1;              // 0 exclusive, 1 borderless, 2 windowed
        public int resW = 0, resH = 0;           // 0 = desktop
        public bool vsync = false;
        public int frameCap = 120;               // -1 unlimited
        public int quality = 2;                  // 0 low 1 medium 2 high 3 custom
        public float renderScale = 1f;
        public int shadowQuality = 3;            // 0 off 1 low 2 medium 3 high
        public int aa = 2;                       // 0 none 1 FXAA 2 SMAA
        public bool ssao = true, bloom = true, vignette = true, lensFlare = true;
        public int grassDensity = 2;             // 0 off 1 low 2 normal 3 high
        public int grassDistance = 1;            // 40 / 66 / 90
        public int decoDistance = 1;             // near / normal / far
        public float brightness = 0f;            // EV
        public float fov = 55f;
        // controls
        public float mouseSensX = 33f, mouseSensY = 33f, padSensX = 50f, padSensY = 50f;
        public bool padAccel = true, invertX = false, invertY = false;
        public float deadzoneL = 0.15f, deadzoneR = 0.20f;
        public int stickCurve = 1;               // 0 linear 1 smooth 2 aggressive
        public float triggerThreshold = 0.5f;
        public float vibration = 0.5f;
        public bool vibCombat = true, vibMove = true, vibFx = true, vibUI = false, lightBar = true;
        public int sprintMode = 0;               // 0 hold 1 toggle 2 auto only
        public int autoSprintDelay = 2;          // off / 2 / 3.5 / 5
        public bool dodgeRmb = true, autoAim = true;
        public int magnet = 2;                   // 0 / 8 / 15 / 22 m/s
        public bool lockCamTrack = true;
        public int lockAssist = 1;               // weak / normal / strong
        public bool moveCamCorrect = false;
        public float camDistance = 4.8f, camCombatDistance = 5.6f;
        public bool padAimAssist = true;
        public int glyphStyle = 0;
        // accessibility
        public int colorblind = 0;               // 0 none 1 red-green 2 blue-yellow 3 high contrast
        public bool highContrast = false;
        public float textScale = 1f;
        public bool reduceFlash = false, holdToggleSprint = false, holdToggleGlide = false, holdToggleHeavy = false;
        public int timingAssist = 0;             // 0 off 1 light 2 strong
        // input
        public string inputOverrides = "";
        public bool migratedFromSave = false;
    }

    public enum SettingKind { Toggle, Slider, Cycle, Button }

    /// One row of the settings screen; the screen is generated from these.
    public class SettingDef
    {
        public string key, tab, label, tooltip;
        public SettingKind kind;
        public float min, max, step;
        public string[] options;
        public Func<object> get;
        public Action<object> set;
        public Action onClick;
        public bool dangerous;                   // hold-to-confirm
        public bool restartNote;
    }

    public static class SettingsStore
    {
        public static OptionsData D = new OptionsData();
        public static readonly List<SettingDef> Defs = new List<SettingDef>();
        public static event Action<string> Changed;
        static bool _loaded;
        static float _dirtyAt = -1f;

        public static string PathFor() { return Path.Combine(Application.persistentDataPath, "options.json"); }

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string p = PathFor();
                if (File.Exists(p))
                {
                    var d = JsonUtility.FromJson<OptionsData>(File.ReadAllText(p));
                    if (d != null) D = d;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[WuWa] options load failed: " + ex.Message); }
            if (Defs.Count == 0) SettingsCatalog.Register();
        }

        public static void Save()
        {
            try
            {
                string p = PathFor();
                string tmp = p + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(D, true));
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
            }
            catch (Exception ex) { Debug.LogWarning("[WuWa] options save failed: " + ex.Message); }
            _dirtyAt = -1f;
        }

        /// Deferred save (many slider ticks → one write).
        public static void MarkDirty() { _dirtyAt = Time.unscaledTime; }
        public static void Tick()
        {
            if (_dirtyAt >= 0f && Time.unscaledTime - _dirtyAt > 0.6f) Save();
        }

        public static SettingDef Find(string key)
        {
            for (int i = 0; i < Defs.Count; i++) if (Defs[i].key == key) return Defs[i];
            return null;
        }

        public static void Set(string key, object value)
        {
            var d = Find(key);
            if (d == null || d.set == null) return;
            d.set(value);
            SettingsAppliers.Apply(key);
            MarkDirty();
            if (Changed != null) Changed(key);
        }

        public static object Get(string key)
        {
            var d = Find(key);
            return d != null && d.get != null ? d.get() : null;
        }

        public static void ResetTab(string tab)
        {
            var fresh = new OptionsData();
            foreach (var d in Defs)
            {
                if (d.tab != tab || d.set == null || d.kind == SettingKind.Button) continue;
                var fd = Find(d.key);
                // copy default by re-reading from a fresh instance through the catalog's default map
                object def;
                if (SettingsCatalog.Defaults.TryGetValue(d.key, out def)) { fd.set(def); SettingsAppliers.Apply(d.key); }
            }
            Save();
            if (Changed != null) Changed(tab);
        }

        /// One-time import of the option values that used to live in the save slot.
        public static void MigrateFromSave(float master, float bgm, float sfx, float shake, float hitstop, bool dmg, bool minimap)
        {
            if (D.migratedFromSave || File.Exists(PathFor())) { D.migratedFromSave = true; return; }
            D.masterVol = master; D.bgmVol = bgm; D.sfxVol = sfx; D.shakeMul = shake; D.hitstopMul = hitstop;
            D.dmgNumbers = dmg ? 2 : 0; D.minimap = minimap; D.migratedFromSave = true;
            Save();
            SettingsAppliers.ApplyAll();
            Debug.Log("[WuWa] options migrated from save file");
        }
    }
}
