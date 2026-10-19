using UnityEngine;
using UnityEngine.EventSystems;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    // ---------------- 우하단 업그레이드 (8장) ----------------
    public partial class GameHud
    {
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
    }
}
