using System;
using NUnit.Framework;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Cooking;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>
    /// 기물별 산출 (Docs/00 §6-2·6-3): 보관·만창·오버플로우, 시트(props.json) 값, 탭 수거, 오프라인 정산.
    /// </summary>
    public class PropProductionTests
    {
        // ---------------- PropStorage (순수 규칙) ----------------

        [TestCase(1, 9)]
        [TestCase(9, 9)]
        [TestCase(10, 10)]
        [TestCase(21, 11)]
        public void Capacity_GainsOnePerTenLevels(int level, int expected)
        {
            Assert.AreEqual(expected, PropStorage.Capacity(9, level));
        }

        [TestCase(1, 0.1f)]
        [TestCase(9, 0.9f)]
        [TestCase(10, 0f)]
        [TestCase(11, 0.1f)]
        public void OverflowChance_ResetsEveryTenLevels(int level, float expected)
        {
            Assert.AreEqual(expected, PropStorage.OverflowChance(level), 0.0001f);
        }

        [Test]
        public void Advance_FillsOnePerCycle_ThenJudgesOnceAndHalts_OnFailedRoll()
        {
            var s = new PropStorage.State();
            // 20분 주기, 보관 9: 9개(180분) + 한 사이클 더(20분) → 판정 실패 → 정지
            float worked = PropStorage.Advance(ref s, 1200f, 9, 0.5f, 100000f, () => 0.99f, null);

            Assert.AreEqual(9, s.Stored);
            Assert.IsTrue(PropStorage.IsHalted(s, 9));
            Assert.AreEqual(10 * 1200f, worked, 0.01f); // 정지 이후는 일하지 않음(기력 소모 없음)
            Assert.AreEqual(0f, PropStorage.Advance(ref s, 1200f, 9, 0.5f, 5000f, () => 0f, null));
        }

        [Test]
        public void Advance_OverflowSuccess_StoresCapacityPlusOne()
        {
            var s = new PropStorage.State();
            PropStorage.Advance(ref s, 60f, 1, 0.9f, 100000f, () => 0.1f, null);
            Assert.AreEqual(2, s.Stored);
            Assert.IsTrue(PropStorage.IsHalted(s, 1));
        }

        [Test]
        public void TakeAll_Resumes_NextFullCycleJudgesAgain()
        {
            var s = new PropStorage.State();
            PropStorage.Advance(ref s, 60f, 2, 0f, 100000f, () => 0.5f, null);
            Assert.AreEqual(2, PropStorage.TakeAll(ref s));
            Assert.IsFalse(PropStorage.IsHalted(s, 2));

            PropStorage.Advance(ref s, 60f, 2, 0f, 60f, () => 0.5f, null);
            Assert.AreEqual(1, s.Stored);
        }

        [Test]
        public void LevelUpRaisingCapacity_KeepsOverflowItems_AndReinterprets()
        {
            // 사냥 목적지 Lv9 오버플로우 10/9 → Lv10 기본 보관 10 → 10/10 (삭제 없음, 계속 정지)
            var s = new PropStorage.State { Stored = 10, OverflowJudged = true };
            Assert.IsTrue(PropStorage.IsHalted(s, PropStorage.Capacity(9, 10)));
            Assert.AreEqual(10, s.Stored);
        }

        // ---------------- props.json (시트) ----------------

        [Test]
        public void Catalog_HasSpecValues()
        {
            Assert.IsTrue(PropCatalog.TryGet("옹달샘", out var well));
            Assert.AreEqual(PropResourceType.Water, well.ResourceType);
            Assert.AreEqual(15f, well.cycleMinutes, 0.001f); // v1.2: 모든 기물 15분에 1개 · 보관 15
            Assert.AreEqual(15, well.baseCapacity);

            Assert.IsFalse(PropCatalog.TryGet("갯바위", out _)); // v1.2 삭제

            Assert.IsTrue(PropCatalog.TryGet("화덕", out var oven));
            Assert.IsFalse(oven.upgradable);
        }

        [Test]
        public void RollDrop_FollowsGatherWeights_WithGoldenAtTheEnd()
        {
            Assert.AreEqual((int)CookingIngredientId.Rice, PropCatalog.RollDrop(PropResourceType.Gather, 0f));
            Assert.AreEqual((int)CookingIngredientId.Rice, PropCatalog.RollDrop(PropResourceType.Gather, 0.385f));
            Assert.AreEqual((int)CookingIngredientId.Grain, PropCatalog.RollDrop(PropResourceType.Gather, 0.985f));
            int golden = PropCatalog.RollDrop(PropResourceType.Gather, 0.999f);
            Assert.IsTrue(PropCatalog.IsSpecialCode(golden));
            Assert.AreEqual(SpecialItemId.GoldenRice, PropCatalog.SpecialOf(golden));
            Assert.AreEqual(SpecialItemId.GoldenHoney,
                PropCatalog.SpecialOf(PropCatalog.RollDrop(PropResourceType.Hunt, 1f)));
            Assert.AreEqual((int)CookingIngredientId.Egg, PropCatalog.RollDrop(PropResourceType.Hunt, 0f));
        }

        // ---------------- PropSlot 온라인 + 수거 ----------------

        GameObject economyGO;
        GameEconomy economy;
        GameObject propGO;
        PropSlot prop;
        PropData data;

        void MakeProp(PropResourceType type, float cycleMinutes, int capacity)
        {
            economyGO = new GameObject("GameEconomy_Test");
            economy = economyGO.AddComponent<GameEconomy>();
            economy.BecomeInstance();
            var settings = ScriptableObject.CreateInstance<StartingStateSettings>();
            settings.startingWater = 0;
            settings.startingYeopjeon = 0;
            economy.ApplyStartingState(settings);

            data = ScriptableObject.CreateInstance<PropData>();
            data.propId = "test_" + type;
            data.resourceType = type;
            data.cycleMinutes = cycleMinutes;
            data.baseCapacity = capacity;
            propGO = new GameObject("Prop_Test");
            prop = propGO.AddComponent<PropSlot>();
            prop.data = data;
            prop.ConfigureBuiltState(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (propGO != null) UnityEngine.Object.DestroyImmediate(propGO);
            if (data != null) UnityEngine.Object.DestroyImmediate(data);
            if (economyGO != null) UnityEngine.Object.DestroyImmediate(economyGO);
            propGO = null; data = null; economyGO = null;
        }

        [Test]
        public void Well_ProducesWater_TapCollectsIntoWallet()
        {
            MakeProp(PropResourceType.Water, 30f, 6);
            prop.ProduceWhileStaying(3 * 1800f, 0f, false);

            Assert.AreEqual(3, prop.StoredResources);
            Assert.IsTrue(prop.TryCollect());
            Assert.AreEqual(3, economy.Water);
            Assert.AreEqual(0, prop.StoredResources);
        }

        [Test]
        public void HerbField_StoresRolledIngredients_CollectAddsMaterials()
        {
            MakeProp(PropResourceType.Gather, 20f, 9);
            int before = 0;
            for (int i = 0; i < (int)CookingIngredientId.Count; i++)
                before += economy.GetMaterialCount((CookingIngredientId)i);

            prop.ProduceWhileStaying(4 * 1200f, 0f, false);
            Assert.AreEqual(4, prop.PendingIngredients.Count);
            prop.TryCollect();

            int after = 0;
            for (int i = 0; i < (int)CookingIngredientId.Count; i++)
                after += economy.GetMaterialCount((CookingIngredientId)i);
            Assert.AreEqual(before + 4, after);
        }

        [Test]
        public void GoldenPending_CollectGoesToSpecialItems()
        {
            MakeProp(PropResourceType.Gather, 20f, 9);
            prop.RestoreStorage(2, 0f, false,
                new[] { (int)CookingIngredientId.Rice, PropCatalog.SpecialCodeBase + (int)SpecialItemId.GoldenRice });
            Assert.IsTrue(prop.HasGoldenPending);

            prop.TryCollect();

            Assert.AreEqual(1, economy.GetSpecialItemCount(SpecialItemId.GoldenRice));
            Assert.IsFalse(prop.HasGoldenPending);
        }


        [Test]
        public void MeritPile_StopsAtCapacityMinutes()
        {
            MakeProp(PropResourceType.Merit, 0f, 0);
            data.baseProductionPerMinute = 100;
            data.meritCapacityMinutes = 30f;

            float worked = prop.ProduceWhileStaying(3600f, 0f, false);
            Assert.AreEqual(1800f, worked, 0.5f);
            Assert.AreEqual(3000.0, prop.PendingMerit.ToDouble(), 0.5);
            Assert.IsTrue(prop.IsStorageHalted);
        }

        // ---------------- PropProduction (온라인·오프라인 공용 규칙) ----------------

        static PropProduction.Config Mortar(bool intimacyBonus, double owner) => new PropProduction.Config
        {
            Type = PropResourceType.Merit, MeritPerMinute = 100, LevelGrowth = 1.1,
            MeritCapacityMinutes = 30f, IntimacyBonus = intimacyBonus, OwnerMultiplier = owner,
        };

        [Test]
        public void MeritPerMinute_AppliesSheetFlags()
        {
            // 3차 시트: 친밀도 보정 없음 · 주인 ×1
            Assert.AreEqual(110.0, PropProduction.MeritPerMinute(Mortar(false, 1.0), 2, 50f, true), 0.0001);
            // 보정 켜면: 110 × 1.5 × 2
            Assert.AreEqual(330.0, PropProduction.MeritPerMinute(Mortar(true, 2.0), 2, 50f, true), 0.0001);
            Assert.AreEqual(165.0, PropProduction.MeritPerMinute(Mortar(true, 2.0), 2, 50f, false), 0.0001);
        }

        [Test]
        public void Produce_Merit_StopsAtCapacity_CapIgnoresBonuses()
        {
            var c = Mortar(true, 2.0);
            var st = new PropStorage.State();
            // 보관 = 보정 전 100 × 30 = 3000, 분당 400(친100 ×2 · 주인 ×2) → 7.5분이면 가득
            float worked = PropProduction.Produce(c, 1, 100f, true, 0, ref st, 3600f, () => 0.5f, null, out double add);
            Assert.AreEqual(450f, worked, 0.5f);
            Assert.AreEqual(3000.0, add, 0.5);
            Assert.IsTrue(PropProduction.IsHalted(c, 1, add, st));
        }

        [Test]
        public void Produce_None_WorksWithoutOutput()
        {
            var st = new PropStorage.State();
            float worked = PropProduction.Produce(default, 1, 0f, false, 0, ref st, 100f, () => 0.5f, null, out double add);
            Assert.AreEqual(100f, worked);
            Assert.AreEqual(0.0, add);
        }

        [Test]
        public void Config_PrefersSheet_ThenAsset()
        {
            var asset = ScriptableObject.CreateInstance<PropData>();
            asset.propId = "옹달샘";
            asset.resourceType = PropResourceType.Merit; // 시트가 이긴다
            Assert.AreEqual(PropResourceType.Water, PropProduction.Config.Resolve("옹달샘", asset).Type);
            asset.propId = "시트에_없는_기물";
            Assert.AreEqual(PropResourceType.Merit, PropProduction.Config.Resolve(asset.propId, asset).Type);
            UnityEngine.Object.DestroyImmediate(asset);
        }

        // ---------------- 오프라인 ----------------

        [Test]
        public void Offline_FullWell_StopsStaminaDrain()
        {
            var now = DateTime.UtcNow;
            var data = new GameSaveData
            {
                savedAtUtcTicks = now.AddHours(-10).Ticks,
                props = new[] { new PropSave { propId = "옹달샘", isBuilt = true, level = 1 } },
                agents = new[]
                {
                    new AgentSave
                    {
                        characterId = "SamjokO", stamina = 75f, intimacy = 50f,
                        state = ActionState.Staying, occupiedPropId = "옹달샘"
                    }
                }
            };

            OfflineSimulator.Simulate(data, now);

            // v1.2: 15개(15분×15) + 판정 사이클(15분)만 일함 → 기력 240분 / 10분 = 24 소모
            Assert.GreaterOrEqual(data.props[0].storedResources, 15);
            Assert.IsTrue(data.props[0].overflowJudged);
            Assert.AreEqual(75f - 24f, data.agents[0].stamina, 0.05f);
            Assert.AreEqual(ActionState.Staying, data.agents[0].state);
        }
    }
}
