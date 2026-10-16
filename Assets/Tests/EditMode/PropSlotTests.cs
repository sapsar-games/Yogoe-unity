using NUnit.Framework;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>
    /// PropSlot의 점유/예약/공덕 더미는 온라인 틱과 GameSaveBridge 세이브 복원 양쪽에서 쓰이는
    /// 핵심 상태다. EditMode에서는 AddComponent 시 Awake/OnEnable이 보장되지 않아서
    /// (GameEconomyTests 참고) 전부 공개 API만으로 검증한다 — PropSlot의 점유/예약/더미
    /// 로직은 Awake 실행 여부와 무관하게 안전하도록 이미 짜여 있다(시각 갱신만 Awake 의존).
    /// </summary>
    public class PropSlotTests
    {
        GameObject economyGO;
        GameEconomy economy;
        StartingStateSettings settings;

        GameObject propGO;
        PropSlot prop;
        PropData data;

        GameObject agentGO;
        CharacterAgent agent;
        CharacterData agentData;

        [SetUp]
        public void SetUp()
        {
            economyGO = new GameObject("GameEconomy_Test");
            economy = economyGO.AddComponent<GameEconomy>();
            economy.BecomeInstance();

            settings = ScriptableObject.CreateInstance<StartingStateSettings>();
            settings.startingMerit = 0;
            settings.yutTokenMax = 5;
            economy.ApplyStartingState(settings);

            data = ScriptableObject.CreateInstance<PropData>();
            data.propId = "well";
            data.baseProductionPerMinute = 60;

            propGO = new GameObject("Prop_Test");
            prop = propGO.AddComponent<PropSlot>();
            prop.data = data;

            agentData = ScriptableObject.CreateInstance<CharacterData>();
            agentData.id = CharacterId.Rabbit;

            agentGO = new GameObject("Agent_Test");
            agent = agentGO.AddComponent<CharacterAgent>();
            agent.Data = agentData;
        }

        [TearDown]
        public void TearDown()
        {
            if (agentGO != null) Object.DestroyImmediate(agentGO);
            if (agentData != null) Object.DestroyImmediate(agentData);
            if (propGO != null) Object.DestroyImmediate(propGO);
            if (data != null) Object.DestroyImmediate(data);
            if (settings != null) Object.DestroyImmediate(settings);
            // GameEconomy.Instance는 static이라 다음 테스트 전에 즉시 파괴해야 싱글턴 가드에 안 걸린다.
            if (economyGO != null) Object.DestroyImmediate(economyGO);
        }

        static GameObject MakeOtherAgent(out CharacterAgent other)
        {
            var go = new GameObject("Agent_Other");
            other = go.AddComponent<CharacterAgent>();
            return go;
        }

        [Test]
        public void ConfigureBuiltState_Locked_IsNotBuilt()
        {
            prop.ConfigureBuiltState(false);
            Assert.IsFalse(prop.IsBuilt);
        }

        [Test]
        public void Build_MarksBuiltAndRaisesEventOnlyOnce()
        {
            prop.ConfigureBuiltState(false);
            int fired = 0;
            prop.OnBuilt += _ => fired++;

            prop.Build();
            prop.Build(); // 이미 건립됐으면 두 번째 호출은 무시 (idempotent)

            Assert.IsTrue(prop.IsBuilt);
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void TryReserve_FailsWhenNotBuilt()
        {
            prop.ConfigureBuiltState(false);
            Assert.IsFalse(prop.TryReserve(agent));
        }

        [Test]
        public void TryReserve_SucceedsOnBuiltEmptySlot_AndIsIdempotentForSameAgent()
        {
            prop.ConfigureBuiltState(true);
            Assert.IsTrue(prop.TryReserve(agent));
            Assert.IsTrue(prop.IsReserved);
            Assert.IsTrue(prop.TryReserve(agent)); // 같은 요괴가 다시 예약해도 성공해야 함
        }

        [Test]
        public void TryReserve_FailsForAnotherAgentWhileReserved()
        {
            prop.ConfigureBuiltState(true);
            var otherGO = MakeOtherAgent(out var other);
            try
            {
                prop.TryReserve(agent);
                Assert.IsFalse(prop.TryReserve(other));
            }
            finally
            {
                Object.DestroyImmediate(otherGO);
            }
        }

        [Test]
        public void TryOccupy_PlayerDropOverridesAnotherAgentsReservation()
        {
            // 6-2: 플레이어 드롭 등이 다른 요괴의 걷기 예약보다 우선한다.
            prop.ConfigureBuiltState(true);
            var otherGO = MakeOtherAgent(out var other);
            try
            {
                prop.TryReserve(other);

                bool occupied = prop.TryOccupy(agent);

                Assert.IsTrue(occupied);
                Assert.AreSame(agent, prop.Occupant);
                Assert.IsFalse(prop.IsReserved);
            }
            finally
            {
                Object.DestroyImmediate(otherGO);
            }
        }

        [Test]
        public void TryOccupy_FailsWhenAlreadyOccupiedByAnotherAgent()
        {
            prop.ConfigureBuiltState(true);
            var otherGO = MakeOtherAgent(out var other);
            try
            {
                Assert.IsTrue(prop.TryOccupy(other));
                Assert.IsFalse(prop.TryOccupy(agent));
                Assert.AreSame(other, prop.Occupant);
            }
            finally
            {
                Object.DestroyImmediate(otherGO);
            }
        }

        [Test]
        public void Vacate_OnlyClearsWhenCallerIsCurrentOccupant()
        {
            prop.ConfigureBuiltState(true);
            prop.TryOccupy(agent);
            var otherGO = MakeOtherAgent(out var other);
            try
            {
                prop.Vacate(other); // 점유자가 아닌 요괴가 비우려 하면 무시
                Assert.IsTrue(prop.IsOccupied);

                prop.Vacate(agent);
                Assert.IsFalse(prop.IsOccupied);
            }
            finally
            {
                Object.DestroyImmediate(otherGO);
            }
        }

        [Test]
        public void CanBeUsedBy_NormalProp_AlwaysUsable()
        {
            prop.ConfigureBuiltState(true);
            Assert.IsTrue(prop.CanBeUsedBy(agent));
            Assert.IsFalse(prop.IsForbiddenEndingFor(agent));
        }

        [Test]
        public void GongyangganProp_DoesNotAcceptWorkers()
        {
            data.opensGongyanggan = true;
            prop.ConfigureBuiltState(true);
            Assert.IsFalse(prop.AcceptsWorkers);
            Assert.IsFalse(prop.CanSitNow(agent));
            // 소유 조건과는 별개 — CanBeUsedBy는 true여도 착석 불가
            Assert.IsTrue(prop.CanBeUsedBy(agent));
        }

        [Test]
        public void NoProductionProp_DoesNotAcceptWorkers_EvenWithoutGongyangganFlag()
        {
            data.opensGongyanggan = false;
            data.resourceType = PropResourceType.None;
            data.propId = "StoneLion";
            prop.ConfigureBuiltState(true);
            Assert.IsFalse(prop.AcceptsWorkers);
            Assert.IsFalse(prop.CanSitNow(agent));
        }

        [Test]
        public void CanBeUsedBy_EndingProp_OnlyOwnerAllowed()
        {
            data.isEndingProp = true;
            data.owner = CharacterId.Rabbit;
            prop.ConfigureBuiltState(true);

            agentData.id = CharacterId.Gumiho; // 주인이 아님
            Assert.IsFalse(prop.CanBeUsedBy(agent));
            Assert.IsTrue(prop.IsForbiddenEndingFor(agent));

            agentData.id = CharacterId.Rabbit; // 주인
            Assert.IsTrue(prop.CanBeUsedBy(agent));
            Assert.IsFalse(prop.IsForbiddenEndingFor(agent));
        }

        [Test]
        public void GetBaseProductionThisLevel_ZeroWhenNotBuilt()
        {
            prop.ConfigureBuiltState(false);
            Assert.AreEqual(0, prop.GetBaseProductionThisLevel());
        }

        [Test]
        public void GetBaseProductionThisLevel_AppliesLevelGrowth()
        {
            prop.ConfigureBuiltState(true);

            prop.level = 1;
            Assert.AreEqual(60, prop.GetBaseProductionThisLevel(), 0.0001);

            prop.level = 2;
            Assert.AreEqual(60 * 1.1, prop.GetBaseProductionThisLevel(), 0.0001);
        }

        [Test]
        public void AddToMeritPile_AccumulatesOnlyWhenBuilt()
        {
            prop.ConfigureBuiltState(false);
            prop.AddToMeritPile(50);
            Assert.IsFalse(prop.HasPendingMerit);

            prop.ConfigureBuiltState(true);
            prop.AddToMeritPile(50);
            Assert.IsTrue(prop.HasPendingMerit);
        }

        [Test]
        public void TryCollectMerit_MovesPileIntoWallet_AndClearsPile()
        {
            prop.ConfigureBuiltState(true);
            prop.AddToMeritPile(120);

            bool collected = prop.TryCollectMerit();

            Assert.IsTrue(collected);
            Assert.IsFalse(prop.HasPendingMerit);
            Assert.AreEqual(120, economy.MeritPile.ToDouble(), 0.01);
        }

        [Test]
        public void TryCollectMerit_FailsWhenNothingPending()
        {
            prop.ConfigureBuiltState(true);
            Assert.IsFalse(prop.TryCollectMerit());
        }

        [Test]
        public void TakePendingMerit_ClearsPileWithoutTouchingWallet()
        {
            // 일괄 수거(콜드스타트) 경로: 더미만 비우고 HUD 지갑엔 안 들어간다.
            prop.ConfigureBuiltState(true);
            prop.AddToMeritPile(80);

            var taken = prop.TakePendingMerit();

            Assert.AreEqual(80, taken.ToDouble(), 0.01);
            Assert.IsFalse(prop.HasPendingMerit);
            Assert.AreEqual(0, economy.MeritPile.ToDouble(), 0.01);
        }

        [Test]
        public void GetPileStage_ZeroWhenEmpty()
        {
            prop.ConfigureBuiltState(true);
            Assert.AreEqual(0, prop.GetPileStage());
        }

        [Test]
        public void GetPileStage_CrossesMinuteBoundaries()
        {
            // base 60/분 → 경계: 1분=60, 3분=180, 10분=600, 20분=1200, 30분=1800
            prop.ConfigureBuiltState(true);

            prop.AddToMeritPile(60);
            Assert.AreEqual(1, prop.GetPileStage());

            prop.AddToMeritPile(140); // 누적 200 → 3분 경계(180) 통과, 10분 경계(600) 미만
            Assert.AreEqual(2, prop.GetPileStage());

            prop.AddToMeritPile(1800); // 누적 2000 → 30분 경계(1800) 이상, 최고 단계
            Assert.AreEqual(5, prop.GetPileStage());
        }
    }
}
