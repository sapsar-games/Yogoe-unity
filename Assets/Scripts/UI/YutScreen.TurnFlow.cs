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
    /// <summary>턴 단위 게임 루프 — 던지기·말풍선·이동·골인 선택·이무기 턴 진행. (YutScreen 분할 — 본체는 YutScreen.cs)</summary>
    public partial class YutScreen
    {
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
    }
}
