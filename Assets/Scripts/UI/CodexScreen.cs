using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.UI
{
    /// <summary>
    /// 요리책(도감) (19장): 재료 · 음식 · 공양물 칸, 수량, 수집 N / 60. 발견한 요리를 누르면 이름·조합·효과 팝업.
    /// 못 본 요리는 잠긴 '?' 칸. 공양간 요리 중에 열면 타이머가 멈춘다(<see cref="IsShowing"/>).
    /// 셸은 Prefab(Assets/Prefabs/UI/CodexScreen.prefab), 칸은 동적 콘텐츠.
    /// </summary>
    public class CodexScreen : MonoBehaviour
    {
        public static CodexScreen Instance { get; private set; }
        public static bool IsShowing => Instance != null && Instance.panel != null && Instance.panel.activeSelf;

        [SerializeField] GameObject panel;
        [SerializeField] Text rateText;
        [SerializeField] Button closeButton;
        [SerializeField] RectTransform content;
        [SerializeField] GameObject detailPanel;
        [SerializeField] Text detailName;
        [SerializeField] Text detailCombo;
        [SerializeField] Text detailDesc;
        [SerializeField] Button detailCloseButton;

        static readonly Color KnownCell = new Color(0.33f, 0.26f, 0.2f, 1f);
        static readonly Color LockedCell = new Color(0.16f, 0.13f, 0.11f, 1f);
        static readonly Color HeaderColor = new Color(1f, 0.85f, 0.55f, 1f);

        void Awake()
        {
            Instance = this;
            if (panel != null) panel.SetActive(false);
            if (detailPanel != null) detailPanel.SetActive(false);
        }

        void Start()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }
            if (detailCloseButton != null)
            {
                detailCloseButton.onClick.RemoveAllListeners();
                detailCloseButton.onClick.AddListener(() => detailPanel.SetActive(false));
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Open()
        {
            if (panel == null) return;
            Rebuild();
            if (detailPanel != null) detailPanel.SetActive(false);
            panel.SetActive(true);
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
        }

        /// <summary>버튼·배지용 "n/60".</summary>
        public static string RateShort => CookingCodex.DiscoveredCount + "/" + CookingCodex.Total;

        void Rebuild()
        {
            if (rateText != null) rateText.text = $"수집 {CookingCodex.DiscoveredCount} / {CookingCodex.Total}";
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            var eco = GameEconomy.Instance;

            var ingredients = Section("재료");
            for (int i = 0; i < (int)CookingIngredientId.Count; i++)
            {
                var id = (CookingIngredientId)i;
                string name = CookingRecipeCatalog.DisplayName(id);
                int n = eco != null ? eco.GetMaterialCount(id) : 0;
                string ingId = id.ToString();
                AddCell(ingredients, name, n, known: true, () => ShowDetail(name, "요리 재료", CodexDescriptions.Get(ingId)));
            }
            foreach (SpecialItemId special in System.Enum.GetValues(typeof(SpecialItemId)))
            {
                string name = CodexDescriptions.SpecialItemName(special);
                int n = eco != null ? eco.GetSpecialItemCount(special) : 0;
                string specialId = special.ToString();
                string baseName = CookingSession.TryBaseOf(special, out var baseIng)
                    ? CookingRecipeCatalog.DisplayName(baseIng) : name;
                AddCell(ingredients, name, n, known: true,
                    () => ShowDetail(name, $"요리판에서 {baseName} 칸으로 쓰여요",
                        JoinDesc(CodexDescriptions.Get(specialId), "이게 들어간 음식은 황금음식이 돼요.")));
            }

            var foods = Section("음식");
            var offerings = Section("공양물");
            foreach (var productId in CookingCodex.ProductIds)
            {
                if (!CookingCodex.TryGetRecipe(productId, out var recipe)) continue;
                bool known = CookingCodex.IsDiscovered(productId);
                int n = 0;
                if (eco != null)
                {
                    n = eco.GetOfferingCount(productId);
                    if (recipe.Kind == CookingResultKind.Food)
                        n += eco.GetOfferingCount(OfferingCatalog.GoldenIdOf(productId));
                }
                var grid = recipe.Kind == CookingResultKind.Food ? foods : offerings;
                string id = productId;
                AddCell(grid, known ? recipe.DisplayName : "?", known ? n : -1, known,
                    known ? () => ShowRecipeDetail(id) : (System.Action)null);
            }
        }

        RectTransform Section(string title)
        {
            var header = new GameObject("Header_" + title, typeof(RectTransform));
            header.transform.SetParent(content, false);
            var t = header.AddComponent<Text>();
            StyleText(t, 34, HeaderColor, TextAnchor.MiddleLeft);
            t.text = title;
            header.AddComponent<LayoutElement>().preferredHeight = 56;

            var gridGO = new GameObject("Grid_" + title, typeof(RectTransform));
            gridGO.transform.SetParent(content, false);
            var grid = gridGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(150, 150);
            grid.spacing = new Vector2(12, 12);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperLeft;
            return (RectTransform)gridGO.transform;
        }

        void AddCell(RectTransform parent, string name, int count, bool known, System.Action onClick)
        {
            var cell = new GameObject("Cell_" + name, typeof(RectTransform));
            cell.transform.SetParent(parent, false);
            var bg = cell.AddComponent<Image>();
            bg.color = known ? KnownCell : LockedCell;
            if (onClick != null)
            {
                var b = cell.AddComponent<Button>();
                b.targetGraphic = bg;
                b.onClick.AddListener(() => onClick());
            }

            var nameGO = new GameObject("Name", typeof(RectTransform));
            nameGO.transform.SetParent(cell.transform, false);
            var nrt = (RectTransform)nameGO.transform;
            nrt.anchorMin = new Vector2(0f, 0.3f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.offsetMin = new Vector2(6, 0);
            nrt.offsetMax = new Vector2(-6, -6);
            var nt = nameGO.AddComponent<Text>();
            StyleText(nt, known ? 26 : 48, known ? Color.white : new Color(0.55f, 0.5f, 0.45f), TextAnchor.MiddleCenter);
            nt.text = name;

            if (count < 0) return;
            var countGO = new GameObject("Count", typeof(RectTransform));
            countGO.transform.SetParent(cell.transform, false);
            var crt = (RectTransform)countGO.transform;
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(1f, 0.3f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            var ct = countGO.AddComponent<Text>();
            StyleText(ct, 24, new Color(1f, 0.85f, 0.45f), TextAnchor.MiddleCenter);
            ct.text = "x" + count;
        }

        void StyleText(Text t, int size, Color color, TextAnchor align)
        {
            t.font = rateText != null ? rateText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = UiFonts.Size(size);
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        void ShowRecipeDetail(string productId)
        {
            if (!CookingCodex.TryGetRecipe(productId, out var recipe)) return;
            string effect = recipe.Kind == CookingResultKind.Food
                ? "음식 · 기력 +8\n황금쌀·황금꿀이 들어가면 황금음식(5분간 이동·생산 2배)"
                : "공양물 · 기력 +3 · 친밀도 +2 (선호하는 요괴는 +5)";
            ShowDetail(recipe.DisplayName, "조합: " + CookingCodex.ComboText(productId),
                JoinDesc(CodexDescriptions.Get(recipe.Id), effect));
        }

        /// <summary>시트 설명(있으면) + 빈 줄 + 효과.</summary>
        static string JoinDesc(string description, string effect) =>
            string.IsNullOrEmpty(description) ? effect : description + "\n\n" + effect;

        void ShowDetail(string name, string combo, string desc)
        {
            if (detailPanel == null) return;
            if (detailName != null) detailName.text = name;
            if (detailCombo != null) detailCombo.text = combo;
            if (detailDesc != null) detailDesc.text = desc;
            detailPanel.SetActive(true);
        }
    }
}
