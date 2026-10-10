using UnityEngine;

namespace Yoegoe.Data
{
    /// <summary>
    /// 새 게임 시작 상태 (Docs/00 §2).
    /// Assets/Resources/StartingStateSettings.asset
    /// </summary>
    [CreateAssetMenu(fileName = "StartingStateSettings", menuName = "Yoegoe/Starting State Settings")]
    public class StartingStateSettings : ScriptableObject
    {
        [Header("시작 캐릭터 (토끼·삼족오)")]
        [Tooltip("친밀도 0~100. 시작 2인 = 50.")]
        public float startingIntimacy = 50f;

        [Header("시작 재화")]
        public int startingMerit = 1000;
        public int startingYeopjeon = 100;
        public int startingHyang = 2;
        [Tooltip("물. 시작 0.")]
        public int startingWater = 0;
        public int startingYutToken = 5;
        public int yutTokenMax = 5;

        [Header("시작 공양물·음식 인벤토리")]
        [Tooltip("시작 지급 목록. 기획: 음식·공양물 0 → 비움.")]
        public OfferingData[] startingOfferings;
        public int startingOfferingCountEach = 0;

        static StartingStateSettings runtime;

        /// <summary>에셋 사본에 시트 game_settings 의 start* 값을 입힌 것 (에셋 원본은 건드리지 않는다).</summary>
        public static StartingStateSettings Get()
        {
            if (runtime != null) return runtime;
            var loaded = Resources.Load<StartingStateSettings>("StartingStateSettings");
            runtime = loaded != null ? Instantiate(loaded) : CreateInstance<StartingStateSettings>();
            runtime.hideFlags = HideFlags.DontSave;
            GameSettings.ApplyStart(runtime);
            return runtime;
        }
    }
}
