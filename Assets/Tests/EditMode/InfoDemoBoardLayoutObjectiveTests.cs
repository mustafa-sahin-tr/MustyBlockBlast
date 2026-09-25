using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using MustyBlockBlast.Presentation.Views;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// The board-layout objective card demos (issue #453): the catalog mapping — one cached demo per value the
    /// demo can draw — and, for each demo, that what it shows is what the real game does. The demo's starting
    /// board (with its ice socket, reinforced cell, timer cell or decorated dock piece set up through the
    /// game's own seeding calls) is rebuilt on a real <see cref="BoardSystem"/>, each of the demo's moves is
    /// played through <see cref="BoardSystem.TryPlacePiece"/>, the result is fed to a real
    /// <see cref="ObjectiveSystem"/>, and the objective must advance exactly as the demo's chip does while the
    /// demo ends on exactly the real board — and its ice level, hit count or countdown steps exactly as the
    /// real one does.
    /// </summary>
    public class InfoDemoBoardLayoutObjectiveTests
    {
        private const float TOLERANCE = 0.0001f;

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;
        private ObjectiveModel _objectiveModel;
        private BoardSystem _boardSystem;
        private ObjectiveSystem _objectiveSystem;

        [SetUp]
        public void CreateSystems()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
            _objectiveModel = new ObjectiveModel();

            // Deliberately unstarted, as InfoDemoLineClearObjectiveTests is: StartNewRun would draw over the
            // board and dock each test lays out by hand.
            _boardSystem = new BoardSystem(
                _boardModel,
                _trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                _piecePlacedBroker,
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

            _objectiveSystem = new ObjectiveSystem(
                _objectiveModel,
                new RunPauseModel(),
                _piecePlacedBroker,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<ScoreChangedMessage>(),
                new TestMessageBroker<PowerUpAppliedMessage>(),
                new TestMessageBroker<ObjectiveProgressChangedMessage>(),
                new TestMessageBroker<ObjectiveCompletedMessage>());
        }

        [TearDown]
        public void DisposeSystems() => _objectiveSystem.Dispose();

        // ---------------------------------------------------------------- catalog

        [Test]
        public void ParameterlessTypes_HaveOneCachedDemoEach()
        {
            ObjectiveType[] types =
            {
                ObjectiveType.BoardWipeCount,
                ObjectiveType.FourCornersCleared,
                ObjectiveType.CenterCoreEvacuated,
                ObjectiveType.IceCellsCleared,
                ObjectiveType.ReinforcedCellsCleared,
                ObjectiveType.TimerCellsMeltedInTime,
            };
            float[] durations =
            {
                BoardWipeInfoDemo.LOOP_DURATION,
                FourCornersClearedInfoDemo.LOOP_DURATION,
                CenterCoreEvacuatedInfoDemo.LOOP_DURATION,
                IceCellsClearedInfoDemo.LOOP_DURATION,
                ReinforcedCellsClearedInfoDemo.LOOP_DURATION,
                TimerCellsMeltedInTimeInfoDemo.LOOP_DURATION,
            };

            InfoDemoCatalog catalog = new InfoDemoCatalog();
            HashSet<InfoDemoTimeline> seen = new HashSet<InfoDemoTimeline>();
            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                InfoDemoTimeline first = catalog.FindObjective(Plain(types[typeIndex]));
                Assert.IsNotNull(first, types[typeIndex].ToString());
                Assert.AreSame(first, catalog.FindObjective(Plain(types[typeIndex], target: 4)), types[typeIndex] + " cached");
                Assert.IsTrue(seen.Add(first), types[typeIndex] + " has its own demo");
                Assert.AreEqual(durations[typeIndex], first.Duration, TOLERANCE, types[typeIndex].ToString());
            }
        }

        [Test]
        public void ColourAndDiamonds_HaveOneCachedDemoPerColourId()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();
            HashSet<InfoDemoTimeline> seen = new HashSet<InfoDemoTimeline>();

            for (int colourId = 1; colourId <= Board.COLOUR_COUNT; colourId++)
            {
                InfoDemoTimeline colour = catalog.FindObjective(Coloured(ObjectiveType.ColourCleared, colourId));
                InfoDemoTimeline diamonds = catalog.FindObjective(Coloured(ObjectiveType.DiamondsCleared, colourId));

                Assert.IsNotNull(colour, $"colour {colourId}");
                Assert.IsNotNull(diamonds, $"diamonds {colourId}");
                Assert.AreSame(colour, catalog.FindObjective(Coloured(ObjectiveType.ColourCleared, colourId, 10)));
                Assert.AreSame(diamonds, catalog.FindObjective(Coloured(ObjectiveType.DiamondsCleared, colourId, 10)));
                Assert.IsTrue(seen.Add(colour));
                Assert.IsTrue(seen.Add(diamonds));
            }

            // The constructor refuses any other colour id, so these are the demos' own guards.
            Assert.IsFalse(ColourClearedInfoDemo.Supports(0));
            Assert.IsFalse(ColourClearedInfoDemo.Supports(Board.COLOUR_COUNT + 1));
            Assert.IsFalse(DiamondsClearedInfoDemo.Supports(0));
            Assert.IsFalse(DiamondsClearedInfoDemo.Supports(Board.COLOUR_COUNT + 1));
        }

        [Test]
        public void NoIsolatedHoles_HasOneCachedDemoPerTarget_AndItsChipCountsToThatTarget()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            InfoDemoTimeline five = catalog.FindObjective(Plain(ObjectiveType.NoIsolatedHolesStreak, target: 5));
            Assert.IsNotNull(five);
            Assert.AreSame(five, catalog.FindObjective(Plain(ObjectiveType.NoIsolatedHolesStreak, target: 5)));
            Assert.AreNotSame(five, catalog.FindObjective(Plain(ObjectiveType.NoIsolatedHolesStreak, target: 3)));
            Assert.IsFalse(NoIsolatedHolesStreakInfoDemo.Supports(NoIsolatedHolesStreakInfoDemo.MAX_TARGET + 1));

            // 5: 2/5 → 5/5, one step per placement.
            NoIsolatedHolesStreakInfoDemo.Build(5, out InfoDemoProgressChip chipFive);
            Assert.AreEqual(5, chipFive.Value);
            Assert.IsTrue(chipFive.IsComplete);
            InfoDemoTimeline timeline = NoIsolatedHolesStreakInfoDemo.Build(5, out InfoDemoProgressChip chip);
            Assert.AreEqual(1f, Sample(timeline, 0.2f)[chip.CounterLabelId(2)].Alpha, TOLERANCE, "starts at 2/5");
            for (int placementIndex = 0; placementIndex < NoIsolatedHolesStreakInfoDemo.PLACEMENT_COUNT; placementIndex++)
            {
                float settled = NoIsolatedHolesStreakInfoDemo.LandTime(placementIndex) + 0.5f;
                Assert.AreEqual(1f, Sample(timeline, settled)[chip.CounterLabelId(3 + placementIndex)].Alpha, TOLERANCE);
            }

            // Under three, from 0 and only as far as the target.
            NoIsolatedHolesStreakInfoDemo.Build(1, out InfoDemoProgressChip chipOne);
            Assert.AreEqual(1, chipOne.Value);
            Assert.AreEqual(0, NoIsolatedHolesStreakInfoDemo.ChipStart(2));
        }

        // ---------------------------------------------------------------- Board Wipe

        [Test]
        public void BoardWipeDemo_ItsSingleLeavesTheRealBoardEmpty_AndTheObjectiveAdvances()
        {
            ApplyRows(BoardWipeInfoDemo.Rows);
            ObjectiveProgress wipe = Track(Plain(ObjectiveType.BoardWipeCount));

            Place(PieceCatalog.SingleCell, BoardWipeInfoDemo.LAND_ROW, BoardWipeInfoDemo.LAND_COLUMN);

            Assert.IsTrue(LastPlacement().BoardEmptyAfterPlacement);
            Assert.IsTrue(_boardModel.Board.IsEmpty());
            Assert.AreEqual(1, wipe.CurrentValue);
            Assert.IsTrue(wipe.IsComplete);

            InfoDemoTimeline timeline = BoardWipeInfoDemo.Build(out InfoDemoProgressChip chip);
            InfoDemoElementState[] after = Sample(timeline, BoardWipeInfoDemo.CHIP_ADVANCE_TIME + 1f);
            AssertBoardMatches(after, _boardModel.Board);
            Assert.AreEqual(1f, after[chip.CheckId].Alpha, TOLERANCE);
            Assert.AreEqual(0f, Sample(timeline, BoardWipeInfoDemo.CHIP_ADVANCE_TIME - 0.05f)[chip.CheckId].Alpha, TOLERANCE);
        }

        [Test]
        public void BoardWipe_IsNotAdvancedByAClearThatLeavesBlocksBehind()
        {
            // The Center Core demo's clear empties the centre but not the board.
            ApplyRows(CenterCoreEvacuatedInfoDemo.Rows);
            ObjectiveProgress wipe = Track(Plain(ObjectiveType.BoardWipeCount));

            Place(PieceCatalog.SingleCell, CenterCoreEvacuatedInfoDemo.LAND_ROW, CenterCoreEvacuatedInfoDemo.LAND_COLUMN);

            Assert.AreEqual(1, LastPlacement().LinesCleared);
            Assert.AreEqual(0, wipe.CurrentValue);
        }

        // ---------------------------------------------------------------- Four Corners

        [Test]
        public void FourCornersDemo_ItsRowClearTouchesTwoCorners_AndTheObjectiveAdvancesOnce()
        {
            ApplyRows(FourCornersClearedInfoDemo.Rows);
            ObjectiveProgress corners = Track(Plain(ObjectiveType.FourCornersCleared, target: 2));

            Place(PieceCatalog.SingleCell, FourCornersClearedInfoDemo.LAND_ROW, FourCornersClearedInfoDemo.LAND_COLUMN);

            PiecePlacedMessage placed = LastPlacement();
            Assert.AreEqual(1, placed.RowsCleared);
            Assert.IsTrue(placed.AnyCornerCleared);
            Assert.AreEqual(1, corners.CurrentValue, "one placement is one count, however many corners it took");

            AssertBoardMatches(Sample(FourCornersClearedInfoDemo.Build(), 3.5f), _boardModel.Board);
        }

        [Test]
        public void FourCorners_IsNotAdvancedByAClearAwayFromTheEdges()
        {
            ApplyRows(CenterCoreEvacuatedInfoDemo.Rows);
            ObjectiveProgress corners = Track(Plain(ObjectiveType.FourCornersCleared));

            Place(PieceCatalog.SingleCell, CenterCoreEvacuatedInfoDemo.LAND_ROW, CenterCoreEvacuatedInfoDemo.LAND_COLUMN);

            Assert.AreEqual(1, LastPlacement().LinesCleared, "row 4 clears");
            Assert.IsFalse(LastPlacement().AnyCornerCleared);
            Assert.AreEqual(0, corners.CurrentValue);
        }

        // ---------------------------------------------------------------- Center Core

        [Test]
        public void CenterCoreDemo_ItsClearEmptiesTheRealCentre4x4_AndTheObjectiveAdvances()
        {
            ApplyRows(CenterCoreEvacuatedInfoDemo.Rows);
            ObjectiveProgress core = Track(Plain(ObjectiveType.CenterCoreEvacuated));
            Assert.IsFalse(_boardModel.Board.IsCenterCoreEmpty(), "the centre starts occupied");

            Place(PieceCatalog.SingleCell, CenterCoreEvacuatedInfoDemo.LAND_ROW, CenterCoreEvacuatedInfoDemo.LAND_COLUMN);

            Assert.IsTrue(LastPlacement().CenterCoreEmptyAfterPlacement);
            Assert.AreEqual(1, core.CurrentValue);

            InfoDemoElementState[] after = Sample(CenterCoreEvacuatedInfoDemo.Build(), 3.5f);
            AssertBoardMatches(after, _boardModel.Board);
            for (int row = CenterCoreEvacuatedInfoDemo.CORE_FIRST; row <= CenterCoreEvacuatedInfoDemo.CORE_LAST; row++)
            {
                for (int column = CenterCoreEvacuatedInfoDemo.CORE_FIRST; column <= CenterCoreEvacuatedInfoDemo.CORE_LAST; column++)
                {
                    Assert.IsTrue(_boardModel.Board.IsInside(ToGrid(row, column)));
                    Assert.AreEqual(InfoDemoPaint.NONE, after[InfoDemoLayout.BoardBlockId(row, column)].Paint, $"({row},{column})");
                }
            }
        }

        [Test]
        public void CenterCore_IsNotAdvancedByAClearThatLeavesACentreBlock()
        {
            // The Four Corners demo's row-0 clear leaves (2,3)-(2,4) inside the core.
            ApplyRows(FourCornersClearedInfoDemo.Rows);
            ObjectiveProgress core = Track(Plain(ObjectiveType.CenterCoreEvacuated));

            Place(PieceCatalog.SingleCell, FourCornersClearedInfoDemo.LAND_ROW, FourCornersClearedInfoDemo.LAND_COLUMN);

            Assert.AreEqual(1, LastPlacement().LinesCleared);
            Assert.AreEqual(0, core.CurrentValue);
        }

        // ---------------------------------------------------------------- No Isolated Holes

        [Test]
        public void NoHolesDemo_EachPlacementLeavesNoIsolatedHole_AndTheRealStreakGrowsWithEach()
        {
            ApplyRows(NoIsolatedHolesStreakInfoDemo.Rows);
            ObjectiveProgress streak = Track(Plain(ObjectiveType.NoIsolatedHolesStreak, target: 3));

            for (int placementIndex = 0; placementIndex < NoIsolatedHolesStreakInfoDemo.PLACEMENT_COUNT; placementIndex++)
            {
                Vector2Int land = NoIsolatedHolesStreakInfoDemo.LandCells[placementIndex];
                Place(CatalogPiece(NoIsolatedHolesStreakInfoDemo.Shapes[placementIndex]), land.y, land.x);

                PiecePlacedMessage placed = LastPlacement();
                Assert.AreEqual(0, placed.LinesCleared, $"placement {placementIndex} clears nothing");
                Assert.IsFalse(placed.HasIsolatedHolesAfterPlacement, $"placement {placementIndex} walls nothing off");
                Assert.AreEqual(placementIndex + 1, streak.CurrentValue, $"after placement {placementIndex}");
            }

            Assert.IsTrue(streak.IsComplete);
            AssertBoardMatches(Sample(NoIsolatedHolesStreakInfoDemo.Build(3), 5.0f), _boardModel.Board);
        }

        [Test]
        public void NoHoles_APlacementThatWallsOffTheGap_ResetsTheLiveStreak()
        {
            ApplyRows(NoIsolatedHolesStreakInfoDemo.Rows);
            ObjectiveProgress streak = Track(Plain(ObjectiveType.NoIsolatedHolesStreak, target: 5));

            Place(CatalogPiece(NoIsolatedHolesStreakInfoDemo.SquareShape), 6, 3);
            Place(CatalogPiece(NoIsolatedHolesStreakInfoDemo.VerticalDominoShape), 6, 5);
            Assert.AreEqual(2, streak.CurrentValue);

            // One cell to the left of the demo's third move: (6,2) is sealed in.
            Place(CatalogPiece(NoIsolatedHolesStreakInfoDemo.HorizontalDominoShape), 5, 2);
            Assert.IsTrue(LastPlacement().HasIsolatedHolesAfterPlacement);
            Assert.AreEqual(2, streak.CurrentValue, "the best streak stays");

            // The live streak restarted and the hole is still there: another placement cannot extend anything.
            Place(PieceCatalog.SingleCell, 0, 0);
            Assert.IsTrue(LastPlacement().HasIsolatedHolesAfterPlacement);
            Assert.AreEqual(2, streak.CurrentValue, "no progress past the best while a cell is walled off");
            Assert.IsFalse(streak.IsComplete);
        }

        // ---------------------------------------------------------------- Colour Cleared

        [Test]
        public void ColourDemo_EveryColourId_ItsTwoClearsDestroyThreeBlocksOfTheColour_AsTheChipCounts()
        {
            for (int colourId = 1; colourId <= Board.COLOUR_COUNT; colourId++)
            {
                CreateSystems();
                InfoDemoTimeline timeline = ColourClearedInfoDemo.Build(colourId, out InfoDemoProgressChip chip);
                ApplyBoard(Sample(timeline, 0f));
                Assert.AreEqual(ColourClearedInfoDemo.TARGET, ColourClearedInfoDemo.CellsOfColour(colourId).Length, $"colour {colourId}");

                ObjectiveProgress colour = Track(Coloured(ObjectiveType.ColourCleared, colourId, ColourClearedInfoDemo.TARGET));

                // The singles are other colours: only the board's own blocks of the colour count.
                Place(PieceCatalog.SingleCell, ColourClearedInfoDemo.FIRST_ROW, ColourClearedInfoDemo.FIRST_COLUMN,
                    ColourClearedInfoDemo.PaintFor('D', colourId));
                Assert.AreEqual(2, colour.CurrentValue, $"colour {colourId}: row 7 takes two");
                Assert.AreEqual(1f, Sample(timeline, 2.1f)[chip.CounterLabelId(2)].Alpha, TOLERANCE, $"colour {colourId} chip 2/3");

                Place(PieceCatalog.SingleCell, ColourClearedInfoDemo.SECOND_ROW, ColourClearedInfoDemo.SECOND_COLUMN,
                    ColourClearedInfoDemo.PaintFor('B', colourId));
                Assert.AreEqual(3, colour.CurrentValue, $"colour {colourId}: row 5 takes the last");
                Assert.IsTrue(colour.IsComplete);

                InfoDemoElementState[] after = Sample(timeline, 4.2f);
                Assert.AreEqual(1f, after[chip.CheckId].Alpha, TOLERANCE, $"colour {colourId} done");
                AssertBoardMatches(after, _boardModel.Board, $"colour {colourId}");
            }
        }

        [Test]
        public void ColourDemo_NoOtherCellOrTrayPieceIsTheObjectivesColour()
        {
            for (int colourId = 1; colourId <= Board.COLOUR_COUNT; colourId++)
            {
                Assert.AreNotEqual(colourId, ColourClearedInfoDemo.PaintFor('A', colourId));
                Assert.AreNotEqual(colourId, ColourClearedInfoDemo.PaintFor('B', colourId));
                Assert.AreNotEqual(colourId, ColourClearedInfoDemo.PaintFor('C', colourId));
                Assert.AreNotEqual(colourId, ColourClearedInfoDemo.PaintFor('D', colourId));
                Assert.AreNotEqual(InfoDemoPaint.NONE, ColourClearedInfoDemo.PaintFor('D', colourId));
            }
        }

        // ---------------------------------------------------------------- Diamonds Cleared

        [Test]
        public void DiamondsDemo_TheDecoratedPieceLandsAsDiamondCells_AndItsClearCollectsBoth()
        {
            for (int colourId = 1; colourId <= Board.COLOUR_COUNT; colourId++)
            {
                CreateSystems();
                ApplyRows(DiamondsClearedInfoDemo.Rows);
                ObjectiveProgress diamonds = Track(Coloured(ObjectiveType.DiamondsCleared, colourId, 10));
                ObjectiveProgress otherColour = new ObjectiveProgress(
                    Coloured(ObjectiveType.DiamondsCleared, (colourId % Board.COLOUR_COUNT) + 1, 10));
                _objectiveModel.SetObjectives(new[] { diamonds, otherColour });

                Piece line = CatalogPiece(DiamondsClearedInfoDemo.LineShape);
                int[] decoration = DecorationFor(line, DiamondsClearedInfoDemo.LineShape, DiamondsClearedInfoDemo.DiamondColourIds(colourId));
                GridPosition anchor = AnchorFor(line, DiamondsClearedInfoDemo.LAND_ROW, DiamondsClearedInfoDemo.LAND_COLUMN);
                _trayModel.SetSlot(0, line, DiamondsClearedInfoDemo.PiecePaint(colourId), SpecialPieceKind.None, decoration);
                Assert.IsTrue(_trayModel.HasDiamonds(0));

                // Before the clear resolves the gems are diamond cells: check through a copy of the landing.
                Board preview = _boardModel.Board.Clone();
                for (int cellIndex = 0; cellIndex < line.Offsets.Count; cellIndex++)
                {
                    GridPosition cell = anchor + line.Offsets[cellIndex];
                    Assert.IsTrue(DiamondCellRules.CanCarryDiamond(preview, cell), $"colour {colourId}");
                }

                Assert.IsTrue(_boardSystem.TryPlacePiece(0, anchor), $"colour {colourId}");

                PiecePlacedMessage placed = LastPlacement();
                Assert.AreEqual(1, placed.RowsCleared);
                Assert.AreEqual(DiamondsClearedInfoDemo.GemCount, diamonds.CurrentValue, $"colour {colourId}: both gems");
                Assert.AreEqual(0, otherColour.CurrentValue, $"colour {colourId}: another colour's gems count nothing");

                InfoDemoTimeline timeline = DiamondsClearedInfoDemo.Build(colourId, out InfoDemoProgressChip chip);
                InfoDemoElementState[] after = Sample(timeline, 3.5f);
                AssertBoardMatches(after, _boardModel.Board, $"colour {colourId}");
                Assert.AreEqual(1f, after[chip.CheckId].Alpha, TOLERANCE);
            }
        }

        [Test]
        public void DiamondsDemo_DecoratesAsTheRealDecoratorCan_AndDrawsTheGemInItsOwnColour()
        {
            // DiamondPieceDecorator never decorates every cell of a piece: at most cells - 1.
            Assert.LessOrEqual(DiamondsClearedInfoDemo.GemCount, DiamondsClearedInfoDemo.LineShape.Length - 1);
            Assert.GreaterOrEqual(DiamondsClearedInfoDemo.GemCount, 1);

            for (int colourId = 1; colourId <= Board.COLOUR_COUNT; colourId++)
            {
                Assert.AreNotEqual(colourId, DiamondsClearedInfoDemo.PiecePaint(colourId), "the gem reads against its block");

                InfoDemoTimeline timeline = DiamondsClearedInfoDemo.Build(colourId, out InfoDemoProgressChip chip);
                InfoDemoElement chipIcon = timeline.GetElement(chip.CaptionId);
                Assert.AreEqual(InfoDemoSprite.DiamondIcon, chipIcon.Sprite);
                Assert.AreEqual(colourId, Sample(timeline, 0f)[chip.CaptionId].Paint, "the chip's gem is the objective's colour");

                // The decorated tray piece and the gems on the board carry the objective's colour.
                int layers = 0;
                for (int elementId = 0; elementId < timeline.ElementCount; elementId++)
                {
                    InfoDemoElement element = timeline.GetElement(elementId);
                    if (element.Kind == InfoDemoElementKind.Piece && element.CellValues != null)
                    {
                        CollectionAssert.AreEqual(DiamondsClearedInfoDemo.DiamondColourIds(colourId), element.CellValues);
                    }

                    if (element.Kind == InfoDemoElementKind.CellLayer)
                    {
                        Assert.AreEqual((int)InfoDemoCellLayer.Diamond, element.SpriteParameter);
                        Assert.AreEqual(colourId, element.Initial.Paint);
                        Assert.AreEqual(0f, Sample(timeline, 0.5f)[elementId].Alpha, TOLERANCE, "not on the board before landing");
                        Assert.AreEqual(1f, Sample(timeline, 1.25f)[elementId].Alpha, TOLERANCE, "on the board once landed");
                        layers++;
                    }
                }

                Assert.AreEqual(DiamondsClearedInfoDemo.GemCount, layers);
            }
        }

        // ---------------------------------------------------------------- Ice

        [Test]
        public void IceDemo_EachClearOfTheBlockOnTheSocketMeltsOneLevel_AndOnlyTheLastCounts()
        {
            ApplyRows(IceCellsClearedInfoDemo.Rows);
            GridPosition socket = ToGrid(IceCellsClearedInfoDemo.ICE_ROW, IceCellsClearedInfoDemo.ICE_COLUMN);
            _boardModel.SetIceLevel(socket, IceCellsClearedInfoDemo.START_ICE_LEVEL);
            ObjectiveProgress ice = Track(Plain(ObjectiveType.IceCellsCleared));
            Assert.IsFalse(_boardModel.Board.IsOccupied(socket), "an ice socket starts empty and playable");

            InfoDemoTimeline timeline = IceCellsClearedInfoDemo.Build(out InfoDemoProgressChip chip, out int iceLayer);
            Assert.AreEqual((int)InfoDemoCellLayer.IceSocket, timeline.GetElement(iceLayer).SpriteParameter);
            Assert.AreEqual(IceCellsClearedInfoDemo.START_ICE_LEVEL, Sample(timeline, 0.2f)[iceLayer].Paint);
            Assert.AreEqual(InfoDemoPaint.NONE, Sample(timeline, 0.2f)[BlockAt(IceCellsClearedInfoDemo.ICE_ROW, IceCellsClearedInfoDemo.ICE_COLUMN)].Paint);

            // 1. A single on the socket; row 7 clears it: 2 → 1, not counted.
            Place(PieceCatalog.SingleCell, IceCellsClearedInfoDemo.ICE_ROW, IceCellsClearedInfoDemo.ICE_COLUMN);
            Assert.AreEqual(1, LastPlacement().RowsCleared);
            Assert.AreEqual(1, _boardModel.GetIceLevel(socket));
            Assert.AreEqual(0, ice.CurrentValue);
            InfoDemoElementState[] between = Sample(timeline, IceCellsClearedInfoDemo.SECOND_PLACE_START);
            Assert.AreEqual(_boardModel.GetIceLevel(socket), between[iceLayer].Paint, "the demo's socket is at level 1 too");
            AssertBoardMatches(between, _boardModel.Board, "after the row");
            Assert.AreEqual(0f, between[chip.CheckId].Alpha, TOLERANCE);

            // 2. A vertical 1x2 onto it; column 4 clears it: the last level melts and it counts.
            Place(CatalogPiece(IceCellsClearedInfoDemo.VerticalDominoShape), IceCellsClearedInfoDemo.SECOND_LAND_ROW, IceCellsClearedInfoDemo.ICE_COLUMN);
            Assert.AreEqual(1, LastPlacement().ColumnsCleared);
            Assert.AreEqual(1, LastPlacement().IceCellsMeltedCount);
            Assert.AreEqual(0, _boardModel.GetIceLevel(socket));
            Assert.AreEqual(1, ice.CurrentValue);
            Assert.IsTrue(ice.IsComplete);

            InfoDemoElementState[] after = Sample(timeline, 4.6f);
            Assert.AreEqual(0, after[iceLayer].Paint, "no ice left in the demo either");
            AssertBoardMatches(after, _boardModel.Board, "after the column");
            Assert.AreEqual(1f, after[chip.CheckId].Alpha, TOLERANCE);
        }

        // ---------------------------------------------------------------- Reinforced

        [Test]
        public void ReinforcedDemo_TheRowClearOnlySpendsAHit_AndTheColumnClearBreaksIt()
        {
            ApplyRows(ReinforcedCellsClearedInfoDemo.Rows);
            GridPosition cell = ToGrid(ReinforcedCellsClearedInfoDemo.REINFORCED_ROW, ReinforcedCellsClearedInfoDemo.REINFORCED_COLUMN);
            int colour = _boardModel.GetCell(cell);
            _boardModel.Clear(cell);
            _boardModel.OccupyReinforced(cell, colour, ReinforcedCellsClearedInfoDemo.START_HIT_COUNT, ReinforcedCellsClearedInfoDemo.SKIN);
            ObjectiveProgress reinforced = Track(Plain(ObjectiveType.ReinforcedCellsCleared));

            InfoDemoTimeline timeline = ReinforcedCellsClearedInfoDemo.Build(out InfoDemoProgressChip chip, out int armour);
            Assert.AreEqual((int)InfoDemoCellLayer.Armour, timeline.GetElement(armour).SpriteParameter);
            Assert.AreEqual(ReinforcedCellsClearedInfoDemo.SKIN, timeline.GetElement(armour).Variant);
            Assert.AreEqual(ReinforcedCellsClearedInfoDemo.START_HIT_COUNT, Sample(timeline, 0.2f)[armour].Paint);

            // 1. Row 7 clears around it: one hit spent, the block still stands, nothing counted.
            Place(PieceCatalog.SingleCell, ReinforcedCellsClearedInfoDemo.FIRST_LAND_ROW, ReinforcedCellsClearedInfoDemo.FIRST_LAND_COLUMN);
            Assert.AreEqual(1, LastPlacement().RowsCleared);
            Assert.IsTrue(_boardModel.Board.IsOccupied(cell));
            Assert.AreEqual(1, _boardModel.GetHitCount(cell));
            Assert.AreEqual(0, reinforced.CurrentValue);
            InfoDemoElementState[] between = Sample(timeline, ReinforcedCellsClearedInfoDemo.SECOND_PLACE_START);
            Assert.AreEqual(_boardModel.GetHitCount(cell), between[armour].Paint, "one armour stage left in the demo too");
            AssertBoardMatches(between, _boardModel.Board, "after the row");

            // 2. Column 3 clears it on its last hit: destroyed and counted.
            Place(PieceCatalog.SingleCell, ReinforcedCellsClearedInfoDemo.SECOND_LAND_ROW, ReinforcedCellsClearedInfoDemo.SECOND_LAND_COLUMN);
            Assert.AreEqual(1, LastPlacement().ColumnsCleared);
            Assert.AreEqual(1, LastPlacement().ReinforcedCellsFullyClearedCount);
            Assert.IsFalse(_boardModel.Board.IsOccupied(cell));
            Assert.AreEqual(1, reinforced.CurrentValue);
            Assert.IsTrue(reinforced.IsComplete);

            InfoDemoElementState[] after = Sample(timeline, 4.6f);
            Assert.AreEqual(0, after[armour].Paint);
            AssertBoardMatches(after, _boardModel.Board, "after the column");
            Assert.AreEqual(1f, after[chip.CheckId].Alpha, TOLERANCE);
        }

        // ---------------------------------------------------------------- Timer

        [Test]
        public void TimerDemo_ThePlacementTicksItToOne_AndTheClearTakesItInTime()
        {
            ApplyRows(TimerCellsMeltedInTimeInfoDemo.Rows);
            GridPosition cell = ToGrid(TimerCellsMeltedInTimeInfoDemo.TIMER_ROW, TimerCellsMeltedInTimeInfoDemo.TIMER_COLUMN);
            int colour = _boardModel.GetCell(cell);
            _boardModel.Clear(cell);
            _boardModel.OccupyTimer(cell, colour, TimerCellsMeltedInTimeInfoDemo.START_COUNTDOWN);
            ObjectiveProgress timers = Track(Plain(ObjectiveType.TimerCellsMeltedInTime));

            InfoDemoTimeline timeline = TimerCellsMeltedInTimeInfoDemo.Build(out InfoDemoProgressChip chip, out int timer);
            Assert.AreEqual((int)InfoDemoCellLayer.Timer, timeline.GetElement(timer).SpriteParameter);
            Assert.AreEqual(TimerCellsMeltedInTimeInfoDemo.START_COUNTDOWN, Sample(timeline, 0.2f)[timer].Paint);

            // 1. The 1x2 clears nothing; the countdown still ticks.
            Place(CatalogPiece(new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) }),
                TimerCellsMeltedInTimeInfoDemo.FIRST_LAND_ROW, TimerCellsMeltedInTimeInfoDemo.FIRST_LAND_COLUMN);
            Assert.AreEqual(0, LastPlacement().LinesCleared);
            Assert.AreEqual(1, _boardModel.GetTimerCountdown(cell));
            Assert.AreEqual(SpecialCellKind.Timer, _boardModel.GetSpecialKind(cell));
            InfoDemoElementState[] between = Sample(timeline, TimerCellsMeltedInTimeInfoDemo.SECOND_PLACE_START);
            Assert.AreEqual(_boardModel.GetTimerCountdown(cell), between[timer].Paint, "the demo's number reads 1 too");
            AssertBoardMatches(between, _boardModel.Board, "after the 1x2");

            // 2. Row 7 clears it before it runs out: counted.
            Place(PieceCatalog.SingleCell, TimerCellsMeltedInTimeInfoDemo.SECOND_LAND_ROW, TimerCellsMeltedInTimeInfoDemo.SECOND_LAND_COLUMN);
            Assert.AreEqual(1, LastPlacement().RowsCleared);
            Assert.AreEqual(1, LastPlacement().TimerCellsClearedInTimeCount);
            Assert.AreEqual(1, timers.CurrentValue);
            Assert.IsTrue(timers.IsComplete);

            InfoDemoElementState[] after = Sample(timeline, 4.4f);
            Assert.AreEqual(0, after[timer].Paint);
            AssertBoardMatches(after, _boardModel.Board, "after the clear");
            Assert.AreEqual(1f, after[chip.CheckId].Alpha, TOLERANCE);
        }

        [Test]
        public void Timer_OneThatRunsOutBeforeItIsCleared_CountsNothing()
        {
            ApplyRows(TimerCellsMeltedInTimeInfoDemo.Rows);
            GridPosition cell = ToGrid(TimerCellsMeltedInTimeInfoDemo.TIMER_ROW, TimerCellsMeltedInTimeInfoDemo.TIMER_COLUMN);
            int colour = _boardModel.GetCell(cell);
            _boardModel.Clear(cell);
            _boardModel.OccupyTimer(cell, colour, 1);
            ObjectiveProgress timers = Track(Plain(ObjectiveType.TimerCellsMeltedInTime));

            // With one placement left, the 1x2 runs it out: it becomes a plain block and row 7's clear is too late.
            Place(CatalogPiece(new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) }),
                TimerCellsMeltedInTimeInfoDemo.FIRST_LAND_ROW, TimerCellsMeltedInTimeInfoDemo.FIRST_LAND_COLUMN);
            Assert.AreEqual(SpecialCellKind.None, _boardModel.GetSpecialKind(cell));
            Place(PieceCatalog.SingleCell, TimerCellsMeltedInTimeInfoDemo.SECOND_LAND_ROW, TimerCellsMeltedInTimeInfoDemo.SECOND_LAND_COLUMN);

            Assert.AreEqual(1, LastPlacement().RowsCleared);
            Assert.AreEqual(0, timers.CurrentValue);
        }

        // ---------------------------------------------------------------- copy

        [Test]
        public void TimerCellsMeltedInTime_HasItsOwnTitleAndDescriptionKeys()
        {
            Assert.AreEqual(
                LocalizationKeys.OBJECTIVE_NAME_TIMER_CELLS_MELTED_IN_TIME,
                ObjectiveDescriptionFormatter.TitleKey(ObjectiveType.TimerCellsMeltedInTime));
            Assert.AreNotEqual(
                ObjectiveDescriptionFormatter.TitleKey(ObjectiveType.SimultaneousLineClear),
                ObjectiveDescriptionFormatter.TitleKey(ObjectiveType.TimerCellsMeltedInTime));
        }

        // ---------------------------------------------------------------- helpers

        private ObjectiveProgress Track(ObjectiveDefinition definition)
        {
            ObjectiveProgress progress = new ObjectiveProgress(definition);
            _objectiveModel.SetCurrentObjective(progress);
            return progress;
        }

        private PiecePlacedMessage LastPlacement()
        {
            Assert.Greater(_piecePlacedBroker.Published.Count, 0, "a placement was published");
            return _piecePlacedBroker.Published[_piecePlacedBroker.Published.Count - 1];
        }

        private static ObjectiveDefinition Plain(ObjectiveType type, int target = 1)
            => new ObjectiveDefinition("test", type, ObjectiveScope.PerRun, target);

        private static ObjectiveDefinition Coloured(ObjectiveType type, int colourId, int target = 3)
            => new ObjectiveDefinition("test", type, ObjectiveScope.PerRun, target, requiredColourId: colourId);

        /// <summary>Places <paramref name="piece"/> through the real <see cref="BoardSystem.TryPlacePiece"/> with its
        /// footprint's top-left on demo cell (<paramref name="topRow"/>, <paramref name="leftColumn"/>).</summary>
        private void Place(Piece piece, int topRow, int leftColumn, int colourId = InfoDemoPaint.BLOCK_4)
        {
            _trayModel.SetSlot(0, piece, colourId);
            Assert.IsTrue(_boardSystem.TryPlacePiece(0, AnchorFor(piece, topRow, leftColumn)), $"{piece.Id} at ({topRow},{leftColumn})");
        }

        /// <summary>The catalog anchor that puts <paramref name="piece"/>'s footprint top-left on demo cell
        /// (<paramref name="topRow"/>, <paramref name="leftColumn"/>).</summary>
        private static GridPosition AnchorFor(Piece piece, int topRow, int leftColumn)
        {
            int minX = int.MaxValue;
            int maxY = int.MinValue;
            for (int offsetIndex = 0; offsetIndex < piece.Offsets.Count; offsetIndex++)
            {
                minX = Mathf.Min(minX, piece.Offsets[offsetIndex].X);
                maxY = Mathf.Max(maxY, piece.Offsets[offsetIndex].Y);
            }

            return new GridPosition(leftColumn - minX, Board.SIZE - 1 - topRow - maxY);
        }

        /// <summary>The catalog piece whose footprint is exactly the demo shape <paramref name="shape"/>.</summary>
        private static Piece CatalogPiece(Vector2Int[] shape)
        {
            IReadOnlyList<Piece> pieces = PieceCatalog.AllPieces;
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                Vector2Int[] candidate = PieceIdLineClearInfoDemo.DemoShape(pieces[pieceIndex]);
                if (SameCells(candidate, shape))
                {
                    return pieces[pieceIndex];
                }
            }

            Assert.Fail("No catalog piece has the demo's shape.");
            return null;
        }

        /// <summary><paramref name="piece"/>'s decoration (one gem colour id per catalog offset) for the demo's
        /// per-cell ids <paramref name="demoIds"/>, parallel to <paramref name="demoShape"/>.</summary>
        private static int[] DecorationFor(Piece piece, Vector2Int[] demoShape, int[] demoIds)
        {
            Vector2Int[] pieceShape = PieceIdLineClearInfoDemo.DemoShape(piece);
            int[] decoration = new int[piece.Offsets.Count];
            for (int offsetIndex = 0; offsetIndex < pieceShape.Length; offsetIndex++)
            {
                for (int demoIndex = 0; demoIndex < demoShape.Length; demoIndex++)
                {
                    if (demoShape[demoIndex] == pieceShape[offsetIndex])
                    {
                        decoration[offsetIndex] = demoIds[demoIndex];
                    }
                }
            }

            return decoration;
        }

        private static bool SameCells(Vector2Int[] a, Vector2Int[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int aIndex = 0; aIndex < a.Length; aIndex++)
            {
                if (System.Array.IndexOf(b, a[aIndex]) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static int BlockAt(int row, int column) => InfoDemoLayout.BoardBlockId(row, column);

        private static InfoDemoElementState[] Sample(InfoDemoTimeline timeline, float time)
        {
            InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];
            timeline.Evaluate(time, states);
            return states;
        }

        private void ApplyRows(string[] rows)
        {
            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                {
                    int paint = InfoDemoPaint.FromPatternChar(rows[row][column]);
                    if (paint != InfoDemoPaint.NONE)
                    {
                        _boardModel.Occupy(ToGrid(row, column), paint);
                    }
                }
            }
        }

        /// <summary>Occupies the real board exactly where the demo's sampled board has a block, in its colour.</summary>
        private void ApplyBoard(InfoDemoElementState[] states)
        {
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    int paint = states[InfoDemoLayout.BoardBlockId(row, column)].Paint;
                    if (paint != InfoDemoPaint.NONE)
                    {
                        _boardModel.Occupy(ToGrid(row, column), paint);
                    }
                }
            }
        }

        /// <summary>The demo's final occupancy is exactly the real board's, except for a reward cell the real
        /// clear spawned on a cell it had just emptied — see <c>InfoDemoLineClearObjectiveTests</c>.</summary>
        private static void AssertBoardMatches(InfoDemoElementState[] states, Board board, string context = "")
        {
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    SpecialCellKind spawned = board.GetSpecialKind(ToGrid(row, column));
                    bool isReward = spawned == SpecialCellKind.ExplosiveCore || spawned == SpecialCellKind.Vortex;
                    bool demoOccupied = states[InfoDemoLayout.BoardBlockId(row, column)].Paint != InfoDemoPaint.NONE;
                    if (isReward && !demoOccupied)
                    {
                        continue;
                    }

                    Assert.AreEqual(board.IsOccupied(ToGrid(row, column)), demoOccupied, $"{context} ({row},{column}) vs the real board");
                }
            }
        }

        /// <summary>Demo (row, column) — rows counting down from the top — as a board position, whose Y counts
        /// up from the bottom.</summary>
        private static GridPosition ToGrid(int row, int column) => new GridPosition(column, Board.SIZE - 1 - row);
    }
}
