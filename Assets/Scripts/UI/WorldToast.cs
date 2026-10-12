using UnityEngine;
using UnityEngine.UI;

namespace Yoegoe.UI
{
    /// <summary>
    /// 맵 화면 아래쪽에 잠깐 뜨는 안내 토스트 (v1.2 시연 — 일하는 중 요괴 탭 현황, 부엌으로 안내 등).
    /// 코드로만 만든다(프리팹 없음).
    /// </summary>
    public class WorldToast : MonoBehaviour
    {
        public static WorldToast Instance { get; private set; }
        public Font font;

        const float ShowSeconds = 2.8f;

        Text text;
        GameObject box;
        float hideAt;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static void Show(string message) => Instance?.ShowInternal(message);

        void ShowInternal(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            EnsureBuilt();
            text.text = message;
            box.SetActive(true);
            hideAt = Time.unscaledTime + ShowSeconds;
        }

        void Update()
        {
            if (box != null && box.activeSelf && Time.unscaledTime > hideAt)
                box.SetActive(false);
        }

        void EnsureBuilt()
        {
            if (box != null) return;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("Canvas_WorldToast");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 850;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            box = new GameObject("Toast", typeof(RectTransform));
            var rect = (RectTransform)box.transform;
            rect.SetParent(canvasGO.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0, 240);
            rect.sizeDelta = new Vector2(900, 90);

            var bg = box.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.09f, 0.07f, 0.92f);
            bg.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            var textRt = (RectTransform)textGo.transform;
            textRt.SetParent(box.transform, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(24, 8);
            textRt.offsetMax = new Vector2(-24, -8);

            text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = UiFonts.Size(26);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.95f, 0.85f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            box.SetActive(false);
        }
    }
}
