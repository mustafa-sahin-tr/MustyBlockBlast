using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// The pure monochrome multiplier formula: +0.5x per cleared line that was entirely one colour, the
    /// same step size as the streak bonus it stacks with.
    /// </summary>
    public class ScoreRulesMonochromeTests
    {
        [TestCase(0, 0.0)]
        [TestCase(-1, 0.0)]
        [TestCase(1, 0.5)]
        [TestCase(2, 1.0)]
        [TestCase(3, 1.5)]
        [TestCase(4, 2.0)]
        public void MonochromeMultiplierBonus_IsHalfAnXPerLine(int monochromeLineCount, double expected)
        {
            Assert.AreEqual(expected, ScoreRules.MonochromeMultiplierBonus(monochromeLineCount), 0.0001);
        }

        /// <summary>Uncapped, unlike the streak bonus — more monochrome lines keep paying.</summary>
        [Test]
        public void MonochromeMultiplierBonus_GrowsWithEachExtraLine()
        {
            Assert.Greater(
                ScoreRules.MonochromeMultiplierBonus(3),
                ScoreRules.MonochromeMultiplierBonus(2));
        }
    }
}
