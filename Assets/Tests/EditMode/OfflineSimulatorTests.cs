using System;
using NUnit.Framework;
using Yoegoe.Data;
using Yoegoe.Save;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>
    /// OfflineSimulator는 씬·MonoBehaviour 없이 GameSaveData DTO만으로 동작해서
    /// EditMode 테스트로 바로 검증 가능하다 (이 코드베이스에서 드문 순수 로직 클래스).
    /// </summary>
    public class OfflineSimulatorTests
    {
        static GameSaveData MakeSave(DateTime savedAtUtc)
        {
            return new GameSaveData
            {
                savedAtUtcTicks = savedAtUtc.Ticks,
                props = new[]
                {
                    new PropSave { propId = "Mortar", isBuilt = true } // 시트 props 의 공덕 기물
                },
                agents = new[]
                {
                    new AgentSave
                    {
                        characterId = "rabbit",
                        stamina = 100f,
                        intimacy = 0f,
                        state = ActionState.Staying,
                        occupiedPropId = "떡절구"
                    }
                }
            };
        }

        [Test]
        public void Simulate_NoSavedTimestamp_ReturnsNotApplied()
        {
            var data = new GameSaveData { savedAtUtcTicks = 0 };
            var result = OfflineSimulator.Simulate(data, DateTime.UtcNow);
            Assert.IsFalse(result.Applied);
        }

        [Test]
        public void Simulate_SavedTimeInFuture_DoesNothing()
        {
            var now = DateTime.UtcNow;
            var data = MakeSave(now.AddMinutes(5)); // 시계가 되감긴 것처럼 저장 시각이 미래
            var result = OfflineSimulator.Simulate(data, now);
            Assert.AreEqual(0f, result.ElapsedSeconds);
            Assert.IsFalse(result.Applied);
        }

        [Test]
        public void Simulate_CapsSimulatedSecondsAtMaxOffline()
        {
            var now = DateTime.UtcNow;
            var data = MakeSave(now.AddSeconds(-OfflineSimulator.MaxOfflineSeconds * 2));
            var result = OfflineSimulator.Simulate(data, now);
            Assert.AreEqual(OfflineSimulator.MaxOfflineSeconds, result.SimulatedSeconds, 0.01f);
        }

        [Test]
        public void Simulate_StayingAgent_DrainsStaminaAndProducesMerit()
        {
            var now = DateTime.UtcNow;
            // 10분당 1 → 600초면 기력 1 감소.
            var data = MakeSave(now.AddSeconds(-600));
            data.agents[0].stamina = 70f;

            var result = OfflineSimulator.Simulate(data, now);

            Assert.IsTrue(result.Applied);
            Assert.AreEqual(69f, data.agents[0].stamina, 0.01f);
            Assert.Greater(data.props[0].pendingMerit.ToBigNumber().ToDouble(), 0);
        }

        [Test]
        public void Simulate_StaminaReachesZero_EntersPlayingAndVacatesProp()
        {
            var now = DateTime.UtcNow;
            // 기력 1 → 600초면 0, 나머지 시간(1시간 중 남은 3000초)은 기력0 놀기
            var data = MakeSave(now.AddHours(-1));
            data.agents[0].stamina = 1f;

            OfflineSimulator.Simulate(data, now);

            var agent = data.agents[0];
            Assert.AreEqual(ActionState.Playing, agent.state);
            Assert.AreEqual(0f, agent.stamina, 0.01f);
            Assert.AreEqual("", agent.occupiedPropId);
        }

        [Test]
        public void Simulate_PlayingAtZeroPastThreshold_Faints()
        {
            var now = DateTime.UtcNow;
            var data = MakeSave(now.AddSeconds(-100));
            data.agents[0].state = ActionState.Playing;
            data.agents[0].stamina = 0f;
            data.agents[0].occupiedPropId = "";
            data.agents[0].stateTimer = 18f * 60f * 60f - 10f; // 기절까지 10초 남음

            OfflineSimulator.Simulate(data, now);

            Assert.AreEqual(ActionState.Fainted, data.agents[0].state);
        }

        [Test]
        public void Simulate_FaintedAgent_NeverRevivesOffline()
        {
            var now = DateTime.UtcNow;
            var data = MakeSave(now.AddHours(-6));
            data.agents[0].state = ActionState.Fainted;
            data.agents[0].stamina = 0f;

            OfflineSimulator.Simulate(data, now);

            Assert.AreEqual(ActionState.Fainted, data.agents[0].state);
        }

        [Test]
        public void Simulate_WalkingAgent_OnlyAdvancesTimerWithNoProduction()
        {
            var now = DateTime.UtcNow;
            var data = MakeSave(now.AddSeconds(-100));
            data.agents[0].state = ActionState.Walking;
            data.agents[0].stamina = 50f;

            OfflineSimulator.Simulate(data, now);

            var agent = data.agents[0];
            Assert.AreEqual(50f, agent.stamina); // 걷기는 기력 소모·생산 없음
            Assert.AreEqual(100f, agent.stateTimer, 0.01f);
            Assert.AreEqual(0.0, data.props[0].pendingMerit.ToBigNumber().ToDouble());
        }
    }
}
