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
    /// <summary>도전과제 배너·하트·말풍선·플레이 로그·토큰 [+] 버튼. (YutMiniGame 분할 — 본체는 YutMiniGame.cs)</summary>
    public partial class YutMiniGame
    {
        /// <summary>상단(하트·기록 줄) 바로 아래 — 매 판 도전과제 문구.</summary>
        public void SetChallengeBanner(string text)
        {
            EnsureChallengeBanner();
            if (_challengeBannerText == null) return;
            bool show = !string.IsNullOrEmpty(text);
            _challengeBannerText.gameObject.SetActive(show);
            if (show) _challengeBannerText.text = text;
        }

        void EnsureChallengeBanner()
        {
            if (_challengeBannerText != null) return;
            var existing = transform.Find("ChallengeBanner");
            if (existing != null)
            {
                _challengeBannerText = existing.GetComponent<Text>();
                if (_challengeBannerText != null) return;
            }

            var go = new GameObject("ChallengeBanner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            // 하트 줄(0.9~0.97) 바로 아래.
            SetAnchor(rt, 0.2f, 0.825f, 0.8f, 0.895f, 0, 0, 0, 0);
            _challengeBannerText = go.GetComponent<Text>();
            _challengeBannerText.font = font;
            _challengeBannerText.fontSize = 22;
            _challengeBannerText.alignment = TextAnchor.MiddleCenter;
            _challengeBannerText.color = new Color(1f, 0.92f, 0.7f, 1f);
            _challengeBannerText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _challengeBannerText.verticalOverflow = VerticalWrapMode.Truncate;
            _challengeBannerText.raycastTarget = false;
            go.SetActive(false);
        }

        public void SetThrowVisible(bool on)
        {
            if (_throwZone == null) return;
            _throwZone.SetActive(on);
        }

        public void RefreshHearts(int hearts)
        {
            if (_heartIcons == null) return;
            for (int i = 0; i < _heartIcons.Length; i++)
                _heartIcons[i].color = i < hearts ? HeartOn : HeartOff;
        }

        /// <summary>플레이어 쪽 요괴 하나(pieceId) 말 위 대화 말풍선. 같은 말이면 교체,
        /// 다음 ClearBubbles(다음 액션)까지 유지. 놀이기록엔 안 남는다.
        /// 보드에 없으면 동(東) 완주 초상 위를 앵커로 쓴다(옥토끼 완주 후 해설용).</summary>
        public void ShowPieceBubble(string pieceId, string text)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pieceId)) return;
            if (_yokaiPieces.TryGetValue(pieceId, out var anchor) && anchor != null)
            {
                ShowBubbleAbove(pieceId, anchor, text);
                return;
            }
            if (_finishedPieceAnchors.TryGetValue(pieceId, out var finished) && finished != null)
                ShowBubbleAbove(pieceId, finished, text);
        }

        /// <summary>이무기 말 위 대화 말풍선. 다음 ClearBubbles까지 유지.</summary>
        public void ShowOpponentBubble(string text)
        {
            if (!string.IsNullOrEmpty(text) && _opponentPiece != null && _opponentPiece.gameObject.activeInHierarchy)
                ShowBubbleAbove(OpponentBubbleKey, _opponentPiece, text);
        }

        /// <summary>떠 있는 말풍선을 전부 지운다 — 던지기/말 선택/턴 전환 등 다음 액션 시점.</summary>
        public void ClearBubbles()
        {
            foreach (var kv in _activeBubbles)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _activeBubbles.Clear();
            _bubbleAnchors.Clear();
        }

        void LateUpdate()
        {
            // 홉/슬라이드 중에도 이무기·요괴 대사가 말 위를 따라가게
            RepositionBubbles();
        }

        void RepositionBubbles()
        {
            if (_activeBubbles.Count == 0) return;
            foreach (var kv in _activeBubbles)
            {
                if (kv.Value == null) continue;
                if (!_bubbleAnchors.TryGetValue(kv.Key, out var anchor) || anchor == null || !anchor.gameObject.activeInHierarchy)
                    continue;
                var rt = (RectTransform)kv.Value.transform;
                rt.position = BubbleWorldPosAbove(anchor);
            }
        }

        static Vector3 BubbleWorldPosAbove(RectTransform anchor) =>
            anchor.position + new Vector3(0f, anchor.rect.height * 0.6f + 12f, 0f);

        void ShowBubbleAbove(string key, RectTransform anchor, string text)
        {
            if (_activeBubbles.TryGetValue(key, out var prev) && prev != null)
                Destroy(prev);
            _activeBubbles.Remove(key);
            _bubbleAnchors.Remove(key);

            var go = new GameObject("Bubble", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            // anchor.parent(칸 하나)에 붙이면 그 칸 안에서만 맨 위로 와서, 옆 칸(형제 순서상 뒤에
            // 오는 칸)에 말풍선이 걸쳐 나올 때 그 칸의 말한테 가려졌다 — 최상위(transform)에
            // 붙여서 보드 위 어떤 칸·말보다도 항상 위에 그려지게 한다. 위치는 world position
            // (.position)으로 잡으므로 부모를 바꿔도 계산은 그대로 유효하다.
            rt.SetParent(transform, false);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(220f, 56f);
            rt.position = BubbleWorldPosAbove(anchor);
            rt.SetAsLastSibling();
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.99f, 0.97f, 0.9f, 0.97f);
            bg.raycastTarget = false;

            var label = CreateText(rt, "Text", text, 20, TextAnchor.MiddleCenter);
            UiRectUtils.Stretch(label.rectTransform);
            label.color = new Color(0.15f, 0.12f, 0.08f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;

            _activeBubbles[key] = go;
            _bubbleAnchors[key] = anchor;
        }

        void EnsurePlayLogButton()
        {
            if (_playLogButton != null) return;

            var existing = transform.Find("PlayLogButton")?.GetComponent<Button>();
            if (existing != null)
            {
                _playLogButton = existing;
                WireButton(_playLogButton, TogglePlayLog);
                return;
            }

            var go = new GameObject("PlayLogButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            SetAnchor((RectTransform)go.transform, PlayLogLeft, 0.9f, PlayLogRight, 0.97f, 0, 0, 0, 0);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.22f, 0.19f, 0.14f, 0.9f);
            _playLogButton = go.AddComponent<Button>();
            _playLogButton.targetGraphic = bg;

            var label = CreateText(go.transform, "Label", "기록", 22, TextAnchor.MiddleCenter);
            UiRectUtils.Stretch(label.rectTransform);
            label.raycastTarget = false;

            _playLogButton.onClick.AddListener(TogglePlayLog);
        }

        /// <summary>윷 토큰(하트) 바로 왼쪽의 [+] — 놀이판 안에서도 토큰을 충전/구매할 수 있게.
        /// 실제 구매 로직(엽전/광고)은 YutScreen이 여는 DualActionPopup이 담당하고, 여기선
        /// 탭 이벤트만 올려보낸다(YutMiniGame은 재화를 모른다).</summary>
        void EnsureTokenPlusButton()
        {
            if (_tokenPlusButton != null) return;

            var existing = transform.Find("TokenPlusButton")?.GetComponent<Button>();
            if (existing != null)
            {
                _tokenPlusButton = existing;
                WireButton(_tokenPlusButton, () => OnBuyTokensPressed?.Invoke());
                return;
            }

            var go = new GameObject("TokenPlusButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            SetAnchor((RectTransform)go.transform, TokenPlusLeft, 0.9f, TokenPlusRight, 0.97f, 0, 0, 0, 0);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.3f, 0.5f, 0.45f, 0.95f);
            _tokenPlusButton = go.AddComponent<Button>();
            _tokenPlusButton.targetGraphic = bg;

            var label = CreateText(go.transform, "Label", "+", 26, TextAnchor.MiddleCenter);
            UiRectUtils.Stretch(label.rectTransform);
            label.raycastTarget = false;

            _tokenPlusButton.onClick.AddListener(() => OnBuyTokensPressed?.Invoke());
        }

        void EnsurePlayLogPanel()
        {
            if (_playLogPanel != null) return;

            var existing = transform.Find("PlayLogPanel") as RectTransform;
            if (existing != null)
            {
                _playLogPanel = existing.gameObject;
                _playLogViewport = existing.Find("Viewport") as RectTransform;
                _playLogContent = _playLogViewport != null ? _playLogViewport.Find("Content") as RectTransform : null;
                _playLogScroll = existing.GetComponent<ScrollRect>();
                if (_playLogScroll == null && _playLogViewport != null && _playLogContent != null)
                {
                    _playLogScroll = existing.gameObject.AddComponent<ScrollRect>();
                    _playLogScroll.viewport = _playLogViewport;
                    _playLogScroll.content = _playLogContent;
                    _playLogScroll.horizontal = false;
                    _playLogScroll.vertical = true;
                    _playLogScroll.movementType = ScrollRect.MovementType.Clamped;
                }
                var closeBtn = existing.Find("Close")?.GetComponent<Button>();
                WireButton(closeBtn, () => ShowPlayLog(false));
                _playLogPanel.SetActive(false);
                return;
            }

            var go = new GameObject("PlayLogPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            SetAnchor(rt, 0.1f, 0.25f, 0.9f, 0.72f, 0, 0, 0, 0);
            go.GetComponent<Image>().color = new Color(0.07f, 0.07f, 0.06f, 0.97f);
            _playLogPanel = go;

            var title = CreateText(rt, "Title", "놀이기록", 30, TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = new Vector2(0f, 0.88f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
            title.raycastTarget = false;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            viewportGo.transform.SetParent(rt, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            SetAnchor(viewportRt, 0.04f, 0.16f, 0.96f, 0.86f, 0, 0, 0, 0);
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
            viewportGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportRt, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            _playLogViewport = viewportRt;
            _playLogContent = contentRt;
            _playLogScroll = go.AddComponent<ScrollRect>();
            _playLogScroll.viewport = _playLogViewport;
            _playLogScroll.content = _playLogContent;
            _playLogScroll.horizontal = false;
            _playLogScroll.vertical = true;
            _playLogScroll.movementType = ScrollRect.MovementType.Clamped;

            var closeBtnNew = CreateButton(rt, "Close", "닫기", () => ShowPlayLog(false));
            SetAnchor(closeBtnNew.GetComponent<RectTransform>(), 0.32f, 0.02f, 0.68f, 0.14f, 0, 0, 0, 0);

            go.SetActive(false);
        }

        void TogglePlayLog()
        {
            EnsurePlayLogPanel();
            ShowPlayLog(!_playLogPanel.activeSelf);
        }

        public void ShowPlayLog(bool on)
        {
            EnsurePlayLogPanel();
            _playLogPanel.SetActive(on);
            if (on) RebuildPlayLogView();
        }

        /// <summary>대화 말풍선과는 별개로 쌓이는 짧은 사건형 문장 — 놀이기록 팝업에서만 보인다.</summary>
        public void AddPlayLogEntry(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _playLogEntries.Add(text);
            if (_playLogEntries.Count > MaxPlayLogEntries) _playLogEntries.RemoveAt(0);
            if (_playLogPanel != null && _playLogPanel.activeSelf) RebuildPlayLogView();
        }

        void RebuildPlayLogView()
        {
            if (_playLogContent == null) return;

            for (int i = _playLogContent.childCount - 1; i >= 0; i--)
                Destroy(_playLogContent.GetChild(i).gameObject);

            const float lineH = 40f;
            _playLogContent.sizeDelta = new Vector2(0f, _playLogEntries.Count * lineH + 8f);

            for (int i = 0; i < _playLogEntries.Count; i++)
            {
                var t = CreateText(_playLogContent, $"Line{i}", $"· {_playLogEntries[i]}", 22, TextAnchor.MiddleLeft);
                t.rectTransform.anchorMin = new Vector2(0f, 1f);
                t.rectTransform.anchorMax = new Vector2(1f, 1f);
                t.rectTransform.pivot = new Vector2(0.5f, 1f);
                t.rectTransform.anchoredPosition = new Vector2(0f, -i * lineH);
                t.rectTransform.sizeDelta = new Vector2(0f, lineH);
                t.raycastTarget = false;
            }

            Canvas.ForceUpdateCanvases();
            if (_playLogScroll != null)
                _playLogScroll.verticalNormalizedPosition = 0f; // 최신 줄(맨 아래)이 보이게
        }

        /// <summary>매치 시작/재입장 때 이전 놀이기록이 안 남게 비운다.</summary>
        public void ClearPlayLog()
        {
            _playLogEntries.Clear();
            if (_playLogContent == null) return;
            for (int i = _playLogContent.childCount - 1; i >= 0; i--)
                Destroy(_playLogContent.GetChild(i).gameObject);
            _playLogContent.sizeDelta = Vector2.zero;
        }
    }
}
