using NUnit.Framework;
using Yoegoe.Characters;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>요괴 말풍선 줄바꿈 — 12자가 넘으면 띄어쓰기 자리에서 나눔 (최대 3줄).</summary>
    public class BubbleWrapTests
    {
        [Test]
        public void ShortLine_Unchanged()
        {
            Assert.AreEqual("다녀왔네.", CharacterAgent.WrapBubbleText("다녀왔네."));
        }

        [Test]
        public void LongLine_BreaksAtSpace()
        {
            string w = CharacterAgent.WrapBubbleText("풀 뜯기라… 내 격엔 안 맞지만 가 보지.");
            StringAssert.Contains("\n", w);
            foreach (var line in w.Split('\n'))
            {
                Assert.LessOrEqual(line.Length, 14, line);
                Assert.IsFalse(line.StartsWith(" ") || line.EndsWith(" "), line);
            }
            Assert.AreEqual("풀 뜯기라… 내 격엔 안 맞지만 가 보지.", w.Replace("\n", " "));
        }

        [Test]
        public void NoSpaces_BreaksByLength_MaxThreeLines()
        {
            string w = CharacterAgent.WrapBubbleText(new string('가', 50));
            Assert.LessOrEqual(w.Split('\n').Length, 3);
            Assert.AreEqual(50, w.Replace("\n", "").Length);
        }
    }
}
