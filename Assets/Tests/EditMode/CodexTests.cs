using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;
using Yoegoe.UI;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>요리책(도감) 19장: 수집 60 = 음식 36 + 공양물 24, 처음 완성하면 발견, 나가리면 그 판 발견만 다시 잠금.</summary>
    public class CodexTests
    {
        GameObject ecoGO;
        GameEconomy eco;

        /// <summary>레시피 표의 첫 음식 — 시트에서 레시피가 바뀌어도 테스트가 특정 요리 id에 묶이지 않게.</summary>
        static Yoegoe.Cooking.CookingRecipe AnyFood => System.Linq.Enumerable.First(
            Yoegoe.Cooking.CookingRecipeCatalog.Recipes, r => r.Kind == Yoegoe.Cooking.CookingResultKind.Food);
        static string FoodId => AnyFood.Id;

        [SetUp]
        public void SetUp()
        {
            OfferingCatalog.Build(null);
            ecoGO = new GameObject("Eco");
            eco = ecoGO.AddComponent<GameEconomy>();
            eco.BecomeInstance();
            eco.ApplyStartingState(ScriptableObject.CreateInstance<StartingStateSettings>());
            for (int i = 0; i < (int)CookingIngredientId.Count; i++)
            {
                var id = (CookingIngredientId)i;
                eco.TrySpendMaterial(id, eco.GetMaterialCount(id));
            }
            CookingCodex.ResetFromSave(null);
        }

        [TearDown]
        public void TearDown()
        {
            CookingCodex.ResetFromSave(null);
            Object.DestroyImmediate(ecoGO);
        }

        [Test]
        public void Total_IsDistinctRecipeProducts()
        {
            // 수집 수 = 레시피 결과물 수 (시트 recipes 탭 — 지금 60 = 음식 36 + 공양물 24)
            var distinct = CookingRecipeCatalog.Recipes.Select(r => r.Id).Distinct().Count();
            Assert.AreEqual(distinct, CookingCodex.Total);
            Assert.Greater(CookingCodex.Total, 0);
        }

        [Test]
        public void RecipesJson_FollowsRules()
        {
            // 음식 = 재료 2, 공양물 = 재료 3, 같은 조합이 두 요리에 없음
            var seen = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var r in CookingRecipeCatalog.Recipes)
            {
                Assert.AreEqual(r.Kind == CookingResultKind.Food ? 2 : 3, r.Ingredients.Length, r.Id);
                string key = string.Join(",", r.Ingredients);
                if (seen.TryGetValue(key, out var other)) Assert.AreEqual(other, r.Id, "같은 조합: " + key);
                seen[key] = r.Id;
            }
        }

        [Test]
        public void Catalog_LoadsFromJson_SheetEditsChangeMatching()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("bad: 재료"));
            try
            {
                CookingRecipeCatalog.LoadFromJson(
                    "{\"recipes\":[{\"id\":\"newdish\",\"name\":\"새요리\",\"kind\":\"Food\",\"ingredients\":[\"Rice\",\"Seafood\"]}," +
                    "{\"id\":\"bad\",\"name\":\"잘못\",\"kind\":\"Food\",\"ingredients\":[\"Rice\",\"Nope\"]}]}");
                Assert.IsTrue(CookingRecipeCatalog.TryMatch(new[] { CookingIngredientId.Seafood, CookingIngredientId.Rice }, out var r));
                Assert.AreEqual("newdish", r.Id);
                Assert.AreEqual(1, CookingCodex.Total, "잘못된 재료 줄은 건너뜀");
            }
            finally
            {
                CookingRecipeCatalog.LoadFromJson(Resources.Load<TextAsset>(CookingRecipeCatalog.ResourcePath).text);
            }
        }

        [Test]
        public void Discover_GoldenCountsAsBase_AndSaveRoundTrips()
        {
            Assert.IsTrue(CookingCodex.Discover(OfferingCatalog.GoldenIdOf(FoodId)));
            Assert.IsTrue(CookingCodex.IsDiscovered(FoodId));
            Assert.IsFalse(CookingCodex.Discover(FoodId), "이미 발견");
            Assert.IsFalse(CookingCodex.Discover("not_a_recipe"));

            var saved = CookingCodex.CaptureToSave();
            CookingCodex.ResetFromSave(null);
            Assert.AreEqual(0, CookingCodex.DiscoveredCount);
            CookingCodex.ResetFromSave(saved);
            Assert.AreEqual(1, CookingCodex.DiscoveredCount);
        }

        [Test]
        public void ComboText_ListsEveryCombination()
        {
            Assert.AreEqual("쌀 + 잡곡", CookingCodex.ComboText("patteok"));
            StringAssert.Contains(" / ", CookingCodex.ComboText("gogijuk")); // 쌀+새고기 / 쌀+멧돼지고기
        }

        CookingSession StartRiceBeanBoard()
        {
            eco.AddMaterial(CookingIngredientId.Rice, 1);
            eco.AddMaterial(CookingIngredientId.Grain, 1);
            var session = new CookingSession();
            session.Prepare(CookingCharmType.None);
            Assert.IsTrue(session.StartRound());
            return session;
        }

        static bool CompleteRiceBean(CookingSession session)
        {
            (int x, int y)? rice = null, bean = null;
            for (int y = 0; y < CookingSession.GridSize; y++)
            for (int x = 0; x < CookingSession.GridSize; x++)
            {
                if (session.Grid[x, y] == CookingIngredientId.Rice) rice = (x, y);
                if (session.Grid[x, y] == CookingIngredientId.Grain) bean = (x, y);
            }
            if (!CookingRecipeCatalog.Adjacent(rice.Value.x, rice.Value.y, bean.Value.x, bean.Value.y, false)) return false;
            session.TryBeginPath(rice.Value.x, rice.Value.y);
            session.TryExtendPath(bean.Value.x, bean.Value.y);
            session.EndPath();
            return CollectAllPerfect(session);
        }

        /// <summary>화로가 김 단계가 되는 즉시 퍼펙트로 꺼낸다.</summary>
        public static bool CollectAllPerfect(CookingSession session)
        {
            float t = 0f;
            while (session.ActiveCooks.Count > 0 && t < 8f)
            {
                session.Tick(0.05f);
                t += 0.05f;
                for (int y = 0; y < CookingSession.GridSize; y++)
                for (int x = 0; x < CookingSession.GridSize; x++)
                {
                    var job = session.CookAt(x, y);
                    if (job != null && job.IsPerfectWindow)
                        session.TryCollectCook(x, y);
                }
            }
            return session.ActiveCooks.Count == 0;
        }

        [Test]
        public void Completing_Discovers_AndShowsInResult()
        {
            var session = StartRiceBeanBoard();
            Assume.That(CompleteRiceBean(session), "판 배치가 연결되지 않으면(드묾) 건너뜀");
            Assert.IsTrue(CookingCodex.IsDiscovered("patteok"));
            CollectionAssert.Contains(session.NewlyDiscovered, "patteok");
            StringAssert.Contains("새로 얻은 레시피", GongyangganScreen.BuildResultText(session));
        }

        [Test]
        public void Nagari_RelocksOnlyThisRoundsDiscoveries()
        {
            CookingCodex.Discover("kimchi"); // 예전에 발견
            eco.AddMaterial(CookingIngredientId.Fruit, 12); // 과실 2 = 곶감, 12개면 곶감을 만들어도 판이 남는다
            var session = new CookingSession();
            session.Prepare(CookingCharmType.None);
            Assert.IsTrue(session.StartRound());

            // 붙어 있는 과실 두 칸으로 곶감
            bool made = false;
            for (int y = 0; y < CookingSession.GridSize && !made; y++)
            for (int x = 0; x < CookingSession.GridSize - 1 && !made; x++)
            {
                if (session.Grid[x, y] != CookingIngredientId.Fruit || session.Grid[x + 1, y] != CookingIngredientId.Fruit) continue;
                session.TryBeginPath(x, y);
                session.TryExtendPath(x + 1, y);
                session.EndPath();
                made = CollectAllPerfect(session);
            }
            Assert.IsTrue(made, "과실 12개가 아래 세 줄에 깔리면 가로로 붙은 쌍이 반드시 있다");
            Assert.IsTrue(CookingCodex.IsDiscovered("gotgam"));
            Assert.IsFalse(session.Finished);

            eco.AddCharm(CookingCharmType.Cancel, 1); // 나가리는 소모품
            session.CancelNagari();
            Assert.IsFalse(CookingCodex.IsDiscovered("gotgam"), "이번 판 발견은 다시 잠김");
            Assert.IsTrue(CookingCodex.IsDiscovered("kimchi"), "예전 발견은 유지");
        }

        [Test]
        public void MakeableLine_ShowsQuestionMarkForUnknown()
        {
            eco.AddMaterial(CookingIngredientId.Fruit, 2);
            var session = new CookingSession();
            session.Prepare(CookingCharmType.None);
            Assume.That(session.MakeableNow().Count > 0, "과실 2개가 붙어 있어야 함");
            Assert.AreEqual("만들 수 있는 요리: ?", GongyangganScreen.MakeableLine(session));
            CookingCodex.Discover("gotgam");
            Assert.AreEqual("만들 수 있는 요리: 곶감", GongyangganScreen.MakeableLine(session));
        }

        [Test]
        public void Descriptions_FromRecipesAndIngredients()
        {
            try
            {
                CookingRecipeCatalog.LoadFromJson(
                    "{\"recipes\":[{\"id\":\"bap\",\"name\":\"밥\",\"kind\":\"Food\",\"ingredients\":[\"Water\",\"Rice\"],\"description\":\"갓 지은 밥.\"}]}");
                CodexDescriptions.LoadIngredientsFromJson("{\"ingredients\":[{\"id\":\"Rice\",\"description\":\"약초밭에서 나요.\"}]}");
                Assert.AreEqual("갓 지은 밥.", CodexDescriptions.Get("bap"));
                Assert.AreEqual("약초밭에서 나요.", CodexDescriptions.Get("Rice"));
                Assert.AreEqual("", CodexDescriptions.Get("kimchi"));
            }
            finally
            {
                CookingRecipeCatalog.LoadFromJson(Resources.Load<TextAsset>(CookingRecipeCatalog.ResourcePath).text);
                CodexDescriptions.LoadIngredientsFromJson(Resources.Load<TextAsset>(CodexDescriptions.IngredientsResourcePath)?.text);
            }
        }

        [Test]
        public void IngredientsJson_HasEveryIngredientCell()
        {
            // Resources/ingredients.json = 시트 ingredients 탭 (재료 13 + 황금 재료 — v1.2 에서 황금쌀·황금꿀은 빠짐)
            var text = Resources.Load<TextAsset>(CodexDescriptions.IngredientsResourcePath)?.text;
            Assert.IsNotNull(text);
            for (int i = 0; i < (int)CookingIngredientId.Count; i++)
                StringAssert.Contains("\"" + (CookingIngredientId)i + "\"", text);
        }

        [Test]
        public void Prefabs_AreWired()
        {
            const string codexPath = "Assets/Prefabs/UI/CodexScreen.prefab";
            var codex = AssetDatabase.LoadAssetAtPath<GameObject>(codexPath);
            Assert.IsNotNull(codex);
            var so = new SerializedObject(codex.GetComponent<CodexScreen>());
            foreach (var f in new[] { "panel", "rateText", "closeButton", "content", "detailPanel", "detailName", "detailCombo", "detailDesc", "detailCloseButton" })
                Assert.IsNotNull(so.FindProperty(f).objectReferenceValue, f);
            StringAssert.Contains(AssetDatabase.AssetPathToGUID(codexPath), System.IO.File.ReadAllText("Assets/Scenes/Main.unity"));

            var g = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/GongyangganScreen.prefab");
            var root = g.transform.Find("Canvas_Gongyanggan/Root");
            Assert.IsNotNull(root.Find("CodexButton")?.GetComponent<UnityEngine.UI.Button>());
            Assert.IsNotNull(root.Find("Makeable")?.GetComponent<UnityEngine.UI.Text>());
            Assert.IsNotNull(root.Find("ResultPopup/Box/Rekindle")?.GetComponent<UnityEngine.UI.Button>());
            Assert.AreEqual("ResultPopup", root.GetChild(root.childCount - 1).name, "결과창이 맨 위에 그려져야 함");
        }
    }
}
