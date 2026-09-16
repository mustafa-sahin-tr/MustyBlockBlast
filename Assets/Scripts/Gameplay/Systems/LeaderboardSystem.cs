using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// A run that ends with no connection is not dropped: the score goes to
    /// <see cref="PendingScoreQueueSystem"/>, which persists it and files it once the backend is
    /// reachable again. A leaderboard being unreachable must never cost the player a score they earned.
    /// </para>
    /// <para>
    /// Subscribes in its constructor like <see cref="TimedHighScoreSystem"/>, so it must be resolved
    /// eagerly by the LifetimeScope rather than waiting for a first lazy resolve that may never come
    /// before the first game over.
    /// </para>
    /// </summary>
    public sealed class LeaderboardSystem : IDisposable
    {
        private readonly ILeaderboardsService _leaderboardsService;
        private readonly IAuthService _authService;
        private readonly IConnectivityService _connectivityService;
        private readonly PendingScoreQueueSystem _pendingScoreQueueSystem;
        private readonly ScoreModel _scoreModel;
        private readonly GameModeSystem _gameModeSystem;
        private readonly ProfileModel _profileModel;
        private readonly IDisposable _subscriptions;

        /// <summary>
        /// Reused metadata map, rewritten per submission. One dictionary for the life of the system
        /// rather than one per game over, because the keys never change — only the values do.
        /// </summary>
        private readonly Dictionary<string, string> _metadata = new Dictionary<string, string>(1);

        /// <summary>
        /// Cancels submissions still in flight when the scope goes away, so an await never resumes into
        /// a disposed container.
        /// </summary>
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public LeaderboardSystem(
            ILeaderboardsService leaderboardsService,
            IAuthService authService,
            IConnectivityService connectivityService,
            PendingScoreQueueSystem pendingScoreQueueSystem,
            ScoreModel scoreModel,
            GameModeSystem gameModeSystem,
            ProfileModel profileModel,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _leaderboardsService = leaderboardsService;
            _authService = authService;
            _connectivityService = connectivityService;
            _pendingScoreQueueSystem = pendingScoreQueueSystem;
            _scoreModel = scoreModel;
            _gameModeSystem = gameModeSystem;
            _profileModel = profileModel;

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
            if (!LeaderboardBoardIds.TryGetBoards(mode, out (string AllTimeId, string WeeklyId) boards))
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

            // Bank it instead of attempting a submission that cannot succeed. Deliberately before the
            // try rather than inside it: a known-offline device would otherwise spend a round trip to
            // fail, and the queue drains itself as soon as the connection is back.
            if (_connectivityService.IsOffline)
            {
                _pendingScoreQueueSystem.Enqueue(mode, score);
                return;
            }

            try
            {
                // Built once for both boards: the same run by the same player, so an entry that differed
                // between the two would be two identities for one person.
                IReadOnlyDictionary<string, string> metadata = BuildMetadata();

                // In parallel: the two boards are independent, and a game-over card should not wait out
                // two sequential round trips.
                await UniTask.WhenAll(
                    _leaderboardsService.AddPlayerScoreAsync(boards.AllTimeId, score, metadata, _cts.Token),
                    _leaderboardsService.AddPlayerScoreAsync(boards.WeeklyId, score, metadata, _cts.Token));
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

        /// <summary>
        /// Everything a rendered row needs that the backend does not already know. The player's name is
        /// deliberately absent: it is published to the identity service by <see cref="ProfileSystem"/>
        /// and comes back on the entry itself, so carrying a second copy here would let a renamed player
        /// show two different names on two boards.
        /// <para>
        /// Read at submission time rather than cached, so a player who changes avatar between runs is
        /// filed under the one they are wearing now.
        /// </para>
        /// </summary>
        private IReadOnlyDictionary<string, string> BuildMetadata()
        {
            // Invariant culture so a device locale cannot change the digits another player's client
            // parses back — see UnityLeaderboardsService's reader, which parses with the same culture.
            _metadata[LeaderboardMetadataKeys.AVATAR_ID] =
                _profileModel.AvatarId.Value.ToString(CultureInfo.InvariantCulture);
            return _metadata;
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
