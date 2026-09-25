using System;
using System.Text.RegularExpressions;
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
    /// hourly refill against a seeded clock, and persistence. The system is built directly with
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
                _gameOverBroker, _runRescuedBroker, _runStartedBroker, () => _now);

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

        private void CreateSystem()
        {
            _system = new LivesSystem(
                _model, _config, _gameModeModel,
                _gameOverBroker, _runRescuedBroker, _runStartedBroker, () => _now);
        }

        private void Fail(bool isRescueAvailable = false)
            => _gameOverBroker.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable));

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
