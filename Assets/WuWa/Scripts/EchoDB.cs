using UnityEngine;

namespace WuWa
{
    public enum EchoFamily { Shadow, Guard }
    public enum EchoPassive { AtkPct, MoveSpeedPct, SkillDmgPct, DamageReductionPct, AllElemPct }

    [System.Serializable]
    public class EchoDef
    {
        public int id;
        public string name;
        public int star;
        public int cost;
        public EchoFamily family;
        public string activeName;
        public string activeDesc;
        public EchoPassive passive;
        public float passiveValue;      // percent
        public string lore;
        public Color Tint
        {
            get
            {
                switch (star)
                {
                    case 1: return new Color(0.62f, 0.65f, 0.62f);
                    case 3: return new Color(0.25f, 0.62f, 0.72f);
                    case 5: return new Color(0.85f, 0.65f, 0.2f);
                    default: return Color.white;
                }
            }
        }
    }

    /// Static echo catalogue (demo scope: 5 entries, see GDD ch.6).
    public static class EchoDB
    {
        public static readonly EchoDef[] All =
        {
            new EchoDef{ id=0, name="그림자 방랑자", star=1, cost=1, family=EchoFamily.Shadow,
                activeName="그림자 할퀴기", activeDesc="전방 3연격 (각 1.2×)",
                passive=EchoPassive.AtkPct, passiveValue=4f,
                lore="그림자가 되기 전, 그것은 떠돌이 악사의 발소리였다." },
            new EchoDef{ id=1, name="질풍의 그림자", star=1, cost=1, family=EchoFamily.Shadow,
                activeName="질풍 가르기", activeDesc="6m 관통 돌진 (1.8×)",
                passive=EchoPassive.MoveSpeedPct, passiveValue=6f,
                lore="숲을 달리던 파발꾼의 숨소리. 아직도 어딘가로 달리고 있다." },
            new EchoDef{ id=2, name="주술사의 그림자", star=3, cost=3, family=EchoFamily.Shadow,
                activeName="그림자 화살 ×3", activeDesc="유도 화살 3발 (각 1.4×)",
                passive=EchoPassive.SkillDmgPct, passiveValue=8f,
                lore="기우제의 주문 소리. 비 대신 어둠이 내렸다." },
            new EchoDef{ id=3, name="거암의 그림자", star=3, cost=3, family=EchoFamily.Guard,
                activeName="대지 강타", activeDesc="광역 넉업 + 강한 그로기 (2.6×)",
                passive=EchoPassive.DamageReductionPct, passiveValue=6f,
                lore="채석장 정 소리의 잔재. 무너진 산을 아직 지고 있다." },
            new EchoDef{ id=4, name="무관의 그림자", star=5, cost=4, family=EchoFamily.Guard,
                activeName="무관의 군림", activeDesc="광폭 이중 충격파 (4.5×)",
                passive=EchoPassive.AllElemPct, passiveValue=10f,
                lore="첫 번째 노래의 첫 소절 — 왕관 없이 군림하던 서곡." },
            new EchoDef{ id=5, name="잿불의 그림자", star=5, cost=4, family=EchoFamily.Shadow,
                activeName="잿불의 군림", activeDesc="광폭 이중 충격파 (4.5×) — 잿불 판",
                passive=EchoPassive.AllElemPct, passiveValue=10f,
                lore="불타버린 도시가 마지막으로 부른 화음. 재 속에서도 박자는 남는다." },
        };

        public static EchoDef Get(int id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
            return null;
        }

        /// Which echo an enemy kind can drop.
        public static int IdForKind(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Melee: return 0;
                case EnemyKind.Fast: return 1;
                case EnemyKind.Ranged: return 2;
                case EnemyKind.Tank: return 3;
                case EnemyKind.Boss: return 4;
                default: return 0;
            }
        }

        public static float DropChance(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Melee:
                case EnemyKind.Fast: return 0.2f;
                default: return 1f;      // elites & boss always drop
            }
        }
    }
}
