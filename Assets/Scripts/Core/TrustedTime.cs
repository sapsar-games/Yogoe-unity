using System;

namespace Yoegoe.Core
{
    /// <summary>
    /// 게임 시각의 단일 출처. 게임 코드는 DateTime.Now/UtcNow 대신 이것만 쓴다.
    ///
    /// - 지금: 기기 시계(UTC) 그대로.
    /// - 나중(Docs/03_백엔드_설계.md): Firebase RTDB `.info/serverTimeOffset`을 받아 <see cref="SetServerOffset"/>에
    ///   넣으면 모든 시각이 서버 기준이 된다 — 여기 한 곳만 바꾸면 되도록 모아 둔 것.
    /// - '하루'(출석 윷점 등)는 기기 타임존과 무관하게 **KST(UTC+9) 고정**으로 계산한다.
    /// </summary>
    public static class TrustedTime
    {
        /// <summary>한국 표준시 — 일일 리셋 기준 타임존 (서버/KST 고정 결정, Docs/05 B5 해결).</summary>
        public static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

        static TimeSpan serverOffset = TimeSpan.Zero;

        /// <summary>서버 시각을 한 번이라도 받았는지. false면 기기 시계 폴백 중.</summary>
        public static bool HasServerTime { get; private set; }

        /// <summary>신뢰 시각(UTC). 서버 오프셋 적용.</summary>
        public static DateTime UtcNow => DateTime.UtcNow + serverOffset;

        /// <summary>신뢰 시각을 KST 벽시계로 (Kind = Unspecified).</summary>
        public static DateTime KstNow => DateTime.SpecifyKind(UtcNow + KstOffset, DateTimeKind.Unspecified);

        /// <summary>Firebase 등에서 받은 "서버 − 기기" 시간차.</summary>
        public static void SetServerOffset(TimeSpan offset)
        {
            serverOffset = offset;
            HasServerTime = true;
        }

        /// <summary>테스트·서버 연결 끊김 시 기기 시계로 되돌림.</summary>
        public static void ClearServerOffset()
        {
            serverOffset = TimeSpan.Zero;
            HasServerTime = false;
        }

        /// <summary>테스트용 — 신뢰 시각을 앞으로 민다 (나루터·오프라인 정산 등).</summary>
        public static void DebugAdvance(TimeSpan delta)
        {
            if (delta <= TimeSpan.Zero) return;
            serverOffset += delta;
        }
    }
}
