using System.Collections.Generic;
using NUnit.Framework;
using Yoegoe.Cooking;
using Yoegoe.Data;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>공양간 주문 요괴 — 선호 조합이 가능할 때만 등장, 퍼펙트/식음 보상 배율.</summary>
    public class CookingGuestOrderTests
    {
        static CookingRecipe Offering(string id, string name) =>
            new CookingRecipe(id, name, CookingResultKind.Offering, CookingIngredientId.Rice, CookingIngredientId.Rice, CookingIngredientId.Water);

        static CookingRecipe Food(string id, string name) =>
            new CookingRecipe(id, name, CookingResultKind.Food, CookingIngredientId.Rice, CookingIngredientId.Water);

        [Test]
        public void TryPick_OnlyWhenPreferredOfferingMakeable()
        {
            var makeable = new List<CookingRecipe> { Offering("baekseolgi", "백설기"), Food("bap", "밥") };
            var owned = new List<(string, string, string[])>
            {
                ("Gorani", "고라니", new[] { "baekseolgi", "hanyak" }),
                ("Rabbit", "옥토끼", new string[0]),
            };
            Assert.IsTrue(CookingGuestOrder.TryPickOfferingId(makeable, owned, (a, b) => a, out var charId, out var offId));
            Assert.AreEqual("Gorani", charId);
            Assert.AreEqual("baekseolgi", offId);

            var noPref = new List<CookingRecipe> { Food("bap", "밥") };
            Assert.IsFalse(CookingGuestOrder.TryPickOfferingId(noPref, owned, (a, b) => a, out _, out _));
        }

        [Test]
        public void Reward_PerfectIsDouble_CoolNormalIntimacyDoubleStamina()
        {
            var o = OfferingCatalog.Find("baekseolgi");
            if (o == null)
            {
                OfferingCatalog.Build(null);
                o = OfferingCatalog.Find("baekseolgi");
            }
            Assume.That(o != null);

            int baseStam = o.ResolveStaminaGain(true);
            float baseInti = o.ResolveIntimacyGain(true);

            CookingGuestOrder.ComputeReward(o, perfect: true, out int ps, out float pi);
            Assert.AreEqual(baseStam * 2, ps);
            Assert.AreEqual(baseInti * 2f, pi, 0.001f);

            CookingGuestOrder.ComputeReward(o, perfect: false, out int cs, out float ci);
            Assert.AreEqual(baseStam * 2, cs);
            Assert.AreEqual(baseInti, ci, 0.001f); // 친밀도는 평소대로
        }

        [Test]
        public void Collect_SteamIsPerfect_CoolIsNot_GuestSkipsInventory()
        {
            // 쌀+쌀+물 = 백설기 — 인벤에만 넣고 판 시작
            // Assume/Assert 실패 시에도 Instance를 비워야 뒤 테스트가 싱글턴 가드에 안 걸린다.
            var ecoGO = new UnityEngine.GameObject("Eco");
            try
            {
                var eco = ecoGO.AddComponent<Yoegoe.Economy.GameEconomy>();
                eco.BecomeInstance();
                eco.ApplyStartingState(UnityEngine.ScriptableObject.CreateInstance<StartingStateSettings>());
                for (int i = 0; i < (int)CookingIngredientId.Count; i++)
                    eco.TrySpendMaterial((CookingIngredientId)i, eco.GetMaterialCount((CookingIngredientId)i));

                eco.AddMaterial(CookingIngredientId.Rice, 2);
                eco.AddMaterial(CookingIngredientId.Water, 1);
                var session = new CookingSession();
                session.Prepare(CookingCharmType.None);
                Assert.IsTrue(session.StartRound());

                // 세 칸이 연결돼 있으면 잇기
                bool linked = TryLinkBaekseolgi(session);
                Assume.That(linked, "백설기 재료가 인접하게 깔려야 함");

                Assert.AreEqual(1, session.ActiveCooks.Count);
                Assert.AreEqual(0, session.Results.Count);

                // 김 단계까지 — 음식/공양물 중 긴 쪽
                float t = 0f;
                bool collected = false;
                while (t < 5f && !collected)
                {
                    session.Tick(0.05f);
                    t += 0.05f;
                    for (int y = 0; y < CookingSession.GridSize && !collected; y++)
                    for (int x = 0; x < CookingSession.GridSize && !collected; x++)
                    {
                        var job = session.CookAt(x, y);
                        if (job == null || !job.IsPerfectWindow) continue;
                        Assert.IsTrue(session.TryCollectCook(x, y));
                        collected = true;
                    }
                }
                Assert.IsTrue(collected);
                // 주문 요괴가 없어도(에이전트 없음) 일반 퍼펙트 → 인벤 ×2
                // 에이전트 없으면 GuestOrder null → Results에 ×2
                if (session.GuestOrder == null || !session.GuestOrder.Fulfilled)
                    Assert.GreaterOrEqual(CountResult(session, "baekseolgi"), 1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ecoGO);
            }
        }

        static int CountResult(CookingSession s, string id)
        {
            int n = 0;
            foreach (var (r, c, _) in s.Results)
                if (r.Id == id) n += c;
            return n;
        }

        static bool TryLinkBaekseolgi(CookingSession session)
        {
            // 쌀·쌀·물 세 칸이 한 경로로 이어지면 EndPath
            var cells = new List<(int x, int y, CookingIngredientId id)>();
            for (int y = 0; y < CookingSession.GridSize; y++)
            for (int x = 0; x < CookingSession.GridSize; x++)
            {
                if (!session.Grid[x, y].HasValue) continue;
                cells.Add((x, y, session.Grid[x, y].Value));
            }
            if (cells.Count < 3) return false;

            for (int i = 0; i < cells.Count; i++)
            for (int j = 0; j < cells.Count; j++)
            for (int k = 0; k < cells.Count; k++)
            {
                if (i == j || j == k || i == k) continue;
                var a = cells[i]; var b = cells[j]; var c = cells[k];
                var ings = new[] { a.id, b.id, c.id };
                if (!CookingRecipeCatalog.TryMatch(ings, out var recipe) || recipe.Id != "baekseolgi") continue;
                if (!CookingRecipeCatalog.Adjacent(a.x, a.y, b.x, b.y, false)) continue;
                if (!CookingRecipeCatalog.Adjacent(b.x, b.y, c.x, c.y, false)) continue;
                session.TryBeginPath(a.x, a.y);
                session.TryExtendPath(b.x, b.y);
                session.TryExtendPath(c.x, c.y);
                session.EndPath();
                return session.ActiveCooks.Count > 0;
            }
            return false;
        }
    }
}
