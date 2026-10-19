using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Cooking;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Minigames.Yut;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 윷놀이 진입점(11장). 윷 토큰 소모 → 현재 슬롯의 혼 전원(옥토끼 제외 — v1.3 NPC화) vs 이무기(말 1개) →
    /// 완주 시 부적 지급까지의 최소 완결 루프. 화면/입력은 YutMiniGame, 규칙은 YutMatch가 담당.
    /// </summary>
    public partial class YutScreen : SingletonBehaviour<YutScreen>, IYutChallengeHost, IYutSquareRewardHost
    {
        YutMiniGame IYutChallengeHost.MiniGame => miniGame;
        void IYutChallengeHost.ShowNotice(string message, Action onOk) => ShowNotice(message, onOk);
        YutSquareReward IYutChallengeHost.RollTreasureReward() => squareRewards.RollTreasure(this);
        string IYutChallengeHost.ApplySquareRewardToEconomy(YutSquareReward reward, int multiplier)
            => ApplySquareRewardToEconomy(reward, multiplier);
        void IYutChallengeHost.RefreshCollectedItemsDisplay() => RefreshCollectedItemsDisplay();
        void IYutChallengeHost.SaveFromWorld() => GameSaveBridge.SaveFromWorld();

        YutMiniGame IYutSquareRewardHost.MiniGame => miniGame;
        void IYutSquareRewardHost.ShowNotice(string message, Action onOk) => ShowNotice(message, onOk);
        void IYutSquareRewardHost.ShowRewardChoice(string message, Action onPlain, Action onAd, Sprite icon)
            => ShowRewardChoice(message, onPlain, onAd, icon);
        Coroutine IYutSquareRewardHost.StartRoutine(IEnumerator routine) => StartCoroutine(routine);
        IReadOnlyList<OfferingData> IYutSquareRewardHost.GetTreasureOfferingPool() => GetOfferingPool();
        string IYutSquareRewardHost.ApplySquareRewardToEconomy(YutSquareReward reward, int multiplier)
            => ApplySquareRewardToEconomy(reward, multiplier);
        void IYutSquareRewardHost.ClearConsumedSpecialSquareAt(int nodeId) => ClearConsumedSpecialSquareAt(nodeId);
        void IYutSquareRewardHost.RefreshCollectedItemsDisplay() => RefreshCollectedItemsDisplay();
        void IYutSquareRewardHost.SaveFromWorld() => GameSaveBridge.SaveFromWorld();
        void IYutSquareRewardHost.OnSquareRewardFlowEnded(bool pendingBonusThrow) => OnSquareRewardFlowEnded(pendingBonusThrow);

        public Font font;

        /// <summary>말 이동 시 친밀도 +0.25(11장) 적용을 위한 piece id → 캐릭터 매핑.</summary>
        readonly Dictionary<string, CharacterAgent> teamById = new Dictionary<string, CharacterAgent>();

        /// <summary>이번 윷 Open 세션에서 말 이동으로 오른 친밀도(요괴별). Close 때 하트 연출용.</summary>
        readonly Dictionary<string, float> sessionIntimacyGain = new Dictionary<string, float>();

        [Header("셸 (Prefab — 필수)")]
        [SerializeField] GameObject root;
        [SerializeField] YutMiniGame miniGame;
        [SerializeField] GameObject noticeRoot;
        [SerializeField] Text noticeText;
        [SerializeField] Button noticeOkButton;
        [SerializeField] GameObject choiceRoot;
        [SerializeField] Text choiceText;
        [SerializeField] Button choiceContinueButton;
        [SerializeField] Button choiceStopButton;
        [SerializeField] GameObject rewardRoot;
        [SerializeField] Image rewardIcon;
        [SerializeField] Text rewardText;
        [SerializeField] Button rewardPlainButton;
        [SerializeField] Button rewardAdButton;
        [SerializeField] GameObject reviveRoot;
        [SerializeField] Text reviveText;
        [SerializeField] Button reviveYesButton;
        [SerializeField] Button reviveNoButton;

        const float ReviveAdWatchSeconds = 0.8f;

        public bool HasPrefabShell => root != null && miniGame != null;

        /// <summary>윷 화면이 떠 있는 동안 본맵 핀치/휠 줌·드래그가 새면 안 된다.
        /// Prefab Root가 켜져 있어도, Open() 전에는 맵 입력을 막지 않는다.
        /// set 될 때마다 UiBlockGate에 자동으로 등록/해제 — MapPointerRouter가 이 화면 입장에서
        /// 입력을 막아야 한다는 걸 안다(값 바뀌는 지점을 일일이 찾아다니지 않아도 됨).</summary>
        bool isOpen;
        public bool IsOpen
        {
            get => isOpen;
            private set
            {
                if (isOpen == value) return;
                isOpen = value;
                if (value) Yoegoe.Core.UiBlockGate.Register(this);
                else Yoegoe.Core.UiBlockGate.Unregister(this);
            }
        }

        YutMatch match;
        YutThrowOutcome? pendingOutcome;
        /// <summary>칸 홉 연출 중 — 연타로 Apply가 두 번 돌지 않게.</summary>
        bool moveInProgress;

        /// <summary>말 한 마리가 골인해서 "계속할지/그만할지" 다이얼로그가 떠 있는 동안, 방금 던진
        /// 결과가 보너스였는지 기억해뒀다가 '계속하기'를 고르면 그대로 이어서 써야 한다.</summary>
        bool awaitingFinishChoice;
        bool pendingBonusAfterContinue;

        /// <summary>완주 부적 지급용 — 이번 판 완주 말 수.</summary>
        int pendingFinishCharmCount;
        int pendingFinishStack;

        /// <summary>직전 OnPlayerPieceFinished 스택(자동 플레이가 그만/계속 판단용).</summary>
        int lastFinishEventStack;
        readonly List<string> lastFinishedIds = new List<string>();

        /// <summary>특수 칸 보상 — 팝업·비행·지급 흐름은 Presenter.</summary>
        readonly YutSquareRewardPresenter squareRewards = new YutSquareRewardPresenter();

        /// <summary>매 판 도전과제 — 배너·완료 보상(보물상자 ×3)은 Presenter가 담당.</summary>
        readonly YutChallengePresenter challenge = new YutChallengePresenter();

        /// <summary>"광고 보고 말 되살리기" 팝업이 떠 있는 동안 이무기 보너스 턴 진행을 멈춘다.</summary>
        bool awaitingReviveChoice;
        List<YutMatch.CapturedPieceSnapshot> pendingReviveSnapshots;

        /// <summary>이번 매치에서 특수 칸으로 모은 것들 — 동(東) 구역에 표시, 매치가 끝나면 요약
        /// 다이얼로그로도 보여준다. 새 매치 시작할 때 비운다(재시작 복원 시엔 다시 0부터).</summary>
        readonly Dictionary<OfferingData, int> matchOfferingCounts = new Dictionary<OfferingData, int>();
        readonly Dictionary<CookingIngredientId, int> matchIngredientCounts = new Dictionary<CookingIngredientId, int>();
        readonly Dictionary<CookingCharmType, int> matchCharmCounts = new Dictionary<CookingCharmType, int>();
        int matchYeopjeonTotal;
        int matchWaterTotal;
        int matchHyangTotal;
        int matchAdTicketTotal;
        int matchYutTokenTotal;

        /// <summary>이번 매치에서 모았지만 아직 경제에 안 넣은 획득(완주 때 지급·연출용으로 옮긴다).</summary>
        readonly List<PostYutLootEntry> pendingLoot = new List<PostYutLootEntry>();
        /// <summary>완주로 경제에 이미 넣은 뒤, Close 때 머리 위 만세 연출에 쓸 목록.</summary>
        readonly List<PostYutLootEntry> presentableLoot = new List<PostYutLootEntry>();
        /// <summary>이번 윷 세션에서 한 번이라도 완주(매치 종료)가 났으면 true — 그때만 Close에서
        /// 머리 위 만세·공양물 연출을 켠다. 중도 나가기에는 연출하지 않는다.</summary>
        bool allowPostYutLootPresentation;
        /// <summary>완주 정산 구간(종료 처리~새 판 BeginMatch 전)에만 true. 이때 pending/도전 보상을
        /// 경제에 넣는다. 새 판이 열리면 false로 돌아가 다시 미지급 적립.</summary>
        bool grantRewardsToEconomyNow;

        Action pendingNoticeAction;
        Action pendingChoiceContinue;
        Action pendingChoiceStop;
        Action pendingRewardPlain;
        Action pendingRewardAd;
        Action pendingReviveYes;
        Action pendingReviveNo;

        Dictionary<int, Sprite> BuildSpecialSquareIcons()
        {
            var icons = new Dictionary<int, Sprite>();
            for (int nodeId = 0; nodeId < YutBoardLayout.NodeCount; nodeId++)
            {
                var kind = YutBoardLayout.GetSpecialKind(nodeId);
                switch (kind)
                {
                    case YutBoardLayout.SpecialSquareKind.Coin:
                        icons[nodeId] = YutMiniGame.YeopjeonIcon();
                        break;
                    case YutBoardLayout.SpecialSquareKind.IngredientBag:
                        icons[nodeId] = YutMiniGame.IngredientBagIcon();
                        break;
                    case YutBoardLayout.SpecialSquareKind.Treasure:
                        icons[nodeId] = Resources.Load<Sprite>("UI/GiftChest_Closed");
                        break;
                    case YutBoardLayout.SpecialSquareKind.Water:
                        icons[nodeId] = YutMiniGame.WaterIcon();
                        break;
                }
            }
            return icons;
        }

        /// <summary>윷 토큰(하트) 옆 [+] — 메인 HUD와 같은 팝업(엽전 구매/광고 충전)을 윷놀이
        /// 화면 안에서도 그대로 연다.</summary>
        void HandleBuyTokensPressed()
        {
            if (DualActionPopup.Instance != null) DualActionPopup.Instance.OpenYutTokenShop();
        }

        void HandlePiecesChanged()
        {
            if (match == null) return;

            // LINQ(.Select/.ToList)를 새 struct(RosterEntry)에 처음 쓰면 IL2CPP WebGL 빌드에서
            // "RuntimeError: null function"이 나는 경우가 있어(제네릭 인스턴스 누락) — 수동 루프로 우회.
            var roster = new List<YutMiniGame.RosterEntry>(match.PlayerPieces.Count);
            foreach (var p in match.PlayerPieces)
            {
                teamById.TryGetValue(p.Id, out var agent);
                int stamina = agent != null && agent.Stats != null ? Mathf.RoundToInt(agent.Stats.Stamina) : 0;
                int intimacy = agent != null && agent.Stats != null ? Mathf.RoundToInt(agent.Stats.Intimacy) : 0;
                // 출발 전·잡혀 복귀는 슬롯에 말 대기, 보드/완주는 슬롯 실루엣.
                bool waitingInSlot = !p.Finished && p.NodeId < 0;
                roster.Add(new YutMiniGame.RosterEntry(p.Id, p.DisplayName, stamina, intimacy, PositionLabelFor(p),
                    waitingInSlot));
            }
            // 지금 키우는(소환된) 요괴 수만큼만 말을 쓸 수 있다. 빈 슬롯(고라니, 연 4번째 슬롯의 구미호)이
            // 있으면 "소환하기" 슬롯을 안내한다 — 대상은 CharacterSummon.NextSummonTarget.
            bool showExtraSlot = CharacterSummon.NextSummonTarget(out _);
            // 대기말을 슬롯에 붙이려면 로스터 칩이 먼저 있어야 한다.
            miniGame.ShowRoster(roster, showExtraSlot, showExtraSlot ? "소환하기" : null,
                showExtraSlot ? OnSummonSlotTapped : (Action)null);

            var infos = match.PlayerPieces
                .Where(p => !p.Finished)
                .Select(p => new YutMiniGame.YokaiPieceInfo(p.Id, p.DisplayName, p.NodeId))
                .ToList();
            miniGame.ShowYokaiPieces(infos);

            // 참(시작점)에 멈춘 것과 완주(골인)한 건 구별돼야 한다 — 완주하면 보드에서 빠지는
            // 대신 동(東) 구역 하단에 작은 초상으로 표시한다.
            var finishedIds = new List<string>();
            foreach (var p in match.PlayerPieces)
                if (p.Finished) finishedIds.Add(p.Id);
            // 옥토끼는 말로 뛰지 않지만(NPC화) 항상 동 줄에 있는 것으로 쳐서 해설 말풍선 자리를 유지한다.
            finishedIds.Add(nameof(CharacterId.Rabbit));
            miniGame.ShowFinishedPieces(finishedIds);

            var opp = match.OpponentPiece;
            if (opp.OnBoard)
            {
                miniGame.ShowOpponentPiece(true);
                miniGame.SetOpponentPieceIndex(opp.NodeId);
            }
            else
            {
                // 잡힌 뒤(또는 입장 전) 대기 — 참먹이 칸이 아니라 바로 아래.
                miniGame.ShowOpponentPiece(true);
                miniGame.PlaceOpponentWaitingBelowStart();
            }
        }

        // 소환 연출(로스터 [소환하기] 탭 → 암전 → 요괴 아이콘 낙하)은 YutScreen.Summon.cs로 분리.

        /// <summary>말 하나의 현재 보드 위치를 사람이 읽는 이름으로 — 대기/완주가 아니면 잘 알려진
        /// 이름 있는 칸(참·도·개·걸·윷·모·뒷모·찌모·방)만 그대로, 그 외는 "N칸째"로 안전하게 표기.</summary>
        static string PositionLabelFor(YutPiece p)
        {
            if (p.Finished) return "완주";
            if (p.NodeId < 0) return p.IsPlayer ? "출발 대기" : "참 아래 대기";
            return p.NodeId switch
            {
                YutBoardLayout.Start => "참",
                1 => "도",
                2 => "개",
                3 => "걸",
                4 => "윷",
                YutBoardLayout.Mo => "모",
                YutBoardLayout.DwitMo => "뒷모",
                YutBoardLayout.JjiMo => "찌모",
                YutBoardLayout.Bang => "방",
                _ => $"{p.NodeId}칸"
            };
        }

        void EnsureBuilt()
        {
            if (!HasPrefabShell)
            {
                Debug.LogError(
                    "[YutScreen] Prefab 셸이 없습니다. Main 씬에 YutScreen Prefab 인스턴스를 배치하세요.");
                return;
            }

            // 예전 Prefab엔 리워드/되살리기 팝업이 없을 수 있어 항상 보정한다.
            EnsureExtraPanels();
        }

        void EnsureExtraPanels()
        {
            if (root == null) return;
            var rootRt = (RectTransform)root.transform;
            if (rewardRoot == null) BuildRewardPanel(rootRt);
            if (reviveRoot == null) BuildRevivePanel(rootRt);
        }

        void WireRuntimeListeners()
        {
            if (miniGame == null) return;

            miniGame.OnThrowPressed -= HandleThrowPressed;
            miniGame.OnLeavePressed -= HandleLeavePressed;
            miniGame.OnCandidateTapped -= HandleCandidateTapped;
            miniGame.OnBuyTokensPressed -= HandleBuyTokensPressed;
            miniGame.OnThrowPressed += HandleThrowPressed;
            miniGame.OnLeavePressed += HandleLeavePressed;
            miniGame.OnCandidateTapped += HandleCandidateTapped;
            miniGame.OnBuyTokensPressed += HandleBuyTokensPressed;

            // 윷놀이 화면 안에서 [+]로 토큰을 사도(또는 광고로 충전해도) 하트가 바로 갱신되게.
            if (GameEconomy.Instance != null)
            {
                GameEconomy.Instance.OnYutTokenChanged -= HandleYutTokenChanged;
                GameEconomy.Instance.OnYutTokenChanged += HandleYutTokenChanged;
            }

            miniGame.font = font;
            miniGame.BindFromHierarchy();

            if (noticeOkButton != null)
            {
                noticeOkButton.onClick.RemoveAllListeners();
                noticeOkButton.onClick.AddListener(OnNoticeOk);
            }

            if (choiceContinueButton != null)
            {
                choiceContinueButton.onClick.RemoveAllListeners();
                choiceContinueButton.onClick.AddListener(OnChoiceContinueClicked);
            }

            if (choiceStopButton != null)
            {
                choiceStopButton.onClick.RemoveAllListeners();
                choiceStopButton.onClick.AddListener(OnChoiceStopClicked);
            }

            if (rewardPlainButton != null)
            {
                rewardPlainButton.onClick.RemoveAllListeners();
                rewardPlainButton.onClick.AddListener(OnRewardPlainClicked);
            }

            if (rewardAdButton != null)
            {
                rewardAdButton.onClick.RemoveAllListeners();
                rewardAdButton.onClick.AddListener(OnRewardAdClicked);
            }

            if (reviveYesButton != null)
            {
                reviveYesButton.onClick.RemoveAllListeners();
                reviveYesButton.onClick.AddListener(OnReviveYesClicked);
            }

            if (reviveNoButton != null)
            {
                reviveNoButton.onClick.RemoveAllListeners();
                reviveNoButton.onClick.AddListener(OnReviveNoClicked);
            }
        }

    }
}
