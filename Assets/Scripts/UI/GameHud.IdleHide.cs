using UnityEngine;
using UnityEngine.InputSystem;
using Yoegoe.Core;

namespace Yoegoe.UI
{
    public partial class GameHud
    {
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
    }
}
