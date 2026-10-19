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
    /// <summary>매치 단위 생명주기(시작/재개/종료/재시작)와 YutMatch 이벤트 브릿지. (YutScreen 분할 — 본체는 YutScreen.cs)</summary>
    public partial class YutScreen
    {
        protected override void Awake()
        {
            base.Awake();
            CharacterSummon.Summoned += OnCharacterSummoned;
        }

        void Start()
        {
            EnsureBuilt();
            WireRuntimeListeners();
            if (root != null) root.SetActive(false);
            IsOpen = false;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CharacterSummon.Summoned -= OnCharacterSummoned;
            Yoegoe.Core.UiBlockGate.Unregister(this);
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

            // 옥토끼는 NPC화(v1.3) — 말로 뛰지 않는다. 대신 동(東) 완주 줄에 고정으로 앉아 던지기마다 해설만 한다.
            var agents = CharacterAgent.All
                .Where(a => a != null && a.Stats != null && (a.Data == null || a.Data.id != CharacterId.Rabbit))
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
    }
}
