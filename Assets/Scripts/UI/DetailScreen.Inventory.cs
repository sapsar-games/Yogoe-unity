using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.UI
{
    public partial class DetailScreen
    {
        private void ToggleInventory()
        {
            if (inventoryPanel == null) return;
            bool show = !inventoryPanel.activeSelf;
            inventoryPanel.SetActive(show);
            if (show) RebuildInventoryRow();
        }

        private void RebuildPreferredRow()
        {
            if (preferredRow == null) return;
            ClearChildren(preferredRow);
            PruneDeadCountBadges();

            CharacterCatalog.PreferredOffering[] prefs = null;
            if (currentAgent?.Data != null
                && CharacterCatalog.TryGet(currentAgent.Data.id, out var entry)
                && entry != null)
                prefs = entry.preferredOfferings;

            int shown = 0;
            if (prefs == null || prefs.Length == 0)
            {
                var soPrefs = currentAgent?.Data?.preferredOfferings;
                if (soPrefs != null)
                {
                    foreach (var o in soPrefs)
                    {
                        if (o == null) continue;
                        if (currentAgent.Stats.IsPreferenceRevealed(o.offeringId))
                            CreatePreferredChip(preferredRow, o.displayName, o.icon, o);
                        else
                            CreateHiddenPreferredChip(preferredRow);
                        shown++;
                    }
                }
            }
            else
            {
                foreach (var p in prefs)
                {
                    if (p == null) continue;
                    shown++;
                    if (!currentAgent.Stats.IsPreferenceRevealed(p.id))
                    {
                        CreateHiddenPreferredChip(preferredRow);
                        continue;
                    }
                    var resolved = CharacterCatalog.FindOffering(p.id);
                    string label = !string.IsNullOrEmpty(p.name) ? p.name : p.id;
                    CreatePreferredChip(preferredRow, label, resolved != null ? resolved.icon : null, resolved);
                }
            }

            // 선호 공양물이 없는 요괴(옥토끼) — 처음부터 X로 "없음"을 알려 준다
            if (shown == 0)
                CreateMarkChip(preferredRow, "X", "없음");
        }

        private void RebuildInventoryRow(string highlightOfferingId = null)
        {
            if (inventoryRow == null) return;
            ClearChildren(inventoryRow);
            PruneDeadCountBadges();
            if (offerings == null) return;

            // 요구한 음식을 앞으로(강조 슬롯) — 10-2: 가진 것만, 없으면 강조 없음
            var highlighted = FindOwnedRequestTarget(highlightOfferingId);
            if (highlighted != null)
                CreatePreferredChip(inventoryRow, highlighted.displayName, highlighted.icon, highlighted, highlight: true,
                    goldFrame: IsRevealedPreferred(highlighted));

            // 음식·공양물·황금음식 — 가진 것만 나열
            foreach (var offering in offerings)
            {
                if (offering == null || offering == highlighted) continue;
                if (IsWaterOffering(offering)) continue;
                if (GameEconomy.Instance == null || GameEconomy.Instance.GetOfferingCount(offering) <= 0) continue;
                CreatePreferredChip(inventoryRow, offering.displayName, offering.icon, offering, highlight: false,
                    goldFrame: IsRevealedPreferred(offering));
            }
        }

        /// <summary>
        /// 요구 강조 대상: 요구한 음식을 가졌으면 그것, 없고 그 음식의 황금 버전을 가졌으면 황금 버전
        /// (황금음식을 줘도 요구가 채워진다). 둘 다 없으면 null — 강조 칸을 만들지 않는다.
        /// </summary>
        public static OfferingData FindOwnedRequestTarget(string requestedId)
        {
            var eco = GameEconomy.Instance;
            if (string.IsNullOrEmpty(requestedId) || eco == null) return null;
            var food = OfferingCatalog.Find(requestedId) ?? CharacterCatalog.FindOffering(requestedId);
            if (food != null && eco.GetOfferingCount(food) > 0) return food;
            var golden = OfferingCatalog.FindGolden(requestedId);
            if (golden != null && eco.GetOfferingCount(golden) > 0) return golden;
            return null;
        }

        void PruneDeadCountBadges()
        {
            offeringCountBadges.RemoveAll(b => b.Label == null);
        }

        bool IsRevealedPreferred(OfferingData offering) =>
            offering != null && currentAgent != null
            && currentAgent.Stats.IsPreferenceRevealed(offering.offeringId)
            && IsPreferred(offering);

        /// <summary>아직 먹여 보지 않은 선호 — 이름·아이콘 없이 "?" (드래그 불가).</summary>
        private void CreateHiddenPreferredChip(Transform parent) => CreateMarkChip(parent, "?", "???");

        /// <summary>아이콘 대신 큰 글자 하나(?, X)를 띄우는 드래그 불가 칩.</summary>
        private void CreateMarkChip(Transform parent, string mark, string label)
        {
            CreatePreferredChip(parent, label, null, null);
            var circle = parent.GetChild(parent.childCount - 1).Find("Circle");
            if (circle == null) return;
            var icon = circle.Find("Icon");
            if (icon != null) icon.gameObject.SetActive(false);
            var q = CreateText(circle, mark, F.title, TextAnchor.MiddleCenter);
            q.color = C.textDark;
            q.raycastTarget = false;
            UiRectUtils.SetupRect(q.gameObject, circle, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
        }

        private void CreatePreferredChip(Transform parent, string label, Sprite icon, OfferingData feedTarget, bool highlight = false,
            bool goldFrame = false)
        {
            var itemGO = new GameObject("Pref_" + (label ?? "?"));
            itemGO.transform.SetParent(parent, false);
            var le = itemGO.AddComponent<LayoutElement>();
            le.preferredWidth = 100;
            le.preferredHeight = 130;

            var v = itemGO.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            v.childControlHeight = true;
            v.childControlWidth = true;

            var circleGO = new GameObject("Circle");
            circleGO.transform.SetParent(itemGO.transform, false);
            var circleLe = circleGO.AddComponent<LayoutElement>();
            circleLe.preferredWidth = 72;
            circleLe.preferredHeight = 72;
            var circleImg = circleGO.AddComponent<Image>();
            circleImg.color = highlight
                ? C.offeringHighlight
                : C.offeringIdle;
            if (goldFrame)
            {
                var frame = circleGO.AddComponent<Outline>();
                frame.effectColor = C.preferredGoldFrame;
                frame.effectDistance = new Vector2(4f, -4f);
            }

            var iconGO = new GameObject("Icon");
            UiRectUtils.SetupRect(iconGO, circleGO.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconImg.color = icon != null ? Color.white : new Color(0.7f, 0.7f, 0.75f, 1f);
            iconImg.raycastTarget = false;

            if (feedTarget != null && feedTarget.golden)
                GoldenSparkle.Attach(circleImg); // 황금음식 — 금빛 반짝임

            if (feedTarget != null)
            {
                var drag = circleGO.AddComponent<OfferingDragItem>();
                drag.Configure(this, feedTarget, IsWaterOffering(feedTarget), icon != null ? icon : feedTarget.icon);
                AttachCountBadge(circleGO.transform, feedTarget, isWater: false);
            }

            var tagGO = new GameObject("Tag");
            tagGO.transform.SetParent(itemGO.transform, false);
            var tagLe = tagGO.AddComponent<LayoutElement>();
            tagLe.preferredHeight = 28;
            var tagBg = tagGO.AddComponent<Image>();
            tagBg.color = C.accentBlue;
            tagBg.raycastTarget = false;
            var tagText = CreateText(tagGO.transform, label ?? "", F.caption, TextAnchor.MiddleCenter);
            tagText.color = Color.white;
            UiRectUtils.SetupRect(tagText.gameObject, tagGO.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
        }

        Text AttachCountBadge(Transform iconParent, OfferingData offering, bool isWater)
        {
            var badgeGO = new GameObject("CountBadge");
            UiRectUtils.SetupRect(badgeGO, iconParent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-4f, -4f), new Vector2(36f, 22f));
            var bg = badgeGO.AddComponent<Image>();
            bg.color = C.badgeBg;
            bg.raycastTarget = false;
            int n = 0;
            if (GameEconomy.Instance != null)
            {
                n = isWater
                    ? GameEconomy.Instance.Water
                    : GameEconomy.Instance.GetOfferingCount(offering);
            }
            var text = CreateText(badgeGO.transform, "x" + n, F.caption, TextAnchor.MiddleCenter);
            text.color = Color.white;
            UiRectUtils.SetupRect(text.gameObject, badgeGO.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            offeringCountBadges.Add(new CountBadge
            {
                Label = text,
                Offering = offering,
                Water = isWater
            });
            return text;
        }
    }
}
