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
    /// Covers the "2x frenzy" window: that it doubles every score gain in the run while open — a
    /// placement's total and a power-up clear's alike — that it stops counting down while the run is
    /// paused, that it stops doubling once it expires, and that doubling nothing still yields nothing.
    /// <para>
    /// The countdown is driven through <see cref="DoubleMultiplierSystem.Advance"/> with explicit
    /// deltas rather than through <c>ITickable.Tick</c>'s <c>Time.deltaTime</c>, so every one of these
    /// is a plain synchronous EditMode test with no frames, no wall clock and no tolerance windows.
    /// </para>
    /// </summary>
    public class DoubleMultiplierTests
    {
        private const string HIGH_SCORE_PREFS_KEY = "Score.HighScore";

        private DoubleMultiplierModel _doubleMultiplierModel;
        private DoubleMultiplierSystem _doubleMultiplierSystem;
        private RunPauseModel _runPauseModel;
        private TimerRunSystem _timerRunSystem;
        private ScoreModel _scoreModel;
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;
        private TestMessageBroker<PowerUpAppliedMessage> _appliedBroker;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private TestMessageBroker<ScoreChangedMessage> _scoreChangedBroker;
        private TestMessageBroker<BonusScoredMessage> _bonusScoredBroker;

        /// <summary>ScoreSystem loads (and, in endless mode, writes) the persisted high score, so a
        /// value left behind by another test would leak into these.</summary>
        [SetUp]
        public void CreateSystems()
        {
            PlayerPrefs.DeleteKey(HIGH_SCORE_PREFS_KEY);

            _doubleMultiplierModel = new DoubleMultiplierModel();
            _runPauseModel = new RunPauseModel();
            _scoreModel = new ScoreModel();
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
            _appliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _scoreChangedBroker = new TestMessageBroker<ScoreChangedMessage>();
            _bonusScoredBroker = new TestMessageBroker<BonusScoredMessage>();

            _doubleMultiplierSystem = new DoubleMultiplierSystem(
                _doubleMultiplierModel, _runPauseModel, _runStartedBroker, _gameOverBroker);
            _timerRunSystem = CreateTimerRunSystem();
        }

        [TearDown]
        public void ClearPersistedHighScore() => PlayerPrefs.DeleteKey(HIGH_SCORE_PREFS_KEY);

        // --- The window itself -------------------------------------------------------------------

        [Test]
        public void Advance_ShorterThanTheWindow_LeavesItOpen()
        {
            _doubleMultiplierSystem.Activate();

            _doubleMultiplierSystem.Advance(DoubleMultiplierModel.WINDOW_SECONDS - 1f);

            Assert.IsTrue(_doubleMultiplierModel.IsActive);
            Assert.AreEqual(1f, _doubleMultiplierModel.RemainingSeconds.Value, 0.0001f);
        }

        [Test]
        public void Advance_PastTheWindow_ClosesItAndClampsAtZero()
        {
            _doubleMultiplierSystem.Activate();

            _doubleMultiplierSystem.Advance(DoubleMultiplierModel.WINDOW_SECONDS + 5f);

            Assert.IsFalse(_doubleMultiplierModel.IsActive);
            Assert.AreEqual(0f, _doubleMultiplierModel.RemainingSeconds.Value);
        }

        /// <summary>AC2: a modal panel holds the window exactly as it holds the timed-mode countdown —
        /// the same <see cref="RunPauseModel"/> the settings, level-path and badges panels all write.</summary>
        [Test]
        public void Advance_WhilePaused_BurnsNothingAndResumesOnClose()
        {
            _doubleMultiplierSystem.Activate();
            _doubleMultiplierSystem.Advance(5f);

            _timerRunSystem.SetMenuPaused(true);
            _doubleMultiplierSystem.Advance(60f);

            Assert.IsTrue(_doubleMultiplierModel.IsActive, "A paused run must not burn the window.");
            Assert.AreEqual(
                DoubleMultiplierModel.WINDOW_SECONDS - 5f,
                _doubleMultiplierModel.RemainingSeconds.Value,
                0.0001f);

            _timerRunSystem.SetMenuPaused(false);
            _doubleMultiplierSystem.Advance(4f);

            Assert.AreEqual(
                DoubleMultiplierModel.WINDOW_SECONDS - 9f,
                _doubleMultiplierModel.RemainingSeconds.Value,
                0.0001f);
        }

        /// <summary>Every pause reason holds it, not just the menu one: aiming another power-up is free
        /// time for the timed countdown and must be free time for the frenzy too.</summary>
        [Test]
        public void Advance_WhileAPowerUpIsArmed_BurnsNothing()
        {
            _doubleMultiplierSystem.Activate();
            _timerRunSystem.SetPowerUpArmedPaused(true);

            _doubleMultiplierSystem.Advance(60f);

            Assert.IsTrue(_doubleMultiplierModel.IsActive);
        }

        [Test]
        public void Advance_WithNoWindowOpen_DoesNothing()
        {
            _doubleMultiplierSystem.Advance(60f);

            Assert.IsFalse(_doubleMultiplierModel.IsActive);
            Assert.AreEqual(0f, _doubleMultiplierModel.RemainingSeconds.Value);
        }

        [Test]
        public void RunStarted_ClosesAnOpenWindow()
        {
            _doubleMultiplierSystem.Activate();

            _runStartedBroker.Publish(new RunStartedMessage());

            Assert.IsFalse(_doubleMultiplierModel.IsActive);
        }

        [Test]
        public void GameOver_ClosesAnOpenWindow()
        {
            _doubleMultiplierSystem.Activate();

            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));

            Assert.IsFalse(_doubleMultiplierModel.IsActive);
        }

        // --- Placements (ScoreSystem) ------------------------------------------------------------

        /// <summary>
        /// AC1, placements half: the finished total is doubled, not any one rule in the stack. The
        /// whole additive stack is exercised — placement points plus a line clear — and the assertion
        /// is against twice what those rules pay together.
        /// </summary>
        [Test]
        public void OnPiecePlaced_DuringTheWindow_DoublesTheScoreGained()
        {
            CreateScoreSystem(new PlacementScoreRule(), new LineClearScoreRule());
            _doubleMultiplierSystem.Activate();

            int gained = ScoreFor(ClearingPlacement());

            int expectedUndoubled = ScoreRules.PlacementScore(4)
                + ScoreRules.ClearScore(lines: 1, streak: 0);
            Assert.AreEqual(expectedUndoubled * DoubleMultiplierModel.SCORE_FACTOR, gained);
            Assert.AreEqual(gained, _scoreModel.Score.Value);
        }

        /// <summary>AC4: the window expiring is what stops the doubling — the very next placement after
        /// it runs out is credited the plain total.</summary>
        [Test]
        public void OnPiecePlaced_AfterTheWindowExpires_IsNotDoubled()
        {
            CreateScoreSystem(new PlacementScoreRule(), new LineClearScoreRule());
            _doubleMultiplierSystem.Activate();
            _doubleMultiplierSystem.Advance(DoubleMultiplierModel.WINDOW_SECONDS);

            int gained = ScoreFor(ClearingPlacement());

            Assert.AreEqual(
                ScoreRules.PlacementScore(4) + ScoreRules.ClearScore(lines: 1, streak: 0),
                gained);
        }

        /// <summary>AC4 again, from the other side: expiry is not instantaneous at activation — a
        /// placement one second in is still inside the window.</summary>
        [Test]
        public void OnPiecePlaced_JustBeforeTheWindowExpires_IsStillDoubled()
        {
            CreateScoreSystem(new PlacementScoreRule());
            _doubleMultiplierSystem.Activate();
            _doubleMultiplierSystem.Advance(DoubleMultiplierModel.WINDOW_SECONDS - 0.5f);

            int gained = ScoreFor(ClearingPlacement());

            Assert.AreEqual(
                ScoreRules.PlacementScore(4) * DoubleMultiplierModel.SCORE_FACTOR, gained);
        }

        /// <summary>AC3: a placement no rule pays for scores zero, doubled or not — there is no
        /// non-zero floor hiding in the multiplier.</summary>
        [Test]
        public void OnPiecePlaced_ThatScoresNothing_StaysZeroDuringTheWindow()
        {
            CreateScoreSystem();
            _doubleMultiplierSystem.Activate();

            int gained = ScoreFor(ClearingPlacement());

            Assert.AreEqual(0, gained);
            Assert.AreEqual(0, _scoreModel.Score.Value);
        }

        /// <summary>
        /// The bonus subtotal behind <see cref="BonusScoredMessage"/> is a presentation-only slice of
        /// the same total, so it has to be doubled with it: a celebration announcing the undoubled
        /// bonus would be describing points the player was never credited. Asserted as a slice, not
        /// just as a number — the doubled bonus must still be a part of the doubled total.
        /// </summary>
        [Test]
        public void OnPiecePlaced_DuringTheWindow_DoublesTheBonusSubtotalWithTheTotal()
        {
            CreateScoreSystem(new LineClearScoreRule(), new MonochromeScoreRule());
            _doubleMultiplierSystem.Activate();

            int gained = ScoreFor(MonochromePlacement());

            int undoubledBonus = new MonochromeScoreRule().ComputeBonus(new ScorePlacementContext(
                cellCount: 4,
                linesCleared: 1,
                streakBeforePlacement: 0,
                monochromeLineCount: 1,
                multiClearStreakBeforePlacement: 0,
                cumulativeMultiClearCountBeforePlacement: 0,
                boardEmptyAfterPlacement: false));
            Assert.Greater(undoubledBonus, 0, "The placement must earn a bonus for this to prove anything.");

            Assert.AreEqual(1, _bonusScoredBroker.Published.Count);
            Assert.AreEqual(
                undoubledBonus * DoubleMultiplierModel.SCORE_FACTOR,
                _bonusScoredBroker.Published[0].BonusAmount);
            Assert.Less(
                _bonusScoredBroker.Published[0].BonusAmount, gained,
                "The doubled bonus is still only part of the doubled total it came from.");
        }

        // --- Power-up clears (PowerUpScoreSystem) ------------------------------------------------

        /// <summary>AC1, power-ups half: the open note resolved to "the frenzy applies to every score
        /// gain in the run", so a bomb's clear is doubled exactly as a placement's total is.</summary>
        [Test]
        public void OnPowerUpApplied_DuringTheWindow_DoublesTheScoreGained()
        {
            CreatePowerUpScoreSystem();
            _doubleMultiplierSystem.Activate();

            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Bomb, clearedCellCount: 5));

            Assert.AreEqual(
                ScoreRules.PlacementScore(5) * DoubleMultiplierModel.SCORE_FACTOR,
                _scoreModel.Score.Value);
        }

        [Test]
        public void OnPowerUpApplied_AfterTheWindowExpires_IsNotDoubled()
        {
            CreatePowerUpScoreSystem();
            _doubleMultiplierSystem.Activate();
            _doubleMultiplierSystem.Advance(DoubleMultiplierModel.WINDOW_SECONDS);

            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Bomb, clearedCellCount: 5));

            Assert.AreEqual(ScoreRules.PlacementScore(5), _scoreModel.Score.Value);
        }

        /// <summary>AC3 for the power-up path: a clear worth nothing is still worth nothing, and
        /// nothing is published for it.</summary>
        [Test]
        public void OnPowerUpApplied_ThatClearedNothing_StaysZeroDuringTheWindow()
        {
            CreatePowerUpScoreSystem();
            _doubleMultiplierSystem.Activate();

            _appliedBroker.Publish(new PowerUpAppliedMessage(PowerUpKind.Bomb, clearedCellCount: 0));

            Assert.AreEqual(0, _scoreModel.Score.Value);
            Assert.AreEqual(0, _scoreChangedBroker.Published.Count);
        }

        /// <summary>The activation itself clears nothing, so opening a window must never pay the player
        /// for opening it.</summary>
        [Test]
        public void OnPowerUpApplied_TheDoubleMultiplierItself_ScoresNothing()
        {
            CreatePowerUpScoreSystem();
            _doubleMultiplierSystem.Activate();

            _appliedBroker.Publish(new PowerUpAppliedMessage(
                PowerUpKind.DoubleMultiplier, clearedCellCount: 0));

            Assert.AreEqual(0, _scoreModel.Score.Value);
        }

        // --- Helpers -----------------------------------------------------------------------------

        /// <summary>Publishes <paramref name="message"/> and hands back the gain the score system
        /// credited for it, read from the message it published rather than from the running total.</summary>
        private int ScoreFor(PiecePlacedMessage message)
        {
            int publishedBefore = _scoreChangedBroker.Published.Count;
            _piecePlacedBroker.Publish(message);

            Assert.Greater(
                _scoreChangedBroker.Published.Count, publishedBefore,
                "Every placement publishes a score change, even a zero-scoring one.");
            return _scoreChangedBroker.Published[_scoreChangedBroker.Published.Count - 1].Gained;
        }

        /// <summary>A four-cell piece that clears one line — enough for both the placement and the
        /// line-clear rules to pay something, so a doubling is visible in the total.</summary>
        private static PiecePlacedMessage ClearingPlacement()
        {
            return new PiecePlacedMessage(
                pieceId: "line_h4",
                anchor: new GridPosition(0, 0),
                pieceFamily: PieceFamily.Line,
                cellCount: 4,
                colourId: 1,
                linesCleared: 1,
                rowsCleared: 1,
                columnsCleared: 0,
                monochromeLineCount: 0,
                boardEmptyAfterPlacement: false,
                occupiedCellCountBeforeClear: 12,
                anyCornerCleared: false,
                centerCoreEmptyAfterPlacement: false,
                hasIsolatedHolesAfterPlacement: false);
        }

        /// <summary><see cref="ClearingPlacement"/> whose cleared line was all one colour, so a bonus
        /// rule pays something on top of the plain clear and the bonus subtotal is non-zero.</summary>
        private static PiecePlacedMessage MonochromePlacement()
        {
            return new PiecePlacedMessage(
                pieceId: "line_h4",
                anchor: new GridPosition(0, 0),
                pieceFamily: PieceFamily.Line,
                cellCount: 4,
                colourId: 1,
                linesCleared: 1,
                rowsCleared: 1,
                columnsCleared: 0,
                monochromeLineCount: 1,
                boardEmptyAfterPlacement: false,
                occupiedCellCountBeforeClear: 12,
                anyCornerCleared: false,
                centerCoreEmptyAfterPlacement: false,
                hasIsolatedHolesAfterPlacement: false);
        }

        private void CreateScoreSystem(params IScoreRule[] scoreRules)
        {
            BoardSystem boardSystem = CreateBoardSystem();
            ScoreSystem unused = new ScoreSystem(
                _scoreModel,
                new GameModeSystem(new GameModeModel(), boardSystem),
                _doubleMultiplierModel,
                new List<IScoreRule>(scoreRules),
                _piecePlacedBroker,
                _runStartedBroker,
                _scoreChangedBroker,
                new TestMessageBroker<NewRecordMessage>(),
                _bonusScoredBroker);
        }

        private void CreatePowerUpScoreSystem()
        {
            PowerUpScoreSystem unused = new PowerUpScoreSystem(
                _scoreModel, _doubleMultiplierModel, _appliedBroker, _scoreChangedBroker);
        }

        /// <summary>Only ever asked to hold and release the pause flags here; it is never ticked.</summary>
        private TimerRunSystem CreateTimerRunSystem()
        {
            BoardSystem boardSystem = CreateBoardSystem();
            var timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
            return new TimerRunSystem(
                new TimerModel(),
                _runPauseModel,
                new GameModeSystem(new GameModeModel(), boardSystem),
                new TimedModeSystem(new TimedModeModel(), timedModeConfig),
                boardSystem,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        /// <summary>A real, unstarted board system. Nothing here places a piece through it; it exists
        /// only because the systems under test need one to construct.</summary>
        private static BoardSystem CreateBoardSystem()
        {
            return new BoardSystem(
                new BoardModel(),
                new TrayModel(),
                new PerfectRoundModel(),
                new WeightedPieceDraw(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexPulledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                reinforcedCellSeeder: null);
        }
    }
}
