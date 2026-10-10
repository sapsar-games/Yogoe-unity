using UnityEngine;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Characters
{
    /// <summary>공양이 막힌 이유 — UI(상세 화면)가 문구로 바꿔 보여 준다.</summary>
    public enum FeedBlock
    {
        None,
        /// <summary>인벤토리에 없음.</summary>
        NoItem,
        /// <summary>기력이 가득 찼고 오를 친밀도도 없음(음식·물).</summary>
        StaminaFull,
        /// <summary>기력·친밀도가 모두 가득.</summary>
        StaminaAndIntimacyFull,
        /// <summary>기절 — 물로만 깨어난다.</summary>
        FaintedNeedsWater,
    }

    /// <summary>공양 한 번의 결과.</summary>
    public readonly struct FeedResult
    {
        public readonly FeedBlock Block;
        public readonly int StaminaGain;
        public readonly float IntimacyGain;
        /// <summary>음식 요구를 채웠는지 (+12, 선물꾸러미 판정은 요구 쪽에서).</summary>
        public readonly bool RequestFulfilled;
        /// <summary>이번 공양으로 선호가 처음 공개됐는지.</summary>
        public readonly bool PreferenceRevealed;
        /// <summary>황금음식이라 5분 황금 버프가 걸렸는지.</summary>
        public readonly bool GoldenBuff;

        public bool Success => Block == FeedBlock.None;

        public FeedResult(FeedBlock block, int stamina = 0, float intimacy = 0f, bool request = false, bool revealed = false,
            bool golden = false)
        {
            GoldenBuff = golden;
            Block = block;
            StaminaGain = stamina;
            IntimacyGain = intimacy;
            RequestFulfilled = request;
            PreferenceRevealed = revealed;
        }
    }

    // 공양 규칙 (Docs/00 5장). 상세 화면은 결과만 받아 문구·연출을 띄운다.
    public partial class CharacterAgent
    {
        /// <summary>이 요괴의 선호 공양물인지 (시트 character_preferences 우선, 없으면 CharacterData).</summary>
        public bool IsPreferredOffering(OfferingData offering)
        {
            if (offering == null || Data == null) return false;
            if (CharacterCatalog.TryGet(Data.id, out var entry) && entry?.preferredOfferings != null)
            {
                foreach (var p in entry.preferredOfferings)
                    if (p != null && string.Equals(p.id, offering.offeringId, System.StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            var prefs = Data.preferredOfferings;
            if (prefs == null) return false;
            foreach (var p in prefs)
                if (p == offering) return true;
            return false;
        }

        bool IsStaminaFull => Stats.Stamina >= MaxStamina - 0.001f;

        /// <summary>
        /// 물 공양: 기력 +3(물 데이터 값). 기절이면 +1만(0→1, 기절 회복은 물만).
        /// 기력이 가득이면 오를 게 없어 막는다(낭비 방지).
        /// </summary>
        public FeedResult TryFeedWater(OfferingData waterData = null, GameEconomy economy = null)
        {
            var eco = economy != null ? economy : GameEconomy.Instance;
            if (eco == null || eco.Water < 1) return new FeedResult(FeedBlock.NoItem);
            if (IsStaminaFull) return new FeedResult(FeedBlock.StaminaFull);
            if (!eco.TrySpendWater(1)) return new FeedResult(FeedBlock.NoItem);

            int gain = Stats.State == ActionState.Fainted
                ? 1
                : waterData != null ? waterData.ResolveStaminaGain(false) : 3;
            ReceiveOffering(gain, 0f, OfferingKind.Water);
            return new FeedResult(FeedBlock.None, gain);
        }

        /// <summary>
        /// 음식·공양물 공양. 물이면 <see cref="TryFeedWater"/>로.
        /// - 기절 중엔 막힘(물로만)
        /// - 기력 가득: 친밀도가 오를 때만 허용(음식·친밀도 100이면 막힘)
        /// - 그릇 말풍선(배고픔)이 이번 그릇으로 풀리면 requestFulfillBonus 추가 · 선물꾸러미 판정
        /// - 선호 공양물은 성공하면 영구 공개
        /// - 황금음식: 효과는 같은 음식과 같고 + 5분 황금 버프. 버프가 목적이라 기력이 가득이어도 먹일 수 있다
        /// </summary>
        public FeedResult TryFeed(OfferingData offering, GameEconomy economy = null)
        {
            if (offering == null) return new FeedResult(FeedBlock.NoItem);
            if (offering.IsWater) return TryFeedWater(offering, economy);
            if (Stats.State == ActionState.Fainted) return new FeedResult(FeedBlock.FaintedNeedsWater);

            var eco = economy != null ? economy : GameEconomy.Instance;
            bool preferred = IsPreferredOffering(offering);
            var kind = preferred ? OfferingKind.Preferred : OfferingKind.General;
            int staminaGain = offering.ResolveStaminaGain(preferred);
            float intimacyGain = offering.ResolveIntimacyGain(preferred);

            if (IsStaminaFull && !Requests.HasOfferingRequest && !offering.golden)
            {
                if (intimacyGain <= 0.0001f) return new FeedResult(FeedBlock.StaminaFull);
                if (Stats.Intimacy >= 100f - 0.001f) return new FeedResult(FeedBlock.StaminaAndIntimacyFull);
            }

            if (eco == null || !eco.TrySpendOffering(offering, 1)) return new FeedResult(FeedBlock.NoItem);

            bool fulfilled = false;
            // 그릇 말풍선(배고픔)을 이번 그릇으로 풀면 보너스 + 고맙다는 말·선물꾸러미 (v1.3)
            if (Requests.TryHandleFeed(offering, false, preferred, out int reqStamina, out float reqIntimacy, out _))
            {
                staminaGain = reqStamina;
                intimacyGain = reqIntimacy;
                fulfilled = true;
            }

            ReceiveOffering(staminaGain, intimacyGain, kind);
            // 선호 공양물은 실제로 먹여야 영구 공개(상세 표기·인벤 금테)
            bool revealed = preferred && Stats.RevealPreference(offering.offeringId);
            if (offering.golden) ApplyGoldenBuff();
            return new FeedResult(FeedBlock.None, staminaGain, intimacyGain, fulfilled, revealed, offering.golden);
        }
    }
}
