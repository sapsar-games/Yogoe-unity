using Yoegoe.Cooking;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    public partial class GongyangganScreen
    {
        /// <summary>
        /// 시작 버튼. 재료가 20개 미만이면 "재료가 모자라는데도 요리할까요?" 확인 (19장).
        /// 보이는 판(미리보기)을 그대로 시작하고, 재료·부적은 이때 처음 차감된다.
        /// </summary>
        void OnStart()
        {
            if (session == null || session.Running || session.Finished) return;
            if (session.MaterialsOnBoard < CookingSession.MinMaterialsToCook)
            {
                if (statusText != null) statusText.text = "재료가 모자라 요리할 수 없어요";
                return;
            }
            if (session.IsShortBoard)
                ConfirmPopup.Show("재료가 모자라는데도 요리할까요?", BeginRound);
            else
                BeginRound();
        }

        void BeginRound()
        {
            if (session == null || session.Running || session.Finished) return;
            if (selectedCharm != CookingCharmType.None
                && (GameEconomy.Instance == null || !GameEconomy.Instance.TrySpendCharm(selectedCharm)))
            {
                // 부적이 그사이 없어졌으면 부적 없는 판으로 다시 깐다
                selectedCharm = CookingCharmType.None;
                RebuildSession();
                return;
            }
            if (!session.StartRound())
            {
                // 판에 깔린 재료가 인벤에 더는 없음 — 부적은 돌려주고 판을 다시 깐다
                if (selectedCharm != CookingCharmType.None && GameEconomy.Instance != null)
                    GameEconomy.Instance.AddCharm(selectedCharm, 1);
                RebuildSession();
                return;
            }
            GameSaveBridge.RequestSave(); // 재료·부적 차감
            RefreshView();
        }

        /// <summary>선택한 부적으로 판(미리보기)을 새로 깐다. 재료는 차감하지 않는다.</summary>
        void RebuildSession()
        {
            if (session != null)
            {
                session.Changed -= RefreshView;
                session.RoundEnded -= OnRoundEnded;
                session.TimeUp -= OnTimeUp;
            }
            session = new CookingSession();
            session.Changed += RefreshView;
            session.RoundEnded += OnRoundEnded;
            session.TimeUp += OnTimeUp;
            session.Prepare(selectedCharm);
            RefreshView();
        }

        void SelectCharm(CookingCharmType charm)
        {
            if (session != null && session.Running) return;
            if (charm != CookingCharmType.None)
            {
                int held = GameEconomy.Instance != null ? GameEconomy.Instance.GetCharmCount(charm) : 0;
                if (held <= 0 && selectedCharm != charm) return;
            }
            selectedCharm = selectedCharm == charm ? CookingCharmType.None : charm;
            RebuildSession();
        }

        /// <summary>시간 종료 → "광고 보고 15초 더?" (19장, 무제한). 아니오·바깥 탭 = 그만하고 정산.
        /// 광고 SDK 미연동 — 예를 누르면 바로 연장(스텁).</summary>
        void OnTimeUp()
        {
            if (session == null || !session.AwaitingExtend) return;
            var s = session;
            ConfirmPopup.Show("시간이 다 됐어요!\n광고 보고 15초 더 할까요?",
                () => { if (session == s) s.ExtendByAd(); },
                () => { if (session == s) s.FinishAfterTimeUp(); },
                yes: "광고 보고 +15초", no: "그만하기");
        }

        /// <summary>'다시 지피다' — 같은 부적(남아 있으면)으로 새 판을 깔고 바로 시작 절차(재료 부족 확인 포함).</summary>
        void OnRekindle()
        {
            if (resultPopup != null) resultPopup.SetActive(false);
            if (selectedCharm != CookingCharmType.None
                && (GameEconomy.Instance == null || GameEconomy.Instance.GetCharmCount(selectedCharm) <= 0))
                selectedCharm = CookingCharmType.None;
            RebuildSession();
            OnStart();
        }
    }
}
