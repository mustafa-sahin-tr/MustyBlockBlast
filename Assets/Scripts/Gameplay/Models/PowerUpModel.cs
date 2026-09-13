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
    }
}
