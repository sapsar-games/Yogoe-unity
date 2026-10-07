using Yoegoe.Cooking;
using Yoegoe.Data;

namespace Yoegoe.Economy
{
    /// <summary>
    /// "요리재료 랜덤 N개" 추첨 — 선물꾸러미·윷판 재료보따리 공용 (기획 10-3 · 11장).
    /// 한 개마다 채집/사냥을 50%씩 고른 뒤 7-3 확률표(시트 prop_drop_tables)를 적용한다.
    /// 황금쌀·황금꿀은 요리재료가 아니라 제외.
    /// </summary>
    public static class IngredientDraw
    {
        public const int BundleCount = 3;

        public static CookingIngredientId[] Roll(int count = BundleCount)
        {
            int n = count < 0 ? 0 : count;
            var result = new CookingIngredientId[n];
            for (int i = 0; i < n; i++)
                result[i] = RollOne();
            return result;
        }

        public static CookingIngredientId RollOne()
        {
            var table = UnityEngine.Random.value < 0.5f ? PropResourceType.Gather : PropResourceType.Hunt;
            return (CookingIngredientId)PropCatalog.RollDrop(table, UnityEngine.Random.value, includeSpecial: false);
        }

        /// <summary>"쌀, 새알, 꿀" — 같은 재료는 "쌀 ×2".</summary>
        public static string Describe(CookingIngredientId[] ingredients)
        {
            if (ingredients == null || ingredients.Length == 0) return "";
            var counts = new System.Collections.Generic.List<(CookingIngredientId id, int n)>();
            foreach (var id in ingredients)
            {
                int k = counts.FindIndex(c => c.id == id);
                if (k >= 0) counts[k] = (id, counts[k].n + 1);
                else counts.Add((id, 1));
            }
            var parts = new string[counts.Count];
            for (int i = 0; i < counts.Count; i++)
                parts[i] = CookingRecipeCatalog.DisplayName(counts[i].id) + (counts[i].n > 1 ? " ×" + counts[i].n : "");
            return string.Join(", ", parts);
        }
    }
}
