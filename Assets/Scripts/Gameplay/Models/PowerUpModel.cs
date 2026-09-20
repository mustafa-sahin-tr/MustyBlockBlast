using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>How many of each power-up the player currently holds. Persisted across runs — unlike
    /// the score these are not reset at run start, since they are earned outside a run.</summary>
    public sealed class PowerUpModel
    {
        public ReactiveProperty<int> BombCount { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<int> RowClearCount { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<int> ColumnClearCount { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<int> JokerCount { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<int> ColorCleanserCount { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<int> RotateCount { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<int> RerollCount { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<int> DoubleMultiplierCount { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<int> GhostFitCount { get; } = new ReactiveProperty<int>(0);

        /// <summary>
        /// Coin Sower units bought but not yet sown. An ordinary inventory slot, persisted like the
        /// others — the kind is bought at a level-start screen and spent there in the same breath, but it
        /// still passes through the inventory rather than around it, because that is the one place a
        /// power-up is ever earned or spent (see <c>PowerUpSystem</c>).
        /// </summary>
        public ReactiveProperty<int> CoinSowerCount { get; } = new ReactiveProperty<int>(0);

        /// <summary>
        /// Hold charges: one is spent every time a dock piece is parked into the pocket, whether it
        /// was empty or already held a piece to swap out. An ordinary inventory slot, persisted like
        /// the others. Read by <c>BoardSystem</c>'s game-over check as well as spent by
        /// <c>PowerUpSystem.TryApplyHold</c>: a parked piece only counts as a move the player still
        /// has while there is a charge left to swap it back out with.
        /// </summary>
        public ReactiveProperty<int> HoldCount { get; } = new ReactiveProperty<int>(0);

        /// <summary>
        /// The power-up the player has selected and is now aiming — at the board, or at the tray for
        /// <see cref="PowerUpKind.Rotate"/> — or null when none is.
        /// Unlike the counts this is run state, not inventory: it is dropped on every run boundary and
        /// never persisted. Selecting arms immediately — there is no queue, so at most one kind is
        /// armed at a time. <see cref="PowerUpKind.Reroll"/> never appears here: it has no target to
        /// aim at, so it is applied on the tap that selects it and is never an armed selection.
        /// </summary>
        public ReactiveProperty<PowerUpKind?> Armed { get; } = new ReactiveProperty<PowerUpKind?>(null);
    }
}
