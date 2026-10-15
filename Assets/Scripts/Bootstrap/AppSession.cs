using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Economy;
using Yoegoe.Save;
using Yoegoe.UI;

namespace Yoegoe.Bootstrap
{
    /// <summary>
    /// 앱 세션: 벽시계 정산, pause/focus 세이브, 출석 팝업, WebGL 로딩 오버레이.
    /// Unity 콜백은 <see cref="Main"/>에 두고 여기로 위임한다.
    /// </summary>
    public sealed class AppSession
    {
        /// <summary>이보다 긴 벽시계 공백이면 캐릭터 정산을 돌린다 (WebGL 탭 숨김 등).</summary>
        const float WallClockCatchUpThresholdSeconds = 1f;
        /// <summary>자동 저장 간격 — 웹은 새로고침·탭 닫기에서 종료 이벤트가 안 올 수 있어 주기적으로 저장한다.</summary>
        const float AutoSaveSeconds = 30f;
        /// <summary>RequestSave 후 이만큼 모아서 한 번 저장 (연속 탭마다 저장하지 않게).</summary>
        const float RequestedSaveDelaySeconds = 2f;

        float lastSaveAt;

        DateTime lastActiveUtc;
        bool worldReady;
        readonly Font hudFont;

        /// <summary>테스트 버튼용 — 현재 세션.</summary>
        static AppSession current;

        /// <summary>테스트용 벽시계 배속 (1 = 보통). HUD 2·4·8·16배 버튼.</summary>
        public static float DebugTimeScale { get; private set; } = 1f;

        public AppSession(Font hudFont)
        {
            this.hudFont = hudFont;
            lastActiveUtc = TrustedTime.UtcNow;
            current = this;
        }

        /// <summary>테스트용 — 벽시계 배속. 같은 값을 다시 누르면 1배로.</summary>
        public static void SetDebugTimeScale(float scale)
        {
            if (scale < 1.001f) DebugTimeScale = 1f;
            else DebugTimeScale = scale;
        }

        /// <summary>테스트용 — 벽시계를 minutes만큼 앞으로 밀고 나루터·요괴 정산을 바로 돌린다.</summary>
        public static void DebugAdvanceMinutes(float minutes)
        {
            current?.AdvanceDebug(minutes);
        }

        void AdvanceDebug(float minutes)
        {
            if (!worldReady || minutes <= 0f) return;
            TrustedTime.DebugAdvance(TimeSpan.FromMinutes(minutes));
            ApplyWallClockCatchUpIfNeeded();
        }

        public void MarkReady()
        {
            lastActiveUtc = TrustedTime.UtcNow;
            worldReady = true;
            lastSaveAt = Time.unscaledTime;
            Greeting.Request(); // 앱을 켜면 놀던 요괴들이 인사 (출석 윷점 대사 뒤)
        }

        public void Tick()
        {
            if (!worldReady) return;

            float scale = DebugTimeScale;
            float realDt = Time.unscaledDeltaTime;
            // 배속: 실시간에 (scale-1)배를 더해 벽시계를 앞으로 민다
            if (scale > 1.001f)
                TrustedTime.DebugAdvance(TimeSpan.FromSeconds(realDt * (scale - 1f)));

            var now = TrustedTime.UtcNow;
            double gap = (now - lastActiveUtc).TotalSeconds;
            lastActiveUtc = now;

            GameEconomy.Instance?.EnsureYutTokenFresh(now);
            AdvancePier(now);

            if (scale > 1.001f)
            {
                // Update()가 이미 1배만큼 돌리므로, 가속분만 추가 정산
                float extra = realDt * (scale - 1f);
                if (extra > 0.0001f)
                    CharacterAgent.CatchUpAll(Mathf.Min(extra, OfflineSimulator.MaxOfflineSeconds));
            }
            else if (gap >= WallClockCatchUpThresholdSeconds)
            {
                float seconds = (float)Math.Min(gap, OfflineSimulator.MaxOfflineSeconds);
                CharacterAgent.CatchUpAll(seconds);
            }
            if (gap >= Greeting.AwaySecondsForGreeting) Greeting.Request();

            Greeting.Tick(AttendanceScreen.IsOpen);
            TickAutoSave();
        }

        /// <summary>나루터 혼령 줄 — 지난 시간만큼 도착·떠남 (오프라인 포함).</summary>
        static void AdvancePier(DateTime now)
        {
            var eco = GameEconomy.Instance;
            if (eco == null) return;
            SpiritPier.Advance(now, () => SpiritPier.BuildOrderPool(eco), UnityEngine.Random.Range);
        }

        void TickAutoSave()
        {
            float since = Time.unscaledTime - lastSaveAt;
            bool due = since >= AutoSaveSeconds
                       || (GameSaveBridge.SaveRequested && since >= RequestedSaveDelaySeconds);
            if (!due) return;
            GameSaveBridge.SaveFromWorld();
            lastSaveAt = Time.unscaledTime;
        }

        public void OnPause(bool pause)
        {
            if (pause)
            {
                if (worldReady) GameSaveBridge.SaveFromWorld();
                return;
            }

            ApplyWallClockCatchUpIfNeeded();
            if (worldReady) AttendanceScreen.TryOpenIfDue(hudFont);
        }

        public void OnFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                if (worldReady) GameSaveBridge.SaveFromWorld();
                return;
            }

            ApplyWallClockCatchUpIfNeeded();
            if (worldReady) AttendanceScreen.TryOpenIfDue(hudFont);
        }

        public void OnQuit()
        {
            GameSaveBridge.SaveFromWorld();
        }

        public void TryOpenAttendanceIfDue()
        {
            AttendanceScreen.TryOpenIfDue(hudFont);
        }

        public void HideWebGlLoadingOverlay()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            YogoeHideLoadingOverlay();
#endif
        }

        void ApplyWallClockCatchUpIfNeeded()
        {
            if (!worldReady) return;

            var now = TrustedTime.UtcNow;
            double gap = (now - lastActiveUtc).TotalSeconds;
            lastActiveUtc = now;

            GameEconomy.Instance?.EnsureYutTokenFresh(now);
            AdvancePier(now);

            if (gap < WallClockCatchUpThresholdSeconds) return;

            float seconds = (float)Math.Min(gap, OfflineSimulator.MaxOfflineSeconds);
            CharacterAgent.CatchUpAll(seconds);
            if (gap >= Greeting.AwaySecondsForGreeting) Greeting.Request();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void YogoeHideLoadingOverlay();
#endif
    }
}
