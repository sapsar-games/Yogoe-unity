using System;
using System.Collections.Generic;
using System.Linq;

namespace Yoegoe.Cooking
{
    /// <summary>
    /// 요리책(도감) 발견 기록 (19장). 수집 수 = 레시피 결과물 수(지금 음식 36 + 공양물 24 = <b>60</b>, 시트 recipes 탭) — 같은 결과물의 다른 조합은 하나.
    /// 요리판에서 처음 완성하면 발견. 나가리를 쓰면 그 판에서 새로 발견한 것만 다시 잠근다.
    /// 황금음식은 원래 음식과 같은 칸. 세이브: <see cref="CaptureToSave"/> / <see cref="ResetFromSave"/>.
    /// </summary>
    public static class CookingCodex
    {
        static readonly HashSet<string> discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>엽전 보상을 이미 받은 결과물 id — 나가리로 discovered가 풀려도 다시 받지 않는다(레시피마다 한 번).</summary>
        static readonly HashSet<string> claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static List<string> productIds;
        static int productIdsVersion = -1;

        public static event Action Changed;

        /// <summary>도감 칸 순서 = 레시피 표 순서(음식 → 공양물), 결과물 id 중복 없이.</summary>
        public static IReadOnlyList<string> ProductIds
        {
            get
            {
                var recipes = CookingRecipeCatalog.Recipes;
                if (productIds != null && productIdsVersion == CookingRecipeCatalog.Version) return productIds;
                productIdsVersion = CookingRecipeCatalog.Version;
                productIds = new List<string>();
                var seen = new HashSet<string>();
                foreach (var r in recipes)
                    if (seen.Add(r.Id)) productIds.Add(r.Id);
                return productIds;
            }
        }

        public static int Total => ProductIds.Count;
        /// <summary>발견 수 — 발견 기록엔 레시피 결과물 id만 들어간다(Discover·ResetFromSave에서 거름).</summary>
        public static int DiscoveredCount => discovered.Count;

        /// <summary>황금음식("bap_golden")은 원래 결과물 id로.</summary>
        public static string Canonical(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            if (id.EndsWith(Yoegoe.Data.OfferingCatalog.GoldenSuffix, StringComparison.OrdinalIgnoreCase))
                id = id.Substring(0, id.Length - Yoegoe.Data.OfferingCatalog.GoldenSuffix.Length);
            return id;
        }

        public static bool IsProduct(string id) => ProductIds.Contains(Canonical(id));

        public static bool IsDiscovered(string id) => discovered.Contains(Canonical(id));

        /// <summary>발견. 처음이면 true.</summary>
        public static bool Discover(string id)
        {
            id = Canonical(id);
            if (!IsProduct(id) || !discovered.Add(id)) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>나가리 — 그 판에서 새로 발견한 것만 다시 잠근다.</summary>
        public static void Forget(string id)
        {
            if (discovered.Remove(Canonical(id))) Changed?.Invoke();
        }

        /// <summary>발견했지만 엽전 보상을 아직 안 받은 칸인지 (19장: 새 레시피 칸 → 엽전 1개).</summary>
        public static bool HasUnclaimedReward(string id)
        {
            id = Canonical(id);
            return discovered.Contains(id) && !claimed.Contains(id);
        }

        /// <summary>엽전 보상 받기 — 레시피마다 한 번뿐. 받을 게 있었으면 true(호출 쪽이 엽전을 지급).</summary>
        public static bool TryClaimReward(string id)
        {
            id = Canonical(id);
            if (!discovered.Contains(id) || !claimed.Add(id)) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>결과물의 조합(여러 개면 " / "로) — "쌀 + 팥".</summary>
        public static string ComboText(string id)
        {
            id = Canonical(id);
            var combos = new List<string>();
            foreach (var r in CookingRecipeCatalog.Recipes)
            {
                if (!string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                combos.Add(string.Join(" + ", r.Ingredients.Select(CookingRecipeCatalog.DisplayName)));
            }
            return string.Join(" / ", combos);
        }

        public static bool TryGetRecipe(string id, out CookingRecipe recipe)
        {
            id = Canonical(id);
            foreach (var r in CookingRecipeCatalog.Recipes)
            {
                if (string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) { recipe = r; return true; }
            }
            recipe = default;
            return false;
        }

        public static void ResetFromSave(IEnumerable<string> ids)
        {
            discovered.Clear();
            if (ids != null)
                foreach (var id in ids)
                    if (IsProduct(id)) discovered.Add(Canonical(id));
            Changed?.Invoke();
        }

        public static string[] CaptureToSave() => discovered.ToArray();

        public static void ResetClaimedFromSave(IEnumerable<string> ids)
        {
            claimed.Clear();
            if (ids != null)
                foreach (var id in ids)
                    if (IsProduct(id)) claimed.Add(Canonical(id));
        }

        public static string[] CaptureClaimedToSave() => claimed.ToArray();
    }
}
