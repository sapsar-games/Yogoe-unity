using Yoegoe.Cooking;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    public partial class GongyangganScreen
    {
        void OnRoundEnded()
        {
            if (resultPopup == null || resultBody == null || session == null) return;
            resultBody.text = BuildResultText(session);
            resultPopup.SetActive(true);
            GameSaveBridge.RequestSave(); // 완성품·도감·회수 재료
            RefreshView();
        }

        /// <summary>결과창 (19장): 이번에 만든 요리 · 새로 얻은 레시피(금색) · 스러진 재료(회수 부적이면 회수한 재료).</summary>
        public static string BuildResultText(CookingSession s)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("이번에 만든 요리");
            if (s.Results.Count == 0)
                sb.AppendLine("· 없음");
            foreach (var (recipe, count, golden) in s.Results)
                sb.AppendLine(golden
                    ? $"· <color=#FFD54A>황금 {recipe.DisplayName}</color> x{count}"
                    : $"· {recipe.DisplayName} x{count}");

            var guest = s.GuestOrder;
            if (guest != null)
            {
                sb.AppendLine();
                sb.AppendLine("주문 요괴");
                if (guest.Fulfilled)
                {
                    string how = guest.Perfect ? "완벽하게 " : "";
                    sb.AppendLine($"· {guest.DisplayName}에게 {guest.OfferingName}을(를) {how}대접했어요");
                    sb.AppendLine(guest.RevivedFromFaint
                        ? $"· 기절에서 깨어났어요 · 기력 +{guest.StaminaGain}"
                        : $"· 기력 +{guest.StaminaGain} · 친밀도 +{guest.IntimacyGain:0.#}");
                }
                else if (guest.Failed)
                    sb.AppendLine($"· {guest.DisplayName}이(가) 아쉬워하며 돌아갔어요 — 실망…");
            }

            if (s.PerfectCollectCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"김 오를 때 꺼낸 요리 {s.PerfectCollectCount}번");
            }

            if (s.NewlyDiscovered.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("새로 얻은 레시피");
                foreach (var id in s.NewlyDiscovered)
                    if (CookingCodex.TryGetRecipe(id, out var r))
                        sb.AppendLine($"<color=#FFD54A>· {r.DisplayName}</color>");
            }

            if (s.Leftover.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(s.PreCharm == CookingCharmType.Recycle ? "회수한 재료" : "스러진 재료");
                var counts = new System.Collections.Generic.Dictionary<string, int>();
                var order = new System.Collections.Generic.List<string>();
                foreach (var item in s.Leftover)
                {
                    string name = (item.Golden ? "황금" : "") + CookingRecipeCatalog.DisplayName(item.Id);
                    if (!counts.ContainsKey(name)) { counts[name] = 0; order.Add(name); }
                    counts[name]++;
                }
                var parts = new System.Collections.Generic.List<string>();
                foreach (var name in order) parts.Add($"{name} {counts[name]}");
                sb.AppendLine(string.Join(" · ", parts));
            }
            return sb.ToString().TrimEnd();
        }
    }
}
