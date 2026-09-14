using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The player's lifetime counters — the raw material every badge is measured against. Unlike
    /// <see cref="ScoreModel"/> nothing here is reset at a run boundary: these accumulate across runs
    /// and across app launches, and are persisted by <c>BadgeStatsSystem</c>.
    /// <para>
    /// Explicit named properties rather than a dictionary keyed by <c>BadgeStatType</c>: the same
    /// choice <see cref="PowerUpModel"/> makes for its three counts. A named
    /// <see cref="ReactiveProperty{T}"/> is what lets a View subscribe to one counter, and it keeps
    /// the lookup a compile-time thing rather than a runtime one.
    /// </para>
    /// <para>
    /// <c>long</c> rather than <c>int</c> because these only ever grow and are never cleared — a
    /// counter with no upper bound and no reset has no business being one careless idle session away
    /// from overflowing.
    /// </para>
    /// </summary>
    public sealed class BadgeStatsModel
    {
        public ReactiveProperty<long> TotalPiecesPlaced { get; } = new ReactiveProperty<long>(0L);

        public ReactiveProperty<long> TotalLinesCleared { get; } = new ReactiveProperty<long>(0L);

        public ReactiveProperty<long> TotalBoardWipes { get; } = new ReactiveProperty<long>(0L);

        /// <summary>High-water mark, not a sum: the best single run score ever reached.</summary>
        public ReactiveProperty<long> HighestScoreEver { get; } = new ReactiveProperty<long>(0L);

        public ReactiveProperty<long> TotalRunsPlayed { get; } = new ReactiveProperty<long>(0L);

        public ReactiveProperty<long> TotalPowerUpsApplied { get; } = new ReactiveProperty<long>(0L);
    }
}
