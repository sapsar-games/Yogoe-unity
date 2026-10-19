using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Economy;
using Yoegoe.Minigames.Yut;

namespace Yoegoe.UI
{
    public partial class GameHud
    {
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
    }
}
