using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Bootstrap.EditorTools;
using Yoegoe.UI;

namespace Yoegoe.UI.EditorTools
{
    /// <summary>
    /// PropPanel 셸 → Assets/Prefabs/UI/PropPanel.prefab 저장 후 Main 씬에 Prefab 인스턴스 연결.
    /// 메뉴: Yoegoe/Bake PropPanel Prefab
    /// </summary>
    public static class PropPanelPrefabBaker
    {
        public const string PrefabPath = "Assets/Prefabs/UI/PropPanel.prefab";

        static readonly Color BoxColor = new Color(0.14f, 0.1f, 0.08f, 0.98f);
        static readonly Color TextColor = new Color(1f, 0.95f, 0.85f);
        static readonly Color DimText = new Color(0.85f, 0.78f, 0.66f);

        [MenuItem("Yoegoe/Bake PropPanel Prefab")]
        public static void BakeFromMenu()
        {
            BakeAndConnectInMainScene();
            Debug.Log("[PropPanelPrefabBaker] PropPanel Prefab 저장 및 Main 씬 연결 완료.");
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
            var existing = Object.FindAnyObjectByType<PropPanel>(FindObjectsInactive.Include);
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
            var host = new GameObject("PropPanel");
            var panel = host.AddComponent<PropPanel>();

            var canvasGO = new GameObject("Canvas_PropPanel", typeof(RectTransform));
            canvasGO.transform.SetParent(host.transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 870;
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

            var box = UiRectUtils.CreateCenteredRect("Box", rootRt, Vector2.zero, new Vector2(940, 1360));
            var boxImg = box.gameObject.AddComponent<Image>();
            boxImg.color = BoxColor;
            var boxBtn = box.gameObject.AddComponent<Button>();
            boxBtn.transition = Selectable.Transition.None;
            boxBtn.targetGraphic = boxImg;

            var title = TextIn(box, font, "", new Vector2(0, 620), 44, TextColor, FontStyle.Bold, new Vector2(700, 70));
            var sub = TextIn(box, font, "", new Vector2(0, 560), 26, DimText, FontStyle.Normal, new Vector2(800, 44));

            var close = UiRectUtils.CreateCenteredRect("Close", box, new Vector2(415, 625), new Vector2(80, 80));
            var closeImg = close.gameObject.AddComponent<Image>();
            closeImg.color = new Color(0.35f, 0.27f, 0.2f);
            var closeBtn = close.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;
            TextIn(close, font, "X", Vector2.zero, 36, TextColor, FontStyle.Bold, new Vector2(80, 80));

            var body = UiRectUtils.CreateCenteredRect("Body", box, Vector2.zero, new Vector2(940, 1360));
            root.SetActive(false);

            var so = new SerializedObject(panel);
            so.FindProperty("root").objectReferenceValue = root;
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("subText").objectReferenceValue = sub;
            so.FindProperty("dimButton").objectReferenceValue = dimBtn;
            so.FindProperty("closeButton").objectReferenceValue = closeBtn;
            so.ApplyModifiedPropertiesWithoutUndo();

            return host;
        }

        static Text TextIn(Transform parent, Font font, string msg, Vector2 pos, int size, Color color, FontStyle style, Vector2 box)
        {
            var t = UiRectUtils.CreateCenteredRect("Text", parent, pos, box).gameObject.AddComponent<Text>();
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
