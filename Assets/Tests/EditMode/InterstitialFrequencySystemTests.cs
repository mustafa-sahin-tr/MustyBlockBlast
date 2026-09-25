using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the interstitial rhythm (issue #502): one ad every 3 run ends per mode, counted across
    /// wins and losses and across relaunches, held back during the first 5 lifetime runs, after Path
    /// levels 1–5, and after a rewarded ad in the same run; deferred rather than reset on a no-fill.
    /// </summary>
    public class InterstitialFrequencySystemTests
    {
        private const string LIFETIME_RUNS_KEY = "Ads.Interstitial.LifetimeRuns";
        private const string COUNTER_KEY_PREFIX = "Ads.Interstitial.RunsSinceAd.";

        private GameModeModel _gameModeModel;
        private PathRunModel _pathRunModel;
        private FakeInterstitialAdSource _adSource;
        private TestMessageBroker<RunStartedMessage> _runStarted;
        private TestMessageBroker<GameOverMessage> _gameOver;
        private TestMessageBroker<RewardedAdShownMessage> _rewardedAdShown;
        private InterstitialFrequencySystem _system;

        [SetUp]
        public void SetUp()
        {
            ClearPrefs();
            _gameModeModel = new GameModeModel();
            _gameModeModel.CurrentMode.Value = GameMode.Endless;
            _pathRunModel = new PathRunModel();
            _adSource = new FakeInterstitialAdSource();
            _runStarted = new TestMessageBroker<RunStartedMessage>();
            _gameOver = new TestMessageBroker<GameOverMessage>();
            _rewardedAdShown = new TestMessageBroker<RewardedAdShownMessage>();
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            ClearPrefs();
        }

        [Test]
        public void ShowIfDue_DuringTheFirstFiveLifetimeRuns_NeverShowsAnAd()
        {
            _system = CreateSystem();

            for (int runIndex = 0; runIndex < 5; runIndex++)
            {
                PlayRunAndTapNext();
            }

            Assert.AreEqual(0, _adSource.ShowCalls);
        }

        [Test]
        public void ShowIfDue_OnEveryThirdRunAfterTheExemption_ShowsAnAd()
        {
            PlayerPrefs.SetInt(LIFETIME_RUNS_KEY, 5);
            _system = CreateSystem();

            PlayRunAndTapNext();
            PlayRunAndTapNext();
            Assert.AreEqual(0, _adSource.ShowCalls);

            PlayRunAndTapNext();
            Assert.AreEqual(1, _adSource.ShowCalls);

            PlayRunAndTapNext();
            PlayRunAndTapNext();
            Assert.AreEqual(1, _adSource.ShowCalls);

            PlayRunAndTapNext();
            Assert.AreEqual(2, _adSource.ShowCalls);
        }

        [Test]
        public void Counters_AreSeparatePerMode()
        {
            PlayerPrefs.SetInt(LIFETIME_RUNS_KEY, 5);
            _system = CreateSystem();

            PlayRunAndTapNext();
            PlayRunAndTapNext();
            _gameModeModel.CurrentMode.Value = GameMode.Timed;
            PlayRunAndTapNext();

            Assert.AreEqual(0, _adSource.ShowCalls);
        }

        [Test]
        public void Counter_SurvivesARelaunch()
        {
            PlayerPrefs.SetInt(LIFETIME_RUNS_KEY, 5);
            _system = CreateSystem();
            PlayRunAndTapNext();
            PlayRunAndTapNext();
            _system.Dispose();

            _system = CreateSystem();
            PlayRunAndTapNext();

            Assert.AreEqual(1, _adSource.ShowCalls);
        }

        [Test]
        public void ARescuedRunThatEndsAgain_CountsOnce()
        {
            PlayerPrefs.SetInt(LIFETIME_RUNS_KEY, 5);
            _system = CreateSystem();
            PlayRunAndTapNext();

            _runStarted.Publish(new RunStartedMessage());
            _gameOver.Publish(new GameOverMessage(GameOverReason.NoMovesLeft, isRescueAvailable: true));
            _gameOver.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));
            TapNext();

            Assert.AreEqual(0, _adSource.ShowCalls);
            Assert.AreEqual(2, PlayerPrefs.GetInt(COUNTER_KEY_PREFIX + GameMode.Endless));
        }

        [Test]
        public void ShowIfDue_AfterARewardedAdInTheSameRun_HoldsTheAdForTheNextRun()
        {
            PlayerPrefs.SetInt(LIFETIME_RUNS_KEY, 5);
            _system = CreateSystem();
            PlayRunAndTapNext();
            PlayRunAndTapNext();

            _runStarted.Publish(new RunStartedMessage());
            _rewardedAdShown.Publish(new RewardedAdShownMessage());
            _gameOver.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));
            TapNext();
            Assert.AreEqual(0, _adSource.ShowCalls);

            PlayRunAndTapNext();
            Assert.AreEqual(1, _adSource.ShowCalls);
        }

        [Test]
        public void ShowIfDue_WhenNoAdIsReady_KeepsTheCounterAndRetriesNextRun()
        {
            PlayerPrefs.SetInt(LIFETIME_RUNS_KEY, 5);
            _system = CreateSystem();
            _adSource.NextResult = false;

            PlayRunAndTapNext();
            PlayRunAndTapNext();
            PlayRunAndTapNext();
            Assert.AreEqual(1, _adSource.ShowCalls);
            Assert.AreEqual(3, PlayerPrefs.GetInt(COUNTER_KEY_PREFIX + GameMode.Endless));

            _adSource.NextResult = true;
            PlayRunAndTapNext();
            Assert.AreEqual(2, _adSource.ShowCalls);
            Assert.AreEqual(0, PlayerPrefs.GetInt(COUNTER_KEY_PREFIX + GameMode.Endless));
        }

        [Test]
        public void ShowIfDue_AfterEarlyPathLevels_NeverShowsAnAd()
        {
            PlayerPrefs.SetInt(LIFETIME_RUNS_KEY, 5);
            _gameModeModel.CurrentMode.Value = GameMode.Path;
            _system = CreateSystem();

            for (int level = 1; level <= 5; level++)
            {
                _pathRunModel.ActiveLevelNumber.Value = level;
                PlayRunAndTapNext();
            }

            Assert.AreEqual(0, _adSource.ShowCalls);

            _pathRunModel.ActiveLevelNumber.Value = 6;
            PlayRunAndTapNext();
            Assert.AreEqual(1, _adSource.ShowCalls);
        }

        [Test]
        public void ShowIfDue_WithoutAnEndedRun_DoesNothing()
        {
            PlayerPrefs.SetInt(LIFETIME_RUNS_KEY, 5);
            PlayerPrefs.SetInt(COUNTER_KEY_PREFIX + GameMode.Endless, 3);
            _system = CreateSystem();

            TapNext();

            Assert.AreEqual(0, _adSource.ShowCalls);
        }

        private InterstitialFrequencySystem CreateSystem()
        {
            return new InterstitialFrequencySystem(
                _gameModeModel, _pathRunModel, _adSource, _runStarted, _gameOver, _rewardedAdShown);
        }

        private void PlayRunAndTapNext()
        {
            _runStarted.Publish(new RunStartedMessage());
            _gameOver.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));
            TapNext();
        }

        private void TapNext()
        {
            _system.ShowIfDueAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private static void ClearPrefs()
        {
            PlayerPrefs.DeleteKey(LIFETIME_RUNS_KEY);
            PlayerPrefs.DeleteKey(COUNTER_KEY_PREFIX + GameMode.Endless);
            PlayerPrefs.DeleteKey(COUNTER_KEY_PREFIX + GameMode.Timed);
            PlayerPrefs.DeleteKey(COUNTER_KEY_PREFIX + GameMode.Path);
        }

        private sealed class FakeInterstitialAdSource : IInterstitialAdSource
        {
            public int ShowCalls { get; private set; }

            public bool NextResult { get; set; } = true;

            public UniTask PreloadAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;

            public UniTask<bool> TryShowAsync(CancellationToken cancellationToken)
            {
                ShowCalls++;
                return UniTask.FromResult(NextResult);
            }
        }
    }
}
