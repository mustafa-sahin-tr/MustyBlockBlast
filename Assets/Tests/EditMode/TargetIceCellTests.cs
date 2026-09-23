using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Cover for the ice socket / "Buzlu Hedef Hücre" (issue #433): a level-authored, per-POSITION ice
    /// level that starts a run empty and playable, melts by one each time the block the player put on
    /// it is destroyed, and is an ordinary cell once it reaches 0.
    /// <para>
    /// The file is deliberately ordered by what it is protecting. First the <see cref="Board"/>
    /// primitives, including the single regression guard that matters most — <see cref="Board.Clear"/>
    /// does NOT reset the ice level, the one per-cell value that belongs to the position rather than to
    /// the block (every other array is reset by it, and a Clear that reset this one would melt a whole
    /// socket on its first clear). Then the melt seam itself (<see cref="Board.TryDamage"/>), then
    /// Clone/CopyFrom (what Undo is built on), then each acceptance criterion in turn through the Core
    /// resolvers and the real <see cref="BoardSystem"/>, including the explicit cascade-safety negative
    /// case the issue calls out: a socket's block destroyed by a special cell's own wipe mid-cascade —
    /// not by a completed line — melts and is counted just the same.
    /// </para>
    /// <para>
    /// <b>AC8 (undo restores an ice level)</b> is covered only at the <see cref="Board.Clone"/>/
    /// <see cref="Board.CopyFrom"/> level, exactly as <c>ReinforcedCellTests</c> and
    /// <c>TimerCellTests</c> cover their own: one-step undo does not exist anywhere in this project yet,
    /// so there is nothing further to integrate with, and an undo built on those two methods will
    /// restore ice levels with no change here.
    /// </para>
    /// <para>
    /// The View's overlay (a flat frost tint whose alpha scales with the level) is not covered: it is
    /// a MonoBehaviour layer with no logic beyond one <c>Clamp01</c>, and the model event that drives it
    /// (<see cref="BoardModel.TargetIceLevelChanged"/>) IS covered below.
    /// </para>
    /// </summary>
    public class TargetIceCellTests
    {
        private const int COLOUR = 3;

        /// <summary>The serialized form of <see cref="ObjectiveType.IceCellsCleared"/>: a row is authored
        /// as JSON, and JsonUtility writes an enum as its underlying int.</summary>
        private const int ICE_CELLS_CLEARED = (int)ObjectiveType.IceCellsCleared;

        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        // --- Board primitives ---

        [Test]
        public void SetIceLevel_ThenGetIceLevel_RoundTrips()
        {
            var board = new Board();
            var position = new GridPosition(2, 3);

            board.SetIceLevel(position, 3);

            Assert.AreEqual(3, board.GetIceLevel(position));
            Assert.AreEqual(0, board.GetIceLevel(new GridPosition(3, 2)), "Nothing else is icy.");
        }

        /// <summary>AC2: marking a position with ice does NOT occupy it — unlike a reinforced or timer
        /// seed, a socket brings no block of its own.</summary>
        [Test]
        public void SetIceLevel_LeavesTheCellEmpty()
        {
            var board = new Board();
            var position = new GridPosition(2, 3);

            board.SetIceLevel(position, 2);

            Assert.IsFalse(board.IsOccupied(position));
            Assert.AreEqual(Board.EMPTY, board[position]);
            Assert.IsTrue(board.IsEmpty(), "The board as a whole is still empty.");
        }

        [Test]
        public void SetIceLevel_OnAHole_Throws()
        {
            var shape = new BoardShape(Board.SIZE, Board.SIZE, new[] { new GridPosition(1, 1) });
            var board = new Board(shape);

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => board.SetIceLevel(new GridPosition(1, 1), 1));
        }

        [Test]
        public void SetIceLevel_Negative_Throws()
        {
            var board = new Board();

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => board.SetIceLevel(new GridPosition(0, 0), -1));
        }

        /// <summary>The headline regression guard: Clear resets every per-BLOCK array but must leave the
        /// per-POSITION ice level alone. Without this, a socket melts entirely on its first clear.</summary>
        [Test]
        public void Clear_DoesNotResetTheIceLevel()
        {
            var board = new Board();
            var position = new GridPosition(4, 4);
            board.SetIceLevel(position, 3);
            board.Occupy(position, COLOUR);
            board.SetSpecialKind(position, SpecialCellKind.ScoreGem);

            board.Clear(position);

            Assert.IsFalse(board.IsOccupied(position));
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(position), "The kind went with the block.");
            Assert.AreEqual(3, board.GetIceLevel(position), "The ice belongs to the position and stays.");
        }

        // --- Board.TryDamage: the one seam every destruction path melts through ---

        /// <summary>AC4 at the primitive: destroying the block on a socket melts exactly one level and
        /// leaves the position empty.</summary>
        [Test]
        public void TryDamage_OnAnOccupiedIceSocket_MeltsOneLevelAndEmptiesTheCell()
        {
            var board = new Board();
            var position = new GridPosition(4, 4);
            board.SetIceLevel(position, 3);
            board.Occupy(position, COLOUR);

            bool removed = board.TryDamage(position);

            Assert.IsTrue(removed);
            Assert.IsFalse(board.IsOccupied(position));
            Assert.AreEqual(2, board.GetIceLevel(position));
        }

        /// <summary>A hit that is merely spent melts nothing: the block is still standing, so the socket
        /// has not been cleared.</summary>
        [Test]
        public void TryDamage_OnAReinforcedBlockWithHitsToSpare_MeltsNothing()
        {
            var board = new Board();
            var position = new GridPosition(4, 4);
            board.SetIceLevel(position, 2);
            board.OccupyReinforced(position, COLOUR, 3, 0);

            Assert.IsFalse(board.TryDamage(position));

            Assert.IsTrue(board.IsOccupied(position));
            Assert.AreEqual(2, board.GetIceLevel(position), "Still standing, still two levels of ice.");
        }

        /// <summary>The regression guard for every board that never authored ice: an ordinary cell
        /// reads 0 before and 0 after, and TryDamage behaves exactly as it always did.</summary>
        [Test]
        public void TryDamage_OnAnOrdinaryCell_LeavesTheIceLevelAtZero()
        {
            var board = new Board();
            var position = new GridPosition(1, 1);
            board.Occupy(position, COLOUR);

            Assert.IsTrue(board.TryDamage(position));
            Assert.AreEqual(0, board.GetIceLevel(position));
            Assert.AreEqual(0, board.CountIceCells());
        }

        [Test]
        public void CountIceCells_CountsOnlyPositionsStillAboveZero()
        {
            var board = new Board();
            board.SetIceLevel(new GridPosition(0, 0), 1);
            board.SetIceLevel(new GridPosition(1, 0), 3);
            board.SetIceLevel(new GridPosition(2, 0), 0);

            Assert.AreEqual(2, board.CountIceCells());
        }

        [Test]
        public void ClearAllIceLevels_DropsEveryPosition()
        {
            var board = new Board();
            board.SetIceLevel(new GridPosition(0, 0), 1);
            board.SetIceLevel(new GridPosition(7, 7), 3);

            board.ClearAllIceLevels();

            Assert.AreEqual(0, board.CountIceCells());
            Assert.AreEqual(0, board.GetIceLevel(new GridPosition(7, 7)));
        }

        // --- Clone / CopyFrom: what Undo is built on (AC8) ---

        [Test]
        public void Clone_CopiesIceLevels_AndTheCopyIsIndependent()
        {
            var board = new Board();
            var position = new GridPosition(5, 2);
            board.SetIceLevel(position, 3);

            Board copy = board.Clone();
            Assert.AreEqual(3, copy.GetIceLevel(position));

            copy.SetIceLevel(position, 1);
            Assert.AreEqual(3, board.GetIceLevel(position), "Mutating the clone leaves the original alone.");
        }

        [Test]
        public void CopyFrom_CopiesIceLevels()
        {
            var source = new Board();
            var position = new GridPosition(6, 6);
            source.SetIceLevel(position, 2);

            var destination = new Board();
            destination.CopyFrom(source);

            Assert.AreEqual(2, destination.GetIceLevel(position));
        }

        /// <summary>The Undo-relevant half: restoring a snapshot taken before a melt puts the level back,
        /// and restoring one with no ice at all clears a stale level rather than leaving it behind.</summary>
        [Test]
        public void CopyFrom_ABoardWithNoIce_ClearsStaleIceLevels()
        {
            var source = new Board();
            var destination = new Board();
            destination.SetIceLevel(new GridPosition(3, 3), 3);

            destination.CopyFrom(source);

            Assert.AreEqual(0, destination.GetIceLevel(new GridPosition(3, 3)));
            Assert.AreEqual(0, destination.CountIceCells());
        }

        // --- AC3 (negative): placement onto a socket is the ordinary placement, to the letter ---

        [Test]
        public void CanPlace_OntoAnIceSocket_IsExactlyTheOrdinaryEmptyCellAnswer()
        {
            var icy = new Board();
            var plain = new Board();
            var position = new GridPosition(3, 3);
            icy.SetIceLevel(position, 3);

            Assert.IsTrue(PlacementRules.CanPlace(icy, Single, position));
            Assert.AreEqual(
                PlacementRules.CanPlace(plain, Single, position),
                PlacementRules.CanPlace(icy, Single, position));

            // And once something stands on it, it is refused exactly as any occupied cell is.
            icy.Occupy(position, COLOUR);
            plain.Occupy(position, COLOUR);
            Assert.IsFalse(PlacementRules.CanPlace(icy, Single, position));
            Assert.AreEqual(
                PlacementRules.CanPlace(plain, Single, position),
                PlacementRules.CanPlace(icy, Single, position));
        }

        /// <summary>An icy EMPTY cell is an empty cell to the line-clear rule: a row is not "full" because
        /// one of its cells is frosted.</summary>
        [Test]
        public void IsRowFull_IgnoresIce()
        {
            var board = new Board();
            for (int x = 1; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 0), COLOUR);
            }

            board.SetIceLevel(new GridPosition(0, 0), 2);

            Assert.IsFalse(board.IsRowFull(0), "The icy cell is empty, so the row is one short.");
        }

        // --- AC4 through the Core resolver: a completed line melts one level and empties the socket ---

        [Test]
        public void ResolveClears_ThroughAnIceSocket_MeltsOneLevelAndLeavesItEmpty()
        {
            var board = new Board();
            var socket = new GridPosition(2, 5);
            board.SetIceLevel(socket, 3);
            FillRow(board, 5);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(1, result.LineCount);
            Assert.AreEqual(Board.SIZE, result.ClearedCellCount, "The socket's block is an ordinary cleared cell.");
            Assert.IsFalse(board.IsOccupied(socket));
            Assert.AreEqual(2, board.GetIceLevel(socket));
        }

        /// <summary>A row-and-column intersection is one destruction, so it melts one level, not two —
        /// the same "counted once" rule reinforced cells spend one hit on.</summary>
        [Test]
        public void ResolveClears_WithARowAndAColumnThroughOneSocket_MeltsExactlyOneLevel()
        {
            var board = new Board();
            var socket = new GridPosition(4, 4);
            board.SetIceLevel(socket, 3);
            FillRow(board, 4);
            for (int y = 0; y < Board.SIZE; y++)
            {
                var cell = new GridPosition(4, y);
                if (!board.IsOccupied(cell))
                {
                    board.Occupy(cell, COLOUR);
                }
            }

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(2, result.LineCount);
            Assert.AreEqual(2, board.GetIceLevel(socket));
        }

        // --- AC5: every other destruction path melts the same way ---

        [Test]
        public void ResolveBombClear_OverAnOccupiedSocket_MeltsOneLevel()
        {
            var board = new Board();
            var socket = new GridPosition(3, 3);
            board.SetIceLevel(socket, 2);
            board.Occupy(socket, COLOUR);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, socket);

            Assert.AreEqual(1, result.ClearedCellCount);
            Assert.IsFalse(board.IsOccupied(socket));
            Assert.AreEqual(1, board.GetIceLevel(socket));
        }

        /// <summary>An EMPTY socket inside a bomb's area is not "destroyed" — there was no block to
        /// destroy — so it melts nothing. Ice melts with the block on it, never on its own.</summary>
        [Test]
        public void ResolveBombClear_OverAnEmptySocket_MeltsNothing()
        {
            var board = new Board();
            var socket = new GridPosition(3, 3);
            board.SetIceLevel(socket, 2);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, socket);

            Assert.AreEqual(0, result.ClearedCellCount);
            Assert.AreEqual(2, board.GetIceLevel(socket));
        }

        [Test]
        public void ResolveFill_CompletingARowThroughASocket_MeltsOneLevel()
        {
            var board = new Board();
            var socket = new GridPosition(6, 2);
            board.SetIceLevel(socket, 1);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                board.Occupy(new GridPosition(x, 2), COLOUR);
            }

            // The joker fills the socket itself, completing the row; the fill's own cell is then cleared.
            JokerFillResult result = JokerFillResolver.ResolveFill(board, new GridPosition(7, 2), COLOUR);

            Assert.IsTrue(result.Filled);
            Assert.AreEqual(1, result.LineCount);
            Assert.AreEqual(0, board.GetIceLevel(socket));
        }

        [Test]
        public void LaserEffect_WipingThroughAnOccupiedSocket_MeltsOneLevel()
        {
            var board = new Board();
            var socket = new GridPosition(0, 6);
            board.SetIceLevel(socket, 3);
            board.Occupy(socket, COLOUR);

            var laser = new LaserEffect();
            laser.BeginResolution();

            // A laser destroyed by a row clear wipes its column — which holds the socket's block.
            laser.Apply(board, new SpecialCellTrigger(new GridPosition(0, 2), SpecialCellKind.Laser, ClearAxis.Row));

            Assert.IsFalse(board.IsOccupied(socket));
            Assert.AreEqual(2, board.GetIceLevel(socket));
        }

        // --- AC6: at 0 the position is an ordinary cell ---

        [Test]
        public void TryDamage_OnTheLastLevel_LeavesAnOrdinaryEmptyCell()
        {
            var board = new Board();
            var socket = new GridPosition(2, 2);
            board.SetIceLevel(socket, 1);
            board.Occupy(socket, COLOUR);

            board.TryDamage(socket);

            Assert.AreEqual(0, board.GetIceLevel(socket));
            Assert.AreEqual(0, board.CountIceCells());
            Assert.IsFalse(board.IsOccupied(socket));
            Assert.IsTrue(DiamondCellRules.CanCarryDiamond(board, socket), "Indistinguishable from a never-icy cell.");

            // And the next cycle through it is an ordinary one: destroy it again, still 0, never negative.
            board.Occupy(socket, COLOUR);
            board.TryDamage(socket);
            Assert.AreEqual(0, board.GetIceLevel(socket));
        }

        // --- Diamond exclusion (product decision) ---

        [Test]
        public void CanCarryDiamond_IsFalseOnAPositionWithIce_AndTrueOnceMelted()
        {
            var board = new Board();
            var position = new GridPosition(1, 1);

            Assert.IsTrue(DiamondCellRules.CanCarryDiamond(board, position));

            board.SetIceLevel(position, 1);
            Assert.IsFalse(DiamondCellRules.CanCarryDiamond(board, position));

            board.SetIceLevel(position, 0);
            Assert.IsTrue(DiamondCellRules.CanCarryDiamond(board, position));
        }

        // --- Through the real System: placement, cascade (AC5 negative case), the message, the event ---

        /// <summary>AC4 end to end: place onto the socket, complete the row through it; the socket is
        /// empty again at level 2, and the message reports no socket fully melted yet.</summary>
        [Test]
        public void TryPlacePiece_ClearingThroughASocket_MeltsOneLevelAndReportsNoneFullyMelted()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PiecePlacedMessage> placedBroker);
            trayModel.SetSlot(0, Single, COLOUR);

            var socket = new GridPosition(Board.SIZE - 1, 3);
            boardModel.SetIceLevel(socket, 3);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                boardModel.Occupy(new GridPosition(x, 3), COLOUR);
            }

            Assert.IsTrue(system.TryPlacePiece(0, socket), "Placing onto the socket is an ordinary placement.");

            Assert.AreEqual(1, placedBroker.Published.Count);
            Assert.AreEqual(1, placedBroker.Published[0].LinesCleared);
            Assert.AreEqual(0, placedBroker.Published[0].IceCellsMeltedCount, "Level 2 is not 0.");
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(socket));
            Assert.AreEqual(2, boardModel.GetIceLevel(socket));
        }

        /// <summary>AC7's positive half, end to end: the third fill-and-clear cycle takes the last level
        /// off, and THAT placement is the one that reports a fully melted socket.</summary>
        [Test]
        public void TryPlacePiece_OnTheThirdCycle_ReportsTheSocketFullyMelted()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PiecePlacedMessage> placedBroker);

            var socket = new GridPosition(Board.SIZE - 1, 3);
            boardModel.SetIceLevel(socket, 3);

            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int x = 0; x < Board.SIZE - 1; x++)
                {
                    boardModel.Occupy(new GridPosition(x, 3), COLOUR);
                }

                trayModel.SetSlot(0, Single, COLOUR);
                Assert.IsTrue(system.TryPlacePiece(0, socket));
            }

            Assert.AreEqual(3, placedBroker.Published.Count);
            Assert.AreEqual(0, placedBroker.Published[0].IceCellsMeltedCount);
            Assert.AreEqual(0, placedBroker.Published[1].IceCellsMeltedCount);
            Assert.AreEqual(1, placedBroker.Published[2].IceCellsMeltedCount);
            Assert.AreEqual(0, boardModel.GetIceLevel(socket));
        }

        /// <summary>
        /// AC5, the explicit cascade-safety negative case: the socket's block is destroyed NOT by the
        /// line the player completed but by an explosive core's wipe mid-cascade. The core sits in the
        /// completed row and wipes its column, which holds the socket's block two rows away. The melt
        /// must happen AND the placement message must credit it — the Reinforced Cell cascade-reporting
        /// gap (#249) is what this guards against.
        /// </summary>
        [Test]
        public void TryPlacePiece_WhereACascadedWipeDestroysTheSocketsBlock_MeltsAndReportsIt()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PiecePlacedMessage> placedBroker);
            trayModel.SetSlot(0, Single, COLOUR);

            // Row 5 is full but for the gap the piece will fill; the core at (0,5) is destroyed by that
            // row clear and wipes column 0.
            var gap = new GridPosition(3, 5);
            for (int x = 0; x < Board.SIZE; x++)
            {
                var cell = new GridPosition(x, 5);
                if (!cell.Equals(gap))
                {
                    boardModel.Occupy(cell, COLOUR);
                }
            }

            boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ExplosiveCore);

            // The socket, on its last level, with a bystander block on it — nowhere near the row.
            var socket = new GridPosition(0, 1);
            boardModel.SetIceLevel(socket, 1);
            boardModel.Occupy(socket, COLOUR);

            Assert.IsTrue(system.TryPlacePiece(0, gap));

            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(socket), "The wipe took the block.");
            Assert.AreEqual(0, boardModel.GetIceLevel(socket), "And the ice melted with it.");
            Assert.AreEqual(1, placedBroker.Published[0].LinesCleared, "Only the row the player completed.");
            Assert.AreEqual(
                1, placedBroker.Published[0].IceCellsMeltedCount,
                "A socket melted by a cascaded wipe is credited exactly as one melted by the line itself.");
        }

        /// <summary>AC7's negative half: a placement that destroys the block on a socket without taking
        /// its last level reports nothing — and neither does one that never touched a socket.</summary>
        [Test]
        public void TryPlacePiece_WithNoClear_LeavesEveryIceLevelUntouched()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel, out _);
            trayModel.SetSlot(0, Single, COLOUR);

            var socket = new GridPosition(6, 6);
            boardModel.SetIceLevel(socket, 2);

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 0)));

            Assert.AreEqual(2, boardModel.GetIceLevel(socket));
        }

        /// <summary>The View's seam: a placement that thins a socket announces the new level through
        /// <see cref="BoardModel.TargetIceLevelChanged"/>, even though the cell itself just went empty
        /// (which is exactly why the reinforced-cell event's "the cell emptied, so the look goes" reading
        /// does not apply here).</summary>
        [Test]
        public void TryPlacePiece_ClearingThroughASocket_AnnouncesTheNewLevel()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel, out _);
            trayModel.SetSlot(0, Single, COLOUR);

            var socket = new GridPosition(Board.SIZE - 1, 3);
            boardModel.SetIceLevel(socket, 3);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                boardModel.Occupy(new GridPosition(x, 3), COLOUR);
            }

            var announced = new List<(GridPosition Position, int Level)>();
            boardModel.TargetIceLevelChanged += (position, level) => announced.Add((position, level));

            Assert.IsTrue(system.TryPlacePiece(0, socket));

            Assert.IsTrue(
                announced.Contains((socket, 2)),
                "The socket's thinner ice was announced after the clear.");
        }

        // --- The objective ---

        [Test]
        public void ApplyPlacement_ForIceCellsCleared_AdvancesByTheMeltedCount()
        {
            var progress = new ObjectiveProgress(
                new ObjectiveDefinition("ice", ObjectiveType.IceCellsCleared, ObjectiveScope.PerRun, 3));

            Assert.IsFalse(progress.ApplyPlacement(APlacementMelting(0)), "Nothing melted, nothing moved.");
            Assert.IsTrue(progress.ApplyPlacement(APlacementMelting(2)));
            Assert.AreEqual(2, progress.CurrentValue);
            Assert.IsFalse(progress.IsComplete);

            Assert.IsTrue(progress.ApplyPlacement(APlacementMelting(1)));
            Assert.IsTrue(progress.IsComplete);
        }

        [Test]
        public void ApplyPowerUpIceCellsMelted_AdvancesOnlyTheIceObjective()
        {
            var ice = new ObjectiveProgress(
                new ObjectiveDefinition("ice", ObjectiveType.IceCellsCleared, ObjectiveScope.PerRun, 2));
            var other = new ObjectiveProgress(
                new ObjectiveDefinition("other", ObjectiveType.ReinforcedCellsCleared, ObjectiveScope.PerRun, 2));

            Assert.IsTrue(ice.ApplyPowerUpIceCellsMelted(2));
            Assert.IsTrue(ice.IsComplete);

            Assert.IsFalse(other.ApplyPowerUpIceCellsMelted(2));
            Assert.AreEqual(0, other.CurrentValue);
            Assert.IsFalse(ice.ApplyPowerUpIceCellsMelted(0), "A zero count is a no-op.");
        }

        /// <summary>The melted count reaches no other objective: a reinforced-cell objective does not
        /// move because an ice socket melted.</summary>
        [Test]
        public void ApplyPlacement_ForAnyOtherType_IgnoresTheMeltedCount()
        {
            var progress = new ObjectiveProgress(
                new ObjectiveDefinition("other", ObjectiveType.ReinforcedCellsCleared, ObjectiveScope.PerRun, 2));

            Assert.IsFalse(progress.ApplyPlacement(APlacementMelting(2)));
            Assert.AreEqual(0, progress.CurrentValue);
        }

        // --- Level authoring ---

        [Test]
        public void TargetIceCells_OnARowAuthoredBeforeTheFieldExisted_IsEmpty()
        {
            LevelObjectiveConfig config = ARow("{\"_levelNumber\":1,\"_targetValue\":1}");

            Assert.AreEqual(0, config.TargetIceCells.Count);
        }

        [Test]
        public void TargetIceCells_OnARowAuthoringTwo_ReadsBothPositionsAndLevels()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_targetIceCells\":["
                + "{\"_x\":1,\"_y\":2,\"_iceLevel\":1},{\"_x\":5,\"_y\":6,\"_iceLevel\":3}]}");

            Assert.AreEqual(2, config.TargetIceCells.Count);
            Assert.AreEqual(1, config.TargetIceCells[0].IceLevel);
            Assert.AreEqual(3, config.TargetIceCells[1].IceLevel);
        }

        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(3, 3)]
        [TestCase(9, 3)]
        [TestCase(-3, 1)]
        public void ValidateInEditor_ClampsTheIceLevelIntoRange(int authored, int expected)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_targetIceCells\":["
                + $"{{\"_x\":1,\"_y\":1,\"_iceLevel\":{authored}}}]}}");

            config.ValidateInEditor();

            Assert.AreEqual(expected, config.TargetIceCells[0].IceLevel);
        }

        [TestCase(8, 1)]
        [TestCase(1, 8)]
        [TestCase(-1, 1)]
        public void IsValid_WithASocketOutsideTheBoard_Fails(int x, int y)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_targetIceCells\":["
                + $"{{\"_x\":{x},\"_y\":{y},\"_iceLevel\":1}}]}}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [TestCase(0)]
        [TestCase(4)]
        public void IsValid_WithAnIceLevelOutsideOneToThree_Fails(int iceLevel)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_targetIceCells\":["
                + $"{{\"_x\":1,\"_y\":1,\"_iceLevel\":{iceLevel}}}]}}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void IsValid_WithASocketOnAHole_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_boardHoles\":[{\"_x\":2,\"_y\":2}],"
                + "\"_targetIceCells\":[{\"_x\":2,\"_y\":2,\"_iceLevel\":1}]}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        /// <summary>A socket starts EMPTY, so it cannot share a cell with either pre-filling mechanic.</summary>
        [Test]
        public void IsValid_WithASocketOnAReinforcedOrTimerCell_Fails()
        {
            LevelObjectiveConfig onReinforced = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_reinforcedCells\":[{\"_x\":2,\"_y\":2,\"_hitCount\":2}],"
                + "\"_targetIceCells\":[{\"_x\":2,\"_y\":2,\"_iceLevel\":1}]}");
            LevelObjectiveConfig onTimer = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_timerCells\":[{\"_x\":3,\"_y\":3,\"_startingCountdown\":3}],"
                + "\"_targetIceCells\":[{\"_x\":3,\"_y\":3,\"_iceLevel\":1}]}");

            Assert.IsFalse(onReinforced.IsValid(out string reinforcedError));
            StringAssert.Contains("reinforced", reinforcedError);
            Assert.IsFalse(onTimer.IsValid(out string timerError));
            StringAssert.Contains("timer", timerError);
        }

        [Test]
        public void IsValid_WithTheSameSocketAuthoredTwice_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_targetIceCells\":["
                + "{\"_x\":1,\"_y\":1,\"_iceLevel\":1},{\"_x\":1,\"_y\":1,\"_iceLevel\":2}]}");

            Assert.IsFalse(config.IsValid(out string error));
            StringAssert.Contains("more than once", error);
        }

        [Test]
        public void IsValid_WithAWellFormedSocket_Passes()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_targetIceCells\":["
                + "{\"_x\":2,\"_y\":2,\"_iceLevel\":3}]}");

            Assert.IsTrue(config.IsValid(out string error), error);
        }

        /// <summary>AC7: the target is "all of them", always — whatever <c>_targetValue</c> the Inspector
        /// holds, mirroring the reinforced-cell objective's override.</summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(99)]
        public void ToObjectiveDefinition_ForAnIceCellsClearedObjective_TargetsEveryAuthoredSocket(
            int authoredTarget)
        {
            LevelObjectiveConfig config = ARow(
                $"{{\"_levelNumber\":1,\"_objectiveType\":{ICE_CELLS_CLEARED},"
                + $"\"_targetValue\":{authoredTarget},\"_targetIceCells\":["
                + "{\"_x\":1,\"_y\":1,\"_iceLevel\":1},{\"_x\":2,\"_y\":2,\"_iceLevel\":2},"
                + "{\"_x\":3,\"_y\":3,\"_iceLevel\":3}]}");

            ObjectiveDefinition definition = config.ToObjectiveDefinition();

            Assert.AreEqual(ObjectiveType.IceCellsCleared, definition.Type);
            Assert.AreEqual(3, definition.TargetValue);
        }

        [Test]
        public void ToObjectiveDefinition_ForAnyOtherObjectiveType_StillUsesTheAuthoredTarget()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":5,\"_requiredLineCount\":1,\"_targetIceCells\":["
                + "{\"_x\":1,\"_y\":1,\"_iceLevel\":1}]}");

            Assert.AreEqual(5, config.ToObjectiveDefinition().TargetValue);
        }

        [Test]
        public void IsValid_WithAnIceCellsClearedObjectiveAndNoSockets_Fails()
        {
            LevelObjectiveConfig config = ARow(
                $"{{\"_levelNumber\":1,\"_objectiveType\":{ICE_CELLS_CLEARED},"
                + "\"_targetValue\":1,\"_requiredLineCount\":1}");

            Assert.IsFalse(config.IsValid(out string error));
            StringAssert.Contains("IceCellsCleared", error);
        }

        // --- Level-start seeding (AC1/AC2) ---

        [Test]
        public void StartNewRun_OnALevelAuthoringSockets_MarksThemAndLeavesThemEmpty()
        {
            var catalog = ACatalogOf(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_targetIceCells\":["
                + "{\"_x\":1,\"_y\":1,\"_iceLevel\":1},{\"_x\":6,\"_y\":7,\"_iceLevel\":3}]}");
            var progressionModel = new LevelProgressionModel();
            progressionModel.CurrentLevelNumber.Value = 1;

            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out _, out _,
                new LevelTargetIceCellSeeder(catalog, progressionModel, new PathRunModel()));

            system.StartNewRun();

            Assert.AreEqual(1, boardModel.GetIceLevel(new GridPosition(1, 1)));
            Assert.AreEqual(3, boardModel.GetIceLevel(new GridPosition(6, 7)));
            Assert.IsTrue(boardModel.Board.IsEmpty(), "A socket brings no block: the board is still empty.");
            Assert.AreEqual(2, boardModel.Board.CountIceCells());

            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void StartNewRun_OnALevelAuthoringNone_LeavesNoIce()
        {
            var catalog = ACatalogOf("{\"_levelNumber\":1,\"_targetValue\":1}");
            var progressionModel = new LevelProgressionModel();
            progressionModel.CurrentLevelNumber.Value = 1;

            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out _, out _,
                new LevelTargetIceCellSeeder(catalog, progressionModel, new PathRunModel()));

            system.StartNewRun();

            Assert.AreEqual(0, boardModel.Board.CountIceCells());

            Object.DestroyImmediate(catalog);
        }

        /// <summary>A new run inherits nothing: a socket half-melted last run is reset to its authored
        /// level, and a socket the new level does not author is gone — which is exactly the case
        /// <see cref="Board.Clear"/> cannot cover, since it leaves ice alone by design.</summary>
        [Test]
        public void StartNewRun_Twice_ResetsIceToTheAuthoredLevelsAndDropsEverythingElse()
        {
            var catalog = ACatalogOf(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_targetIceCells\":["
                + "{\"_x\":1,\"_y\":1,\"_iceLevel\":3}]}");
            var progressionModel = new LevelProgressionModel();
            progressionModel.CurrentLevelNumber.Value = 1;

            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out _, out _,
                new LevelTargetIceCellSeeder(catalog, progressionModel, new PathRunModel()));

            system.StartNewRun();
            boardModel.Board.Occupy(new GridPosition(1, 1), COLOUR);
            boardModel.Board.TryDamage(new GridPosition(1, 1));
            Assert.AreEqual(2, boardModel.GetIceLevel(new GridPosition(1, 1)));
            boardModel.SetIceLevel(new GridPosition(5, 5), 2);

            system.StartNewRun();

            Assert.AreEqual(3, boardModel.GetIceLevel(new GridPosition(1, 1)), "Re-seeded at its authored level.");
            Assert.AreEqual(0, boardModel.GetIceLevel(new GridPosition(5, 5)), "Not authored, so gone.");
            Assert.AreEqual(1, boardModel.Board.CountIceCells());

            Object.DestroyImmediate(catalog);
        }

        // --- Helpers ---

        private static void FillRow(Board board, int y)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, y), COLOUR);
            }
        }

        private static ObjectivePlacementContext APlacementMelting(int iceCellsMelted)
        {
            return new ObjectivePlacementContext(
                linesCleared: 1, rowsCleared: 1, columnsCleared: 0, PieceFamily.Single, "test_single",
                currentRunScore: 0, boardEmptyAfterPlacement: false, currentStreak: 0,
                occupiedCellCountBeforeClear: 8, anyCornerCleared: false,
                centerCoreEmptyAfterPlacement: false, hasIsolatedHolesAfterPlacement: false,
                elapsedRunSeconds: 0f, reinforcedCellsFullyCleared: 0, destroyedCellCountByColour: null,
                timerCellsClearedInTime: 0, destroyedDiamondCountByColour: null,
                iceCellsMelted: iceCellsMelted);
        }

        private static BoardSystem CreateSystem(
            out BoardModel boardModel,
            out TrayModel trayModel,
            out TestMessageBroker<PiecePlacedMessage> placedBroker,
            LevelTargetIceCellSeeder seeder = null)
        {
            boardModel = new BoardModel();
            trayModel = new TrayModel();
            placedBroker = new TestMessageBroker<PiecePlacedMessage>();

            // Deliberately unstarted (unless a test starts it), as the reinforced and timer fixtures
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
                targetIceCellSeeder: seeder);
        }

        /// <summary>Authors one catalog row through <see cref="JsonUtility"/>, exactly as
        /// <c>ReinforcedCellTests</c> does: the serialized field names are the asset's own contract.</summary>
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
