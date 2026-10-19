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
    /// <summary>QA 전용 자동 플레이·보상/도전 재현 도구 (에디터·개발 빌드에만 포함). (YutScreen 분할 — 본체는 YutScreen.cs)</summary>
    public partial class YutScreen
    {
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

        /// <summary>
        /// QA: 실제로 윷을 던지고(연출 포함) 말을 고르며 진행한다.
        /// stopAtStack &gt; 0 이면 그 수 이상 업고 골인했을 때 "그만"으로 보상 확인.
        /// 0 이면 매치가 끝날 때까지(전원 완주 등) 계속.
        /// </summary>
        public void DebugStartAutoPlay(int stopAtStack = 0)
        {
            EnsureBuilt();
            if (root == null || miniGame == null)
            {
                Debug.LogWarning("[Yut QA] YutScreen 셸이 없습니다.");
                return;
            }

            DebugStopAutoPlay();

            // QA는 항상 새 매치에서 던지기를 본다(끝난 매치·애매한 턴 상태 방지).
            if (match != null)
            {
                UnsubscribeMatchEvents();
                match = null;
            }

            int need = Mathf.Max(1, stopAtStack);
            if (!EnsureMatchReadyForFinishSim(need))
                return;

            ClearBlockingUiForFinishSim();
            miniGame.BindFromHierarchy();
            HandlePiecesChanged();
            root.SetActive(true);
            miniGame.Show();
            miniGame.SetLeaveVisible(true);
            miniGame.SetThrowVisible(true);

            autoPlayStopAtStack = Mathf.Max(0, stopAtStack);
            autoPlayStopAtSquareReward = false;
            autoPlayStopAtChallengeReward = false;
            autoPlayStopAtImugiCapture = false;
            autoPlayImugiCaptureReached = false;
            debugForcedThrowRepeat = null;
            autoPlayRunning = true;
            autoPlayRoutine = StartCoroutine(AutoPlayRoutine());
            Debug.Log(autoPlayStopAtStack > 0
                ? $"[Yut QA] 자동 플레이 시작 — 업고 골인 ×{autoPlayStopAtStack}이면 그만 (말 {match.PlayerPieces.Count}개)"
                : $"[Yut QA] 자동 플레이 시작 — 매치 종료까지 (말 {match.PlayerPieces.Count}개, throwVisible={miniGame.IsThrowVisible})");
        }

        /// <summary>QA: 지정 특수칸을 밟아 Presenter 보상 UI(받기/광고)까지 자동으로 간 뒤 멈춘다.
        /// 보물상자는 내용 확인 notice를 먼저 넘긴다.</summary>
        public void DebugStartSquareRewardQa(YutBoardLayout.SpecialSquareKind kind)
        {
            if (kind == YutBoardLayout.SpecialSquareKind.None)
            {
                Debug.LogWarning("[Yut QA] 특수칸 종류가 None입니다.");
                return;
            }

            EnsureBuilt();
            if (root == null || miniGame == null)
            {
                Debug.LogWarning("[Yut QA] YutScreen 셸이 없습니다.");
                return;
            }

            DebugStopAutoPlay();

            if (match != null)
            {
                UnsubscribeMatchEvents();
                match = null;
            }

            if (!EnsureMatchReadyForFinishSim(1))
                return;

            if (!SetupBoardForSquareRewardQa(kind))
            {
                Debug.LogWarning($"[Yut QA] 특수칸 QA 보드 준비 실패 ({kind}).");
                return;
            }

            ClearBlockingUiForFinishSim();
            miniGame.BindFromHierarchy();
            HandlePiecesChanged();
            ApplySpecialSquareVisuals();
            root.SetActive(true);
            miniGame.Show();
            miniGame.SetLeaveVisible(true);
            miniGame.SetThrowVisible(true);

            autoPlayStopAtStack = 0;
            autoPlayStopAtSquareReward = true;
            autoPlayStopAtChallengeReward = false;
            autoPlayStopAtImugiCapture = false;
            autoPlayImugiCaptureReached = false;
            debugForcedThrow = YutThrowResult.Do;
            debugForcedThrowRepeat = null;
            autoPlayRunning = true;
            autoPlayRoutine = StartCoroutine(AutoPlayRoutine());
            Debug.Log($"[Yut QA] 특수칸 Presenter ({SquareRewardQaLabel(kind)}) — 받기/광고 선택까지 자동 진행 후 정지");
        }

        static string SquareRewardQaLabel(YutBoardLayout.SpecialSquareKind kind) => kind switch
        {
            YutBoardLayout.SpecialSquareKind.Coin => "엽전",
            YutBoardLayout.SpecialSquareKind.IngredientBag => "재료보따리",
            YutBoardLayout.SpecialSquareKind.Treasure => "보물상자",
            YutBoardLayout.SpecialSquareKind.Water => "물",
            _ => kind.ToString(),
        };

        /// <summary>QA: 지정 도전과제를 완료해 보물상자×3 안내까지 자동으로 간 뒤 멈춘다.</summary>
        public void DebugStartChallengeQa(YutChallengeKind kind)
        {
            EnsureBuilt();
            if (root == null || miniGame == null)
            {
                Debug.LogWarning("[Yut QA] YutScreen 셸이 없습니다.");
                return;
            }

            DebugStopAutoPlay();

            if (match != null)
            {
                UnsubscribeMatchEvents();
                match = null;
            }

            int needPieces = kind == YutChallengeKind.FinishAllUncaptured
                ? YutRewards.ChallengeFinishAllPieceCount
                : 1;
            if (!EnsureMatchReadyForFinishSim(needPieces))
                return;

            if (!SetupBoardForChallengeQa(kind))
            {
                Debug.LogWarning($"[Yut QA] 도전과제 QA 보드 준비 실패 ({kind}).");
                return;
            }

            challenge.Restore(kind, completed: false, failed: false, streak: 0, this);

            ClearBlockingUiForFinishSim();
            miniGame.BindFromHierarchy();
            HandlePiecesChanged();
            ApplySpecialSquareVisuals();
            challenge.RefreshBanner(this);
            root.SetActive(true);
            miniGame.Show();
            miniGame.SetLeaveVisible(true);
            miniGame.SetThrowVisible(true);

            autoPlayStopAtStack = 0;
            autoPlayStopAtSquareReward = false;
            autoPlayStopAtChallengeReward = true;
            autoPlayStopAtImugiCapture = false;
            autoPlayImugiCaptureReached = false;
            debugForcedThrow = null;
            debugForcedThrowRepeat = kind switch
            {
                YutChallengeKind.ConsecutiveMo => YutThrowResult.Mo,
                YutChallengeKind.ConsecutiveBaekdo => YutThrowResult.Baekdo,
                YutChallengeKind.FinishAllUncaptured => null,
                _ => null,
            };
            if (kind == YutChallengeKind.FinishAllUncaptured)
                debugForcedThrow = YutThrowResult.Do;

            autoPlayRunning = true;
            autoPlayRoutine = StartCoroutine(AutoPlayRoutine());
            Debug.Log($"[Yut QA] 도전과제 Presenter ({ChallengeQaLabel(kind)}) — 보물상자×3 안내까지 자동 진행 후 정지");
        }

        static string ChallengeQaLabel(YutChallengeKind kind) => kind switch
        {
            YutChallengeKind.ConsecutiveMo => $"모 {YutRewards.ChallengeConsecutiveNeeded}연속",
            YutChallengeKind.ConsecutiveBaekdo => $"빽도 {YutRewards.ChallengeConsecutiveNeeded}연속",
            YutChallengeKind.FinishAllUncaptured => "미잡힘 넷 완주",
            _ => kind.ToString(),
        };

        /// <summary>도전 QA용 보드. 연속 모/빽도는 대기 말+이무기 멀리.
        /// 미잡힘 완주는 말 넷을 참에 업어 두고 한 수로 전원 완주.</summary>
        bool SetupBoardForChallengeQa(YutChallengeKind kind)
        {
            if (match == null || match.PlayerPieces.Count == 0) return false;

            // 특수칸이 경로를 가리지 않게 멀리.
            var kinds = new Dictionary<int, YutBoardLayout.SpecialSquareKind>
            {
                { 8, YutBoardLayout.SpecialSquareKind.Coin },
                { 9, YutBoardLayout.SpecialSquareKind.Coin },
                { 12, YutBoardLayout.SpecialSquareKind.IngredientBag },
                { 14, YutBoardLayout.SpecialSquareKind.Treasure },
                { 16, YutBoardLayout.SpecialSquareKind.Water },
            };
            YutBoardLayout.RestoreSpecialSquares(kinds);

            match.OpponentPiece.NodeId = YutBoardLayout.JjiMo;
            match.OpponentPiece.Finished = false;
            match.OpponentPiece.History.Clear();
            match.OpponentPiece.History.Add(YutBoardLayout.JjiMo);

            if (kind == YutChallengeKind.FinishAllUncaptured)
            {
                if (match.PlayerPieces.Count < YutRewards.ChallengeFinishAllPieceCount)
                    return false;

                // 참에 올라와 있는 말은 다음 던지기로 바로 완주 — 넷을 업어 한 번에.
                for (int i = 0; i < match.PlayerPieces.Count; i++)
                {
                    var p = match.PlayerPieces[i];
                    p.NodeId = YutBoardLayout.Start;
                    p.Finished = false;
                    p.History.Clear();
                    p.History.Add(YutBoardLayout.Bang);
                    p.History.Add(YutBoardLayout.Start);
                }
            }
            else
            {
                for (int i = 0; i < match.PlayerPieces.Count; i++)
                {
                    var p = match.PlayerPieces[i];
                    p.NodeId = -1;
                    p.Finished = false;
                    p.History.Clear();
                }
            }

            GameSaveBridge.SaveFromWorld();
            return true;
        }

        /// <summary>QA: 이무기를 잡아 참 아래 대기 배치까지 자동으로 간 뒤 멈춘다.</summary>
        public void DebugStartCaptureImugiQa()
        {
            EnsureBuilt();
            if (root == null || miniGame == null)
            {
                Debug.LogWarning("[Yut QA] YutScreen 셸이 없습니다.");
                return;
            }

            DebugStopAutoPlay();

            if (match != null)
            {
                UnsubscribeMatchEvents();
                match = null;
            }

            if (!EnsureMatchReadyForFinishSim(1))
                return;

            if (!SetupBoardForCaptureImugiQa())
            {
                Debug.LogWarning("[Yut QA] 이무기 잡기 QA 보드 준비 실패.");
                return;
            }

            ClearBlockingUiForFinishSim();
            miniGame.BindFromHierarchy();
            HandlePiecesChanged();
            ApplySpecialSquareVisuals();
            root.SetActive(true);
            miniGame.Show();
            miniGame.SetLeaveVisible(true);
            miniGame.SetThrowVisible(true);

            autoPlayStopAtStack = 0;
            autoPlayStopAtSquareReward = false;
            autoPlayStopAtChallengeReward = false;
            autoPlayStopAtImugiCapture = true;
            autoPlayImugiCaptureReached = false;
            debugForcedThrow = YutThrowResult.Do;
            debugForcedThrowRepeat = null;
            autoPlayRunning = true;
            autoPlayRoutine = StartCoroutine(AutoPlayRoutine());
            Debug.Log("[Yut QA] 이무기 잡기→참 아래 대기까지 자동 진행");
        }

        /// <summary>말 1을 노드2, 이무기를 노드3에 두고 도(Do)로 잡아 참 아래 대기로 보낸다.</summary>
        bool SetupBoardForCaptureImugiQa()
        {
            if (match == null || match.PlayerPieces.Count == 0) return false;

            const int playerNode = 2;
            const int imugiNode = 3;

            // 특수 칸이 잡기 칸을 가리지 않게 멀리.
            var kinds = new Dictionary<int, YutBoardLayout.SpecialSquareKind>
            {
                { 8, YutBoardLayout.SpecialSquareKind.Coin },
                { 9, YutBoardLayout.SpecialSquareKind.Coin },
                { 12, YutBoardLayout.SpecialSquareKind.IngredientBag },
                { 14, YutBoardLayout.SpecialSquareKind.Treasure },
                { 16, YutBoardLayout.SpecialSquareKind.Water },
            };
            YutBoardLayout.RestoreSpecialSquares(kinds);

            var piece = match.PlayerPieces[0];
            piece.NodeId = playerNode;
            piece.Finished = false;
            piece.History.Clear();
            piece.History.Add(YutBoardLayout.Start);
            piece.History.Add(1);
            piece.History.Add(playerNode);

            match.OpponentPiece.NodeId = imugiNode;
            match.OpponentPiece.Finished = false;
            match.OpponentPiece.History.Clear();
            match.OpponentPiece.History.Add(YutBoardLayout.Start);
            match.OpponentPiece.History.Add(1);
            match.OpponentPiece.History.Add(2);
            match.OpponentPiece.History.Add(imugiNode);

            GameSaveBridge.SaveFromWorld();
            return true;
        }

        /// <summary>말 1을 노드1에 두고, 노드2에 지정 특수칸을 놓아 도(Do) 한 수로 밟게 한다.</summary>
        bool SetupBoardForSquareRewardQa(YutBoardLayout.SpecialSquareKind targetKind)
        {
            if (match == null || match.PlayerPieces.Count == 0) return false;
            if (targetKind == YutBoardLayout.SpecialSquareKind.None) return false;

            const int approachNode = 1;
            const int targetNode = 2;

            var kinds = new Dictionary<int, YutBoardLayout.SpecialSquareKind>
            {
                { targetNode, targetKind },
            };
            // 나머지 특수 칸은 경로를 가리지 않게 멀리(이름 있는 칸·참 제외) 유지.
            int[] extras = { 8, 9, 11, 12, 13, 14, 16, 17 };
            var extraKinds = new[]
            {
                YutBoardLayout.SpecialSquareKind.Coin,
                YutBoardLayout.SpecialSquareKind.Coin,
                YutBoardLayout.SpecialSquareKind.IngredientBag,
                YutBoardLayout.SpecialSquareKind.Treasure,
                YutBoardLayout.SpecialSquareKind.Water,
            };
            for (int i = 0; i < extraKinds.Length && i < extras.Length; i++)
            {
                if (extras[i] == targetNode || extras[i] == approachNode) continue;
                // 목표 종류와 겹치면 멀리 둔 칸도 다른 종류로 바꿔 헷갈리지 않게.
                var kind = extraKinds[i] == targetKind
                    ? YutBoardLayout.SpecialSquareKind.Coin
                    : extraKinds[i];
                if (kind == targetKind)
                    kind = YutBoardLayout.SpecialSquareKind.IngredientBag;
                kinds[extras[i]] = kind;
            }

            YutBoardLayout.RestoreSpecialSquares(kinds);

            var piece = match.PlayerPieces[0];
            piece.NodeId = approachNode;
            piece.Finished = false;
            piece.History.Clear();
            piece.History.Add(YutBoardLayout.Start);
            piece.History.Add(approachNode);

            // 이무기는 방해하지 않게 멀리.
            match.OpponentPiece.NodeId = YutBoardLayout.JjiMo;
            match.OpponentPiece.History.Clear();
            match.OpponentPiece.History.Add(YutBoardLayout.JjiMo);

            GameSaveBridge.SaveFromWorld();
            return true;
        }

        public void DebugStopAutoPlay()
        {
            autoPlayRunning = false;
            autoPlayStopAtSquareReward = false;
            autoPlayStopAtChallengeReward = false;
            autoPlayStopAtImugiCapture = false;
            autoPlayImugiCaptureReached = false;
            debugForcedThrow = null;
            debugForcedThrowRepeat = null;
            if (autoPlayRoutine != null)
            {
                StopCoroutine(autoPlayRoutine);
                autoPlayRoutine = null;
            }
        }

        IEnumerator AutoPlayRoutine()
        {
            // 한 프레임 기다려 보드/던지기 UI가 켜진 뒤 시작한다.
            yield return null;

            float startedAt = Time.unscaledTime;
            int steps = 0;

            while (autoPlayRunning && match != null && steps++ < 2000)
            {
                if (Time.unscaledTime - startedAt > 600f)
                {
                    Debug.LogWarning("[Yut QA] 자동 플레이 시간 초과");
                    break;
                }

                // 특수칸 Presenter QA: 받기/광고 선택 UI가 뜨면 여기서 멈춘다(유저가 확인).
                if (autoPlayStopAtSquareReward && rewardRoot != null && rewardRoot.activeSelf)
                {
                    Debug.Log("[Yut QA] 특수칸 보상 선택 UI 도착 — 자동 플레이 정지");
                    break;
                }

                // 도전과제 Presenter QA: 보물상자×3 지급 흐름이 시작되면 멈춘다.
                if (autoPlayStopAtChallengeReward && challenge.AwaitingReward)
                {
                    // notice가 뜰 때까지 한두 프레임 기다린다.
                    for (int i = 0; i < 30 && autoPlayRunning && noticeRoot != null && !noticeRoot.activeSelf; i++)
                        yield return null;
                    Debug.Log("[Yut QA] 도전과제 보물상자×3 안내 도착 — 자동 플레이 정지");
                    break;
                }

                // 이무기 잡기 QA: 참 아래 대기 배치까지 끝나면 멈춘다.
                if (autoPlayStopAtImugiCapture && autoPlayImugiCaptureReached)
                {
                    yield return null;
                    yield return new WaitForSecondsRealtime(0.35f);
                    Debug.Log("[Yut QA] 이무기 잡힘→참 아래 대기 도착 — 자동 플레이 정지");
                    break;
                }

                // 안내/보상/골인선택/특수칸/되살리기/확인 — 전부 자동으로 넘긴다.
                if (TryAutoDismissPopups())
                {
                    yield return new WaitForSecondsRealtime(0.2f);
                    continue;
                }

                if (match.IsEnded)
                    break;

                if (pendingOutcome != null)
                {
                    var candidates = match.GetPlayerCandidates(pendingOutcome.Value.Result);
                    if (candidates.Count == 0)
                    {
                        pendingOutcome = null;
                        yield return null;
                        continue;
                    }

                    var best = PickAutoPlayCandidate(candidates);
                    // 홉 연출이 끝날 때까지 기다린다(연타/다음 던지기가 겹치지 않게).
                    yield return PlayerMoveRoutine(best.PieceId, best.UseShortcut, pendingOutcome.Value);
                    yield return new WaitForSecondsRealtime(0.25f);
                    continue;
                }

                // 던지기 존이 아직 없으면 바인딩 재시도.
                if (!miniGame.IsThrowVisible)
                {
                    miniGame.BindFromHierarchy();
                    miniGame.SetThrowVisible(true);
                    if (!miniGame.IsThrowVisible)
                    {
                        yield return null;
                        continue;
                    }
                }

                HandleThrowPressed(1f);

                float wait = 0f;
                while (autoPlayRunning && match != null && !match.IsEnded && wait < 25f)
                {
                    if (pendingOutcome != null) break;
                    if (HasBlockingPopup()) break;
                    if (autoPlayStopAtSquareReward && rewardRoot != null && rewardRoot.activeSelf)
                        break;
                    if (autoPlayStopAtChallengeReward && challenge.AwaitingReward)
                        break;
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                    if (pendingOutcome != null || HasBlockingPopup())
                        break;
                    if (autoPlayStopAtSquareReward && rewardRoot != null && rewardRoot.activeSelf)
                        break;
                    if (autoPlayStopAtChallengeReward && challenge.AwaitingReward)
                        break;
                    // 이무기 턴이 끝나고 다시 던질 수 있으면 바깥 루프로.
                    if (miniGame.IsThrowVisible && pendingOutcome == null && wait > 0.6f)
                        break;
                }
            }

            // 특수칸/도전 선택·안내 UI에서 멈춘 경우엔 팝업을 닫지 않는다.
            bool stopOnSquare = autoPlayStopAtSquareReward && rewardRoot != null && rewardRoot.activeSelf;
            bool stopOnChallenge = autoPlayStopAtChallengeReward && challenge.AwaitingReward;
            if (!stopOnSquare && !stopOnChallenge)
            {
                for (int i = 0; i < 10 && autoPlayRunning && TryAutoDismissPopups(); i++)
                    yield return new WaitForSecondsRealtime(0.15f);
            }

            string reason = !autoPlayRunning ? "stop"
                : stopOnSquare ? "square reward UI"
                : stopOnChallenge ? "challenge reward"
                : autoPlayStopAtImugiCapture && autoPlayImugiCaptureReached ? "imugi captured waiting"
                : match == null ? "match=null"
                : match != null && match.IsEnded ? "match ended"
                : "step limit";
            autoPlayRunning = false;
            autoPlayRoutine = null;
            debugForcedThrowRepeat = null;
            Debug.Log($"[Yut QA] 자동 플레이 종료 ({reason})");
        }

        bool HasBlockingPopup()
        {
            if (squareRewards.Awaiting || awaitingFinishChoice || awaitingReviveChoice || challenge.AwaitingReward) return true;
            if (noticeRoot != null && noticeRoot.activeSelf) return true;
            if (rewardRoot != null && rewardRoot.activeSelf) return true;
            if (choiceRoot != null && choiceRoot.activeSelf) return true;
            if (reviveRoot != null && reviveRoot.activeSelf) return true;
            if (ConfirmPopup.Instance != null && ConfirmPopup.Instance.IsOpen) return true;
            return false;
        }

        /// <summary>자동 플레이용 — 팝업은 전부 그냥 받기/계속/확인으로 넘긴다(광고는 스킵).
        /// 특수칸 Presenter QA는 내용 확인(notice)만 넘기고 받기/광고는 남긴다.
        /// 도전과제 QA는 완주 안내는 넘기고, 보물상자×3 안내(AwaitingReward)는 남긴다.</summary>
        bool TryAutoDismissPopups()
        {
            if (autoPlayStopAtSquareReward && rewardRoot != null && rewardRoot.activeSelf)
                return false;

            if (autoPlayStopAtChallengeReward && challenge.AwaitingReward)
                return false;

            if (noticeRoot != null && noticeRoot.activeSelf)
            {
                OnNoticeOk();
                return true;
            }

            if (rewardRoot != null && rewardRoot.activeSelf)
            {
                OnRewardPlainClicked();
                return true;
            }

            if (squareRewards.Awaiting)
            {
                // 보상 선택 패널이 떠 있으면 그냥 받기.
                if (rewardRoot != null && rewardRoot.activeSelf)
                    OnRewardPlainClicked();
                else if (!autoPlayStopAtSquareReward)
                    squareRewards.GrantPlain(this);
                else
                    return false;
                return true;
            }

            if (awaitingReviveChoice || (reviveRoot != null && reviveRoot.activeSelf))
            {
                if (reviveRoot != null) reviveRoot.SetActive(false);
                pendingReviveYes = null;
                pendingReviveNo = null;
                HandleReviveNo();
                return true;
            }

            if (awaitingFinishChoice || (choiceRoot != null && choiceRoot.activeSelf))
            {
                if (choiceRoot != null) choiceRoot.SetActive(false);
                pendingChoiceContinue = null;
                pendingChoiceStop = null;
                if (autoPlayStopAtStack > 0 && lastFinishEventStack >= autoPlayStopAtStack)
                    HandleStopAfterFinish(lastFinishedIds);
                else
                    HandleContinueAfterFinish();
                return true;
            }

            if (ConfirmPopup.Instance != null && ConfirmPopup.Instance.IsOpen)
            {
                ConfirmPopup.Dismiss();
                return true;
            }

            return false;
        }

        YutMatch.YutMoveCandidate PickAutoPlayCandidate(IReadOnlyList<YutMatch.YutMoveCandidate> candidates)
        {
            YutMatch.YutMoveCandidate best = candidates[0];
            int bestScore = int.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                int score = ScoreAutoPlayCandidate(candidates[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidates[i];
                }
            }
            return best;
        }

        int ScoreAutoPlayCandidate(YutMatch.YutMoveCandidate c)
        {
            int score = 0;
            var piece = match.PlayerPieces.FirstOrDefault(p => p.Id == c.PieceId);
            int stackHere = 0;
            if (piece != null && piece.OnBoard)
            {
                for (int i = 0; i < match.PlayerPieces.Count; i++)
                {
                    var p = match.PlayerPieces[i];
                    if (!p.Finished && p.NodeId == piece.NodeId) stackHere++;
                }
            }

            if (c.WillFinish)
            {
                score += 1000;
                score += stackHere * 50;
                if (autoPlayStopAtStack > 0 && stackHere >= autoPlayStopAtStack)
                    score += 500;
            }

            var ally = match.PlayerPieces.FirstOrDefault(
                p => !p.Finished && p.Id != c.PieceId && p.OnBoard && p.NodeId == c.DestinationNode);
            if (ally != null) score += 400;

            if (match.OpponentPiece.OnBoard && match.OpponentPiece.NodeId == c.DestinationNode)
                score += autoPlayStopAtImugiCapture ? 5000 : 300;

            if (c.UseShortcut) score += 80;
            if (YutBoardLayout.IsSpecialReward(c.DestinationNode))
                score += autoPlayStopAtSquareReward ? 5000 : 40;
            if (c.DestinationNode == YutBoardLayout.Start && !c.WillFinish) score += 60;

            return score;
        }

        bool EnsureMatchReadyForFinishSim(int stackCount)
        {
            if (match != null && match.IsEnded)
            {
                UnsubscribeMatchEvents();
                match = null;
            }

            if (match == null)
            {
                var team = new List<(string id, string name)>();
                if (teamById.Count > 0)
                {
                    foreach (var kv in teamById)
                    {
                        string name = kv.Value != null && kv.Value.Data != null && !string.IsNullOrEmpty(kv.Value.Data.displayName)
                            ? kv.Value.Data.displayName
                            : kv.Key;
                        team.Add((kv.Key, name));
                    }
                }
                else
                {
                    foreach (var a in CharacterAgent.All)
                    {
                        if (a == null || a.Stats == null) continue;
                        string id = a.Data != null ? a.Data.id.ToString() : a.name;
                        string name = a.Data != null && !string.IsNullOrEmpty(a.Data.displayName) ? a.Data.displayName : a.name;
                        teamById[id] = a;
                        team.Add((id, name));
                    }
                }

                while (team.Count < stackCount)
                {
                    string id = $"qa_sim_{team.Count}";
                    team.Add((id, $"QA{team.Count + 1}"));
                }

                if (team.Count == 0)
                {
                    Debug.LogWarning("[Yut QA] 시뮬할 말이 없습니다.");
                    return false;
                }

                BeginMatch(team);
            }
            else
            {
                while (match.PlayerPieces.Count < stackCount)
                {
                    int i = match.PlayerPieces.Count;
                    match.TryAddPlayerPiece($"qa_sim_{i}", $"QA{i + 1}");
                }
            }

            return match != null && !match.IsEnded;
        }

        void ClearBlockingUiForFinishSim()
        {
            awaitingFinishChoice = false;
            squareRewards.Clear();
            awaitingReviveChoice = false;
            challenge.ClearBlockingFlags();
            pendingOpponentLappedFx = false;
            pendingBonusAfterContinue = false;
            pendingOutcome = null;
            if (noticeRoot != null) noticeRoot.SetActive(false);
            if (choiceRoot != null) choiceRoot.SetActive(false);
            if (rewardRoot != null) rewardRoot.SetActive(false);
            if (reviveRoot != null) reviveRoot.SetActive(false);
            ConfirmPopup.Dismiss();
            if (miniGame != null) miniGame.ClearCandidates();
        }
#endif
    }
}
