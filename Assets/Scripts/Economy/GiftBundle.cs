using UnityEngine;
using Yoegoe.Cooking;

namespace Yoegoe.Economy
{
    /// <summary>10장 선물꾸러미 판정·내용물(요리재료 3개). 유저 공용 카운터(요괴별 아님).</summary>
    public static class GiftBundle
    {
        /// <summary>연속 빈손 횟수. 4면 다음은 확정.</summary>
        public static int MissStreak { get; private set; }

        /// <summary>맨 처음 들어준 요구는 확률 없이 확정.</summary>
        public static bool FirstGrantDone { get; private set; }

        public static int AdTickets { get; private set; }

        public static void ResetFromSave(int missStreak, bool firstGrantDone, int adTickets)
        {
            MissStreak = Mathf.Max(0, missStreak);
            FirstGrantDone = firstGrantDone;
            AdTickets = Mathf.Max(0, adTickets);
        }

        public static void CaptureToSave(out int missStreak, out bool firstGrantDone, out int adTickets)
        {
            missStreak = MissStreak;
            firstGrantDone = FirstGrantDone;
            adTickets = AdTickets;
        }

        public static bool TrySpendAdTicket(int amount = 1)
        {
            if (amount <= 0 || AdTickets < amount) return false;
            AdTickets -= amount;
            return true;
        }

        /// <summary>윷놀이 보물상자 등 다른 출처에서 광고보상권을 직접 지급할 때.</summary>
        public static void AddAdTickets(int amount)
        {
            if (amount <= 0) return;
            AdTickets += amount;
        }

        /// <summary>요구 들어주기 직후 호출. true면 꾸러미 지급.</summary>
        public static bool RollAfterRequestFulfilled()
        {
            if (!FirstGrantDone)
            {
                FirstGrantDone = true;
                MissStreak = 0;
                return true;
            }

            if (MissStreak >= Yoegoe.Data.GameSettings.GiftPityMisses)
            {
                MissStreak = 0;
                return true;
            }

            if (Random.value < Yoegoe.Data.GameSettings.GiftChance)
            {
                MissStreak = 0;
                return true;
            }

            MissStreak++;
            return false;
        }

        /// <summary>꾸러미 내용물 [확정] — 요리재료 랜덤 3개 (채집/사냥 50% → 7-3 확률표).</summary>
        public static CookingIngredientId[] RollContents() => IngredientDraw.Roll(IngredientDraw.BundleCount);

        /// <summary>재료를 인벤토리에 넣는다. displayName = "쌀, 새알, 꿀".</summary>
        public static void Grant(CookingIngredientId[] ingredients, out string displayName)
        {
            displayName = IngredientDraw.Describe(ingredients);
            if (ingredients == null || GameEconomy.Instance == null) return;
            foreach (var id in ingredients)
                GameEconomy.Instance.AddMaterial(id, 1);
        }
    }
}
