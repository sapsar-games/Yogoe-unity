using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.UI;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>글자 크기 설정: 원래 크기 × (시트 배율 × 플레이어 단계), 코드가 크기를 바꾸면 그 값이 새 원래 크기.</summary>
    public class UiTextScaleTests
    {
        int savedLevel;
        GameObject scalerGO, textGO;

        [SetUp]
        public void SetUp()
        {
            savedLevel = UiTextScale.Level;
            scalerGO = new GameObject("Scaler");
            textGO = new GameObject("Label");
        }

        [TearDown]
        public void TearDown()
        {
            UiTextScale.Level = savedLevel;
            Object.DestroyImmediate(textGO);
            Object.DestroyImmediate(scalerGO);
        }

        [Test]
        public void ScalesFromOriginal_AndFollowsCodeChanges()
        {
            UiTextScale.Level = UiTextScale.DefaultLevel;
            var t = textGO.AddComponent<Text>();
            t.fontSize = 20;
            var scaler = scalerGO.AddComponent<UiTextScaler>();
            scaler.ApplyNow();
            Assert.AreEqual(20, t.fontSize, "보통 · 시트 1 이면 그대로");

            UiTextScale.Level = 3; // 아주 크게 1.3 — Changed 로 바로 적용
            Assert.AreEqual(26, t.fontSize);

            t.fontSize = 30; // 코드가 크기를 바꿈 → 새 원래 크기
            scaler.ApplyNow();
            Assert.AreEqual(39, t.fontSize);

            UiTextScale.Level = UiTextScale.DefaultLevel;
            Assert.AreEqual(30, t.fontSize, "보통으로 돌리면 원래 크기");
        }
    }
}
