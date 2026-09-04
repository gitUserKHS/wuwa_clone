using UnityEngine;

namespace WuWa
{
    /// Post-combat rank (GDD ch.4): tracks kills, hits taken, parries and intro
    /// skills across one engagement; when combat ends the fight is graded D~S on
    /// a rank card. S pays out a bonus echo drop, A grants shards. Also keeps the
    /// hit combo (consecutive hits without being hit, 2 s grace) for the HUD.
    public class CombatScore : MonoBehaviour
    {
        public static CombatScore I { get; private set; }
        public const float ComboGrace = 2f;

        bool _inCombat;
        float _exitHold;
        int _kills, _hitsTaken, _parries, _intros;
        float _startTime;
        int _combo, _bestCombo;
        float _lastHit = -99f;

        public static int Combo { get { return I != null ? I._combo : 0; } }
        public static bool InCombat { get { return I != null && I._inCombat; } }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public static void NotifyKill() { if (I != null && I._inCombat) I._kills++; }
        public static void NotifyParry() { ContentStats.Parries++; if (I != null && I._inCombat) I._parries++; }
        public static void NotifyIntro() { if (I != null && I._inCombat) I._intros++; }

        /// A player attack landed on an enemy: extend the combo.
        public static void NotifyHit()
        {
            if (I == null) return;
            I._combo++;
            I._lastHit = Time.time;
            if (I._combo > I._bestCombo) I._bestCombo = I._combo;
            HUDController.SetCombo(I._combo);
        }

        /// The player got hit: the combo breaks.
        public static void NotifyHitTaken()
        {
            if (I == null) return;
            if (I._inCombat) I._hitsTaken++;
            I.BreakCombo();
        }

        void BreakCombo()
        {
            if (_combo == 0) return;
            _combo = 0;
            HUDController.SetCombo(0);
        }

        void Update()
        {
            if (_combo > 0 && Time.time - _lastHit > ComboGrace) BreakCombo();

            bool aggro = false;
            var player = PlayerController.Instance;
            if (player != null)
            {
                for (int i = 0; i < EnemyAI.All.Count; i++)
                {
                    var e = EnemyAI.All[i];
                    if (e == null || e.Hp == null || !e.Hp.IsAlive || !e.gameObject.activeInHierarchy) continue;
                    if (WuWaUtil.Flat(e.transform.position - player.transform.position).magnitude < 22f && e.IsAggro)
                    {
                        aggro = true;
                        break;
                    }
                }
            }

            if (aggro)
            {
                if (!_inCombat)
                {
                    _inCombat = true;
                    _kills = 0; _hitsTaken = 0; _parries = 0; _intros = 0; _bestCombo = 0;
                    _startTime = Time.time;
                }
                _exitHold = 5f;
            }
            else if (_inCombat)
            {
                _exitHold -= Time.deltaTime;
                if (_exitHold <= 0f)
                {
                    _inCombat = false;
                    Grade(player);
                }
            }
        }

        /// Rank letter for a finished engagement (shared with the test harness).
        public static string RankFor(int kills, int hitsTaken, int parries, int intros)
        {
            int score = 45 + kills * 12 + parries * 10 + intros * 7 - hitsTaken * 9;
            if (hitsTaken == 0 && (parries > 0 || intros > 0 || kills >= 3)) return "S";
            if (score >= 85) return "A";
            if (score >= 60) return "B";
            if (score >= 35) return "C";
            return "D";
        }

        void Grade(PlayerController player)
        {
            if (_kills <= 0) return;                              // no clean fight, no grade
            Present(RankFor(_kills, _hitsTaken, _parries, _intros), _kills, _hitsTaken, _parries, _intros, _bestCombo, player);
        }

        /// Shows the rank card and pays the rank rewards (S: bonus echo + 30, A: +15).
        public static void Present(string rank, int kills, int hitsTaken, int parries, int intros, int bestCombo, PlayerController player)
        {
            Debug.Log("[WuWa] combat graded " + rank + ": kills=" + kills + " hits=" + hitsTaken + " parries=" + parries + " intros=" + intros + " combo=" + bestCombo);
            string detail = "처치 " + kills + " · 피격 " + hitsTaken + " · 패리 " + parries + " · 변주 " + intros + " · 최대 콤보 " + bestCombo;
            BountyBoard.NotifyRank(rank);
            string bonus = "";
            if (rank == "S")
            {
                ContentStats.RankS++;
                bonus = "보너스 에코  ·  조각소리 +30";
                if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(30);
                if (player != null)
                    EchoOrb.SpawnAt(player.transform.position + player.transform.forward * 2f, 2, EchoDB.IdForKind(EnemyKind.Ranged));
                AudioMan.I.Play2D(Sfx.PerfectDodge(), 0.7f, 1.1f);
            }
            else if (rank == "A")
            {
                bonus = "조각소리 +15";
                if (ProgressSystem.I != null) ProgressSystem.I.GrantShards(15);
                AudioMan.I.Play2D(Sfx.Absorb(), 0.5f, 1.2f);
            }
            HUDController.ShowRankCard(rank, detail, bonus);
        }
    }
}
