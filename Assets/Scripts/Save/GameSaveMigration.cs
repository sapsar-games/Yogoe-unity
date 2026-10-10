using UnityEngine;

namespace Yoegoe.Save
{
    /// <summary>
    /// 구버전 세이브를 최신 GameSaveData 스키마로 끌어올린다.
    /// GameSaveBridge.TryLoadSimulateAndApply()가 로드 직후·오프라인 정산 전에 호출한다.
    ///
    /// 세이브 필드를 추가/의미 변경할 때마다: CurrentVersion을 올리고,
    /// 아래 MigrateToCurrent에 "data.version < N" 단계를 하나 추가한다.
    /// (지금은 v1이 최초 스키마라 실제 이관 로직은 없음 — 다음 스키마 변경 때 여기 채울 자리.)
    /// </summary>
    public static class GameSaveMigration
    {
        public const int CurrentVersion = 2;

        public static void MigrateToCurrent(GameSaveData data)
        {
            if (data == null) return;

            if (data.version > CurrentVersion)
            {
                // 구버전 빌드로 최신 세이브를 열었을 때 (롤백 등). 모르는 필드는 무시하고 그대로 둔다.
                Debug.LogWarning($"[GameSaveMigration] 세이브 버전({data.version})이 " +
                                  $"현재 빌드({CurrentVersion})보다 높습니다. 마이그레이션 없이 진행합니다.");
                return;
            }

            // v2: 나루터(pier) 추가 — 값 보정 없음. 비어 있으면 SpiritPier.ResetFromSave가 지금부터 시작한다.
            if (data.version < 2) data.version = 2;

            data.version = CurrentVersion;
        }
    }
}
