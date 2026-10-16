using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Yoegoe.UI.EditorTools
{
    /// <summary>
    /// GameHud.prefab의 ShopButton/YutButton에 UiNavigationButton을 붙이고
    /// Button.OnClick()을 그 Invoke()에 영구 연결한다 (프리팹 전체를 다시 짜지 않고 기존 하이어라키를 그대로 둔 채
    /// 두 버튼만 건드리는 증분 수정 — ScrollScreenUI 사고처럼 프리팹을 통째로 재생성하지 않음).
    /// 메뉴: Yoegoe/Bake GameHud Navigation Buttons
    /// </summary>
    public static class GameHudNavigationBaker
    {
        const string PrefabPath = "Assets/Prefabs/UI/GameHud.prefab";

        [MenuItem("Yoegoe/Bake GameHud Navigation Buttons")]
        public static void Bake()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Wire(root.transform, "ShopButton", UiNavigationTarget.ShopScreen);
                Wire(root.transform, "YutButton", UiNavigationTarget.YutScreen);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[GameHudNavigationBaker] ShopButton/YutButton 셀프와이어링 완료.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void Wire(Transform root, string childName, UiNavigationTarget wantedTarget)
        {
            var found = root.Find(childName);
            if (found == null)
            {
                Debug.LogError($"[GameHudNavigationBaker] '{childName}'을 찾지 못했습니다.");
                return;
            }

            var button = found.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogError($"[GameHudNavigationBaker] '{childName}'에 Button 컴포넌트가 없습니다.");
                return;
            }

            var nav = found.GetComponent<UiNavigationButton>();
            if (nav == null) nav = found.gameObject.AddComponent<UiNavigationButton>();

            var so = new SerializedObject(nav);
            so.FindProperty("target").enumValueIndex = (int)wantedTarget;
            so.ApplyModifiedPropertiesWithoutUndo();

            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, nav.Invoke);
        }
    }
}
