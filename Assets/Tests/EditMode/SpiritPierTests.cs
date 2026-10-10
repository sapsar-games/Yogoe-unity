using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>나루터 혼령 줄 (v1.3): 20분에 1명 · 줄 5명 · 3시간 뒤 떠남 · 조각 음식 1 / 공양물 2.</summary>
    public class SpiritPierTests
    {
        static readonly DateTime T0 = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
        static int First(int min, int max) => min;

        List<string> pool;
        IReadOnlyList<string> Pool() => pool;

        void At(double minutes) => SpiritPier.Advance(T0.AddMinutes(minutes), Pool, First);

        [SetUp]
        public void SetUp()
        {
            pool = new List<string> { "kimchi", "baekseolgi", "sanchae", "yakcha", "kkulmul", "suyuk" };
            SpiritPier.Reset(T0);
        }

        [Test]
        public void Arrives_Every20Minutes_UpToFive_TimerStopsWhileFull()
        {
            At(19);
            Assert.AreEqual(0, SpiritPier.Spirits.Count);
            At(20);
            Assert.AreEqual(1, SpiritPier.Spirits.Count);
            At(100);
            Assert.AreEqual(5, SpiritPier.Spirits.Count);
            At(199);
            Assert.AreEqual(5, SpiritPier.Spirits.Count, "줄이 차 있는 동안엔 더 오지 않는다");
            At(210);
            Assert.AreEqual(4, SpiritPier.Spirits.Count, "첫 혼령(20분 도착)은 3시간 뒤인 200분에 떠난다");
            At(218);
            Assert.AreEqual(4, SpiritPier.Spirits.Count);
            At(219);
            Assert.AreEqual(5, SpiritPier.Spirits.Count, "자리가 난 200분부터 다시 20분 (그 분 안에 도착)");
            At(220);
            Assert.AreEqual(4, SpiritPier.Spirits.Count, "40분에 온 혼령은 220분에 떠난다");
        }

        [Test]
        public void NoMakeableDish_NoSpirit_ThenComesAtOnce()
        {
            pool.Clear();
            At(60);
            Assert.AreEqual(0, SpiritPier.Spirits.Count);
            pool.Add("kimchi");
            At(61);
            Assert.AreEqual(1, SpiritPier.Spirits.Count, "타이머는 다 찬 채로 기다렸다가 바로 온다");
        }

        [Test]
        public void AvoidsDishAlreadyInQueue()
        {
            pool = new List<string> { "kimchi", "sanchae" };
            At(40);
            Assert.AreEqual(2, SpiritPier.Spirits.Count);
            Assert.AreNotEqual(SpiritPier.Spirits[0].DishId, SpiritPier.Spirits[1].DishId);
            Assert.AreNotEqual(SpiritPier.Spirits[0].Slot, SpiritPier.Spirits[1].Slot);
        }

        [Test]
        public void Offline_CatchesUpFromSave()
        {
            var save = new SpiritPier.PierSave { lastUtcTicks = T0.Ticks, nextId = 1 };
            SpiritPier.ResetFromSave(save, T0);
            At(60);
            Assert.AreEqual(3, SpiritPier.Spirits.Count);
            At(60 * 24 * 3);
            // 오래 비우면 '하나 떠나고 20분 뒤 하나 옴'이 되풀이 — 4~5명, 넘치지 않음
            Assert.LessOrEqual(SpiritPier.Spirits.Count, 5, "오래 비워도 줄은 5명까지");
            Assert.GreaterOrEqual(SpiritPier.Spirits.Count, 4);
        }

        [Test]
        public void SaveRoundTrip_KeepsQueueAndPieces()
        {
            At(40);
            var save = JsonUtility.FromJson<SpiritPier.PierSave>(JsonUtility.ToJson(SpiritPier.CaptureToSave()));
            SpiritPier.Reset(T0);
            SpiritPier.ResetFromSave(save, T0.AddMinutes(40));
            Assert.AreEqual(2, SpiritPier.Spirits.Count);
            Assert.AreEqual("kimchi", SpiritPier.Spirits[0].DishId);
            Assert.AreEqual(T0.AddMinutes(40).Ticks, SpiritPier.LastUtcTicks);
        }

        [Test]
        public void Serve_Food1_Offering2_SpendsStock_SpiritLeaves()
        {
            var ecoGO = new GameObject("Eco");
            try
            {
                var eco = ecoGO.AddComponent<GameEconomy>();
                eco.BecomeInstance();
                eco.ApplyStartingState(ScriptableObject.CreateInstance<StartingStateSettings>());
                SpiritPier.Reset(T0);
                if (OfferingCatalog.Find("kimchi") == null) OfferingCatalog.Build(null);
                var kimchi = OfferingCatalog.Find("kimchi");
                var baekseolgi = OfferingCatalog.Find("baekseolgi");
                Assume.That(kimchi != null && baekseolgi != null);

                pool = new List<string> { "kimchi", "baekseolgi" };
                At(40); // 김치 · 백설기 혼령
                var a = SpiritPier.Spirits[0];
                var b = SpiritPier.Spirits[1];

                Assert.IsFalse(SpiritPier.TryServe(a.Id, eco, out _), "창고에 없으면 못 내준다");

                eco.AddOffering(kimchi, 1);
                eco.AddOffering(baekseolgi, 1);
                Assert.IsTrue(SpiritPier.TryServe(a.Id, eco, out int p1));
                Assert.AreEqual(1, p1);
                Assert.IsTrue(SpiritPier.TryServe(b.Id, eco, out int p2));
                Assert.AreEqual(2, p2);
                Assert.AreEqual(3, SpiritPier.MemoryPieces);
                Assert.AreEqual(0, SpiritPier.Spirits.Count);
                Assert.AreEqual(0, eco.GetOfferingCount(kimchi));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ecoGO);
            }
        }

        [Test]
        public void SpiritLines_AskFillsDish()
        {
            SpiritLines.LoadFromJson("{\"lines\":[{\"kind\":\"elder\",\"type\":\"ask\",\"text\":\"‘{dish}’ 한 그릇 주게.\"}]}");
            try
            {
                Assert.AreEqual("‘떡국’ 한 그릇 주게.", SpiritLines.Ask((int)SpiritKind.Elder, "떡국"));
                Assert.AreEqual("고마워요.", SpiritLines.Thanks((int)SpiritKind.Child), "없으면 기본 대사");
            }
            finally
            {
                SpiritLines.LoadFromJson(Resources.Load<TextAsset>(SpiritLines.ResourcePath)?.text);
            }
        }
    }
}
