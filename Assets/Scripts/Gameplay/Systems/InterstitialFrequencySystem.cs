using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Decides when a forced interstitial is shown (issue #502, part of #388): once every
    /// <see cref="RUNS_PER_AD"/> finished runs, counted separately per <see cref="GameMode"/>, at the
    /// moment the player leaves the end-of-run card for their next run ("Next level" in Path, "Play
    /// again" in Endless and Timed). The card itself always appears ad-free first.
    /// <para>
    /// <b>What counts.</b> Every run end, win or loss, is one tick on the counter of the mode it was
    /// played in — replays of already-cleared Path levels included. A no-moves ending that is rescued
    /// and later ends again is still one run: only the first <see cref="GameOverMessage"/> after a
    /// <see cref="RunStartedMessage"/> counts.
    /// </para>
    /// <para>
    /// <b>When a due ad is held back.</b> The counter keeps ticking but no ad is shown during the
    /// player's first <see cref="EXEMPT_LIFETIME_RUNS"/> runs across all modes, after Path levels 1 to
    /// <see cref="EXEMPT_PATH_LEVELS"/>, or when a rewarded ad was watched since the run started (so
    /// two ads never come back to back). A held-back or unfilled ad keeps the counter where it is, so
    /// the ad is simply tried again at the next run end; the counter only resets when an ad was really
    /// shown. "Remove ads" is honoured by <see cref="IInterstitialAdSource"/> itself.
    /// </para>
    /// <para>
    /// Counters and the lifetime run total live in PlayerPrefs, so the rhythm survives a relaunch.
    /// </para>
    /// </summary>
    public sealed class InterstitialFrequencySystem : IDisposable
    {
        private const int RUNS_PER_AD = 3;
        private const int EXEMPT_LIFETIME_RUNS = 5;
        private const int EXEMPT_PATH_LEVELS = 5;

        private const string COUNTER_KEY_PREFIX = "Ads.Interstitial.RunsSinceAd.";
        private const string LIFETIME_RUNS_KEY = "Ads.Interstitial.LifetimeRuns";

        private static readonly GameMode[] AllModes = (GameMode[])Enum.GetValues(typeof(GameMode));

        private readonly GameModeModel _gameModeModel;
        private readonly PathRunModel _pathRunModel;
        private readonly IInterstitialAdSource _interstitialAdSource;
        private readonly IDisposable _subscriptions;
        private readonly CancellationTokenSource _lifetimeCts = new();

        private readonly int[] _runsSinceAd;
        private int _lifetimeRuns;

        /// <summary>A run has ended and the player has not yet started the next one.</summary>
        private bool _hasEndedRun;
        private GameMode _endedRunMode;
        private bool _isEndedRunExempt;
        private bool _hasRewardedAdSinceRunStart;
        private bool _isShowing;

        public InterstitialFrequencySystem(
            GameModeModel gameModeModel,
            PathRunModel pathRunModel,
            IInterstitialAdSource interstitialAdSource,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RewardedAdShownMessage> rewardedAdShownSubscriber)
        {
            _gameModeModel = gameModeModel;
            _pathRunModel = pathRunModel;
            _interstitialAdSource = interstitialAdSource;
            _runsSinceAd = new int[AllModes.Length];

            Load();

            _subscriptions = DisposableBag.Create(
                runStartedSubscriber.Subscribe(_ => OnRunStarted()),
                gameOverSubscriber.Subscribe(_ => OnGameOver()),
                rewardedAdShownSubscriber.Subscribe(_ => _hasRewardedAdSinceRunStart = true));
        }

        /// <summary>
        /// Called by the end-of-run card's "Next level" / "Play again" before the next run starts: shows
        /// an interstitial if one is due and completes once it is closed. Completes straight away when
        /// none is due or none is ready — the player is never made to wait on a load.
        /// </summary>
        public async UniTask ShowIfDueAsync(CancellationToken cancellationToken)
        {
            if (!IsAdDue())
            {
                return;
            }

            _isShowing = true;
            bool shown;
            try
            {
                shown = await _interstitialAdSource.TryShowAsync(cancellationToken);
            }
            finally
            {
                _isShowing = false;
            }

            if (!shown)
            {
                return;
            }

            _runsSinceAd[(int)_endedRunMode] = 0;

            // One ad per ended run, however many times the player taps before the next run starts.
            _hasEndedRun = false;
            Save();
        }

        public void Dispose()
        {
            _subscriptions.Dispose();
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
        }

        private bool IsAdDue()
        {
            return _hasEndedRun
                && !_isShowing
                && !_isEndedRunExempt
                && !_hasRewardedAdSinceRunStart
                && _runsSinceAd[(int)_endedRunMode] >= RUNS_PER_AD;
        }

        private void OnRunStarted()
        {
            _hasEndedRun = false;
            _hasRewardedAdSinceRunStart = false;
        }

        private void OnGameOver()
        {
            if (_hasEndedRun)
            {
                // A rescued run ending again, or a duplicate announcement: still the same run.
                return;
            }

            _hasEndedRun = true;
            _endedRunMode = _gameModeModel.CurrentMode.Value;
            _lifetimeRuns++;
            _runsSinceAd[(int)_endedRunMode]++;

            int pathLevel = _pathRunModel.ActiveLevelNumber.Value;
            bool isEarlyPathLevel = _endedRunMode == GameMode.Path
                && pathLevel != PathRunModel.NO_ACTIVE_LEVEL
                && pathLevel <= EXEMPT_PATH_LEVELS;
            _isEndedRunExempt = _lifetimeRuns <= EXEMPT_LIFETIME_RUNS || isEarlyPathLevel;

            Save();

            if (_runsSinceAd[(int)_endedRunMode] >= RUNS_PER_AD && !_isEndedRunExempt)
            {
                // Normally already loaded at boot or after the last show; this covers a load that failed.
                _interstitialAdSource.PreloadAsync(_lifetimeCts.Token).SuppressCancellationThrow().Forget();
            }
        }

        private void Load()
        {
            for (int modeIndex = 0; modeIndex < AllModes.Length; modeIndex++)
            {
                _runsSinceAd[modeIndex] = Mathf.Max(0, PlayerPrefs.GetInt(CounterKey(AllModes[modeIndex]), 0));
            }

            _lifetimeRuns = Mathf.Max(0, PlayerPrefs.GetInt(LIFETIME_RUNS_KEY, 0));
        }

        private void Save()
        {
            for (int modeIndex = 0; modeIndex < AllModes.Length; modeIndex++)
            {
                PlayerPrefs.SetInt(CounterKey(AllModes[modeIndex]), _runsSinceAd[modeIndex]);
            }

            PlayerPrefs.SetInt(LIFETIME_RUNS_KEY, _lifetimeRuns);
        }

        private static string CounterKey(GameMode mode)
        {
            return COUNTER_KEY_PREFIX + mode;
        }
    }
}
