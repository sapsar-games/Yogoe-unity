using UnityEngine;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Characters
{
    // 이동 상태머신: Walking → Staying(생산) → [기력 0] Playing → [18시간] Fainted / Playing(드롭).
    public partial class CharacterAgent
    {
        private const float PlayDurationSeconds = 1f * 60f; // 기력 있는 놀기
        private static float FaintThresholdSeconds => GameSettings.FaintThresholdSeconds; // 기력0 놀기 → 기절 (시트 game_settings)
        private static float StaminaDrainPerSecond => GameSettings.StaminaDrainPerSecond; // 10분당 1 (시트 game_settings)
        private const float WanderRetrySeconds = 30f;
        private const float SeparationRadius = 0.55f;
        /// <summary>전진을 죽이지 않도록 이동 속도보다 낮게 유지.</summary>
        private const float SeparationSpeed = 1.2f;

        private PropSlot currentProp;
        /// <summary>지금 앉아 일하는 기물 (없으면 null).</summary>
        public PropSlot CurrentProp => currentProp;
        private PropSlot previousProp;
        private PropSlot destination;
        private bool isWandering;
        private float wanderTimer;
        private Vector3? wanderTarget; // isWandering 중 실제로 걸어갈 맵 안의 임시 목적지
        private float boundaryStuckTimer;
        private const float BoundaryStuckSeconds = 0.35f;
        /// <summary>만창으로 멈춰 있을 때 다른 기물을 찾아보는 간격.</summary>
        private const float HaltedRecheckSeconds = 2f;
        private float haltedRecheckTimer;

        /// <summary>
        /// 복귀 catch-up 중 Walking이면 목적지만 보장한다.
        /// 위치 스냅은 하지 않는다 — 화면이 보이는 상태에서 기물로 순간이동하면 UX가 깨진다.
        /// </summary>
        private void EnsureWalkingDestination()
        {
            if (isWandering || destination == null)
                PickDestination();
        }

        // ---------------- Walking ----------------

        private void EnterWalking()
        {
            Stats.State = ActionState.Walking;
            Stats.StateTimer = 0f;
            animFrame = 0;
            animTimer = 0f;
            lastPosition = transform.position;
            PickDestination();
        }

        private void PickDestination()
        {
            PickDestinationExcluding(previousProp);
        }

        private void PickDestinationExcluding(PropSlot exclude)
        {
            // 이전에 찜해둔 목적지가 있으면(도착 못 하고 재추첨하는 경우) 먼저 예약 해제.
            if (destination != null) destination.ReleaseReservation(this);

            destination = PropManager.Instance != null
                ? PropManager.Instance.GetRandomAvailableProp(this, exclude)
                : null;

            // 고르는 즉시 찜해둬서, 같은 프레임에 다른 캐릭터가 고를 때 후보에서 빠지게 한다
            // (다 같이 같은 기물로 몰려가는 문제 방지).
            if (destination != null) destination.TryReserve(this);

            isWandering = destination == null;
            wanderTimer = 0f;
            wanderTarget = null;
            boundaryStuckTimer = 0f;
        }

        private void TickWalking(float dt)
        {
            if (isWandering)
            {
                // 6-2 방황/재추첨 — Docs/06_행동룰.md (후보없음·자리없음을 한 루프로 통합)
                wanderTimer += dt;
                if (wanderTimer >= WanderRetrySeconds) { PickDestination(); return; }

                if (wanderTarget == null || Vector3.Distance(transform.position, wanderTarget.Value) < 0.05f)
                {
                    wanderTarget = MapBounds.RandomPoint(transform.position.z);
                    boundaryStuckTimer = 0f;
                }

                transform.position = MapBounds.MoveClamped(
                    transform.position, wanderTarget.Value, MoveSpeed * dt, out bool blocked);
                ResolveSeparation(dt);
                if (blocked) OnBoundaryBlocked(dt, excludeProp: null);
                else boundaryStuckTimer = 0f;
                return;
            }

            if (destination == null) { PickDestination(); return; }

            Vector3 targetPos = destination.transform.position;
            // 기물이 맵 밖이면 가장 가까운 가장 점까지만 가고, 막히면 다른 목적지로
            if (!MapBounds.Contains(targetPos))
                targetPos = MapBounds.Clamp(targetPos);

            transform.position = MapBounds.MoveClamped(
                transform.position, targetPos, MoveSpeed * dt, out bool blockedTowardProp);
            ResolveSeparation(dt);

            if (blockedTowardProp)
            {
                OnBoundaryBlocked(dt, excludeProp: destination);
                return;
            }

            boundaryStuckTimer = 0f;

            if (Vector3.Distance(transform.position, targetPos) < 0.05f)
            {
                if (destination.TryOccupy(this))
                {
                    currentProp = destination;
                    destination = null;
                    EnterStaying();
                }
                else
                {
                    // 6-2 "밀린 쪽은 되돌지 않고 직진해 다음 기물로" — 실제 경로 기하가 없어서
                    // 다음 후보를 즉시 재선정하는 것으로 단순화
                    PickDestination();
                }
            }
        }

        /// <summary>
        /// 맵 경계(비직사각형 포함)에 막혀 목표로 못 나가면 방향을 바꾼다.
        /// </summary>
        private void OnBoundaryBlocked(float dt, PropSlot excludeProp)
        {
            boundaryStuckTimer += dt;
            if (boundaryStuckTimer < BoundaryStuckSeconds) return;
            boundaryStuckTimer = 0f;

            if (excludeProp != null)
            {
                // 막힌 기물은 잠시 제외하고 다른 곳으로
                PickDestinationExcluding(excludeProp);
                if (destination != null) return;
            }

            // 방황 중이거나 대체 기물 없음 → 랜덤 방향
            if (destination != null)
            {
                destination.ReleaseReservation(this);
                destination = null;
            }
            isWandering = true;
            wanderTarget = MapBounds.RandomPoint(transform.position.z);
            wanderTimer = 0f;
        }

        /// <summary>
        /// 걷는 중인 캐릭터끼리 너무 가까워지면 서로 살짝 밀어낸다.
        /// 상대가 앉아 있는 경우만 피하고, 전진 방향 성분은 약하게 유지해 제자리 떨림을 막는다.
        /// </summary>
        private void ResolveSeparation(float dt)
        {
            Vector3 push = Vector3.zero;
            for (int i = 0; i < ActiveAgents.Count; i++)
            {
                var other = ActiveAgents[i];
                if (other == null || other == this) continue;
                // 앉아/기절한 상대는 밀되, 같이 걷는 상대만 상호 분리(앉아있는 요괴 자리는 건드리지 않음)
                if (other.Stats.State == ActionState.Fainted) continue;

                Vector3 diff = transform.position - other.transform.position;
                diff.z = 0f;
                float dist = diff.magnitude;
                if (dist > 0.0001f && dist < SeparationRadius)
                    push += diff.normalized * (SeparationRadius - dist);
            }

            if (push == Vector3.zero) return;

            // 한 프레임 밀림 상한 — moveSpeed 대비 과도하면 전진이 상쇄되어 끊겨 보인다.
            float maxPush = MoveSpeed * 0.55f * dt;
            Vector3 step = push * SeparationSpeed * dt;
            if (step.sqrMagnitude > maxPush * maxPush)
                step = step.normalized * maxPush;

            transform.position = MapBounds.Clamp(transform.position + step);
        }

        // ---------------- Staying ----------------

        private void EnterStaying()
        {
            Stats.State = ActionState.Staying;
            Stats.StateTimer = 0f;
        }

        private void TickStaying(float dt)
        {
            // 긴 dt에서도 기력 고갈 전 구간만 생산 (OfflineSimulator와 동일).
            float left = dt;
            int guard = 0;
            while (left > 0.0001f && Stats.State == ActionState.Staying && guard++ < 8)
                left -= TickStayingSlice(left);
        }

        /// <summary>
        /// 머물기 한 구간. 소모한 초를 반환한다.
        /// 생산은 앉아 있는 동안 기력 0 또는 기물 만창까지. 만창이면 생산·기력소모 둘 다 멈추고 앉아만 있다.
        /// </summary>
        private float TickStayingSlice(float dt)
        {
            if (currentProp != null && currentProp.IsStorageHalted)
            {
                // 만창인데 기력이 남았으면 일할 수 있는 다른 기물로 간다. 없으면 놀기 (v1.2 · 시연).
                haltedRecheckTimer += dt;
                if (haltedRecheckTimer >= HaltedRecheckSeconds)
                {
                    haltedRecheckTimer = 0f;
                    if (TryLeaveHaltedProp()) return dt;
                    if (Stats.Stamina > 0f)
                    {
                        // 갈 곳이 없으면 '다 찼어. 이제 놀래!' 하고 놀기 — 기력이 남아 있어 기절로 이어지지 않는다
                        EnterPlaying();
                        TrySayCatalogLine(e => e.fullIdleLines);
                        return dt;
                    }
                }
                Stats.StateTimer += dt;
                return dt;
            }
            haltedRecheckTimer = 0f;

            float drain = StaminaDrainPerSecond;
            float timeToZero = Stats.Stamina > 0f ? Stats.Stamina / drain : 0f;
            float slice = Mathf.Min(dt, timeToZero);

            if (slice <= 0f)
            {
                Stats.Stamina = 0f;
                EnterPlaying(); // 기력 0 → 놀기/쉬기 (점유 해제)
                TrySayCatalogLine(e => e.tiredLines);
                return 0.0001f;
            }

            // 기물이 만창에 닿으면 그 시점까지만 일한 것으로 친다
            float worked = currentProp != null
                ? currentProp.ProduceWhileStaying(slice, Stats.Intimacy, currentProp.IsOwnerOnEndingProp(this), SpeedMultiplier)
                : slice;
            if (worked <= 0f && currentProp != null && currentProp.IsStorageHalted)
                return 0.0001f; // 다음 구간에서 정지 분기로

            Stats.StateTimer += worked;
            Stats.Stamina -= worked * drain;
            if (Stats.Stamina < 0f) Stats.Stamina = 0f;
            slice = worked > 0f ? worked : slice;

            if (Stats.Stamina <= 0f)
            {
                Stats.Stamina = 0f;
                EnterPlaying();
                TrySayCatalogLine(e => e.tiredLines);
            }

            return slice;
        }

        /// <summary>7-1: 머물기·생산 중일 때만 분당 생산량.</summary>
        public double GetProductionPerMinuteIfStaying()
        {
            if (Stats.State != ActionState.Staying || currentProp == null) return 0;
            return currentProp.MeritPerMinuteFor(this);
        }

        /// <summary>전용 점유 아트 표시 중에는 캐릭터 스프라이트를 숨긴다.</summary>
        public void SetSpriteVisible(bool visible)
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null)
                spriteRenderer.enabled = visible;
        }

        private void LeaveCurrentProp()
        {
            if (currentProp != null)
            {
                currentProp.Vacate(this);
                previousProp = currentProp;
                currentProp = null;
            }
            SetSpriteVisible(true);
        }

        /// <summary>보관함이 찬 요괴가 옮겨 갈 수 있는 기물 — 옹달샘 · 제단(공덕 기물) (v1.2).</summary>
        static bool IsOverflowSpot(PropSlot p) =>
            p.ResourceType == PropResourceType.Water || p.ResourceType == PropResourceType.Merit;

        /// <summary>만창 기물에서 일어나 빈 옹달샘·제단 중 하나로 걸어간다. 갈 곳이 없거나 기력이 없으면 false.</summary>
        private bool TryLeaveHaltedProp()
        {
            if (Stats.Stamina <= 0f || PropManager.Instance == null || currentProp == null) return false;
            var target = PropManager.Instance.GetRandomAvailableProp(this, currentProp, IsOverflowSpot);
            if (target == null) return false;
            LeaveCurrentProp();   // previousProp = 만창 기물 → 걷기 목적지에서 빠진다
            EnterWalking();
            if (destination != target)
            {
                if (destination != null) destination.ReleaseReservation(this);
                destination = target;
                destination.TryReserve(this);
                isWandering = false;
            }
            // '다 찼어. {d}에 가 있을게' (v1.2 시연) — {d} = 걸어갈 기물
            TrySayCatalogLine(e => e.fullLines, destination != null ? destination.DisplayName : "다른 데");
            return true;
        }

        // ---------------- Playing (놀기 / 기력0 쉬기) ----------------

        /// <summary>
        /// 놀기: 기물 점유를 풀고 맵을 돌아다닌다. 기력 소모·생산 없음.
        /// 기력 &gt; 0: 5분 후 Walking. 기력 0: 쓰러지지 않고 배회, 18시간 후 기절. 드래그로 앉히면 재가동.
        /// </summary>
        public void EnterPlaying()
        {
            if (Stats.State == ActionState.Fainted) return;

            ClearWalkDestination();
            LeaveCurrentProp();
            isWandering = false;
            wanderTarget = null;
            Stats.State = ActionState.Playing;
            Stats.StateTimer = 0f;
            animFrame = 0;
            animTimer = 0f;
            lastPosition = transform.position;
        }

        private void TickPlaying(float dt)
        {
            Stats.StateTimer += dt;

            if (Stats.Stamina <= 0f)
            {
                if (Stats.StateTimer >= FaintThresholdSeconds)
                    EnterFainted();
            }
            else if (Stats.StateTimer >= PlayDurationSeconds)
            {
                EnterWalking();
                return;
            }

            if (Stats.State != ActionState.Playing) return;

            if (wanderTarget == null || Vector3.Distance(transform.position, wanderTarget.Value) < 0.05f)
                wanderTarget = MapBounds.RandomPoint(transform.position.z);

            transform.position = MapBounds.Clamp(
                Vector3.MoveTowards(transform.position, wanderTarget.Value, MoveSpeed * dt));
            ResolveSeparation(dt);
        }

        /// <summary>
        /// 놀기·걷기 중 기물에 올려 앉히기. 성공 시 머물기(기력 0까지 생산)가 새로 시작된다.
        /// 기력 0 놀기 중에도 무료로 앉혀 재가동 가능.
        /// </summary>
        public bool TrySitOnProp(PropSlot prop)
        {
            if (prop == null) return false;
            if (Stats.State == ActionState.Fainted) return false;

            ClearWalkDestination();
            LeaveCurrentProp();
            isWandering = false;
            wanderTarget = null;

            if (!prop.AcceptsWorkers || !prop.CanBeUsedBy(this)) return false;
            if (!prop.TryOccupy(this)) return false;

            currentProp = prop;
            var p = prop.transform.position;
            transform.position = MapBounds.Clamp(new Vector3(p.x, p.y, transform.position.z));
            EnterStaying();
            return true;
        }

        private void ClearWalkDestination()
        {
            if (destination != null)
            {
                destination.ReleaseReservation(this);
                destination = null;
            }
            isWandering = false;
            wanderTarget = null;
        }

        // ---------------- Fainted ----------------

        void EnterFainted()
        {
            Stats.State = ActionState.Fainted;
            Stats.StateTimer = 0f;
            // 기절 중에도 점유 유지 — 이미 놀기에서 비웠으면 null
            Requests.ClearAll();
            ShowFaintedEllipsis();
        }

        private float TickPlayingSlice(float dt)
        {
            if (Stats.Stamina <= 0f)
            {
                float timeToFaint = Mathf.Max(0f, FaintThresholdSeconds - Stats.StateTimer);
                float slice = Mathf.Min(dt, timeToFaint > 0f ? timeToFaint : dt);
                if (slice <= 0f) slice = dt;
                Stats.StateTimer += slice;
                if (Stats.StateTimer >= FaintThresholdSeconds)
                    EnterFainted();
                return slice;
            }

            float timeToEnd = Mathf.Max(0f, PlayDurationSeconds - Stats.StateTimer);
            float sliceWalk = Mathf.Min(dt, timeToEnd > 0f ? timeToEnd : dt);
            if (sliceWalk <= 0f) sliceWalk = dt;
            Stats.StateTimer += sliceWalk;
            if (Stats.StateTimer >= PlayDurationSeconds)
                EnterWalking();
            return sliceWalk;
        }
    }
}
