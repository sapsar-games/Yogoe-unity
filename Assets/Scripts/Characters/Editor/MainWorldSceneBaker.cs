using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yoegoe.Bootstrap;
using Yoegoe.Bootstrap.EditorTools;
using Yoegoe.Data;

namespace Yoegoe.Characters.EditorTools
{
    /// <summary>
    /// Main 씬에 맵 배경·기물 Prefab·버드나무·PropManager를 Edit 모드 배치.
    /// 메뉴: Yoegoe/Place Map & Props In Main Scene
    /// </summary>
    public static class MainWorldSceneBaker
    {
        const string MainScenePath = BootstrapPrefabBaker.MainScenePath;
        const string MapFolder = "Assets/Prefabs/Map";
        const string PropsFolder = PropPrefabBaker.PrefabFolder;
        const string LayoutPath = "Assets/Resources/PropLayoutSettings.asset";

        [MenuItem("Yoegoe/Place Map & Props In Main Scene")]
        public static void PlaceFromMenu()
        {
            int n = PlaceAll();
            Debug.Log($"[MainWorldSceneBaker] Main 씬에 맵·기물 {n}개 배치 완료. Scene 뷰에서 드래그로 위치를 조절하세요.");
        }

        /// <summary>배치 모드용.</summary>
        public static void PlaceAllBatch()
        {
            PlaceAll();
            AssetDatabase.SaveAssets();
        }

        public static int PlaceAll()
        {
            PropPrefabBaker.BakeAll();

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            var main = Object.FindAnyObjectByType<Yoegoe.Main>(FindObjectsInactive.Include);
            if (main == null)
            {
                Debug.LogError("[MainWorldSceneBaker] Main 컴포넌트가 없습니다.");
                return 0;
            }

            var scale = main.artScale != null ? main.artScale : ArtScaleSettings.GetOrDefault();
            float mapScale = Mathf.Max(0.01f, scale.mapScale);

            EnsurePropManager();
            EnsureMapBackgrounds(main, scale, mapScale);
            int propCount = EnsureProps(main, mapScale, scale);
            EnsureMeritWillow(main, mapScale, scale);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return propCount;
        }

        static void EnsurePropManager()
        {
            if (Object.FindAnyObjectByType<PropManager>(FindObjectsInactive.Include) != null) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootstrapPrefabBaker.PropManagerPrefabPath);
            GameObject go;
            if (prefab != null)
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            else
            {
                go = new GameObject("PropManager");
                go.AddComponent<PropManager>();
            }
            Undo.RegisterCreatedObjectUndo(go, "Place PropManager Prefab");
        }

        static void EnsureMapBackgrounds(Yoegoe.Main main, ArtScaleSettings scale, float mapScale)
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(MapFolder);

            if (main.overviewBackgroundSprite != null)
            {
                var overview = EnsureNamedBackground(
                    WorldAssembler.OverviewName,
                    main.overviewBackgroundSprite,
                    new Vector3(main.overviewOffset.x * mapScale, main.overviewOffset.y * mapScale, 1f),
                    mapScale,
                    scale.backgroundSort,
                    $"{MapFolder}/Background_Overview.prefab");
                if (overview != null)
                    Undo.RegisterCreatedObjectUndo(overview, "Place Overview");
            }

            if (main.playfieldSprite != null)
            {
                var playfield = EnsureNamedBackground(
                    WorldAssembler.PlayfieldName,
                    main.playfieldSprite,
                    new Vector3(0f, 0f, 0.9f),
                    mapScale,
                    scale.backgroundSort + 1,
                    $"{MapFolder}/Background_Playfield.prefab");
                if (playfield != null)
                    Undo.RegisterCreatedObjectUndo(playfield, "Place Playfield");
            }
        }

        static GameObject EnsureNamedBackground(
            string name, Sprite sprite, Vector3 worldPos, float mapScale, int sort, string prefabPath)
        {
            var existing = GameObject.Find(name);
            if (existing != null)
            {
                existing.transform.position = worldPos;
                existing.transform.localScale = new Vector3(mapScale, mapScale, 1f);
                var sr = existing.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sprite = sprite;
                    sr.sortingOrder = sort;
                }
                return null;
            }

            var go = new GameObject(name);
            go.transform.position = worldPos;
            go.transform.localScale = new Vector3(mapScale, mapScale, 1f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sort;

            PrefabUtility.SaveAsPrefabAssetAndConnect(go, prefabPath, InteractionMode.AutomatedAction);
            return go;
        }

        static int EnsureProps(Yoegoe.Main main, float mapScale, ArtScaleSettings scale)
        {
            var layout = main.propLayout != null
                ? main.propLayout
                : AssetDatabase.LoadAssetAtPath<PropLayoutSettings>(LayoutPath);
            if (layout?.placements == null) return 0;

            // 기존 씬 기물 정리 후 재배치 (메뉴 재실행 시)
            foreach (var old in Object.FindObjectsByType<PropSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (old == null) continue;
                Undo.DestroyObjectImmediate(old.gameObject);
            }

            int count = 0;
            for (int i = 0; i < layout.placements.Length; i++)
            {
                var place = layout.placements[i];
                if (place?.data == null) continue;

                PropSlot prefab = place.prefab;
                if (prefab == null)
                {
                    string path = $"{PropsFolder}/{Sanitize(place.data.name)}.prefab";
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    prefab = go != null ? go.GetComponent<PropSlot>() : null;
                }
                if (prefab == null)
                {
                    Debug.LogWarning($"[MainWorldSceneBaker] Prefab 없음: {place.data.name}");
                    continue;
                }

                var instance = (PropSlot)PrefabUtility.InstantiatePrefab(prefab);
                string label = !string.IsNullOrEmpty(place.data.displayName)
                    ? place.data.displayName
                    : place.data.name;
                instance.gameObject.name = "Prop_" + label;
                Vector3 world = new Vector3(
                    place.position.x * mapScale,
                    place.position.y * mapScale,
                    place.position.z);
                instance.transform.position = world;
                if (instance.data == null) instance.data = place.data;

                var sr = instance.GetComponent<SpriteRenderer>();
                if (sr != null)
                    sr.sortingOrder = scale.SortOrderForProp(world.y);

                Undo.RegisterCreatedObjectUndo(instance.gameObject, "Place Prop");
                count++;
            }

            return count;
        }

        static void EnsureMeritWillow(Yoegoe.Main main, float mapScale, ArtScaleSettings scale)
        {
            var existing = Object.FindAnyObjectByType<MeritWillow>(FindObjectsInactive.Include);
            var layout = main.propLayout != null
                ? main.propLayout
                : AssetDatabase.LoadAssetAtPath<PropLayoutSettings>(LayoutPath);
            Vector3 pos = layout != null
                ? new Vector3(layout.willowPosition.x * mapScale, layout.willowPosition.y * mapScale, 0f)
                : Vector3.zero;

            if (existing != null)
            {
                existing.transform.position = pos;
                return;
            }

            var go = new GameObject("MeritWillow");
            go.transform.position = pos;
            go.AddComponent<MeritWillow>();
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = scale.SortOrderForProp(pos.y);
            Undo.RegisterCreatedObjectUndo(go, "Place MeritWillow");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Prop";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
