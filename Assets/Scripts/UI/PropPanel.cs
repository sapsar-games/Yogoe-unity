using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Data;

namespace Yoegoe.UI
{
    /// <summary>
    /// 기물 창 (v1.3). 보관함이 빈 기물 탭 · ▲ 딱지 탭 · 사냥터·채집터에 요괴를 끌어다 놓을 때 열린다.
    /// - 빈 기물: (사냥터·채집터) 목적지 3곳 → 보낼 요괴 → [보내기] · 레벨업
    /// - 일하는 중: 요괴 · 어디서 · 보관함 · 기력 → [불러들이기] [받기 n개] · 레벨업
    /// 친밀도가 모자란 요괴를 고르면 "거기는 가기 싫어." 하고 필요한 친밀도를 보여 준다.
    /// 셸은 Prefab(Assets/Prefabs/UI/PropPanel.prefab), 목적지·요괴 칸 등 동적 콘텐츠만 코드로 만든다.
    ///
    /// 영역별로 파일을 나눠뒀다 — 모두 같은 partial class라 필드·헬퍼는 공유:
    /// - PropPanel.cs (여기): 필드, 생명주기(Start/Update), Open/Close
    /// - PropPanel.Actions.cs: 보내기/불러들이기/받기/레벨업 등 규칙
    /// - PropPanel.View.cs: 화면(body) 다시 그리기
    /// - PropPanel.Layout.cs: 범용 UI 레이아웃 헬퍼(Label/Card/Button/Bar 등)
    /// </summary>
    public partial class PropPanel : SingletonBehaviour<PropPanel>
    {
        public Font font;

        [SerializeField] GameObject root;
        [SerializeField] RectTransform body;
        [SerializeField] Text titleText;
        [SerializeField] Text subText;
        [SerializeField] Button dimButton;
        [SerializeField] Button closeButton;

        PropSlot prop;             // 지금 열려 있는 기물 (허브가 아니라 실제 앉을 자리로 치환된 뒤의 값)
        string selectedDest;       // 고른 목적지 id (사냥터·채집터일 때만 의미 있음)
        CharacterAgent selectedAgent; // 보낼 요괴로 고른 카드
        CharacterAgent refusing;   // 방금 "거기는 가기 싫어"라고 거절한 요괴 (말풍선 잠깐 보여주는 용도)
        float refuseUntil;         // refusing을 지울 시각 (Time.unscaledTime 기준)

        // 카드·버튼 배경색 팔레트
        static readonly Color TextColor = new Color(1f, 0.95f, 0.85f);
        static readonly Color DimText = new Color(0.85f, 0.78f, 0.66f);
        static readonly Color CardOff = new Color(0.27f, 0.21f, 0.16f, 1f);
        static readonly Color CardOn = new Color(0.62f, 0.45f, 0.2f, 1f);
        static readonly Color CardDisabled = new Color(0.2f, 0.17f, 0.15f, 1f);
        static readonly Color Gold = new Color(0.95f, 0.75f, 0.25f, 1f);
        static readonly Color Warn = new Color(1f, 0.55f, 0.45f, 1f);
        static readonly Color Btn = new Color(0.3f, 0.5f, 0.45f, 1f);
        static readonly Color BtnOff = new Color(0.3f, 0.28f, 0.26f, 1f);

        float refreshTimer;
        bool lastOccupied;

        string levelNote; // 레벨업 직후 한 번만 보여줄 안내 문구 (다음 Rebuild에서 지워짐)

        protected override void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            base.Awake();
            if (root != null) root.SetActive(false);
            MapPointerRouter.PropPanelRequested += Open;
        }

        void Start()
        {
            UiButtonUtils.Bind(dimButton, Close);
            UiButtonUtils.Bind(closeButton, Close);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            MapPointerRouter.PropPanelRequested -= Open;
        }

        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>기물 창 열기. preselect = 끌어다 놓은 요괴(미리 골라 둠).</summary>
        public void Open(PropSlot target, CharacterAgent preselect)
        {
            if (target == null || !target.IsBuilt || !target.AcceptsWorkers || root == null) return;
            prop = ResolveOpenTarget(target);
            prop.ApplyFixedDestination();
            var dests = PropCatalog.DestinationsFor(prop.ResourceType);
            selectedDest = prop.DestinationId;
            if (prop.NeedsDestinationPick && (selectedDest == null || PropCatalog.FindDestination(selectedDest) == null))
                selectedDest = dests.Count > 0 ? dests[0].id : null;
            else if (prop.HasFixedDestination)
                selectedDest = prop.DestinationId;
            selectedAgent = preselect;
            refusing = null;
            root.SetActive(true);
            Rebuild();
            // 끌어다 놓은 요괴가 못 가는 곳이면 바로 물어본 것처럼
            if (preselect != null && WhyNot(preselect, out bool refuse) != null && refuse) Refuse(preselect);
        }

        /// <summary>사냥터·채집터(허브)를 열었는데 그 목적지 중 하나가 이미 일하는 중이면
        /// 그 개별 기물(멧돼지 앞산 등)을 바로 보여준다 — 허브 자신은 앉는 자리가 아니다.
        /// 개별 기물을 직접 열었으면(일하는 중 탭 등) 그대로.</summary>
        static PropSlot ResolveOpenTarget(PropSlot target)
        {
            if (!target.NeedsDestinationPick || PropManager.Instance == null) return target;
            foreach (var p in PropManager.Instance.All)
                if (p != null && p != target && p.ResourceType == target.ResourceType
                    && (p.IsOccupied || p.HasPendingCollectible)) return p;
            return target;
        }

        /// <summary>창 닫기. 선택 상태(selectedAgent 등)도 같이 비운다.</summary>
        public void Close()
        {
            if (root != null) root.SetActive(false);
            prop = null;
            selectedAgent = null;
        }

        /// <summary>창이 열려 있는 동안 1초마다 내용을 갱신 (보관함·기력 등은 시간이 지나면 바뀌므로).
        /// 일하는 중(IsOccupied)이 아니면 누르던 칸이 사라지지 않게 상태가 실제로 바뀔 때만 다시 그린다.</summary>
        void Update()
        {
            if (!IsOpen || prop == null) return;
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = 1f;
            bool changed = false;
            if (refusing != null && Time.unscaledTime > refuseUntil) { refusing = null; changed = true; }
            // 일하는 중엔 1초마다 보관함·기력 갱신. 빈 기물은 고르는 중이라 상태가 바뀔 때만 (누르는 중 칸이 사라지지 않게)
            // 손가락이 눌린 동안엔 다시 그리지 않는다 — 누르던 버튼이 사라지면 탭이 씹히므로
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer != null && pointer.press.isPressed) return;
            if (prop.IsOccupied || prop.IsOccupied != lastOccupied || changed) Rebuild();
        }
    }

    /// <summary>기물 창 문구 (시트로 옮길 후보).</summary>
    static class GameSettingsText
    {
        public const string Refuse = "거기는 가기 싫어.";
    }
}
