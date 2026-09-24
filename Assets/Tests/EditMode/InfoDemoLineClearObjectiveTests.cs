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
    /// The line-clear objective card demos (issue #452): the catalog mapping — one cached demo per
    /// parameter value the demo can draw, null for any other — and, for each demo, that what it shows is
    /// what the real objective engine credits. The demo's starting board is rebuilt on a real
    /// <see cref="BoardSystem"/>, the demo's move is played through the game's own entry point
    /// (<see cref="BoardSystem.TryPlacePiece"/>, or <see cref="PowerUpClearResolver.ResolveBombClear"/> for
    /// the Bomb), the result is fed to a real <see cref="ObjectiveSystem"/>, and the objective must advance
    /// exactly as the demo's chip does while the demo ends on exactly the real board.
    /// </summary>
    public class InfoDemoLineClearObjectiveTests
    {
        private const float TOLERANCE = 0.0001f;

        /// <summary>After every placement demo's clear has settled and before its loop fade-out starts.</summary>
        private const float AFTER_EFFECT = 3.5f;

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;
        private TestMessageBroker<PowerUpAppliedMessage> _powerUpAppliedBroker;
        private ObjectiveModel _objectiveModel;
        private BoardSystem _boardSystem;
        private ObjectiveSystem _objectiveSystem;

        [SetUp]
        public void CreateSystems()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
            _powerUpAppliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            _objectiveModel = new ObjectiveModel();

            // Deliberately unstarted, as InfoDemoSpecialPieceTests is: StartNewRun would draw over the board
            // and dock each test lays out by hand.
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
                _powerUpAppliedBroker,
                new TestMessageBroker<ObjectiveProgressChangedMessage>(),
                new TestMessageBroker<ObjectiveCompletedMessage>());
        }

        [TearDown]
        public void DisposeSystems() => _objectiveSystem.Dispose();

        // ---------------------------------------------------------------- catalog

        [Test]
        public void AtLeast_HasOneCachedDemoPerSupportedCount_AndNoneOutsideIt()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();
            HashSet<InfoDemoTimeline> seen = new HashSet<InfoDemoTimeline>();

            for (int lineCount = AtLeastLineClearInfoDemo.MIN_LINE_COUNT;
                lineCount <= AtLeastLineClearInfoDemo.MAX_LINE_COUNT;
                lineCount++)
            {
                InfoDemoTimeline first = catalog.FindObjective(LineObjective(ObjectiveType.AtLeastLineClear, lineCount));
                Assert.IsNotNull(first, $"N={lineCount}");
                Assert.AreSame(first, catalog.FindObjective(LineObjective(ObjectiveType.AtLeastLineClear, lineCount)));
                Assert.IsTrue(seen.Add(first), $"N={lineCount} has its own demo");
                Assert.AreEqual(AtLeastLineClearInfoDemo.LOOP_DURATION, first.Duration, TOLERANCE);
            }

            Assert.AreEqual(5, AtLeastLineClearInfoDemo.MAX_LINE_COUNT, "a 1x5 is the longest catalog line");
            Assert.IsNull(catalog.FindObjective(LineObjective(ObjectiveType.AtLeastLineClear, 1)));
            Assert.IsNull(catalog.FindObjective(LineObjective(ObjectiveType.AtLeastLineClear, 6)));
            Assert.AreNotSame(
                catalog.FindObjective(LineObjective(ObjectiveType.SimultaneousLineClear, 3)),
                catalog.FindObjective(LineObjective(ObjectiveType.AtLeastLineClear, 3)),
                "an exact-N card and an at-least-N card never share a demo");
        }

        [Test]
        public void AtLeast_ChipReadsAtLeastN_NotExactlyN()
        {
            InfoDemoTimeline timeline = AtLeastLineClearInfoDemo.Build(3);
            int caption = FindLabel(timeline, LocalizationKeys.INFO_POPUP_DEMO_CHIP_AT_LEAST);

            Assert.GreaterOrEqual(caption, 0);
            Assert.AreEqual("3", timeline.GetElement(caption).LabelArgument);
            Assert.AreEqual(-1, FindLabel(timeline, LocalizationKeys.INFO_POPUP_DEMO_CHIP_EXACTLY));
        }

        [Test]
        public void CrossAndBomb_HaveOneCachedDemoEach()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            InfoDemoTimeline cross = catalog.FindObjective(PlainObjective(ObjectiveType.RowAndColumnCrossClear));
            InfoDemoTimeline bomb = catalog.FindObjective(PlainObjective(ObjectiveType.BombInducedLineClear));

            Assert.IsNotNull(cross);
            Assert.IsNotNull(bomb);
            Assert.AreNotSame(cross, bomb);
            Assert.AreSame(cross, catalog.FindObjective(PlainObjective(ObjectiveType.RowAndColumnCrossClear)));
            Assert.AreSame(bomb, catalog.FindObjective(PlainObjective(ObjectiveType.BombInducedLineClear)));
            Assert.AreEqual(RowAndColumnCrossClearInfoDemo.LOOP_DURATION, cross.Duration, TOLERANCE);
            Assert.AreEqual(BombInducedLineClearInfoDemo.LOOP_DURATION, bomb.Duration, TOLERANCE);
        }

        [Test]
        public void PieceId_HasOneCachedDemoPerCatalogPiece_AndNoneForAnUnknownId()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            InfoDemoTimeline square = catalog.FindObjective(PieceObjective("square_3x3"));
            InfoDemoTimeline line = catalog.FindObjective(PieceObjective("line_h5"));

            Assert.IsNotNull(square);
            Assert.IsNotNull(line);
            Assert.AreNotSame(square, line);
            Assert.AreSame(square, catalog.FindObjective(PieceObjective("square_3x3")));
            Assert.AreEqual(PieceIdLineClearInfoDemo.LOOP_DURATION, square.Duration, TOLERANCE);

            Assert.IsNull(catalog.FindObjective(PieceObjective("not_a_piece")));
            Assert.IsNull(catalog.FindObjective(PieceObjective(null)));
            Assert.IsNull(catalog.FindObjective(PieceObjective(string.Empty)));
        }

        [Test]
        public void PieceId_ChipShowsTheRequiredPiecesShape_InsteadOfACaption()
        {
            InfoDemoTimeline timeline = PieceIdLineClearInfoDemo.Build("square_3x3");
            Vector2Int[] square = PieceIdLineClearInfoDemo.DemoShape(PieceIdLineClearInfoDemo.FindPiece("square_3x3"));

            // The chip's glyph is the first piece element (built before the tray), a miniature of the piece.
            int glyph = FirstPiece(timeline);
            InfoDemoElementState[] states = Sample(timeline, 0f);
            Assert.AreEqual(9, timeline.GetElement(glyph).Shape.Length);
            Assert.AreEqual(InfoDemoChoreography.ChipGlyphScale(square), states[glyph].Scale, TOLERANCE);
            Assert.Less(states[glyph].Scale, InfoDemoLayout.TRAY_PIECE_SCALE, "smaller than a tray piece");
            Assert.AreEqual(1f, states[glyph].Alpha, TOLERANCE);
        }

        [Test]
        public void RollingWindow_HasOneCachedDemoPerWholeSecond_AndNoneForAWindowTheDemoOutruns()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            InfoDemoTimeline fifteen = catalog.FindObjective(WindowObjective(15f));
            Assert.IsNotNull(fifteen);
            Assert.AreSame(fifteen, catalog.FindObjective(WindowObjective(15.2f)), "rounded as the card text is");
            Assert.AreNotSame(fifteen, catalog.FindObjective(WindowObjective(20f)));
            Assert.AreEqual(RollingLineClearWindowInfoDemo.LOOP_DURATION, fifteen.Duration, TOLERANCE);

            Assert.IsNotNull(catalog.FindObjective(WindowObjective(RollingLineClearWindowInfoDemo.MIN_WINDOW_SECONDS)));
            Assert.IsNull(catalog.FindObjective(WindowObjective(RollingLineClearWindowInfoDemo.MIN_WINDOW_SECONDS - 1)));
        }

        [Test]
        public void RollingWindow_ChipCaptionIsTheObjectivesOwnWindow()
        {
            InfoDemoTimeline timeline = new InfoDemoCatalog().FindObjective(WindowObjective(12f));
            int caption = FindLabel(timeline, LocalizationKeys.INFO_POPUP_DEMO_CHIP_WINDOW);

            Assert.GreaterOrEqual(caption, 0);
            Assert.AreEqual("12", timeline.GetElement(caption).LabelArgument);
        }

        // ---------------------------------------------------------------- At Least

        [Test]
        public void AtLeastDemo_ItsPlacementClearsNLines_AndTheRealObjectiveAdvancesOnce()
        {
            for (int lineCount = AtLeastLineClearInfoDemo.MIN_LINE_COUNT;
                lineCount <= AtLeastLineClearInfoDemo.MAX_LINE_COUNT;
                lineCount++)
            {
                CreateSystems();
                InfoDemoTimeline timeline = AtLeastLineClearInfoDemo.Build(lineCount);
                ApplyBoard(Sample(timeline, 0f));

                ObjectiveProgress atLeast = Track(LineObjective(ObjectiveType.AtLeastLineClear, lineCount));
                ObjectiveProgress oneMore = new ObjectiveProgress(LineObjective(ObjectiveType.AtLeastLineClear, lineCount + 1));
                _objectiveModel.SetObjectives(new[] { atLeast, oneMore });

                // The demo's vertical 1xN, dropped into the gap column with its foot on row 7.
                _trayModel.SetSlot(0, FindPiece("line_v" + lineCount), 4);
                Assert.IsTrue(_boardSystem.TryPlacePiece(
                    0, ToGrid(InfoDemoLayout.BOARD_SIZE - 1, SimultaneousLineClearInfoDemo.GAP_COLUMN)), $"N={lineCount}");

                PiecePlacedMessage placed = LastPlacement();
                Assert.AreEqual(lineCount, placed.LinesCleared, $"N={lineCount} lines");
                Assert.AreEqual(1, atLeast.CurrentValue, $"N={lineCount} credited, as the chip's 0/1 → 1/1");
                Assert.IsTrue(atLeast.IsComplete);
                Assert.AreEqual(0, oneMore.CurrentValue, $"N={lineCount}: it is N lines, not more");

                AssertBoardMatches(Sample(timeline, AFTER_EFFECT), _boardModel.Board);
            }
        }

        // ---------------------------------------------------------------- Row and Column Cross

        [Test]
        public void CrossDemo_ItsSingleClearsARowAndAColumnAtOnce_AndTheRealObjectiveAdvances()
        {
            ApplyRows(RowAndColumnCrossClearInfoDemo.Rows);
            ObjectiveProgress cross = Track(PlainObjective(ObjectiveType.RowAndColumnCrossClear));

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 4);
            Assert.IsTrue(_boardSystem.TryPlacePiece(
                0, ToGrid(RowAndColumnCrossClearInfoDemo.CROSS_ROW, RowAndColumnCrossClearInfoDemo.CROSS_COLUMN)));

            PiecePlacedMessage placed = LastPlacement();
            Assert.AreEqual(1, placed.RowsCleared);
            Assert.AreEqual(1, placed.ColumnsCleared);
            Assert.AreEqual(1, cross.CurrentValue);
            Assert.IsTrue(cross.IsComplete);

            InfoDemoElementState[] after = Sample(RowAndColumnCrossClearInfoDemo.Build(), AFTER_EFFECT);
            AssertBoardMatches(after, _boardModel.Board);
            for (int index = 0; index < InfoDemoLayout.BOARD_SIZE; index++)
            {
                Assert.AreEqual(InfoDemoPaint.NONE, after[InfoDemoLayout.BoardBlockId(RowAndColumnCrossClearInfoDemo.CROSS_ROW, index)].Paint);
                Assert.AreEqual(InfoDemoPaint.NONE, after[InfoDemoLayout.BoardBlockId(index, RowAndColumnCrossClearInfoDemo.CROSS_COLUMN)].Paint);
            }
        }

        [Test]
        public void CrossDemo_ClearsTheSharedCornerCellOnce_BeforeTheRestOfEitherLine()
        {
            InfoDemoTimeline timeline = RowAndColumnCrossClearInfoDemo.Build();
            int corner = InfoDemoLayout.BoardBlockId(RowAndColumnCrossClearInfoDemo.CROSS_ROW, RowAndColumnCrossClearInfoDemo.CROSS_COLUMN);
            int rowEnd = InfoDemoLayout.BoardBlockId(RowAndColumnCrossClearInfoDemo.CROSS_ROW, InfoDemoLayout.BOARD_SIZE - 1);
            int columnEnd = InfoDemoLayout.BoardBlockId(0, RowAndColumnCrossClearInfoDemo.CROSS_COLUMN);

            // Just after the crossing cell has gone, both far ends are still there — the arms run outward.
            float cornerGone = InfoDemoChoreography.ClearCellShrinkStart(RowAndColumnCrossClearInfoDemo.CLEAR_START, 0)
                + InfoDemoChoreography.CLEAR_SHRINK_DURATION + 0.01f;
            InfoDemoElementState[] states = Sample(timeline, cornerGone);
            Assert.AreEqual(InfoDemoPaint.NONE, states[corner].Paint);
            Assert.AreNotEqual(InfoDemoPaint.NONE, states[rowEnd].Paint);
            Assert.AreNotEqual(InfoDemoPaint.NONE, states[columnEnd].Paint);

            // The two arms move in step: both far ends go at the same moment.
            Assert.AreEqual(states[rowEnd].Alpha, states[columnEnd].Alpha, TOLERANCE);
        }

        // ---------------------------------------------------------------- Bomb-induced

        [Test]
        public void BombDemo_TheBlastEmptiesExactlyRow2_AndTheRealObjectiveAdvancesOffTheBomb()
        {
            ApplyRows(BombInducedLineClearInfoDemo.Rows);
            ObjectiveProgress bombLine = Track(PlainObjective(ObjectiveType.BombInducedLineClear));

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(
                _boardModel.Board,
                ToGrid(BombInducedLineClearInfoDemo.TARGET_ROW, BombInducedLineClearInfoDemo.TARGET_COLUMN));

            Assert.AreEqual(BombInducedLineClearInfoDemo.BlastCells().Length, result.ClearedCellCount);
            Assert.AreEqual(1, result.EmptiedRows.Count, "one row emptied");
            Assert.AreEqual(ToGrid(BombInducedLineClearInfoDemo.EMPTIED_ROW, 0).Y, result.EmptiedRows[0], "row 2");
            Assert.AreEqual(0, result.EmptiedColumns.Count, "every blasted column keeps a block outside the 3x3");

            // Exactly what PowerUpSystem.TryApplyBomb publishes for this result.
            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Bomb, result.ClearedCellCount, clearedLineCount: 0, emptiedLineCount: result.EmptiedLineCount));

            Assert.AreEqual(1, bombLine.CurrentValue, "as the chip's 0/1 → 1/1");
            Assert.IsTrue(bombLine.IsComplete);

            InfoDemoTimeline timeline = BombInducedLineClearInfoDemo.Build(out InfoDemoProgressChip chip, out _);
            InfoDemoElementState[] after = Sample(timeline, BombInducedLineClearInfoDemo.CHIP_ADVANCE_TIME + 1f);
            AssertBoardMatches(after, _boardModel.Board);
            Assert.AreEqual(1f, after[chip.CounterLabelId(1)].Alpha, TOLERANCE);
            Assert.AreEqual(1f, after[chip.CheckId].Alpha, TOLERANCE);
        }

        [Test]
        public void BombObjective_IsNotAdvancedByAFullLineClearedByAPlacement()
        {
            // The Cross demo's placement clears a full row and a full column — the opposite condition.
            ApplyRows(RowAndColumnCrossClearInfoDemo.Rows);
            ObjectiveProgress bombLine = Track(PlainObjective(ObjectiveType.BombInducedLineClear));

            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 4);
            Assert.IsTrue(_boardSystem.TryPlacePiece(0, ToGrid(7, 0)));

            Assert.AreEqual(2, LastPlacement().LinesCleared);
            Assert.AreEqual(0, bombLine.CurrentValue);
        }

        [Test]
        public void BombDemo_ShowsTheRealBombIcon_BesideTheChip()
        {
            InfoDemoTimeline timeline = BombInducedLineClearInfoDemo.Build(out InfoDemoProgressChip chip, out InfoDemoPowerUpButton button);
            InfoDemoElement icon = timeline.GetElement(button.IconId);

            Assert.AreEqual(InfoDemoSprite.PowerUpIcon, icon.Sprite);
            Assert.AreEqual((int)PowerUpKind.Bomb, icon.SpriteParameter);

            // The button sits right of the chip, clear of it.
            InfoDemoElementState[] states = Sample(timeline, 0f);
            float chipRight = states[chip.PanelId].Position.x + (timeline.GetElement(chip.PanelId).Size.x * 0.5f);
            float plateLeft = states[button.PlateId].Position.x - (timeline.GetElement(button.PlateId).Size.x * 0.5f);
            Assert.Greater(plateLeft, chipRight);
        }

        // ---------------------------------------------------------------- Piece Id

        [Test]
        public void PieceIdDemo_EveryCatalogPiece_LandsLegally_ClearsItsRows_AndTheRealObjectiveAdvances()
        {
            IReadOnlyList<Piece> pieces = PieceCatalog.AllPieces;
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                Piece piece = pieces[pieceIndex];
                CreateSystems();

                InfoDemoTimeline timeline = PieceIdLineClearInfoDemo.Build(piece.Id);
                Vector2Int[] shape = PieceIdLineClearInfoDemo.DemoShape(piece);
                ApplyBoard(Sample(timeline, 0f));

                ObjectiveProgress wanted = new ObjectiveProgress(PieceObjective(piece.Id));
                ObjectiveProgress other = new ObjectiveProgress(PieceObjective(piece.Id == "line_h2" ? "line_v2" : "line_h2"));
                _objectiveModel.SetObjectives(new[] { wanted, other });

                // The demo lands the footprint's top-left at (LandRow, LAND_COLUMN); the catalog anchor is the
                // offset origin, so it sits at the footprint's bottom-left less the smallest offsets.
                int minX = int.MaxValue;
                int minY = int.MaxValue;
                for (int offsetIndex = 0; offsetIndex < piece.Offsets.Count; offsetIndex++)
                {
                    minX = Mathf.Min(minX, piece.Offsets[offsetIndex].X);
                    minY = Mathf.Min(minY, piece.Offsets[offsetIndex].Y);
                }

                GridPosition footprintBottomLeft = ToGrid(InfoDemoLayout.BOARD_SIZE - 1, PieceIdLineClearInfoDemo.LAND_COLUMN);
                GridPosition anchor = new GridPosition(footprintBottomLeft.X - minX, footprintBottomLeft.Y - minY);

                _trayModel.SetSlot(0, piece, 4);
                Assert.IsTrue(_boardSystem.TryPlacePiece(0, anchor), piece.Id);

                PiecePlacedMessage placed = LastPlacement();
                Assert.AreEqual(PieceIdLineClearInfoDemo.RowSpan(shape), placed.RowsCleared, piece.Id + " clears every row it spans");
                Assert.AreEqual(0, placed.ColumnsCleared, piece.Id + " clears no column");
                Assert.AreEqual(1, wanted.CurrentValue, piece.Id + " credited");
                Assert.AreEqual(0, other.CurrentValue, piece.Id + " is not another piece");

                AssertBoardMatches(Sample(timeline, AFTER_EFFECT), _boardModel.Board, piece.Id);
            }
        }

        [Test]
        public void PieceIdDemo_Square3x3_MatchesTheAuthoredBoard()
        {
            string[] rows = PieceIdLineClearInfoDemo.Rows(
                PieceIdLineClearInfoDemo.DemoShape(PieceIdLineClearInfoDemo.FindPiece("square_3x3")));

            CollectionAssert.AreEqual(
                new[] { "........", "........", "....g...", "...uu...", ".....b..", "...gbbuu", "...uuggp", "...bbgpu" },
                rows);
        }

        // ---------------------------------------------------------------- Rolling window

        [Test]
        public void RollingDemo_EachSingleClearsOneRow_AndTheRealObjectiveSumsThemToThree()
        {
            ApplyRows(RollingLineClearWindowInfoDemo.Rows);
            ObjectiveProgress burst = Track(WindowObjective(15f, RollingLineClearWindowInfoDemo.CLEAR_COUNT));

            for (int dropIndex = 0; dropIndex < RollingLineClearWindowInfoDemo.CLEAR_COUNT; dropIndex++)
            {
                Vector2Int cell = RollingLineClearWindowInfoDemo.LandCells[dropIndex];
                _trayModel.SetSlot(0, PieceCatalog.SingleCell, 4);
                Assert.IsTrue(_boardSystem.TryPlacePiece(0, ToGrid(cell.y, cell.x)), $"drop {dropIndex}");

                Assert.AreEqual(1, LastPlacement().LinesCleared, $"drop {dropIndex} clears one row");
                Assert.AreEqual(dropIndex + 1, burst.CurrentValue, $"after drop {dropIndex}");
            }

            Assert.IsTrue(burst.IsComplete);
            AssertBoardMatches(Sample(RollingLineClearWindowInfoDemo.Build(15f), 4.6f), _boardModel.Board);
        }

        [Test]
        public void RollingDemo_ChipTicksWithEachClear()
        {
            InfoDemoTimeline timeline = RollingLineClearWindowInfoDemo.Build(15f, out InfoDemoProgressChip chip);

            Assert.AreEqual(1f, Sample(timeline, 0.2f)[chip.CounterLabelId(0)].Alpha, TOLERANCE);
            for (int dropIndex = 0; dropIndex < RollingLineClearWindowInfoDemo.CLEAR_COUNT; dropIndex++)
            {
                float settled = RollingLineClearWindowInfoDemo.ClearTime(dropIndex) + RollingLineClearWindowInfoDemo.CHIP_DELAY + 0.5f;
                InfoDemoElementState[] states = Sample(timeline, settled);
                Assert.AreEqual(1f, states[chip.CounterLabelId(dropIndex + 1)].Alpha, TOLERANCE, $"{dropIndex + 1}/3");
                Assert.AreEqual(0f, states[chip.CounterLabelId(dropIndex)].Alpha, TOLERANCE);
            }

            Assert.AreEqual(1f, Sample(timeline, 4.8f)[chip.CheckId].Alpha, TOLERANCE, "done");
            Assert.AreEqual(0f, Sample(timeline, 2.0f)[chip.CheckId].Alpha, TOLERANCE, "not yet at 1/3");
        }

        [Test]
        public void RollingDemo_ItsClearTimings_FitTheShortestSupportedWindow_AndNoShorterOne()
        {
            // The real trailing-window rule, fed the demo's own placement times: every supported window
            // holds all three clears; one second less and the first clear has already aged out.
            Assert.AreEqual(3, SumAtDemoTimes(RollingLineClearWindowInfoDemo.MIN_WINDOW_SECONDS));
            Assert.AreEqual(3, SumAtDemoTimes(15f));
            Assert.Less(SumAtDemoTimes(RollingLineClearWindowInfoDemo.MIN_WINDOW_SECONDS - 1), 3);
        }


        // ---------------------------------------------------------------- helpers

        private static int SumAtDemoTimes(float windowSeconds)
        {
            ObjectiveProgress burst = new ObjectiveProgress(new ObjectiveDefinition(
                "burst", ObjectiveType.RollingLineClearWindow, ObjectiveScope.PerRun, 99, windowSeconds: windowSeconds));
            for (int dropIndex = 0; dropIndex < RollingLineClearWindowInfoDemo.CLEAR_COUNT; dropIndex++)
            {
                burst.ApplyPlacement(new ObjectivePlacementContext(
                    1, 1, 0, PieceFamily.Single, PieceCatalog.SingleCell.Id, 0, false, 0, 0, false, false, false,
                    RollingLineClearWindowInfoDemo.LandTime(dropIndex), 0));
            }

            return burst.CurrentValue;
        }

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

        private static ObjectiveDefinition LineObjective(ObjectiveType type, int requiredLineCount)
            => new ObjectiveDefinition("test", type, ObjectiveScope.PerRun, 1, requiredLineCount);

        private static ObjectiveDefinition PlainObjective(ObjectiveType type)
            => new ObjectiveDefinition("test", type, ObjectiveScope.PerRun, 1);

        private static ObjectiveDefinition PieceObjective(string pieceId)
            => new ObjectiveDefinition("test", ObjectiveType.PieceIdLineClear, ObjectiveScope.PerRun, 1, requiredPieceId: pieceId);

        private static ObjectiveDefinition WindowObjective(float windowSeconds, int target = 6)
            => new ObjectiveDefinition(
                "test", ObjectiveType.RollingLineClearWindow, ObjectiveScope.PerRun, target, windowSeconds: windowSeconds);

        private static Piece FindPiece(string id)
        {
            Piece piece = PieceIdLineClearInfoDemo.FindPiece(id);
            Assert.IsNotNull(piece, id);
            return piece;
        }

        private static InfoDemoElementState[] Sample(InfoDemoTimeline timeline, float time)
        {
            InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];
            timeline.Evaluate(time, states);
            return states;
        }

        private static int FindLabel(InfoDemoTimeline timeline, string key)
        {
            for (int elementId = 0; elementId < timeline.ElementCount; elementId++)
            {
                InfoDemoElement element = timeline.GetElement(elementId);
                if (element.Kind == InfoDemoElementKind.Label && element.LabelKey == key)
                {
                    return elementId;
                }
            }

            return -1;
        }

        private static int FirstPiece(InfoDemoTimeline timeline)
        {
            for (int elementId = 0; elementId < timeline.ElementCount; elementId++)
            {
                if (timeline.GetElement(elementId).Kind == InfoDemoElementKind.Piece)
                {
                    return elementId;
                }
            }

            Assert.Fail("No piece element.");
            return -1;
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

        /// <summary>Occupies the real board exactly where the demo's sampled board has a block.</summary>
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

        /// <summary>
        /// The demo's final occupancy is exactly the real board's — except for a reward cell the real clear
        /// spawned on a cell it had just emptied (an Explosive Core after a cross-clear, a Vortex once the
        /// run's cleared-line total reaches 5). Those are the special cells' own mechanics, shown by their
        /// own demos; an objective demo shows the objective, so the demo leaves that cell empty. (A Perfect
        /// Match Vortex converts an occupied block in place, so it never changes occupancy.)
        /// </summary>
        private static void AssertBoardMatches(InfoDemoElementState[] states, Board board, string context = "")
        {
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    SpecialCellKind spawned = board.GetSpecialKind(ToGrid(row, column));
                    bool isReward = spawned == SpecialCellKind.ExplosiveCore || spawned == SpecialCellKind.Vortex;
                    if (isReward && states[InfoDemoLayout.BoardBlockId(row, column)].Paint == InfoDemoPaint.NONE)
                    {
                        continue;
                    }

                    bool demoOccupied = states[InfoDemoLayout.BoardBlockId(row, column)].Paint != InfoDemoPaint.NONE;
                    Assert.AreEqual(board.IsOccupied(ToGrid(row, column)), demoOccupied, $"{context} ({row},{column}) vs the real board");
                }
            }
        }

        /// <summary>Demo (row, column) — rows counting down from the top — as a board position, whose Y
        /// counts up from the bottom.</summary>
        private static GridPosition ToGrid(int row, int column) => new GridPosition(column, Board.SIZE - 1 - row);
    }
}
