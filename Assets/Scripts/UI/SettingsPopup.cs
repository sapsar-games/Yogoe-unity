using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Yoegoe.Bootstrap;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>
    /// 설정 화면 — HUD 상단 오른쪽 [설정].
    /// 글자 크기 + 테스트용 시간 가속(2·4·8·16배) · +20분 · 초기화 · 캐시 날리기.
    /// 고른 글자 크기는 기기에 저장(<see cref="UiTextScale"/>)되고 게임 전체 글자에 바로 적용된다.
    /// 코드로만 만든다(프리팹 없음) — GiftBundlePopup 과 같은 방식.
    /// </summary>
    public class SettingsPopup : SingletonBehaviour<SettingsPopup>
    {
        public Font font;

        GameObject root;
        readonly List<Image> levelButtons = new List<Image>();
        readonly List<Image> timeScaleButtonImgs = new List<Image>();
        Text previewText;

        public bool IsOpen => root != null && root.activeSelf;

        static readonly Color BoxColor = new Color(0.14f, 0.1f, 0.08f, 0.98f);
        static readonly Color TextColor = new Color(1f, 0.95f, 0.85f);
        static readonly Color DimText = new Color(0.82f, 0.78f, 0.7f);
        static readonly Color ButtonOff = new Color(0.32f, 0.25f, 0.19f, 1f);
        static readonly Color ButtonOn = new Color(0.78f, 0.58f, 0.25f, 1f);
        static readonly Color TimeScaleOff = new Color(0.22f, 0.38f, 0.34f, 1f);
        static readonly Color TimeScaleOn = new Color(0.85f, 0.55f, 0.15f, 0.95f);
        static readonly float[] TimeScales = { 2f, 4f, 8f, 16f };
        const float SkipMinutes = 20f;

        protected override void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            base.Awake();
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

        void ChooseTimeScale(float scale)
        {
            float cur = AppSession.DebugTimeScale;
            float next = Mathf.Approximately(cur, scale) ? 1f : scale;
            AppSession.SetDebugTimeScale(next);
            RefreshTimeScaleButtons();
            WorldToast.Show(next <= 1.001f ? "시간 1배 (보통)" : $"시간 {next:0}배속");
        }

        void SkipTwentyMinutes()
        {
            AppSession.DebugAdvanceMinutes(SkipMinutes);
            WorldToast.Show($"+{SkipMinutes:0}분 진행");
        }

        void Refresh()
        {
            for (int i = 0; i < levelButtons.Count; i++)
                levelButtons[i].color = i == UiTextScale.Level ? ButtonOn : ButtonOff;
            if (previewText != null)
                previewText.text = $"지금: {UiTextScale.LevelNames[UiTextScale.Level]} — 요괴들이 마당에서 기다리고 있어요.";
            RefreshTimeScaleButtons();
        }

        void RefreshTimeScaleButtons()
        {
            float cur = AppSession.DebugTimeScale;
            for (int i = 0; i < timeScaleButtonImgs.Count; i++)
            {
                if (i >= TimeScales.Length) break;
                timeScaleButtonImgs[i].color =
                    Mathf.Approximately(cur, TimeScales[i]) ? TimeScaleOn : TimeScaleOff;
            }
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

            var box = MakeRect("Box", rootRt, Vector2.zero, new Vector2(760, 920));
            box.gameObject.AddComponent<Image>().color = BoxColor;
            box.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // 상자 안 탭은 닫지 않게

            MakeText(box, "설정", 44, new Vector2(0, 400), new Vector2(700, 70));
            MakeText(box, "글자 크기", 32, new Vector2(0, 320), new Vector2(700, 56));

            levelButtons.Clear();
            int n = UiTextScale.LevelNames.Length;
            float w = 160f, gap = 16f, total = n * w + (n - 1) * gap;
            for (int i = 0; i < n; i++)
            {
                int level = i;
                float x = -total / 2f + w / 2f + i * (w + gap);
                var b = MakeRect("Level" + i, box, new Vector2(x, 235), new Vector2(w, 80));
                var img = b.gameObject.AddComponent<Image>();
                img.color = ButtonOff;
                var btn = b.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => Choose(level));
                MakeText(b, UiTextScale.LevelNames[i], 28, Vector2.zero, new Vector2(w, 80));
                levelButtons.Add(img);
            }

            previewText = MakeText(box, "", 26, new Vector2(0, 140), new Vector2(700, 90));
            previewText.color = DimText;

            MakeText(box, "시간 (테스트)", 32, new Vector2(0, 50), new Vector2(700, 56));

            timeScaleButtonImgs.Clear();
            float tw = 120f, tgap = 14f;
            float tTotal = TimeScales.Length * tw + (TimeScales.Length - 1) * tgap;
            for (int i = 0; i < TimeScales.Length; i++)
            {
                float scale = TimeScales[i];
                float x = -tTotal / 2f + tw / 2f + i * (tw + tgap);
                var b = MakeRect("TimeScale" + scale, box, new Vector2(x, -25), new Vector2(tw, 72));
                var img = b.gameObject.AddComponent<Image>();
                img.color = TimeScaleOff;
                var btn = b.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => ChooseTimeScale(scale));
                MakeText(b, $"{scale:0}x", 28, Vector2.zero, new Vector2(tw, 72));
                timeScaleButtonImgs.Add(img);
            }

            var skip = MakeRect("Skip20", box, new Vector2(0, -115), new Vector2(280, 72));
            var skipImg = skip.gameObject.AddComponent<Image>();
            skipImg.color = new Color(0.25f, 0.42f, 0.5f, 1f);
            var skipBtn = skip.gameObject.AddComponent<Button>();
            skipBtn.targetGraphic = skipImg;
            skipBtn.onClick.AddListener(SkipTwentyMinutes);
            MakeText(skip, $"+{SkipMinutes:0}분", 28, Vector2.zero, new Vector2(280, 72));

            MakeText(box, "데이터 (테스트)", 32, new Vector2(0, -185), new Vector2(700, 56));

            var reset = MakeRect("Reset", box, new Vector2(-150, -260), new Vector2(260, 72));
            var resetImg = reset.gameObject.AddComponent<Image>();
            resetImg.color = new Color(0.55f, 0.18f, 0.16f, 0.95f);
            var resetBtn = reset.gameObject.AddComponent<Button>();
            resetBtn.targetGraphic = resetImg;
            resetBtn.onClick.AddListener(OnResetClicked);
            MakeText(reset, "초기화", 28, Vector2.zero, new Vector2(260, 72));

            var clear = MakeRect("ClearCache", box, new Vector2(150, -260), new Vector2(260, 72));
            var clearImg = clear.gameObject.AddComponent<Image>();
            clearImg.color = new Color(0.35f, 0.22f, 0.45f, 0.95f);
            var clearBtn = clear.gameObject.AddComponent<Button>();
            clearBtn.targetGraphic = clearImg;
            clearBtn.onClick.AddListener(OnClearCacheClicked);
            MakeText(clear, "캐시 날리기", 28, Vector2.zero, new Vector2(260, 72));

            var ok = MakeRect("Close", box, new Vector2(0, -360), new Vector2(240, 72));
            var okImg = ok.gameObject.AddComponent<Image>();
            okImg.color = new Color(0.3f, 0.5f, 0.45f, 1f);
            var okBtn = ok.gameObject.AddComponent<Button>();
            okBtn.targetGraphic = okImg;
            okBtn.onClick.AddListener(Close);
            MakeText(ok, "닫기", 30, Vector2.zero, new Vector2(240, 72));

            root.SetActive(false);
        }

        static void OnResetClicked()
        {
            GameSaveService.DeleteSave();
            ReloadActiveScene();
        }

        static void OnClearCacheClicked()
        {
            GameSaveService.ClearAllLocalData();
#if UNITY_WEBGL && !UNITY_EDITOR
            YogoeClearBrowserCacheAndReload();
#else
            ReloadActiveScene();
#endif
        }

        static void ReloadActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0)
                SceneManager.LoadScene(scene.buildIndex);
            else
                SceneManager.LoadScene(scene.name);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void YogoeClearBrowserCacheAndReload();
#endif

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
