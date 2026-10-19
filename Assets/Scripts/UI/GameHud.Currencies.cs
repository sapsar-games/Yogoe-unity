using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.UI
{
    public partial class GameHud
    {
        private void RefreshCurrencies()
        {
            var eco = GameEconomy.Instance;
            if (eco == null) return;

            if (meritText != null)
            {
                if (meritCounting)
                    TickMeritCountUp();
                else
                {
                    var merit = eco.MeritPile;
                    if (!hasLastMeritShown || !merit.Equals(lastMeritShown))
                    {
                        hasLastMeritShown = true;
                        lastMeritShown = merit;
                        meritText.text = "공덕 " + merit.ToDisplayString();
                    }
                }
            }
            if (yeopjeonText != null && eco.Yeopjeon != lastYeopjeon)
            {
                lastYeopjeon = eco.Yeopjeon;
                yeopjeonText.text = "엽전 " + lastYeopjeon;
            }
        }

        static readonly List<ExtraSlot> desiredExtras = new List<ExtraSlot>(3);

        static List<ExtraSlot> DesiredExtraSlots()
        {
            desiredExtras.Clear();
            if (!CharacterSummon.IsPresent(CharacterId.Gorani)) desiredExtras.Add(ExtraSlot.SummonGorani);
            if (!CharacterSummon.LockedSlotUnlocked) desiredExtras.Add(ExtraSlot.Locked);
            else if (!CharacterSummon.IsPresent(CharacterId.Gumiho)) desiredExtras.Add(ExtraSlot.SummonGumiho);
            return desiredExtras;
        }

        static readonly List<CharacterAgent> slotBarAgents = new List<CharacterAgent>(8);

        /// <summary>슬롯 바에 칩으로 보일 요괴 — 옥토끼는 NPC라 뺀다(기획 7-2 · v1.3).</summary>
        static List<CharacterAgent> SlotBarAgents()
        {
            slotBarAgents.Clear();
            var all = CharacterAgent.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Data == null || all[i].Data.id != CharacterId.Rabbit)
                    slotBarAgents.Add(all[i]);
            return slotBarAgents;
        }

        private void RefreshSlotBar()
        {
            var agents = SlotBarAgents();
            var extras = DesiredExtraSlots();
            int agentCount = agents.Count;
            bool mismatched = slotChips.Count != agentCount + extras.Count;
            for (int i = 0; !mismatched && i < slotChips.Count; i++)
            {
                var chip = slotChips[i];
                mismatched = i < agentCount
                    ? chip.IsSummonSlot || chip.Agent != agents[i]
                    : !chip.IsSummonSlot || chip.Extra != extras[i - agentCount];
            }
            if (mismatched) RebuildSlotBar();

            foreach (var chip in slotChips)
            {
                if (chip.IsSummonSlot || chip.Agent == null) continue;
                string name = chip.Agent.Data != null ? chip.Agent.Data.displayName : "?";
                if (chip.NameText != null && chip.LastDisplayName != name)
                {
                    chip.LastDisplayName = name;
                    chip.LastNameLabel = name;
                    chip.NameText.text = chip.LastNameLabel;
                }
                if (chip.StaminaFill != null && chip.StaminaFillRt != null)
                {
                    var state = chip.Agent.Stats.State;
                    bool alert = state == ActionState.Fainted;
                    float maxStamina = Mathf.Max(0.001f, chip.Agent.MaxStamina);
                    float ratio = alert ? 1f : Mathf.Clamp01(chip.Agent.Stats.Stamina / maxStamina);

                    bool flash = state == ActionState.Fainted;
                    bool ratioChanged = Mathf.Abs(ratio - chip.LastStaminaRatio) > 0.002f;
                    bool stateChanged = state != chip.LastStaminaState;
                    if (ratioChanged || stateChanged || flash)
                    {
                        chip.LastStaminaRatio = ratio;
                        chip.LastStaminaState = state;

                        var parentRt = chip.StaminaFillRt.parent as RectTransform;
                        float parentW = parentRt != null ? parentRt.rect.width : 130f;
                        if (parentW < 1f) parentW = 130f;

                        chip.StaminaFillRt.anchorMin = new Vector2(0f, 0f);
                        chip.StaminaFillRt.anchorMax = new Vector2(0f, 1f);
                        chip.StaminaFillRt.pivot = new Vector2(0f, 0.5f);
                        chip.StaminaFillRt.anchoredPosition = Vector2.zero;
                        chip.StaminaFillRt.sizeDelta = new Vector2(parentW * ratio, 0f);
                        chip.StaminaFillRt.localScale = Vector3.one;
                        chip.StaminaFill.color = CharacterStatusPresentation.ForStaminaBar(
                            state, Time.unscaledTime);
                    }
                }

                RefreshStatusTag(chip);
            }
        }

        private static void RefreshStatusTag(SlotChip chip)
        {
            if (chip.StatusTagRoot == null || chip.StatusTagText == null) return;

            var state = chip.Agent.Stats.State;
            if (state == chip.LastStatusState) return;
            chip.LastStatusState = state;

            var badge = CharacterStatusPresentation.ForSlot(state);
            chip.StatusTagRoot.SetActive(badge.Visible);
            if (!badge.Visible) return;

            chip.StatusTagText.text = badge.Label;
            if (chip.StatusTagBg != null) chip.StatusTagBg.color = badge.Color;
        }

        private void RebuildSlotBar()
        {
            foreach (Transform child in slotBarRoot) Destroy(child.gameObject);
            slotChips.Clear();

            foreach (var agent in SlotBarAgents())
            {
                var chipGO = new GameObject("Slot_" + (agent.Data != null ? agent.Data.displayName : "?"));
                var chipRt = UiRectUtils.SetupRect(chipGO, slotBarRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 100));
                var chipLayout = chipGO.AddComponent<LayoutElement>();
                chipLayout.preferredWidth = 160;
                chipLayout.preferredHeight = 100;

                var bg = chipGO.AddComponent<Image>();
                bg.color = C.hudSlotChip;

                var capturedAgent = agent;
                var button = chipGO.AddComponent<Button>();
                button.targetGraphic = bg;
                button.onClick.AddListener(() =>
                {
                    if (detailScreen != null) detailScreen.Open(capturedAgent);
                });

                var tagGO = new GameObject("StatusTag");
                UiRectUtils.SetupRect(tagGO, chipRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0f),
                    new Vector2(0, 4), new Vector2(72, 22));
                var tagBg = tagGO.AddComponent<Image>();
                tagBg.color = C.hudStatusTag;
                var tagTextGO = new GameObject("Label");
                UiRectUtils.SetupRect(tagTextGO, tagGO.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero);
                var tagText = tagTextGO.AddComponent<Text>();
                tagText.font = font;
                tagText.fontSize = UiFonts.Small;
                tagText.alignment = TextAnchor.MiddleCenter;
                tagText.color = Color.white;
                tagText.text = "일하는";
                tagText.raycastTarget = false;
                tagGO.SetActive(false);

                var nameGO = new GameObject("Name");
                UiRectUtils.SetupRect(nameGO, chipRt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f),
                    new Vector2(0, -6), new Vector2(-10, 26));
                var nameText = nameGO.AddComponent<Text>();
                nameText.font = font;
                nameText.fontSize = UiFonts.HudSlotName;
                nameText.alignment = TextAnchor.MiddleCenter;
                nameText.color = Color.white;
                nameText.raycastTarget = false;

                var barBgGO = new GameObject("StaminaBarBg");
                UiRectUtils.SetupRect(barBgGO, chipRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0, 10), new Vector2(130, 14));
                var barBg = barBgGO.AddComponent<Image>();
                barBg.color = C.hudStaminaTrack;

                var barFillGO = new GameObject("StaminaBarFill");
                var barFillRt = UiRectUtils.SetupRect(barFillGO, barBgGO.transform, Vector2.zero, Vector2.one,
                    new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
                var barFill = barFillGO.AddComponent<Image>();
                barFill.color = C.hudStaminaFill;
                barFill.raycastTarget = false;

                slotChips.Add(new SlotChip
                {
                    Agent = agent,
                    IsSummonSlot = false,
                    NameText = nameText,
                    StatusTagText = tagText,
                    StatusTagRoot = tagGO,
                    StatusTagBg = tagBg,
                    StaminaFill = barFill,
                    StaminaFillRt = barFillRt
                });
            }

            foreach (var extra in DesiredExtraSlots())
                AddExtraSlot(extra);
        }

        private void AddExtraSlot(ExtraSlot extra)
        {
            bool locked = extra == ExtraSlot.Locked;
            var summonTarget = extra == ExtraSlot.SummonGumiho ? CharacterId.Gumiho : CharacterId.Gorani;
            var chipGO = new GameObject(locked ? "Slot_Locked" : "Slot_Summon");
            var chipRt = UiRectUtils.SetupRect(chipGO, slotBarRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 100));
            var chipLayout = chipGO.AddComponent<LayoutElement>();
            chipLayout.preferredWidth = 160;
            chipLayout.preferredHeight = 100;

            var bg = chipGO.AddComponent<Image>();
            bg.color = locked ? C.hudSummonChip * new Color(0.6f, 0.6f, 0.6f, 1f) : C.hudSummonChip;

            var button = chipGO.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(() =>
            {
                if (SummonPopup.Instance == null) return;
                if (locked) SummonPopup.Instance.OpenUnlock();
                else SummonPopup.Instance.Open(summonTarget);
            });

            var nameGO = new GameObject("Name");
            UiRectUtils.SetupRect(nameGO, chipRt, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var nameText = nameGO.AddComponent<Text>();
            nameText.font = font;
            nameText.fontSize = UiFonts.HudSummonName;
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.color = C.hudSummonName;
            nameText.raycastTarget = false;
            nameText.text = locked ? "잠김\n엽전 " + CharacterSummon.LockedSlotYeopjeonCost : "+ 소환";
            if (locked) nameText.color = C.hudSummonName * new Color(0.75f, 0.75f, 0.75f, 1f);

            slotChips.Add(new SlotChip
            {
                Agent = null,
                IsSummonSlot = true,
                Extra = extra,
                NameText = nameText
            });
        }
    }
}
