using UnityEngine;
using Yoegoe.Core;
using Yoegoe.Data;

namespace Yoegoe.Characters
{
    /// <summary>
    /// 접속 인사 (6장): 앱을 켜거나 오래 비웠다 돌아오면 <b>놀고 있던</b> 요괴들이 「어서와」「기다렸어」류 인사.
    /// 출석 윷점이 뜬 날엔 옥토끼 대사가 끝난 뒤(<see cref="SpeechGate"/> 해제 후)에 한다.
    /// 대사 = 시트 character_lines type=greeting.
    /// </summary>
    public static class Greeting
    {
        /// <summary>이보다 오래 비웠다 돌아오면 다시 인사한다 (앱 재실행은 항상, 시트 game_settings).</summary>
        public static float AwaySecondsForGreeting => GameSettings.AwaySecondsForGreeting;
        /// <summary>화면이 뜬 뒤 첫 인사까지 잠깐 쉼 + 요괴끼리 순서대로 말하게 하는 간격.</summary>
        const float FirstDelaySeconds = 1.2f;
        const float StaggerSeconds = 0.7f;
        const string DefaultLine = "어서 와! 기다렸어.";

        static bool pending;
        static float readyAt;

        /// <summary>인사 예약 (앱 시작 / 오래 비웠다 복귀).</summary>
        public static void Request()
        {
            pending = true;
            readyAt = Time.unscaledTime + FirstDelaySeconds;
        }

        /// <summary>매 프레임 — 출석 윷점 대사가 끝나야 실행.</summary>
        public static void Tick(bool blockedByUi)
        {
            if (!pending || blockedByUi || SpeechGate.YokaiSilenced) return;
            if (Time.unscaledTime < readyAt) return;
            pending = false;

            int order = 0;
            foreach (var agent in CharacterAgent.All)
            {
                if (!ShouldGreet(agent)) continue;
                agent.SayAfter(order * StaggerSeconds, PickLine(agent));
                order++;
            }
        }

        public static bool ShouldGreet(CharacterAgent agent) =>
            agent != null && agent.Stats != null && agent.Stats.State == ActionState.Playing;

        public static string PickLine(CharacterAgent agent)
        {
            CharacterCatalog.Entry entry = null;
            if (agent != null && agent.Data != null) CharacterCatalog.TryGet(agent.Data.id, out entry);
            return CharacterCatalog.PickLine(entry?.greetingLines, DefaultLine);
        }
    }
}
