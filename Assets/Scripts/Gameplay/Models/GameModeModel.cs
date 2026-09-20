using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>The mode the current run is being played under. Not persisted: every boot starts endless.</summary>
    public sealed class GameModeModel
    {
        public ReactiveProperty<GameMode> CurrentMode { get; } =
            new ReactiveProperty<GameMode>(GameMode.Endless);

        /// <summary>
        /// Whether special cells and power-ups are part of the current mode's ruleset (issue #355).
        /// True for every mode except <see cref="GameMode.Timed"/> — the enum member kept its ordinal
        /// and its identifier for save-data compatibility, but it is now "Classic" (Turkish: "Klasik
        /// Mod"): the one mode with no special cells and no power-ups, whatever duration (including the
        /// endless one) it is played with.
        /// <para>
        /// The single source of truth every special-cell spawn and every power-up arm/use/grant path
        /// reads instead of comparing <see cref="CurrentMode"/> to <see cref="GameMode.Timed"/> itself,
        /// so a future mode that also wants the no-extras ruleset only has to change this one line.
        /// </para>
        /// </summary>
        public bool ExtrasEnabled => CurrentMode.Value != GameMode.Timed;
    }
}
