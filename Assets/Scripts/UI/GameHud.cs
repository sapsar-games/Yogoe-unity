using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Minigames.Yut;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 메인 HUD (상단 재화 바 + 하단 슬롯바).
    /// 셸은 Prefab만 사용. 슬롯 칩은 항상 코드로 갱신.
    /// </summary>
    public class GameHud : SingletonBehaviour<GameHud>
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

        /// <summary>일괄 수거 만땅: 광고 2배 / 그냥 받기 (공덕은 일괄 대기분으로 옮겨진 상태).</summary>
        void HandleBatchMeritFull(Vector3 worldPos)
        {
            if (DualActionPopup.Instance != null)
            {
                DualActionPopup.Instance.OpenBatchFromWorld(worldPos);
                return;
            }
            var before = GameEconomy.Instance.MeritPile;
            if (!GameEconomy.Instance.TryClaimBatchMerit(1)) return;
            BeginMeritCountUpPublic(before, GameEconomy.Instance.MeritPile);
            PlayMeritCollectFxFromWorld(worldPos);
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

        const string BatchCollectButtonName = "BatchCollectButton";

        /// <summary>떡절구 공덕 일괄 수거 버튼 — CurrencyRow 아래, 평소엔 숨김. 코드로만 만든다.</summary>
        void EnsureBatchCollectButton()
        {
            if (hudCanvas == null) return;
            if (batchCollectButton == null)
                batchCollectButton = CreateBatchCollectButton(hudCanvas.transform);

            batchCollectButton.onClick.RemoveAllListeners();
            batchCollectButton.onClick.AddListener(OnBatchCollectClicked);
            batchCollectButton.gameObject.SetActive(false);
        }

        Button CreateBatchCollectButton(Transform parent)
        {
            var go = new GameObject(BatchCollectButtonName);
            var rt = SetupRect(go, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(300f, 70f));

            var img = go.AddComponent<Image>();
            img.color = C.hudUpgradeButton;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var labelGO = new GameObject("Label");
            var labelRt = labelGO.AddComponent<RectTransform>();
            labelRt.SetParent(rt, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var label = labelGO.AddComponent<Text>();
            label.font = font;
            label.fontSize = UiFonts.HudCurrency;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = C.hudUpgradeCost;
            label.text = "공덕 일괄 수거";
            label.raycastTarget = false;

            return btn;
        }

        /// <summary>앱 시작/복귀 시점 — 모아 둔(또는 전에 못 받은) 공덕이 있을 때만 버튼을 띄운다.</summary>
        void ShowBatchCollectButtonIfAny()
        {
            if (batchCollectButton == null) return;
            bool any = MeritBatchCollect.HasMerit
                || (GameEconomy.Instance != null && GameEconomy.Instance.HasPendingBatchMerit);
            if (any) batchCollectButton.gameObject.SetActive(true);
        }

        void OnBatchCollectClicked()
        {
            if (batchCollectButton == null) return;
            MeritBatchCollect.Collect(UiChipToWorld((RectTransform)batchCollectButton.transform, Camera.main));
            batchCollectButton.gameObject.SetActive(false);
        }

        /// <summary>공덕 수거 연출 타겟 — 공덕 칩이 없으면 엽전/CurrencyRow.</summary>
        RectTransform MeritFxTargetRt()
        {
            if (meritTextRt != null) return meritTextRt;
            var chip = ChipRt(yeopjeonText);
            if (chip != null) return chip;
            return currencyRow;
        }

        /// <summary>공덕 수거 연출 타겟(상단 재화)으로 꽃잎 Gather.</summary>
        public void PlayMeritCollectFx(RectTransform from)
        {
            var to = MeritFxTargetRt();
            if (hudCanvas == null || to == null || from == null) return;
            MeritCollectFx.Play(hudCanvas, from, to, font);
        }

        public void PlayMeritCollectFxFromWorld(Vector3 worldPos)
        {
            var to = MeritFxTargetRt();
            if (hudCanvas == null || to == null) return;
            MeritCollectFx.PlayFromWorld(hudCanvas, worldPos, to);
        }

        /// <summary>윷 복귀 아이템 비행 목표(월드). 공양물·광고권은 공덕 칩, 없으면 엽전.</summary>
        public Vector3 GetLootFlyTargetWorld(YutSquareRewardKind kind, Camera worldCam = null)
        {
            RectTransform rt = kind switch
            {
                YutSquareRewardKind.Yeopjeon => ChipRt(yeopjeonText),
                _ => MeritFxTargetRt(),
            };
            return UiChipToWorld(rt, worldCam);
        }

        static RectTransform ChipRt(Text text) =>
            text != null ? text.rectTransform : null;

        static Vector3 UiChipToWorld(RectTransform ui, Camera worldCam)
        {
            if (worldCam == null) worldCam = Camera.main;
            if (ui == null || worldCam == null) return Vector3.zero;

            Canvas canvas = ui.GetComponentInParent<Canvas>();
            Camera uiCam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                uiCam = canvas.worldCamera;

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCam, ui.TransformPoint(ui.rect.center));
            float z = Mathf.Abs(worldCam.transform.position.z);
            Vector3 w = worldCam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, z));
            w.z = 0f;
            return w;
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

        // ── 화면 감상 (30초 무입력 — v1.3 §7) ─────────────────────────────
        const float IdleHideSeconds = 30f;
        const float HideSlideSeconds = 0.3f;

        RectTransform idleCurrencyRowRt;
        RectTransform idleSlotBarRt;
        Vector2 idleCurrencyRowShownPos;
        Vector2 idleSlotBarShownPos;
        /// <summary>0 = 제자리, 1 = 완전히 숨김. MoveTowards 로만 움직여서 방향이 바뀌어도 끊기지 않는다.</summary>
        float uiHideAmount;
        float idleTimer;

        /// <summary>UI가 완전히 숨겨진 상태인지 — 숨김 상태의 첫 탭은 복귀만 하고 다른 동작으로
        /// 이어지지 않아야 해서 <see cref="MapPointerRouter"/>가 이걸로 그 탭을 삼킨다.</summary>
        public bool IsUiHidden => uiHideAmount >= 1f;

        void CacheIdleHideTargets()
        {
            if (idleCurrencyRowRt != null || idleSlotBarRt != null) return;
            idleCurrencyRowRt = currencyRow;
            idleSlotBarRt = slotBarRoot as RectTransform;
            if (idleCurrencyRowRt != null) idleCurrencyRowShownPos = idleCurrencyRowRt.anchoredPosition;
            if (idleSlotBarRt != null) idleSlotBarShownPos = idleSlotBarRt.anchoredPosition;
        }

        void TickIdleHide()
        {
            CacheIdleHideTargets();
            if (idleCurrencyRowRt == null && idleSlotBarRt == null) return;

            bool blocked = UiBlockGate.IsBlocking
                || (PropPanel.Instance != null && PropPanel.Instance.IsOpen)
                || (FeedPopup.Instance != null && FeedPopup.Instance.IsOpen)
                || (ConfirmPopup.Instance != null && ConfirmPopup.Instance.IsOpen)
                || CodexScreen.IsShowing
                || (PierScreen.Instance != null && PierScreen.Instance.IsOpen)
                || (PropUpgradePopup.Instance != null && PropUpgradePopup.Instance.IsOpen)
                || (SettingsPopup.Instance != null && SettingsPopup.Instance.IsOpen)
                || (DualActionPopup.Instance != null && DualActionPopup.Instance.IsOpen)
                || (GiftBundlePopup.Instance != null && GiftBundlePopup.Instance.IsOpen);

            float target;
            if (blocked || AnyPointerActive())
            {
                idleTimer = 0f;
                target = 0f;
            }
            else
            {
                idleTimer += Time.unscaledDeltaTime;
                target = idleTimer >= IdleHideSeconds ? 1f : 0f;
            }

            uiHideAmount = Mathf.MoveTowards(uiHideAmount, target, Time.unscaledDeltaTime / HideSlideSeconds);
            ApplyUiHideAmount(uiHideAmount);
        }

        static bool AnyPointerActive()
        {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.scroll.ReadValue() != Vector2.zero))
                return true;
            var ts = Touchscreen.current;
            if (ts != null)
            {
                var touches = ts.touches;
                for (int i = 0; i < touches.Count; i++)
                    if (touches[i].press.isPressed) return true;
            }
            return false;
        }

        void ApplyUiHideAmount(float amount)
        {
            if (idleCurrencyRowRt != null)
            {
                float offset = idleCurrencyRowRt.rect.height + 40f;
                idleCurrencyRowRt.anchoredPosition = idleCurrencyRowShownPos + new Vector2(0f, offset * amount);
            }
            if (idleSlotBarRt != null)
            {
                float offset = idleSlotBarRt.rect.height + 40f;
                idleSlotBarRt.anchoredPosition = idleSlotBarShownPos - new Vector2(0f, offset * amount);
            }
        }

        /// <summary>UI 숨김 상태에서 탭이 오면 복귀만 하고(true) 그 탭은 다른 동작으로 잇지 않는다.</summary>
        public bool ConsumeWakeTapIfHidden()
        {
            if (!IsUiHidden) return false;
            idleTimer = 0f;
            return true;
        }

        private void RefreshCurrencies()
        {
            var eco = GameEconomy.Instance;
            if (eco == null) return;

            if (meritText != null)
            {
                if (meritCounting)
                    TickMeritCountUp();
                else
                {
                    var merit = eco.MeritPile;
                    if (!hasLastMeritShown || !merit.Equals(lastMeritShown))
                    {
                        hasLastMeritShown = true;
                        lastMeritShown = merit;
                        meritText.text = "공덕 " + merit.ToDisplayString();
                    }
                }
            }
            if (yeopjeonText != null && eco.Yeopjeon != lastYeopjeon)
            {
                lastYeopjeon = eco.Yeopjeon;
                yeopjeonText.text = "엽전 " + lastYeopjeon;
            }
        }

        /// <summary>요괴 칩 뒤에 붙는 칸 (기획 2·9장: 3번째 빈 슬롯 → 고라니, 4번째 잠긴 칸 → 엽전 99 → 구미호).</summary>
        private enum ExtraSlot { None, SummonGorani, Locked, SummonGumiho }

        static readonly List<ExtraSlot> desiredExtras = new List<ExtraSlot>(3);

        static List<ExtraSlot> DesiredExtraSlots()
        {
            desiredExtras.Clear();
            if (!CharacterSummon.IsPresent(CharacterId.Gorani)) desiredExtras.Add(ExtraSlot.SummonGorani);
            if (!CharacterSummon.LockedSlotUnlocked) desiredExtras.Add(ExtraSlot.Locked);
            else if (!CharacterSummon.IsPresent(CharacterId.Gumiho)) desiredExtras.Add(ExtraSlot.SummonGumiho);
            return desiredExtras;
        }

        static readonly List<CharacterAgent> slotBarAgents = new List<CharacterAgent>(8);

        /// <summary>슬롯 바에 칩으로 보일 요괴 — 옥토끼는 NPC라 뺀다(기획 7-2 · v1.3).</summary>
        static List<CharacterAgent> SlotBarAgents()
        {
            slotBarAgents.Clear();
            var all = CharacterAgent.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Data == null || all[i].Data.id != CharacterId.Rabbit)
                    slotBarAgents.Add(all[i]);
            return slotBarAgents;
        }

        private void RefreshSlotBar()
        {
            var agents = SlotBarAgents();
            var extras = DesiredExtraSlots();
            int agentCount = agents.Count;
            bool mismatched = slotChips.Count != agentCount + extras.Count;
            for (int i = 0; !mismatched && i < slotChips.Count; i++)
            {
                var chip = slotChips[i];
                mismatched = i < agentCount
                    ? chip.IsSummonSlot || chip.Agent != agents[i]
                    : !chip.IsSummonSlot || chip.Extra != extras[i - agentCount];
            }
            if (mismatched) RebuildSlotBar();

            foreach (var chip in slotChips)
            {
                if (chip.IsSummonSlot || chip.Agent == null) continue;
                string name = chip.Agent.Data != null ? chip.Agent.Data.displayName : "?";
                if (chip.NameText != null && chip.LastDisplayName != name)
                {
                    chip.LastDisplayName = name;
                    chip.LastNameLabel = name;
                    chip.NameText.text = chip.LastNameLabel;
                }
                if (chip.StaminaFill != null && chip.StaminaFillRt != null)
                {
                    var state = chip.Agent.Stats.State;
                    bool alert = state == ActionState.Fainted;
                    float maxStamina = Mathf.Max(0.001f, chip.Agent.MaxStamina);
                    float ratio = alert ? 1f : Mathf.Clamp01(chip.Agent.Stats.Stamina / maxStamina);

                    bool flash = state == ActionState.Fainted;
                    bool ratioChanged = Mathf.Abs(ratio - chip.LastStaminaRatio) > 0.002f;
                    bool stateChanged = state != chip.LastStaminaState;
                    if (ratioChanged || stateChanged || flash)
                    {
                        chip.LastStaminaRatio = ratio;
                        chip.LastStaminaState = state;

                        var parentRt = chip.StaminaFillRt.parent as RectTransform;
                        float parentW = parentRt != null ? parentRt.rect.width : 130f;
                        if (parentW < 1f) parentW = 130f;

                        chip.StaminaFillRt.anchorMin = new Vector2(0f, 0f);
                        chip.StaminaFillRt.anchorMax = new Vector2(0f, 1f);
                        chip.StaminaFillRt.pivot = new Vector2(0f, 0.5f);
                        chip.StaminaFillRt.anchoredPosition = Vector2.zero;
                        chip.StaminaFillRt.sizeDelta = new Vector2(parentW * ratio, 0f);
                        chip.StaminaFillRt.localScale = Vector3.one;
                        chip.StaminaFill.color = CharacterStatusPresentation.ForStaminaBar(
                            state, Time.unscaledTime);
                    }
                }

                RefreshStatusTag(chip);
            }
        }

        private static void RefreshStatusTag(SlotChip chip)
        {
            if (chip.StatusTagRoot == null || chip.StatusTagText == null) return;

            var state = chip.Agent.Stats.State;
            if (state == chip.LastStatusState) return;
            chip.LastStatusState = state;

            var badge = CharacterStatusPresentation.ForSlot(state);
            chip.StatusTagRoot.SetActive(badge.Visible);
            if (!badge.Visible) return;

            chip.StatusTagText.text = badge.Label;
            if (chip.StatusTagBg != null) chip.StatusTagBg.color = badge.Color;
        }

        private void RebuildSlotBar()
        {
            foreach (Transform child in slotBarRoot) Destroy(child.gameObject);
            slotChips.Clear();

            foreach (var agent in SlotBarAgents())
            {
                var chipGO = new GameObject("Slot_" + (agent.Data != null ? agent.Data.displayName : "?"));
                var chipRt = SetupRect(chipGO, slotBarRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 100));
                var chipLayout = chipGO.AddComponent<LayoutElement>();
                chipLayout.preferredWidth = 160;
                chipLayout.preferredHeight = 100;

                var bg = chipGO.AddComponent<Image>();
                bg.color = C.hudSlotChip;

                var capturedAgent = agent;
                var button = chipGO.AddComponent<Button>();
                button.targetGraphic = bg;
                button.onClick.AddListener(() =>
                {
                    if (detailScreen != null) detailScreen.Open(capturedAgent);
                });

                var tagGO = new GameObject("StatusTag");
                SetupRect(tagGO, chipRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0f),
                    new Vector2(0, 4), new Vector2(72, 22));
                var tagBg = tagGO.AddComponent<Image>();
                tagBg.color = C.hudStatusTag;
                var tagTextGO = new GameObject("Label");
                SetupRect(tagTextGO, tagGO.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero);
                var tagText = tagTextGO.AddComponent<Text>();
                tagText.font = font;
                tagText.fontSize = UiFonts.Small;
                tagText.alignment = TextAnchor.MiddleCenter;
                tagText.color = Color.white;
                tagText.text = "일하는";
                tagText.raycastTarget = false;
                tagGO.SetActive(false);

                var nameGO = new GameObject("Name");
                SetupRect(nameGO, chipRt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f),
                    new Vector2(0, -6), new Vector2(-10, 26));
                var nameText = nameGO.AddComponent<Text>();
                nameText.font = font;
                nameText.fontSize = UiFonts.HudSlotName;
                nameText.alignment = TextAnchor.MiddleCenter;
                nameText.color = Color.white;
                nameText.raycastTarget = false;

                var barBgGO = new GameObject("StaminaBarBg");
                SetupRect(barBgGO, chipRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0, 10), new Vector2(130, 14));
                var barBg = barBgGO.AddComponent<Image>();
                barBg.color = C.hudStaminaTrack;

                var barFillGO = new GameObject("StaminaBarFill");
                var barFillRt = SetupRect(barFillGO, barBgGO.transform, Vector2.zero, Vector2.one,
                    new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
                var barFill = barFillGO.AddComponent<Image>();
                barFill.color = C.hudStaminaFill;
                barFill.raycastTarget = false;

                slotChips.Add(new SlotChip
                {
                    Agent = agent,
                    IsSummonSlot = false,
                    NameText = nameText,
                    StatusTagText = tagText,
                    StatusTagRoot = tagGO,
                    StatusTagBg = tagBg,
                    StaminaFill = barFill,
                    StaminaFillRt = barFillRt
                });
            }

            foreach (var extra in DesiredExtraSlots())
                AddExtraSlot(extra);
        }

        private void AddExtraSlot(ExtraSlot extra)
        {
            bool locked = extra == ExtraSlot.Locked;
            var summonTarget = extra == ExtraSlot.SummonGumiho ? CharacterId.Gumiho : CharacterId.Gorani;
            var chipGO = new GameObject(locked ? "Slot_Locked" : "Slot_Summon");
            var chipRt = SetupRect(chipGO, slotBarRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 100));
            var chipLayout = chipGO.AddComponent<LayoutElement>();
            chipLayout.preferredWidth = 160;
            chipLayout.preferredHeight = 100;

            var bg = chipGO.AddComponent<Image>();
            bg.color = locked ? C.hudSummonChip * new Color(0.6f, 0.6f, 0.6f, 1f) : C.hudSummonChip;

            var button = chipGO.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(() =>
            {
                if (SummonPopup.Instance == null) return;
                if (locked) SummonPopup.Instance.OpenUnlock();
                else SummonPopup.Instance.Open(summonTarget);
            });

            var nameGO = new GameObject("Name");
            SetupRect(nameGO, chipRt, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var nameText = nameGO.AddComponent<Text>();
            nameText.font = font;
            nameText.fontSize = UiFonts.HudSummonName;
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.color = C.hudSummonName;
            nameText.raycastTarget = false;
            nameText.text = locked ? "잠김\n엽전 " + CharacterSummon.LockedSlotYeopjeonCost : "+ 소환";
            if (locked) nameText.color = C.hudSummonName * new Color(0.75f, 0.75f, 0.75f, 1f);

            slotChips.Add(new SlotChip
            {
                Agent = null,
                IsSummonSlot = true,
                Extra = extra,
                NameText = nameText
            });
        }

        /// <summary>공덕 HUD 숫자를 from→to로 단계적으로 올린다 (일괄 수거 연출).</summary>
        public void BeginMeritCountUpPublic(BigNumber from, BigNumber to) => BeginMeritCountUp(from, to);

        private void BeginMeritCountUp(BigNumber from, BigNumber to)
        {
            if (meritText == null) return;
            if (from.Equals(to)) return;

            // 이미 카운트 중이면 현재 표시값에서 이어서 목표만 갱신
            if (meritCounting)
                meritAnimFrom = meritAnimShown;
            else
                meritAnimFrom = from;

            meritAnimTo = to;
            meritAnimShown = meritAnimFrom;
            meritAnimElapsed = 0f;
            meritCounting = true;
            hasLastMeritShown = true;
            lastMeritShown = meritAnimFrom;
            meritText.text = "공덕 " + meritAnimFrom.ToDisplayString();
        }

        private void TickMeritCountUp()
        {
            // 실제 지갑이 연출 목표와 어긋나면(소비·추가 수거) 즉시 실제값으로 맞춤
            var real = GameEconomy.Instance.MeritPile;
            if (!real.Equals(meritAnimTo))
            {
                // 목표가 더 커진 경우(연출 중 추가 수거)는 이어서 카운트
                if (real > meritAnimTo)
                {
                    meritAnimFrom = meritAnimShown;
                    meritAnimTo = real;
                    meritAnimElapsed = MeritCountDelay; // 딜레이 없이 바로 이어서
                }
                else
                {
                    EndMeritCountUp(real);
                    return;
                }
            }

            meritAnimElapsed += Time.unscaledDeltaTime;
            if (meritAnimElapsed < MeritCountDelay)
            {
                // 꽃잎이 흩날리는 동안은 시작값 유지
                if (!meritAnimShown.Equals(meritAnimFrom))
                {
                    meritAnimShown = meritAnimFrom;
                    lastMeritShown = meritAnimShown;
                    meritText.text = "공덕 " + meritAnimShown.ToDisplayString();
                }
                return;
            }

            float u = Mathf.Clamp01((meritAnimElapsed - MeritCountDelay) / MeritCountDuration);
            // easeOutCubic — 초반에 빠르게 오르다 끝에서 감속
            float e = 1f - (1f - u) * (1f - u) * (1f - u);
            var shown = meritAnimFrom + (meritAnimTo - meritAnimFrom) * e;

            if (!shown.Equals(meritAnimShown))
            {
                meritAnimShown = shown;
                lastMeritShown = shown;
                meritText.text = "공덕 " + shown.ToDisplayString();
            }

            if (u >= 1f)
                EndMeritCountUp(meritAnimTo);
        }

        private void EndMeritCountUp(BigNumber final)
        {
            meritCounting = false;
            meritAnimShown = final;
            lastMeritShown = final;
            hasLastMeritShown = true;
            if (meritText != null)
                meritText.text = "공덕 " + final.ToDisplayString();
        }

        // ---------------- 우하단 업그레이드 (8장) ----------------

        private void RefreshUpgradeButton()
        {
            if (upgradeButtonRoot == null) return;

            var target = PropEconomy.FindCheapestUpgradeTarget();
            if (target == null)
            {
                if (lastUpgradeVisible)
                {
                    lastUpgradeVisible = false;
                    upgradeButtonRoot.SetActive(false);
                }
                lastUpgradeTarget = null;
                upgradeHoldActive = false;
                return;
            }

            var cost = PropEconomy.GetUpgradeCost(target);
            bool canAfford = GameEconomy.Instance.MeritPile >= cost;
            string name = target.DisplayName;
            bool changed = !lastUpgradeVisible
                || target != lastUpgradeTarget
                || name != lastUpgradeName
                || !hasLastUpgradeCost
                || !cost.Equals(lastUpgradeCostValue)
                || canAfford != lastUpgradeCanAfford;

            if (!lastUpgradeVisible)
            {
                lastUpgradeVisible = true;
                upgradeButtonRoot.SetActive(true);
            }

            if (changed)
            {
                lastUpgradeTarget = target;
                lastUpgradeName = name;
                lastUpgradeCostValue = cost;
                hasLastUpgradeCost = true;
                lastUpgradeCanAfford = canAfford;

                if (upgradeNameText != null)
                    upgradeNameText.text = name;
                if (upgradeCostText != null)
                {
                    upgradeCostText.text = cost.ToDisplayString();
                    upgradeCostText.color = canAfford
                        ? C.hudUpgradeCost
                        : new Color(0.75f, 0.4f, 0.35f);
                }

                if (upgradeIconImage != null)
                {
                    var icon = target.data != null ? target.data.icon : null;
                    upgradeIconImage.sprite = icon;
                    upgradeIconImage.enabled = icon != null;
                    if (icon == null)
                        upgradeIconImage.color = new Color(0.55f, 0.45f, 0.3f, 1f);
                    else
                        upgradeIconImage.color = Color.white;
                }
            }
        }

        private void TickUpgradeHold()
        {
            if (!upgradeHoldActive) return;
            upgradeHoldTimer -= Time.unscaledDeltaTime;
            if (upgradeHoldTimer > 0f) return;
            if (!TryPerformUpgrade())
            {
                upgradeHoldActive = false;
                return;
            }
            upgradeHoldTimer = UpgradeHoldInterval;
        }

        private void OnUpgradePointerDown()
        {
            upgradeHoldActive = true;
            upgradeHoldTimer = UpgradeHoldInitialDelay;
            TryPerformUpgrade();
        }

        private void OnUpgradePointerUp()
        {
            upgradeHoldActive = false;
        }

        private bool TryPerformUpgrade()
        {
            var upgraded = PropEconomy.TryUpgradeCheapest();
            if (upgraded == null) return false;

            PropUpgradeFx.SpawnWorld(upgraded.transform.position, "+1 급", font);
            if (upgradeButtonRt != null)
                PropUpgradeFx.SpawnUi(upgradeButtonRt, upgraded.DisplayName + " +1급", font);
            GameSaveBridge.SaveFromWorld();
            RefreshUpgradeButton();
            return true;
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

        void WireUpgradeHoldTriggers()
        {
            if (upgradeButtonRoot == null) return;

            var trigger = upgradeButtonRoot.GetComponent<EventTrigger>();
            if (trigger == null) trigger = upgradeButtonRoot.AddComponent<EventTrigger>();
            trigger.triggers.Clear();

            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => OnUpgradePointerDown());
            trigger.triggers.Add(down);
            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => OnUpgradePointerUp());
            trigger.triggers.Add(up);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => OnUpgradePointerUp());
            trigger.triggers.Add(exit);
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
