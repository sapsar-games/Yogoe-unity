using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yoegoe.Bootstrap.EditorTools;

namespace Yoegoe.UI.EditorTools
{
    /// <summary>
    /// Main 씬에 부트스트랩 Prefab(<see cref="Yoegoe.Main"/>)이 없으면 배치한다.
    /// Prefab: Assets/Prefabs/Bootstrap/Main.prefab
    /// </summary>
    public static class MainSceneBootstrap
    {
        public const string MainScenePath = BootstrapPrefabBaker.MainScenePath;

        [MenuItem("Yoegoe/Ensure Main Bootstrap (In Main Scene)")]
        public static void EnsureFromMenu()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            if (EnsureInOpenScene())
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[MainSceneBootstrap] Main Prefab을 씬에 배치하고 저장했습니다.");
            }
            else
                Debug.Log("[MainSceneBootstrap] Main 부트스트랩이 이미 있습니다.");
        }

        /// <summary>현재 열린 씬 기준. 추가했으면 true.</summary>
        public static bool EnsureInOpenScene()
        {
            return BootstrapPrefabBaker.EnsurePrefabInstanceInOpenScene<Yoegoe.Main>(
                BootstrapPrefabBaker.MainPrefabPath, "Main");
        }
    }
}
