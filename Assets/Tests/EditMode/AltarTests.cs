using NUnit.Framework;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>v1.3 북제단·남제단: 15분에 공덕 300(분당 20) · 보관 3시간 치 · 레벨마다 보관 ×1.1 (생산은 그대로).</summary>
    public class AltarTests
    {
        [TestCase("북제단")]
        [TestCase("남제단")]
        public void Altar_ProducesMerit_CapacityGrowsPerLevel(string id)
        {
            var c = PropProduction.Config.Resolve(id, null);
            Assert.AreEqual(PropResourceType.Merit, c.Type);
            Assert.AreEqual(20.0, PropProduction.BaseMeritPerMinute(c, 1), 0.001);
            Assert.AreEqual(20.0, PropProduction.BaseMeritPerMinute(c, 5), 0.001, "레벨이 올라도 생산은 그대로");
            Assert.AreEqual(3600.0, PropProduction.MeritCapacity(c, 1), 0.01);
            Assert.AreEqual(3960.0, PropProduction.MeritCapacity(c, 2), 0.01);
        }

        [Test]
        public void Mortar_Unchanged_CapacityGrowthOne()
        {
            var c = PropProduction.Config.Resolve("떡절구", null);
            Assert.AreEqual(PropProduction.BaseMeritPerMinute(c, 2) * c.MeritCapacityMinutes, PropProduction.MeritCapacity(c, 2), 0.01);
        }
    }
}
