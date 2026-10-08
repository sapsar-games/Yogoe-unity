using System;
using Yoegoe.Core;

namespace Yoegoe.Economy
{
    /// <summary>
    /// 출석보상 윷점 (기획 18장) 진행 상태·규칙.
    /// - '하루' = **KST 새벽 4시** 기준(기기 타임존 무관, 시각은 TrustedTime — 나중에 서버 시각). 그날 처음 들어오면 팝업.
    /// - 오늘 칸 수령 → 다음 날은 다음 칸. 7일차 다음은 1일차. 하루 걸러도 초기화 안 됨.
    /// - 수령하지 않고 닫아도 그날은 다시 안 뜬다(칸도 그대로).
    /// </summary>
    public static class Attendance
    {
        public const int ResetHour = 4;

        /// <summary>다음에 받을 칸 (0 = 1일차).</summary>
        public static int NextDayIndex { get; private set; }
        /// <summary>마지막으로 팝업을 처리(수령·닫기)한 날 키 (yyyyMMdd). 0 = 없음.</summary>
        public static int LastHandledDayKey { get; private set; }

        public static void ResetFromSave(int nextDayIndex, int lastHandledDayKey)
        {
            NextDayIndex = Math.Max(0, nextDayIndex);
            LastHandledDayKey = Math.Max(0, lastHandledDayKey);
        }

        public static void CaptureToSave(out int nextDayIndex, out int lastHandledDayKey)
        {
            nextDayIndex = NextDayIndex;
            lastHandledDayKey = LastHandledDayKey;
        }

        /// <summary>KST 벽시계 기준 날짜 키. 새벽 4시 이전은 전날로 친다.</summary>
        public static int DayKey(DateTime kst)
        {
            var d = kst.AddHours(-ResetHour).Date;
            return d.Year * 10000 + d.Month * 100 + d.Day;
        }

        /// <summary>UTC 시각 → KST 날짜 키.</summary>
        public static int DayKeyFromUtc(DateTime utc) => DayKey(utc + TrustedTime.KstOffset);

        public static int TodayKey => DayKey(TrustedTime.KstNow);

        public static bool ShouldOpen(int todayKey) => LastHandledDayKey != todayKey;

        /// <summary>오늘 칸 수령. 반환 = 받은 엽전(이미 처리한 날이면 0).</summary>
        public static int Claim(int todayKey, int[] rewards)
        {
            if (!ShouldOpen(todayKey) || rewards == null || rewards.Length == 0) return 0;
            int idx = NextDayIndex % rewards.Length;
            int amount = rewards[idx];
            NextDayIndex = (idx + 1) % rewards.Length;
            LastHandledDayKey = todayKey;
            GameEconomy.Instance?.AddYeopjeon(amount);
            return amount;
        }

        /// <summary>받지 않고 닫기 — 그날은 다시 안 뜨고 칸은 그대로.</summary>
        public static void Dismiss(int todayKey) => LastHandledDayKey = todayKey;

        /// <summary>
        /// 윷 3번 → 괘 번호(0~63). 윷점에서 윷은 모와 같은 것으로 쳐서 도·개·걸·윷 4가지, 각 25%.
        /// randRange(max) = 0 이상 max 미만 정수.
        /// </summary>
        public static int RollGua(Func<int, int> randRange, out int first, out int second, out int third)
        {
            first = randRange(4);
            second = randRange(4);
            third = randRange(4);
            return first * 16 + second * 4 + third;
        }

        public static string ThrowName(int v) => v switch { 0 => "도", 1 => "개", 2 => "걸", _ => "윷" };
    }
}
