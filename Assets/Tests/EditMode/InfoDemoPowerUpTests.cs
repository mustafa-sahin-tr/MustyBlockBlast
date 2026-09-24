using System;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Presentation.Views;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// The board-targeted power-up info demos (issue #448): which power-ups get one, the reusable beats
    /// they are built from (tap, beam, button press, charge spend, distance-ordered clears), and — for
    /// each demo — that the board after its effect is what the real power-up would leave
    /// (<c>PowerUpSystem.TryApplyBomb</c>/<c>RowClear</c>/<c>ColumnClear</c>/<c>Joker</c>/
    /// <c>ColorCleanser</c>/<c>PaintCross</c>).
    /// </summary>
    public class InfoDemoPowerUpTests
    {
        private const float TOLERANCE = 0.0001f;

        /// <summary>Well after every demo's effect has settled and before any loop fade-out starts.</summary>
        private const float AFTER_EFFECT = 4.4f;

        /// <summary>Every power-up kind has a demo since issue #449 (the tray / targetless six are covered
        /// in detail by <c>InfoDemoTrayPowerUpTests</c>).</summary>
        private static readonly PowerUpKind[] DemoKinds =
        {
            PowerUpKind.Bomb, PowerUpKind.RowClear, PowerUpKind.ColumnClear, PowerUpKind.Joker,
            PowerUpKind.ColorCleanser, PowerUpKind.PaintCross, PowerUpKind.Rotate, PowerUpKind.Reroll,
            PowerUpKind.DoubleMultiplier, PowerUpKind.GhostFit, PowerUpKind.CoinSower, PowerUpKind.Hold,
        };

        // ---------------------------------------------------------------- catalog

        [Test]
        public void EveryPowerUp_HasADemo_EachCached()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            for (int kindIndex = 0; kindIndex < DemoKinds.Length; kindIndex++)
            {
                PowerUpKind kind = DemoKinds[kindIndex];
                InfoDemoTimeline first = catalog.Find(InfoPopupSubjectKind.PowerUp, (int)kind);
                InfoDemoTimeline second = catalog.Find(InfoPopupSubjectKind.PowerUp, (int)kind);

                Assert.IsNotNull(first, kind.ToString());
                Assert.AreSame(first, second, kind.ToString());
            }

            Assert.AreNotSame(
                catalog.Find(InfoPopupSubjectKind.PowerUp, (int)PowerUpKind.RowClear),
                catalog.Find(InfoPopupSubjectKind.PowerUp, (int)PowerUpKind.ColumnClear));
        }

        [Test]
        public void EveryOtherPowerUp_AndOutOfRangeValues_HaveNoDemo()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();
            Array kinds = Enum.GetValues(typeof(PowerUpKind));

            for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
            {
                PowerUpKind kind = (PowerUpKind)kinds.GetValue(kindIndex);
                if (Array.IndexOf(DemoKinds, kind) >= 0)
                {
                    continue;
                }

                Assert.IsNull(catalog.Find(InfoPopupSubjectKind.PowerUp, (int)kind), kind.ToString());
            }

            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.PowerUp, -1));
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.PowerUp, 999));
        }

        // ---------------------------------------------------------------- beats

        [Test]
        public void Tap_FingerGlidesInFromBelowRight_PressesAtTheTime_ThenFades_AndItsRingExpands()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            Vector2 target = InfoDemoLayout.Cell(3, 4);
            float end = InfoDemoChoreography.Tap(builder, 2f, target);
            InfoDemoTimeline timeline = builder.Build();

            // Tap adds shadow, rim, face (icons), then the ring.
            int face = InfoDemoLayout.BOARD_CELL_COUNT + 2;
            int ring = InfoDemoLayout.BOARD_CELL_COUNT + 3;
            Assert.AreEqual(InfoDemoElementKind.Icon, timeline.GetElement(face).Kind);
            Assert.AreEqual(InfoDemoSprite.Disc, timeline.GetElement(face).Sprite);
            Assert.AreEqual(InfoDemoElementKind.Ring, timeline.GetElement(ring).Kind);
            Assert.AreEqual(2f + InfoDemoChoreography.TAP_RING_DURATION, end, TOLERANCE);

            InfoDemoElementState[] before = Sample(timeline, 1f);
            Assert.AreEqual(0f, before[face].Alpha, TOLERANCE, "hidden before the glide");
            Assert.AreEqual(0f, before[ring].Alpha, TOLERANCE);

            InfoDemoElementState[] glideStart = Sample(timeline, 2f - InfoDemoChoreography.TAP_GLIDE_DURATION);
            Assert.Greater(glideStart[face].Position.x, target.x, "starts right of the target");
            Assert.Greater(glideStart[face].Position.y, target.y, "starts below the target");

            InfoDemoElementState[] touch = Sample(timeline, 2f);
            Assert.AreEqual(target.x, touch[face].Position.x, TOLERANCE);
            Assert.AreEqual(target.y, touch[face].Position.y, TOLERANCE);
            Assert.AreEqual(1f, touch[face].Alpha, TOLERANCE);
            Assert.AreEqual(0.78f, touch[face].Scale, TOLERANCE, "pressed at the touch");
            Assert.AreEqual(0.3f, touch[ring].Scale, TOLERANCE);
            Assert.AreEqual(0.9f, touch[ring].Alpha, TOLERANCE);
            Assert.AreEqual(target, touch[ring].Position);

            InfoDemoElementState[] after = Sample(timeline, 3f);
            Assert.AreEqual(1f, after[face].Scale, TOLERANCE, "released");
            Assert.AreEqual(0f, after[face].Alpha, TOLERANCE, "lifted away");
            Assert.AreEqual(1.7f, after[ring].Scale, TOLERANCE);
            Assert.AreEqual(0f, after[ring].Alpha, TOLERANCE);
        }

        [Test]
        public void Beam_GrowsAlongItsLineFromTheCentre_ThenFades()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            float rowEnd = InfoDemoChoreography.Beam(builder, 1f, true, 4, InfoDemoPaint.PLATE_LINE_CLEAR);
            InfoDemoChoreography.Beam(builder, 1f, false, 3, InfoDemoPaint.PLATE_LINE_CLEAR);
            InfoDemoTimeline timeline = builder.Build();

            int rowGlow = InfoDemoLayout.BOARD_CELL_COUNT;
            int rowBar = rowGlow + 1;
            int columnBar = rowGlow + 3;
            Assert.AreEqual(InfoDemoElementKind.Glow, timeline.GetElement(rowGlow).Kind);
            Assert.AreEqual(InfoDemoElementKind.Panel, timeline.GetElement(rowBar).Kind);

            InfoDemoElementState[] start = Sample(timeline, 1f);
            Assert.AreEqual(new Vector2(0f, 1f), start[rowBar].Stretch, "a row beam starts collapsed along x");
            Assert.AreEqual(new Vector2(1f, 0f), start[columnBar].Stretch, "a column beam starts collapsed along y");
            Assert.AreEqual(4f, start[rowBar].Position.y, TOLERANCE);
            Assert.AreEqual(3f, start[columnBar].Position.x, TOLERANCE);

            InfoDemoElementState[] grown = Sample(timeline, 1f + InfoDemoChoreography.BEAM_GROW_DURATION);
            Assert.AreEqual(Vector2.one, grown[rowBar].Stretch);
            Assert.AreEqual(Vector2.one, grown[columnBar].Stretch);
            Assert.AreEqual(1f, grown[rowBar].Alpha, TOLERANCE);

            InfoDemoElementState[] gone = Sample(timeline, rowEnd);
            Assert.AreEqual(0f, gone[rowBar].Alpha, TOLERANCE);
            Assert.AreEqual(0f, gone[rowGlow].Alpha, TOLERANCE);
        }

        [Test]
        public void PressButton_PopsThePlate_ArmsUntilDisarm_AndSpendChargeTicksTheCountDown()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.Bomb, InfoDemoPaint.PLATE_BOMB, 3);
            InfoDemoPowerUpChoreography.PressButton(builder, button, 1f, true, 2f);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, 1.8f);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(InfoDemoLayout.ChipCentre, button.Centre);
            Assert.AreEqual(InfoDemoSprite.PowerUpIcon, timeline.GetElement(button.IconId).Sprite);
            Assert.AreEqual((int)PowerUpKind.Bomb, timeline.GetElement(button.IconId).SpriteParameter);
            Assert.AreEqual("3", timeline.GetElement(button.CountLabelId).LabelArgument);
            Assert.AreEqual("2", timeline.GetElement(button.SpentCountLabelId).LabelArgument);

            InfoDemoElementState[] idle = Sample(timeline, 0.5f);
            Assert.AreEqual(InfoDemoPaint.PLATE_BOMB, idle[button.PlateId].Paint);
            Assert.AreEqual(1f, idle[button.PlateId].Alpha, TOLERANCE);
            Assert.AreEqual(0f, idle[button.ArmedRingId].Alpha, TOLERANCE, "not armed yet");
            Assert.AreEqual(1f, idle[button.CountLabelId].Alpha, TOLERANCE);
            Assert.AreEqual(0f, idle[button.SpentCountLabelId].Alpha, TOLERANCE);

            InfoDemoElementState[] pressed = Sample(timeline, 1f);
            Assert.AreEqual(InfoDemoPowerUpChoreography.BUTTON_PRESS_SCALE, pressed[button.PlateId].Scale, TOLERANCE);

            InfoDemoElementState[] armed = Sample(timeline, 1.5f);
            Assert.AreEqual(1f, armed[button.PlateId].Scale, TOLERANCE, "popped back");
            Assert.AreEqual(1f, armed[button.ArmedRingId].Alpha, TOLERANCE, "armed");
            Assert.Greater(armed[button.ArmedGlowId].Alpha, 0f);

            InfoDemoElementState[] disarmed = Sample(timeline, 2.5f);
            Assert.AreEqual(0f, disarmed[button.ArmedRingId].Alpha, TOLERANCE, "disarmed");
            Assert.AreEqual(0f, disarmed[button.ArmedGlowId].Alpha, TOLERANCE);
            Assert.AreEqual(0f, disarmed[button.CountLabelId].Alpha, TOLERANCE, "3 is gone");
            Assert.AreEqual(1f, disarmed[button.SpentCountLabelId].Alpha, TOLERANCE, "2 shows");
        }

        [Test]
        public void PressButton_WithoutArming_NeverShowsTheArmedRing()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.Bomb, InfoDemoPaint.PLATE_BOMB, 3);
            InfoDemoPowerUpChoreography.PressButton(builder, button, 1f, false, 2f);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(0f, Sample(timeline, 1.5f)[button.ArmedRingId].Alpha, TOLERANCE);
        }

        [Test]
        public void OrderByDistance_PutsTheNearestFirst_AndKeepsTiesInTheirGivenOrder()
        {
            Vector2Int[] cells = { new Vector2Int(0, 0), new Vector2Int(3, 3), new Vector2Int(2, 3), new Vector2Int(4, 3) };

            Vector2Int[] ordered = InfoDemoChoreography.OrderByDistance(cells, new Vector2(3f, 3f));

            Assert.AreEqual(new Vector2Int(3, 3), ordered[0]);
            Assert.AreEqual(new Vector2Int(2, 3), ordered[1], "tie with (4,3): given order kept");
            Assert.AreEqual(new Vector2Int(4, 3), ordered[2]);
            Assert.AreEqual(new Vector2Int(0, 0), ordered[3]);
        }

        [Test]
        public void ClearCells_ClearsNearestFirst_AndLeavesEveryCellEmpty()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            builder.SetBoardRow(3, "..ppp...");
            Vector2Int[] cells = { new Vector2Int(2, 3), new Vector2Int(3, 3), new Vector2Int(4, 3) };
            float end = InfoDemoChoreography.ClearCells(builder, cells, new Vector2(4f, 3f), 1f, 0.1f);
            InfoDemoTimeline timeline = builder.Build();

            // (4,3) is nearest, so it starts shrinking first and is gone first.
            InfoDemoElementState[] midway = Sample(timeline, 1f + InfoDemoChoreography.CLEAR_FLASH_DURATION + 0.1f);
            Assert.Less(midway[InfoDemoLayout.BoardBlockId(3, 4)].Scale, midway[InfoDemoLayout.BoardBlockId(3, 2)].Scale);

            InfoDemoElementState[] states = Sample(timeline, end);
            for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(cells[cellIndex].y, cells[cellIndex].x)].Paint);
            }
        }

        [Test]
        public void Recolour_SwitchesThePaint_AndThePopSettlesBackToRest()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            builder.SetBoardRow(2, "..g.....");
            float settled = InfoDemoChoreography.Recolour(builder, 2, 2, InfoDemoPaint.BLOCK_1, 1f);
            InfoDemoTimeline timeline = builder.Build();
            int block = InfoDemoLayout.BoardBlockId(2, 2);

            Assert.AreEqual(InfoDemoPaint.BLOCK_2, Sample(timeline, 0.9f)[block].Paint);
            InfoDemoElementState[] popping = Sample(timeline, 1f + (InfoDemoChoreography.RECOLOUR_POP_DURATION * 0.5f));
            Assert.AreEqual(InfoDemoPaint.BLOCK_1, popping[block].Paint);
            Assert.Greater(popping[block].Scale, 1f);
            Assert.Greater(popping[block].Flash, 0f);

            InfoDemoElementState[] rest = Sample(timeline, settled);
            Assert.AreEqual(1f, rest[block].Scale, 0.001f);
            Assert.AreEqual(0f, rest[block].Flash, 0.001f);
        }

        // ---------------------------------------------------------------- demos

        [Test]
        public void BombDemo_ClearsExactlyTheThreeByThreeAroundTheTappedCell()
        {
            InfoDemoTimeline timeline = Demo(PowerUpKind.Bomb);
            Assert.AreEqual(BombInfoDemo.LOOP_DURATION, timeline.Duration, TOLERANCE);

            InfoDemoElementState[] before = Sample(timeline, 1f);
            InfoDemoElementState[] after = Sample(timeline, AFTER_EFFECT);

            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    int block = InfoDemoLayout.BoardBlockId(row, column);
                    bool inBlast = Mathf.Abs(row - BombInfoDemo.TARGET_ROW) <= 1 && Mathf.Abs(column - BombInfoDemo.TARGET_COLUMN) <= 1;
                    int expected = inBlast ? InfoDemoPaint.NONE : InfoDemoBoardPattern.PaintAt(BombInfoDemo.Rows, row, column);
                    Assert.AreEqual(expected, after[block].Paint, $"({row},{column})");

                    if (inBlast)
                    {
                        Assert.AreNotEqual(InfoDemoPaint.NONE, before[block].Paint, $"({row},{column}) starts occupied");
                    }
                }
            }
        }

        [Test]
        public void RowClearDemo_ClearsTheWholeNotFullRow_AndNothingElse()
        {
            string[] rows = LineClearPowerUpInfoDemo.RowClearRows;
            int targetRow = LineClearPowerUpInfoDemo.TARGET_ROW;
            Assert.Less(Occupied(rows, targetRow, 0, targetRow, 7), InfoDemoLayout.BOARD_SIZE, "the row starts not full");

            InfoDemoElementState[] after = Sample(Demo(PowerUpKind.RowClear), AFTER_EFFECT);

            AssertBoard(after, rows, (row, column) => row == targetRow);
        }

        [Test]
        public void ColumnClearDemo_ClearsTheWholeNotFullColumn_AndNothingElse()
        {
            string[] rows = LineClearPowerUpInfoDemo.ColumnClearRows;
            int targetColumn = LineClearPowerUpInfoDemo.TARGET_COLUMN;
            Assert.Less(Occupied(rows, 0, targetColumn, 7, targetColumn), InfoDemoLayout.BOARD_SIZE, "the column starts not full");

            InfoDemoElementState[] after = Sample(Demo(PowerUpKind.ColumnClear), AFTER_EFFECT);

            AssertBoard(after, rows, (row, column) => column == targetColumn);
        }

        [Test]
        public void JokerDemo_FillsTheTappedEmptyCell_ThenTheRowItCompletedClears()
        {
            string[] rows = JokerInfoDemo.Rows;
            int targetRow = JokerInfoDemo.TARGET_ROW;
            int targetBlock = InfoDemoLayout.BoardBlockId(targetRow, JokerInfoDemo.TARGET_COLUMN);
            InfoDemoTimeline timeline = Demo(PowerUpKind.Joker);

            Assert.AreEqual(InfoDemoPaint.NONE, Sample(timeline, 1f)[targetBlock].Paint, "the tapped cell starts empty");

            InfoDemoElementState[] filled = Sample(timeline, JokerInfoDemo.CLEAR_START - 0.05f);
            Assert.AreEqual(InfoDemoPaint.BLOCK_1, filled[targetBlock].Paint);
            for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
            {
                Assert.AreNotEqual(InfoDemoPaint.NONE, filled[InfoDemoLayout.BoardBlockId(targetRow, column)].Paint, "row full");
            }

            AssertBoard(Sample(timeline, AFTER_EFFECT), rows, (row, column) => row == targetRow);
        }

        [Test]
        public void ColorCleanserDemo_ClearsEveryBlockOfTheTappedColour_AndNoOther()
        {
            string[] rows = ColorCleanserInfoDemo.Rows;
            int targetPaint = InfoDemoBoardPattern.PaintAt(rows, ColorCleanserInfoDemo.TARGET_ROW, ColorCleanserInfoDemo.TARGET_COLUMN);
            Assert.AreNotEqual(InfoDemoPaint.NONE, targetPaint, "the tapped cell is occupied");

            InfoDemoElementState[] after = Sample(Demo(PowerUpKind.ColorCleanser), AFTER_EFFECT);

            AssertBoard(after, rows, (row, column) => InfoDemoBoardPattern.PaintAt(rows, row, column) == targetPaint);
        }

        [Test]
        public void PaintCrossDemo_PaintsEveryOccupiedCrossCell_ClearsNothing()
        {
            string[] rows = PaintCrossInfoDemo.Rows;
            int targetRow = PaintCrossInfoDemo.TARGET_ROW;
            int targetColumn = PaintCrossInfoDemo.TARGET_COLUMN;
            InfoDemoTimeline timeline = Demo(PowerUpKind.PaintCross);
            Assert.AreEqual(PaintCrossInfoDemo.LOOP_DURATION, timeline.Duration, TOLERANCE);

            InfoDemoElementState[] after = Sample(timeline, AFTER_EFFECT);
            int occupiedBefore = 0;
            int occupiedAfter = 0;

            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    int initial = InfoDemoBoardPattern.PaintAt(rows, row, column);
                    int now = after[InfoDemoLayout.BoardBlockId(row, column)].Paint;
                    occupiedBefore += initial != InfoDemoPaint.NONE ? 1 : 0;
                    occupiedAfter += now != InfoDemoPaint.NONE ? 1 : 0;

                    bool onCross = row == targetRow || column == targetColumn;
                    int expected = onCross && initial != InfoDemoPaint.NONE ? PaintCrossInfoDemo.PAINT : initial;
                    Assert.AreEqual(expected, now, $"({row},{column})");
                }
            }

            Assert.AreEqual(occupiedBefore, occupiedAfter, "nothing cleared, nothing added");
            Assert.AreEqual(11, InfoDemoBoardPattern.OccupiedCross(rows, targetRow, targetColumn).Length);
        }

        // ---------------------------------------------------------------- helpers

        private static InfoDemoTimeline Demo(PowerUpKind kind)
            => new InfoDemoCatalog().Find(InfoPopupSubjectKind.PowerUp, (int)kind);

        private static InfoDemoElementState[] Sample(InfoDemoTimeline timeline, float time)
        {
            InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];
            timeline.Evaluate(time, states);
            return states;
        }

        private static int Occupied(string[] rows, int topRow, int leftColumn, int bottomRow, int rightColumn)
            => InfoDemoBoardPattern.OccupiedCells(rows, topRow, leftColumn, bottomRow, rightColumn).Length;

        /// <summary>Every cell <paramref name="isCleared"/> picks is empty; every other cell still holds
        /// its starting paint.</summary>
        private static void AssertBoard(InfoDemoElementState[] states, string[] rows, Func<int, int, bool> isCleared)
        {
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    int expected = isCleared(row, column) ? InfoDemoPaint.NONE : InfoDemoBoardPattern.PaintAt(rows, row, column);
                    Assert.AreEqual(expected, states[InfoDemoLayout.BoardBlockId(row, column)].Paint, $"({row},{column})");
                }
            }
        }
    }
}
