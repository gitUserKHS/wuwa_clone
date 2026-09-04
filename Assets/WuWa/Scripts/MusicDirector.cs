using System.Threading.Tasks;
using UnityEngine;

namespace WuWa
{
    /// BGM director (GDD ch.11). Exploration: four synchronized stems of one 104 BPM piece play forever
    /// and are mixed by volume (each freed resonance tower unlocks more melody). Combat: a separate
    /// 160 BPM piece takes the floor - a one-bar pickup fill covers the handoff, the combat stems start
    /// sample-locked on the next downbeat and loop seamlessly, and leaving combat fades them out while
    /// the exploration stems (never stopped) return wherever they are. Two tempi never sound at once.
    public class MusicDirector : MonoBehaviour
    {
        public static MusicDirector I { get; private set; }
        public static float BgmMul = 1f;             // options screen
        public static bool ForceCombat;               // arena / scripted fights keep the combat mix

        public float masterVol = 0.42f;
        public float combatVol = 0.8f;               // combat piece trim: drums-heavy piece sits ~3 dB above the old combat mix
        public float combatExitDelay = 4f;           // aggro-boundary hysteresis
        public float exploreRise = 0.7f;             // exploration returns gently after a fight
        public float exploreFall = 0.9f;             // ...and clears out within the pickup bar
        public float combatRise = 4f;                // combat stems are silent until their downbeat anyway
        public float combatFall = 1.0f;

        AudioSource[] _stems;                        // exploration: 0 pad, 1 melody, 2 bass, 3 perc
        AudioSource[] _cstems;                       // combat: 0 stabs, 1 lead, 2 bass, 3 drums
        AudioSource _oneShot, _pickup;
        Task<float[][]> _tLayers, _tCombat;
        Task<float[]> _tSting;
        AudioClip _stingClip, _pickupClip;
        bool _ready;
        readonly float[] _w = { 1f, 0f, 0f, 0f };    // exploration stem weights
        readonly float[] _cw = { 0f, 0f, 0f, 0f };   // combat stem weights
        float _checkTimer;
        float _combatHold;
        bool _combatState;
        bool _combatPlaying;                          // combat sources scheduled or running
        double _combatDownbeat;
        float _duck;                                  // SFX ducking envelope
        float _resync;

        public bool InCombatMusic { get { return _combatState; } }
        public bool CombatStemsPlaying { get { return _combatPlaying; } }
        public float CombatWeight { get { return _cw[3]; } }
        public float ExploreWeight { get { return _w[0]; } }
        public double CombatDownbeat { get { return _combatDownbeat; } }

        void Awake()
        {
            I = this;
            _stems = new AudioSource[4];
            _cstems = new AudioSource[4];
            for (int i = 0; i < 4; i++) { _stems[i] = MakeSource(true); _cstems[i] = MakeSource(true); }
            _oneShot = MakeSource(false);
            _pickup = MakeSource(false);

            _tLayers = Task.Run(MusicGen.Layers);
            _tCombat = Task.Run(MusicGen.CombatLayers);
            _tSting = Task.Run(MusicGen.VictorySting);
        }

        AudioSource MakeSource(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.volume = 0f;
            return s;
        }

        void Update()
        {
            if (!_ready)
            {
                if (_tLayers != null && _tLayers.IsCompleted && _tCombat.IsCompleted && _tSting.IsCompleted)
                {
                    var stems = _tLayers.Result;
                    string[] names = { "bgm_pad", "bgm_melody", "bgm_bass", "bgm_perc" };
                    for (int i = 0; i < 4; i++) _stems[i].clip = MakeClip(names[i], stems[i]);
                    var cst = _tCombat.Result;
                    string[] cnames = { "bgm_c_stabs", "bgm_c_lead", "bgm_c_bass", "bgm_c_drums" };
                    for (int i = 0; i < 4; i++) _cstems[i].clip = MakeClip(cnames[i], cst[i]);
                    _stingClip = MakeClip("bgm_victory", _tSting.Result);
                    _pickupClip = MakeClip("bgm_pickup", MusicGen.CombatPickup());

                    double start = AudioSettings.dspTime + 0.15;      // sample-locked start
                    for (int i = 0; i < 4; i++) _stems[i].PlayScheduled(start);
                    _ready = true;
                    Debug.Log("[WuWa] BGM ready: explore " + MusicGen.ExploreBpm + " BPM 4 stems " + _stems[0].clip.length.ToString("F1")
                        + "s, combat " + MusicGen.CombatBpm + " BPM 4 stems " + _cstems[0].clip.length.ToString("F1")
                        + "s loop, pickup " + MusicGen.CombatBarSec.ToString("F2") + "s");
                }
                return;
            }

            float dt = Time.unscaledDeltaTime;

            // ---- combat detection with exit hysteresis
            _checkTimer -= dt;
            if (_checkTimer <= 0f)
            {
                _checkTimer = 0.3f;
                bool wantCombat = false;
                var player = Object.FindAnyObjectByType<PlayerController>();
                if (player != null)
                {
                    for (int i = 0; i < EnemyAI.All.Count; i++)
                    {
                        var e = EnemyAI.All[i];
                        if (e == null || e.Hp == null || !e.Hp.IsAlive || !e.gameObject.activeInHierarchy) continue;
                        float d = WuWaUtil.Flat(e.transform.position - player.transform.position).magnitude;
                        if (d < 18f && e.IsAggro) { wantCombat = true; break; }
                    }
                }
                if (wantCombat || ForceCombat)
                {
                    if (!_combatState) EnterCombat();
                    _combatState = true;
                    _combatHold = combatExitDelay;
                }
                else
                {
                    _combatHold -= 0.3f;
                    if (_combatHold <= 0f) _combatState = false;
                }
            }

            // ---- layer targets. Exploration: melody unlocks with each freed tower, bass hums underneath.
            // Combat: the exploration piece clears out during the pickup bar and the 160 BPM piece takes over.
            float melodyUnlock = Mathf.Clamp01(0.25f + 0.25f * ResonanceTower.ActiveCount);
            float tPad = _combatState ? 0f : 1f;
            float tMel = _combatState ? 0f : melodyUnlock;
            float tBass = _combatState ? 0f : 0.10f;
            float tC = _combatState ? 1f : 0f;

            _w[0] = Move(_w[0], tPad, dt, exploreRise, exploreFall);
            _w[1] = Move(_w[1], tMel, dt, exploreRise, exploreFall);
            _w[2] = Move(_w[2], tBass, dt, exploreRise, exploreFall);
            _w[3] = Move(_w[3], 0f, dt, exploreRise, exploreFall);
            for (int i = 0; i < 4; i++) _cw[i] = Move(_cw[i], tC, dt, combatRise, combatFall);

            if (_combatPlaying && !_combatState && _cw[0] <= 0f && _cw[1] <= 0f && _cw[2] <= 0f && _cw[3] <= 0f)
            {
                for (int i = 0; i < 4; i++) _cstems[i].Stop();   // the next fight restarts on its own downbeat
                _combatPlaying = false;
            }

            // ---- SFX ducking + victory sting duck
            _duck = Mathf.Max(0f, _duck - dt * 3.2f);
            float duck = 1f - _duck;
            if (_oneShot.isPlaying && _oneShot.clip == _stingClip) duck *= 0.45f;

            float[] mix = { 0.9f, 0.85f, 0.8f, 0.85f };
            float[] cmix = { 0.8f, 0.9f, 0.85f, 0.8f };
            for (int i = 0; i < 4; i++)
            {
                _stems[i].volume = masterVol * BgmMul * mix[i] * _w[i] * duck;
                _cstems[i].volume = masterVol * combatVol * BgmMul * cmix[i] * _cw[i] * duck;
            }

            // ---- drift guard: each set shares one clip length, keep it sample-locked
            _resync -= dt;
            if (_resync <= 0f)
            {
                _resync = 45f;
                Resync(_stems);
                if (_combatPlaying && AudioSettings.dspTime > _combatDownbeat + 1.0) Resync(_cstems);
            }
        }

        static void Resync(AudioSource[] set)
        {
            int reference = set[0].timeSamples;
            for (int i = 1; i < set.Length; i++)
                if (Mathf.Abs(set[i].timeSamples - reference) > 2048)
                    set[i].timeSamples = reference;
        }

        /// Pickup bar now, combat stems sample-locked on the downbeat exactly one bar later.
        void EnterCombat()
        {
            if (_pickupClip == null) return;
            double t = AudioSettings.dspTime + 0.08;
            double bar = MusicGen.CombatBarSec;
            _pickup.Stop();
            _pickup.clip = _pickupClip;
            _pickup.volume = masterVol * combatVol * BgmMul * 0.95f;
            _pickup.PlayScheduled(t);
            for (int i = 0; i < 4; i++)
            {
                _cstems[i].Stop();
                _cw[i] = 0f;
                _cstems[i].PlayScheduled(t + bar);
            }
            _combatDownbeat = t + bar;
            _combatPlaying = true;
        }

        static float Move(float cur, float target, float dt, float rise, float fall)
        {
            return Mathf.MoveTowards(cur, target, (target > cur ? rise : fall) * dt);
        }

        /// Momentary BGM duck under a loud SFX (heavy hits, parries).
        public void Duck(float amount)
        {
            _duck = Mathf.Max(_duck, Mathf.Clamp01(amount));
        }

        static AudioClip MakeClip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, MusicGen.SR, false);
            c.SetData(data, 0);
            return c;
        }

        public void PlayVictory()
        {
            if (_stingClip == null) return;
            _oneShot.clip = _stingClip;
            _oneShot.volume = 0.5f * BgmMul;
            _oneShot.loop = false;
            _oneShot.Play();
        }
    }
}
