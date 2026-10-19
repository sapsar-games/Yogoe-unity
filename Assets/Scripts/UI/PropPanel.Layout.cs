using UnityEngine;
using UnityEngine.UI;

namespace Yoegoe.UI
{
    // ---------------- 동적 칸 (body 안) ----------------
    // 아래는 전부 범용 UI 레이아웃 헬퍼 — Build*()가 매번 새로 그릴 때 쓰는 블록 조립 함수들.
    // pos는 body(또는 넘긴 parent) 중심 기준 anchoredPosition.
    public partial class PropPanel
    {
        /// <summary>인스펙터에 font가 안 꽂혀 있으면 Unity 기본 폰트로 대체.</summary>
        Font FontOrBuiltin()
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }

        /// <summary>중앙 피벗·anchoredPosition으로 세팅된 빈 RectTransform 하나 생성.</summary>
        static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>parent 밑에 텍스트 한 줄(또는 wrap 박스)을 만든다. 클릭을 가리지 않게 raycastTarget은 꺼둠.</summary>
        Text TextIn(Transform parent, string msg, Vector2 pos, int size, Color color, FontStyle style = FontStyle.Normal, Vector2? box = null)
        {
            var t = Rect("Text", parent, pos, box ?? new Vector2(860, 50)).gameObject.AddComponent<Text>();
            t.font = FontOrBuiltin();
            t.fontSize = UiFonts.Size(size);
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = msg;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>body에 바로 붙는 일반 텍스트 한 줄 (TextIn의 단축형).</summary>
        Text Label(string msg, Vector2 pos, int size, Color color, Vector2? boxSize = null) =>
            TextIn(body, msg, pos, size, color, FontStyle.Normal, boxSize ?? new Vector2(880, 50));

        /// <summary>목적지·요괴 선택용 큰 클릭 카드 (배경 Image + Button). 내용(텍스트)은 호출부에서 TextIn으로 따로 채운다.</summary>
        RectTransform Card(Vector2 pos, Vector2 size, Color color, System.Action onClick)
        {
            var rt = Rect("Card", body, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var b = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return rt;
        }

        /// <summary>라벨 텍스트까지 포함한 버튼. onClick이 null이면 비활성(회색, 못 누름)으로 그려진다.</summary>
        UnityEngine.UI.Button Button(string label, Vector2 pos, Vector2 size, Color color, System.Action onClick)
        {
            var rt = Rect("Button", body, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var b = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            b.interactable = onClick != null;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            TextIn(rt, label, Vector2.zero, 28, TextColor, FontStyle.Bold, size);
            return b;
        }

        /// <summary>보관함·기력 등에 쓰는 가로 진행바. t는 0~1로 클램프.</summary>
        void Bar(Vector2 pos, float t, Color color)
        {
            var bg = Rect("Bar", body, pos, new Vector2(760, 26));
            bg.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.06f, 0.05f, 1f);
            var fill = Rect("Fill", bg, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<Image>().color = color;
        }
    }
}
