using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Cooking;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.UI
{
    public partial class PropPanel
    {
        // ---------------- 화면 (body 안의 동적 콘텐츠를 매번 지우고 다시 그림) ----------------

        /// <summary>body를 싹 비우고 지금 상태(점유 여부)에 맞는 화면을 새로 그린다.
        /// 칸 개수가 상태마다 달라서(목적지 수·요괴 수 등) Prefab 대신 매번 코드로 그리는 방식.</summary>
        void Rebuild()
        {
            if (prop == null || body == null || titleText == null || subText == null) return;
            for (int i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);

            lastOccupied = prop.IsOccupied;
            titleText.text = prop.DisplayName;
            subText.text = SubLine();
            if (prop.IsOccupied) BuildBusy(); else BuildFree();
        }

        /// <summary>제목 밑 한 줄 요약 — 레벨·생산 주기·보관량.</summary>
        string SubLine()
        {
            var c = prop.ProductionConfig;
            if (c.Type == PropResourceType.Merit)
            {
                BigNumber per15 = PropProduction.BaseMeritPerMinute(c, prop.level) * 15.0;
                return $"Lv{prop.level} · 15분에 공덕 {per15.ToDisplayString()} · 보관 {((BigNumber)prop.MeritCapacity).ToDisplayString()}";
            }
            return $"Lv{prop.level} · {c.CycleMinutes:0}분에 1개 · 보관 {prop.ResourceCapacity}";
        }

        /// <summary>비어 있는 기물 화면: (사냥터·채집터면) 목적지 3칸 → 보낼 요괴 카드들 → 상황 요약 →
        /// [보내기]/[받기] → 레벨업. y를 계속 깎아가며 위에서 아래로 쌓는 수동 레이아웃.</summary>
        void BuildFree()
        {
            float y = 470f;
            // v1.3: 사냥터·채집터는 어느 개별 기물(멧돼지 앞산 등)에 드래그해도 똑같이
            // "어디로 보낼까요" 3곳 선택창이 뜬다 — 6개는 배경일 뿐, 실제 목적지는 여기서 고른다.
            // (고른 destId와 같은 propId를 가진 실제 기물에 앉힌다 — ResolveSeatProp.)
            var dests = PropCatalog.DestinationsFor(prop.ResourceType);
            if (dests.Count > 0)
            {
                Label("어디로 보낼까요?  목적지마다 나오는 재료가 달라요", new Vector2(0, y), 26, DimText);
                y -= 150f;
                float w = 270f, gap = 18f, total = dests.Count * w + (dests.Count - 1) * gap;
                for (int i = 0; i < dests.Count; i++)
                {
                    var d = dests[i];
                    var destSeatProp = ResolveSeatProp(prop, d.id);
                    bool destBusy = destSeatProp != null && destSeatProp != prop && destSeatProp.IsOccupied;
                    var card = Card(new Vector2(-total / 2f + w / 2f + i * (w + gap), y), new Vector2(w, 230),
                        destBusy ? CardDisabled : d.id == selectedDest ? CardOn : CardOff,
                        destBusy ? null : () => { selectedDest = d.id; refusing = null; Rebuild(); });
                    TextIn(card, d.name, new Vector2(0, 80), 30, TextColor, FontStyle.Bold);
                    TextIn(card, d.minIntimacy > 0 ? $"♥ 친밀도 {d.minIntimacy:0} 이상" : "누구나 · 희귀도 하", new Vector2(0, 40), 22, DimText);
                    if (destBusy)
                        TextIn(card, "다른 요괴가 일하는 중", new Vector2(0, -30), 22, DimText, FontStyle.Normal, new Vector2(w - 16, 90));
                    else
                        TextIn(card, DropLine(d.id), new Vector2(0, -30), 22, TextColor, FontStyle.Normal, new Vector2(w - 16, 90));
                    if (!destBusy && d.goldenChance > 0f) TextIn(card, $"황금 {d.goldenChance:0.#}%", new Vector2(0, -92), 22, Gold, FontStyle.Bold);
                }
                y -= 160f;
            }
            else if (prop.HasFixedDestination && Dest != null)
            {
                Label($"{Dest.name} · " + (Dest.minIntimacy > 0 ? $"♥ 친밀도 {Dest.minIntimacy:0} 이상" : "누구나"),
                    new Vector2(0, y), 26, DimText);
                y -= 50f;
                Label(DropLine(Dest.id), new Vector2(0, y), 22, TextColor, new Vector2(860, 50));
                y -= 70f;
            }

            var seatProp = ResolveSeatProp(prop, selectedDest);
            Label("누구를 보낼까요?  보관함이 차면 빈 옹달샘·제단으로 옮겨 가요", new Vector2(0, y), 26, DimText);
            y -= 140f;
            var workers = new List<CharacterAgent>(Workers());
            if (selectedAgent != null && WhyNot(selectedAgent, out _) != null) selectedAgent = null;
            float cw = 260f, cg = 16f, ct = workers.Count * cw + Mathf.Max(0, workers.Count - 1) * cg;
            for (int i = 0; i < workers.Count; i++)
            {
                var a = workers[i];
                string why = WhyNot(a, out bool refuse);
                bool on = a == selectedAgent;
                var color = on ? CardOn : (why != null && !refuse ? CardDisabled : CardOff);
                var card = Card(new Vector2(-ct / 2f + cw / 2f + i * (cw + cg), y), new Vector2(cw, 210), color, () =>
                {
                    if (refuse) { Refuse(a); Rebuild(); return; }
                    if (why != null) return;
                    selectedAgent = selectedAgent == a ? null : a;
                    Rebuild();
                });
                TextIn(card, a.Data.displayName, new Vector2(0, 70), 30, TextColor, FontStyle.Bold);
                TextIn(card, $"친밀도 {Mathf.FloorToInt(a.Stats.Intimacy)} · 기력 {Mathf.FloorToInt(a.Stats.Stamina)}/{Mathf.FloorToInt(a.MaxStamina)}",
                    new Vector2(0, 25), 21, DimText, FontStyle.Normal, new Vector2(cw - 10, 40));
                TextIn(card, why ?? "갈 수 있어요", new Vector2(0, -45), 22, refuse ? Warn : (why != null ? DimText : Gold), FontStyle.Normal, new Vector2(cw - 10, 80));
            }
            y -= 150f;

            string summary;
            var dd = Dest;
            if (refusing != null && dd != null)
                summary = $"{refusing.Data.displayName} “{GameSettingsText.Refuse}”  {dd.name}{Josa(dd.name, "은", "는")} 친밀도 {dd.minIntimacy:0}부터 가요 · 지금 {Mathf.FloorToInt(refusing.Stats.Intimacy)}";
            else if (seatProp.IsStorageHalted)
                summary = "보관함이 가득 찼어요. 먼저 받아 주세요.";
            else if (selectedAgent != null)
            {
                float gi = selectedAgent.Stats.Stamina;
                float cycle, fillMin;
                if (seatProp.ResourceType == PropResourceType.Merit)
                {
                    double perMin = PropProduction.BaseMeritPerMinute(seatProp.ProductionConfig, seatProp.level);
                    cycle = 15f;
                    fillMin = perMin > 0 ? (float)((seatProp.MeritCapacity - seatProp.PendingMerit.ToDouble()) / perMin) : 0f;
                }
                else
                {
                    int room = Mathf.Max(0, seatProp.ResourceCapacity - seatProp.StoredResources);
                    cycle = seatProp.ProductionConfig.CycleMinutes;
                    fillMin = room * cycle;
                }
                float workMin = gi * GameSettings.Get("staminaDrainMinutes");
                summary = workMin <= fillMin
                    ? $"기력 {Mathf.FloorToInt(gi)}이면 약 {Dur(workMin)} 일하고 쉬어요 ({Mathf.FloorToInt(workMin / Mathf.Max(1f, cycle))}개쯤)"
                    : $"약 {Dur(fillMin)}이면 보관함이 차고, 남은 기력으로 빈 옹달샘·제단에 옮겨 가요";
            }
            else summary = (dd != null ? dd.name + "(으)로 " : "") + "보낼 요괴를 골라 주세요." + (prop.StoredResources > 0 ? $"  보관함에 {prop.StoredResources}개가 있어요." : "");
            Label(summary, new Vector2(0, y), 24, refusing != null || seatProp.IsStorageHalted ? Warn : TextColor, new Vector2(860, 70));
            y -= 100f;

            bool canSend = selectedAgent != null && !seatProp.IsOccupied && !seatProp.IsStorageHalted;
            Button(selectedAgent != null ? selectedAgent.Data.displayName + " 보내기" : "보내기", new Vector2(0, y), new Vector2(380, 90),
                canSend ? Btn : BtnOff, canSend ? Send : (System.Action)null);
            y -= 120f;
            if (prop.HasPendingCollectible)
            {
                Button(TakeLabel(), new Vector2(0, y), new Vector2(340, 70), Btn, Take);
                y -= 90f;
            }
            BuildLevelRow(y);
        }

        /// <summary>점유된 기물 화면: 누가·어디서 일하는지 → 보관함/기력 게이지 → [불러들이기]/[받기] → 레벨업.</summary>
        void BuildBusy()
        {
            var a = prop.Occupant;
            float y = 420f;
            string where = prop.HasDestinations && PropCatalog.FindDestination(prop.DestinationId) is var pd && pd != null
                ? pd.name + "에서" : prop.DisplayName + "에서";
            Label($"{a.Data.displayName} · {where} 일하는 중", new Vector2(0, y), 32, TextColor);
            y -= 90f;

            int cap = prop.ResourceCapacity, n = prop.StoredResources;
            if (prop.ResourceType == PropResourceType.Merit)
            {
                double mc = prop.MeritCapacity, mp = prop.PendingMerit.ToDouble();
                Label($"쌓인 공덕  {prop.PendingMerit.ToDisplayString()} / {((BigNumber)mc).ToDisplayString()}" + (prop.IsStorageHalted ? "  (가득 — 받아 주세요)" : ""),
                    new Vector2(0, y), 28, prop.IsStorageHalted ? Warn : TextColor);
                y -= 60f;
                Bar(new Vector2(0, y), mc > 0 && !double.IsInfinity(mc) ? (float)(mp / mc) : 0f, Gold);
                y -= 70f;
                n = prop.HasPendingMerit ? 1 : 0;
            }
            else if (prop.IsResourceProp)
            {
                Label($"보관함  {n}/{cap}" + (prop.IsStorageHalted ? "  (가득 — 받아 주세요)" : ""), new Vector2(0, y), 28, prop.IsStorageHalted ? Warn : TextColor);
                y -= 60f;
                Bar(new Vector2(0, y), cap > 0 ? n / (float)cap : 0f, Gold);
                y -= 70f;
            }
            float max = a.MaxStamina;
            Label($"기력  {Mathf.FloorToInt(a.Stats.Stamina)}/{Mathf.FloorToInt(max)}", new Vector2(0, y), 28, TextColor);
            y -= 60f;
            Bar(new Vector2(0, y), max > 0 ? a.Stats.Stamina / max : 0f, new Color(0.45f, 0.75f, 0.4f));
            y -= 80f;

            if (prop.IsResourceProp)
            {
                prop.CaptureStorage(out _, out float progress, out _, out _);
                float left = Mathf.Max(1f, prop.ProductionConfig.CycleMinutes - progress / 60f);
                string gold = a.IsGolden ? "  (황금 요리 효과 중)" : "";
                Label($"다음 것까지 약 {left:0}분 · 보관함이 차면 남은 기력으로 빈 옹달샘·제단에 옮겨 가요{gold}",
                    new Vector2(0, y), 24, DimText, new Vector2(860, 70));
                y -= 110f;
            }

            Button("불러들이기", new Vector2(-170, y), new Vector2(300, 90), BtnOff, Recall);
            Button(n > 0 ? TakeLabel() : "받기", new Vector2(170, y), new Vector2(300, 90), n > 0 ? Btn : BtnOff, n > 0 ? Take : (System.Action)null);
            y -= 130f;
            BuildLevelRow(y);
            Label("기물마다 한 번에 한 마리만 일해요.", new Vector2(0, -600), 22, DimText);
        }

        /// <summary>바닥에 붙는 레벨업 행 (비용·버튼). 더 못 올리면 아무것도 안 그린다.</summary>
        void BuildLevelRow(float y)
        {
            if (!PropEconomy.CanUpgrade(prop)) return;
            var cost = PropEconomy.GetUpgradeCost(prop);
            var eco = GameEconomy.Instance;
            bool ok = eco != null && eco.MeritPile >= cost;
            string have = eco != null ? eco.MeritPile.ToDisplayString() : "0";
            Label(prop.IsResourceProp ? $"지금 Lv{prop.level} · 보관함 {prop.ResourceCapacity}칸"
                  : prop.ResourceType == PropResourceType.Merit ? $"지금 Lv{prop.level} · 보관 공덕 {((BigNumber)prop.MeritCapacity).ToDisplayString()} (레벨마다 ×{prop.ProductionConfig.CapacityGrowth:0.##})"
                  : $"지금 Lv{prop.level}", new Vector2(0, y), 24, DimText);
            y -= 70f;
            var b = Button($"레벨업 Lv{prop.level} → {prop.level + 1}", new Vector2(0, y), new Vector2(420, 80), ok ? Gold * 0.85f : BtnOff, ok ? LevelUp : (System.Action)null);
            y -= 60f;
            Label(ok ? $"공덕 {cost.ToDisplayString()}" : $"공덕 {cost.ToDisplayString()} 필요 · 지금 {have}", new Vector2(0, y), 22, ok ? Gold : Warn);
            if (!string.IsNullOrEmpty(levelNote))
            {
                Label(levelNote, new Vector2(0, y - 40), 24, Gold);
                levelNote = null;
            }
        }

        /// <summary>[받기] 버튼 라벨 — 공덕이면 금액, 자원이면 개수.</summary>
        string TakeLabel() => prop.ResourceType == PropResourceType.Merit
            ? $"받기 공덕 {prop.PendingMerit.ToDisplayString()}"
            : $"받기 {prop.StoredResources}개";

        /// <summary>목적지 카드에 보이는 "재료 n · 재료 n · 황금 n%" 한 줄 요약.</summary>
        static string DropLine(string destId)
        {
            var sb = new StringBuilder();
            foreach (var (code, w) in PropCatalog.DestinationDrops(destId))
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(PropCatalog.IsSpecialCode(code) ? PropSlot.SpecialItemName(PropCatalog.SpecialOf(code))
                    : CookingRecipeCatalog.DisplayName((CookingIngredientId)code));
                sb.Append(' ').Append(w.ToString("0"));
            }
            var dest = PropCatalog.FindDestination(destId);
            if (dest != null && dest.goldenChance > 0f)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append($"황금 {dest.goldenChance:0.#}%");
            }
            return sb.ToString();
        }

        /// <summary>분을 "n분"/"n시간 m분"으로.</summary>
        static string Dur(float minutes)
        {
            int m = Mathf.Max(0, Mathf.RoundToInt(minutes));
            if (m < 60) return m + "분";
            int h = m / 60, mm = m % 60;
            return mm > 0 ? $"{h}시간 {mm}분" : $"{h}시간";
        }

        /// <summary>받침 유무에 따라 조사(은/는, 이/가 등)를 골라 붙인다.</summary>
        static string Josa(string word, string withBatchim, string without)
        {
            if (string.IsNullOrEmpty(word)) return without;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return without;
            return (c - 0xAC00) % 28 != 0 ? withBatchim : without;
        }
    }
}
