using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;
using UnityEngine.EventSystems;
using Yoegoe.Cooking;

namespace Yoegoe.UI
{
    /// <summary>
    /// 캐릭터 상세 다이얼로그(화면 중앙). 좌 초상 / 우 이름·스탯·설명 / 하단 물·선호공양·인벤토리.
    /// 레이아웃은 Prefab만 사용. 선호·인벤토리 칩은 항상 코드로 갱신. 물·공양물은 드래그해서 본문에 놓아 먹인다.
    /// </summary>
    public class DetailScreen : MonoBehaviour
    {
        [Header("주입 (Main)")]
        public Font font;
        public OfferingData[] offerings;
        public Sprite waterIcon;

        [Header("셸 (Prefab — 필수)")]
        [SerializeField] GameObject root;
        [SerializeField] Canvas rootCanvas;
        [SerializeField] Image portraitImage;
        [SerializeField] RectTransform portraitRt;
        [SerializeField] RectTransform portraitDropRt;
        /// <summary>급여 드롭 판정용 — 초상만이 아니라 본문 전체(정보 패널 포함).</summary>
        [SerializeField] RectTransform feedDropRt;
        [SerializeField] Image portraitPanelHighlight;
        [SerializeField] Text nameValueText;
        [SerializeField] Text statusText;
        [SerializeField] Text intimacyLevelText;
        [SerializeField] Image intimacyFill;
        [SerializeField] RectTransform intimacyFillRt;
        [SerializeField] Text staminaValueText;
        [SerializeField] Image staminaFill;
        [SerializeField] RectTransform staminaFillRt;
        [SerializeField] Text descriptionText;
        [SerializeField] RectTransform preferredRow;
        [SerializeField] GameObject preferredHostGO;
        [SerializeField] GameObject inventoryButtonGO;
        [SerializeField] GameObject intimacyColGO;
        [SerializeField] GameObject inventoryPanel;
        [SerializeField] RectTransform inventoryRow;
        [SerializeField] Text feedHintText;
        [SerializeField] Text waterCountText;
        [SerializeField] CanvasGroup waterDragGroup;
        [SerializeField] Button dimCloseButton;
        [SerializeField] Button closeButton;
        [SerializeField] Button inventoryButton;

        /// <summary>Prefab/씬에 상세 셸이 이미 연결돼 있는지.</summary>
        public bool HasPrefabShell =>
            root != null && rootCanvas != null && portraitImage != null && preferredRow != null;

        private CharacterAgent currentAgent;
        private Vector3 portraitBaseScale = Vector3.one;
        private readonly List<CountBadge> offeringCountBadges = new List<CountBadge>();
        private bool offeringDragActive;
        /// <summary>드래그 중 한 프레임이라도 드롭존 위였으면 터치 릴리즈 지터로 실패하지 않게.</summary>
        private bool feedDropHoverLatched;
        /// <summary>드래그 중인 공양물 — 초상 위에서 선호 여부 말풍선(♥/💢) 힌트용.</summary>
        private OfferingData draggedOffering;
        private bool draggedWater;
        private EmoteBubble emoteBubble;

        struct CountBadge
        {
            public Text Label;
            public OfferingData Offering;
            public bool Water;
        }

        // 색·글자 크기: Resources/UiStyleSettings.asset
        UiStyleSettings Style => UiStyleSettings.Get();
        UiStyleSettings.Colors C => Style.colors;
        UiStyleSettings.FontSizes F => Style.fontSizes;

        private void Start()
        {
            EnsureBuilt();
            WireRuntimeListeners();
            if (root != null) root.SetActive(false);
        }

        private void Update()
        {
            if (currentAgent == null || root == null || !root.activeSelf) return;
            RefreshStats();
        }

        private void OnDestroy() => UiBlockGate.Unregister(this);

        /// <summary>set 될 때마다 UiBlockGate에 자동 등록/해제 — 열려 있는 동안 맵 입력 차단.</summary>
        bool isOpen;
        public bool IsOpen
        {
            get => isOpen;
            private set
            {
                if (isOpen == value) return;
                isOpen = value;
                if (value) UiBlockGate.Register(this);
                else UiBlockGate.Unregister(this);
            }
        }

        public void Open(CharacterAgent agent, string highlightOfferingId = null)
        {
            EnsureBuilt();
            if (!HasPrefabShell || root == null) return;
            currentAgent = agent;
            root.SetActive(true);
            IsOpen = true;
            if (inventoryPanel != null) inventoryPanel.SetActive(false);


            ApplyLayout();
            RefreshDescription();
            RefreshIdentity();
            RefreshPortrait();
            RebuildPreferredRow();
            RebuildInventoryRow(highlightOfferingId);
            RefreshStats();
            RefreshItemCounts();

            if (!string.IsNullOrEmpty(highlightOfferingId) && inventoryPanel != null)
                inventoryPanel.SetActive(true);
        }

        void ApplyLayout()
        {
            if (preferredHostGO != null) preferredHostGO.SetActive(true);
            if (inventoryButtonGO != null) inventoryButtonGO.SetActive(true);

            if (intimacyColGO != null)
            {
                intimacyColGO.SetActive(true);
                // 예전 Prefab에 남은 반투명 CanvasGroup 복구
                var cg = intimacyColGO.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
            }

            if (waterDragGroup != null)
            {
                waterDragGroup.alpha = 1f;
                waterDragGroup.blocksRaycasts = true;
                waterDragGroup.interactable = true;
            }

            if (feedHintText != null)
            {
                feedHintText.text = DefaultFeedHint();
            }
        }

        void RefreshDescription()
        {
            if (descriptionText == null) return;
            string desc = "";
            if (currentAgent?.Data != null)
            {
                desc = currentAgent.Data.detailDescription;
                if (string.IsNullOrEmpty(desc)
                    && CharacterCatalog.TryGet(currentAgent.Data.id, out var entry)
                    && entry != null)
                    desc = entry.detailDescription;
            }
            descriptionText.text = desc ?? "";
        }

        public void Close()
        {
            if (emoteBubble != null) emoteBubble.Hide();
            if (inventoryPanel != null) inventoryPanel.SetActive(false);
            if (root != null) root.SetActive(false);
            IsOpen = false;
            currentAgent = null;
        }

        /// <summary>Prefab 셸만 사용. 없으면 에러 (런타임 생성 없음).</summary>
        public void EnsureBuilt()
        {
            if (HasPrefabShell)
            {
                if (portraitRt != null) portraitBaseScale = portraitRt.localScale;
                EnsureWaterCountBadgeRegistered();
                return;
            }

            Debug.LogError(
                "[DetailScreen] Prefab 셸이 없습니다. Main 씬에 DetailScreen Prefab 인스턴스를 배치하세요.");
        }

        /// <summary>
        /// Button.onClick 리스너는 Prefab에 저장되지 않으므로 Play/Start마다 다시 연결한다.
        /// </summary>
        void WireRuntimeListeners()
        {
            if (dimCloseButton == null && root != null)
                dimCloseButton = root.GetComponent<Button>();
            if (closeButton == null && root != null)
                closeButton = root.transform.Find("Dialog/CloseButton")?.GetComponent<Button>();
            if (inventoryButton == null && inventoryButtonGO != null)
                inventoryButton = inventoryButtonGO.transform.Find("IconBox")?.GetComponent<Button>();

            if (dimCloseButton != null)
            {
                dimCloseButton.onClick.RemoveAllListeners();
                dimCloseButton.onClick.AddListener(Close);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }

            if (inventoryButton != null)
            {
                inventoryButton.onClick.RemoveAllListeners();
                inventoryButton.onClick.AddListener(ToggleInventory);
            }

            RebindWaterDragItem();
            EnsureWaterCountBadgeRegistered();
        }

        void RebindWaterDragItem()
        {
            if (root == null) return;
            var chip = root.transform.Find("Dialog/BottomBar/Action_물");
            if (chip == null) return;

            var drag = chip.GetComponentInChildren<OfferingDragItem>(true);
            if (drag == null) return;

            var pw = FindWater();
            Sprite icon = waterIcon;
            if (icon == null && pw != null) icon = pw.icon;
            drag.Configure(this, pw, isWater: true, icon);
        }

        void EnsureWaterCountBadgeRegistered()
        {
            if (waterCountText == null && root != null)
            {
                var badge = root.transform.Find("Dialog/BottomBar/Action_물/IconBox/CountBadge");
                if (badge != null)
                    waterCountText = badge.GetComponentInChildren<Text>(true);
            }

            if (waterCountText == null) return;
            for (int i = 0; i < offeringCountBadges.Count; i++)
            {
                if (offeringCountBadges[i].Water && offeringCountBadges[i].Label == waterCountText)
                    return;
            }

            offeringCountBadges.Add(new CountBadge
            {
                Label = waterCountText,
                Offering = null,
                Water = true
            });
        }

        // ---------------- refresh ----------------

        private void RefreshIdentity()
        {
            if (currentAgent == null || nameValueText == null) return;
            var data = currentAgent.Data;
            string name = data != null ? data.displayName : "?";
            if (string.IsNullOrEmpty(name) || name == "?")
            {
                if (data != null && CharacterCatalog.TryGet(data.id, out var entry) && entry != null)
                    name = entry.displayName;
            }
            nameValueText.text = name;
            if (statusText != null)
                statusText.text = CharacterStatusPresentation.ForDetail(currentAgent.Stats.State);
        }

        private void RefreshStats()
        {
            if (currentAgent == null) return;
            var stats = currentAgent.Stats;

            int stamina = Mathf.RoundToInt(stats.Stamina);
            int maxStamina = Mathf.RoundToInt(currentAgent.MaxStamina);
            if (staminaValueText != null)
                staminaValueText.text = stamina + "/" + maxStamina;
            SetBarFill(staminaFillRt, Mathf.Clamp01(stats.Stamina / Mathf.Max(0.001f, currentAgent.MaxStamina)));
            if (staminaFill != null)
                staminaFill.color = CharacterStatusPresentation.ForStaminaBar(stats.State, Time.unscaledTime);

            int lv = Mathf.Clamp(Mathf.FloorToInt(stats.Intimacy / 20f), 0, 5);
            if (stats.Intimacy > 0f && lv == 0) lv = 1;
            if (intimacyLevelText != null)
                intimacyLevelText.text = "Lv. " + lv;
            SetBarFill(intimacyFillRt, Mathf.Clamp01(stats.Intimacy / 100f));

            RefreshIdentity();
            RefreshPortrait();
            RefreshItemCounts();
            RefreshWaterFaintFrame();
        }

        /// <summary>기절한 요괴는 물로만 깨어난다 → 물 칸에 금빛 테두리가 반짝인다 (5장).</summary>
        Outline waterFaintFrame;

        void RefreshWaterFaintFrame()
        {
            bool fainted = currentAgent != null && currentAgent.Stats.State == ActionState.Fainted;
            if (waterFaintFrame == null)
            {
                if (!fainted || root == null) return;
                var chip = root.transform.Find("Dialog/BottomBar/Action_물");
                if (chip == null) return;
                var box = chip.Find("IconBox");
                Graphic target = box != null ? box.GetComponent<Graphic>() : null;
                if (target == null) target = chip.GetComponentInChildren<Image>(true);
                if (target == null) return;
                waterFaintFrame = target.gameObject.AddComponent<Outline>();
                waterFaintFrame.effectDistance = new Vector2(5f, -5f);
                waterFaintFrame.useGraphicAlpha = false;
            }
            waterFaintFrame.enabled = fainted;
            if (fainted)
                waterFaintFrame.effectColor = GoldenSparkle.Pulse(
                    new Color(1f, 0.86f, 0.35f, 0.15f), new Color(1f, 0.95f, 0.6f, 1f));
        }

        private void RefreshItemCounts()
        {
            if (waterCountText != null)
                waterCountText.text = "x" + GameEconomy.Instance.Water;

            for (int i = 0; i < offeringCountBadges.Count; i++)
            {
                var b = offeringCountBadges[i];
                if (b.Label == null) continue;
                int n = b.Water
                    ? GameEconomy.Instance.Water
                    : GameEconomy.Instance.GetOfferingCount(b.Offering);
                b.Label.text = "x" + n;
            }
        }

        static void SetBarFill(RectTransform fillRt, float ratio)
        {
            if (fillRt == null) return;
            var parentRt = fillRt.parent as RectTransform;
            float parentW = parentRt != null ? parentRt.rect.width : 160f;
            if (parentW < 1f) parentW = 160f;
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;
            fillRt.sizeDelta = new Vector2(parentW * ratio, 0f);
            fillRt.localScale = Vector3.one;
        }

        private void RefreshPortrait()
        {
            if (portraitImage == null || currentAgent == null) return;
            if (portraitRt != null) portraitRt.localScale = portraitBaseScale;
            var sprite = FirstSprite(currentAgent.Data);
            if (portraitImage.sprite != sprite) portraitImage.sprite = sprite;
            portraitImage.color = Color.white;
            portraitImage.preserveAspect = true;
        }

        // ---------------- feed / drag ----------------

        public void NotifyOfferingDragBegan(OfferingData offering, bool isWater)
        {
            offeringDragActive = true;
            feedDropHoverLatched = false;
            draggedOffering = offering;
            draggedWater = isWater;
            if (feedHintText != null)
                feedHintText.text = "캐릭터 위에 놓아 공양하세요";
            SetPortraitDropHighlight(false);
        }

        public void NotifyOfferingDragMoved(Vector2 screenPos)
        {
            bool over = IsOverFeedTarget(screenPos);
            if (over) feedDropHoverLatched = true;
            SetPortraitDropHighlight(over || feedDropHoverLatched);
            UpdateEmoteHint(over);
        }

        /// <summary>
        /// 초상 위로 공양물을 끌고 오면 표정 힌트: 선호면 ♥, 아니면 💢.
        /// 공개 여부와 무관하게 보여 준다(비공개 선호를 찾는 힌트). 물은 힌트 없음.
        /// </summary>
        void UpdateEmoteHint(bool over)
        {
            bool show = over
                && offeringDragActive
                && currentAgent != null
                && draggedOffering != null
                && !draggedWater
                && !IsWaterOffering(draggedOffering);
            if (!show)
            {
                if (emoteBubble != null) emoteBubble.Hide();
                return;
            }
            if (emoteBubble == null)
            {
                if (portraitDropRt == null) return;
                emoteBubble = EmoteBubble.Create(portraitDropRt);
            }
            emoteBubble.Show(IsPreferred(draggedOffering) ? EmoteBubble.Kind.Happy : EmoteBubble.Kind.Dislike);
        }

        public void NotifyOfferingDragEnded(bool accepted)
        {
            offeringDragActive = false;
            feedDropHoverLatched = false;
            draggedOffering = null;
            draggedWater = false;
            if (emoteBubble != null) emoteBubble.Hide();
            SetPortraitDropHighlight(false);
            if (feedHintText == null) return;
            if (accepted)
            {
                feedHintText.text = "";
                return;
            }
            // 실패 사유를 NotifyFeedBlocked 로 이미 넣었으면 유지
            if (!string.IsNullOrEmpty(feedHintText.text)
                && feedHintText.text != "캐릭터 위에 놓아 공양하세요")
                return;
            feedHintText.text = DefaultFeedHint();
        }

        public void NotifyFeedBlocked(string reason)
        {
            if (feedHintText != null && !string.IsNullOrEmpty(reason))
                feedHintText.text = reason;
        }

        static string DefaultFeedHint() => "물·공양물을 드래그해 캐릭터에게 먹이세요";

        public bool TryAcceptOfferingDrop(Vector2 screenPos, OfferingData offering, bool water)
        {
            bool overNow = IsOverFeedTarget(screenPos);
            if (!overNow && !feedDropHoverLatched)
            {
                NotifyFeedBlocked("캐릭터(초상·정보) 위에 놓아 주세요");
                return false;
            }

            if (CeremonyGate.BlocksWorldInput)
            {
                NotifyFeedBlocked("연출이 끝난 뒤 다시 시도해 주세요");
                return false;
            }

            if (water || (offering != null && IsWaterOffering(offering)))
                return OnFeedWater();
            if (offering == null) return false;
            return OnFeed(offering);
        }

        bool IsOverFeedTarget(Vector2 screenPos)
        {
            var target = feedDropRt != null ? feedDropRt : portraitDropRt;
            if (target == null) return false;
            Camera cam = null;
            if (rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = rootCanvas.worldCamera;
            // 터치 릴리즈 오차 여유 (레퍼런스 해상도 기준 로컬 유닛)
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    target, screenPos, cam, out var local))
                return false;
            var r = target.rect;
            const float pad = 28f;
            r.xMin -= pad;
            r.xMax += pad;
            r.yMin -= pad;
            r.yMax += pad;
            return r.Contains(local);
        }

        void SetPortraitDropHighlight(bool on)
        {
            if (portraitPanelHighlight == null) return;
            portraitPanelHighlight.color = on ? C.portraitHighlight : C.portraitBg;
        }

        private bool OnFeedWater() => ApplyFeedResult(currentAgent?.TryFeedWater(FindWater()), null);

        /// <summary>공양 규칙은 CharacterAgent.TryFeed — 여기선 결과 문구·연출·갱신만.</summary>
        private bool OnFeed(OfferingData offering)
        {
            if (currentAgent == null || offering == null) return false;
            return ApplyFeedResult(currentAgent.TryFeed(offering), offering);
        }

        bool ApplyFeedResult(FeedResult? maybe, OfferingData offering)
        {
            if (currentAgent == null || maybe == null) return false;
            var r = maybe.Value;
            if (!r.Success)
            {
                NotifyFeedBlocked(FeedBlockMessage(r.Block, offering));
                return false;
            }

            PlayGainPopup(r.StaminaGain, r.IntimacyGain);
            RefreshStats();
            RefreshItemCounts();
            if (r.PreferenceRevealed)
                RebuildPreferredRow();
            bool ranOut = offering != null && GameEconomy.Instance != null
                          && GameEconomy.Instance.GetOfferingCount(offering) <= 0;
            if (r.RequestFulfilled || r.PreferenceRevealed || ranOut)
                RebuildInventoryRow();
            GameSaveBridge.SaveFromWorld();
            return true;
        }

        static string FeedBlockMessage(FeedBlock block, OfferingData offering) => block switch
        {
            FeedBlock.NoItem => offering == null || offering.IsWater ? "물이 없어요" : "공양물이 없어요",
            FeedBlock.StaminaFull => "기력이 가득 찼어요",
            FeedBlock.StaminaAndIntimacyFull => "기력·친밀도가 모두 가득 찼어요",
            FeedBlock.FaintedNeedsWater => "기절한 요괴는 물로만 깨어나요",
            _ => "",
        };

        void PlayGainPopup(int staminaGain, float intimacyGain)
        {
            if (rootCanvas == null || portraitDropRt == null) return;
            OfferingGainPopup.Play(rootCanvas.transform, portraitDropRt, font, staminaGain, intimacyGain);
            IntimacyHeartFx.PlayUi(rootCanvas.transform, portraitDropRt, intimacyGain);
        }

        static bool IsWaterOffering(OfferingData offering) => offering != null && offering.IsWater;

        private bool IsPreferred(OfferingData offering) =>
            currentAgent != null && currentAgent.IsPreferredOffering(offering);

        private OfferingData FindWater()
        {
            if (offerings == null) return null;
            foreach (var o in offerings)
            {
                if (o == null) continue;
                if (IsWaterOffering(o)) return o;
            }
            return CharacterCatalog.FindOffering("water");
        }

        private void ToggleInventory()
        {
            if (inventoryPanel == null) return;
            bool show = !inventoryPanel.activeSelf;
            inventoryPanel.SetActive(show);
            if (show) RebuildInventoryRow();
        }

        private void RebuildPreferredRow()
        {
            if (preferredRow == null) return;
            ClearChildren(preferredRow);
            PruneDeadCountBadges();

            CharacterCatalog.PreferredOffering[] prefs = null;
            if (currentAgent?.Data != null
                && CharacterCatalog.TryGet(currentAgent.Data.id, out var entry)
                && entry != null)
                prefs = entry.preferredOfferings;

            int shown = 0;
            if (prefs == null || prefs.Length == 0)
            {
                var soPrefs = currentAgent?.Data?.preferredOfferings;
                if (soPrefs != null)
                {
                    foreach (var o in soPrefs)
                    {
                        if (o == null) continue;
                        if (currentAgent.Stats.IsPreferenceRevealed(o.offeringId))
                            CreatePreferredChip(preferredRow, o.displayName, o.icon, o);
                        else
                            CreateHiddenPreferredChip(preferredRow);
                        shown++;
                    }
                }
            }
            else
            {
                foreach (var p in prefs)
                {
                    if (p == null) continue;
                    shown++;
                    if (!currentAgent.Stats.IsPreferenceRevealed(p.id))
                    {
                        CreateHiddenPreferredChip(preferredRow);
                        continue;
                    }
                    var resolved = CharacterCatalog.FindOffering(p.id);
                    string label = !string.IsNullOrEmpty(p.name) ? p.name : p.id;
                    CreatePreferredChip(preferredRow, label, resolved != null ? resolved.icon : null, resolved);
                }
            }

            // 선호 공양물이 없는 요괴(옥토끼) — 처음부터 X로 "없음"을 알려 준다
            if (shown == 0)
                CreateMarkChip(preferredRow, "X", "없음");
        }

        private void RebuildInventoryRow(string highlightOfferingId = null)
        {
            if (inventoryRow == null) return;
            ClearChildren(inventoryRow);
            PruneDeadCountBadges();
            if (offerings == null) return;

            // 요구한 음식을 앞으로(강조 슬롯) — 10-2: 가진 것만, 없으면 강조 없음
            var highlighted = FindOwnedRequestTarget(highlightOfferingId);
            if (highlighted != null)
                CreatePreferredChip(inventoryRow, highlighted.displayName, highlighted.icon, highlighted, highlight: true,
                    goldFrame: IsRevealedPreferred(highlighted));

            // 음식·공양물·황금음식 — 가진 것만 나열
            foreach (var offering in offerings)
            {
                if (offering == null || offering == highlighted) continue;
                if (IsWaterOffering(offering)) continue;
                if (GameEconomy.Instance == null || GameEconomy.Instance.GetOfferingCount(offering) <= 0) continue;
                CreatePreferredChip(inventoryRow, offering.displayName, offering.icon, offering, highlight: false,
                    goldFrame: IsRevealedPreferred(offering));
            }
        }

        /// <summary>
        /// 요구 강조 대상: 요구한 음식을 가졌으면 그것, 없고 그 음식의 황금 버전을 가졌으면 황금 버전
        /// (황금음식을 줘도 요구가 채워진다). 둘 다 없으면 null — 강조 칸을 만들지 않는다.
        /// </summary>
        public static OfferingData FindOwnedRequestTarget(string requestedId)
        {
            var eco = GameEconomy.Instance;
            if (string.IsNullOrEmpty(requestedId) || eco == null) return null;
            var food = OfferingCatalog.Find(requestedId) ?? CharacterCatalog.FindOffering(requestedId);
            if (food != null && eco.GetOfferingCount(food) > 0) return food;
            var golden = OfferingCatalog.FindGolden(requestedId);
            if (golden != null && eco.GetOfferingCount(golden) > 0) return golden;
            return null;
        }

        void PruneDeadCountBadges()
        {
            offeringCountBadges.RemoveAll(b => b.Label == null);
        }

        bool IsRevealedPreferred(OfferingData offering) =>
            offering != null && currentAgent != null
            && currentAgent.Stats.IsPreferenceRevealed(offering.offeringId)
            && IsPreferred(offering);

        /// <summary>아직 먹여 보지 않은 선호 — 이름·아이콘 없이 "?" (드래그 불가).</summary>
        private void CreateHiddenPreferredChip(Transform parent) => CreateMarkChip(parent, "?", "???");

        /// <summary>아이콘 대신 큰 글자 하나(?, X)를 띄우는 드래그 불가 칩.</summary>
        private void CreateMarkChip(Transform parent, string mark, string label)
        {
            CreatePreferredChip(parent, label, null, null);
            var circle = parent.GetChild(parent.childCount - 1).Find("Circle");
            if (circle == null) return;
            var icon = circle.Find("Icon");
            if (icon != null) icon.gameObject.SetActive(false);
            var q = CreateText(circle, mark, F.title, TextAnchor.MiddleCenter);
            q.color = C.textDark;
            q.raycastTarget = false;
            SetupRect(q.gameObject, circle, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
        }

        private void CreatePreferredChip(Transform parent, string label, Sprite icon, OfferingData feedTarget, bool highlight = false,
            bool goldFrame = false)
        {
            var itemGO = new GameObject("Pref_" + (label ?? "?"));
            itemGO.transform.SetParent(parent, false);
            var le = itemGO.AddComponent<LayoutElement>();
            le.preferredWidth = 100;
            le.preferredHeight = 130;

            var v = itemGO.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childControlWidth = true;

            var circleGO = new GameObject("Circle");
            circleGO.transform.SetParent(itemGO.transform, false);
            var circleLe = circleGO.AddComponent<LayoutElement>();
            circleLe.preferredWidth = 72;
            circleLe.preferredHeight = 72;
            var circleImg = circleGO.AddComponent<Image>();
            circleImg.color = highlight
                ? C.offeringHighlight
                : C.offeringIdle;
            if (goldFrame)
            {
                var frame = circleGO.AddComponent<Outline>();
                frame.effectColor = C.preferredGoldFrame;
                frame.effectDistance = new Vector2(4f, -4f);
            }

            var iconGO = new GameObject("Icon");
            SetupRect(iconGO, circleGO.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconImg.color = icon != null ? Color.white : new Color(0.7f, 0.7f, 0.75f, 1f);
            iconImg.raycastTarget = false;

            if (feedTarget != null && feedTarget.golden)
                GoldenSparkle.Attach(circleImg); // 황금음식 — 금빛 반짝임

            if (feedTarget != null)
            {
                var drag = circleGO.AddComponent<OfferingDragItem>();
                drag.Configure(this, feedTarget, IsWaterOffering(feedTarget), icon != null ? icon : feedTarget.icon);
                AttachCountBadge(circleGO.transform, feedTarget, isWater: false);
            }

            var tagGO = new GameObject("Tag");
            tagGO.transform.SetParent(itemGO.transform, false);
            var tagLe = tagGO.AddComponent<LayoutElement>();
            tagLe.preferredHeight = 28;
            var tagBg = tagGO.AddComponent<Image>();
            tagBg.color = C.accentBlue;
            tagBg.raycastTarget = false;
            var tagText = CreateText(tagGO.transform, label ?? "", F.caption, TextAnchor.MiddleCenter);
            tagText.color = Color.white;
            SetupRect(tagText.gameObject, tagGO.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
        }

        Text AttachCountBadge(Transform iconParent, OfferingData offering, bool isWater)
        {
            var badgeGO = new GameObject("CountBadge");
            SetupRect(badgeGO, iconParent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-4f, -4f), new Vector2(36f, 22f));
            var bg = badgeGO.AddComponent<Image>();
            bg.color = C.badgeBg;
            bg.raycastTarget = false;
            int n = 0;
            if (GameEconomy.Instance != null)
            {
                n = isWater
                    ? GameEconomy.Instance.Water
                    : GameEconomy.Instance.GetOfferingCount(offering);
            }
            var text = CreateText(badgeGO.transform, "x" + n, F.caption, TextAnchor.MiddleCenter);
            text.color = Color.white;
            SetupRect(text.gameObject, badgeGO.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            offeringCountBadges.Add(new CountBadge
            {
                Label = text,
                Offering = offering,
                Water = isWater
            });
            return text;
        }

        static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Object.Destroy(t.GetChild(i).gameObject);
        }

        private static Sprite FirstSprite(CharacterData data)
        {
            if (data == null) return null;
            if (data.walkDown != null) foreach (var s in data.walkDown) if (s != null) return s;
            if (data.walkLeft != null) foreach (var s in data.walkLeft) if (s != null) return s;
            if (data.walkRight != null) foreach (var s in data.walkRight) if (s != null) return s;
            if (data.walkUp != null) foreach (var s in data.walkUp) if (s != null) return s;
            return null;
        }

        private Text CreateText(Transform parent, string initial, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = UiFonts.Size(fontSize);
            text.alignment = alignment;
            text.color = C.textDark;
            text.text = initial;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform SetupRect(GameObject go, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
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
