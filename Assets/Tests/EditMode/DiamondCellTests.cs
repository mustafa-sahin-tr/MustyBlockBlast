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
    /// Cover for the diamond cell's Core slice (issue #393): a collectible <see cref="SpecialCellKind"/>
    /// with a colour of its own, a counting-only effect, and a colour-scoped
    /// <see cref="ObjectiveType.DiamondsCleared"/> objective fed from every destruction path.
    /// <para>
    /// Shaped after <see cref="TimerCellTests"/>: first the <see cref="Board"/> primitives and the
    /// snapshot copy Undo will restore through, then each destruction path (line clear, a special cell's
    /// own blast/wipe/strike, a spent power-up), then the negatives (no bonus, no credit for anything
    /// that is not a diamond of the wanted colour), then the real System end to end, then the objective
    /// and authoring plumbing.
    /// </para>
    /// <para>
    /// <b>Undo is covered the same way <see cref="TimerCellTests"/> covers it</b> — through
    /// <see cref="Board.Clone"/>/<see cref="Board.CopyFrom"/> directly, because one-step undo does not
    /// exist anywhere in this project yet; those two methods are what an undo built on a full-board
    /// snapshot will restore through, and dropping the diamond there is the risk AC5 names.
    /// </para>
    /// </summary>
    public class DiamondCellTests
    {
        private const int BLOCK_COLOUR = 3;
        private const int DIAMOND_COLOUR = 2;
        private const int OTHER_DIAMOND_COLOUR = 5;

        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        // --- Board primitives ---

        [Test]
        public void OccupyDiamond_SetsBlockColourKindAndDiamondColour_Independently()
        {
            var board = new Board();
            var position = new GridPosition(6, 2);

            board.OccupyDiamond(position, BLOCK_COLOUR, DIAMOND_COLOUR);

            Assert.AreEqual(BLOCK_COLOUR, board[position], "The block keeps its own colour.");
            Assert.AreEqual(SpecialCellKind.Diamond, board.GetSpecialKind(position));
            Assert.AreEqual(DIAMOND_COLOUR, board.GetDiamondColourId(position), "The gem has its own colour.");
        }

        [TestCase(0)]
        [TestCase(Board.COLOUR_COUNT + 1)]
        public void OccupyDiamond_WithAColourOutsideThePalette_IsRefused(int diamondColourId)
        {
            var board = new Board();

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => board.OccupyDiamond(new GridPosition(1, 1), BLOCK_COLOUR, diamondColourId));
        }

        [Test]
        public void GetDiamondColourId_OnAnOrdinaryCell_IsZero()
        {
            var board = new Board();
            var position = new GridPosition(1, 1);
            board.Occupy(position, BLOCK_COLOUR);

            Assert.AreEqual(0, board.GetDiamondColourId(position));
        }

        [Test]
        public void Clear_OnADiamondCell_WipesItOutrightColourIncluded()
        {
            var board = new Board();
            var position = new GridPosition(3, 3);
            board.OccupyDiamond(position, BLOCK_COLOUR, DIAMOND_COLOUR);

            board.Clear(position);

            Assert.IsFalse(board.IsOccupied(position));
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(position));
            Assert.AreEqual(0, board.GetDiamondColourId(position));
        }

        [Test]
        public void Clone_CopiesDiamondKindAndColour_AsACopyNotAView()
        {
            var board = new Board();
            var position = new GridPosition(5, 1);
            board.OccupyDiamond(position, BLOCK_COLOUR, DIAMOND_COLOUR);

            Board copy = board.Clone();

            Assert.AreEqual(SpecialCellKind.Diamond, copy.GetSpecialKind(position));
            Assert.AreEqual(DIAMOND_COLOUR, copy.GetDiamondColourId(position));

            copy.SetDiamondColourId(position, OTHER_DIAMOND_COLOUR);
            Assert.AreEqual(DIAMOND_COLOUR, board.GetDiamondColourId(position));
            Assert.AreEqual(OTHER_DIAMOND_COLOUR, copy.GetDiamondColourId(position));
        }

        [Test]
        public void CopyFrom_CopiesDiamondKindAndColour()
        {
            var source = new Board();
            var position = new GridPosition(5, 1);
            source.OccupyDiamond(position, BLOCK_COLOUR, DIAMOND_COLOUR);

            var destination = new Board();
            destination.Occupy(position, BLOCK_COLOUR);
            destination.CopyFrom(source);

            Assert.AreEqual(SpecialCellKind.Diamond, destination.GetSpecialKind(position));
            Assert.AreEqual(DIAMOND_COLOUR, destination.GetDiamondColourId(position));
        }

        [Test]
        public void CopyFrom_ABoardWithNoDiamonds_ClearsStaleDiamondColours()
        {
            var source = new Board();
            var position = new GridPosition(5, 1);
            source.Occupy(position, BLOCK_COLOUR);

            var destination = new Board();
            destination.OccupyDiamond(position, BLOCK_COLOUR, DIAMOND_COLOUR);
            destination.CopyFrom(source);

            Assert.AreEqual(SpecialCellKind.None, destination.GetSpecialKind(position));
            Assert.AreEqual(0, destination.GetDiamondColourId(position));
        }

        /// <summary>AC5, the "Undo" proxy: a diamond placed but not cleared, then a placement elsewhere
        /// that is undone by a full-snapshot restore — the diamond is still there, still a diamond, still
        /// its own colour. A restore that copied the kinds but not the colours would leave a colourless
        /// diamond no objective could ever credit.</summary>
        [Test]
        public void CopyFrom_AfterAPlacementElsewhere_RestoresThePlacedButUnclearedDiamond()
        {
            var board = new Board();
            var diamond = new GridPosition(2, 2);
            board.OccupyDiamond(diamond, BLOCK_COLOUR, DIAMOND_COLOUR);

            Board snapshot = board.Clone();

            // The "next move": another block lands, and the diamond's own cell is tampered with the way
            // a buggy restore might leave it, so the restore has something real to undo.
            board.Occupy(new GridPosition(7, 7), BLOCK_COLOUR);
            board.Clear(diamond);
            Assert.AreEqual(0, board.GetDiamondColourId(diamond), "Sanity: the diamond really went.");

            board.CopyFrom(snapshot);

            Assert.IsFalse(board.IsOccupied(new GridPosition(7, 7)), "The move itself was undone.");
            Assert.IsTrue(board.IsOccupied(diamond));
            Assert.AreEqual(BLOCK_COLOUR, board[diamond]);
            Assert.AreEqual(SpecialCellKind.Diamond, board.GetSpecialKind(diamond));
            Assert.AreEqual(DIAMOND_COLOUR, board.GetDiamondColourId(diamond));
        }

        // --- Destruction paths: line clear ---

        [Test]
        public void ResolveCascade_ThroughADiamond_CountsItUnderItsOwnColour()
        {
            var board = new Board();
            var diamond = new GridPosition(2, 3);
            FillRowWithDiamondAt(board, 3, diamond, DIAMOND_COLOUR);

            var effect = new DiamondClearEffect();
            effect.BeginResolution();
            CascadeClearResolver.ResolveCascade(board, effect);

            Assert.AreEqual(1, effect.DestroyedCountByColour[DIAMOND_COLOUR]);
            Assert.AreEqual(0, effect.DestroyedCountByColour[BLOCK_COLOUR], "Keyed on the gem, not the block.");
            Assert.IsFalse(board.IsOccupied(diamond));
        }

        [Test]
        public void ResolveCascade_ThroughADiamond_LeavesTheOrdinaryColourTallyOnTheBlockColour()
        {
            var board = new Board();
            FillRowWithDiamondAt(board, 3, new GridPosition(2, 3), DIAMOND_COLOUR);

            CascadeClearResult cascade = CascadeClearResolver.ResolveCascade(board, new DiamondClearEffect());

            // The block under the diamond is BLOCK_COLOUR like the rest of the row, and ColourCleared's
            // tally must keep seeing it that way — the diamond's colour never leaks into it.
            Assert.AreEqual(Board.SIZE, cascade.TotalDestroyedCellCountByColour[BLOCK_COLOUR]);
            Assert.AreEqual(0, cascade.TotalDestroyedCellCountByColour[DIAMOND_COLOUR]);
        }

        [Test]
        public void ResolveCascade_ThroughTwoDiamondsOfDifferentColours_CountsEachUnderItsOwn()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 4), BLOCK_COLOUR);
            }

            board.SetSpecialKind(new GridPosition(1, 4), SpecialCellKind.Diamond);
            board.SetDiamondColourId(new GridPosition(1, 4), DIAMOND_COLOUR);
            board.SetSpecialKind(new GridPosition(6, 4), SpecialCellKind.Diamond);
            board.SetDiamondColourId(new GridPosition(6, 4), OTHER_DIAMOND_COLOUR);

            var effect = new DiamondClearEffect();
            effect.BeginResolution();
            CascadeClearResolver.ResolveCascade(board, effect);

            Assert.AreEqual(1, effect.DestroyedCountByColour[DIAMOND_COLOUR]);
            Assert.AreEqual(1, effect.DestroyedCountByColour[OTHER_DIAMOND_COLOUR]);
        }

        [Test]
        public void DiamondClearEffect_BeginResolution_ForgetsThePreviousCount()
        {
            var board = new Board();
            FillRowWithDiamondAt(board, 3, new GridPosition(2, 3), DIAMOND_COLOUR);
            var effect = new DiamondClearEffect();
            effect.BeginResolution();
            CascadeClearResolver.ResolveCascade(board, effect);
            Assert.AreEqual(1, effect.DestroyedCountByColour[DIAMOND_COLOUR], "Sanity.");

            effect.BeginResolution();

            Assert.AreEqual(0, effect.DestroyedCountByColour[DIAMOND_COLOUR]);
        }

        [Test]
        public void DiamondClearEffect_NeverTouchesTheBoard()
        {
            var board = new Board();
            var diamond = new GridPosition(4, 4);
            board.OccupyDiamond(diamond, BLOCK_COLOUR, DIAMOND_COLOUR);
            board.Occupy(new GridPosition(0, 0), BLOCK_COLOUR);
            Board before = board.Clone();

            var effect = new DiamondClearEffect();
            effect.BeginResolution();
            effect.Apply(board, new SpecialCellTrigger(
                new GridPosition(7, 7), SpecialCellKind.Diamond, ClearAxis.Row, 0, DIAMOND_COLOUR));

            Assert.AreEqual(1, effect.DestroyedCountByColour[DIAMOND_COLOUR]);
            Assert.AreEqual(2, board.OccupiedCellCount());
            Assert.AreEqual(before.GetDiamondColourId(diamond), board.GetDiamondColourId(diamond));
        }

        // --- Destruction paths: a special cell's own blast/wipe/strike (the cascade path) ---

        [Test]
        public void ExplosiveCoreEffect_WipingADiamond_CountsItUnderItsOwnColour()
        {
            var board = new Board();
            var diamond = new GridPosition(5, 6);
            board.OccupyDiamond(diamond, BLOCK_COLOUR, DIAMOND_COLOUR);

            var core = new ExplosiveCoreEffect();
            core.BeginResolution();
            core.Apply(
                board,
                new SpecialCellTrigger(new GridPosition(0, 6), SpecialCellKind.ExplosiveCore, ClearAxis.Column));

            Assert.IsFalse(board.IsOccupied(diamond));
            Assert.AreEqual(1, core.DiamondsDestroyedCountByColour[DIAMOND_COLOUR]);
        }

        [Test]
        public void LaserEffect_WipingADiamond_CountsItUnderItsOwnColour()
        {
            var board = new Board();
            var diamond = new GridPosition(5, 6);
            board.OccupyDiamond(diamond, BLOCK_COLOUR, DIAMOND_COLOUR);

            var laser = new LaserEffect();
            laser.BeginResolution();
            laser.Apply(
                board,
                new SpecialCellTrigger(new GridPosition(0, 6), SpecialCellKind.Laser, ClearAxis.Column));

            Assert.IsFalse(board.IsOccupied(diamond));
            Assert.AreEqual(1, laser.DiamondsDestroyedCountByColour[DIAMOND_COLOUR]);
        }

        [Test]
        public void ChainLightningEffect_StrikingTheOnlyOccupiedCell_CountsADiamondUnderItsOwnColour()
        {
            var board = new Board();
            var diamond = new GridPosition(2, 2);
            board.OccupyDiamond(diamond, BLOCK_COLOUR, DIAMOND_COLOUR);

            var chain = new ChainLightningEffect(new System.Random(1));
            chain.BeginResolution();
            chain.Apply(
                board, new SpecialCellTrigger(new GridPosition(7, 7), SpecialCellKind.ChainLightning));

            Assert.IsFalse(board.IsOccupied(diamond));
            Assert.AreEqual(1, chain.DiamondsDestroyedCountByColour[DIAMOND_COLOUR]);
        }

        /// <summary>A reinforced diamond that merely takes a hit from a wipe is not destroyed and must not
        /// be counted — the same gate every other destruction count already respects.</summary>
        [Test]
        public void LaserEffect_WipingAReinforcedDiamondThatSurvives_DoesNotCountIt()
        {
            var board = new Board();
            var diamond = new GridPosition(5, 6);
            board.OccupyReinforced(diamond, BLOCK_COLOUR, hitCount: 2, skin: 0);
            board.SetSpecialKind(diamond, SpecialCellKind.Diamond);
            board.SetDiamondColourId(diamond, DIAMOND_COLOUR);

            var laser = new LaserEffect();
            laser.BeginResolution();
            laser.Apply(
                board,
                new SpecialCellTrigger(new GridPosition(0, 6), SpecialCellKind.Laser, ClearAxis.Column));

            Assert.IsTrue(board.IsOccupied(diamond));
            Assert.AreEqual(0, laser.DiamondsDestroyedCountByColour[DIAMOND_COLOUR]);
        }

        // --- Destruction paths: power-up ---

        [Test]
        public void ResolveBombClear_OverADiamond_ReportsItInTheTriggersUnderItsOwnColour()
        {
            var board = new Board();
            var diamond = new GridPosition(3, 3);
            board.OccupyDiamond(diamond, BLOCK_COLOUR, DIAMOND_COLOUR);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, diamond);

            int[] tally = DiamondClearEffect.CountDestroyedByColour(result.TriggeredSpecials);
            Assert.IsNotNull(tally);
            Assert.AreEqual(1, tally[DIAMOND_COLOUR]);
            Assert.IsFalse(board.IsOccupied(diamond));
        }

        [Test]
        public void ResolveColorCleanser_OverADiamondOfTheTargetBlockColour_ReportsIt()
        {
            var board = new Board();
            var diamond = new GridPosition(3, 3);
            board.OccupyDiamond(diamond, BLOCK_COLOUR, DIAMOND_COLOUR);
            board.Occupy(new GridPosition(0, 0), BLOCK_COLOUR);

            PowerUpClearResult result = PowerUpClearResolver.ResolveColorCleanser(board, new GridPosition(0, 0));

            Assert.AreEqual(1, DiamondClearEffect.CountDestroyedByColour(result.TriggeredSpecials)[DIAMOND_COLOUR]);
        }

        [Test]
        public void CountDestroyedByColour_WithANullList_IsNull()
        {
            Assert.IsNull(DiamondClearEffect.CountDestroyedByColour(null));
        }

        // --- Negatives ---

        [Test]
        public void ResolveCascade_ThroughOrdinaryCellsOnly_CountsNoDiamond()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                // Same colour as the diamonds we care about, so a tally keyed on the block would fail.
                board.Occupy(new GridPosition(x, 3), DIAMOND_COLOUR);
            }

            var effect = new DiamondClearEffect();
            effect.BeginResolution();
            CascadeClearResolver.ResolveCascade(board, effect);

            for (int colourId = 0; colourId < ColourTally.LENGTH; colourId++)
            {
                Assert.AreEqual(0, effect.DestroyedCountByColour[colourId]);
            }
        }

        [Test]
        public void ResolveCascade_ThroughOtherSpecialKinds_CountsNoDiamond()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 3), BLOCK_COLOUR);
            }

            board.SetSpecialKind(new GridPosition(1, 3), SpecialCellKind.ScoreGem);
            board.SetSpecialKind(new GridPosition(2, 3), SpecialCellKind.Timer);
            board.SetTimerCountdown(new GridPosition(2, 3), 3);
            board.SetSpecialKind(new GridPosition(3, 3), SpecialCellKind.Coin);

            var effect = new DiamondClearEffect();
            effect.BeginResolution();
            CascadeClearResolver.ResolveCascade(board, effect);

            for (int colourId = 0; colourId < ColourTally.LENGTH; colourId++)
            {
                Assert.AreEqual(0, effect.DestroyedCountByColour[colourId]);
            }
        }

        /// <summary>AC4: a diamond is a counter, never a multiplier. It must not be counted as a score
        /// gem by either of the two readings the scoring Systems use, and the multiplier those readings
        /// feed must stay at 1x.</summary>
        [Test]
        public void ADestroyedDiamond_IsNotAScoreGem_AndCarriesNoScoreBonus()
        {
            var board = new Board();
            var diamond = new GridPosition(3, 3);
            board.OccupyDiamond(diamond, BLOCK_COLOUR, DIAMOND_COLOUR);

            PowerUpClearResult powerUp = PowerUpClearResolver.ResolveBombClear(board, diamond);
            Assert.AreEqual(0, ScoreGemEffect.CountDestroyed(powerUp.TriggeredSpecials));

            FillRowWithDiamondAt(board, 5, new GridPosition(4, 5), DIAMOND_COLOUR);
            var scoreGemEffect = new ScoreGemEffect();
            scoreGemEffect.BeginResolution();
            CascadeClearResolver.ResolveCascade(board, scoreGemEffect);
            Assert.AreEqual(0, scoreGemEffect.DestroyedCount);

            Assert.AreEqual(100, ScoreRules.ScoreGemMultiplied(100, scoreGemEffect.DestroyedCount));
        }

        // --- The real System, end to end ---

        [Test]
        public void TryPlacePiece_ClearingARowWithADiamond_ReportsItOnThePlacementMessage()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PiecePlacedMessage> placedBroker);
            trayModel.SetSlot(0, Single, BLOCK_COLOUR);

            var diamond = new GridPosition(2, 3);
            boardModel.Board.OccupyDiamond(diamond, BLOCK_COLOUR, DIAMOND_COLOUR);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                var position = new GridPosition(x, 3);
                if (!position.Equals(diamond))
                {
                    boardModel.Occupy(position, BLOCK_COLOUR);
                }
            }

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.AreEqual(1, placedBroker.Published.Count);
            PiecePlacedMessage message = placedBroker.Published[0];
            Assert.IsNotNull(message.DestroyedDiamondCountByColour);
            Assert.AreEqual(1, message.DestroyedDiamondCountByColour[DIAMOND_COLOUR]);
            Assert.AreEqual(0, message.DestroyedScoreGemCount, "AC4: no gem, no bonus.");
        }

        /// <summary>AC3 through the System: a diamond in the cleared row (a clear phase) AND one taken
        /// out by the laser that row destroyed (a mid-cascade wipe, never a clear phase) are both on the
        /// one message — the every-destruction-path sum, not just phase 0.</summary>
        [Test]
        public void TryPlacePiece_ClearingARowThatFiresALaserThroughASecondDiamond_ReportsBoth()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PiecePlacedMessage> placedBroker);
            trayModel.SetSlot(0, Single, BLOCK_COLOUR);

            var laser = new GridPosition(2, 3);
            var rowDiamond = new GridPosition(4, 3);
            var columnDiamond = new GridPosition(2, 6);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                boardModel.Occupy(new GridPosition(x, 3), BLOCK_COLOUR);
            }

            boardModel.Board.SetSpecialKind(laser, SpecialCellKind.Laser);
            boardModel.Board.SetSpecialKind(rowDiamond, SpecialCellKind.Diamond);
            boardModel.Board.SetDiamondColourId(rowDiamond, DIAMOND_COLOUR);
            boardModel.Board.OccupyDiamond(columnDiamond, BLOCK_COLOUR, DIAMOND_COLOUR);

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.IsFalse(boardModel.Board.IsOccupied(columnDiamond), "Sanity: the laser's wipe took it.");
            Assert.AreEqual(2, placedBroker.Published[0].DestroyedDiamondCountByColour[DIAMOND_COLOUR]);
        }

        [Test]
        public void TryPlacePiece_ClearingNoDiamond_PublishesANullDiamondTally()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PiecePlacedMessage> placedBroker);
            trayModel.SetSlot(0, Single, BLOCK_COLOUR);
            boardModel.Board.OccupyDiamond(new GridPosition(7, 7), BLOCK_COLOUR, DIAMOND_COLOUR);

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(0, 0)));

            Assert.IsNull(placedBroker.Published[0].DestroyedDiamondCountByColour);
            Assert.AreEqual(
                SpecialCellKind.Diamond, boardModel.GetSpecialKind(new GridPosition(7, 7)),
                "An untouched diamond is left exactly as it was.");
        }

        // --- ObjectiveProgress ---

        [Test]
        public void ApplyPlacement_ForADiamondsClearedObjective_AdvancesByTheWantedColourOnly()
        {
            ObjectiveProgress objective = DiamondObjective(target: 5);

            bool changed = objective.ApplyPlacement(
                Placement(Tally((DIAMOND_COLOUR, 2), (OTHER_DIAMOND_COLOUR, 3))));

            Assert.IsTrue(changed);
            Assert.AreEqual(2, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void ApplyPlacement_ClampsAtTheTargetAndCompletes()
        {
            ObjectiveProgress objective = DiamondObjective(target: 2);

            objective.ApplyPlacement(Placement(Tally((DIAMOND_COLOUR, 4))));

            Assert.AreEqual(2, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void ApplyPlacement_DestroyingOnlyOtherColouredDiamonds_DoesNotAdvance()
        {
            ObjectiveProgress objective = DiamondObjective(target: 5);

            bool changed = objective.ApplyPlacement(Placement(Tally((OTHER_DIAMOND_COLOUR, 3))));

            Assert.IsFalse(changed);
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void ApplyPlacement_WithNoDiamondTally_DoesNotAdvance()
        {
            ObjectiveProgress objective = DiamondObjective(target: 5);

            Assert.IsFalse(objective.ApplyPlacement(Placement(null)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        /// <summary>The block-colour tally and the diamond tally are separate inputs: a ColourCleared
        /// objective ignores diamonds and a DiamondsCleared objective ignores blocks, even when both
        /// name the same colour id.</summary>
        [Test]
        public void ApplyPlacement_TheTwoColourTalliesNeverCrossFeed()
        {
            ObjectiveProgress diamonds = DiamondObjective(target: 5);
            var colour = new ObjectiveProgress(new ObjectiveDefinition(
                "colour", ObjectiveType.ColourCleared, ObjectiveScope.PerRun, 5, requiredColourId: DIAMOND_COLOUR));

            // Eight blocks of the wanted colour destroyed, zero diamonds.
            ObjectivePlacementContext blocksOnly = new ObjectivePlacementContext(
                1, 1, 0, PieceFamily.Single, null, 0, false, 0, 0, false, false, false, 0f, 0,
                Tally((DIAMOND_COLOUR, 8)), 0, null);
            // Zero blocks tallied, three diamonds of the wanted colour.
            ObjectivePlacementContext diamondsOnly = Placement(Tally((DIAMOND_COLOUR, 3)));

            Assert.IsFalse(diamonds.ApplyPlacement(blocksOnly));
            Assert.IsFalse(colour.ApplyPlacement(diamondsOnly));
            Assert.IsTrue(diamonds.ApplyPlacement(diamondsOnly));
            Assert.IsTrue(colour.ApplyPlacement(blocksOnly));
            Assert.AreEqual(3, diamonds.CurrentValue);
            Assert.AreEqual(5, colour.CurrentValue);
        }

        [Test]
        public void ApplyPowerUpDiamondsCleared_AdvancesByTheWantedCount()
        {
            ObjectiveProgress objective = DiamondObjective(target: 10);

            Assert.IsTrue(objective.ApplyPowerUpDiamondsCleared(Tally((DIAMOND_COLOUR, 2), (OTHER_DIAMOND_COLOUR, 1))));
            Assert.AreEqual(2, objective.CurrentValue);
        }

        [Test]
        public void ApplyPowerUpDiamondsCleared_WithNoneOfTheWantedColour_ChangesNothing()
        {
            ObjectiveProgress objective = DiamondObjective(target: 10);

            Assert.IsFalse(objective.ApplyPowerUpDiamondsCleared(Tally((OTHER_DIAMOND_COLOUR, 4))));
            Assert.IsFalse(objective.ApplyPowerUpDiamondsCleared(null));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void ApplyPowerUpDiamondsCleared_OnAnotherObjectiveType_ChangesNothing()
        {
            var colour = new ObjectiveProgress(new ObjectiveDefinition(
                "colour", ObjectiveType.ColourCleared, ObjectiveScope.PerRun, 5, requiredColourId: DIAMOND_COLOUR));
            var timer = new ObjectiveProgress(new ObjectiveDefinition(
                "timer", ObjectiveType.TimerCellsMeltedInTime, ObjectiveScope.PerRun, 5));

            Assert.IsFalse(colour.ApplyPowerUpDiamondsCleared(Tally((DIAMOND_COLOUR, 3))));
            Assert.IsFalse(timer.ApplyPowerUpDiamondsCleared(Tally((DIAMOND_COLOUR, 3))));
        }

        /// <summary>The full power-up path: a resolver's triggers, read the way PowerUpSystem reads
        /// them at publish time, credit the objective the way ObjectiveSystem applies them.</summary>
        [Test]
        public void BombOverADiamond_ThroughTheTriggerReading_AdvancesTheObjective()
        {
            var board = new Board();
            var diamond = new GridPosition(3, 3);
            board.OccupyDiamond(diamond, BLOCK_COLOUR, DIAMOND_COLOUR);
            ObjectiveProgress objective = DiamondObjective(target: 1);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, diamond);
            bool changed = objective.ApplyPowerUpDiamondsCleared(
                DiamondClearEffect.CountDestroyedByColour(result.TriggeredSpecials));

            Assert.IsTrue(changed);
            Assert.IsTrue(objective.IsComplete);
        }

        // --- Definition and authoring ---

        [TestCase(0)]
        [TestCase(Board.COLOUR_COUNT + 1)]
        public void ObjectiveDefinition_DiamondsCleared_RejectsAColourOutsideThePalette(int colourId)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ObjectiveDefinition(
                "bad", ObjectiveType.DiamondsCleared, ObjectiveScope.PerRun, 1, requiredColourId: colourId));
        }

        [Test]
        public void LevelObjectiveConfig_DiamondsCleared_BuildsADefinitionCarryingTheColourId()
        {
            LevelObjectiveConfig config = ARow(
                $"{{\"_levelNumber\":1,\"_objectiveType\":{(int)ObjectiveType.DiamondsCleared},"
                + "\"_targetValue\":4,\"_requiredColourId\":3}");

            Assert.IsTrue(config.IsValid(out string error), error);
            ObjectiveDefinition definition = config.ToObjectiveDefinition();
            Assert.AreEqual(ObjectiveType.DiamondsCleared, definition.Type);
            Assert.AreEqual(3, definition.RequiredColourId);
        }

        [Test]
        public void LevelObjectiveConfig_DiamondsCleared_WithAColourOutsideThePalette_IsInvalid()
        {
            LevelObjectiveConfig config = ARow(
                $"{{\"_levelNumber\":1,\"_objectiveType\":{(int)ObjectiveType.DiamondsCleared},"
                + "\"_targetValue\":4,\"_requiredColourId\":0}");

            Assert.IsFalse(config.IsValid(out string error));
            StringAssert.Contains("colour id", error);
        }

        // --- Helpers ---

        private static void FillRowWithDiamondAt(Board board, int y, GridPosition diamond, int diamondColourId)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                var position = new GridPosition(x, y);
                if (position.Equals(diamond))
                {
                    board.OccupyDiamond(position, BLOCK_COLOUR, diamondColourId);
                    continue;
                }

                board.Occupy(position, BLOCK_COLOUR);
            }
        }

        private static ObjectiveProgress DiamondObjective(int target)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "diamonds", ObjectiveType.DiamondsCleared, ObjectiveScope.PerRun, target,
                requiredColourId: DIAMOND_COLOUR));
        }

        private static int[] Tally(params (int ColourId, int Count)[] entries)
        {
            var tally = new int[ColourTally.LENGTH];
            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                tally[entries[entryIndex].ColourId] = entries[entryIndex].Count;
            }

            return tally;
        }

        private static ObjectivePlacementContext Placement(int[] diamondTally)
        {
            return new ObjectivePlacementContext(
                1, 1, 0, PieceFamily.Single, null, 0, false, 0, 0,
                false, false, false, 0f, 0, null, 0, diamondTally);
        }

        private static BoardSystem CreateSystem(
            out BoardModel boardModel,
            out TrayModel trayModel,
            out TestMessageBroker<PiecePlacedMessage> placedBroker)
        {
            boardModel = new BoardModel();
            trayModel = new TrayModel();
            placedBroker = new TestMessageBroker<PiecePlacedMessage>();

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
                seed: 1);
        }

        private static LevelObjectiveConfig ARow(string json)
        {
            return JsonUtility.FromJson<LevelObjectiveConfig>(json);
        }
    }
}
