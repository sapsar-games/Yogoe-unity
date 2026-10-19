using UnityEngine;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Save;

namespace Yoegoe.Characters
{
    /// <summary>
    /// 캐릭터 행동 상태머신: 걷기 → 머물기(생산) → [기력 0] 놀기/쉬기 → [18시간] 기절.
    ///
    /// 에셋(스프라이트) 없이도 완전히 동작한다 — 이동은 Transform.position만 사용하고,
    /// SpriteRenderer는 있으면 참조만 해두는 정도. 씬에는 빈 GameObject에 이 스크립트 붙이고
    /// 위치만 잡아두면 테스트 가능 (Scene 뷰 Gizmo: 초록=걷기/파랑=머물기/분홍=놀기/빨강=기절).
    ///
    /// 구현은 관심사별로 여러 파일에 나뉜 partial class다 (이 파일은 라이프사이클/공개 API만):
    /// <see cref="CharacterAgent"/>.Movement.cs(걷기/머물기/놀기 상태머신),
    /// .Drag.cs(플레이어 드래그), .Animation.cs(스프라이트/방향), .Dialogue.cs(혼잣말·임시 대사).
    /// </summary>
    public partial class CharacterAgent : MonoBehaviour
    {
        public CharacterData Data;
        public CharacterRuntimeStats Stats = new CharacterRuntimeStats();

        [Header("이동 (에셋 없어도 동작)")]
        public float moveSpeed = 1.0f / 3f;
        [SerializeField] private SpriteRenderer spriteRenderer; // 없어도 무방, 나중에 아트 연결용

        private static readonly System.Collections.Generic.List<CharacterAgent> ActiveAgents
            = new System.Collections.Generic.List<CharacterAgent>();

        /// <summary>지금 씬에 존재하는 모든 캐릭터 (슬롯바 UI 등에서 순회용). 절대 수정하지 말 것.</summary>
        public static System.Collections.Generic.IReadOnlyList<CharacterAgent> All => ActiveAgents;

        /// <summary>플레이어가 드래그 중이면 AI 틱을 멈춘다.</summary>
        public bool IsBeingDragged { get; private set; }

        /// <summary>
        /// 윷 복귀 만세 연출 등 — true면 걷기/머물기/놀기 틱(생산·기력)을 멈춘다.
        /// 상태는 유지한 채 타이머만 정지.
        /// </summary>
        public bool BehaviorPaused { get; private set; }

        /// <summary>기절·행동 일시정지는 드래그 불가.</summary>
        public bool CanBeDraggedByPlayer =>
            !BehaviorPaused
            && Stats.State != ActionState.Fainted;

        /// <summary>머물기/걷기 등 AI 틱 일시정지·재개.</summary>
        public void SetBehaviorPaused(bool paused) => BehaviorPaused = paused;

        /// <summary>소환·세이브 복원이 Start 기본 스탯을 덮어쓰지 않게 막는다.</summary>
        private bool statsAppliedExternally;

        CharacterRequestState requests;
        public CharacterRequestState Requests => requests ?? (requests = new CharacterRequestState(this));
        public bool HasOfferingRequest => Requests.HasOfferingRequest;

        /// <summary>이보다 긴 dt는 이동·일반 틱에 쓰지 않는다 (프레임 스파이크 방지).</summary>
        private const float MaxContinuousMoveDelta = 0.25f;

        private ArtScaleSettings _artScale;
        private ArtScaleSettings ArtScale =>
            _artScale != null ? _artScale : (_artScale = Resources.Load<ArtScaleSettings>("ArtScaleSettings"));

        private void Awake()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            lastPosition = transform.position;
        }

        private void OnEnable() => ActiveAgents.Add(this);
        private void OnDisable()
        {
            ActiveAgents.Remove(this);
            if (IsBeingDragged) PropDragMarkers.Hide();
            if (dropMarkRenderer != null) Destroy(dropMarkRenderer.gameObject);
            if (bubbleBg != null) Destroy(bubbleBg.gameObject);
            if (bubbleTextMesh != null) Destroy(bubbleTextMesh.gameObject);
            requests?.DestroyVisuals();
        }

        private void Start()
        {
            // Data 초기화는 Awake가 아니라 Start에서 한다: 코드로 캐릭터를 생성할 때
            // AddComponent<CharacterAgent>() 직후에 Data를 대입하는 패턴(Main 등)이
            // 흔한데, AddComponent가 Awake를 즉시 실행시키기 때문에 Awake 시점엔 Data가 아직
            // null이라 초기화가 안 먹는 버그가 있었다. Start는 모든 오브젝트의 Awake가 끝난
            // 다음 프레임 이전에 실행되므로 이 시점엔 Data가 확실히 채워져 있다.
            if (!statsAppliedExternally && Data != null)
                ApplyDefaultStatsFromData();

            EnterWalking();

            // 로딩 직후 모든 캐릭터가 동시에 혼잣말을 시작하지 않도록 첫 대사까지 약간의 랜덤 지연을 둔다.
            monologueTimer = Random.Range(2f, MonologueMinInterval);

            // AddComponent 직후 Data가 늦게 붙는 패턴 대비 — 첫 프레임부터 walk/idle 스프라이트 적용
            lastPosition = transform.position;
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            ApplyAnimationFrameImmediate();
        }

        private void ApplyDefaultStatsFromData()
        {
            var start = StartingStateSettings.Get();
            // 옥토끼는 NPC화(v1.3) — 친밀도 100 고정(바뀌는 경로 없음: 먹이기·윷 모두 제외).
            Stats.Intimacy = IsRabbit ? 100f : start.startingIntimacy;
            // 최대·시작 기력 = 25 + 친밀도
            Stats.Stamina = MaxStaminaFromIntimacy(Stats.Intimacy);
        }

        bool IsRabbit => Data != null && Data.id == CharacterId.Rabbit;

        /// <summary>소환 직후 — 친밀도 0, 기력 1 (Docs/00 §2·§9).</summary>
        public void ApplyFreshSummon()
        {
            statsAppliedExternally = true;
            Stats.Intimacy = 0f;
            Stats.Stamina = 1f;
            Stats.State = ActionState.Walking;
            Stats.StateTimer = 0f;
            lastPosition = transform.position;
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            EnterWalking();
            ApplyAnimationFrameImmediate();
        }

        /// <summary>최대 기력 = 25 + 친밀도.</summary>
        public float MaxStamina => MaxStaminaFromIntimacy(Stats.Intimacy);

        public static float MaxStaminaFromIntimacy(float intimacy) => 25f + Mathf.Clamp(intimacy, 0f, 100f);

        /// <summary>세이브 복원·소환 직후 Start가 스탯을 리셋하지 않도록 표시.</summary>
        public void MarkStatsAppliedExternally() => statsAppliedExternally = true;

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, MaxContinuousMoveDelta);
            Requests.Tick(dt);
            UpdateGoldenTint();

            // 소환 연출 중 맵 AI 정지
            if (CeremonyGate.BlocksWorldInput)
            {
                UpdateSortingOrder();
                return;
            }

            if (IsBeingDragged)
            {
                UpdateSortingOrder();
                return;
            }

            if (BehaviorPaused)
            {
                UpdateSortingOrder();
                return;
            }

            if (showingDropMark)
            {
                TickDropMark(dt);
                UpdateSortingOrder();
                return;
            }

            // 긴 공백 정산은 Main의 벽시계 CatchUpAll이 담당한다.
            // deltaTime에 의존하면 WebGL 탭 복귀 시 스파이크가 안 오거나, 오면 이동이 텔레포트한다.

            switch (Stats.State)
            {
                case ActionState.Walking: TickWalking(dt); break;
                case ActionState.Staying: TickStaying(dt); break;
                case ActionState.Fainted: /* 외부(공양)에서만 깨어남 */ break;
                case ActionState.Playing: TickPlaying(dt); break;
            }

            UpdateWalkAnimation(dt);
            UpdateSortingOrder();
            UpdateMonologue(dt);
        }

        /// <summary>
        /// 탭/앱이 다시 살아났을 때 벽시계로 잰 공백을 전 캐릭터에 반영한다.
        /// 기력·공덕·상태만 따라잡고, 걷기는 위치를 유지한 채 복귀 후 정상 속도로 이어간다.
        /// </summary>
        public static void CatchUpAll(float seconds)
        {
            if (seconds <= 0.001f) return;
            float capped = Mathf.Min(seconds, OfflineSimulator.MaxOfflineSeconds);
            for (int i = 0; i < ActiveAgents.Count; i++)
            {
                var agent = ActiveAgents[i];
                if (agent != null) agent.CatchUpWallClock(capped);
            }
        }

        private void CatchUpWallClock(float remaining)
        {
            if (BehaviorPaused) return;

            int guard = 0;
            while (remaining > 0.0001f && guard++ < 256)
            {
                switch (Stats.State)
                {
                    case ActionState.Staying:
                        remaining -= TickStayingSlice(remaining);
                        break;
                    case ActionState.Playing:
                        remaining -= TickPlayingSlice(remaining);
                        break;
                    case ActionState.Walking:
                        // 오프라인과 동일: 걷는 동안 생산 없음. 순간이동하지 않고 남은 공백은 폐기.
                        EnsureWalkingDestination();
                        return;
                    case ActionState.Fainted:
                        return;
                    default:
                        return;
                }
            }
        }

        /// <summary>Y 기준 sortingOrder — ArtScaleSettings.asset 의 characterSortBase / ySortMultiplier.</summary>
        private void UpdateSortingOrder()
        {
            if (spriteRenderer == null) return;
            var s = ArtScale;
            if (s == null) return;
            spriteRenderer.sortingOrder = s.SortOrderForCharacter(transform.position.y);
        }

        // ---------------- 세이브 복원 ----------------

        /// <summary>오프라인 시뮬 결과를 월드에 붙일 때 호출. 점유 기물이 있으면 강제 앉힌다.</summary>
        public void ApplySaveSnapshot(
            float intimacy,
            float stamina,
            ActionState state,
            float stateTimer,
            Vector3 worldPos,
            PropSlot occupyProp)
        {
            statsAppliedExternally = true;
            ClearWalkDestination();
            if (currentProp != null)
            {
                currentProp.Vacate(this);
                currentProp = null;
            }

            // 옥토끼는 NPC화(v1.3) — 옛 세이브에 남은 값과 상관없이 매번 100으로 되돌린다.
            Stats.Intimacy = IsRabbit ? 100f : intimacy;
            Stats.Stamina = stamina;
            Stats.State = state;
            Stats.StateTimer = stateTimer;
            Stats.Stamina = Mathf.Min(Stats.Stamina, MaxStamina);
            transform.position = MapBounds.Clamp(worldPos);
            lastPosition = transform.position;

            if (occupyProp != null
                && (state == ActionState.Staying || state == ActionState.Fainted))
            {
                occupyProp.ForceOccupyForSaveRestore(this);
                currentProp = occupyProp;
                transform.position = MapBounds.Clamp(new Vector3(
                    occupyProp.transform.position.x,
                    occupyProp.transform.position.y,
                    transform.position.z));
                lastPosition = transform.position;
            }

            ApplyAnimationFrameImmediate();

            if (Stats.State == ActionState.Fainted)
                ShowFaintedEllipsis();
            else if (showingFaintedEllipsis)
                HideMonologue();
        }

        // ---------------- 외부 API (공양 시스템에서 호출) ----------------

        /// <summary>
        /// 공양 처리 (5-3/5-4). 공양물 종류별 수치 계산은 공양 시스템 쪽에서 하고 여기엔 최종값만 넘긴다.
        /// 기절 중엔 물만 받는다(깨어남). 기력은 최대에서 멈추고 친밀도는 계속 오른다.
        /// </summary>
        public void ReceiveOffering(int staminaGain, float intimacyGain, OfferingKind kind = OfferingKind.General)
        {
            if (Stats.State == ActionState.Fainted && kind != OfferingKind.Water)
                return;

            float max = MaxStamina;
            Stats.Stamina = Mathf.Min(max, Stats.Stamina + Mathf.Max(0, staminaGain));
            Stats.Intimacy = Mathf.Min(100f, Stats.Intimacy + intimacyGain);
            Stats.Stamina = Mathf.Min(Stats.Stamina, MaxStamina);

            if (Stats.State == ActionState.Fainted)
            {
                LeaveCurrentProp();
                EnterWalking();
            }
            else if (Stats.State == ActionState.Playing && Stats.Stamina > 0f)
            {
                // 기력0 놀기에서 회복되면 기절 타이머 리셋
                Stats.StateTimer = 0f;
            }
        }

        public void BindSpriteRenderer(SpriteRenderer sr)
        {
            spriteRenderer = sr;
        }

        /// <summary>공양이 아닌 경로(윷놀이 말 이동 등)로 친밀도만 올릴 때 사용.</summary>
        public void AddIntimacy(float amount)
        {
            Stats.Intimacy = Mathf.Min(100f, Stats.Intimacy + amount);
            Stats.Stamina = Mathf.Min(Stats.Stamina, MaxStamina);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Color c;
            switch (Stats.State)
            {
                case ActionState.Walking: c = Color.green; break;
                case ActionState.Staying: c = Color.blue; break;
                case ActionState.Playing: c = new Color(1f, 0.45f, 0.85f); break;
                case ActionState.Fainted: c = Color.red; break;
                default: c = Color.white; break;
            }
            Gizmos.color = c;
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.5f, 0.2f);
        }
#endif
    }
}
