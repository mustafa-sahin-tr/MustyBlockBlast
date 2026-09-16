using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="LeaderboardModel"/>: reads a mode's boards back out of the backend so the
    /// leaderboard card has something to draw. The read half of <see cref="LeaderboardSystem"/>, which
    /// only ever writes.
    /// <para>
    /// Which board is read is never passed in: it is always the selected tab of whatever mode
    /// <see cref="GameModeSystem.CurrentMode"/> currently holds, resolved through the same
    /// <see cref="LeaderboardBoardIds"/> the submitting path uses. That is what makes a mode switch show
    /// that mode's own boards rather than a shared one, without any caller having to remember to say so.
    /// </para>
    /// <para>
    /// Never throws. A leaderboard is a nicety layered on a game that is fully playable offline, so
    /// every failure becomes a state on the model for the card to render as a message — exactly as a
    /// failed submission is logged and swallowed rather than allowed to break the end-of-run card.
    /// </para>
    /// <para>
    /// Nothing here gates on whether an account is linked. An anonymous player ranks and is ranked like
    /// anyone else (see <see cref="LeaderboardSystem"/>), so the only identity question asked is whether
    /// sign-in has completed at all.
    /// </para>
    /// </summary>
    public sealed class LeaderboardQuerySystem : IDisposable
    {
        /// <summary>
        /// Rows fetched per board. Sized a little above what the card can draw, so the card is always
        /// full where the board allows it without paying for a page nobody can scroll to — the card has
        /// no scrolling by design, and the surplus is simply not drawn.
        /// </summary>
        private const int FETCH_LIMIT = 15;

        private readonly LeaderboardModel _model;
        private readonly ILeaderboardsService _leaderboardsService;
        private readonly IAuthService _authService;
        private readonly IConnectivityService _connectivityService;
        private readonly GameModeSystem _gameModeSystem;

        /// <summary>Cancels anything in flight when the scope goes away, so an await never resumes into
        /// a disposed container.</summary>
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

        /// <summary>
        /// The current fetch's token source, linked to <see cref="_lifetimeCts"/>. Replaced — and the
        /// old one cancelled — by every new fetch, which is what keeps a slow answer for the tab the
        /// player just left from landing on top of the tab they are now looking at.
        /// </summary>
        private CancellationTokenSource _fetchCts;

        public LeaderboardQuerySystem(
            LeaderboardModel model,
            ILeaderboardsService leaderboardsService,
            IAuthService authService,
            IConnectivityService connectivityService,
            GameModeSystem gameModeSystem)
        {
            _model = model;
            _leaderboardsService = leaderboardsService;
            _authService = authService;
            _connectivityService = connectivityService;
            _gameModeSystem = gameModeSystem;
        }

        public void Dispose()
        {
            CancelInFlight();

            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
        }

        /// <summary>
        /// Switches board and fetches it. Re-selecting the board already shown still refetches: the card
        /// has no other refresh gesture, so a tap on the active tab is the player asking for fresh
        /// standings.
        /// </summary>
        public void SelectTab(LeaderboardTab tab)
        {
            _model.ActiveTab.Value = tab;
            Refresh();
        }

        /// <summary>
        /// Fetches the selected board of the current mode, replacing whatever the model held. Fire and
        /// forget by design — the card renders <see cref="LeaderboardModel.State"/> rather than awaiting
        /// anything, and every failure is already a state.
        /// </summary>
        public void Refresh()
        {
            if (_lifetimeCts.IsCancellationRequested)
            {
                return;
            }

            GameMode mode = _gameModeSystem.CurrentMode.Value;

            if (!LeaderboardBoardIds.TryGetBoards(mode, out (string AllTimeId, string WeeklyId) boards))
            {
                // A mode with no boards has nothing to read, exactly as it has nothing to submit. Said
                // out loud on the card rather than left as an empty list, which would read as a board
                // nobody has played.
                CancelInFlight();
                SetEmpty(LeaderboardLoadState.NotRanked);
                return;
            }

            if (_connectivityService.IsOffline || !_authService.IsSignedIn)
            {
                // Both are expected states rather than faults — the queue of unsent scores exists
                // precisely because of the first, and sign-in can still be in flight on a slow network —
                // so neither is logged, and neither burns a round trip to learn what is already known.
                CancelInFlight();
                SetEmpty(LeaderboardLoadState.Unavailable);
                return;
            }

            string leaderboardId = _model.ActiveTab.Value == LeaderboardTab.Weekly
                ? boards.WeeklyId
                : boards.AllTimeId;

            CancelInFlight();
            _fetchCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);

            _model.State.Value = LeaderboardLoadState.Loading;
            FetchAsync(leaderboardId, _fetchCts.Token).Forget();
        }

        private async UniTaskVoid FetchAsync(string leaderboardId, CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<LeaderboardEntryData> entries =
                    await _leaderboardsService.GetScoresAsync(leaderboardId, FETCH_LIMIT, cancellationToken);

                // The fetch this method started is not necessarily the fetch that should be shown: a tab
                // switch cancels the token, and a cancelled fetch must leave the newer one's state alone.
                cancellationToken.ThrowIfCancellationRequested();

                _model.Entries.Value = entries ?? Array.Empty<LeaderboardEntryData>();
                _model.State.Value = LeaderboardLoadState.Ready;
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer fetch, or ordinary teardown. Either way the state belongs to
                // whatever cancelled this, so nothing is written here.
            }
            catch (Exception exception)
            {
                Debug.LogError($"Leaderboard fetch failed for {leaderboardId}: {exception.Message}");
                SetEmpty(LeaderboardLoadState.Failed);
            }
        }

        /// <summary>
        /// Drops the rows along with the state. The states this is used for all mean "these standings
        /// are not the current board's", and leaving the previous board's rows under a message about a
        /// different one would be worse than showing none.
        /// </summary>
        private void SetEmpty(LeaderboardLoadState state)
        {
            _model.Entries.Value = Array.Empty<LeaderboardEntryData>();
            _model.State.Value = state;
        }

        private void CancelInFlight()
        {
            if (_fetchCts == null)
            {
                return;
            }

            _fetchCts.Cancel();
            _fetchCts.Dispose();
            _fetchCts = null;
        }
    }
}
