using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Cooking;
using Yoegoe.Economy;

namespace Yoegoe.UI
{
    public partial class GongyangganScreen
    {
        void RefreshMakeable()
        {
            int discovered = CookingCodex.DiscoveredCount;
            if (makeableSession == session && makeableBoardVersion == session.BoardVersion
                && makeableDiscovered == discovered)
                return;
            makeableSession = session;
            makeableBoardVersion = session.BoardVersion;
            makeableDiscovered = discovered;
            if (makeableText != null) makeableText.text = MakeableLine(session);
            if (codexButtonLabel != null) codexButtonLabel.text = "요리책 " + CodexScreen.RateShort;
        }

        /// <summary>상태 줄 '지금 만들 수 있는 요리' — 발견한 요리는 이름, 아직 못 본 건 '?' (19장).</summary>
        public static string MakeableLine(CookingSession s)
        {
            if (s == null || s.Finished) return "";
            var list = s.MakeableNow();
            if (list.Count == 0) return "만들 수 있는 요리 없음";
            var names = new System.Collections.Generic.List<string>();
            int unknown = 0;
            foreach (var r in list)
            {
                if (CookingCodex.IsDiscovered(r.Id)) names.Add(r.DisplayName);
                else unknown++;
            }
            for (int i = 0; i < unknown; i++) names.Add("?");
            return "만들 수 있는 요리: " + string.Join(" · ", names);
        }

        static string GuestStatusLine(CookingSession s)
        {
            var g = s?.GuestOrder;
            if (g == null) return "";
            if (g.Failed) return $"{g.DisplayName}: 실망…";
            if (g.Fulfilled && g.RevivedFromFaint)
                return $"{g.DisplayName}: 깨어났어! 기력 +{g.StaminaGain}";
            if (g.Fulfilled)
                return g.Perfect
                    ? $"{g.DisplayName}: 최고야! 친밀도 +{g.IntimacyGain:0.#} · 기력 +{g.StaminaGain}"
                    : $"{g.DisplayName}: 고마워! 친밀도 +{g.IntimacyGain:0.#} · 기력 +{g.StaminaGain}";
            return $"{g.DisplayName}: {g.WaitLine} ({g.OfferingName})";
        }

        void RefreshView()
        {
            if (session == null) return;
            if (timerText != null)
            {
                timerText.text = $"{session.TimeLeft:0.0}s";
                timerText.color = session.TimeLeft <= 5f
                    ? new Color(0.9f, 0.25f, 0.2f)
                    : Color.white;
            }
            if (statusText != null)
            {
                string charm = selectedCharm == CookingCharmType.None
                    ? "부적 없음"
                    : $"{CharmLabel(selectedCharm)} (보유 {(GameEconomy.Instance != null ? GameEconomy.Instance.GetCharmCount(selectedCharm) : 0)})";
                string guest = GuestStatusLine(session);
                if (session.Running)
                {
                    statusText.text = string.IsNullOrEmpty(guest)
                        ? $"요리 중 · {charm}"
                        : guest;
                }
                else if (session.Finished)
                    statusText.text = string.IsNullOrEmpty(guest) ? $"끝 · {charm}" : guest;
                else
                    statusText.text = $"준비 · 재료 {session.MaterialsOnBoard}개 · {charm}";
            }
            if (titleText != null && session.Running && session.GuestOrder != null
                && !session.GuestOrder.Fulfilled && !session.GuestOrder.Failed)
                titleText.text = "공양간 · " + session.GuestOrder.DisplayName;
            else if (titleText != null)
                titleText.text = "공양간";
            if (makeableText != null || codexButtonLabel != null) RefreshMakeable();
            if (nagariButton != null)
            {
                bool showNagari = session.ShowNagari;
                nagariButton.gameObject.SetActive(showNagari);
                if (showNagari)
                {
                    var label = nagariButton.GetComponentInChildren<Text>(true);
                    int held = GameEconomy.Instance != null ? GameEconomy.Instance.GetCharmCount(CookingCharmType.Cancel) : 0;
                    if (label != null && nagariLabelHeld != held)
                    {
                        nagariLabelHeld = held;
                        label.text = $"나가리 ×{held}";
                    }
                    // 손님에게 요리를 건넨 뒤엔 사용 불가 — 버튼 위에 X
                    bool locked = session.CharmsLocked;
                    nagariButton.interactable = !locked;
                    var x = EnsureNagariLockMark(label != null ? label.font : null);
                    if (x != null && x.activeSelf != locked) x.SetActive(locked);
                }
            }
            if (extendButton != null)
                extendButton.gameObject.SetActive(session.AwaitingExtend);
            if (startButton != null)
                startButton.interactable = !session.Running && !session.Finished;

            for (int y = 0; y < CookingSession.GridSize; y++)
            for (int x = 0; x < CookingSession.GridSize; x++)
            {
                var img = cellImages[x, y];
                var label = cellLabels[x, y];
                if (img == null) continue;
                bool onPath = false;
                if (session.Path != null)
                {
                    for (int i = 0; i < session.Path.Count; i++)
                        if (session.Path[i].x == x && session.Path[i].y == y) { onPath = true; break; }
                }

                if (session.Locked[x, y] || !session.Grid[x, y].HasValue)
                {
                    var cook = session.CookAt(x, y);
                    if (cook != null)
                    {
                        img.color = cook.Phase == CookingCookPhase.Steam
                            ? new Color(0.95f, 0.85f, 0.45f, 1f)
                            : cook.Phase == CookingCookPhase.Cool
                                ? new Color(0.75f, 0.35f, 0.28f, 1f)
                                : new Color(0.55f, 0.32f, 0.18f, 1f);
                        if (label != null)
                        {
                            string tag = cook.Phase == CookingCookPhase.Steam ? "김!"
                                : cook.Phase == CookingCookPhase.Cool ? "식음"
                                : "익는중";
                            label.text = cook.Recipe.DisplayName + "\n" + tag;
                        }
                    }
                    else
                    {
                        img.color = new Color(0.15f, 0.12f, 0.1f, 0.55f);
                        if (label != null) label.text = "";
                    }
                }
                else
                {
                    bool golden = session.Golden != null && session.Golden[x, y];
                    img.color = onPath
                        ? new Color(0.95f, 0.75f, 0.35f, 1f)
                        : golden
                            ? GoldenSparkle.Pulse(new Color(0.55f, 0.42f, 0.12f, 1f), new Color(0.85f, 0.68f, 0.2f, 1f))
                            : new Color(0.35f, 0.28f, 0.22f, 1f);
                    if (label != null)
                        label.text = golden
                            ? "황금" + CookingRecipeCatalog.DisplayName(session.Grid[x, y].Value)
                            : CookingRecipeCatalog.DisplayName(session.Grid[x, y].Value);
                }
            }

            for (int i = 0; i < charmButtons.Length; i++)
            {
                if (charmButtons[i] == null) continue;
                var img = charmButtons[i].GetComponent<Image>();
                var t = CharmAt(i);
                int held = GameEconomy.Instance != null ? GameEconomy.Instance.GetCharmCount(t) : 0;
                bool sel = selectedCharm == t;
                bool canUse = held > 0;
                if (img != null)
                {
                    if (sel) img.color = new Color(0.85f, 0.65f, 0.3f);
                    else if (canUse) img.color = new Color(0.4f, 0.35f, 0.3f);
                    else img.color = new Color(0.22f, 0.2f, 0.18f, 0.7f);
                }
                charmButtons[i].interactable = canUse || sel;
                var label = charmButtons[i].GetComponentInChildren<Text>(true);
                if (label != null)
                    label.text = $"{CharmLabel(t)}\n×{held}";
            }
        }
    }
}
