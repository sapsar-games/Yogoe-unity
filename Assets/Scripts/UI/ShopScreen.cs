using System;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 12장 고가구점. 레이아웃은 Prefab(<c>Assets/Prefabs/UI/ShopScreen.prefab</c>)만 사용.
    /// </summary>
    public class ShopScreen : MonoBehaviour
    {
        public static ShopScreen Instance { get; private set; }

        /// <summary>set 될 때마다 UiBlockGate에 자동 등록/해제 — 열려 있는 동안 맵 입력 차단
        /// (프리팹의 Background raycastTarget 설정에만 기대지 않는 안전망).</summary>
        bool isOpen;
        public bool IsOpen
        {
            get => isOpen;
            private set
            {
                if (isOpen == value) return;
                isOpen = value;
                if (value) Yoegoe.Core.UiBlockGate.Register(this);
                else Yoegoe.Core.UiBlockGate.Unregister(this);
            }
        }

        public Font font;
        public OfferingData[] offerings;
        public Sprite shopBackground;
        public Sprite imugiSprite;

        static readonly string[] ImugiLines =
        {
            "어서 오거라.",
            "뭐가 더 필요하냐.",
            "천천히 둘러보아라."
        };

        struct PackageDef
        {
            public string Id;
            public string Name;
            public string PriceLabel;
            public string Contents;
        }

        static readonly PackageDef[] Packages =
        {
            new PackageDef
            {
                Id = "intro1",
                Name = "입문 패키지",
                PriceLabel = "₩1,500",
                Contents = "엽전 50\n향 1\n물 5"
            },
            new PackageDef
            {
                Id = "intro2",
                Name = "나들이 패키지",
                PriceLabel = "₩4,900",
                Contents = "엽전 200\n향 3\n공양물 묶음"
            },
            new PackageDef
            {
                Id = "intro3",
                Name = "풍요 패키지",
                PriceLabel = "₩9,900",
                Contents = "엽전 500\n향 5\n물 20"
            }
        };

        [SerializeField] GameObject root;
        [SerializeField] Text dialogueText;
        [SerializeField] Image leftIcon;
        [SerializeField] Text leftName;
        [SerializeField] Text leftPriceLabel;
        [SerializeField] Image rightIcon;
        [SerializeField] Text rightName;
        [SerializeField] Text rightPriceLabel;
        [SerializeField] GameObject packagePopup;
        [SerializeField] Text packageTitle;
        [SerializeField] Text packageBody;
        [SerializeField] Text packagePriceBtnLabel;
        [SerializeField] Text currencyText;

        int dialogueIndex;

        void Awake() => Instance = this;

        void Start()
        {
            if (!EnsureShell()) return;
            WireRuntimeListeners();
            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Yoegoe.Core.UiBlockGate.Unregister(this);
        }

        public void Open()
        {
            if (!EnsureShell()) return;
            WireRuntimeListeners();
            ShopStock.SetCatalog(offerings);
            ShopStock.EnsureFresh(TrustedTime.UtcNow);
            dialogueIndex = 0;
            SetDialogue(ImugiLines[0]);
            RefreshSlots();
            RefreshCurrencyBar();
            if (GameEconomy.Instance != null)
            {
                GameEconomy.Instance.OnYeopjeonChanged -= OnYeopjeonChanged;
                GameEconomy.Instance.OnMeritChanged -= OnMeritChanged;
                GameEconomy.Instance.OnYeopjeonChanged += OnYeopjeonChanged;
                GameEconomy.Instance.OnMeritChanged += OnMeritChanged;
            }
            if (packagePopup != null) packagePopup.SetActive(false);
            root.SetActive(true);
            IsOpen = true;
        }

        /// <summary>persist=false: 콜드스타트 강제 닫기 등 — 세이브 불러오기 전에 저장하면 안 될 때.</summary>
        public void Close(bool persist = true)
        {
            if (GameEconomy.Instance != null)
            {
                GameEconomy.Instance.OnYeopjeonChanged -= OnYeopjeonChanged;
                GameEconomy.Instance.OnMeritChanged -= OnMeritChanged;
            }
            if (packagePopup != null) packagePopup.SetActive(false);
            if (root != null) root.SetActive(false);
            IsOpen = false;
            if (persist) GameSaveBridge.SaveFromWorld();
        }

        void OnYeopjeonChanged(int _) => RefreshCurrencyBar();
        void OnMeritChanged(BigNumber _) => RefreshCurrencyBar();

        void RefreshCurrencyBar()
        {
            if (currencyText == null || GameEconomy.Instance == null) return;
            currencyText.text = $"엽전 {GameEconomy.Instance.Yeopjeon}   공덕 {GameEconomy.Instance.MeritPile.ToDisplayString()}";
            RefreshRerollLabel();
        }

        /// <summary>리셋 버튼 글자에 현재 비용(떡절구 분당 × 5).</summary>
        void RefreshRerollLabel()
        {
            var label = root != null ? root.transform.Find("Reroll/Text")?.GetComponent<Text>() : null;
            if (label == null) return;
            var cost = ShopStock.GetRerollCost();
            label.text = cost.Mantissa == 0 ? "진열 바꾸기" : "진열 바꾸기 · 공덕 " + cost.ToDisplayString();
        }

        void OnImugiTapped()
        {
            dialogueIndex = (dialogueIndex + 1) % ImugiLines.Length;
            SetDialogue(ImugiLines[dialogueIndex]);
        }

        void SetDialogue(string line)
        {
            if (dialogueText != null) dialogueText.text = line ?? "";
        }

        void RefreshSlots()
        {
            BindOfferingSlot(ShopStock.Side.Left, leftIcon, leftName, leftPriceLabel);
            BindOfferingSlot(ShopStock.Side.Right, rightIcon, rightName, rightPriceLabel);
        }

        void BindOfferingSlot(ShopStock.Side side, Image icon, Text nameLabel, Text priceLabel)
        {
            var o = ShopStock.GetOffering(side);
            if (icon != null)
            {
                icon.sprite = o != null ? o.icon : null;
                icon.enabled = o != null && o.icon != null;
                icon.color = icon.enabled ? Color.white : new Color(1f, 1f, 1f, 0.2f);
            }
            if (nameLabel != null)
                nameLabel.text = o != null
                    ? (string.IsNullOrEmpty(o.displayName) ? o.offeringId : o.displayName)
                    : "—";
            if (priceLabel != null)
                priceLabel.text = ShopStock.OfferingPriceYeopjeon + " 엽전";
        }

        void OnBuyLeft() => TryBuyOffering(ShopStock.Side.Left);
        void OnBuyRight() => TryBuyOffering(ShopStock.Side.Right);

        void TryBuyOffering(ShopStock.Side side)
        {
            if (ShopStock.TryBuyOffering(side, out var fail))
            {
                SetDialogue("잘 골라갔구나.");
                GameSaveBridge.SaveFromWorld();
                return;
            }
            if (fail == ShopStock.BuyFail.NotEnoughYeopjeon)
                SetDialogue("돈을 더 모아와라.");
        }

        /// <summary>5분치 공덕으로 진열 바꾸기 (12장).</summary>
        void OnReroll()
        {
            if (ShopStock.TryRerollWithMerit(TrustedTime.UtcNow, out var fail))
            {
                SetDialogue("새 물건을 꺼내 왔다.");
                RefreshSlots();
                RefreshCurrencyBar();
                GameSaveBridge.SaveFromWorld();
                return;
            }
            if (fail == ShopStock.BuyFail.NotEnoughMerit)
                SetDialogue("돈을 더 모아와라.");
        }

        void OnBuyHyang()
        {
            if (ShopStock.TryBuyHyang(out var fail))
            {
                SetDialogue("향이 필요하구나.");
                GameSaveBridge.SaveFromWorld();
                return;
            }
            if (fail == ShopStock.BuyFail.NotEnoughYeopjeon)
                SetDialogue("돈을 더 모아와라.");
        }

        void OpenPackage(int index)
        {
            if (index < 0 || index >= Packages.Length || packagePopup == null) return;
            var p = Packages[index];
            if (packageTitle != null) packageTitle.text = p.Name;
            if (packageBody != null) packageBody.text = p.Contents;
            if (packagePriceBtnLabel != null) packagePriceBtnLabel.text = p.PriceLabel;
            packagePopup.SetActive(true);
        }

        void ClosePackage()
        {
            if (packagePopup != null) packagePopup.SetActive(false);
        }

        /// <summary>Prefab 셸만 사용. 없으면 에러 (런타임 생성 없음).</summary>
        bool EnsureShell()
        {
            BindMissingRefsFromHierarchy();
            if (root != null) return true;
            Debug.LogError(
                "[ShopScreen] Prefab 셸이 없습니다. Main 씬에 ShopScreen Prefab 인스턴스를 배치하세요.");
            return false;
        }

        void WireRuntimeListeners()
        {
            if (root == null) return;
            BindMissingRefsFromHierarchy();

            BindButton(root.transform.Find("Imugi"), OnImugiTapped);
            BindButton(root.transform.Find("LeftFood/PriceBuy"), OnBuyLeft);
            BindButton(root.transform.Find("RightFood/PriceBuy"), OnBuyRight);
            BindButton(root.transform.Find("Hyang/PriceBuy"), OnBuyHyang);
            BindButton(root.transform.Find("Reroll"), OnReroll);
            BindButton(root.transform.Find("Close"), () => Close());

            for (int i = 0; i < Packages.Length; i++)
            {
                int captured = i;
                BindButton(root.transform.Find("Pkg_" + Packages[i].Id), () => OpenPackage(captured));
            }

            if (packagePopup != null)
            {
                BindButton(packagePopup.transform, ClosePackage);
                BindButton(packagePopup.transform.Find("Box/ClosePkg"), ClosePackage);
            }
        }

        void BindMissingRefsFromHierarchy()
        {
            if (root == null)
            {
                var canvas = transform.Find("Canvas_Shop");
                if (canvas != null) root = canvas.Find("Root")?.gameObject;
            }
            if (root == null) return;

            var rt = root.transform;
            if (dialogueText == null)
                dialogueText = rt.Find("Dialogue/Text")?.GetComponent<Text>();
            if (currencyText == null)
                currencyText = rt.Find("CurrencyBar/Text")?.GetComponent<Text>();
            if (leftIcon == null)
                leftIcon = rt.Find("LeftFood/Icon")?.GetComponent<Image>();
            if (leftName == null)
                leftName = rt.Find("LeftFood/Name/Text")?.GetComponent<Text>();
            if (leftPriceLabel == null)
                leftPriceLabel = rt.Find("LeftFood/PriceBuy/Text")?.GetComponent<Text>();
            if (rightIcon == null)
                rightIcon = rt.Find("RightFood/Icon")?.GetComponent<Image>();
            if (rightName == null)
                rightName = rt.Find("RightFood/Name/Text")?.GetComponent<Text>();
            if (rightPriceLabel == null)
                rightPriceLabel = rt.Find("RightFood/PriceBuy/Text")?.GetComponent<Text>();
            if (packagePopup == null)
                packagePopup = rt.Find("PackagePopup")?.gameObject;
            if (packagePopup != null)
            {
                if (packageTitle == null)
                    packageTitle = packagePopup.transform.Find("Box/Title/Text")?.GetComponent<Text>();
                if (packageBody == null)
                    packageBody = packagePopup.transform.Find("Box/Body/Text")?.GetComponent<Text>();
                if (packagePriceBtnLabel == null)
                    packagePriceBtnLabel = packagePopup.transform.Find("Box/PriceBtn/Text")?.GetComponent<Text>();
            }
        }

        static void BindButton(Transform t, UnityEngine.Events.UnityAction action)
        {
            if (t == null || action == null) return;
            var btn = t.GetComponent<Button>();
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }
    }
}
