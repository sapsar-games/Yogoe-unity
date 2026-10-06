using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.UI;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>요리판 (19장): 재료 20 + 빈칸 5, 모자라면 아래부터 채우고 확인 팝업. 재료는 시작할 때만 차감.</summary>
    public class CookingBoardTests
    {
        GameObject ecoGO;
        GameEconomy eco;

        [SetUp]
        public void SetUp()
        {
            ecoGO = new GameObject("Eco");
            eco = ecoGO.AddComponent<GameEconomy>();
            eco.BecomeInstance();
            eco.ApplyStartingState(ScriptableObject.CreateInstance<StartingStateSettings>());
            for (int i = 0; i < (int)CookingIngredientId.Count; i++)
            {
                var id = (CookingIngredientId)i;
                eco.TrySpendMaterial(id, eco.GetMaterialCount(id));
            }
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(ecoGO);

        static List<CookingIngredientId> Pool(int n)
        {
            var list = new List<CookingIngredientId>();
            for (int i = 0; i < n; i++) list.Add(CookingIngredientId.Rice);
            return list;
        }

        static int Count(CookingIngredientId?[,] grid)
        {
            int n = 0;
            foreach (var c in grid) if (c.HasValue) n++;
            return n;
        }

        [Test]
        public void FullPool_PlacesTwentyWithFiveEmpties()
        {
            Random.InitState(3);
            var grid = CookingSession.LayoutBoard(Pool(40), Random.Range);
            Assert.AreEqual(CookingSession.FullBoardMaterials, Count(grid));
        }

        [Test]
        public void ShortPool_FillsFromBottom_TopRowsStayEmpty()
        {
            Random.InitState(5);
            for (int k = 0; k < 50; k++)
            {
                // 재료 5 + 빈칸 5 = 10칸 → 아래 두 줄(y=4,3)만 쓰인다
                var grid = CookingSession.LayoutBoard(Pool(5), Random.Range);
                Assert.AreEqual(5, Count(grid));
                for (int y = 0; y < 3; y++)
                for (int x = 0; x < CookingSession.GridSize; x++)
                    Assert.IsFalse(grid[x, y].HasValue, $"({x},{y})");
            }
        }

        [Test]
        public void Prepare_DoesNotSpend_StartSpends()
        {
            eco.AddMaterial(CookingIngredientId.Rice, 3);
            eco.AddMaterial(CookingIngredientId.Grain, 3);

            var session = new CookingSession();
            session.Prepare(CookingCharmType.None);
            Assert.AreEqual(6, session.MaterialsOnBoard);
            Assert.IsTrue(session.IsShortBoard);
            Assert.AreEqual(3, eco.GetMaterialCount(CookingIngredientId.Rice), "미리보기는 차감 없음");

            // 다시 깔아도(부적 변경 등) 차감 없음
            session.Prepare(CookingCharmType.None);
            Assert.AreEqual(3, eco.GetMaterialCount(CookingIngredientId.Rice));

            Assert.IsTrue(session.StartRound());
            Assert.AreEqual(0, eco.GetMaterialCount(CookingIngredientId.Rice));
            Assert.AreEqual(0, eco.GetMaterialCount(CookingIngredientId.Grain));
        }

        [Test]
        public void NoMaterials_CannotStart()
        {
            eco.AddMaterial(CookingIngredientId.Rice, 1);
            var session = new CookingSession();
            session.Prepare(CookingCharmType.None);
            Assert.IsFalse(session.CanStart);
            Assert.IsFalse(session.StartRound());
            Assert.AreEqual(1, eco.GetMaterialCount(CookingIngredientId.Rice));
        }

        [Test]
        public void StartRound_FailsWithoutSpending_WhenInventoryShrank()
        {
            eco.AddMaterial(CookingIngredientId.Rice, 4);
            var session = new CookingSession();
            session.Prepare(CookingCharmType.None);
            eco.TrySpendMaterial(CookingIngredientId.Rice, 2);
            Assert.IsFalse(session.StartRound());
            Assert.AreEqual(2, eco.GetMaterialCount(CookingIngredientId.Rice));
        }

        CookingSession StartedFruitBoard(CookingCharmType charm)
        {
            eco.AddMaterial(CookingIngredientId.Fruit, 20); // 곶감 = 과실 2 → 이웃한 과실만 있으면 계속 만들 수 있는 판
            var session = new CookingSession();
            session.Prepare(charm);
            Assert.IsTrue(session.StartRound());
            return session;
        }

        [Test]
        public void TimeUp_AsksExtend_UnlimitedTimes_ThenFinishes()
        {
            var session = StartedFruitBoard(CookingCharmType.None);
            int timeUps = 0;
            session.TimeUp += () => timeUps++;

            for (int k = 1; k <= 3; k++)
            {
                session.Tick(CookingSession.BaseSeconds + 1f);
                Assert.IsTrue(session.AwaitingExtend);
                Assert.IsFalse(session.Finished, "연장을 묻는 동안은 정산 전");
                Assert.AreEqual(k, timeUps);
                Assert.IsTrue(session.ExtendByAd());
                Assert.IsTrue(session.Running);
                Assert.AreEqual(CookingSession.AdExtendSeconds, session.TimeLeft, 0.001f);
            }

            session.Tick(CookingSession.AdExtendSeconds + 1f);
            session.FinishAfterTimeUp();
            Assert.IsTrue(session.Finished);
            Assert.IsFalse(session.ExtendByAd());
        }

        [Test]
        public void TimeUp_RecycleCharm_FinishesWithoutAsking()
        {
            var session = StartedFruitBoard(CookingCharmType.Recycle);
            session.Tick(CookingSession.BaseSeconds + 1f);
            Assert.IsFalse(session.AwaitingExtend);
            Assert.IsTrue(session.Finished);
        }

        [Test]
        public void ConfirmPrefab_HasAllReferencesWired_AndIsInMainScene()
        {
            const string path = "Assets/Prefabs/UI/ConfirmPopup.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            var so = new SerializedObject(prefab.GetComponent<ConfirmPopup>());
            foreach (var field in new[] { "panel", "messageText", "yesButton", "yesLabel", "noButton", "noLabel", "dimButton" })
                Assert.IsNotNull(so.FindProperty(field).objectReferenceValue, field);
            StringAssert.Contains(AssetDatabase.AssetPathToGUID(path), System.IO.File.ReadAllText("Assets/Scenes/Main.unity"));
        }
    }
}
