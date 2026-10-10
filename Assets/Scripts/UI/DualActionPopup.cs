using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 공통 이중 선택 팝업 셸 — 일괄 수거(1배/광고 2배) · 윷 토큰(엽전 구매/광고 충전).
    /// Prefab화 전이므로 런타임 Build. 호출은 <see cref="OpenBatch"/> / <see cref="OpenYutTokenShop"/>.
    /// </summary>
    public class DualActionPopup : MonoBehaviour
    {
        public static DualActionPopup Instance { get; private set; }

        public Font font;

        GameObject root;
        Text titleText;
        Text bodyText;
        Text hintText;
        Text primaryLabel;
        Text secondaryLabel;
        Button primaryBtn;
        Button secondaryBtn;
        Button closeBtn;
        bool busy;

        enum Mode { None, Batch, YutToken }
        Mode mode;
        RectTransform batchFxFrom;
        Vector3? batchFxFromWorld;

        static int BuyCostYeopjeon => Yoegoe.Data.GameSettings.YutTokenBuyCost; // 시트 game_settings
        static int BuyGrantTokens => Yoegoe.Data.GameSettings.YutTokenBuyAmount;
        const int AdGrantTokens = 1;
        const float AdWatchSeconds = 0.8f;

        void Awake() => Instance = this;

        void Start()
        {
            EnsureBuilt();
            root.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>7-2 일괄 수거. fxFrom은 꽃잎 출발(옥토끼 일괄 버튼).</summary>
        public void OpenBatch(RectTransform fxOrigin = null)
        {
            if (!GameEconomy.Instance.HasPendingBatchMerit) return;
            EnsureBuilt();
            busy = false;
            mode = Mode.Batch;
            batchFxFrom = fxOrigin;
            batchFxFromWorld = null;
            titleText.text = "일괄 수거";
            RefreshBatchLabels();
            SetButtonsInteractable(true);
            root.SetActive(true);
        }

        /// <summary>공덕 버드나무 만땅 탭 — 꽃잎은 버드나무(월드)에서 출발.</summary>
        public void OpenBatchFromWorld(Vector3 worldOrigin)
        {
            OpenBatch(null);
            batchFxFromWorld = worldOrigin;
        }

        /// <summary>윷 토큰 칩 옆 [+] — 엽전 구매 / 광고 충전.</summary>
        public void OpenYutTokenShop()
        {
            EnsureBuilt();
            busy = false;
            mode = Mode.YutToken;
            batchFxFrom = null;
            batchFxFromWorld = null;
            titleText.text = "윷 토큰";
            RefreshYutTokenLabels();
            if (closeBtn != null) closeBtn.interactable = true;
            root.SetActive(true);
        }

        public void Close()
        {
            if (busy) return;
            if (root != null) root.SetActive(false);
            mode = Mode.None;
            batchFxFrom = null;
            batchFxFromWorld = null;
        }

        void RefreshBatchLabels()
        {
            var amount = GameEconomy.Instance.PendingBatchMerit;
            if (bodyText != null)
                bodyText.text = "쌓인 공덕\n" + amount.ToDisplayString();
            if (hintText != null)
            {
                hintText.gameObject.SetActive(true);
                hintText.text = "광고 시 ×2 → " + (amount * 2.0).ToDisplayString();
            }
            if (primaryLabel != null) primaryLabel.text = "받기";
            if (secondaryLabel != null)
            {
                secondaryLabel.text = GiftBundle.AdTickets > 0
                    ? "보상권으로 ×2 (" + GiftBundle.AdTickets + ")"
                    : "광고 보고 ×2";
            }
        }

        void RefreshYutTokenLabels()
        {
            var eco = GameEconomy.Instance;
            bool full = eco.YutToken >= eco.YutTokenMax;

            if (bodyText != null)
                bodyText.text = "윷 토큰 " + eco.YutToken + "/" + eco.YutTokenMax;
            if (hintText != null) hintText.gameObject.SetActive(false);

            if (primaryLabel != null)
                primaryLabel.text = full ? "토큰이 가득 찼어요" : $"엽전 {BuyCostYeopjeon}개로\n토큰 {BuyGrantTokens}개 구매";
            if (secondaryLabel != null)
                secondaryLabel.text = full ? "토큰이 가득 찼어요" : $"광고 보고\n토큰 {AdGrantTokens}개 충전";

            if (primaryBtn != null) primaryBtn.interactable = !full && eco.Yeopjeon >= BuyCostYeopjeon;
            if (secondaryBtn != null) secondaryBtn.interactable = !full;
        }

        void OnPrimaryClicked()
        {
            if (busy) return;
            if (mode == Mode.Batch) FinishBatchClaim(1);
            else if (mode == Mode.YutToken) OnYutTokenBuy();
        }

        void OnSecondaryClicked()
        {
            if (busy) return;
            if (mode == Mode.Batch) StartCoroutine(BatchClaim2xRoutine());
            else if (mode == Mode.YutToken) StartCoroutine(YutTokenAdRoutine());
        }

        IEnumerator BatchClaim2xRoutine()
        {
            busy = true;
            SetButtonsInteractable(false);

            bool usedTicket = GiftBundle.TrySpendAdTicket(1);
            if (!usedTicket)
            {
                if (secondaryLabel != null) secondaryLabel.text = "광고 시청 중…";
                yield return new WaitForSecondsRealtime(AdWatchSeconds);
            }

            FinishBatchClaim(2);
            busy = false;
        }

        void FinishBatchClaim(int multiplier)
        {
            if (!GameEconomy.Instance.HasPendingBatchMerit)
            {
                Close();
                return;
            }

            var before = GameEconomy.Instance.MeritPile;
            if (!GameEconomy.Instance.TryClaimBatchMerit(multiplier))
            {
                busy = false;
                SetButtonsInteractable(true);
                return;
            }

            var after = GameEconomy.Instance.MeritPile;
            GameSaveBridge.SaveFromWorld();

            if (GameHud.Instance != null)
            {
                GameHud.Instance.BeginMeritCountUpPublic(before, after);
                if (batchFxFrom != null)
                    GameHud.Instance.PlayMeritCollectFx(batchFxFrom);
                else if (batchFxFromWorld.HasValue)
                    GameHud.Instance.PlayMeritCollectFxFromWorld(batchFxFromWorld.Value);
            }

            busy = false;
            if (root != null) root.SetActive(false);
            mode = Mode.None;
            batchFxFrom = null;
            batchFxFromWorld = null;
        }

        void OnYutTokenBuy()
        {
            var eco = GameEconomy.Instance;
            if (eco.YutToken >= eco.YutTokenMax) return;
            if (!eco.TrySpendYeopjeon(BuyCostYeopjeon)) return;

            eco.AddYutToken(BuyGrantTokens);
            GameSaveBridge.SaveFromWorld();
            RefreshYutTokenLabels();
        }

        IEnumerator YutTokenAdRoutine()
        {
            busy = true;
            if (primaryBtn != null) primaryBtn.interactable = false;
            if (secondaryBtn != null) secondaryBtn.interactable = false;
            if (closeBtn != null) closeBtn.interactable = false;
            if (secondaryLabel != null) secondaryLabel.text = "광고 시청 중…";

            yield return new WaitForSecondsRealtime(AdWatchSeconds);

            var eco = GameEconomy.Instance;
            if (eco.YutToken < eco.YutTokenMax)
                eco.AddYutToken(AdGrantTokens);
            GameSaveBridge.SaveFromWorld();

            busy = false;
            if (closeBtn != null) closeBtn.interactable = true;
            RefreshYutTokenLabels();
        }

        void SetButtonsInteractable(bool on)
        {
            if (primaryBtn != null) primaryBtn.interactable = on;
            if (secondaryBtn != null) secondaryBtn.interactable = on;
            if (closeBtn != null) closeBtn.interactable = on;
        }

        void EnsureBuilt()
        {
            if (root != null) return;
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
        }

        void Build()
        {
            var canvasGO = new GameObject("Canvas_DualAction");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 850;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            canvasGO.AddComponent<GraphicRaycaster>();

            root = new GameObject("Panel");
            var rootRt = Stretch(root, canvasGO.transform);
            var dim = root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);

            var box = MakeBox(rootRt, new Vector2(590, 520));
            titleText = MakeText(box, "", 40, new Vector2(0, 200));
            bodyText = MakeText(box, "", 32, new Vector2(0, 100), new Color(0.9f, 0.85f, 0.7f));
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hintText = MakeText(box, "", 24, new Vector2(0, 20), new Color(1f, 0.85f, 0.45f));

            primaryBtn = MakeButton(box, "BtnPrimary", "", new Vector2(0, -70),
                new Vector2(420, 90), new Color(0.35f, 0.55f, 0.4f, 1f), OnPrimaryClicked);
            primaryLabel = primaryBtn.GetComponentInChildren<Text>();
            primaryLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

            secondaryBtn = MakeButton(box, "BtnSecondary", "", new Vector2(0, -175),
                new Vector2(420, 90), new Color(0.55f, 0.4f, 0.2f, 1f), OnSecondaryClicked);
            secondaryLabel = secondaryBtn.GetComponentInChildren<Text>();
            secondaryLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

            closeBtn = MakeButton(box, "BtnClose", "닫기", new Vector2(0, -260),
                new Vector2(200, 56), new Color(0.35f, 0.3f, 0.28f, 1f), Close);
        }

        Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size,
            Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            MakeText(rt, label, 28, Vector2.zero);
            return btn;
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
            var go = new GameObject("Box");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.16f, 0.12f, 0.1f, 0.97f);
            return rt;
        }

        Text MakeText(Transform parent, string msg, int size, Vector2 pos, Color? color = null)
        {
            var go = new GameObject("Text");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(520, 90);
            var text = go.AddComponent<Text>();
            text.font = font != null
                ? font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = UiFonts.Size(size);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color ?? Color.white;
            text.text = msg;
            text.raycastTarget = false;
            return text;
        }
    }
}
