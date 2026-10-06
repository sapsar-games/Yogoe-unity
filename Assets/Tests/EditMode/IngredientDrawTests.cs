using NUnit.Framework;
using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>선물꾸러미·재료보따리 공용 추첨: 요리재료 3개, 채집/사냥 50% → 7-3 확률, 물·황금 재료 제외.</summary>
    public class IngredientDrawTests
    {
        static readonly CookingIngredientId[] Gather =
            { CookingIngredientId.Rice, CookingIngredientId.Namul, CookingIngredientId.Fruit,
              CookingIngredientId.Chili, CookingIngredientId.Herb, CookingIngredientId.Grain };

        [Test]
        public void Roll_GivesThree_NeverWaterOrGolden()
        {
            Random.InitState(1234);
            for (int k = 0; k < 500; k++)
            {
                var r = IngredientDraw.Roll();
                Assert.AreEqual(3, r.Length);
                foreach (var id in r)
                {
                    Assert.AreNotEqual(CookingIngredientId.Water, id);
                    Assert.Less((int)id, (int)CookingIngredientId.Count);
                }
            }
        }

        [Test]
        public void Roll_IsRoughlyHalfGatherHalfHunt_AndRiceIsCommon()
        {
            Random.InitState(42);
            int gather = 0, rice = 0, total = 3000;
            for (int k = 0; k < total; k++)
            {
                var id = IngredientDraw.RollOne();
                if (System.Array.IndexOf(Gather, id) >= 0) gather++;
                if (id == CookingIngredientId.Rice) rice++;
            }
            Assert.That(gather / (float)total, Is.InRange(0.45f, 0.55f));
            // 채집 50% × 쌀 39/99 ≈ 19.7%
            Assert.That(rice / (float)total, Is.InRange(0.16f, 0.24f));
        }

        [Test]
        public void RollDrop_ExcludingSpecial_NeverReturnsGolden()
        {
            Assert.IsFalse(PropCatalog.IsSpecialCode(PropCatalog.RollDrop(PropResourceType.Gather, 0.9999f, includeSpecial: false)));
            Assert.IsFalse(PropCatalog.IsSpecialCode(PropCatalog.RollDrop(PropResourceType.Hunt, 1f, includeSpecial: false)));
        }

        [Test]
        public void Describe_GroupsDuplicates()
        {
            var text = IngredientDraw.Describe(new[] { CookingIngredientId.Rice, CookingIngredientId.Egg, CookingIngredientId.Rice });
            Assert.AreEqual("쌀 ×2, 새알", text);
        }

        [Test]
        public void GiftBundle_Grant_AddsMaterials()
        {
            var go = new GameObject("Eco");
            try
            {
                var eco = go.AddComponent<GameEconomy>();
                eco.BecomeInstance();
                int before = eco.GetMaterialCount(CookingIngredientId.Honey);

                GiftBundle.Grant(new[] { CookingIngredientId.Honey, CookingIngredientId.Honey, CookingIngredientId.Oil }, out string name);

                Assert.AreEqual(before + 2, eco.GetMaterialCount(CookingIngredientId.Honey));
                Assert.AreEqual("꿀 ×2, 기름", name);
            }
            finally
            {
                // Assert 실패 시에도 Instance를 비워야 뒤 테스트가 싱글턴 가드에 안 걸린다.
                Object.DestroyImmediate(go);
            }
        }
    }
}
