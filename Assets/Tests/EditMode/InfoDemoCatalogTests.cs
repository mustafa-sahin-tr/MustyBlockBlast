using System;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Presentation.Views;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Which info-popup subjects get an animated demo (issue #446): of the special cells only the
    /// Vortex, plus the objective cards (#447); the power-ups are covered by InfoDemoPowerUpTests (#448).
    /// Every other subject must fall back to the static hero icon (issue #445 AC9), which the View
    /// does whenever the catalog returns null.
    /// </summary>
    public class InfoDemoCatalogTests
    {
        [Test]
        public void Vortex_HasADemo_AndItIsCached()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            InfoDemoTimeline first = catalog.Find(InfoPopupSubjectKind.SpecialCell, (int)SpecialCellKind.Vortex);
            InfoDemoTimeline second = catalog.Find(InfoPopupSubjectKind.SpecialCell, (int)SpecialCellKind.Vortex);

            Assert.IsNotNull(first);
            Assert.AreSame(first, second);
        }

        [Test]
        public void EveryOtherSpecialCell_HasNoDemo()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();
            Array kinds = Enum.GetValues(typeof(SpecialCellKind));

            for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
            {
                SpecialCellKind kind = (SpecialCellKind)kinds.GetValue(kindIndex);
                if (kind == SpecialCellKind.Vortex)
                {
                    continue;
                }

                Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialCell, (int)kind), kind.ToString());
            }
        }

        [Test]
        public void SpecialPiecesAndHold_HaveNoDemo_EvenWithTheVortexKindValue()
        {
            // Power-ups are covered by InfoDemoPowerUpTests (issue #448 gave six of them a demo — one of
            // which, ColorCleanser, shares the Vortex's kind value 4).
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            AssertNoneFor(catalog, InfoPopupSubjectKind.SpecialPiece, typeof(SpecialPieceKind));
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.Hold, 0));
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialPiece, (int)SpecialCellKind.Vortex));
        }

        [Test]
        public void VortexDemo_MatchesTheAuthoredLayout()
        {
            InfoDemoTimeline timeline = new InfoDemoCatalog().Find(
                InfoPopupSubjectKind.SpecialCell, (int)SpecialCellKind.Vortex);
            InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];

            Assert.AreEqual(VortexInfoDemo.LOOP_DURATION, timeline.Duration, 0.0001f);

            // Loop start: row 7 has its single gap at column 4; the three islands are empty.
            timeline.Evaluate(0f, states);
            Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(7, 4)].Paint);
            Assert.AreNotEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(7, 1)].Paint);
            Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(3, 3)].Paint);
            Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(5, 6)].Paint);

            // After the fills (and before row 5's cascade clear) every island cell is Vortex indigo.
            timeline.Evaluate(3.1f, states);
            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(3, 3)].Paint);
            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(5, 2)].Paint);
            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(5, 4)].Paint);
            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(5, 6)].Paint);
            Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(7, 1)].Paint, "row 7 cleared");

            // Near the end: row 5 has cleared, the filled (3,3) survives.
            timeline.Evaluate(4.5f, states);
            for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
            {
                Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(5, column)].Paint);
            }

            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(3, 3)].Paint);
        }

        [Test]
        public void SimultaneousLineClear_HasADemoPerRequiredLineCount_EachCached()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            for (int lineCount = SimultaneousLineClearInfoDemo.MIN_LINE_COUNT;
                lineCount <= SimultaneousLineClearInfoDemo.MAX_LINE_COUNT;
                lineCount++)
            {
                InfoDemoTimeline first = catalog.FindObjective(Objective(ObjectiveType.SimultaneousLineClear, lineCount));
                InfoDemoTimeline second = catalog.FindObjective(Objective(ObjectiveType.SimultaneousLineClear, lineCount));

                Assert.IsNotNull(first, lineCount.ToString());
                Assert.AreSame(first, second, lineCount.ToString());
            }

            Assert.AreNotSame(
                catalog.FindObjective(Objective(ObjectiveType.SimultaneousLineClear, 2)),
                catalog.FindObjective(Objective(ObjectiveType.SimultaneousLineClear, 3)));
        }

        [Test]
        public void SimultaneousLineClear_WithALineCountTheDemoCannotDraw_HasNoDemo()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            Assert.IsNull(catalog.FindObjective(Objective(ObjectiveType.SimultaneousLineClear, 1)));
            Assert.IsNull(catalog.FindObjective(Objective(ObjectiveType.SimultaneousLineClear, 5)));
            Assert.IsNull(catalog.FindObjective(null));
        }

        [Test]
        public void EveryOtherObjectiveType_HasNoDemo()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();
            Array types = Enum.GetValues(typeof(ObjectiveType));

            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                ObjectiveType type = (ObjectiveType)types.GetValue(typeIndex);
                if (type == ObjectiveType.SimultaneousLineClear)
                {
                    continue;
                }

                Assert.IsNull(catalog.FindObjective(Objective(type, 2)), type.ToString());
            }
        }

        [Test]
        public void SimultaneousLineClearDemo_PlacesAVerticalPiece_AndClearsExactlyTheBottomNRowsTogether()
        {
            for (int lineCount = SimultaneousLineClearInfoDemo.MIN_LINE_COUNT;
                lineCount <= SimultaneousLineClearInfoDemo.MAX_LINE_COUNT;
                lineCount++)
            {
                InfoDemoTimeline timeline = new InfoDemoCatalog().FindObjective(
                    Objective(ObjectiveType.SimultaneousLineClear, lineCount));
                InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];
                int firstRow = SimultaneousLineClearInfoDemo.FirstClearedRow(lineCount);
                int gap = SimultaneousLineClearInfoDemo.GAP_COLUMN;

                Assert.AreEqual(SimultaneousLineClearInfoDemo.LOOP_DURATION, timeline.Duration, 0.0001f);

                // Loop start: the bottom N rows are full but for the gap column; the row above them
                // (and every other row) is not full, so the placement clears exactly N lines.
                timeline.Evaluate(0f, states);
                for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
                {
                    int filled = FilledInRow(states, row);
                    int expected = row >= firstRow ? InfoDemoLayout.BOARD_SIZE - 1 : filled;
                    Assert.AreEqual(expected, filled, $"N={lineCount} row {row}");
                    Assert.Less(filled, InfoDemoLayout.BOARD_SIZE, $"N={lineCount} row {row} starts full");
                    if (row >= firstRow)
                    {
                        Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(row, gap)].Paint);
                    }
                }

                // Landed, before the clear: the bottom N rows are full, nothing above is.
                timeline.Evaluate(SimultaneousLineClearInfoDemo.CLEAR_START - 0.01f, states);
                for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
                {
                    bool full = FilledInRow(states, row) == InfoDemoLayout.BOARD_SIZE;
                    Assert.AreEqual(row >= firstRow, full, $"N={lineCount} row {row} full after landing");
                }

                // Well after the clear: all N rows are gone together.
                timeline.Evaluate(3f, states);
                for (int row = firstRow; row < InfoDemoLayout.BOARD_SIZE; row++)
                {
                    Assert.AreEqual(0, FilledInRow(states, row), $"N={lineCount} row {row} cleared");
                }
            }
        }

        private static ObjectiveDefinition Objective(ObjectiveType type, int requiredLineCount)
        {
            return new ObjectiveDefinition(
                "test", type, ObjectiveScope.PerRun, 1, requiredLineCount, requiredColourId: 1);
        }

        private static int FilledInRow(InfoDemoElementState[] states, int row)
        {
            int filled = 0;
            for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
            {
                if (states[InfoDemoLayout.BoardBlockId(row, column)].Paint != InfoDemoPaint.NONE)
                {
                    filled++;
                }
            }

            return filled;
        }

        private static void AssertNoneFor(InfoDemoCatalog catalog, InfoPopupSubjectKind subjectKind, Type enumType)
        {
            Array kinds = Enum.GetValues(enumType);
            for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
            {
                int kindValue = Convert.ToInt32(kinds.GetValue(kindIndex));
                Assert.IsNull(catalog.Find(subjectKind, kindValue), $"{subjectKind} {kinds.GetValue(kindIndex)}");
            }
        }
    }
}
