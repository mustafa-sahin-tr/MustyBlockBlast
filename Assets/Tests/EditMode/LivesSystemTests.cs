using System;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="LivesSystem"/> (issue #477): the Path-failure charge, the rescue refund, the
    /// hourly refill against a seeded clock, and persistence — and (issue #478) the start gate, the
    /// charge the fail card reads, and the ad top-up's clamp. The system is built directly with
    /// <see cref="TestMessageBroker{TMessage}"/> stand-ins and never started, so no countdown loop runs
    /// under a test — every refill check is an explicit <see cref="LivesSystem.RefreshRefill"/>.
    /// </summary>
    public sealed class LivesSystemTests
    {
        private const string SAVE_KEY = "Lives.State";

        /// <summary>10:40 on an arbitrary day: 20 minutes short of a boundary, as in the issue's example.</summary>
        private static readonly DateTime TenForty = new DateTime(2026, 9, 25, 10, 40, 0);

        private LivesModel _model;
        private LivesConfig _config;
        private GameModeModel _gameModeModel;
        private TestMessageBroker<GameOverMessage> _gameOverBroker;
        private TestMessageBroker<RunRescuedMessage> _runRescuedBroker;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private TestMessageBroker<OutOfLivesMessage> _outOfLivesBroker;
        private StubLivesRewardSource _rewardSource;
        private LivesSystem _system;
        private DateTime _now;

        private bool _hadSavedState;
        private string _savedState;

        [SetUp]
        public void SetUp()
        {
            // The real device's lives are backed up and restored, not trampled.
            _hadSavedState = PlayerPrefs.HasKey(SAVE_KEY);
            _savedState = _hadSavedState ? PlayerPrefs.GetString(SAVE_KEY) : null;
            PlayerPrefs.DeleteKey(SAVE_KEY);

            _model = new LivesModel();
            _config = ScriptableObject.CreateInstance<LivesConfig>();
            _gameModeModel = new GameModeModel();
            _gameModeModel.CurrentMode.Value = GameMode.Path;
            _gameOverBroker = new TestMessageBroker<GameOverMessage>();
            _runRescuedBroker = new TestMessageBroker<RunRescuedMessage>();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _outOfLivesBroker = new TestMessageBroker<OutOfLivesMessage>();
            _rewardSource = new StubLivesRewardSource(granted: true);
            _now = TenForty;
        }

        [TearDown]
        public void TearDown()
        {
            if (_system != null)
            {
                _system.Dispose();
                _system = null;
            }

            if (_config != null)
            {
                UnityEngine.Object.DestroyImmediate(_config);
            }

            if (_hadSavedState)
            {
                PlayerPrefs.SetString(SAVE_KEY, _savedState);
            }
            else
            {
                PlayerPrefs.DeleteKey(SAVE_KEY);
            }
        }

        [Test]
        public void FreshInstall_StartsAtTheConfiguredTwentyAndPersistsThem()
        {
            CreateSystem();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(20));
            Assert.That(PlayerPrefs.HasKey(SAVE_KEY), Is.True, "a first launch writes its starting lives");
            Assert.That(_model.SecondsUntilRefill.Value, Is.EqualTo(0), "no countdown at the cap");
        }

        [Test]
        public void PathFailure_CostsOneLife()
        {
            CreateSystem();

            Fail();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(19));
        }

        [Test]
        public void LevelCompleted_CostsNothing()
        {
            CreateSystem();

            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.LevelCompleted));

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(20));
        }

        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Timed)]
        public void FailuresOutsidePathMode_NeverTouchLives(GameMode mode)
        {
            SeedSave(7, TenForty);
            CreateSystem();
            _gameModeModel.CurrentMode.Value = mode;

            for (int runIndex = 0; runIndex < 5; runIndex++)
            {
                _runStartedBroker.Publish(new RunStartedMessage());
                Fail(isRescueAvailable: true);
                _runRescuedBroker.Publish(new RunRescuedMessage());
                Fail();
            }

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(7));
        }

        [Test]
        public void RescuedFailure_IsRefunded()
        {
            CreateSystem();

            Fail(isRescueAvailable: true);
            _runRescuedBroker.Publish(new RunRescuedMessage());

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(20));
        }

        [Test]
        public void RescuedRunThatFailsAgain_NetsOneLife()
        {
            CreateSystem();

            Fail(isRescueAvailable: true);
            _runRescuedBroker.Publish(new RunRescuedMessage());
            Fail(isRescueAvailable: true);

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(19));
        }

        [Test]
        public void RescueAfterANewRunStarted_RefundsNothing()
        {
            CreateSystem();

            Fail(isRescueAvailable: true);
            _runStartedBroker.Publish(new RunStartedMessage());
            _runRescuedBroker.Publish(new RunRescuedMessage());

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(19));
        }

        [Test]
        public void Failure_AtZeroLives_StaysAtZeroAndARescueRefundsNothing()
        {
            SeedSave(1, TenForty);
            CreateSystem();

            Fail();
            Fail(isRescueAvailable: true);
            _runRescuedBroker.Publish(new RunRescuedMessage());

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(0));
        }

        [Test]
        public void CrossingAnHourBoundary_RefillsFive()
        {
            SeedSave(3, TenForty);
            CreateSystem();

            _now = new DateTime(2026, 9, 25, 10, 59, 59);
            _system.RefreshRefill();
            Assert.That(_model.CurrentLives.Value, Is.EqualTo(3), "no refill before xx:00");

            _now = new DateTime(2026, 9, 25, 11, 0, 0);
            _system.RefreshRefill();
            Assert.That(_model.CurrentLives.Value, Is.EqualTo(8));
        }

        [Test]
        public void BoundariesMissedWhileClosed_AllCount_ClampedAtTheCap()
        {
            SeedSave(3, TenForty);

            // Reopened at 14:10: four boundaries (11, 12, 13, 14) — 3 + 20, capped at 20.
            _now = new DateTime(2026, 9, 25, 14, 10, 0);
            CreateSystem();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(20));
        }

        [Test]
        public void TwoMissedBoundaries_PayTwoRefills()
        {
            SeedSave(3, TenForty);

            _now = new DateTime(2026, 9, 25, 12, 5, 0);
            CreateSystem();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(13));
        }

        [Test]
        public void AboveTheCap_TheRefillNeitherAddsNorLowers()
        {
            SeedSave(25, TenForty);
            CreateSystem();

            _now = new DateTime(2026, 9, 25, 13, 0, 0);
            _system.RefreshRefill();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(25));
            Assert.That(_model.SecondsUntilRefill.Value, Is.EqualTo(0));
        }

        [Test]
        public void AboveTheCap_AFailureSpendsNormally()
        {
            SeedSave(25, TenForty);
            CreateSystem();

            Fail();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(24));
        }

        [Test]
        public void BelowTheCap_CountsDownToTheNextHour()
        {
            SeedSave(3, TenForty);
            CreateSystem();

            Assert.That(_model.SecondsUntilRefill.Value, Is.EqualTo(20 * 60));

            _now = new DateTime(2026, 9, 25, 10, 59, 59, 500);
            _system.RefreshRefill();
            Assert.That(_model.SecondsUntilRefill.Value, Is.EqualTo(1), "rounded up, never 0 below the cap");
        }

        [Test]
        public void AFailureFromTheCap_StartsTheCountdown()
        {
            CreateSystem();

            Fail();

            Assert.That(_model.SecondsUntilRefill.Value, Is.EqualTo(20 * 60));
        }

        [Test]
        public void ABoundaryCrossedBetweenChecks_IsPaidBeforeTheCharge()
        {
            SeedSave(3, TenForty);
            CreateSystem();

            _now = new DateTime(2026, 9, 25, 11, 10, 0);
            Fail();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(7));
        }

        [Test]
        public void ClockMovedBackwards_ReanchorsWithoutGranting()
        {
            SeedSave(3, TenForty);
            CreateSystem();

            _now = new DateTime(2026, 9, 25, 8, 30, 0);
            _system.RefreshRefill();
            Assert.That(_model.CurrentLives.Value, Is.EqualTo(3), "going back in time grants nothing");

            // Re-anchored to 08:xx, so 09:00 is one boundary, not a negative one or a replay of 11:00.
            _now = new DateTime(2026, 9, 25, 9, 0, 0);
            _system.RefreshRefill();
            Assert.That(_model.CurrentLives.Value, Is.EqualTo(8));
        }

        [Test]
        public void SaveAndLoad_RoundTripsTheCount()
        {
            CreateSystem();
            Fail();
            Fail();
            _system.Dispose();
            _system = null;

            LivesModel reloadedModel = new LivesModel();
            _system = new LivesSystem(
                reloadedModel, _config, _gameModeModel,
                _gameOverBroker, _runRescuedBroker, _runStartedBroker,
                _rewardSource, _outOfLivesBroker, () => _now);

            Assert.That(reloadedModel.CurrentLives.Value, Is.EqualTo(18));
        }

        [Test]
        public void UnreadableSave_FallsBackToTheStartingLives()
        {
            PlayerPrefs.SetString(SAVE_KEY, "not json at all");

            LogAssert.Expect(LogType.Error, new Regex("Lives save data was unreadable"));
            CreateSystem();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(20));
        }

        // --- Issue #478: the start gate ---

        [Test]
        public void StartGate_AtZeroLivesInPath_RefusesAndAnnouncesIt()
        {
            SeedSave(0, TenForty);
            CreateSystem();

            bool passed = _system.TryPassStartGate();

            Assert.That(passed, Is.False);
            Assert.That(_outOfLivesBroker.Published.Count, Is.EqualTo(1));
        }

        [Test]
        public void StartGate_WithALifeLeftInPath_PassesSilentlyAndSpendsNothing()
        {
            SeedSave(1, TenForty);
            CreateSystem();

            bool passed = _system.TryPassStartGate();

            Assert.That(passed, Is.True);
            Assert.That(_outOfLivesBroker.Published.Count, Is.EqualTo(0));
            Assert.That(_model.CurrentLives.Value, Is.EqualTo(1), "passing the gate is not a charge");
        }

        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Timed)]
        public void StartGate_OutsidePathMode_AlwaysPassesEvenAtZero(GameMode mode)
        {
            SeedSave(0, TenForty);
            CreateSystem();
            _gameModeModel.CurrentMode.Value = mode;

            Assert.That(_system.TryPassStartGate(), Is.True);
            Assert.That(_outOfLivesBroker.Published.Count, Is.EqualTo(0));
        }

        [Test]
        public void StartGate_PaysACrossedBoundaryBeforeDeciding()
        {
            SeedSave(0, TenForty);
            CreateSystem();

            // 11:00 has come since the last tick: the refill is owed, so the start is not refused.
            _now = new DateTime(2026, 9, 25, 11, 0, 0);

            Assert.That(_system.TryPassStartGate(), Is.True);
            Assert.That(_model.CurrentLives.Value, Is.EqualTo(5));
        }

        // --- Issue #478: what the fail card reads ---

        [Test]
        public void AChargedFailure_RecordsTheCountBeforeIt_AndTheNextRunClearsIt()
        {
            SeedSave(17, TenForty);
            CreateSystem();

            Fail();
            Assert.That(_model.LivesBeforeLastCharge.Value, Is.EqualTo(17));
            Assert.That(_model.CurrentLives.Value, Is.EqualTo(16));

            _runStartedBroker.Publish(new RunStartedMessage());
            Assert.That(_model.LivesBeforeLastCharge.Value, Is.EqualTo(0));
        }

        [Test]
        public void AFailureAtZero_RecordsNoCharge()
        {
            SeedSave(0, TenForty);
            CreateSystem();

            Fail();

            Assert.That(_model.LivesBeforeLastCharge.Value, Is.EqualTo(0));
        }

        [Test]
        public void ARefundedCharge_IsClearedToo()
        {
            CreateSystem();

            Fail(isRescueAvailable: true);
            _runRescuedBroker.Publish(new RunRescuedMessage());

            Assert.That(_model.LivesBeforeLastCharge.Value, Is.EqualTo(0));
        }

        [Test]
        public void ALevelCompleted_RecordsNoCharge()
        {
            CreateSystem();

            _gameOverBroker.Publish(new GameOverMessage(GameOverReason.LevelCompleted));

            Assert.That(_model.LivesBeforeLastCharge.Value, Is.EqualTo(0));
        }

        // --- Issue #478: the ad top-up ---

        [TestCase(18, 20)]
        [TestCase(17, 20)]
        [TestCase(15, 18)]
        [TestCase(0, 3)]
        public void AdGrant_AddsThree_ClampedAtTheCap(int livesBefore, int expectedLives)
        {
            SeedSave(livesBefore, TenForty);
            CreateSystem();

            bool granted = RequestAd();

            Assert.That(granted, Is.True);
            Assert.That(_model.CurrentLives.Value, Is.EqualTo(expectedLives));
            Assert.That(_rewardSource.LastRequestedAmount, Is.EqualTo(3));
        }

        [TestCase(20)]
        [TestCase(25)]
        public void AdGrant_AtOrAboveTheCap_IsRefusedWithoutAskingTheSeam(int livesBefore)
        {
            SeedSave(livesBefore, TenForty);
            CreateSystem();

            Assert.That(_system.CanRequestAdLives, Is.False);
            bool granted = RequestAd();

            Assert.That(granted, Is.False);
            Assert.That(_model.CurrentLives.Value, Is.EqualTo(livesBefore), "never raised, never lowered");
            Assert.That(_rewardSource.RequestCount, Is.EqualTo(0));
        }

        [Test]
        public void AdGrant_Declined_GrantsNothing()
        {
            SeedSave(4, TenForty);
            _rewardSource = new StubLivesRewardSource(granted: false);
            CreateSystem();

            bool granted = RequestAd();

            Assert.That(granted, Is.False);
            Assert.That(_model.CurrentLives.Value, Is.EqualTo(4));
            Assert.That(_rewardSource.RequestCount, Is.EqualTo(1));
        }

        [Test]
        public void AdGrant_IsPersisted()
        {
            SeedSave(2, TenForty);
            CreateSystem();

            RequestAd();
            _system.Dispose();
            _system = null;

            LivesModel reloadedModel = new LivesModel();
            _system = new LivesSystem(
                reloadedModel, _config, _gameModeModel,
                _gameOverBroker, _runRescuedBroker, _runStartedBroker,
                _rewardSource, _outOfLivesBroker, () => _now);

            Assert.That(reloadedModel.CurrentLives.Value, Is.EqualTo(5));
        }

        [Test]
        public void AdGrant_ASourceOverpaying_IsStillClampedAtTheCap()
        {
            SeedSave(10, TenForty);
            _rewardSource = new StubLivesRewardSource(granted: true, amountOverride: 1000);
            CreateSystem();

            RequestAd();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(20));
        }

        [Test]
        public void AdGrant_LiftsTheZeroGate()
        {
            SeedSave(0, TenForty);
            CreateSystem();
            Assert.That(_system.TryPassStartGate(), Is.False);

            RequestAd();

            Assert.That(_system.TryPassStartGate(), Is.True);
        }

        // --- Issue #479: the coin lives pack's grant (the debit is CurrencySystemTests') ---

        [TestCase(15, 25)]
        [TestCase(22, 32)]
        [TestCase(0, 10)]
        public void PackGrant_AddsTen_WithNoCap(int livesBefore, int expectedLives)
        {
            SeedSave(livesBefore, TenForty);
            CreateSystem();

            _system.GrantPurchasedLives(_config.LivesPackAmount);

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(expectedLives));
        }

        [Test]
        public void PackGrant_IsPersisted()
        {
            SeedSave(22, TenForty);
            CreateSystem();

            _system.GrantPurchasedLives(10);
            _system.Dispose();
            _system = null;

            LivesModel reloadedModel = new LivesModel();
            _system = new LivesSystem(
                reloadedModel, _config, _gameModeModel,
                _gameOverBroker, _runRescuedBroker, _runStartedBroker,
                _rewardSource, _outOfLivesBroker, () => _now);

            Assert.That(reloadedModel.CurrentLives.Value, Is.EqualTo(32), "a count above the cap survives a reload");
        }

        [Test]
        public void PackGrant_AboveTheCap_TheRefillAddsNothingAndNeverLowersIt()
        {
            SeedSave(15, TenForty);
            CreateSystem();
            _system.GrantPurchasedLives(10);

            _now = new DateTime(2026, 9, 25, 11, 0, 0);
            _system.RefreshRefill();
            _now = new DateTime(2026, 9, 25, 16, 0, 0);
            _system.RefreshRefill();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(25));
            Assert.That(_model.SecondsUntilRefill.Value, Is.EqualTo(0), "no countdown above the cap");
        }

        [Test]
        public void PackGrant_SpentBackBelowTheCap_TheRefillResumes()
        {
            SeedSave(15, TenForty);
            CreateSystem();
            _system.GrantPurchasedLives(10);

            for (int failureIndex = 0; failureIndex < 7; failureIndex++)
            {
                Fail();
            }

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(18));
            Assert.That(_model.SecondsUntilRefill.Value, Is.EqualTo(20 * 60), "the countdown is back below the cap");

            _now = new DateTime(2026, 9, 25, 11, 0, 0);
            _system.RefreshRefill();

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(20), "refilled, and clamped at the cap again");
        }

        [Test]
        public void PackGrant_PaysACrossedBoundaryFirst()
        {
            SeedSave(3, TenForty);
            CreateSystem();

            // 11:00 has passed since the last check: the refill's +5 lands first, then the pack's +10.
            _now = new DateTime(2026, 9, 25, 11, 5, 0);
            _system.GrantPurchasedLives(10);

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(18));
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void PackGrant_WithANonPositiveAmount_IsANoOp(int amount)
        {
            SeedSave(12, TenForty);
            CreateSystem();

            _system.GrantPurchasedLives(amount);

            Assert.That(_model.CurrentLives.Value, Is.EqualTo(12));
        }

        [Test]
        public void LivesConfig_ShipsTheIssuesPlaceholderPack()
        {
            Assert.That(_config.LivesPackAmount, Is.EqualTo(10));
            Assert.That(_config.LivesPackCoinPrice, Is.EqualTo(150));
        }

        private bool RequestAd() => _system.RequestAdLivesAsync(CancellationToken.None).GetAwaiter().GetResult();

        private void CreateSystem()
        {
            _system = new LivesSystem(
                _model, _config, _gameModeModel,
                _gameOverBroker, _runRescuedBroker, _runStartedBroker,
                _rewardSource, _outOfLivesBroker, () => _now);
        }

        private void Fail(bool isRescueAvailable = false)
            => _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable));

        /// <summary>The lives ad seam's stand-in: answers at once with the grant it was built with, and
        /// counts what it was asked for so a test can prove the seam was — or was not — reached.</summary>
        private sealed class StubLivesRewardSource : ILivesRewardSource
        {
            private readonly bool _granted;
            private readonly int _amountOverride;

            internal StubLivesRewardSource(bool granted, int amountOverride = 0)
            {
                _granted = granted;
                _amountOverride = amountOverride;
            }

            internal int RequestCount { get; private set; }

            internal int LastRequestedAmount { get; private set; }

            public UniTask<LivesRewardResult> RequestLivesRewardAsync(int amount, CancellationToken cancellationToken)
            {
                RequestCount++;
                LastRequestedAmount = amount;
                int paid = _granted ? (_amountOverride > 0 ? _amountOverride : amount) : 0;
                return UniTask.FromResult(new LivesRewardResult(paid, _granted));
            }
        }

        private static void SeedSave(int lives, DateTime lastCheck)
        {
            LivesSaveData data = new LivesSaveData
            {
                lives = lives,
                lastRefillHourStamp = lastCheck.Ticks / TimeSpan.TicksPerHour,
            };
            PlayerPrefs.SetString(SAVE_KEY, JsonUtility.ToJson(data));
        }
    }
}
