using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Presentation.Views;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// The special-cell info demos (issue #450): the catalog mapping, the new Bolt beat, and — for each
    /// demo — that it shows what the real special cell does, checked against the real Core code wherever
    /// there is some to check against: the demo board is rebuilt as a real <see cref="Board"/>, the demo's
    /// placement is played on it, and <see cref="CascadeClearResolver"/> with the kind's real effect
    /// resolves it; the demo must end on exactly that board.
    /// </summary>
    public class InfoDemoSpecialCellTests
    {
        private const float TOLERANCE = 0.0001f;

        /// <summary>After every demo's effect has settled and before any loop fade-out starts.</summary>
        private const float AFTER_EFFECT = 4.4f;

        private const string CURRENCY_CONFIG_PATH = "Assets/Settings/CurrencyConfig.asset";

        private static readonly SpecialCellKind[] DemoKinds =
        {
            SpecialCellKind.ExplosiveCore,
            SpecialCellKind.Laser,
            SpecialCellKind.ScoreGem,
            SpecialCellKind.Vortex,
            SpecialCellKind.ChainLightning,
            SpecialCellKind.Coin,
            SpecialCellKind.Timer,
        };

        // ---------------------------------------------------------------- catalog

        [Test]
        public void EverySpecialCellInThisSlice_HasItsOwnCachedDemo()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();
            HashSet<InfoDemoTimeline> seen = new HashSet<InfoDemoTimeline>();

            for (int kindIndex = 0; kindIndex < DemoKinds.Length; kindIndex++)
            {
                SpecialCellKind kind = DemoKinds[kindIndex];
                InfoDemoTimeline first = catalog.Find(InfoPopupSubjectKind.SpecialCell, (int)kind);
                Assert.IsNotNull(first, kind.ToString());
                Assert.AreSame(first, catalog.Find(InfoPopupSubjectKind.SpecialCell, (int)kind), kind.ToString());
                Assert.IsTrue(seen.Add(first), kind + " has its own demo");
            }

            Assert.AreEqual(ExplosiveCoreInfoDemo.LOOP_DURATION, Demo(SpecialCellKind.ExplosiveCore).Duration, TOLERANCE);
            Assert.AreEqual(LaserInfoDemo.LOOP_DURATION, Demo(SpecialCellKind.Laser).Duration, TOLERANCE);
            Assert.AreEqual(ScoreGemInfoDemo.LOOP_DURATION, Demo(SpecialCellKind.ScoreGem).Duration, TOLERANCE);
            Assert.AreEqual(ChainLightningInfoDemo.LOOP_DURATION, Demo(SpecialCellKind.ChainLightning).Duration, TOLERANCE);
            Assert.AreEqual(CoinCellInfoDemo.LOOP_DURATION, Demo(SpecialCellKind.Coin).Duration, TOLERANCE);
            Assert.AreEqual(TimerInfoDemo.LOOP_DURATION, Demo(SpecialCellKind.Timer).Duration, TOLERANCE);
        }

        [Test]
        public void SpecialCellsOutsideThisSlice_HaveNoDemo()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();
            Array kinds = Enum.GetValues(typeof(SpecialCellKind));

            for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
            {
                SpecialCellKind kind = (SpecialCellKind)kinds.GetValue(kindIndex);
                if (Array.IndexOf(DemoKinds, kind) >= 0)
                {
                    continue;
                }

                Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialCell, (int)kind), kind.ToString());
            }

            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialCell, (int)SpecialCellKind.None));
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialCell, -1));
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialCell, 999));
        }

        [Test]
        public void EachDemo_DrawsItsSpecialCellWithTheRealIcon()
        {
            for (int kindIndex = 0; kindIndex < DemoKinds.Length; kindIndex++)
            {
                SpecialCellKind kind = DemoKinds[kindIndex];
                InfoDemoTimeline timeline = Demo(kind);
                bool found = false;
                for (int elementId = 0; elementId < timeline.ElementCount; elementId++)
                {
                    InfoDemoElement element = timeline.GetElement(elementId);
                    if (element.Sprite == InfoDemoSprite.SpecialCellIcon && element.SpriteParameter == (int)kind)
                    {
                        found = true;
                        break;
                    }
                }

                Assert.IsTrue(found, kind.ToString());
            }
        }

        [Test]
        public void SpecialCellGlowPaints_RoundTripTheirKind()
        {
            Array kinds = Enum.GetValues(typeof(SpecialCellKind));
            for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
            {
                SpecialCellKind kind = (SpecialCellKind)kinds.GetValue(kindIndex);
                int paint = InfoDemoPaint.SpecialCellGlow(kind);
                Assert.IsTrue(InfoDemoPaint.TryGetSpecialCellGlow(paint, out SpecialCellKind back), kind.ToString());
                Assert.AreEqual(kind, back);
            }

            Assert.IsFalse(InfoDemoPaint.TryGetSpecialCellGlow(InfoDemoPaint.OFFER_PINK, out SpecialCellKind _));
            Assert.IsFalse(InfoDemoPaint.TryGetSpecialCellGlow(InfoDemoPaint.BLOCK_1, out SpecialCellKind _));
        }

        // ---------------------------------------------------------------- Bolt beat

        [Test]
        public void Bolt_GrowsFromItsSourceTowardsItsTarget_ThenFades()
        {
            Vector2 from = new Vector2(4f, 7f);
            Vector2 to = new Vector2(1f, 3f);
            const float start = 1f;

            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(5f);
            int core = InfoDemoChoreography.Bolt(builder, from, to, InfoDemoPaint.ARMED, start);
            InfoDemoTimeline timeline = builder.Build();

            InfoDemoElement element = timeline.GetElement(core);
            Assert.AreEqual(InfoDemoElementKind.Panel, element.Kind);
            Assert.AreEqual((to - from).magnitude, element.Size.x, TOLERANCE, "the bar is the segment's length");

            // Before it fires: hidden.
            Assert.AreEqual(0f, Sample(timeline, start - 0.1f)[core].Alpha, TOLERANCE);

            // Growing: the source end is pinned while the bar reaches out.
            float expectedAngle = Mathf.Atan2(-(to.y - from.y), to.x - from.x) * Mathf.Rad2Deg;
            Vector2 unit = (to - from).normalized;
            float[] probes = { 0.3f, 0.6f };
            for (int probeIndex = 0; probeIndex < probes.Length; probeIndex++)
            {
                InfoDemoElementState state = Sample(
                    timeline, start + (InfoDemoChoreography.BOLT_GROW_DURATION * probes[probeIndex]))[core];
                Assert.AreEqual(expectedAngle, state.Rotation, TOLERANCE);
                Assert.Greater(state.Stretch.x, 0f);
                Assert.Less(state.Stretch.x, 1f);
                Assert.AreEqual(1f, state.Stretch.y, TOLERANCE);

                Vector2 sourceEnd = state.Position - (unit * (element.Size.x * 0.5f * state.Stretch.x));
                Assert.AreEqual(from.x, sourceEnd.x, 0.001f, "source end stays put");
                Assert.AreEqual(from.y, sourceEnd.y, 0.001f, "source end stays put");
            }

            // Arrived: full length, centred on the segment, fully lit.
            InfoDemoElementState arrived = Sample(timeline, start + InfoDemoChoreography.BOLT_GROW_DURATION + 0.01f)[core];
            Assert.AreEqual(1f, arrived.Stretch.x, TOLERANCE);
            Assert.AreEqual((from + to) * 0.5f, arrived.Position);
            Assert.AreEqual(1f, arrived.Alpha, TOLERANCE);

            // Gone after its hold and fade.
            float gone = start + InfoDemoChoreography.BOLT_GROW_DURATION + InfoDemoChoreography.BOLT_HOLD
                + InfoDemoChoreography.BOLT_FADE_DURATION + 0.01f;
            Assert.AreEqual(0f, Sample(timeline, gone)[core].Alpha, TOLERANCE);
        }

        [Test]
        public void BoltAngle_MapsBoardDirectionsToScreenRotations()
        {
            Assert.AreEqual(0f, InfoDemoChoreography.BoltAngle(Vector2.zero, new Vector2(1f, 0f)), TOLERANCE, "right");
            Assert.AreEqual(90f, InfoDemoChoreography.BoltAngle(Vector2.zero, new Vector2(0f, -1f)), TOLERANCE, "up a row");
            Assert.AreEqual(-90f, InfoDemoChoreography.BoltAngle(Vector2.zero, new Vector2(0f, 1f)), TOLERANCE, "down a row");
        }

        // ---------------------------------------------------------------- Explosive Core

        [Test]
        public void ExplosiveCoreDemo_EndsOnTheBoardTheRealEffectLeaves()
        {
            Board board = BoardFrom(ExplosiveCoreInfoDemo.Rows);
            board.SetSpecialKind(
                ToGrid(ExplosiveCoreInfoDemo.CORE_ROW, ExplosiveCoreInfoDemo.CORE_COLUMN), SpecialCellKind.ExplosiveCore);
            board.Occupy(ToGrid(ExplosiveCoreInfoDemo.LAND_ROW, ExplosiveCoreInfoDemo.LAND_COLUMN), 1);

            ExplosiveCoreEffect effect = new ExplosiveCoreEffect();
            effect.BeginResolution();
            CascadeClearResult cascade = CascadeClearResolver.ResolveCascade(board, effect);

            // The placement closed a column only — so the core was destroyed by a column and wipes its row.
            Assert.AreEqual(0, cascade.Primary.ClearedRows.Count, "no row completes");
            Assert.AreEqual(1, cascade.Primary.ClearedColumns.Count);
            Assert.AreEqual(ExplosiveCoreInfoDemo.CORE_COLUMN, cascade.Primary.ClearedColumns[0]);
            Assert.AreEqual(ExplosiveCoreInfoDemo.WipedCells().Length, effect.WipedCells.Count);
            Assert.AreEqual(6, effect.WipedCells.Count);
            Assert.AreEqual(ScoreRules.PlacementScore(effect.WipedCells.Count), ExplosiveCoreInfoDemo.BonusScore());

            InfoDemoElementState[] after = Sample(Demo(SpecialCellKind.ExplosiveCore), AFTER_EFFECT);
            AssertBoardMatches(after, board);
            AssertBoard(
                after, ExplosiveCoreInfoDemo.Rows,
                (row, column) => column == ExplosiveCoreInfoDemo.CORE_COLUMN || row == ExplosiveCoreInfoDemo.CORE_ROW);
        }

        // ---------------------------------------------------------------- Laser

        [Test]
        public void LaserDemo_EndsOnTheBoardTheRealEffectLeaves()
        {
            Board board = BoardFrom(LaserInfoDemo.Rows);
            board.SetSpecialKind(ToGrid(LaserInfoDemo.LASER_ROW, LaserInfoDemo.LASER_COLUMN), SpecialCellKind.Laser);
            board.Occupy(ToGrid(LaserInfoDemo.LAND_ROW, LaserInfoDemo.LAND_COLUMN), 3);

            LaserEffect effect = new LaserEffect();
            effect.BeginResolution();
            CascadeClearResult cascade = CascadeClearResolver.ResolveCascade(board, effect);

            // A row clear took the laser, so it wiped its column.
            Assert.AreEqual(1, cascade.Primary.ClearedRows.Count);
            Assert.AreEqual(0, cascade.Primary.ClearedColumns.Count);
            Assert.AreEqual(LaserInfoDemo.WipedCells().Length, effect.WipedCells.Count);

            InfoDemoElementState[] after = Sample(Demo(SpecialCellKind.Laser), AFTER_EFFECT);
            AssertBoardMatches(after, board);
            AssertBoard(
                after, LaserInfoDemo.Rows,
                (row, column) => row == LaserInfoDemo.LASER_ROW || column == LaserInfoDemo.LASER_COLUMN);
        }

        // ---------------------------------------------------------------- Score Gem

        [Test]
        public void ScoreGemDemo_ScoresMatchTheRealRules_AndOnlyRowSevenClears()
        {
            int expected = ScoreRules.PlacementScore(1) + ScoreRules.ClearScore(1, 0);
            Assert.AreEqual(expected, ScoreGemInfoDemo.BASE_SCORE);
            Assert.AreEqual(11, ScoreGemInfoDemo.BASE_SCORE);
            Assert.AreEqual(ScoreRules.ScoreGemMultiplied(expected, 1), ScoreGemInfoDemo.MULTIPLIED_SCORE);
            Assert.AreEqual(33, ScoreGemInfoDemo.MULTIPLIED_SCORE);

            Board board = BoardFrom(ScoreGemInfoDemo.Rows);
            board.SetSpecialKind(ToGrid(ScoreGemInfoDemo.GEM_ROW, ScoreGemInfoDemo.GEM_COLUMN), SpecialCellKind.ScoreGem);
            board.Occupy(ToGrid(ScoreGemInfoDemo.LAND_ROW, ScoreGemInfoDemo.LAND_COLUMN), 5);

            ScoreGemEffect effect = new ScoreGemEffect();
            effect.BeginResolution();
            CascadeClearResult cascade = CascadeClearResolver.ResolveCascade(board, effect);
            Assert.AreEqual(1, cascade.Primary.LineCount);
            Assert.AreEqual(1, effect.DestroyedCount, "the gem went with the clear");

            InfoDemoElementState[] after = Sample(Demo(SpecialCellKind.ScoreGem), AFTER_EFFECT);
            AssertBoardMatches(after, board);
            AssertBoard(after, ScoreGemInfoDemo.Rows, (row, column) => row == ScoreGemInfoDemo.GEM_ROW);
        }

        // ---------------------------------------------------------------- Chain Lightning

        [Test]
        public void ChainLightningDemo_StrikesExactlyTheMaximumOfOccupiedCells_AndEndsWithThemAndRowSevenEmpty()
        {
            Vector2Int[] targets = ChainLightningInfoDemo.Targets;
            Assert.AreEqual(ChainLightningEffect.MAX_TARGETS_PER_STRIKE, targets.Length);
            Assert.AreEqual(5, targets.Length);

            HashSet<Vector2Int> distinct = new HashSet<Vector2Int>(targets);
            Assert.AreEqual(targets.Length, distinct.Count, "no cell struck twice");
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                Vector2Int cell = targets[targetIndex];
                Assert.AreNotEqual(
                    InfoDemoPaint.NONE, InfoDemoBoardPattern.PaintAt(ChainLightningInfoDemo.Rows, cell.y, cell.x),
                    $"target {cell} starts occupied");
                Assert.AreNotEqual(ChainLightningInfoDemo.LIGHTNING_ROW, cell.y, "targets survive the row clear");
            }

            // The real strike: after the row clear the board still holds more occupied cells than the
            // strike takes, so it vaporizes exactly the maximum, never fewer.
            Board board = BoardFrom(ChainLightningInfoDemo.Rows);
            board.SetSpecialKind(
                ToGrid(ChainLightningInfoDemo.LIGHTNING_ROW, ChainLightningInfoDemo.LIGHTNING_COLUMN),
                SpecialCellKind.ChainLightning);
            board.Occupy(ToGrid(ChainLightningInfoDemo.LAND_ROW, ChainLightningInfoDemo.LAND_COLUMN), 1);
            ChainLightningEffect effect = new ChainLightningEffect(new System.Random(7));
            effect.BeginResolution();
            CascadeClearResult cascade = CascadeClearResolver.ResolveCascade(board, effect);
            Assert.AreEqual(1, cascade.Primary.LineCount);
            Assert.AreEqual(ChainLightningEffect.MAX_TARGETS_PER_STRIKE, effect.VaporizedCells.Count);

            InfoDemoElementState[] after = Sample(Demo(SpecialCellKind.ChainLightning), AFTER_EFFECT);
            AssertBoard(
                after, ChainLightningInfoDemo.Rows,
                (row, column) => row == ChainLightningInfoDemo.LIGHTNING_ROW || distinct.Contains(new Vector2Int(column, row)));
        }

        [Test]
        public void ChainLightningDemo_EachTargetGoesAsItsBoltArrives()
        {
            InfoDemoTimeline timeline = Demo(SpecialCellKind.ChainLightning);
            for (int targetIndex = 0; targetIndex < ChainLightningInfoDemo.Targets.Length; targetIndex++)
            {
                Vector2Int cell = ChainLightningInfoDemo.Targets[targetIndex];
                int blockId = InfoDemoLayout.BoardBlockId(cell.y, cell.x);
                float arrives = ChainLightningInfoDemo.BoltTime(targetIndex) + InfoDemoChoreography.BOLT_GROW_DURATION;

                Assert.AreNotEqual(InfoDemoPaint.NONE, Sample(timeline, arrives - 0.01f)[blockId].Paint, $"target {targetIndex}");
                Assert.AreEqual(InfoDemoPaint.NONE, Sample(timeline, arrives + 0.5f)[blockId].Paint, $"target {targetIndex}");
            }
        }

        // ---------------------------------------------------------------- Coin

        [Test]
        public void CoinDemo_RowSixClears_AndTheWalletGainsTheShippedPayout()
        {
#if UNITY_EDITOR
            CurrencyConfig config = AssetDatabase.LoadAssetAtPath<CurrencyConfig>(CURRENCY_CONFIG_PATH);
            Assert.IsNotNull(config, CURRENCY_CONFIG_PATH);
            Assert.AreEqual(config.CoinCellPayout, CoinSowerInfoDemo.COIN_CELL_PAYOUT, "the mirrored payout is the shipped one");
#endif

            // The real payout for this clear: a single row takes the coin, so it is not doubled.
            Board board = BoardFrom(CoinCellInfoDemo.Rows);
            board.SetSpecialKind(ToGrid(CoinCellInfoDemo.COIN_ROW, CoinCellInfoDemo.COIN_COLUMN), SpecialCellKind.Coin);
            board.Occupy(ToGrid(CoinCellInfoDemo.LAND_ROW, CoinCellInfoDemo.LAND_COLUMN), 3);
            CoinEffect effect = new CoinEffect(CoinSowerInfoDemo.COIN_CELL_PAYOUT);
            effect.BeginResolution();
            CascadeClearResolver.ResolveCascade(board, effect);
            Assert.AreEqual(CoinSowerInfoDemo.COIN_CELL_PAYOUT, effect.TotalCoinsAwarded);

            InfoDemoTimeline timeline = Demo(SpecialCellKind.Coin);
            InfoDemoElementState[] after = Sample(timeline, AFTER_EFFECT);
            AssertBoardMatches(after, board);
            AssertBoard(after, CoinCellInfoDemo.Rows, (row, column) => row == CoinCellInfoDemo.COIN_ROW);

            string startText = CoinSowerInfoDemo.WALLET_START.ToString();
            string paidText = (CoinSowerInfoDemo.WALLET_START + CoinSowerInfoDemo.COIN_CELL_PAYOUT).ToString();
            int startLabel = FindLiteral(timeline, startText);
            int paidLabel = FindLiteral(timeline, paidText);
            InfoDemoElementState[] beforePayout = Sample(timeline, CoinCellInfoDemo.PayoutTime() - 0.05f);
            Assert.AreEqual(1f, beforePayout[startLabel].Alpha, TOLERANCE);
            Assert.AreEqual(0f, beforePayout[paidLabel].Alpha, TOLERANCE);
            Assert.AreEqual(0f, after[startLabel].Alpha, TOLERANCE);
            Assert.AreEqual(1f, after[paidLabel].Alpha, TOLERANCE);
        }

        // ---------------------------------------------------------------- Timer

        [Test]
        public void TimerDemo_TicksOnThePlacementThatClearsNothing_AndClearsBeforeReachingZero()
        {
            InfoDemoTimeline timeline = TimerInfoDemo.Build(out InfoDemoCountBadge badge);
            Assert.AreEqual(TimerInfoDemo.START_COUNTDOWN, badge.StartCount);
            Assert.GreaterOrEqual(TimerInfoDemo.START_COUNTDOWN, TimerCellAuthoring.MIN_STARTING_COUNTDOWN);
            Assert.LessOrEqual(TimerInfoDemo.START_COUNTDOWN, TimerCellAuthoring.MAX_STARTING_COUNTDOWN);

            AssertBadgeShows(timeline, badge, 0.2f, 3);
            AssertBadgeShows(timeline, badge, TimerInfoDemo.TickTime() + 0.4f, 2);
            AssertBadgeShows(timeline, badge, TimerInfoDemo.CLEAR_START - 0.05f, 2);

            // The real sequence: the first placement clears nothing and ticks 3 → 2; the second clears row 7
            // (the tick runs after the clear), so the timer is gone before it could reach 0.
            Board board = BoardFrom(TimerInfoDemo.Rows);
            GridPosition timer = ToGrid(TimerInfoDemo.TIMER_ROW, TimerInfoDemo.TIMER_COLUMN);
            board.Clear(timer);
            board.OccupyTimer(timer, 5, TimerInfoDemo.START_COUNTDOWN);
            List<GridPosition> expired = new List<GridPosition>();

            board.Occupy(ToGrid(TimerInfoDemo.FIRST_LAND_ROW, TimerInfoDemo.FIRST_LAND_COLUMN), 5);
            board.Occupy(ToGrid(TimerInfoDemo.FIRST_LAND_ROW, TimerInfoDemo.FIRST_LAND_COLUMN + 1), 5);
            Assert.IsFalse(CascadeClearResolver.ResolveCascade(board).Primary.AnyCleared, "the 1x2 clears nothing");
            TimerCellTick.Tick(board, expired);
            Assert.AreEqual(TimerInfoDemo.START_COUNTDOWN - 1, board.GetTimerCountdown(timer));

            board.Occupy(ToGrid(TimerInfoDemo.SECOND_LAND_ROW, TimerInfoDemo.SECOND_LAND_COLUMN), 3);
            TimerCellClearEffect clearEffect = new TimerCellClearEffect();
            clearEffect.BeginResolution();
            CascadeClearResolver.ResolveCascade(board, clearEffect);
            Assert.AreEqual(1, clearEffect.DestroyedCount, "cleared in time");
            Assert.IsFalse(board.IsOccupied(timer));
            TimerCellTick.Tick(board, expired);
            Assert.AreEqual(0, expired.Count, "nothing expired");

            InfoDemoElementState[] after = Sample(timeline, AFTER_EFFECT);
            Assert.AreEqual(0f, after[badge.RimId].Alpha, TOLERANCE, "the badge left with its cell");
            Assert.AreEqual(0f, after[badge.DiscId].Alpha, TOLERANCE);
            for (int count = 0; count <= badge.StartCount; count++)
            {
                Assert.AreEqual(0f, after[badge.LabelIdForCount(count)].Alpha, TOLERANCE, $"label {count}");
            }

            AssertBoardMatches(after, board);
            AssertBoard(
                after, TimerInfoDemo.Rows,
                (row, column) => row == TimerInfoDemo.TIMER_ROW, TimerInfoDemo.FIRST_LAND_ROW,
                TimerInfoDemo.FIRST_LAND_COLUMN, 2);
        }

        // ---------------------------------------------------------------- helpers

        private static InfoDemoTimeline Demo(SpecialCellKind kind)
            => new InfoDemoCatalog().Find(InfoPopupSubjectKind.SpecialCell, (int)kind);

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

        /// <summary>Every cell <paramref name="isCleared"/> picks is empty; every other cell still holds its
        /// starting paint — except the <paramref name="placedLength"/> cells of a horizontal placement that
        /// starts at (<paramref name="placedRow"/>, <paramref name="placedColumn"/>), which are filled.</summary>
        private static void AssertBoard(
            InfoDemoElementState[] states, string[] rows, Func<int, int, bool> isCleared,
            int placedRow = -1, int placedColumn = -1, int placedLength = 0)
        {
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    int actual = states[InfoDemoLayout.BoardBlockId(row, column)].Paint;
                    bool placedHere = row == placedRow && column >= placedColumn && column < placedColumn + placedLength;
                    if (isCleared(row, column))
                    {
                        Assert.AreEqual(InfoDemoPaint.NONE, actual, $"({row},{column}) cleared");
                    }
                    else if (placedHere)
                    {
                        Assert.AreNotEqual(InfoDemoPaint.NONE, actual, $"({row},{column}) placed");
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

        /// <summary>Demo (row, column) — rows counting down from the top — as a board position, whose Y counts
        /// up from the bottom.</summary>
        private static GridPosition ToGrid(int row, int column) => new GridPosition(column, Board.SIZE - 1 - row);

        /// <summary>The real board a demo's pattern rows describe.</summary>
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
                        board.Occupy(ToGrid(row, column), paint);
                    }
                }
            }

            return board;
        }
    }
}
