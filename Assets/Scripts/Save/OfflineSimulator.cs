using System;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Save
{
    /// <summary>
    /// 저장 시각~현재까지 경과 시간을 GameSaveData 위에서 따라잡는다.
    /// 씬을 직접 건드리지 않고 DTO만 수정 → Apply 단계에서 월드에 반영.
    /// </summary>
    public static class OfflineSimulator
    {
        public static float MaxOfflineSeconds = 18f * 60f * 60f;

        private static float StaminaDrainPerSecond => Yoegoe.Data.GameSettings.StaminaDrainPerSecond; // 시트 game_settings
        private static float FaintThresholdSeconds => Yoegoe.Data.GameSettings.FaintThresholdSeconds;
        private const float PlayDurationSeconds = 1f * 60f;

        public struct Result
        {
            public float ElapsedSeconds;
            public float SimulatedSeconds;
            public bool Applied;
        }

        public static Result Simulate(GameSaveData data, DateTime utcNow)
        {
            var result = new Result { Applied = false };
            if (data == null || data.savedAtUtcTicks <= 0) return result;

            var savedAt = new DateTime(data.savedAtUtcTicks, DateTimeKind.Utc);
            double raw = (utcNow - savedAt).TotalSeconds;
            if (raw <= 0)
            {
                result.ElapsedSeconds = 0f;
                return result;
            }

            float elapsed = (float)Math.Min(raw, MaxOfflineSeconds);
            result.ElapsedSeconds = (float)raw;
            result.SimulatedSeconds = elapsed;

            if (data.agents != null)
            {
                foreach (var agent in data.agents)
                {
                    if (agent == null) continue;
                    SimulateAgent(agent, data, elapsed);
                }
            }

            data.savedAtUtcTicks = utcNow.Ticks;
            result.Applied = true;
            return result;
        }

        private static void SimulateAgent(AgentSave agent, GameSaveData data, float remaining)
        {
            int guard = 0;
            while (remaining > 0.0001f && guard++ < 64)
            {
                float used = remaining;
                switch (agent.state)
                {
                    case ActionState.Staying:
                        used = SimulateStaying(agent, data, remaining);
                        break;
                    case ActionState.Fainted:
                        return;
                    case ActionState.Playing:
                        used = SimulatePlaying(agent, remaining);
                        break;
                    case ActionState.Walking:
                    default:
                        agent.stateTimer += remaining;
                        return;
                }
                remaining -= used;
            }
        }

        private static float SimulateStaying(AgentSave agent, GameSaveData data, float dt)
        {
            var prop = FindOccupiedProp(agent, data);
            // 만창: 앉아만 있고 생산·기력소모 정지 (수거는 복귀 후 플레이어가)
            if (prop != null && IsHalted(prop))
            {
                agent.stateTimer += dt;
                return dt;
            }

            float drain = StaminaDrainPerSecond;
            float timeToZero = agent.stamina > 0f ? agent.stamina / drain : 0f;
            float slice = Math.Min(dt, timeToZero);

            if (slice <= 0f)
            {
                agent.stamina = 0f;
                EnterPlayingExhausted(agent);
                return 0.0001f;
            }

            float worked = prop != null ? Produce(agent, prop, slice) : slice;
            if (worked <= 0f && prop != null && IsHalted(prop))
                return 0.0001f;

            agent.stateTimer += worked;
            agent.stamina -= worked * drain;
            if (agent.stamina < 0f) agent.stamina = 0f;

            if (agent.stamina <= 0f)
            {
                agent.stamina = 0f;
                EnterPlayingExhausted(agent);
            }

            return worked > 0f ? worked : slice;
        }

        private static float SimulatePlaying(AgentSave agent, float dt)
        {
            if (agent.stamina <= 0f)
            {
                float timeToFaint = Math.Max(0f, FaintThresholdSeconds - agent.stateTimer);
                float slice = Math.Min(dt, timeToFaint > 0f ? timeToFaint : dt);
                agent.stateTimer += slice;
                if (agent.stateTimer >= FaintThresholdSeconds)
                {
                    agent.state = ActionState.Fainted;
                    agent.stateTimer = 0f;
                }
                return slice <= 0f ? dt : slice;
            }

            float timeToEnd = Math.Max(0f, PlayDurationSeconds - agent.stateTimer);
            float slicePlay = Math.Min(dt, timeToEnd > 0f ? timeToEnd : dt);
            agent.stateTimer += slicePlay;
            if (agent.stateTimer >= PlayDurationSeconds)
                EnterWalking(agent);
            return slicePlay <= 0f ? dt : slicePlay;
        }

        static PropSave FindOccupiedProp(AgentSave agent, GameSaveData data)
        {
            if (string.IsNullOrEmpty(agent.occupiedPropId) || data.props == null) return null;
            var prop = FindProp(data, agent.occupiedPropId);
            return prop != null && prop.isBuilt ? prop : null;
        }

        /// <summary>시트(props.json) 설정 — 시트가 정본. 시트에 없는 기물은 생산 없음.</summary>
        static PropProduction.Config ConfigFor(PropSave prop) =>
            PropCatalog.TryGet(prop.propId, out var e) ? PropProduction.Config.From(e) : default;

        static PropStorage.State ToState(PropSave prop) => new PropStorage.State
        {
            Stored = prop.storedResources,
            CycleProgressSeconds = prop.cycleProgressSeconds,
            OverflowJudged = prop.overflowJudged
        };

        static bool IsHalted(PropSave prop) =>
            PropProduction.IsHalted(ConfigFor(prop), Math.Max(1, prop.level),
                prop.pendingMerit.ToBigNumber().ToDouble(), ToState(prop));

        static bool IsOwnerOnEndingProp(AgentSave agent, PropSave prop) =>
            prop.isEndingProp && !string.IsNullOrEmpty(prop.ownerCharacterId)
            && prop.ownerCharacterId == agent.characterId;

        /// <summary>온라인과 같은 규칙(<see cref="PropProduction.Produce"/>)으로 DTO에 생산. 반환 = 일한 초.</summary>
        private static float Produce(AgentSave agent, PropSave prop, float dt)
        {
            var st = ToState(prop);
            var ingredients = new System.Collections.Generic.List<int>(prop.pendingIngredients ?? Array.Empty<int>());
            float worked = PropProduction.Produce(ConfigFor(prop), prop.level, agent.intimacy,
                IsOwnerOnEndingProp(agent, prop), prop.pendingMerit.ToBigNumber().ToDouble(), ref st, dt,
                () => UnityEngine.Random.value, ingredients.Add, out double meritAdded, prop.destinationId);

            if (meritAdded > 0)
                prop.pendingMerit = BigNumberSave.From(prop.pendingMerit.ToBigNumber() + (BigNumber)meritAdded);
            prop.storedResources = st.Stored;
            prop.cycleProgressSeconds = st.CycleProgressSeconds;
            prop.overflowJudged = st.OverflowJudged;
            prop.pendingIngredients = ingredients.ToArray();
            return worked;
        }

        private static PropSave FindProp(GameSaveData data, string propId)
        {
            foreach (var p in data.props)
            {
                if (p != null && p.propId == propId) return p;
            }
            return null;
        }

        private static void EnterPlayingExhausted(AgentSave agent)
        {
            agent.occupiedPropId = "";
            agent.state = ActionState.Playing;
            agent.stateTimer = 0f;
            agent.stamina = 0f;
        }

        private static void EnterWalking(AgentSave agent)
        {
            agent.state = ActionState.Walking;
            agent.stateTimer = 0f;
        }
    }
}
