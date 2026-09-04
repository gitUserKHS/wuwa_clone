using System;
using UnityEngine;

namespace WuWa
{
    /// What the outgoing member's Outro buff does for the incoming member.
    public enum OutroType { DamageUp, SkillHaste, HeavyUp }

    [Serializable]
    public class AttackDef
    {
        public string state = "A1";        // animator state name
        public float dmgMul = 1f;          // multiplier on member baseAtk
        public float hitTime = 0.35f;      // normalized time into the clip when the hit lands
        public float clipLen = 0.8f;       // seconds (baked at build time)
        public float speed = 1.15f;        // animator playback speed for this attack
        public float range = 2.1f;         // forward offset of the hit sphere
        public float radius = 1.6f;        // hit sphere radius
        public float knockback = 3f;
        public float stagger = 12f;
        public float lunge = 2.2f;         // forward push while attacking (m/s impulse)
        public int vfx = 0;                // 0 slash, 1 heavy, 2 skill nova, 3 ult nova
    }

    /// Per-team-member data + runtime combat resource state.
    /// Lives on each character rig (the visual child that gets swapped).
    public class MemberConfig : MonoBehaviour
    {
        [Header("Identity")]
        public string charName = "Haru";
        public Element element = Element.Spectro;
        public Color themeColor = new Color(1f, 0.87f, 0.45f);
        public string portraitResource = "UI/portrait_0";

        [Header("Stats")]
        public float maxHp = 9000f;
        public float baseAtk = 110f;
        public float critChance = 0.18f;
        public float critMul = 2.0f;

        [Header("Attacks")]
        public AttackDef[] combo = new AttackDef[0];
        public AttackDef heavy = new AttackDef();
        public AttackDef skill = new AttackDef();
        public AttackDef ult = new AttackDef();
        public AttackDef plunge = new AttackDef { state = "Plunge", dmgMul = 2.2f, radius = 3.4f, hitTime = 0.99f, knockback = 6f, stagger = 26f, lunge = 0f, vfx = 2 };
        public AttackDef dashAtk = new AttackDef { state = "DashAtk", dmgMul = 1.6f, hitTime = 0.34f, range = 2.4f, radius = 2.0f, knockback = 4f, stagger = 16f, lunge = 5.5f, vfx = 0 };
        public AttackDef introSkill = new AttackDef { state = "IntroSkill", dmgMul = 3.6f, hitTime = 0.4f, radius = 5.0f, knockback = 7f, stagger = 40f, lunge = 0.5f, vfx = 2 };

        [Header("Resources")]
        public float skillCooldown = 7f;
        public float ultEnergyMax = 100f;

        [Header("Forte / Concerto (WuWa)")]
        public float forteMax = 100f;
        public float forteGainPerHit = 14f;
        public float concertoMax = 100f;
        public OutroType outroType = OutroType.DamageUp;
        public float outroBuffMul = 1.18f;      // meaning depends on outroType (dmg× / cd× / heavy×)
        public float outroBuffDur = 8f;

        [NonSerialized] public float hp = -1f;
        [NonSerialized] public float energy;
        [NonSerialized] public float skillCdLeft;
        [NonSerialized] public float forte;
        [NonSerialized] public float concerto;
        [NonSerialized] public float bonusAtk;      // weapon contribution (set by systems)
        [NonSerialized] public float statMul = 1f;  // party-level growth multiplier
        [NonSerialized] public float echoAtkFlat;   // rolled echo stats (pushed by EchoSystem)
        [NonSerialized] public float echoAtkPct;
        [NonSerialized] public float echoCritChance;
        [NonSerialized] public float echoCritMul;
        [NonSerialized] public float ascCritChance;  // 돌파 노드 (ProgressSystem.ApplyStats)
        [NonSerialized] public float ascAtkPct;
        [NonSerialized] public float ascCritMul;

        /// Effective attack used by all damage formulas.
        public float EffAtk { get { return (baseAtk + bonusAtk + echoAtkFlat) * (1f + echoAtkPct + ascAtkPct) * statMul; } }
        public float EffCrit { get { return critChance + echoCritChance + ascCritChance; } }
        public float EffCritMul { get { return critMul + echoCritMul + ascCritMul; } }

        Animator _anim;
        public Animator Anim { get { if (_anim == null) _anim = GetComponent<Animator>(); return _anim; } }

        void Awake()
        {
            if (hp < 0f) hp = maxHp;
        }

        public void TickResources(float dt)
        {
            if (skillCdLeft > 0f) skillCdLeft = Mathf.Max(0f, skillCdLeft - dt);
        }

        public void GainEnergy(float amt) { energy = Mathf.Min(ultEnergyMax, energy + amt); }
        public void GainForte(float amt) { forte = Mathf.Min(forteMax, forte + amt); }
        public void GainConcerto(float amt) { concerto = Mathf.Min(concertoMax, concerto + amt); }
        public bool UltReady { get { return energy >= ultEnergyMax - 0.01f; } }
        public bool ForteReady { get { return forte >= forteMax - 0.01f; } }
        public bool ConcertoReady { get { return concerto >= concertoMax - 0.01f; } }
    }
}
