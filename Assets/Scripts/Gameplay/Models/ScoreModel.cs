using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>Current run score, persisted high score and the combo streak.</summary>
    public sealed class ScoreModel
    {
        public ReactiveProperty<int> Score { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<int> HighScore { get; } = new ReactiveProperty<int>(0);

        /// <summary>Consecutive placements that cleared at least one line.</summary>
        public ReactiveProperty<int> Streak { get; } = new ReactiveProperty<int>(0);

        /// <summary>Consecutive placements that each cleared 2+ lines ("multi" clears) — a single-line clear
        /// resets this to 0, but a non-clearing placement leaves it unchanged. Stacks alongside <see cref="Streak"/>.</summary>
        public ReactiveProperty<int> MultiClearStreak { get; } = new ReactiveProperty<int>(0);

        /// <summary>Cumulative count of placements that cleared 2+ lines this run, regardless of whether they
        /// were consecutive — never resets except at run start. Drives
        /// <see cref="MustyBlockBlast.Core.CumulativeMultiClearMilestoneScoreRule"/>.</summary>
        public ReactiveProperty<int> CumulativeMultiClearCount { get; } = new ReactiveProperty<int>(0);
    }
}
