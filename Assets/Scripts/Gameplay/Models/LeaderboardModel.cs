using System;
using System.Collections.Generic;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Systems;
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// What the leaderboard card is showing: which board is selected, the rows that came back for it,
    /// and whether they can be trusted yet. Owned and written by <see cref="LeaderboardQuerySystem"/>.
    /// <para>
    /// Holds no board ids and no game mode. The mode is <see cref="GameModeModel"/>'s, and which boards
    /// it maps to is <c>LeaderboardBoardIds</c>' — copying either here would be a second source of truth
    /// that could disagree with the one the submitting path uses.
    /// </para>
    /// <para>
    /// Purely a display cache: nothing is persisted, and a closed card keeps whatever it last fetched
    /// only so re-opening it has something to draw while the next fetch is in flight.
    /// </para>
    /// </summary>
    public sealed class LeaderboardModel
    {
        /// <summary>The board being looked at. All-time first, because that is the board a new personal
        /// best is measured against.</summary>
        public ReactiveProperty<LeaderboardTab> ActiveTab { get; } =
            new ReactiveProperty<LeaderboardTab>(LeaderboardTab.AllTime);

        /// <summary>
        /// The fetched page, best first. Replaced wholesale on every fetch rather than mutated, so a
        /// view holding the previous list can never see it change underneath a repaint.
        /// <para>
        /// Meaningful only while <see cref="State"/> is <see cref="LeaderboardLoadState.Ready"/>; every
        /// other state leaves the last page in place and is what a view should draw instead.
        /// </para>
        /// </summary>
        public ReactiveProperty<IReadOnlyList<LeaderboardEntryData>> Entries { get; } =
            new ReactiveProperty<IReadOnlyList<LeaderboardEntryData>>(Array.Empty<LeaderboardEntryData>());

        public ReactiveProperty<LeaderboardLoadState> State { get; } =
            new ReactiveProperty<LeaderboardLoadState>(LeaderboardLoadState.Idle);
    }
}
