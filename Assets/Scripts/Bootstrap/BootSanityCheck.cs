using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.UI;

namespace Yoegoe.Bootstrap
{
    /// <summary>
    /// 부팅 직후(Main.Start, 모든 Awake가 끝난 뒤) 핵심 풀스크린 화면 Prefab 인스턴스가
    /// 씬에 다 있는지 확인한다.
    ///
    /// 왜 필요한가: MainWorldSceneBaker(에디터 전용 도구)가 씬을 열고 다시 저장하는 과정에서
    /// Main/UI 프리팹 인스턴스가 통째로 빠지는 사고가 실제로 있었다(2026-10-08). 그 결과는
    /// "검정화면"이나 "저장이 조용히 안 됨"처럼 에러 한 줄 없이 겉보기엔 그냥 멈춘 것처럼
    /// 보여서, 원인 찾는 데 오래 걸렸다. 다음에 같은 사고가 나도 1초 안에 "뭐가 빠졌는지"
    /// 알 수 있도록, 콘솔 로그만이 아니라 화면에도 크게 띄운다.
    ///
    /// 한계: Main 프리팹 자체가 씬에서 빠지면 이 코드 자체가 실행되지 않는다 — 그건 이 체크로
    /// 못 잡는다(그 경우는 처음부터 카메라도 안 생겨서 검정화면이 되는데, 그 자체가 이미 꽤
    /// 알아보기 쉬운 신호다).
    /// </summary>
    public static class BootSanityCheck
    {
        public static void Run(Font font)
        {
            var missing = new List<string>();

            if (FindAny<GameHud>() == null) missing.Add("GameHud");
            if (FindAny<ShopScreen>() == null) missing.Add("ShopScreen");
            if (FindAny<YutScreen>() == null) missing.Add("YutScreen");
            if (FindAny<AttendanceScreen>() == null) missing.Add("AttendanceScreen");
            if (FindAny<DetailScreen>() == null) missing.Add("DetailScreen");
            if (FindAny<GongyangganScreen>() == null) missing.Add("GongyangganScreen");

            if (missing.Count == 0) return;

            string listText = string.Join(", ", missing);
            Debug.LogError("[BootSanityCheck] 씬에서 빠진 화면 프리팹: " + listText +
                            " — Main.unity에 해당 Prefab 인스턴스를 다시 배치하세요.");
            ShowOnScreen(
                "⚠ 화면 프리팹이 씬에서 빠졌습니다\n\n" + listText +
                "\n\nMain.unity에 해당 Prefab 인스턴스를 다시 배치하세요.",
                font);
        }

        static T FindAny<T>() where T : Object =>
            Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

        static void ShowOnScreen(string message, Font font)
        {
            var canvasGo = new GameObject("BootSanityCheck_ERROR");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760; // 다른 어떤 UI보다도 위
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var bgGo = new GameObject("Background", typeof(RectTransform));
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.55f, 0.05f, 0.05f, 0.95f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(bgGo.transform, false);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = new Vector2(0.06f, 0.1f);
            textRt.anchorMax = new Vector2(0.94f, 0.9f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 42;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = message;
        }
    }
}
