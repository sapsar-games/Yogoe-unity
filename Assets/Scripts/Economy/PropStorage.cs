using System;

namespace Yoegoe.Economy
{
    /// <summary>
    /// 자원 기물(옹달샘·사냥터·채집터) 보관·만창·오버플로우 규칙 (Docs/00 §6-2). 온라인·오프라인 공용.
    ///
    /// - 산출 주기는 레벨 무관 고정. Capacity(L) = base + floor(L/10)
    /// - 보관이 Capacity로 찬 상태에서 주기가 한 번 더 끝나면 그때 1회 오버플로우 판정
    ///   (확률 (L%10)×10%). 성공하면 Capacity+1개. 결과와 무관하게 그 뒤로 생산·기력소모 정지.
    /// - 수거 등으로 저장량이 Capacity 밑이 되면 재개하고, 다음 만창 사이클에 새로 판정한다.
    /// </summary>
    public static class PropStorage
    {
        [Serializable]
        public struct State
        {
            public int Stored;
            public float CycleProgressSeconds;
            /// <summary>이번 만창 사이클의 오버플로우 판정을 이미 했는지 (= 정지 상태).</summary>
            public bool OverflowJudged;
        }

        public static int Capacity(int baseCapacity, int level) =>
            Math.Max(0, baseCapacity) + Math.Max(1, level) / 10;

        public static float OverflowChance(int level) => (Math.Max(1, level) % 10) * 0.1f;

        public static bool IsHalted(in State s, int capacity) => s.OverflowJudged && s.Stored >= capacity;

        /// <summary>
        /// 앉아 있는 dt만큼 생산을 진행한다. 반환값 = 실제로 "일한" 초(이만큼만 기력이 닳는다).
        /// 새로 쌓일 때마다 onProduced 호출 (재료 기물은 여기서 재료를 뽑는다).
        /// </summary>
        public static float Advance(ref State s, float cycleSeconds, int capacity, float overflowChance,
            float dt, Func<float> random01, Action onProduced)
        {
            if (cycleSeconds <= 0f || dt <= 0f) return 0f;
            // 레벨업·수거로 보관이 비면 재개
            if (s.OverflowJudged && s.Stored < capacity) s.OverflowJudged = false;
            if (IsHalted(s, capacity)) return 0f;

            float worked = 0f;
            int guard = 0;
            while (dt > 0f && guard++ < 10000)
            {
                float need = cycleSeconds - s.CycleProgressSeconds;
                if (dt < need)
                {
                    s.CycleProgressSeconds += dt;
                    worked += dt;
                    break;
                }

                dt -= need;
                worked += need;
                s.CycleProgressSeconds = 0f;

                if (s.Stored < capacity)
                {
                    s.Stored++;
                    onProduced?.Invoke();
                    continue;
                }

                // 만창에서 한 사이클 더 → 1회 판정 후 정지
                s.OverflowJudged = true;
                if (random01 != null && random01() < overflowChance)
                {
                    s.Stored++;
                    onProduced?.Invoke();
                }
                break;
            }
            return worked;
        }

        /// <summary>수거: 전부 비운다. 진행 중이던 주기는 이어간다(만창 정지 중이었으면 0부터).</summary>
        public static int TakeAll(ref State s)
        {
            int n = s.Stored;
            s.Stored = 0;
            s.OverflowJudged = false;
            return n;
        }
    }
}
