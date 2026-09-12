using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the board-wipe ("perfect clear") bonus: it only fires when this placement actually cleared
    /// lines AND the board ended up completely empty afterwards.
    /// </summary>
    public class BoardWipeScoreRuleTests
    {
        [Test]
        public void LinesClearedAndBoardEmpty_AwardsTheFlatBonus()
        {
            BoardWipeScoreRule rule = new BoardWipeScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 2, streakBeforePlacement: 0, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 0,
                boardEmptyAfterPlacement: true);

            Assert.AreEqual(ScoreRules.BoardWipeBonus(2, true), rule.ComputeBonus(context));
            Assert.Greater(rule.ComputeBonus(context), 0);
        }

        [Test]
        public void LinesClearedButBoardNotEmpty_IsZero()
        {
            BoardWipeScoreRule rule = new BoardWipeScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 1, streakBeforePlacement: 0, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 0,
                boardEmptyAfterPlacement: false);

            Assert.AreEqual(0, rule.ComputeBonus(context));
        }

        [Test]
        public void NoLinesCleared_IsZero_EvenIfBoardIsSomehowEmpty()
        {
            BoardWipeScoreRule rule = new BoardWipeScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 0, streakBeforePlacement: 0, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 0,
                boardEmptyAfterPlacement: true);

            Assert.AreEqual(0, rule.ComputeBonus(context));
        }

        [Test]
        public void NoLinesClearedAndBoardNotEmpty_IsZero()
        {
            BoardWipeScoreRule rule = new BoardWipeScoreRule();
            ScorePlacementContext context = new ScorePlacementContext(
                cellCount: 4, linesCleared: 0, streakBeforePlacement: 0, monochromeLineCount: 0,
                multiClearStreakBeforePlacement: 0, cumulativeMultiClearCountBeforePlacement: 0,
                boardEmptyAfterPlacement: false);

            Assert.AreEqual(0, rule.ComputeBonus(context));
        }
    }
}
