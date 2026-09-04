using UnityEngine;

namespace WuWa
{
    public enum ItemCategory { Material, Stone, Consumable, KeyItem }
    public enum ItemEffect { None, Heal, AtkBuff, DefBuff, StaminaBuff }

    public class ItemDef
    {
        public int id;
        public string name, desc, source, usage;
        public ItemCategory cat;
        public int star = 1;
        public int stackCap = 999;
        public int element = -1;          // 0 회절 1 응결 2 용융
        public int expValue;              // stones: character/weapon EXP (S5)
        public int price;                 // shop buy price (0 = not stocked)
        public int sell;                  // sell value (bag = same as shop here)
        public ItemEffect effect;
        public float effectValue, effectDur;
        public string icon = "dot";
        public Color Tint { get { return UIKit.Theme.Rarity(star); } }
    }

    /// The 15 approved item kinds (design doc ch.5 with 공명석/연마석 merged into one EXP series).
    public static class ItemDB
    {
        public const int Residue0 = 100, Residue1 = 101, Residue2 = 102;
        public const int Crystal0 = 110, Crystal1 = 111, Crystal2 = 112;
        public const int Crown = 120;
        public const int Stone0 = 130, Stone1 = 131, Stone2 = 132;
        public const int Tuner = 150;
        public const int Flask = 200, FoodAtk = 201, FoodDef = 202, StaminaPotion = 203;

        public static readonly ItemDef[] All =
        {
            new ItemDef { id = Residue0, name = "흐린 잔재", cat = ItemCategory.Material, star = 1, sell = 5, icon = "shard",
                desc = "그림자가 흩어진 뒤 남는 옅은 소리의 찌꺼기.", source = "잡몹 처치 · 나무 상자 · 군락", usage = "돌파 I · 스킬 Lv2 · 무기 돌파1 · 변환(10 → 짙은 1)" },
            new ItemDef { id = Residue1, name = "짙은 잔재", cat = ItemCategory.Material, star = 2, sell = 20, icon = "shard",
                desc = "정예 그림자의 몸에서 굳은 잔재. 만지면 낮게 울린다.", source = "주술사·거암 처치 · 시련 · 균열 · 은빛 상자", usage = "돌파 II/III · 스킬 Lv3/4 · 무기 돌파2/3" },
            new ItemDef { id = Residue2, name = "검은 잔재", cat = ItemCategory.Material, star = 3, sell = 60, icon = "shard",
                desc = "노래를 삼킨 어둠의 응결체. 보스급 그림자만이 남긴다.", source = "보스 · 시련 완주 · 균열 정예", usage = "돌파 II · 스킬 Lv5" },
            new ItemDef { id = Crystal0, name = "회절 결정", cat = ItemCategory.Material, star = 3, element = 0, sell = 30, price = 60, icon = "crystal",
                desc = "빛이 갈라지며 맺힌 결정. 리라에의 속성과 공명한다.", source = "녹야 평원 · 노을빛 언덕 · 보스 · 상점(일 3)", usage = "리라에 돌파 · 스킬 · 무기 돌파2" },
            new ItemDef { id = Crystal1, name = "응결 결정", cat = ItemCategory.Material, star = 3, element = 1, sell = 30, price = 60, icon = "crystal",
                desc = "서리의 숨결이 굳은 결정. 세레네의 속성과 공명한다.", source = "거울 호수 · 서리 고원 · 상점(일 3)", usage = "세레네 돌파 · 스킬 · 무기 돌파2" },
            new ItemDef { id = Crystal2, name = "용융 결정", cat = ItemCategory.Material, star = 3, element = 2, sell = 30, price = 60, icon = "crystal",
                desc = "잿불 속에서 녹아내린 결정. 에이리스의 속성과 공명한다.", source = "잿빛 황무지 · 노래잃은 도시 · 상점(일 3)", usage = "에이리스 돌파 · 스킬 · 무기 돌파2" },
            new ItemDef { id = Crown, name = "무관의 왕관 파편", cat = ItemCategory.Material, star = 5, stackCap = 99, sell = 200, icon = "crown",
                desc = "무관의 그림자가 쓰던 왕관의 조각. 노래의 첫 박자가 새겨져 있다.", source = "보스 · 시련 완주 · 3장 완료", usage = "돌파 III · 무기 돌파3 · 스킬 Lv5" },
            new ItemDef { id = Stone0, name = "공명석 조각", cat = ItemCategory.Stone, star = 1, expValue = 150, sell = 15, icon = "stone",
                desc = "소리가 조금 스며든 돌조각.", source = "나무 상자 · 퀘스트", usage = "캐릭터·무기 EXP 150" },
            new ItemDef { id = Stone1, name = "공명석", cat = ItemCategory.Stone, star = 2, expValue = 600, sell = 60, price = 120, icon = "stone",
                desc = "탑의 진동을 머금은 돌. 손에 쥐면 따뜻하다.", source = "은빛 상자 · 현상 · 상점(일 5)", usage = "캐릭터·무기 EXP 600" },
            new ItemDef { id = Stone2, name = "공명 결정", cat = ItemCategory.Stone, star = 3, expValue = 2000, sell = 200, icon = "stone",
                desc = "노래 한 소절이 통째로 갇힌 결정.", source = "황금 상자 · 시련 완주 · 3장", usage = "캐릭터·무기 EXP 2,000" },
            new ItemDef { id = Tuner, name = "조율기", cat = ItemCategory.Material, star = 4, sell = 144, price = 180, icon = "tuner",
                desc = "에코의 숨은 부옵을 깨우는 소리굽쇠.", source = "은빛·황금 상자 · 보스 · 균열 · 시련 · 상점(일 2)", usage = "에코 부옵 개방 · 재조율" },
            new ItemDef { id = Flask, name = "공명의 물약", cat = ItemCategory.Consumable, star = 4, stackCap = 3, sell = 0, icon = "flask", effect = ItemEffect.Heal, effectValue = 0.35f, effectDur = 1.2f,
                desc = "1.2초 시전 후 활성 캐릭터 HP 35%, 나머지 10% 회복. 피격 시 취소. 표석·공명탑·리스폰에서 충전.", source = "시작 지급 · 표석/탑 충전", usage = Glyph_X + " 키" },
            new ItemDef { id = FoodAtk, name = "노래풀 구이", cat = ItemCategory.Consumable, star = 2, stackCap = 20, sell = 32, price = 40, icon = "food", effect = ItemEffect.AtkBuff, effectValue = 0.12f, effectDur = 300f,
                desc = "노래풀을 얹어 구운 향긋한 요리. 5분 동안 공격력 +12%.", source = "상점 40 · 군락 채집", usage = "퀵슬롯(Z) 또는 가방에서 사용" },
            new ItemDef { id = FoodDef, name = "강철껍질 조림", cat = ItemCategory.Consumable, star = 2, stackCap = 20, sell = 32, price = 40, icon = "food", effect = ItemEffect.DefBuff, effectValue = 0.15f, effectDur = 300f,
                desc = "거암의 껍질을 오래 조린 국물. 5분 동안 받는 피해 −15%.", source = "상점 40 · 현상", usage = "퀵슬롯(Z) 또는 가방에서 사용" },
            new ItemDef { id = StaminaPotion, name = "질주의 물약", cat = ItemCategory.Consumable, star = 3, stackCap = 10, sell = 48, price = 60, icon = "potion", effect = ItemEffect.StaminaBuff, effectValue = 0.5f, effectDur = 90f,
                desc = "90초 동안 스태미나 소모 −50%, 회복 +50%.", source = "상점 60(일 3) · 은빛 상자", usage = "퀵슬롯(Z) 또는 가방에서 사용" },
        };

        const string Glyph_X = "물약";

        public static ItemDef Get(int id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
            return null;
        }

        public static int CrystalFor(int element) { return Crystal0 + Mathf.Clamp(element, 0, 2); }

        public static string ElementName(int e) { return e == 0 ? "회절" : e == 1 ? "응결" : e == 2 ? "용융" : "-"; }

        public static string CategoryName(ItemCategory c)
        {
            switch (c)
            {
                case ItemCategory.Material: return "재료";
                case ItemCategory.Stone: return "강화석";
                case ItemCategory.Consumable: return "소모품";
                default: return "귀중품";
            }
        }
    }
}
