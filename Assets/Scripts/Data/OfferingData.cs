using UnityEngine;

namespace Yoegoe.Data
{
    /// <summary>음식·공양물·물 정의. 효과는 Docs/00 §4 본문 수치.</summary>
    [CreateAssetMenu(fileName = "OfferingData", menuName = "Yoegoe/Offering Data")]
    public class OfferingData : ScriptableObject
    {
        public string offeringId;
        public string displayName;
        public OfferingKind kind;
        public Sprite icon;

        [Header("공양 효과 (본문 §5)")]
        [Tooltip("0이면 종류 기본값: 음식+8 / 공양물·선호+3 / 물+3")]
        public int staminaGain;

        [Tooltip("0이면 종류 기본값: 음식·물 0 / 공양물+2 / 선호는 코드에서 +5")]
        public float intimacyGain;

        [Header("상점 (12장)")]
        [Tooltip("엽전 가격. 물은 0.")]
        public int shopPriceYeopjeon = 10;

        /// <summary>
        /// 황금음식 (공양간 기획서): 황금쌀·황금꿀이 들어간 음식. 효과는 일반 음식과 같고, 먹으면
        /// 5분간 요괴가 황금색 + 이동·생산 2배. 런타임 카탈로그가 음식마다 만든다(<see cref="OfferingCatalog"/>).
        /// </summary>
        [System.NonSerialized] public bool golden;
        /// <summary>황금음식이면 원래 음식 id (요구 판정 등).</summary>
        [System.NonSerialized] public string baseOfferingId;

        /// <summary>요구·도감에서 같은 음식으로 볼 id — 황금음식이면 원래 음식 id.</summary>
        public string BaseId => string.IsNullOrEmpty(baseOfferingId) ? offeringId : baseOfferingId;

        public bool IsWater => kind == OfferingKind.Water;

        public int ResolveStaminaGain(bool isPreferred)
        {
            if (staminaGain > 0) return staminaGain;
            if (kind == OfferingKind.Water) return 3;
            // 음식·공양물 기력은 시트 game_settings (v1.3: 음식 +10 · 공양물 +15, 선호도 같음)
            if (kind == OfferingKind.Food) return GameSettings.FoodStamina;
            return GameSettings.OfferingStamina;
        }

        public float ResolveIntimacyGain(bool isPreferred)
        {
            if (kind == OfferingKind.Water || kind == OfferingKind.Food) return 0f;
            if (isPreferred) return intimacyGain > 0f ? intimacyGain : GameSettings.PreferredIntimacy;
            return intimacyGain > 0f ? intimacyGain : GameSettings.OfferingIntimacy;
        }
    }
}
