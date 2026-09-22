using System.Reflection;
using MessagePipe;
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
    /// Issue #319: in Timed mode every row/column a placement clears hands +5 seconds back to the
    /// match clock — flat, per line, uncapped — and <see cref="TimerModel.IsLowTime"/> is re-evaluated
    /// on the spot. Every case here is a synchronous message-in, model-out check: nothing depends on
    /// <c>Time.deltaTime</c>, which is 0 outside Play mode, so the whole rule is testable in EditMode.
    /// </summary>
    public sealed class TimerRunSystemLineClearBonusTests
    {
        private const float SECONDS_PER_LINE = 5f;
        private const float LOW_TIME_THRESHOLD = 10f;

        private TimerModel _timerModel;
        private RunPauseModel _runPauseModel;
        private GameModeSystem _gameModeSystem;
        private TimedModeSystem _timedModeSystem;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;
        private TestMessageBroker<TimeExtendedMessage> _timeExtendedBroker;
        private TimedModeConfig _timedModeConfig;
        private CurrencyConfig _currencyConfig;
        private TimerRunSystem _timerRunSystem;

        [SetUp]
        public void CreateModels()
        {
            _timerModel = new TimerModel();
            _runPauseModel = new RunPauseModel();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
            _timeExtendedBroker = new TestMessageBroker<TimeExtendedMessage>();
            _timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
            _currencyConfig = ScriptableObject.CreateInstance<CurrencyConfig>();
        }

        [TearDown]
        public void Cleanup()
        {
            if (_timerRunSystem != null)
            {
                _timerRunSystem.Dispose();
                _timerRunSystem = null;
            }

            if (_timedModeConfig != null)
            {
                Object.DestroyImmediate(_timedModeConfig);
            }

            if (_currencyConfig != null)
            {
                Object.DestroyImmediate(_currencyConfig);
            }
        }

        // --- AC1 / AC7: flat +5s per line, +5×N for N simultaneous lines, every duration alike ---

        [Test]
        public void OneClearedLine_AddsFiveSeconds()
        {
            StartTimedRun(180f);

            PlacePiece(linesCleared: 1);

            Assert.AreEqual(185f, _timerModel.RemainingSeconds.Value);
        }

        [Test]
        public void SimultaneousLines_AddFiveSecondsEach(
            [Values(2, 3, 4)] int linesCleared)
        {
            StartTimedRun(180f);

            PlacePiece(linesCleared);

            Assert.AreEqual(180f + (SECONDS_PER_LINE * linesCleared), _timerModel.RemainingSeconds.Value);
        }

        [Test]
        public void ARowAndAColumn_CountAsTwoLines_NotByCell()
        {
            StartTimedRun(180f);

            // 1 row + 1 column share an intersection cell; the rule is per line, so it is still +10.
            PlacePiece(linesCleared: 2, rowsCleared: 1, columnsCleared: 1);

            Assert.AreEqual(190f, _timerModel.RemainingSeconds.Value);
        }

        [Test]
        public void TheRateIsTheSame_ForEverySelectableDuration(
            [Values(180f, 300f, 600f)] float durationSeconds)
        {
            StartTimedRun(durationSeconds);

            PlacePiece(linesCleared: 1);

            Assert.AreEqual(durationSeconds + SECONDS_PER_LINE, _timerModel.RemainingSeconds.Value);
        }

        [Test]
        public void ConsecutiveClears_Accumulate()
        {
            StartTimedRun(180f);

            PlacePiece(linesCleared: 1);
            PlacePiece(linesCleared: 2);
            PlacePiece(linesCleared: 1);

            Assert.AreEqual(200f, _timerModel.RemainingSeconds.Value);
        }

        // --- AC4: no cap, per clear or per run ---

        [Test]
        public void AddedTime_IsNeverCapped_PerClearOrPerRun()
        {
            StartTimedRun(180f);

            // Far beyond any selectable round length, in one clear and then across many.
            PlacePiece(linesCleared: 8);
            for (int placementIndex = 0; placementIndex < 100; placementIndex++)
            {
                PlacePiece(linesCleared: 4);
            }

            float expected = 180f + (8f * SECONDS_PER_LINE) + (100f * 4f * SECONDS_PER_LINE);
            Assert.AreEqual(expected, _timerModel.RemainingSeconds.Value);
            Assert.Greater(_timerModel.RemainingSeconds.Value, 600f, "Past the longest selectable round: no clamp.");
        }

        // --- AC5: the HUD is told how much, and only when something was added ---

        [Test]
        public void AClear_AnnouncesTheSecondsAdded_AfterTheModelWasWritten()
        {
            StartTimedRun(180f);
            float remainingWhenAnnounced = -1f;
            _timeExtendedBroker.Subscribe(message => remainingWhenAnnounced = _timerModel.RemainingSeconds.Value);

            PlacePiece(linesCleared: 2);

            Assert.AreEqual(1, _timeExtendedBroker.Published.Count);
            Assert.AreEqual(2, _timeExtendedBroker.Published[0].LinesCleared);
            Assert.AreEqual(10f, _timeExtendedBroker.Published[0].SecondsAdded);
            Assert.AreEqual(190f, remainingWhenAnnounced, "A subscriber reading the model must see the new total.");
        }

        // --- AC6: IsLowTime is re-evaluated on every increase, not only on the countdown tick ---

        [Test]
        public void AClearThatLiftsTheClockAboveTheThreshold_ClearsLowTimeImmediately()
        {
            StartTimedRun(180f);
            SimulateCountdownTo(4f);
            Assert.IsTrue(_timerModel.IsLowTime.Value);

            PlacePiece(linesCleared: 2);

            Assert.AreEqual(14f, _timerModel.RemainingSeconds.Value);
            Assert.IsFalse(_timerModel.IsLowTime.Value);
        }

        [Test]
        public void AClearThatLeavesTheClockUnderTheThreshold_KeepsLowTime()
        {
            StartTimedRun(180f);
            SimulateCountdownTo(3f);

            PlacePiece(linesCleared: 1);

            Assert.AreEqual(8f, _timerModel.RemainingSeconds.Value);
            Assert.IsTrue(_timerModel.IsLowTime.Value);
        }

        [Test]
        public void AClearThatLandsExactlyOnTheThreshold_IsNotLowTime()
        {
            // Tick() uses a strict "<" — landing on 10.0 is not low, same as the countdown's own rule.
            StartTimedRun(180f);
            SimulateCountdownTo(LOW_TIME_THRESHOLD - SECONDS_PER_LINE);

            PlacePiece(linesCleared: 1);

            Assert.AreEqual(LOW_TIME_THRESHOLD, _timerModel.RemainingSeconds.Value);
            Assert.IsFalse(_timerModel.IsLowTime.Value);
        }

        // --- AC8: a placement that clears nothing changes nothing ---

        [Test]
        public void APlacementThatClearsNothing_LeavesTheClockAndTheWarningUntouched()
        {
            StartTimedRun(180f);
            SimulateCountdownTo(4f);

            PlacePiece(linesCleared: 0);

            Assert.AreEqual(4f, _timerModel.RemainingSeconds.Value);
            Assert.IsTrue(_timerModel.IsLowTime.Value);
            Assert.AreEqual(0, _timeExtendedBroker.Published.Count, "No '+0s' flash for a clear of nothing.");
        }

        // --- AC2: Timed mode only ---

        [Test]
        public void OutsideTimedMode_AClearAddsNothing(
            [Values(GameMode.Endless, GameMode.Path)] GameMode mode)
        {
            CreateTimerRunSystem(mode, 180f);
            _runStartedBroker.Publish(new RunStartedMessage());
            Assert.IsFalse(_timerModel.IsRunning.Value, "Precondition: no countdown exists in this mode.");

            PlacePiece(linesCleared: 3);

            Assert.AreEqual(0f, _timerModel.RemainingSeconds.Value);
            Assert.IsFalse(_timerModel.IsLowTime.Value);
            Assert.AreEqual(0, _timeExtendedBroker.Published.Count);
        }

        // --- Gating on a real countdown: these are genuine code paths, not structural impossibilities ---

        [Test]
        public void ClassicModeWithTheEndlessDuration_HasNoClockToExtend()
        {
            // Timed mode's "Sınırsız" length: placements are published as in any run, but IsRunning is
            // false because OnRunStarted never started a countdown — so there are no seconds to add.
            CreateTimerRunSystem(GameMode.Timed, TimedModeConfig.ENDLESS_DURATION_SECONDS);
            _runStartedBroker.Publish(new RunStartedMessage());
            Assert.IsFalse(_timerModel.IsRunning.Value);

            PlacePiece(linesCleared: 2);

            Assert.AreEqual(0f, _timerModel.RemainingSeconds.Value);
            Assert.AreEqual(0, _timeExtendedBroker.Published.Count);
        }

        [Test]
        public void AfterTheRunEnded_AClearAddsNothing()
        {
            StartTimedRun(180f);
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable: false));
            Assert.IsFalse(_timerModel.IsRunning.Value);

            PlacePiece(linesCleared: 2);

            Assert.AreEqual(180f, _timerModel.RemainingSeconds.Value);
            Assert.AreEqual(0, _timeExtendedBroker.Published.Count);
        }

        [Test]
        public void BeforeAnyRunStarted_AClearAddsNothing()
        {
            CreateTimerRunSystem(GameMode.Timed, 180f);

            PlacePiece(linesCleared: 2);

            Assert.AreEqual(0f, _timerModel.RemainingSeconds.Value);
            Assert.IsFalse(_timerModel.IsRunning.Value);
        }

        [Test]
        public void WhileHeldForARescue_AClearStillCounts()
        {
            // A rescue pause holds the clock rather than stopping it (issue #370): the seconds stay
            // live, so a placement made after an accepted rescue extends them like any other.
            StartTimedRun(180f);
            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable: true));
            Assert.IsTrue(_timerModel.IsRunning.Value);

            PlacePiece(linesCleared: 1);

            Assert.AreEqual(185f, _timerModel.RemainingSeconds.Value);
        }

        // --- AC3: power-up clears are out of scope, and the System must not even listen for them ---

        [Test]
        public void TheSystem_DoesNotSubscribeToPowerUpAppliedMessage()
        {
            // Power-ups are disabled outright in Timed mode (GameModeModel.ExtrasEnabled), so no
            // PowerUpAppliedMessage is ever published during a countdown. The rule therefore needs no
            // power-up exclusion — and must not grow one by accident, which is what a subscriber
            // parameter would be. Checked structurally: the only way into this System is its constructor.
            ConstructorInfo[] constructors = typeof(TimerRunSystem).GetConstructors();
            foreach (ConstructorInfo constructor in constructors)
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    Assert.AreNotEqual(
                        typeof(ISubscriber<PowerUpAppliedMessage>),
                        parameter.ParameterType,
                        "TimerRunSystem must not listen for power-up clears (issue #319 AC3).");
                }
            }
        }

        [Test]
        public void ExtrasAreDisabledInTimedMode_SoNoPowerUpClearCanEverReachTheClock()
        {
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Timed;

            Assert.IsFalse(gameModeModel.ExtrasEnabled, "The premise AC3 rests on: no power-ups in Timed mode.");
        }

        [Test]
        public void ConstructedWithoutThePlacementSubscriber_StillRunsTheCountdownUnchanged()
        {
            // Every test construction that predates #319 omits the new parameters; they keep working.
            BoardSystem boardSystem = CreateBoardSystem();
            var gameModeSystem = new GameModeSystem(new GameModeModel(), boardSystem);
            gameModeSystem.SelectMode(GameMode.Timed);
            var timedModeSystem = new TimedModeSystem(new TimedModeModel(), _timedModeConfig);
            timedModeSystem.SelectedDuration.Value = 180f;
            _timerRunSystem = new TimerRunSystem(
                _timerModel, _runPauseModel, gameModeSystem, timedModeSystem, boardSystem,
                _runStartedBroker, _gameOverBroker);

            _runStartedBroker.Publish(new RunStartedMessage());
            PlacePiece(linesCleared: 2);

            Assert.IsTrue(_timerModel.IsRunning.Value);
            Assert.AreEqual(180f, _timerModel.RemainingSeconds.Value, "Nothing subscribed, nothing added.");
        }

        // --- Helpers ---

        private void StartTimedRun(float durationSeconds)
        {
            CreateTimerRunSystem(GameMode.Timed, durationSeconds);
            _runStartedBroker.Publish(new RunStartedMessage());
            Assert.IsTrue(_timerModel.IsRunning.Value);
            Assert.AreEqual(durationSeconds, _timerModel.RemainingSeconds.Value);
        }

        /// <summary>
        /// Stands in for the frames the countdown would have ticked: writes the pair Tick() writes,
        /// since Time.deltaTime is 0 in EditMode and the real tick cannot get the clock here.
        /// </summary>
        private void SimulateCountdownTo(float remainingSeconds)
        {
            _timerModel.RemainingSeconds.Value = remainingSeconds;
            _timerModel.IsLowTime.Value = remainingSeconds < LOW_TIME_THRESHOLD;
        }

        private void PlacePiece(int linesCleared)
        {
            PlacePiece(linesCleared, rowsCleared: linesCleared, columnsCleared: 0);
        }

        private void PlacePiece(int linesCleared, int rowsCleared, int columnsCleared)
        {
            _piecePlacedBroker.Publish(new PiecePlacedMessage(
                pieceId: "test",
                anchor: new GridPosition(0, 0),
                pieceFamily: PieceFamily.Single,
                cellCount: 1,
                colourId: 0,
                linesCleared: linesCleared,
                rowsCleared: rowsCleared,
                columnsCleared: columnsCleared,
                monochromeLineCount: 0,
                boardEmptyAfterPlacement: false,
                occupiedCellCountBeforeClear: 1,
                anyCornerCleared: false,
                centerCoreEmptyAfterPlacement: false,
                hasIsolatedHolesAfterPlacement: false));
        }

        private void CreateTimerRunSystem(GameMode mode, float durationSeconds)
        {
            BoardSystem boardSystem = CreateBoardSystem();
            _gameModeSystem = new GameModeSystem(new GameModeModel(), boardSystem);
            _gameModeSystem.SelectMode(mode);

            _timedModeSystem = new TimedModeSystem(new TimedModeModel(), _timedModeConfig);
            _timedModeSystem.SelectedDuration.Value = durationSeconds;

            _timerRunSystem = new TimerRunSystem(
                _timerModel,
                _runPauseModel,
                _gameModeSystem,
                _timedModeSystem,
                boardSystem,
                _runStartedBroker,
                _gameOverBroker,
                runRescuedSubscriber: null,
                piecePlacedSubscriber: _piecePlacedBroker,
                timeExtendedPublisher: _timeExtendedBroker);
        }

        private BoardSystem CreateBoardSystem()
        {
            return new BoardSystem(
                new BoardModel(),
                new TrayModel(),
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                _currencyConfig,
                reinforcedCellSeeder: null);
        }
    }
}
