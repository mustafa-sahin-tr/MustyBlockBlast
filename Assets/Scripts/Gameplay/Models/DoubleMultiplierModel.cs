using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The live "2x frenzy" window opened by <c>PowerUpKind.DoubleMultiplier</c>: while it is running,
    /// every score gain in the run is worth double. Mutated exclusively by
    /// <c>DoubleMultiplierSystem</c>; the HUD and the two scoring systems only read it.
    /// <para>
    /// <see cref="RemainingSeconds"/> is the single source of truth — "active" is derived from it rather
    /// than stored beside it, so the flag and the clock can never disagree about whether the window is
    /// open. Like the armed selection this is run state, never persisted: the inventory count that
    /// bought the window survives a restart, an in-flight window does not.
    /// </para>
    /// </summary>
    public sealed class DoubleMultiplierModel
    {
        /// <summary>How long one activation lasts, in seconds of unpaused run time.</summary>
        public const float WINDOW_SECONDS = 15f;

        /// <summary>What a score gain is multiplied by while the window is open.</summary>
        public const int SCORE_FACTOR = 2;

        /// <summary>Seconds left in the current window. Clamped to zero; never negative. Zero means no
        /// window is open, which is also how the HUD knows to hide itself.</summary>
        public ReactiveProperty<float> RemainingSeconds { get; } = new ReactiveProperty<float>(0f);

        /// <summary>True while a window is open. Derived from <see cref="RemainingSeconds"/>, so there
        /// is nothing to keep in step with it.</summary>
        public bool IsActive => RemainingSeconds.Value > 0f;

        /// <summary>
        /// <paramref name="points"/> as the run should actually be credited them: doubled while the
        /// window is open, untouched otherwise. The doubling rule lives here, in one place, because two
        /// unrelated systems (<c>ScoreSystem</c> for placements, <c>PowerUpScoreSystem</c> for power-up
        /// clears) both have to apply it and must never disagree about what it is.
        /// <para>
        /// Deliberately has no floor: zero points doubled is still zero, so a placement that scored
        /// nothing scores nothing during a frenzy too.
        /// </para>
        /// </summary>
        public int Multiply(int points) => IsActive ? points * SCORE_FACTOR : points;
    }
}
