using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Yoegoe.UI;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>기물 창 Prefab 셸 배선 (v1.3 PropPanel).</summary>
    public class PropPanelPrefabTests
    {
        const string PrefabPath = "Assets/Prefabs/UI/PropPanel.prefab";

        [Test]
        public void Prefab_HasAllReferencesWired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            var panel = prefab.GetComponent<PropPanel>();
            Assert.IsNotNull(panel);

            var so = new SerializedObject(panel);
            foreach (var field in new[] { "root", "body", "titleText", "subText", "dimButton", "closeButton" })
                Assert.IsNotNull(so.FindProperty(field).objectReferenceValue, field);
        }

        [Test]
        public void MainScene_HasPanelInstance()
        {
            string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
            string scene = System.IO.File.ReadAllText("Assets/Scenes/Main.unity");
            StringAssert.Contains(guid, scene);
        }
    }
}
