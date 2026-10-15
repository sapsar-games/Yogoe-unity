using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 빈 자리(자물쇠) 탭 → 구매 확인. 셸은 <see cref="ConfirmPopup"/> Prefab.
    /// </summary>
    public class PropPurchasePopup : SingletonBehaviour<PropPurchasePopup>
    {
        PropSlot target;
        /// <summary>자물쇠 위에 드롭해서 팝업을 열었을 때 — 건설 성공 후 앉힐 요괴.</summary>
        CharacterAgent sitAfterBuild;

        void OnEnable() => MapPointerRouter.PropPurchaseRequested += Open;
        void OnDisable() => MapPointerRouter.PropPurchaseRequested -= Open;

        public void Open(PropSlot prop) => Open(prop, null);

        public void Open(PropSlot prop, CharacterAgent sitAfter)
        {
            if (prop == null || prop.IsBuilt) return;
            target = prop;
            sitAfterBuild = sitAfter;
            string name = prop.DisplayName;
            string cost = PropEconomy.GetNextPurchaseCost().ToDisplayString();
            ConfirmPopup.Show(
                name + "을(를) 그릴까요?\n비용 " + cost,
                OnBuildConfirmed,
                OnCancelled,
                "건설",
                "닫기");
        }

        public void Close()
        {
            ConfirmPopup.Dismiss();
            ClearState();
        }

        void OnCancelled() => ClearState();

        void ClearState()
        {
            target = null;
            sitAfterBuild = null;
        }

        void OnBuildConfirmed()
        {
            if (target == null) return;
            var prop = target;
            var sitAgent = sitAfterBuild;
            if (!PropEconomy.TryPurchase(prop))
            {
                string cost = PropEconomy.GetNextPurchaseCost().ToDisplayString();
                ConfirmPopup.Show(
                    "공덕이 부족합니다\n(" + cost + ")",
                    null,
                    OnCancelled,
                    "확인",
                    "닫기");
                return;
            }

            ClearState();
            if (sitAgent != null)
                sitAgent.TrySitOnProp(prop);
            GameSaveBridge.SaveFromWorld();
        }
    }
}
