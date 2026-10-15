using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 기물 길게 누르기 → 그 기물만 개별 업그레이드 (기획 8장·14장).
    /// 셸은 Prefab(Assets/Prefabs/UI/PropUpgradePopup.prefab), 여기선 문구·버튼 배선만.
    /// 업그레이드해도 닫지 않는다 — 같은 기물을 연달아 올릴 수 있게.
    /// </summary>
    public class PropUpgradePopup : SingletonBehaviour<PropUpgradePopup>
    {
        [SerializeField] GameObject panel;
        [SerializeField] Text titleText;
        [SerializeField] Text effectText;
        [SerializeField] Text costText;
        [SerializeField] Button upgradeButton;
        [SerializeField] Button closeButton;
        [SerializeField] Button dimButton;

        static readonly Color CostOk = new Color(1f, 0.85f, 0.45f);
        static readonly Color CostShort = new Color(1f, 0.5f, 0.45f);

        PropSlot target;

        public bool IsOpen => panel != null && panel.activeSelf;

        protected override void Awake()
        {
            base.Awake();
            if (panel != null) panel.SetActive(false);
        }

        void OnEnable() => MapPointerRouter.PropUpgradeRequested += Open;
        void OnDisable() => MapPointerRouter.PropUpgradeRequested -= Open;

        void Start()
        {
            UiButtonUtils.Bind(upgradeButton, OnUpgradeClicked);
            UiButtonUtils.Bind(closeButton, Close);
            UiButtonUtils.Bind(dimButton, Close);
            if (GameEconomy.Instance != null) GameEconomy.Instance.OnMeritChanged += OnMeritChanged;
        }

        protected override void OnDestroy()
        {
            if (GameEconomy.Instance != null) GameEconomy.Instance.OnMeritChanged -= OnMeritChanged;
            base.OnDestroy();
        }

        public void Open(PropSlot prop)
        {
            if (!PropEconomy.CanUpgrade(prop) || panel == null) return;
            target = prop;
            Refresh();
            panel.SetActive(true);
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
            target = null;
        }

        void OnMeritChanged(BigNumber _)
        {
            if (IsOpen) Refresh();
        }

        void OnUpgradeClicked()
        {
            if (target == null) return;
            if (!PropEconomy.TryUpgrade(target))
            {
                Refresh(notEnough: true);
                return;
            }
            PropUpgradeFx.SpawnWorld(target.transform.position, "+1 급", titleText != null ? titleText.font : null);
            GameSaveBridge.SaveFromWorld();
            Refresh();
        }

        void Refresh(bool notEnough = false)
        {
            if (target == null) return;
            var cost = PropEconomy.GetUpgradeCost(target);
            bool affordable = GameEconomy.Instance != null && GameEconomy.Instance.MeritPile >= cost;

            if (titleText != null) titleText.text = $"{target.DisplayName} {target.level}급";
            if (effectText != null) effectText.text = EffectText(target.ProductionConfig, target.level);
            if (costText != null)
            {
                costText.text = notEnough || !affordable
                    ? $"공덕이 부족해요 (공덕 {cost.ToDisplayString()})"
                    : $"공덕 {cost.ToDisplayString()}";
                costText.color = affordable ? CostOk : CostShort;
            }
        }

        /// <summary>
        /// 다음 급에서 바뀌는 것. 공덕 기물 = 분당 공덕(보정 전), 자원 기물 = 보관 개수·넘침 확률
        /// (보관은 10급마다 +1, 넘침 확률은 급 끝자리 × 10% — <see cref="PropStorage"/>).
        /// </summary>
        public static string EffectText(in PropProduction.Config c, int level)
        {
            int next = level + 1;
            if (c.Type == PropResourceType.Merit)
            {
                BigNumber now = PropProduction.BaseMeritPerMinute(c, level);
                BigNumber after = PropProduction.BaseMeritPerMinute(c, next);
                return $"{level}급 → {next}급\n분당 공덕 {now.ToDisplayString()} → {after.ToDisplayString()}";
            }
            if (PropProduction.IsResource(c.Type))
            {
                int capNow = PropProduction.ResourceCapacity(c, level);
                int capNext = PropProduction.ResourceCapacity(c, next);
                int chanceNow = Mathf.RoundToInt(PropStorage.OverflowChance(level) * 100f);
                int chanceNext = Mathf.RoundToInt(PropStorage.OverflowChance(next) * 100f);
                return $"{level}급 → {next}급\n보관 {capNow}개 → {capNext}개 · 넘침 확률 {chanceNow}% → {chanceNext}%";
            }
            return $"{level}급 → {next}급";
        }
    }
}
