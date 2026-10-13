using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Bootstrap.EditorTools;

namespace Yoegoe.UI.EditorTools
{
    /// <summary>
    /// FeedPopup 셸 → Assets/Prefabs/UI/FeedPopup.prefab 저장 후 Main 씬에 Prefab 인스턴스 연결.
    /// 메뉴: Yoegoe/Bake FeedPopup Prefab
    /// </summary>
    public static class FeedPopupPrefabBaker
    {
        public const string PrefabPath = "Assets/Prefabs/UI/FeedPopup.prefab";

        static readonly Color BoxColor = new Color(0.14f, 0.1f, 0.08f, 0.98f);
        static readonly Color TextColor = new Color(1f, 0.95f, 0.85f);
        static readonly Color DimText = new Color(0.85f, 0.78f, 0.66f);
        static readonly Color GoldColor = new Color(0.95f, 0.75f, 0.25f, 1f);
        static readonly Color SegOn = new Color(0.78f, 0.58f, 0.25f, 1f);
        static readonly Color SegOff = new Color(0.32f, 0.25f, 0.19f, 1f);

        [MenuItem("Yoegoe/Bake FeedPopup Prefab")]
        public static void BakeFromMenu()
        {
            BakeAndConnectInMainScene();
            Debug.Log("[FeedPopupPrefabBaker] FeedPopup Prefab 저장 및 Main 씬 연결 완료.");
        }

        public static void BakeAndConnectInMainScene()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = BuildShell(font);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var scene = EditorSceneManager.OpenScene(BootstrapPrefabBaker.MainScenePath, OpenSceneMode.Single);
            var existing = Object.FindAnyObjectByType<FeedPopup>(FindObjectsInactive.Include);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static GameObject BuildShell(Font font)
        {
            var host = new GameObject("FeedPopup");
            var popup = host.AddComponent<FeedPopup>();

            var canvasGO = new GameObject("Canvas_Feed", typeof(RectTransform));
            canvasGO.transform.SetParent(host.transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 880;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            var root = new GameObject("Panel", typeof(RectTransform));
            var rootRt = (RectTransform)root.transform;
            rootRt.SetParent(canvasGO.transform, false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            var dim = root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            var dimBtn = root.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.targetGraphic = dim;

            var box = Rect("Box", rootRt, Vector2.zero, new Vector2(920, 1380));
            var boxImg = box.gameObject.AddComponent<Image>();
            boxImg.color = BoxColor;
            var boxBtn = box.gameObject.AddComponent<Button>();
            boxBtn.transition = Selectable.Transition.None; // 상자 안 탭은 닫지 않게
            boxBtn.targetGraphic = boxImg;

            var title = TextIn(box, font, "먹이기", new Vector2(0, 620), 44, TextColor, FontStyle.Bold, new Vector2(760, 70));
            var close = Rect("Close", box, new Vector2(400, 625), new Vector2(80, 80));
            var closeImg = close.gameObject.AddComponent<Image>();
            closeImg.color = new Color(0.35f, 0.27f, 0.2f);
            var closeBtn = close.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;
            TextIn(close, font, "X", Vector2.zero, 36, TextColor, FontStyle.Bold, new Vector2(80, 80));

            var sub = TextIn(box, font, "", new Vector2(0, 560), 26, DimText, FontStyle.Normal, new Vector2(820, 44));

            var barBg = Rect("GiBar", box, new Vector2(0, 500), new Vector2(780, 30));
            barBg.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.06f, 0.05f, 1f);
            var fillRt = Rect("Fill", barBg, Vector2.zero, Vector2.zero);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0.5f, 1f);
            fillRt.pivot = new Vector2(0, 0.5f);
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var giFill = fillRt.gameObject.AddComponent<Image>();
            giFill.color = new Color(0.45f, 0.75f, 0.4f, 1f);
            var giText = TextIn(box, font, "", new Vector2(0, 455), 26, TextColor, FontStyle.Normal, new Vector2(820, 40));

            var anyRt = Rect("AnyButton", box, new Vector2(-170, 355), new Vector2(520, 120));
            var anyImg = anyRt.gameObject.AddComponent<Image>();
            anyImg.color = new Color(0.3f, 0.5f, 0.45f, 1f);
            var anyButton = anyRt.gameObject.AddComponent<Button>();
            anyButton.targetGraphic = anyImg;
            anyButton.transition = Selectable.Transition.ColorTint;
            var anyLabel = TextIn(anyRt, font, "", Vector2.zero, 28, TextColor, FontStyle.Normal, new Vector2(520, 120));
            anyRt.gameObject.AddComponent<FeedHoldButton>();

            var foodOnlyButton = SegButton(box, font, "음식만", new Vector2(250, 385), new Vector2(300, 54), SegOn);
            var inclButton = SegButton(box, font, "공양물 포함", new Vector2(250, 325), new Vector2(300, 54), SegOff);

            var statusText = TextIn(box, font, "", new Vector2(0, 262), 26, GoldColor, FontStyle.Normal, new Vector2(820, 44));

            // 목록 (스크롤)
            var view = Rect("List", box, new Vector2(0, -205), new Vector2(840, 860));
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.2f);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var listContent = Rect("Content", view, Vector2.zero, Vector2.zero);
            listContent.anchorMin = new Vector2(0, 1);
            listContent.anchorMax = new Vector2(1, 1);
            listContent.pivot = new Vector2(0.5f, 1);
            listContent.offsetMin = listContent.offsetMax = Vector2.zero;
            var grid = listContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(190, 200);
            grid.spacing = new Vector2(14, 14);
            grid.padding = new RectOffset(12, 12, 12, 12);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = listContent;
            scroll.viewport = view;

            var emptyText = TextIn(view, font, "", new Vector2(0, 60), 28, DimText, FontStyle.Normal, new Vector2(760, 120));
            var kitchenRt = Rect("GoKitchenButton", view, new Vector2(0, -60), new Vector2(260, 70));
            var kitchenImg = kitchenRt.gameObject.AddComponent<Image>();
            kitchenImg.color = new Color(0.55f, 0.4f, 0.2f, 1f);
            var goKitchenButton = kitchenRt.gameObject.AddComponent<Button>();
            goKitchenButton.targetGraphic = kitchenImg;
            TextIn(kitchenRt, font, "화덕으로", Vector2.zero, 28, TextColor, FontStyle.Bold, new Vector2(260, 70));

            TextIn(box, font, "누르면 한 그릇 · 꾹 누르면 기력이 가득 찰 때까지\n아무거나는 황금 요리와 혼령이 청한 요리를 빼요",
                new Vector2(0, -660), 22, DimText, FontStyle.Normal, new Vector2(840, 60));

            root.SetActive(false);

            var so = new SerializedObject(popup);
            so.FindProperty("root").objectReferenceValue = root;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("subText").objectReferenceValue = sub;
            so.FindProperty("giText").objectReferenceValue = giText;
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.FindProperty("anyLabel").objectReferenceValue = anyLabel;
            so.FindProperty("emptyText").objectReferenceValue = emptyText;
            so.FindProperty("giFill").objectReferenceValue = giFill;
            so.FindProperty("anyButton").objectReferenceValue = anyButton;
            so.FindProperty("foodOnlyButton").objectReferenceValue = foodOnlyButton;
            so.FindProperty("inclButton").objectReferenceValue = inclButton;
            so.FindProperty("goKitchenButton").objectReferenceValue = goKitchenButton;
            so.FindProperty("closeButton").objectReferenceValue = closeBtn;
            so.FindProperty("dimButton").objectReferenceValue = dimBtn;
            so.FindProperty("listContent").objectReferenceValue = listContent;
            so.FindProperty("scroll").objectReferenceValue = scroll;
            so.ApplyModifiedPropertiesWithoutUndo();

            return host;
        }

        static Button SegButton(Transform parent, Font font, string label, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Rect("SegButton", parent, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            TextIn(rt, font, label, Vector2.zero, 26, TextColor, FontStyle.Normal, size);
            return b;
        }

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

        static Text TextIn(Transform parent, Font font, string msg, Vector2 pos, int size, Color color, FontStyle style, Vector2 box)
        {
            var t = Rect("Text", parent, pos, box).gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.text = msg;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folderName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
