using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The round length the player picked for <see cref="GameMode.Timed"/>. Persisted by
    /// <c>TimedModeSystem</c> (issue #379) — the pick is made in the mode-select scene and has to reach
    /// the gameplay scene's own instance of this Model — and mutated exclusively by it.
    /// </summary>
    public sealed class TimedModeModel
    {
        /// <summary>Chosen round length in seconds. Seeded from the saved value, else the config, by <c>TimedModeSystem</c>.</summary>
        public ReactiveProperty<float> SelectedDurationSeconds { get; } = new ReactiveProperty<float>(0f);
    }
}
