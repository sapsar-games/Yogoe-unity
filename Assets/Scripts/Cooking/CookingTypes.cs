using System;
using System.Collections.Generic;
using Yoegoe.Core;
using Yoegoe.Data;
using UnityEngine;

namespace Yoegoe.Cooking
{
    /// <summary>재료 13종 (채집6·사냥6·물). Docs/00 부록 A.</summary>
    public enum CookingIngredientId
    {
        Water = 0,   // 물
        Chili,       // 고추
        Rice,        // 쌀
        Grain,       // 잡곡 (구 팥 RedBean)
        Fruit,       // 과실
        Namul,       // 산나물
        Herb,        // 약재
        Honey,       // 꿀
        Boar,        // 멧돼지고기
        Bird,        // 새고기
        Seafood,     // 해산물 (구 물고기 Fish)
        Egg,         // 새알
        Oil,         // 기름
        Count
    }

    public enum CookingCharmType
    {
        None = 0,
        PlusFive,    // +5초
        Diagonal,    // 대각선
        Clairvoyance,// 천리안
        Recycle,     // 회수
        Double,      // 몰빵
        Cancel       // 나가리 (게임 중)
    }

    public enum CookingResultKind
    {
        Food,
        Offering
    }

    public readonly struct CookingRecipe
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly CookingResultKind Kind;
        public readonly CookingIngredientId[] Ingredients; // sorted multiset

        public CookingRecipe(string id, string name, CookingResultKind kind, params CookingIngredientId[] ingredients)
        {
            Id = id;
            DisplayName = name;
            Kind = kind;
            Ingredients = ingredients ?? Array.Empty<CookingIngredientId>();
            Array.Sort(Ingredients);
        }
    }

    /// <summary>제자리 조리 단계 — 익는 중 → 김(퍼펙트) → 식음.</summary>
    public enum CookingCookPhase
    {
        Cooking,
        Steam,
        Cool,
        Done
    }

    /// <summary>판 위에서 익는 중인 요리 하나.</summary>
    public sealed class CookingCookJob
    {
        // 익는 시간 · 김 시간 — 시트 game_settings
        public static float FoodSeconds => GameSettings.CookFoodSeconds;
        public static float OfferingSeconds => GameSettings.CookOfferingSeconds;
        public static float SteamSeconds => GameSettings.CookSteamSeconds;

        public readonly int Id;
        public readonly CookingRecipe Recipe;
        public readonly (int x, int y)[] Cells;
        public readonly bool Golden;
        public CookingCookPhase Phase { get; private set; }
        public float PhaseLeft { get; private set; }

        public CookingCookJob(int id, CookingRecipe recipe, (int x, int y)[] cells, bool golden)
        {
            Id = id;
            Recipe = recipe;
            Cells = cells ?? Array.Empty<(int, int)>();
            Golden = golden;
            Phase = CookingCookPhase.Cooking;
            PhaseLeft = recipe.Kind == CookingResultKind.Offering ? OfferingSeconds : FoodSeconds;
        }

        public void Tick(float dt)
        {
            if (Phase == CookingCookPhase.Done || Phase == CookingCookPhase.Cool) return;
            PhaseLeft -= dt;
            if (PhaseLeft > 0f) return;
            if (Phase == CookingCookPhase.Cooking)
            {
                Phase = CookingCookPhase.Steam;
                PhaseLeft = SteamSeconds;
            }
            else if (Phase == CookingCookPhase.Steam)
            {
                Phase = CookingCookPhase.Cool;
                PhaseLeft = 0f;
            }
        }

        public bool CanCollect => Phase == CookingCookPhase.Steam || Phase == CookingCookPhase.Cool;
        public bool IsPerfectWindow => Phase == CookingCookPhase.Steam;

        public void MarkDone()
        {
            Phase = CookingCookPhase.Done;
            PhaseLeft = 0f;
        }
    }
}
