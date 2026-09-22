using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Cover for the reinforced cell (issue #153): a pre-filled block that absorbs a fixed number of
    /// hits before a clear can take it away.
    /// <para>
    /// The file is deliberately ordered by what it is protecting. First the
    /// <see cref="Board.TryDamage"/> primitive, including the regression guard that matters most — on a
    /// cell nothing reinforced it behaves exactly as the unconditional <see cref="Board.Clear"/> it
    /// replaced at every clearing call site, which is what keeps every other special-cell mechanic
    /// untouched. Then each acceptance criterion in turn.
    /// </para>
    /// <para>
    /// <b>AC8 (undo restores a hit count) is not covered here because it is not implemented</b>, by
    /// explicit product decision: one-step undo does not exist anywhere in this project yet, so there
    /// is nothing to integrate with. <see cref="Board.Clone"/>/<see cref="Board.CopyFrom"/> do copy hit
    /// counts (covered below), which is what an undo built on them will need.
    /// </para>
    /// </summary>
    public class ReinforcedCellTests
    {
        private const int COLOUR = 3;
        private const int OTHER_COLOUR = 5;

        /// <summary>The serialized form of <see cref="ObjectiveType.ReinforcedCellsCleared"/>: a row is
        /// authored as JSON, and JsonUtility writes an enum as its underlying int.</summary>
        private const int REINFORCED_CELLS_CLEARED = (int)ObjectiveType.ReinforcedCellsCleared;

        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        /// <summary>Covers the row's one gap at (0,4) and the column's one gap at (4,0) in a single
        /// placement — the only way a 1x1-sized gap pair can be filled simultaneously, and so the only
        /// way to make one placement clear a row and a column through a cell placed earlier. Not a
        /// catalog piece; placement only ever asks whether the cells are free.</summary>
        private static readonly Piece GapPair = new Piece(
            "test_gap_pair", new[] { new GridPosition(0, 0), new GridPosition(4, -4) });

        // --- Board.TryDamage: the primitive every clearing path now removes cells through ---

        /// <summary>The headline regression guard: on an ordinary cell the gate is the old
        /// unconditional clear, to the letter. Every existing mechanic's behaviour rests on this.</summary>
        [Test]
        public void TryDamage_OnAnOrdinaryOccupiedCell_RemovesItAndReportsSo()
        {
            var board = new Board();
            var position = new GridPosition(2, 3);
            board.Occupy(position, COLOUR);

            bool removed = board.TryDamage(position);

            Assert.IsTrue(removed);
            Assert.IsFalse(board.IsOccupied(position));
            Assert.AreEqual(0, board.GetHitCount(position));
        }

        /// <summary>And on a cell that carried a special kind, the kind goes with it — same as
        /// <see cref="Board.Clear"/>, so detection's "read it before you destroy it" contract is
        /// unchanged.</summary>
        [Test]
        public void TryDamage_OnAnOrdinarySpecialCell_ResetsItsKind()
        {
            var board = new Board();
            var position = new GridPosition(0, 0);
            board.Occupy(position, COLOUR);
            board.SetSpecialKind(position, SpecialCellKind.ExplosiveCore);

            Assert.IsTrue(board.TryDamage(position));
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(position));
        }

        [Test]
        public void TryDamage_OnAReinforcedCellWithHitsToSpare_SpendsOneAndLeavesItStanding()
        {
            var board = new Board();
            var position = new GridPosition(4, 4);
            board.OccupyReinforced(position, COLOUR, 3);

            bool removed = board.TryDamage(position);

            Assert.IsFalse(removed);
            Assert.IsTrue(board.IsOccupied(position));
            Assert.AreEqual(COLOUR, board[position], "A damaged block keeps its colour.");
            Assert.AreEqual(2, board.GetHitCount(position));
        }

        [Test]
        public void TryDamage_OnAReinforcedCellOnItsLastHit_RemovesItLikeAnyOtherCell()
        {
            var board = new Board();
            var position = new GridPosition(4, 4);
            board.OccupyReinforced(position, COLOUR, 1);

            bool removed = board.TryDamage(position);

            Assert.IsTrue(removed);
            Assert.IsFalse(board.IsOccupied(position));
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(position));
            Assert.AreEqual(0, board.GetHitCount(position));
        }

        /// <summary>Four hits authored, four hits spent: the counts really do step down one at a
        /// time.</summary>
        [Test]
        public void TryDamage_RepeatedOnAFourHitCell_RemovesItOnTheFourthCall()
        {
            var board = new Board();
            var position = new GridPosition(1, 1);
            board.OccupyReinforced(position, COLOUR, 4);

            Assert.IsFalse(board.TryDamage(position));
            Assert.IsFalse(board.TryDamage(position));
            Assert.IsFalse(board.TryDamage(position));
            Assert.AreEqual(1, board.GetHitCount(position));

            Assert.IsTrue(board.TryDamage(position));
            Assert.IsFalse(board.IsOccupied(position));
        }

        [Test]
        public void OccupyReinforced_SetsBothTheColourAndTheHitCount()
        {
            var board = new Board();
            var position = new GridPosition(6, 2);

            board.OccupyReinforced(position, COLOUR, 2);

            Assert.AreEqual(COLOUR, board[position]);
            Assert.AreEqual(2, board.GetHitCount(position));
        }

        /// <summary>An ordinary placement never reinforces anything — the two lifecycles are
        /// separate.</summary>
        [Test]
        public void Occupy_LeavesTheHitCountAtZero()
        {
            var board = new Board();
            var position = new GridPosition(6, 2);

            board.Occupy(position, COLOUR);

            Assert.AreEqual(0, board.GetHitCount(position));
        }

        /// <summary>The force-clear primitive is still a force-clear: it spends no hit and asks for
        /// none, which is what lets a run start on a board that inherits nothing.</summary>
        [Test]
        public void Clear_OnAReinforcedCell_WipesItOutrightHitCountIncluded()
        {
            var board = new Board();
            var position = new GridPosition(3, 3);
            board.OccupyReinforced(position, COLOUR, 4);

            board.Clear(position);

            Assert.IsFalse(board.IsOccupied(position));
            Assert.AreEqual(0, board.GetHitCount(position));
        }

        [Test]
        public void Clone_CopiesHitCounts()
        {
            var board = new Board();
            var position = new GridPosition(5, 1);
            board.OccupyReinforced(position, COLOUR, 3);

            Board copy = board.Clone();

            Assert.AreEqual(3, copy.GetHitCount(position));

            // And it is a copy, not a view: damaging one must not damage the other.
            copy.TryDamage(position);
            Assert.AreEqual(3, board.GetHitCount(position));
            Assert.AreEqual(2, copy.GetHitCount(position));
        }

        [Test]
        public void CopyFrom_CopiesHitCounts()
        {
            var source = new Board();
            var position = new GridPosition(5, 1);
            source.OccupyReinforced(position, COLOUR, 2);

            var destination = new Board();
            destination.Occupy(position, OTHER_COLOUR);
            destination.CopyFrom(source);

            Assert.AreEqual(2, destination.GetHitCount(position));
            Assert.AreEqual(COLOUR, destination[position]);
        }

        /// <summary>The other direction: copying a board that reinforced nothing must clear the
        /// destination's stale counts, or a reused scratch board would drift out of step with the real
        /// one.</summary>
        [Test]
        public void CopyFrom_ABoardWithNoReinforcedCells_ClearsStaleHitCounts()
        {
            var source = new Board();
            var position = new GridPosition(5, 1);
            source.Occupy(position, COLOUR);

            var destination = new Board();
            destination.OccupyReinforced(position, COLOUR, 4);
            destination.CopyFrom(source);

            Assert.AreEqual(0, destination.GetHitCount(position));
        }

        // --- AC3: a line clear through a reinforced cell damages it and scores nothing for it ---

        [Test]
        public void ResolveClears_ThroughAReinforcedCell_SpendsOneHitAndLeavesItOccupied()
        {
            var board = new Board();
            var reinforced = new GridPosition(2, 3);
            FillRowWithReinforcedCellAt(board, 3, reinforced, hitCount: 2);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(1, result.LineCount, "The row was full, so it cleared.");
            Assert.IsTrue(board.IsOccupied(reinforced), "The reinforced cell absorbed the clear.");
            Assert.AreEqual(1, board.GetHitCount(reinforced));
            Assert.AreEqual(COLOUR, board[reinforced]);
        }

        [Test]
        public void ResolveClears_ThroughAReinforcedCell_DoesNotCountItAsCleared()
        {
            var board = new Board();
            FillRowWithReinforcedCellAt(board, 3, new GridPosition(2, 3), hitCount: 2);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(
                Board.SIZE - 1, result.ClearedCellCount,
                "Seven cells went; the reinforced one is still standing and must not be scored.");
            Assert.AreEqual(0, result.ReinforcedCellsFullyClearedCount);
        }

        /// <summary>Every other cell of the line still goes — the reinforced cell absorbs the clear for
        /// itself alone.</summary>
        [Test]
        public void ResolveClears_ThroughAReinforcedCell_StillEmptiesTheRestOfTheLine()
        {
            var board = new Board();
            var reinforced = new GridPosition(2, 3);
            FillRowWithReinforcedCellAt(board, 3, reinforced, hitCount: 2);

            LineClearResolver.ResolveClears(board);

            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, 3);
                if (position.Equals(reinforced))
                {
                    continue;
                }

                Assert.IsFalse(board.IsOccupied(position), $"Cell {position} should have been cleared.");
            }
        }

        /// <summary>The surviving cell keeps the row from clearing again for free: the row is no longer
        /// full, so the next pass finds nothing.</summary>
        [Test]
        public void ResolveClears_RunAgainAfterAReinforcedCellSurvived_ClearsNothing()
        {
            var board = new Board();
            FillRowWithReinforcedCellAt(board, 3, new GridPosition(2, 3), hitCount: 3);

            LineClearResolver.ResolveClears(board);
            LineClearResult second = LineClearResolver.ResolveClears(board);

            Assert.IsFalse(second.AnyCleared);
            Assert.AreEqual(2, board.GetHitCount(new GridPosition(2, 3)), "Only the first clear hit it.");
        }

        /// <summary>The drag hint agrees with the clear it is hinting at: a reinforced cell that would
        /// only be damaged is not counted, and nothing on the real board is touched.</summary>
        [Test]
        public void PreviewClears_ThroughAReinforcedCell_CountsOnlyTheCellsThatWouldGo()
        {
            var board = new Board();
            var reinforced = new GridPosition(2, 3);
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, 3);
                if (position.Equals(reinforced))
                {
                    board.OccupyReinforced(position, COLOUR, 2);
                }
                else if (x != Board.SIZE - 1)
                {
                    board.Occupy(position, COLOUR);
                }
            }

            LineClearResult preview = LineClearResolver.PreviewClears(
                board, Single, new GridPosition(Board.SIZE - 1, 3), new Board(),
                new List<int>(), new List<int>());

            Assert.AreEqual(1, preview.LineCount);
            Assert.AreEqual(Board.SIZE - 1, preview.ClearedCellCount);
            Assert.AreEqual(2, board.GetHitCount(reinforced), "A preview must not damage anything.");
        }

        // --- AC4: a row and a column clearing through one reinforced cell is ONE hit ---

        [Test]
        public void ResolveClears_WithARowAndAColumnThroughTheSameReinforcedCell_SpendsExactlyOneHit()
        {
            var board = new Board();
            var reinforced = new GridPosition(4, 4);
            board.OccupyReinforced(reinforced, COLOUR, 3);

            for (int i = 0; i < Board.SIZE; i++)
            {
                var rowCell = new GridPosition(i, 4);
                if (!rowCell.Equals(reinforced))
                {
                    board.Occupy(rowCell, COLOUR);
                }

                var columnCell = new GridPosition(4, i);
                if (!columnCell.Equals(reinforced))
                {
                    board.Occupy(columnCell, COLOUR);
                }
            }

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(2, result.LineCount, "One row and one column cleared.");
            Assert.AreEqual(
                2, board.GetHitCount(reinforced),
                "Two lines destroyed it at once, but a cell is only hit once per clear.");
            Assert.IsTrue(board.IsOccupied(reinforced));
        }

        // --- AC6: the last hit is an ordinary destruction in every respect ---

        [Test]
        public void ResolveClears_FinishingOffAReinforcedCell_CountsAndReportsItLikeAnyCell()
        {
            var board = new Board();
            var reinforced = new GridPosition(2, 3);
            FillRowWithReinforcedCellAt(board, 3, reinforced, hitCount: 1);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(Board.SIZE, result.ClearedCellCount, "Every cell of the row went.");
            Assert.AreEqual(
                1, result.ReinforcedCellsFullyClearedCount,
                "One of them was a reinforced cell taking its last hit.");
            Assert.IsFalse(board.IsOccupied(reinforced));
            Assert.AreEqual(0, board.GetHitCount(reinforced));
        }

        // --- AC5: a power-up's clear damages exactly as a line clear does ---

        [Test]
        public void ResolveBombClear_OverAReinforcedCell_SpendsOneHitAndDoesNotReportIt()
        {
            var board = new Board();
            var reinforced = new GridPosition(3, 3);
            board.OccupyReinforced(reinforced, COLOUR, 2);
            board.Occupy(new GridPosition(4, 3), OTHER_COLOUR);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(4, 3));

            Assert.IsTrue(board.IsOccupied(reinforced));
            Assert.AreEqual(1, board.GetHitCount(reinforced));
            Assert.AreEqual(1, result.ClearedCellCount, "Only the ordinary neighbour was destroyed.");
            Assert.AreEqual(new GridPosition(4, 3), result.ClearedCells[0]);
            Assert.AreEqual(0, result.ReinforcedCellsFullyClearedCount);
        }

        [Test]
        public void ResolveBombClear_FinishingOffAReinforcedCell_ReportsItAsCleared()
        {
            var board = new Board();
            var reinforced = new GridPosition(3, 3);
            board.OccupyReinforced(reinforced, COLOUR, 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(3, 3));

            Assert.IsFalse(board.IsOccupied(reinforced));
            Assert.AreEqual(1, result.ClearedCellCount);
            Assert.AreEqual(1, result.ReinforcedCellsFullyClearedCount);
        }

        /// <summary>
        /// The invariant that would be silently wrong if the gate ran after detection instead of before
        /// it: a cell that survived was not destroyed, so it owes no effect. Nothing in the game gives a
        /// reinforced cell a <see cref="SpecialCellKind"/> — the two systems are independent by design —
        /// so this arranges the combination by hand purely to pin the ordering down.
        /// </summary>
        [Test]
        public void ResolveBombClear_OverASurvivingReinforcedCell_TriggersNoSpecialEffect()
        {
            var board = new Board();
            var reinforced = new GridPosition(3, 3);
            board.OccupyReinforced(reinforced, COLOUR, 2);
            board.SetSpecialKind(reinforced, SpecialCellKind.ExplosiveCore);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, reinforced);

            Assert.AreEqual(0, result.TriggeredSpecials.Count, "A cell that survived owes nothing.");
            Assert.AreEqual(
                SpecialCellKind.ExplosiveCore, board.GetSpecialKind(reinforced),
                "And it keeps its kind, because it was not destroyed.");
        }

        [Test]
        public void ResolveRowClear_OverAReinforcedCell_SpendsOneHit()
        {
            var board = new Board();
            var reinforced = new GridPosition(2, 5);
            board.OccupyReinforced(reinforced, COLOUR, 3);
            board.Occupy(new GridPosition(3, 5), OTHER_COLOUR);

            PowerUpClearResult result = PowerUpClearResolver.ResolveRowClear(board, 5);

            Assert.AreEqual(2, board.GetHitCount(reinforced));
            Assert.AreEqual(1, result.ClearedCellCount);
            Assert.IsFalse(
                board.IsRowEmpty(5),
                "The row is not empty, so it must not be reported as emptied.");
            Assert.AreEqual(0, result.EmptiedRows.Count);
        }

        /// <summary>A cascade effect's own destruction goes through the same gate: a laser's wipe
        /// damages a reinforced cell rather than removing it, and does not report it as wiped.</summary>
        [Test]
        public void LaserEffect_WipingThroughAReinforcedCell_SpendsOneHitAndDoesNotReportIt()
        {
            var board = new Board();
            var reinforced = new GridPosition(2, 6);
            board.OccupyReinforced(reinforced, COLOUR, 2);
            board.Occupy(new GridPosition(5, 6), OTHER_COLOUR);

            var laser = new LaserEffect();
            laser.BeginResolution();
            laser.Apply(
                board,
                new SpecialCellTrigger(new GridPosition(0, 6), SpecialCellKind.Laser, ClearAxis.Column));

            Assert.IsTrue(board.IsOccupied(reinforced));
            Assert.AreEqual(1, board.GetHitCount(reinforced));
            Assert.IsFalse(Contains(laser.WipedCells, reinforced));
            Assert.IsTrue(Contains(laser.WipedCells, new GridPosition(5, 6)));
        }

        /// <summary>The core's bonus wipe (issue #398) goes through the same gate as a laser's: it
        /// damages a reinforced cell on the wiped line rather than removing it, and does not report it
        /// as wiped.</summary>
        [Test]
        public void ExplosiveCoreEffect_WipingThroughAReinforcedCell_SpendsOneHitAndDoesNotReportIt()
        {
            var board = new Board();
            var reinforced = new GridPosition(2, 6);
            board.OccupyReinforced(reinforced, COLOUR, 2);
            board.Occupy(new GridPosition(5, 6), OTHER_COLOUR);

            var core = new ExplosiveCoreEffect();
            core.BeginResolution();
            core.Apply(
                board,
                new SpecialCellTrigger(new GridPosition(0, 6), SpecialCellKind.ExplosiveCore, ClearAxis.Column));

            Assert.IsTrue(board.IsOccupied(reinforced));
            Assert.AreEqual(1, board.GetHitCount(reinforced));
            Assert.IsFalse(Contains(core.WipedCells, reinforced));
            Assert.IsTrue(Contains(core.WipedCells, new GridPosition(5, 6)));
        }

        /// <summary>
        /// Issue #349: a fill only ever targets empty cells, so a Reinforced one — occupied by
        /// definition — is never overwritten by it. With nothing else on the board there is no island
        /// (one single connected empty region) and the reinforced cell is the only occupied cell, so the
        /// vortex hands its tag off to it instead — a hand-off only relabels a cell, so neither its
        /// colour nor its remaining hits change.
        /// </summary>
        [Test]
        public void VortexEffect_WithAnIsolatedReinforcedCell_HandsOffToItWithoutDamagingIt()
        {
            var board = new Board();
            var reinforced = new GridPosition(0, 0);
            board.OccupyReinforced(reinforced, COLOUR, 2);

            var vortex = new VortexEffect(new System.Random(1));
            vortex.BeginResolution();
            vortex.Apply(board, new SpecialCellTrigger(new GridPosition(4, 4), SpecialCellKind.Vortex));

            Assert.AreEqual(0, vortex.FilledCells.Count, "A reinforced cell is never a fill target.");
            Assert.AreEqual(1, vortex.HandOffTargets.Count);
            Assert.AreEqual(reinforced, vortex.HandOffTargets[0]);
            Assert.AreEqual(SpecialCellKind.Vortex, board.GetSpecialKind(reinforced));
            Assert.IsTrue(board.IsOccupied(reinforced));
            Assert.AreEqual(COLOUR, board[reinforced], "Untouched colour.");
            Assert.AreEqual(2, board.GetHitCount(reinforced), "Untouched hit count.");
        }

        // --- AC7 (negative) and the placement-level report, through the real System ---

        /// <summary>AC7: a placement that completes nothing leaves every reinforced cell exactly as it
        /// was.</summary>
        [Test]
        public void TryPlacePiece_WithNoClearAndNoEffect_LeavesEveryHitCountUntouched()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel, out _);
            trayModel.SetSlot(0, Single, COLOUR);

            var reinforced = new GridPosition(6, 6);
            boardModel.Board.OccupyReinforced(reinforced, COLOUR, 3);

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 0)));

            Assert.AreEqual(3, boardModel.GetHitCount(reinforced));
            Assert.IsTrue(boardModel.Board.IsOccupied(reinforced));
        }

        /// <summary>The #154 groundwork, end to end: a placement that finishes off a reinforced cell
        /// reports exactly one, and an ordinary clear reports none.</summary>
        [Test]
        public void TryPlacePiece_FinishingOffAReinforcedCell_ReportsItOnThePlacementMessage()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PiecePlacedMessage> placedBroker);
            trayModel.SetSlot(0, Single, COLOUR);

            var reinforced = new GridPosition(2, 3);
            boardModel.Board.OccupyReinforced(reinforced, COLOUR, 1);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                var position = new GridPosition(x, 3);
                if (!position.Equals(reinforced))
                {
                    boardModel.Occupy(position, COLOUR);
                }
            }

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.AreEqual(1, placedBroker.Published.Count);
            Assert.AreEqual(1, placedBroker.Published[0].LinesCleared, "The row cleared.");
            Assert.AreEqual(1, placedBroker.Published[0].ReinforcedCellsFullyClearedCount);
        }

        [Test]
        public void TryPlacePiece_ClearingThroughASurvivingReinforcedCell_ReportsNoneFullyCleared()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PiecePlacedMessage> placedBroker);
            trayModel.SetSlot(0, Single, COLOUR);

            var reinforced = new GridPosition(2, 3);
            boardModel.Board.OccupyReinforced(reinforced, COLOUR, 2);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                var position = new GridPosition(x, 3);
                if (!position.Equals(reinforced))
                {
                    boardModel.Occupy(position, COLOUR);
                }
            }

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.AreEqual(0, placedBroker.Published[0].ReinforcedCellsFullyClearedCount);
            Assert.AreEqual(1, boardModel.GetHitCount(reinforced));
        }

        /// <summary>AC4 through the real System, with a single placement that completes the row and the
        /// column at once: one piece, two lines, one hit.</summary>
        [Test]
        public void TryPlacePiece_ClearingARowAndAColumnThroughOneReinforcedCell_SpendsOneHit()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel, out _);

            var reinforced = new GridPosition(4, 4);
            boardModel.Board.OccupyReinforced(reinforced, COLOUR, 3);

            // Row 4 is short of (0,4) and column 4 is short of (4,0); the piece below covers exactly
            // those two cells, so both lines complete in the same placement and neither before it.
            for (int i = 0; i < Board.SIZE; i++)
            {
                var rowCell = new GridPosition(i, 4);
                if (!rowCell.Equals(reinforced) && i != 0)
                {
                    boardModel.Occupy(rowCell, COLOUR);
                }

                var columnCell = new GridPosition(4, i);
                if (!columnCell.Equals(reinforced) && i != 0)
                {
                    boardModel.Occupy(columnCell, COLOUR);
                }
            }

            trayModel.SetSlot(0, GapPair, COLOUR);
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 4)));

            Assert.AreEqual(
                2, boardModel.GetHitCount(reinforced),
                "Row and column cleared together — one hit, not two.");
            Assert.IsTrue(boardModel.Board.IsOccupied(reinforced));
        }

        // --- Level authoring ---

        [Test]
        public void ReinforcedCells_OnARowAuthoredBeforeTheFieldExisted_IsEmpty()
        {
            LevelObjectiveConfig config = ARow("{\"_levelNumber\":1,\"_targetValue\":1}");

            Assert.AreEqual(0, config.ReinforcedCells.Count);
        }

        [Test]
        public void ReinforcedCells_OnARowAuthoringTwo_ReadsBothPositionsAndCounts()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_reinforcedCells\":["
                + "{\"_x\":1,\"_y\":2,\"_hitCount\":2},{\"_x\":5,\"_y\":6,\"_hitCount\":4}]}");

            Assert.AreEqual(2, config.ReinforcedCells.Count);
            Assert.AreEqual(2, config.ReinforcedCells[0].HitCount);
            Assert.AreEqual(4, config.ReinforcedCells[1].HitCount);
        }

        [TestCase(0, 2)]
        [TestCase(1, 2)]
        [TestCase(2, 2)]
        [TestCase(4, 4)]
        [TestCase(9, 4)]
        [TestCase(-3, 2)]
        public void ValidateInEditor_ClampsTheHitCountIntoRange(int authored, int expected)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_reinforcedCells\":["
                + $"{{\"_x\":1,\"_y\":1,\"_hitCount\":{authored}}}]}}");

            config.ValidateInEditor();

            Assert.AreEqual(expected, config.ReinforcedCells[0].HitCount);
        }

        [TestCase(8, 1)]
        [TestCase(1, 8)]
        [TestCase(-1, 1)]
        public void IsValid_WithAReinforcedCellOutsideTheBoard_Fails(int x, int y)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_reinforcedCells\":["
                + $"{{\"_x\":{x},\"_y\":{y},\"_hitCount\":2}}]}}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [TestCase(1)]
        [TestCase(5)]
        public void IsValid_WithAHitCountOutsideTwoToFour_Fails(int hitCount)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_reinforcedCells\":["
                + $"{{\"_x\":1,\"_y\":1,\"_hitCount\":{hitCount}}}]}}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void IsValid_WithAReinforcedCellOnAHole_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_boardHoles\":[{\"_x\":2,\"_y\":2}],"
                + "\"_reinforcedCells\":[{\"_x\":2,\"_y\":2,\"_hitCount\":2}]}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void IsValid_WithAWellFormedReinforcedCell_Passes()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_reinforcedCells\":["
                + "{\"_x\":2,\"_y\":2,\"_hitCount\":3}]}");

            Assert.IsTrue(config.IsValid(out string error), error);
        }

        // --- The "clear all reinforced cells" objective's authored target (issue #154) ---

        /// <summary>#154 AC2: the target is "all of them", always. Whatever <c>_targetValue</c> the
        /// Inspector holds is ignored for this type — a subset target would clear the level with
        /// reinforced blocks still standing.</summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(99)]
        public void ToObjectiveDefinition_ForAReinforcedCellsClearedObjective_TargetsEveryAuthoredCell(
            int authoredTarget)
        {
            LevelObjectiveConfig config = ARow(
                $"{{\"_levelNumber\":1,\"_objectiveType\":{REINFORCED_CELLS_CLEARED},"
                + $"\"_targetValue\":{authoredTarget},\"_reinforcedCells\":["
                + "{\"_x\":1,\"_y\":1,\"_hitCount\":2},{\"_x\":2,\"_y\":2,\"_hitCount\":3},"
                + "{\"_x\":3,\"_y\":3,\"_hitCount\":4}]}");

            ObjectiveDefinition definition = config.ToObjectiveDefinition();

            Assert.AreEqual(ObjectiveType.ReinforcedCellsCleared, definition.Type);
            Assert.AreEqual(3, definition.TargetValue);
        }

        /// <summary>The converse: the override is scoped to the one type, so every other objective still
        /// builds with exactly the target it authored — reinforced cells on the board or not.</summary>
        [Test]
        public void ToObjectiveDefinition_ForAnyOtherObjectiveType_StillUsesTheAuthoredTarget()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":5,\"_requiredLineCount\":1,\"_reinforcedCells\":["
                + "{\"_x\":1,\"_y\":1,\"_hitCount\":2}]}");

            Assert.AreEqual(5, config.ToObjectiveDefinition().TargetValue);
        }

        /// <summary>#154 AC2's failure mode, caught in validation rather than as a throw from
        /// <see cref="ObjectiveDefinition"/>'s non-positive-target guard.</summary>
        [Test]
        public void IsValid_WithAReinforcedCellsClearedObjectiveAndNoReinforcedCells_Fails()
        {
            LevelObjectiveConfig config = ARow(
                $"{{\"_levelNumber\":1,\"_objectiveType\":{REINFORCED_CELLS_CLEARED},"
                + "\"_targetValue\":1,\"_requiredLineCount\":1}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
            StringAssert.Contains("ReinforcedCellsCleared", error);
        }

        [Test]
        public void IsValid_WithAReinforcedCellsClearedObjectiveAndOneCell_Passes()
        {
            LevelObjectiveConfig config = ARow(
                $"{{\"_levelNumber\":1,\"_objectiveType\":{REINFORCED_CELLS_CLEARED},"
                + "\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_reinforcedCells\":[{\"_x\":1,\"_y\":1,\"_hitCount\":2}]}");

            Assert.IsTrue(config.IsValid(out string error), error);
        }

        // --- Level-start seeding (AC1) ---

        [Test]
        public void StartNewRun_OnALevelAuthoringReinforcedCells_OccupiesThemWithTheAuthoredHitCounts()
        {
            var catalog = ACatalogOf(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_reinforcedCells\":["
                + "{\"_x\":1,\"_y\":1,\"_hitCount\":2},{\"_x\":6,\"_y\":7,\"_hitCount\":4}]}");
            var progressionModel = new LevelProgressionModel();
            progressionModel.CurrentLevelNumber.Value = 1;

            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out _, out _,
                new LevelReinforcedCellSeeder(
                    catalog, progressionModel, new PathRunModel(), new WeightedPieceDraw(seed: 1)));

            system.StartNewRun();

            Assert.IsTrue(boardModel.Board.IsOccupied(new GridPosition(1, 1)));
            Assert.AreEqual(2, boardModel.GetHitCount(new GridPosition(1, 1)));
            Assert.IsTrue(boardModel.Board.IsOccupied(new GridPosition(6, 7)));
            Assert.AreEqual(4, boardModel.GetHitCount(new GridPosition(6, 7)));
            Assert.AreEqual(2, boardModel.Board.OccupiedCellCount(), "Nothing else is on the board.");

            Object.DestroyImmediate(catalog);
        }

        /// <summary>A run of a level that authors none opens on a bare board, exactly as every level
        /// authored before the mechanic existed does.</summary>
        [Test]
        public void StartNewRun_OnALevelAuthoringNone_LeavesTheBoardEmpty()
        {
            var catalog = ACatalogOf("{\"_levelNumber\":1,\"_targetValue\":1}");
            var progressionModel = new LevelProgressionModel();
            progressionModel.CurrentLevelNumber.Value = 1;

            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out _, out _,
                new LevelReinforcedCellSeeder(
                    catalog, progressionModel, new PathRunModel(), new WeightedPieceDraw(seed: 1)));

            system.StartNewRun();

            Assert.IsTrue(boardModel.Board.IsEmpty());

            Object.DestroyImmediate(catalog);
        }

        /// <summary>A new run must inherit nothing: a reinforced cell left over from the last one is
        /// wiped by the board reset, and only the level's own cells are put back.</summary>
        [Test]
        public void StartNewRun_Twice_DoesNotStackReinforcedCells()
        {
            var catalog = ACatalogOf(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_reinforcedCells\":["
                + "{\"_x\":1,\"_y\":1,\"_hitCount\":2}]}");
            var progressionModel = new LevelProgressionModel();
            progressionModel.CurrentLevelNumber.Value = 1;

            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out _, out _,
                new LevelReinforcedCellSeeder(
                    catalog, progressionModel, new PathRunModel(), new WeightedPieceDraw(seed: 1)));

            system.StartNewRun();
            boardModel.Board.TryDamage(new GridPosition(1, 1));
            Assert.AreEqual(1, boardModel.GetHitCount(new GridPosition(1, 1)));

            system.StartNewRun();

            Assert.AreEqual(
                2, boardModel.GetHitCount(new GridPosition(1, 1)),
                "The fresh run re-seeded the cell at its authored count.");
            Assert.AreEqual(1, boardModel.Board.OccupiedCellCount());

            Object.DestroyImmediate(catalog);
        }

        // --- Helpers ---

        /// <summary>Fills row <paramref name="y"/> completely, with <paramref name="reinforced"/> as a
        /// reinforced cell and every other cell an ordinary block — a row that is full, and so clears,
        /// with one cell that will resist.</summary>
        private static void FillRowWithReinforcedCellAt(
            Board board, int y, GridPosition reinforced, int hitCount)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, y);
                if (position.Equals(reinforced))
                {
                    board.OccupyReinforced(position, COLOUR, hitCount);
                    continue;
                }

                board.Occupy(position, COLOUR);
            }
        }

        private static bool Contains(IReadOnlyList<GridPosition> cells, GridPosition position)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].Equals(position))
                {
                    return true;
                }
            }

            return false;
        }

        private static BoardSystem CreateSystem(
            out BoardModel boardModel,
            out TrayModel trayModel,
            out TestMessageBroker<PiecePlacedMessage> placedBroker,
            LevelReinforcedCellSeeder seeder = null)
        {
            boardModel = new BoardModel();
            trayModel = new TrayModel();
            placedBroker = new TestMessageBroker<PiecePlacedMessage>();

            // Deliberately unstarted (unless a test starts it), as the laser, vortex and coin fixtures
            // are: StartNewRun would draw over the board and dock a test lays out by hand.
            return new BoardSystem(
                boardModel,
                trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                placedBroker,
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: 1,
                reinforcedCellSeeder: seeder);
        }

        /// <summary>Authors one catalog row through <see cref="JsonUtility"/>, exactly as
        /// <c>LevelObjectiveConfigCoinCellTests</c> does: the serialized field names are the asset's own
        /// contract, so a row written this way is what the Inspector would have produced.</summary>
        private static LevelObjectiveConfig ARow(string json)
        {
            return JsonUtility.FromJson<LevelObjectiveConfig>(json);
        }

        private static LevelCatalog ACatalogOf(params string[] levels)
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            JsonUtility.FromJsonOverwrite($"{{\"_levels\":[{string.Join(",", levels)}]}}", catalog);
            return catalog;
        }
    }
}
