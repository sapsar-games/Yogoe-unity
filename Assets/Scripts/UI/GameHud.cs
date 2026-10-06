using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
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
    public class GameHud : MonoBehaviour
    {
        public static GameHud Instance { get; private set; }

        // 색·글자 크기: Resources/UiStyleSettings.asset
        UiStyleSettings Style => UiStyleSettings.Get();
        UiStyleSettings.Colors C => Style.colors;

        [Header("주입 (Main)")]
        public Font font;
        public Sprite waterIcon;
        public DetailScreen detailScreen;

        [Header("셸 (Prefab/씬 — 비어 있으면 Play 시 코드 조립)")]
        [SerializeField] Canvas hudCanvas;
        [SerializeField] Text meritText;
        [SerializeField] RectTransform meritTextRt;
        [SerializeField] Text yeopjeonText;
        [SerializeField] Text hyangText;
        [SerializeField] Text waterText;
        [SerializeField] Text yutTokenText;
        [SerializeField] Button yutTokenPlusButton;
        [SerializeField] Button tempResetButton;
        [SerializeField] Button tempClearCacheButton;
        [SerializeField] Button shopButton;
        [SerializeField] Button yutButton;
        [SerializeField] Transform slotBarRoot;
        [SerializeField] GameObject upgradeButtonRoot;
        [SerializeField] RectTransform upgradeButtonRt;
        [SerializeField] Image upgradeIconImage;
        [SerializeField] Text upgradeNameText;
        [SerializeField] Text upgradeCostText;

        /// <summary>Prefab/씬에 HUD 셸이 이미 연결돼 있는지.</summary>
        public bool HasPrefabShell =>
            hudCanvas != null && slotBarRoot != null && meritText != null;

        private bool upgradeHoldActive;
        private float upgradeHoldTimer;
        private const float UpgradeHoldInitialDelay = 0.35f;
        private const float UpgradeHoldInterval = 0.12f;

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
            public GameObject BatchButtonRoot;
            public Text BatchButtonLabel;
            public string LastNameLabel;
            public string LastDisplayName;
            public ActionState LastStatusState = (ActionState)(-1);
            public bool LastBatchVisible;
            public string LastBatchLabel;
            public BigNumber LastBatchAmount;
            public bool HasLastBatchAmount;
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
        private int lastHyang = int.MinValue;
        private int lastWater = int.MinValue;
        private int lastYutToken = int.MinValue;
        private int lastYutTokenMax = int.MinValue;
        private string lastUpgradeName;
        private BigNumber lastUpgradeCostValue;
        private bool hasLastUpgradeCost;
        private bool lastUpgradeVisible;
        private PropSlot lastUpgradeTarget;
        private bool lastUpgradeCanAfford;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            MapPointerRouter.CharacterDetailRequested += HandleCharacterDetailRequested;
            PropSlot.MeritCollectedAtWorld += PlayMeritCollectFxFromWorld;
            MeritWillow.FullTapped += HandleWillowFullTapped;
        }

        private void OnDisable()
        {
            MapPointerRouter.CharacterDetailRequested -= HandleCharacterDetailRequested;
            PropSlot.MeritCollectedAtWorld -= PlayMeritCollectFxFromWorld;
            MeritWillow.FullTapped -= HandleWillowFullTapped;
        }

        /// <summary>버드나무 만땅 탭: 광고 2배 / 그냥 받기 (공덕은 일괄 대기분으로 옮겨진 상태).</summary>
        void HandleWillowFullTapped(Vector3 worldPos)
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

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void HandleCharacterDetailRequested(CharacterAgent agent, string highlight)
        {
            if (detailScreen == null) return;
            detailScreen.Open(agent, highlight);
        }

        private void Start()
        {
            EnsureHudShell();
            EnsureYutTokenPlusButton();
            EnsureSettingsButton();
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

        /// <summary>재화 칩 4개 + [+] 버튼이 한 줄에 다 들어가는 데 필요한 TopBar 최소 폭.</summary>
        const float MinTopBarWidth = 750f;

        /// <summary>
        /// 윷 토큰 칩 옆 [+] 버튼 — Prefab에 이미 있으면 그대로 쓰고, 예전 Prefab에도
        /// 이름으로 찾아 붙여서 항상 나타나게 한다.
        /// </summary>
        const string SettingsButtonName = "SettingsButton";
        const float SettingsButtonWidth = 104f;

        /// <summary>
        /// 상단 오른쪽 [설정] — 상점 버튼을 복제해 같은 모양으로 만들고, 상점 버튼은 그 왼쪽으로 민다.
        /// 프리팹에 없어도(구운 HUD) 매번 Start 에서 찾거나 만든다.
        /// </summary>
        void EnsureSettingsButton()
        {
            if (shopButton == null) return;
            var parent = shopButton.transform.parent;
            var existing = parent.Find(SettingsButtonName);
            Button btn;
            if (existing != null) btn = existing.GetComponent<Button>();
            else
            {
                var go = Instantiate(shopButton.gameObject, parent);
                go.name = SettingsButtonName;
                btn = go.GetComponent<Button>();
                var rt = (RectTransform)go.transform;
                var shopRt = (RectTransform)shopButton.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(SettingsButtonWidth, shopRt.sizeDelta.y);
                rt.anchoredPosition = shopRt.anchoredPosition;
                var label = go.GetComponentInChildren<Text>(true);
                if (label != null) label.text = "설정";
                // 상점 버튼은 설정 버튼 왼쪽으로
                shopRt.anchoredPosition = shopRt.anchoredPosition + new Vector2(-(SettingsButtonWidth + 12f), 0f);
            }
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                if (SettingsPopup.Instance != null) SettingsPopup.Instance.Open();
            });
        }

        void EnsureYutTokenPlusButton()
        {
            if (yutTokenPlusButton == null)
            {
                var row = hudCanvas != null ? hudCanvas.transform.Find("TopBar/CurrencyRow") : null;
                if (row != null)
                {
                    var existing = row.Find("YutTokenPlus");
                    yutTokenPlusButton = existing != null
                        ? existing.GetComponent<Button>()
                        : CreateYutTokenPlusButton(row);
                }
            }

            // 예전 Prefab은 재화 칩 4개 기준 폭(690)으로 굳어 있어서, [+] 버튼이 추가된 뒤로는
            // 오른쪽 칩(윷 토큰 등)이 잘려 보인다 — 최소 폭만 보장(더 넓게 손댔으면 안 줄임).
            var topBarRt = hudCanvas != null ? hudCanvas.transform.Find("TopBar") as RectTransform : null;
            if (topBarRt != null && topBarRt.sizeDelta.x < MinTopBarWidth)
                topBarRt.sizeDelta = new Vector2(MinTopBarWidth, topBarRt.sizeDelta.y);
        }

        /// <summary>공덕 수거 연출 타겟(상단 공덕 텍스트)으로 꽃잎 Gather.</summary>
        public void PlayMeritCollectFx(RectTransform from)
        {
            if (hudCanvas == null || meritTextRt == null || from == null) return;
            MeritCollectFx.Play(hudCanvas, from, meritTextRt, font);
        }

        public void PlayMeritCollectFxFromWorld(Vector3 worldPos)
        {
            if (hudCanvas == null || meritTextRt == null) return;
            MeritCollectFx.PlayFromWorld(hudCanvas, worldPos, meritTextRt);
        }

        /// <summary>윷 복귀 아이템 비행 목표(월드). 공양물·광고권은 공덕 칩, 없으면 상단 중앙.</summary>
        public Vector3 GetLootFlyTargetWorld(YutSquareRewardKind kind, Camera worldCam = null)
        {
            RectTransform rt = kind switch
            {
                YutSquareRewardKind.Yeopjeon => ChipRt(yeopjeonText),
                YutSquareRewardKind.Hyang => ChipRt(hyangText),
                YutSquareRewardKind.Water => ChipRt(waterText),
                YutSquareRewardKind.YutToken => ChipRt(yutTokenText),
                _ => meritTextRt != null ? meritTextRt : ChipRt(yeopjeonText),
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
            if (GameEconomy.Instance == null) return;

            RefreshCurrencies();
            RefreshSlotBar();
            RefreshUpgradeButton();
            TickUpgradeHold();
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
            if (hyangText != null && eco.Hyang != lastHyang)
            {
                lastHyang = eco.Hyang;
                hyangText.text = "향 " + lastHyang;
            }
            if (waterText != null && eco.Water != lastWater)
            {
                lastWater = eco.Water;
                waterText.text = "물 " + lastWater;
            }
            if (yutTokenText != null
                && (eco.YutToken != lastYutToken || eco.YutTokenMax != lastYutTokenMax))
            {
                lastYutToken = eco.YutToken;
                lastYutTokenMax = eco.YutTokenMax;
                yutTokenText.text = "윷 " + lastYutToken + "/" + lastYutTokenMax;
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

        private void RefreshSlotBar()
        {
            var extras = DesiredExtraSlots();
            int agentCount = CharacterAgent.All.Count;
            bool mismatched = slotChips.Count != agentCount + extras.Count;
            for (int i = 0; !mismatched && i < slotChips.Count; i++)
            {
                var chip = slotChips[i];
                mismatched = i < agentCount
                    ? chip.IsSummonSlot || chip.Agent != CharacterAgent.All[i]
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
                RefreshBatchButton(chip);
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

        /// <summary>옥토끼 슬롯 위 일괄 수거 버튼 — 버드나무 만땅 팝업에서 받지 않은 대기분.</summary>
        private static void RefreshBatchButton(SlotChip chip)
        {
            if (chip.BatchButtonRoot == null || chip.BatchButtonLabel == null) return;
            bool show = GameEconomy.Instance.HasPendingBatchMerit;
            if (show != chip.LastBatchVisible)
            {
                chip.LastBatchVisible = show;
                chip.BatchButtonRoot.SetActive(show);
            }
            if (!show) return;

            var amount = GameEconomy.Instance.PendingBatchMerit;
            if (chip.HasLastBatchAmount && amount.Equals(chip.LastBatchAmount)) return;
            chip.HasLastBatchAmount = true;
            chip.LastBatchAmount = amount;
            chip.LastBatchLabel = "일괄 수거\n" + amount.ToDisplayString() + "\n(광고×2)";
            chip.BatchButtonLabel.text = chip.LastBatchLabel;
        }

        private void RebuildSlotBar()
        {
            foreach (Transform child in slotBarRoot) Destroy(child.gameObject);
            slotChips.Clear();

            foreach (var agent in CharacterAgent.All)
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

                GameObject batchRoot = null;
                Text batchLabel = null;
                // 기획 7-2: 옥토끼 슬롯 위에만 앱 재시작 일괄 수거
                if (agent.Data != null && agent.Data.id == CharacterId.Rabbit)
                {
                    batchRoot = new GameObject("BatchCollect");
                    SetupRect(batchRoot, chipRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0f),
                        new Vector2(0, 30), new Vector2(148, 56));
                    var batchBg = batchRoot.AddComponent<Image>();
                    batchBg.color = C.hudBatchCollect;
                    var batchBtn = batchRoot.AddComponent<Button>();
                    batchBtn.targetGraphic = batchBg;
                    var batchRt = batchRoot.GetComponent<RectTransform>();
                    batchBtn.onClick.AddListener(() => OnBatchCollectClicked(batchRt));
                    var batchLabelGO = new GameObject("Label");
                    SetupRect(batchLabelGO, batchRoot.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                        Vector2.zero, Vector2.zero);
                    batchLabel = batchLabelGO.AddComponent<Text>();
                    batchLabel.font = font;
                    batchLabel.fontSize = UiFonts.Caption;
                    batchLabel.alignment = TextAnchor.MiddleCenter;
                    batchLabel.color = Color.white;
                    batchLabel.raycastTarget = false;
                    batchLabel.text = "일괄 수거";
                    batchRoot.SetActive(false);
                }

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
                    StaminaFillRt = barFillRt,
                    BatchButtonRoot = batchRoot,
                    BatchButtonLabel = batchLabel
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

        private void OnBatchCollectClicked(RectTransform from)
        {
            // 버드나무 만땅 팝업을 받지 않고 닫은 일괄 대기분
            if (!GameEconomy.Instance.HasPendingBatchMerit) return;
            if (DualActionPopup.Instance != null)
                DualActionPopup.Instance.OpenBatch(from);
            else
                ClaimBatchWithoutPopup(from);
        }

        /// <summary>팝업 없을 때 폴백 (1배).</summary>
        void ClaimBatchWithoutPopup(RectTransform from)
        {
            var before = GameEconomy.Instance.MeritPile;
            if (!GameEconomy.Instance.TryClaimBatchMerit(1)) return;
            var after = GameEconomy.Instance.MeritPile;
            GameSaveBridge.SaveFromWorld();
            BeginMeritCountUp(before, after);
            if (from != null) PlayMeritCollectFx(from);
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
            if (shopButton != null)
            {
                shopButton.onClick.RemoveAllListeners();
                shopButton.onClick.AddListener(() =>
                {
                    if (ShopScreen.Instance != null) ShopScreen.Instance.Open();
                });
            }

            if (yutButton != null)
            {
                yutButton.onClick.RemoveAllListeners();
                yutButton.onClick.AddListener(() =>
                {
                    if (YutScreen.Instance != null) YutScreen.Instance.Open();
                });
            }

            if (yutTokenPlusButton != null)
            {
                yutTokenPlusButton.onClick.RemoveAllListeners();
                yutTokenPlusButton.onClick.AddListener(() =>
                {
                    if (DualActionPopup.Instance != null) DualActionPopup.Instance.OpenYutTokenShop();
                });
            }

            WireTempDebugButtons();
            WireUpgradeHoldTriggers();
        }

        /// <summary>
        /// "초기화"/"캐시 날리기" 버튼은 Button.onClick.AddListener를 코드로만 붙이는데, 이건
        /// UnityEvent의 "런타임 전용" 리스너라 프리팹에 저장되지 않는다.
        /// Prefab에 버튼이 남아있으면 이름으로 찾아 매번 다시 연결한다.
        /// </summary>
        void WireTempDebugButtons()
        {
            if (hudCanvas == null) return;

            if (tempResetButton == null)
                tempResetButton = hudCanvas.transform.Find("TempResetButton")?.GetComponent<Button>();
            if (tempClearCacheButton == null)
                tempClearCacheButton = hudCanvas.transform.Find("TempClearCacheButton")?.GetComponent<Button>();

            if (tempResetButton != null)
            {
                tempResetButton.onClick.RemoveAllListeners();
                tempResetButton.onClick.AddListener(OnTempResetClicked);
            }

            if (tempClearCacheButton != null)
            {
                tempClearCacheButton.onClick.RemoveAllListeners();
                tempClearCacheButton.onClick.AddListener(OnClearCacheClicked);
            }
        }

        private static void OnTempResetClicked()
        {
            GameSaveService.DeleteSave();
            ReloadActiveScene();
        }

        private static void OnClearCacheClicked()
        {
            GameSaveService.ClearAllLocalData();
#if UNITY_WEBGL && !UNITY_EDITOR
            YogoeClearBrowserCacheAndReload();
#else
            ReloadActiveScene();
#endif
        }

        static void ReloadActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
                SceneManager.LoadScene(scene.buildIndex);
            else
                SceneManager.LoadScene(scene.name);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void YogoeClearBrowserCacheAndReload();
#endif

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

        /// <summary>윷 토큰 칩 옆 작은 [+] — 눌러 DualActionPopup 윷 토큰 상점(엽전 구매/광고 충전)을 연다.</summary>
        Button CreateYutTokenPlusButton(Transform parent)
        {
            const float size = 40f;
            var go = new GameObject("YutTokenPlus");
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = size;

            var img = go.AddComponent<Image>();
            img.color = C.currencyYutToken;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRt = labelGO.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var label = labelGO.AddComponent<Text>();
            label.font = font;
            label.fontSize = UiFonts.HudCurrency;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = C.textOnDark;
            label.text = "+";
            label.raycastTarget = false;

            return btn;
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
