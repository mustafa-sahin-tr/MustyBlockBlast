using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Presentation.Views;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// The tray / targetless power-up info demos and the Hold pocket demo (issue #449): the catalog
    /// mapping (including both ways the Hold card opens), the new reusable beats (a dock piece turning,
    /// sliding, being discarded and dealt, a finger drag, a flying sprite, a countdown bar, a charge badge
    /// counting down, the overlay layer), and — for each demo — that it shows what the real power-up does,
    /// checked against the real Core code wherever there is some to check against (<c>PieceRotator</c>,
    /// <c>GhostFitSearch</c>, <c>ScoreRules</c>).
    /// </summary>
    public class InfoDemoTrayPowerUpTests
    {
        private const float TOLERANCE = 0.0001f;

        /// <summary>After every demo's effect has settled and before any loop fade-out starts.</summary>
        private const float AFTER_EFFECT = 4.4f;

        // ---------------------------------------------------------------- catalog

        [Test]
        public void TheTrayAndTargetlessPowerUps_EachHaveTheirOwnDemo()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            AssertDemo(catalog, PowerUpKind.Rotate, RotateInfoDemo.LOOP_DURATION);
            AssertDemo(catalog, PowerUpKind.Reroll, RerollInfoDemo.LOOP_DURATION);
            AssertDemo(catalog, PowerUpKind.DoubleMultiplier, DoubleMultiplierInfoDemo.LOOP_DURATION);
            AssertDemo(catalog, PowerUpKind.GhostFit, GhostFitInfoDemo.LOOP_DURATION);
            AssertDemo(catalog, PowerUpKind.CoinSower, CoinSowerInfoDemo.LOOP_DURATION);

            HashSet<InfoDemoTimeline> distinct = new HashSet<InfoDemoTimeline>
            {
                catalog.Find(InfoPopupSubjectKind.PowerUp, (int)PowerUpKind.Rotate),
                catalog.Find(InfoPopupSubjectKind.PowerUp, (int)PowerUpKind.Reroll),
                catalog.Find(InfoPopupSubjectKind.PowerUp, (int)PowerUpKind.DoubleMultiplier),
                catalog.Find(InfoPopupSubjectKind.PowerUp, (int)PowerUpKind.GhostFit),
                catalog.Find(InfoPopupSubjectKind.PowerUp, (int)PowerUpKind.CoinSower),
                catalog.Find(InfoPopupSubjectKind.PowerUp, (int)PowerUpKind.Hold),
            };
            Assert.AreEqual(6, distinct.Count);
        }

        /// <summary>The Hold card opens as the Hold subject (first park, a tap on the pocket) and as the
        /// Hold power-up (its first granted charge) — InfoPopupSystem shows the same card for both, so both
        /// get the same, cached demo.</summary>
        [Test]
        public void HoldCard_GetsTheHoldDemo_FromBothEntryPoints()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            InfoDemoTimeline asSubject = catalog.Find(InfoPopupSubjectKind.Hold, -1);
            Assert.IsNotNull(asSubject);
            Assert.AreEqual(HoldInfoDemo.LOOP_DURATION, asSubject.Duration, TOLERANCE);
            Assert.AreSame(asSubject, catalog.Find(InfoPopupSubjectKind.Hold, 0));
            Assert.AreSame(asSubject, catalog.Find(InfoPopupSubjectKind.PowerUp, (int)PowerUpKind.Hold));
        }

        // ---------------------------------------------------------------- beats

        [Test]
        public void RotatePiece_TurnsAQuarterClockwise_ThenHandsOverToTheRotatedShape()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            Vector2 slot = new Vector2(4f, 8.5f);
            int piece = builder.AddPiece(RotateInfoDemo.PieceShape, InfoDemoPaint.BLOCK_3, slot, 0.42f);
            int rotated = builder.AddPiece(RotateInfoDemo.RotatedShape, InfoDemoPaint.BLOCK_3, slot, 0.42f, 0f);
            InfoDemoTrayChoreography.RotatePiece(builder, piece, rotated, 0.42f, 1f, 0.4f, 1.5f);
            InfoDemoTimeline timeline = builder.Build();

            InfoDemoElementState[] before = Sample(timeline, 0.9f);
            Assert.AreEqual(0f, before[piece].Rotation, TOLERANCE);
            Assert.AreEqual(1f, before[piece].Alpha, TOLERANCE);
            Assert.AreEqual(0f, before[rotated].Alpha, TOLERANCE);

            InfoDemoElementState[] turning = Sample(timeline, 1.2f);
            Assert.Less(turning[piece].Rotation, 0f, "clockwise is negative in UI space");
            Assert.Greater(turning[piece].Rotation, -90f);
            Assert.AreEqual(slot, turning[piece].Position, "turns about its own centre");

            InfoDemoElementState[] turned = Sample(timeline, 1.45f);
            Assert.AreEqual(-90f, turned[piece].Rotation, TOLERANCE);

            InfoDemoElementState[] swapped = Sample(timeline, 2f);
            Assert.AreEqual(0f, swapped[piece].Alpha, TOLERANCE);
            Assert.AreEqual(1f, swapped[rotated].Alpha, TOLERANCE);
            Assert.AreEqual(0f, swapped[rotated].Rotation, TOLERANCE, "the rotated shape is drawn upright");
            Assert.AreEqual(0.42f, swapped[rotated].Scale, TOLERANCE, "its swap pop has settled");
        }

        [Test]
        public void SlideDiscardAndDeal_MoveFadeAndPopThePieces()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            Vector2 from = new Vector2(1f, 8.5f);
            Vector2 to = new Vector2(6f, 8.5f);
            int slid = builder.AddPiece(RotateInfoDemo.PieceShape, InfoDemoPaint.BLOCK_1, from, 0.42f);
            int discarded = builder.AddPiece(RotateInfoDemo.PieceShape, InfoDemoPaint.BLOCK_2, to, 0.42f);
            int dealt = builder.AddPiece(RotateInfoDemo.PieceShape, InfoDemoPaint.BLOCK_3, to, 0.42f, 0f);
            float arrival = InfoDemoTrayChoreography.SlidePiece(builder, slid, from, to, 0.48f, 0.42f, 1f, 0.5f);
            float gone = InfoDemoTrayChoreography.DiscardPiece(builder, discarded, to, 0.42f, 1f);
            float settled = InfoDemoTrayChoreography.DealPiece(builder, dealt, 0.42f, 2f);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(1.5f, arrival, TOLERANCE);
            InfoDemoElementState[] start = Sample(timeline, 1f);
            Assert.AreEqual(from, start[slid].Position);
            Assert.AreEqual(0.48f, start[slid].Scale, TOLERANCE);
            Assert.AreEqual(0f, start[dealt].Alpha, TOLERANCE, "not dealt yet");

            InfoDemoElementState[] end = Sample(timeline, settled + 0.1f);
            Assert.AreEqual(to, end[slid].Position);
            Assert.AreEqual(0.42f, end[slid].Scale, TOLERANCE);
            Assert.Greater(gone, 1f);
            Assert.AreEqual(0f, end[discarded].Alpha, TOLERANCE);
            Assert.Greater(end[discarded].Position.y, to.y, "it drops away");
            Assert.AreEqual(1f, end[dealt].Alpha, TOLERANCE);
            Assert.AreEqual(0.42f, end[dealt].Scale, TOLERANCE);
        }

        [Test]
        public void Drag_FingerPressesOnTheGrabPoint_TravelsWithTheMove_ThenLifts_OverThePieces()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            Vector2 from = new Vector2(0.5f, 8.5f);
            Vector2 to = new Vector2(6.5f, 8.5f);
            float release = InfoDemoChoreography.Drag(builder, 1f, from, to, 1.2f, 0.6f);
            InfoDemoTimeline timeline = builder.Build();
            int face = timeline.ElementCount - 1;

            Assert.AreEqual(1.8f, release, TOLERANCE);
            Assert.IsTrue(timeline.GetElement(face).OnTop, "a finger draws over the tray pieces");
            Assert.IsTrue(timeline.GetElement(face - 1).OnTop);
            Assert.IsTrue(timeline.GetElement(face - 2).OnTop);

            InfoDemoElementState[] grabbed = Sample(timeline, 1.1f);
            Assert.AreEqual(from, grabbed[face].Position);
            Assert.AreEqual(1f, grabbed[face].Alpha, TOLERANCE);
            Assert.Less(grabbed[face].Scale, 1f, "pressed");

            InfoDemoElementState[] moving = Sample(timeline, 1.5f);
            Assert.Greater(moving[face].Position.x, from.x);
            Assert.Less(moving[face].Position.x, to.x);
            Assert.Less(moving[face].Scale, 1f, "still pressed while dragging");

            InfoDemoElementState[] released = Sample(timeline, 1.85f);
            Assert.AreEqual(to, released[face].Position);

            Assert.AreEqual(0f, Sample(timeline, 3f)[face].Alpha, TOLERANCE, "lifted away");
        }

        [Test]
        public void FlyTo_ArcsAboveTheStraightLine_LandsOnTheTarget_AndIsGone()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            Vector2 from = new Vector2(0f, 8.5f);
            Vector2 to = new Vector2(4f, 4f);
            int coin = InfoDemoHudChoreography.FlyTo(builder, InfoDemoSprite.Coin, 0, 0.7f, from, to, 1f, 0.4f, 1.2f);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(InfoDemoSprite.Coin, timeline.GetElement(coin).Sprite);
            Assert.IsTrue(timeline.GetElement(coin).OnTop);
            Assert.AreEqual(0f, Sample(timeline, 0.9f)[coin].Alpha, TOLERANCE, "not launched yet");

            InfoDemoElementState[] launched = Sample(timeline, 1f);
            Assert.AreEqual(from, launched[coin].Position);

            InfoDemoElementState[] apex = Sample(timeline, 1.2f);
            Vector2 straightMidpoint = (from + to) * 0.5f;
            Assert.AreEqual(straightMidpoint.y - 1.2f, apex[coin].Position.y, TOLERANCE, "arcs above the line");
            Assert.Greater(apex[coin].Alpha, 0.9f);

            InfoDemoElementState[] landed = Sample(timeline, 1.4f);
            Assert.AreEqual(0f, Vector2.Distance(to, landed[coin].Position), TOLERANCE);
            Assert.AreEqual(0f, landed[coin].Alpha, TOLERANCE);
        }

        [Test]
        public void CountdownBar_DrainsLinearly_TowardsItsFixedLeftEdge()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            Vector2 centre = new Vector2(5f, 1f);
            const float length = 2f;
            int fill = InfoDemoHudChoreography.CountdownBar(
                builder, centre, length, 0.1f, InfoDemoPaint.PLATE_DOUBLE, InfoDemoPaint.INK, 0.2f, 0.5f, 1f, 5f);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(0f, Sample(timeline, 0.4f)[fill].Alpha, TOLERANCE, "appears with its pill");

            float leftEdge = centre.x - (length * 0.5f);
            float[] times = { 1f, 2f, 3f, 4.9f };
            float[] expectedFractions = { 1f, 0.75f, 0.5f, 0.025f };
            for (int sampleIndex = 0; sampleIndex < times.Length; sampleIndex++)
            {
                InfoDemoElementState state = Sample(timeline, times[sampleIndex])[fill];
                Assert.AreEqual(1f, state.Alpha, TOLERANCE);
                Assert.AreEqual(expectedFractions[sampleIndex], state.Stretch.x, 0.001f, $"t={times[sampleIndex]}");
                Assert.AreEqual(1f, state.Stretch.y, TOLERANCE);

                float drawnLeft = state.Position.x - (length * state.Stretch.x * 0.5f);
                Assert.AreEqual(leftEdge, drawnLeft, 0.001f, "the left edge never moves");
            }
        }

        [Test]
        public void CountBadge_TicksDownToZero_AndTurnsItsDiscOnEmpty()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            InfoDemoCountBadge badge = InfoDemoHudChoreography.CountBadge(
                builder, new Vector2(6f, 8.5f), 2, InfoDemoPaint.HOLD_BADGE, 0.57f, 0.07f, 0.33f, true);
            InfoDemoHudChoreography.TickBadge(builder, badge, 1f, InfoDemoPaint.OFFER_PINK);
            InfoDemoHudChoreography.TickBadge(builder, badge, 2f, InfoDemoPaint.OFFER_PINK);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(0, badge.Count);
            Assert.AreEqual("2", timeline.GetElement(badge.LabelIdForCount(2)).LabelArgument);
            Assert.AreEqual("1", timeline.GetElement(badge.LabelIdForCount(1)).LabelArgument);
            Assert.AreEqual("0", timeline.GetElement(badge.LabelIdForCount(0)).LabelArgument);
            Assert.IsTrue(timeline.GetElement(badge.DiscId).OnTop);
            Assert.IsTrue(timeline.GetElement(badge.LabelIdForCount(0)).OnTop);

            AssertBadgeShows(timeline, badge, 0.5f, 2);
            Assert.AreEqual(InfoDemoPaint.HOLD_BADGE, Sample(timeline, 1.5f)[badge.DiscId].Paint, "still holds a charge");
            AssertBadgeShows(timeline, badge, 1.5f, 1);
            AssertBadgeShows(timeline, badge, 2.5f, 0);
            Assert.AreEqual(InfoDemoPaint.OFFER_PINK, Sample(timeline, 2.5f)[badge.DiscId].Paint);
        }

        [Test]
        public void PowerUpButton_SpendChargeRepeatedly_CountsItsBadgeAllTheWayDown()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, InfoDemoSprite.Coin, 0, InfoDemoPaint.PLATE_GOLD, 3);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, 1f);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, 1.5f);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, 2f);
            InfoDemoTimeline timeline = builder.Build();

            Assert.AreEqual(InfoDemoSprite.Coin, timeline.GetElement(button.IconId).Sprite);
            AssertBadgeShows(timeline, button.Badge, 0.5f, 3);
            AssertBadgeShows(timeline, button.Badge, 1.4f, 2);
            AssertBadgeShows(timeline, button.Badge, 1.9f, 1);
            AssertBadgeShows(timeline, button.Badge, 3f, 0);
        }

        [Test]
        public void Tap_DrawsItsFingerAndRingOverTheTrayPieces()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            int piece = builder.AddPiece(RotateInfoDemo.PieceShape, InfoDemoPaint.BLOCK_1, new Vector2(4f, 8.5f), 0.42f);
            InfoDemoChoreography.Tap(builder, 1f, new Vector2(4f, 8.5f));
            InfoDemoTimeline timeline = builder.Build();

            Assert.IsFalse(timeline.GetElement(piece).OnTop);
            for (int elementId = piece + 1; elementId < timeline.ElementCount; elementId++)
            {
                Assert.IsTrue(timeline.GetElement(elementId).OnTop, $"element {elementId}");
            }
        }

        // ---------------------------------------------------------------- Rotate

        /// <summary>The demo's before/after shapes are exactly what the real rotation produces: the dealt
        /// piece is the catalog's <c>j_right</c>, and <c>PieceRotator.TryRotateClockwise</c> turns it into
        /// the demo's rotated shape (<c>j_down</c>).</summary>
        [Test]
        public void RotateDemo_TurnsThePieceExactlyAsPieceRotatorDoes()
        {
            Piece dealt = CatalogPieceFor(RotateInfoDemo.PieceShape);
            Assert.AreEqual("j_right", dealt.Id);

            Assert.IsTrue(PieceRotator.TryRotateClockwise(dealt, out Piece rotated));
            Assert.AreEqual("j_down", rotated.Id);
            Assert.AreSame(rotated, CatalogPieceFor(RotateInfoDemo.RotatedShape));
        }

        [Test]
        public void RotateDemo_ClearsRowsSixAndSeven_AndPaysTheChargeOnlyWhenDisarmed()
        {
            InfoDemoTimeline timeline = PowerUpDemo(PowerUpKind.Rotate);
            AssertBoard(Sample(timeline, AFTER_EFFECT), RotateInfoDemo.Rows, (row, column) => row >= 6);

            // Right after landing, both rows are full — the turned piece is what completed them.
            InfoDemoElementState[] landed = Sample(timeline, RotateInfoDemo.CLEAR_START - 0.01f);
            for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
            {
                Assert.AreNotEqual(InfoDemoPaint.NONE, landed[InfoDemoLayout.BoardBlockId(6, column)].Paint);
                Assert.AreNotEqual(InfoDemoPaint.NONE, landed[InfoDemoLayout.BoardBlockId(7, column)].Paint);
            }

            // Turning is a free preview: the count still reads 1 after the turn, 0 once disarmed.
            InfoDemoPowerUpButton button = RotateButtonHandle();
            AssertBadgeShows(timeline, button.Badge, RotateInfoDemo.SWAP_TIME + 0.1f, 1);
            AssertBadgeShows(timeline, button.Badge, RotateInfoDemo.DISARM_TIME + 0.5f, 0);
        }

        // ---------------------------------------------------------------- Reroll

        [Test]
        public void RerollDemo_NothingFitsBefore_SomethingFitsAfter_AndRowSixClears()
        {
            Board board = BoardFrom(RerollInfoDemo.Rows);
            GhostFitSearch search = new GhostFitSearch();

            Assert.IsFalse(
                search.TryFindBestMove(board, CatalogPiecesFor(RerollInfoDemo.OldShapes), false, out GhostFitMove _),
                "none of the old dock fits anywhere");
            Assert.IsTrue(
                search.TryFindBestMove(board, CatalogPiecesFor(RerollInfoDemo.NewShapes), false, out GhostFitMove _),
                "the reroll's draw guarantees at least one fits");

            InfoDemoTimeline timeline = PowerUpDemo(PowerUpKind.Reroll);
            AssertBoard(Sample(timeline, AFTER_EFFECT), RerollInfoDemo.Rows, (row, column) => row == RerollInfoDemo.LAND_ROW);
        }

        // ---------------------------------------------------------------- Double Multiplier

        [Test]
        public void DoubleMultiplierDemo_ScoresMatchTheRealRules_AndRowSevenClears()
        {
            int expected = ScoreRules.PlacementScore(1) + ScoreRules.ClearScore(1, 0);
            Assert.AreEqual(expected, DoubleMultiplierInfoDemo.BASE_SCORE);
            Assert.AreEqual(expected * 2, DoubleMultiplierInfoDemo.DOUBLED_SCORE);

            InfoDemoTimeline timeline = PowerUpDemo(PowerUpKind.DoubleMultiplier);
            AssertBoard(
                Sample(timeline, AFTER_EFFECT), DoubleMultiplierInfoDemo.Rows,
                (row, column) => row == DoubleMultiplierInfoDemo.LAND_ROW);
        }

        // ---------------------------------------------------------------- Ghost Fit

        /// <summary>The demo's silhouette is the move the real search picks on the demo board — most lines
        /// at once (two here) — and exactly where the piece then lands.</summary>
        [Test]
        public void GhostFitDemo_SilhouetteIsTheRealSearchesBestMove_AndRowsSixAndSevenClear()
        {
            Board board = BoardFrom(GhostFitInfoDemo.Rows);
            Piece[] dock = CatalogPiecesFor(GhostFitInfoDemo.DockShapes);

            Assert.IsTrue(new GhostFitSearch().TryFindBestMove(board, dock, false, out GhostFitMove move));
            Assert.AreEqual(GhostFitInfoDemo.SUGGESTED_SLOT, move.SlotIndex);
            Assert.AreEqual(2, move.LineCount);

            HashSet<Vector2Int> searched = new HashSet<Vector2Int>();
            Piece suggested = dock[move.SlotIndex];
            for (int offsetIndex = 0; offsetIndex < suggested.Offsets.Count; offsetIndex++)
            {
                GridPosition cell = move.Anchor + suggested.Offsets[offsetIndex];
                searched.Add(new Vector2Int(cell.X, Board.SIZE - 1 - cell.Y));
            }

            Vector2Int[] silhouette = GhostFitInfoDemo.SilhouetteCells();
            Assert.IsTrue(searched.SetEquals(silhouette), "silhouette == the real search's move");

            InfoDemoTimeline timeline = PowerUpDemo(PowerUpKind.GhostFit);
            InfoDemoElementState[] landed = Sample(timeline, GhostFitInfoDemo.CLEAR_START - 0.01f);
            for (int cellIndex = 0; cellIndex < silhouette.Length; cellIndex++)
            {
                Vector2Int cell = silhouette[cellIndex];
                Assert.AreEqual(
                    InfoDemoPaint.NONE, InfoDemoBoardPattern.PaintAt(GhostFitInfoDemo.Rows, cell.y, cell.x), "was empty");
                Assert.AreNotEqual(
                    InfoDemoPaint.NONE, landed[InfoDemoLayout.BoardBlockId(cell.y, cell.x)].Paint, "the piece landed there");
            }

            AssertBoard(Sample(timeline, AFTER_EFFECT), GhostFitInfoDemo.Rows, (row, column) => row >= 6);
        }

        // ---------------------------------------------------------------- Coin Sower

        [Test]
        public void CoinSowerDemo_SowsThreeCoinCellsOnBlocks_TheClearTakesOne_AndTheWalletPaysOut()
        {
            InfoDemoTimeline timeline = PowerUpDemo(PowerUpKind.CoinSower);

            List<int> coinCells = new List<int>();
            for (int elementId = 0; elementId < timeline.ElementCount; elementId++)
            {
                InfoDemoElement element = timeline.GetElement(elementId);
                if (element.Sprite == InfoDemoSprite.SpecialCellIcon && element.SpriteParameter == (int)SpecialCellKind.Coin)
                {
                    coinCells.Add(elementId);
                }
            }

            Assert.AreEqual(CoinSowerInfoDemo.CoinCells.Length, coinCells.Count, "one coin cell per charge");
            Assert.AreEqual(3, coinCells.Count);

            InfoDemoElementState[] start = Sample(timeline, 0.2f);
            InfoDemoElementState[] sown = Sample(timeline, CoinSowerInfoDemo.ConvertTime(2) + 0.35f);
            InfoDemoElementState[] after = Sample(timeline, AFTER_EFFECT);
            for (int coinIndex = 0; coinIndex < coinCells.Count; coinIndex++)
            {
                Vector2Int cell = CoinSowerInfoDemo.CoinCells[coinIndex];
                int icon = coinCells[coinIndex];
                Assert.AreEqual(InfoDemoLayout.Cell(cell.y, cell.x), start[icon].Position);
                Assert.AreNotEqual(
                    InfoDemoPaint.NONE, InfoDemoBoardPattern.PaintAt(CoinSowerInfoDemo.Rows, cell.y, cell.x),
                    "a coin is sown on a block, never an empty cell");
                Assert.AreEqual(0f, start[icon].Alpha, TOLERANCE, "not sown yet");
                Assert.AreEqual(1f, sown[icon].Alpha, TOLERANCE, "sown");

                bool destroyedByTheClear = cell.y == CoinSowerInfoDemo.LAND_ROW;
                Assert.AreEqual(destroyedByTheClear ? 0f : 1f, after[icon].Alpha, TOLERANCE, $"coin {coinIndex}");
            }

            AssertBoard(after, CoinSowerInfoDemo.Rows, (row, column) => row == CoinSowerInfoDemo.LAND_ROW);

            // The wallet ticks up by one coin cell's payout once its coin arrives.
            string paid = (CoinSowerInfoDemo.WALLET_START + CoinSowerInfoDemo.COIN_CELL_PAYOUT).ToString();
            int paidLabel = FindLiteral(timeline, paid);
            int startLabel = FindLiteral(timeline, CoinSowerInfoDemo.WALLET_START.ToString());
            Assert.AreEqual(1f, Sample(timeline, CoinSowerInfoDemo.PayoutTime() - 0.05f)[startLabel].Alpha, TOLERANCE);
            Assert.AreEqual(1f, after[paidLabel].Alpha, TOLERANCE);
            Assert.AreEqual(0f, after[startLabel].Alpha, TOLERANCE);
        }

        // ---------------------------------------------------------------- Hold

        [Test]
        public void HoldDemo_EndsWithTheTwoByTwoParked_TheJBackInTheTray_AndNoChargesLeft()
        {
            InfoDemoTimeline timeline = HoldInfoDemo.Build(out int firstPiece, out int secondPiece, out InfoDemoCountBadge badge);

            InfoDemoElementState[] held = Sample(timeline, HoldInfoDemo.FIRST_TICK_TIME + 0.5f);
            Assert.AreEqual(HoldInfoDemo.PocketCentre, held[firstPiece].Position, "the J is parked");
            Assert.AreEqual(HoldInfoDemo.SecondSlot, held[secondPiece].Position, "the 2x2 is still in the tray");
            AssertBadgeShows(timeline, badge, HoldInfoDemo.FIRST_TICK_TIME + 0.5f, 1);

            InfoDemoElementState[] after = Sample(timeline, AFTER_EFFECT);
            Assert.AreEqual(HoldInfoDemo.PocketCentre, after[secondPiece].Position, "the 2x2 is parked");
            Assert.AreEqual(HoldInfoDemo.SecondSlot, after[firstPiece].Position, "the J took the 2x2's tray slot");
            Assert.AreEqual(InfoDemoLayout.TRAY_PIECE_SCALE, after[firstPiece].Scale, TOLERANCE);
            Assert.AreEqual(InfoDemoLayout.TRAY_PIECE_SCALE, after[secondPiece].Scale, TOLERANCE);
            Assert.AreEqual(1f, after[firstPiece].Alpha, TOLERANCE);
            Assert.AreEqual(1f, after[secondPiece].Alpha, TOLERANCE);
            AssertBadgeShows(timeline, badge, AFTER_EFFECT, 0);
            Assert.AreEqual(InfoDemoPaint.OFFER_PINK, after[badge.DiscId].Paint);

            AssertBoard(after, HoldInfoDemo.Rows, (row, column) => false);
        }

        // ---------------------------------------------------------------- helpers

        private static void AssertDemo(InfoDemoCatalog catalog, PowerUpKind kind, float loopDuration)
        {
            InfoDemoTimeline first = catalog.Find(InfoPopupSubjectKind.PowerUp, (int)kind);
            Assert.IsNotNull(first, kind.ToString());
            Assert.AreSame(first, catalog.Find(InfoPopupSubjectKind.PowerUp, (int)kind), kind.ToString());
            Assert.AreEqual(loopDuration, first.Duration, TOLERANCE, kind.ToString());
        }

        private static InfoDemoTimeline PowerUpDemo(PowerUpKind kind)
            => new InfoDemoCatalog().Find(InfoPopupSubjectKind.PowerUp, (int)kind);

        /// <summary>A fresh button built the way <see cref="RotateInfoDemo"/> builds its own — right after
        /// the 64 board blocks — so its element ids are the demo's.</summary>
        private static InfoDemoPowerUpButton RotateButtonHandle()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(RotateInfoDemo.LOOP_DURATION);
            return InfoDemoPowerUpChoreography.PowerUpButton(builder, PowerUpKind.Rotate, InfoDemoPaint.PLATE_GOLD, 1);
        }

        private static InfoDemoElementState[] Sample(InfoDemoTimeline timeline, float time)
        {
            InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];
            timeline.Evaluate(time, states);
            return states;
        }

        private static void AssertBadgeShows(InfoDemoTimeline timeline, InfoDemoCountBadge badge, float time, int count)
        {
            InfoDemoElementState[] states = Sample(timeline, time);
            for (int value = 0; value <= badge.StartCount; value++)
            {
                float expected = value == count ? 1f : 0f;
                Assert.AreEqual(expected, states[badge.LabelIdForCount(value)].Alpha, TOLERANCE, $"t={time} label {value}");
            }
        }

        private static int FindLiteral(InfoDemoTimeline timeline, string text)
        {
            for (int elementId = 0; elementId < timeline.ElementCount; elementId++)
            {
                InfoDemoElement element = timeline.GetElement(elementId);
                if (element.Kind == InfoDemoElementKind.Label && element.LabelKey == null && element.LabelArgument == text)
                {
                    return elementId;
                }
            }

            Assert.Fail($"No literal label '{text}'.");
            return -1;
        }

        /// <summary>Every cell <paramref name="isCleared"/> picks is empty; every other cell still holds
        /// its starting paint.</summary>
        private static void AssertBoard(InfoDemoElementState[] states, string[] rows, System.Func<int, int, bool> isCleared)
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

        /// <summary>The real board a demo's pattern rows describe. Demo rows count down from the top; the
        /// board's Y counts up from the bottom.</summary>
        private static Board BoardFrom(string[] rows)
        {
            Board board = new Board();
            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                {
                    int paint = InfoDemoPaint.FromPatternChar(rows[row][column]);
                    if (paint != InfoDemoPaint.NONE)
                    {
                        board.Occupy(new GridPosition(column, Board.SIZE - 1 - row), paint);
                    }
                }
            }

            return board;
        }

        private static Piece[] CatalogPiecesFor(Vector2Int[][] shapes)
        {
            Piece[] pieces = new Piece[shapes.Length];
            for (int shapeIndex = 0; shapeIndex < shapes.Length; shapeIndex++)
            {
                pieces[shapeIndex] = CatalogPieceFor(shapes[shapeIndex]);
            }

            return pieces;
        }

        /// <summary>The catalog piece whose cells are <paramref name="shape"/> — demo (column, row) offsets,
        /// row 0 at the top, flipped into the catalog's Y-up offsets.</summary>
        private static Piece CatalogPieceFor(Vector2Int[] shape)
        {
            InfoDemoLayout.ShapeBounds(shape, out Vector2Int min, out Vector2Int max);
            HashSet<GridPosition> cells = new HashSet<GridPosition>();
            for (int cellIndex = 0; cellIndex < shape.Length; cellIndex++)
            {
                cells.Add(new GridPosition(shape[cellIndex].x - min.x, max.y - shape[cellIndex].y));
            }

            IReadOnlyList<Piece> catalog = PieceCatalog.AllPieces;
            for (int pieceIndex = 0; pieceIndex < catalog.Count; pieceIndex++)
            {
                Piece piece = catalog[pieceIndex];
                if (piece.Offsets.Count != cells.Count)
                {
                    continue;
                }

                bool matches = true;
                for (int offsetIndex = 0; offsetIndex < piece.Offsets.Count; offsetIndex++)
                {
                    if (!cells.Contains(piece.Offsets[offsetIndex]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return piece;
                }
            }

            Assert.Fail("No catalog piece has that shape.");
            return null;
        }
    }
}
