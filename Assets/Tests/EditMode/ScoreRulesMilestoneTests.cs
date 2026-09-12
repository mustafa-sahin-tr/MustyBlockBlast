using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// The pure cumulative-milestone formula: a flat reward only on every 5th multi-line clear of the run,
    /// scaling with the milestone reached (occurrence x 10). Everything between milestones pays nothing.
    /// </summary>
    public class ScoreRulesMilestoneTests
    {
        [TestCase(-3)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(6)]
        [TestCase(9)]
        [TestCase(11)]
        public void MultiClearMilestoneBonus_OffAMilestone_IsZero(int occurrenceCount)
        {
            Assert.AreEqual(0, ScoreRules.MultiClearMilestoneBonus(occurrenceCount));
        }

        [TestCase(5, 50)]
        [TestCase(10, 100)]
        [TestCase(15, 150)]
        [TestCase(20, 200)]
        public void MultiClearMilestoneBonus_OnAMilestone_ScalesWithTheMilestoneReached(
            int occurrenceCount, int expected)
        {
            Assert.AreEqual(expected, ScoreRules.MultiClearMilestoneBonus(occurrenceCount));
        }

        /// <summary>Unbounded, unlike the streak bonus — later milestones keep paying more.</summary>
        [Test]
        public void MultiClearMilestoneBonus_GrowsWithEachLaterMilestone()
        {
            Assert.Greater(
                ScoreRules.MultiClearMilestoneBonus(15),
                ScoreRules.MultiClearMilestoneBonus(10));
        }
    }
}
