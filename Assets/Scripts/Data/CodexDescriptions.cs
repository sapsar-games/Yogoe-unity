using System;
using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Cooking;

namespace Yoegoe.Data
{
    /// <summary>
    /// 요리책 칸 설명. 요리 = 시트 recipes 탭 description(recipes.json), 재료 = 시트 ingredients 탭(ingredients.json).
    /// id = 레시피 결과물 id(bap, yukjeon…) 또는 재료 id(Water·Rice… GoldenRice·GoldenHoney).
    /// 재료 이름도 ingredients 탭 name 에서 온다 (<see cref="IngredientName"/>).
    /// </summary>
    public static class CodexDescriptions
    {
        [Serializable] class Ingredient { public string id; public string name; public string description; }
        [Serializable] class IngredientFile { public Ingredient[] ingredients; }

        public const string IngredientsResourcePath = "ingredients";
        static Dictionary<string, string> ingredients;
        static Dictionary<string, string> ingredientNames;

        public static string Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string recipe = CookingRecipeCatalog.Description(id);
            if (!string.IsNullOrEmpty(recipe)) return recipe;
            EnsureIngredients();
            return ingredients.TryGetValue(id, out var d) ? d : "";
        }

        /// <summary>시트에서 정한 재료 이름. 없으면 null (호출 쪽 기본 이름 사용).</summary>
        public static string IngredientName(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureIngredients();
            return ingredientNames.TryGetValue(id, out var n) ? n : null;
        }

        /// <summary>재료 설명 다시 읽기 (테스트·핫리로드).</summary>
        public static void LoadIngredientsFromJson(string json)
        {
            ingredients = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ingredientNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json)) return;
            var file = JsonUtility.FromJson<IngredientFile>(json);
            if (file?.ingredients == null) return;
            foreach (var e in file.ingredients)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                ingredients[e.id] = e.description ?? "";
                if (!string.IsNullOrEmpty(e.name)) ingredientNames[e.id] = e.name;
            }
        }

        static void EnsureIngredients()
        {
            if (ingredients != null) return;
            LoadIngredientsFromJson(Resources.Load<TextAsset>(IngredientsResourcePath)?.text);
        }
    }
}
