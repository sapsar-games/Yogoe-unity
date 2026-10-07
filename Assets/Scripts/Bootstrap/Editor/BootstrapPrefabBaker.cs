using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yoegoe.Characters;

namespace Yoegoe.Bootstrap.EditorTools
{
    /// <summary>
    /// Main · PropManager → Assets/Prefabs/Bootstrap/*.prefab 저장 후 Main 씬에 Prefab 인스턴스로 연결.
    /// 메뉴: Yoegoe/Bake Bootstrap Prefabs (Main · PropManager)
    /// </summary>
    public static class BootstrapPrefabBaker
    {
        public const string PrefabFolder = "Assets/Prefabs/Bootstrap";
        public const string MainPrefabPath = PrefabFolder + "/Main.prefab";
        public const string PropManagerPrefabPath = PrefabFolder + "/PropManager.prefab";
        public const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Yoegoe/Bake Bootstrap Prefabs (Main · PropManager)")]
        public static void BakeFromMenu()
        {
            BakeAndConnectInMainScene();
            Debug.Log("[BootstrapPrefabBaker] Main · PropManager Prefab 저장 및 Main 씬 연결 완료.");
        }

        /// <summary>배치 모드: -executeMethod Yoegoe.Bootstrap.EditorTools.BootstrapPrefabBaker.BakeAllBatch</summary>
        public static void BakeAllBatch()
        {
            BakeAndConnectInMainScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void BakeAndConnectInMainScene()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            SaveOrCreateConnected(typeof(Yoegoe.Main), "Main", MainPrefabPath, go => go.AddComponent<Yoegoe.Main>());
            SaveOrCreateConnected(typeof(PropManager), "PropManager", PropManagerPrefabPath, go => go.AddComponent<PropManager>());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>씬에 Prefab 인스턴스가 없으면 Prefab에서 배치. 추가했으면 true.</summary>
        public static bool EnsurePrefabInstanceInOpenScene<T>(string prefabPath, string fallbackName)
            where T : Component
        {
            if (Object.FindAnyObjectByType<T>(FindObjectsInactive.Include) != null)
                return false;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                PrefabUtility.InstantiatePrefab(prefab);
                return true;
            }

            var go = new GameObject(fallbackName);
            go.AddComponent<T>();
            return true;
        }

        static void SaveOrCreateConnected(
            System.Type componentType, string name, string prefabPath, System.Action<GameObject> addComponent)
        {
            var existing = Object.FindObjectsByType(componentType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            GameObject go = null;
            if (existing != null && existing.Length > 0)
                go = ((Component)existing[0]).gameObject;

            if (go == null)
            {
                go = new GameObject(name);
                addComponent(go);
                Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            }

            PrefabUtility.SaveAsPrefabAssetAndConnect(go, prefabPath, InteractionMode.AutomatedAction);
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
