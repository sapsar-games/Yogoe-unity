using System;
using UnityEngine;
using Yoegoe.Data;

namespace Yoegoe.UI
{
    /// <summary>
    /// 게임 전체 글자 크기 = 기준값(시트 game_settings) × 플레이어 설정(설정 화면 · 기기에 저장).
    /// - 화면 글자(UI Text): uiTextScale
    /// - 맵 글자(기물 이름표·보관 숫자 등 TextMesh): worldTextScale
    /// - 요괴 말풍선: bubbleTextScale · 한 줄 bubbleMaxChars 자
    /// 실제 적용은 <see cref="UiTextScaler"/>가 모든 글자의 원래 크기를 기억해 두고 배율을 곱한다.
    /// </summary>
    public static class UiTextScale
    {
        public const string PrefKey = "settings.textSize";

        /// <summary>플레이어 단계: 작게 · 보통 · 크게 · 아주 크게.</summary>
        public static readonly string[] LevelNames = { "작게", "보통", "크게", "아주 크게" };
        static readonly float[] LevelScales = { 0.85f, 1f, 1.15f, 1.3f };
        public const int DefaultLevel = 1;

        public static event Action Changed;

        static int level = -1;

        public static int Level
        {
            get
            {
                if (level < 0)
                {
                    try { level = PlayerPrefs.GetInt(PrefKey, DefaultLevel); } catch { level = DefaultLevel; }
                    level = Mathf.Clamp(level, 0, LevelScales.Length - 1);
                }
                return level;
            }
            set
            {
                int v = Mathf.Clamp(value, 0, LevelScales.Length - 1);
                if (v == Level) return;
                level = v;
                try { PlayerPrefs.SetInt(PrefKey, v); PlayerPrefs.Save(); } catch { }
                Changed?.Invoke();
            }
        }

        public static float Player => LevelScales[Level];

        /// <summary>화면(UI) 글자 배율.</summary>
        public static float Ui => Clamp(GameSettings.Get("uiTextScale")) * Player;
        /// <summary>맵 글자(기물 라벨 등) 배율.</summary>
        public static float World => Clamp(GameSettings.Get("worldTextScale")) * Player;
        /// <summary>요괴 말풍선 배율.</summary>
        public static float Bubble => Clamp(GameSettings.Get("bubbleTextScale")) * Player;
        /// <summary>말풍선 한 줄 최대 글자 수.</summary>
        public static int BubbleMaxChars => Mathf.Clamp(Mathf.RoundToInt(GameSettings.Get("bubbleMaxChars")), 4, 40);

        static float Clamp(float v) => v <= 0f ? 1f : Mathf.Clamp(v, 0.5f, 2.5f);

        /// <summary>시트 값이 바뀌었을 때(핫리로드·테스트) 다시 적용하게.</summary>
        public static void NotifyChanged() => Changed?.Invoke();
    }
}
