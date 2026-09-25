using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
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
    /// The counter / streak objective card demos (issue #454): the catalog mapping — one cached demo per
    /// parameter the demo can draw, and no demo for one it cannot — and, for each demo, that what it shows is
    /// what the real game does. The demo's board is rebuilt on a real <see cref="BoardSystem"/>, its moves are
    /// played through <see cref="BoardSystem.TryPlacePiece"/> (or, for Reroll Save, the real
    /// <see cref="BoardSystem.TryRerollTray"/>), scored by a real <see cref="ScoreSystem"/> with every production
    /// score rule, and fed to a real <see cref="ObjectiveSystem"/>: the objective must advance exactly as the
    /// demo's chip does, and the demo must end on exactly the real board.
    /// </summary>
    public class InfoDemoCounterObjectiveTests
    {
        private const float TOLERANCE = 0.0001f;
        private const string SHIPPED_CATALOG_PATH = "Assets/Content/Levels/LevelCatalog.asset";

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private ScoreModel _scoreModel;
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;
        private TestMessageBroker<ScoreChangedMessage> _scoreChangedBroker;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private TestMessageBroker<PowerUpAppliedMessage> _powerUpAppliedBroker;
        private ObjectiveModel _objectiveModel;
        private BoardSystem _boardSystem;
        private ScoreSystem _scoreSystem;
        private ObjectiveSystem _objectiveSystem;

        [SetUp]
        public void CreateSystems()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _scoreModel = new ScoreModel();
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
            _scoreChangedBroker = new TestMessageBroker<ScoreChangedMessage>();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _powerUpAppliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            _objectiveModel = new ObjectiveModel();

            // Deliberately unstarted, as the other objective demo tests are: StartNewRun would draw over the
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

            // Path mode, so the scoring never writes the persisted Endless high score.
            GameModeModel gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Path;

            // Every production score rule (GameLifetimeScope registers the same six). Built before the
            // ObjectiveSystem, so a placement's score is published before the objectives read it — the order the
            // live game relies on (ObjectiveSystem caches the latest ScoreChangedMessage).
            _scoreSystem = new ScoreSystem(
                _scoreModel,
                new GameModeSystem(gameModeModel, _boardSystem),
                new DoubleMultiplierModel(),
                new List<IScoreRule>
                {
                    new PlacementScoreRule(),
                    new LineClearScoreRule(),
                    new MonochromeScoreRule(),
                    new MultiClearStreakScoreRule(),
                    new CumulativeMultiClearMilestoneScoreRule(),
                    new BoardWipeScoreRule(),
                },
                _piecePlacedBroker,
                _runStartedBroker,
                _scoreChangedBroker,
                new TestMessageBroker<NewRecordMessage>(),
                new TestMessageBroker<BonusScoredMessage>());

            _objectiveSystem = new ObjectiveSystem(
                _objectiveModel,
                new RunPauseModel(),
                _piecePlacedBroker,
                _runStartedBroker,
                _scoreChangedBroker,
                _powerUpAppliedBroker,
                new TestMessageBroker<ObjectiveProgressChangedMessage>(),
                new TestMessageBroker<ObjectiveCompletedMessage>());

            // A fresh run: the score, the streaks and the run clock all start from zero.
            _runStartedBroker.Publish(new RunStartedMessage());
        }

        [TearDown]
        public void DisposeSystems()
        {
            _objectiveSystem.Dispose();
            _scoreSystem.Dispose();
        }

        // ---------------------------------------------------------------- catalog

        [Test]
        public void Catalog_CachesOneDemoPerParameter_AndFallsBackForValuesItCannotDraw()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            // Piece Family: per (family, target).
            InfoDemoTimeline corner5 = catalog.FindObjective(Family(PieceFamily.Corner, 5));
            Assert.IsNotNull(corner5);
            Assert.AreSame(corner5, catalog.FindObjective(Family(PieceFamily.Corner, 5)));
            Assert.AreNotSame(corner5, catalog.FindObjective(Family(PieceFamily.Corner, 6)));
            Assert.AreNotSame(corner5, catalog.FindObjective(Family(PieceFamily.TShape, 5)));
            Assert.IsNull(catalog.FindObjective(Family(PieceFamily.Corner, InfoDemoCountedPlacements.MAX_TARGET + 1)));
            Assert.IsNull(catalog.FindObjective(Family((PieceFamily)99, 5)));

            // Piece Id Count: per (id, target); an id outside the catalog has none.
            InfoDemoTimeline square = catalog.FindObjective(PieceId("square_3x3", 3));
            Assert.IsNotNull(square);
            Assert.AreSame(square, catalog.FindObjective(PieceId("square_3x3", 3)));
            Assert.AreNotSame(square, catalog.FindObjective(PieceId("line_h5", 3)));
            Assert.IsNull(catalog.FindObjective(PieceId("not_a_piece", 3)));
            Assert.IsNull(catalog.FindObjective(PieceId(null, 3)));

            // Score In Run: per target.
            InfoDemoTimeline score = catalog.FindObjective(Plain(ObjectiveType.ScoreInRun, 3000));
            Assert.IsNotNull(score);
            Assert.AreSame(score, catalog.FindObjective(Plain(ObjectiveType.ScoreInRun, 3000)));
            Assert.AreNotSame(score, catalog.FindObjective(Plain(ObjectiveType.ScoreInRun, 4000)));
            Assert.IsNull(catalog.FindObjective(Plain(ObjectiveType.ScoreInRun, ScoreInRunInfoDemo.MAX_TARGET + 1)));

            // Early Score Rush: per (whole seconds, target); a deadline the move cannot beat has none.
            InfoDemoTimeline rush = catalog.FindObjective(Rush(15f, 3000));
            Assert.IsNotNull(rush);
            Assert.AreSame(rush, catalog.FindObjective(Rush(15.2f, 3000)), "rounded like the card text");
            Assert.AreNotSame(rush, catalog.FindObjective(Rush(20f, 3000)));
            Assert.IsNull(catalog.FindObjective(Rush(EarlyScoreRushInfoDemo.MIN_WINDOW_SECONDS - 1, 3000)));

            // Streak Threshold: per target, 1 to 5.
            for (int target = StreakThresholdInfoDemo.MIN_TARGET; target <= StreakThresholdInfoDemo.MAX_TARGET; target++)
            {
                InfoDemoTimeline streak = catalog.FindObjective(Plain(ObjectiveType.StreakThreshold, target));
                Assert.IsNotNull(streak, target.ToString());
                Assert.AreSame(streak, catalog.FindObjective(Plain(ObjectiveType.StreakThreshold, target)));
            }

            Assert.IsNull(catalog.FindObjective(Plain(ObjectiveType.StreakThreshold, StreakThresholdInfoDemo.MAX_TARGET + 1)));

            // Clutch Recovery: per target, for any threshold the demo's board reaches.
            InfoDemoTimeline clutch = catalog.FindObjective(Clutch(52, 1));
            Assert.IsNotNull(clutch);
            Assert.AreSame(clutch, catalog.FindObjective(Clutch(ClutchRecoveryClearInfoDemo.OccupiedAfterLanding, 1)));
            Assert.IsNull(catalog.FindObjective(Clutch(ClutchRecoveryClearInfoDemo.OccupiedAfterLanding + 1, 1)));

            // Reroll Save: per target.
            InfoDemoTimeline reroll = catalog.FindObjective(Plain(ObjectiveType.RerollSave, 1));
            Assert.IsNotNull(reroll);
            Assert.AreSame(reroll, catalog.FindObjective(Plain(ObjectiveType.RerollSave, 1)));
            Assert.IsNull(catalog.FindObjective(Plain(ObjectiveType.RerollSave, RerollSaveInfoDemo.MAX_TARGET + 1)));
        }

        [Test]
        public void Catalog_EveryObjectiveTypeTheLevelCatalogUses_HasADemoForItsAuthoredValues()
        {
            LevelCatalog levels = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelCatalog>(SHIPPED_CATALOG_PATH);
            Assert.IsNotNull(levels, "LevelCatalog.asset");

            InfoDemoCatalog catalog = new InfoDemoCatalog();
            int checkedCount = 0;
            for (int levelIndex = 0; levelIndex < levels.Levels.Count; levelIndex++)
            {
                ObjectiveDefinition definition = levels.Levels[levelIndex].ToObjectiveDefinition();
                if (definition.Type != ObjectiveType.PieceFamilyCount && definition.Type != ObjectiveType.ScoreInRun)
                {
                    continue;
                }

                checkedCount++;
                Assert.IsNotNull(catalog.FindObjective(definition), $"level {levelIndex + 1}: {definition.Type} {definition.TargetValue}");
            }

            Assert.Greater(checkedCount, 0, "the level catalog uses Piece Family and Score In Run");
        }

        // ---------------------------------------------------------------- Piece Family

        [Test]
        public void PieceFamilyDemo_EveryFamily_BothMembersCountOnTheRealBoard_AndTheChipCountsToTheTarget()
        {
            for (int familyValue = 0; familyValue <= (int)PieceFamily.ZShape; familyValue++)
            {
                PieceFamily family = (PieceFamily)familyValue;
                ResetBoard();
                ApplyRows(PieceFamilyCountInfoDemo.Rows);
                ObjectiveProgress progress = Track(Family(family, 5));

                for (int placementIndex = 0; placementIndex < PieceFamilyCountInfoDemo.PLACEMENT_COUNT; placementIndex++)
                {
                    Piece member = FindPiece(PieceFamilyCountInfoDemo.MemberId(family, placementIndex));
                    Vector2Int land = PieceFamilyCountInfoDemo.LandCells[placementIndex];
                    Place(member, land.y, land.x);

                    PiecePlacedMessage placed = LastPlacement();
                    Assert.AreEqual(family, placed.PieceFamily, $"{family} member {placementIndex}");
                    Assert.AreEqual(0, placed.LinesCleared, $"{family} member {placementIndex} clears nothing");
                    Assert.AreEqual(placementIndex + 1, progress.CurrentValue, $"{family} after member {placementIndex}");
                }

                // The demo's chip: 3/5 → 5/5, and its end board is the real one.
                InfoDemoTimeline timeline = PieceFamilyCountInfoDemo.Build(family, 5, out InfoDemoProgressChip chip);
                Assert.AreEqual(5, chip.Value);
                Assert.IsTrue(chip.IsComplete);
                Assert.AreEqual(1f, Sample(timeline, 0.2f)[chip.CounterLabelId(3)].Alpha, TOLERANCE, $"{family} starts at 3/5");
                InfoDemoElementState[] end = Sample(timeline, 4.0f);
                Assert.AreEqual(1f, end[chip.CounterLabelId(5)].Alpha, TOLERANCE);
                AssertBoardMatches(end, _boardModel.Board, family.ToString());

                // The members are two different pieces (except for Single, which has one) of that family.
                Assert.AreEqual(family, PieceFamilyClassifier.Classify(PieceFamilyCountInfoDemo.MemberId(family, 0)));
                Assert.AreEqual(family, PieceFamilyClassifier.Classify(PieceFamilyCountInfoDemo.MemberId(family, 1)));
                if (family != PieceFamily.Single)
                {
                    Assert.AreNotEqual(PieceFamilyCountInfoDemo.MemberId(family, 0), PieceFamilyCountInfoDemo.MemberId(family, 1));
                }
            }
        }

        [Test]
        public void PieceFamily_TheTrayPieceOfAnotherFamily_DoesNotCount()
        {
            for (int familyValue = 0; familyValue <= (int)PieceFamily.ZShape; familyValue++)
            {
                PieceFamily family = (PieceFamily)familyValue;
                ResetBoard();
                ApplyRows(PieceFamilyCountInfoDemo.Rows);
                ObjectiveProgress progress = Track(Family(family, 5));

                Piece other = FindPiece(PieceFamilyCountInfoDemo.OtherId(family));
                Assert.AreNotEqual(family, PieceFamilyClassifier.Classify(other.Id));

                // The free 2x2 nook at rows 5-6, columns 6-7.
                Place(other, 5, 6);
                Assert.AreEqual(0, LastPlacement().LinesCleared);
                Assert.AreEqual(0, progress.CurrentValue, family.ToString());
            }
        }

        [Test]
        public void PieceFamilyChip_UnderTwo_StartsAtZero()
        {
            PieceFamilyCountInfoDemo.Build(PieceFamily.Square, 1, out InfoDemoProgressChip chip);
            Assert.AreEqual(1, chip.Value);
            Assert.IsTrue(chip.HasValue(0));
            Assert.IsFalse(chip.HasValue(2));
        }

        // ---------------------------------------------------------------- Piece Id Count

        [Test]
        public void PieceIdCountDemo_EveryCatalogPiece_BothCopiesCountOnTheRealBoard()
        {
            IReadOnlyList<Piece> pieces = PieceCatalog.AllPieces;
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                Piece piece = pieces[pieceIndex];
                ResetBoard();
                ApplyRows(PieceIdCountInfoDemo.Rows);
                ObjectiveProgress progress = Track(PieceId(piece.Id, 4));

                Vector2Int[] land = PieceIdCountInfoDemo.LandCells(PieceIdLineClearInfoDemo.DemoShape(piece));
                for (int placementIndex = 0; placementIndex < PieceIdCountInfoDemo.PLACEMENT_COUNT; placementIndex++)
                {
                    Place(piece, land[placementIndex].y, land[placementIndex].x);
                    Assert.AreEqual(0, LastPlacement().LinesCleared, $"{piece.Id} copy {placementIndex} clears nothing");
                    Assert.AreEqual(placementIndex + 1, progress.CurrentValue, $"{piece.Id} after copy {placementIndex}");
                }

                InfoDemoTimeline timeline = PieceIdCountInfoDemo.Build(piece.Id, 4, out InfoDemoProgressChip chip);
                Assert.AreEqual(4, chip.Value, piece.Id);
                AssertBoardMatches(Sample(timeline, 4.0f), _boardModel.Board, piece.Id);
                Assert.AreNotEqual(piece.Id, PieceIdCountInfoDemo.OtherId(piece.Id));
            }
        }

        [Test]
        public void PieceIdCount_AnotherPieceOfTheSameFamily_DoesNotCount()
        {
            ApplyRows(PieceIdCountInfoDemo.Rows);
            ObjectiveProgress progress = Track(PieceId("square_3x3", 2));

            Place(FindPiece("square_2x2"), 0, 0);

            Assert.AreEqual(PieceFamily.Square, LastPlacement().PieceFamily);
            Assert.AreEqual(0, progress.CurrentValue);
        }

        // ---------------------------------------------------------------- Score In Run

        [Test]
        public void ScoreInRunDemo_TheMoveScoresTheRealPoints_AndCrossesEachTarget()
        {
            Assert.AreEqual(183, ScoreInRunInfoDemo.Gain, "3 cells + 10 x 3 lines x 6");

            int[] targets = { 100, 1000, 3000, 12000 };
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                int target = targets[targetIndex];
                ResetBoard();
                ApplyRows(ScoreInRunInfoDemo.Rows);
                ObjectiveProgress progress = Track(Plain(ObjectiveType.ScoreInRun, target));

                // The run so far scored exactly the chip's start reading, with no streak going.
                int start = ScoreInRunInfoDemo.ChipStart(target);
                Assert.Less(start, target, $"{target}: starts short of the target");
                _scoreModel.Score.Value = start;

                Place(FindPiece("line_v3"), ScoreInRunInfoDemo.LAND_ROW, ScoreInRunInfoDemo.LAND_COLUMN);

                Assert.AreEqual(ScoreInRunInfoDemo.CLEARED_LINE_COUNT, LastPlacement().LinesCleared);
                Assert.AreEqual(0, LastPlacement().MonochromeLineCount, "no bonus rule pays on top");
                Assert.AreEqual(ScoreInRunInfoDemo.Gain, LastScore().Gained, $"{target}: the real rules' points");
                Assert.AreEqual(start + ScoreInRunInfoDemo.Gain, _scoreModel.Score.Value);
                Assert.AreEqual(ScoreInRunInfoDemo.ChipEnd(target), progress.CurrentValue, $"{target}: clamped to the target");
                Assert.AreEqual(target, progress.CurrentValue);
                Assert.IsTrue(progress.IsComplete);

                InfoDemoTimeline timeline = ScoreInRunInfoDemo.Build(target, out InfoDemoProgressChip chip);
                Assert.IsTrue(chip.IsComplete);
                Assert.AreEqual(1f, Sample(timeline, 0.2f)[chip.CounterLabelId(start)].Alpha, TOLERANCE);
                InfoDemoElementState[] end = Sample(timeline, 4.0f);
                Assert.AreEqual(1f, end[chip.CounterLabelId(target)].Alpha, TOLERANCE);
                Assert.AreEqual(1f, end[chip.CheckId].Alpha, TOLERANCE);
                AssertBoardMatches(end, _boardModel.Board, target.ToString());
            }
        }

        // ---------------------------------------------------------------- Early Score Rush

        [Test]
        public void EarlyScoreRushDemo_InsideTheDeadline_TheMoveCompletesTheObjective()
        {
            const int target = 3000;
            ApplyRows(ScoreInRunInfoDemo.Rows);
            ObjectiveProgress progress = Track(Rush(15f, target));
            _scoreModel.Score.Value = ScoreInRunInfoDemo.ChipStart(target);

            // The run clock started with the run a moment ago — well inside 15 s.
            Place(FindPiece("line_v3"), ScoreInRunInfoDemo.LAND_ROW, ScoreInRunInfoDemo.LAND_COLUMN);

            Assert.AreEqual(target, progress.CurrentValue);
            Assert.IsTrue(progress.IsComplete);

            InfoDemoTimeline timeline = EarlyScoreRushInfoDemo.Build(15, target, out InfoDemoProgressChip chip);
            Assert.IsTrue(chip.IsComplete);
            AssertBoardMatches(Sample(timeline, 4.0f), _boardModel.Board);

            // The chip reaches the target before even the shortest supported deadline runs out.
            Assert.Less(ScoreInRunInfoDemo.CHIP_ADVANCE_TIME, EarlyScoreRushInfoDemo.MIN_WINDOW_SECONDS);
            Assert.AreEqual(1f - (EarlyScoreRushInfoDemo.LOOP_DURATION / 15f), EarlyScoreRushInfoDemo.BarLeftAtLoopEnd(15), TOLERANCE);
            Assert.AreEqual(0f, EarlyScoreRushInfoDemo.BarLeftAtLoopEnd(EarlyScoreRushInfoDemo.MIN_WINDOW_SECONDS), TOLERANCE);
        }

        [Test]
        public void EarlyScoreRush_TheSameScorePastTheDeadline_DoesNotCount()
        {
            const int target = 3000;
            ObjectiveProgress progress = new ObjectiveProgress(Rush(15f, target));
            ObjectivePlacementContext late = new ObjectivePlacementContext(
                ScoreInRunInfoDemo.CLEARED_LINE_COUNT, ScoreInRunInfoDemo.CLEARED_LINE_COUNT, 0, PieceFamily.Line, "line_v3",
                ScoreInRunInfoDemo.ChipEnd(target), false, 1, 20, false, false, false, 15.5f, 0);

            progress.ApplyPlacement(late);

            Assert.AreEqual(0, progress.CurrentValue);
            Assert.IsFalse(progress.IsComplete);
        }

        // ---------------------------------------------------------------- Streak Threshold

        [Test]
        public void StreakDemo_EveryTarget_ConsecutiveClearsBuildTheRealStreak()
        {
            for (int target = StreakThresholdInfoDemo.MIN_TARGET; target <= StreakThresholdInfoDemo.MAX_TARGET; target++)
            {
                ResetBoard();
                string[] rows = StreakThresholdInfoDemo.Rows(target);
                ApplyRows(rows);
                AssertNoFullLine(_boardModel.Board, $"target {target}");
                ObjectiveProgress progress = Track(Plain(ObjectiveType.StreakThreshold, target));

                for (int placementIndex = 0; placementIndex < target; placementIndex++)
                {
                    Vector2Int cell = StreakThresholdInfoDemo.LandCell(placementIndex);
                    Place(PieceCatalog.SingleCell, cell.y, cell.x);

                    Assert.AreEqual(1, LastPlacement().RowsCleared, $"target {target}, single {placementIndex}");
                    Assert.AreEqual(0, LastPlacement().ColumnsCleared, $"target {target}, single {placementIndex}");
                    Assert.AreEqual(placementIndex + 1, _scoreModel.Streak.Value, "the HUD's streak");
                    Assert.AreEqual(placementIndex + 1, progress.CurrentValue, $"target {target}, after single {placementIndex}");
                }

                Assert.IsTrue(progress.IsComplete, $"target {target}");

                InfoDemoTimeline timeline = StreakThresholdInfoDemo.Build(target, out InfoDemoProgressChip chip);
                Assert.IsTrue(chip.IsComplete);
                Assert.LessOrEqual(timeline.Duration, 6.5f + TOLERANCE, "within the loop budget");
                AssertBoardMatches(Sample(timeline, timeline.Duration - 0.4f), _boardModel.Board, $"target {target}");
            }
        }

        [Test]
        public void Streak_ANonClearingPlacementBreaksIt_AndTheBestStreakFallsShort()
        {
            ApplyRows(StreakThresholdInfoDemo.Rows(3));
            ObjectiveProgress progress = Track(Plain(ObjectiveType.StreakThreshold, 3));

            Vector2Int first = StreakThresholdInfoDemo.LandCell(0);
            Place(PieceCatalog.SingleCell, first.y, first.x);
            Assert.AreEqual(1, _scoreModel.Streak.Value);

            // A single in the empty top clears nothing: the streak drops to 0.
            Place(PieceCatalog.SingleCell, 0, 0);
            Assert.AreEqual(0, LastPlacement().LinesCleared);
            Assert.AreEqual(0, _scoreModel.Streak.Value);

            for (int placementIndex = 1; placementIndex < 3; placementIndex++)
            {
                Vector2Int cell = StreakThresholdInfoDemo.LandCell(placementIndex);
                Place(PieceCatalog.SingleCell, cell.y, cell.x);
            }

            Assert.AreEqual(2, _scoreModel.Streak.Value);
            Assert.AreEqual(2, progress.CurrentValue, "best streak 2, never a sum");
            Assert.IsFalse(progress.IsComplete);
        }

        // ---------------------------------------------------------------- Clutch Recovery

        [Test]
        public void ClutchDemo_TheBoardHasNoFullLine_AndTheSaveCountsAtTheDefaultThreshold()
        {
            ApplyRows(ClutchRecoveryClearInfoDemo.Rows);
            AssertNoFullLine(_boardModel.Board, "clutch");
            Assert.AreEqual(ClutchRecoveryClearInfoDemo.OccupiedBeforeLanding, _boardModel.Board.OccupiedCellCount());
            ObjectiveProgress progress = Track(Clutch(52, 1));

            Place(PieceCatalog.SingleCell, ClutchRecoveryClearInfoDemo.LAND_ROW, ClutchRecoveryClearInfoDemo.LAND_COLUMN);

            PiecePlacedMessage placed = LastPlacement();
            Assert.AreEqual(1, placed.RowsCleared);
            Assert.AreEqual(0, placed.ColumnsCleared, "only row 7");
            Assert.AreEqual(ClutchRecoveryClearInfoDemo.OccupiedAfterLanding, placed.OccupiedCellCountBeforeClear, "read before the clear");
            Assert.GreaterOrEqual(placed.OccupiedCellCountBeforeClear, 52);
            Assert.AreEqual(1, progress.CurrentValue);
            Assert.IsTrue(progress.IsComplete);

            InfoDemoTimeline timeline = ClutchRecoveryClearInfoDemo.Build(1, out InfoDemoProgressChip chip);
            InfoDemoElementState[] end = Sample(timeline, 4.0f);
            Assert.AreEqual(1f, end[chip.CheckId].Alpha, TOLERANCE);
            AssertBoardMatches(end, _boardModel.Board);
        }

        [Test]
        public void Clutch_AboveTheBoardsOccupancy_OrOnASparseBoard_DoesNotCount()
        {
            ApplyRows(ClutchRecoveryClearInfoDemo.Rows);
            ObjectiveProgress tooHigh = Track(Clutch(ClutchRecoveryClearInfoDemo.OccupiedAfterLanding + 1, 1));
            Place(PieceCatalog.SingleCell, ClutchRecoveryClearInfoDemo.LAND_ROW, ClutchRecoveryClearInfoDemo.LAND_COLUMN);
            Assert.AreEqual(1, LastPlacement().LinesCleared);
            Assert.AreEqual(0, tooHigh.CurrentValue);
            Assert.IsFalse(ClutchRecoveryClearInfoDemo.Supports(ClutchRecoveryClearInfoDemo.OccupiedAfterLanding + 1, 1));

            // A three-line clear on the Score In Run demo's sparse board is no clutch.
            ResetBoard();
            ApplyRows(ScoreInRunInfoDemo.Rows);
            ObjectiveProgress sparse = Track(Clutch(52, 1));
            Place(FindPiece("line_v3"), ScoreInRunInfoDemo.LAND_ROW, ScoreInRunInfoDemo.LAND_COLUMN);
            Assert.AreEqual(3, LastPlacement().LinesCleared);
            Assert.AreEqual(0, sparse.CurrentValue);
        }

        // ---------------------------------------------------------------- Reroll Save

        [Test]
        public void RerollSaveDemo_ADeadDockKeptAliveByTheHeldPiece_IsAClutchSave()
        {
            ApplyRows(RerollInfoDemo.Rows);
            ObjectiveProgress progress = Track(Plain(ObjectiveType.RerollSave, 1));
            SetDock(RerollInfoDemo.OldShapes);
            _trayModel.SetHeld(PieceCatalog.SingleCell, 2);

            // Nothing in the dock fits anywhere (real CanPlace, every anchor) — only the parked single does.
            List<Piece> dock = new List<Piece>();
            _trayModel.CollectRemaining(dock);
            Assert.AreEqual(3, dock.Count);
            Assert.IsFalse(MoveAvailability.HasAnyMove(_boardModel.Board, dock));
            Assert.IsTrue(MoveAvailability.CanPlaceAnywhere(_boardModel.Board, PieceCatalog.SingleCell));

            // The parked piece is what keeps the run alive.
            _boardSystem.RecheckGameOver();
            Assert.IsFalse(_boardSystem.IsGameOver);

            Assert.IsTrue(_boardSystem.TryRerollTray(out bool wasClutchSave));
            Assert.IsTrue(wasClutchSave);
            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Reroll, 0, 0, 0, wasClutchSave));
            Assert.AreEqual(1, progress.CurrentValue);
            Assert.IsTrue(progress.IsComplete);

            // The demo's draw has a move too: its 1x2 completes row 6 on the real board.
            Place(FindPiece("line_h2"), RerollSaveInfoDemo.LAND_ROW, RerollSaveInfoDemo.LAND_COLUMN);
            Assert.AreEqual(1, LastPlacement().RowsCleared);

            InfoDemoTimeline timeline = RerollSaveInfoDemo.Build(1, out InfoDemoProgressChip chip, out InfoDemoPowerUpButton button);
            Assert.IsTrue(chip.IsComplete);
            InfoDemoElementState[] end = Sample(timeline, 4.8f);
            Assert.AreEqual(1f, end[chip.CheckId].Alpha, TOLERANCE);
            Assert.AreEqual(1f, end[button.Badge.Counter.LabelId(1)].Alpha, TOLERANCE, "the charge is spent");
            AssertBoardMatches(end, _boardModel.Board);
        }

        [Test]
        public void RerollSave_WithoutTheHeldPiece_TheRunIsAlreadyOver_AndNoRerollHappens()
        {
            ApplyRows(RerollInfoDemo.Rows);
            SetDock(RerollInfoDemo.OldShapes);

            _boardSystem.RecheckGameOver();

            Assert.IsTrue(_boardSystem.IsGameOver);
            Assert.IsFalse(_boardSystem.TryRerollTray(out bool wasClutchSave));
            Assert.IsFalse(wasClutchSave);
        }

        [Test]
        public void RerollSave_ARerollWithAMoveStillOnOffer_DoesNotCount()
        {
            ApplyRows(RerollInfoDemo.Rows);
            ObjectiveProgress progress = Track(Plain(ObjectiveType.RerollSave, 1));
            SetDock(RerollInfoDemo.NewShapes);

            Assert.IsTrue(_boardSystem.TryRerollTray(out bool wasClutchSave));
            Assert.IsFalse(wasClutchSave);
            _powerUpAppliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Reroll, 0, 0, 0, wasClutchSave));

            Assert.AreEqual(0, progress.CurrentValue);
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

        private ScoreChangedMessage LastScore()
        {
            Assert.Greater(_scoreChangedBroker.Published.Count, 0, "a score was published");
            return _scoreChangedBroker.Published[_scoreChangedBroker.Published.Count - 1];
        }

        private static ObjectiveDefinition Plain(ObjectiveType type, int target)
            => new ObjectiveDefinition("test", type, ObjectiveScope.PerRun, target);

        private static ObjectiveDefinition Family(PieceFamily family, int target)
            => new ObjectiveDefinition("test", ObjectiveType.PieceFamilyCount, ObjectiveScope.PerRun, target, requiredPieceFamily: family);

        private static ObjectiveDefinition PieceId(string pieceId, int target)
            => new ObjectiveDefinition("test", ObjectiveType.PieceIdCount, ObjectiveScope.PerRun, target, requiredPieceId: pieceId);

        private static ObjectiveDefinition Rush(float windowSeconds, int target)
            => new ObjectiveDefinition("test", ObjectiveType.EarlyScoreRush, ObjectiveScope.PerRun, target, windowSeconds: windowSeconds);

        private static ObjectiveDefinition Clutch(int threshold, int target)
            => new ObjectiveDefinition(
                "test", ObjectiveType.ClutchRecoveryClear, ObjectiveScope.PerRun, target, requiredOccupancyThreshold: threshold);

        /// <summary>A fresh board, dock, score and objective engine between the cases of a looped test — nothing
        /// (a streak, a line count towards a reward cell) carries over from the previous case.</summary>
        private void ResetBoard()
        {
            DisposeSystems();
            CreateSystems();
        }

        private static Piece FindPiece(string pieceId)
        {
            Piece piece = PieceIdLineClearInfoDemo.FindPiece(pieceId);
            Assert.IsNotNull(piece, pieceId);
            return piece;
        }

        /// <summary>Deals the three demo shapes into the dock as their catalog pieces.</summary>
        private void SetDock(Vector2Int[][] shapes)
        {
            for (int slotIndex = 0; slotIndex < shapes.Length; slotIndex++)
            {
                _trayModel.SetSlot(slotIndex, CatalogPiece(shapes[slotIndex]), slotIndex + 1);
            }
        }

        /// <summary>Places <paramref name="piece"/> through the real <see cref="BoardSystem.TryPlacePiece"/> with its
        /// footprint's top-left on demo cell (<paramref name="topRow"/>, <paramref name="leftColumn"/>).</summary>
        private void Place(Piece piece, int topRow, int leftColumn)
        {
            _trayModel.SetSlot(0, piece, InfoDemoPaint.BLOCK_4);
            Assert.IsTrue(_boardSystem.TryPlacePiece(0, AnchorFor(piece, topRow, leftColumn)), $"{piece.Id} at ({topRow},{leftColumn})");
        }

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

        private static Piece CatalogPiece(Vector2Int[] shape)
        {
            IReadOnlyList<Piece> pieces = PieceCatalog.AllPieces;
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                if (SameCells(PieceIdLineClearInfoDemo.DemoShape(pieces[pieceIndex]), shape))
                {
                    return pieces[pieceIndex];
                }
            }

            Assert.Fail("No catalog piece has the demo's shape.");
            return null;
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

        /// <summary>A real board never holds a full line — one clears the moment it fills.</summary>
        private static void AssertNoFullLine(Board board, string context)
        {
            for (int line = 0; line < Board.SIZE; line++)
            {
                Assert.IsFalse(board.IsRowFull(line), $"{context}: row y={line} is full");
                Assert.IsFalse(board.IsColumnFull(line), $"{context}: column x={line} is full");
            }
        }

        /// <summary>The demo's final occupancy is exactly the real board's, except for a reward cell the real clear
        /// spawned on a cell it had just emptied — see <c>InfoDemoLineClearObjectiveTests</c>.</summary>
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

        private static GridPosition ToGrid(int row, int column) => new GridPosition(column, Board.SIZE - 1 - row);
    }
}
