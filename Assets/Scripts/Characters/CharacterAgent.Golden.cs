using System;
using UnityEngine;
using Yoegoe.Core;
using Yoegoe.Data;

namespace Yoegoe.Characters
{
    // 황금음식 버프 (공양간 기획서): 먹으면 5분간 요괴가 황금색, 이동 속도 2배, 기물에서 일하면 생산 속도 2배.
    // 끝나는 시각은 실제 시각(TrustedTime)으로 세이브 — 앱을 껐다 켜도 남은 시간만큼 이어진다.
    // 오프라인 정산(OfflineSimulator)에는 반영하지 않는다(최대 5분).
    public partial class CharacterAgent
    {
        // 시트 game_settings
        public static float GoldenBuffSeconds => GameSettings.GoldenBuffSeconds;
        public static float GoldenSpeedMultiplier => GameSettings.GoldenSpeedMul;

        static readonly Color GoldenTint = new Color(1f, 0.84f, 0.3f, 1f);
        bool goldenTinted;

        public bool IsGolden => Stats.GoldenBuffEndsUtcTicks > TrustedTime.UtcNow.Ticks;

        /// <summary>이동·생산 배율 (황금 버프 중 2, 아니면 1).</summary>
        public float SpeedMultiplier => IsGolden ? GoldenSpeedMultiplier : 1f;

        float MoveSpeed => moveSpeed * SpeedMultiplier;

        public float GoldenSecondsLeft =>
            Math.Max(0f, (float)TimeSpan.FromTicks(Stats.GoldenBuffEndsUtcTicks - TrustedTime.UtcNow.Ticks).TotalSeconds);

        /// <summary>황금음식을 먹음 — 5분 (이미 버프 중이면 지금부터 다시 5분).</summary>
        public void ApplyGoldenBuff()
        {
            Stats.GoldenBuffEndsUtcTicks = TrustedTime.UtcNow.AddSeconds(GoldenBuffSeconds).Ticks;
        }

        void UpdateGoldenTint()
        {
            bool golden = IsGolden;
            if (!golden && !goldenTinted) return;
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer == null) return;
            if (golden)
            {
                // 황금색 + 살짝 반짝임
                spriteRenderer.color = Color.Lerp(GoldenTint, Color.white,
                    0.15f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f));
                goldenTinted = true;
            }
            else
            {
                spriteRenderer.color = Color.white;
                goldenTinted = false;
            }
        }
    }
}
