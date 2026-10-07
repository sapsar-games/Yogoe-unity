using System;
using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.UI;

namespace Yoegoe.Characters
{
    /// <summary>
    /// 씬에 배치된 기물 하나.
    /// 공덕은 기물별 더미에 쌓이고, 탭으로 수거한다 (기획 7-1·7-2).
    /// 자원 기물(물·엽전·재료)은 주기마다 1개씩 보관에 쌓이고, 만창이면 생산·기력소모가 멈춘다 (Docs/00 §6-2).
    /// 미건립(자물쇠)은 걷기·생산·점유 대상이 아니다 (기획 8장).
    /// </summary>
    [DisallowMultipleComponent]
    public partial class PropSlot : MonoBehaviour
    {
        public PropData data;
        [Min(1)] public int level = 1;

        /// <summary>건립 여부. prebuilt면 시작 true, 자물쇠는 구매 후 true.</summary>
        public bool IsBuilt { get; private set; }

        /// <summary>v1.3 사냥터·채집터: 지금 보낸 목적지 id (시트 destinations). null 이면 예전 Hunt/Gather 표.</summary>
        public string DestinationId { get; set; }

        /// <summary>목적지가 있는 기물(사냥터·채집터)인지.</summary>
        public bool HasDestinations => ResourceType == PropResourceType.Hunt || ResourceType == PropResourceType.Gather;

        /// <summary>지금 목적지의 입장 친밀도 (없으면 0).</summary>
        public float RequiredIntimacy => PropCatalog.FindDestination(DestinationId)?.minIntimacy ?? 0f;

        public CharacterAgent Occupant { get; private set; }
        public bool IsOccupied => Occupant != null;

        public CharacterAgent ReservedBy { get; private set; }
        public bool IsReserved => ReservedBy != null;

        /// <summary>아직 수거하지 않은 기물 공덕 더미 (7-2).</summary>
        public BigNumber PendingMerit { get; private set; } = BigNumber.Zero;
        public bool HasPendingMerit => IsBuilt && PendingMerit.Mantissa != 0;

        PropStorage.State storage;
        /// <summary>활터·약초밭: 보관 중인 재료(1개당 1칸, 뽑힌 순서).</summary>
        readonly List<int> pendingIngredients = new List<int>();

        /// <summary>생산 설정 — 시트(props.json) 우선, 없으면 PropData. 규칙은 <see cref="PropProduction"/>.</summary>
        public PropProduction.Config ProductionConfig =>
            data != null ? PropProduction.Config.Resolve(data.propId, data) : default;

        public PropResourceType ResourceType => data != null ? ProductionConfig.Type : PropResourceType.None;
        public bool IsResourceProp => PropProduction.IsResource(ResourceType);
        public int StoredResources => storage.Stored;
        public int ResourceCapacity => data != null ? PropProduction.ResourceCapacity(ProductionConfig, level) : 0;
        public bool HasPendingResources => IsBuilt && IsResourceProp && storage.Stored > 0;
        /// <summary>탭 수거할 게 있는지 (공덕 더미 또는 자원 보관).</summary>
        public bool HasPendingCollectible => HasPendingMerit || HasPendingResources;
        public IReadOnlyList<int> PendingIngredients => pendingIngredients;

        /// <summary>보관 중에 황금쌀·황금꿀이 있으면 기물이 반짝인다 (기획 7-3).</summary>
        public bool HasGoldenPending
        {
            get
            {
                foreach (var code in pendingIngredients)
                    if (PropCatalog.IsSpecialCode(code)) return true;
                return false;
            }
        }

        /// <summary>황금 재료를 캐낸 요괴 — 수거 탭 때 이 요괴가 "발견했어!"를 말한다.</summary>
        CharacterAgent goldenFinder;
        bool sparkling;
        float petalTimer;
        float nextPetalAt = 20f;

        /// <summary>만창 — 앉아 있어도 생산·기력소모 정지.</summary>
        public bool IsStorageHalted =>
            IsBuilt && data != null
            && PropProduction.IsHalted(ProductionConfig, level, PendingMerit.ToDouble(), storage);

        public double MeritCapacity => data == null ? double.PositiveInfinity
            : PropProduction.MeritCapacity(ProductionConfig, level);

        private TextMesh pileLabel;
        private TextMesh lockLabel;
        private SpriteRenderer spriteRenderer;
        private Renderer meshRenderer;
        private Sprite builtSprite;
        private Sprite occupiedByOwnerSprite;
        private Color builtTint = Color.white;
        private static Font sharedPileFont;
        private CharacterAgent hiddenOccupantVisual;
        private int lastPileStage = -1;
        private string lastPileAmount;
        private int lastPileRounded = int.MinValue;
        private double lastPileDisplayKey = double.NaN;

        public event Action<PropSlot> OnBuilt;
        public event Action<PropSlot> OnLevelUp;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            meshRenderer = GetComponent<Renderer>();
        }

        private void OnEnable() => PropManager.Instance?.Register(this);
        private void OnDisable() => PropManager.Instance?.Unregister(this);

        private void LateUpdate()
        {
            RefreshPileLabel();
            RefreshLockVisual();
            RefreshGoldenSparkle();
            RefreshLevelTag();
        }

        /// <summary>Main 스폰 직후 호출. prebuilt면 즉시 건립.</summary>
        public void ConfigureBuiltState(bool built)
        {
            IsBuilt = built;
            if (built)
                ApplyBuiltVisual();
            else
                ApplyLockVisual();
        }

        public void Build()
        {
            if (IsBuilt) return;
            IsBuilt = true;
            level = Math.Max(1, level);
            ApplyBuiltVisual();
            OnBuilt?.Invoke(this);
        }

        public void NotifyLevelUp() => OnLevelUp?.Invoke(this);

        public bool TryReserve(CharacterAgent agent)
        {
            if (!IsBuilt) return false;
            if (IsOccupied) return false;
            if (IsReserved && ReservedBy != agent) return false;
            ReservedBy = agent;
            return true;
        }

        public void ReleaseReservation(CharacterAgent agent)
        {
            if (ReservedBy == agent) ReservedBy = null;
        }

        public bool TryOccupy(CharacterAgent agent)
        {
            if (!IsBuilt) return false;
            if (IsOccupied) return false;
            // 플레이어 드롭 등이 다른 요괴의 걷기 예약보다 우선
            if (IsReserved && ReservedBy != agent)
                ReservedBy = null;
            Occupant = agent;
            ReservedBy = null;
            RefreshOccupancyVisual();
            return true;
        }

        /// <summary>점유 해제. 기절 중에는 호출하지 않는다. 더미는 기물에 남는다.</summary>
        public void Vacate(CharacterAgent agent)
        {
            if (Occupant == agent)
            {
                Occupant = null;
                RefreshOccupancyVisual();
            }
        }

        /// <summary>세이브 복원용 건립/레벨.</summary>
        public void ApplySaveBuiltState(bool built, int savedLevel)
        {
            level = Mathf.Max(1, savedLevel);
            if (built && !IsBuilt)
                Build();
            else if (!built && IsBuilt)
            {
                IsBuilt = false;
                ApplyLockVisual();
            }
            else if (built)
                ApplyBuiltVisual();
        }

        /// <summary>세이브 복원 전 점유만 비운다 (더미는 유지).</summary>
        public void ClearOccupantForSaveRestore()
        {
            Occupant = null;
            ReservedBy = null;
            RefreshOccupancyVisual();
        }

        /// <summary>세이브 복원용 강제 점유.</summary>
        public void ForceOccupyForSaveRestore(CharacterAgent agent)
        {
            if (!IsBuilt) return;
            Occupant = agent;
            ReservedBy = null;
            RefreshOccupancyVisual();
        }

        /// <summary>자원 기물 수거 지점 통지 (propSlot, 종류, 개수). UI가 구독해 연출.</summary>
        public static event Action<PropSlot, PropResourceType, int> ResourcesCollected;

        /// <summary>
        /// 7-2: 기물 탭 수거. 더미를 비우고 플레이어 공덕(HUD)에 더한다.
        /// </summary>
        /// <summary>
        /// 기물 수거 연출 지점 통지. UI(GameHud)가 구독해서 실제 이펙트를 재생한다 —
        /// 이 클래스는 UI를 모른다.
        /// </summary>
        public static event System.Action<Vector3> MeritCollectedAtWorld;

        /// <summary>
        /// 요괴가 앉아 일할 수 있는 기물인지.
        /// 화덕은 탭→요리만 — 착석·드래그 ▼·자동 이동 후보에서 제외
        /// (opensGongyanggan 또는 산출 없음).
        /// </summary>
        public bool AcceptsWorkers
        {
            get
            {
                if (data != null && data.opensGongyanggan) return false;
                return ResourceType != PropResourceType.None;
            }
        }

        /// <summary>드래그 중인 요괴가 지금 바로 앉을 수 있는 기물인지 (금색 ▼ 마커).</summary>
        public bool CanSitNow(CharacterAgent agent) =>
            IsBuilt && !IsOccupied && AcceptsWorkers && CanBeUsedBy(agent);

        public bool CanBeUsedBy(CharacterAgent agent)
        {
            if (!IsBuilt) return false;
            if (data == null || agent == null || agent.Data == null) return true;
            if (data.isEndingProp && data.owner != agent.Data.id) return false;
            // v1.3: 사냥터·채집터는 지금 목적지의 입장 친밀도가 모자라면 못 간다
            if (HasDestinations && agent.Stats.Intimacy + 0.001f < RequiredIntimacy) return false;
            return true;
        }

        /// <summary>다른 요괴의 엔딩 기물(앉을 수 없음 → 옆 배치·거절 연출용).</summary>
        public bool IsForbiddenEndingFor(CharacterAgent agent)
        {
            if (!IsBuilt || data == null || agent == null || agent.Data == null) return false;
            return data.isEndingProp && data.owner != agent.Data.id;
        }

        public string DisplayName => data != null && !string.IsNullOrEmpty(data.displayName)
            ? data.displayName
            : name;

        int lastResourceStored = -1;
        int lastResourceCapacity = -1;
        bool lastResourceHalted;

        private void OnDestroy()
        {
            if (pileLabel != null)
            {
                Destroy(pileLabel.gameObject);
                pileLabel = null;
            }
            DestroyLevelTag();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = !IsBuilt ? Color.gray
                : IsOccupied ? new Color(1f, 0.5f, 0f)
                : IsReserved ? Color.yellow
                : Color.cyan;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
        }
#endif
    }
}
