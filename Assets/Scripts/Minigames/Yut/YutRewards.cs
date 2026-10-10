using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Data;

namespace Yoegoe.Minigames.Yut
{
    /// <summary>특수 칸·보물상자·완주에서 고른 재화 종류.</summary>
    public enum YutSquareRewardKind
    {
        Offering,
        Yeopjeon,
        Hyang,
        AdTicket,
        YutToken,
        Water,
        IngredientBundle,
        Charm,
    }

    /// <summary>특수 칸 보상 팝업에 올리기 전, 지급할 내용(배율 적용 전).</summary>
    public readonly struct YutSquareReward
    {
        public readonly YutSquareRewardKind Kind;
        public readonly OfferingData Offering; // Kind == Offering일 때만
        public readonly int Amount; // 배율 적용 전 기본 개수
        public readonly CookingIngredientId[] Ingredients; // IngredientBundle
        public readonly CookingCharmType Charm; // Charm

        public YutSquareReward(YutSquareRewardKind kind, OfferingData offering, int amount,
            CookingIngredientId[] ingredients = null, CookingCharmType charm = CookingCharmType.None)
        {
            Kind = kind;
            Offering = offering;
            Amount = amount;
            Ingredients = ingredients;
            Charm = charm;
        }
    }

    /// <summary>
    /// 윷놀이 보상 수치·판정·뽑기. 지급(GameEconomy)·팝업은 Presenter/Screen 책임.
    /// Docs/00 §11: 특수칸 5 · 보물상자 확률 · 완주 부적.
    /// </summary>
    public static class YutRewards
    {
        /// <summary>재료보따리 — 랜덤 재료 개수(광고 2배 시 ×배수).</summary>
        public const int IngredientBundleCount = Yoegoe.Economy.IngredientDraw.BundleCount;

        /// <summary>특수 칸 — "그냥 받기" / "광고 보고 2배".</summary>
        public const int SquareRewardBase = 1;
        public const int SquareRewardAdMultiplier = 2;
        public const float SquareRewardAdWatchSeconds = 0.8f;

        /// <summary>윷 토큰 보상이 평소 상한을 넘어 쌓일 수 있는 최대치 (보물상자 확률표엔 지금 토큰이 없음).</summary>
        public const int YutTokenHardCap = 7;

        /// <summary>매 판 도전과제 — 완료 시 보물상자 개수(한 번에 개봉, 광고 2배 없음).</summary>
        public const int ChallengeChestCount = 3;
        /// <summary>연속 모/빽도 과제에 필요한 연속 횟수.</summary>
        public const int ChallengeConsecutiveNeeded = 2;
        /// <summary>미잡힘 전원 완주 과제에 필요한 최소 말 수(“넷 다”).</summary>
        public const int ChallengeFinishAllPieceCount = 4;

        /// <summary>말 완주 보상 부적 1개 — 6종(나가리 포함) 중 시트 charms 탭 가중치대로.</summary>
        public static CookingCharmType RollFinishCharm() => CharmDropRates.Roll(UnityEngine.Random.value);

        /// <summary>재료보따리 — 선물꾸러미와 같은 추첨(채집/사냥 50% → 7-3 확률표). IngredientDraw 참고.</summary>
        public static CookingIngredientId[] RollIngredientBundle(int count = IngredientBundleCount) =>
            Yoegoe.Economy.IngredientDraw.Roll(count);

        /// <summary>
        /// 보물상자 — 공양물 50% · 광고보상권 30% · 향 5% · 엽전 3개 15% (비율·엽전 수 = 시트 game_settings).
        /// </summary>
        public static YutSquareReward RollTreasure(IReadOnlyList<OfferingData> offeringPool)
        {
            float wO = UnityEngine.Mathf.Max(0f, GameSettings.ChestOfferingWeight);
            float wA = UnityEngine.Mathf.Max(0f, GameSettings.ChestAdTicketWeight);
            float wH = UnityEngine.Mathf.Max(0f, GameSettings.ChestHyangWeight);
            float wY = UnityEngine.Mathf.Max(0f, GameSettings.ChestYeopjeonWeight);
            float total = wO + wA + wH + wY;
            float r = total > 0f ? UnityEngine.Random.value * total : 0f;
            if (total <= 0f || r < wO)
            {
                var offering = offeringPool != null && offeringPool.Count > 0
                    ? offeringPool[UnityEngine.Random.Range(0, offeringPool.Count)]
                    : null;
                return new YutSquareReward(YutSquareRewardKind.Offering, offering, 1);
            }
            if (r < wO + wA)
                return new YutSquareReward(YutSquareRewardKind.AdTicket, null, 1);
            if (r < wO + wA + wH)
                return new YutSquareReward(YutSquareRewardKind.Hyang, null, 1);
            return new YutSquareReward(YutSquareRewardKind.Yeopjeon, null, GameSettings.ChestYeopjeonAmount);
        }

        /// <summary>지급 전 미리보기용 문구(배율 1 기준).</summary>
        public static string DescribeSquareReward(YutSquareReward reward)
        {
            int amount = reward.Amount;
            switch (reward.Kind)
            {
                case YutSquareRewardKind.Yeopjeon: return $"엽전 {amount}개";
                case YutSquareRewardKind.Water: return $"물 {amount}개";
                case YutSquareRewardKind.Hyang: return $"향 {amount}개";
                case YutSquareRewardKind.AdTicket: return $"광고보상권 {amount}개";
                case YutSquareRewardKind.YutToken: return $"윷 토큰 {amount}개";
                case YutSquareRewardKind.Offering:
                    string name = reward.Offering != null ? reward.Offering.displayName : "공양물";
                    return $"{name} {amount}개";
                case YutSquareRewardKind.IngredientBundle:
                    return DescribeIngredients(reward.Ingredients);
                case YutSquareRewardKind.Charm:
                    return CharmDisplayName(reward.Charm);
                default: return "보상";
            }
        }

        public static string CharmDisplayName(CookingCharmType charm) => charm switch
        {
            CookingCharmType.PlusFive => "+5초 부적",
            CookingCharmType.Diagonal => "대각선 부적",
            CookingCharmType.Clairvoyance => "천리안 부적",
            CookingCharmType.Recycle => "회수 부적",
            CookingCharmType.Double => "몰빵 부적",
            CookingCharmType.Cancel => "나가리",
            _ => "부적",
        };

        public static string DescribeIngredients(CookingIngredientId[] ingredients)
        {
            if (ingredients == null || ingredients.Length == 0) return "재료보따리";
            var sb = new StringBuilder();
            sb.Append("재료 ");
            for (int i = 0; i < ingredients.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(CookingRecipeCatalog.DisplayName(ingredients[i]));
            }
            return sb.ToString();
        }
    }
}
