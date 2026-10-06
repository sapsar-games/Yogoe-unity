namespace Yoegoe.Data
{
    /// <summary>행동 상태 5종 (기획서 6-2). 룰: Docs/06_행동룰.md</summary>
    // 번호는 세이브에 정수로 저장되므로 고정 (2 = 삭제된 주저앉기)
    public enum ActionState { Walking = 0, Staying = 1, Fainted = 3, Playing = 4 }

    /// <summary>기물 산출물 (Docs/00 §6-1). 시트 props.resourceType.</summary>
    public enum PropResourceType { None = 0, Merit = 1, Water = 2, Hunt = 4, Gather = 5 } // 3 = 옛 엽전(갯바위, v1.2 삭제)

    /// <summary>요리 재료가 아닌 특수 수집품 (활터·약초밭 1% — 기획 7-3). 지금은 인벤에 쌓기만.</summary>
    public enum SpecialItemId { GoldenRice, GoldenHoney }

    public enum CharacterId { Rabbit, SamjokO, Gumiho, Gorani } // 옥토끼, 삼족오, 구미호, 고라니

    /// <summary>공양/음식 종류. Preferred는 캐릭터 선호 판정용(데이터 kind로는 거의 안 씀).</summary>
    public enum OfferingKind { General, Preferred, Water, Food } // 공양물, (선호표시), 물, 음식
}
