using System;
using System.Collections.Generic;
using Yoegoe.Characters;
using Yoegoe.Data;

namespace Yoegoe.Cooking
{
    /// <summary>
    /// 공양간 주문 요괴: 선호 공양물을 지금 판에서 지을 수 있을 때만 등장.
    /// 퍼펙트(김) → 친밀도·기력 ×2 · 인벤 미지급 / 식음 → 친밀도 평소대로·기력 ×2 · 인벤 미지급 / 미제공 → 실망.
    /// 기절한 요괴도 손님으로 온다 — 받으면 친밀도 없이 그 기력만큼 회복하며 바로 깨어난다(물의 '기력 1' 단계를 건너뜀).
    /// </summary>
    public sealed class CookingGuestOrder
    {
        public const int PerfectStaminaMul = 2;
        public const int PerfectIntimacyMul = 2;
        public const int CoolStaminaMul = 2;
        /// <summary>식음은 친밀도 평소대로(×1).</summary>
        public const int CoolIntimacyMul = 1;

        public CharacterAgent Yokai { get; }
        public string CharacterId { get; }
        public string DisplayName { get; }
        public string OfferingId { get; }
        public string OfferingName { get; }

        public bool Fulfilled { get; private set; }
        public bool Failed { get; private set; }
        public bool Perfect { get; private set; }
        public int StaminaGain { get; private set; }
        public float IntimacyGain { get; private set; }
        /// <summary>기절한 요괴가 받아서 깨어났는지 (친밀도 없음).</summary>
        public bool RevivedFromFaint { get; private set; }

        public CookingGuestOrder(CharacterAgent yokai, string characterId, string displayName,
            string offeringId, string offeringName)
        {
            Yokai = yokai;
            CharacterId = characterId ?? "";
            DisplayName = displayName ?? "";
            OfferingId = offeringId ?? "";
            OfferingName = offeringName ?? "";
        }

        /// <summary>보유 요괴 × 지금 만들 수 있는 선호 공양물 후보에서 하나 고른다. 없으면 null.</summary>
        public static CookingGuestOrder TryCreate(IReadOnlyList<CookingRecipe> makeable,
            Func<int, int, int> randRange)
        {
            if (makeable == null || makeable.Count == 0 || randRange == null) return null;

            var offeringIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < makeable.Count; i++)
            {
                var r = makeable[i];
                if (r.Kind != CookingResultKind.Offering || string.IsNullOrEmpty(r.Id)) continue;
                offeringIds.Add(r.Id);
                nameById[r.Id] = r.DisplayName;
            }
            if (offeringIds.Count == 0) return null;

            var cands = new List<(CharacterAgent agent, string charId, string charName, string offId, string offName)>();
            var agents = CharacterAgent.All;
            for (int a = 0; a < agents.Count; a++)
            {
                var agent = agents[a];
                if (agent == null || agent.Data == null) continue;
                CollectPrefCandidates(agent, agent.Data.id, agent.Data.displayName, offeringIds, nameById, cands);
            }
            if (cands.Count == 0) return null;

            var pick = cands[randRange(0, cands.Count)];
            return new CookingGuestOrder(pick.agent, pick.charId, pick.charName, pick.offId, pick.offName);
        }

        /// <summary>테스트용 — 에이전트 없이 선호 id 목록만으로 후보를 고른다.</summary>
        public static bool TryPickOfferingId(IReadOnlyList<CookingRecipe> makeable,
            IReadOnlyList<(string characterId, string displayName, string[] prefIds)> owned,
            Func<int, int, int> randRange,
            out string characterId, out string offeringId)
        {
            characterId = null;
            offeringId = null;
            if (makeable == null || owned == null || randRange == null) return false;

            var offeringIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < makeable.Count; i++)
            {
                var r = makeable[i];
                if (r.Kind == CookingResultKind.Offering && !string.IsNullOrEmpty(r.Id))
                    offeringIds.Add(r.Id);
            }

            var cands = new List<(string charId, string offId)>();
            for (int i = 0; i < owned.Count; i++)
            {
                var o = owned[i];
                if (o.prefIds == null) continue;
                for (int p = 0; p < o.prefIds.Length; p++)
                {
                    var id = o.prefIds[p];
                    if (!string.IsNullOrEmpty(id) && offeringIds.Contains(id))
                        cands.Add((o.characterId, id));
                }
            }
            if (cands.Count == 0) return false;
            var pick = cands[randRange(0, cands.Count)];
            characterId = pick.charId;
            offeringId = pick.offId;
            return true;
        }

        static void CollectPrefCandidates(CharacterAgent agent, CharacterId charId, string charName,
            HashSet<string> offeringIds, Dictionary<string, string> nameById,
            List<(CharacterAgent, string, string, string, string)> cands)
        {
            string charKey = charId.ToString();
            if (CharacterCatalog.TryGet(charId, out var entry) && entry?.preferredOfferings != null)
            {
                foreach (var p in entry.preferredOfferings)
                {
                    if (p == null || string.IsNullOrEmpty(p.id) || !offeringIds.Contains(p.id)) continue;
                    string nm = !string.IsNullOrEmpty(p.name) ? p.name
                        : (nameById.TryGetValue(p.id, out var n) ? n : p.id);
                    cands.Add((agent, charKey, charName, p.id, nm));
                }
                return;
            }

            var soPrefs = agent.Data?.preferredOfferings;
            if (soPrefs == null) return;
            foreach (var o in soPrefs)
            {
                if (o == null || string.IsNullOrEmpty(o.offeringId) || !offeringIds.Contains(o.offeringId)) continue;
                cands.Add((agent, charKey, charName, o.offeringId,
                    string.IsNullOrEmpty(o.displayName) ? o.offeringId : o.displayName));
            }
        }

        /// <summary>선호 공양물 기본 수치에 배율 적용. 퍼펙트=기력·친밀 ×2, 식음=친밀 ×1·기력 ×2.</summary>
        public static void ComputeReward(OfferingData offering, bool perfect, out int stamina, out float intimacy)
        {
            int baseStam = offering != null ? offering.ResolveStaminaGain(true) : 3;
            float baseInti = offering != null ? offering.ResolveIntimacyGain(true) : 5f;
            if (perfect)
            {
                stamina = baseStam * PerfectStaminaMul;
                intimacy = baseInti * PerfectIntimacyMul;
            }
            else
            {
                stamina = baseStam * CoolStaminaMul;
                intimacy = baseInti * CoolIntimacyMul;
            }
        }

        public bool Matches(CookingRecipe recipe) =>
            recipe.Kind == CookingResultKind.Offering
            && !string.IsNullOrEmpty(OfferingId)
            && string.Equals(recipe.Id, OfferingId, StringComparison.OrdinalIgnoreCase);

        /// <summary>주문 공양물을 줬을 때. 인벤에는 넣지 않고 요괴에게 바로 적용.</summary>
        public void Deliver(bool perfect)
        {
            if (Fulfilled || Failed) return;
            Fulfilled = true;
            Perfect = perfect;

            var offering = OfferingCatalog.Find(OfferingId);
            ComputeReward(offering, perfect, out int stam, out float inti);
            StaminaGain = stam;
            IntimacyGain = inti;

            if (Yokai != null && Yokai.Data != null)
            {
                if (Yokai.Stats.State == ActionState.Fainted)
                {
                    // 기절: 기력만 회복하며 깨어남 (기절을 깨우는 경로는 물과 같다 — 친밀도 0)
                    RevivedFromFaint = true;
                    IntimacyGain = 0f;
                    Yokai.ReceiveOffering(stam, 0f, OfferingKind.Water);
                }
                else
                    Yokai.ReceiveOffering(stam, inti, OfferingKind.Preferred);
                if (!string.IsNullOrEmpty(OfferingId))
                    Yokai.Stats.RevealPreference(OfferingId);
            }
        }

        public void MarkFailed()
        {
            if (Fulfilled || Failed) return;
            Failed = true;
        }

        public string WaitLine => "맛있는 냄새가 나…";
        public string ResultLine =>
            Failed ? "실망…"
            : !Fulfilled ? WaitLine
            : Perfect ? "최고야!"
            : "고마워!";

        public string RewardHint =>
            Failed ? OfferingName + "을(를) 못 먹었어"
            : !Fulfilled ? OfferingName + "?"
            : RevivedFromFaint ? $"기력 +{StaminaGain} — 깨어났어"
            : Perfect ? $"친밀도·기력 ×{PerfectStaminaMul}"
            : $"기력 ×{CoolStaminaMul}";
    }
}
