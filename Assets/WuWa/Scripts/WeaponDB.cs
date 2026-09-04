using UnityEngine;

namespace WuWa
{
    public enum WeaponPassive { None, SkillDmgPct, ConcertoGainPct, CritRatePct }

    [System.Serializable]
    public class WeaponDef
    {
        public int id;
        public string name;
        public int tier;            // 1..3
        public float atk;
        public WeaponPassive passive;
        public float passiveValue;
        public string lore;

        public Color Tint
        {
            get
            {
                switch (tier)
                {
                    case 1: return new Color(0.68f, 0.70f, 0.72f);
                    case 2: return new Color(0.35f, 0.75f, 0.80f);
                    case 3: return new Color(0.95f, 0.75f, 0.25f);
                    default: return Color.white;
                }
            }
        }

        public string PassiveText
        {
            get
            {
                switch (passive)
                {
                    case WeaponPassive.SkillDmgPct: return "스킬 피해 +" + passiveValue + "%";
                    case WeaponPassive.ConcertoGainPct: return "협주 에너지 획득 +" + passiveValue + "%";
                    case WeaponPassive.CritRatePct: return "크리티컬 확률 +" + passiveValue + "%";
                    default: return "패시브 없음";
                }
            }
        }
    }

    /// Demo weapon catalogue — one sword line, three tiers (GDD ch.5).
    public static class WeaponDB
    {
        public static readonly WeaponDef[] All =
        {
            new WeaponDef{ id=0, name="연습검", tier=1, atk=12f, passive=WeaponPassive.None, passiveValue=0f,
                lore="탑 아래 훈련장에서 쓰이던 검. 낡았지만 손에 익는다." },
            new WeaponDef{ id=1, name="조율검", tier=2, atk=28f, passive=WeaponPassive.SkillDmgPct, passiveValue=10f,
                lore="조율사 공방에서 벼려낸 표준 지급품. 칼날이 낮게 공명한다." },
            new WeaponDef{ id=2, name="잔향검 · 명기", tier=3, atk=50f, passive=WeaponPassive.ConcertoGainPct, passiveValue=25f,
                lore="첫 번째 노래의 파편을 벼려 넣은 명기. 휘두를 때마다 화음이 남는다." },
        };

        public static WeaponDef Get(int id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
            return null;
        }
    }
}
