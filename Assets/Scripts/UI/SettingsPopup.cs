using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Yoegoe.UI
{
    /// <summary>
    /// 설정 화면 — HUD 상단 오른쪽 [설정]. 지금은 글자 크기(작게 · 보통 · 크게 · 아주 크게)만.
    /// 고른 값은 기기에 저장(<see cref="UiTextScale"/>)되고 게임 전체 글자에 바로 적용된다.
    /// 코드로만 만든다(프리팹 없음) — GiftBundlePopup 과 같은 방식.
    /// </summary>
    public class SettingsPopup : MonoBehaviour
    {
        public static SettingsPopup Instance { get; private set; }

        public Font font;

        GameObject root;
        readonly List<Image> levelButtons = new List<Image>();
        Text previewText;

        static readonly Color BoxColor = new Color(0.14f, 0.1f, 0.08f, 0.98f);
        static readonly Color TextColor = new Color(1f, 0.95f, 0.85f);
        static readonly Color ButtonOff = new Color(0.32f, 0.25f, 0.19f, 1f);
        static readonly Color ButtonOn = new Color(0.78f, 0.58f, 0.25f, 1f);

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Open()
        {
            EnsureBuilt();
            Refresh();
            root.SetActive(true);
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
        }

        void Choose(int level)
        {
            UiTextScale.Level = level;
            Refresh();
        }

        void Refresh()
        {
            for (int i = 0; i < levelButtons.Count; i++)
                levelButtons[i].color = i == UiTextScale.Level ? ButtonOn : ButtonOff;
            if (previewText != null)
                previewText.text = $"지금: {UiTextScale.LevelNames[UiTextScale.Level]} — 요괴들이 마당에서 기다리고 있어요.";
        }

        void EnsureBuilt()
        {
            if (root != null) return;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("Canvas_Settings");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            root = new GameObject("Panel");
            var rootRt = Stretch(root, canvasGO.transform);
            var dim = root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            var dimBtn = root.AddComponent<Button>();
            dimBtn.targetGraphic = dim;
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Close);

            var box = MakeRect("Box", rootRt, Vector2.zero, new Vector2(760, 560));
            box.gameObject.AddComponent<Image>().color = BoxColor;
            box.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // 상자 안 탭은 닫지 않게

            MakeText(box, "설정", 44, new Vector2(0, 220), new Vector2(700, 70));
            MakeText(box, "글자 크기", 32, new Vector2(0, 130), new Vector2(700, 56));

            levelButtons.Clear();
            int n = UiTextScale.LevelNames.Length;
            float w = 160f, gap = 16f, total = n * w + (n - 1) * gap;
            for (int i = 0; i < n; i++)
            {
                int level = i;
                float x = -total / 2f + w / 2f + i * (w + gap);
                var b = MakeRect("Level" + i, box, new Vector2(x, 40), new Vector2(w, 80));
                var img = b.gameObject.AddComponent<Image>();
                img.color = ButtonOff;
                var btn = b.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => Choose(level));
                MakeText(b, UiTextScale.LevelNames[i], 28, Vector2.zero, new Vector2(w, 80));
                levelButtons.Add(img);
            }

            previewText = MakeText(box, "", 26, new Vector2(0, -70), new Vector2(700, 90));

            var ok = MakeRect("Close", box, new Vector2(0, -205), new Vector2(240, 72));
            var okImg = ok.gameObject.AddComponent<Image>();
            okImg.color = new Color(0.3f, 0.5f, 0.45f, 1f);
            var okBtn = ok.gameObject.AddComponent<Button>();
            okBtn.targetGraphic = okImg;
            okBtn.onClick.AddListener(Close);
            MakeText(ok, "닫기", 30, Vector2.zero, new Vector2(240, 72));

            root.SetActive(false);
        }

        static RectTransform Stretch(GameObject go, Transform parent)
        {
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static RectTransform MakeRect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Text MakeText(Transform parent, string msg, int size, Vector2 pos, Vector2 box)
        {
            var rt = MakeRect("Text", parent, pos, box);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = UiFonts.Size(size);
            t.alignment = TextAnchor.MiddleCenter;
            t.color = TextColor;
            t.text = msg;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
