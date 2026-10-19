using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yoegoe.Characters.EditorTools
{
    /// <summary>
    /// Main 씬 루트가 기물·UI 화면으로 평평하게 다 나열돼 길어지는 문제 — 빈 부모(폴더)로 묶어 정리한다.
    /// 월드 위치는 그대로 유지(worldPositionStays). UI 화면들은 각자 자기 Canvas를 들고 있어
    /// 부모를 바꿔도 화면엔 영향 없다. GameObject.Find·직렬화 참조는 이름/인스턴스 기반이라
    /// 재부모해도 깨지지 않는다.
    /// 메뉴: Yoegoe/Group Main Hierarchy
    /// </summary>
    public static class MainHierarchyGrouper
    {
        static readonly string[] BackgroundNames =
        {
            "Background_Overview", "Background_Playfield",
        };

        static readonly string[] PropNames =
        {
            "Prop_화덕", "Prop_옹달샘", "Prop_떡절구", "Prop_북제단", "Prop_남제단",
            "Prop_나루터", "Prop_정자", "Prop_평상",
            "Prop_멧돼지 앞산", "Prop_꿀벌의 숲", "Prop_파도치는 섬",
            "Prop_첫걸음 들판", "Prop_허수아비 논밭", "Prop_절벽의 약초골",
            "Prop_사냥터", "Prop_채집터", "PropManager",
        };

        static readonly string[] UiNames =
        {
            "PropUpgradePopup", "ConfirmPopup", "CodexScreen", "GameHud",
            "ShopScreen", "YutScreen", "AttendanceScreen", "DetailScreen",
            "GongyangganScreen", "FeedPopup", "PropPanel",
        };

        [MenuItem("Yoegoe/Group Main Hierarchy")]
        public static void Group()
        {
            int moved = 0;
            moved += GroupInto("_Backgrounds", BackgroundNames);
            moved += GroupInto("_Props", PropNames);
            moved += GroupInto("_UI", UiNames);
            Debug.Log($"[MainHierarchyGrouper] {moved}개 오브젝트를 폴더로 정리했습니다. " +
                "씬이 괜찮아 보이면 Cmd+S로 저장하세요. (위에 경고가 떴다면 그 이름은 못 찾은 것)");
        }

        static int GroupInto(string folderName, string[] names)
        {
            var scene = SceneManager.GetActiveScene();
            var folder = GameObject.Find("/" + folderName);
            if (folder == null)
            {
                folder = new GameObject(folderName);
                Undo.RegisterCreatedObjectUndo(folder, "Group Main Hierarchy");
            }

            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == folder) continue;
                foreach (var n in names)
                {
                    if (root.name == n)
                    {
                        Undo.SetTransformParent(root.transform, folder.transform, "Group Main Hierarchy");
                        count++;
                        break;
                    }
                }
            }

            foreach (var n in names)
            {
                bool found = false;
                foreach (Transform t in folder.transform)
                    if (t.name == n) { found = true; break; }
                if (!found)
                    Debug.LogWarning($"[MainHierarchyGrouper] '{n}'을 씬 루트에서 못 찾았습니다 (이름이 다르거나 이미 다른 곳에 있을 수 있음).");
            }

            return count;
        }
    }
}
