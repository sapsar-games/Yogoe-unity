using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Cooking;

namespace Yoegoe.UI
{
    public partial class GongyangganScreen
    {
        public void OnCellDown(int x, int y)
        {
            if (session != null && session.CookAt(x, y) != null)
            {
                bool guestWasDone = session.GuestOrder != null && session.GuestOrder.Fulfilled;
                if (!session.TryCollectCook(x, y) && statusText != null)
                {
                    var job = session.CookAt(x, y);
                    if (job != null && job.Phase == CookingCookPhase.Cooking)
                        statusText.text = "아직 익는 중";
                }
                else
                {
                    var g = session.GuestOrder;
                    if (g != null && g.Fulfilled && !guestWasDone && g.IntimacyGain > 0f)
                        IntimacyHeartFx.PlayFromAgent(g.Yokai, g.IntimacyGain);
                }
                return;
            }
            pointerDown = true;
            session?.TryBeginPath(x, y);
        }

        public void OnCellEnter(int x, int y)
        {
            if (!pointerDown) return;
            session?.TryExtendPath(x, y);
        }

        public void OnCellUp()
        {
            if (!pointerDown) return;
            pointerDown = false;
            session?.EndPath();
        }

        static CookingCharmType CharmAt(int i) => i switch
        {
            0 => CookingCharmType.PlusFive,
            1 => CookingCharmType.Diagonal,
            2 => CookingCharmType.Clairvoyance,
            3 => CookingCharmType.Recycle,
            4 => CookingCharmType.Double,
            _ => CookingCharmType.None
        };

        static string CharmLabel(CookingCharmType t) => t switch
        {
            CookingCharmType.PlusFive => "+5초",
            CookingCharmType.Diagonal => "대각선",
            CookingCharmType.Clairvoyance => "천리안",
            CookingCharmType.Recycle => "회수",
            CookingCharmType.Double => "몰빵",
            CookingCharmType.Cancel => "나가리",
            _ => ""
        };

        /// <summary>나가리 버튼 위 X 표시 (동적 콘텐츠 — 처음 필요할 때 한 번 만든다).</summary>
        GameObject EnsureNagariLockMark(Font labelFont)
        {
            if (nagariLockMark != null || nagariButton == null) return nagariLockMark;
            var go = new GameObject("LockedX", typeof(RectTransform));
            go.transform.SetParent(nagariButton.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.font = labelFont != null ? labelFont : font;
            t.text = "X";
            t.fontStyle = FontStyle.Bold;
            t.fontSize = 64;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(0.9f, 0.2f, 0.2f, 0.95f);
            t.raycastTarget = false;
            go.SetActive(false);
            nagariLockMark = go;
            return go;
        }
    }
}
