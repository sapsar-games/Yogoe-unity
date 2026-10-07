using NUnit.Framework;
using UnityEngine;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>
    /// 저장 → 불러오기 전체 경로(GameSaveService → Migration → OfflineSimulator → ApplyToWorld)가 예외 없이
    /// 출석·상점·엽전을 되살리는지. 불러오기가 깨지면 매번 새 게임으로 시작하고 그 상태가 다시 저장된다.
    /// </summary>
    public class SaveLoadRoundTripTests
    {
        GameObject ecoGO;
        GameEconomy eco;
        string backupPrefs, backupFile;

        [SetUp]
        public void SetUp()
        {
            backupPrefs = PlayerPrefs.HasKey(GameSaveService.PrefsKey) ? PlayerPrefs.GetString(GameSaveService.PrefsKey) : null;
            backupFile = System.IO.File.Exists(GameSaveService.FilePath) ? System.IO.File.ReadAllText(GameSaveService.FilePath) : null;
            ecoGO = new GameObject("Eco");
            eco = ecoGO.AddComponent<GameEconomy>();
            eco.BecomeInstance();
            eco.ApplyStartingState(ScriptableObject.CreateInstance<StartingStateSettings>());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(ecoGO);
            if (backupPrefs != null) PlayerPrefs.SetString(GameSaveService.PrefsKey, backupPrefs);
            else PlayerPrefs.DeleteKey(GameSaveService.PrefsKey);
            if (backupFile != null) System.IO.File.WriteAllText(GameSaveService.FilePath, backupFile);
            else if (System.IO.File.Exists(GameSaveService.FilePath)) System.IO.File.Delete(GameSaveService.FilePath);
            PlayerPrefs.Save();
        }

        [Test]
        public void SaveThenLoad_RestoresAttendanceShopAndYeopjeon()
        {
            int today = Attendance.TodayKey;
            Attendance.ResetFromSave(0, 0);
            Assert.Greater(Attendance.Claim(today, new[] { 5, 5, 5, 7, 9, 10, 20 }), 0);
            eco.AddYeopjeon(77);
            int yeop = eco.Yeopjeon;
            var data = GameSaveBridge.CaptureFromWorld();
            Assert.AreEqual(today, data.economy.attendanceLastHandledDayKey);
            GameSaveService.Save(data);
            string shopLeft = data.economy.shopLeftOfferingId;
            long shopNext = data.economy.shopNextRefreshUtcTicks;

            // 새 게임처럼 흩뜨려 놓고 불러오기
            Attendance.ResetFromSave(0, 0);
            eco.ApplyStartingState(ScriptableObject.CreateInstance<StartingStateSettings>());
            ShopStock.ResetFromSave("", "", 0);

            Assert.IsTrue(GameSaveBridge.TryLoadSimulateAndApply(), "불러오기가 예외 없이 끝나야 함");
            Assert.IsFalse(GameSaveBridge.SaveBlockedByLoadFailure);
            Assert.AreEqual(today, Attendance.LastHandledDayKey, "출석 처리한 날이 되살아나야 다시 안 뜬다");
            Assert.AreEqual(1, Attendance.NextDayIndex);
            Assert.AreEqual(yeop, eco.Yeopjeon);
            Assert.AreEqual(shopLeft, ShopStock.LeftOfferingId);
            Assert.AreEqual(shopNext, data.economy.shopNextRefreshUtcTicks);
        }
    }
}
