using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialCellKind.ScoreGem"/>'s "effect": counting, and nothing else. A gem clears no
    /// area and refills none, so this deliberately never touches the board — it exists because the
    /// cascade loop (<see cref="CascadeClearResolver"/>) keeps the triggers it collects to itself, and
    /// <see cref="ISpecialCellEffect"/> is the one seam through which a caller can learn that a gem was
    /// among the cells a resolution destroyed. <see cref="ISpecialCellEffect"/> explicitly allows an
    /// implementation that does nothing, which is exactly what this is from the board's point of view.
    /// <para>
    /// Because it mutates nothing it can never chain: a resolution that destroys only gems settles on
    /// the very next iteration, the same as one that destroyed no special cell at all.
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="ExplosiveCoreEffect"/> and <see cref="LaserEffect"/>
    /// are: one instance per System that resolves clears, with <see cref="BeginResolution"/> called
    /// before each resolution rather than a fresh instance allocated. <see cref="DestroyedCount"/> is
    /// therefore a reading this instance overwrites — a caller that needs it beyond the current
    /// resolution must copy it.
    /// </para>
    /// </summary>
    public sealed class ScoreGemEffect : ISpecialCellEffect
    {
        /// <summary>How many gems have been destroyed since the last <see cref="BeginResolution"/>.</summary>
        public int DestroyedCount { get; private set; }

        /// <summary>
        /// How many of <paramref name="triggers"/> are gems. For the callers that already hold the
        /// trigger list outright — the power-up path, where <c>PowerUpClearResult.TriggeredSpecials</c>
        /// is handed back directly — so "what counts as destroying a gem" has one definition whether it
        /// is read from a list or accumulated through <see cref="Apply"/>.
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
                if (triggers[i].Kind == SpecialCellKind.ScoreGem)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Starts a new resolution: forgets the previous one's count. Must be called before the
        /// resolution that will apply this effect, or two resolutions' gems would be reported as one —
        /// and the second event would be multiplied for a gem the first already paid out.</summary>
        public void BeginResolution() => DestroyedCount = 0;

        /// <summary>Records <paramref name="trigger"/> when it is a gem, and does nothing else to
        /// anything. <paramref name="board"/> is validated but never read or written.</summary>
        public void Apply(Board board, SpecialCellTrigger trigger)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (trigger.Kind != SpecialCellKind.ScoreGem)
            {
                return;
            }

            DestroyedCount++;
        }
    }
}
