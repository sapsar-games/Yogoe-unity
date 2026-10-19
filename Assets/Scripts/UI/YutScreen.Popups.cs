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
    /// <summary>알림·선택·보상·되살리기·확인 팝업 UI. (YutScreen 분할 — 본체는 YutScreen.cs)</summary>
    public partial class YutScreen
    {
        void ShowNotice(string message, Action onOk)
        {
            noticeText.text = message;
            pendingNoticeAction = onOk;
            if (!root.activeSelf) root.SetActive(true);
            noticeRoot.SetActive(true);
        }

        void OnNoticeOk()
        {
            noticeRoot.SetActive(false);
            // 매치 종료 안내(완주)는 action(OnMatchEndedNoticeOk) 안에서 match를 null로 비운다 —
            // action 실행 "후"에 match == null을 검사하면 그 케이스까지 "매치 시작 전 안내"로
            // 오인해 화면을 닫아버린다(완주해도 윷판이 사라지던 버그). action 실행 전 상태로 판단한다.
            bool matchWasNullBeforeAction = match == null;
            var action = pendingNoticeAction;
            pendingNoticeAction = null;
            action?.Invoke();
            // 매치 시작 전 안내(토큰 부족 등)였다면 화면 자체를 다시 닫는다.
            if (matchWasNullBeforeAction && root != null) root.SetActive(false);
        }

        void BuildChoiceButton(Transform parent, Vector2 pos, string label, out Button button)
        {
            var btnGO = new GameObject($"Btn_{label}", typeof(RectTransform));
            var btnRt = (RectTransform)btnGO.transform;
            btnRt.SetParent(parent, false);
            btnRt.anchorMin = btnRt.anchorMax = btnRt.pivot = new Vector2(0.5f, 0.5f);
            btnRt.anchoredPosition = pos;
            btnRt.sizeDelta = new Vector2(320, 115);
            var btnImg = btnGO.AddComponent<Image>();
            btnImg.color = new Color(0.3f, 0.5f, 0.45f, 1f);
            button = btnGO.AddComponent<Button>();
            button.targetGraphic = btnImg;

            var labelGO = new GameObject("Label", typeof(RectTransform));
            var labelRt = (RectTransform)labelGO.transform;
            labelRt.SetParent(btnRt, false);
            UiRectUtils.Stretch(labelRt);
            var labelText = labelGO.AddComponent<Text>();
            labelText.font = font;
            labelText.fontSize = UiFonts.Size(34);
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = Color.white;
            labelText.text = label;
            labelText.raycastTarget = false;
        }

        void ShowChoice(string message, Action onContinue, Action onStop)
        {
            choiceText.text = message;
            pendingChoiceContinue = onContinue;
            pendingChoiceStop = onStop;
            if (!root.activeSelf) root.SetActive(true);
            choiceRoot.SetActive(true);
        }

        void OnChoiceContinueClicked()
        {
            choiceRoot.SetActive(false);
            var action = pendingChoiceContinue;
            pendingChoiceContinue = null;
            pendingChoiceStop = null;
            action?.Invoke();
        }

        void OnChoiceStopClicked()
        {
            choiceRoot.SetActive(false);
            var action = pendingChoiceStop;
            pendingChoiceContinue = null;
            pendingChoiceStop = null;
            action?.Invoke();
        }

        /// <summary>특수 칸 보상 "그냥 받기"/"광고 보고 2배" 팝업.</summary>
        void BuildRewardPanel(Transform parent)
        {
            rewardRoot = new GameObject("SquareReward", typeof(RectTransform));
            var rt = (RectTransform)rewardRoot.transform;
            rt.SetParent(parent, false);
            UiRectUtils.Stretch(rt);
            var dim = rewardRoot.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);

            var box = new GameObject("Box", typeof(RectTransform));
            var boxRt = (RectTransform)box.transform;
            boxRt.SetParent(rt, false);
            boxRt.anchorMin = boxRt.anchorMax = boxRt.pivot = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(700, 410);
            var boxImg = box.AddComponent<Image>();
            boxImg.color = new Color(0.14f, 0.1f, 0.08f, 0.98f);

            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer));
            var iconRt = (RectTransform)iconGO.transform;
            iconRt.SetParent(boxRt, false);
            iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = new Vector2(0, 160);
            iconRt.sizeDelta = new Vector2(96, 96);
            rewardIcon = iconGO.AddComponent<Image>();
            rewardIcon.preserveAspect = true;
            rewardIcon.raycastTarget = false;
            rewardIcon.gameObject.SetActive(false);

            var textGO = new GameObject("Text", typeof(RectTransform));
            var textRt = (RectTransform)textGO.transform;
            textRt.SetParent(boxRt, false);
            textRt.anchorMin = textRt.anchorMax = textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.anchoredPosition = new Vector2(0, 35);
            textRt.sizeDelta = new Vector2(610, 140);
            rewardText = textGO.AddComponent<Text>();
            rewardText.font = font;
            rewardText.fontSize = UiFonts.Size(38);
            rewardText.alignment = TextAnchor.MiddleCenter;
            rewardText.color = new Color(1f, 0.95f, 0.85f);
            rewardText.horizontalOverflow = HorizontalWrapMode.Wrap;

            BuildChoiceButton(boxRt, new Vector2(-165, -130), "그냥 받기", out rewardPlainButton);
            BuildChoiceButton(boxRt, new Vector2(165, -130), "광고 보고\n2배로 받기", out rewardAdButton);

            rewardRoot.SetActive(false);
        }

        void ShowRewardChoice(string message, Action onPlain, Action onAd, Sprite icon = null)
        {
            rewardText.text = message;
            // 구운 Prefab이 아이콘 슬롯 없이 만들어졌을 수 있어 방어적으로 null 체크.
            if (rewardIcon != null)
            {
                rewardIcon.sprite = icon;
                rewardIcon.gameObject.SetActive(icon != null);
            }
            pendingRewardPlain = onPlain;
            pendingRewardAd = onAd;
            if (!root.activeSelf) root.SetActive(true);
            rewardRoot.SetActive(true);
        }

        void OnRewardPlainClicked()
        {
            rewardRoot.SetActive(false);
            var action = pendingRewardPlain;
            pendingRewardPlain = null;
            pendingRewardAd = null;
            action?.Invoke();
        }

        void OnRewardAdClicked()
        {
            rewardRoot.SetActive(false);
            var action = pendingRewardAd;
            pendingRewardPlain = null;
            pendingRewardAd = null;
            action?.Invoke();
        }

        /// <summary>이무기한테 말이 잡혔을 때 "광고 보고 되살리기"/"그냥 두기" 팝업.</summary>
        void BuildRevivePanel(Transform parent)
        {
            reviveRoot = new GameObject("Revive", typeof(RectTransform));
            var rt = (RectTransform)reviveRoot.transform;
            rt.SetParent(parent, false);
            UiRectUtils.Stretch(rt);
            var dim = reviveRoot.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);

            var box = new GameObject("Box", typeof(RectTransform));
            var boxRt = (RectTransform)box.transform;
            boxRt.SetParent(rt, false);
            boxRt.anchorMin = boxRt.anchorMax = boxRt.pivot = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(700, 410);
            var boxImg = box.AddComponent<Image>();
            boxImg.color = new Color(0.14f, 0.1f, 0.08f, 0.98f);

            var textGO = new GameObject("Text", typeof(RectTransform));
            var textRt = (RectTransform)textGO.transform;
            textRt.SetParent(boxRt, false);
            textRt.anchorMin = textRt.anchorMax = textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.anchoredPosition = new Vector2(0, 70);
            textRt.sizeDelta = new Vector2(610, 220);
            reviveText = textGO.AddComponent<Text>();
            reviveText.font = font;
            reviveText.fontSize = UiFonts.Size(38);
            reviveText.alignment = TextAnchor.MiddleCenter;
            reviveText.color = new Color(1f, 0.95f, 0.85f);
            reviveText.horizontalOverflow = HorizontalWrapMode.Wrap;

            BuildChoiceButton(boxRt, new Vector2(-165, -130), "광고 보고\n되살리기", out reviveYesButton);
            BuildChoiceButton(boxRt, new Vector2(165, -130), "그냥 두기", out reviveNoButton);

            reviveRoot.SetActive(false);
        }

        void ShowReviveChoice(string message, Action onYes, Action onNo)
        {
            reviveText.text = message;
            pendingReviveYes = onYes;
            pendingReviveNo = onNo;
            if (!root.activeSelf) root.SetActive(true);
            reviveRoot.SetActive(true);
        }

        void OnReviveYesClicked()
        {
            reviveRoot.SetActive(false);
            var action = pendingReviveYes;
            pendingReviveYes = null;
            pendingReviveNo = null;
            action?.Invoke();
        }

        void OnReviveNoClicked()
        {
            reviveRoot.SetActive(false);
            var action = pendingReviveNo;
            pendingReviveYes = null;
            pendingReviveNo = null;
            action?.Invoke();
        }
    }
}
