using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;

namespace Yoegoe.UI
{
    /// <summary>
    /// 캐릭터 상세 다이얼로그(화면 중앙). 좌 초상 / 우 이름·스탯·설명 / 하단 물·선호공양·인벤토리.
    /// 레이아웃은 Prefab만 사용. 선호·인벤토리 칩은 항상 코드로 갱신. 물·공양물은 드래그해서 본문에 놓아 먹인다.
    ///
    /// 영역별로 파일을 나눠뒀다 — 모두 같은 partial class라 필드·헬퍼는 공유:
    /// - DetailScreen.cs (여기): 필드, 생명주기(Start/Update), Open/Close, 셸/배선
    /// - DetailScreen.Stats.cs: 이름/스탯/초상 리프레시
    /// - DetailScreen.Feed.cs: 물·공양물 드래그 수신 + 먹이기
    /// - DetailScreen.Inventory.cs: 선호/인벤토리 칩 빌드
    /// </summary>
    public partial class DetailScreen : MonoBehaviour
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

        /// <summary>기절한 요괴는 물로만 깨어난다 → 물 칸에 금빛 테두리가 반짝인다 (5장).</summary>
        Outline waterFaintFrame;

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

        // ---------------- 공유 헬퍼 ----------------

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
