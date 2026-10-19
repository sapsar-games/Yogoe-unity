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
    /// <summary>말 배치·홉 이동 연출·말 그림. (YutMiniGame 분할 — 본체는 YutMiniGame.cs)</summary>
    public partial class YutMiniGame
    {
        /// <summary>상대(이무기 등) 말 표시를 켜고 끈다. 켜기 전까지는 판 위에 안 보인다.</summary>
        public void ShowOpponentPiece(bool on)
        {
            EnsureBoard();
            if (_opponentPiece != null) _opponentPiece.gameObject.SetActive(on);
        }

        public void SetOpponentPieceIndex(int nodeId)
        {
            EnsureBoard();
            if (_pads == null || _pads.Length == 0 || _opponentPiece == null) return;

            Vector3 fromPos = _opponentPiece.position;
            nodeId = Mathf.Clamp(nodeId, 0, _pads.Length - 1);
            var pad = _pads[nodeId].rectTransform;
            _opponentPiece.SetParent(pad, false);
            _opponentPiece.anchorMin = new Vector2(0.15f, 0.15f);
            _opponentPiece.anchorMax = new Vector2(0.85f, 0.85f);
            _opponentPiece.offsetMin = Vector2.zero;
            _opponentPiece.offsetMax = Vector2.zero;
            SlideIn(_opponentPiece, fromPos);
        }

        /// <summary>대기(잡히거나 아직 입장 전) 이무기 — 참먹이 칸이 아니라 그 바로 아래에 둔다.</summary>
        public void PlaceOpponentWaitingBelowStart()
        {
            EnsureBoard();
            if (_pads == null || _pads.Length == 0 || _opponentPiece == null) return;

            Vector3 fromPos = _opponentPiece.position;
            var startPad = _pads[YutBoardLayout.Start].rectTransform;
            _opponentPiece.SetParent(startPad, false);
            // 참 패드 기준 아래쪽(음수 y 앵커) — 칸과 겹치지 않게.
            _opponentPiece.anchorMin = new Vector2(0.15f, -1.05f);
            _opponentPiece.anchorMax = new Vector2(0.85f, -0.35f);
            _opponentPiece.offsetMin = Vector2.zero;
            _opponentPiece.offsetMax = Vector2.zero;
            _opponentPiece.localScale = Vector3.one;
            SlideIn(_opponentPiece, fromPos);
        }

        /// <summary>
        /// 본게임 수련장 전용 — 보유 요괴 전체를 각자 위치에 동시에 말로 표시한다.
        /// 목록에 없는 요괴의 말은 정리된다.
        /// 말 아이콘: 캐릭터 CharacterData 스프라이트 / 이무기는 UI/ImugiPortrait (없으면 색+이니셜 폴백).
        /// </summary>
        public void ShowYokaiPieces(IReadOnlyList<YokaiPieceInfo> pieces)
        {
            EnsureBoard();
            if (_pads == null || _pads.Length == 0 || pieces == null) return;

            _lastYokaiPieces = new List<YokaiPieceInfo>(pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
                _lastYokaiPieces.Add(pieces[i]);

            var keep = new HashSet<string>();
            foreach (var info in pieces) keep.Add(info.Id);
            var stale = new List<string>();
            foreach (var kv in _yokaiPieces)
                if (!keep.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                if (_yokaiPieces.TryGetValue(id, out var rt) && rt != null) Destroy(rt.gameObject);
                _yokaiPieces.Remove(id);
                _yokaiPieceLabels.Remove(id);
            }

            var waiting = new List<(string Id, RectTransform Piece)>();
            var byNode = new Dictionary<int, List<RectTransform>>();
            foreach (var info in pieces)
            {
                if (!_yokaiPieces.TryGetValue(info.Id, out var piece) || piece == null)
                {
                    piece = BuildYokaiPieceVisual(info.Id, info.DisplayName, out var label);
                    _yokaiPieces[info.Id] = piece;
                    _yokaiPieceLabels[info.Id] = label;
                }

                if (info.NodeId < 0)
                {
                    waiting.Add((info.Id, piece)); // 로스터 슬롯에 대기
                }
                else
                {
                    if (!byNode.TryGetValue(info.NodeId, out var list))
                    {
                        list = new List<RectTransform>();
                        byNode[info.NodeId] = list;
                    }
                    list.Add(piece);
                }
            }
            // 같은 칸에 업힌 말이 여럿이면 겹쳐 보이지 않게 그 칸 안에서 가로로 나란히 배치.
            foreach (var kv in byNode)
                PlaceGroupOnNode(kv.Value, kv.Key);
            for (int i = 0; i < waiting.Count; i++)
                LayoutWaitingPieceInRosterSlot(waiting[i].Id, waiting[i].Piece);
        }

        void OnValidate()
        {
            // Play 중 Inspector에서 말 크기 필드를 바꾸면 바로 다시 배치한다(슬라이드 연출은 생략).
            if (!Application.isPlaying || _lastYokaiPieces == null || _lastYokaiPieces.Count == 0) return;
            _suppressPieceSlide = true;
            try { ShowYokaiPieces(_lastYokaiPieces); }
            finally { _suppressPieceSlide = false; }
        }

        RectTransform BuildYokaiPieceVisual(string id, string displayName, out Text label)
        {
            var go = new GameObject($"YokaiPiece_{id}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_pads[0].transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.18f, 0.18f); // PlaceOnNode 기본값과 맞춰서, 새로 생긴 말도
            rt.anchorMax = new Vector2(0.82f, 0.82f); // SlideIn의 시작점이 참 중앙으로 잘 정의되게 함
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            var sprite = PieceSpriteFor(id);
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
                img.preserveAspect = true;
                label = null;
            }
            else
            {
                img.color = ColorForYokai(id);
                label = CreateText(go.transform, "Label", InitialOf(displayName), 30, TextAnchor.MiddleCenter);
                UiRectUtils.Stretch(label.rectTransform);
                label.raycastTarget = false;
            }

            // 누르고 후보 칸까지 드래그해서 이동시킬 수 있게 — 후보가 아닐 때 드롭하면 그냥 무시된다.
            var drag = go.AddComponent<YutPieceDragHandle>();
            drag.Owner = this;
            drag.PieceId = id;

            return go.GetComponent<RectTransform>();
        }

        /// <summary>출발 전(또는 잡혀 복귀) 대기말 — 해당 요괴 로스터 슬롯 초상 자리에 말을 둔다.
        /// 예전 South 대기칸은 쓰지 않는다.</summary>
        void LayoutWaitingPieceInRosterSlot(string pieceId, RectTransform piece)
        {
            EnsureRosterPanel();
            if (piece == null || string.IsNullOrEmpty(pieceId)) return;

            RosterChip chip = default;
            bool found = false;
            for (int i = 0; i < _rosterChips.Count; i++)
            {
                if (_rosterChips[i].Id == pieceId)
                {
                    chip = _rosterChips[i];
                    found = true;
                    break;
                }
            }
            if (!found || chip.Root == null || chip.Portrait == null) return;

            Vector3 fromPos = piece.position;
            var portraitRt = chip.Portrait.rectTransform;
            // 초상 Image를 끄더라도 말이 보이도록 칩 루트에 붙이고, 초상과 같은 자리·크기를 맞춘다.
            piece.SetParent(chip.Root, false);
            SetPieceDragEnabled(piece, false); // 대기말은 드래그로 후보에 올리지 않는다

            float inset = Mathf.Clamp(waitingPieceInSlotInset, 0f, 0.45f);
            piece.anchorMin = portraitRt.anchorMin;
            piece.anchorMax = portraitRt.anchorMax;
            piece.pivot = portraitRt.pivot;
            piece.sizeDelta = portraitRt.sizeDelta * (1f - inset * 2f);
            piece.anchoredPosition = portraitRt.anchoredPosition + new Vector2(0f, -portraitRt.sizeDelta.y * inset);
            piece.localScale = Vector3.one;
            piece.SetAsLastSibling();

            // 말이 초상 자리를 차지하므로 슬롯 초상은 끈다(보드 나가면 ShowRoster가 실루엣으로 켠다).
            chip.Portrait.enabled = false;

            SlideIn(piece, fromPos);
        }

        /// <summary>로스터 칩을 다시 만들기 전에, 슬롯에 붙어 있던 대기말이 같이 Destroy되지 않게 떼어 둔다.</summary>
        void DetachYokaiPiecesFromRoster()
        {
            if (_rosterPanel == null || _pads == null || _pads.Length == 0) return;
            var safeParent = _pads[0].rectTransform;
            foreach (var kv in _yokaiPieces)
            {
                var piece = kv.Value;
                if (piece == null) continue;
                if (piece.IsChildOf(_rosterPanel))
                    piece.SetParent(safeParent, true);
            }
        }

        /// <summary>한 칸에 말이 한 마리면 예전처럼 칸 가운데 크게, 업혀서 여럿이면 칸 폭에
        /// 욱여넣어 작아지는 대신 평소 크기를 유지한 채 가로로 겹치며 퍼진다 — 칸 밖으로
        /// 넘쳐도 된다(마스크가 없어 잘리지 않는다).</summary>
        void PlaceGroupOnNode(List<RectTransform> pieces, int nodeId)
        {
            nodeId = Mathf.Clamp(nodeId, 0, _pads.Length - 1);
            var padRt = _pads[nodeId].rectTransform;
            int count = pieces.Count;
            float fill = Mathf.Max(0.01f, boardPieceFill);
            var pieceSize = new Vector2(padRt.rect.width * fill, padRt.rect.height * fill);

            for (int i = 0; i < count; i++)
            {
                var piece = pieces[i];
                Vector3 fromPos = piece.position;
                piece.SetParent(padRt, false);
                SetPieceDragEnabled(piece, true);

                piece.anchorMin = piece.anchorMax = new Vector2(0.5f, 0.5f);
                piece.pivot = new Vector2(0.5f, 0.5f);
                piece.sizeDelta = pieceSize;

                piece.anchoredPosition = SpreadOffset(i, count, pieceSize.x, stackedPieceSpread);

                SlideIn(piece, fromPos);
            }
        }

        /// <summary>한 칸 안에서 나란히 늘어놓을 때 index번째 말의 중심 기준 오프셋(가로).</summary>
        static Vector2 SpreadOffset(int index, int total, float pieceWidth, float spread)
        {
            if (total <= 1) return Vector2.zero;
            float step = pieceWidth * spread; // 서로 살짝 겹치도록 한 칸보다 좁게
            return new Vector2((index - (total - 1) / 2f) * step, 0f);
        }

        /// <summary>지금 실제로 그 칸(nodeId)에 있는 말들의 RectTransform — 후보 미리보기가 겹쳐서
        /// 가리지 않게 옆으로 비켜 놓을 때 쓴다. excludeIds에 든 id(그 후보 자신)는 제외.</summary>
        List<RectTransform> RealOccupantsAtNode(int nodeId, string[] excludeIds)
        {
            var result = new List<RectTransform>();
            if (_lastYokaiPieces == null) return result;
            for (int i = 0; i < _lastYokaiPieces.Count; i++)
            {
                var info = _lastYokaiPieces[i];
                if (info.NodeId != nodeId) continue;
                if (excludeIds != null && System.Array.IndexOf(excludeIds, info.Id) >= 0) continue;
                if (_yokaiPieces.TryGetValue(info.Id, out var rt) && rt != null)
                    result.Add(rt);
            }
            return result;
        }

        static void SetPieceDragEnabled(RectTransform piece, bool on)
        {
            if (piece == null) return;
            var drag = piece.GetComponent<YutPieceDragHandle>();
            if (drag != null) drag.enabled = on;
        }

        /// <summary>
        /// 말이 순간이동하지 않고 눈에 보이게 미끄러지도록 한다 — 이미 새 위치로 배치된 rt.position을
        /// 목표로 잡고 fromPos에서 슬라이드한다. 던질 때마다("모→이동→윷→이동→도→이동") 실제로
        /// 움직이는 게 보여야 보너스 턴이 이어지는 게 자연스럽게 읽힌다.
        /// 칸 단위 홉(PlayHopAlongPath) 직후 재배치할 때는 SetSuppressPieceSlide로 끈다.
        /// </summary>
        void SlideIn(RectTransform rt, Vector3 fromPos)
        {
            if (_suppressPieceSlide) return;
            Vector3 toPos = rt.position;
            if ((toPos - fromPos).sqrMagnitude < 1f) return; // 실질적으로 제자리면 생략
            rt.position = fromPos;
            StartCoroutine(SlideRoutine(rt, fromPos, toPos));
        }

        /// <summary>홉 연출 직후 ShowYokaiPieces가 다시 미끄러지지 않게 끈다.</summary>
        public void SetSuppressPieceSlide(bool on) => _suppressPieceSlide = on;

        /// <summary>선택/이무기 이동 전 — hopNodes 칸을 순서대로 밟는다. finishing이면 마지막에 페이드아웃.</summary>
        public IEnumerator PlayHopAlongPath(IReadOnlyList<string> pieceIds, IReadOnlyList<int> hopNodes, bool finishing)
        {
            EnsureBoard();
            if (pieceIds == null || pieceIds.Count == 0) yield break;

            var pieces = new List<RectTransform>(pieceIds.Count);
            for (int i = 0; i < pieceIds.Count; i++)
            {
                if (_yokaiPieces.TryGetValue(pieceIds[i], out var rt) && rt != null)
                    pieces.Add(rt);
            }
            if (pieces.Count == 0) yield break;

            yield return HopPiecesAlongPath(pieces, hopNodes, finishing);
        }

        /// <summary>이무기 말 한 개용 홉. 대기에서 첫 입장 전이면 호출부가 참에 미리 띄워 둔다.</summary>
        public IEnumerator PlayOpponentHopAlongPath(IReadOnlyList<int> hopNodes)
        {
            EnsureBoard();
            if (_opponentPiece == null || !_opponentPiece.gameObject.activeInHierarchy) yield break;
            var pieces = new List<RectTransform>(1) { _opponentPiece };
            yield return HopPiecesAlongPath(pieces, hopNodes, finishing: false);
        }

        IEnumerator HopPiecesAlongPath(List<RectTransform> pieces, IReadOnlyList<int> hopNodes, bool finishing)
        {
            // 말은 평소엔 칸(Node) 자식이라, 월드 좌표로 옮겨도 형제 칸·사분면 Image 뒤에 깔린다.
            // 홉 연출 동안만 화면 루트로 올려 항상 위에 보이게 한다.
            RaisePiecesForHop(pieces);

            if (hopNodes != null)
            {
                for (int n = 0; n < hopNodes.Count; n++)
                {
                    int nodeId = hopNodes[n];
                    if (_pads == null || nodeId < 0 || nodeId >= _pads.Length || _pads[nodeId] == null)
                        continue;
                    // 완주여도 참먹이는 실제로 밟고, 그다음 동으로 간다(직행하지 않음).
                    yield return HopPiecesTo(pieces, _pads[nodeId].rectTransform.position);
                }
            }

            if (!finishing) yield break;

            // 참을 들른 뒤(또는 이미 참에 서서 홉이 비어 있을 때) 동(東) 완주 자리로.
            yield return HopPiecesTo(pieces, GetFinishWorldPosition());

            var images = new List<Image>(pieces.Count);
            var labels = new List<Text>(pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] == null) continue;
                images.Add(pieces[i].GetComponent<Image>());
                labels.Add(pieces[i].GetComponentInChildren<Text>());
            }

            float fade = 0f;
            while (fade < HopFinishFade)
            {
                fade += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(fade / HopFinishFade);
                for (int i = 0; i < images.Count; i++)
                {
                    if (images[i] != null)
                    {
                        var c = images[i].color;
                        c.a = a;
                        images[i].color = c;
                    }
                    if (i < labels.Count && labels[i] != null)
                    {
                        var c = labels[i].color;
                        c.a = a;
                        labels[i].color = c;
                    }
                }
                yield return null;
            }
        }

        /// <summary>홉 중 말이 칸·사분면에 가려지지 않게 화면 루트 맨 앞으로 올린다.</summary>
        void RaisePiecesForHop(List<RectTransform> pieces)
        {
            if (pieces == null) return;
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (piece == null) continue;
                piece.SetParent(transform, worldPositionStays: true);
                piece.SetAsLastSibling();
            }
        }

        IEnumerator HopPiecesTo(List<RectTransform> pieces, Vector3 target)
        {
            var from = new Vector3[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
                from[i] = pieces[i] != null ? pieces[i].position : target;

            float t = 0f;
            while (t < HopDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / HopDuration);
                float eased = 1f - (1f - u) * (1f - u);
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (pieces[i] == null) continue;
                    pieces[i].position = Vector3.Lerp(from[i], target, eased);
                }
                yield return null;
            }
            for (int i = 0; i < pieces.Count; i++)
                if (pieces[i] != null) pieces[i].position = target;

            yield return new WaitForSecondsRealtime(0.035f);
        }

        Vector3 GetFinishWorldPosition()
        {
            var anchor = GetFinishCandidateAnchor();
            return anchor != null ? anchor.position : Vector3.zero;
        }

        IEnumerator SlideRoutine(RectTransform rt, Vector3 fromPos, Vector3 toPos)
        {
            const float duration = 0.3f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                if (rt == null) yield break;
                float u = Mathf.Clamp01(t / duration);
                float eased = 1f - (1f - u) * (1f - u);
                rt.position = Vector3.Lerp(fromPos, toPos, eased);
                yield return null;
            }
            if (rt != null) rt.position = toPos;
        }

        static string InitialOf(string displayName) =>
            string.IsNullOrEmpty(displayName) ? "?" : displayName.Substring(0, 1);

        /// <summary>
        /// 이무기 → Resources/UI/ImugiPortrait.
        /// 그 외 요괴 → CharacterData(월드 에이전트 / Main 연결 / Gorani Resources) 첫 스프라이트.
        /// </summary>
        static Sprite PieceSpriteFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (PieceSpriteCache.TryGetValue(id, out var cached) && cached != null) return cached;

            Sprite sprite;
            if (id == "Imugi")
                sprite = Resources.Load<Sprite>("UI/ImugiPortrait");
            else
                sprite = CharacterSpawner.FirstSprite(FindCharacterData(id));

            if (sprite != null) PieceSpriteCache[id] = sprite;
            return sprite;
        }

        static CharacterData FindCharacterData(string id)
        {
            foreach (var agent in CharacterAgent.All)
            {
                if (agent?.Data != null && agent.Data.id.ToString() == id)
                    return agent.Data;
            }

            var main = UnityEngine.Object.FindObjectOfType<Yoegoe.Main>();
            if (main != null)
            {
                if (id == nameof(CharacterId.Rabbit)) return main.oktoData;
                if (id == nameof(CharacterId.SamjokO)) return main.samjokOData;
                if (id == nameof(CharacterId.Gumiho)) return main.gumihoData;
                if (id == nameof(CharacterId.Gorani))
                    return main.goraniData != null
                        ? main.goraniData
                        : Resources.Load<CharacterData>("Characters/Gorani");
            }

            if (id == nameof(CharacterId.Gorani))
                return Resources.Load<CharacterData>("Characters/Gorani");
            return null;
        }

        static Color ColorForYokai(string id)
        {
            int hash = 0;
            if (!string.IsNullOrEmpty(id))
                foreach (char c in id) hash = hash * 31 + c;
            float hue = (Mathf.Abs(hash) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.55f, 0.9f);
        }

        void BindOrCreateOpponentPiece()
        {
            if (_pads == null || _pads.Length == 0) return;
            if (_opponentPiece != null) return;

            var opponentT = _pads[0].transform.Find("ImugiPiece");
            if (opponentT != null)
            {
                _opponentPiece = opponentT as RectTransform;
                return;
            }

            var opponentGo = new GameObject("ImugiPiece", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            opponentGo.transform.SetParent(_pads[0].transform, false);
            _opponentPiece = opponentGo.GetComponent<RectTransform>();
            _opponentPiece.anchorMin = new Vector2(0.15f, 0.15f);
            _opponentPiece.anchorMax = new Vector2(0.85f, 0.85f);
            _opponentPiece.offsetMin = Vector2.zero;
            _opponentPiece.offsetMax = Vector2.zero;
            var opponentImg = opponentGo.GetComponent<Image>();
            var imugiSprite = PieceSpriteFor("Imugi");
            if (imugiSprite != null)
            {
                opponentImg.sprite = imugiSprite;
                opponentImg.color = Color.white;
                opponentImg.preserveAspect = true;
            }
            else
            {
                opponentImg.color = new Color(0.25f, 0.55f, 0.85f, 1f);
            }
            opponentGo.SetActive(false);
        }
    }
}
