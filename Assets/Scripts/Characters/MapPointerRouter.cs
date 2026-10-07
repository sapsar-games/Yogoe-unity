using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Debugging;
using Yoegoe.Economy;
using Yoegoe.UI;

namespace Yoegoe.Characters
{
    /// <summary>
    /// 맵 포인터 제스처 단일 진입점.
    /// 캐릭터: 길게 누르기(또는 임계 이동) → 들어올림 드래그.
    /// 맵: 임계 이동 후 패닝. 두 손가락 핀치·마우스 휠 → 줌.
    /// 캐릭터 탭: 더블탭이면 상세, 단일탭(더블탭 창 만료 후)이면 혼잣말.
    /// 기물: 탭 → 수거(쌓인 게 없거나 2초 안에 다시 탭하면 기물 창 = 업그레이드 팝업, v1.2 시연),
    /// 길게 누르기 → 개별 업그레이드 팝업. 기물 위에 앉은 요괴는 요괴가 우선.
    ///
    /// 설계 원칙(반복된 회귀 버그를 겪고 정리함): "무엇을 눌렀는지"는 press 시점에 딱 한 번만
    /// 정한다(<see cref="PressTarget"/>). Hold·Release는 그 판정을 다시 계산하지 않고 그대로
    /// 따라간다 — 특히 release 시점에 손 위치로 반경을 재검사하지 않는다. press 이후 화면이
    /// 줌되거나 손이 살짝 흔들려도(스크린 픽셀 vs 월드 반경 단위 차이) 판정이 뒤집히지 않게 하기
    /// 위함. 이 파일을 고칠 때 새 특수 케이스가 필요하면, 기존 분기에 조건을 덧붙이지 말고
    /// PressTarget에 값을 추가하는 방향으로 확장할 것.
    /// </summary>
    public class MapPointerRouter : MonoBehaviour
    {
        /// <summary>
        /// 캐릭터 상세화면을 열어달라는 요청 (agent, 하이라이트할 offeringId).
        /// UI(GameHud)가 구독해서 실제 DetailScreen을 연다 — 이 클래스는 UI를 모른다.
        /// </summary>
        public static event System.Action<CharacterAgent, string> CharacterDetailRequested;

        /// <summary>기물 창 요청 (v1.3) — (기물, 미리 골라 둘 요괴 · 탭이면 null). UI(PropPanel)가 구독한다.</summary>
        public static event System.Action<PropSlot, CharacterAgent> PropPanelRequested;

        /// <summary>먹이기 창 요청 (v1.3) — 그릇 말풍선 탭, 노는 요괴 탭. UI(FeedPopup)가 구독한다.</summary>
        public static event System.Action<CharacterAgent> FeedRequested;

        /// <summary>
        /// 잠긴 기물 탭·자물쇠 위 앉히기 시도 → 구매 팝업 요청.
        /// 두 번째 인자는 건설 후 앉힐 요괴(탭이면 null). UI(PropPurchasePopup→ConfirmPopup)가 구독한다.
        /// </summary>
        public static event System.Action<PropSlot, CharacterAgent> PropPurchaseRequested;

        /// <summary>건립된 기물 길게 누르기 → 개별 업그레이드 팝업 요청. UI(PropUpgradePopup)가 구독한다.</summary>
        public static event System.Action<PropSlot> PropUpgradeRequested;

        public Camera targetCamera;
        public MapCameraDrag mapDrag;

        [Tooltip("이보다 많이 움직이면 탭이 아니라 드래그로 확정.")]
        public float dragThresholdPixels = 24f;

        [Tooltip("캐릭터를 이 시간 이상 누르고 있으면 이동 없이도 들어올림.")]
        public float longPressSeconds = 0.35f;

        [Tooltip("더블탭으로 인정할 최대 간격(초). 이 안에 같은 캐릭터를 다시 탭하면 상세화면.")]
        public float doubleTapSeconds = 0.35f;

        [Tooltip("캐릭터 스프라이트 bounds에 더하는 여유(월드). 너무 크면 근처 맵 드래그가 캐릭터로 잡힘.")]
        public float characterHitPadding = 0.12f;

        [Tooltip("스프라이트가 없을 때 쓰는 고정 히트 반경(월드).")]
        public float characterHitRadiusFallback = 0.45f;

        [Tooltip("드롭 시 기물 스프라이트 bounds 바깥으로 허용할 여유(월드). 0이면 PNG 박스 안만.")]
        public float propDropRadius = 0.15f;

        [Tooltip("기물 본체 탭(수거·길게 누르기 업그레이드) 시 스프라이트 bounds 바깥 여유(월드).")]
        public float propTapRadius = 0.12f;

        [Tooltip("보관 라벨(***·숫자) 탭 여유(월드). 라벨 탭도 본체 탭과 같은 수거.")]
        public float pileLabelTapPadding = 0.18f;

        [Tooltip("수거한 뒤 이 시간 안에 같은 기물을 다시 탭하면 기물 창(업그레이드 팝업)을 연다 (v1.2 시연 2초).")]
        public float propReopenSeconds = 2f;

        [Tooltip("자물쇠(미건립) 탭 여유. 0이면 bounds 안만 — 초가집처럼 작고 캐릭터와 겹치면 구매가 잘 안 됨.")]
        public float lockTapRadius = 0.28f;

        private enum Phase { Idle, Pending, MapDrag, CharacterDrag, PinchZoom }

        /// <summary>press 시점에 딱 한 번 정해지는 "무엇을 눌렀는지". Hold/Release는 이 값만 본다.</summary>
        public enum PressTarget { Empty, LockedProp, Prop, Character, GongyangganProp, Willow, PropLevelTag, PierProp }

        /// <summary>길게 누르기로 이미 처리(업그레이드 팝업)한 press — release에서 탭으로 다시 처리하지 않는다.</summary>
        private bool pressConsumed;

        private Phase phase = Phase.Idle;
        private PressTarget pressTarget = PressTarget.Empty;
        private bool wasPressed;
        private Vector2 pressStartScreen;
        private Vector2 lastScreen;
        private float pressUnscaledTime;
        private CharacterAgent pressCharacter;
        private CharacterAgent dragCharacter;
        private PropSlot pressProp;

        /// <summary>마지막으로 탭 수거한 기물과 시각 — 2초 안에 다시 탭하면 기물 창.</summary>
        private PropSlot lastCollectedProp;
        private float lastCollectedTime = -999f;

        /// <summary>첫 탭 후 더블탭 대기 중인 캐릭터. 창이 지나면 혼잣말.</summary>
        private CharacterAgent pendingMonologueTap;
        private float pendingMonologueDeadline;

        bool pinchActive;
        float lastPinchDistance;

        private static readonly List<RaycastResult> UiRaycastHits = new List<RaycastResult>(8);

        private void Awake()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            ResolveMapDrag();
        }

        private void Update()
        {
            FlushPendingMonologueTapIfDue();

            if (CeremonyGate.BlocksWorldInput) return;
            // 윷은 풀스크린 UI인데 보드 칸 Image가 raycast를 안 먹는 구멍이 있어,
            // 휠/핀치가 IsBlockingUi를 통과해 본맵 카메라로 새는 경우가 있다.
            if (YutScreen.Instance != null && YutScreen.Instance.IsOpen) return;
            if (GongyangganScreen.Instance != null && GongyangganScreen.Instance.IsOpen) return;

            // 핀치·휠은 단일 포인터 제스처보다 우선
            if (TryHandlePinchZoom()) return;
            TryHandleScrollZoom();

            if (!TryReadPointer(out Vector2 screenPos, out bool pressed)) return;

            bool justPressed = pressed && !wasPressed;
            bool justReleased = !pressed && wasPressed;
            wasPressed = pressed;

            if (justPressed) OnPress(screenPos);
            else if (pressed && phase != Phase.Idle) OnHold(screenPos);
            else if (justReleased) OnRelease(screenPos);
        }

        /// <summary>두 손가락 핀치 → 맵 줌.</summary>
        bool TryHandlePinchZoom()
        {
            var ts = Touchscreen.current;
            if (ts == null) return false;

            int n = 0;
            Vector2 p0 = default;
            Vector2 p1 = default;
            int touchCount = ts.touches.Count;
            for (int i = 0; i < touchCount; i++)
            {
                var touch = ts.touches[i];
                var ph = touch.phase.ReadValue();
                if (ph == UnityEngine.InputSystem.TouchPhase.None
                    || ph == UnityEngine.InputSystem.TouchPhase.Ended
                    || ph == UnityEngine.InputSystem.TouchPhase.Canceled)
                    continue;

                Vector2 pos = touch.position.ReadValue();
                if (n == 0) p0 = pos;
                else if (n == 1) p1 = pos;
                n++;
                if (n >= 2) break;
            }

            if (n < 2)
            {
                if (pinchActive)
                {
                    pinchActive = false;
                    phase = Phase.Idle;
                }
                return false;
            }

            float dist = Vector2.Distance(p0, p1);
            if (dist < 8f) return true;

            Vector2 mid = (p0 + p1) * 0.5f;
            if (IsBlockingUi(mid) || IsBlockingUi(p0) || IsBlockingUi(p1))
            {
                if (pinchActive)
                {
                    pinchActive = false;
                    phase = Phase.Idle;
                }
                return true;
            }

            if (!pinchActive)
            {
                pinchActive = true;
                lastPinchDistance = dist;
                AbortSingleFingerGesture();
                phase = Phase.PinchZoom;
                return true;
            }

            if (lastPinchDistance > 0.01f)
            {
                // 손가락을 벌리면 dist↑ → 확대(ortho↓) → ratio = last/dist
                float ratio = lastPinchDistance / dist;
                ratio = Mathf.Clamp(ratio, 0.85f, 1.18f);
                ResolveMapDrag();
                if (mapDrag != null) mapDrag.ZoomByRatio(mid, ratio);
            }

            lastPinchDistance = dist;
            phase = Phase.PinchZoom;
            wasPressed = true;
            return true;
        }

        /// <summary>에디터·데스크톱 WebGL용 마우스 휠 줌.</summary>
        void TryHandleScrollZoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;
            if (IsBlockingUi(mouse.position.ReadValue())) return;

            ResolveMapDrag();
            if (mapDrag == null) return;

            float ratio = scroll > 0f ? 0.90f : 1.11f;
            mapDrag.ZoomByRatio(mouse.position.ReadValue(), ratio);
        }

        void AbortSingleFingerGesture()
        {
            CancelPendingMonologueTap();
            if (phase == Phase.CharacterDrag && dragCharacter != null)
            {
                dragCharacter.EndPlayerDrag(null, showFloorMark: false);
                dragCharacter = null;
            }
            pressCharacter = null;
            pressProp = null;
            pressTarget = PressTarget.Empty;
        }

        private void OnPress(Vector2 screenPos)
        {
            if (IsBlockingUi(screenPos))
            {
                phase = Phase.Idle;
                return;
            }

            pressStartScreen = screenPos;
            lastScreen = screenPos;
            pressUnscaledTime = Time.unscaledTime;
            pressCharacter = FindNearestCharacter(screenPos);
            // ▲ 레벨업 딱지 — 요괴보다 먼저 (딱지는 작아서 겹치면 딱지를 누른 것으로)
            var tagged = FindLevelTagAt(screenPos);
            if (tagged != null)
            {
                pressProp = tagged;
                dragCharacter = null;
                pressCharacter = null;
                pressConsumed = false;
                pressTarget = PressTarget.PropLevelTag;
                phase = Phase.Pending;
                return;
            }
            pressProp = FindNearestPileLabel(screenPos);
            if (pressProp == null) pressProp = FindNearestProp(screenPos);
            dragCharacter = null;
            pressConsumed = false;
            pressTarget = ClassifyPress(pressCharacter != null, pressProp != null,
                pressProp != null && pressProp.IsBuilt,
                pressProp != null && pressProp.data != null && pressProp.data.opensGongyanggan);
            // 나루터(요괴가 앉지 않는 시설) — 누르면 나루터 화면 (v1.3)
            if (pressTarget == PressTarget.Prop && pressProp != null && pressProp.data != null && pressProp.data.opensPier)
                pressTarget = PressTarget.PierProp;
            // 공덕 버드나무: 요괴·기물이 아닌 곳에서만 (드래그 방해 안 하게)
            if (pressTarget == PressTarget.Empty && IsOverWillow(screenPos))
                pressTarget = PressTarget.Willow;
            phase = Phase.Pending;
        }

        /// <summary>
        /// "무엇을 눌렀는지"를 press 시점에 딱 한 번 정한다. Hold/Release는 이 결과만 본다.
        /// 우선순위(Docs/07): 요괴 몸 > 자물쇠 > 화덕 > 기물(본체·보관 라벨) > 버드나무 > 빈 맵.
        /// 기물 위에 앉은 요괴를 누르면 기물이 아니라 요괴가 반응해야 한다.
        /// </summary>
        public static PressTarget ClassifyPress(bool characterHit, bool propHit, bool propBuilt, bool propOpensGongyanggan)
        {
            // 기절 등으로 드래그 불가한 캐릭터도 탭(상세화면 진입)은 가능해야 한다 —
            // "기절한 요괴는 상세 화면 공양으로만 깨어난다" — 그래서 CanBeDraggedByPlayer로
            // 걸러내지 않는다. 드래그 가능 여부는 Hold에서 따로 본다.
            if (characterHit) return PressTarget.Character;
            if (!propHit) return PressTarget.Empty;
            if (!propBuilt) return PressTarget.LockedProp;
            if (propOpensGongyanggan) return PressTarget.GongyangganProp;
            return PressTarget.Prop;
        }

        private void OnHold(Vector2 screenPos)
        {
            Vector2 delta = screenPos - lastScreen;
            lastScreen = screenPos;

            if (phase == Phase.Pending)
            {
                float moved = Vector2.Distance(screenPos, pressStartScreen);
                float held = Time.unscaledTime - pressUnscaledTime;

                switch (pressTarget)
                {
                    case PressTarget.LockedProp:
                        // 자물쇠 탭은 캐릭터·지도 드래그로 절대 전환되지 않는다 — release에서 구매 판정.
                        return;

                    case PressTarget.GongyangganProp:
                    case PressTarget.Prop:
                        // 움직이면 지도 패닝, 가만히 길게 누르면 개별 업그레이드 팝업(화덕은 업그레이드 없음).
                        // 팝업을 이미 띄웠으면 이 누름은 끝 — 손을 움직여도 팝업 뒤 지도가 움직이지 않게.
                        if (pressConsumed) return;
                        if (moved > dragThresholdPixels)
                        {
                            phase = Phase.MapDrag;
                            ResolveMapDrag();
                            if (mapDrag != null) mapDrag.ApplyScreenDelta(screenPos - pressStartScreen);
                            return;
                        }
                        if (!pressConsumed && held >= longPressSeconds
                            && pressTarget == PressTarget.Prop && PropEconomy.CanUpgrade(pressProp))
                        {
                            pressConsumed = true;
                            CancelPendingMonologueTap();
                            PropUpgradeRequested?.Invoke(pressProp);
                        }
                        return;

                    case PressTarget.Character:
                        if (!pressCharacter.CanBeDraggedByPlayer)
                        {
                            // 드래그 불가 캐릭터(기절 등) 위는 지도 패닝 임계값만 적용 —
                            // 움직임이 없으면 release에서 탭(상세화면 진입)으로 처리된다.
                            if (moved <= dragThresholdPixels) return;
                            phase = Phase.MapDrag;
                            ResolveMapDrag();
                            if (mapDrag != null) mapDrag.ApplyScreenDelta(screenPos - pressStartScreen);
                            return;
                        }
                        if (held >= longPressSeconds || moved > dragThresholdPixels)
                            BeginCharacterDrag(screenPos);
                        return;

                    default: // Empty — 빈 맵 위: 임계 이동 이상이면 패닝
                        if (moved <= dragThresholdPixels) return;
                        phase = Phase.MapDrag;
                        ResolveMapDrag();
                        if (mapDrag != null) mapDrag.ApplyScreenDelta(screenPos - pressStartScreen);
                        return;
                }
            }

            if (phase == Phase.MapDrag)
            {
                ResolveMapDrag();
                if (mapDrag != null) mapDrag.ApplyScreenDelta(delta);
            }
            else if (phase == Phase.CharacterDrag && dragCharacter != null)
            {
                MoveDragCharacter(screenPos);
            }
        }

        void BeginCharacterDrag(Vector2 screenPos)
        {
            CancelPendingMonologueTap();
            dragCharacter = pressCharacter;
            dragCharacter.BeginPlayerDrag();
            phase = Phase.CharacterDrag;
            MoveDragCharacter(screenPos);
        }

        private void OnRelease(Vector2 screenPos)
        {
            if (phase == Phase.Pending && !pressConsumed)
            {
                // press 시점 판정(pressTarget)만 신뢰한다 — release 손 위치로 반경을 다시 재는
                // 순간, 화면 픽셀 이동과 월드 반경 단위가 안 맞아(특히 줌아웃 상태) 손이 살짝만
                // 흔들려도 오탐 실패하는 회귀가 반복됐다.
                switch (pressTarget)
                {
                    case PressTarget.LockedProp:
                        CancelPendingMonologueTap();
                        PropPurchaseRequested?.Invoke(pressProp, null);
                        break;

                    case PressTarget.GongyangganProp:
                        CancelPendingMonologueTap();
                        {
                            var screen = GongyangganScreen.Resolve();
                            if (screen != null)
                                screen.Open();
                            else
                                Debug.LogError(
                                    "[MapPointerRouter] GongyangganScreen을 찾을 수 없습니다. " +
                                    "Main 씬 Hierarchy에 Prefab 인스턴스가 있는지 확인하세요.");
                        }
                        break;

                    case PressTarget.Prop:
                        // 기물 본체·보관 라벨 탭 → 쌓인 자원 수거.
                        // 쌓인 게 없거나, 방금(2초 안) 수거한 기물을 다시 탭하면 기물 창 (v1.2 시연).
                        CancelPendingMonologueTap();
                        HandlePropTap(pressProp);
                        break;

                    case PressTarget.PierProp:
                        CancelPendingMonologueTap();
                        PierScreen.Instance?.Open();
                        break;

                    case PressTarget.PropLevelTag:
                        CancelPendingMonologueTap();
                        PropPanelRequested?.Invoke(pressProp, null);
                        break;

                    case PressTarget.Willow:
                        CancelPendingMonologueTap();
                        MeritWillow.Instance?.OnTapped();
                        Yoegoe.Save.GameSaveBridge.RequestSave();
                        break;

                    case PressTarget.Character:
                        HandleCharacterTap(pressCharacter);
                        break;
                }
            }
            else if (phase == Phase.CharacterDrag && dragCharacter != null)
            {
                CancelPendingMonologueTap();
                var prop = FindDropProp(dragCharacter, screenPos);
                // 사냥터·채집터는 목적지를 골라야 해서 바로 앉히지 않고 기물 창(그 요괴를 미리 골라 둠) (v1.3)
                var dropTarget = prop != null ? prop : FindAnyDestinationPropAt(dragCharacter, screenPos);
                if (dropTarget != null && dropTarget.HasDestinations && !dropTarget.IsOccupied)
                {
                    var who = dragCharacter;
                    who.EndPlayerDrag(null, showFloorMark: false);
                    PropPanelRequested?.Invoke(dropTarget, who);
                    phase = Phase.Idle;
                    pressTarget = PressTarget.Empty;
                    pressCharacter = null;
                    dragCharacter = null;
                    pressProp = null;
                    pressConsumed = false;
                    return;
                }
                // 건립된 기물에 못 앉히면 — 자물쇠 위에 놓았는지 보고 구매 팝업(탭과 동일 경로).
                // 건설 확정 시 이 요괴를 자동 앉히도록 함께 넘긴다.
                bool purchasePrompted = false;
                if (prop == null)
                {
                    var locked = FindUnbuiltDropProp(dragCharacter, screenPos);
                    if (locked != null)
                    {
                        PropPurchaseRequested?.Invoke(locked, dragCharacter);
                        purchasePrompted = true;
                    }
                }
                dragCharacter.EndPlayerDrag(prop, showFloorMark: !purchasePrompted);
            }

            phase = Phase.Idle;
            pressTarget = PressTarget.Empty;
            pressCharacter = null;
            dragCharacter = null;
            pressProp = null;
            pressConsumed = false;
        }

        void HandlePropTap(PropSlot prop)
        {
            if (prop == null) return;
            bool reopen = prop == lastCollectedProp
                          && Time.unscaledTime - lastCollectedTime <= propReopenSeconds;
            if (!reopen && prop.HasPendingCollectible && prop.TryCollect())
            {
                lastCollectedProp = prop;
                lastCollectedTime = Time.unscaledTime;
                Yoegoe.Save.GameSaveBridge.RequestSave();
                return;
            }
            lastCollectedProp = null;
            // 떡절구(버드나무로 가는 공덕)는 v1.3에서도 '지금 그대로' — 탭은 수거만, 레벨업은 길게 누르기로.
            if (prop.ResourceType == PropResourceType.Merit && !prop.CollectsByTap) return;
            // 보관함이 비었으면(또는 방금 받았으면) 기물 창 — 보내기 / 일하는 중 / 레벨업 (v1.3)
            PropPanelRequested?.Invoke(prop, null);
        }

        /// <summary>
        /// 더블탭이면 상세화면. 아니면 창이 끝날 때까지 기다렸다가 혼잣말
        /// (상세 진입 시 말풍선이 뜨지 않도록 단일탭을 즉시 처리하지 않음).
        /// 음식 요구가 떠 있으면 단일탭도 즉시 상세 (Docs/07).
        /// </summary>
        void HandleCharacterTap(CharacterAgent agent)
        {
            if (agent == null) return;

            // 윷 복귀 만세·들고 있는 아이템: 탭하면 수거 연출(상세/혼잣말 없음)
            if (PostYutLootPresenter.Instance != null
                && PostYutLootPresenter.Instance.TryHandleCharacterTap(agent))
            {
                CancelPendingMonologueTap();
                return;
            }

            // 더블탭 = 상세
            if (pendingMonologueTap == agent && Time.unscaledTime <= pendingMonologueDeadline)
            {
                CancelPendingMonologueTap();
                OpenCharacterDetail(agent);
                return;
            }

            if (pendingMonologueTap != null && pendingMonologueTap != agent)
                FlushPendingMonologueTap();

            // 그릇 말풍선(배고픔) 탭 → 바로 먹이기 창 (v1.3)
            if (agent.HasOfferingRequest)
            {
                CancelPendingMonologueTap();
                agent.SayCatalogLine(e => e.hungryLines);
                FeedRequested?.Invoke(agent);
                return;
            }

            pendingMonologueTap = agent;
            pendingMonologueDeadline = Time.unscaledTime + doubleTapSeconds;
        }

        void FlushPendingMonologueTapIfDue()
        {
            if (pendingMonologueTap == null) return;
            if (Time.unscaledTime < pendingMonologueDeadline) return;
            FlushPendingMonologueTap();
        }

        void FlushPendingMonologueTap()
        {
            var agent = pendingMonologueTap;
            pendingMonologueTap = null;
            if (agent == null) return;
            if (agent.HasOfferingRequest)
            {
                FeedRequested?.Invoke(agent);
                return;
            }
            agent.OnTapped();
            // 노는(일하지 않는) 요괴를 누르면 혼잣말 + 먹이기 창 (v1.3). 일하는 중은 일 대사만, 기절은 "..."만.
            if (agent.Stats.State != ActionState.Staying && agent.Stats.State != ActionState.Fainted)
                FeedRequested?.Invoke(agent);
        }

        void CancelPendingMonologueTap()
        {
            pendingMonologueTap = null;
        }

        static void OpenCharacterDetail(CharacterAgent agent)
        {
            if (agent == null) return;
            string highlight = null;
            if (agent.HasOfferingRequest && agent.Requests.OfferingRequest != null)
                highlight = agent.Requests.OfferingRequest.offeringId;
            CharacterDetailRequested?.Invoke(agent, highlight);
        }

        private void ResolveMapDrag()
        {
            if (mapDrag != null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera != null)
                mapDrag = targetCamera.GetComponent<MapCameraDrag>();
        }

        private void MoveDragCharacter(Vector2 screenPos)
        {
            if (dragCharacter == null || targetCamera == null) return;
            float depth = -targetCamera.transform.position.z;
            Vector3 world = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
            world.z = 0f;
            dragCharacter.SetDragWorldPosition(world);
        }

        private CharacterAgent FindNearestCharacter(Vector2 screenPos)
        {
            if (targetCamera == null) return null;
            float depth = -targetCamera.transform.position.z;
            Vector3 world = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
            world.z = 0f;

            CharacterAgent nearest = null;
            float nearestScore = float.MaxValue;
            foreach (var agent in CharacterAgent.All)
            {
                if (agent == null) continue;
                if (!TryGetCharacterHitScore(agent, world, out float score)) continue;
                if (score < nearestScore)
                {
                    nearestScore = score;
                    nearest = agent;
                }
            }
            return nearest;
        }

        private bool TryGetCharacterHitScore(CharacterAgent agent, Vector3 world, out float score)
        {
            score = 0f;
            // 머리 위 말풍선·요구 아이콘·획득품 = 몸 탭과 동일 (Docs/07). 겹치면 그 요괴가 우선.
            if (agent.HitOverhead(world, characterHitPadding * 0.5f)) return true;
            var sr = agent.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                Bounds b = sr.bounds;
                b.Expand(characterHitPadding);
                if (!b.Contains(world)) return false;
                score = Vector2.Distance(b.center, world);
                return true;
            }

            float d = Vector2.Distance(agent.transform.position, world);
            if (d > characterHitRadiusFallback) return false;
            score = d;
            return true;
        }

        PropSlot FindLevelTagAt(Vector2 screenPos)
        {
            if (targetCamera == null || PropManager.Instance == null) return null;
            float depth = -targetCamera.transform.position.z;
            Vector3 w = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
            foreach (var p in PropManager.Instance.AllProps)
                if (p != null && p.HitLevelTag(w, 0.12f)) return p;
            return null;
        }

        /// <summary>요괴를 놓은 자리의 사냥터·채집터 (친밀도·점유와 상관없이) — 기물 창으로 보낼 때.</summary>
        private PropSlot FindAnyDestinationPropAt(CharacterAgent agent, Vector2 screenPos)
        {
            if (targetCamera == null || PropManager.Instance == null || agent == null) return null;
            float depth = -targetCamera.transform.position.z;
            Vector3 fingerWorld = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
            fingerWorld.z = 0f;
            var p = PropManager.Instance.FindNearestProp(agent.transform.position, propDropRadius);
            if (p == null || !p.IsBuilt || !p.HasDestinations)
                p = PropManager.Instance.FindNearestProp(fingerWorld, propDropRadius);
            return p != null && p.IsBuilt && p.HasDestinations ? p : null;
        }

        private PropSlot FindDropProp(CharacterAgent agent, Vector2 screenPos)
        {
            if (targetCamera == null || PropManager.Instance == null || agent == null) return null;
            float depth = -targetCamera.transform.position.z;
            Vector3 fingerWorld = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
            fingerWorld.z = 0f;

            // 빈 기물(착석) → 점유 기물(옆 배치) → 타 엔딩 기물(거절 연출)
            var atChar = PropManager.Instance.FindNearestDropTarget(agent, agent.transform.position, propDropRadius, allowOccupied: false);
            if (atChar != null) return atChar;
            atChar = PropManager.Instance.FindNearestDropTarget(agent, fingerWorld, propDropRadius, allowOccupied: false);
            if (atChar != null) return atChar;

            atChar = PropManager.Instance.FindNearestDropTarget(agent, agent.transform.position, propDropRadius, allowOccupied: true);
            if (atChar != null) return atChar;
            atChar = PropManager.Instance.FindNearestDropTarget(agent, fingerWorld, propDropRadius, allowOccupied: true);
            if (atChar != null) return atChar;

            atChar = PropManager.Instance.FindNearestDropTarget(
                agent, agent.transform.position, propDropRadius, allowOccupied: true, allowEndingRefuse: true);
            if (atChar != null && atChar.IsForbiddenEndingFor(agent)) return atChar;
            atChar = PropManager.Instance.FindNearestDropTarget(
                agent, fingerWorld, propDropRadius, allowOccupied: true, allowEndingRefuse: true);
            if (atChar != null && atChar.IsForbiddenEndingFor(agent)) return atChar;
            return null;
        }

        /// <summary>앉히기 실패 시 — 캐릭터/손가락 근처에 자물쇠(미건립)가 있으면 반환.</summary>
        PropSlot FindUnbuiltDropProp(CharacterAgent agent, Vector2 screenPos)
        {
            if (targetCamera == null || PropManager.Instance == null || agent == null) return null;
            float depth = -targetCamera.transform.position.z;
            Vector3 fingerWorld = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
            fingerWorld.z = 0f;

            float radius = Mathf.Max(propDropRadius, lockTapRadius);
            var atChar = PropManager.Instance.FindNearestUnbuiltProp(agent.transform.position, radius);
            if (atChar != null) return atChar;
            return PropManager.Instance.FindNearestUnbuiltProp(fingerWorld, radius);
        }

        private PropSlot FindNearestProp(Vector2 screenPos)
        {
            if (targetCamera == null || PropManager.Instance == null) return null;
            float depth = -targetCamera.transform.position.z;
            Vector3 world = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
            world.z = 0f;
            // 자물쇠는 더 넓은 반경으로 먼저 찾고, 없으면 일반 반경
            var locked = PropManager.Instance.FindNearestUnbuiltProp(world, lockTapRadius);
            if (locked != null) return locked;
            return PropManager.Instance.FindNearestProp(world, propTapRadius);
        }

        bool IsOverWillow(Vector2 screenPos)
        {
            if (targetCamera == null || MeritWillow.Instance == null) return false;
            float depth = -targetCamera.transform.position.z;
            Vector3 world = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
            return MeritWillow.Instance.HitTest(world, 0.05f);
        }

        /// <summary>보관 라벨(***·숫자) TextMesh 히트 — 라벨이 기물 bounds 밖으로 나와 있어도 그 기물로.</summary>
        PropSlot FindNearestPileLabel(Vector2 screenPos)
        {
            if (targetCamera == null || PropManager.Instance == null) return null;
            float depth = -targetCamera.transform.position.z;
            Vector3 world = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
            world.z = 0f;

            PropSlot best = null;
            float bestScore = float.MaxValue;
            var all = PropManager.Instance.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null) continue;
                if (!p.TryGetPileLabelHitScore(world, pileLabelTapPadding, out float score)) continue;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }

        private static bool IsBlockingUi(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;

            var eventData = new PointerEventData(EventSystem.current) { position = screenPos };
            UiRaycastHits.Clear();
            EventSystem.current.RaycastAll(eventData, UiRaycastHits);

            for (int i = 0; i < UiRaycastHits.Count; i++)
            {
                var go = UiRaycastHits[i].gameObject;
                if (go == null || !go.activeInHierarchy) continue;

                if (go.GetComponentInParent<Selectable>() != null)
                    return true;

                var rt = go.transform as RectTransform;
                if (rt != null
                    && rt.anchorMin == Vector2.zero
                    && rt.anchorMax == Vector2.one
                    && go.GetComponent<Graphic>() != null)
                    return true;
            }
            return false;
        }

        private static bool TryReadPointer(out Vector2 screenPos, out bool pressed)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var touch = touchscreen.primaryTouch;
                var touchPhase = touch.phase.ReadValue();
                bool touchDown = touchPhase == UnityEngine.InputSystem.TouchPhase.Began
                                 || touchPhase == UnityEngine.InputSystem.TouchPhase.Moved
                                 || touchPhase == UnityEngine.InputSystem.TouchPhase.Stationary;
                if (touchDown || touch.press.isPressed)
                {
                    screenPos = touch.position.ReadValue();
                    pressed = true;
                    return true;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                screenPos = mouse.position.ReadValue();
                pressed = mouse.leftButton.isPressed;
                return true;
            }

            screenPos = default;
            pressed = false;
            return false;
        }
    }
}
