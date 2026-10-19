using UnityEngine;

namespace Yoegoe.UI
{
    /// <summary>
    /// 런타임으로 UI를 만들 때마다 각 화면이 따로 구현하던 RectTransform 앵커/피벗/크기
    /// 설정 보일러플레이트를 공용화한 헬퍼.
    /// </summary>
    public static class UiRectUtils
    {
        public static RectTransform SetupRect(GameObject go, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return rt;
        }

        /// <summary>이름 붙은 새 GameObject를 만들어 중앙 피벗·고정 크기로 배치한다 — 라벨/버튼/아이콘처럼
        /// 부모 중심 기준 anchoredPosition 하나로 자리를 잡는 가장 흔한 생성 패턴.</summary>
        public static RectTransform CreateCenteredRect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>go에 RectTransform을 달아 parent를 꽉 채우도록(앵커 0~1, 오프셋 0) 배치한다.</summary>
        public static RectTransform Stretch(GameObject go, Transform parent)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>이미 부모에 연결된 RectTransform을 그대로 parent를 꽉 채우도록 만든다.</summary>
        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
