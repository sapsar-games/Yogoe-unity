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
    }
}
