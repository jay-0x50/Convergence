using System.Collections.Generic;

namespace Convergence.Duel
{
    /// <summary>
    /// Temporary balance and reference names for the first playable prototype.
    /// Series, archetype and element are descriptive metadata, never deck restrictions.
    /// Every call returns fresh collections so editing a preset does not change another deck.
    /// </summary>
    public static class SampleContent
    {
        public static IReadOnlyList<CardDefinition> CreateCatalog()
        {
            return new List<CardDefinition>
            {
                new CardDefinition("march7", "Mar. 7th", "붕괴: 스타레일", "개척", "얼음",
                    CardKind.Character, attack: 1400, maxHealth: 2400,
                    description: "높은 체력으로 전선을 지키는 캐릭터. 프로토타입: 고유 효과 없음."),
                new CardDefinition("dan_heng", "단항", "붕괴: 스타레일", "개척", "바람",
                    CardKind.Character, attack: 1900, maxHealth: 1600,
                    description: "공격에 특화된 캐릭터. 프로토타입: 고유 효과 없음."),
                new CardDefinition("trailblazer", "개척자", "붕괴: 스타레일", "개척", "물리",
                    CardKind.Character, attack: 1700, maxHealth: 2100,
                    description: "공격과 생존을 겸비한 캐릭터. 프로토타입: 고유 효과 없음."),
                new CardDefinition("himeko", "히메코", "붕괴: 스타레일", "개척", "불",
                    CardKind.Character, attack: 2000, maxHealth: 1400,
                    description: "강한 공격력과 낮은 체력을 가진 캐릭터. 프로토타입: 고유 효과 없음."),
                new CardDefinition("amber", "엠버", "원신", "몬드", "불",
                    CardKind.Character, attack: 1600, maxHealth: 1900,
                    description: "전개와 전투의 기본이 되는 캐릭터. 프로토타입: 고유 효과 없음."),
                new CardDefinition("kaeya", "케이아", "원신", "몬드", "얼음",
                    CardKind.Character, attack: 1800, maxHealth: 1800,
                    description: "균형 잡힌 전투형 캐릭터. 프로토타입: 고유 효과 없음."),
                new CardDefinition("noelle", "노엘", "원신", "몬드", "바위",
                    CardKind.Character, attack: 1200, maxHealth: 2600,
                    description: "방어적인 전개에 적합한 캐릭터. 프로토타입: 고유 효과 없음."),
                new CardDefinition("lisa", "리사", "원신", "몬드", "번개",
                    CardKind.Character, attack: 1900, maxHealth: 1500,
                    description: "지원 명령과 함께 쓰기 좋은 공격형 캐릭터. 프로토타입: 고유 효과 없음."),

                new CardDefinition("shared_notes", "공유하는 작전 노트", "컨버전스", "공용 지원", "무속성",
                    CardKind.Command, effect: EffectKind.Draw, amount: 1,
                    description: "카드를 1장 드로우한다."),
                new CardDefinition("deep_insight", "전황 분석", "컨버전스", "공용 지원", "무속성",
                    CardKind.Command, effect: EffectKind.Draw, amount: 2,
                    description: "카드를 2장 드로우한다. 초기 전개 실험용 카드."),
                new CardDefinition("focused_strike", "집중 포격", "컨버전스", "공용 지원", "물리",
                    CardKind.Command, effect: EffectKind.DamageUnit, amount: 800,
                    target: TargetKind.EnemyUnit, description: "상대 필드의 유닛 1체에 피해 800을 준다."),
                new CardDefinition("falling_star", "낙하하는 별", "컨버전스", "공용 지원", "불",
                    CardKind.Command, effect: EffectKind.DamageUnit, amount: 1400,
                    target: TargetKind.EnemyUnit, description: "상대 필드의 유닛 1체에 피해 1400을 준다."),
                new CardDefinition("rally", "함께하는 진격", "컨버전스", "공용 지원", "무속성",
                    CardKind.Command, effect: EffectKind.BoostUnit, amount: 500,
                    target: TargetKind.FriendlyUnit, description: "아군 필드의 유닛 1체의 공격력을 500 올린다."),
                new CardDefinition("crossworld_relay", "차원을 잇는 합류", "컨버전스", "공용 지원", "무속성",
                    CardKind.Command, effect: EffectKind.SpecialSummonFromHand,
                    target: TargetKind.HandCharacter,
                    description: "패의 캐릭터 1체를 특수 소환한다. 일반 소환 횟수를 사용하지 않는다."),
                new CardDefinition("second_chance", "다시 만나는 동료", "컨버전스", "공용 지원", "무속성",
                    CardKind.Command, effect: EffectKind.Revive,
                    target: TargetKind.GraveyardCharacter, description: "자신의 묘지에 있는 캐릭터 1체를 부활시킨다."),
                new CardDefinition("memory_echo", "기억의 잔향", "컨버전스", "기억", "무속성",
                    CardKind.Command, effect: EffectKind.CreateToken, amount: 1,
                    description: "기억의 파편 토큰 1체를 소환한다. 공격력 1000 / 체력 1400."),
                new CardDefinition("safe_haven", "잠깐의 쉼터", "컨버전스", "공용 지원", "무속성",
                    CardKind.Command, effect: EffectKind.HealPlayer, amount: 1000,
                    description: "자신의 라이프를 1000 회복한다."),

                new CardDefinition("convergence_field", "교차하는 세계", "컨버전스", "공용 필드", "무속성",
                    CardKind.Domain, effect: EffectKind.FieldAura, amount: 300,
                    description: "자신의 필드 유닛 공격력이 300 오른다. 나중에 소환한 유닛에도 적용된다. 자신의 필드 카드는 1장만 유지할 수 있다."),

                new CardDefinition("disrupt", "작전 교란", "컨버전스", "공용 대응", "무속성",
                    CardKind.Intercept, effect: EffectKind.Negate,
                    description: "직전 체인 링크의 효과 또는 공격을 무효로 한다."),
                new CardDefinition("hold_the_line", "전선 사수", "컨버전스", "공용 대응", "무속성",
                    CardKind.Intercept, effect: EffectKind.Negate,
                    description: "공격에 대응하여 그 공격을 무효로 한다.",
                    trigger: InterceptTrigger.AttackOnly),
                new CardDefinition("ambush", "차원 매복", "컨버전스", "공용 대응", "무속성",
                    CardKind.Intercept, effect: EffectKind.DestroyUnit,
                    target: TargetKind.EnemyUnit, description: "상대 필드의 유닛 1체를 파괴한다."),

                new CardDefinition("token_memory", "기억의 파편", "컨버전스", "기억", "무속성",
                    CardKind.Token, attack: 1000, maxHealth: 1400,
                    description: "효과로만 소환되는 토큰. 덱에 넣을 수 없다.")
            }.AsReadOnly();
        }

        // Both 28-card presets deliberately mix characters from different series.
        public static List<string> CreateStarterDeck()
        {
            return new List<string>
            {
                "march7", "march7", "dan_heng", "dan_heng", "trailblazer", "trailblazer", "himeko",
                "amber", "amber", "kaeya", "kaeya", "noelle", "noelle", "lisa",
                "shared_notes", "shared_notes", "focused_strike", "focused_strike", "falling_star",
                "rally", "crossworld_relay", "crossworld_relay", "second_chance", "memory_echo", "safe_haven",
                "disrupt", "hold_the_line", "ambush"
            };
        }

        public static List<string> CreateOpponentDeck()
        {
            return new List<string>
            {
                "march7", "dan_heng", "dan_heng", "trailblazer", "trailblazer", "himeko", "himeko",
                "amber", "kaeya", "kaeya", "noelle", "noelle", "lisa", "lisa",
                "shared_notes", "deep_insight", "focused_strike", "falling_star", "rally", "rally",
                "crossworld_relay", "crossworld_relay", "second_chance", "memory_echo", "safe_haven",
                "disrupt", "hold_the_line", "ambush"
            };
        }
    }
}
