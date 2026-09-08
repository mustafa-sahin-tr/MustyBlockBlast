using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>Current run score, persisted high score and the combo streak.</summary>
    public sealed class ScoreModel
    {
        public ReactiveProperty<int> Score { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<int> HighScore { get; } = new ReactiveProperty<int>(0);

        /// <summary>Consecutive placements that cleared at least one line.</summary>
        public ReactiveProperty<int> Streak { get; } = new ReactiveProperty<int>(0);
    }
}
