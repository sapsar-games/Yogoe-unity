using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.UI;

namespace Yoegoe.Minigames.Yut
{
    /// <summary>이동 후보 표시·칸 강조·드래그 드롭 판정. (YutMiniGame 분할 — 본체는 YutMiniGame.cs)</summary>
    public partial class YutMiniGame
    {
        /// <summary>던진 결과로 움직일 수 있는 말 후보를 보드 위에 직접 보여준다. 후보가 한 마리면
        /// 그 칸 위에 바로, 여럿이 같은 칸으로 겹치면 그 칸 둘레(상하좌우)에 하나씩 붙여서 —
        /// 다이얼로그 없이 보드만 보고 원하는 말을 탭해서 고르게 한다.
        /// 업힌 스택이면 대표 한 마리가 아니라 스택 전원 초상을 칸에 나란히 그린다.</summary>
        public void FlashCandidates(IReadOnlyList<YokaiMoveCandidate> candidates)
        {
            ClearCandidates();
            EnsureBoard();
            if (_pads == null || _pads.Length == 0 || candidates == null || candidates.Count == 0) return;

            var byNode = new Dictionary<int, List<YokaiMoveCandidate>>();
            foreach (var c in candidates)
            {
                // 날 경우(완주)는 참먹이가 아니라 동 구역을 도착지로 보여준다.
                int node = c.WillFinish
                    ? FinishCandidateSlot
                    : Mathf.Clamp(c.DestinationNode, 0, _pads.Length - 1);
                if (!byNode.TryGetValue(node, out var list))
                {
                    list = new List<YokaiMoveCandidate>();
                    byNode[node] = list;
                }
                list.Add(c);
            }

            foreach (var kv in byNode)
            {
                if (kv.Key == FinishCandidateSlot)
                {
                    if (kv.Value.Count == 1)
                        _candidateMarkers.Add(BuildCandidateOnEast(kv.Value[0]));
                    else
                        _candidateMarkers.AddRange(BuildCandidateCrossOnEast(kv.Value));
                    HighlightEastFinish();
                    continue;
                }

                if (kv.Value.Count == 1)
                    _candidateMarkers.Add(BuildCandidateOnNode(kv.Key, kv.Value[0]));
                else
                    _candidateMarkers.AddRange(BuildCandidateCross(kv.Key, kv.Value));
                HighlightPad(kv.Key);
            }
        }

        /// <summary>FlashCandidates로 띄운 후보 마커를 전부 지운다.</summary>
        public void ClearCandidates()
        {
            foreach (var go in _candidateMarkers)
                if (go != null) Destroy(go);
            _candidateMarkers.Clear();
            _candidateHits.Clear();
            ClearPadHighlights();
            RestoreShiftedOccupants();
        }

        /// <summary>후보 미리보기 때문에 옆으로 비켜 놓았던 실제 말들을 원래 자리(칸 중앙/정상 스택)로 되돌린다.</summary>
        void RestoreShiftedOccupants()
        {
            if (_shiftedOccupantNodes.Count == 0) return;
            bool prevSuppress = _suppressPieceSlide;
            _suppressPieceSlide = true;
            try
            {
                foreach (var nodeId in _shiftedOccupantNodes)
                {
                    var occupants = RealOccupantsAtNode(nodeId, null);
                    if (occupants.Count > 0)
                        PlaceGroupOnNode(occupants, nodeId);
                }
            }
            finally
            {
                _suppressPieceSlide = prevSuppress;
            }
            _shiftedOccupantNodes.Clear();
        }

        void HighlightPad(int nodeId)
        {
            if (_pads == null || nodeId < 0 || nodeId >= _pads.Length || _pads[nodeId] == null) return;
            if (!_highlightedPadOriginals.ContainsKey(nodeId))
                _highlightedPadOriginals[nodeId] = _pads[nodeId].color;
            EnsurePadHighlightRoutine();
        }

        /// <summary>완주(날 경우) 도착지 — 동 구역 하단 절반(완주 말 자리)만 노란빛으로 펄스.</summary>
        void HighlightEastFinish()
        {
            var east = GetQuadrant(YutBoardQuadrant.East);
            EnsureFinishedPiecesRoot(east);
            var img = ((RectTransform)_finishedPiecesRoot).GetComponent<Image>();
            if (img == null) return;
            if (!_eastFinishHighlightActive)
            {
                _eastFinishHighlight = img;
                _eastFinishHighlightOriginal = img.color;
                _eastFinishHighlightActive = true;
            }
            EnsurePadHighlightRoutine();
        }

        void EnsurePadHighlightRoutine()
        {
            if (!_padHighlightRunning)
                StartCoroutine(PadHighlightRoutine());
        }

        void ClearPadHighlights()
        {
            foreach (var kv in _highlightedPadOriginals)
                if (_pads != null && kv.Key < _pads.Length && _pads[kv.Key] != null)
                    _pads[kv.Key].color = kv.Value;
            _highlightedPadOriginals.Clear();

            if (_eastFinishHighlightActive && _eastFinishHighlight != null)
                _eastFinishHighlight.color = _eastFinishHighlightOriginal;
            _eastFinishHighlightActive = false;
            _eastFinishHighlight = null;
        }

        /// <summary>갈 수 있는 칸(또는 완주 시 동 구역)을 노란빛으로 은은하게 펄스시킨다.</summary>
        IEnumerator PadHighlightRoutine()
        {
            _padHighlightRunning = true;
            var glow = new Color(1f, 0.92f, 0.35f, 1f);
            var eastGlow = new Color(1f, 0.92f, 0.35f, 0.55f);
            while (_highlightedPadOriginals.Count > 0 || _eastFinishHighlightActive)
            {
                float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
                foreach (var nodeId in _highlightedPadOriginals.Keys)
                {
                    if (_pads == null || nodeId >= _pads.Length || _pads[nodeId] == null) continue;
                    _pads[nodeId].color = Color.Lerp(_highlightedPadOriginals[nodeId], glow, pulse);
                }
                if (_eastFinishHighlightActive && _eastFinishHighlight != null)
                    _eastFinishHighlight.color = Color.Lerp(_eastFinishHighlightOriginal, eastGlow, pulse);
                yield return null;
            }
            _padHighlightRunning = false;
        }

        /// <summary>
        /// 말 아이콘을 드래그해서 놓았을 때(YutPieceDragHandle) 호출된다. 지금 보드 위에 떠 있는
        /// 후보 마커 중 이 말 것이면서 놓은 지점과 겹치는 게 있으면 그 후보를 고른 것으로 처리한다
        /// (탭했을 때와 동일하게 OnCandidateTapped를 쏨). 겹치는 후보가 없으면 조용히 무시.
        /// 업힌 스택이면 어느 멤버를 끌어도 그 스택 후보로 인정한다.
        /// </summary>
        public void ResolveDrop(string pieceId, Vector2 screenPos)
        {
            foreach (var hit in _candidateHits)
            {
                if (hit.rect == null) continue;
                if (!CandidateHitMatches(hit, pieceId)) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(hit.rect, screenPos, null))
                {
                    OnCandidateTapped?.Invoke(hit.pieceId, hit.useShortcut);
                    return;
                }
            }
        }

        static bool CandidateHitMatches((RectTransform rect, string pieceId, bool useShortcut, string[] stackMemberIds) hit, string pieceId)
        {
            if (hit.pieceId == pieceId) return true;
            if (hit.stackMemberIds == null) return false;
            for (int i = 0; i < hit.stackMemberIds.Length; i++)
                if (hit.stackMemberIds[i] == pieceId) return true;
            return false;
        }

        GameObject BuildCandidateOnNode(int nodeId, YokaiMoveCandidate candidate)
        {
            var go = new GameObject($"Candidate_{nodeId}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_pads[nodeId].transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, 0.05f);
            rt.anchorMax = new Vector2(0.95f, 0.95f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var hitImg = go.GetComponent<Image>();
            hitImg.color = new Color(1f, 1f, 1f, 0.01f); // 투명에 가깝지만 Button 레이캐스트용
            var padSize = _pads[nodeId].rectTransform.rect.size;
            var stackArea = new Vector2(padSize.x * 0.9f, padSize.y * 0.9f);

            // 그 칸에 이미 실제로 있는 말이 있으면 후보 미리보기가 겹쳐서 가리지 않도록,
            // 업은 것처럼 옆으로 나란히 배치한다 — 실제 말은 안 깜빡이고(PulseScale 대상 아님),
            // 후보(이 rt)만 깜빡여서 "여기로 갈 수 있다"를 구분한다.
            var occupants = RealOccupantsAtNode(nodeId, candidate.StackMemberIds);
            int candidateCount = candidate.StackMemberIds != null && candidate.StackMemberIds.Length > 0
                ? candidate.StackMemberIds.Length : 1;
            if (occupants.Count > 0)
            {
                int total = occupants.Count + candidateCount;
                float fill = Mathf.Max(0.01f, boardPieceFill);
                float pieceWidth = stackArea.x * fill;
                for (int i = 0; i < occupants.Count; i++)
                    occupants[i].anchoredPosition = SpreadOffset(i, total, pieceWidth, stackedPieceSpread);
                if (!_shiftedOccupantNodes.Contains(nodeId)) _shiftedOccupantNodes.Add(nodeId);
                FillCandidateStackVisuals(rt, candidate, stackArea, occupants.Count, total);
            }
            else
            {
                FillCandidateStackVisuals(rt, candidate, stackArea);
            }
            WireCandidateButton(go, hitImg, candidate.Id, candidate.UseShortcut, candidate.StackMemberIds);
            StartCoroutine(PulseScale(rt));
            return go;
        }

        /// <summary>완주(날 경우) 후보 — 참먹이 대신 동(東) 완주 자리 위에 띄운다.</summary>
        GameObject BuildCandidateOnEast(YokaiMoveCandidate candidate)
        {
            var east = GetQuadrant(YutBoardQuadrant.East);
            EnsureFinishedPiecesRoot(east);
            var go = new GameObject("Candidate_Finish", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            // FinishedPieces는 HorizontalLayoutGroup이라 후보 마커 부모로 쓰면 배치가 깨진다.
            go.transform.SetParent(east, false);
            var rt = go.GetComponent<RectTransform>();
            // 동 하단 절반(완주 말 자리)에만 후보를 올린다.
            rt.anchorMin = new Vector2(0.05f, 0f);
            rt.anchorMax = new Vector2(0.95f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var hitImg = go.GetComponent<Image>();
            hitImg.color = new Color(1f, 1f, 1f, 0.01f);
            var area = rt.rect.size;
            if (area.x < 1f) area = new Vector2(72f, 40f);
            FillCandidateStackVisuals(rt, candidate, new Vector2(area.x * 0.9f, area.y * 0.9f));
            WireCandidateButton(go, hitImg, candidate.Id, candidate.UseShortcut, candidate.StackMemberIds);
            StartCoroutine(PulseScale(rt));
            return go;
        }

        List<GameObject> BuildCandidateCrossOnEast(List<YokaiMoveCandidate> group)
        {
            var result = new List<GameObject>();
            var east = GetQuadrant(YutBoardQuadrant.East);
            EnsureFinishedPiecesRoot(east);
            var area = ((RectTransform)_finishedPiecesRoot).rect.size;
            if (area.x < 1f) area = new Vector2(64f, 40f);
            float step = Mathf.Max(area.x, 36f) * 0.7f;

            for (int i = 0; i < group.Count && i < CrossDirs.Length; i++)
            {
                var candidate = group[i];
                var dir = CrossDirs[i];
                var go = new GameObject($"Candidate_Finish_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(east, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.25f);
                rt.sizeDelta = area;
                rt.anchoredPosition = dir * step;
                var hitImg = go.GetComponent<Image>();
                hitImg.color = new Color(1f, 1f, 1f, 0.01f);
                FillCandidateStackVisuals(rt, candidate, area);
                WireCandidateButton(go, hitImg, candidate.Id, candidate.UseShortcut, candidate.StackMemberIds);
                StartCoroutine(PulseScale(rt));
                result.Add(go);
            }
            return result;
        }

        /// <summary>완주 홉 착지용 — 동 구역 하단(FinishedPieces) 월드 좌표.</summary>
        RectTransform GetFinishCandidateAnchor()
        {
            var east = GetQuadrant(YutBoardQuadrant.East);
            EnsureFinishedPiecesRoot(east);
            return (RectTransform)_finishedPiecesRoot;
        }

        /// <summary>경합 밭(같은 칸으로 갈 수 있는 후보 2마리 이상)을 홀드 없이 처음부터 사방에
        /// 펼쳐서 보여준다 — 각 아이콘은 탭도 되고(WireCandidateButton) 말을 드래그해서 놓아도
        /// 된다(ResolveDrop이 _candidateHits로 판정). 업힌 스택이면 그 방향에 전원 초상.</summary>
        List<GameObject> BuildCandidateCross(int nodeId, List<YokaiMoveCandidate> group)
        {
            var result = new List<GameObject>();
            var pad = _pads[nodeId].rectTransform;
            Vector2 size = pad.anchorMax - pad.anchorMin;

            for (int i = 0; i < group.Count && i < CrossDirs.Length; i++)
            {
                var candidate = group[i];
                var dir = CrossDirs[i];
                var go = new GameObject($"Candidate_{nodeId}_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(_boardRoot, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = pad.anchorMin + Vector2.Scale(dir, size);
                rt.anchorMax = pad.anchorMax + Vector2.Scale(dir, size);
                rt.offsetMin = pad.offsetMin;
                rt.offsetMax = pad.offsetMax;
                var hitImg = go.GetComponent<Image>();
                hitImg.color = new Color(1f, 1f, 1f, 0.01f);
                FillCandidateStackVisuals(rt, candidate, pad.rect.size);
                WireCandidateButton(go, hitImg, candidate.Id, candidate.UseShortcut, candidate.StackMemberIds);
                StartCoroutine(PulseScale(rt));
                result.Add(go);
            }
            return result;
        }

        /// <summary>후보 마커 안에 스택 멤버 초상을 가로로 나란히 깐다(보드 말 PlaceGroupOnNode와 같은 간격 감각).</summary>
        /// <summary>indexOffset/totalOverride는 그 칸에 이미 있는 실제 말과 나란히 배치할 때
        /// (BuildCandidateOnNode) 전체 자리 수·시작 인덱스를 맞추기 위해 쓴다. 기본값이면 후보끼리만 채운다.</summary>
        void FillCandidateStackVisuals(RectTransform parent, YokaiMoveCandidate candidate, Vector2 areaSize,
            int indexOffset = 0, int totalOverride = -1)
        {
            var members = candidate.StackMemberIds;
            var names = candidate.StackDisplayNames;
            int count = members != null ? members.Length : 1;
            if (count <= 0) count = 1;
            int total = totalOverride > 0 ? totalOverride : count;

            float fill = Mathf.Max(0.01f, boardPieceFill);
            float areaW = areaSize.x > 1f ? areaSize.x : 64f;
            float areaH = areaSize.y > 1f ? areaSize.y : 64f;
            var pieceSize = new Vector2(areaW * fill, areaH * fill);

            for (int i = 0; i < count; i++)
            {
                string memberId = members != null && i < members.Length ? members[i] : candidate.Id;
                string name = names != null && i < names.Length ? names[i] : candidate.DisplayName;

                var child = new GameObject($"Stack_{memberId}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                child.transform.SetParent(parent, false);
                var rt = child.GetComponent<RectTransform>();
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = pieceSize;
                rt.anchoredPosition = SpreadOffset(indexOffset + i, total, pieceSize.x, stackedPieceSpread);

                var img = child.GetComponent<Image>();
                img.raycastTarget = false;
                ApplyCandidateMemberVisual(img, memberId, name);
            }
        }

        void ApplyCandidateMemberVisual(Image img, string pieceId, string displayName)
        {
            var sprite = PieceSpriteFor(pieceId);
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
                img.preserveAspect = true;
            }
            else
            {
                img.color = ColorForYokai(pieceId);
                var label = CreateText(img.transform, "Label", InitialOf(displayName), 28, TextAnchor.MiddleCenter);
                UiRectUtils.Stretch(label.rectTransform);
                label.raycastTarget = false;
            }
        }

        void WireCandidateButton(GameObject go, Image img, string tappedId, bool useShortcut, string[] stackMemberIds)
        {
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnCandidateTapped?.Invoke(tappedId, useShortcut));
            _candidateHits.Add((go.GetComponent<RectTransform>(), tappedId, useShortcut, stackMemberIds));
        }

        IEnumerator PulseScale(RectTransform rt)
        {
            while (rt != null)
            {
                float s = 1f + 0.1f * Mathf.Sin(Time.unscaledTime * 4.5f);
                rt.localScale = Vector3.one * s;
                yield return null;
            }
        }

        static bool IsWaypoint(int nodeId) =>
            nodeId == YutBoardLayout.Start || nodeId == YutBoardLayout.Mo ||
            nodeId == YutBoardLayout.DwitMo || nodeId == YutBoardLayout.JjiMo ||
            nodeId == YutBoardLayout.Bang;
    }
}
