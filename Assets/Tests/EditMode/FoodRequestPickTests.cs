using NUnit.Framework;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>음식 요구 대상 (Docs/00 10-2): ① 보유 음식 → ② 보유 재료로 만들 수 있는 음식 → ③ 랜덤 음식. 공양물은 요구 안 함.</summary>
    public class FoodRequestPickTests
    {
        GameObject ecoGO;
        GameEconomy eco;

        [SetUp]
        public void SetUp()
        {
            OfferingCatalog.Build(null);
            ecoGO = new GameObject("Eco");
            eco = ecoGO.AddComponent<GameEconomy>();
            eco.BecomeInstance();
            var settings = ScriptableObject.CreateInstance<StartingStateSettings>();
            eco.ApplyStartingState(settings);
            // 시작 상태가 재료를 5개씩 깔아 주므로 비우고 시작
            for (int i = 0; i < (int)CookingIngredientId.Count; i++)
            {
                var id = (CookingIngredientId)i;
                eco.TrySpendMaterial(id, eco.GetMaterialCount(id));
            }
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(ecoGO);

        [Test]
        public void OwnedFood_ComesFirst()
        {
            eco.AddMaterial(CookingIngredientId.Rice, 5);
            eco.AddMaterial(CookingIngredientId.Grain, 5);
            eco.AddOffering(OfferingCatalog.Find("kimchi"), 1);
            for (int k = 0; k < 20; k++)
                Assert.AreEqual("kimchi", CharacterRequestState.PickFoodRequest(eco).offeringId);
        }

        [Test]
        public void OwnedOfferingIsIgnored_CraftableFoodIsNext()
        {
            eco.AddOffering(OfferingCatalog.Find("yukjeon"), 3); // 공양물 — 요구 대상 아님
            eco.AddMaterial(CookingIngredientId.Rice, 1);
            eco.AddMaterial(CookingIngredientId.Grain, 1); // 쌀+팥 = 팥떡만
            for (int k = 0; k < 20; k++)
                Assert.AreEqual("patteok", CharacterRequestState.PickFoodRequest(eco).offeringId);
        }

        [Test]
        public void NothingOwned_PicksRandomFood()
        {
            Random.InitState(7);
            for (int k = 0; k < 50; k++)
            {
                var o = CharacterRequestState.PickFoodRequest(eco);
                Assert.IsNotNull(o);
                Assert.AreEqual(OfferingKind.Food, o.kind);
            }
        }

        [Test]
        public void CanMakeWith_NeedsTwoOfSameIngredient()
        {
            CookingRecipe gotgam = default;
            foreach (var r in CookingRecipeCatalog.Recipes)
                if (r.Id == "gotgam") gotgam = r;

            int fruit = 1;
            Assert.IsFalse(CookingRecipeCatalog.CanMakeWith(gotgam, id => id == CookingIngredientId.Fruit ? fruit : 0));
            fruit = 2;
            Assert.IsTrue(CookingRecipeCatalog.CanMakeWith(gotgam, id => id == CookingIngredientId.Fruit ? fruit : 0));
        }
    }
}
