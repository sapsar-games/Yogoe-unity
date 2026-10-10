using System;
using System.Collections.Generic;
using Yoegoe.Core;
using Yoegoe.Data;
using UnityEngine;

namespace Yoegoe.Cooking
{
    /// <summary>
    /// 공양간 레시피. 정본 = 시트 recipes 탭 → Resources/recipes.json (npm run recipes) — 기획자가 조합을 바꾼다.
    /// 재료 순서는 무시(정렬 키 비교). 같은 결과물 id의 여러 줄 = 다른 조합.
    /// </summary>
    public static class CookingRecipeCatalog
    {
        static readonly Dictionary<string, CookingRecipe> ByKey = new Dictionary<string, CookingRecipe>();
        static readonly List<CookingRecipe> All = new List<CookingRecipe>();
        static bool loaded;

        public const string ResourcePath = "recipes";
        /// <summary>레시피를 다시 읽을 때마다 +1 (도감 칸 목록 캐시 무효화용).</summary>
        public static int Version { get; private set; }

        public static IReadOnlyList<CookingRecipe> Recipes
        {
            get { Ensure(); return All; }
        }

        [Serializable] class RecipeRow { public string id; public string name; public string kind; public string[] ingredients; public string description; }

        static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>요리책 상세 설명 (시트 recipes 탭 description). 없으면 빈 문자열.</summary>
        public static string Description(string productId)
        {
            Ensure();
            return !string.IsNullOrEmpty(productId) && Descriptions.TryGetValue(productId, out var d) ? d : "";
        }
        [Serializable] class RecipeFile { public RecipeRow[] recipes; }

        public static void Ensure()
        {
            if (loaded) return;
            loaded = true;
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError("[CookingRecipeCatalog] Resources/recipes.json 이 없습니다 — npm run recipes");
                return;
            }
            LoadFromJson(asset.text);
        }

        /// <summary>recipes.json 내용으로 다시 채운다 (테스트·핫리로드). 잘못된 줄은 건너뛰고 로그.</summary>
        public static void LoadFromJson(string json)
        {
            loaded = true;
            Version++;
            All.Clear();
            ByKey.Clear();
            Descriptions.Clear();
            var file = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<RecipeFile>(json);
            if (file?.recipes == null) return;
            foreach (var row in file.recipes)
            {
                if (row == null || string.IsNullOrEmpty(row.id) || row.ingredients == null) continue;
                if (!Enum.TryParse(row.kind, true, out CookingResultKind kind))
                {
                    Debug.LogError($"[CookingRecipeCatalog] {row.id}: kind '{row.kind}' 를 모름");
                    continue;
                }
                var ings = new CookingIngredientId[row.ingredients.Length];
                bool ok = row.ingredients.Length >= 2;
                for (int i = 0; i < ings.Length && ok; i++)
                    ok = Enum.TryParse(row.ingredients[i], true, out ings[i]) && ings[i] != CookingIngredientId.Count;
                if (!ok)
                {
                    Debug.LogError($"[CookingRecipeCatalog] {row.id}: 재료 '{string.Join(",", row.ingredients)}' 를 모름");
                    continue;
                }
                Add(new CookingRecipe(row.id, string.IsNullOrEmpty(row.name) ? row.id : row.name, kind, ings));
                if (!string.IsNullOrEmpty(row.description) && !Descriptions.ContainsKey(row.id))
                    Descriptions[row.id] = row.description;
            }
        }

        static void Add(CookingRecipe r)
        {
            All.Add(r);
            string key = KeyOf(r.Ingredients);
            if (!ByKey.ContainsKey(key))
                ByKey[key] = r;
        }

        public static bool TryMatch(IList<CookingIngredientId> path, out CookingRecipe recipe)
        {
            Ensure();
            recipe = default;
            if (path == null || (path.Count != 2 && path.Count != 3)) return false;
            var arr = new CookingIngredientId[path.Count];
            for (int i = 0; i < path.Count; i++) arr[i] = path[i];
            Array.Sort(arr);
            return ByKey.TryGetValue(KeyOf(arr), out recipe);
        }

        /// <summary>재료 개수(count)만으로 이 레시피를 만들 수 있는지 — 같은 재료 2개(곶감 등)는 2개 필요.</summary>
        public static bool CanMakeWith(in CookingRecipe recipe, Func<CookingIngredientId, int> count)
        {
            if (recipe.Ingredients == null || recipe.Ingredients.Length == 0 || count == null) return false;
            var ings = recipe.Ingredients; // 정렬돼 있어 같은 재료가 붙어 있다
            for (int i = 0; i < ings.Length;)
            {
                int j = i;
                while (j < ings.Length && ings[j] == ings[i]) j++;
                if (count(ings[i]) < j - i) return false;
                i = j;
            }
            return true;
        }

        /// <summary>현재 남은 칸으로 완성 가능한 레시피가 하나라도 있으면 true.</summary>
        public static bool AnyCompletable(CookingIngredientId?[,] grid, bool diagonal)
        {
            foreach (var _ in EnumerateCompletable(grid, diagonal)) return true;
            return false;
        }

        /// <summary>현재 판에서 만들 수 있는 레시피 (결과물 id 기준 중복 없이) — 상태 줄 '지금 만들 수 있는 요리'.</summary>
        public static List<CookingRecipe> CompletableRecipes(CookingIngredientId?[,] grid, bool diagonal)
        {
            var list = new List<CookingRecipe>();
            var seen = new HashSet<string>();
            foreach (var r in EnumerateCompletable(grid, diagonal))
                if (seen.Add(r.Id)) list.Add(r);
            return list;
        }

        /// <summary>연결된 2~3칸 조합 중 레시피가 되는 것 (같은 레시피가 여러 번 나올 수 있음).</summary>
        static IEnumerable<CookingRecipe> EnumerateCompletable(CookingIngredientId?[,] grid, bool diagonal)
        {
            Ensure();
            if (grid == null) yield break;
            int w = grid.GetLength(0);
            int h = grid.GetLength(1);
            var cells = new List<(int x, int y, CookingIngredientId id)>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (grid[x, y].HasValue)
                    cells.Add((x, y, grid[x, y].Value));
            }
            if (cells.Count < 2) yield break;

            // 2~3칸 부분집합 + 연결성 검사 (작아서 전수 OK)
            for (int i = 0; i < cells.Count; i++)
            for (int j = i + 1; j < cells.Count; j++)
            {
                var two = new[] { cells[i].id, cells[j].id };
                Array.Sort(two);
                if (ByKey.TryGetValue(KeyOf(two), out var r2)
                    && IsConnected(new[] { cells[i], cells[j] }, diagonal))
                    yield return r2;

                for (int k = j + 1; k < cells.Count; k++)
                {
                    var three = new[] { cells[i].id, cells[j].id, cells[k].id };
                    Array.Sort(three);
                    if (ByKey.TryGetValue(KeyOf(three), out var r3)
                        && IsConnected(new[] { cells[i], cells[j], cells[k] }, diagonal))
                        yield return r3;
                }
            }
        }

        static bool IsConnected((int x, int y, CookingIngredientId id)[] nodes, bool diagonal)
        {
            if (nodes.Length <= 1) return true;
            var seen = new bool[nodes.Length];
            var q = new Queue<int>();
            q.Enqueue(0);
            seen[0] = true;
            int found = 1;
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (seen[i]) continue;
                    if (!Adjacent(nodes[cur].x, nodes[cur].y, nodes[i].x, nodes[i].y, diagonal)) continue;
                    seen[i] = true;
                    found++;
                    q.Enqueue(i);
                }
            }
            return found == nodes.Length;
        }

        public static bool Adjacent(int x0, int y0, int x1, int y1, bool diagonal)
        {
            int dx = Math.Abs(x0 - x1);
            int dy = Math.Abs(y0 - y1);
            if (dx + dy == 0) return false;
            if (diagonal) return dx <= 1 && dy <= 1;
            return (dx + dy) == 1;
        }

        static string KeyOf(CookingIngredientId[] sorted)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < sorted.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append((int)sorted[i]);
            }
            return sb.ToString();
        }

        /// <summary>재료 이름 — 시트 ingredients 탭 name 우선, 없으면 기본 이름.</summary>
        public static string DisplayName(CookingIngredientId id) =>
            CodexDescriptions.IngredientName(id.ToString()) ?? DefaultDisplayName(id);

        static string DefaultDisplayName(CookingIngredientId id) => id switch
        {
            CookingIngredientId.Water => "물",
            CookingIngredientId.Chili => "고추",
            CookingIngredientId.Rice => "쌀",
            CookingIngredientId.Grain => "잡곡",
            CookingIngredientId.Fruit => "과실",
            CookingIngredientId.Namul => "산나물",
            CookingIngredientId.Herb => "약재",
            CookingIngredientId.Honey => "꿀",
            CookingIngredientId.Boar => "멧돼지고기",
            CookingIngredientId.Bird => "새고기",
            CookingIngredientId.Seafood => "해산물",
            CookingIngredientId.Egg => "새알",
            CookingIngredientId.Oil => "기름",
            _ => id.ToString()
        };
    }
}
