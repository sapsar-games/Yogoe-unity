using NUnit.Framework;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>v1.3 그릇 말풍선: 기력 ≤ 최대 − 10 이면 뜨고, 그보다 높아지면 사라진다. 특정 음식 요구 없음.</summary>
    public class HungerBowlTests
    {
        GameObject ecoGO, agentGO;
        GameEconomy eco;
        CharacterAgent agent;
        OfferingData food;

        [SetUp]
        public void SetUp()
        {
            ecoGO = new GameObject("Eco");
            eco = ecoGO.AddComponent<GameEconomy>();
            eco.BecomeInstance();
            eco.ApplyStartingState(ScriptableObject.CreateInstance<StartingStateSettings>());

            agentGO = new GameObject("Agent");
            agent = agentGO.AddComponent<CharacterAgent>();
            agent.Data = ScriptableObject.CreateInstance<CharacterData>();
            agent.Data.id = CharacterId.SamjokO;
            agent.Stats.State = ActionState.Walking;
            agent.Stats.Intimacy = 50f; // 최대 기력 75

            food = ScriptableObject.CreateInstance<OfferingData>();
            food.offeringId = "test_food";
            food.kind = OfferingKind.Food;
        }

        [TearDown]
        public void TearDown()
        {
            agent.Requests.DestroyVisuals();
            Object.DestroyImmediate(food);
            Object.DestroyImmediate(agent.Data);
            Object.DestroyImmediate(agentGO);
            Object.DestroyImmediate(ecoGO);
        }

        [Test]
        public void ShowsBelowMaxMinus10_HidesAbove()
        {
            agent.Stats.Stamina = 66f;
            agent.Requests.Tick(0f);
            Assert.IsFalse(agent.HasOfferingRequest);

            agent.Stats.Stamina = 65f;
            agent.Requests.Tick(0f);
            Assert.IsTrue(agent.HasOfferingRequest);
            Assert.IsNull(agent.Requests.OfferingRequest, "특정 음식을 요구하지 않는다");

            agent.Stats.Stamina = 70f; // 물 등으로 기력이 오르면 바로 사라짐
            agent.Requests.Tick(0f);
            Assert.IsFalse(agent.HasOfferingRequest);
        }

        [Test]
        public void FeedThatLiftsAboveBand_Fulfills()
        {
            agent.Stats.Stamina = 50f;
            agent.Requests.Tick(0f);
            Assert.IsTrue(agent.HasOfferingRequest);

            eco.AddOffering(food, 2);
            var r1 = agent.TryFeed(food, eco); // 50 → 60 (아직 ≤ 65)
            Assert.IsTrue(r1.Success);
            Assert.IsFalse(r1.RequestFulfilled);
            Assert.IsTrue(agent.HasOfferingRequest);

            var r2 = agent.TryFeed(food, eco); // 60 → 70 (> 65) — 그릇이 사라짐
            Assert.IsTrue(r2.RequestFulfilled);
            Assert.IsFalse(agent.HasOfferingRequest);
            Assert.AreEqual(60f + GameSettings.FoodStamina + GameSettings.RequestFulfillBonus, agent.Stats.Stamina, 0.01f);
        }

        [Test]
        public void Fainted_NoBowl()
        {
            agent.Stats.Stamina = 0f;
            agent.Stats.State = ActionState.Fainted;
            agent.Requests.Tick(0f);
            Assert.IsFalse(agent.HasOfferingRequest);
        }
    }
}
