using NUnit.Framework;
using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>부적 6종 모두 윷 완주로 얻는 소모품 — 나가리도 가진 개수만큼만. 완주 확률은 시트 charms 가중치.</summary>
    public class CharmRulesTests
    {
        GameObject ecoGO;
        GameEconomy eco;

        [SetUp]
        public void SetUp()
        {
            ecoGO = new GameObject("Eco");
            eco = ecoGO.AddComponent<GameEconomy>();
            eco.BecomeInstance();
            eco.ApplyStartingState(ScriptableObject.CreateInstance<StartingStateSettings>());
        }

        [TearDown]
        public void TearDown()
        {
            CharmDropRates.LoadFromJson(Resources.Load<TextAsset>(CharmDropRates.ResourcePath)?.text);
            Object.DestroyImmediate(ecoGO);
        }

        CookingSession StartedBoard()
        {
            eco.AddMaterial(CookingIngredientId.Fruit, 12);
            var s = new CookingSession();
            s.Prepare(CookingCharmType.None);
            Assert.IsTrue(s.StartRound());
            return s;
        }

        [Test]
        public void Nagari_HiddenWithoutCharm_AndDoesNothing()
        {
            var s = StartedBoard();
            Assert.AreEqual(0, eco.GetCharmCount(CookingCharmType.Cancel));
            Assert.IsFalse(s.ShowNagari);
            s.CancelNagari();
            Assert.IsFalse(s.Finished, "나가리가 없으면 판이 그대로");
        }

        [Test]
        public void Nagari_ConsumesOneCharm()
        {
            var s = StartedBoard();
            eco.AddCharm(CookingCharmType.Cancel, 2);
            Assert.IsTrue(s.ShowNagari);
            s.CancelNagari();
            Assert.IsTrue(s.Finished);
            Assert.AreEqual(1, eco.GetCharmCount(CookingCharmType.Cancel));
        }

        [Test]
        public void Nagari_LockedAfterGuestDelivery()
        {
            var s = StartedBoard();
            eco.AddCharm(CookingCharmType.Cancel, 1);
            Assert.IsTrue(s.CanUseNagari);

            var guest = new CookingGuestOrder(null, "Gorani", "고라니", "baekseolgi", "백설기");
            s.SetGuestOrderForTest(guest);
            guest.Deliver(perfect: true);

            Assert.IsTrue(s.CharmsLocked);
            Assert.IsTrue(s.ShowNagari, "버튼은 보이되(X 표시)");
            Assert.IsFalse(s.CanUseNagari, "쓸 수는 없음");
            s.CancelNagari();
            Assert.IsFalse(s.Finished);
            Assert.AreEqual(1, eco.GetCharmCount(CookingCharmType.Cancel), "소모 안 됨");
        }

        [Test]
        public void FaintedGuest_WakesWithStaminaOnly()
        {
            OfferingCatalog.Build(null);
            var go = new GameObject("Gorani");
            try
            {
                var agent = go.AddComponent<Yoegoe.Characters.CharacterAgent>();
                agent.Data = ScriptableObject.CreateInstance<CharacterData>();
                agent.Data.id = CharacterId.Gorani;
                agent.Stats.Intimacy = 10f;
                agent.Stats.Stamina = 0f;
                agent.Stats.State = ActionState.Fainted;

                var guest = new CookingGuestOrder(agent, "Gorani", "고라니", "baekseolgi", "백설기");
                guest.Deliver(perfect: true);

                Assert.IsTrue(guest.RevivedFromFaint);
                Assert.AreNotEqual(ActionState.Fainted, agent.Stats.State, "깨어남");
                Assert.AreEqual(guest.StaminaGain, agent.Stats.Stamina, 0.001f, "기력 1 단계 없이 받은 만큼");
                Assert.Greater(guest.StaminaGain, 1);
                Assert.AreEqual(10f, agent.Stats.Intimacy, 0.001f, "친밀도 없음");
                Assert.AreEqual(0f, guest.IntimacyGain);
                Object.DestroyImmediate(agent.Data);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void DropRates_FollowSheetWeights()
        {
            CharmDropRates.LoadFromJson("{\"charms\":[{\"id\":\"Cancel\",\"weight\":1},{\"id\":\"Double\",\"weight\":0}]}");
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(CookingCharmType.Cancel, CharmDropRates.Roll(i / 20f));

            CharmDropRates.LoadFromJson("{\"charms\":[{\"id\":\"PlusFive\",\"weight\":3},{\"id\":\"Cancel\",\"weight\":1}]}");
            Assert.AreEqual(CookingCharmType.PlusFive, CharmDropRates.Roll(0.74f));
            Assert.AreEqual(CookingCharmType.Cancel, CharmDropRates.Roll(0.76f));
        }

        [Test]
        public void DropRates_EmptyFile_IsEvenOverSix()
        {
            CharmDropRates.LoadFromJson(null);
            Assert.AreEqual(6, CharmDropRates.Table.Count);
        }

        [Test]
        public void ResourceFile_Loads()
        {
            // Resources/charms.json = 시트 charms 탭 (weight 0 인 부적은 표에서 빠짐)
            Assert.IsNotNull(Resources.Load<TextAsset>(CharmDropRates.ResourcePath));
            CharmDropRates.LoadFromJson(Resources.Load<TextAsset>(CharmDropRates.ResourcePath).text);
            Assert.Greater(CharmDropRates.Table.Count, 0);
        }
    }
}
