using System;
using Yoegoe.Data;

namespace Yoegoe.Economy
{
    /// <summary>
    /// 기물 생산 규칙의 단일 소스 (Docs/00 7장). 온라인(<see cref="Yoegoe.Characters.PropSlot"/>)과
    /// 오프라인 정산(<see cref="Yoegoe.Save.OfflineSimulator"/>)은 반드시 이 클래스만 호출한다 —
    /// 각자 구현하면 온라인/오프라인 생산량이 갈라진다.
    ///
    /// - 공덕 기물: 분당 = base × levelGrowth^(L−1) × (친밀도 보정?) × (주인 배율?), 보관 = 보정 전 분당 × N분
    /// - 자원 기물: 주기마다 1개, 보관·만창·오버플로우는 <see cref="PropStorage"/>
    /// - 그 외(화덕): 산출 없음, 기력은 평소대로 닳는다
    /// </summary>
    public static class PropProduction
    {
        /// <summary>기물 하나의 생산 설정 (시트 props 탭 한 줄).</summary>
        public struct Config
        {
            public PropResourceType Type;
            public double MeritPerMinute;
            public double LevelGrowth;
            public float MeritCapacityMinutes;
            public bool IntimacyBonus;
            public double OwnerMultiplier;
            public float CycleMinutes;
            public int BaseCapacity;

            public static Config From(PropCatalog.Entry e) => new Config
            {
                Type = e.ResourceType,
                MeritPerMinute = e.meritPerMinute,
                LevelGrowth = e.levelGrowth > 0 ? e.levelGrowth : 1.0,
                MeritCapacityMinutes = e.meritCapacityMinutes,
                IntimacyBonus = e.intimacyBonus,
                OwnerMultiplier = e.ownerMultiplier > 0 ? e.ownerMultiplier : 1.0,
                CycleMinutes = e.cycleMinutes,
                BaseCapacity = e.baseCapacity,
            };

            public static Config From(PropData d) => new Config
            {
                Type = d.resourceType,
                MeritPerMinute = d.baseProductionPerMinute,
                LevelGrowth = d.levelGrowth > 0 ? d.levelGrowth : 1.0,
                MeritCapacityMinutes = d.meritCapacityMinutes,
                IntimacyBonus = d.intimacyBonus,
                OwnerMultiplier = d.ownerMultiplier > 0 ? d.ownerMultiplier : 1.0,
                CycleMinutes = d.cycleMinutes,
                BaseCapacity = d.baseCapacity,
            };

            /// <summary>propId로 시트(props.json) 값을 쓰고, 없으면 fallback.</summary>
            public static Config Resolve(string propId, PropData fallbackData)
            {
                if (PropCatalog.TryGet(propId, out var e)) return From(e);
                return fallbackData != null ? From(fallbackData) : default;
            }
        }

        public static bool IsResource(PropResourceType t) =>
            t == PropResourceType.Water
            || t == PropResourceType.Hunt || t == PropResourceType.Gather;

        /// <summary>공덕 기물의 레벨 기준 분당 산출(보정 전). 공덕 기물이 아니면 0.</summary>
        public static double BaseMeritPerMinute(in Config c, int level) =>
            c.Type == PropResourceType.Merit ? c.MeritPerMinute * ProductionFormula.LevelMultiplier(level, c.LevelGrowth) : 0;

        /// <summary>앉은 요괴 기준 분당 공덕 (친밀도 보정·주인 배율 포함).</summary>
        public static double MeritPerMinute(in Config c, int level, float intimacy, bool ownerOnEndingProp)
        {
            double perMin = BaseMeritPerMinute(c, level);
            if (perMin <= 0) return 0;
            if (c.IntimacyBonus) perMin *= ProductionFormula.IntimacyMultiplier(intimacy);
            if (ownerOnEndingProp) perMin *= c.OwnerMultiplier;
            return perMin;
        }

        /// <summary>공덕 보관(만창) = 보정 전 분당 × MeritCapacityMinutes. 0 이하면 무제한.</summary>
        public static double MeritCapacity(in Config c, int level) =>
            c.MeritCapacityMinutes > 0f ? BaseMeritPerMinute(c, level) * c.MeritCapacityMinutes : double.PositiveInfinity;

        public static int ResourceCapacity(in Config c, int level) => PropStorage.Capacity(c.BaseCapacity, level);

        /// <summary>만창 — 앉아 있어도 생산·기력 소모 정지.</summary>
        public static bool IsHalted(in Config c, int level, double pendingMerit, in PropStorage.State storage)
        {
            if (IsResource(c.Type)) return PropStorage.IsHalted(storage, ResourceCapacity(c, level));
            if (c.Type == PropResourceType.Merit)
            {
                double cap = MeritCapacity(c, level);
                return !double.IsInfinity(cap) && pendingMerit >= cap - 0.0001;
            }
            return false;
        }

        /// <summary>
        /// 앉은 요괴가 dt초 머무는 동안의 생산. 반환 = 실제로 일한 초(이만큼만 기력이 닳는다, 만창이면 0).
        /// meritAdded = 공덕 더미에 더할 양. 활터·약초밭은 뽑힌 드롭 코드를 onDrop으로 넘긴다.
        /// </summary>
        public static float Produce(in Config c, int level, float intimacy, bool ownerOnEndingProp,
            double pendingMerit, ref PropStorage.State storage, float dt,
            Func<float> random01, Action<int> onDrop, out double meritAdded)
        {
            meritAdded = 0;
            if (dt <= 0f) return 0f;
            level = Math.Max(1, level);

            if (c.Type == PropResourceType.Merit)
            {
                double perMinute = MeritPerMinute(c, level, intimacy, ownerOnEndingProp);
                if (perMinute <= 0) return dt;
                double cap = MeritCapacity(c, level);
                double room = double.IsInfinity(cap) ? double.MaxValue : cap - pendingMerit;
                if (room <= 0.0001) return 0f;
                float worked = (float)Math.Min(dt, room / perMinute * 60.0);
                meritAdded = perMinute / 60.0 * worked;
                return worked;
            }

            if (IsResource(c.Type))
            {
                var type = c.Type;
                bool dropsIngredients = type == PropResourceType.Hunt || type == PropResourceType.Gather;
                // 오프라인 오버플로우 판정도 온라인과 동일 (Docs/05 B3 미확정 — 바뀌면 여기만)
                return PropStorage.Advance(ref storage, c.CycleMinutes * 60f, ResourceCapacity(c, level),
                    PropStorage.OverflowChance(level), dt, random01,
                    () =>
                    {
                        if (dropsIngredients)
                            onDrop?.Invoke(PropCatalog.RollDrop(type, random01()));
                    });
            }

            return dt; // 화덕 등 — 산출 없음, 기력은 평소대로
        }
    }
}
