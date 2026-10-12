using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.UI
{
    /// <summary>10장 선물꾸러미. 탭으로 개봉 → 보상 → (1회) 광고/보상권으로 하나 더.</summary>
    public class GiftBundlePopup : MonoBehaviour
    {
        public static GiftBundlePopup Instance { get; private set; }

        public Font font;
        public Sprite closedChestSprite;
        public Sprite openChestSprite;

        GameObject root;
        public bool IsOpen => root != null && root.activeSelf;
        GameObject closedView;
        GameObject openView;
        Text titleText;
        Text rewardText;
        Image rewardIcon;
        GameObject moreBtnGO;
        Text moreBtnLabel;
        Action onClosed;
        bool bonusUsed;
        bool opening;
        /// <summary>첫 개봉에 나온 재료 — "하나 더"는 같은 구성을 한 번 더.</summary>
        Yoegoe.Cooking.CookingIngredientId[] lastGranted;

        void Awake() => Instance = this;

        void OnEnable() => CharacterRequestState.GiftBundleAwarded += HandleGiftBundleAwarded;
        void OnDisable() => CharacterRequestState.GiftBundleAwarded -= HandleGiftBundleAwarded;

        void HandleGiftBundleAwarded(string reward) => OpenForReward(reward);

        void Start()
        {
            EnsureBuilt();
            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void OpenForReward(string messageBeforeOpen, Action closed = null)
        {
            EnsureBuilt();
            onClosed = closed;
            bonusUsed = false;
            opening = false;
            lastGranted = null;
            titleText.text = string.IsNullOrEmpty(messageBeforeOpen)
                ? "선물꾸러미"
                : messageBeforeOpen;
            closedView.SetActive(true);
            openView.SetActive(false);
            if (moreBtnGO != null) moreBtnGO.SetActive(false);
            root.SetActive(true);
        }

        void OnTapClosed()
        {
            if (opening) return;
            opening = true;
            ShowReward(GiftBundle.RollContents(), firstOpen: true);
        }

        void ShowReward(Yoegoe.Cooking.CookingIngredientId[] ingredients, bool firstOpen)
        {
            GiftBundle.Grant(ingredients, out string name);
            if (firstOpen) lastGranted = ingredients;
            closedView.SetActive(false);
            openView.SetActive(true);
            rewardText.text = (firstOpen ? "요리재료\n" : "하나 더!\n") + name;
            SetBagIcon();

            RefreshMoreButton();
            opening = false;
            Yoegoe.Save.GameSaveBridge.SaveFromWorld();
        }

        void RefreshMoreButton()
        {
            if (moreBtnGO == null) return;
            // 세션당 1회만
            bool show = !bonusUsed;
            moreBtnGO.SetActive(show);
            if (!show || moreBtnLabel == null) return;

            if (GiftBundle.AdTickets > 0)
                moreBtnLabel.text = "보상권으로 하나 더 (" + GiftBundle.AdTickets + ")";
            else
                moreBtnLabel.text = "광고 보고 하나 더";
        }

        void OnMoreClicked()
        {
            if (bonusUsed || opening) return;
            StartCoroutine(ClaimBonusRoutine());
        }

        IEnumerator ClaimBonusRoutine()
        {
            opening = true;
            bool usedTicket = GiftBundle.TrySpendAdTicket(1);
            if (!usedTicket)
            {
                // 광고 SDK 미연동 — 짧은 대기 후 성공 처리(스텁)
                if (moreBtnLabel != null) moreBtnLabel.text = "광고 시청 중…";
                yield return new WaitForSecondsRealtime(0.8f);
            }

            bonusUsed = true;
            if (moreBtnGO != null) moreBtnGO.SetActive(false);
            // 첫 개봉과 동일 내용 1회 추가 지급
            GiftBundle.Grant(lastGranted ?? GiftBundle.RollContents(), out string name);
            rewardText.text = "하나 더!\n" + name;
            SetBagIcon();
            opening = false;
            Yoegoe.Save.GameSaveBridge.SaveFromWorld();
        }

        /// <summary>재료보따리 아이콘 (윷판 재료보따리 칸과 같은 그림).</summary>
        void SetBagIcon()
        {
            if (rewardIcon == null) return;
            var icon = Yoegoe.Minigames.Yut.YutMiniGame.IngredientBagIcon();
            rewardIcon.sprite = icon;
            rewardIcon.enabled = icon != null;
            rewardIcon.color = Color.white;
        }

        void OnDismiss()
        {
            if (opening) return;
            root.SetActive(false);
            var cb = onClosed;
            onClosed = null;
            cb?.Invoke();
            Yoegoe.Save.GameSaveBridge.SaveFromWorld();
        }

        void EnsureBuilt()
        {
            if (root != null) return;
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("Canvas_GiftBundle");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 860;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            canvasGO.AddComponent<GraphicRaycaster>();

            root = new GameObject("Panel");
            var rootRt = Stretch(root, canvasGO.transform);
            var dim = root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);

            closedView = new GameObject("Closed");
            Stretch(closedView, rootRt);
            var closedBtn = closedView.AddComponent<Button>();
            closedBtn.targetGraphic = dim;
            closedBtn.onClick.AddListener(OnTapClosed);

            EnsureChestSprites();

            var box = MakeBox(closedView.transform, new Vector2(520, 460));
            titleText = MakeText(box, "선물꾸러미", 36, new Vector2(0, 170));
            MakeText(box, "탭하여 열기", 26, new Vector2(0, -170), new Color(1f, 0.85f, 0.55f));
            MakeChestImage(box, "ClosedChest", closedChestSprite, new Vector2(0, 0), new Vector2(220, 220));

            openView = new GameObject("Open");
            Stretch(openView, rootRt);
            openView.SetActive(false);
            var openBox = MakeBox(openView.transform, new Vector2(560, 580));
            MakeText(openBox, "획득!", 34, new Vector2(0, 230));
            MakeChestImage(openBox, "OpenChest", openChestSprite, new Vector2(0, 60), new Vector2(220, 220));

            var iconGO = new GameObject("RewardIcon");
            var iconRt = iconGO.AddComponent<RectTransform>();
            iconRt.SetParent(openBox, false);
            iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = new Vector2(0, 70);
            iconRt.sizeDelta = new Vector2(96, 96);
            rewardIcon = iconGO.AddComponent<Image>();
            rewardIcon.preserveAspect = true;
            rewardIcon.raycastTarget = false;

            rewardText = MakeText(openBox, "", 28, new Vector2(0, -90));
            rewardText.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 100);

            // 광고/보상권으로 하나 더 (세션 1회)
            moreBtnGO = new GameObject("BtnMore");
            var moreRt = moreBtnGO.AddComponent<RectTransform>();
            moreRt.SetParent(openBox, false);
            moreRt.anchorMin = moreRt.anchorMax = moreRt.pivot = new Vector2(0.5f, 0.5f);
            moreRt.anchoredPosition = new Vector2(0, -175);
            moreRt.sizeDelta = new Vector2(360, 64);
            var moreImg = moreBtnGO.AddComponent<Image>();
            moreImg.color = new Color(0.55f, 0.4f, 0.2f, 1f);
            var moreBtn = moreBtnGO.AddComponent<Button>();
            moreBtn.targetGraphic = moreImg;
            moreBtn.onClick.AddListener(OnMoreClicked);
            moreBtnLabel = MakeText(moreRt, "광고 보고 하나 더", 24, Vector2.zero);
            moreBtnGO.SetActive(false);

            var ok = new GameObject("BtnOk");
            var okRt = ok.AddComponent<RectTransform>();
            okRt.SetParent(openBox, false);
            okRt.anchorMin = okRt.anchorMax = okRt.pivot = new Vector2(0.5f, 0.5f);
            okRt.anchoredPosition = new Vector2(0, -250);
            okRt.sizeDelta = new Vector2(240, 64);
            var okImg = ok.AddComponent<Image>();
            okImg.color = new Color(0.3f, 0.5f, 0.45f, 1f);
            var okBtn = ok.AddComponent<Button>();
            okBtn.targetGraphic = okImg;
            okBtn.onClick.AddListener(OnDismiss);
            MakeText(okRt, "확인", 28, Vector2.zero);
        }

        void EnsureChestSprites()
        {
            if (closedChestSprite == null)
                closedChestSprite = Resources.Load<Sprite>("UI/GiftChest_Closed");
            if (openChestSprite == null)
                openChestSprite = Resources.Load<Sprite>("UI/GiftChest_Open");
        }

        static void MakeChestImage(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
            }
            else
                img.color = new Color(0.72f, 0.48f, 0.22f, 1f);
        }

        static RectTransform Stretch(GameObject go, Transform parent)
        {
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static RectTransform MakeBox(Transform parent, Vector2 size)
        {
            var box = new GameObject("Box");
            var rt = box.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            var bg = box.AddComponent<Image>();
            bg.color = new Color(0.14f, 0.1f, 0.08f, 0.98f);
            return rt;
        }

        Text MakeText(Transform parent, string msg, int size, Vector2 pos, Color? color = null)
        {
            var go = new GameObject("Text");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(500, 80);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = UiFonts.Size(size);
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color ?? new Color(1f, 0.95f, 0.85f);
            t.text = msg;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }
    }
}
