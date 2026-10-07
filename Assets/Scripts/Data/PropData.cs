using UnityEngine;

namespace Yoegoe.Data
{
        /// <summary>기물(맵 오브젝트) 정의 (기획서 7,8장). Assets/Data/Props/*.asset.
        /// 맵 비주얼·배치는 Prefabs/Props + Main 씬.</summary>
    [CreateAssetMenu(fileName = "PropData", menuName = "Yoegoe/Prop Data")]
    public class PropData : ScriptableObject
    {
        public string propId;
        public string displayName;
        public Sprite icon;

        [Header("산출 — Resources/props.json(구글 시트 props 탭)이 소스. 여기 값은 폴백")]
        public PropResourceType resourceType = PropResourceType.Merit;

        [Tooltip("공덕 기물: Lv1 분당 공덕. 분당 = base × levelGrowth^(L-1) (× 친밀도보정 × 주인보정)")]
        public double baseProductionPerMinute = 100;
        [Tooltip("공덕 기물: 레벨당 산출 배율")]
        public double levelGrowth = 1.1;
        [Tooltip("공덕 기물: 보관(만창) = 분당 산출 × 이 분")]
        public float meritCapacityMinutes = 30f;
        [Tooltip("공덕 기물: 친밀도 보정(1 + 친밀도/100) 적용 여부")]
        public bool intimacyBonus = true;
        [Tooltip("공덕 기물: 엔딩기물 주인이 앉았을 때 배율")]
        public double ownerMultiplier = 2.0;

        [Tooltip("자원 기물: 1개 산출 주기(분, 레벨 무관)")]
        public float cycleMinutes;
        [Tooltip("자원 기물: Lv1 기본 보관. Capacity(L) = base + floor(L/10)")]
        public int baseCapacity;

        [Tooltip("레벨업 가능 여부 (화덕은 false)")]
        public bool upgradable = true;

        [Header("업그레이드 비용 (8장): 500 * 1.15^(L-1), 레벨업마다 15% 증가")]
        public double upgradeBaseCost = 500;
        public double upgradeCostMultiplier = 1.15;

        [Tooltip("MVP 시작 시 이미 지어져 있는 기물인지 (돌탑/우물/떡절구). " +
                 "false면 빈 자리(자물쇠)로 시작하며, 최초 구매 비용은 800 * 1.4^(n-1) (n=구매 순서)로 별도 계산.")]
        public bool isPrebuilt;

        [Header("엔딩 기물 (8장): 지정된 캐릭터만 착석 가능, 주인이 앉으면 생산 x2")]
        public bool isEndingProp;
        public CharacterId owner;

        [Tooltip("떡절구처럼 전용 애니메이션이 있는지. 없으면 '자기 엔딩 기물 앞에서 기도하기'로 통일 (8장).")]
        public bool hasUniqueEndingAnimation;

        [Tooltip("주인 캐릭터가 점유 중일 때 기물에 표시할 전용 스프라이트 (예: 옥토끼+떡절구). 비우면 기본 기물 그림 유지.")]
        public Sprite occupiedByOwnerSprite;

        [Header("공양간")]
        [Tooltip("탭하면 공양간(요리) 화면을 연다. 화덕.")]
        public bool opensGongyanggan;

        [Tooltip("탭하면 나루터 화면을 연다 (v1.3). 요괴가 앉지 않는 시설.")]
        public bool opensPier;

        [Header("v1.3 제단")]
        [Tooltip("공덕을 버드나무로 보내지 않고 이 기물을 눌러 바로 받는다 (북제단·남제단).")]
        public bool collectByTap;

        [Tooltip("공덕 보관량이 레벨마다 몇 배로 늘어나는지 (제단 1.1). 시트 props capacityGrowth 가 덮어쓴다.")]
        public double capacityGrowth = 1.0;

        [Header("v1.3 목적지 (사냥·채집)")]
        [Tooltip("비어 있지 않으면 이 기물의 목적지가 고정된다 (시트 destinations id). 보내기 창에서 목적지 선택 없음.")]
        public string fixedDestinationId;
    }
}

