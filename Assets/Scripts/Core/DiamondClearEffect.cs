using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialCellKind.Diamond"/>'s "effect": counting the ones destroyed, per colour, and
    /// nothing else. A diamond clears no area and refills none, so this deliberately never touches the
    /// board — it exists for the same reason <see cref="TimerCellClearEffect"/> and
    /// <see cref="ScoreGemEffect"/> do: the cascade loop (<see cref="CascadeClearResolver"/>) keeps the
    /// triggers it collects to itself, and <see cref="ISpecialCellEffect"/> is the one seam through
    /// which a caller can learn that a diamond was among the cells a resolution destroyed.
    /// <para>
    /// Counted by the gem's own colour (<see cref="SpecialCellTrigger.DiamondColourId"/>), never by the
    /// colour of the block it rode on: a <c>ObjectiveType.DiamondsCleared</c> objective is scoped by the
    /// diamond, and the block's colour already counts towards whatever a <c>ColourCleared</c> objective
    /// wants through the ordinary per-colour destroyed tally. The two tallies are independent by
    /// construction — a red diamond on a blue block adds one to this tally's red slot and one to the
    /// ordinary tally's blue slot.
    /// </para>
    /// <para>
    /// Deliberately no score: a diamond is a counter, not a multiplier. The score-gem multiplication is
    /// keyed on <see cref="ScoreGemEffect.DestroyedCount"/>/<see cref="ScoreGemEffect.CountDestroyed"/>,
    /// both of which filter on <see cref="SpecialCellKind.ScoreGem"/> alone, so a diamond can never reach
    /// that path (issue #393 AC4).
    /// </para>
    /// <para>
    /// Because it mutates nothing it can never chain: a resolution that destroys only diamonds settles
    /// on the very next iteration, the same as one that destroyed no special cell at all.
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="TimerCellClearEffect"/> is: one instance per System
    /// that resolves clears, with <see cref="BeginResolution"/> called before each resolution rather
    /// than a fresh instance allocated. <see cref="DestroyedCountByColour"/> is therefore a buffer this
    /// instance overwrites — a caller that needs it beyond the current resolution must copy it.
    /// </para>
    /// </summary>
    public sealed class DiamondClearEffect : ISpecialCellEffect
    {
        private readonly int[] _destroyedCountByColour = new int[Collectibles.TALLY_LENGTH];

        /// <summary>How many diamonds of each colour have been destroyed since the last
        /// <see cref="BeginResolution"/>, indexed by the gem's colour id — the <see cref="ColourTally"/>
        /// shape, so an objective reads it exactly as it reads the ordinary per-colour tally.</summary>
        public IReadOnlyList<int> DestroyedCountByColour => _destroyedCountByColour;

        /// <summary>
        /// How many of <paramref name="triggers"/> are diamonds, per gem colour. For the callers that
        /// already hold the trigger list outright — the power-up path, where
        /// <c>PowerUpClearResult.TriggeredSpecials</c> is handed back directly — mirroring
        /// <see cref="TimerCellClearEffect.CountDestroyed"/>, so "what counts as clearing a diamond" has
        /// one definition whether it is read from a list or accumulated through <see cref="Apply"/>.
        /// Null for a null list; otherwise a fresh <see cref="Collectibles.TALLY_LENGTH"/>-long array — one
        /// small allocation per power-up application, never per frame.
        /// </summary>
        public static int[] CountDestroyedByColour(IReadOnlyList<SpecialCellTrigger> triggers)
        {
            if (triggers == null)
            {
                return null;
            }

            var tally = new int[Collectibles.TALLY_LENGTH];
            for (int i = 0; i < triggers.Count; i++)
            {
                if (triggers[i].Kind == SpecialCellKind.Diamond)
                {
                    Collectibles.Increment(tally, triggers[i].DiamondColourId);
                }
            }

            return tally;
        }

        /// <summary>Starts a new resolution: forgets the previous one's counts. Must be called before the
        /// resolution that will apply this effect, or two resolutions' diamonds would be reported as
        /// one.</summary>
        public void BeginResolution() => Array.Clear(_destroyedCountByColour, 0, _destroyedCountByColour.Length);

        /// <summary>Records <paramref name="trigger"/> under its gem colour when it is a diamond, and does
        /// nothing else to anything. <paramref name="board"/> is validated but never read or written.</summary>
        public void Apply(Board board, SpecialCellTrigger trigger)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (trigger.Kind != SpecialCellKind.Diamond)
            {
                return;
            }

            Collectibles.Increment(_destroyedCountByColour, trigger.DiamondColourId);
        }
    }
}
