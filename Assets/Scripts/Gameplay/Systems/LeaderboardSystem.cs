using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Files a finished run's score on its mode's two boards — all-time and weekly — the moment the run
    /// ends. Nothing else in the game talks to the leaderboard backend.
    /// <para>
    /// The boards are configured "Keep Best" server-side, so there is deliberately no client-side
    /// comparison against a previous best and no dedup: every run is submitted, and the backend decides
    /// whether it displaces the player's entry. Duplicating that rule here would be a second source of
    /// truth that could disagree with the server after a reinstall or a device swap.
    /// </para>
    /// <para>
    /// Subscribes in its constructor like <see cref="TimedHighScoreSystem"/>, so it must be resolved
    /// eagerly by the LifetimeScope rather than waiting for a first lazy resolve that may never come
    /// before the first game over.
    /// </para>
    /// </summary>
    public sealed class LeaderboardSystem : IDisposable
    {
        /// <summary>
        /// Board ids per mode, exactly as configured on the UGS Dashboard. Absence is meaningful:
        /// <see cref="GameMode.Path"/> has no entry because a path run is bound to one authored level, so
        /// its score measures the level rather than the player and ranking it against other players'
        /// runs would compare unlike things. Any mode added later is likewise a no-op until it is
        /// deliberately given boards here.
        /// </summary>
        private static readonly Dictionary<GameMode, (string AllTimeId, string WeeklyId)> BoardIds =
            new Dictionary<GameMode, (string AllTimeId, string WeeklyId)>
            {
                { GameMode.Endless, ("endless_all_time", "endless_weekly") },
                { GameMode.Timed, ("timed_all_time", "timed_weekly") },
            };

        private readonly ILeaderboardsService _leaderboardsService;
        private readonly IAuthService _authService;
        private readonly ScoreModel _scoreModel;
        private readonly GameModeSystem _gameModeSystem;
        private readonly IDisposable _subscriptions;

        /// <summary>
        /// Cancels submissions still in flight when the scope goes away, so an await never resumes into
        /// a disposed container.
        /// </summary>
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public LeaderboardSystem(
            ILeaderboardsService leaderboardsService,
            IAuthService authService,
            ScoreModel scoreModel,
            GameModeSystem gameModeSystem,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _leaderboardsService = leaderboardsService;
            _authService = authService;
            _scoreModel = scoreModel;
            _gameModeSystem = gameModeSystem;

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            gameOverSubscriber.Subscribe(OnGameOver).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _subscriptions.Dispose();
        }

        /// <summary>
        /// Submits <paramref name="score"/> to both of <paramref name="mode"/>'s boards. Public because
        /// the submission rules — which modes rank, what happens when signed out, what happens when the
        /// backend is down — are worth testing without staging a whole run to game over.
        /// <para>
        /// Never throws. A leaderboard is a nicety layered on top of a game that is fully playable
        /// offline, so a failed submission is logged and swallowed rather than propagated: the caller is
        /// the game-over flow, and letting a backend outage break the end-of-run card would turn a
        /// cosmetic problem into a broken game.
        /// </para>
        /// </summary>
        public async UniTask SendScore(GameMode mode, int score)
        {
            if (!BoardIds.TryGetValue(mode, out (string AllTimeId, string WeeklyId) boards))
            {
                return;
            }

            // No identity yet means there is nobody to attribute the score to. This is an expected race
            // rather than a fault — sign-in runs alongside the splash and can still be in flight on a
            // slow network — so it is silent, in keeping with AuthBootSystem treating an identity as
            // optional.
            if (!_authService.IsSignedIn)
            {
                return;
            }

            try
            {
                // In parallel: the two boards are independent, and a game-over card should not wait out
                // two sequential round trips.
                await UniTask.WhenAll(
                    _leaderboardsService.AddPlayerScoreAsync(boards.AllTimeId, score, _cts.Token),
                    _leaderboardsService.AddPlayerScoreAsync(boards.WeeklyId, score, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                // Ordinary teardown — the scope was disposed mid-submission. Not an error.
            }
            catch (Exception exception)
            {
                Debug.LogError($"Leaderboard submission failed for {mode}: {exception.Message}");
            }
        }

        private void OnGameOver(GameOverMessage message)
        {
            // Read here rather than off the message: GameOverMessage carries only a reason, and both the
            // mode and the final score are still live at this instant — ScoreModel.Score is not reset
            // until the next run starts.
            //
            // Fire and forget, because a MessagePipe handler is synchronous: nothing downstream waits on
            // the submission, and SendScore already swallows every failure, so a bare Forget() cannot
            // lose an exception.
            SendScore(_gameModeSystem.CurrentMode.Value, _scoreModel.Score.Value).Forget();
        }
    }
}
