using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// How the current dock cycle is going: how many pieces have been played out of it, and how many of
    /// those cleared at least one line. A "perfect round" is a cycle in which every piece played cleared
    /// something, and completing one is what earns a <c>SpecialCellKind.ScoreGem</c>.
    /// <para>
    /// A cycle runs from one tray refill to the next, so both counters are reset by
    /// <c>BoardSystem.RefillTray</c> — which is also what a run start goes through, so nothing can leak
    /// from one run into the next.
    /// </para>
    /// <para>
    /// Reactive rather than plain ints purely so this is ordinary, observable run state like every other
    /// Model: it is state a future one-step-undo has to snapshot and restore alongside the board and the
    /// score, and keeping it here rather than as private bookkeeping inside a System is what makes that
    /// possible at all.
    /// </para>
    /// </summary>
    public sealed class PerfectRoundModel
    {
        /// <summary>How many pieces of the current cycle have been placed and resolved.</summary>
        public ReactiveProperty<int> PiecesResolvedInCycle { get; } = new ReactiveProperty<int>(0);

        /// <summary>Of <see cref="PiecesResolvedInCycle"/>, how many cleared at least one line.</summary>
        public ReactiveProperty<int> PiecesClearedInCycle { get; } = new ReactiveProperty<int>(0);

        /// <summary>
        /// True when the cycle as it stands is a perfect round: a full dock's worth of pieces has been
        /// played out of it and every single one of them cleared a line.
        /// <para>
        /// "At least a full dock" rather than "exactly three", because the Reroll power-up restocks all
        /// three slots part-way through a cycle and so a cycle can legitimately run longer than three
        /// placements. The equality is what keeps that honest: a longer cycle only qualifies if every
        /// one of its placements cleared, which is strictly harder than the ordinary three.
        /// </para>
        /// <para>
        /// A cycle cut <em>short</em> — the Hold pocket can take one piece out of the dock, so the dock
        /// can run dry after two placements — correspondingly does not qualify: fewer than a dock's
        /// worth of pieces were actually played, so there was no full round to be perfect.
        /// </para>
        /// </summary>
        internal bool IsPerfectRound
            => PiecesResolvedInCycle.Value >= TrayModel.SLOT_COUNT
                && PiecesClearedInCycle.Value == PiecesResolvedInCycle.Value;

        /// <summary>Records one resolved placement of the current cycle.</summary>
        internal void RecordPlacement(bool clearedAnyLine)
        {
            PiecesResolvedInCycle.Value += 1;

            if (clearedAnyLine)
            {
                PiecesClearedInCycle.Value += 1;
            }
        }

        /// <summary>Starts a fresh cycle. Called on every refill, whether or not the cycle that just
        /// ended earned anything, so a near-miss can never bleed into the next dock.</summary>
        internal void ResetCycle()
        {
            PiecesResolvedInCycle.Value = 0;
            PiecesClearedInCycle.Value = 0;
        }
    }
}
