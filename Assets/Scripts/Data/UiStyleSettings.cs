using System;
using UnityEngine;

namespace Yoegoe.Data
{
    /// <summary>
    /// UI 색·글자 크기 토큰. Assets/Resources/UiStyleSettings.asset 하나만 바꾸면 된다.
    /// (1차: DetailScreen·GameHud. 팝업·상점·윷은 이후 치환.)
    /// </summary>
    [CreateAssetMenu(fileName = "UiStyleSettings", menuName = "Yoegoe/UI Style Settings")]
    public class UiStyleSettings : ScriptableObject
    {
        [Serializable]
        public class Colors
        {
            [Header("상세 화면")]
            public Color detailBg = new Color(0.90f, 0.90f, 0.92f, 1f);
            public Color panel = new Color(1f, 1f, 1f, 1f);
            public Color border = new Color(0.15f, 0.15f, 0.18f, 1f);
            public Color portraitBg = new Color(0.78f, 0.88f, 0.95f, 1f);
            public Color portraitHighlight = new Color(0.55f, 0.85f, 0.55f, 1f);
            public Color accentBlue = new Color(0.35f, 0.55f, 0.95f, 1f);
            public Color intimacyPink = new Color(0.92f, 0.35f, 0.45f, 1f);
            public Color staminaGreen = new Color(0.35f, 0.78f, 0.42f, 1f);
            public Color barTrack = new Color(0.85f, 0.85f, 0.87f, 1f);
            public Color mutedLabel = new Color(0.45f, 0.45f, 0.5f, 1f);
            public Color feedHint = new Color(0.35f, 0.35f, 0.4f, 1f);
            public Color badgeBg = new Color(0.12f, 0.12f, 0.14f, 0.85f);
            public Color offeringHighlight = new Color(1f, 0.92f, 0.45f, 1f);
            public Color offeringIdle = new Color(0.95f, 0.95f, 0.97f, 1f);
            /// <summary>공개된 선호 공양물 인벤 칩 금테.</summary>
            public Color preferredGoldFrame = new Color(0.93f, 0.7f, 0.16f, 1f);

            [Header("공통 글자")]
            public Color textDark = new Color(0.12f, 0.12f, 0.14f, 1f);
            public Color textCream = new Color(1f, 0.95f, 0.85f, 1f);
            public Color textOnDark = new Color(1f, 0.98f, 0.92f, 1f);

            [Header("HUD")]
            public Color hudTopBar = new Color(0.1f, 0.07f, 0.06f, 0.6f);
            public Color hudSlotChip = new Color(0.11f, 0.08f, 0.07f, 0.85f);
            public Color hudSummonChip = new Color(0.08f, 0.12f, 0.14f, 0.85f);
            public Color hudStatusTag = new Color(0.2f, 0.45f, 0.85f, 0.95f);
            public Color hudStaminaTrack = new Color(0.25f, 0.2f, 0.18f, 1f);
            public Color hudStaminaFill = new Color(0.35f, 0.75f, 0.4f, 1f);
            public Color hudUpgradeButton = new Color(0.18f, 0.12f, 0.08f, 0.92f);
            public Color hudUpgradeCost = new Color(1f, 0.92f, 0.55f, 1f);
            public Color hudShopButton = new Color(0.35f, 0.25f, 0.18f, 0.92f);
            public Color hudYutButton = new Color(0.2f, 0.3f, 0.4f, 0.92f);
            public Color hudSummonName = new Color(0.7f, 0.9f, 1f, 1f);

            [Header("재화 칩 액센트")]
            public Color currencyYeopjeon = new Color(0.85f, 0.72f, 0.25f, 1f);
            public Color currencyHyang = new Color(0.75f, 0.42f, 0.85f, 1f);
            public Color currencyWater = new Color(0.4f, 0.68f, 0.9f, 1f);
            public Color currencyYutToken = new Color(0.55f, 0.75f, 0.35f, 1f);

            [Header("팝업 공통 (이후 화면 치환용)")]
            public Color dim = new Color(0f, 0f, 0f, 0.55f);
            public Color popupBox = new Color(0.14f, 0.1f, 0.08f, 0.98f);
            public Color popupConfirm = new Color(0.3f, 0.5f, 0.45f, 1f);
            public Color popupCancel = new Color(0.4f, 0.3f, 0.28f, 1f);
            public Color popupCost = new Color(1f, 0.85f, 0.45f, 1f);
        }

        [Serializable]
        public class FontSizes
        {
            [Tooltip("뱃지·아주 작은 라벨")]
            public int caption = 14;
            [Tooltip("상태 태그·보조")]
            public int small = 16;
            [Tooltip("힌트·보조 본문")]
            public int hint = 18;
            [Tooltip("본문")]
            public int body = 20;
            [Tooltip("섹션 라벨")]
            public int label = 22;
            [Tooltip("비용·중간 강조")]
            public int emphasis = 24;
            [Tooltip("버튼·상점/윷 라벨")]
            public int button = 26;
            [Tooltip("이름·타이틀급")]
            public int title = 28;
            [Tooltip("HUD 공덕")]
            public int hudMerit = 40;
            [Tooltip("HUD 재화 칩")]
            public int hudCurrency = 28;
            [Tooltip("슬롯 이름")]
            public int hudSlotName = 20;
            [Tooltip("소환 슬롯 이름")]
            public int hudSummonName = 22;
        }

        public Colors colors = new Colors();
        public FontSizes fontSizes = new FontSizes();

        static UiStyleSettings _cached;

        public static UiStyleSettings Get()
        {
            if (_cached != null) return _cached;
            _cached = Resources.Load<UiStyleSettings>("UiStyleSettings");
            if (_cached != null) return _cached;
            return CreateInstance<UiStyleSettings>();
        }

#if UNITY_EDITOR
        void OnValidate() => _cached = this;
#endif
    }
}
