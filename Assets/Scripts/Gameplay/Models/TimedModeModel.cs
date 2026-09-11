using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The round length the player picked for <see cref="GameMode.Timed"/>. Not persisted: every boot
    /// starts on the config's default, the same way <c>GameModeModel</c> always boots endless.
    /// Mutated exclusively by <c>TimedModeSystem</c>.
    /// </summary>
    public sealed class TimedModeModel
    {
        /// <summary>Chosen round length in seconds. Seeded from the config by <c>TimedModeSystem</c>.</summary>
        public ReactiveProperty<float> SelectedDurationSeconds { get; } = new ReactiveProperty<float>(0f);
    }
}
