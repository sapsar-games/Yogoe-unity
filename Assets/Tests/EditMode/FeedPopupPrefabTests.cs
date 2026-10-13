using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Yoegoe.UI;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>먹이기 창 Prefab 셸 배선 (v1.3 FeedPopup).</summary>
    public class FeedPopupPrefabTests
    {
        const string PrefabPath = "Assets/Prefabs/UI/FeedPopup.prefab";

        [Test]
        public void Prefab_HasAllReferencesWired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            var popup = prefab.GetComponent<FeedPopup>();
            Assert.IsNotNull(popup);

            var so = new SerializedObject(popup);
            foreach (var field in new[]
            {
                "root", "titleText", "subText", "giText", "statusText", "anyLabel", "emptyText",
                "giFill", "anyButton", "foodOnlyButton", "inclButton", "goKitchenButton",
                "closeButton", "dimButton", "listContent", "scroll",
            })
                Assert.IsNotNull(so.FindProperty(field).objectReferenceValue, field);
        }

        [Test]
        public void MainScene_HasPopupInstance()
        {
            string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
            string scene = System.IO.File.ReadAllText("Assets/Scenes/Main.unity");
            StringAssert.Contains(guid, scene);
        }
    }
}
