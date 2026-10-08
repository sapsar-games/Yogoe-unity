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
    /// 윷놀이 진입점(11장). 윷 토큰 소모 → 옥토끼+현재 슬롯의 혼 전원 vs 이무기(말 1개) →
    /// 완주 시 부적 지급까지의 최소 완결 루프. 화면/입력은 YutMiniGame, 규칙은 YutMatch가 담당.
    /// </summary>
    public partial class YutScreen : MonoBehaviour, IYutChallengeHost, IYutSquareRewardHost
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

        public static YutScreen Instance { get; private set; }

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
        /// Prefab Root가 켜져 있어도, Open() 전에는 맵 입력을 막지 않는다.</summary>
        public bool IsOpen { get; private set; }

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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Coroutine autoPlayRoutine;
        bool autoPlayRunning;
        /// <summary>0이면 매치 종료까지. N이면 골인 스택이 N 이상일 때 그만하기.</summary>
        int autoPlayStopAtStack;
        /// <summary>true면 특수칸 내용 확인→받기/광고 2배 UI까지 간 뒤 멈춘다.</summary>
        bool autoPlayStopAtSquareReward;
        /// <summary>true면 도전과제 보물상자×3 안내(notice)까지 간 뒤 멈춘다.</summary>
        bool autoPlayStopAtChallengeReward;
        /// <summary>true면 이무기를 잡아 참 아래 대기까지 간 뒤 멈춘다.</summary>
        bool autoPlayStopAtImugiCapture;
        bool autoPlayImugiCaptureReached;
        YutThrowResult? debugForcedThrow;
        /// <summary>한 번이 아니라 매 던지기마다 같은 결과를 강제(도전 연속 모/빽도 QA).</summary>
        YutThrowResult? debugForcedThrowRepeat;
#endif

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

        void Awake()
        {
            Instance = this;
            CharacterSummon.Summoned += OnCharacterSummoned;
        }

        void Start()
        {
            EnsureBuilt();
            WireRuntimeListeners();
            if (root != null) root.SetActive(false);
            IsOpen = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CharacterSummon.Summoned -= OnCharacterSummoned;
        }

        public void Open()
        {
            EnsureBuilt();
            if (!HasPrefabShell) return;

            // 맵 만세 연출·수거 후 공양 요구 타이머가 남아 있으면 취소.
            if (PostYutLootPresenter.Instance != null)
                PostYutLootPresenter.Instance.ForceComplete(skipOfferingRequest: true);

            // 나갔다 왔거나(Close) 앱을 껐다 켜서(ApplyFromSave) 이어할 매치가 이미 있으면
            // 토큰을 새로 안 쓰고 그대로 이어서 보여준다.
            if (match != null && !match.IsEnded)
            {
                ResumeExistingMatch();
                return;
            }

            if (GameEconomy.Instance == null || !GameEconomy.Instance.TrySpendYutToken(1))
            {
                ShowNotice("윷 토큰이 부족합니다.", null);
                return;
            }

            var agents = CharacterAgent.All
                .Where(a => a != null && a.Stats != null)
                .ToList();

            if (agents.Count == 0)
            {
                GameEconomy.Instance.AddYutToken(1); // 참가할 요괴가 없으면 토큰 환불
                ShowNotice("참가할 요괴가 없습니다.", null);
                return;
            }

            teamById.Clear();
            sessionIntimacyGain.Clear();
            var team = new List<(string id, string name)>();
            foreach (var a in agents)
            {
                string id = a.Data != null ? a.Data.id.ToString() : a.name;
                string name = a.Data != null && !string.IsNullOrEmpty(a.Data.displayName) ? a.Data.displayName : a.name;
                teamById[id] = a;
                team.Add((id, name));
            }

            BeginMatch(team);
        }

        void BeginMatch(List<(string id, string name)> team)
        {
            match = new YutMatch(team);
            YutBoardLayout.RegenerateSpecialSquares(); // 진짜 새 매치일 때만 새로 뽑는다
            SubscribeMatchEvents();

            awaitingFinishChoice = false;
            squareRewards.Clear();
            awaitingReviveChoice = false;
            pendingOpponentLappedFx = false;
            matchOfferingCounts.Clear();
            matchIngredientCounts.Clear();
            matchCharmCounts.Clear();
            matchYeopjeonTotal = 0;
            matchWaterTotal = 0;
            matchHyangTotal = 0;
            matchAdTicketTotal = 0;
            matchYutTokenTotal = 0;
            pendingLoot.Clear();
            // 완주 후 새 판 — 정산 구간 종료. 연출용 presentable·allow 플래그는 Close까지 유지.
            grantRewardsToEconomyNow = false;
            challenge.StartNew(match.PlayerPieces.Count, this);
            root.SetActive(true);
            IsOpen = true;
            miniGame.Show();
            miniGame.SetLeaveVisible(true);
            miniGame.SetThrowVisible(true);
            // 말풍선 규칙 1번: 던지기 전에는 어떤 요괴도(이무기 포함) 말하지 않는다.
            miniGame.RefreshHearts(GameEconomy.Instance.YutToken);
            HandlePiecesChanged();
            RefreshCollectedItemsDisplay();
            ApplySpecialSquareVisuals();
            GameSaveBridge.SaveFromWorld();
        }

        /// <summary>이미 진행 중이던(나갔다 왔거나 앱 재시작으로 복원된) 매치를 그대로 보여준다 —
        /// 토큰 소모·팀 재구성 없이 화면만 다시 연다.</summary>
        void ResumeExistingMatch()
        {
            root.SetActive(true);
            IsOpen = true;
            miniGame.Show();
            miniGame.SetLeaveVisible(true);
            miniGame.SetThrowVisible(true);
            miniGame.RefreshHearts(GameEconomy.Instance != null ? GameEconomy.Instance.YutToken : 0);
            challenge.RefreshBanner(this);
            HandlePiecesChanged();
            RefreshCollectedItemsDisplay(); // 세이브에 남은 미지급 획득분이 있으면 동 구역에 다시 표시
            ApplySpecialSquareVisuals(); // 셸이 다시 만들어졌을 수도 있어 매번 다시 입힌다
        }

        void HandleYutTokenChanged(int token)
        {
            if (miniGame != null) miniGame.RefreshHearts(token);
        }
        /// <summary>
        /// 윷놀이 이벤트 구독
        /// </summary>
        void SubscribeMatchEvents()
        {
            if (match == null) return;
            UnsubscribeMatchEvents();
            match.OnPiecesChanged += HandlePiecesChanged;
            match.OnMatchEnded += HandleMatchEnded;
            match.OnPlayerPiecesMoved += HandlePlayerPiecesMoved;
            match.OnPlayerPiecesCaptured += HandlePlayerPiecesCaptured;
            match.OnPlayerPiecesCapturedRevivable += HandlePlayerPiecesCapturedRevivable;
            match.OnOpponentCaptured += HandleOpponentCaptured;
            match.OnPlayerPieceFinished += HandlePlayerPieceFinished;
            match.OnSpecialSquareReached += HandleSpecialSquareReached;
            match.OnOpponentLapped += HandleOpponentLapped;
        }

        void UnsubscribeMatchEvents()
        {
            if (match == null) return;
            match.OnPiecesChanged -= HandlePiecesChanged;
            match.OnMatchEnded -= HandleMatchEnded;
            match.OnPlayerPiecesMoved -= HandlePlayerPiecesMoved;
            match.OnPlayerPiecesCaptured -= HandlePlayerPiecesCaptured;
            match.OnPlayerPiecesCapturedRevivable -= HandlePlayerPiecesCapturedRevivable;
            match.OnOpponentCaptured -= HandleOpponentCaptured;
            match.OnPlayerPieceFinished -= HandlePlayerPieceFinished;
            match.OnSpecialSquareReached -= HandleSpecialSquareReached;
            match.OnOpponentLapped -= HandleOpponentLapped;
        }

        /// <summary>
        /// "나가기"를 눌러도 승패가 안 난 매치는 메모리에 그대로 둔다 — 다시 열면 이어서 하고,
        /// 세이브에도 매번 담겨서 앱을 껐다 켜도 이어진다. 승패가 이미 난 매치만 완전히 정리한다.
        /// persist=false: 콜드스타트 강제 닫기 등 — 세이브 불러오기 전에 저장하면 안 될 때.
        /// </summary>
        public void Close(bool persist = true)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugStopAutoPlay();
#endif
            if (match != null && match.IsEnded)
            {
                UnsubscribeMatchEvents();
                match = null;
            }
            // 팝업이 떠 있던 채로 나가면 그 선택은 그냥 흘려보낸다(다음에 열면 던지기 대기 상태로).
            pendingOutcome = null;
            moveInProgress = false;
            awaitingFinishChoice = false;
            pendingBonusAfterContinue = false;
            squareRewards.Clear();
            challenge.ClearBlockingFlags();
            awaitingReviveChoice = false;
            pendingReviveSnapshots = null;
            // root만 꺼두면 팝업 자신의 activeSelf는 그대로 남아있어서, 다음에 다시 열 때
            // (ResumeExistingMatch) 엉뚱하게 같이 떠버린다 — 하나씩 확실히 내려둔다.
            if (noticeRoot != null) noticeRoot.SetActive(false);
            if (choiceRoot != null) choiceRoot.SetActive(false);
            if (rewardRoot != null) rewardRoot.SetActive(false);
            if (reviveRoot != null) reviveRoot.SetActive(false);
            ConfirmPopup.Dismiss();
            if (miniGame != null) miniGame.Hide();
            if (root != null) root.SetActive(false);
            IsOpen = false;
            if (persist) GameSaveBridge.SaveFromWorld();
            // 머리 위 획득물·이어지는 공양 요구는 완주 후 윷을 완전히 끝낼 때만.
            // 중도 나가기는 매치를 이어가므로 연출하지 않고, 미연출 목록은 유지한다.
            if (allowPostYutLootPresentation)
            {
                allowPostYutLootPresentation = false;
                TryPresentPostYutLoot();
            }
            // 친밀도 하트: 이번 Open에서 오른 분(반내림) — 완주·중도 나가기 모두.
            PlaySessionIntimacyHearts();
        }

        /// <summary>세션 중 쌓인 친밀도만큼 맵 요괴 머리 위 하트 연출 후 누적 초기화.</summary>
        void PlaySessionIntimacyHearts()
        {
            if (sessionIntimacyGain.Count == 0) return;
            foreach (var kv in sessionIntimacyGain)
            {
                if (kv.Value <= 0.0001f) continue;
                CharacterAgent agent = null;
                if (!string.IsNullOrEmpty(kv.Key))
                    teamById.TryGetValue(kv.Key, out agent);
                if (agent == null)
                {
                    for (int i = 0; i < CharacterAgent.All.Count; i++)
                    {
                        var a = CharacterAgent.All[i];
                        if (a == null || a.Data == null) continue;
                        if (a.Data.id.ToString() == kv.Key) { agent = a; break; }
                    }
                }
                if (agent != null)
                    IntimacyHeartFx.PlayFromAgent(agent, kv.Value);
            }
            sessionIntimacyGain.Clear();
        }

        /// <summary>기획 11장: 말을 움직일 때마다 그 요괴 친밀도 +0.25.</summary>
        void HandlePlayerPiecesMoved(IReadOnlyList<string> pieceIds)
        {
            foreach (var id in pieceIds)
            {
                if (!teamById.TryGetValue(id, out var agent) || agent == null) continue;
                agent.AddIntimacy(0.25f);
                sessionIntimacyGain.TryGetValue(id, out float cur);
                sessionIntimacyGain[id] = cur + 0.25f;
            }
        }

        /// <summary>이무기한테 내 말이 잡혔을 때 — 말풍선 규칙: 반드시 먼저 "으악"이라고 말한
        /// 뒤에 출발점으로 돌아간다. 이 시점엔 모델 위치(NodeId)는 이미 -1이지만 화면상 말은
        /// 아직 잡힌 자리에 있다(OnPiecesChanged가 이 다음에 따로 발행되어야 실제로 이동한다) —
        /// 그래서 여기서 말풍선을 먼저 띄우면 자연히 "말하고 나서 이동"이 된다.</summary>
        void HandlePlayerPiecesCaptured(IReadOnlyList<YutPiece> captured)
        {
            foreach (var p in captured)
            {
                miniGame.ShowPieceBubble(p.Id, YutBubbleCatalog.Get(YutBubbleCatalog.Ids.EventCaptured));
                miniGame.AddPlayLogEntry($"{p.DisplayName} 잡힘.");
            }
            if (challenge.State != null)
            {
                challenge.NotifyPlayerCaptured(this);
                GameSaveBridge.SaveFromWorld();
            }
        }

        /// <summary>같은 시점 — "광고 보고 되살리기" 팝업. 선택이 끝날 때까지 이무기 보너스 턴
        /// 진행을 멈춘다(RunOpponentTurnRoutine의 awaitingReviveChoice 대기).</summary>
        void HandlePlayerPiecesCapturedRevivable(List<YutMatch.CapturedPieceSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0) return;
            pendingReviveSnapshots = snapshots;
            awaitingReviveChoice = true;
            string names = JoinPieceNames(snapshots);
            ShowReviveChoice($"{names} 잡혔어요!\n광고 보고 되살릴까요?",
                onYes: HandleReviveYes,
                onNo: HandleReviveNo);
        }

        // LINQ(.Select)를 새 struct(CapturedPieceSnapshot)에 처음 쓰면 IL2CPP WebGL 빌드에서
        // "RuntimeError: null function"이 나는 경우가 있어(제네릭 인스턴스 누락) — 수동 루프로 우회.
        string JoinPieceNames(List<YutMatch.CapturedPieceSnapshot> snapshots)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(NameFor(snapshots[i].PieceId));
            }
            return sb.ToString();
        }

        void HandleReviveYes() => StartCoroutine(ReviveAdRoutine());

        IEnumerator ReviveAdRoutine()
        {
            // 실제 광고 SDK가 붙기 전까지 DualActionPopup과 동일한 짧은 지연으로 시청을 흉내낸다.
            yield return new WaitForSecondsRealtime(ReviveAdWatchSeconds);

            if (match != null && pendingReviveSnapshots != null && match.ReviveCapturedPieces(pendingReviveSnapshots))
            {
                string names = JoinPieceNames(pendingReviveSnapshots);
                miniGame.ShowPieceBubble(
                    pendingReviveSnapshots[0].PieceId,
                    YutBubbleCatalog.Get(YutBubbleCatalog.Ids.EventRevived));
                miniGame.AddPlayLogEntry($"{names} 되살아남.");
                GameSaveBridge.SaveFromWorld();
            }
            pendingReviveSnapshots = null;
            awaitingReviveChoice = false;
        }

        void HandleReviveNo()
        {
            pendingReviveSnapshots = null;
            awaitingReviveChoice = false;
        }

        /// <summary>내가 이무기를 잡았을 때.</summary>
        void HandleOpponentCaptured()
        {
            miniGame.ShowOpponentBubble(YutBubbleCatalog.Get(YutBubbleCatalog.Ids.EventOpponentCaught));
            miniGame.AddPlayLogEntry("이무기 잡음.");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (autoPlayStopAtImugiCapture)
                autoPlayImugiCaptureReached = true;
#endif
        }

        /// <summary>이무기가 참을 지나 한 바퀴 — 턴 루틴이 연출(흔들림·칸 비행)을 yield한다.</summary>
        bool pendingOpponentLappedFx;

        /// <summary>이무기가 참을 지나 한 바퀴 — 플래그만 세운다(연출은 RunOpponentTurnRoutine).</summary>
        void HandleOpponentLapped()
        {
            pendingOpponentLappedFx = true;
        }

        /// <summary>"지루하군." + 보드 흔들림 + 남은 보상 칸이 새 자리로 슝 이동.</summary>
        IEnumerator OpponentLappedFxRoutine()
        {
            miniGame.ShowOpponentBubble(YutBubbleCatalog.Get(YutBubbleCatalog.Ids.OpponentLapped));
            miniGame.AddPlayLogEntry("이무기 한 바퀴 — 남은 보상 칸이 바뀜.");

            var before = SnapshotSpecialSquareVisuals();
            if (before.Count == 0)
            {
                yield return miniGame.PlaySpecialSquaresReshuffleAnim(Array.Empty<YutMiniGame.SpecialSquareFlight>());
                yield break;
            }

            ReshuffleRemainingSpecialSquares(applyVisuals: false);
            var after = SnapshotSpecialSquareVisuals();
            var flights = BuildSpecialSquareFlights(before, after);
            yield return miniGame.PlaySpecialSquaresReshuffleAnim(flights);
            ApplySpecialSquareVisuals();
            GameSaveBridge.SaveFromWorld();
        }

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

        /// <summary>power(0~1)는 슬라이드 속도 기반 — 던지는 연출에만 쓰고 결과 확률엔 영향 없다.</summary>
        void HandleThrowPressed(float power)
        {
            if (match == null || match.IsEnded) return;
            YutThrowOutcome outcome;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugForcedThrow.HasValue)
            {
                var forced = debugForcedThrow.Value;
                debugForcedThrow = null;
                bool bonus = forced == YutThrowResult.Yut || forced == YutThrowResult.Mo;
                outcome = new YutThrowOutcome(forced, bonus);
            }
            else if (debugForcedThrowRepeat.HasValue)
            {
                var forced = debugForcedThrowRepeat.Value;
                bool bonus = forced == YutThrowResult.Yut || forced == YutThrowResult.Mo;
                outcome = new YutThrowOutcome(forced, bonus);
            }
            else
#endif
            {
                outcome = match.ThrowForPlayer();
            }
            StartCoroutine(PlayerThrowRoutine(outcome, power));
        }

        /// <summary>말풍선 규칙 2번 — 옥토끼가 결과를 말한다. 문구는 YutBubbleCatalog(시트→JSON).
        /// 윷/모는 "다시"만 알리고 칸수는 말하지 않는다(보너스라 이번 결과로는 안 움직일 수도 있어서).
        /// 옥토끼가 이미 완주해 동에 있으면 해설 톤(cheer)으로 바꾼다.</summary>
        string RabbitThrowLine(YutThrowResult result)
        {
            if (IsRabbitFinished())
            {
                return YutBubbleCatalog.Format(
                    YutBubbleCatalog.Ids.RabbitCheer,
                    ("result", result.DisplayName()));
            }

            switch (result)
            {
                case YutThrowResult.Yut: return YutBubbleCatalog.Get(YutBubbleCatalog.Ids.RabbitYut);
                case YutThrowResult.Mo: return YutBubbleCatalog.Get(YutBubbleCatalog.Ids.RabbitMo);
                case YutThrowResult.Baekdo: return YutBubbleCatalog.Get(YutBubbleCatalog.Ids.RabbitBaekdo);
                default:
                    int steps = result switch
                    {
                        YutThrowResult.Do => 1,
                        YutThrowResult.Gae => 2,
                        YutThrowResult.Geol => 3,
                        _ => 0,
                    };
                    return YutBubbleCatalog.Format(
                        YutBubbleCatalog.Ids.RabbitSteps,
                        ("result", result.DisplayName()),
                        ("steps", steps.ToString()));
            }
        }

        bool IsRabbitFinished()
        {
            if (match == null) return false;
            string rabbitId = nameof(CharacterId.Rabbit);
            for (int i = 0; i < match.PlayerPieces.Count; i++)
            {
                var p = match.PlayerPieces[i];
                if (p.Id == rabbitId && p.Finished) return true;
            }
            return false;
        }

        /// <summary>말풍선 규칙 2번 — 각 요괴는 아래 조건 중 하나에 해당할 때만(우선순위 순으로
        /// 하나만) 말한다. 일반 칸으로만 이동 가능하거나 아예 이동할 수 없으면 말하지 않는다.
        /// 문구는 YutBubbleCatalog, 우선순위/조건은 여기 코드.</summary>
        string BubbleLineFor(YutMatch.YutMoveCandidate c)
        {
            switch (YutBoardLayout.GetSpecialKind(c.DestinationNode))
            {
                case YutBoardLayout.SpecialSquareKind.Treasure:
                    return YutBubbleCatalog.Get(YutBubbleCatalog.Ids.CandidateTreasure);
                case YutBoardLayout.SpecialSquareKind.IngredientBag:
                    return YutBubbleCatalog.Get(YutBubbleCatalog.Ids.CandidateIngredientBag);
                case YutBoardLayout.SpecialSquareKind.Coin:
                    return YutBubbleCatalog.Get(YutBubbleCatalog.Ids.CandidateCoin);
                case YutBoardLayout.SpecialSquareKind.Water:
                    return YutBubbleCatalog.Get(YutBubbleCatalog.Ids.CandidateWater);
            }

            if (match.OpponentPiece.OnBoard && match.OpponentPiece.NodeId == c.DestinationNode)
                return YutBubbleCatalog.Get(YutBubbleCatalog.Ids.CandidateCapture);

            var ally = match.PlayerPieces.FirstOrDefault(
                p => !p.Finished && p.Id != c.PieceId && p.OnBoard && p.NodeId == c.DestinationNode);
            if (ally != null)
                return YutBubbleCatalog.Format(
                    YutBubbleCatalog.Ids.CandidateStack,
                    ("ally", ally.DisplayName));

            if (c.WillFinish) return YutBubbleCatalog.Get(YutBubbleCatalog.Ids.CandidateFinish);

            return null;
        }

        void ShowMoveCandidateBubbles(IReadOnlyList<YutMatch.YutMoveCandidate> candidates)
        {
            // 같은 문구면 한 명만 말한다 — 로스터/대기 슬롯 앞쪽(PlayerPieces 앞) 우선.
            // (시작 전 대기말 여럿이 같은 특수칸·같은 대사를 동시에 띄우던 중복 방지)
            var speakerByLine = new Dictionary<string, string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                string line = BubbleLineFor(c);
                if (line == null) continue;
                if (!speakerByLine.TryGetValue(line, out var currentId))
                {
                    speakerByLine[line] = c.PieceId;
                    continue;
                }
                if (PieceSlotIndex(c.PieceId) < PieceSlotIndex(currentId))
                    speakerByLine[line] = c.PieceId;
            }

            foreach (var kv in speakerByLine)
                miniGame.ShowPieceBubble(kv.Value, kv.Key);

            // 옥토끼가 동에 있으면, 남은 말이 완주 가능할 때 옆에서 한 마디.
            if (!IsRabbitFinished() || candidates == null) return;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].WillFinish) continue;
                miniGame.ShowPieceBubble(
                    nameof(CharacterId.Rabbit),
                    YutBubbleCatalog.Get(YutBubbleCatalog.Ids.RabbitUrgeFinish));
                break;
            }
        }

        /// <summary>대기/로스터 슬롯 순서 — 앞(0)일수록 대표 말풍선 우선.</summary>
        int PieceSlotIndex(string pieceId)
        {
            if (match == null || string.IsNullOrEmpty(pieceId)) return int.MaxValue;
            for (int i = 0; i < match.PlayerPieces.Count; i++)
            {
                if (match.PlayerPieces[i].Id == pieceId)
                    return i;
            }
            return int.MaxValue;
        }

        /// <summary>말이 실제로 움직인 뒤 — 놀이기록(짧은 사건형 문장)에 한 줄 남긴다. 말풍선
        /// 대사와는 분리된 별개 기록이라 여기 텍스트는 대화체가 아니라 사건 요약체로 쓴다.</summary>
        void LogPlayerMoveOutcome(string pieceId, string moverName, YutThrowOutcome outcome, bool bonusTurn)
        {
            var movedPiece = match.PlayerPieces.FirstOrDefault(p => p.Id == pieceId);
            if (movedPiece != null)
            {
                if (movedPiece.Finished)
                {
                    miniGame.AddPlayLogEntry($"{moverName} 완주.");
                }
                else
                {
                    miniGame.AddPlayLogEntry(
                        $"{outcome.Result.DisplayName()}. {moverName} {PositionLabelFor(movedPiece)}(으)로 이동.");
                    var partners = match.PlayerPieces
                        .Where(p => !p.Finished && p.Id != pieceId && p.NodeId == movedPiece.NodeId)
                        .Select(p => p.DisplayName)
                        .ToList();
                    if (partners.Count > 0)
                        miniGame.AddPlayLogEntry($"{string.Join(", ", partners)}와 업음.");
                }
            }
            if (bonusTurn)
                miniGame.AddPlayLogEntry("다시 던짐.");
        }

        IEnumerator PlayerThrowRoutine(YutThrowOutcome outcome, float power)
        {
            miniGame.SetThrowVisible(false);
            miniGame.ClearBubbles();
            yield return miniGame.PlayThrowAnim(outcome.Result, power);
            if (match == null || match.IsEnded) yield break;
            miniGame.ShowPieceBubble("Rabbit", RabbitThrowLine(outcome.Result));

            if (challenge.NotifyPlayerThrow(outcome.Result, this))
            {
                yield return challenge.PlayRewardRoutine(this);
                if (match == null || match.IsEnded) yield break;
            }

            var candidates = match.GetPlayerCandidates(outcome.Result);
            if (candidates.Count == 0)
            {
                // 이동할 말이 없음(예: 대기 말뿐인데 빽도) — 턴을 그냥 넘긴다.
                yield return RunOpponentTurnRoutine();
                yield break;
            }

            ShowMoveCandidateBubbles(candidates);

            pendingOutcome = outcome;
            var uiCandidates = new List<YutMiniGame.YokaiMoveCandidate>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var memberIds = StackMemberIdsFor(c.PieceId);
                var memberNames = new string[memberIds.Length];
                for (int m = 0; m < memberIds.Length; m++)
                    memberNames[m] = NameFor(memberIds[m]);
                uiCandidates.Add(new YutMiniGame.YokaiMoveCandidate(
                    c.PieceId,
                    NameFor(c.PieceId),
                    c.DestinationNode,
                    c.UseShortcut,
                    memberIds,
                    memberNames,
                    c.WillFinish));
            }
            miniGame.FlashCandidates(uiCandidates);
        }

        /// <summary>보드에 업혀 있으면 같은 칸 전원, 대기 말이면 본인만 — 후보 칸에 초상을 전부 그리기 위함.</summary>
        string[] StackMemberIdsFor(string pieceId)
        {
            if (match == null) return new[] { pieceId };
            YutPiece piece = null;
            for (int i = 0; i < match.PlayerPieces.Count; i++)
            {
                var p = match.PlayerPieces[i];
                if (p.Id == pieceId && !p.Finished) { piece = p; break; }
            }
            if (piece == null || !piece.OnBoard) return new[] { pieceId };

            var ids = new List<string>();
            for (int i = 0; i < match.PlayerPieces.Count; i++)
            {
                var p = match.PlayerPieces[i];
                if (!p.Finished && p.NodeId == piece.NodeId) ids.Add(p.Id);
            }
            return ids.Count > 0 ? ids.ToArray() : new[] { pieceId };
        }

        void HandleCandidateTapped(string pieceId, bool useShortcut)
        {
            if (match == null || match.IsEnded || pendingOutcome == null || moveInProgress) return;
            StartCoroutine(PlayerMoveRoutine(pieceId, useShortcut, pendingOutcome.Value));
        }

        IEnumerator PlayerMoveRoutine(string pieceId, bool useShortcut, YutThrowOutcome outcome)
        {
            moveInProgress = true;
            miniGame.ClearCandidates();
            miniGame.ClearBubbles();
            pendingOutcome = null;
            string moverName = NameFor(pieceId);

            var preview = match.PreviewPlayerHop(pieceId, useShortcut, outcome);
            if (preview.Ok)
                yield return miniGame.PlayHopAlongPath(preview.PieceIds, preview.HopNodes, preview.WillFinish);

            if (match == null || match.IsEnded)
            {
                moveInProgress = false;
                yield break;
            }

            // 홉으로 이미 도착해 있으니 ShowYokaiPieces의 미끄러짐은 생략
            miniGame.SetSuppressPieceSlide(true);
            bool bonusTurn = match.ApplyPlayerMove(pieceId, useShortcut, outcome);
            miniGame.SetSuppressPieceSlide(false);

            LogPlayerMoveOutcome(pieceId, moverName, outcome, bonusTurn);

            if (match == null || match.IsEnded)
            {
                moveInProgress = false;
                yield break; // HandleMatchEnded가 이미 결과 처리
            }

            if (squareRewards.Awaiting)
            {
                // 특수 칸 보상 팝업("그냥 받기"/"광고 보고 2배")이 이미 떴다 — 그 선택이 끝나야 다음이 진행된다.
                squareRewards.PendingBonusThrow = bonusTurn;
                moveInProgress = false;
                yield break;
            }

            if (awaitingFinishChoice)
            {
                // 골인 다이얼로그("계속하기"/"그만하기")가 이미 떴다 — 그 선택이 끝나야 다음이 진행된다.
                pendingBonusAfterContinue = bonusTurn;
                moveInProgress = false;
                yield break;
            }

            if (bonusTurn)
                miniGame.SetThrowVisible(true);
            else
                yield return RunOpponentTurnRoutine();

            moveInProgress = false;
        }

        /// <summary>말이 골인했는데 아직 안 들어온 말이 남아있을 때 — 여기서 그만 받을지, 계속할지 묻는다.</summary>
        void HandlePlayerPieceFinished(IReadOnlyList<string> finishedIds)
        {
            lastFinishEventStack = finishedIds != null ? finishedIds.Count : 0;
            lastFinishedIds.Clear();
            if (finishedIds != null)
            {
                for (int i = 0; i < finishedIds.Count; i++)
                    lastFinishedIds.Add(finishedIds[i]);
            }

            awaitingFinishChoice = true;
            miniGame.SetThrowVisible(false);
            string names = string.Join(", ", finishedIds.Select(NameFor));

            // 옥토끼가 이번 골인에 포함되면 동 초상 위에서 한 마디(이미 ShowFinishedPieces로 앵커 있음).
            if (finishedIds != null)
            {
                string rabbitId = nameof(CharacterId.Rabbit);
                for (int i = 0; i < finishedIds.Count; i++)
                {
                    if (finishedIds[i] != rabbitId) continue;
                    miniGame.ShowPieceBubble(
                        rabbitId,
                        YutBubbleCatalog.Get(YutBubbleCatalog.Ids.RabbitFinished));
                    break;
                }
            }

            ShowChoice($"{names} 골인!\n여기서 그만 받을까요, 남은 말로 계속할까요?",
                onContinue: HandleContinueAfterFinish,
                onStop: () => HandleStopAfterFinish(finishedIds));
        }

        /// <summary>말이 하나 골인해서 계속하기로 했으면 — 기존 말은 그대로 두고 게임 이어가기
        void HandleContinueAfterFinish()
        {
            awaitingFinishChoice = false;
            ProceedAfterFinishContinue();
        }

        /// <summary>특수 칸(엽전/공양물/보물상자) 배치만 다시 뽑는다 — 말 위치·역사는 안 건드린다.</summary>
        void ResetSpecialSquares()
        {
            YutBoardLayout.RegenerateSpecialSquares();
            ApplySpecialSquareVisuals();
        }

        void ProceedAfterFinishContinue()
        {
            if (pendingBonusAfterContinue)
                miniGame.SetThrowVisible(true);
            else
                StartCoroutine(RunOpponentTurnRoutine());
        }

        /// <summary>골인 후 "여기서 그만" — 완주로 매치 종료.</summary>
        void HandleStopAfterFinish(IReadOnlyList<string> finishedIds)
        {
            awaitingFinishChoice = false;
            match?.EndAsFinished(finishedIds != null ? finishedIds.Count : 1);
        }

        /// <summary>
        /// 이무기 턴 전체(보너스 턴 포함)를 한 번씩 던지기 애니메이션까지 보여주며 진행한다.
        /// 플레이어 턴과 대칭으로 ThrowForOpponent/ApplyOpponentMove를 한 스텝씩 돌려서,
        /// 이무기도 실제로 던지는 모습이 보이고 게임로그로 지금 누구 차례인지 알 수 있게 한다.
        /// 보드 한가운데 큰 연출은 플레이어 전용 — 이무기는 초상 밑에 조그맣게 던진다.
        /// </summary>
        IEnumerator RunOpponentTurnRoutine()
        {
            miniGame.SetThrowVisible(false);
            miniGame.ClearBubbles();
            yield return new WaitForSecondsRealtime(0.4f);

            bool bonus;
            int guard = 0;
            do
            {
                guard++;
                var outcome = match.ThrowForOpponent();
                yield return miniGame.PlayOpponentMiniThrowAnim(outcome.Result);
                if (match == null || match.IsEnded) yield break;
                miniGame.ClearBubbles();
                miniGame.ShowOpponentBubble(YutBubbleCatalog.Format(
                    YutBubbleCatalog.Ids.OpponentThrow,
                    ("result", outcome.Result.DisplayName())));

                bonus = false;
                var oppPreview = match.PreviewOpponentHop(outcome);
                if (oppPreview.Ok)
                {
                    // 대기에서 첫 입장: 참에 띄운 뒤 경로를 밟는다(웹도 출발→각 칸).
                    if (!match.OpponentPiece.OnBoard)
                    {
                        miniGame.SetSuppressPieceSlide(true);
                        miniGame.ShowOpponentPiece(true);
                        miniGame.SetOpponentPieceIndex(YutBoardLayout.Start);
                        miniGame.SetSuppressPieceSlide(false);
                    }
                    yield return miniGame.PlayOpponentHopAlongPath(oppPreview.HopNodes);
                }

                if (match == null || match.IsEnded) yield break;

                miniGame.SetSuppressPieceSlide(true);
                bonus = match.ApplyOpponentMove(outcome);
                miniGame.SetSuppressPieceSlide(false);
                if (match.IsEnded) yield break;
                if (pendingOpponentLappedFx)
                {
                    pendingOpponentLappedFx = false;
                    yield return OpponentLappedFxRoutine();
                    if (match == null || match.IsEnded) yield break;
                }
                if (match.OpponentPiece.OnBoard)
                    miniGame.AddPlayLogEntry($"이무기, {PositionLabelFor(match.OpponentPiece)}(으)로 이동.");
                if (bonus)
                    miniGame.AddPlayLogEntry("다시 던짐.");

                // 말이 잡혔으면 "광고 보고 되살리기" 팝업이 뜬다 — 선택이 끝날 때까지 다음 던지기를 멈춘다.
                if (awaitingReviveChoice)
                    yield return new WaitUntil(() => !awaitingReviveChoice);
                if (match == null || match.IsEnded) yield break;

                if (bonus)
                    yield return new WaitForSecondsRealtime(0.4f);
            } while (bonus && guard < 20);

            if (match != null && !match.IsEnded)
            {
                miniGame.SetThrowVisible(true);
            }
        }

        void HandleLeavePressed() => Close();

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

        /// <summary>로스터 [소환하기] — 맵과 같은 확인 창(SummonPopup)·같은 절차(CharacterSummon)를 쓴다.
        /// 확인하면 SummonPopup이 윷 화면이 열려 있는 걸 보고 <see cref="PlaySummon"/>으로 연출을 넘긴다.</summary>
        void OnSummonSlotTapped()
        {
            if (!CharacterSummon.NextSummonTarget(out var target)) return;
            if (SummonPopup.Instance != null) SummonPopup.Instance.Open(target);
        }

        bool summonPresenting;

        /// <summary>윷 화면용 소환 연출 (맵 SummonCeremony는 월드라 윷 패널에 가려진다). 시작하면 true.</summary>
        public bool PlaySummon(CharacterId target)
        {
            if (!CharacterSummon.CanSummon(target) || !isActiveAndEnabled) return false;
            StartCoroutine(SummonCeremonyRoutine(target));
            return true;
        }

        /// <summary>어디서 소환됐든(맵·윷) 진행 중인 매치에 바로 대기 말로 합류 — 소환 즉시 말로 쓸 수 있다.</summary>
        void OnCharacterSummoned(CharacterAgent agent)
        {
            if (agent == null) return;
            if (match != null && !match.IsEnded)
            {
                string id = agent.Data != null ? agent.Data.id.ToString() : agent.name;
                string name = agent.Data != null && !string.IsNullOrEmpty(agent.Data.displayName)
                    ? agent.Data.displayName
                    : agent.name;
                if (match.TryAddPlayerPiece(id, name))
                    teamById[id] = agent;
            }
            // 윷 화면 연출 중이면 아이콘이 슬롯에 떨어진 뒤 갱신(슬롯이 먼저 사라지지 않게)
            if (IsOpen && !summonPresenting) HandlePiecesChanged();
        }

        /// <summary>메인 화면 소환 연출(암전 → 요괴 등장)과 같은 느낌을, 윷 화면 안에서 직접
        /// 재현한다 — SummonCeremony는 월드 스페이스 연출이라 윷 화면의 불투명 패널에
        /// 가려져 안 보인다(SummonPopup과 같은 문제). 대신 화면을 어둡게 했다 밝히면서 그
        /// 사이에 요괴를 소환해 "슬롯에 요괴가 들어오는" 느낌만 살린다.</summary>
        IEnumerator SummonCeremonyRoutine(CharacterId target)
        {
            summonPresenting = true;
            CeremonyGate.Begin();
            var dimGo = new GameObject("SummonDim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dimGo.transform.SetParent(root.transform, false);
            Stretch((RectTransform)dimGo.transform);
            dimGo.transform.SetAsLastSibling();
            var dimImg = dimGo.GetComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0.05f, 0f);

            float t = 0f;
            const float dimIn = 0.45f;
            while (t < dimIn)
            {
                t += Time.unscaledDeltaTime;
                // 완전히 새까맣게는 안 하고(0.72) — 소환하기 슬롯이 은은하게 비쳐서 "저기로
                // 떨어진다"는 느낌이 나게. 메인 화면 SummonCeremony와 같은 어둡기.
                dimImg.color = new Color(0f, 0f, 0.05f, Mathf.Lerp(0f, 0.72f, t / dimIn));
                yield return null;
            }

            // 절차(향·스폰·초기화·저장·매치 합류 이벤트)는 CharacterSummon.TrySummon
            var agent = CharacterSummon.TrySummon(target, font);

            // 소환된 요괴 아이콘이 화면 위에서 로스터의 "소환하기" 슬롯 자리로 떨어져 안착하는 연출 —
            // dimGo의 자식으로 붙여서 암전 위에 확실히 보이게 한다(YutMiniGame 쪽에 붙이면
            // 암전 오버레이보다 그리기 순서가 앞서서 안 보였다).
            if (agent != null)
                yield return PlaySummonDrop(dimGo.transform, agent);
            else
                yield return new WaitForSecondsRealtime(0.4f);

            t = 0f;
            const float dimOut = 0.5f;
            while (t < dimOut)
            {
                t += Time.unscaledDeltaTime;
                dimImg.color = new Color(0f, 0f, 0.05f, Mathf.Lerp(0.72f, 0f, t / dimOut));
                yield return null;
            }
            Destroy(dimGo);
            CeremonyGate.End();
            summonPresenting = false;

            if (agent == null)
            {
                ShowNotice("소환에 실패했습니다.", null);
                yield break;
            }

            HandlePiecesChanged();
            GameSaveBridge.SaveFromWorld();
        }

        /// <summary>소환된 요괴 아이콘을 화면 위쪽에서 로스터의 "소환하기" 슬롯 위치까지 떨어뜨린다.
        /// 슬롯 위치를 못 구하면(레이아웃 준비 전 등) 화면 중앙으로 대신 떨어뜨린다.</summary>
        IEnumerator PlaySummonDrop(Transform parent, CharacterAgent summoned)
        {
            var go = new GameObject("SummonDrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(64f, 64f);
            var img = go.GetComponent<Image>();
            var sprite = summoned != null ? CharacterSpawner.FirstSprite(summoned.Data) : null;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
                img.preserveAspect = true;
            }
            else
            {
                img.color = CharacterSummon.PlaceholderColor; // 아트 없을 때 폴백
            }

            Vector3? slotPos = miniGame != null ? miniGame.GetSummonSlotWorldPosition() : null;
            Vector3 targetPos = slotPos ?? rt.position;
            Vector3 startPos = targetPos + new Vector3(0f, 520f, 0f);
            rt.position = startPos;

            const float fall = 0.55f;
            float t = 0f;
            while (t < fall)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / fall);
                float eased = 1f - (1f - u) * (1f - u);
                rt.position = Vector3.Lerp(startPos, targetPos, eased);
                yield return null;
            }

            const float bounce = 0.2f;
            t = 0f;
            while (t < bounce)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / bounce);
                float bob = Mathf.Sin(u * Mathf.PI) * 10f * (1f - u);
                rt.position = targetPos + new Vector3(0f, bob, 0f);
                yield return null;
            }

            rt.position = targetPos;
            yield return new WaitForSecondsRealtime(0.2f);
            Destroy(go);
        }

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

        /// <summary>매치 종료(완주) — 보상은 이번 판에서 완주한 말 전체 수 기준. 4마리를 한 번에
        /// 업고 동시 완주했을 때만 광고 2배 선택.</summary>
        void HandleMatchEnded(int finishStackCount)
        {
            // 이후 나가기(Close)에서 머리 위 만세 연출을 허용하고, 쌓아 둔 재화를 경제에 넣는다.
            allowPostYutLootPresentation = true;
            grantRewardsToEconomyNow = true;
            CommitPendingLootOnFinish();

            miniGame.SetThrowVisible(false);
            miniGame.ClearCandidates();

            int totalFinished = 0;
            if (match != null)
            {
                for (int i = 0; i < match.PlayerPieces.Count; i++)
                    if (match.PlayerPieces[i].Finished) totalFinished++;
            }

            if (challenge.State != null && match != null)
                challenge.TryMarkAllFinishedComplete(match.PlayerPieces.Count, totalFinished, this);

            int stack = Mathf.Max(1, finishStackCount);
            pendingFinishStack = stack;
            // 보상은 이번 골인 스택이 아니라 이번 판에서 완주한 말 전체 수 기준 — 말들이 따로
            // 골인해서 "계속하기"를 거쳤어도 먼저 들어온 말의 몫이 누락되지 않도록 한다.
            pendingFinishCharmCount = Mathf.Max(1, totalFinished);
            GrantFinishRewardAndNotice();
        }

        void OnMatchEndedNoticeOk()
        {
            if (challenge.PendingRewardAfterFinish)
            {
                challenge.PendingRewardAfterFinish = false;
                StartCoroutine(ChallengeRewardThenRestartRoutine());
                return;
            }
            RestartBoardAfterAllFinished();
        }

        IEnumerator ChallengeRewardThenRestartRoutine()
        {
            yield return challenge.PlayRewardRoutine(this);
            RestartBoardAfterAllFinished();
        }

        /// <summary>같은 팀으로 새 매치를 연다. 특수칸·말 위치·획득 내역을 처음부터.</summary>
        void RestartBoardAfterAllFinished()
        {
            var team = new List<(string id, string name)>();
            if (match != null)
            {
                for (int i = 0; i < match.PlayerPieces.Count; i++)
                {
                    var p = match.PlayerPieces[i];
                    team.Add((p.Id, p.DisplayName));
                }
                UnsubscribeMatchEvents();
                match = null;
            }
            else
            {
                foreach (var kv in teamById)
                {
                    string name = kv.Value != null && kv.Value.Data != null && !string.IsNullOrEmpty(kv.Value.Data.displayName)
                        ? kv.Value.Data.displayName
                        : kv.Key;
                    team.Add((kv.Key, name));
                }
            }

            if (team.Count == 0) return;

            BeginMatch(team);
            if (miniGame != null)
            {
                miniGame.ClearPlayLog();
                miniGame.ClearBubbles();
                miniGame.AddPlayLogEntry("새 판이 열렸습니다.");
            }
        }

        string NameFor(string pieceId) =>
            match?.PlayerPieces.FirstOrDefault(p => p.Id == pieceId)?.DisplayName ?? "?";

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
