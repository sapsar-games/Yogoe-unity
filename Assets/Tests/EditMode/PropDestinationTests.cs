using NUnit.Framework;
using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Save;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>v1.3 사냥터·채집터 목적지: 시트 destinations · prop_drop_tables (table = 목적지 id).</summary>
    public class PropDestinationTests
    {
        [Test]
        public void ThreeDestinationsEach_WithEntryIntimacy()
        {
            var hunt = PropCatalog.DestinationsFor(PropResourceType.Hunt);
            var gather = PropCatalog.DestinationsFor(PropResourceType.Gather);
            Assert.AreEqual(3, hunt.Count);
            Assert.AreEqual(3, gather.Count);
            Assert.AreEqual(0f, hunt[0].minIntimacy);
            Assert.AreEqual(50f, PropCatalog.FindDestination("WaveIsland").minIntimacy);
            Assert.IsNull(PropCatalog.FindDestination("nowhere"));
        }

        [Test]
        public void RollDrop_UsesDestinationTable_ElseLegacy()
        {
            // 멧돼지 앞산 = 멧돼지고기 80 · 새고기 15 · 새알 5 → 맨 앞 확률 구간
            Assert.AreEqual((int)CookingIngredientId.Boar, PropCatalog.RollDrop(PropResourceType.Hunt, "BoarHill", 0f));
            Assert.AreEqual((int)CookingIngredientId.Egg, PropCatalog.RollDrop(PropResourceType.Hunt, "BoarHill", 0.999f));
            Assert.Greater(PropCatalog.DestinationDrops("BeeForest").Count, 0);
            // 목적지가 없으면 예전 Hunt 표
            Assert.AreEqual(PropCatalog.RollDrop(PropResourceType.Hunt, 0.3f), PropCatalog.RollDrop(PropResourceType.Hunt, null, 0.3f));
        }

        [Test]
        public void SaveKeepsDestination()
        {
            var json = JsonUtility.ToJson(new PropSave { propId = "활터", destinationId = "BeeForest" });
            Assert.AreEqual("BeeForest", JsonUtility.FromJson<PropSave>(json).destinationId);
        }
    }
}
