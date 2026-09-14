using MustyBlockBlast.Gameplay.Reactive;

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
