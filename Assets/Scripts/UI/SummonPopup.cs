using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.UI
{
    /// <summary>
    /// 빈 캐릭터 슬롯 탭 → 소환 확인 / 잠긴 슬롯 해금 확인.
    /// 셸은 <see cref="ConfirmPopup"/> Prefab. (기획 9-1)
    /// </summary>
    public class SummonPopup : SingletonBehaviour<SummonPopup>
    {
        public Font font;
        public CharacterData goraniData;

        enum Mode { Summon, Unlock }
        Mode mode;
        CharacterId target = CharacterId.Gorani;

        /// <summary>빈 슬롯 소환 확인. target: 3번째 슬롯 고라니, 4번째(연 뒤) 구미호.</summary>
        public void Open(CharacterId summonTarget = CharacterId.Gorani)
        {
            if (!CharacterSummon.HasOpenSlotFor(summonTarget) || CharacterSummon.IsPresent(summonTarget)) return;
            mode = Mode.Summon;
            target = summonTarget;
            int have = GameEconomy.Instance != null ? GameEconomy.Instance.Hyang : 0;
            bool can = CharacterSummon.CanSummon(target);
            string costLine = can
                ? "향 " + CharacterSummon.HyangCost + "개 소모 (보유 " + have + ")"
                : "향이 부족합니다 (필요 " + CharacterSummon.HyangCost + ", 보유 " + have + ")";
            ConfirmPopup.Show(
                "향 " + CharacterSummon.HyangCost + "개를 피워 요괴를 부르시겠습니까?\n" + costLine,
                OnConfirmed,
                null,
                "부르기",
                "닫기");
        }

        /// <summary>잠긴 4번째 슬롯: 엽전 99개로 열 수 있다는 안내 + 열기.</summary>
        public void OpenUnlock()
        {
            if (CharacterSummon.LockedSlotUnlocked) return;
            mode = Mode.Unlock;
            ShowUnlockConfirm();
        }

        public void Close() => ConfirmPopup.Dismiss();

        void ShowUnlockConfirm()
        {
            int have = GameEconomy.Instance != null ? GameEconomy.Instance.Yeopjeon : 0;
            int need = CharacterSummon.LockedSlotYeopjeonCost;
            bool can = have >= need;
            string costLine = can
                ? "엽전 " + need + "개 소모 (보유 " + have + ")"
                : "엽전이 부족합니다 (필요 " + need + ", 보유 " + have + ")";
            ConfirmPopup.Show(
                "잠긴 자리입니다.\n엽전 " + need + "개로 열 수 있어요.\n" + costLine,
                OnConfirmed,
                null,
                "열기",
                "닫기");
        }

        void OnConfirmed()
        {
            if (mode == Mode.Unlock)
            {
                if (CharacterSummon.TryUnlockLockedSlot())
                {
                    Yoegoe.Save.GameSaveBridge.SaveFromWorld();
                    return;
                }
                // 엽전 부족 — 안내 유지
                ShowUnlockConfirm();
                return;
            }

            if (CharacterSummon.IsPresent(target))
                return;

            if (!CharacterSummon.CanSummon(target))
            {
                if (ShopScreen.Instance != null) ShopScreen.Instance.Open();
                return;
            }

            // 절차는 CharacterSummon.TrySummon 하나 — 여기선 지금 보이는 화면에 맞는 연출만 고른다.
            // 윷 화면이 열려 있으면 맵 연출(월드)은 가려지므로 윷 화면 연출로.
            if (YutScreen.Instance != null && YutScreen.Instance.IsOpen && YutScreen.Instance.PlaySummon(target))
                return;
            if (SummonCeremony.Instance != null && SummonCeremony.Instance.TryPlay(target))
                return;

            if (CharacterSummon.TrySummon(target, font, target == CharacterId.Gorani ? goraniData : null) == null)
                Debug.LogWarning("[SummonPopup] 소환 실패");
        }
    }
}
