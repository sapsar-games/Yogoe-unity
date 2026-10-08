using UnityEngine;
using Yoegoe.Data;

namespace Yoegoe.Characters
{
    // 플레이어 드래그(집어서 놓기)와 내려놓은 뒤 2초 딱지((?)/(x)).
    public partial class CharacterAgent
    {
        const float DragLiftScale = 1.12f;
        const float DragBesideDistance = 0.85f;
        /// <summary>드롭 실패 (?)/(x) 딱지 지속 시간 (시트 game_settings, Docs/06: 2초).</summary>
        static float DropMarkSeconds => GameSettings.DropMarkSeconds;
        const float DropMarkPad = 0.08f;
        Vector3 dragScaleBefore;
        private bool showingDropMark;
        private float dropMarkTimer;
        private SpriteRenderer dropMarkRenderer;

        /// <summary>플레이어 드래그 시작 — 예약/점유를 풀고 AI를 멈춘다. 살짝 키워 들어올린 느낌을 낸다.</summary>
        public void BeginPlayerDrag()
        {
            if (!CanBeDraggedByPlayer) return;
            IsBeingDragged = true;
            HideDropMark();
            ClearWalkDestination();
            LeaveCurrentProp();
            lastPosition = transform.position;
            dragScaleBefore = transform.localScale;
            transform.localScale = dragScaleBefore * DragLiftScale;
            PropDragMarkers.Show(this);
        }

        public void SetDragWorldPosition(Vector3 world)
        {
            if (!IsBeingDragged) return;
            world.z = transform.position.z;
            transform.position = MapBounds.Clamp(world);
            lastPosition = transform.position; // 드롭 직후 가짜 이동량으로 방향이 튀지 않게
        }

        /// <summary>
        /// 드래그 종료.
        /// 앉을 수 있는 빈 기물 → 즉시 착석.
        /// 못 앉는 기물(점유됨·다른 요괴의 엔딩기물) → 옆에 내려놓고 2초 (x) 후 놀기.
        /// 바닥 → 2초 (?) 후 놀기. showFloorMark=false면(자물쇠 구매 팝업·제스처 취소) 딱지 없이 놀기.
        /// </summary>
        public void EndPlayerDrag(PropSlot dropProp, bool showFloorMark = true)
        {
            if (!IsBeingDragged) return;
            IsBeingDragged = false;
            PropDragMarkers.Hide();
            transform.localScale = dragScaleBefore.sqrMagnitude > 0.0001f ? dragScaleBefore : transform.localScale;
            lastPosition = transform.position;

            if (dropProp != null && dropProp.CanSitNow(this) && TrySitOnProp(dropProp))
            {
                // 보낼 때 대사 (v1.2 시연) — 기물 종류별
                TrySayCatalogLine(e => e.GoLinesFor(dropProp.ResourceType));
                return;
            }

            if (dropProp != null)
            {
                PlaceBesideProp(dropProp);
                EnterPlaying();
                BeginDropMark(DropMarkBadge.Kind.Cross);
                return;
            }

            EnterPlaying();
            if (showFloorMark)
                BeginDropMark(DropMarkBadge.Kind.Question);
        }

        /// <summary>기물 옆에 내려놓는다.</summary>
        void PlaceBesideProp(PropSlot prop)
        {
            ClearWalkDestination();
            LeaveCurrentProp();
            isWandering = false;
            wanderTarget = null;

            Vector3 origin = prop.transform.position;
            Vector3 dir = Vector3.right;
            if (prop.Occupant != null)
            {
                Vector3 away = transform.position - prop.Occupant.transform.position;
                if (away.sqrMagnitude < 0.0001f)
                    away = Random.value < 0.5f ? Vector3.left : Vector3.right;
                dir = away.normalized;
            }
            else
            {
                Vector3 away = transform.position - origin;
                if (away.sqrMagnitude > 0.0001f)
                    dir = away.normalized;
                else
                    dir = Random.value < 0.5f ? Vector3.left : Vector3.right;
            }

            Vector3 beside = origin + dir * DragBesideDistance;
            beside.z = transform.position.z;
            transform.position = MapBounds.Clamp(beside);
            lastPosition = transform.position;
        }

        /// <summary>머리 위 딱지 2초 — 그동안 제자리에 서 있다가 놀기를 이어간다.</summary>
        void BeginDropMark(DropMarkBadge.Kind kind)
        {
            if (dropMarkRenderer == null)
            {
                var go = new GameObject("DropMark_" + name);
                dropMarkRenderer = go.AddComponent<SpriteRenderer>();
                dropMarkRenderer.sortingOrder = 1400;
            }
            dropMarkRenderer.sprite = DropMarkBadge.Get(kind);
            dropMarkRenderer.gameObject.SetActive(true);
            showingDropMark = true;
            dropMarkTimer = DropMarkSeconds;
            PlaceDropMark();
            ApplyAnimationFrameImmediate();
        }

        void TickDropMark(float dt)
        {
            dropMarkTimer -= dt;
            PlaceDropMark();
            if (dropMarkTimer > 0f) return;
            HideDropMark();
            if (Stats.State == ActionState.Playing) Stats.StateTimer = 0f; // 놀기는 딱지가 끝난 뒤부터
        }

        void PlaceDropMark()
        {
            if (dropMarkRenderer == null) return;
            float top = spriteRenderer != null && spriteRenderer.sprite != null
                ? spriteRenderer.bounds.max.y
                : transform.position.y + 0.3f;
            dropMarkRenderer.transform.position = new Vector3(
                transform.position.x, top + DropMarkPad + DropMarkBadge.WorldSize * 0.5f, transform.position.z);
        }

        void HideDropMark()
        {
            showingDropMark = false;
            if (dropMarkRenderer != null) dropMarkRenderer.gameObject.SetActive(false);
        }
    }
}
