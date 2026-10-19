using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Economy;

namespace Yoegoe.UI
{
    /// <summary>
    /// 메인 HUD (상단 재화 바 + 하단 슬롯바).
    /// 셸은 Prefab만 사용. 슬롯 칩은 항상 코드로 갱신.
    ///
    /// 영역별로 파일을 나눠뒀다 — 모두 같은 partial class라 필드·헬퍼는 공유:
    /// - GameHud.cs (여기): 필드, 생명주기(OnEnable/Start/Update), 셸/배선
    /// - GameHud.IdleHide.cs: 30초 무입력 시 UI 숨김
    /// - GameHud.Currencies.cs: 재화 표시 + 하단 슬롯바(캐릭터 로스터) 갱신
    /// - GameHud.Merit.cs: 공덕 수거 연출 + 카운트업 애니메이션 + 일괄수거 버튼
    /// - GameHud.Upgrade.cs: 우하단 업그레이드 롱프레스
    /// </summary>
    public partial class GameHud : SingletonBehaviour<GameHud>
    {

        // 색·글자 크기: Resources/UiStyleSettings.asset
        UiStyleSettings Style => UiStyleSettings.Get();
        UiStyleSettings.Colors C => Style.colors;

        [Header("주입 (Main)")]
        public Font font;
        public Sprite waterIcon;
        public DetailScreen detailScreen;

        [Header("셸 (Prefab/씬 — Inspector에서 연결)")]
        [SerializeField] Canvas hudCanvas;
        [SerializeField] RectTransform currencyRow;
        [SerializeField] Text meritText;
        [SerializeField] RectTransform meritTextRt;
        [SerializeField] Text yeopjeonText;
        [SerializeField] Button settingsButton;
        [SerializeField] Transform slotBarRoot;
        [SerializeField] GameObject upgradeButtonRoot;
        [SerializeField] RectTransform upgradeButtonRt;
        [SerializeField] Image upgradeIconImage;
        [SerializeField] Text upgradeNameText;
        [SerializeField] Text upgradeCostText;

        /// <summary>Prefab/씬에 HUD 셸이 이미 연결돼 있는지. 상단은 CurrencyRow만 있으면 됨.</summary>
        public bool HasPrefabShell =>
            hudCanvas != null && slotBarRoot != null && currencyRow != null;

        private bool upgradeHoldActive;
        private float upgradeHoldTimer;
        private const float UpgradeHoldInitialDelay = 0.35f;
        private const float UpgradeHoldInterval = 0.12f;

        /// <summary>버드나무 삭제(2026-10-13)로 대체된 떡절구 공덕 일괄 수거 — 앱을 켜거나 오래 비웠다 돌아왔을 때만 뜬다.</summary>
        Button batchCollectButton;
        const string BatchCollectButtonName = "BatchCollectButton";

        private readonly List<SlotChip> slotChips = new List<SlotChip>();

        private class SlotChip
        {
            public CharacterAgent Agent;
            /// <summary>요괴가 아닌 칸(빈 소환 슬롯·잠긴 슬롯).</summary>
            public bool IsSummonSlot;
            public ExtraSlot Extra;
            public Text NameText;
            public Text StatusTagText;
            public GameObject StatusTagRoot;
            public Image StatusTagBg;
            public Image StaminaFill;
            public RectTransform StaminaFillRt;
            public string LastNameLabel;
            public string LastDisplayName;
            public ActionState LastStatusState = (ActionState)(-1);
            public float LastStaminaRatio = -1f;
            public ActionState LastStaminaState = (ActionState)(-1);
        }

        /// <summary>요괴 칩 뒤에 붙는 칸 (기획 2·9장: 3번째 빈 슬롯 → 고라니, 4번째 잠긴 칸 → 엽전 99 → 구미호).</summary>
        private enum ExtraSlot { None, SummonGorani, Locked, SummonGumiho }

        private BigNumber lastMeritShown;
        private bool hasLastMeritShown;
        /// <summary>일괄 수거 시 HUD 공덕 숫자 카운트업.</summary>
        private bool meritCounting;
        private BigNumber meritAnimFrom;
        private BigNumber meritAnimTo;
        private BigNumber meritAnimShown;
        private float meritAnimElapsed;
        // MeritCollectFx: 첫 꽃잎 도착 ≈ Scatter(0.4)+Hover(0.28), 마지막까지 ≈ Stagger*17+Fly(0.55)
        private const float MeritCountDelay = 0.68f;
        private const float MeritCountDuration = 1.35f;
        private int lastYeopjeon = int.MinValue;
        private string lastUpgradeName;
        private BigNumber lastUpgradeCostValue;
        private bool hasLastUpgradeCost;
        private bool lastUpgradeVisible;
        private PropSlot lastUpgradeTarget;
        private bool lastUpgradeCanAfford;

        private void OnEnable()
        {
            MapPointerRouter.CharacterDetailRequested += HandleCharacterDetailRequested;
            PropSlot.MeritCollectedAtWorld += PlayMeritCollectFxFromWorld;
            MeritBatchCollect.FullRequested += HandleBatchMeritFull;
            Greeting.Returned += ShowBatchCollectButtonIfAny;
        }

        private void OnDisable()
        {
            MapPointerRouter.CharacterDetailRequested -= HandleCharacterDetailRequested;
            PropSlot.MeritCollectedAtWorld -= PlayMeritCollectFxFromWorld;
            MeritBatchCollect.FullRequested -= HandleBatchMeritFull;
            Greeting.Returned -= ShowBatchCollectButtonIfAny;
        }

        private void HandleCharacterDetailRequested(CharacterAgent agent, string highlight)
        {
            if (detailScreen == null) return;
            detailScreen.Open(agent, highlight);
        }

        private void Start()
        {
            EnsureHudShell();
            EnsureSettingsButton();
            EnsureBatchCollectButton();
            WireRuntimeListeners();
            RefreshCurrencies();
            RefreshUpgradeButton();
        }

        /// <summary>
        /// Prefab 셸만 사용. 없으면 에러 (런타임 생성 없음).
        /// </summary>
        public void EnsureHudShell()
        {
            if (HasPrefabShell) return;
            Debug.LogError(
                "[GameHud] Prefab 셸이 없습니다. Main 씬에 GameHud Prefab 인스턴스를 배치하세요.");
        }

        /// <summary>상단 오른쪽 [설정] — Prefab에 고정 배치된 settingsButton의 클릭을 연결한다.</summary>
        void EnsureSettingsButton()
        {
            if (settingsButton == null) return;
            settingsButton.onClick.RemoveAllListeners();
            settingsButton.onClick.AddListener(() =>
            {
                if (SettingsPopup.Instance != null) SettingsPopup.Instance.Open();
            });
        }

        private void Update()
        {
            TickIdleHide();

            if (GameEconomy.Instance == null) return;

            RefreshCurrencies();
            RefreshSlotBar();
            RefreshUpgradeButton();
            TickUpgradeHold();
        }

        // ---------------- 런타임 배선 ----------------

        void WireRuntimeListeners()
        {
            // ShopButton/YutButton: Assets/Scripts/UI/UiNavigationButton.cs가 프리팹에 셀프와이어링되어 있음
            // (Yoegoe/Bake GameHud Navigation Buttons로 베이킹) — 여기서 더 와이어링하지 않음.

            WireTempDebugButtons();
            WireUpgradeHoldTriggers();
        }

        /// <summary>예전 HUD 테스트 버튼은 설정 팝업으로 옮김 — Prefab에 남아 있으면 숨긴다.</summary>
        void WireTempDebugButtons()
        {
            if (hudCanvas == null) return;
            HideIfExists(hudCanvas.transform, "TempResetButton");
            HideIfExists(hudCanvas.transform, "TempClearCacheButton");
            HideIfExists(hudCanvas.transform, "TempSkipTimeButton");
            HideIfExists(hudCanvas.transform, "TempTimeScale2x");
            HideIfExists(hudCanvas.transform, "TempTimeScale4x");
            HideIfExists(hudCanvas.transform, "TempTimeScale8x");
            HideIfExists(hudCanvas.transform, "TempTimeScale16x");
        }

        static void HideIfExists(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) t.gameObject.SetActive(false);
        }

        private static RectTransform SetupRect(GameObject go, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return rt;
        }
    }
}
