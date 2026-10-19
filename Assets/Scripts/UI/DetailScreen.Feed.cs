using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    public partial class DetailScreen
    {
        public void NotifyOfferingDragBegan(OfferingData offering, bool isWater)
        {
            offeringDragActive = true;
            feedDropHoverLatched = false;
            draggedOffering = offering;
            draggedWater = isWater;
            if (feedHintText != null)
                feedHintText.text = "캐릭터 위에 놓아 공양하세요";
            SetPortraitDropHighlight(false);
        }

        public void NotifyOfferingDragMoved(Vector2 screenPos)
        {
            bool over = IsOverFeedTarget(screenPos);
            if (over) feedDropHoverLatched = true;
            SetPortraitDropHighlight(over || feedDropHoverLatched);
            UpdateEmoteHint(over);
        }

        /// <summary>
        /// 초상 위로 공양물을 끌고 오면 표정 힌트: 선호면 ♥, 아니면 💢.
        /// 공개 여부와 무관하게 보여 준다(비공개 선호를 찾는 힌트). 물은 힌트 없음.
        /// </summary>
        void UpdateEmoteHint(bool over)
        {
            bool show = over
                && offeringDragActive
                && currentAgent != null
                && draggedOffering != null
                && !draggedWater
                && !IsWaterOffering(draggedOffering);
            if (!show)
            {
                if (emoteBubble != null) emoteBubble.Hide();
                return;
            }
            if (emoteBubble == null)
            {
                if (portraitDropRt == null) return;
                emoteBubble = EmoteBubble.Create(portraitDropRt);
            }
            emoteBubble.Show(IsPreferred(draggedOffering) ? EmoteBubble.Kind.Happy : EmoteBubble.Kind.Dislike);
        }

        public void NotifyOfferingDragEnded(bool accepted)
        {
            offeringDragActive = false;
            feedDropHoverLatched = false;
            draggedOffering = null;
            draggedWater = false;
            if (emoteBubble != null) emoteBubble.Hide();
            SetPortraitDropHighlight(false);
            if (feedHintText == null) return;
            if (accepted)
            {
                feedHintText.text = "";
                return;
            }
            // 실패 사유를 NotifyFeedBlocked 로 이미 넣었으면 유지
            if (!string.IsNullOrEmpty(feedHintText.text)
                && feedHintText.text != "캐릭터 위에 놓아 공양하세요")
                return;
            feedHintText.text = DefaultFeedHint();
        }

        public void NotifyFeedBlocked(string reason)
        {
            if (feedHintText != null && !string.IsNullOrEmpty(reason))
                feedHintText.text = reason;
        }

        static string DefaultFeedHint() => "물·공양물을 드래그해 캐릭터에게 먹이세요";

        public bool TryAcceptOfferingDrop(Vector2 screenPos, OfferingData offering, bool water)
        {
            bool overNow = IsOverFeedTarget(screenPos);
            if (!overNow && !feedDropHoverLatched)
            {
                NotifyFeedBlocked("캐릭터(초상·정보) 위에 놓아 주세요");
                return false;
            }

            if (CeremonyGate.BlocksWorldInput)
            {
                NotifyFeedBlocked("연출이 끝난 뒤 다시 시도해 주세요");
                return false;
            }

            if (water || (offering != null && IsWaterOffering(offering)))
                return OnFeedWater();
            if (offering == null) return false;
            return OnFeed(offering);
        }

        bool IsOverFeedTarget(Vector2 screenPos)
        {
            var target = feedDropRt != null ? feedDropRt : portraitDropRt;
            if (target == null) return false;
            Camera cam = null;
            if (rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = rootCanvas.worldCamera;
            // 터치 릴리즈 오차 여유 (레퍼런스 해상도 기준 로컬 유닛)
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    target, screenPos, cam, out var local))
                return false;
            var r = target.rect;
            const float pad = 28f;
            r.xMin -= pad;
            r.xMax += pad;
            r.yMin -= pad;
            r.yMax += pad;
            return r.Contains(local);
        }

        void SetPortraitDropHighlight(bool on)
        {
            if (portraitPanelHighlight == null) return;
            portraitPanelHighlight.color = on ? C.portraitHighlight : C.portraitBg;
        }

        private bool OnFeedWater() => ApplyFeedResult(currentAgent?.TryFeedWater(FindWater()), null);

        /// <summary>공양 규칙은 CharacterAgent.TryFeed — 여기선 결과 문구·연출·갱신만.</summary>
        private bool OnFeed(OfferingData offering)
        {
            if (currentAgent == null || offering == null) return false;
            return ApplyFeedResult(currentAgent.TryFeed(offering), offering);
        }

        bool ApplyFeedResult(FeedResult? maybe, OfferingData offering)
        {
            if (currentAgent == null || maybe == null) return false;
            var r = maybe.Value;
            if (!r.Success)
            {
                NotifyFeedBlocked(FeedBlockMessage(r.Block, offering));
                return false;
            }

            PlayGainPopup(r.StaminaGain, r.IntimacyGain);
            RefreshStats();
            RefreshItemCounts();
            if (r.PreferenceRevealed)
                RebuildPreferredRow();
            bool ranOut = offering != null && GameEconomy.Instance != null
                          && GameEconomy.Instance.GetOfferingCount(offering) <= 0;
            if (r.RequestFulfilled || r.PreferenceRevealed || ranOut)
                RebuildInventoryRow();
            GameSaveBridge.SaveFromWorld();
            return true;
        }

        static string FeedBlockMessage(FeedBlock block, OfferingData offering) => block switch
        {
            FeedBlock.NoItem => offering == null || offering.IsWater ? "물이 없어요" : "공양물이 없어요",
            FeedBlock.StaminaFull => "기력이 가득 찼어요",
            FeedBlock.StaminaAndIntimacyFull => "기력·친밀도가 모두 가득 찼어요",
            FeedBlock.FaintedNeedsWater => "기절한 요괴는 물로만 깨어나요",
            FeedBlock.NotFeedable => "먹이기 대상이 아니에요",
            _ => "",
        };

        void PlayGainPopup(int staminaGain, float intimacyGain)
        {
            if (rootCanvas == null || portraitDropRt == null) return;
            OfferingGainPopup.Play(rootCanvas.transform, portraitDropRt, font, staminaGain, intimacyGain);
            IntimacyHeartFx.PlayUi(rootCanvas.transform, portraitDropRt, intimacyGain);
        }

        static bool IsWaterOffering(OfferingData offering) => offering != null && offering.IsWater;

        private bool IsPreferred(OfferingData offering) =>
            currentAgent != null && currentAgent.IsPreferredOffering(offering);

        private OfferingData FindWater()
        {
            if (offerings == null) return null;
            foreach (var o in offerings)
            {
                if (o == null) continue;
                if (IsWaterOffering(o)) return o;
            }
            return CharacterCatalog.FindOffering("water");
        }
    }
}
