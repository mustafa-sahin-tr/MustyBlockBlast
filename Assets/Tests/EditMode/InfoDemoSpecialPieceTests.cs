using System;
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
    /// The special dock piece info demos (issue #451): the catalog mapping, the new special-piece beats,
    /// and — for each demo — that it shows what the real piece does, checked against the real
    /// <see cref="BoardSystem"/>: the demo board and dock are rebuilt on a real system, the demo's move is
    /// played through the same public entry point the game uses (<see cref="BoardSystem.TryPlacePiece"/>,
    /// <see cref="BoardSystem.TryUseDemolitionHammer"/>), and the demo must end on exactly that board.
    /// </summary>
    public class InfoDemoSpecialPieceTests
    {
        private const float TOLERANCE = 0.0001f;

        /// <summary>After every demo's effect has settled and before any loop fade-out starts.</summary>
        private const float AFTER_EFFECT = 4.4f;

        private static readonly SpecialPieceKind[] DemoKinds =
        {
            SpecialPieceKind.Golden,
            SpecialPieceKind.PiercingRocket,
            SpecialPieceKind.DemolitionHammer,
        };

        private BoardModel _boardModel;
        private TrayModel _trayModel;
        private TestMessageBroker<PiercingRocketFiredMessage> _rocketFiredBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private BoardSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            _boardModel = new BoardModel();
            _trayModel = new TrayModel();
            _rocketFiredBroker = new TestMessageBroker<PiercingRocketFiredMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();

            // Deliberately unstarted, as BoardSystemSpecialDockPieceTests is: StartNewRun would draw over
            // the board and dock each test lays out by hand.
            _system = new BoardSystem(
                _boardModel,
                _trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                _gameOverBroker,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                _rocketFiredBroker,
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: 1);
        }

        // ---------------------------------------------------------------- catalog

        [Test]
        public void EverySpecialPiece_HasItsOwnCachedDemo()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();
            HashSet<InfoDemoTimeline> seen = new HashSet<InfoDemoTimeline>();

            for (int kindIndex = 0; kindIndex < DemoKinds.Length; kindIndex++)
            {
                SpecialPieceKind kind = DemoKinds[kindIndex];
                InfoDemoTimeline first = catalog.Find(InfoPopupSubjectKind.SpecialPiece, (int)kind);
                Assert.IsNotNull(first, kind.ToString());
                Assert.AreSame(first, catalog.Find(InfoPopupSubjectKind.SpecialPiece, (int)kind), kind.ToString());
                Assert.IsTrue(seen.Add(first), kind + " has its own demo");
            }

            Assert.AreEqual(GoldenPieceInfoDemo.LOOP_DURATION, Demo(SpecialPieceKind.Golden).Duration, TOLERANCE);
            Assert.AreEqual(PiercingRocketInfoDemo.LOOP_DURATION, Demo(SpecialPieceKind.PiercingRocket).Duration, TOLERANCE);
            Assert.AreEqual(
                DemolitionHammerInfoDemo.LOOP_DURATION, Demo(SpecialPieceKind.DemolitionHammer).Duration, TOLERANCE);
        }

        [Test]
        public void EveryKindButNone_IsCovered_AndNoneOrOutOfRangeHasNoDemo()
        {
            Array kinds = Enum.GetValues(typeof(SpecialPieceKind));
            for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
            {
                SpecialPieceKind kind = (SpecialPieceKind)kinds.GetValue(kindIndex);
                if (kind != SpecialPieceKind.None)
                {
                    Assert.GreaterOrEqual(Array.IndexOf(DemoKinds, kind), 0, kind + " is in this slice");
                }
            }

            InfoDemoCatalog catalog = new InfoDemoCatalog();
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialPiece, (int)SpecialPieceKind.None));
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialPiece, -1));
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialPiece, 99));
        }

        [Test]
        public void EachDemo_DrawsItsPieceAsTheDockDoes()
        {
            AssertCarries(GoldenPieceInfoDemo.Build(out _, out int golden), golden, SpecialPieceKind.Golden);
            Assert.AreEqual(InfoDemoPaint.GOLDEN_PIECE, GoldenPieceInfoDemo.Build().GetElement(golden).Initial.Paint,
                "a golden plate is the dock's gold, not a theme colour");

            AssertCarries(PiercingRocketInfoDemo.Build(out int rocket), rocket, SpecialPieceKind.PiercingRocket);
            AssertCarries(DemolitionHammerInfoDemo.Build(out int hammer), hammer, SpecialPieceKind.DemolitionHammer);

            // Every other piece is an ordinary one.
            InfoDemoTimeline timeline = PiercingRocketInfoDemo.Build();
            for (int elementId = 0; elementId < timeline.ElementCount; elementId++)
            {
                InfoDemoElement element = timeline.GetElement(elementId);
                if (element.Kind == InfoDemoElementKind.Piece && elementId != rocket)
                {
                    Assert.AreEqual((int)SpecialPieceKind.None, element.SpriteParameter, $"piece {elementId}");
                }
            }
        }

        [Test]
        public void SpecialPieceIdentityPaints_RoundTripTheirKind_AndGoldMatchesTheDock()
        {
            Array kinds = Enum.GetValues(typeof(SpecialPieceKind));
            for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
            {
                SpecialPieceKind kind = (SpecialPieceKind)kinds.GetValue(kindIndex);
                int paint = InfoDemoPaint.SpecialPieceIdentity(kind);
                Assert.IsTrue(InfoDemoPaint.TryGetSpecialPieceIdentity(paint, out SpecialPieceKind back), kind.ToString());
                Assert.AreEqual(kind, back);
                Assert.IsFalse(InfoDemoPaint.TryGetSpecialCellGlow(paint, out SpecialCellKind _), "no clash with #450's ids");
            }

            Assert.IsFalse(InfoDemoPaint.TryGetSpecialPieceIdentity(InfoDemoPaint.GOLDEN_PIECE, out SpecialPieceKind _));
            Assert.IsFalse(InfoDemoPaint.TryGetSpecialPieceIdentity(InfoDemoPaint.SPECIAL_CELL_GLOW_LAST, out SpecialPieceKind _));

            Assert.IsTrue(SpecialPieceVisuals.TryGetFill(SpecialPieceKind.Golden, out Color fill, out Color _, out Color _));
            Assert.AreEqual(fill, SpecialPieceVisuals.IdentityColour(SpecialPieceKind.Golden), "one gold everywhere");
            Assert.IsFalse(SpecialPieceVisuals.TryGetFill(SpecialPieceKind.PiercingRocket, out Color _, out Color _, out Color _));
            Assert.AreNotEqual(
                SpecialPieceVisuals.IdentityColour(SpecialPieceKind.Golden),
                SpecialPieceVisuals.IdentityColour(SpecialPieceKind.DemolitionHammer));
        }

        // ---------------------------------------------------------------- beats

        [Test]
        public void DragPiece_LandsWhenPlacePieceDoes_WithAFingerThatTravelsBelowIt()
        {
            Vector2Int[] single = { new Vector2Int(0, 0) };
            Vector2 tray = InfoDemoLayout.TraySlot(0);
            const float start = 1f;

            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            int piece = builder.AddPiece(single, InfoDemoPaint.BLOCK_1, tray, InfoDemoLayout.TRAY_PIECE_SCALE);
            int firstFingerPart = piece + 1;
            float land = InfoDemoSpecialPieceChoreography.DragPiece(builder, piece, single, InfoDemoPaint.BLOCK_3, tray, 6, 2, start);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(start + InfoDemoChoreography.LIFT_DURATION + InfoDemoChoreography.GLIDE_DURATION, land, TOLERANCE);

            int blockId = InfoDemoLayout.BoardBlockId(6, 2);
            Assert.AreEqual(InfoDemoPaint.NONE, Sample(timeline, land - 0.01f)[blockId].Paint);
            Assert.AreEqual(InfoDemoPaint.BLOCK_3, Sample(timeline, land + 0.01f)[blockId].Paint);

            // Mid-glide the finger rides DRAG_FINGER_DROP below the piece (the face is the last finger part).
            float middle = start + InfoDemoChoreography.LIFT_DURATION + (InfoDemoChoreography.GLIDE_DURATION * 0.5f);
            InfoDemoElementState[] states = Sample(timeline, middle);
            int face = firstFingerPart + 2;
            Assert.AreEqual(InfoDemoElementKind.Icon, timeline.GetElement(face).Kind);
            Assert.AreEqual(states[piece].Position.x, states[face].Position.x, 0.001f);
            Assert.AreEqual(
                states[piece].Position.y + InfoDemoSpecialPieceChoreography.DRAG_FINGER_DROP, states[face].Position.y, 0.001f);
        }

        [Test]
        public void ArmPiece_LiftsThePieceInsideAnArmedRing()
        {
            Vector2Int[] single = { new Vector2Int(0, 0) };
            Vector2 tray = InfoDemoLayout.TraySlot(1);
            const float rest = 0.42f;

            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            int piece = builder.AddPiece(single, InfoDemoPaint.BLOCK_1, tray, rest);
            int ring = InfoDemoSpecialPieceChoreography.ArmPiece(builder, piece, tray, rest, 1f, 1f, 3f);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(InfoDemoPaint.ARMED, timeline.GetElement(ring).Initial.Paint);
            Assert.AreEqual(0f, Sample(timeline, 0.9f)[ring].Alpha, TOLERANCE);
            InfoDemoElementState[] armed = Sample(timeline, 2f);
            Assert.AreEqual(1f, armed[ring].Alpha, TOLERANCE);
            Assert.AreEqual(rest * InfoDemoSpecialPieceChoreography.ARMED_LIFT, armed[piece].Scale, 0.001f);
            Assert.AreEqual(0f, Sample(timeline, 3.5f)[ring].Alpha, TOLERANCE);
        }

        // ---------------------------------------------------------------- Golden

        [Test]
        public void GoldenDemo_StreakPillCountsUpToTheRealTrigger()
        {
            InfoDemoTimeline timeline = GoldenPieceInfoDemo.Build(out InfoDemoCounter streak, out _);
            int trigger = GoldenPieceTriggerSystem.TRIGGER_STREAK;
            Assert.AreEqual(5, trigger);
            Assert.AreEqual(2, streak.ValueCount);
            Assert.AreEqual("x" + (trigger - 1), timeline.GetElement(streak.LabelId(0)).LabelArgument);
            Assert.AreEqual("x" + trigger, timeline.GetElement(streak.LabelId(1)).LabelArgument);

            InfoDemoElementState[] before = Sample(timeline, GoldenPieceInfoDemo.STREAK_TIME - 0.05f);
            Assert.AreEqual(1f, before[streak.LabelId(0)].Alpha, TOLERANCE);
            Assert.AreEqual(0f, before[streak.LabelId(1)].Alpha, TOLERANCE);
            InfoDemoElementState[] after = Sample(timeline, GoldenPieceInfoDemo.DEAL_START);
            Assert.AreEqual(0f, after[streak.LabelId(0)].Alpha, TOLERANCE);
            Assert.AreEqual(1f, after[streak.LabelId(1)].Alpha, TOLERANCE);

            // The combo pill wears the HUD's own flame.
            Assert.IsTrue(HasIcon(timeline, InfoDemoSprite.StreakFlame));
        }

        [Test]
        public void GoldenDemo_DealsTheGoldenPieceIntoTheSlotTheRealRefillClaims()
        {
            // An empty board, one plain single left in the dock and a golden piece owed: playing the single
            // empties the dock, and the refill pays the golden piece.
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1);
            _system.RequestGoldenPieceInjection();
            Assert.IsTrue(_system.TryPlacePiece(0, new GridPosition(0, 0)));

            Assert.AreEqual(SpecialPieceKind.Golden, _trayModel.GetSpecialKind(GoldenPieceInfoDemo.GOLDEN_SLOT));
            Assert.AreEqual(PieceCatalog.SingleCell.Id, _trayModel.GetPiece(GoldenPieceInfoDemo.GOLDEN_SLOT).Id);
        }

        [Test]
        public void GoldenDemo_EndsOnTheBoardTheRealPlacementLeaves()
        {
            ApplyRows(GoldenPieceInfoDemo.Rows);
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 3, SpecialPieceKind.Golden);
            _trayModel.SetSlot(1, FindPiece("line_h2"), 5);

            Assert.IsTrue(_system.TryPlacePiece(
                0, ToGrid(GoldenPieceInfoDemo.LAND_ROW, GoldenPieceInfoDemo.LAND_COLUMN)));

            InfoDemoTimeline timeline = Demo(SpecialPieceKind.Golden);
            InfoDemoElementState[] after = Sample(timeline, AFTER_EFFECT);
            AssertBoardMatches(after, _boardModel.Board);
            AssertBoard(after, GoldenPieceInfoDemo.Rows, (row, column) => row == GoldenPieceInfoDemo.LAND_ROW);

            // Exactly what an ordinary single would have done on the same board.
            BoardModel plainBoard = new BoardModel();
            FillRows(plainBoard, GoldenPieceInfoDemo.Rows);
            plainBoard.Occupy(ToGrid(GoldenPieceInfoDemo.LAND_ROW, GoldenPieceInfoDemo.LAND_COLUMN), 3);
            Assert.AreEqual(1, CascadeClearResolver.ResolveCascade(plainBoard.Board).Primary.LineCount);
            AssertBoardMatches(after, plainBoard.Board);
        }

        // ---------------------------------------------------------------- Piercing Rocket

        [Test]
        public void RocketDemo_DealsTheRocketIntoTheSlotTheRealRefillClaims_AfterAThreeLineClear()
        {
            // Rows y = 5, 6, 7 are full but for x = 0; a vertical 1x3 there clears all three at once, and
            // it was the dock's last piece, so the refill that follows pays the rocket.
            for (int y = 5; y < Board.SIZE; y++)
            {
                for (int x = 1; x < Board.SIZE; x++)
                {
                    _boardModel.Occupy(new GridPosition(x, y), 2);
                }
            }

            _trayModel.SetSlot(0, FindPiece("line_v3"), 1);
            Assert.IsTrue(_system.TryPlacePiece(0, new GridPosition(0, 5)));

            Assert.AreEqual(SpecialPieceKind.PiercingRocket, _trayModel.GetSpecialKind(PiercingRocketInfoDemo.ROCKET_SLOT));
            Assert.AreEqual(PieceCatalog.SingleCell.Id, _trayModel.GetPiece(PiercingRocketInfoDemo.ROCKET_SLOT).Id);
        }

        [Test]
        public void RocketDemo_EndsOnTheBoardTheRealRocketLeaves()
        {
            ApplyRows(PiercingRocketInfoDemo.Rows);
            _trayModel.SetSlot(0, PieceCatalog.SingleCell, 1, SpecialPieceKind.PiercingRocket);
            _trayModel.SetSlot(1, FindPiece("line_h2"), 5);

            int occupiedBefore = _boardModel.Board.OccupiedCellCount();
            Assert.IsTrue(_system.TryPlacePiece(
                0, ToGrid(PiercingRocketInfoDemo.LAND_ROW, PiercingRocketInfoDemo.LAND_COLUMN)));

            // Neither line was full, and both went whole — the rocket's own cell with them.
            Assert.AreEqual(1, _rocketFiredBroker.Published.Count);
            Assert.AreEqual(PiercingRocketInfoDemo.WipedCells().Length, _rocketFiredBroker.Published[0].WipedCellCount);
            Assert.AreEqual(occupiedBefore + 1 - PiercingRocketInfoDemo.WipedCells().Length, _boardModel.Board.OccupiedCellCount());

            InfoDemoElementState[] after = Sample(Demo(SpecialPieceKind.PiercingRocket), AFTER_EFFECT);
            AssertBoardMatches(after, _boardModel.Board);
            AssertBoard(
                after, PiercingRocketInfoDemo.Rows,
                (row, column) => row == PiercingRocketInfoDemo.LAND_ROW || column == PiercingRocketInfoDemo.LAND_COLUMN);
        }

        // ---------------------------------------------------------------- Demolition Hammer

        /// <summary>
        /// Sets up the demo's crowded dock — the 3x3 (slot 0), the hammer (<see cref="DemolitionHammerInfoDemo.HAMMER_SLOT"/>)
        /// and the horizontal 1x2 (slot 2) — exactly as the demo's board (<see cref="DemolitionHammerInfoDemo.Rows"/>)
        /// is already applied. The hammer is set directly rather than earned through
        /// <see cref="BoardSystem.RecheckGameOver"/>'s occupancy gate: that gate (>= 90% full) is already
        /// covered end to end by <c>BoardSystemSpecialDockPieceTests</c>, and demanding it here as well
        /// would force the board below into having some other, already-complete row or column — which a
        /// board reachable through real play, where every placement clears whatever it completes, could
        /// never have. This board settles for "very full and thoroughly stuck" (55 of 64 cells, every line
        /// keeping exactly the gap that stops it being complete) instead.
        /// </summary>
        private void SetUpStuckDock()
        {
            ApplyRows(DemolitionHammerInfoDemo.Rows);
            _trayModel.SetSlot(0, FindPiece("square_3x3"), 3);
            _trayModel.SetSlot(
                DemolitionHammerInfoDemo.HAMMER_SLOT, PieceCatalog.SingleCell, 1, SpecialPieceKind.DemolitionHammer);
            _trayModel.SetSlot(2, FindPiece("line_h2"), 1);
        }

        [Test]
        public void HammerDemo_WithTheHammerInDock_TheStuckRunIsReprievedNotOver()
        {
            SetUpStuckDock();

            Assert.GreaterOrEqual(
                _boardModel.Board.OccupiedCellCount() / (float)_boardModel.Board.PlayableCellCount, 0.8f, "very full");

            _system.RecheckGameOver();

            Assert.IsFalse(_system.IsGameOver, "reprieved rather than ended");
            Assert.AreEqual(0, _gameOverBroker.Published.Count);
        }

        [Test]
        public void HammerDemo_NoDockPieceFitsAnywhere_BeforeTheHammer()
        {
            SetUpStuckDock();

            // The real fit check, not just an occupancy ratio: neither the 3x3 nor the 1x2 fits anywhere —
            // every row's gaps are isolated, so a horizontal 1x2 never finds two adjacent empty cells.
            AssertNoDockPieceFitsAnywhere();
        }

        [Test]
        public void HammerDemo_OpensExactlyTheGapTheDockPieceNeeds_AndRow7Clears()
        {
            SetUpStuckDock();
            AssertNoDockPieceFitsAnywhere();

            // The hammer empties exactly (7,5) — beside the row's existing gap at (7,6) — and spends itself.
            int occupiedBefore = _boardModel.Board.OccupiedCellCount();
            GridPosition target = ToGrid(DemolitionHammerInfoDemo.TARGET_ROW, DemolitionHammerInfoDemo.TARGET_COLUMN);
            Assert.IsTrue(_system.TryUseDemolitionHammer(DemolitionHammerInfoDemo.HAMMER_SLOT, target));

            Assert.AreEqual(occupiedBefore - 1, _boardModel.Board.OccupiedCellCount());
            Assert.AreEqual(Board.EMPTY, _boardModel.GetCell(target));
            Assert.AreEqual(SpecialPieceKind.None, _trayModel.GetSpecialKind(DemolitionHammerInfoDemo.HAMMER_SLOT), "spent");

            // Only now does the dock's 1x2 fit — exactly at the two-cell gap the hammer opened.
            GridPosition placeAnchor = ToGrid(DemolitionHammerInfoDemo.PLACE_ROW, DemolitionHammerInfoDemo.PLACE_COLUMN);
            Assert.IsTrue(_system.CanPlace(2, placeAnchor));
            Assert.IsTrue(_system.TryPlacePiece(2, placeAnchor));

            // Landing it completes row 7, which clears whole.
            for (int column = 0; column < Board.SIZE; column++)
            {
                Assert.AreEqual(
                    Board.EMPTY, _boardModel.GetCell(ToGrid(DemolitionHammerInfoDemo.TARGET_ROW, column)),
                    $"row 7 column {column} cleared");
            }

            InfoDemoTimeline timeline = DemolitionHammerInfoDemo.Build(out int hammer);
            InfoDemoElementState[] after = Sample(timeline, AFTER_EFFECT);
            AssertBoardMatches(after, _boardModel.Board);
            AssertBoard(after, DemolitionHammerInfoDemo.Rows, (row, column) => row == DemolitionHammerInfoDemo.TARGET_ROW);
            Assert.AreEqual(0f, after[hammer].Alpha, TOLERANCE, "the hammer left the dock");

            // The target is still standing until the smash.
            int targetBlock = InfoDemoLayout.BoardBlockId(DemolitionHammerInfoDemo.TARGET_ROW, DemolitionHammerInfoDemo.TARGET_COLUMN);
            Assert.AreNotEqual(InfoDemoPaint.NONE, Sample(timeline, DemolitionHammerInfoDemo.SMASH_TIME - 0.01f)[targetBlock].Paint);

            // The 1x2 has not yet landed just before it is dragged onto the gap.
            int placeBlock = InfoDemoLayout.BoardBlockId(DemolitionHammerInfoDemo.PLACE_ROW, DemolitionHammerInfoDemo.PLACE_COLUMN);
            Assert.AreEqual(InfoDemoPaint.NONE, Sample(timeline, DemolitionHammerInfoDemo.PLACE_START - 0.01f)[placeBlock].Paint);
        }

        /// <summary>Every dock slot but the hammer's, at every board anchor, refuses to place — the real
        /// check (<see cref="BoardSystem.CanPlace"/>), not an occupancy heuristic.</summary>
        private void AssertNoDockPieceFitsAnywhere()
        {
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                if (_trayModel.GetSpecialKind(slotIndex) == SpecialPieceKind.DemolitionHammer
                    || _trayModel.GetPiece(slotIndex) == null)
                {
                    continue;
                }

                for (int y = 0; y < Board.SIZE; y++)
                {
                    for (int x = 0; x < Board.SIZE; x++)
                    {
                        Assert.IsFalse(_system.CanPlace(slotIndex, new GridPosition(x, y)), $"slot {slotIndex} at ({x},{y})");
                    }
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static InfoDemoTimeline Demo(SpecialPieceKind kind)
            => new InfoDemoCatalog().Find(InfoPopupSubjectKind.SpecialPiece, (int)kind);

        private static InfoDemoElementState[] Sample(InfoDemoTimeline timeline, float time)
        {
            InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];
            timeline.Evaluate(time, states);
            return states;
        }

        private static void AssertCarries(InfoDemoTimeline timeline, int pieceId, SpecialPieceKind kind)
        {
            InfoDemoElement element = timeline.GetElement(pieceId);
            Assert.AreEqual(InfoDemoElementKind.Piece, element.Kind, kind.ToString());
            Assert.AreEqual((int)kind, element.SpriteParameter, kind.ToString());
            Assert.AreEqual(1, element.Shape.Length, kind + " is offered on the 1x1");
        }

        private static bool HasIcon(InfoDemoTimeline timeline, InfoDemoSprite sprite)
        {
            for (int elementId = 0; elementId < timeline.ElementCount; elementId++)
            {
                if (timeline.GetElement(elementId).Sprite == sprite)
                {
                    return true;
                }
            }

            return false;
        }

        private static Piece FindPiece(string id)
        {
            for (int pieceIndex = 0; pieceIndex < PieceCatalog.AllPieces.Count; pieceIndex++)
            {
                if (PieceCatalog.AllPieces[pieceIndex].Id == id)
                {
                    return PieceCatalog.AllPieces[pieceIndex];
                }
            }

            Assert.Fail($"No catalog piece '{id}'.");
            return null;
        }

        private void ApplyRows(string[] rows) => FillRows(_boardModel, rows);

        private static void FillRows(BoardModel boardModel, string[] rows)
        {
            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                {
                    int paint = InfoDemoPaint.FromPatternChar(rows[row][column]);
                    if (paint != InfoDemoPaint.NONE)
                    {
                        boardModel.Occupy(ToGrid(row, column), paint);
                    }
                }
            }
        }

        /// <summary>Every cell <paramref name="isCleared"/> picks is empty; every other cell still holds its
        /// starting paint.</summary>
        private static void AssertBoard(InfoDemoElementState[] states, string[] rows, Func<int, int, bool> isCleared)
        {
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    int actual = states[InfoDemoLayout.BoardBlockId(row, column)].Paint;
                    if (isCleared(row, column))
                    {
                        Assert.AreEqual(InfoDemoPaint.NONE, actual, $"({row},{column}) cleared");
                    }
                    else
                    {
                        Assert.AreEqual(InfoDemoBoardPattern.PaintAt(rows, row, column), actual, $"({row},{column}) unchanged");
                    }
                }
            }
        }

        /// <summary>The demo's final occupancy is exactly the real board's.</summary>
        private static void AssertBoardMatches(InfoDemoElementState[] states, Board board)
        {
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    bool demoOccupied = states[InfoDemoLayout.BoardBlockId(row, column)].Paint != InfoDemoPaint.NONE;
                    Assert.AreEqual(board.IsOccupied(ToGrid(row, column)), demoOccupied, $"({row},{column}) vs the real board");
                }
            }
        }

        /// <summary>Demo (row, column) — rows counting down from the top — as a board position, whose Y
        /// counts up from the bottom.</summary>
        private static GridPosition ToGrid(int row, int column) => new GridPosition(column, Board.SIZE - 1 - row);
    }
}
