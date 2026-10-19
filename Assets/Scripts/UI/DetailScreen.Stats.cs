using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.UI
{
    public partial class DetailScreen
    {
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
    }
}
