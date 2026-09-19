using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialCellKind.Timer"/>'s "effect": counting the ones destroyed in time, and nothing
    /// else. A timer cell clears no area and refills none, so this deliberately never touches the board
    /// — it exists for the same reason <see cref="ScoreGemEffect"/> does: the cascade loop
    /// (<see cref="CascadeClearResolver"/>) keeps the triggers it collects to itself, and
    /// <see cref="ISpecialCellEffect"/> is the one seam through which a caller can learn that a timer
    /// cell was among the cells a resolution destroyed.
    /// <para>
    /// A cell reaching here as a <see cref="SpecialCellTrigger"/> at all already proves it was cleared
    /// while still carrying <see cref="SpecialCellKind.Timer"/> — an expired cell loses that kind the
    /// moment its countdown reaches zero (see <see cref="TimerCellTick"/>), before any clear phase can
    /// see it, so every count this class produces is, by construction, a "cleared before expiry" count —
    /// exactly what issue #307 AC5's objective needs.
    /// </para>
    /// <para>
    /// Because it mutates nothing it can never chain: a resolution that destroys only timer cells settles
    /// on the very next iteration, the same as one that destroyed no special cell at all.
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="ScoreGemEffect"/> is: one instance per System that
    /// resolves clears, with <see cref="BeginResolution"/> called before each resolution rather than a
    /// fresh instance allocated. <see cref="DestroyedCount"/> is therefore a reading this instance
    /// overwrites — a caller that needs it beyond the current resolution must copy it.
    /// </para>
    /// </summary>
    public sealed class TimerCellClearEffect : ISpecialCellEffect
    {
        /// <summary>How many timer cells have been destroyed in time since the last
        /// <see cref="BeginResolution"/>.</summary>
        public int DestroyedCount { get; private set; }

        /// <summary>
        /// How many of <paramref name="triggers"/> are timer cells. For the callers that already hold
        /// the trigger list outright — the power-up path, where <c>PowerUpClearResult.TriggeredSpecials</c>
        /// is handed back directly — mirroring <see cref="ScoreGemEffect.CountDestroyed"/>, so "what
        /// counts as clearing a timer cell in time" has one definition whether it is read from a list or
        /// accumulated through <see cref="Apply"/>.
        /// </summary>
        public static int CountDestroyed(IReadOnlyList<SpecialCellTrigger> triggers)
        {
            if (triggers == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < triggers.Count; i++)
            {
                if (triggers[i].Kind == SpecialCellKind.Timer)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Starts a new resolution: forgets the previous one's count. Must be called before the
        /// resolution that will apply this effect, or two resolutions' timer cells would be reported as
        /// one.</summary>
        public void BeginResolution() => DestroyedCount = 0;

        /// <summary>Records <paramref name="trigger"/> when it is a timer cell, and does nothing else to
        /// anything. <paramref name="board"/> is validated but never read or written.</summary>
        public void Apply(Board board, SpecialCellTrigger trigger)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (trigger.Kind != SpecialCellKind.Timer)
            {
                return;
            }

            DestroyedCount++;
        }
    }
}
