using NUnit.Framework;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>공양 규칙 (Docs/00 5장) — CharacterAgent.TryFeed / TryFeedWater.</summary>
    public class FeedingRulesTests
    {
        GameObject ecoGO, agentGO;
        GameEconomy eco;
        CharacterAgent agent;
        OfferingData food, offering, preferred, water;

        static OfferingData Make(string id, OfferingKind kind)
        {
            var o = ScriptableObject.CreateInstance<OfferingData>();
            o.offeringId = id;
            o.kind = kind;
            return o;
        }

        [SetUp]
        public void SetUp()
        {
            ecoGO = new GameObject("Eco");
            eco = ecoGO.AddComponent<GameEconomy>();
            eco.BecomeInstance();
            var settings = ScriptableObject.CreateInstance<StartingStateSettings>();
            settings.startingWater = 0;
            eco.ApplyStartingState(settings);

            agentGO = new GameObject("Agent");
            agent = agentGO.AddComponent<CharacterAgent>();
            agent.Data = ScriptableObject.CreateInstance<CharacterData>();
            agent.Data.id = CharacterId.SamjokO; // 선호: 약주·화채 빙수·육전 (시트, v1.2 시연값)
            agent.Stats.State = ActionState.Walking;
            agent.Stats.Intimacy = 50f;
            agent.Stats.Stamina = 40f; // 최대 75

            food = Make("bap", OfferingKind.Food);
            offering = Make("samgyetang", OfferingKind.General);
            preferred = Make("yakju", OfferingKind.General);
            water = Make("water", OfferingKind.Water);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in new Object[] { food, offering, preferred, water, agent.Data }) Object.DestroyImmediate(o);
            Object.DestroyImmediate(agentGO);
            Object.DestroyImmediate(ecoGO);
        }

        [Test]
        public void Food_GivesStaminaOnly_AndSpendsOne()
        {
            eco.AddOffering(food, 2);
            var r = agent.TryFeed(food, eco);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(10, r.StaminaGain); // v1.3 음식 +10 (game_settings)
            Assert.AreEqual(0f, r.IntimacyGain);
            Assert.AreEqual(50f, agent.Stats.Stamina, 0.001f);
            Assert.AreEqual(1, eco.GetOfferingCount(food));
        }

        [Test]
        public void NoItem_IsBlocked()
        {
            Assert.AreEqual(FeedBlock.NoItem, agent.TryFeed(offering, eco).Block);
            Assert.AreEqual(FeedBlock.NoItem, agent.TryFeedWater(water, eco).Block);
        }

        [Test]
        public void Fainted_OnlyWaterWakes_ZeroToOne()
        {
            agent.Stats.State = ActionState.Fainted;
            agent.Stats.Stamina = 0f;
            eco.AddOffering(offering, 1);
            eco.AddWater(1);

            Assert.AreEqual(FeedBlock.FaintedNeedsWater, agent.TryFeed(offering, eco).Block);
            Assert.AreEqual(1, eco.GetOfferingCount(offering)); // 소모 안 됨

            var r = agent.TryFeed(water, eco); // 물이면 물 규칙으로
            Assert.IsTrue(r.Success);
            Assert.AreEqual(1, r.StaminaGain);
            Assert.AreEqual(1f, agent.Stats.Stamina, 0.001f);
            Assert.AreNotEqual(ActionState.Fainted, agent.Stats.State);
        }

        [Test]
        public void FullStamina_FoodAndWaterBlocked_OfferingRaisesIntimacyOnly()
        {
            agent.Stats.Stamina = 75f;
            eco.AddOffering(food, 1);
            eco.AddOffering(offering, 1);
            eco.AddWater(1);

            Assert.AreEqual(FeedBlock.StaminaFull, agent.TryFeed(food, eco).Block);
            Assert.AreEqual(FeedBlock.StaminaFull, agent.TryFeedWater(water, eco).Block);

            var r = agent.TryFeed(offering, eco);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(51f, agent.Stats.Intimacy, 0.001f); // v1.3 공양물 친밀도 +1
            Assert.AreEqual(75f, agent.Stats.Stamina, 0.001f);
        }

        [Test]
        public void FullStaminaAndIntimacy_Blocked()
        {
            agent.Stats.Intimacy = 100f;
            agent.Stats.Stamina = 125f;
            eco.AddOffering(offering, 1);
            Assert.AreEqual(FeedBlock.StaminaAndIntimacyFull, agent.TryFeed(offering, eco).Block);
            Assert.AreEqual(1, eco.GetOfferingCount(offering));
        }

        [Test]
        public void Preferred_GivesFive_RevealsOnlyFirstTime()
        {
            eco.AddOffering(preferred, 2);
            var first = agent.TryFeed(preferred, eco);
            Assert.AreEqual(5f, first.IntimacyGain);
            Assert.IsTrue(first.PreferenceRevealed);
            Assert.IsTrue(agent.Stats.IsPreferenceRevealed("yakju"));

            var second = agent.TryFeed(preferred, eco);
            Assert.IsTrue(second.Success);
            Assert.IsFalse(second.PreferenceRevealed);
        }

        [Test]
        public void Water_GivesThree_WhenAwake()
        {
            eco.AddWater(2);
            var r = agent.TryFeedWater(water, eco);
            Assert.AreEqual(3, r.StaminaGain);
            Assert.AreEqual(1, eco.Water);
        }
    }
}
